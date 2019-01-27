//
// StorageManager.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2018 M.A.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using MKAh;
using Serilog;

namespace Taskmaster
{
	/// <summary>
	/// Manager for non-volatile memory (NVM).
	/// </summary>
	sealed public class StorageManager : IDisposable
	{
		static readonly string systemTemp = System.IO.Path.Combine(System.Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
		static string userTemp => System.IO.Path.GetTempPath();

		readonly System.IO.FileSystemWatcher userWatcher;
		readonly System.IO.FileSystemWatcher sysWatcher;

		readonly System.Timers.Timer TempScanTimer = null;
		TimeSpan TimerDue = TimeSpan.FromHours(24);

		public StorageManager()
		{
			if (Taskmaster.TempMonitorEnabled)
			{
				userWatcher = new System.IO.FileSystemWatcher(userTemp)
				{
					NotifyFilter = System.IO.NotifyFilters.Size,
					IncludeSubdirectories = true
				};
				userWatcher.Deleted += ModifyTemp;
				userWatcher.Changed += ModifyTemp;
				userWatcher.Created += ModifyTemp;
				if (systemTemp != userTemp)
				{
					sysWatcher = new System.IO.FileSystemWatcher(systemTemp)
					{
						NotifyFilter = System.IO.NotifyFilters.Size,
						IncludeSubdirectories = true
					};
					sysWatcher.Deleted += ModifyTemp;
					sysWatcher.Changed += ModifyTemp;
					sysWatcher.Created += ModifyTemp;
				}

				TempScanTimer = new System.Timers.Timer(TimerDue.TotalMilliseconds);
				TempScanTimer.Elapsed += ScanTemp;
				TempScanTimer.Start();

				Task.Run(() => ScanTemp(null, null));

				Log.Information("<Maintenance> Temp folder scanner will be performed once per day.");

				onBurden += ReScanTemp;
			}

			if (Taskmaster.DebugStorage) Log.Information("<Maintenance> Component loaded.");

			Taskmaster.DisposalChute.Push(this);
		}

		static long ReScanBurden = 0;

		async void ModifyTemp(object _, FileSystemEventArgs ev)
		{
			try
			{
				Debug.WriteLine("TEMP modified (" + ev.ChangeType.ToString() + "): " + ev.FullPath);

				if (ReScanBurden % 100 == 0)
				{
					Log.Debug("<Maintenance> Significant amount of changes have occurred to temp folders");
				}

				if (ReScanBurden % 1000 == 0)
				{
					Log.Warning("<Maintenance> Number of changes to temp folders exceeding tolerance.");
					onBurden?.Invoke(this, null);
				}
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		DateTimeOffset LastTempScan = DateTimeOffset.MinValue;
		void ReScanTemp(object _, EventArgs _ea)
		{
			var now = DateTimeOffset.UtcNow;
			if (now.TimeSince(LastTempScan).TotalMinutes <= 15) return; // too soon
			LastTempScan = now;

			TempScanTimer?.Stop();
			Task.Run(() => Task.Delay(5_000).ContinueWith((_x) => ScanTemp(null, null)));
		}

		event EventHandler onBurden;

		public struct DirectoryStats
		{
			public long Size;
			public long Files;
			public long Dirs;
		}

		void DirectorySize(System.IO.DirectoryInfo dinfo, ref DirectoryStats stats)
		{
			var i = 1;
			var dea = new StorageEventArgs { State = ScanState.Segment, Stats = stats };
			try
			{
				foreach (System.IO.FileInfo fi in dinfo.GetFiles())
				{
					stats.Size += fi.Length;
					stats.Files += 1;
					if (i++ % 100 == 0) onTempScan?.Invoke(null, dea);
				}

				foreach (System.IO.DirectoryInfo di in dinfo.GetDirectories())
				{
					DirectorySize(di, ref stats);
					stats.Dirs += 1;

					if (i++ % 100 == 0) onTempScan?.Invoke(null, dea);
				}
			}
			catch (OutOfMemoryException) { throw; }
			catch
			{
				Log.Error("Failed to access temp folder: " + dinfo.FullName);
			}
		}

		int scantemp_lock = 0;

		async void ScanTemp(object _, EventArgs _ea)
		{
			if (!Atomic.Lock(ref scantemp_lock)) return;
			if (disposed) return; // HACK: timers be dumb

			try
			{
				await Task.Delay(0);

				var dst = new DirectoryStats { Files = 0, Dirs = 0, Size = 0 };

				ReScanBurden = 0;

				Log.Information("Temp folders scanning initiated...");
				onTempScan?.Invoke(null, new StorageEventArgs { State = ScanState.Start, Stats = dst });
				DirectorySize(new System.IO.DirectoryInfo(systemTemp), ref dst);
				if (systemTemp != userTemp)
				{
					onTempScan?.Invoke(null, new StorageEventArgs { State = ScanState.Segment, Stats = dst });
					DirectorySize(new System.IO.DirectoryInfo(userTemp), ref dst);
				}

				onTempScan?.Invoke(null, new StorageEventArgs { State = ScanState.End, Stats = dst });
				Log.Information("Temp contents: " + dst.Files + " files, " + dst.Dirs + " dirs, " + $"{(dst.Size / 1_000_000f):N2} MBs");
			}
			finally
			{
				Atomic.Unlock(ref scantemp_lock);

				TempScanTimer?.Start();
			}
		}

		public enum ScanState
		{
			Start,
			Segment,
			End
		};

		public event EventHandler<StorageEventArgs> onTempScan;

		public void Dispose()
		{
			Dispose(true);
		}

		bool disposed = false;
		void Dispose(bool disposing)
		{
			if (disposed) return;

			if (disposing)
			{
				if (Taskmaster.Trace)
					Log.Verbose("Disposing storage manager...");

				onTempScan = null;

				sysWatcher?.Dispose();
				userWatcher?.Dispose();
				TempScanTimer?.Dispose();
			}

			disposed = true;
		}
	}

	sealed public class StorageEventArgs : EventArgs
	{
		public StorageManager.ScanState State { get; set; }
		public StorageManager.DirectoryStats Stats { get; set; }
	}
}