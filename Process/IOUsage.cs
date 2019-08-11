//
// Process.IOUsage.cs
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
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Taskmaster.Process
{
	public class IOUsage
	{
		readonly System.Diagnostics.Process prc;

		readonly Stopwatch timer = new Stopwatch();

		float oldIO = 0f;

		public IOUsage(System.Diagnostics.Process process) => prc = process;

		NativeMethods.IO_COUNTERS counters = new NativeMethods.IO_COUNTERS();

		/// <summary>
		/// 
		/// </summary>
		/// <returns>IO kilobytes.</returns>
		public float Sample()
		{
			var period = timer.ElapsedMilliseconds; // period

			NativeMethods.GetProcessIoCounters(prc.Handle, out counters);
			timer.Restart();

			float newIO = (counters.OtherTransferCount + counters.ReadTransferCount + counters.WriteTransferCount) / 1_024f; // kiB
			float diff = (newIO - oldIO) / period;
			oldIO = newIO;
			return diff;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="period">Observation period in milliseconds.</param>
		public float Sample(long period)
		{
			NativeMethods.GetProcessIoCounters(prc.Handle, out counters);
			float newIO = (counters.OtherTransferCount + counters.ReadTransferCount + counters.WriteTransferCount) / 1_024f; // kiB
			float diff = (newIO - oldIO) / period;
			oldIO = newIO;
			return diff;
		}
	}
}
