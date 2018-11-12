﻿//
// ProcessExtensions.cs
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

using System.Diagnostics;
using System.Threading;

namespace Taskmaster
{
	public static class ProcessExtensions
	{
		/// <summary>
		/// Sets the priority based on limitations. Can throw an error if Process.PriorityClass is inaccessible.
		/// </summary>
		/// <exception cref="System.ComponentModel.Win32Exception">Access denied</exception>
		/// <exception cref="System.InvalidOperationException">Process exited</exception>
		public static bool SetLimitedPriority(this Process process, ProcessPriorityClass target, ProcessPriorityStrategy strategy = ProcessPriorityStrategy.Decrease)
		{
			switch (strategy)
			{
				case ProcessPriorityStrategy.Force:
					if (process.PriorityClass.ToInt32() == target.ToInt32())
						return false;
					break;
				case ProcessPriorityStrategy.Increase:
					if (process.PriorityClass.ToInt32() >= target.ToInt32())
						return false;
					break;
				case ProcessPriorityStrategy.Decrease:
					if (process.PriorityClass.ToInt32() <= target.ToInt32())
						return false;
					break;
				case ProcessPriorityStrategy.None:
				default:
					return false;
			}

			process.PriorityClass = target;
			return true;
		}

		public static ThreadPriority ToThreadPriority(this ProcessPriorityClass priority)
		{
			switch (priority)
			{
				case ProcessPriorityClass.AboveNormal:
					return ThreadPriority.AboveNormal;
				case ProcessPriorityClass.BelowNormal:
					return ThreadPriority.BelowNormal;
				case ProcessPriorityClass.High:
					return ThreadPriority.Highest;
				case ProcessPriorityClass.Idle:
					return ThreadPriority.Lowest;
				default:
					return ThreadPriority.Normal;
			}
		}
	}
}
