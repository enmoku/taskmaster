﻿//
// SelfMaintenance.cs
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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Serilog;

namespace Taskmaster
{
	public class SelfMaintenance : IDisposable
	{
		public SelfMaintenance()
		{
			try
			{
				var now = DateTime.Now;
				var next = new DateTime(now.Year, now.Month, now.Day + 1, 0, 0, 15);
				var nextmidnight = Convert.ToInt64(now.TimeTo(next).TotalMilliseconds);

				Log.Information("<Self-Maintenance> Next maintenance: {Date} {Time} [in {Ms} ms]",
					next.ToLongDateString(), next.ToLongTimeString(), nextmidnight);

				timer = new System.Threading.Timer(Tick, null,
					nextmidnight,
					86400000 // once a day
					);
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex, true);
				throw;
			}

			Log.Information("<Self-Maintenance> Initialized.");
		}

		readonly System.Threading.Timer timer = null;

		int CallbackLimiter = 0;
		public void Tick(object state)
		{
			if (!Atomic.Lock(ref CallbackLimiter)) return;

			try
			{
				long oldmem = GC.GetTotalMemory(false);
				System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
				GC.Collect(2, GCCollectionMode.Forced, true, true);
				long newmem = GC.GetTotalMemory(true);

				Log.Debug("<Self-Maintenance> Done, saved {kBytes} kB.", (oldmem-newmem)/1000);

				Taskmaster.Config.Save();
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				timer.Dispose();
				throw;
			}
			finally
			{
				Atomic.Unlock(ref CallbackLimiter);
			}
		}

		#region IDisposable Support
		private bool disposed = false; // To detect redundant calls

		protected virtual void Dispose(bool disposing)
		{
			if (!disposed)
			{
				if (disposing)
				{
					timer?.Dispose();
				}

				disposed = true;
			}
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}
		#endregion


	}
}
