//
// Utility.cs
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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;

namespace TaskMaster
{
	public enum Trinary
	{
		False = 0,
		True = 1,
		Nonce = -1,
	}

	public static class TrinaryExtensions
	{
		public static bool True(this Trinary tri)
		{
			return (tri == Trinary.True);
		}
		public static bool False(this Trinary tri)
		{
			return (tri == Trinary.False);
		}
		public static bool Nonce(this Trinary tri)
		{
			return (tri == Trinary.Nonce);
		}
	}

	public enum OpStatus
	{
		Done,
		Retry,
		NoRetry,
		Fail
	}

	public enum Timescale
	{
		Seconds,
		Minutes,
		Hours,
	}

	public static class Utility
	{
		public static string TimescaleString(Timescale t)
		{
			switch (t)
			{
				case Timescale.Seconds:
					return "second(s)";
				case Timescale.Minutes:
					return "minute(s)";
				case Timescale.Hours:
					return "hour(s)";
			}
			return null;
		}

		public static double SimpleTime(double seconds, out Timescale scale)
		{
			Debug.Assert(seconds >= 0);

			if (seconds > (120.0 * 60.0))
			{
				double hours = seconds / 60.0 / 60.0;
				scale = Timescale.Hours;
				return hours;
			}
			else if (seconds > 120.0)
			{
				double minutes = seconds / 60.0;
				scale = Timescale.Minutes;
				return minutes;
			}
			else
			{
				scale = Timescale.Seconds;
				return seconds;
			}
		}

		public static string ByterateString(long bps)
		{
			Debug.Assert(bps >= 0);

			if (bps >= 1000000000d)
				return Math.Round(bps * 0.000000001d, 2) + " Gb/s";
			if (bps >= 1000000d)
				return Math.Round(bps * 0.000001d, 2) + " Mb/s";
			if (bps >= 1000d)
				return Math.Round(bps * 0.001d, 2) + " kb/s";
			return bps + " b/s";
		}
	}

	public static class Logging
	{
		public static void Log(string text, [CallerFilePath] string file = "", [CallerMemberName] string member = "", [CallerLineNumber] int line = 0)
		{
			Console.WriteLine("{0}_{1}({2}): {3}", System.IO.Path.GetFileName(file), member, line, text);
		}
	}

	public static class TypeExtensions
	{
		// Core Type extensions
		public static int Limit(this int value, int InclusiveMinimum, int InclusiveMaximum)
		{
			if (value < InclusiveMinimum) { return InclusiveMinimum; }
			if (value > InclusiveMaximum) { return InclusiveMaximum; }
			return value;
		}

		public static int Min(this int value, int Minimum)
		{
			if (value < Minimum) { return Minimum; }
			return value;
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
			{
				ipa.Add(ip.Address);
			}

			return ipa.ToArray();
		}
	}
}
