//
// ActiveAppManager.cs
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
using System.Diagnostics;
using System.Security.Permissions;
using Serilog;

namespace Taskmaster
{
	sealed public class WindowChangedArgs : EventArgs
	{
		public IntPtr hWnd { get; set; }
		public int Id { get; set; }
		public Process Process { get; set; }
		public string Title { get; set; }
		public Trinary Fullscreen { get; set; }
		public string Executable { get; set; }
	}

	sealed public class ActiveAppManager : IDisposable
	{
		public event EventHandler<WindowChangedArgs> ActiveChanged;

		public ActiveAppManager(bool eventhook = true)
		{
			ForegroundEventDelegate = new NativeMethods.WinEventDelegate(WinEventProc);

			if (eventhook)
			{
				if (!SetupEventHook())
					throw new Exception("Failed to initialize active app manager.");
			}

			// get current window, just in case it's something we're monitoring
			var hwnd = NativeMethods.GetForegroundWindow();
			NativeMethods.GetWindowThreadProcessId(hwnd, out int pid);
			Foreground = pid;
			// Console.WriteLine("--- Foreground Process Identifier: " + Foreground);

			var perfsec = Taskmaster.cfg["Performance"];
			Hysterisis = perfsec.GetSetDefault("Foreground hysterisis", 1500, out bool modified).IntValue.Constrain(0, 30000);
			perfsec["Foreground hysterisis"].Comment = "In milliseconds, from 0 to 30000. Delay before we inspect foreground app, in case user rapidly swaps apps.";
			if (modified) Taskmaster.Config.MarkDirtyINI(Taskmaster.cfg);

			// TODO: Add timer to check foreground app hanging
			// TODO: Hang check should only take action if user fails to swap apps (e.g. ctrl-alt-esc for taskmanager)
			// TODO: Hang check should potentially do the following:
			//		Minimize app, Reduce priority, Reduce cores, Kill it

			hungTimer.Elapsed += HangDetector;
			hungTimer.Start();

			Log.Information("<Foreground Manager> Component loaded.");
		}

		int Hysterisis = 500;

		NativeMethods.WinEventDelegate ForegroundEventDelegate;
		IntPtr windowseventhook = IntPtr.Zero;

		public int Foreground { get; private set; } = -1;

		DateTime LastSwap = DateTime.MinValue;

		/// <summary>
		/// Calls SetWinEventHook, which delivers messages to the _thread_ that called it.
		/// As such fails if it's set up in transitory thread.
		/// </summary>
		public bool SetupEventHook()
		{
			uint flags = NativeMethods.WINEVENT_OUTOFCONTEXT; // NativeMethods.WINEVENT_SKIPOWNPROCESS 
			windowseventhook = NativeMethods.SetWinEventHook(
				NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
				IntPtr.Zero, ForegroundEventDelegate, 0, 0, flags);
			// FIXME: Seems to stop functioning really easily? Possibly from other events being caught.
			if (windowseventhook == IntPtr.Zero)
			{
				Log.Error("<Foreground> Window event hook failed to initialize.");
				return false;
			}

			Log.Information("<Foreground> Event hook initialized.");
			return true;
		}

		System.Timers.Timer hungTimer = new System.Timers.Timer(60_000);
		DateTime HangTime = DateTime.MaxValue;

