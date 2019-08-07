//
// Process.ProcessLoad.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2019 M.A.
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

namespace Taskmaster.Process
{
	public class ProcessLoad : IDisposable
	{
		MKAh.Wrapper.Windows.PerformanceCounter CPUCounter, IOCounter;

		readonly string Instance;
		readonly int Id;

		public float CPU { get; private set; } = float.NaN;

		public float IO { get; private set; } = float.NaN;

		/// <summary>
		/// 
		/// </summary>
		/// <param name="pid">Process identifier.</param>
		/// <param name="executable">Executable name without extension.</param>
		public ProcessLoad(System.Diagnostics.Process process)
		{
			Id = process.Id;
			Instance = process.ProcessName;

			CpuLoad = new CpuUsage(process);

			Refresh();

			Reset();
		}

		public DateTimeOffset Start { get; private set; }

		public void Reset() => Start = DateTimeOffset.UtcNow;

		public bool Update()
		{
			if (disposed) return false;

			if (Slowed && SlowCount < 3)
				return false;
			else
				SlowCount = 0;

			try
			{
				//CPU = CPUCounter.Value / Environment.ProcessorCount;
				CPU = Convert.ToSingle(CpuLoad.Sample()) * 100f;
				//if (CPU > 3f) Logging.DebugMsg($"ProcessLoad --- PFC: {CPU:N1}% --- TMS: {cpu*100d:N1}%");

				IO = IOCounter.Value;

				LoadHistory.Push(CPU);

				if (CPU < 3f) Low++;
				else if (CPU > 30f) High++;
				else Mid++;

				if (Low > 30 && High == 0)
				{
					Slowed = true;
					SlowCount = 0;

					High = Mid = Low = 0;
				}
				else if (Mid > 2 || High > 0)
					Slowed = false;

				return true;
			}
			catch (NullReferenceException)
			{
				Logging.DebugMsg("ProcessEx null counter: " + Instance + " #" + Id.ToString());
				/* don't really care, probably weird timing with disposal */
			}

			return false;
		}

		public bool Slowed { get; set; } = false;

		public void Resume() => Slowed = false;

		int SlowCount { get; set; } = 0;

		public long Low { get; private set; } = 0;
		public long Mid { get; private set; } = 0;
		public long High { get; private set; } = 0;

		public MKAh.Container.CircularBuffer<float> LoadHistory { get; private set; } = new MKAh.Container.CircularBuffer<float>(10);

		void Refresh()
		{
			Scrap();

			//CPUCounter = new MKAh.Wrapper.Windows.PerformanceCounter("Process", "% Processor Time", Instance);
			IOCounter = new MKAh.Wrapper.Windows.PerformanceCounter("Process", "IO Data Bytes/sec", Instance);
		}

		CpuUsage CpuLoad;

		#region IDisposable Support
		bool disposed = false;

		void Scrap()
		{
			//CPUCounter?.Dispose();
			//CPUCounter = null;
			IOCounter?.Dispose();
			IOCounter = null;
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposed) return;
			disposed = true;

			if (disposing)
			{
				Scrap();

				//base.Dispose();
			}
		}

		~ProcessLoad() => Dispose(false);

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}
		#endregion
	}
}
