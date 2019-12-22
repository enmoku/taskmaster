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

using MKAh;
using MKAh.Synchronize;
using Serilog;
using System;
using System.Diagnostics;

namespace Taskmaster.Process
{
	using System.Text;
	using static Application;

	public class WindowChangedArgs : EventArgs
	{
		public IntPtr HWND { get; set; }
		public int Id { get; set; }
		public System.Diagnostics.Process? Process { get; set; }
		public string Title { get; set; }
		public bool Fullscreen { get; set; }
		public string Executable { get; set; }
	}

	public class ForegroundManager : IComponent
	{
		public event EventHandler<WindowChangedArgs>? ActiveChanged;

		/// <summary>Manager for foreground/active process specific events.</summary>
		/// <exception cref="InitFailure">Event hook creation failed.</exception>
		public ForegroundManager()
		{
			ForegroundEventDelegate = new Taskmaster.NativeMethods.WinEventDelegate(WinEventProc);

			// get current window, just in case it's something we're monitoring
			var hwnd = Taskmaster.NativeMethods.GetForegroundWindow();
			NativeMethods.GetWindowThreadProcessId(hwnd, out int pid);

			lock (FGLock)
			{
				PreviousFG = -1;
				ForegroundId = pid;
			}

			using var corecfg = Application.Config.Load(CoreConfigFilename);
			var perfsec = corecfg.Config["Performance"];
			var hysterisisSetting = perfsec.GetOrSet("Foreground hysterisis", 1500)
				.InitComment("In milliseconds, from 500 to 30000. Delay before we inspect foreground app, in case user rapidly swaps apps.")
				.Int.Constrain(200, 30000);
			Hysterisis = TimeSpan.FromMilliseconds(hysterisisSetting);

			var emsec = corecfg.Config["Emergency"];
			int hangkilltimet = emsec.GetOrSet("Kill hung", 180 * 5)
				.InitComment("Kill the application after this many seconds. 0 disables. Minimum actual kill time is minimize/reduce time + 60.")
				.Int.Constrain(0, 60 * 60 * 4);
			HangKillTime = hangkilltimet > 0 ? TimeSpan.FromSeconds(hangkilltimet) : TimeSpan.Zero;

			int hangminimizetimet = emsec.GetOrSet("Hung minimize time", 180)
				.InitComment("Try to minimize hung app after this many seconds.")
				.Int.Constrain(0, 60 * 60 * 2);
			HangMinimizeTime = hangminimizetimet > 0 ? TimeSpan.FromSeconds(hangminimizetimet) : TimeSpan.Zero;

			int hangreducetimet = emsec.GetOrSet("Hung reduce time", 300)
				.InitComment("Reduce affinity and priority of hung app after this many seconds.")
				.Int.Constrain(0, 60 * 60 * 2);
			HangReduceTime = hangreducetimet > 0 ? TimeSpan.FromSeconds(hangreducetimet) : TimeSpan.Zero;

			int killtickmin = Math.Max(hangreducetimet, hangminimizetimet) + 60;
			if (!HangKillTime.IsZero() && HangKillTime.TotalSeconds < killtickmin)
				HangKillTime = TimeSpan.FromSeconds(killtickmin);

			if (!HangKillTime.IsZero() || !HangReduceTime.IsZero() || !HangMinimizeTime.IsZero())
			{
				var sbs = new StringBuilder("<Foreground> Hang action timers: ", 256);

				if (!HangMinimizeTime.IsZero())
				{
					sbs.Append("Minimize: ").Append(HangMinimizeTime.TotalSeconds.ToString("N0")).Append('s');
					if (!HangReduceTime.IsZero() || !HangKillTime.IsZero()) sbs.Append(", ");
				}
				if (!HangReduceTime.IsZero())
				{
					sbs.Append("Reduce: ").Append(HangReduceTime.TotalSeconds.ToString("N0")).Append('s');
					if (!HangKillTime.IsZero()) sbs.Append(", ");
				}
				if (!HangKillTime.IsZero()) sbs.Append("Kill: ").Append(HangKillTime.TotalSeconds.ToString("N0")).Append('s');

				Log.Information(sbs.ToString());
			}

			HangTimer.Elapsed += HangDetector;

			if (DebugForeground) Log.Information("<Foreground> Component loaded.");
		}