		void HangDetector(object sender, EventArgs ev)
		{
			try
			{
				DateTime now = DateTime.Now;
				TimeSpan since = (now - LastSwap);
				if (since.Seconds > 5)
				{
					Process fg = Process.GetProcessById(Foreground);
					if (fg != null && !fg.Responding)
					{
						if (HangTime != DateTime.MaxValue)
							Log.Warning("<Foreground> {Name} (#Pid) is not responding!", fg.ProcessName, fg.Id);
						else
							HangTime = now;
					}
					else
						HangTime = DateTime.MaxValue;
				}
			}
			catch (ArgumentException)
			{
				// NOP, already exited
				HangTime = DateTime.MaxValue;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		public void SetupEventHookEvent(object sender, ProcessEventArgs e)
		{
			// SetupEventHook();
		}

		~ActiveAppManager()
		{
			Dispose(false);
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		bool disposed = false;
		void Dispose(bool disposing)
		{
			if (disposed) return;

			if (disposing)
			{
				if (Taskmaster.Trace)
					Log.Verbose("Disposing FG monitor...");

				Utility.Dispose(ref hungTimer);

				NativeMethods.UnhookWinEvent(windowseventhook); // Automatic
			}

			disposed = true;
		}

		/*
		public static string GetActiveWindowTitle()
		{
			const int nChars = 256;
			IntPtr handle = IntPtr.Zero;
			System.Text.StringBuilder Buff = new System.Text.StringBuilder(nChars);
			handle = GetForegroundWindow();

			if (GetWindowText(handle, Buff, nChars) > 0)
				return Buff.ToString();
			
			return null;
		}
		*/

		string GetWindowTitle(IntPtr hwnd)
		{
			const int nChars = 256; // Why this limit?
			var buff = new System.Text.StringBuilder(nChars);

			// Window title, we don't care tbh.
			if (NativeMethods.GetWindowText(hwnd, buff, nChars) > 0) // get title? not really useful for most things
			{
				// System.Console.WriteLine("Active window: {0}", buff);
				return buff.ToString();
			}

			return string.Empty;
		}

		bool Fullscreen(IntPtr hwnd)
		{
			NativeMethods.RECT rect = new NativeMethods.RECT();

			System.Windows.Forms.Screen screen = System.Windows.Forms.Screen.FromHandle(hwnd); // passes

			NativeMethods.GetWindowRect(hwnd, ref rect);
			var r = new System.Drawing.Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

			bool full = r.Equals(screen.Bounds);

			return full;
		}

		int foreground_counter = 0;
		object foregroundswap_lock = new object();

		/// <summary>
		/// SetWinEventHook sends messages to this. Don't call it on your own.
		/// </summary>
		// [UIPermissionAttribute(SecurityAction.Demand)] // fails
		//[SecurityPermissionAttribute(SecurityAction.Demand, UnmanagedCode = true)]
		async void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
		{
			if (eventType != NativeMethods.EVENT_SYSTEM_FOREGROUND) return; // does this ever trigger?

			foreground_counter++;

			await System.Threading.Tasks.Task.Delay(Hysterisis); // minded

			lock (foregroundswap_lock)
			{
				if (foreground_counter > 1) // if we've swapped in this time, we won't bother checking anything about it.
				{
					foreground_counter--;
					// Log.Verbose("<Foreground> {0} apps in foreground, we're late to the party.", old);
					return;
				}
			}

			// IntPtr handle = IntPtr.Zero; // hwnd arg already has this
			// handle = GetForegroundWindow();

			try
			{
				var activewindowev = new WindowChangedArgs()
				{
					hWnd = hwnd,
					Title = string.Empty,
					Fullscreen = Trinary.Nonce,
				};

				NativeMethods.GetWindowThreadProcessId(hwnd, out int pid);
				Foreground = activewindowev.Id = pid;
				// Console.WriteLine("--- Foreground Process Identifier: " + Foreground);
				try
				{
					var proc = Process.GetProcessById(activewindowev.Id);
					activewindowev.Process = proc;
					activewindowev.Executable = proc.ProcessName;
				}
				catch (ArgumentException)
				{
					// Process already gone
					return;
				}
				catch (Exception ex) { Logging.Stacktrace(ex); } // NOP

				if (Taskmaster.DebugForeground && Taskmaster.ShowInaction)
					Log.Debug("Active Window (#{Pid}): {Title}", activewindowev.Id, activewindowev.Title);

				LastSwap = DateTime.Now;
				ActiveChanged?.Invoke(this, activewindowev);
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				lock (foregroundswap_lock)
				{
					foreground_counter--;
				}
			}
		}
	}
}