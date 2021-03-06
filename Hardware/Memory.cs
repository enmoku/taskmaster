﻿//
// Memory.cs
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
using System.Globalization;
using System.Runtime.InteropServices;

using Windows = MKAh.Wrapper.Windows;

namespace Taskmaster
{
	using static Application;

	static class Memory
	{
		/// <summary>
		/// Total physical system memory in bytes.
		/// </summary>
		public static long Total { get; private set; }

		/// <summary>
		/// Memory used in bytes.
		/// </summary>
		public static long Used { get; private set; }

		/// <summary>
		/// Private bytes.
		/// </summary>
		public static ulong Private => Convert.ToUInt64(pfcprivate?.Value ?? 0);

		/// <summary>
		/// Memory pressure. 0.0 to 1.0 scale.
		/// Queries a performance counter and may be slow as such.
		/// </summary>
		public static double Pressure => (double)Private / (double)Total;

		public static ulong Standby;

		/*
		/// <summary>
		/// Free memory, in megabytes.
		/// </summary>
		public static float Free => pfcfree?.Value ?? 0;
		*/

		/// <summary>
		/// Free memory in bytes.
		/// Update() required to match current state.
		/// </summary>
		public static long Free { get; private set; }

		static readonly Windows.PerformanceCounter pfcprivate = new Windows.PerformanceCounter("Process", "Private Bytes", "_Total"); // no p/invoke alternative?
		//static Windows.PerformanceCounter pfccommit = new Windows.PerformanceCounter("Memory", "% Committed Bytes In Use", null);
		//static Windows.PerformanceCounter pfcfree = new Windows.PerformanceCounter("Memory", "Available MBytes", null);

		const int PurgeStandbyList = 4;

		// ctor
		static Memory() => Update();

		[Conditional("DEBUG")]
		static void PrintMemoryStats() => Logging.DebugMsg("<System> Memory --- Total: " + HumanInterface.ByteString(Convert.ToInt64(Total)) + " --- Private: " + HumanInterface.ByteString(Convert.ToInt64(Private)));

		public static void PurgeStandby()
		{
			int length = Marshal.SizeOf(PurgeStandbyList);
			var handle = GCHandle.Alloc(PurgeStandbyList, GCHandleType.Pinned);
			NativeMethods.NtSetSystemInformation(NativeMethods.SYSTEM_INFORMATION_CLASS.SystemMemoryListInformation, handle.AddrOfPinnedObject(), length);
			handle.Free();
		}

		public static void UpdateAll()
		{
			IntPtr buffer = IntPtr.Zero;
			NativeMethods.SystemMemoryListInformation info;
			try
			{
				buffer = Marshal.AllocHGlobal(256);

				const uint size = 0; // Marshal.SizeOf(NativeMethods.SYSTEM_MEMORY_LIST_INFORMATION);

				NativeMethods.NtQuerySystemInformation(NativeMethods.SYSTEM_INFORMATION_CLASS.SystemMemoryListInformation, buffer, size, out uint length);
				info = (NativeMethods.SystemMemoryListInformation)Marshal.PtrToStructure(buffer, typeof(NativeMethods.SystemMemoryListInformation));
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				return;
			}
			finally
			{
				if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
			}
		}

		[Conditional("DEBUG")]
		public static void GetCache()
		{
			if (NativeMethods.GetSystemFileCacheSize(out uint minCache, out uint maxCache, out _))
				Logging.DebugMsg("MEMORY CACHE - Min: " + minCache.ToString(CultureInfo.InvariantCulture) + ", Max: " + maxCache.ToString(CultureInfo.InvariantCulture));
			else
				Logging.DebugMsg("MEMORY CACHE - information unavailable");
		}

		static NativeMethods.MemoryStatusEx mem = new NativeMethods.MemoryStatusEx { dwLength = (uint)Marshal.SizeOf(typeof(NativeMethods.MemoryStatusEx)) };