		Process.Manager? processmanager = null;

		public void Hook(Process.Manager procman)
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

		readonly System.Timers.Timer HangTimer = new System.Timers.Timer(60_000);

		DateTimeOffset HangTime = DateTimeOffset.MaxValue;

		readonly object FGLock = new object();

		int PreviousFG = 0;
		TimeSpan
			HangMinimizeTime = TimeSpan.FromMinutes(3d),
			HangReduceTime = TimeSpan.FromMinutes(4d),
			HangKillTime = TimeSpan.FromMinutes(5d);

		int HangTick = -1;

		bool HangWarning = false;

		void ResetHanging()
		{
			HangTick = -1;
			ReduceTried = Reduced = false;
			MinimizeTried = Minimized = false;

			if (HangWarning) Log.Debug("<Foreground> Hung application status reset (observed hang time: " + HangTime.To(DateTimeOffset.UtcNow).TotalSeconds.ToString("N1") + " seconds).");

			HangWarning = false;
		}

		System.Diagnostics.Process? Foreground = null;

		bool Minimized = false, MinimizeTried = false, Reduced = false, ReduceTried = false;

		/// <summary>
		/// This is reported to process manager to be ignored for modifications.
		/// </summary>
		int IgnoreHung = -1;

		int hangdetector_lock = Atomic.Unlocked;

		int PreviouslyHung = -1;

