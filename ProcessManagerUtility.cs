//
// ProcessManagerUtility.cs
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
using System.Diagnostics;
using Serilog;

namespace Taskmaster
{
	public static class ProcessManagerUtility
	{
		static Cache<int, string, string> pathCache;

		public static void Initialize()
		{
			pathCache = new Cache<int, string, string>(Taskmaster.PathCacheMaxAge, Taskmaster.PathCacheLimit, (Taskmaster.PathCacheLimit / 10).Constrain(5, 10));
		}

		public static void PathCacheStats()
		{
			Log.Debug("Path cache state: {Count} items (Hits: {Hits}, Misses: {Misses}, Ratio: {Ratio})",
					  Statistics.PathCacheCurrent, Statistics.PathCacheHits, Statistics.PathCacheMisses,
					  string.Format("{0:N2}", Statistics.PathCacheMisses > 0 ? (Statistics.PathCacheHits / Statistics.PathCacheMisses) : 1));
		}

		public static bool FindPath(ProcessEx info)
		{
			var cacheGet = false;

			// Try to get the path from cache
			if (pathCache != null)
			{
				if (pathCache.Get(info.Id, out string cpath, info.Name) != null)
				{
					if (!string.IsNullOrEmpty(cpath))
					{
						Statistics.PathCacheHits++;
						cacheGet = true;
						info.Path = cpath;
						// Log.Debug("PATH CACHE ITEM GET: {Path}", info.Path);
					}
					else
					{
						// Statistics.PathCacheMisses++; // will be done when adding the entry
						pathCache.Drop(info.Id);
						// Log.Debug("PATH CACHE ITEM BEGONE!");
					}

					Statistics.PathCacheCurrent = pathCache.Count;
				}
			}

			// Try harder
			if (string.IsNullOrEmpty(info.Path))
			{
				GetPath(info);

				if (string.IsNullOrEmpty(info.Path)) return false;
			}

			if (pathCache != null && !cacheGet)
			{
				pathCache.Add(info.Id, info.Name, info.Path);
				Statistics.PathCacheMisses++; // adding new entry is as bad a miss

				Statistics.PathCacheCurrent = pathCache.Count;
				if (Statistics.PathCacheCurrent > Statistics.PathCachePeak)
					Statistics.PathCachePeak = Statistics.PathCacheCurrent;
				// Log.Debug("PATH CACHE ADD: {Path}", info.Path);
			}

			return true;
		}

		static bool GetPath(ProcessEx info)
		{
			System.Diagnostics.Debug.Assert(string.IsNullOrEmpty(info.Path), "GetPath called even though path known.");

			try
			{
				info.Path = info.Process.MainModule?.FileName; // this will cause win32exception of various types, we don't Really care which error it is
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				// NOP, don't care 
			}

			if (string.IsNullOrEmpty(info.Path))
			{
				info.Path = GetProcessPathViaC(info.Id);

				if (string.IsNullOrEmpty(info.Path))
				{
					info.Path = GetProcessPathViaWMI(info.Id);
					if (string.IsNullOrEmpty(info.Path)) return false;
				}
			}

			return true;
		}

		// https://stackoverflow.com/a/34991822
		public static string GetProcessPathViaC(int pid)
		{
			const int PROCESS_QUERY_INFORMATION = 0x0400;
			// const int PROCESS_VM_READ = 0x0010; // is this really needed?

			var processHandle = NativeMethods.OpenProcess(PROCESS_QUERY_INFORMATION, false, pid);

			if (processHandle == IntPtr.Zero) return null;

			const int lengthSb = 4000;

			var sb = new System.Text.StringBuilder(lengthSb);

			string result = null;

			if (NativeMethods.GetModuleFileNameEx(processHandle, IntPtr.Zero, sb, lengthSb) > 0)
			{
				// result = Path.GetFileName(sb.ToString());
				result = sb.ToString();
			}

			NativeMethods.CloseHandle(processHandle);

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
			if (!Taskmaster.WMIQueries) return null;

			var wmitime = Stopwatch.StartNew();

			Statistics.WMIqueries++;

			string path = null;
			var wmiQueryString = "SELECT ExecutablePath FROM Win32_Process WHERE ProcessId = " + processId;
			try
			{
				using (var searcher = new System.Management.ManagementObjectSearcher(wmiQueryString))
				{
					foreach (System.Management.ManagementObject item in searcher.Get())
					{
						var mpath = item["ExecutablePath"];
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
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				// NOP, don't caree
			}

			wmitime.Stop();
			Statistics.WMIquerytime += wmitime.Elapsed.TotalSeconds;

			return path;
		}
	}
}