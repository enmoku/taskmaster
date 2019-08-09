//
// Process.Extensions.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2016 M.A.
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

using MKAh;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace Taskmaster
{
	public static partial class ProcessExtensions
	{
		/// <summary>
		/// Sets the priority based on limitations. Can throw an error if Process.PriorityClass is inaccessible.
		/// </summary>
		/// <exception cref="System.ComponentModel.Win32Exception">Access denied</exception>
		/// <exception cref="System.InvalidOperationException">Process exited</exception>
		public static bool SetLimitedPriority(this System.Diagnostics.Process process, ProcessPriorityClass target, Process.PriorityStrategy strategy = Process.PriorityStrategy.Decrease)
		{
			switch (strategy)
			{
				case Process.PriorityStrategy.Force:
					if (process.PriorityClass.ToInt32() == target.ToInt32())
						return false;
					break;
				case Process.PriorityStrategy.Increase:
					if (process.PriorityClass.ToInt32() >= target.ToInt32())
						return false;
					break;
				case Process.PriorityStrategy.Decrease:
					if (process.PriorityClass.ToInt32() <= target.ToInt32())
						return false;
					break;
				case Process.PriorityStrategy.None:
				default:
					return false;
			}

			process.PriorityClass = target;
			return true;
		}

		public static ThreadPriority ToThreadPriority(this ProcessPriorityClass priority)
			=> priority switch
			{
				ProcessPriorityClass.AboveNormal => ThreadPriority.AboveNormal,
				ProcessPriorityClass.BelowNormal => ThreadPriority.BelowNormal,
				ProcessPriorityClass.High => ThreadPriority.Highest,
				ProcessPriorityClass.Idle => ThreadPriority.Lowest,
				_ => ThreadPriority.Normal,
			};
	}

	public static partial class TypeExtensions
	{
		public static StringBuilder Append(this StringBuilder sbs, Process.ProcessEx info)
			=> sbs.Append(info.Name).Append(" #").Append(info.Id);
	}
}
