//
// Process.NativeMethods.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2019 M.A.
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
using System.Runtime.InteropServices;

namespace Taskmaster.Process
{
	public static partial class NativeMethods
	{
		[DllImport("kernel32.dll", CharSet = CharSet.Auto)] // SetLastError = true
		internal static extern bool SetPriorityClass(IntPtr handle, uint priorityClass);

		/// <summary>
		/// The process must have PROCESS_QUERY_INFORMATION and PROCESS_VM_READ access rights.
		/// </summary>
		[DllImport("psapi.dll", SetLastError = true, CharSet = CharSet.Unicode, BestFitMapping = false, ThrowOnUnmappableChar = true)]
		internal static extern uint GetModuleFileNameEx(IntPtr hProcess, IntPtr hModule, [Out] System.Text.StringBuilder lpBaseName, [In] [MarshalAs(UnmanagedType.U4)] uint nSize);

		internal static Taskmaster.NativeMethods.HANDLE? OpenProcessFully(System.Diagnostics.Process process)
		{
			try
			{
				return OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_ALL_ACCESS, false, process.Id);
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}

			return null;
		}

		public static bool SetIOPriority(SafeHandle handle, int priority)
		{
			// the functionality changed with Windows 8 drastically and likely does something else
			if (MKAh.Execution.IsWin7)
			{
				try
				{
					var ioPrio = new IntPtr(priority);
					NtSetInformationProcess(handle, PROCESS_INFORMATION_CLASS_WIN7.ProcessIoPriority, ref ioPrio, 4);

					int error = Marshal.GetLastWin32Error();
					//Logging.DebugMsg($"SetInformationProcess error code: {error} --- return value: {rv:X}");

					return error == 0;
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
				}
			}
			return false;
		}

		public static int GetIOPriority(SafeHandle handle)
		{
			// the functionality changed with Windows 8 drastically and likely does something else
			if (MKAh.Execution.IsWin7)
			{
				try
				{
					int resLen = 0;
					var ioPrio = new IntPtr(0);
					if (handle is null) return -1;
					int rv = NtQueryInformationProcess(handle, PROCESS_INFORMATION_CLASS_WIN7.ProcessIoPriority, ref ioPrio, 4, ref resLen);

					int error = Marshal.GetLastWin32Error();
					//Logging.DebugMsg($"QueryInformationProcess error code: {error} --- return value: {rv:X}");

					return error == 0 ? ioPrio.ToInt32() : -1;
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
				}
			}
			return 0;
		}

		[DllImport("ntdll.dll", SetLastError = true)]
		internal static extern int NtSetInformationProcess(SafeHandle hProcess, PROCESS_INFORMATION_CLASS_WIN7 ProcessInformationClass, ref IntPtr ProcessInformation, uint ProcessInformationSize);

		[DllImport("ntdll.dll", SetLastError = true)]
		internal static extern int NtQueryInformationProcess(SafeHandle hProcess, PROCESS_INFORMATION_CLASS_WIN7 ProcessInformationClass, ref IntPtr ProcessInformation, uint ProcessInformationSize, ref int ReturnSize);

		const int SystemMemoryListInformation = 0x0050;

