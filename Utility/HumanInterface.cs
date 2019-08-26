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
using System.Text;

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

		public static string PureTimeString(TimeSpan time)
			=> $"{time.Days}:{time.Hours:00}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}";

		public static string TimeString(TimeSpan time)
		{
			if (time.TotalMilliseconds <= 0) return HumanReadable.Generic.NotAvailable;

			var sbs = new StringBuilder(64);

			var days = false;
			if (time.Days > 0)
			{
				sbs.Append(time.Days).Append(time.Days == 1 ? " day" : " days");
				days = true;
			}

			var hours = false;
			if (time.Hours > 0)
			{
				if (days) sbs.Append(", ");
				sbs.Append(time.Hours).Append(time.Hours == 1 ? " hour" : " hours");
				hours = true;
			}

			if (hours || days)
				sbs.Append(", ");

			var min = time.Minutes + (time.Seconds / 60.0);
			sbs.AppendFormat("{0:N1}", min).Append(" minute");
			if (min > 1 || min < 1) sbs.Append('s');

			return sbs.ToString();
		}

		const float SizeThreshold = 1.2f;
		const int Giga = 3;
		const int Mega = 2;
		const int Kilo = 1;
		const int Byte = 0;
		readonly static double[] MultiplierSI = { 1, 1_000, 1_000_000, 1_000_000_000 };
		readonly static double[] MultiplierIEC = { 1, 1_024, 1_048_576, 1_073_741_824 };
		readonly static string[] ByteLetterSI = { "B", "kB", "MB", "GB" };
		readonly static string[] ByteLetterIEC = { "B", "kiB", "MiB", "GiB" };

		readonly static System.Globalization.NumberFormatInfo[] DecimalFormatting = {
			new System.Globalization.NumberFormatInfo() { NumberDecimalDigits = 0 },
			new System.Globalization.NumberFormatInfo() { NumberDecimalDigits = 1 },
			new System.Globalization.NumberFormatInfo() { NumberDecimalDigits = 2 },
			//new System.Globalization.NumberFormatInfo() { NumberDecimalDigits = 3 },
		};

		/// <summary>
		/// Turns bytes into more human readable values. E.g. 17534252 into 17.53 MiB.
		/// </summary>
		/// <param name="bytes"></param>
		/// <param name="positivesign"></param>
		/// <param name="iec">Use IEC's 1024 multiplier instead of SI's 1000.</param>
		/// <returns></returns>
		public static string ByteString(long bytes, bool positivesign = false, bool iec = false)
		{
			int scale;

			var multiplier = iec ? MultiplierIEC : MultiplierSI;

			long absbytes = Math.Abs(bytes);

			double div;
			if (absbytes > ((div = multiplier[Giga]) * SizeThreshold))
				scale = Giga;
			else if (absbytes > ((div = multiplier[Mega]) * SizeThreshold))
				scale = Mega;
			else if (absbytes > ((div = multiplier[Kilo]) * SizeThreshold))
				scale = Kilo;
			else
				div = multiplier[scale = Byte];

			double num = bytes / div;
			//int decimalSpot = div == 1 ? 0 : ((num < 10) ? 3 : ((num > 100) ? 1 : 2));
			//int decimalSpot = div == 1 ? 0 : ((num > 100) ? 1 : 2);

			var format = DecimalFormatting[div == 1 ? 0 : ((num > 100) ? 1 : 2)];

			return (positivesign ? format.PositiveSign : string.Empty) + num.ToString("N", format) + " " + (iec ? ByteLetterIEC[scale] : ByteLetterSI[scale]);
		}
	}
}