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
	public partial class NativeMethods
	{
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
