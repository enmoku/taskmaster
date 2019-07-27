//
// Process.LoadInfo.cs
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
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Management;
using MKAh;

namespace Taskmaster.Process
{
	public class LoadInfo : IDisposable
	{
		public ConcurrentDictionary<int, ProcessEx> Processes = new ConcurrentDictionary<int, ProcessEx>();

		public int Order { get; set; }

		public string Instance { get; }

		public int LastPid { get; private set; }

		public int InstanceCount => Processes.Count;

		public LoadType Heaviest { get; private set; } = LoadType.None;

		public DateTimeOffset First { get; } = DateTimeOffset.UtcNow;

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

		public LoadInfo(string instance, LoadType load, ProcessEx initial = null)
		{
			Debug.Assert(load != LoadType.None, "Load can't be none");

			Instance = instance;
			First = DateTimeOffset.UtcNow;

			//if ((load & LoadType.CPU) != 0)
			//	CPU = new MKAh.Wrapper.Windows.PerformanceCounter("Process", "% Processor Time", instance);
			//if ((load & LoadType.RAM) != 0)
			//	RAM = new MKAh.Wrapper.Windows.PerformanceCounter("Process", "Private Bytes", instance);
			//if ((load & LoadType.IO) != 0)
			//	IO = new MKAh.Wrapper.Windows.PerformanceCounter("Process", "IO Data Bytes/sec", instance);

			if (Taskmaster.Trace)
				Logging.DebugMsg("LOADER CONFIG: " + instance + " - Types: " + load.ToString());

			if (initial != null) Add(initial);
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

			float ramload = 0f, // GB
				cpuloadraw = 0f,
				ioload = 0f;

			float highRam = 0f, highCpu = 0f, highIo = 0f;
			int highPid = 0;

			var removeList = new System.Collections.Generic.List<ProcessEx>(2);

			foreach (var info in Processes.Values)
			{
				try
				{
					if (info.Process.HasExited)
					{
						removeList.Add(info);
						continue;
					}

					info.Loaders.Update();

					float tram = Convert.ToSingle(info.Process.PrivateMemorySize64) / 1_073_741_824f;
					ramload += tram;
					var load = info.Loaders;
					cpuloadraw += load.CPU;
					ioload += load.IO;

					if (tram > highRam)
					{
						highRam = tram;
						highPid = info.Id;
					}
					if (load.CPU > highCpu)
					{
						highCpu = load.CPU;
						highPid = info.Id;
					}
					if (load.IO > highIo)
					{
						highIo = load.IO;
						highPid = info.Id;
					}
				}
				catch { } // ignore

				// TODO: Add fake load for each failed thing to mimic knowing it?
			}

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

		public void Remove(ProcessEx info)
		{
			Processes.TryRemove(info.Id, out _);
			info.Loaders = null;
		}

		public void Add(ProcessEx info)
		{
			if (Processes.TryAdd(info.Id, info))
			{
				try
				{
					info.Loaders = new ProcessLoad(info.Id, info.Name);
					info.Loaders.Update();
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
					Remove(info);
				}
			}
		}

		public bool LastHeavy { get; set; } = false;

		public bool LastLight { get; set; } = false;

		public uint Heavy { get; private set; } = 0;

		public uint Light { get; private set; } = 0;

		public bool Mixed => Light > 2 && Heavy > 2;

		#region IDisposable Support
		bool disposed = false;

		~LoadInfo() => Dispose();

		protected virtual void Dispose(bool disposing)
		{
			if (disposed) return;
			disposed = true;

			if (disposing)
			{
				Processes?.Clear();
			}
		}

		public void Dispose() => Dispose(true);
		#endregion
	}

	public class LoadValue
	{
		public bool Heavy { get; set; } = false;

		public uint HeavyCount { get; set; } = 0;

		public bool Light { get; set; } = false;

		public uint LightCount { get; set; } = 0;

		public float Min { get; private set; } = float.MaxValue;

		public float Current { get; private set; } = 0f;

		public float Max { get; private set; } = float.MinValue;

		public float Average { get; private set; } = 0f;

		float[] Samples = { 0, 0, 0, 0, 0 };
		int SampleIndex = 0;

		public LoadType Type { get; set; } = LoadType.None;

		float AverageThreshold, MaxThreshold, MinThreshold;

		public LoadValue(LoadType type, float average, float max, float min)
		{
			Type = type;

			AverageThreshold = average;
			MaxThreshold = max;
			MinThreshold = min;
		}

		public void Update(float value)
		{
			if (float.IsNaN(value)) return;

			Current = value;
			if (Min > Current) Min = Current;
			if (Max < Current) Max = Current;

			Samples[SampleIndex++ % 5] = Current; // BUG: Does ++ wrap around or just break?

			Average = (Samples[0] + Samples[1] + Samples[2] + Samples[3] + Samples[4]) / 5;
		}
	}

	[Flags]
	public enum LoadType : int
	{
		None = 0,
		CPU = 1,
		RAM = 2,
		IO = 4,
		Storage = RAM | IO,
		Volatile = CPU | RAM,
		All = CPU | RAM | IO,
	}
}
