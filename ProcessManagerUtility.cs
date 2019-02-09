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
using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Text;
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

			string path = string.Empty;
			try
			{
				path = info.Process?.MainModule?.FileName ?? string.Empty; // this will cause win32exception of various types, we don't Really care which error it is
			}
			catch (InvalidOperationException)
			{
				// already gone
				return false;
			}
			catch (Win32Exception)
			{
				// Access denied problems of varying sorts
			}
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
				Debug.WriteLine("GetModuleFileNameEx - Access Denied - " + $"{info.Name} (#{info.Id})");
			}
			catch (InvalidOperationException)
			{
				// Already exited
			}
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
			if (Taskmaster.DebugProcesses)
			{
				sbs = new StringBuilder();
				sbs.Append("Affinity Strategy(").Append(Convert.ToString(source, 2)).Append(", ").Append(strategy.ToString()).Append(")");
			}

			// Don't increase the number of cores
			if (strategy == ProcessAffinityStrategy.Limit)
			{
				if (sbs != null) sbs.Append(" Cores(").Append(Bit.Count(source)).Append("->").Append(Bit.Count(target)).Append(")");

				int excesscores = Bit.Count(target) - Bit.Count(source);
				if (excesscores > 0)
				{
					for (int i = 0; i < ProcessManager.CPUCount; i++)
					{
						if (Bit.IsSet(newAffinityMask, i))
						{
							newAffinityMask = Bit.Unset(newAffinityMask, i);
							if (sbs != null) sbs.Append(" -> ").Append(Convert.ToString(newAffinityMask, 2));
							if (--excesscores <= 0) break;
						}
					}
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

			if (sbs != null)
			{
				sbs.Append(" = ").Append(Convert.ToString(newAffinityMask, 2));
				Debug.WriteLine(sbs.ToString());
			}

			return newAffinityMask;
		}
	}

	public enum PathVisibilityOptions : int
	{
		/// <summary>
		/// Process name. Usually executable name without extension.
		/// </summary>
		Process = -1,
		/// <summary>
		/// Partial path removes some basic elements that seem redundant.
		/// </summary>
		Partial = 1,
		/// <summary>
		/// Smart reduction of full path. Not always as smart as desirable.
		/// </summary>
		Smart = 2,
		/// <summary>
		/// Complete path.
		/// </summary>
		Full = 3,
		/// <summary>
		/// Invalid.
		/// </summary>
		Invalid = 0,
	}
}