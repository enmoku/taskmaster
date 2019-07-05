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

namespace Taskmaster
{
	public static partial class NativeMethods
	{
		[DllImport("kernel32.dll", CharSet = CharSet.Auto)] // SetLastError = true
		public static extern bool SetPriorityClass(IntPtr handle, uint priorityClass);

		/// <summary>
		/// The process must have PROCESS_QUERY_INFORMATION and PROCESS_VM_READ access rights.
		/// </summary>
		[DllImport("psapi.dll", SetLastError = true, CharSet = CharSet.Unicode, BestFitMapping = false, ThrowOnUnmappableChar = true)]
		public static extern uint GetModuleFileNameEx(IntPtr hProcess, IntPtr hModule, [Out] System.Text.StringBuilder lpBaseName, [In] [MarshalAs(UnmanagedType.U4)] uint nSize);

		public static HANDLE OpenProcessFully(System.Diagnostics.Process process)
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
					int rv = NtSetInformationProcess(handle, PROCESS_INFORMATION_CLASS_WIN7.ProcessIoPriority, ref ioPrio, 4);

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
		public static extern int NtSetInformationProcess(SafeHandle hProcess, PROCESS_INFORMATION_CLASS_WIN7 ProcessInformationClass, ref IntPtr ProcessInformation, uint ProcessInformationSize);

		[DllImport("ntdll.dll", SetLastError = true)]
		public static extern int NtQueryInformationProcess(SafeHandle hProcess, PROCESS_INFORMATION_CLASS_WIN7 ProcessInformationClass, ref IntPtr ProcessInformation, uint ProcessInformationSize, ref int ReturnSize);

		const int SystemMemoryListInformation = 0x0050;

		[DllImport("ntdll.dll")]
		public static extern uint NtSetSystemInformation(SYSTEM_INFORMATION_CLASS InfoClass, IntPtr Info, int Length);

		[DllImport("ntdll.dll")]
		public static extern uint NtQuerySystemInformation(SYSTEM_INFORMATION_CLASS InfoClass, IntPtr Info, uint Size, out uint Length);

