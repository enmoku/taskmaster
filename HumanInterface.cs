//
// HumanInterface.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2016-2019 M.A.
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

namespace Taskmaster
{
	public static class HumanInterface
	{
		public static string BitMask(long num, int padding)
		{
			var tmp = Convert.ToString(num, 2).PadLeft(padding, '0');
			var arr = tmp.ToCharArray();
			Array.Reverse(arr);
			return new string(arr);
		}

		public static string TimeString(TimeSpan time)
		{
			if (time.TotalMilliseconds <= 0) return HumanReadable.Generic.NotAvailable;

			var s = new System.Text.StringBuilder();

			var days = false;
			if (time.Days > 0)
			{
				s.Append(time.Days);
				if (time.Days == 1) s.Append(" day");
				else s.Append(" days");
				days = true;
			}

			var hours = false;
			if (time.Hours > 0)
			{
				if (days) s.Append(", ");
				s.Append(time.Hours);
				if (time.Hours == 1) s.Append(" hour");
				else s.Append(" hours");
				hours = true;
			}

			if (hours || days)
				s.Append(", ");
			var min = time.Minutes + (time.Seconds / 60.0);
			s.Append($"{min:N1}")
				.Append(" minute");
			if (min > 1 || min < 1) s.Append("s");

			return s.ToString();
		}

		static float SizeThreshold = 1.2f;
		const int Giga = 3;
		const int Mega = 2;
		const int Kilo = 1;
		const int Byte = 0;
		static double[] MultiplierSI = { 1, 1_000, 1_000_000, 1_000_000_000 };
		static double[] MultiplierIEC = { 1, 1_024, 1_048_576, 1_073_741_824 };
		static string[] ByteLetterSI = {"B","kB","MB","GB"};
		static string[] ByteLetterIEC = { "B", "KiB", "MiB", "GiB" };

		static System.Globalization.NumberFormatInfo numberformat = new System.Globalization.NumberFormatInfo() { NumberDecimalDigits = 3 };

		public static string ByteString(long bytes, bool positivesign=false, bool iec=false)
		{
			double div = 1;
			int letter = 0;

			var multiplier = iec ? MultiplierIEC : MultiplierSI;
			var byteletter = iec ? ByteLetterIEC : ByteLetterSI;

			if (Math.Abs(bytes) > (multiplier[Giga] * SizeThreshold))
			{
				div = multiplier[Giga];
				letter = Giga;
			}
			else if (Math.Abs(bytes) > (multiplier[Mega] * SizeThreshold))
			{
				div = multiplier[Mega];
				letter = Mega;
			}
			else if (Math.Abs(bytes) > (Kilo * SizeThreshold))
			{
				div = multiplier[Kilo];
				letter = Kilo;
			}
			else
			{
				div = 1;
				letter = Byte;
			}

			double num = bytes / div;

			lock (numberformat) // lock vs new.. hmm
			{
				// Don't show decimals for bytes, do whatever for the rest.
				numberformat.NumberDecimalDigits = div == 1 ? 0 : ((num < 10) ? 3 : ((num > 100) ? 1 : 2));

				return string.Format(
					numberformat,
					"{1}{0:N} {2}",
					num, ((positivesign && bytes > 0) ? "+" : ""), byteletter[letter]);
			}
		}
	}
}