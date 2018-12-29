//
// TypeExtensions.cs
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
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;

namespace Taskmaster
{
	static public class CoreTypeExtensions
	{
		// Core Type extension
		/// <summary>
		/// int.Constrain(minimum, maximum)
		/// </summary>
		public static int Constrain(this int value, int InclusiveMinimum, int InclusiveMaximum)
		{
			return Math.Min(Math.Max(value, InclusiveMinimum), InclusiveMaximum);
		}

		public static long Constrain(this long value, long InclusiveMinimum, long InclusiveMaximum)
		{
			return Math.Min(Math.Max(value, InclusiveMinimum), InclusiveMaximum);
		}

		public static float Constrain(this float value, float InclusiveMinimum, float InclusiveMaximum)
		{
			return Math.Min(Math.Max(value, InclusiveMinimum), InclusiveMaximum);
		}

		public static double Constrain(this double value, double InclusiveMinimum, double InclusiveMaximum)
		{
			return Math.Min(Math.Max(value, InclusiveMinimum), InclusiveMaximum);
		}

		public static decimal Constrain(this decimal value, decimal InclusiveMinimum, decimal InclusiveMaximum)
		{
			return Math.Min(Math.Max(value, InclusiveMinimum), InclusiveMaximum);
		}

		/// <summary>
		/// Constrains maximum value.
		/// </summary>
		public static int Max(this int value, int max)
		{
			return Math.Min(value, max);
		}

		/// <summary>
		/// Constrains minimum value.
		/// </summary>
		public static int Min(this int value, int min)
		{
			return Math.Max(value, min);
		}

		/// <summary>
		/// Replace value with another
		/// </summary>
		public static int Replace(this int value, int from, int to)
		{
			return value == from ? to : value;
		}

		public static IPAddress GetAddress(this NetworkInterface iface)
		{
			Debug.Assert(iface != null);

			return GetAddresses(iface)[0] ?? IPAddress.None;
		}

		public static IPAddress[] GetAddresses(this NetworkInterface iface)
		{
			Debug.Assert(iface != null);

			if (iface.NetworkInterfaceType == NetworkInterfaceType.Loopback || iface.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
				return null;

			var ipa = new List<IPAddress>(2);
			foreach (UnicastIPAddressInformation ip in iface.GetIPProperties().UnicastAddresses)
				ipa.Add(ip.Address);


			return ipa.ToArray();
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
		static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

		public static long Unixstamp(this DateTime dt) => Convert.ToInt64(dt.ToUniversalTime().Subtract(UnixEpoch).TotalSeconds);

		public static DateTime Unixstamp(this long unixstamp) => (UnixEpoch + TimeSpan.FromSeconds(unixstamp));

		/// <summary>
		/// Time since, because (now - since) just confuses me.
		/// </summary>
		public static TimeSpan TimeSince(this DateTime now, DateTime since) => (now - since);

		public static TimeSpan TimeTo(this DateTime now, DateTime to) => (to - now);
	}
}