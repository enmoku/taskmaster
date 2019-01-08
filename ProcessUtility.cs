//
// ProcessUtility.cs
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
using Serilog;

namespace Taskmaster
{
	public static class ProcessUtility
	{
		static Cache<int, string, string> pathCache = null;
		public static void InitializeCache()
		{
			// this is really dumb
			pathCache = new Cache<int, string, string>(Taskmaster.PathCacheMaxAge, (uint)Taskmaster.PathCacheLimit, (uint)(Taskmaster.PathCacheLimit / 10).Constrain(20, 60));
		}

		public static ProcessEx GetInfo(int ProcessID, Process process = null, string name = null, string path = null, bool getPath = false)
		{
			try
			{
				if (process == null) process = Process.GetProcessById(ProcessID);

				var info = new ProcessEx()
				{
					Id = ProcessID,
					Process = process,
					Name = string.IsNullOrEmpty(name) ? process.ProcessName : name,
					State = ProcessModification.OK,
					Path = path,
				};

				if (getPath && string.IsNullOrEmpty(path)) FindPath(info);

				return info;
			}
			catch (InvalidOperationException) { } // already exited
			catch (ArgumentException) { } // already exited
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}

			return null;
		}

		public static bool FindPath(ProcessEx info)
		{
			var cacheGet = false;

			// Try to get the path from cache
			if (pathCache.Get(info.Id, out string cpath, info.Name) != null)
			{
				if (!string.IsNullOrEmpty(cpath))
				{
					Statistics.PathCacheHits++;
					cacheGet = true;
					info.Path = cpath;
				}
				else
				{
					pathCache.Drop(info.Id);
				}
			}

			// Try harder
			if (string.IsNullOrEmpty(info.Path) && !ProcessManagerUtility.FindPathExtended(info)) return false;

			// Add to path cache
			if (!cacheGet)
			{
				pathCache.Add(info.Id, info.Name, info.Path);
				Statistics.PathCacheMisses++; // adding new entry is as bad a miss

				if (Statistics.PathCacheCurrent > Statistics.PathCachePeak) Statistics.PathCachePeak = Statistics.PathCacheCurrent;
			}

			Statistics.PathCacheCurrent = pathCache.Count;

			return true;
		}

		[Conditional("DEBUG")]
		public static void PathCacheStats()
		{
			Log.Debug("Path cache state: " + Statistics.PathCacheCurrent + " items (Hits: " + Statistics.PathCacheHits +
				", Misses: " + Statistics.PathCacheMisses +
				", Ratio: " + $"{(Statistics.PathCacheMisses > 0 ? (Statistics.PathCacheHits / Statistics.PathCacheMisses) : 1):N2})");
		}
	}
}
