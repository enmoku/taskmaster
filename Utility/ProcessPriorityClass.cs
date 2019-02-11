//
// ProcessPriorityClass.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2018 M.A.
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

namespace MKAh
{
	public static partial class Readable
	{
		public static string ProcessPriority(ProcessPriorityClass priority)
		{
			switch (priority)
			{
				case ProcessPriorityClass.RealTime: return "Real Time";
				case ProcessPriorityClass.High: return "High";
				case ProcessPriorityClass.AboveNormal: return "Above Normal";
				default:
				case ProcessPriorityClass.Normal: return "Normal";
				case ProcessPriorityClass.BelowNormal: return "Below Normal";
				case ProcessPriorityClass.Idle: return "Low"; // Idle is true only on pre-W2k
			}
		}
	}

	public static partial class Utility
	{
		public static ProcessPriorityClass ProcessPriority(string priority)
		{
			switch (priority.ToLowerInvariant())
			{
				case "real time": return ProcessPriorityClass.RealTime;
				case "high": return ProcessPriorityClass.High;
				case "above normal": return ProcessPriorityClass.AboveNormal;
				default:
				case "normal": return ProcessPriorityClass.Normal;
				case "below normal": return ProcessPriorityClass.BelowNormal;
				case "idle":
				case "low":return ProcessPriorityClass.Idle;
			}
		}
	}
}
