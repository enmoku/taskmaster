//
// HumanInterface.cs
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

namespace TaskMaster
{
	public static class HumanInterface
	{
		public static string TimeString(TimeSpan time)
		{
			if (time.TotalMilliseconds <= 0)
				return "n/a";

			var s = new System.Text.StringBuilder();

			bool days = false;
			if (time.Days > 0)
			{
				s.Append(time.Days);
				if (time.Days == 1)
					s.Append(" day");
				else
					s.Append(" days");
				days = true;
			}

			bool hours = false;
			if (time.Hours > 0)
			{
				if (days)
					s.Append(", ");
				s.Append(time.Hours);
				if (time.Hours == 1)
					s.Append(" hour");
				else
					s.Append(" hours");
				hours = true;
			}

			//if (time.Minutes > 0)
			//{
			if (hours || days)
				s.Append(", ");
			double min = time.Minutes + (time.Seconds / 60.0);
			s.Append(string.Format("{0:N1}", min));
			if (min > 1 || min < 1)
				s.Append(" minutes");
			else
				s.Append(" minute");
			//}

			return s.ToString();
		}

		public static string ByteRateString(long bytes)
		{
			var s = new System.Text.StringBuilder();

			const long giga = 1000000000;
			const long mega = 1000000;
			const long kilo = 1000;

			if (bytes > (giga / 10))
			{
				s.Append(string.Format("{0:N3}", bytes / giga));
				s.Append(" G");
			}
			else if (bytes > (mega / 10))
			{
				s.Append(string.Format("{0:N2}", bytes / mega));
				s.Append(" M");
			}
			else if (bytes > (kilo / 10))
			{
				s.Append(string.Format("{0:N1}", bytes / kilo));
				s.Append(" k");
			}
			else
			{
				s.Append(bytes);
				s.Append(" ");
			}
			s.Append("B/s");

			return s.ToString();
		}
	}
}