		public static void Update()
		{
			try
			{
				//GetCache();

				NativeMethods.GlobalMemoryStatusEx(ref mem);

				Total = Convert.ToInt64(mem.ullTotalPhys);
				Free = Convert.ToInt64(mem.ullAvailPhys);
				Used = Total - Convert.ToInt64(mem.ullAvailPhys);

				if (Trace && DebugMemory) Logging.DebugMsg($"MEMORY - Total: {Total}, Free: {Free}, Used: {Used}");
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		// weird hack
		/*
		static readonly Finalizer finalizer = new Finalizer();

		sealed class Finalizer
		{
			~Finalizer()
			{
				// Nothing too important here
				Logging.DebugMsg("MemoryManager static finalization");
				pfcprivate.Dispose();
				//pfccommit?.Dispose();
				//pfccommit = null;
				//pfcfree?.Dispose();
				//pfcfree = null;
			}
		}
		*/
	}

	public static partial class NativeMethods
	{
		// https://docs.microsoft.com/en-us/windows/desktop/api/sysinfoapi/ns-sysinfoapi-_memorystatusex
		[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
		internal struct MemoryStatusEx
		{
			// size of the structure in bytes. Used by C functions
			public uint dwLength;

			/// <summary>
			/// 0 to 100, percentage of memory usage
			/// </summary>
			public uint dwMemoryLoad;

			/// <summary>
			/// Total size of physical memory, in bytes.
			/// </summary>
			public ulong ullTotalPhys;

			/// <summary>
			/// Size of physical memory available, in bytes.
			/// </summary>
			public ulong ullAvailPhys;

			/// <summary>
			/// Size of the committed memory limit, in bytes. This is physical memory plus the size of the page file, minus a small overhead.
			/// </summary>
			public ulong ullTotalPageFile;

			/// <summary>
			/// Size of available memory to commit, in bytes. The limit is ullTotalPageFile.
			/// </summary>
			public ulong ullAvailPageFile;

			/// <summary>
			/// Total size of the user mode portion of the virtual address space of the calling process, in bytes.
			/// </summary>
			public ulong ullTotalVirtual;

			/// <summary>
			/// Size of unreserved and uncommitted memory in the user mode portion of the virtual address space of the calling process, in bytes.
			/// </summary>
			public ulong ullAvailVirtual;

			/// <summary>
			/// Size of unreserved and uncommitted memory in the extended portion of the virtual address space of the calling process, in bytes.
			/// </summary>
			public ulong ullAvailExtendedVirtual;
		}

		// https://docs.microsoft.com/en-us/windows/desktop/api/sysinfoapi/nf-sysinfoapi-globalmemorystatusex
		[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
		[System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		static internal extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);
	}

	/*
	class MemoryController
	{
		readonly Process process;
		readonly MemoryPriority priority;

		public static void EnforceProcess(Process prc, MemoryPriority prio = MemoryPriority.Normal)
		{
			var memInfo = new MEMORY_PRIORITY_INFORMATION { MemoryPriority = Convert.ToUInt32(prio) };
			uint memSize = Convert.ToUInt32(Marshal.SizeOf(memInfo));
			IntPtr memInfoP = new IntPtr();
			Marshal.StructureToPtr(memInfo, memInfoP, true);
			MemoryManager.SetProcessInformation(prc.Handle, PROCESS_INFORMATION_CLASS.ProcessMemoryPriority, memInfoP, memSize);
		}

		public MemoryController(Process prc, MemoryPriority prio = MemoryPriority.Normal)
		{
			process = prc;
			priority = prio;
		}
	}

	// BUG: Requires Windows 8 or later
	[StructLayout(LayoutKind.Sequential)]
	public struct MEMORY_PRIORITY_INFORMATION
	{
		public ulong MemoryPriority;
	}

	// BUG: Requires Windows 8 or later
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
	}
	*/
}