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
		/// <summary>
		/// Throws: InvalidOperationException, ArgumentException
		/// </summary>
		/// <param name="target">0 = Background, 1 = Low, 2 = Normal, 3 = Elevated, 4 = High</param>
		public static int SetIO(Process process, int target, out int newIO, bool decrease=true)
		{
			int handle = 0;
			int original = -1;
			Debug.Assert(target >= 0 && target <= 4, "I/O target set to undefined value: " + target);

			target = target.Constrain(0, 4); // ensure no invalid data is used.

			try
			{
				if ((handle = NativeMethods.OpenProcessFully(process)) != 0)
				{
					original = NativeMethods.GetIOPriority(handle);

					if (original < 0)
						newIO = -1;
					else if (!decrease && target < original)
						newIO = -1;
					else if (original != target)
					{
						if (NativeMethods.SetIOPriority(handle, target))
						{
							newIO = NativeMethods.GetIOPriority(handle);
							if (newIO != target)
								Debug.WriteLine($"{process.ProcessName} (#{process.Id}) - I/O not set correctly: {newIO} instead of {target}");
						}
						else
							throw new InvalidOperationException("Failed to modify process I/O priority");
					}
					else
						newIO = target;
				}
				else
					throw new ArgumentException("Failed to open process");
			}
			finally
			{
				if (handle != 0)
					NativeMethods.CloseHandle(handle);
			}

			return original;
		}


		internal enum PriorityTypes
		{
			ABOVE_NORMAL_PRIORITY_CLASS = 0x00008000,
			BELOW_NORMAL_PRIORITY_CLASS = 0x00004000,
			HIGH_PRIORITY_CLASS = 0x00000080,
			IDLE_PRIORITY_CLASS = 0x00000040,
			NORMAL_PRIORITY_CLASS = 0x00000020,
			PROCESS_MODE_BACKGROUND_BEGIN = 0x00100000,
			PROCESS_MODE_BACKGROUND_END = 0x00200000,
			REALTIME_PRIORITY_CLASS = 0x00000100
		}

		// Windows doesn't allow setting this for other processes
		public static bool SafeSetBackground(Process process)
		{
			return SafeSetIOPriority(process, PriorityTypes.PROCESS_MODE_BACKGROUND_BEGIN);
		}

		public static bool UnsetBackground(Process process)
		{
			return SafeSetIOPriority(process, PriorityTypes.PROCESS_MODE_BACKGROUND_END);
		}

		/// <summary>
		/// Set disk I/O priority. Works only for setting own process priority.
		/// Would require invasive injecting to other process to affect them.
		/// </summary>
		/// <exception>None</exception>
		internal static bool SafeSetIOPriority(Process process, PriorityTypes priority)
		{
			try
			{
				var rv = NativeMethods.SetPriorityClass(process.Handle, (uint)priority);
				return rv;
			}
			catch (InvalidOperationException) { } // Already exited
			catch (ArgumentException) { } // already exited?
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex) { Logging.Stacktrace(ex); }

			return false;
		}

		static Cache<int, string, string> pathCache = null;
		public static void InitializeCache()
		{
			// this is really dumb
			pathCache = new Cache<int, string, string>(Taskmaster.PathCacheMaxAge, (uint)Taskmaster.PathCacheLimit, (uint)(Taskmaster.PathCacheLimit / 10).Constrain(20, 60));
		}

		public static bool GetInfo(int ProcessID, out ProcessEx info, Process process = null, ProcessController controller = null, string name = null, string path = null, bool getPath = false)
		{
			try
			{
				if (process == null) process = Process.GetProcessById(ProcessID);

				info = new ProcessEx()
				{
					Id = ProcessID,
					Process = process,
					Controller = controller,
					Name = string.IsNullOrEmpty(name) ? process.ProcessName : name,
					State = ProcessHandlingState.Triage,
					Path = path,
				};

				if (getPath && string.IsNullOrEmpty(path)) FindPath(info);

				return true;
			}
			catch (InvalidOperationException) { } // already exited
			catch (ArgumentException) { } // already exited
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}

			info = null;
			return false;
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
