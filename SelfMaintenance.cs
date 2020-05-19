//
// SelfMaintenance.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2018–2019 M.A.
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
using MKAh.Synchronize;
using Serilog;
using System;
using System.Threading.Tasks;

namespace Taskmaster
{
	using static Application;

	[Context(RequireMainThread = false)]
	public class SelfMaintenance : IComponent
	{
		public SelfMaintenance()
		{
			timer.Elapsed += MaintenanceTick;
			TimerReset();

			if (Trace) Log.Information("<Self-Maintenance> Initialized.");
		}

		void TimerReset()
		{
			var now = DateTimeOffset.Now;
			int skip = 24 - now.Hour;
			var next = now.AddHours(skip).AddDays(skip < 1 ? 1 : 0);
			var nextmidnight = now.To(next);
			//var nextmidnightms = Convert.ToInt64(nextmidnight.TotalMilliseconds);
			DateTime lnext = next.DateTime;

			// TODO: Use human readable time for nextmidnight

			Log.Verbose($"<Self-Maintenance> Next maintenance: {lnext.ToLongDateString()} {lnext.ToLongTimeString()}");

			timer.Interval = nextmidnight.TotalMilliseconds;
			timer.Start();
		}

		readonly System.Timers.Timer timer = new System.Timers.Timer(86_400_000) { AutoReset = false }; // once a day

		Process.Manager? processmanager = null;

		void Hook(Process.Manager procman)
		{
			processmanager = procman;
			processmanager.OnDisposed += (_, _ea) => processmanager = null;
		}

		int CallbackLimiter = 0;

		async void MaintenanceTick(object _sender, System.Timers.ElapsedEventArgs _)
		{
			if (disposed) return;

			if (!Atomic.Lock(ref CallbackLimiter)) return;

			await Task.Delay(0).ConfigureAwait(false);

			var time = System.Diagnostics.Stopwatch.StartNew();

			try
			{
				long oldmem = GC.GetTotalMemory(false);

				System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
				GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
				GC.WaitForPendingFinalizers();

				long newmem = GC.GetTotalMemory(true);

				Log.Debug("<Self-Maintenance> Done, saved " + ((oldmem - newmem) / 1_000).ToString() + " kB.");

				if (Trace) Log.Verbose("Running periodic cleanup");

				// TODO: This starts getting weird if cleanup interval is smaller than total delay of testing all items.
				// (15*60) / 2 = item limit, and -1 or -2 for safety margin. Unlikely, but should probably be covered anyway.

				processmanager?.Cleanup();

				Refresh();

				Config.Flush();
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				timer.Dispose();
				throw;
			}
			finally
			{
				time.Stop();
				Statistics.MaintenanceCount++;
				Statistics.MaintenanceTime += time.Elapsed.TotalSeconds;

				if (Trace) Log.Verbose($"Maintenance took: {time.Elapsed.TotalSeconds:N2}s");

				TimerReset();

				Atomic.Unlock(ref CallbackLimiter);
			}
		}

		#region IDisposable Support
		private bool disposed = false; // To detect redundant calls

		public event EventHandler<DisposedEventArgs>? OnDisposed;

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposed) return;
			disposed = true;

			if (disposing)
			{
				timer.Dispose();

				//base.Dispose();

				OnDisposed?.Invoke(this, DisposedEventArgs.Empty);
				OnDisposed = null;
			}
		}

		public void ShutdownEvent(object sender, EventArgs ea) => timer?.Stop();
		#endregion
	}
}
