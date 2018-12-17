//
// CPUMonitor.cs
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
using MKAh;
using Serilog;

namespace Taskmaster
{
	public class CPUMonitor : IDisposable
	{
		public event EventHandler<ProcessorEventArgs> onSampling;

		/// <summary>
		/// Sample Interval. In seconds.
		/// </summary>
		public int SampleInterval { get; set; } = 5;
		public int SampleCount { get; set; } = 5;
		PerformanceCounterWrapper Counter = null;
		System.Timers.Timer Timer = null;

		float[] Samples;
		int SampleLoop = 0;
		float Average, Low, High;

		public CPUMonitor()
		{
			LoadConfig();

			Start();

			Taskmaster.DisposalChute.Push(this);
		}

		public void LoadConfig()
		{
			var corecfg = Taskmaster.Config.Load(Taskmaster.coreconfig);

			bool dirtyconfig = false, modified = false;

			// SAMPLING
			// this really should be elsewhere
			var hwsec = corecfg.Config["Hardware"];
			SampleInterval = hwsec.GetSetDefault("CPU sample interval", 2, out modified).IntValue.Constrain(1, 15);
			hwsec["CPU sample interval"].Comment = "1 to 15, in seconds. Frequency at which CPU usage is sampled. Recommended value: 1 to 5 seconds.";
			dirtyconfig |= modified;
			SampleCount = hwsec.GetSetDefault("CPU sample count", 5, out modified).IntValue.Constrain(3, 30);
			hwsec["CPU sample count"].Comment = "3 to 30. Number of CPU samples to keep. Recommended value is: Count * Interval <= 30 seconds";
			dirtyconfig |= modified;

			Log.Information("<CPU> Sampler: " + SampleInterval + "s × " + SampleCount +
				" = " + (SampleCount * SampleInterval) + "s observation period");

			if (dirtyconfig) corecfg.MarkDirty();
		}

		public void SaveConfig()
		{
			var corecfg = Taskmaster.Config.Load(Taskmaster.coreconfig);

			// SAMPLING
			var hwsec = corecfg.Config["Hardware"];
			hwsec["CPU sample interval"].IntValue = SampleInterval;
			hwsec["CPU sample count"].IntValue = SampleCount;

			corecfg.MarkDirty();
		}

		public void Start()
		{
			try
			{
				if (Counter == null)
					Counter = new PerformanceCounterWrapper("Processor", "% Processor Time", "_Total");

				if (Timer == null)
				{
					Timer = new System.Timers.Timer(SampleInterval * 1000);
					Timer.Elapsed += Sampler;

					Samples = new float[SampleCount];

					// prepopulate
					for (int i = 0; i < SampleCount; i++)
					{
						Samples[i] = Counter.Value;
						Average += Samples[i];
					}
				}

				Timer.Start();
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				Stop();
			}
		}

		public void Stop()
		{
			Timer?.Dispose();
			Timer = null;

			Counter?.Dispose();
			Counter = null;
		}

		int sampler_lock = 0;

		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		void Calculate()
		{
			float tLow = float.MaxValue, tHigh = float.MinValue, tAverage = 0;
			for (int i = 0; i < SampleCount; i++)
			{
				var cur = Samples[i];
				tAverage += cur;
				if (cur < tLow) tLow = cur;
				if (cur > tHigh) tHigh = cur;
			}

			Low = tLow;
			High = tHigh;
			Average = tAverage / SampleCount;
		}

		void Sampler(object sender, EventArgs ev)
		{
			if (!Atomic.Lock(ref sampler_lock)) return; // uhhh... probably should ping warning if this return is triggered

			float sample = Counter.Value; // slowest part
			Samples[SampleLoop] = sample;
			SampleLoop = (SampleLoop + 1) % SampleCount; // loop offset

			Calculate();

			onSampling?.Invoke(this, new ProcessorEventArgs()
			{
				Current = sample,
				Average = Average,
				High = High,
				Low = Low
			});

			Atomic.Unlock(ref sampler_lock);
		}

		#region IDisposable Support
		private bool disposed = false; // To detect redundant calls

		protected virtual void Dispose(bool disposing)
		{
			if (!disposed)
			{
				if (disposing)
				{
					Stop();
					SaveConfig();
				}

				disposed = true;
			}
		}

		public void Dispose()
		{
			Dispose(true);
		}
		#endregion
	}

	public class ProcessorEventArgs : EventArgs
	{
		public float Current;
		public float Average;
		public float Low;
		public float High;
	}
}
