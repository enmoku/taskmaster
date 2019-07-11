//
// MKAh.Lock.Scoping.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2019 M.A.
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

namespace MKAh.Lock
{
	public class Monitor
	{
		readonly object _Lock = new object();

		public Monitor()
		{
			// NOP
		}

		public int Queue { get; private set; } = 0;

		public bool Waiting => Queue > 0;

		public bool TryLock() => System.Threading.Monitor.TryEnter(_Lock);

		/// <summary>
		/// Acquire lock.
		/// </summary>
		/// <returns>Number of items waiting for the lock after this.</returns>
		public int Lock()
		{
			Queue++;
			System.Threading.Monitor.Enter(_Lock);
			Queue--;
			if (Disposed) throw new ObjectDisposedException(nameof(Monitor), "Lock entered after dispose");
			return Queue;
		}

		/// <summary>
		/// Wait for the lock to be released.
		/// </summary>
		public void Wait()
		{
			using var tlock = ScopedLock();
		}

		public MonitorScope ScopedLock()
		{
			Lock();

			return new MonitorScope(this);
		}

		public MonitorScope TryScopedLock()
		{
			if (TryLock())
				return new MonitorScope(this);

			return null;
		}

		public void Unlock()
		{
			if (Disposed) throw new ObjectDisposedException(nameof(Monitor), "Lock exited after dispose");

			System.Threading.Monitor.Exit(_Lock);
		}

		#region IDisposable Support
		bool Disposed = false;

		protected virtual void Dispose(bool disposing)
		{
			if (Disposed) return;

			Unlock();

			Disposed = true;
		}

		public void Dispose() => Dispose(true);
		#endregion
	}

	public class MonitorScope : IDisposable
	{
		readonly Monitor Monitor;

		public MonitorScope(Monitor monitor) => Monitor = monitor;

		/// <summary>
		/// How many things are waiting for this lock.
		/// </summary>
		public int Queue => Monitor.Queue;

		/// <summary>
		/// There's things waiting for this lock.
		/// </summary>
		public bool Waiting => Monitor.Waiting;

		#region IDisposable Support
		bool Disposed = false; // To detect redundant calls

		void Dispose(bool disposing)
		{
			if (Disposed) return;

			if (disposing)
				Monitor.Unlock();

			Disposed = true;
		}

		public void Dispose() => Dispose(true);
		#endregion
	}
}
