//
// DiskManager.cs
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
using System.Threading.Tasks;
using Serilog;
using System.IO;

namespace Taskmaster
{
	public class DiskManager : IDisposable
	{
		static readonly string systemTemp = System.IO.Path.Combine(System.Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
		static string userTemp { get { return System.IO.Path.GetTempPath(); } }

		readonly System.IO.FileSystemWatcher userWatcher;
		readonly System.IO.FileSystemWatcher sysWatcher;

		static System.Threading.Timer TempScanTimer;
		static int TimerDue = 1000 * 60 * 60 * 24;

		public DiskManager()
		{
			userWatcher = new System.IO.FileSystemWatcher(userTemp);
			userWatcher.NotifyFilter = System.IO.NotifyFilters.Size;
			userWatcher.Deleted += ModifyTemp;
			userWatcher.Created += ModifyTemp;
			if (systemTemp != userTemp)
			{
				sysWatcher = new System.IO.FileSystemWatcher(systemTemp);
				sysWatcher.NotifyFilter = System.IO.NotifyFilters.Size;
				sysWatcher.Deleted += ModifyTemp;
				sysWatcher.Created += ModifyTemp;
			}

			TempScanTimer = new System.Threading.Timer(async (state) => { await ScanTemp().ConfigureAwait(false); }, null, 0, TimerDue);
			Log.Information("Temp folder scanner will be performed once per day.");

			onBurden += ReScanTemp;
		}

		static long ReScanBurden = 0;

		void ModifyTemp(object sender, FileSystemEventArgs ev)
		{
			ReScanBurden++;
			if (ReScanBurden % 100 == 0)
			{
				Log.Information("Significant amount of changes have occurred to temp folders");
			}
			if (ReScanBurden % 1000 == 0)
			{
				Log.Warning("Number of changes to temp folders exceeding tolerance.");
				onBurden?.Invoke(this, null);
			}
		}

		void ReScanTemp(object sender, EventArgs ev)
		{
			TempScanTimer.Change(Taskmaster.TempRescanDelay, TimerDue);
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
			int i = 1;
			DiskEventArgs dea = new DiskEventArgs { State = ScanState.Segment, Stats = stats };
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
			catch
			{
				Log.Error("Failed to access temp folder: " + dinfo.FullName);
			}
		}

		public async Task ScanTemp()
		{
			await Task.Delay(0);

			using (var m = SelfAwareness.Mind(DateTime.Now.AddSeconds(120)))
			{
				var dst = new DirectoryStats { Files = 0, Dirs = 0, Size = 0 };

				ReScanBurden = 0;

				Log.Information("Temp folders scanning initiated...");
				onTempScan?.Invoke(null, new DiskEventArgs { State = ScanState.Start, Stats = dst });
				DirectorySize(new System.IO.DirectoryInfo(systemTemp), ref dst);
				if (systemTemp != userTemp)
				{
					onTempScan?.Invoke(null, new DiskEventArgs { State = ScanState.Segment, Stats = dst });
					DirectorySize(new System.IO.DirectoryInfo(userTemp), ref dst);
				}
				onTempScan?.Invoke(null, new DiskEventArgs { State = ScanState.End, Stats = dst });
				Log.Information("Temp contents: {Files} files, {Dirs} dirs, {Size} MBs", dst.Files, dst.Dirs, string.Format("{0:N2}", dst.Size / 1000f / 1000f));
			}
		}

		public enum ScanState
		{
			Start,
			Segment,
			End
		};

		public event EventHandler<DiskEventArgs> onTempScan;

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		bool disposed = false;
		void Dispose(bool disposing)
		{
			if (disposed)
				return;

			if (disposing)
			{
				if (Taskmaster.Trace)
					Log.Verbose("Disposing disk manager...");

				sysWatcher?.Dispose();
				userWatcher?.Dispose();
			}

			disposed = true;
		}
	}

	public class DiskEventArgs : EventArgs
	{
		public DiskManager.ScanState State { get; set; }
		public DiskManager.DirectoryStats Stats { get; set; }
	}
}
