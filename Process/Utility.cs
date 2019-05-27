//
// Process.Utility.cs
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

using MKAh;
using System;
using System.Diagnostics;
using Serilog;
using System.ComponentModel;
using System.Text;
using MKAh.Logic;

namespace Taskmaster.Process
{
	using static Taskmaster;

	public static class Utility
	{
		public static int CPUCount => Environment.ProcessorCount; // pointless
		public static int FullCPUMask => (1 << CPUCount) - 1;

		/// <summary>
		/// Tests if the process ID is core system process (0[idle] or 4[system]) that can never be valid program.
		/// </summary>
		/// <returns>true if the pid should not be used</returns>
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		public static bool SystemProcessId(int pid) => pid <= 4;

		internal class PathCacheObject
		{
			internal string Name;
			internal string Path;
		}

		static MKAh.Cache.SimpleCache<int, PathCacheObject> PathCache = null;
		internal static void InitializeCache()
			=> PathCache = new MKAh.Cache.SimpleCache<int, PathCacheObject>((uint)PathCacheLimit, (uint)(PathCacheLimit / 10).Constrain(10, 100), PathCacheMaxAge);

		public static bool FindPath(ProcessEx info)
		{
			if (info.PathSearched)
			{
				Statistics.PathSearchMisfire++;
				return !string.IsNullOrEmpty(info.Path);
			}

			info.PathSearched = true; // mark early

			if (PathCache.Get(info.Id, out var cob))
			{
				if (!cob.Name.Equals(info.Name, StringComparison.InvariantCultureIgnoreCase))
				{
					PathCache.Drop(info.Id);
					cob = null;
				}
				else
					Statistics.PathCacheHits++;
			}

			if (cob is null)
			{
				Statistics.PathCacheMisses++;

				if (!FindPathExtended(info)) return false;

				PathCache.Add(info.Id, new PathCacheObject() { Name = info.Name, Path = info.Path });

				if (Statistics.PathCacheCurrent > Statistics.PathCachePeak) Statistics.PathCachePeak = Statistics.PathCacheCurrent;
				Statistics.PathCacheCurrent = PathCache.Count;
			}
			else
			{
				info.Path = cob.Path;
			}

			return true;
		}

		/// <summary>
		/// Use FindPath() instead. This is called by it.
		/// </summary>
		public static bool FindPathExtended(ProcessEx info)
		{
			Debug.Assert(!(info is null), "FindPathExtended received null");
			Debug.Assert(string.IsNullOrEmpty(info.Path), "FindPathExtended called even though path known.");

			Statistics.PathFindAttempts++;

			string path = string.Empty;
			try
			{
				// this will cause win32exception of various types, we don't Really care which error it is
				path = info.Process?.MainModule?.FileName ?? string.Empty;
			}
			catch (NullReferenceException) // .filename sometimes throws this even when mainmodule is not null
			{
				// ignore?
			}
			catch (InvalidOperationException)
			{
				// already gone
				return false;
			}
			catch (Win32Exception) { } // Access denied problems of varying sorts
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				// NOP, don't care
			}

			if (string.IsNullOrEmpty(path))
			{
				if (!GetPathViaC(info, out path))
				{
					Statistics.PathNotFound++;
					return false;
				}
				else
					Statistics.PathFindViaC++;
			}
			else
				Statistics.PathFindViaModule++;

			info.Path = path;

			return true;
		}

