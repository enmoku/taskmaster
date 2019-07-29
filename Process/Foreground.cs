//
// Process.Foreground.cs
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

namespace Taskmaster.Process
{
	using System.Text;
    using System.Threading.Tasks;
    using static Taskmaster;

	public class WindowChangedArgs : EventArgs
	{
		public IntPtr HWND { get; set; }
		public int Id { get; set; }
		public System.Diagnostics.Process Process { get; set; }
		public string Title { get; set; }
		public bool Fullscreen { get; set; }
		public string Executable { get; set; }
	}

	public class ForegroundManager : IDisposal, IDisposable
	{
		public event EventHandler<WindowChangedArgs> ActiveChanged;

		/// <summary>Manager for foreground/active process specific events.</summary>
		/// <exception cref="InitFailure">Event hook creation failed.</exception>
		public ForegroundManager()
		{
			ForegroundEventDelegate = new global::Taskmaster.NativeMethods.WinEventDelegate(WinEventProc);

			// get current window, just in case it's something we're monitoring
			var hwnd = global::Taskmaster.NativeMethods.GetForegroundWindow();
			global::Taskmaster.NativeMethods.GetWindowThreadProcessId(hwnd, out int pid);
			lock (FGLock)
			{
				PreviousFG = -1;
				ForegroundId = pid;
			}

			using var corecfg = Taskmaster.Config.Load(CoreConfigFilename);
			var perfsec = corecfg.Config["Performance"];
			var hysterisisSetting = perfsec.GetOrSet("Foreground hysterisis", 1500)
				.InitComment("In milliseconds, from 500 to 30000. Delay before we inspect foreground app, in case user rapidly swaps apps.")
				.Int.Constrain(200, 30000);
			Hysterisis = TimeSpan.FromMilliseconds(hysterisisSetting);

			var emsec = corecfg.Config["Emergency"];
			HangKillTick = emsec.GetOrSet("Kill hung", 180 * 5)
				.InitComment("Kill the application after this many seconds. 0 disables. Minimum actual kill time is minimize/reduce time + 60.")
				.Int.Constrain(0, 60 * 60 * 4);

			HangMinimizeTick = emsec.GetOrSet("Hung minimize time", 180)
				.InitComment("Try to minimize hung app after this many seconds.")
				.Int.Constrain(0, 60 * 60 * 2);

			HangReduceTick = emsec.GetOrSet("Hung reduce time", 300)
				.InitComment("Reduce affinity and priority of hung app after this many seconds.")
				.Int.Constrain(0, 60 * 60 * 2);

			int killtickmin = (Math.Max(HangReduceTick, HangMinimizeTick)) + 60;
			if (HangKillTick > 0 && HangKillTick < killtickmin)
				HangKillTick = killtickmin;

			if (HangKillTick > 0 || HangReduceTick > 0 || HangMinimizeTick > 0)
			{
				var sbs = new StringBuilder("<Foreground> Hang action timers: ", 256);

				if (HangMinimizeTick > 0)
				{
					sbs.Append("Minimize: ").Append(HangMinimizeTick).Append('s');
					if (HangReduceTick > 0 || HangKillTick > 0) sbs.Append(", ");
				}
				if (HangReduceTick > 0)
				{
					sbs.Append("Reduce: ").Append(HangReduceTick).Append('s');
					if (HangKillTick > 0) sbs.Append(", ");
				}
				if (HangKillTick > 0) sbs.Append("Kill: ").Append(HangKillTick).Append('s');

				Log.Information(sbs.ToString());
			}

			HangTimer = new System.Timers.Timer(60_000);
			HangTimer.Elapsed += HangDetector;

			if (DebugForeground) Log.Information("<Foreground> Component loaded.");
			RegisterForExit(this);
			DisposalChute.Push(this);
		}

		Process.Manager processmanager = null;

		public async Task Hook(Process.Manager procman)
		{
			processmanager = procman;
			HangTimer.Start();
		}

		public TimeSpan Hysterisis { get; private set; } = TimeSpan.FromSeconds(0.5d);
		public void SetHysterisis(TimeSpan time) => Hysterisis = time;

		readonly global::Taskmaster.NativeMethods.WinEventDelegate ForegroundEventDelegate;
		IntPtr windowseventhook = IntPtr.Zero;

		public int ForegroundId { get; private set; } = -1;

		DateTimeOffset LastSwap = DateTimeOffset.MinValue;

