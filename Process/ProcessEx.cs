﻿//
// ProcessEx.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2016–2020 M.A.
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
using System.Globalization;

namespace Taskmaster.Process
{
	public delegate void InfoDelegate(ProcessEx info);

	public class ProcessEx
	{
		public ProcessEx(int pid, DateTimeOffset startTime)
		{
			Start = startTime;
			Id = pid;
		}

		public bool Restricted { get; set; }

		/// <summary>
		/// Process filename without extension
		/// Cached from Process.ProcessFilename
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Cache for Hwnd.
		/// </summary>
		public IntPtr Handle { get; set; } = IntPtr.Zero;

		/// <summary>
		/// Process fullpath, including filename with extension
		/// </summary>
		public string Path { get; set; } = string.Empty;

		/// <summary>
		/// For use by FormatPathName()
		/// </summary>
		public string FormattedPath { get; set; } = string.Empty;

		/// <summary>
		/// As seen in task manager.
		/// </summary>
		public string Description { get; set; } = string.Empty;

		public Stopwatch Timer = new Stopwatch();
		public TimeSpan WMIDelay = TimeSpan.Zero;

		public readonly DateTimeOffset Start;

		public LegacyLevel Legacy { get; set; } = LegacyLevel.Undefined;

		public bool IsUniprocessorOnly { get; set; }

		public bool IsLargeAddressAware { get; set; }

		public bool Is32BitExecutable { get; set; }

		/// <summary>
		/// Process Id.
		/// </summary>
		public int Id { get; set; }

		/// <summary>
		/// Process reference.
		/// </summary>
		public System.Diagnostics.Process? Process { get; set; }

		/// <summary>
		/// Controller associated with this process.
		/// </summary>
		public Process.Controller? Controller { get; set; }

		public bool FullyProtected { get; set; }
		public bool PriorityProtected { get; set; }
		public bool AffinityProtected { get; set; }

		public bool ExitWait { get; set; }

		/// <summary>
		/// Power plan forced, waiting for exit to restore it.
		/// </summary>
		public bool PowerWait { get; set; }

		/// <summary>
		/// This is triggered by foreground transitions.
		/// </summary>
		public bool ForegroundWait { get; set; }

		/// <summary>
		/// Waiting for exit to reset color.
		/// </summary>
		public bool ColorReset { get; set; }

		/// <summary>
		/// Currently in background.
		/// </summary>
		public bool InBackground { get; set; }

		public DateTimeOffset Modified { get; set; } = DateTimeOffset.MinValue;

		private HandlingState i_state { get; set; } = HandlingState.Invalid;

		public HandlingState State
		{
			get => i_state;
			set
			{
				i_state = value;

				switch (value)
				{
					case HandlingState.Exited:
						Exited = true;
						goto doneHandling;
					case HandlingState.Modified:
						Modified = DateTimeOffset.UtcNow;
						goto doneHandling;
					case HandlingState.Unmodified:
					case HandlingState.AccessDenied:
					case HandlingState.Finished:
					case HandlingState.Abandoned:
					case HandlingState.Invalid:
						doneHandling:
						Handled = true;
						Timer.Stop();
						break;
					case HandlingState.Triage:
						Handled = false;
						break;
					default: break;
				}
			}
		}

		public bool Valid { get; set; }

		public bool Handled { get; set; }

		public void HookExit()
		{
			if (!ExitWait)
			{
				ExitWait = true;
				Process.Exited += ExitedEvent;
				Process.EnableRaisingEvents = true;
			}
		}

		void ExitedEvent(object sender, EventArgs e)
		{
			Exited = true;
			State = HandlingState.Exited;
		}

		public bool Exited { get; set; }

		public bool PathSearched { get; set; }

		public DateTime Found { get; set; } = DateTime.UtcNow;

		// internal loaders
		public ProcessLoad? Load;

		/// <summary>
		/// Display: <code>Name #PID</code> or <code>#PID</code>
		/// </summary>
		public override string ToString() => Name + (Name.Length>0?" ":string.Empty) + "#" + Id.ToString(CultureInfo.InvariantCulture);

		public bool IsPathFormatted => FormattedPath.Length > 0;

		public string ToFormattedString() => FormattedPath + (IsPathFormatted ? " " : (Name + (Name.Length > 0 ? " " : string.Empty))) + "#" + Id.ToString(CultureInfo.InvariantCulture);

		/// <summary>
		/// Same as ToString() but prepends controller name.
		/// </summary>
		public string ToFullString() => (Controller != null ? ("[" + Controller.FriendlyName + "] ") : string.Empty) + ToString();

		public string ToFullFormattedString() => (Controller != null ? ("[" + Controller.FriendlyName + "] ") : string.Empty) + ToFormattedString();
	}
}