		// https://stackoverflow.com/a/34991822
		public static bool GetPathViaC(ProcessEx info, out string path)
		{
			path = string.Empty;
			int handle = 0;

			try
			{
				handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_RIGHTS.PROCESS_QUERY_INFORMATION | NativeMethods.PROCESS_RIGHTS.PROCESS_VM_READ, false, info.Id);
				if (handle == 0) return false; // failed to open process

				const int lengthSb = 32768; // this is the maximum path length NTFS supports

				var sb = new System.Text.StringBuilder(lengthSb);

				if (NativeMethods.GetModuleFileNameEx(info.Process.Handle, IntPtr.Zero, sb, lengthSb) > 0)
				{
					// result = Path.GetFileName(sb.ToString());
					path = sb.ToString();
					return true;
				}
			}
			catch (Win32Exception) // Access Denied
			{
				// NOP
				Logging.DebugMsg("GetModuleFileNameEx - Access Denied - " + $"{info.Name} (#{info.Id})");
			}
			catch (InvalidOperationException) { }// Already exited
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				NativeMethods.CloseHandle(handle);
			}

			return false;
		}

		public static int ApplyAffinityStrategy(int source, int target, ProcessAffinityStrategy strategy)
		{
			int newAffinityMask = target;
			StringBuilder sbs = null;

			if (Process.Manager.DebugProcesses)
			{
				sbs = new StringBuilder()
					.Append("Affinity Strategy(").Append(Convert.ToString(source, 2)).Append(", ").Append(strategy.ToString()).Append(")");
			}

			// Don't increase the number of cores
			if (strategy == ProcessAffinityStrategy.Limit)
			{
				sbs?.Append(" Cores(").Append(Bit.Count(source)).Append("->").Append(Bit.Count(target)).Append(")");

				int excesscores = Bit.Count(target) - Bit.Count(source);
				if (excesscores > 0)
				{
					for (int i = 0; i < Process.Utility.CPUCount; i++)
					{
						if (Bit.IsSet(newAffinityMask, i))
						{
							newAffinityMask = Bit.Unset(newAffinityMask, i);
							sbs?.Append(" -> ").Append(Convert.ToString(newAffinityMask, 2));
							if (--excesscores <= 0) break;
						}
					}
				}
			}
			else if (strategy == ProcessAffinityStrategy.Scatter)
			{
				throw new NotImplementedException("Affinitry scatter strategy not implemented.");

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

			if (sbs != null)
			{
				sbs.Append(" = ").Append(Convert.ToString(newAffinityMask, 2));
				Logging.DebugMsg(sbs.ToString());
			}

			return newAffinityMask;
		}

		/// <summary>
		/// Throws: InvalidOperationException, ArgumentException
		/// </summary>
		/// <param name="target">0 = Background, 1 = Low, 2 = Normal, 3 = Elevated, 4 = High</param>
		public static int SetIO(System.Diagnostics.Process process, int target, out int newIO, bool decrease = true)
		{
			int handle = 0;
			int original = -1;
			Debug.Assert(target >= 0 && target <= 2, "I/O target set to undefined value: " + target);

			target = target.Constrain(0, 2); // ensure no invalid data is used.

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
								Logging.DebugMsg($"{process.ProcessName} (#{process.Id}) - I/O not set correctly: {newIO} instead of {target}");
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
		public static bool SetBackground(System.Diagnostics.Process process)
			=> SafeSetIOPriority(process, PriorityTypes.PROCESS_MODE_BACKGROUND_BEGIN);

		public static bool UnsetBackground(System.Diagnostics.Process process)
			=> SafeSetIOPriority(process, PriorityTypes.PROCESS_MODE_BACKGROUND_END);

		/// <summary>
		/// Set disk I/O priority. Works only for setting own process priority.
		/// Would require invasive injecting to other process to affect them.
		/// </summary>
		/// <exception>None</exception>
		internal static bool SafeSetIOPriority(System.Diagnostics.Process process, PriorityTypes priority)
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

		public static bool GetInfo(int ProcessID, out ProcessEx info, System.Diagnostics.Process process = null, Process.Controller controller = null, string name = null, string path = null, bool getPath = false)
		{
			try
			{
				if (process is null) process = System.Diagnostics.Process.GetProcessById(ProcessID);

				info = new ProcessEx()
				{
					Id = ProcessID,
					Process = process,
					Controller = controller,
					Name = string.IsNullOrEmpty(name) ? process.ProcessName : name,
					State = ProcessHandlingState.Triage,
					Path = path
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

		public static ProcessEx GetParentProcess(ProcessEx info)
		{
			try
			{
				int ppid = info.Process.ParentProcessId();
				if (GetInfo(ppid, out var parent, null, null, null, null, true))
					return parent;
			}
			catch
			{

			}

			return null;
		}

		[Conditional("DEBUG")]
		public static void PathCacheStats()
			=> Log.Debug("Path cache state: " + Statistics.PathCacheCurrent + " items (Hits: " + Statistics.PathCacheHits +
				", Misses: " + Statistics.PathCacheMisses +
				", Ratio: " + $"{(Statistics.PathCacheMisses > 0 ? (Statistics.PathCacheHits / Statistics.PathCacheMisses) : 1):N2})");
	}
}