//
// PerformanceCounterWrapper.cs
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

namespace Taskmaster
{
	sealed public class PerformanceCounterWrapper : IDisposable
	{
		public PerformanceCounter Counter { get; private set; }

		string p_CategoryName = null;
		string p_CounterName = null;
		string p_InstanceName = null;
		bool p_ScrapFirst = true;

		void InitCounter()
		{
			Counter = new System.Diagnostics.PerformanceCounter()
			{
				CategoryName = p_CategoryName,
				CounterName = p_CounterName,
				InstanceName = p_InstanceName,
				ReadOnly = true,
			};

			if (p_ScrapFirst) { var scrap = Value; }

			Manager.Sensors.Add(Counter);
		}

		public PerformanceCounterWrapper(string category, string counter, string instance = null, bool scrapfirst = true)
		{
			p_CategoryName = category;
			p_CounterName = counter;
			p_InstanceName = instance;
			p_ScrapFirst = scrapfirst;
			InitCounter();
		}

		bool disposed = false;

		public void Dispose()
		{
			Dispose(true);
		}

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
						Manager.Sensors?.Remove(Counter);
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
					Manager.Sensors.Remove(Counter);
					Counter.Dispose();
					// TODO: Driver/Adapter vanished and other problems, try to re-acquire it.
					Debug.WriteLine("DEBUG :: PFC(" + Counter.CategoryName + "//" + Counter.CounterName + "//" + Counter.InstanceName + ") vanished.");
					throw;
				}

				return float.NaN;
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
