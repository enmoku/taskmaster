//
// ProcessManagerUtility.cs
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
using System.Diagnostics;
using System.Management;
using MKAh;
using Serilog;

namespace Taskmaster
{
	public static class ProcessManagerUtility
	{
		/// <summary>
		/// Use FindPath() instead. This is called by it.
		/// </summary>
		public static bool FindPathExtended(ProcessEx info)
		{
			Debug.Assert(string.IsNullOrEmpty(info.Path), "FindPathExtended called even though path known.");

			Statistics.PathFindAttempts++;

			try
			{
				info.Path = info.Process?.MainModule?.FileName ?? string.Empty; // this will cause win32exception of various types, we don't Really care which error it is
			}
			catch (System.ComponentModel.Win32Exception)
			{
				// Access denied problems of varying sorts
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				// NOP, don't care 
			}

			if (string.IsNullOrEmpty(info.Path))
			{
				if (!GetProcessPathViaC(info.Id, out info.Path))
				{
					if (!GetProcessPathViaWMI(info.Id, out info.Path))
					{
						Statistics.PathNotFound++;
						return false;
					}
					Statistics.PathFindViaWMI++;
				}
				else
					Statistics.PathFindViaC++;
			}
			else
				Statistics.PathFindViaModule++;

			return true;
		}

		// https://stackoverflow.com/a/34991822
		public static bool GetProcessPathViaC(int pid, out string path)
		{
			//const int PROCESS_QUERY_INFORMATION = 0x0400;
			// const int PROCESS_VM_READ = 0x0010; // is this really needed?
			path = string.Empty;

			//var processHandle = NativeMethods.OpenProcess(PROCESS_QUERY_INFORMATION, false, pid);
			var proc = Process.GetProcessById(pid);
			//if (processHandle == IntPtr.Zero)
			if (proc == null) return false;

			const int lengthSb = 32768; // this is the maximum path length NTFS supports

			var sb = new System.Text.StringBuilder(lengthSb);

			if (NativeMethods.GetModuleFileNameEx(proc.Handle, IntPtr.Zero, sb, lengthSb) > 0)
			{
				// result = Path.GetFileName(sb.ToString());
				path = sb.ToString();
				return true;
			}

			//NativeMethods.CloseHandle(processHandle);
			return false;
		}

		/// <summary>
		/// Retrieve file path for the process.
		/// Slow due to use of WMI.
		/// </summary>
		/// <returns>The process path.</returns>
		/// <param name="processId">Process ID</param>
		static bool GetProcessPathViaWMI(int processId, out string path)
		{
			path = string.Empty;

			if (!Taskmaster.WMIQueries) return false;

			var wmitime = Stopwatch.StartNew();

			Statistics.WMIqueries++;

			try
			{
				using (var searcher = new ManagementObjectSearcher(
					new ManagementScope(@"\\.\root\CIMV2"),
					new SelectQuery("SELECT ExecutablePath FROM Win32_Process WHERE ProcessId = " + processId),
					new EnumerationOptions(null, new TimeSpan(0, 1, 0), 1, false, true, false, false, false, true, false)
					))
				{
					foreach (ManagementObject item in searcher.Get())
					{
						using (item)
						{
							var mpath = item["ExecutablePath"];
							if (mpath != null)
							{
								Log.Verbose("WMI fetch (#" + processId + "): " + path);
								wmitime.Stop();
								Statistics.WMIquerytime += wmitime.Elapsed.TotalSeconds;
								path = mpath.ToString();
								return path.Length > 1;
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				// NOP, don't caree
			}
			finally
			{
				wmitime.Stop();
				Statistics.WMIquerytime += wmitime.Elapsed.TotalSeconds;
			}

			return false;
		}

		public static int ApplyAffinityStrategy(int source, int target, ProcessAffinityStrategy strategy)
		{
			int newAffinityMask = target;
			// Don't increase the number of cores
			if (strategy == ProcessAffinityStrategy.Limit)
			{
				int excesscores = Bit.Count(target) - Bit.Count(source);
				if (excesscores > 0)
				{
					if (Taskmaster.DebugProcesses) Debug.WriteLine("Old Affinity Mask: " + Convert.ToString(newAffinityMask, 2));
					for (int i = 0; i < ProcessManager.CPUCount; i++)
					{
						if (Bit.IsSet(newAffinityMask, i))
						{
							newAffinityMask = Bit.Unset(newAffinityMask, i);
							if (Taskmaster.DebugProcesses) Debug.WriteLine("Int Affinity Mask: " + Convert.ToString(newAffinityMask, 2));
							if (--excesscores <= 0) break;
						}
					}
					if (Taskmaster.DebugProcesses) Debug.WriteLine("New Affinity Mask: " + Convert.ToString(newAffinityMask, 2));
				}
			}
			else if (strategy == ProcessAffinityStrategy.Scatter)
			{
				// NOT IMPLEMENTED
				/*
				for (; ScatterOffset < ProcessManager.CPUCount; ScatterOffset++)
				{
					if (Bit.IsSet(newAffinityMask, ScatterOffset))
					{

					}
				}
				*/
			}

			return newAffinityMask;
		}
	}

	public enum PathVisibilityOptions
	{
		/// <summary>
		/// Process name. Usually executable name without extension.
		/// </summary>
		Process = -1,
		/// <summary>
		/// Filename with extension.
		/// </summary>
		File = 0,
		/// <summary>
		/// .../Folder/executable.ext
		/// </summary>
		Folder = 1,
		/// <summary>
		/// Smart reduction of full path. Not always as smart as desirable.
		/// </summary>
		Smart = 2,
		/// <summary>
		/// Complete path.
		/// </summary>
		Full = 3,
		/// <summary>
		/// Partial path removes some basic elements that seem redundant.
		/// </summary>
		Partial = 4,
		/// <summary>
		/// Invalid.
		/// </summary>
		Invalid = -2,
	}
}