		/// <summary>
		/// Calls SetWinEventHook, which delivers messages to the _thread_ that called it.
		/// As such fails if it's set up in transitory thread.
		/// </summary>
		public bool SetupEventHooks()
		{
			const uint flags = NativeMethods.WINEVENT_OUTOFCONTEXT; // NativeMethods.WINEVENT_SKIPOWNPROCESS
			windowseventhook = global::Taskmaster.NativeMethods.SetWinEventHook(
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

		readonly object FGLock = new object();

		int PreviousFG = 0;
		int HangTick = 0, HangMinimizeTick = 180, HangReduceTick = 240, HangKillTick = 300;

		System.Diagnostics.Process Foreground = null;

		bool Minimized = false, Reduced = false;
		int IgnoreHung = -1;

		int hangdetector_lock = Atomic.Unlocked;

		int PreviouslyHung = -1;

		/// <summary>
		/// Timer callback to occasionally detect if the foreground app is hung.
		/// </summary>
		// TODO: Hang check should only take action if user fails to swap apps (e.g. ctrl-alt-esc for taskmanager)
		// TODO: Hang check should potentially do the following: Minimize app, Reduce priority, Reduce cores, Kill it
		void HangDetector(object _sender, System.Timers.ElapsedEventArgs _)
		{
			if (!Atomic.Lock(ref hangdetector_lock)) return;
			if (DisposedOrDisposing) return; // kinda dumb, but apparently timer can fire off after being disposed...

			try
			{
				int lfgpid, previoushang;
				System.Diagnostics.Process fgproc;
				lock (FGLock)
				{
					lfgpid = ForegroundId;
					previoushang = PreviouslyHung;
					fgproc = Foreground;

					if (fgproc?.Responding == false) PreviouslyHung = fgproc.Id;
				}

				if (!previoushang.Equals(lfgpid)) // foreground changed since last test
					HangTick = 0;

				DateTimeOffset now = DateTimeOffset.UtcNow;
				var since = now.Since(LastSwap); // since app was last changed
				if (since.TotalSeconds < 5) return;

				if (fgproc?.Responding == false)
				{
					string name = string.Empty;
					try
					{
						name = fgproc.ProcessName;
					}
					catch (OutOfMemoryException) { throw; }
					catch
					{
						// probably gone?
						if (Utility.SystemProcessId(lfgpid)) name = "<OS>"; // this might also signify the desktop, for some reason
					}

					var sbs = new StringBuilder("<Foreground> ", 128);

					if (!string.IsNullOrEmpty(name))
						sbs.Append(name).Append(" #").Append(lfgpid);
					else
						sbs.Append('#').Append(lfgpid);

					if (HangTick == 1)
					{
						sbs.Append(" is not responding!");
						Log.Warning(sbs.ToString());
					}
					else if (HangTick > 1)
					{
						double hung = now.Since(HangTime).TotalSeconds;

						sbs.Append(" hung!");

						if (Utility.SystemProcessId(lfgpid))
						{
							Log.Warning(sbs.ToString());
							return; // Ignore system processes. We can do nothing useful for them.
						}

						sbs.Append(" – ");
						bool acted = false;
						if (HangMinimizeTick > 0 && hung > HangMinimizeTick && !Minimized)
						{
							bool rv = global::Taskmaster.NativeMethods.ShowWindow(fgproc.Handle, 11); // 6 = minimize, 11 = force minimize
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
								fgproc.ProcessorAffinity = new IntPtr(1); // TODO: set this to something else than the first core
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
								fgproc.PriorityClass = ProcessPriorityClass.Idle;
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
								lock (FGLock)
								{
									fgproc.Kill();
									fgproc?.Dispose();
									if (fgproc == Foreground)
										Foreground = null;
								}
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

						// TODO: There has to be better way to do this
						// Prevent changes to hung app by other 
						processmanager.Unignore(IgnoreHung);
						IgnoreHung = lfgpid;
						processmanager.Ignore(IgnoreHung);
					}

					HangTick++;
				}
				else
				{
					processmanager.Unignore(IgnoreHung);
					IgnoreHung = -1;

					Reduced = false;
					Minimized = false;

					HangTick = 0;
					HangTime = DateTimeOffset.MaxValue;
				}
			}
			catch (InvalidOperationException) { } // NOP, already exited
			catch (ArgumentException) { } // NOP, already exited
			catch (Exception ex)
			{
				processmanager.Unignore(IgnoreHung);

				Logging.Stacktrace(ex);
			}
			finally
			{
				Atomic.Unlock(ref hangdetector_lock);
			}
		}

		public void SetupEventHookEvent(object _, ProcessModificationEventArgs _ea)
		{
			// SetupEventHook();
		}

		/*
		public static string GetActiveWindowTitle()
		{
			const int nChars = 256;
			IntPtr handle = IntPtr.Zero;
			StringBuilder Buff = new StringBuilder(nChars);
			handle = GetForegroundWindow();

			if (GetWindowText(handle, Buff, nChars) > 0)
				return Buff.ToString();

			return null;
		}
		*/

		static string GetWindowTitle(IntPtr hwnd)
		{
			const int nChars = 256; // Why this limit?
			var buff = new StringBuilder(nChars);

			// Window title, we don't care tbh.

			 // get title? not really useful for most things
			return (global::Taskmaster.NativeMethods.GetWindowText(hwnd, buff, nChars) > 0) ? buff.ToString() : string.Empty;
		}

		System.Drawing.Rectangle WindowRectangle;

		NativeMethods.Rectangle ScreenRectangle;

		bool Fullscreen(IntPtr hwnd)
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException(nameof(ForegroundManager), "Fullscreen called after ActiveAppManager was disposed");

			// TODO: Is it possible to cache screen? multimonitor setup may make it hard... would that save anything?
			var screen = System.Windows.Forms.Screen.FromHandle(hwnd); // passes

			NativeMethods.GetWindowRect(hwnd, ref ScreenRectangle);
			//var windowrect = new System.Drawing.Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
			WindowRectangle.Height = ScreenRectangle.Bottom - ScreenRectangle.Top;
			WindowRectangle.Width = ScreenRectangle.Right - ScreenRectangle.Left;
			WindowRectangle.X = ScreenRectangle.Left;
			WindowRectangle.Y = ScreenRectangle.Top;

			bool full = WindowRectangle.Equals(screen.Bounds);

			return full;
		}

		readonly MKAh.Lock.Monitor WinPrcLock = new MKAh.Lock.Monitor();

		/// <summary>
		/// SetWinEventHook sends messages to this. Don't call it on your own.
		/// </summary>
		// [UIPermissionAttribute(SecurityAction.Demand)] // fails
		//[SecurityPermissionAttribute(SecurityAction.Demand, UnmanagedCode = true)]
		async void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
		{
			if (eventType != NativeMethods.EVENT_SYSTEM_FOREGROUND) return; // does this ever trigger?

			if (DisposedOrDisposing) return;

			await System.Threading.Tasks.Task.Delay(Hysterisis).ConfigureAwait(false); // asyncify

			using var sl = WinPrcLock.ScopedLock();
			if (sl.Waiting) return; // unlikely, but....

			// IntPtr handle = IntPtr.Zero; // hwnd arg already has this
			// handle = GetForegroundWindow();

			try
			{
				LastSwap = DateTimeOffset.UtcNow;

				global::Taskmaster.NativeMethods.GetWindowThreadProcessId(hwnd, out int pid);

				bool fs = Fullscreen(hwnd);

				var activewindowev = new WindowChangedArgs()
				{
					HWND = hwnd,
					Title = string.Empty,
					Fullscreen = fs,
				};

				PreviousFG = ForegroundId;
				ForegroundId = activewindowev.Id = pid;
				HangTick = 0;

				if (!Utility.SystemProcessId(pid))
				{
					try
					{
						lock (FGLock)
						{
							Foreground?.Dispose(); // pointless?
							Foreground = System.Diagnostics.Process.GetProcessById(pid);
							ForegroundId = pid;
							activewindowev.Process = Foreground;
							activewindowev.Executable = Foreground.ProcessName;
						}
					}
					catch (ArgumentException)
					{
						// Process already gone
						return;
					}

					if (DebugForeground && ShowInaction)
						Log.Debug("<Foreground> Active #" + activewindowev.Id.ToString() + ": " + activewindowev.Title);
				}
				else
				{
					lock (FGLock)
					{
						Foreground = null;
						ForegroundId = -1;
					}

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

		#region IDisposable
		public event EventHandler<DisposedEventArgs> OnDisposed;

		~ForegroundManager() => Dispose(false);

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		bool DisposedOrDisposing = false;

		void Dispose(bool disposing)
		{
			if (DisposedOrDisposing) return;

			global::Taskmaster.NativeMethods.UnhookWinEvent(windowseventhook); // Automatic

			if (disposing)
			{
				if (Trace) Log.Verbose("Disposing FG monitor...");

				ActiveChanged = null;
				HangTimer?.Dispose();
			}

			DisposedOrDisposing = true;

			OnDisposed?.Invoke(this, DisposedEventArgs.Empty);
			OnDisposed = null;
		}
		#endregion

		public void ShutdownEvent(object sender, EventArgs ea)
		{
			global::Taskmaster.NativeMethods.UnhookWinEvent(windowseventhook); // Automatic
			HangTimer?.Stop();
		}
	}
}