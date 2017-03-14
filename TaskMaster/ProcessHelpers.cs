//
// ProcessHelpers.cs
//
// Author:
//       M.A. (enmoku) <>
//
// Copyright (c) 2016 M.A. (enmoku)
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
using System.Diagnostics; // Process
using System.Runtime.InteropServices; // Marshal
using System.Runtime.ConstrainedExecution; // attributes
using System.ComponentModel; // Win32Exception
using System.Security; // SuppressUnmanagedCodeSecurity
using System.Security.Permissions; // HostProtection
using Microsoft.Win32.SafeHandles; // SafeHandleMinusOneIsInvalid

namespace TaskMaster
{
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
				case ProcessPriorityClass.Idle: return 0;
				case ProcessPriorityClass.BelowNormal: return 1;
				default: return 2; //ProcessPriorityClass.Normal, 2
				case ProcessPriorityClass.AboveNormal: return 3;
				case ProcessPriorityClass.High: return 4;
			}
		}

		/// <summary>
		/// Converts int to ProcessPriorityClass.
		/// </summary>
		/// <returns>Idle [0] to High [4]; default: Normal [2]</returns>
		/// <param name="priority">0 [Idle] to 4 [High]</param>
		public static ProcessPriorityClass IntToPriority(int priority)
		{
			switch (priority)
			{
				case 0: return ProcessPriorityClass.Idle;
				case 1: return ProcessPriorityClass.BelowNormal;
				default: return ProcessPriorityClass.Normal;
				case 3: return ProcessPriorityClass.AboveNormal;
				case 4: return ProcessPriorityClass.High;
			}
		}

		/*
		[DllImport("psapi.dll")]
		public static extern uint GetProcessImageFileName(IntPtr hProcess, [Out] System.Text.StringBuilder lpImageFileName, [In] [MarshalAs(UnmanagedType.U4)] int nSize);
		*/
	}
}

/// <summary>
/// Credit goes to Mark Hurd at http://stackoverflow.com/a/16756808/6761963
/// </summary>

public static class ProcessExtensions
{
	public static int ParentProcessId(this Process process)
	{
		return ParentProcessId(process.Id);
	}

	public static int ParentProcessId(int Id)
	{
		var pe32 = new PROCESSENTRY32 { };
		pe32.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));
		using (var hSnapshot = CreateToolhelp32Snapshot(SnapshotFlags.Process, (uint)Id))
		{
			if (hSnapshot.IsInvalid)
				throw new Win32Exception();

			if (!Process32First(hSnapshot, ref pe32))
			{
				int errno = Marshal.GetLastWin32Error();
				if (errno == ERROR_NO_MORE_FILES)
					return -1;
				throw new Win32Exception(errno);
			}

			do
			{
				if (pe32.th32ProcessID == (uint)Id)
					return (int)pe32.th32ParentProcessID;
			}
			while (Process32Next(hSnapshot, ref pe32));
		}
		return -1;
	}

	const int ERROR_NO_MORE_FILES = 0x12;
	[DllImport("kernel32.dll", SetLastError = true)]
	static extern SafeSnapshotHandle CreateToolhelp32Snapshot(SnapshotFlags flags, uint id);
	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool Process32First(SafeSnapshotHandle hSnapshot, ref PROCESSENTRY32 lppe);
	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool Process32Next(SafeSnapshotHandle hSnapshot, ref PROCESSENTRY32 lppe);

	[Flags]
	enum SnapshotFlags : uint
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
	struct PROCESSENTRY32
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

	[SuppressUnmanagedCodeSecurity, HostProtection(SecurityAction.LinkDemand, MayLeakOnAbort = true)]
	internal sealed class SafeSnapshotHandle : SafeHandleMinusOneIsInvalid
	{
		internal SafeSnapshotHandle() : base(true)
		{
		}

		[SecurityPermission(SecurityAction.LinkDemand, UnmanagedCode = true)]
		internal SafeSnapshotHandle(IntPtr handle) : base(true)
		{
			SetHandle(handle);
		}

		protected override bool ReleaseHandle()
		{
			return CloseHandle(handle);
		}

		[ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success), DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true, ExactSpelling = true)]
		static extern bool CloseHandle(IntPtr handle);
	}
}