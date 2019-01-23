//
// CPUMonitor.cs
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
using System.Collections.Concurrent;
using System.Diagnostics;
using MKAh;
using Serilog;

namespace Taskmaster
{
	public class CPUMonitor : IDisposable
	{
		// Experimental feature
		public bool CPULoaderMonitoring { get; set; } = false;

		public event EventHandler<ProcessorLoadEventArgs> onSampling;

		/// <summary>
		/// Sample Interval.
		/// </summary>
		public TimeSpan SampleInterval { get; set; } = TimeSpan.FromSeconds(5);
		public int SampleCount { get; set; } = 5;
		PerformanceCounterWrapper Counter = new PerformanceCounterWrapper("Processor", "% Processor Time", "_Total");
		System.Threading.Timer CPUSampleTimer = null;

		readonly float[] Samples;
		int SampleLoop = 0;
		float Average, Low, High;

		public CPUMonitor()
		{
			LoadConfig();

			try
			{
				if (CPUSampleTimer == null)
				{
					Samples = new float[SampleCount];

					// prepopulate
					for (int i = 0; i < SampleCount; i++)
					{
						Samples[i] = Counter.Value;
						Average += Samples[i];
					}

					CPUSampleTimer = new System.Threading.Timer(Sampler, null, System.Threading.Timeout.InfiniteTimeSpan, SampleInterval);
				}

				CPUSampleTimer.Change(TimeSpan.FromSeconds(1), SampleInterval); // start
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				CPUSampleTimer?.Dispose();
			}

			Taskmaster.DisposalChute.Push(this);
		}

		public void LoadConfig()
		{
			var corecfg = Taskmaster.Config.Load(Taskmaster.coreconfig);

			bool dirtyconfig = false, modified = false;

			// SAMPLING
			// this really should be elsewhere
			var hwsec = corecfg.Config[HumanReadable.Hardware.Section];
			SampleInterval = TimeSpan.FromSeconds(hwsec.GetSetDefault(HumanReadable.Hardware.CPU.Settings.SampleInterval, 2, out modified).IntValue.Constrain(1, 15));
			hwsec[HumanReadable.Hardware.CPU.Settings.SampleInterval].Comment = "1 to 15, in seconds. Frequency at which CPU usage is sampled. Recommended value: 1 to 5 seconds.";
			dirtyconfig |= modified;
			SampleCount = hwsec.GetSetDefault(HumanReadable.Hardware.CPU.Settings.SampleCount, 5, out modified).IntValue.Constrain(3, 30);
			hwsec[HumanReadable.Hardware.CPU.Settings.SampleCount].Comment = "3 to 30. Number of CPU samples to keep. Recommended value is: Count * Interval <= 30 seconds";
			dirtyconfig |= modified;

			var exsec = corecfg.Config["Experimental"];
			CPULoaderMonitoring = exsec.TryGet("CPU loaders")?.BoolValue ?? false;

			Log.Information("<CPU> Sampler: " + $"{ SampleInterval.TotalSeconds:N0}" + "s × " + SampleCount +
				" = " + $"{SampleCount * SampleInterval.TotalSeconds:N0}s" + " observation period");

			if (dirtyconfig) corecfg.MarkDirty();
		}

