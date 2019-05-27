﻿//
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
using System.IO;
using System.Threading.Tasks;
using MKAh;
using Serilog;

namespace Taskmaster
{
	using static Taskmaster;

	/// <summary>
	/// Manager for non-volatile memory (NVM).
	/// </summary>
	sealed public class StorageManager : IDisposal, IDisposable
	{
		bool Verbose = false;

		static readonly string systemTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
		static string UserTemp => Path.GetTempPath();

		readonly FileSystemWatcher UserWatcher;
		readonly FileSystemWatcher SysWatcher;

		readonly System.Timers.Timer TempScanTimer = null;
		TimeSpan TimerDue = TimeSpan.FromHours(24);

		public StorageManager()
		{
			if (TempMonitorEnabled)
			{
				UserWatcher = new FileSystemWatcher(UserTemp)
				{
					NotifyFilter = NotifyFilters.Size,
					IncludeSubdirectories = true
				};
				UserWatcher.Deleted += ModifyTemp;
				UserWatcher.Changed += ModifyTemp;
				UserWatcher.Created += ModifyTemp;
				if (systemTemp != UserTemp)
				{
					SysWatcher = new FileSystemWatcher(systemTemp)
					{
						NotifyFilter = NotifyFilters.Size,
						IncludeSubdirectories = true
					};
					SysWatcher.Deleted += ModifyTemp;
					SysWatcher.Changed += ModifyTemp;
					SysWatcher.Created += ModifyTemp;
				}

				TempScanTimer = new System.Timers.Timer(TimerDue.TotalMilliseconds);
				TempScanTimer.Elapsed += ScanTemp;
				TempScanTimer.Start();

				Taskmaster.OnStart += OnStart;

				Log.Information("<Maintenance> Temp folder scanner will be performed once per day.");
			}

			using (var corecfg = Config.Load(CoreConfigFilename).BlockUnload())
			{
				Verbose = corecfg.Config[HumanReadable.Generic.Debug].Get("Storage")?.Bool ?? false;
			}

			if (Verbose) Log.Information("<Maintenance> Component loaded.");

			RegisterForExit(this);
			DisposalChute.Push(this);
		}

		async void OnStart(object sender, EventArgs ea)
			=> await Task.Run(() => ScanTemp(this, EventArgs.Empty)).ConfigureAwait(false);

		static long ReScanBurden = 0;

		void ModifyTemp(object _, FileSystemEventArgs ev)
		{
			if (DisposedOrDisposing) return;

			try
			{
				Logging.DebugMsg("TEMP modified (" + ev.ChangeType.ToString() + "): " + ev.FullPath);

				if (ReScanBurden % 100 == 0)
					Log.Debug("<Maintenance> Significant amount of changes have occurred to temp folders");

				if (ReScanBurden % 1000 == 0)
				{
					Log.Warning("<Maintenance> Number of changes to temp folders exceeding tolerance.");

					ReScanTemp();
				}
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		DateTimeOffset LastTempScan = DateTimeOffset.MinValue;
		async void ReScanTemp()
		{
			if (DisposedOrDisposing) return;

			var now = DateTimeOffset.UtcNow;
			if (now.TimeSince(LastTempScan).TotalMinutes <= 15) return; // too soon
			LastTempScan = now;

			TempScanTimer?.Stop();
			await Task.Run(() => Task.Delay(5_000).ContinueWith((_x) => ScanTemp(this, EventArgs.Empty))).ConfigureAwait(false);
		}

		public struct DirectoryStats
		{
			public long Size;
			public long Files;
			public long Dirs;
		}

		void DirectorySize(DirectoryInfo dinfo, ref DirectoryStats stats)
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException(nameof(StorageManager), "DirectorySize called after StorageManager was disposed.");

			var i = 1;
			var dea = new StorageEventArgs { State = ScanState.Segment, Stats = stats };
			try
			{
				foreach (FileInfo fi in dinfo.GetFiles())
				{
					stats.Size += fi.Length;
					stats.Files += 1;
					if (i++ % 100 == 0) TempScan?.Invoke(null, dea);
				}

				foreach (System.IO.DirectoryInfo di in dinfo.GetDirectories())
				{
					DirectorySize(di, ref stats);
					stats.Dirs += 1;

					if (i++ % 100 == 0) TempScan?.Invoke(null, dea);
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
			if (DisposedOrDisposing) return;

			if (!Atomic.Lock(ref scantemp_lock)) return;

			try
			{
				await Task.Delay(0);

				var dst = new DirectoryStats { Files = 0, Dirs = 0, Size = 0 };

				ReScanBurden = 0;

				Log.Information("Temp folders scanning initiated...");
				TempScan?.Invoke(null, new StorageEventArgs { State = ScanState.Start, Stats = dst });
				DirectorySize(new System.IO.DirectoryInfo(systemTemp), ref dst);
				if (systemTemp != UserTemp)
				{
					TempScan?.Invoke(null, new StorageEventArgs { State = ScanState.Segment, Stats = dst });
					DirectorySize(new System.IO.DirectoryInfo(UserTemp), ref dst);
				}

				TempScan?.Invoke(null, new StorageEventArgs { State = ScanState.End, Stats = dst });
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

		public event EventHandler<StorageEventArgs> TempScan;

		#region IDisposable Support
		public event EventHandler<DisposedEventArgs> OnDisposed;

		public void Dispose() => Dispose(true);

		bool DisposedOrDisposing = false;
		void Dispose(bool disposing)
		{
			if (DisposedOrDisposing) return;
			DisposedOrDisposing = true;

			if (disposing)
			{
				if (Trace) Log.Verbose("Disposing storage manager...");

				TempScan = null;

				SysWatcher?.Dispose();
				UserWatcher?.Dispose();
				TempScanTimer?.Dispose();
			}

			OnDisposed?.Invoke(this, DisposedEventArgs.Empty);
			OnDisposed = null;
		}

		public void ShutdownEvent(object sender, EventArgs ea)
		{
			TempScanTimer?.Dispose();
			SysWatcher?.Dispose();
			UserWatcher?.Dispose();
		}
		#endregion
	}

	sealed public class StorageEventArgs : EventArgs
	{
		public StorageManager.ScanState State { get; set; }
		public StorageManager.DirectoryStats Stats { get; set; }
	}
}