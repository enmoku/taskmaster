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

namespace Taskmaster
{
	// badly named class
	public class LinearMeter
	{
		public long Peak { get; set; } = long.MaxValue;
		public long Level { get; set; } = 0;

		/// <summary>
		/// Returns true if the meter has peaked but has not zeroed since.
		/// </summary>
		public bool Peaked { get; private set; } = false;

		public LinearMeter(long peak, long initial=0)
		{
			Peak = peak;
			Level = initial;
			if (Level >= Peak)
			{
				Level = Peak;
				Peaked = true;
			}
		}

		public bool IsPeaked => Level == Peak;
		public bool IsEmpty => Level == 0;
		public bool IsEmptyOrPeaked => (IsEmpty || IsPeaked);

		// pumps the internal value, returns if
		/// <summary>
		/// Pumps the meter up.
		/// </summary>
		/// <returns>True if peaked, false if not</returns>
		public bool Pump(long amount=1)
		{
			bool pumped = ((amount > 0) && (Level < Peak));

			if (pumped)
			{
				Level += amount;

				if (Level >= Peak)
				{
					Level = Peak;
					Peaked = true;
				}
			}

			return pumped;
		}

		/// <summary>
		/// Reduce level only if we've peaked.
		/// </summary>
		public bool Drain(long amount=1)
		{
			bool drained = false;

			if (Peaked)
			{
				drained = Leak(amount);
			}

			return drained;
		}

		/// <summary>
		/// Reduce level by specified amount.
		/// </summary>
		/// <param name="amount"></param>
		/// <returns></returns>
		public bool Leak(long amount=1)
		{
			bool leaked = Level > 0;

			if (leaked)
			{
				Level -= amount;
				if (Level <= 0)
				{
					Level = 0;
					Peaked = false;
				}
			}

			return leaked;
		}

		public void Empty()
		{
			Level = 0;
			Peaked = false;
		}
	}
}
