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
using MKAh.Logic;
using Serilog;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Taskmaster.Process
{
	using static Application;

	public static partial class Utility
	{
		public static int CPUCount => Environment.ProcessorCount; // pointless

		public static int FullCPUMask => (1 << CPUCount) - 1;

		public static bool IsFullscreen(IntPtr hwnd)
		{
			// TODO: Is it possible to cache screen? multimonitor setup may make it hard... would that save anything?
			var screen = System.Windows.Forms.Screen.FromHandle(hwnd); // passes

			var WindowRectangle = new System.Drawing.Rectangle();
			var ScreenRectangle = new NativeMethods.Rectangle();

			NativeMethods.GetWindowRect(hwnd, ref ScreenRectangle);
			//var windowrect = new System.Drawing.Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
			WindowRectangle.Height = ScreenRectangle.Bottom - ScreenRectangle.Top;
			WindowRectangle.Width = ScreenRectangle.Right - ScreenRectangle.Left;
			WindowRectangle.X = ScreenRectangle.Left;
			WindowRectangle.Y = ScreenRectangle.Top;

			return WindowRectangle.Equals(screen.Bounds);
		}

		/// <summary>
		/// Tests if the process ID is core system process (0[idle] or 4[system]) that can never be valid program.
		/// </summary>
		/// <returns>true if the pid should not be used</returns>
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		public static bool SystemProcessId(int pid) => pid <= 4;

		internal sealed class PathCacheObject
		{
			internal string Name;
			internal string Path;

			internal PathCacheObject(string name, string path)
			{
				Name = name;
				Path = path;
			}
		}

		static MKAh.Cache.SimpleCache<int, PathCacheObject> PathCache;

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

				PathCache.Add(info.Id, new PathCacheObject(info.Name, info.Path));

				if (Statistics.PathCacheCurrent > Statistics.PathCachePeak) Statistics.PathCachePeak = Statistics.PathCacheCurrent;
				Statistics.PathCacheCurrent = PathCache.Count;
			}
			else
				info.Path = cob.Path;

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

			if (info.Restricted)
			{
				if (Manager.DebugProcesses) Logging.DebugMsg($"<Process> {info} RESTRICTED - cancelling FindPathExtended");
				return false;
			}

			string path;

			if (!GetPathViaC(info, out path))
			{
				if (GetPathViaModule(info, out path))
					Statistics.PathFindViaModule++;
				else
				{
					Statistics.PathNotFound++;
					return false;
				}
			}
			else
				Statistics.PathFindViaC++;

			info.Path = path;
			return true;
		}

		public static bool GetPathViaModule(ProcessEx info, out string path)
		{
			try
			{
				// this will cause win32exception of various types, we don't Really care which error it is
				path = info.Process?.MainModule?.FileName ?? string.Empty;
				return true;
			}
			catch (NullReferenceException) // .filename sometimes throws this even when mainmodule is not null
			{
				// ignore?
			}
			catch (InvalidOperationException)
			{
				// already gone
			}
			catch (Win32Exception) // Access denied problems of varying sorts
			{
				info.Restricted = true;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				// NOP, don't care
			}

			path = string.Empty;
			return false;
		}

		// Initial version gained from: https://stackoverflow.com/a/34991822
		public static bool GetPathViaC(ProcessEx info, out string path)
		{
			Taskmaster.NativeMethods.HANDLE handle = null;

			try
			{
				handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_INFORMATION | NativeMethods.PROCESS_ACCESS_RIGHTS.PROCESS_VM_READ, false, info.Id);
				if (handle is null) // failed to open process
				{
					path = string.Empty;
					return false;
				}

				const int lengthSb = 32768 + 256; // this is the maximum path length NTFS supports. 256 is arbitrary space for expansion of \\?\

				var sb = new System.Text.StringBuilder(lengthSb);

				if (NativeMethods.GetModuleFileNameEx(info.Process.Handle, IntPtr.Zero, sb, lengthSb) > 0)
				{
					path = sb.ToString();
					return true;
				}
			}
			catch (Win32Exception) // Access Denied
			{
				info.Restricted = true;

				if (Process.Manager.DebugProcesses)
					Logging.DebugMsg("GetModuleFileNameEx - Access Denied - " + info.ToString());
			}
			catch (InvalidOperationException) { /* already exited */ }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				handle?.Close();
			}

			path = string.Empty;
			return false;
		}

		public static int ApplyAffinityStrategy(int initialmask, int targetmask, AffinityStrategy strategy)
		{
			StringBuilder sbs = null;

			Debug.Assert((initialmask & FullCPUMask) == initialmask, "Initial value has bits set outside of valid range.");
			Debug.Assert((targetmask & FullCPUMask) == targetmask, "Target mask has bits set outside of valid range.");

			if (Process.Manager.DebugProcesses)
			{
				sbs = new StringBuilder("Affinity Strategy(", 256)
					.Append(Convert.ToString(initialmask, 2).PadLeft(CPUCount, '0')).Append(" -> ")
					.Append(Convert.ToString(targetmask, 2).PadLeft(CPUCount, '0'))
					.Append(", ").Append(strategy.ToString()).Append(')');
			}

			int result;

			if (strategy == AffinityStrategy.Force)
			{
				result = targetmask;
			}
			else if (strategy == AffinityStrategy.Limit) // Don't increase the number of cores but move them around
			{
				int initialCores = Bit.Count(initialmask);
				int targetCores = Bit.Count(targetmask);
				int excesscores = initialCores - targetCores;
				int deficitcores = targetCores - initialCores;
				int availablecores = targetCores & ~initialmask;

				result = initialmask;

				sbs?.Append(" Cores(").Append(Bit.Count(initialmask)).Append(" / ").Append(Bit.Count(targetmask))
					.Append(") old mask ").Append(Convert.ToString(initialmask, 2).PadLeft(CPUCount, '0'));

				if (excesscores > 0)
				{
					result = Bit.Unfill(result, targetmask, excesscores);
					sbs?.Append("; excess: ").Append(excesscores.ToString())
						.Append(" pruned to: ").Append(Convert.ToString(result, 2).PadLeft(CPUCount, '0'));
				}

				bool correctlySlotted = Bit.And(result, targetmask) == result;
				int incorrectMask = result & ~targetmask;
				int incorrectCount = Bit.Count(incorrectMask);

				sbs?.Append("; misplaced: ").Append(!correctlySlotted);

				if (incorrectCount > 0)
				{
					result = Bit.Move(result, targetmask);

					//result = Bit.Unfill(result, targetmask, excesscores);
					//int curCores = Bit.Count(result);

					//if (curCores < targetCores)
					//	result = Bit.Fill(result, targetmask, excesscores);

					sbs?.Append(" ×").Append(incorrectCount.ToString())
						.Append("; corrected to ").Append(Convert.ToString(result, 2).PadLeft(CPUCount, '0'));
				}
			}
			else if (strategy == AffinityStrategy.Scatter)
			{
				result = targetmask;

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
			else
			{
				result = targetmask;
			}

			if (sbs != null)
			{
				sbs.Append("; new = ").Append(Convert.ToString(result, 2).PadLeft(CPUCount, '0'));
				Logging.DebugMsg(sbs.ToString());
			}

			return result;
		}

		/// <summary>
		/// Throws: InvalidOperationException, ArgumentException
		/// </summary>
		/// <param name="target">0 = Background, 1 = Low, 2 = Normal, 3 = Elevated, 4 = High</param>
		public static int SetIO(System.Diagnostics.Process process, int target, out int newIO, bool decrease = true)
		{
			Taskmaster.NativeMethods.HANDLE handle = null;
			int original = -1;
			Debug.Assert(target >= 0 && target <= 2, "I/O target set to undefined value: " + target.ToString());

			target = target.Constrain(0, 2); // ensure no invalid data is used.

			try
			{
				if (!((handle = NativeMethods.OpenProcessFully(process)) is null))
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
								Logging.DebugMsg($"{process.ProcessName} #{process.Id} - I/O not set correctly: {newIO} instead of {target}");
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
				handle?.Close();
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
			catch (InvalidOperationException) { /* already exited */ }
			catch (ArgumentException) { /* already exited? */ }
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex) { Logging.Stacktrace(ex); }

			return false;
		}

		public static bool GetInfo(int ProcessID, out ProcessEx info, System.Diagnostics.Process? process = null, Process.Controller? controller = null, string name = null, string path = null, bool getPath = false)
		{
			try
			{
				if (process is null) process = System.Diagnostics.Process.GetProcessById(ProcessID);

				info = new ProcessEx(ProcessID, DateTimeOffset.UtcNow)
				{
					Process = process,
					Controller = controller,
					Name = string.IsNullOrEmpty(name) ? process.ProcessName : name,
					State = HandlingState.Triage,
					Path = path
				};

				if (getPath && string.IsNullOrEmpty(path)) FindPath(info);

				return true;
			}
			catch (InvalidOperationException) { /* already exited */ }
			catch (ArgumentException) { /* already exited */ }
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
				if (GetInfo(info.Process.ParentProcessId(), out var parent, null, null, null, null, true))
					return parent;
			}
			catch { /* don't care */ }

			return null;
		}

		[Conditional("DEBUG")]
		public static void PathCacheStats()
			=> Log.Debug("Path cache state: " + Statistics.PathCacheCurrent.ToString() + " items (Hits: " + Statistics.PathCacheHits.ToString() +
				", Misses: " + Statistics.PathCacheMisses.ToString() +
				", Ratio: " + $"{(Statistics.PathCacheMisses > 0 ? (Statistics.PathCacheHits / Statistics.PathCacheMisses) : 1):N2})");
	}
}