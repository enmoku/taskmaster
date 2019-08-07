//
// Process.ProcessorLoad.cs
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

namespace Taskmaster.Process
{
	public class CpuUsage
	{
		readonly System.Diagnostics.Process prc;

		TimeSpan oldSample;

		readonly Stopwatch timer;

		public CpuUsage(System.Diagnostics.Process process)
		{
			prc = process;
			timer = Stopwatch.StartNew();
			oldSample = prc.TotalProcessorTime;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <returns>CPU usage on scale of 0 to 1.</returns>
		public double Sample()
		{
			var period = timer.ElapsedMilliseconds; // period
			var newTime = prc.TotalProcessorTime;
			timer.Restart();

			var usedMs = (newTime - oldSample).TotalMilliseconds; // used ms in the sample period
			oldSample = newTime; // 

			return usedMs / (period * Environment.ProcessorCount);
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="period">Observation period in milliseconds.</param>
		public double Sample(long period)
		{
			var newSample = prc.TotalProcessorTime;

			var diff = (newSample - oldSample).TotalMilliseconds;
			oldSample = newSample; // 

			return diff / period;
		}
	}
}