		[DllImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool GetSystemFileCacheSize(out uint lpMinimumFileCacheSize, out uint lpMaximumFileCacheSize, out FileCacheFlags Flags);

		[Flags]
		public enum FileCacheFlags : uint
		{
			NOT_PRESENT = 0x0,
			MAX_HARD_ENABLE = 0x00000001,
			MAX_HARD_DISABLE = 0x00000002,
			MIN_HARD_ENABLE = 0x00000004,
			MIN_HARD_DISABLE = 0x00000008,
		}

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
			STANDARD_RIGHTS_ALL = DELETE | READ_CONTROL | WRITE_DAC | WRITE_OWNER | SYNCHRONIZE,
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
		public static extern HANDLE OpenProcess(PROCESS_ACCESS_RIGHTS dwDesiredAccess, bool bInheritHandle, int dwProcessId);

		[StructLayout(LayoutKind.Sequential)]
		public struct RECT
		{
			public int Left;
			public int Top;
			public int Right;
			public int Bottom;
		}

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool GetWindowRect(IntPtr hWnd, [In, Out] ref RECT rect);

		/// <summary>
		/// Empties the working set.
		/// </summary>
		/// <returns>Uhh?</returns>
		/// <param name="hwProc">Process handle.</param>
		[DllImport("psapi.dll")]
		public static extern int EmptyWorkingSet(IntPtr hwProc);

		public enum SYSTEM_INFORMATION_CLASS
		{
			SystemBasicInformation = 0x0000,
			SystemProcessorInformation = 0x0001,
			SystemPerformanceInformation = 0x0002,
			SystemTimeOfDayInformation = 0x0003,
			SystemPathInformation = 0x0004,
			SystemProcessInformation = 0x0005,
			SystemCallCountInformation = 0x0006,
			SystemDeviceInformation = 0x0007,
			SystemProcessorPerformanceInformation = 0x0008,
			SystemFlagsInformation = 0x0009,
			SystemCallTimeInformation = 0x000A,
			SystemModuleInformation = 0x000B,
			SystemLocksInformation = 0x000C,
			SystemStackTraceInformation = 0x000D,
			SystemPagedPoolInformation = 0x000E,
			SystemNonPagedPoolInformation = 0x000F,
			SystemHandleInformation = 0x0010,
			SystemObjectInformation = 0x0011,
			SystemPageFileInformation = 0x0012,
			SystemVdmInstemulInformation = 0x0013,
			SystemVdmBopInformation = 0x0014,
			SystemFileCacheInformation = 0x0015,
			SystemPoolTagInformation = 0x0016,
			SystemInterruptInformation = 0x0017,
			SystemDpcBehaviorInformation = 0x0018,
			SystemFullMemoryInformation = 0x0019,
			SystemLoadGdiDriverInformation = 0x001A,
			SystemUnloadGdiDriverInformation = 0x001B,
			SystemTimeAdjustmentInformation = 0x001C,
			SystemSummaryMemoryInformation = 0x001D,
			SystemMirrorMemoryInformation = 0x001E,
			SystemPerformanceTraceInformation = 0x001F,
			SystemCrashDumpInformation = 0x0020,
			SystemExceptionInformation = 0x0021,
			SystemCrashDumpStateInformation = 0x0022,
			SystemKernelDebuggerInformation = 0x0023,
			SystemContextSwitchInformation = 0x0024,
			SystemRegistryQuotaInformation = 0x0025,
			SystemExtendServiceTableInformation = 0x0026,
			SystemPrioritySeperation = 0x0027,
			SystemVerifierAddDriverInformation = 0x0028,
			SystemVerifierRemoveDriverInformation = 0x0029,
			SystemProcessorIdleInformation = 0x002A,
			SystemLegacyDriverInformation = 0x002B,
			SystemCurrentTimeZoneInformation = 0x002C,
			SystemLookasideInformation = 0x002D,
			SystemTimeSlipNotification = 0x002E,
			SystemSessionCreate = 0x002F,
			SystemSessionDetach = 0x0030,
			SystemSessionInformation = 0x0031,
			SystemRangeStartInformation = 0x0032,
			SystemVerifierInformation = 0x0033,
			SystemVerifierThunkExtend = 0x0034,
			SystemSessionProcessInformation = 0x0035,
			SystemLoadGdiDriverInSystemSpace = 0x0036,
			SystemNumaProcessorMap = 0x0037,
			SystemPrefetcherInformation = 0x0038,
			SystemExtendedProcessInformation = 0x0039,
			SystemRecommendedSharedDataAlignment = 0x003A,
			SystemComPlusPackage = 0x003B,
			SystemNumaAvailableMemory = 0x003C,
			SystemProcessorPowerInformation = 0x003D,
			SystemEmulationBasicInformation = 0x003E,
			SystemEmulationProcessorInformation = 0x003F,
			SystemExtendedHandleInformation = 0x0040,
			SystemLostDelayedWriteInformation = 0x0041,
			SystemBigPoolInformation = 0x0042,
			SystemSessionPoolTagInformation = 0x0043,
			SystemSessionMappedViewInformation = 0x0044,
			SystemHotpatchInformation = 0x0045,
			SystemObjectSecurityMode = 0x0046,
			SystemWatchdogTimerHandler = 0x0047,
			SystemWatchdogTimerInformation = 0x0048,
			SystemLogicalProcessorInformation = 0x0049,
			SystemWow64SharedInformationObsolete = 0x004A,
			SystemRegisterFirmwareTableInformationHandler = 0x004B,
			SystemFirmwareTableInformation = 0x004C,
			SystemModuleInformationEx = 0x004D,
			SystemVerifierTriageInformation = 0x004E,
			SystemSuperfetchInformation = 0x004F,
			SystemMemoryListInformation = 0x0050, // SYSTEM_MEMORY_LIST_INFORMATION
			SystemFileCacheInformationEx = 0x0051,
			SystemThreadPriorityClientIdInformation = 0x0052,
			SystemProcessorIdleCycleTimeInformation = 0x0053,
			SystemVerifierCancellationInformation = 0x0054,
			SystemProcessorPowerInformationEx = 0x0055,
			SystemRefTraceInformation = 0x0056,
			SystemSpecialPoolInformation = 0x0057,
			SystemProcessIdInformation = 0x0058,
			SystemErrorPortInformation = 0x0059,
			SystemBootEnvironmentInformation = 0x005A,
			SystemHypervisorInformation = 0x005B,
			SystemVerifierInformationEx = 0x005C,
			SystemTimeZoneInformation = 0x005D,
			SystemImageFileExecutionOptionsInformation = 0x005E,
			SystemCoverageInformation = 0x005F,
			SystemPrefetchPatchInformation = 0x0060,
			SystemVerifierFaultsInformation = 0x0061,
			SystemSystemPartitionInformation = 0x0062,
			SystemSystemDiskInformation = 0x0063,
			SystemProcessorPerformanceDistribution = 0x0064,
			SystemNumaProximityNodeInformation = 0x0065,
			SystemDynamicTimeZoneInformation = 0x0066,
			SystemCodeIntegrityInformation = 0x0067,
			SystemProcessorMicrocodeUpdateInformation = 0x0068,
			SystemProcessorBrandString = 0x0069,
			SystemVirtualAddressInformation = 0x006A,
			SystemLogicalProcessorAndGroupInformation = 0x006B,
			SystemProcessorCycleTimeInformation = 0x006C,
			SystemStoreInformation = 0x006D,
			SystemRegistryAppendString = 0x006E,
			SystemAitSamplingValue = 0x006F,
			SystemVhdBootInformation = 0x0070,
			SystemCpuQuotaInformation = 0x0071,
			SystemNativeBasicInformation = 0x0072,
			SystemErrorPortTimeouts = 0x0073,
			SystemLowPriorityIoInformation = 0x0074,
			SystemBootEntropyInformation = 0x0075,
			SystemVerifierCountersInformation = 0x0076,
			SystemPagedPoolInformationEx = 0x0077,
			SystemSystemPtesInformationEx = 0x0078,
			SystemNodeDistanceInformation = 0x0079,
			SystemAcpiAuditInformation = 0x007A,
			SystemBasicPerformanceInformation = 0x007B,
			SystemQueryPerformanceCounterInformation = 0x007C,
			SystemSessionBigPoolInformation = 0x007D,
			SystemBootGraphicsInformation = 0x007E,
			SystemScrubPhysicalMemoryInformation = 0x007F,
			SystemBadPageInformation = 0x0080,
			SystemProcessorProfileControlArea = 0x0081,
			SystemCombinePhysicalMemoryInformation = 0x0082,
			SystemEntropyInterruptTimingInformation = 0x0083,
			SystemConsoleInformation = 0x0084,
			SystemPlatformBinaryInformation = 0x0085,
			SystemThrottleNotificationInformation = 0x0086,
			SystemHypervisorProcessorCountInformation = 0x0087,
			SystemDeviceDataInformation = 0x0088,
			SystemDeviceDataEnumerationInformation = 0x0089,
			SystemMemoryTopologyInformation = 0x008A,
			SystemMemoryChannelInformation = 0x008B,
			SystemBootLogoInformation = 0x008C,
			SystemProcessorPerformanceInformationEx = 0x008D,
			SystemSpare0 = 0x008E,
			SystemSecureBootPolicyInformation = 0x008F,
			SystemPageFileInformationEx = 0x0090,
			SystemSecureBootInformation = 0x0091,
			SystemEntropyInterruptTimingRawInformation = 0x0092,
			SystemPortableWorkspaceEfiLauncherInformation = 0x0093,
			SystemFullProcessInformation = 0x0094,
			MaxSystemInfoClass = 0x0095
		}

		[StructLayout(LayoutKind.Sequential, Pack = 1)]
		public struct SYSTEM_MEMORY_LIST_INFORMATION
		{
			public uint ZeroPageCount; // Size=4 Offset=0

			public uint FreePageCount; // Size=4 Offset=4

			public uint ModifiedPageCount; // Size=4 Offset=8

			public uint ModifiedNoWritePageCount; // Size=4 Offset=12

			public uint BadPageCount; // Size=4 Offset=16

			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] // 7 priority levels
			public uint[] PageCountByPriority; // Size=32 Offset=20

			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] // 7 priority levels
			public uint[] RepurposedPagesByPriority; // Size=32 Offset=52

			public uint ModifiedPageCountPageFile; // Size=4 Offset=84
		}

		[DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool GetICMProfile([In] IntPtr hdc, ulong pBufSize, [Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFilename);
	}
}
