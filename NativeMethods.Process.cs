//
// NativeMethods.Process.cs
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
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Taskmaster
{
	public static partial class NativeMethods
	{
		public static int OpenProcessFully(Process process)
		{
			try
			{
				int pid = process.Id;
				int Handle = OpenProcess(PROCESS_RIGHTS.PROCESS_ALL_ACCESS, false, pid);
				return Handle;
			}
			catch (Exception ex	)
			{
				Logging.Stacktrace(ex);
			}

			return 0;
		}

		public static bool SetIOPriority(int Handle, int Priority)
		{
			// the functionality changed with Windows 8 drastically and likely does something else
			if (MKAh.OperatingSystem.IsWin7)
			{
				try
				{
					var ioPrio = new IntPtr(Priority);
					int rv = NtSetInformationProcess(Handle, PROCESS_INFORMATION_CLASS_WIN7.ProcessIoPriority, ref ioPrio, 4);

					int error = Marshal.GetLastWin32Error();
					//Debug.WriteLine($"SetInformationProcess error code: {error} --- return value: {rv:X}");

					return error == 0;
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
				}
			}
			return false;
		}

		public static int GetIOPriority(int Handle)
		{
			// the functionality changed with Windows 8 drastically and likely does something else
			if (MKAh.OperatingSystem.IsWin7)
			{
				try
				{
					int resLen = 0;
					var ioPrio = new IntPtr(0);
					if (Handle == 0) return -1;
					int rv = NtQueryInformationProcess(Handle, PROCESS_INFORMATION_CLASS_WIN7.ProcessIoPriority, ref ioPrio, 4, ref resLen);

					int error = Marshal.GetLastWin32Error();
					//Debug.WriteLine($"QueryInformationProcess error code: {error} --- return value: {rv:X}");

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
		public static extern int NtSetInformationProcess(int hProcess, PROCESS_INFORMATION_CLASS_WIN7 ProcessInformationClass, ref IntPtr ProcessInformation, uint ProcessInformationSize);

		[DllImport("ntdll.dll", SetLastError = true)]
		public static extern int NtQueryInformationProcess(int hProcess, PROCESS_INFORMATION_CLASS_WIN7 ProcessInformationClass, ref IntPtr ProcessInformation, uint ProcessInformationSize, ref int ReturnSize);

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

		public enum STANDARD_RIGHTS : uint
		{
			WRITE_OWNER = 524288,
			WRITE_DAC = 262144,
			READ_CONTROL = 131072,
			DELETE = 65536,
			SYNCHRONIZE = 1048576,
			STANDARD_RIGHTS_REQUIRED = 983040,
			STANDARD_RIGHTS_WRITE = READ_CONTROL,
			STANDARD_RIGHTS_EXECUTE = READ_CONTROL,
			STANDARD_RIGHTS_READ = READ_CONTROL,
			STANDARD_RIGHTS_ALL = 2031616,
			SPECIFIC_RIGHTS_ALL = 65535,
			ACCESS_SYSTEM_SECURITY = 16777216,
			MAXIMUM_ALLOWED = 33554432,
			GENERIC_WRITE = 1073741824,
			GENERIC_EXECUTE = 536870912,
			GENERIC_READ = UInt16.MaxValue,
			GENERIC_ALL = 268435456
		}

		public enum PROCESS_RIGHTS : uint
		{
			PROCESS_TERMINATE = 1,
			PROCESS_CREATE_THREAD = 2,
			PROCESS_SET_SESSIONID = 4,
			PROCESS_VM_OPERATION = 8,
			PROCESS_VM_READ = 16,
			PROCESS_VM_WRITE = 32,
			PROCESS_DUP_HANDLE = 64,
			PROCESS_CREATE_PROCESS = 128,
			PROCESS_SET_QUOTA = 256,
			PROCESS_SET_INFORMATION = 512,
			PROCESS_QUERY_INFORMATION = 1024,
			PROCESS_SUSPEND_RESUME = 2048,
			PROCESS_QUERY_LIMITED_INFORMATION = 4096,
			PROCESS_ALL_ACCESS = STANDARD_RIGHTS.STANDARD_RIGHTS_REQUIRED | STANDARD_RIGHTS.SYNCHRONIZE | 65535
		}

		/// <summary>
		/// MUST CLOSE THE RETURNED HANDLE WITH CLOSEHANDLE!!!
		/// </summary>
		[DllImport("kernel32.dll", SetLastError = true)]
		public static extern int OpenProcess(PROCESS_RIGHTS dwDesiredAccess, bool bInheritHandle, int dwProcessId);
	}
}
