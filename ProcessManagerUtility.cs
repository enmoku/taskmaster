//
// ProcessManagerUtility.cs
//
// Author:
//       M.A. (enmoku) <>
//
// Copyright (c) 2018 M.A. (enmoku)
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
using System.Runtime.InteropServices;
using Serilog;

namespace TaskMaster
{
	public static class ProcessManagerUtility
	{
		static Cache<int, string, string> pathCache;

		public static void Initialize()
		{
			pathCache = new Cache<int, string, string>(TaskMaster.PathCacheMaxAge, TaskMaster.PathCacheLimit, (TaskMaster.PathCacheLimit / 10).Constrain(5, 10));
		}

		public static void PathCacheStats()
		{
			Log.Debug("Path cache state: {Count} items (Hits: {Hits}, Misses: {Misses}, Ratio: {Ratio})",
					  Statistics.PathCacheCurrent, Statistics.PathCacheHits, Statistics.PathCacheMisses,
					  string.Format("{0:N2}", Statistics.PathCacheMisses > 0 ? (Statistics.PathCacheHits / Statistics.PathCacheMisses) : 1));
		}

		public static bool FindPath(BasicProcessInfo info)
		{
			bool cacheGet = false;

			// Try to get the path from cache
			if (pathCache != null)
			{
				string cpath;
				if (pathCache.Get(info.Id, out cpath, info.Name) != null)
				{
					if (!string.IsNullOrEmpty(cpath))
					{
						Statistics.PathCacheHits++;
						cacheGet = true;
						info.Path = cpath;
						//Log.Debug("PATH CACHE ITEM GET: {Path}", info.Path);
					}
					else
					{
						//Statistics.PathCacheMisses++; // will be done when adding the entry
						pathCache.Drop(info.Id);
						//Log.Debug("PATH CACHE ITEM BEGONE!");
					}

					Statistics.PathCacheCurrent = pathCache.Count;
				}
			}

			// Try harder
			if (info.Path == null)
			{
				GetPath(info);

				if (info.Path == null)
					return false;
			}

			if (pathCache != null && !cacheGet)
			{
				pathCache.Add(info.Id, info.Name, info.Path);
				Statistics.PathCacheMisses++; // adding new entry is as bad a miss


				Statistics.PathCacheCurrent = pathCache.Count;
				if (Statistics.PathCacheCurrent > Statistics.PathCachePeak)
					Statistics.PathCachePeak = Statistics.PathCacheCurrent;
				//Log.Debug("PATH CACHE ADD: {Path}", info.Path);
			}

			return true;
		}

		static bool GetPath(BasicProcessInfo info)
		{
			System.Diagnostics.Debug.Assert(info.Path == null, "GetPath called even though path known.");

			try
			{
				info.Path = info.Process.MainModule?.FileName; // this will cause win32exception of various types, we don't Really care which error it is
			}
			catch
			{
				// NOP, don't care 
			}

			if (string.IsNullOrEmpty(info.Path))
			{
				info.Path = GetProcessPathViaC(info.Id);

				if (info.Path == null)
				{
					info.Path = GetProcessPathViaWMI(info.Id);
					if (string.IsNullOrEmpty(info.Path))
						return false;
				}
			}

			return true;
		}

		// https://stackoverflow.com/a/34991822
		public static string GetProcessPathViaC(int pid)
		{
			var processHandle = OpenProcess(0x0400 | 0x0010, false, pid);

			if (processHandle == IntPtr.Zero)
			{
				return null;
			}

			const int lengthSb = 4000;

			var sb = new System.Text.StringBuilder(lengthSb);

			string result = null;

			if (GetModuleFileNameEx(processHandle, IntPtr.Zero, sb, lengthSb) > 0)
			{
				//result = Path.GetFileName(sb.ToString());
				result = sb.ToString();
			}

			CloseHandle(processHandle);

			return result;
		}

		/// <summary>
		/// Retrieve file path for the process.
		/// Slow due to use of WMI.
		/// </summary>
		/// <returns>The process path.</returns>
		/// <param name="processId">Process ID</param>
		static string GetProcessPathViaWMI(int processId)
		{
			if (!TaskMaster.WMIQueries) return null;

			var wmitime = Stopwatch.StartNew();

			Statistics.WMIqueries++;

			string path = null;
			string wmiQueryString = "SELECT ExecutablePath FROM Win32_Process WHERE ProcessId = " + processId;
			try
			{
				using (var searcher = new System.Management.ManagementObjectSearcher(wmiQueryString))
				{
					foreach (System.Management.ManagementObject item in searcher.Get())
					{
						object mpath = item["ExecutablePath"];
						if (mpath != null)
						{
							Log.Verbose(string.Format("WMI fetch (#{0}): {1}", processId, path));
							wmitime.Stop();
							Statistics.WMIquerytime += wmitime.Elapsed.TotalSeconds;
							return mpath.ToString();
						}
					}
				}
			}
			catch
			{
				// NOP, don't caree
			}

			wmitime.Stop();
			Statistics.WMIquerytime += wmitime.Elapsed.TotalSeconds;

			return path;
		}

		[DllImport("kernel32.dll")]
		public static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

		[DllImport("psapi.dll")]
		static extern uint GetModuleFileNameEx(IntPtr hProcess, IntPtr hModule, [Out] System.Text.StringBuilder lpBaseName, [In] [MarshalAs(UnmanagedType.U4)] int nSize);

		[DllImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		static extern bool CloseHandle(IntPtr hObject);
	}
}
