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
using System.Linq;

namespace Taskmaster.Process
{
	public class InstanceGroupLoad : IDisposable
	{
		public ConcurrentDictionary<int, ProcessEx> Processes = new ConcurrentDictionary<int, ProcessEx>();

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

		public InstanceGroupLoad(string instance, LoadType load, ProcessEx initial = null)
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

			if (Taskmaster.Trace)
				Logging.DebugMsg("LOADER CONFIG: " + instance + " - Types: " + load.ToString());

			if (initial != null) TryAdd(initial);
		}

		public LoadValue CPULoad { get; set; } = new LoadValue(LoadType.CPU, 60f, 20f, 5f);

		public LoadValue IOLoad { get; set; } = new LoadValue(LoadType.IO, 1f, 1f, 1f);

		public LoadValue RAMLoad { get; } = new LoadValue(LoadType.RAM, 2048f, 512f, 256f);

		public int Samples { get; private set; } = 0;

		public float Load { get; private set; } = 0;

		//readonly ManagementObjectSearcher cpusearcher = new ManagementObjectSearcher("SELECT * from Win32_PerfFormattedData_PerfOS_Processor");

		public void Update()
		{
			if (disposed) return;

			Samples++;

			Last = DateTimeOffset.UtcNow;

			/*
			float cpuTimes = cpusearcher.Get()
				.Cast<ManagementObject>()
				.Where(mo => (mo["Name"] as string)?.Equals(Instance, StringComparison.InvariantCultureIgnoreCase) ?? false)
				.Select(mo => ulong.TryParse(mo["PercentProcessorTime"] as string, out ulong result) ? result : 0f)
				.DefaultIfEmpty()
				.Average();
			*/

			//RAMLoad.Update(RAM.Value / 1_048_576f); // MiB

			bool heavy = false, light = false;

			// 1/10 %CPU usage, Gigabytes, MB/s IO usage
			// 5 = heavy load, 10 = very heavy load, 15+ system should be having trouble

			float cpuloadraw = 0f,
				ioload = 0f;

			long ramloadt = 0L;


			var now = DateTimeOffset.UtcNow;

			float highRam = 0f, highCpu = 0f, highIo = 0f, cpu, io;
			int highPid = 0;
			long mem;

			var removeList = new System.Collections.Generic.List<ProcessEx>(2);

			foreach (var info in Processes.Values)
			{
				if (info.Exited)
				{
					removeList.Add(info);
					continue;
				}

				try
				{
					ref var load = ref info.Load;
					if (load is null) continue;

					load.Update();

					// cache so we don't update only half if there's failures
					cpu = load.CPU;
					io = load.IO;
					mem = info.Process.PrivateMemorySize64;

					// update
					ramloadt += mem;
					cpuloadraw += load.CPU;
					ioload += load.IO;
				}
				catch
				{
					removeList.Add(info);
				} // ignore

				// TODO: Add fake load for each failed thing to mimic knowing it?
			}

			float ramload = Convert.ToSingle(Convert.ToDouble(ramloadt) / 1_073_741_824d);

			foreach (var info in removeList)
				Remove(info);

			CPULoad.Update(cpuloadraw);

			float cpuload = CPULoad.Average / 10f;

			CPULoad.Update(cpuload); //CPU.Value
			RAMLoad.Update(ramload);
			IOLoad.Update(ioload / 1_048_576f); // MiB/s

			Load = cpuload + ramload + IOLoad.Average.Max(10f);
			if (cpuload < 20f && ramload < 4f) Load -= ramload / 3f; // reduce effect of ramload on low cpu load

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

			if (IOLoad.Average > 1f)
			{
				IOLoad.HeavyCount++;
				IOLoad.Heavy = true;
				Heavy++;
				heavy = true;
			}
			else if (IOLoad.Max < 1f)
			{
				IOLoad.LightCount++;
				IOLoad.Light = true;
				Light++;
				light = true;
			}

			LastHeavy = heavy;
			LastLight = light;

			Heaviest = (CPULoad.Heavy ? LoadType.CPU : LoadType.None)
				| (RAMLoad.Heavy ? LoadType.RAM : LoadType.None)
				| (IOLoad.Heavy ? LoadType.IO : LoadType.None);

			LastPid = highPid;
		}

