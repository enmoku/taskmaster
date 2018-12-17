//
// UserActivity.cs
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

using System;
using System.Runtime.InteropServices;

namespace MKAh
{
	public static class User
	{
		public static double UserIdleTime()
		{
			var lastact = LastActive();
			return TicksToSeconds(lastact);
		}

		/// <summary>
		/// Pass this to IdleFor(uint).
		/// </summary>
		/// <returns>Ticks since boot.</returns>
		public static uint LastActive()
		{
			var info = new LASTINPUTINFO();
			info.cbSize = (uint)Marshal.SizeOf(info);
			info.dwTime = 0;
			bool rv = GetLastInputInfo(ref info);
			if (rv) return info.dwTime;

			return uint.MinValue;
		}

		/// <summary>
		/// Should be called in same thread as LastActive. Odd behaviour expected if the code runs on different core.
		/// </summary>
		/// <param name="lastActive">Last active time, as returned by LastActive</param>
		/// <returns>Seconds for how long user has been idle</returns>
		public static double TicksToSeconds(uint lastActive)
		{
			double eticks = Convert.ToDouble(Environment.TickCount);
			double uticks = Convert.ToDouble(lastActive);
			return (eticks - uticks) / 1000f;
		}

		[DllImport("user32.dll")]
		internal static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

		[StructLayout(LayoutKind.Sequential)]
		internal struct LASTINPUTINFO
		{
			public static readonly int SizeOf = Marshal.SizeOf(typeof(LASTINPUTINFO));

			[MarshalAs(UnmanagedType.U4)]
			public UInt32 cbSize;
			[MarshalAs(UnmanagedType.U4)]
			public UInt32 dwTime;
		}
	}
}
