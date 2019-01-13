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
	public static class NativeMethods
	{
		/// <summary>
		/// Tick count as ulong. Actually milliseconds.
		/// </summary>
		[DllImport("kernel32.dll")]
		public static extern ulong GetTickCount64();

		/// <summary>
		/// Tick count as uint. Actually milliseconds.
		/// </summary>
		[DllImport("kernel32.dll")]
		public static extern uint GetTickCount();

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

		public const uint WINEVENT_OUTOFCONTEXT = 0x0000; // async
		public const uint WINEVENT_SKIPOWNPROCESS = 0x0002; // skip self

		public const uint EVENT_SYSTEM_FOREGROUND = 3;

		[DllImport("user32.dll")]
		public static extern IntPtr GetForegroundWindow();

		[DllImport("user32.dll", CharSet = CharSet.Unicode, ThrowOnUnmappableChar = true)]
		public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

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

		// for ProcessManager.cs

		/// <summary>
		/// Empties the working set.
		/// </summary>
		/// <returns>Uhh?</returns>
		/// <param name="hwProc">Process handle.</param>
		[DllImport("psapi.dll")]
		public static extern int EmptyWorkingSet(IntPtr hwProc);

		// for PowerManager.cs

		// UserPowerKey is reserved for future functionality and must always be null
		[DllImport("powrprof.dll", EntryPoint = "PowerSetActiveScheme")]
		public static extern uint PowerSetActiveScheme(IntPtr UserPowerKey, ref Guid PowerPlanGuid);

		[DllImport("powrprof.dll", EntryPoint = "PowerGetActiveScheme")]
		public static extern uint PowerGetActiveScheme(IntPtr UserPowerKey, out IntPtr PowerPlanGuid);

		public const int DEVICE_NOTIFY_WINDOW_HANDLE = 0x00000000; // DWORD

		// SetLastError
		[DllImport("user32.dll", EntryPoint = "RegisterPowerSettingNotification", CallingConvention = CallingConvention.StdCall)]
		public static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid PowerSettingGuid, int Flags);

		[StructLayout(LayoutKind.Sequential, Pack = 4)]
		public struct POWERBROADCAST_SETTING
		{
			public Guid PowerSetting;
			public uint DataLength;
			public byte Data;
		}

		// uMsg = uint, but Windows.Forms.Message.Msg is int
		// lParam = int or long
		// wParam = uint or ulong

		public const int WM_HOTKEY = 0x0312; // uMsg
		public const int WM_COMPACTING = 0x0041; // uMsg
		public const int WM_SYSCOMMAND = 0x0112; // uMsg
		public const int WM_POWERBROADCAST = 0x218; // uMsg

		public const long SC_MONITORPOWER = 0xF170; // wParam
		public const long PBT_POWERSETTINGCHANGE = 0x8013; // wParam

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

		// for ProcessMAnagerUtility.cs

		[DllImport("kernel32.dll")]
		public static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

		[DllImport("psapi.dll", CharSet = CharSet.Unicode, ThrowOnUnmappableChar = true)]
		public static extern uint GetModuleFileNameEx(IntPtr hProcess, IntPtr hModule, [Out] System.Text.StringBuilder lpBaseName, [In] [MarshalAs(UnmanagedType.U4)] int nSize);

		[DllImport("kernel32.dll")] // SetLastError = true
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool CloseHandle(IntPtr hObject);

		// for ProcessController.cs

		[DllImport("kernel32.dll", CharSet = CharSet.Auto)] // SetLastError = true
		public static extern bool SetPriorityClass(IntPtr handle, uint priorityClass);

		public enum PriorityTypes
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

		[DllImport("Kernel32.dll", CharSet = CharSet.Auto)] // SetLastError = true
		public static extern bool DeviceIoControl(
			Microsoft.Win32.SafeHandles.SafeFileHandle hDevice,
			int dwIoControlCode,
			IntPtr InBuffer,
			int nInBufferSize,
			IntPtr OutBuffer,
			int nOutBufferSize,
			ref int pBytesReturned,
			[In] ref System.Threading.NativeOverlapped lpOverlapped
		);

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
			/// The system does not display the critical-error-handler message box. Instead, the system sends the error to the calling process.
			/// 
			/// Best practice is that all applications call the process-wide SetErrorMode function with a parameter of SEM_FAILCRITICALERRORS at startup. This is to prevent error mode dialogs from hanging the application.
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
	}
}