		MKAh.Container.CircularBuffer<float> LoadHistory = new MKAh.Container.CircularBuffer<float>(10);

		public void Remove(ProcessEx info)
		{
			Processes.TryRemove(info.Id, out _);
			info.Load = null;
		}

		public bool TryAdd(ProcessEx info)
		{
			int pid = info.Id;
			string instance;

			if (!GetInstance(pid, out instance))
			{
				Logging.DebugMsg("LoadInfo.TryAdd failed to locate instance for " + Instance + " #" + pid);
				return false;
			}

			if (Processes.TryAdd(pid, info))
			{
				ProcessLoad loader = null;
				try
				{
					loader = new ProcessLoad(info.Process);
					loader.Update();

					info.Load = loader;

					return true;
				}
				catch (ArgumentException)
				{
					Logging.DebugMsg("LoadInfo process ID not found: " + pid.ToString() + " for " + Instance);
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
				}

				loader?.Dispose();
				Remove(info);
			}

			return false;
		}

		char[] PCInstanceSeparator = { '#' };

		readonly object instancelock = new object();

		bool GetInstance(int pid, out string instance)
		{
			var now = DateTimeOffset.UtcNow;

			bool rv;
			lock (instancelock)
			{
				if (LastInstancePull.To(now).TotalSeconds > 5)
				{
					GetInstanceNames();
					LastInstancePull = DateTimeOffset.UtcNow;
				}

				rv = IdToInstanceMap.TryGetValue(pid, out var cache);
				instance = cache?.Instance;
			}
			return rv;
		}

		DateTimeOffset LastInstancePull = DateTimeOffset.MinValue;

		/// <summary>
		/// Searches for performance counter instance name when it has changed.
		/// </summary>
		void GetInstanceNames()
		{
			var processCategory = new PerformanceCounterCategory("Process");

			var instances = processCategory.GetInstanceNames()
				.Where(inst => inst.Equals(Instance, StringComparison.InvariantCultureIgnoreCase) || inst.StartsWith(SubInstance, StringComparison.InvariantCultureIgnoreCase));

			var now = DateTimeOffset.UtcNow;

			foreach (var name in instances)
			{
				using var idpc = new MKAh.Wrapper.Windows.PerformanceCounter("Process", "ID Process", name, false);

				_ = idpc.Raw;

				int pid = Convert.ToInt32(idpc.Raw);

				//if (Trace) Logging.DebugMsg("PFInstance found: " + name + " for #" + pid.ToString());

				var cache = new InstanceCachePair(name, now);

				IdToInstanceMap.AddOrUpdate(pid, cache, (_, oldValue) => oldValue.Replace(name, now));
			}
		}

		ConcurrentDictionary<int, InstanceCachePair> IdToInstanceMap = new ConcurrentDictionary<int, InstanceCachePair>(2, 128);

		internal class InstanceCachePair
		{
			public string Instance;
			public DateTimeOffset Seen;

			internal InstanceCachePair(string instance, DateTimeOffset seen)
			{
				Instance = instance;
				Seen = seen;
			}

			internal InstanceCachePair Replace(string instance, DateTimeOffset seen)
			{
				Instance = instance;
				Seen = seen;
				return this;
			}
		}

		public bool LastHeavy { get; set; } = false;

		public bool LastLight { get; set; } = false;

		public uint Heavy { get; private set; } = 0;

		public uint Light { get; private set; } = 0;

		public bool Mixed => Light > 2 && Heavy > 2;

		#region IDisposable Support
		bool disposed = false;

		~InstanceGroupLoad() => Dispose(false);

		protected virtual void Dispose(bool disposing)
		{
			if (disposed) return;
			disposed = true;

			if (disposing)
			{
				Processes?.Clear();

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