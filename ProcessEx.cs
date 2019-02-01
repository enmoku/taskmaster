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
using System.Diagnostics;

namespace Taskmaster
{
	sealed public class ProcessEx
	{
		/// <summary>
		/// Process filename without extension
		/// Cached from Process.ProcessFilename
		/// </summary>
		public string Name { get; set; } = string.Empty;

		/// <summary>
		/// Process fullpath, including filename with extension
		/// </summary>
		public string Path { get; set; } = string.Empty;

		/// <summary>
		/// For use by FormatPathName()
		/// </summary>
		public string FormattedPath { get; set; } = null;

        public Stopwatch Timer = null;
        public int WMIDelay = 0;

		/// <summary>
		/// Process Id.
		/// </summary>
		public int Id { get; set; } = -1;

		/// <summary>
		/// Process reference.
		/// </summary>
		public Process Process { get; set; } = null;

		/// <summary>
		/// Controller associated with this process.
		/// </summary>
		public ProcessController Controller { get; set; } = null;

		public bool PowerWait { get; set; } = false;
		public bool ActiveWait { get; set; } = false;

		public DateTimeOffset Modified { get; set; } = DateTimeOffset.MinValue;
		internal ProcessHandlingState _state = ProcessHandlingState.Invalid;
		public ProcessHandlingState State
		{
			get => _state;
			set
			{
				_state = value;

				switch (value)
				{
					case ProcessHandlingState.Exited:
						Exited = true;
                        goto handled;
					case ProcessHandlingState.Modified:
						Modified = DateTimeOffset.UtcNow;
						goto handled;
					case ProcessHandlingState.Unmodified:
                    case ProcessHandlingState.AccessDenied:
                    case ProcessHandlingState.Finished:
					case ProcessHandlingState.Abandoned:
					case ProcessHandlingState.Invalid:
                    handled:
                        Handled = true;
                        Timer?.Stop();
						break;
				}
			}
		}

		public bool Valid { get; set; } = false;
		public bool Handled { get; set; } = false;
		public bool Exited { get; set; } = false;
	}
}