		/// <summary>
		/// Timer callback to occasionally detect if the foreground app is hung.
		/// </summary>
		// TODO: Hang check should potentially do the following: Minimize app, Reduce priority, Reduce cores, Kill it
		// TODO: Hang check might not be very useful if the user is AFK and/or the workstation is locked.
		void HangDetector(object _sender, System.Timers.ElapsedEventArgs _)
		{
			if (!Atomic.Lock(ref hangdetector_lock)) return;
			if (disposed) return; // kinda dumb, but apparently timer can fire off after being disposed...

			try
			{
				int lfgpid, previoushang;
				System.Diagnostics.Process? fgproc;

				bool responding;

				lock (FGLock)
				{
					lfgpid = ForegroundId;
					previoushang = PreviouslyHung;
					fgproc = Foreground;

					if (lfgpid < Process.Utility.LowestValidId)
					{
						Logging.DebugMsg("HangDetector received invalid PID");
						return;
					}
					else if (fgproc is null)
					{
						Log.Fatal("<Foreground> Hang detector received invalid process instance.");
						return;
					}

					//fgproc?.Refresh();
					//responding = fgproc?.Responding ?? true;
					responding = !NativeMethods.IsHungAppWindow(fgproc.MainWindowHandle);

					if (!responding) PreviouslyHung = fgproc.Id;
				}

				if (Utility.SystemProcessId(lfgpid))
				{
					ResetHanging();
					return; // don't care
				}

				if (DebugForeground)
					Logging.DebugMsg("<Foreground::DEBUG> Foreground: " + fgproc.ProcessName + " #" + lfgpid.ToString() + " --- responding: " + responding.ToString());

				DateTimeOffset now = DateTimeOffset.UtcNow;
				var since = now.Since(LastSwap); // since app was last changed
				if (since.TotalSeconds < 5)
				{
					ResetHanging();
					return;
				}

				if (fgproc is null)
				{
					// why would this happen?
					return;
				}

				if (!responding)
				{
					if (previoushang != lfgpid) // foreground changed since last test
					{
						if (DebugForeground && ShowInaction)
							Log.Debug("<Foreground> Hung app #" + previoushang.ToString() + " swapped away but #" + lfgpid.ToString() + " is hanging too!");

						ResetHanging();
					}

					HangTick++;

					string name = string.Empty;
					try
					{
						name = fgproc.ProcessName;
					}
					catch (OutOfMemoryException) { throw; }
					catch
					{
						ResetHanging();
						lock (FGLock)
						{
							if (ForegroundId == lfgpid)
							{
								Foreground = null;
								ForegroundId = -1;
							}
						}

						return;
					}

					var sbs = new StringBuilder("<Foreground> ", 128);

					if (!string.IsNullOrEmpty(name))
						sbs.Append(name).Append(" #").Append(lfgpid);
					else
						sbs.Append('#').Append(lfgpid);

					var hungTime = HangTime.To(now);

					if (HangTick == 0)
					{
						HangTime = now;
						Reduced = ReduceTried = false;
						Minimized = MinimizeTried = false;

						// TODO: There has to be better way to do this
						// Prevent changes to hung app by other 
						processmanager.Unignore(IgnoreHung);
						IgnoreHung = lfgpid;
						processmanager.Ignore(IgnoreHung);

						return;
					}
					else if (HangTick == 5)
					{
						sbs.Append(" is not responding!")
							.Append(" (Hung for ").Append(hungTime.TotalSeconds.ToString("N1")).Append(" seconds).");
						Log.Warning(sbs.ToString());
						HangWarning = true;

						// TODO: State how long to next actions.
						trayaccess?.Tooltip(5000, name + " #" + lfgpid.ToString(), "Foreground HUNG!\nHung for " + hungTime.TotalSeconds.ToString("N1") + " seconds.", System.Windows.Forms.ToolTipIcon.Warning);

						return;
					}
					else if (HangTick >= 10)
					{
						trayaccess.Tooltip(2000, name + " #" + lfgpid.ToString(), "Foreground not responding\nHung for " + hungTime.TotalSeconds.ToString("N1") + " seconds.", System.Windows.Forms.ToolTipIcon.Warning);
					}

					sbs.Append(" hung!").Append(" – ");

					bool acted = false;
					if (!HangMinimizeTime.IsZero() && hungTime >= HangMinimizeTime && !Minimized)
					{
						lock (FGLock)
						{
							if (lfgpid != ForegroundId) return;

							bool rv = Taskmaster.NativeMethods.ShowWindow(fgproc.Handle, 11); // 6 = minimize, 11 = force minimize

							if (rv)
							{
								sbs.Append("Minimized");
								Minimized = true;
							}
							else
								sbs.Append("Minimize failed");
							acted = true;
						}
					}

					if (!HangReduceTime.IsZero() && hungTime >= HangReduceTime && !Reduced)
					{
						bool aff = false, prio = false;
						try
						{
							lock (FGLock)
							{
								if (lfgpid != ForegroundId) return;

								if (fgproc.ProcessorAffinity.ToInt32() != 1)
								{
									fgproc.ProcessorAffinity = new IntPtr(1); // TODO: set this to something else than the first core
									if (acted) sbs.Append(", ");
									sbs.Append("Affinity reduced");
									aff = true;
								}

								if (fgproc.PriorityClass.ToInt32() > ProcessPriorityClass.Idle.ToInt32())
								{
									if (aff || acted) sbs.Append(", ");
									fgproc.PriorityClass = ProcessPriorityClass.Idle;
									sbs.Append("Priority reduced");
									prio = true;
								}
							}
						}
						catch (OutOfMemoryException) { throw; }
						catch
						{
							sbs.Append("Affinity/Priority reduction failed");
						}

						if (aff || prio)
						{
							acted = true;
							Reduced = true;
						}
					}

					if (!HangKillTime.IsZero() && hungTime > HangKillTime)
					{
						try
						{
							lock (FGLock)
							{
								if (lfgpid != ForegroundId) return; // last chance to bail

								fgproc.Kill();
								fgproc?.Dispose();

								if (fgproc == Foreground)
								{
									Foreground = null;
									ForegroundId = -1;
								}

								acted = true;
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
					}

					if (acted) Log.Warning(sbs.ToString());
				}
				else
				{
					processmanager.Unignore(IgnoreHung);
					IgnoreHung = -1;

					ResetHanging();

					HangTime = DateTimeOffset.MaxValue;
				}
			}
			catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
			{
				// NOP, already exited

				ResetHanging();
			}
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

		public void SetupEventHookEvent(object _, ModificationInfo _ea)
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

		int LastEvent = 0;

		/// <summary>
		/// SetWinEventHook sends messages to this. Don't call it on your own.
		/// </summary>
		// [UIPermissionAttribute(SecurityAction.Demand)] // fails
		//[SecurityPermissionAttribute(SecurityAction.Demand, UnmanagedCode = true)]
		void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
		{
			if (eventType != NativeMethods.EVENT_SYSTEM_FOREGROUND) // does this ever trigger?
			{
				Logging.DebugMsg("<Foreground> Wrong event: " + eventType.ToString());
				return;
			}

			int ev = System.Threading.Interlocked.Increment(ref LastEvent);
			LastSwap = DateTimeOffset.UtcNow;

			ResetHanging();

			NativeMethods.GetWindowThreadProcessId(hwnd, out int pid);

			if (disposed || LastEvent != ev) return; // more events have arrived or disposed

			try
			{
				lock (FGLock)
				{
					Foreground = System.Diagnostics.Process.GetProcessById(pid);
					PreviousFG = ForegroundId;
					ForegroundId = pid;
				}

				//bool fs = Utility.IsFullscreen(hwnd);

				var activewindowev = new WindowChangedArgs()
				{
					HWND = hwnd,
					Title = string.Empty,
					//Fullscreen = fs,
					Id = pid,
					Process = Foreground,
					Executable = Foreground.ProcessName,
				};

				if (DebugForeground && ShowInaction)
					Log.Debug("<Foreground> Active #" + activewindowev.Id.ToString() + ": " + activewindowev.Title);

				if (Utility.SystemProcessId(pid))
				{
					if (DebugForeground) Log.Debug("<Foreground> System process in foreground: " + pid.ToString());

					Reset();
				}

				if (disposed || LastEvent != ev) return; // more events have arrived or disposed

				ActiveChanged?.Invoke(this, activewindowev);
			}
			catch (ArgumentException)
			{
				// Process already gone probably
				Reset();
			}
			catch (ObjectDisposedException)
			{
				Reset();
				Statistics.DisposedAccesses++;
			}
			catch (InvalidOperationException)
			{
				// process exited most likely

				Reset();

				ActiveChanged?.Invoke(this, new WindowChangedArgs { Id = -1 });
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				Reset();
				return; // HACK, WndProc probably shouldn't throw
			}
		}

		void Reset()
		{
			lock (FGLock)
			{
				Foreground = null;
				ForegroundId = -1;
			}
		}

		#region IDisposable
		public event EventHandler<DisposedEventArgs>? OnDisposed;

		~ForegroundManager() => Dispose(false);

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		bool disposed = false;

		void Dispose(bool disposing)
		{
			if (disposed) return;
			disposed = true;

			global::Taskmaster.NativeMethods.UnhookWinEvent(windowseventhook); // Automatic

			if (disposing)
			{
				if (Trace) Log.Verbose("Disposing FG monitor...");

				ActiveChanged = null;
				HangTimer.Dispose();

				//base.Dispose();

				OnDisposed?.Invoke(this, DisposedEventArgs.Empty);
				OnDisposed = null;
			}
		}
		#endregion

		public void ShutdownEvent(object sender, EventArgs ea)
		{
			global::Taskmaster.NativeMethods.UnhookWinEvent(windowseventhook); // Automatic
			HangTimer.Stop();
		}
	}
}