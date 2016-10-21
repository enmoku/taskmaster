//
// SampleSet.cs
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

namespace TaskMaster
{
	public class UptimeTracker
	{
		readonly System.Collections.Stack stack;
		readonly System.Collections.Stack stamp;

		public int Max { get; set; }
		public int Min { get; set; }

		UptimeTracker(int capacity=10, int cap=10)
		{
			Capacity = capacity;
			stack = new System.Collections.Stack(capacity);
		}

		public void Push(int obj)
		{
			stack.Push(obj);
		}

		public void Pull()
		{
		}

		public int Pop()
		{
			return (int)stack.Pop();
		}

		public int Total { get; set; } = 0;
		public int Cap { get; set; } = 10;
		public int Capacity { get; set; } = 10;
		public int Count { get; set; } = 0;
	}
}
