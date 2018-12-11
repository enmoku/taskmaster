﻿//
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

		/// <exception cref="InitFailure">Event hook creation failed.</exception>
		public ActiveAppManager(bool eventhook = true)
		{
			ForegroundEventDelegate = new NativeMethods.WinEventDelegate(WinEventProc);

			if (eventhook)
			{
				if (!SetupEventHook())
					throw new InitFailure("Failed to initialize active app manager.");
			}

			// get current window, just in case it's something we're monitoring
			var hwnd = NativeMethods.GetForegroundWindow();
			NativeMethods.GetWindowThreadProcessId(hwnd, out int pid);
			Foreground = pid;

			var corecfg = Taskmaster.Config.Load(Taskmaster.coreconfig);

			bool dirty = false, modified = false;
			var perfsec = corecfg.Config["Performance"];
			Hysterisis = perfsec.GetSetDefault("Foreground hysterisis", 1500, out modified).IntValue.Constrain(0, 30000);
			perfsec["Foreground hysterisis"].Comment = "In milliseconds, from 0 to 30000. Delay before we inspect foreground app, in case user rapidly swaps apps.";
			dirty |= modified;

			var emsec = corecfg.Config["Emergency"];
			HangKillTick = emsec.GetSetDefault("Kill hung", 180 * 5, out modified).IntValue.Constrain(0, 60 * 60 * 4);
			emsec["Hung kill time"].Comment = "Kill the application after this many seconds. 0 disables. Minimum actual kill time is minimize/reduce time + 60.";
			dirty |= modified;

			HangMinimizeTick = emsec.GetSetDefault("Hung minimize time", 180, out modified).IntValue.Constrain(0, 60 * 60 * 2);
			emsec["Hung minimize time"].Comment = "Try to minimize hung app after this many seconds.";
			dirty |= modified;

			HangReduceTick = emsec.GetSetDefault("Hung reduce time", 300, out modified).IntValue.Constrain(0, 60 * 60 * 2);
			emsec["Hung reduce time"].Comment = "Reduce affinity and priority of hung app after this many seconds.";
			dirty |= modified;

			int killtickmin = (Math.Max(HangReduceTick, HangMinimizeTick)) + 60;
			if (HangKillTick > 0 && HangKillTick < killtickmin)
				HangKillTick = killtickmin;

			if (HangKillTick > 0 || HangReduceTick > 0 || HangMinimizeTick > 0)
			{
				var sbs = new System.Text.StringBuilder();
				sbs.Append("<Foreground> Hang action timers: ");

				if (HangMinimizeTick > 0)
				{
					sbs.Append("Minimize: ").Append(HangMinimizeTick).Append("s");
					if (HangReduceTick > 0 || HangKillTick > 0) sbs.Append(", ");
				}
				if (HangReduceTick > 0)
				{
					sbs.Append("Reduce: ").Append(HangReduceTick).Append("s");
					if (HangKillTick > 0) sbs.Append(", ");
				}
				if (HangKillTick > 0)
					sbs.Append("Kill: ").Append(HangKillTick).Append("s");

				Log.Information(sbs.ToString());
			}

			if (dirty) corecfg.MarkDirty();

			hungTimer.Elapsed += HangDetector;
			hungTimer.Start();

			if (Taskmaster.DebugForeground) Log.Information("<Foreground> Component loaded.");
		}

		int Hysterisis = 500;

		readonly NativeMethods.WinEventDelegate ForegroundEventDelegate;
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

		int PreviousFG = 0;
		int HangTick = 0;
		int HangMinimizeTick = 180;
		int HangReduceTick = 240;
		int HangKillTick = 300;

		Process fg = null;

		bool Minimized = false;
		bool Reduced = false;
		int IgnoreHung = -1;

		int hangdetector_lock = 0;

		/// <summary>
		/// Timer callback to occasionally detect if the foreground app is hung.
		/// </summary>
		// TODO: Hang check should only take action if user fails to swap apps (e.g. ctrl-alt-esc for taskmanager)
		// TODO: Hang check should potentially do the following: Minimize app, Reduce priority, Reduce cores, Kill it
		void HangDetector(object sender, EventArgs ev)
		{
			if (!Atomic.Lock(ref hangdetector_lock)) return;

			try
			{
				int pid = Foreground;

				DateTime now = DateTime.Now;
				TimeSpan since = now.TimeSince(LastSwap); // since app was last changed
				if (since.TotalSeconds < 5) return;

				if (PreviousFG != pid)
				{
					// foreground changed
					fg = Process.GetProcessById(pid);
					HangTick = 0;
				}

				if (fg != null && !fg.Responding)
				{
					string name = string.Empty;
					try
					{
						name = fg.ProcessName;
					}
					catch
					{
						// probably gone?
						if (pid <= 4) name = "<OS>";
					}

					var sbs = new System.Text.StringBuilder();
					sbs.Append("<Foreground> ");
					if (!string.IsNullOrEmpty(name))
						sbs.Append(name).Append(" (#").Append(pid).Append(")");
					else
						sbs.Append("#").Append(pid);

					if (HangTick == 1)
					{
						sbs.Append(" is not responding!");
						Log.Warning(sbs.ToString());
					}
					else if (HangTick > 1)
					{
						double hung = now.TimeSince(HangTime).TotalSeconds;

						sbs.Append(" hung!");

						if (pid <= 4)
						{
							Log.Warning(sbs.ToString());
							return; // Ignore system processes. We can do nothing useful for them.
						}

						sbs.Append(" – ");
						bool acted = false;
						if (HangMinimizeTick > 0 && hung > HangMinimizeTick && !Minimized)
						{
							bool rv = NativeMethods.ShowWindow(fg.Handle, 11); // 6 = minimize, 11 = force minimize
							if (rv)
							{
								sbs.Append("Minimized");
								Minimized = true;
							}
							else
								sbs.Append("Minimize failed");
							acted = true;
						}
						if (HangReduceTick > 0 && hung > HangReduceTick && !Reduced)
						{
							bool aff = false, prio = false;
							try
							{
								fg.ProcessorAffinity = new IntPtr(1);
								if (acted) sbs.Append(", ");
								sbs.Append("Affinity reduced");
								aff = true;
							}
							catch
							{
								if (acted) sbs.Append(", ");
								sbs.Append("Affinity reduction failed");
							}
							acted = true;
							try
							{
								fg.PriorityClass = ProcessPriorityClass.Idle;
								sbs.Append(", Priority reduced");
								prio = true;
							}
							catch
							{
								sbs.Append(", Priority reduction failed");
							}

							if (aff && prio) Reduced = true;
						}
						if (HangKillTick > 0 && hung > HangKillTick)
						{
							try
							{
								fg.Kill();
								if (acted) sbs.Append(", ");
								sbs.Append("Terminated");
							}
							catch
							{
								if (acted) sbs.Append(", ");
								sbs.Append("Termination failed");
							}
							acted = true;
						}

						if (acted) Log.Warning(sbs.ToString());
					}
					else
					{
						HangTime = now;
						Reduced = false;
						Minimized = false;

						Taskmaster.Components.processmanager.Unignore(IgnoreHung);
						IgnoreHung = pid;
						Taskmaster.Components.processmanager.Ignore(IgnoreHung);
					}

					HangTick++;

					return;
				}
			}
			catch (InvalidOperationException) { } // NOP, already exited
			catch (ArgumentException) { } // NOP, already exited
			catch (Exception ex)
			{
				Taskmaster.Components.processmanager.Unignore(IgnoreHung);

				Logging.Stacktrace(ex);
				return;
			}
			finally
			{
				Atomic.Unlock(ref hangdetector_lock);
			}

			Taskmaster.Components.processmanager.Unignore(IgnoreHung);
			IgnoreHung = -1;

			HangTick = 0;
			HangTime = DateTime.MaxValue;
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

				ActiveChanged = null;

				hungTimer?.Dispose();

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

		System.Drawing.Rectangle windowrect;
		NativeMethods.RECT screenrect;
		bool Fullscreen(IntPtr hwnd)
		{
			// TODO: Is it possible to cache screen? multimonitor setup may make it hard... would that save anything?
			var screen = System.Windows.Forms.Screen.FromHandle(hwnd); // passes

			NativeMethods.GetWindowRect(hwnd, ref screenrect);
			//var windowrect = new System.Drawing.Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
			windowrect.Height = screenrect.Bottom - screenrect.Top;
			windowrect.Width = screenrect.Right - screenrect.Left;
			windowrect.X = screenrect.Left;
			windowrect.Y = screenrect.Top;

			bool full = windowrect.Equals(screen.Bounds);

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

			await System.Threading.Tasks.Task.Delay(Hysterisis);

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

				bool fs = Fullscreen(hwnd);
				activewindowev.Fullscreen = fs ? Trinary.True : Trinary.False;

				Foreground = activewindowev.Id = pid;
				HangTick = 0;

				if (pid > 4)
				{

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

					if (Taskmaster.DebugForeground && Taskmaster.ShowInaction)
						Log.Debug("<Foreground >Active #" + activewindowev.Id + ": " + activewindowev.Title);
				}
				else
				{
					// shouldn't happen, but who knows?
					activewindowev.Process = null;
					activewindowev.Executable = string.Empty;
				}

				LastSwap = DateTime.Now;
				ActiveChanged?.Invoke(this, activewindowev);
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				return; // HACK, WndProc probably shouldn't throw
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