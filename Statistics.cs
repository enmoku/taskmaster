//
// Statistics.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2016-2018 M.A.
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

namespace Taskmaster
{
	public static class Statistics
	{
		public static int ParentSeeks { get; set; } = 0;
		public static double Parentseektime { get; set; } = 0;

		public static int WMIqueries { get; set; } = 0;
		public static double WMIquerytime { get; set; } = 0;

		public static ulong WMIPolling { get; set; } = 0;
		public static double WMIPollTime { get; set; } = 0;

		public static ulong MaintenanceCount { get; set; } = 0;
		public static double MaintenanceTime { get; set; } = 0;

		public static ulong PathCacheCurrent { get; set; } = 0;
		public static ulong PathCachePeak { get; set; } = 0;
		public static ulong PathCacheHits { get; set; } = 0;
		public static ulong PathCacheMisses { get; set; } = 0;

		public static ulong TouchCount { get; set; } = 0;
		public static ulong TouchIgnore { get; set; } = 0;

		public static ulong TouchTime { get; set; } = 50L; // double the expected average
		public static ulong TouchTimeLongest { get; set; } = 0L;
		public static ulong TouchTimeShortest { get; set; } = ulong.MaxValue;

		public static uint FatalErrors { get; set; } = 0;

		public static ulong PathFindAttempts { get; set; } = 0;
		public static ulong PathFindViaModule { get; set; } = 0;
		public static ulong PathFindViaC { get; set; } = 0;
		public static ulong PathFindViaWMI { get; set; } = 0;
		public static ulong PathNotFound { get; set; } = 0;
	}
}