		public void SaveConfig()
		{
			return; // these are not modified at runtime YET

			var corecfg = Taskmaster.Config.Load(Taskmaster.coreconfig);

			// SAMPLING
			var hwsec = corecfg.Config[HumanReadable.Hardware.Section];
			hwsec[HumanReadable.Hardware.CPU.Settings.SampleInterval].IntValue = Convert.ToInt32(SampleInterval.TotalSeconds);
			hwsec[HumanReadable.Hardware.CPU.Settings.SampleCount].IntValue = SampleCount;

			corecfg.MarkDirty();
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

		void Sampler(object _)
		{
			if (!Atomic.Lock(ref sampler_lock)) return; // uhhh... probably should ping warning if this return is triggered
			if (disposed) return; // HACK: dumbness with timers

			try
			{
				float sample = Counter.Value; // slowest part
				Samples[SampleLoop] = sample;
				SampleLoop = (SampleLoop + 1) % SampleCount; // loop offset

				Calculate();

				onSampling?.Invoke(this, new ProcessorLoadEventArgs()
				{
					Current = sample,
					Average = Average,
					High = High,
					Low = Low,
					Period = SampleInterval
				});
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				Atomic.Unlock(ref sampler_lock);
			}
		}


		ProcessManager prcman = null;
		public void Hook(ProcessManager processmanager)
		{
			prcman = processmanager;

			if (CPULoaderMonitoring)
			{
				//prcman.ProcessDetectedEvent += ProcessDetectedEvent;
			}
		}

		/*
		#region Load monitoring
		ConcurrentDictionary<int, ProcessEx> Loaders = new ConcurrentDictionary<int, ProcessEx>();
		ConcurrentDictionary<int, ProcessEx> Monitoring = new ConcurrentDictionary<int, ProcessEx>();
		ConcurrentDictionary<string, CounterChunk> ProcessCounters = new ConcurrentDictionary<string, CounterChunk>();

		object monitoring_lock = new object();

		async void FindLoaders()
		{
			try
			{

			}
			catch (Exception ex)
			{

			}
		}

		async void ProcessDetectedEvent(object _, HandlingStateChangeEventArgs ev)
		{
			ReceiveProcess(ev.Info);
		}

		async void ReceiveProcess(ProcessEx info)
		{
			try
			{
				if (Monitoring.TryAdd(info.Id, info))
				{
					info.Process.Exited += RemoveProcess;
					info.Process.EnableRaisingEvents = true;

					CounterChunk counterchunk = null;
					if (ProcessCounters.TryGetValue(info.Name, out counterchunk))
					{
						counterchunk.References += 1;
					}
					else
					{
						var cpucounter = new PerformanceCounterWrapper("Processor", "% Processor Time", info.Name);
						var memcounter = new PerformanceCounterWrapper("Process", "Working Set", info.Name);

						counterchunk = new CounterChunk()
						{
							CPUCounter = cpucounter,
							MEMCounter = memcounter,

							Name = info.Name,
							References = 1
						};

						ProcessCounters.TryAdd(info.Name, counterchunk);
					}

					info.Process.Refresh();
					if (info.Process.HasExited) RemoveProcess(info.Process, null);
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		async void RemoveProcess(object sender, EventArgs _)
		{
			try
			{
				var process = (Process)sender;

				if (Monitoring.TryRemove(process.Id, out ProcessEx info))
				{
					if (ProcessCounters.TryGetValue(info.Name, out var counterchunk))
					{
						if (--counterchunk.References <= 0)
						{
							ProcessCounters.TryRemove(info.Name, out _);
						}
					}
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}
		#endregion Load monitoring
		*/

		#region IDisposable Support
		private bool disposed = false; // To detect redundant calls

		protected virtual void Dispose(bool disposing)
		{
			if (disposed) return;

			if (disposing)
			{
				CPUSampleTimer?.Dispose();
				CPUSampleTimer = null;

				Counter?.Dispose();
				Counter = null;

				if (prcman != null)
				{
					//prcman.ProcessDetectedEvent -= ProcessDetectedEvent;
					prcman = null;
				}

				SaveConfig();
			}

			disposed = true;
		}

		public void Dispose()
		{
			Dispose(true);
		}
		#endregion
	}

	internal sealed class CounterChunk
	{
		internal PerformanceCounterWrapper CPUCounter = null;
		internal PerformanceCounterWrapper MEMCounter = null;
		internal uint References = 0;
		internal string Name = string.Empty;
		internal ConcurrentDictionary<int, ProcessEx> Processes = new ConcurrentDictionary<int, ProcessEx>();
	}
}
