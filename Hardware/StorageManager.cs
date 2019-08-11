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

using MKAh;
using Serilog;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Taskmaster
{
	using static Taskmaster;

	/// <summary>
	/// Manager for non-volatile memory (NVM).
	/// </summary>
	[Component(RequireMainThread = false)]
	public class StorageManager : Component, IDisposal
	{
		bool Verbose = false;

		static readonly string systemTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
		static string UserTemp => Path.GetTempPath();

		readonly FileSystemWatcher? UserWatcher = null;
		readonly FileSystemWatcher? SysWatcher = null;

		readonly System.Timers.Timer TempScanTimer;
		TimeSpan TimerDue = TimeSpan.FromHours(24);

		public StorageManager()
		{
			TempScanTimer = new System.Timers.Timer(TimerDue.TotalMilliseconds);
			TempScanTimer.Elapsed += ScanTemp;

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

				TempScanTimer.Start();

				Taskmaster.OnStart += OnStart;

				Log.Information("<Maintenance> Temp folder scanner will be performed once per day.");
			}

			using var corecfg = Config.Load(CoreConfigFilename);
			Verbose = corecfg.Config[HumanReadable.Generic.Debug].Get(Constants.Storage)?.Bool ?? false;

			if (Verbose) Log.Information("<Maintenance> Component loaded.");

			RegisterForExit(this);
			DisposalChute.Push(this);
		}

		async void OnStart(object sender, EventArgs ea)
			=> await Task.Run(() => ScanTemp(this, EventArgs.Empty)).ConfigureAwait(false);

		static long ReScanBurden = 0;

		void ModifyTemp(object _, FileSystemEventArgs ev)
		{
			if (disposed) return;

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

		async Task ReScanTemp()
		{
			if (disposed) return;

			var now = DateTimeOffset.UtcNow;
			if (now.Since(LastTempScan).TotalMinutes <= 15) return; // too soon
			LastTempScan = now;

			TempScanTimer.Stop();
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
			if (disposed) throw new ObjectDisposedException(nameof(StorageManager), "DirectorySize called after StorageManager was disposed.");

			var i = 1;
			var dea = new StorageEventArgs { State = ScanState.Segment, Stats = stats };
			try
			{
				foreach (FileInfo fi in dinfo.GetFiles())
				{
					stats.Size += fi.Length;
					stats.Files++;
					if (i++ % 100 == 0) TempScan?.Invoke(ScanState.Segment, stats);
				}

				foreach (System.IO.DirectoryInfo di in dinfo.GetDirectories())
				{
					DirectorySize(di, ref stats);
					stats.Dirs++;

					if (i++ % 100 == 0) TempScan?.Invoke(ScanState.Segment, stats);
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
			if (disposed) return;

			if (!Atomic.Lock(ref scantemp_lock)) return;

			try
			{
				await Task.Delay(0).ConfigureAwait(false);

				var dst = new DirectoryStats { Files = 0, Dirs = 0, Size = 0 };

				ReScanBurden = 0;

				Log.Information("Temp folders scanning initiated...");
				TempScan?.Invoke(ScanState.Start, dst);
				DirectorySize(new System.IO.DirectoryInfo(systemTemp), ref dst);
				if (systemTemp != UserTemp)
				{
					TempScan?.Invoke(ScanState.Segment, dst);
					DirectorySize(new System.IO.DirectoryInfo(UserTemp), ref dst);
				}

				TempScan?.Invoke(ScanState.End, dst);
				Log.Information($"Temp contents: {dst.Files} files, {dst.Dirs} dirs, {(dst.Size / 1_000_000f):N2} MBs");
			}
			finally
			{
				Atomic.Unlock(ref scantemp_lock);

				TempScanTimer.Start();
			}
		}

		public enum ScanState
		{
			Start,
			Segment,
			End
		};

		public StorageScanStateDelegate? TempScan;

		#region IDisposable Support
		public event EventHandler<DisposedEventArgs> OnDisposed;

		bool disposed = false;

		public override void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected void Dispose(bool disposing)
		{
			if (disposed) return;
			disposed = true;

			if (disposing)
			{
				if (Trace) Log.Verbose("Disposing storage manager...");

				TempScan = null;

				SysWatcher?.Dispose();
				UserWatcher?.Dispose();
				TempScanTimer.Dispose();

				//base.Dispose();

				OnDisposed?.Invoke(this, DisposedEventArgs.Empty);
				OnDisposed = null;
			}
		}

		public void ShutdownEvent(object sender, EventArgs ea)
		{
			try
			{
				TempScanTimer.Dispose();
				SysWatcher?.Dispose();
				UserWatcher?.Dispose();
			}
			catch { /* don't care */ }
		}
		#endregion
	}

	public delegate void StorageScanStateDelegate(StorageManager.ScanState state, StorageManager.DirectoryStats stats);

	public class StorageEventArgs : EventArgs
	{
		public StorageManager.ScanState State { get; set; }
		public StorageManager.DirectoryStats Stats { get; set; }
	}
}