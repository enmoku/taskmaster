//
// NativeMethods.cs
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
using System.Runtime.InteropServices;

namespace Taskmaster
{
	public static partial class NativeMethods
	{
		// for ActiveAppManager.cs

		/// <summary>
		/// https://docs.microsoft.com/en-us/windows/desktop/api/winuser/nf-winuser-getwindowthreadprocessid
		/// </summary>
		/// <param name="hWnd">Window handle</param>
		/// <param name="lpdwProcessId">Process ID of the hwnd's creator.</param>
		/// <returns>Thread ID of the hwnd's creator</returns>
		[DllImport("user32.dll")] // SetLastError=true
		public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

		public delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

		[DllImport("user32.dll")]
		public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool UnhookWinEvent(IntPtr hWinEventHook); // automatic

		[DllImport("user32.dll")]
		public static extern IntPtr GetForegroundWindow();

		[DllImport("user32.dll", CharSet = CharSet.Unicode, ThrowOnUnmappableChar = true)]
		public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

		// uMsg = uint, but Windows.Forms.Message.Msg is int
		// lParam = int or long
		// wParam = uint or ulong

		public const int WM_HOTKEY = 0x0312; // uMsg
		public const int WM_COMPACTING = 0x0041; // uMsg
		public const int WM_SYSCOMMAND = 0x0112; // uMsg

		public const int HWND_BROADCAST = 0xFFFF; // hWnd
		public const int HWND_TOPMOST = -1; // hWnd

		[Flags]
		public enum SendMessageTimeoutFlags : uint
		{
			/// <summary>
			/// The calling thread is not prevented from processing other requests while waiting for the function to return.
			/// </summary>
			SMTO_NORMAL = 0x0,

			/// <summary>
			/// Prevents the calling thread from processing any other requests until the function returns.
			/// </summary>
			SMTO_BLOCK = 0x1,

			/// <summary>
			/// The function returns without waiting for the time-out period to elapse if the receiving thread appears to not respond or "hangs."
			/// </summary>
			SMTO_ABORTIFHUNG = 0x2,

			/// <summary>
			/// The function does not enforce the time-out period as long as the receiving thread is processing messages.
			/// </summary>
			SMTO_NOTIMEOUTIFNOTHUNG = 0x8,

			/// <summary>
			/// The function should return 0 if the receiving window is destroyed or its owning thread dies while the message is being processed.
			/// </summary>
			SMTO_ERRORONEXIT = 0x20
		}

		[DllImport("user32.dll", CharSet = CharSet.Auto)] // SetLastError
		public static extern IntPtr SendMessageTimeout(
			IntPtr hWnd, int Msg, ulong wParam, long lParam,
			SendMessageTimeoutFlags flags, uint timeout, out IntPtr result);

		[DllImport("kernel32.dll")] // SetLastError = true
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool CloseHandle(IntPtr Handle);

		//     No dialog box confirming the deletion of the objects will be displayed.
		public const int SHERB_NOCONFIRMATION = 0x00000001;
		//     No dialog box indicating the progress will be displayed.
		public const int SHERB_NOPROGRESSUI = 0x00000002;
		//     No sound will be played when the operation is complete.
		public const int SHERB_NOSOUND = 0x00000004;

		/// <summary>
		/// Empty recycle bin.
		/// </summary>
		[DllImport("shell32.dll", CharSet = CharSet.Unicode, BestFitMapping = false, ThrowOnUnmappableChar = true)]
		public static extern int SHEmptyRecycleBin(IntPtr hWnd, string pszRootPath, uint dwFlags);

		[StructLayout(LayoutKind.Sequential)] // , Pack = 4 causes shqueryrecyclebin to error with invalid args
		public struct SHQUERYRBINFO
		{
			public int cbSize;
			public long i64Size;
			public long i64NumItems;
		}

		[DllImport("shell32.dll", CharSet = CharSet.Unicode)] // SetLastError = true
		public static extern uint SHQueryRecycleBin(string pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

		[DllImport("user32.dll")] // SetLastError = true
		public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

		[System.Runtime.InteropServices.DllImport("user32.dll")]
		public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

		[System.Runtime.InteropServices.DllImport("user32.dll")]
		public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

		[Flags]
		public enum KeyModifier
		{
			None = 0,
			Alt = 1,
			Control = 2,
			Shift = 4,
			WinKey = 8
		}

		[DllImport("user32.dll")]
		public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

		public const int SW_FORCEMINIMIZE = 11;
		public const int SW_MINIMIZE = 6;

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool IsWindowVisible(IntPtr hWnd);

		[Flags]
		public enum ErrorModes : uint
		{
			/// <summary>
			/// Use the system default, which is to display all error dialog boxes.
			/// </summary>
			SEM_SYSTEMDEFAULT = 0x0,

			/// <summary>
			/// <para>The system does not display the critical-error-handler message box. Instead, the system sends the error to the calling process.</para>
			/// <para>Best practice is that all applications call the process-wide SetErrorMode function with a parameter of SEM_FAILCRITICALERRORS at startup. This is to prevent error mode dialogs from hanging the application.</para>
			/// </summary>
			SEM_FAILCRITICALERRORS = 0x0001,

			/// <summary>
			/// Relevant only to Itanium processors.
			/// </summary>
			SEM_NOALIGNMENTFAULTEXCEPT = 0x0004,

			/// <summary>
			/// The system does not display the Windows Error Reporting dialog.
			/// </summary>
			SEM_NOGPFAULTERRORBOX = 0x0002,

			/// <summary>
			/// The OpenFile function does not display a message box when it fails to find a file. Instead, the error is returned to the caller. This error mode overrides the OF_PROMPT flag.
			/// </summary>
			SEM_NOOPENFILEERRORBOX = 0x8000
		}

		[DllImport("kernel32.dll")]
		public static extern ErrorModes SetErrorMode(ErrorModes mode);

		/// <summary>
		/// "Safe" version of HANDLE.
		/// </summary>
		public class HANDLE : SafeHandle
		{
			protected HANDLE()
				: base(new IntPtr(-1), true)
			{
				// NOP
			}

			public override bool IsInvalid => handle.ToInt32() == -1;

			protected override bool ReleaseHandle() => CloseHandle(handle);
		}

		[DllImport("ntdll.dll")]
		public static extern uint NtSetSystemInformation(SYSTEM_INFORMATION_CLASS InfoClass, IntPtr Info, int Length);

		[DllImport("ntdll.dll")]
		public static extern uint NtQuerySystemInformation(SYSTEM_INFORMATION_CLASS InfoClass, IntPtr Info, uint Size, out uint Length);

		[Flags]
		public enum FileCacheFlags : uint
		{
			NOT_PRESENT = 0x0,
			MAX_HARD_ENABLE = 0x00000001,
			MAX_HARD_DISABLE = 0x00000002,
			MIN_HARD_ENABLE = 0x00000004,
			MIN_HARD_DISABLE = 0x00000008,
		}

		[DllImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool GetSystemFileCacheSize(out uint lpMinimumFileCacheSize, out uint lpMaximumFileCacheSize, out FileCacheFlags Flags);

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