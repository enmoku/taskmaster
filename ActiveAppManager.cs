//
// ActiveAppManager.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2016-2019 M.A.
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
using MKAh;
using Serilog;

namespace Taskmaster
{
	using static Taskmaster;

	sealed public class WindowChangedArgs : EventArgs
	{
		public IntPtr hWnd { get; set; }
		public int Id { get; set; }
		public Process Process { get; set; }
		public string Title { get; set; }
		public Trinary Fullscreen { get; set; }
		public string Executable { get; set; }
	}

	sealed public class ActiveAppManager : IComponent, IDisposable
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

			using (var corecfg = Config.Load(CoreConfigFilename).BlockUnload())
			{
				bool dirty = false, modified = false;
				var perfsec = corecfg.Config["Performance"];
				var hysterisisSetting = perfsec.GetOrSet("Foreground hysterisis", 1500, out modified);
				Hysterisis = TimeSpan.FromMilliseconds(hysterisisSetting.IntValue.Constrain(200, 30000));
				if (modified) hysterisisSetting.Comment = "In milliseconds, from 500 to 30000. Delay before we inspect foreground app, in case user rapidly swaps apps.";
				dirty |= modified;

				var emsec = corecfg.Config["Emergency"];
				var hangKillTickSetting = emsec.GetOrSet("Kill hung", 180 * 5, out modified);
				HangKillTick = hangKillTickSetting.IntValue.Constrain(0, 60 * 60 * 4);
				if (modified) hangKillTickSetting.Comment = "Kill the application after this many seconds. 0 disables. Minimum actual kill time is minimize/reduce time + 60.";
				dirty |= modified;

				var hangMinTickSetting = emsec.GetOrSet("Hung minimize time", 180, out modified);
				HangMinimizeTick = hangMinTickSetting.IntValue.Constrain(0, 60 * 60 * 2);
				if (modified) hangMinTickSetting.Comment = "Try to minimize hung app after this many seconds.";
				dirty |= modified;

				var hangReduceTickSetting = emsec.GetOrSet("Hung reduce time", 300, out modified);
				HangReduceTick = hangReduceTickSetting.IntValue.Constrain(0, 60 * 60 * 2);
				if (modified) hangReduceTickSetting.Comment = "Reduce affinity and priority of hung app after this many seconds.";
				dirty |= modified;

				if (dirty) corecfg.MarkDirty();
			}

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

			HangTimer = new System.Timers.Timer(60_000);
			HangTimer.Elapsed += HangDetector;
			HangTimer.Start();

			if (DebugForeground) Log.Information("<Foreground> Component loaded.");

			DisposalChute.Push(this);
		}

		public TimeSpan Hysterisis { get; set; } = TimeSpan.FromSeconds(0.5d);

		readonly NativeMethods.WinEventDelegate ForegroundEventDelegate;
		IntPtr windowseventhook = IntPtr.Zero;

		public int Foreground { get; private set; } = -1;

		DateTimeOffset LastSwap = DateTimeOffset.MinValue;

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

			if (Trace) Log.Information("<Foreground> Event hook initialized.");
			return true;
		}

		readonly System.Timers.Timer HangTimer = null;
		DateTimeOffset HangTime = DateTimeOffset.MaxValue;

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
		void HangDetector(object _, EventArgs _ea)
		{
			if (!Atomic.Lock(ref hangdetector_lock)) return;
			if (DisposedOrDisposing) return; // kinda dumb, but apparently timer can fire off after being disposed...

			try
			{
				int pid = Foreground;

				DateTimeOffset now = DateTimeOffset.UtcNow;
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
					catch (OutOfMemoryException) { throw; }
					catch
					{
						// probably gone?
						if (ProcessManager.SystemProcessId(pid)) name = "<OS>"; // this might also signify the desktop, for some reason
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

						if (ProcessManager.SystemProcessId(pid))
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
								fg.ProcessorAffinity = new IntPtr(1); // TODO: set this to something else than the first core
								if (acted) sbs.Append(", ");
								sbs.Append("Affinity reduced");
								aff = true;
							}
							catch (OutOfMemoryException) { throw; }
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
							catch (OutOfMemoryException) { throw; }
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
							catch (OutOfMemoryException) { throw; }
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

						processmanager.Unignore(IgnoreHung);
						IgnoreHung = pid;
						processmanager.Ignore(IgnoreHung);
					}

					HangTick++;

					return;
				}
			}
			catch (InvalidOperationException) { } // NOP, already exited
			catch (ArgumentException) { } // NOP, already exited
			catch (Exception ex)
			{
				processmanager.Unignore(IgnoreHung);

				Logging.Stacktrace(ex);
				return;
			}
			finally
			{
				Atomic.Unlock(ref hangdetector_lock);
			}

			processmanager.Unignore(IgnoreHung);
			IgnoreHung = -1;

			HangTick = 0;
			HangTime = DateTimeOffset.MaxValue;
		}

		public void SetupEventHookEvent(object _, ProcessModificationEventArgs _ea)
		{
			// SetupEventHook();
		}

		#region IDisposable
		public event EventHandler OnDisposed;

		~ActiveAppManager() => Dispose(false);

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		bool DisposedOrDisposing = false;
		void Dispose(bool disposing)
		{
			if (DisposedOrDisposing) return;

			if (disposing)
			{
				if (Trace) Log.Verbose("Disposing FG monitor...");

				ActiveChanged = null;
				HangTimer?.Dispose();
			}

			NativeMethods.UnhookWinEvent(windowseventhook); // Automatic

			DisposedOrDisposing = true;

			OnDisposed?.Invoke(this, EventArgs.Empty);
			OnDisposed = null;
		}
		#endregion

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
				return buff.ToString();

			return string.Empty;
		}

		System.Drawing.Rectangle windowrect;
		NativeMethods.RECT screenrect;
		bool Fullscreen(IntPtr hwnd)
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException("Fullscreen called after ActiveAppManager was disposed");

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

		MKAh.Lock.Monitor ScopedLock = new MKAh.Lock.Monitor();

		/// <summary>
		/// SetWinEventHook sends messages to this. Don't call it on your own.
		/// </summary>
		// [UIPermissionAttribute(SecurityAction.Demand)] // fails
		//[SecurityPermissionAttribute(SecurityAction.Demand, UnmanagedCode = true)]
		async void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
		{
			if (eventType != NativeMethods.EVENT_SYSTEM_FOREGROUND) return; // does this ever trigger?

			await System.Threading.Tasks.Task.Delay(Hysterisis); // asyncify
			if (DisposedOrDisposing) return;

			using (var sl = ScopedLock.ScopedLock())
			{
				if (sl.Waiting) return;

				// IntPtr handle = IntPtr.Zero; // hwnd arg already has this
				// handle = GetForegroundWindow();

				try
				{
					LastSwap = DateTimeOffset.UtcNow;

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

					if (!ProcessManager.SystemProcessId(pid))
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

						if (DebugForeground && ShowInaction)
							Log.Debug("<Foreground> Active #" + activewindowev.Id + ": " + activewindowev.Title);
					}
					else
					{
						// shouldn't happen, but who knows?
						activewindowev.Process = null;
						activewindowev.Executable = string.Empty;
					}

					if (sl.Waiting) return;

					ActiveChanged?.Invoke(this, activewindowev);
				}
				catch (ObjectDisposedException) { Statistics.DisposedAccesses++; } // NOP
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
					return; // HACK, WndProc probably shouldn't throw
				}
			}
		}
	}
}