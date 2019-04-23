//
// ProcessHelpers.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2016–2019 M.A.
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
using System.ComponentModel; // Win32Exception
using System.Diagnostics; // Process
using System.Runtime.ConstrainedExecution; // attributes
using System.Runtime.InteropServices; // Marshal
using System.Security; // SuppressUnmanagedCodeSecurity
using System.Security.Permissions; // HostProtection
using Microsoft.Win32.SafeHandles; // SafeHandleMinusOneIsInvalid

namespace Taskmaster
{
	public enum ProcessHandlingState
	{
		/// <summary>
		/// New instance
		/// </summary>
		Triage,

		/// <summary>
		/// Collecting info
		/// </summary>
		Datamining, // unused

		/// <summary>
		/// Brief activation
		/// </summary>
		Active, // unused

		/// <summary>
		/// Affecting changes
		/// </summary>
		Processing, // unused

		/// <summary>
		/// Done modifying with some modifications enacted.
		/// </summary>
		Modified,
		/// <summary>
		/// Done modifying but nothing was done.
		/// </summary>
		Unmodified,

		/// <summary>
		/// Done processing.
		/// </summary>
		Finished,

		/// <summary>
		/// Background transition
		/// </summary>
		Paused,
		/// <summary>
		/// Foreground transition
		/// </summary>
		Resumed,
		/// <summary>
		/// Done processing.
		/// </summary>
		Abandoned,
		/// <summary>
		/// Tin.
		/// </summary>
		Invalid,
		/// <summary>
		/// Failed to access process.
		/// </summary>
		AccessDenied,
		/// <summary>
		/// No longer running
		/// </summary>
		Exited
	}

	public enum ProcessAffinityStrategy
	{
		None = 0,
		/// <summary>
		/// Set affinity as is, ignoring everything.
		/// </summary>
		Force = 1,
		/// <summary>
		/// Move and constrain cores to allowed cores but don't increase if setting has more than original
		/// </summary>
		Limit = 2,
		/// <summary>
		/// Assign to cores 1 by 1 linearly.
		/// </summary>
		Scatter = 3,
		/// <summary>
		/// Set foreground process to dedicated core and scatter the rest.
		/// </summary>
		BackgroundScatter = 4,
		/// <summary>
		/// Use all but one of the defined cores for foreground and push all others to 1 remaining.
		/// </summary>
		BackgroundSideline = 5,
	}

	public enum ProcessPriorityStrategy
	{
		None = 0,
		Increase = 1,
		Decrease = 2,
		/// <summary>
		/// Bi-directional
		/// </summary>
		Force = 3,
	}

	public enum WindowResizeStrategy
	{
		None = 0,
		Size = 1,
		Position = 2,
		Both = 3
	}

	public static class ProcessHelpers
	{
		/// <summary>
		/// Converts ProcessPriorityClass to ordered int for programmatic comparison.
		/// </summary>
		/// <returns>0 [Idle] to 4 [High]; defaultl: 2 [Normal]</returns>
		public static int PriorityToInt(ProcessPriorityClass priority)
		{
			switch (priority)
			{
				case ProcessPriorityClass.Idle:
					return 0;
				case ProcessPriorityClass.BelowNormal:
					return 1;
				default:
					return 2; //ProcessPriorityClass.Normal, 2
				case ProcessPriorityClass.AboveNormal:
					return 3;
				case ProcessPriorityClass.High:
					return 4;
			}
		}

		/// <summary>
		/// Converts int to ProcessPriorityClass.
		/// </summary>
		/// <returns>Idle [0] to High [4]; default: Normal [2]</returns>
		/// <param name="priority">0 [Idle] to 4 [High]</param>
		public static ProcessPriorityClass IntToPriority(int priority)
		{
			Debug.Assert(priority >= 0 && priority <= 4);

			switch (priority)
			{
				case 0:
					return ProcessPriorityClass.Idle;
				case 1:
					return ProcessPriorityClass.BelowNormal;
				default:
					return ProcessPriorityClass.Normal;
				case 3:
					return ProcessPriorityClass.AboveNormal;
				case 4:
					return ProcessPriorityClass.High;
			}
		}

		public static ProcessPriorityClass? IntToNullablePriority(int priority)
		{
			if (priority < 0 || priority > 4) return null;
			return IntToPriority(priority);
		}

		/*
		[DllImport("psapi.dll")]
		public static extern uint GetProcessImageFileName(IntPtr hProcess, [Out] System.Text.StringBuilder lpImageFileName, [In] [MarshalAs(UnmanagedType.U4)] int nSize);
		*/
	}

	/// <summary>
	/// Credit goes to Mark Hurd at http://stackoverflow.com/a/16756808/6761963
	/// </summary>
	///
	public static partial class ProcessExtensions
	{
		/// <exception cref="Win32Exception">If system snapshot does not return anything.</exception>
		public static int ParentProcessId(this Process process) => ParentProcessId(process.Id);

		/// <exception cref="Win32Exception">If system snapshot does not return anything.</exception>
		public static int ParentProcessId(int Id)
		{
			Debug.Assert(Id > -1);
			int ptr = 0;
			try
			{
				ptr = NativeMethods.CreateToolhelp32Snapshot(SnapshotFlags.Process, (uint)Id);
				if (ptr == 0)
					return -1;

				var pe32 = new PROCESSENTRY32 { };
				pe32.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));

				if (!NativeMethods.Process32First(ptr, ref pe32))
				{
					var errno = Marshal.GetLastWin32Error();
					if (errno == NativeMethods.ERROR_NO_MORE_FILES)
						return -1;
					throw new Win32Exception(errno);
				}

				do
				{
					if (pe32.th32ProcessID == (uint)Id)
						return (int)pe32.th32ParentProcessID;
				}
				while (NativeMethods.Process32Next(ptr, ref pe32));
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				if (ptr != 0)
					NativeMethods.CloseHandle(ptr);
			}

			return -1;
		}
	}

	static partial class NativeMethods
	{
		internal const int ERROR_NO_MORE_FILES = 0x12;

		[DllImport("kernel32.dll", SetLastError = true)]
		static internal extern int CreateToolhelp32Snapshot(SnapshotFlags dwFlags, uint th32ProcessID);

		/// <summary>
		/// Retrieves information about the first process encountered in a system snapshot.
		/// </summary>
		[DllImport("kernel32.dll")]
		static internal extern bool Process32First(int hSnapshot, ref PROCESSENTRY32 lppe);

		/// <summary>
		/// Retrieves information about the next process recorded in a system snapshot.
		/// </summary>
		[DllImport("kernel32.dll")]
		static internal extern bool Process32Next(int hSnapshot, ref PROCESSENTRY32 lppe);
	}

	[Flags]
	internal enum SnapshotFlags : uint
	{
		HeapList = 0x00000001,
		Process = 0x00000002,
		Thread = 0x00000004,
		Module = 0x00000008,
		Module32 = 0x00000010,
		All = (HeapList | Process | Thread | Module),
		Inherit = 0x80000000,
		NoHeaps = 0x40000000
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct PROCESSENTRY32
	{
		public uint dwSize;
		public uint cntUsage;
		public uint th32ProcessID;
		public IntPtr th32DefaultHeapID;
		public uint th32ModuleID;
		public uint cntThreads;
		public uint th32ParentProcessID;
		public int pcPriClassBase;
		public uint dwFlags;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
		public string szExeFile;
	};
}
