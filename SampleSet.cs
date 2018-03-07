//
// SampleSet.cs
//
// Author:
//       M.A. (enmoku) <>
//
// Copyright (c) 2016-2018 M.A. (enmoku)
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

namespace TaskMaster
{
	public class Uptime
	{
		public int Time { get; set; }
		public DateTime Stamp { get; set; }
	}

	public class UptimeTracker
	{
		readonly System.Collections.Queue Stack;

		public int Max { get; set; }
		public int Min { get; set; }

		UptimeTracker(int cap = 10)
		{
			Cap = cap;
			Stack = new System.Collections.Queue(Cap);
		}

		public void Push(int time)
		{
			Count += 1;
			Total += time;
			Stack.Enqueue(new Uptime { Time = time, Stamp = System.DateTime.UtcNow });
		}

		public void Pull()
		{
			//??
		}

		public Uptime Pop()
		{
			Uptime up = (Uptime)Stack.Dequeue();
			Count -= 1;
			Total -= up.Time;
			return up;
		}

		public int Total { get; set; } = 0; // Total value of held items.
		public int Cap { get; set; } = 10; // Start culling
		public int Count { get; set; } = 0; // Items held
	}
}