		// It seems the contents of this enum were changed with Windows 8
		public enum PROCESS_INFORMATION_CLASS_WIN7 : int // PROCESS_INFORMATION_CLASS, for Win7
		{
			/*
			ProcessBasicInformation = 0,
			ProcessQuotaLimits = 1,
			ProcessIoCounters = 2,
			ProcessVmCounters = 3,
			ProcessTimes = 4,
			ProcessBasePriority = 5,
			ProcessRaisePriority = 6,
			ProcessDebugPort = 7,
			ProcessExceptionPort = 8,
			ProcessAccessToken = 9,
			ProcessLdtInformation = 10,
			ProcessLdtSize = 11,
			ProcessDefaultHardErrorMode = 12,
			ProcessIoPortHandlers = 13,
			ProcessPooledUsageAndLimits = 14,
			ProcessWorkingSetWatch = 15,
			ProcessUserModeIOPL = 16,
			ProcessEnableAlignmentFaultFixup = 17,
			ProcessPriorityClass = 18,
			ProcessWx86Information = 19,
			ProcessHandleCount = 20,
			ProcessAffinityMask = 21,
			ProcessPriorityBoost = 22,
			ProcessDeviceMap = 23,
			ProcessSessionInformation = 24,
			ProcessForegroundInformation = 25,
			ProcessWow64Information = 26,
			ProcessImageFileName = 27,
			ProcessLUIDDeviceMapsEnabled = 28,
			ProcessBreakOnTermination = 29,
			ProcessDebugObjectHandle = 30,
			ProcessDebugFlags = 31,
			ProcessHandleTracing = 32,
			*/
			ProcessIoPriority = 0x21,
			/*
			ProcessExecuteFlags = 0x22,
			ProcessResourceManagement,
			ProcessCookie,
			ProcessImageInformation,
			ProcessCycleTime,
			ProcessPagePriority,
			ProcessInstrumentationCallback = 40,
			ProcessThreadStackAllocation,
			ProcessWorkingSetWatchEx,
			ProcessImageFileNameWin32,
			ProcessImageFileMapping,
			ProcessAffinityUpdateMode,
			ProcessMemoryAllocationMode,
			*/
		}

		enum PROCESS_INFORMATION_CLASS_WIN8 : int // PROCESS_INFORMATION_CLASS, for Win8 and newer (Win10 compatible, too)
		{
			ProcessMemoryPriority = 0,
			ProcessMemoryExhaustionInfo = 1,
			ProcessAppMemoryInfo = 2,
			ProcessInPrivateInfo = 3,
			ProcessPowerThrottling = 4,
			ProcessReservedValue1 = 5,
			ProcessTelemetryCoverageInfo = 6,
			ProcessProtectionLevelInfo = 7,
			ProcessLeapSecondInfo = 8,
			ProcessInformationClassMax = 9,
		}

		[Flags]
		public enum STANDARD_ACCESS_RIGHTS : ulong
		{
			NONE = 0,
			DELETE = 0x00010000L, // Delete object
			READ_CONTROL = 0x00020000L, // Read security descriptor. Not SACL access (requires ACCESS_SYSTEM_SECURITY instead)
			SYNCHRONIZE = 0x00100000L, // Right to use object for synchronization signaling
			WRITE_DAC = 0x00040000L, // Right to modify DACL
			WRITE_OWNER = 0x00080000L, // Right to change owner

			// winnt.h additional standard rights defined as combinations of the above
			STANDARD_RIGHTS_ALL = DELETE | READ_CONTROL | WRITE_DAC | WRITE_OWNER | SYNCHRONIZE,
			STANDARD_RIGHTS_EXECUTE = READ_CONTROL,
			STANDARD_RIGHTS_READ = READ_CONTROL,
			STANDARD_RIGHTS_REQUIRED = DELETE | READ_CONTROL | WRITE_DAC | WRITE_OWNER,
			STANDARD_RIGHTS_WRITE = READ_CONTROL,

			ACCESS_SYSTEM_SECURITY = 16777216,
			MAXIMUM_ALLOWED = 33554432,
			GENERIC_WRITE = 1073741824,
			GENERIC_EXECUTE = 536870912,
			GENERIC_READ = 65535,
			GENERIC_ALL = 268435456,

			SPECIFIC_RIGHTS_ALL = GENERIC_READ, // ?
		}

