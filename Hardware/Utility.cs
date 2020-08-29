//
// Hardwar.Utility.cs
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

namespace Taskmaster.Hardware
{
	public static class Utility
	{
		/// <summary>
		/// Actual number of processor cores. You still want to use Environment.ProcessorCount when the accessible number (to .NET) matters.
		/// </summary>
		public static int ProcessorCount { get; private set; } = 0;

		static Utility()
		{
			Process.NativeMethods.GetSystemInfo(out var sysinfo);
			var pcores = Convert.ToInt32(sysinfo.dwNumberOfProcessors);
			var ecores = Environment.ProcessorCount;

			ProcessorCount = pcores;

			/*
			try
			{
				uint tCount = 0;
				// Because Environment.ProcessorCount sometimes gives random numbers less than the actual core count.
				foreach (var processor in new System.Management.ManagementObjectSearcher("Select * from Win32_Processor").Get())
				{
					tCount += (UInt32)processor["NumberOfCores"];
					//ProcessorCount += int.Parse(processor["NumberOfCores"].ToString());
				}
				ProcessorCount = Convert.ToInt32(tCount);
			}
			catch (System.Runtime.InteropServices.COMException e)
			{
				Logging.DebugMsg("<Hardware> Utility initialization failed due to COM error: " + e.Message);
				throw;
			}
			*/

			Logging.DebugMsg("<Hardware> Processor count: " + ProcessorCount.ToString(CultureInfo.InvariantCulture));

			if (ecores != pcores)
				Logging.DebugMsg(".NET and W32API disagree on number of cores: " + ecores.ToString(CultureInfo.InvariantCulture) + " != " + pcores.ToString(CultureInfo.InvariantCulture));
			if (ecores != ProcessorCount)
				Logging.DebugMsg(".NET and WMI disagree on number of cores: " + ecores.ToString(CultureInfo.InvariantCulture) + " != " + ProcessorCount.ToString(CultureInfo.InvariantCulture));
		}
	}
}
