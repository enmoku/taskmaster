//
// MKAh.Atomic.Lock.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2018–2019 M.A.
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
using System.Runtime.CompilerServices;

namespace MKAh
{
	/// <summary>
	/// Simplified wrappers for System.Threading.Interlocked stuff.
	/// </summary>
	public static partial class Atomic
	{
		public const int Locked = 1;
		public const int Unlocked = 0;

		/// <summary>
		/// Lock the specified lockvalue.
		/// Performs simple check and set swap of 0 and 1.
		/// 0 is unlocked, 1 is locked.
		/// Simplifies basic use of System.Threading.Interlocked.CompareExchange
		/// </summary>
		/// <returns>If lock was successfully acquired.</returns>
		/// <param name="lockvalue">Variable used as the lock.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool Lock(ref int lockvalue)
		{
			Debug.Assert(lockvalue == Locked || lockvalue == Unlocked);
			return (System.Threading.Interlocked.CompareExchange(ref lockvalue, Locked, Unlocked) == Unlocked);
		}

		/// <summary>
		/// Release the lock.
		/// </summary>
		/// <param name="lockvalue">Variable used as the lock.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Unlock(ref int lockvalue)
		{
			Debug.Assert(lockvalue != Unlocked);
			lockvalue = Unlocked;
		}
	}

	public class NonBlockingLock
	{
		int internal_lock = Atomic.Unlocked;

		NonBlockingLock(bool locked=false) => internal_lock = locked ? Atomic.Locked : Atomic.Unlocked;

		public bool Locked => internal_lock == Atomic.Locked;

		public bool Lock() => Atomic.Lock(ref internal_lock);

		public void Unlock() => Atomic.Unlock(ref internal_lock);
	}

	public class AutoUnlocker : IDisposable
	{
		readonly NonBlockingLock internal_lock = null;

		AutoUnlocker(NonBlockingLock nblock)
		{
			if (nblock == null) throw new ArgumentNullException(nameof(nblock));
			if (!nblock.Lock()) throw new ArgumentException("Already locked");

			internal_lock = nblock;
		}

		#region IDisposable Support
		private bool disposed = false; // To detect redundant calls

		protected virtual void Dispose(bool disposing)
		{
			if (disposed) return;

			internal_lock?.Unlock();

			disposed = true;
		}

		public void Dispose() => Dispose(true);
		#endregion
	}
}