		[Flags]
		public enum PROCESS_ACCESS_RIGHTS : ulong
		{
			NONE = 0,
			PROCESS_ALL_ACCESS = STANDARD_ACCESS_RIGHTS.STANDARD_RIGHTS_REQUIRED | STANDARD_ACCESS_RIGHTS.SYNCHRONIZE | 65535, // All possible access rights for a process object.
			PROCESS_CREATE_PROCESS = 0x0080, // Required to create a process.
			PROCESS_CREATE_THREAD = 0x0002, // Required to create a thread.
			PROCESS_DUP_HANDLE = 0x0040, // Required to duplicate a handle using DuplicateHandle.
			PROCESS_QUERY_INFORMATION = 0x0400, // Required to retrieve certain information about a process, such as its token, exit code, and priority class (see OpenProcessToken).
			PROCESS_QUERY_LIMITED_INFORMATION = 0x1000, // Required to retrieve certain information about a process(see GetExitCodeProcess, GetPriorityClass, IsProcessInJob, QueryFullProcessImageName). A handle that has the PROCESS_QUERY_INFORMATION access right is automatically granted PROCESS_QUERY_LIMITED_INFORMATION.Windows Server 2003 and Windows XP: This access right is not supported.
			PROCESS_SET_INFORMATION = 0x0200, // Required to set certain information about a process, such as its priority class (see SetPriorityClass).
			PROCESS_SET_QUOTA = 0x0100, // Required to set memory limits using SetProcessWorkingSetSize.
			PROCESS_SUSPEND_RESUME = 0x0800, // Required to suspend or resume a process.
			PROCESS_TERMINATE = 0x0001, // Required to terminate a process using TerminateProcess.
			PROCESS_VM_OPERATION = 0x0008, // Required to perform an operation on the address space of a process(see VirtualProtectEx and WriteProcessMemory).
			PROCESS_VM_READ = 0x0010, // Required to read memory in a process using ReadProcessMemory.
			PROCESS_VM_WRITE = 0x0020, // Required to write to memory in a process using WriteProcessMemory.
			SYNCHRONIZE = 0x00100000L, // Required to wait for the process to terminate using the wait functions.
			// PROCESS_SET_SESSIONID = 4,
		}

		/// <summary>
		/// MUST CLOSE THE RETURNED HANDLE WITH CLOSEHANDLE!!!
		/// </summary>
		[DllImport("kernel32.dll", SetLastError = true)]
		internal static extern Taskmaster.NativeMethods.HANDLE OpenProcess(PROCESS_ACCESS_RIGHTS dwDesiredAccess, bool bInheritHandle, int dwProcessId);

		[StructLayout(LayoutKind.Sequential)]
		public struct Rectangle
		{
			public int Left;
			public int Top;
			public int Right;
			public int Bottom;
		}

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static extern bool GetWindowRect(IntPtr hWnd, [In, Out] ref Rectangle rect);

		/// <summary>
		/// Empties the working set.
		/// </summary>
		/// <returns>Uhh?</returns>
		/// <param name="hwProc">Process handle.</param>
		[DllImport("psapi.dll")]
		internal static extern int EmptyWorkingSet(IntPtr hwProc);

		public const uint WINEVENT_OUTOFCONTEXT = 0x0000; // async
		public const uint WINEVENT_SKIPOWNPROCESS = 0x0002; // skip self
		public const uint EVENT_SYSTEM_FOREGROUND = 3;

		/// <summary>
		/// 
		/// </summary>
		/// <param name="hWnd">Window handle.</param>
		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static extern bool IsHungAppWindow(IntPtr hWnd);

		[DllImport("kernel32.dll", SetLastError = true)]
		internal static extern bool GetSystemTimes(out System.Runtime.InteropServices.ComTypes.FILETIME lpIdleTime, out System.Runtime.InteropServices.ComTypes.FILETIME lpKernelTime, out System.Runtime.InteropServices.ComTypes.FILETIME lpUserTime);

		/// <summary>
		/// 
		/// </summary>
		public static long FiletimeToLong(System.Runtime.InteropServices.ComTypes.FILETIME ft) => ((long)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;

		[StructLayout(LayoutKind.Sequential)]
		internal struct IO_COUNTERS
		{
			public ulong ReadOperationCount;
			public ulong WriteOperationCount;
			public ulong OtherOperationCount;
			public ulong ReadTransferCount;
			public ulong WriteTransferCount;
			public ulong OtherTransferCount;
		}

		[DllImport("kernel32.dll", SetLastError = true)]
		internal static extern bool GetProcessIoCounters(IntPtr hProcess, out IO_COUNTERS counters);

		/// <summary>
		/// https://docs.microsoft.com/en-us/windows/desktop/api/winuser/nf-winuser-getwindowthreadprocessid
		/// </summary>
		/// <param name="hWnd">Window handle</param>
		/// <param name="lpdwProcessId">Process ID of the hwnd's creator.</param>
		/// <returns>Thread ID of the hwnd's creator</returns>
		[DllImport("user32.dll")] // SetLastError=true
		internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);
	}
}
