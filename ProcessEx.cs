//
// ProcessEx.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2016–2019 M.A.
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
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Taskmaster
{
	sealed public class ProcessEx : IDisposable
	{
		/// <summary>
		/// Process filename without extension
		/// Cached from Process.ProcessFilename
		/// </summary>
		public string Name = string.Empty;
		/// <summary>
		/// Process fullpath, including filename with extension
		/// </summary>
		public string Path = string.Empty;
		/// <summary>
		/// Process Id.
		/// </summary>
		public int Id = -1;

		public Stopwatch Timer;

		/// <summary>
		/// Process reference.
		/// </summary>
		public Process Process = null;
		/// <summary>
		/// .Process.Refresh() should be called for this if the Process needs to be accessed.
		/// </summary>
		public bool NeedsRefresh = false;

		/// <summary>
		/// Has this Process been handled already?
		/// </summary>
		public bool Handled = false;
		/// <summary>
		/// Path was matched with a rule.
		/// </summary>
		public bool PathMatched = false;

		public bool PowerWait = false;
		public bool ActiveWait = false;

		public DateTimeOffset Modified = DateTimeOffset.MinValue;
		public ProcessModification State = ProcessModification.Invalid;

		#region IDisposable Support
		private bool disposed = false; // To detect redundant calls

		void Dispose(bool disposing)
		{
			if (!disposed)
			{
				if (disposing)
				{
					Process?.Dispose();
					Process = null;
					Timer?.Stop();
					Timer = null;
				}

				disposed = true;
			}
		}

		public void Dispose()
		{
			Dispose(true);
		}
		#endregion
	}
}
