//
// TypeExtensions.cs
//
// Author:
//       M.A. (enmoku) <>
//
// Copyright (c) 2018 M.A. (enmoku)
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
namespace TaskMaster
{
	static public class IntExtensions
	{
		public static int Constrain(this int i, int min, int max)
		{
			return (i < min) ? min : ((i > max) ? max : i);
		}
	}

	static public class PriorityClassExtensions
	{
		public static int ToInt32(this ProcessPriorityClass pc)
		{
			switch (pc)
			{
				case ProcessPriorityClass.Idle:
					return 0;
				case ProcessPriorityClass.BelowNormal:
					return 1;
				default:
				case ProcessPriorityClass.Normal:
					return 2;
				case ProcessPriorityClass.AboveNormal:
					return 3;
				case ProcessPriorityClass.High:
					return 4;
			}
		}
	}

	static public class DateExtensions
	{
		static DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

		public static long Unixstamp(this DateTime dt)
		{
			return Convert.ToInt64(dt.ToUniversalTime().Subtract(UnixEpoch).TotalSeconds);
		}

		public static DateTime Unixstamp(this long stamp)
		{
			return (UnixEpoch + TimeSpan.FromSeconds(stamp));
		}
	}
}
