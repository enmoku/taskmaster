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
using System.Linq;
using System.Text;

namespace Taskmaster.Process
{
	public class ProcessEx
	{
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
		public string FormattedPath { get; set; } = null;

		/// <summary>
		/// As seen in task manager.
		/// </summary>
		public string Description { get; set; } = null;

		public Stopwatch Timer = null;
		public double WMIDelay = 0;

		public LegacyLevel Legacy { get; set; } = LegacyLevel.Undefined;

		/// <summary>
		/// Process Id.
		/// </summary>
		public int Id { get; set; } = -1;

		/// <summary>
		/// Process reference.
		/// </summary>
		public System.Diagnostics.Process Process { get; set; } = null;

		/// <summary>
		/// Controller associated with this process.
		/// </summary>
		public Process.Controller Controller { get; set; } = null;

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

		internal HandlingState _state { get; set; } = HandlingState.Invalid;

		public HandlingState State
		{
			get => _state;
			set
			{
				_state = value;

				switch (value)
				{
					case HandlingState.Exited:
					case HandlingState.Modified:
					case HandlingState.Unmodified:
					case HandlingState.AccessDenied:
					case HandlingState.Finished:
					case HandlingState.Abandoned:
					case HandlingState.Invalid:
						if (value == HandlingState.Exited) Exited = true;
						else if (value == HandlingState.Modified) Modified = DateTimeOffset.UtcNow;
						Handled = true;
						Timer?.Stop();
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
		public ProcessLoad Loaders;

		/// <summary>
		/// Display: <code>Name #PID</code>
		/// </summary>
		public override string ToString() => Name + " #" + Id.ToString();

		/// <summary>
		/// Same as ToString() but prepends controller name.
		/// </summary>
		public string ToFullString() => "[" + Controller.FriendlyName + "]" + ToString();
	}

	public class ProcessLoad : IDisposable
	{
		MKAh.Wrapper.Windows.PerformanceCounter CPUCounter, IOCounter;

		readonly string Instance;
		readonly int Id;
		string PFCInstance;

		public float CPU { get; private set; } = float.NaN;

		public float IO { get; private set; } = float.NaN;

		public ProcessLoad(int pid, string instance)
		{
			Instance = instance;
			Id = pid;

			GetInstanceName();
			Refresh();
		}

		public bool Update(bool noRecovery=false)
		{
			if (disposed) return false;

			try
			{
				CPU = CPUCounter.Value / Environment.ProcessorCount;
				IO = IOCounter.Value;
				return true;
			}
			catch (NullReferenceException ex)
			{
				Logging.DebugMsg("LOAD NULL: " + Instance + " #" + Id.ToString());
				Logging.Stacktrace(ex);
			}
			catch
			{
				if (!noRecovery)
				{
					Refresh();
					return Update(noRecovery: true);
				}
			}

			return false;
		}

		void Refresh()
		{
			Scrap();

			CPUCounter = new MKAh.Wrapper.Windows.PerformanceCounter("Process", "% Processor Time", PFCInstance);
			IOCounter = new MKAh.Wrapper.Windows.PerformanceCounter("Process", "IO Data Bytes/sec", PFCInstance);
		}

		bool GetInstanceName()
		{
			var processCategory = new PerformanceCounterCategory("Process");

			char[] separator = { '#' };
			var instances = processCategory.GetInstanceNames()
				.Where(inst => inst.StartsWith(Instance, StringComparison.InvariantCultureIgnoreCase));

			foreach (var name in instances)
			{
				using var idpc = new MKAh.Wrapper.Windows.PerformanceCounter("Process", "ID Process", name, false);
				if (Id == idpc.Raw)
				{
					PFCInstance = name;
					return true;
				}
			}

			return false;
		}

		#region IDisposable Support
		bool disposed = false;

		void Scrap()
		{
			CPUCounter?.Dispose();
			CPUCounter = null;
			IOCounter?.Dispose();
			IOCounter = null;
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposed) return;
			disposed = true;

			if (disposing)
			{
				Scrap();

				//base.Dispose();
			}
		}

		~ProcessLoad() => Dispose(false);

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}
		#endregion
	}
}