//
// MemoryManager.cs
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

namespace TaskMaster
{
	class MemoryController
	{
		readonly Process process;
		readonly MemoryPriority priority;

		public static void EnforceProcess(Process prc, MemoryPriority prio = MemoryPriority.Normal)
		{
			/*
			var memInfo = new MEMORY_PRIORITY_INFORMATION { MemoryPriority = Convert.ToUInt32(prio) };
			uint memSize = Convert.ToUInt32(Marshal.SizeOf(memInfo));
			var memInfoP = System.Runtime.InteropServices.Marshal.AllocHGlobal(memInfo);
			MemoryManager.SetProcessInformation(prc.Handle, PROCESS_INFORMATION_CLASS.ProcessMemoryPriority, memInfoP, memSize);
			*/
		}

		public MemoryController(Process prc, MemoryPriority prio = MemoryPriority.Normal)
		{
			process = prc;
			priority = prio;
		}
	}

	public enum PROCESS_INFORMATION_CLASS
	{
		ProcessMemoryPriority,
		ProcessPowerThrottling,
	}

	// BUG: Requires Windows 8
	[StructLayoutAttribute(LayoutKind.Sequential)]
	public struct MEMORY_PRIORITY_INFORMATION
	{
		public ulong MemoryPriority;
	}

	public enum MemoryPriority
	{
		VeryLow = 1, // MEMORY_PRIORITY_VERY_LOWW
		Low = 2, // MEMORY_PRIORITY_LOWW
		Medium = 3, // MEMORY_PRIORITY_MEDIUMm
		BelowNormal = 4, // MEMORY_PRIORITY_BELOW_NORMALL
		Normal = 5 // MEMORY_PRIORITY_NORMALL
	}

	public static class MemoryManager
	{
		// [DllImport("kernel32.dll", SetLastError=true]
		//[DllImport("kernel32.dll", EntryPoint = "SetProcessInformation")]
		//static extern bool SetProcessInformation(IntPtr hProcess, PROCESS_INFORMATION_CLASS ProcessInformationClass, IntPtr ProcessInformation, uint ProcessInformationSize);
	}
}