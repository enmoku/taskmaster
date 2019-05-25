//
// ProcessorLoad.cs
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

namespace Taskmaster
{
	public class ProcessorLoad
	{
		/// <summary>
		/// Current load, from 0.0f to 100.0f.
		/// </summary>
		public float Current { get; set; } = float.NaN;
		/// <summary>
		/// Averageload, from 0.0f to 100.0f.
		/// </summary>
		public float Mean { get; set; } = float.NaN;
		/// <summary>
		/// Lowest load, from 0.0f to 100.0f.
		/// </summary>
		public float Low { get; set; } = float.NaN;
		/// <summary>
		/// Highest load, from 0.0f to 100.0f.
		/// </summary>
		public float High { get; set; } = float.NaN;

		/// <summary>
		/// Time period for when Low, Average, and High loads were observed.
		/// </summary>
		public TimeSpan Period { get; set; } = TimeSpan.Zero;

		/// <summary>
		/// Thread queue length
		/// </summary>
		public float Queue { get; set; } = float.NaN;
	}

	/// <summary>
	/// Processor load event.
	/// Values are in percentages from 0.0f to 100.0f
	/// </summary>
	public class ProcessorLoadEventArgs : EventArgs
	{
		public ProcessorLoad Load { get; set; } = new ProcessorLoad();
	}
}
