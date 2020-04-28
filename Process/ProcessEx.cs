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

		public bool Restricted { get; set; } = false;

		/// <summary>
		/// Process filename without extension
		/// Cached from Process.ProcessFilename
		/// </summary>
		public string Name { get; set; } = string.Empty;

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

		public bool IsUniprocessorOnly { get; set; } = false;

		public bool IsLargeAddressAware { get; set; } = false;

		public bool Is32BitExecutable { get; set; } = false;

		/// <summary>
		/// Process Id.
		/// </summary>
		public int Id { get; set; }

		/// <summary>
		/// Process reference.
		/// </summary>
		public System.Diagnostics.Process? Process { get; set; } = null;

		/// <summary>
		/// Controller associated with this process.
		/// </summary>
		public Process.Controller? Controller { get; set; } = null;

		public bool FullyProtected { get; set; } = false;
		public bool PriorityProtected { get; set; } = false;
		public bool AffinityProtected { get; set; } = false;

		public bool ExitWait { get; set; } = false;

		/// <summary>
		/// Power plan forced, waiting for exit to restore it.
		/// </summary>
		public bool PowerWait { get; set; } = false;

		/// <summary>
		/// This is triggered by foreground transitions.
		/// </summary>
		public bool ForegroundWait { get; set; } = false;

		/// <summary>
		/// Has exlusive mode enabled and waiting for exit.
		/// </summary>
		public bool Exclusive { get; set; } = false;

		/// <summary>
		/// Resized, monitoring for exit.
		/// </summary>
		public bool Resize { get; set; } = false;

		/// <summary>
		/// Waiting for exit to reset color.
		/// </summary>
		public bool ColorReset { get; set; } = false;

		/// <summary>
		/// Currently in background.
		/// </summary>
		public bool InBackground { get; set; } = false;

		public DateTimeOffset Modified { get; set; } = DateTimeOffset.MinValue;

		private HandlingState _state { get; set; } = HandlingState.Invalid;

		public HandlingState State
		{
			get => _state;
			set
			{
				_state = value;

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

		public bool Valid { get; set; } = false;

		public bool Handled { get; set; } = false;

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

		public bool Exited { get; set; } = false;

		public bool PathSearched { get; set; } = false;

		public DateTime Found { get; set; } = DateTime.UtcNow;

		// internal loaders
		public ProcessLoad? Load = null;

		/// <summary>
		/// Display: <code>Name #PID</code> or <code>#PID</code>
		/// </summary>
		public override string ToString() => Name + (Name.Length>0?" ":string.Empty) + "#" + Id.ToString();

		public string ToFormattedString() => FormattedPath + (FormattedPath.Length > 0 ? " " : string.Empty) + "#" + Id.ToString();

		/// <summary>
		/// Same as ToString() but prepends controller name.
		/// </summary>
		public string ToFullString() => "[" + Controller.FriendlyName + "] " + ToString();

		public string ToFullFormattedString() => "[" + Controller.FriendlyName + "] " + ToFormattedString();
	}
}