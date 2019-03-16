﻿//
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

using System;
using MKAh;
using Serilog;

namespace Taskmaster
{
	public class SelfMaintenance : IDisposable
	{
		public SelfMaintenance()
		{
			try
			{
				var now = DateTimeOffset.UtcNow;
				var next = now.AddDays(1);
				var nextmidnight = now.TimeTo(next);
				//var nextmidnightms = Convert.ToInt64(nextmidnight.TotalMilliseconds);
				DateTime lnext = next.UtcDateTime;

				Log.Information("<Self-Maintenance> Next maintenance: " +
					lnext.ToLongDateString() + " " + lnext.ToLongTimeString() + " [in " + HumanInterface.PureTimeString(nextmidnight)+"]");

				timer = new System.Timers.Timer(86_400_000); // once a day
				timer.Elapsed += MaintenanceTick;
				timer.Start();
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex, true);
				throw;
			}

			if (Taskmaster.Trace) Log.Information("<Self-Maintenance> Initialized.");

			Taskmaster.DisposalChute.Push(this);
		}

		readonly System.Timers.Timer timer = null;

		int CallbackLimiter = 0;
		async void MaintenanceTick(object _, EventArgs _ea)
		{
			if (DisposedOrDisposing) return;

			if (!Atomic.Lock(ref CallbackLimiter)) return;

			var time = System.Diagnostics.Stopwatch.StartNew();

			try
			{
				long oldmem = GC.GetTotalMemory(false);

				System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
				GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
				GC.WaitForPendingFinalizers();

				long newmem = GC.GetTotalMemory(true);

				Log.Debug("<Self-Maintenance> Done, saved " + ((oldmem - newmem) / 1_000) + " kB.");

				if (Taskmaster.Trace) Log.Verbose("Running periodic cleanup");

				// TODO: This starts getting weird if cleanup interval is smaller than total delay of testing all items.
				// (15*60) / 2 = item limit, and -1 or -2 for safety margin. Unlikely, but should probably be covered anyway.

				Taskmaster.processmanager?.Cleanup();

				Taskmaster.Config.Flush();
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

				if (Taskmaster.Trace) Log.Verbose("Maintenance took: " + $"{time.Elapsed.TotalSeconds:N2}s");

				Atomic.Unlock(ref CallbackLimiter);
			}
		}

		#region IDisposable Support
		private bool DisposedOrDisposing = false; // To detect redundant calls

		protected virtual void Dispose(bool disposing)
		{
			if (!DisposedOrDisposing)
			{
				DisposedOrDisposing = true;

				if (disposing)
				{
					timer?.Dispose();
				}
			}
		}

		public void Dispose() => Dispose(true);
		#endregion
	}
}
