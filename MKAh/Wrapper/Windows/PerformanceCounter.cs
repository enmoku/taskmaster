//
// Wrapper.Windows.PerformanceCounter.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2017-2018 M.A.
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
using System.Diagnostics;

namespace MKAh.Wrapper.Windows
{
	sealed public class PerformanceCounter : IDisposable
	{
		System.Diagnostics.PerformanceCounter Counter { get; set; } = null;

		readonly string p_CategoryName = null;
		readonly string p_CounterName = null;
		readonly string p_InstanceName = null;
		readonly bool p_ScrapFirst = true;

		public PerformanceCounter(string category, string counter, string instance = null, bool scrapfirst = true)
		{
			p_CategoryName = category;
			p_CounterName = counter;
			p_InstanceName = instance;
			p_ScrapFirst = scrapfirst;

			Counter = new System.Diagnostics.PerformanceCounter()
			{
				CategoryName = p_CategoryName,
				CounterName = p_CounterName,
				InstanceName = p_InstanceName,
				ReadOnly = true,
			};

			if (p_ScrapFirst) { var scrap = Value; }

			Manager.Sensors.Add(this);
		}

		bool disposed = false;

		public void Dispose() => Dispose(true);

		void Dispose(bool disposing)
		{
			if (disposed) return;
			if (disposing)
			{
				if (Counter != null)
				{
					Counter.Close(); // probably superfluous
					Counter.Dispose();
					try
					{
						Manager.Sensors?.Remove(this);
					}
					catch { }
					Counter = null;
				}

				disposed = true;
			}
		}

		public float Value
		{
			get
			{
				try
				{
					return Counter.NextValue();
				}
				catch (System.InvalidOperationException)
				{
					Manager.Sensors?.Remove(this);
					Counter.Dispose();
					// TODO: Driver/Adapter vanished and other problems, try to re-acquire it.
					// OR: The counter may require admin rights
					Debug.WriteLine("DEBUG :: PFC(" + Counter.CategoryName + "//" + Counter.CounterName + "//" + Counter.InstanceName + ") vanished.");
					throw;
				}
			}
		}

		public CounterSample Sample => Counter.NextSample();
	}

	// Manager for ensuring disposal of sensors
	internal static class Manager
	{
		internal static List<PerformanceCounter> Sensors = new List<PerformanceCounter>(3);

		static Manager()
		{
			// NOP
		}

		// weird hack
		static readonly Finalizer finalizer = new Finalizer();
		sealed class Finalizer
		{
			~Finalizer()
			{
				Debug.WriteLine("PerformanceCounterManager static finalization");
				Sensors.Clear();
				Sensors = null;
			}
		}
	}
}
