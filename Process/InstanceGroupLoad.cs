//
// Process.InstanceGroupLoad.cs
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

using MKAh;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace Taskmaster.Process
{
	public class InstanceGroupLoad : IDisposable
	{
		public ConcurrentDictionary<int, ProcessEx> Processes = new ConcurrentDictionary<int, ProcessEx>();

		public enum InterestLevel { Active, Partial, Ignoring }

		public InterestLevel InterestType { get; set; } = InterestLevel.Active;

		public int Order { get; set; }

		public string Instance { get; }

		string SubInstance { get; }

		public int LastPid { get; private set; }

		public int InstanceCount => Processes.Count;

		public LoadType Heaviest { get; private set; } = LoadType.None;

		public DateTimeOffset First { get; }

		public DateTimeOffset Last { get; private set; } = DateTimeOffset.UtcNow;

		/*
		ManagementObjectSearcher searcher = new ManagementObjectSearcher("select * from Win32_PerfFormattedData_PerfOS_Processor");
		var cpuTimes = searcher.Get()
		.Cast<ManagementObject>()
		.Select(mo => new
			{
	            Name = mo["Name"],
				Usage = mo["PercentProcessorTime"]
			}
	    ).ToArray();
		*/

		public InstanceGroupLoad(string instance, LoadType load, ProcessEx? initial = null)
		{
			Debug.Assert(load != LoadType.None, "Load can't be none");

			Instance = instance;
			SubInstance = instance + "#";
			First = DateTimeOffset.UtcNow;

			//if ((load & LoadType.CPU) != 0)
			//	CPU = new MKAh.Wrapper.Windows.PerformanceCounter("Process", "% Processor Time", instance);
			//if ((load & LoadType.RAM) != 0)
			//	RAM = new MKAh.Wrapper.Windows.PerformanceCounter("Process", "Private Bytes", instance);
			//if ((load & LoadType.IO) != 0)
			//	IO = new MKAh.Wrapper.Windows.PerformanceCounter("Process", "IO Data Bytes/sec", instance);

			if (Application.Trace)
				Logging.DebugMsg("LOADER CONFIG: " + instance + " - Types: " + load.ToString());

			if (initial != null) TryAdd(initial);
		}

		public LoadValue CPULoad { get; set; } = new LoadValue(LoadType.CPU, 60f, 20f, 5f);

		public LoadValue IOLoad { get; set; } = new LoadValue(LoadType.IO, 1f, 1f, 1f);

		public LoadValue RAMLoad { get; } = new LoadValue(LoadType.RAM, 2048f, 512f, 256f);

		public int Samples { get; private set; }

		public float Load { get; private set; }

		//readonly ManagementObjectSearcher cpusearcher = new ManagementObjectSearcher("SELECT * from Win32_PerfFormattedData_PerfOS_Processor");

		readonly Stopwatch updateTimer = Stopwatch.StartNew();

		public int UninterestingInstances { get; private set; }

		public bool Interesting { get; private set; } = true;

		public long Interest { get; set; }

		public int Disinterest { get; set; }

		public int MaxDisinterest { get; set; } = 3;

		public void Update()
		{
			if (isdisposed || InstanceCount == 0) return;

			Samples++;

			Last = DateTimeOffset.UtcNow;

			//RAMLoad.Update(RAM.Value / 1_048_576f); // MiB

			bool heavy = false, light = false;

			// 1/10 %CPU usage, Gigabytes, MB/s IO usage
			// 5 = heavy load, 10 = very heavy load, 15+ system should be having trouble

			float cpuloadraw = 0f, io_op_load = 0f;

			long ramloadt = 0L;

			//var now = DateTimeOffset.UtcNow;

			float highRam = 0f, highCpu = 0f, highIo = 0f, cput, io_ops_t;
			int highPid = LastPid;

			//var removeList = new System.Collections.Generic.List<ProcessEx>(2);
			var elapsed = updateTimer.ElapsedMilliseconds;

			int newUninterestingInstances = 0;

			foreach (var info in Processes.Values)
			{
				if (info.Exited)
				{
					//removeList.Add(info);
					Remove(info);
					continue;
				}

				try
				{
					ref var load = ref info.Load;
					if (load is null)
					{
						// Why isn't this being tracked? Why is it Still here?

						continue;
					}

					if (!load.Update(elapsed))
					{
						// Disinterest
						newUninterestingInstances++;

						if (load.Mid == 0 && load.High == 0)
							load.LessenInterest();
						else
							load.ResetInterest();

						continue;
					}

					// cache so we don't update only half if there's failures
					cput = load.CPU;
					cpuloadraw += cput;
					if (highCpu < cput)
					{
						highCpu = cput;
						highPid = info.Id;
					}

					io_ops_t = load.IO;
					io_op_load += io_ops_t;
					if (highIo < io_ops_t) highIo = io_ops_t;

					// update
					var ram_t = info.Process.PrivateMemorySize64;
					ramloadt += ram_t; // process needs to be refreshed for this somewhere

					if (highRam < ram_t) highRam = ram_t;
				}
				catch
				{
					//removeList.Add(info);
					Remove(info);
				} // ignore
				// TODO: Add fake load for each failed thing to mimic knowing it?
			}
			updateTimer.Restart();

			UninterestingInstances = newUninterestingInstances;

			Interesting = InstanceCount > UninterestingInstances;

			float ramloadgb = Convert.ToSingle(Convert.ToDouble(ramloadt) / MKAh.Units.Binary.Giga);

			//foreach (var info in removeList)
			//	Remove(info);

			cpuloadraw *= 100f / Hardware.Utility.ProcessorCount;
			//cpuloadraw /= (Hardware.Utility.ProcessorCount * 100f); // turn load into human readable format

			CPULoad.Update(cpuloadraw);
			RAMLoad.Update(ramloadt);
			IOLoad.Update(io_op_load);

			var ioaverage_ops = IOLoad.Average;

			Load = (CPULoad.Average / 10f) + ramloadgb + (ioaverage_ops / 100f).Max(2f);

			if (cpuloadraw < 20f && ramloadgb < 4f) Load -= ramloadgb / 3f; // reduce effect of ramload on low cpu load

			if (CPULoad.Average > 60f)
			{
				CPULoad.HeavyCount++;
				CPULoad.Heavy = true;
				Heavy++;
				heavy = true;
			}

			if (CPULoad.Max < 20f && CPULoad.Min < 5f)
			{
				CPULoad.LightCount++;
				CPULoad.Light = true;
				Light++;
				light = true;
			}

			if (RAMLoad.Average > 1f) // over 1GB
			{
				RAMLoad.HeavyCount++;
				RAMLoad.Heavy = true;
				Heavy++;
				heavy = true;
			}
			else if (RAMLoad.Max < 0.5f) // under 0.5 GB
			{
				RAMLoad.LightCount++;
				RAMLoad.Light = true;
				Light++;
				light = true;
			}

			if (ioaverage_ops > 100f)
			{
				IOLoad.HeavyCount++;
				IOLoad.Heavy = true;
				Heavy++;
				heavy = true;
			}
			else if (IOLoad.Max < 5f)
			{
				IOLoad.LightCount++;
				IOLoad.Light = true;
				Light++;
				light = true;
			}

			LastHeavy = heavy;
			LastLight = light;

			/*
			Heaviest = (CPULoad.Heavy ? LoadType.CPU : LoadType.None)
				| (RAMLoad.Heavy ? LoadType.RAM : LoadType.None)
				| (IOLoad.Heavy ? LoadType.IO : LoadType.None);
			*/

			LastPid = highPid;
		}

		public void Remove(ProcessEx info)
		{
			Processes.TryRemove(info.Id, out _);
			info.Load?.Dispose();
			info.Load = null;
		}

		public bool TryAdd(ProcessEx info)
		{
			int pid = info.Id;
			if (Processes.TryAdd(pid, info))
			{
				ProcessLoad? loader = null;
				try
				{
					loader = new ProcessLoad(info);
					loader.Update();

					info.Load = loader;

					if (info.Exited) return false;

					return true;
				}
				catch (ArgumentException)
				{
					Logging.DebugMsg("LoadInfo process ID not found: " + pid.ToString(CultureInfo.InvariantCulture) + " for " + Instance);
				}
				catch (InvalidOperationException)
				{
					// exited
					loader?.Dispose();
					Remove(info);
					throw;
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
					throw;
				}

				loader?.Dispose();
				Remove(info);
			}

			return false;
		}

		public bool LastHeavy { get; set; }

		public bool LastLight { get; set; }

		public uint Heavy { get; private set; }

		public uint Light { get; private set; }

		public bool Mixed => Light > 2 && Heavy > 2;

		#region IDisposable Support
		bool isdisposed;

		~InstanceGroupLoad() => Dispose(false);

		protected virtual void Dispose(bool disposing)
		{
			if (isdisposed) return;
			isdisposed = true;

			if (disposing)
			{
				Processes.Clear();

				//base.Dispose();
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