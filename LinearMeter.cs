//
// LinearMeter.cs
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Taskmaster
{
	// badly named class
	public class LinearMeter
	{
		public long Peak { get; set; } = long.MaxValue;
		public long Level { get; set; } = 0;

		public LinearMeter(long peak, long initial=0)
		{
			Peak = peak;
			Level = initial;
		}

		// pumps the internal value, returns if 
		/// <summary>
		/// Pumps the meter up.
		/// </summary>
		/// <returns>True if peaked, false if not</returns>
		public bool Pump(long amount=1)
		{
			if (Level < Peak)
			{
				Level += amount;
				if (Level > Peak)
					Level = Peak;

				return true;
			}

			return false;
		}

		public bool IsBrimming
		{
			get
			{
				return (Level == Peak);
			}
		}

		public bool IsEmpty
		{
			get
			{
				return (Level == 0);
			}
		}

		public bool IsEmptyOrBrimming
		{
			get
			{
				return (IsEmpty || IsBrimming);
			}
		}

		public void Leak(long amount=1)
		{
			if (Level > 0)
			{
				Level -= amount;
				if (Level < 0)
					Level = 0;
			}
		}

		public void Empty()
		{
			Level = 0;
		}
	}
}
