//
// Power.Manager.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2018–2019 M.A.
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
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using MKAh;
using MKAh.Human.Readable;
using Ini = MKAh.Ini;
using Serilog;
using MKAh.Ini;

namespace Taskmaster.Power
{
	using static Taskmaster;

	sealed public class ModeEventArgs : EventArgs
	{
		public ModeEventArgs(Mode newmode, Mode oldmode = Mode.Undefined, Cause cause = null)
		{
			NewMode = newmode;
			OldMode = oldmode;
			Cause = cause;
		}

		public Mode OldMode { get; set; } = Mode.Undefined;
		public Mode NewMode { get; set; } = Mode.Undefined;
		public Cause Cause { get; set; } = null;
	}

	sealed public class PowerRequestEventArgs : EventArgs
	{
		public Mode Mode { get; set; } = Mode.Undefined;
		public ProcessEx Info { get; set; } = null;

		public PowerRequestEventArgs(Mode mode, ProcessEx info)
		{
			Mode = mode;
			Info = info;
		}
	}

	public enum MonitorPowerMode
	{
		On = -1,
		Off = 2,
		Standby = 1,
		Invalid = 0
	}

	public class MonitorPowerEventArgs : EventArgs
	{
		public MonitorPowerMode Mode;
		public MonitorPowerEventArgs(MonitorPowerMode mode) => Mode = mode;
	}

	public class SessionLockEventArgs : EventArgs
	{
		public bool Locked = false;
		public SessionLockEventArgs(bool locked = false) => Locked = locked;
	}

	sealed public class Manager : Form, IDisposal // form is required for receiving messages, no other reason
	{
		const string DefaultModeSettingName = "Default mode";
		const string RestoreModeSettingName = "Restore mode";
		const string LowBackOffLevelName = "Low backoff level";
		const string HighBackoffLevelName = "High backoff level";
		const string LowCommitLevelName = "Low commit level";
		const string HighCommitLevelName = "High commit level";
		const string HighThresholdName = "High threshold";
		const string HighBackoffThresholdsName = "High backoff thresholds";
		const string LowThresholdName = "Low threshold";
		const string LowBackoffThresholdsName = "Low backoff thresholds";
		const string LowModeName = "Low mode";
		const string HighModeName = "High mode";
		const string HighQueueBarrierName = "High queue barrier";
		const string LowQueueBarrierName = "Low queue barrier";
		const string SessionLockName = "Session lock";
		const string AFKPowerName = "AFK Power";
		const string InitialName = "Initial";

		bool VerbosePowerRelease = false;

		// static Guid GUID_POWERSCHEME_PERSONALITY = new Guid("245d8541-3943-4422-b025-13A7-84F679B7");
		/// <summary>
		/// Power mode notifications
		/// </summary>
		static Guid GUID_POWERSCHEME_PERSONALITY = new Guid(0x245D8541, 0x3943, 0x4422, 0xB0, 0x25, 0x13, 0xA7, 0x84, 0xF6, 0x79, 0xB7);
		/// <summary>
		/// Monitor state notifications
		/// </summary>
		static Guid GUID_CONSOLE_DISPLAY_STATE = new Guid(0x6fe69556, 0x704a, 0x47a0, 0x8f, 0x24, 0xc2, 0x8d, 0x93, 0x6f, 0xda, 0x47);

		public event EventHandler<SessionLockEventArgs> SessionLock;
		public event EventHandler<MonitorPowerEventArgs> MonitorPower;

		bool DebugAutoPower = false;

		public Manager()
		{
			ExpectedMode = OriginalMode = getPowerMode();

			AutoAdjust = PowerAutoadjustPresets.Default();

			LoadConfig();

			// SystemEvents.PowerModeChanged += BatteryChargingEvent; // Without laptop testing this feature is difficult

			if (Behaviour == PowerBehaviour.RuleBased && !Forced)
				Restore(new Cause(OriginType.Internal, InitialName));

			if (SessionLockPowerOffIdleTimeout.HasValue)
			{
				var stopped = System.Threading.Timeout.InfiniteTimeSpan;
				MonitorSleepTimer = new System.Timers.Timer(60_000);
				MonitorSleepTimer.Elapsed += MonitorSleepTimerTick;

				MonitorPower += MonitorPowerEvent;
			}

			RegisterForExit(this);
			DisposalChute.Push(this);
		}

		public int ForceCount => ForceModeSourcesMap.Count;
		ConcurrentDictionary<int, Mode> ForceModeSourcesMap = new ConcurrentDictionary<int, Mode>();

		CPUMonitor cpumonitor = null;

		public void Hook(CPUMonitor monitor)
		{
			cpumonitor = monitor;
			cpumonitor.OnDisposed += (_, _ea) => cpumonitor = null;
			cpumonitor.onSampling += CPULoadHandler;
		}

		void MonitorPowerEvent(object _, MonitorPowerEventArgs ev)
		{
			if (DisposedOrDisposing) return;

			try
			{
				var OldPowerState = CurrentMonitorState;
				CurrentMonitorState = ev.Mode;

				if (DebugMonitor)
				{
					var idle = User.IdleTime();
					double sidletime = Time.Simplify(idle, out Time.Timescale scale);
					var timename = Time.TimescaleString(scale, !sidletime.RoughlyEqual(1d));

					//uint lastact = User.LastActive();
					//var idle = User.LastActiveTimespan(lastact);
					//if (lastact == uint.MinValue) idle = TimeSpan.Zero; // HACK

					Log.Debug($"<Monitor> Power state: {ev.Mode.ToString()} (last user activity {sidletime:N1} {timename} ago)");
				}

				if (OldPowerState == CurrentMonitorState)
				{
					Log.Debug($"Received monitor power event: {OldPowerState.ToString()} → {ev.Mode.ToString()}");
					return; //
				}

				if (ev.Mode == MonitorPowerMode.On && SessionLocked)
				{
					if (SessionLockPowerMode != Mode.Undefined)
					{
						lock (power_lock)
						{
							if (CurrentMode == SessionLockPowerMode)
								InternalSetMode(Mode.Balanced, new Cause(OriginType.Session, "User activity"), verbose: false);
						}
					}

					StartDisplayTimer();
					MonitorOffLastLock?.Stop();

					if (DebugMonitor) DebugMonitorWake().ConfigureAwait(false);
				}
				else if (ev.Mode == MonitorPowerMode.Off)
				{
					StopDisplayTimer();
					if (SessionLocked) MonitorOffLastLock?.Start();
				}
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		async Task DebugMonitorWake()
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException("DebugMonitorWake called after PowerManager was disposed.");

			await Task.Delay(0).ConfigureAwait(false);

			string pcfg = "PowerCfg";

			var timer = Stopwatch.StartNew();

			ProcessStartInfo info = null;

			info = new ProcessStartInfo(pcfg, "-lastwake")
			{
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				UseShellExecute = false
			};
			using (var proc = System.Diagnostics.Process.Start(info))
			{
				Debug.WriteLine($"{info.FileName} {info.Arguments}");
				while (!proc.StandardOutput.EndOfStream)
				{
					if (timer.ElapsedMilliseconds > 30_000) return;
					Debug.WriteLine(proc.StandardOutput.ReadLine());
				}
			}

			info = new ProcessStartInfo(pcfg, "-RequestsOverride")
			{
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				UseShellExecute = false
			};
			using (var proc = System.Diagnostics.Process.Start(info))
			{
				Debug.WriteLine($"{info.FileName} {info.Arguments}");
				while (!proc.StandardOutput.EndOfStream)
				{
					if (timer.ElapsedMilliseconds > 30_000) return;
					Debug.WriteLine(proc.StandardOutput.ReadLine());
				}
			}

			info = new ProcessStartInfo(pcfg, "-Requests")
			{
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				UseShellExecute = false
			};
			using (var proc = System.Diagnostics.Process.Start(info))
			{
				Debug.WriteLine($"{info.FileName} {info.Arguments}");
				while (!proc.StandardOutput.EndOfStream)
				{
					if (timer.ElapsedMilliseconds > 30_000) return;
					Debug.WriteLine(proc.StandardOutput.ReadLine());
				}
			}
		}

		void StopDisplayTimer(bool reset = false)
		{
			if (reset) SleepTickCount = -1;
			MonitorSleepTimer?.Stop();
		}

		void StartDisplayTimer()
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException("StartDisplayTimer called after PowerManager was disposed.");

			if (SleepTickCount < 0) SleepTickCount = 0; // reset
			MonitorSleepTimer?.Start();
		}

		System.Timers.Timer MonitorSleepTimer = null;

		int SleepTickCount = -1;
		int SleepGivenUp = 0;
		int monitorsleeptimer_lock = 0;
		void MonitorSleepTimerTick(object _, EventArgs _ea)
		{
			if (DisposedOrDisposing) return;

			var idle = User.IdleTime();

			if (SessionLockPowerMode != Mode.Undefined)
			{
				lock (power_lock)
				{
					if (CurrentMode != SessionLockPowerMode && idle.TotalMinutes > 2d)
						InternalSetMode(SessionLockPowerMode, new Cause(OriginType.Session, "User inactivity"), verbose: false);
				}
			}

			if (CurrentMonitorState == MonitorPowerMode.Off || !SessionLocked || SleepTickCount < 0)
			{
				StopDisplayTimer();
				return;
			}

			if (!Atomic.Lock(ref monitorsleeptimer_lock)) return;

			try
			{
				if (SleepTickCount >= 5)
				{
					// it would be better if this wasn't needed, but we don't want to spam our failure in the logs too much
					if (SleepGivenUp == 0)
						Log.Warning("<Session:Lock> Repeated failure to put monitor to sleep. Other apps may be interfering");
					else if (SleepGivenUp == 1)
						Log.Warning("<Session:Lock> Monitor sleep failures persist, stopping logging.");
					// TODO: Detect other apps that have used SetThreadExecutionState(ES_CONTINUOUS) to prevent monitor sleep
					// ... this is supposedly not possible.
					SleepGivenUp++;

					//StopDisplayTimer(reset: true); // stop sleep attempts
				}
				else if (idle >= SessionLockPowerOffIdleTimeout)
				{
					SleepTickCount++;

					double sidletime = Time.Simplify(idle, out Time.Timescale scale);
					var timename = Time.TimescaleString(scale, !sidletime.RoughlyEqual(1d));

					if ((ShowSessionActions && SleepGivenUp <= 1) || DebugMonitor)
						Log.Information($"<Session:Lock> User idle ({sidletime:N1} {timename}); Monitor power down, attempt {SleepTickCount}...");

					SetMonitorMode(MonitorPowerMode.Off);
				}
				else
				{
					if (ShowSessionActions || DebugMonitor)
						Log.Information($"<Session:Lock> User active too recently ({idle.TotalSeconds:N1}s ago), delaying monitor power down...");

					StartDisplayTimer(); // TODO: Make this happen sooner if user was not active recently
				}
			}
			finally
			{
				Atomic.Unlock(ref monitorsleeptimer_lock);
			}
		}

		MonitorPowerMode CurrentMonitorState = MonitorPowerMode.Invalid;

		/// <summary>
		/// Requires main thread?
		/// </summary>
		public void SetupEventHook()
		{
			NativeMethods.RegisterPowerSettingNotification(
				Handle, ref GUID_POWERSCHEME_PERSONALITY, NativeMethods.DEVICE_NOTIFY_WINDOW_HANDLE);
			NativeMethods.RegisterPowerSettingNotification(
				Handle, ref GUID_CONSOLE_DISPLAY_STATE, NativeMethods.DEVICE_NOTIFY_WINDOW_HANDLE);

			SystemEvents.SessionSwitch += SessionLockEvent; // BUG: this is wrong, TM should pause to not interfere with other users
		}

		/// <summary>
		/// Power saver on monitor sleep
		/// </summary>
		bool SaverOnMonitorSleep = false;
		/// <summary>
		/// Session lock power mode
		/// </summary>
		public Mode SessionLockPowerMode { get; set; } = Mode.PowerSaver;
		/// <summary>
		/// Power saver on log off
		/// </summary>
		bool SaverOnLogOff = false;
		/// <summary>
		/// Power saver on screen saver
		/// </summary>
		bool SaverOnScreensaver = false;
		/// <summary>
		/// Cancel any power modes on user activity.
		/// </summary>
		bool UserActiveCancel = true;
		/// <summary>
		/// Power saver after user idle for # minutes.
		/// </summary>
		int SaverOnUserAFK = 0;

		/// <summary>
		/// User must be inactive for this many seconds.
		/// </summary>
		TimeSpan? SessionLockPowerOffIdleTimeout = TimeSpan.FromSeconds(120);
		/// <summary>
		/// Power off monitor directly on lock off.
		/// </summary>
		public bool SessionLockPowerOff { get; set; } = true;

		public event EventHandler<AutoAdjustReactionEventArgs> onAutoAdjustAttempt;
		public event EventHandler<ModeEventArgs> onPlanChange;
		public event EventHandler<PowerBehaviourEventArgs> onBehaviourChange;
		public event EventHandler onSuspendResume;

		public enum Reaction
		{
			High = 1,
			Average = 0,
			Low = -1,
		}

		bool Paused = false;
		public bool SessionLocked { get; private set; } = false;
		/// <summary>
		/// Power mode is forced, auto-adjust and other automatic changes are disabled.
		/// </summary>
		bool Forced = false;

		long AutoAdjustCounter = 0;

		public AutoAdjustSettings AutoAdjust { get; private set; } = new AutoAdjustSettings();
		readonly object autoadjust_lock = new object();

		public void SetAutoAdjust(AutoAdjustSettings settings)
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException("SetAutoAdjust called after PowerManager was disposed.");

			lock (autoadjust_lock)
			{
				AutoAdjust = settings;

				if (RestoreMethod == RestoreModeMethod.Default)
					SetRestoreMode(RestoreMethod, RestoreMode);
			}
			// TODO: Call reset on power manager?
		}

		int HighPressure = 0;
		int LowPressure = 0;
		float queuePressureAdjust = 0f;
		Reaction PreviousReaction = Reaction.Average;

		// TODO: Simplify this mess
		public void CPULoadHandler(object _, ProcessorLoadEventArgs pev)
		{
			if (DisposedOrDisposing) return;

			if (Behaviour != PowerBehaviour.Auto) return;

			var ev = AutoAdjustReactionEventArgs.From(pev);

			lock (autoadjust_lock)
			{
				var Reaction = Power.Manager.Reaction.Average;

				var Ready = false;

				ev.Pressure = 0;
				ev.Enacted = false;

				//Debug.WriteLine("AUTO-ADJUST: Previous Reaction: " + PreviousReaction.ToString());
				//Debug.WriteLine("AUTO-ADJUST: Queue Length: " + ev.Queue.ToString());
				if (PreviousReaction == Reaction.High)
				{
					// Backoff from High to Medium power level
					if (ev.Queue <= AutoAdjust.Queue.High
						&& (ev.High <= AutoAdjust.High.Backoff.High
						|| ev.Mean <= AutoAdjust.High.Backoff.Mean
						|| ev.Low <= AutoAdjust.High.Backoff.Low))
					{
						//Debug.WriteLine("AUTO-ADJUST: Motive: High to Average");
						Reaction = Reaction.Average;

						BackoffCounter++;

						if (BackoffCounter >= AutoAdjust.High.Backoff.Level)
							Ready = true;

						queuePressureAdjust = (AutoAdjust.Queue.High > 0 ? ev.Queue / AutoAdjust.Queue.High : 0f);
						//Debug.WriteLine("AUTO-ADJUST: Queue pressure adjust: " + $"{queuePressureAdjust:N1}");
						ev.Pressure = ((float)BackoffCounter) / ((float)AutoAdjust.High.Backoff.Level) - queuePressureAdjust;
						//Debug.WriteLine("AUTO-ADJUST: Final pressure: " + $"{ev.Pressure:N1}");
					}
					else
					{
						//Debug.WriteLine("AUTO-ADJUST: Motive: High - Steady");
						Reaction = Reaction.High;
						ev.Steady = true;
					}
				}
				else if (PreviousReaction == Reaction.Low)
				{
					// Backoff from Low to Medium power level
					if (ev.Queue >= AutoAdjust.Queue.Low
						|| ev.High >= AutoAdjust.Low.Backoff.High
						|| ev.Mean >= AutoAdjust.Low.Backoff.Mean
						|| ev.Low >= AutoAdjust.Low.Backoff.Low)
					{
						//Debug.WriteLine("AUTO-ADJUST: Motive: Low to Average");
						Reaction = Reaction.Average;

						BackoffCounter++;

						if (BackoffCounter >= AutoAdjust.Low.Backoff.Level)
							Ready = true;

						queuePressureAdjust = (AutoAdjust.Queue.Low > 0 ? ev.Queue / AutoAdjust.Queue.Low : 0);
						//Debug.WriteLine("AUTO-ADJUST: Queue pressure adjust: " + $"{queuePressureAdjust:N1}");
						ev.Pressure = ((float)BackoffCounter) / ((float)AutoAdjust.Low.Backoff.Level) + queuePressureAdjust;
						//Debug.WriteLine("AUTO-ADJUST: Final pressure: " + $"{ev.Pressure:N1}");
					}
					else
					{
						//Debug.WriteLine("AUTO-ADJUST: Motive: Low - Steady");
						Reaction = Reaction.Low;
						ev.Steady = true;
					}
				}
				else // Currently at medium power
				{
					if (ev.Low > AutoAdjust.High.Commit.Threshold // Low CPU is above threshold for High mode
						&& AutoAdjust.High.Mode != CurrentMode)
					{
						//Debug.WriteLine("AUTO-ADJUST: Motive: Average to High");
						// Commit to High power level
						Reaction = Reaction.High;

						LowPressure = 0; // reset
						HighPressure++;

						if (HighPressure >= AutoAdjust.High.Commit.Level)
							Ready = true;

						queuePressureAdjust = (AutoAdjust.Queue.High > 0 ? ev.Queue / 5 : 0); // 20% per queued thread
						//Debug.WriteLine("AUTO-ADJUST: Queue pressure adjust: " + $"{queuePressureAdjust:N1}");
						ev.Pressure = ((float)HighPressure) / ((float)AutoAdjust.High.Commit.Level) + queuePressureAdjust;
						//Debug.WriteLine("AUTO-ADJUST: Final pressure: " + $"{ev.Pressure:N1}");
					}
					else if (ev.Queue < AutoAdjust.Queue.Low
						&& ev.High < AutoAdjust.Low.Commit.Threshold // High CPU is below threshold for Low mode
						&& AutoAdjust.Low.Mode != CurrentMode)
					{
						//Debug.WriteLine("AUTO-ADJUST: Motive: Average to Low");
						// Commit to Low power level
						Reaction = Reaction.Low;

						HighPressure = 0; // reset
						LowPressure++;

						if (LowPressure >= AutoAdjust.Low.Commit.Level)
							Ready = true;

						queuePressureAdjust = (AutoAdjust.Queue.High > 0 ? ev.Queue / AutoAdjust.Queue.High : 0);
						//Debug.WriteLine("AUTO-ADJUST: Queue pressure adjust: " + $"{queuePressureAdjust:N1}");
						ev.Pressure = ((float)LowPressure) / ((float)AutoAdjust.Low.Commit.Level) + queuePressureAdjust;
						//Debug.WriteLine("AUTO-ADJUST: Final pressure: " + $"{ev.Pressure:N1}");
					}
					else // keep power at medium
					{
						//Debug.WriteLine("AUTO-ADJUST: Motive: Average - Steady");
						if (DebugAutoPower) Debug.WriteLine("Auto-adjust NOP");

						Reaction = Reaction.Average;
						ev.Steady = true;

						ResetAutoadjust();

						// Only time this should cause actual power mode change is when something else changes power mode
						Ready = true;
						//ev.Pressure = 1; // required for actually changing mode
					}
				}

				var ReadyToAdjust = (Ready && !Forced && !SessionLocked);

				var ReactionaryPlan = ReactionToMode(Reaction);

				if (ReadyToAdjust && ev.Pressure >= 1f && ReactionaryPlan != CurrentMode)
				{
					if (DebugPower) Log.Debug("<Power> Auto-adjust: " + Reaction.ToString());

					string explanation = string.Empty;
					if (CurrentMode != Mode.Balanced && Reaction == Reaction.Average)
						explanation = $"Backing off from {PreviousReaction}";
					else
						explanation = $"Committing to {Reaction}";

					if (AutoAdjustSetMode(ReactionaryPlan, new Cause(OriginType.AutoAdjust, explanation)))
					{
						AutoAdjustCounter++;
						ev.Enacted = true;
					}
					else
					{
						if (DebugPower && Trace) Log.Warning("<Power> Failed to auto-adjust power.");
						// should reset
					}

					ResetAutoadjust();

					PreviousReaction = Reaction;
				}
				else
				{
					if (Forced)
					{
						if (DebugPower && ShowInaction && !WarnedForceMode)
							Log.Debug("<Power> Can't override forced power mode.");
						WarnedForceMode = true;
					}
					else if (ReadyToAdjust)
					{
						// Should probably reset on >=1.0 pressure regardless of anything.

						if (ReactionaryPlan == CurrentMode && ev.Pressure > 1.0)
						{
							// This usually happens when auto-adjust is paused and pressure keeps building.
							// Harmless and kind of expected.
							if (DebugPower) Log.Debug("<Power> Something went wrong. Resetting auto-adjust.");

							// Reset
							ResetAutoadjust();
							ev.Pressure = 0f;
						}
					}
				}

				ev.Reaction = Reaction;
				ev.Mode = ReactionaryPlan;
			} // lock (autoadjust_lock)

			onAutoAdjustAttempt?.Invoke(this, ev);
		}

		Mode ReactionToMode(Reaction reaction)
		{
			Mode mode = Mode.Undefined;
			switch (reaction)
			{
				case Reaction.High:
					mode = AutoAdjust.High.Mode;
					break;
				default:
				case Reaction.Average:
					mode = AutoAdjust.DefaultMode;
					break;
				case Reaction.Low:
					mode = AutoAdjust.Low.Mode;
					break;
			}
			return mode;
		}

		bool WarnedForceMode = false;

		void ResetAutoadjust()
		{
			BackoffCounter = HighPressure = LowPressure = 0;
			PreviousReaction = Reaction.Average;
			WarnedForceMode = false;
		}

		public TimeSpan? PowerdownDelay { get; set; } = null;

		void LoadConfig()
		{
			using (var corecfg = Config.Load(CoreConfigFilename).BlockUnload())
			{
				var power = corecfg.Config[HumanReadable.Hardware.Power.Section];

				var behaviourstring = power.GetOrSet(Constants.Behaviour, HumanReadable.Hardware.Power.RuleBased)
					.InitComment("auto, manual, or rule-based")
					.Value;

				if (behaviourstring.StartsWith("auto", StringComparison.InvariantCultureIgnoreCase))
					LaunchBehaviour = PowerBehaviour.Auto;
				else if (behaviourstring.StartsWith("manual", StringComparison.InvariantCultureIgnoreCase))
					LaunchBehaviour = PowerBehaviour.Manual;
				else
					LaunchBehaviour = PowerBehaviour.RuleBased;
				Behaviour = LaunchBehaviour;

				AutoAdjust.DefaultMode = GetModeByName(power.GetOrSet(DefaultModeSettingName, GetModeName(Mode.Balanced))
					.InitComment("This is what power plan we fall back on when nothing else is considered.")
					.Value);
				if (AutoAdjust.DefaultMode == Mode.Undefined)
				{
					Log.Warning("<Power> Default mode malconfigured, defaulting to balanced.");
					AutoAdjust.DefaultMode = Mode.Balanced;
				}

				var restoremode = power.GetOrSet(RestoreModeSettingName, "Default")
					.InitComment("Default, Original, Saved, or specific power mode. Power mode to restore with rule-based behaviour.")
					.Value.ToLowerInvariant();

				RestoreModeMethod newmodemethod = RestoreModeMethod.Default;
				Mode newrestoremode = Mode.Undefined;

				switch (restoremode)
				{
					case "original":
						newmodemethod = RestoreModeMethod.Original;
						newrestoremode = OriginalMode;
						break;
					case "default":
						newmodemethod = RestoreModeMethod.Default;
						newrestoremode = AutoAdjust.DefaultMode;
						break;
					case "saved":
						newmodemethod = RestoreModeMethod.Saved;
						newrestoremode = Mode.Undefined;
						break;
					default:
						newmodemethod = RestoreModeMethod.Custom;
						newrestoremode = GetModeByName(restoremode);
						if (RestoreMode == Mode.Undefined)
						{
							// TODO: Complain about bad config
							Log.Warning("<Power> Restore mode name unintelligible.");
							newrestoremode = AutoAdjust.DefaultMode;
						}
						break;
				}

				SetRestoreMode(newmodemethod, newrestoremode);

				var tdelay = power.GetOrSet("Watchlist powerdown delay", 0)
					.InitComment("Delay, in seconds (0 to 300, 0 disables), for when to wind down power mode set by watchlist.")
					.Int.Constrain(0, 60 * 5);
				if (tdelay > 0) PowerdownDelay = TimeSpan.FromSeconds(tdelay);
				else PowerdownDelay = null;

				var autopower = corecfg.Config["Power / Auto"];

				// BACKOFF
				AutoAdjust.Low.Backoff.Level = autopower.GetOrSet(LowBackOffLevelName, AutoAdjust.Low.Backoff.Level)
					.InitComment("1 to 10. Consequent backoff reactions that is required before it actually triggers.")
					.Int.Constrain(0, 10);

				AutoAdjust.High.Backoff.Level = autopower.GetOrSet(HighBackoffLevelName, AutoAdjust.High.Backoff.Level)
					.InitComment("1 to 10. Consequent backoff reactions that is required before it actually triggers.")
					.Int.Constrain(0, 10);

				// COMMIT
				AutoAdjust.Low.Commit.Level = autopower.GetOrSet(LowCommitLevelName, AutoAdjust.Low.Commit.Level)
					.InitComment("1 to 10. Consequent commit reactions that is required before it actually triggers.")
					.Int.Constrain(1, 10);

				AutoAdjust.High.Commit.Level = autopower.GetOrSet(HighCommitLevelName, AutoAdjust.High.Backoff.Level)
					.InitComment("1 to 10. Consequent commit reactions that is required before it actually triggers.")
					.Int.Constrain(1, 10);

				// THRESHOLDS
				AutoAdjust.High.Commit.Threshold = autopower.GetOrSet(HighThresholdName, AutoAdjust.High.Commit.Threshold)
					.InitComment("If low CPU value keeps over this, we swap to high mode.")
					.Float;

				var hbtt = autopower.GetOrSet(HighBackoffThresholdsName, new float[] { AutoAdjust.High.Backoff.High, AutoAdjust.High.Backoff.Mean, AutoAdjust.High.Backoff.Low })
					.InitComment("High, Mean and Low CPU usage values, any of which is enough to break away from high power mode.")
					.FloatArray;
				if (hbtt != null && hbtt.Length == 3)
				{
					AutoAdjust.High.Backoff.Low = hbtt[2];
					AutoAdjust.High.Backoff.Mean = hbtt[1];
					AutoAdjust.High.Backoff.High = hbtt[0];
				}

				AutoAdjust.Low.Commit.Threshold = autopower.GetOrSet(LowThresholdName, 15)
					.InitComment("If high CPU value keeps under this, we swap to low mode.")
					.Float;

				var lbtt = autopower.GetOrSet(LowBackoffThresholdsName, new float[] { AutoAdjust.Low.Backoff.High, AutoAdjust.Low.Backoff.Mean, AutoAdjust.Low.Backoff.Low })
					.InitComment("High, Mean and Low CPU uage values, any of which is enough to break away from low mode.")
					.FloatArray;
				if (lbtt != null && lbtt.Length == 3)
				{
					AutoAdjust.Low.Backoff.Low = lbtt[2];
					AutoAdjust.Low.Backoff.Mean = lbtt[1];
					AutoAdjust.Low.Backoff.High = lbtt[0];
				}

				// POWER MODES
				AutoAdjust.Low.Mode = GetModeByName(power.GetOrSet(LowModeName, GetModeName(Mode.PowerSaver)).Value);
				AutoAdjust.High.Mode = GetModeByName(power.GetOrSet(HighModeName, GetModeName(Mode.HighPerformance)).Value);

				// QUEUE BARRIERS
				AutoAdjust.Queue.High = autopower.GetOrSet(HighQueueBarrierName, AutoAdjust.Queue.High).Int.Constrain(0, 50);
				AutoAdjust.Queue.Low = autopower.GetOrSet(LowQueueBarrierName, AutoAdjust.Queue.Low).Int.Constrain(0, 20);
				if (AutoAdjust.Queue.Low >= AutoAdjust.Queue.High) AutoAdjust.Queue.Low = Math.Max(0, AutoAdjust.Queue.High - 1);

				var saver = corecfg.Config[AFKPowerName];
				//saver.Comment = "All these options control when to enforce power save mode regardless of any other options.";

				var sessionlockmodename = saver.GetOrSet(SessionLockName, GetModeName(Mode.PowerSaver))
					.InitComment("Power mode to set when session is locked, such as by pressing winkey+L. Unrecognizable values disable this.")
					.Value;
				SessionLockPowerMode = GetModeByName(sessionlockmodename);

				// SaverOnMonitorSleep = saver.GetSetDefault("Monitor sleep", true, out modified).Bool;
				// dirtyconfig |= modified;

				// SaverOnUserAFK = saver.GetSetDefault("User idle", 30, out modified).Int;
				// dirtyconfig |= modified;
				// UserActiveCancel = saver.GetSetDefault("Cancel on activity", true, out modified).Bool;
				// dirtyconfig |= modified;

				int monoffidletime = saver.GetOrSet("Monitor power off idle timeout", 180)
					.InitComment("User needs to be this many seconds idle before we power down monitors when session is locked. 0 disables. Less than 30 is rounded up to 30.")
					.Int;
				SessionLockPowerOffIdleTimeout = monoffidletime > 0 ? (TimeSpan?)TimeSpan.FromSeconds(monoffidletime.Constrain(30, 600)) : null;

				SessionLockPowerOff = saver.GetOrSet("Monitor power off on lock", true)
					.InitComment("Power off monitor instantly on session lock.")
					.Bool;

				var dbgsec = corecfg.Config[HumanReadable.Generic.Debug];
				DebugAutoPower = dbgsec.Get(HumanReadable.Hardware.Power.AutoAdjust)?.Bool ?? false;
			}

			// --------------------------------------------------------------------------------------------------------

			// --------------------------------------------------------------------------------------------------------

			Log.Information("<Power> Behaviour: " + GetBehaviourName(Behaviour) + "; Restore mode: " + RestoreMethod.ToString() + " [" + RestoreMode.ToString() + "]");

			Log.Information("<Power> Watchlist powerdown delay: " +
				(PowerdownDelay.HasValue ? $"{PowerdownDelay.Value.TotalSeconds:N0} s" : HumanReadable.Generic.Disabled));

			Log.Information("<Session> On session lock – User AFK timeout: " + (SessionLockPowerOffIdleTimeout.HasValue ? $"{SessionLockPowerOffIdleTimeout.Value.TotalSeconds:N0}s" : HumanReadable.Generic.Disabled)
				+ "; Monitor power off: " + (SessionLockPowerOff ? "Immediate" : "Delayed")
				+ "; Power mode: " + (SessionLockPowerMode == Mode.Undefined ? HumanReadable.Generic.Ignore : SessionLockPowerMode.ToString()));
		}

		// TODO: Should detect if saving is ACTUALLY needed
		public void SaveConfig()
		{
			using (var corecfg = Config.Load(CoreConfigFilename).BlockUnload())
			{
				var power = corecfg.Config[HumanReadable.Hardware.Power.Section];

				lock (autoadjust_lock)
				{
					lock (power_lock)
					{
						string sbehaviour = HumanReadable.Hardware.Power.RuleBased.ToLower(); // default to rule-based
						switch (LaunchBehaviour)
						{
							case PowerBehaviour.Auto:
								sbehaviour = HumanReadable.Hardware.Power.AutoAdjust.ToLower();
								break;
							case PowerBehaviour.Manual:
								sbehaviour = HumanReadable.Hardware.Power.Manual.ToLower();
								break;
							default: break; // ignore
						}
						power[Constants.Behaviour].Value = sbehaviour;

						power[DefaultModeSettingName].Value = AutoAdjust.DefaultMode.ToString();

						power[RestoreModeSettingName].Value = (RestoreMethod == RestoreModeMethod.Custom ? RestoreMode.ToString() : RestoreMethod.ToString());
						if (PowerdownDelay.HasValue)
							power["Watchlist powerdown delay"].Int = Convert.ToInt32(PowerdownDelay.Value.TotalSeconds);
						else
							power.TryRemove("Watchlist powerdown delay");

						var autopower = corecfg.Config["Power / Auto"];

						// BACKOFF
						autopower[LowBackOffLevelName].Int = AutoAdjust.Low.Backoff.Level;
						autopower[HighBackoffLevelName].Int = AutoAdjust.High.Backoff.Level;

						// COMMIT
						autopower[LowCommitLevelName].Int = AutoAdjust.Low.Commit.Level;
						autopower[HighCommitLevelName].Int = AutoAdjust.High.Commit.Level;

						// THRESHOLDS
						autopower[HighThresholdName].Float = AutoAdjust.High.Commit.Threshold;
						autopower[HighBackoffThresholdsName].FloatArray = new float[] { AutoAdjust.High.Backoff.High, AutoAdjust.High.Backoff.Mean, AutoAdjust.High.Backoff.Low };

						autopower[LowThresholdName].Float = AutoAdjust.Low.Commit.Threshold;
						autopower[LowBackoffThresholdsName].FloatArray = new float[] { AutoAdjust.Low.Backoff.High, AutoAdjust.Low.Backoff.Mean, AutoAdjust.Low.Backoff.Low };

						// QUEUE BARRIERS
						autopower[HighQueueBarrierName].Int = AutoAdjust.Queue.High;
						autopower[LowQueueBarrierName].Int = AutoAdjust.Queue.Low;

						// POWER MODES
						power[LowModeName].Value = GetModeName(AutoAdjust.Low.Mode);
						power[HighModeName].Value = GetModeName(AutoAdjust.High.Mode);

						var saver = corecfg.Config[AFKPowerName];
						saver[SessionLockName].Value = GetModeName(SessionLockPowerMode);

						saver["Monitor power off idle timeout"].Int = Convert.ToInt32(SessionLockPowerOffIdleTimeout.Value.TotalSeconds);
						saver["Monitor power off on lock"].Bool = SessionLockPowerOff;

						// --------------------------------------------------------------------------------------------------------
					}
				}
			}
		}

		public void LogBehaviourState()
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException("LogBehaviourState called after PowerManager was disposed.");

			Log.Information("<Power> Behaviour: " + GetBehaviourName(Behaviour));
		}

		int BackoffCounter { get; set; } = 0;

		public enum RestoreModeMethod
		{
			Original,
			Saved,
			Default,
			Custom
		};

		public RestoreModeMethod RestoreMethod { get; private set; } = RestoreModeMethod.Default;
		public Mode RestoreMode { get; private set; } = Mode.Balanced;

		Stopwatch SessionLockCounter = null;
		Stopwatch MonitorOffLastLock = null;
		Stopwatch MonitorPowerOffCounter = new Stopwatch(); // unused?

		async void SessionLockEvent(object _, SessionSwitchEventArgs ev)
		{
			if (DisposedOrDisposing) return;

			bool loudMonitor = (ShowSessionActions || DebugSession || DebugMonitor),
				loudPower = (ShowSessionActions || DebugSession || DebugPower);

			// BUG: ODD BEHAVIOUR ON ACCOUNT SWAP
			switch (ev.Reason)
			{
				case SessionSwitchReason.SessionLogoff:
					Log.Information("<Session> Logoff detected. Exiting.");
					UnifiedExit(restart:false);
					return;
				case SessionSwitchReason.SessionLock:
					SessionLocked = true;
					MonitorOffLastLock = Stopwatch.StartNew();
					SessionLockCounter = Stopwatch.StartNew();
					// TODO: Pause most of TM's functionality to avoid problems with account swapping
					break;
				case SessionSwitchReason.SessionUnlock:
					SessionLocked = false;
					MonitorOffLastLock?.Stop();
					SessionLockCounter?.Stop();
					break;
			}

			if (DebugSession) Log.Debug("<Session> State: " + (SessionLocked ? "Locked" : "Unlocked"));

			await Task.Delay(0).ConfigureAwait(false); // async

			if (SessionLocked)
			{
				if (CurrentMonitorState != MonitorPowerMode.Off)
				{
					if (SessionLockPowerOff)
					{
						if (loudMonitor) Log.Information("<Session:Lock> Instant monitor power off.");

						SetMonitorMode(MonitorPowerMode.Off);
					}
					else
					{
						if (loudMonitor) Log.Information("<Session:Lock> Instant monitor power off disabled, waiting for user idle.");

						StartDisplayTimer();
					}
				}
				else
				{
					if (loudMonitor) Log.Information("<Session:Lock> Monitor already off, leaving it be.");
				}
			}
			else
			{
				StopDisplayTimer(reset:true);

				// should be unnecessary, but...
				if (CurrentMonitorState != MonitorPowerMode.On) // session unlocked but monitor still off?
				{
					Log.Warning("<Session:Unlock> Monitor still not on... Concerning, isn't it?");
					SetMonitorMode(MonitorPowerMode.On); // attempt to wake it
				}

				if (SessionLockPowerOff)
				{
					var off = MonitorOffLastLock?.Elapsed ?? TimeSpan.Zero;
					var total = SessionLockCounter?.Elapsed ?? TimeSpan.Zero;
					double percentage = off.TotalMinutes / total.TotalMinutes;

					Log.Information("<Session:Unlock> Monitor off time: " + $"{off.TotalMinutes:N1} / {total.TotalMinutes:N1} minutes ({percentage * 100d:N1} %)");
				}
			}

			if (SessionLockPowerMode == Mode.Undefined) return;

			try
			{
				switch (ev.Reason)
				{
					case SessionSwitchReason.SessionLogoff:
						if (!SaverOnLogOff) return;
						goto setpowersaver;
					case SessionSwitchReason.SessionLock:
						setpowersaver:
						// SET POWER SAVER
						if (DebugSession) Log.Debug("<Session:Lock> Enforcing power plan: " + SessionLockPowerMode.ToString());

						if (SessionLockPowerMode != Mode.Undefined)
						{
							lock (power_lock)
							{
								if (CurrentMode != SessionLockPowerMode)
									InternalSetMode(SessionLockPowerMode, new Cause(OriginType.Session, "Lock"), verbose: true);
							}
						}
						break;
					case SessionSwitchReason.SessionLogon:
					case SessionSwitchReason.SessionUnlock:
						// RESTORE POWER MODE
						if (loudPower) Log.Information("<Session:Unlock> Restoring previous power configuration.");

						SleepGivenUp = 0;

						// TODO: Add configuration for this
						ResetPower(new Cause(OriginType.Session, "Unlock"), true);
						break;
					default:
						// HANDS OFF MODE
						break;
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}

			try
			{
				SessionLock?.Invoke(this, new SessionLockEventArgs(SessionLocked));
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		void ResetPower(Cause cause = null, bool verbose = false)
		{
			Mode mode = RestoreMode;
			lock (power_lock)
			{
				if (Forced)
				{
					mode = Mode.PowerSaver;
					foreach (var pow in ForceModeSourcesMap)
					{
						if (pow.Value == Mode.HighPerformance)
						{
							mode = pow.Value;
							break;
						}
						else if (pow.Value == Mode.Balanced)
							mode = Mode.Balanced;
					}
				}
				else
				{
					if (mode == Mode.Undefined && SavedMode != Mode.Undefined)
						mode = SavedMode;
					else
						mode = Mode.Balanced;
				}

				InternalSetMode(mode, cause, verbose: verbose);
			}

			Behaviour = LaunchBehaviour;
		}

		void BatteryChargingEvent(object _, PowerModeChangedEventArgs ev)
		{
			if (DisposedOrDisposing) return;

			switch (ev.Mode)
			{
				case Microsoft.Win32.PowerModes.StatusChange:
					Log.Information("Undefined battery/AC change detected.");
					// System.Windows.Forms.PowerStatus
					break;
				case Microsoft.Win32.PowerModes.Suspend:
					// DON'T TOUCH
					Log.Information("Hibernation/Suspend start detected.");
					break;
				case Microsoft.Win32.PowerModes.Resume:
					Log.Information("Hibernation/Suspend end detected.");
					onSuspendResume?.Invoke(this, EventArgs.Empty);
					// Invoke whatever is necessary to restore functionality after suspend breaking shit.
					break;
			}
		}

		Cause ExpectedCause = new Cause(OriginType.None);
		Mode ExpectedMode = Mode.Undefined;
		MonitorPowerMode ExpectedMonitorPower = MonitorPowerMode.On;
		DateTimeOffset LastExternalWarning = DateTimeOffset.MinValue;

		protected override void WndProc(ref Message m)
		{
			if (DisposedOrDisposing) return;

			if (m.Msg == NativeMethods.WM_POWERBROADCAST &&
				m.WParam.ToInt32() == NativeMethods.PBT_POWERSETTINGCHANGE)
			{
				var ps = (NativeMethods.POWERBROADCAST_SETTING)Marshal.PtrToStructure(m.LParam, typeof(NativeMethods.POWERBROADCAST_SETTING));

				if (ps.PowerSetting == GUID_POWERSCHEME_PERSONALITY && ps.DataLength == Marshal.SizeOf(typeof(Guid)))
				{
					var pData = (IntPtr)(m.LParam.ToInt32() + Marshal.SizeOf(ps) - 4); // -4 is to align to the ps.Data
					var newPersonality = (Guid)Marshal.PtrToStructure(pData, typeof(Guid));
					var old = CurrentMode;
					if (newPersonality == Balanced) { CurrentMode = Mode.Balanced; }
					else if (newPersonality == HighPerformance) { CurrentMode = Mode.HighPerformance; }
					else if (newPersonality == PowerSaver) { CurrentMode = Mode.PowerSaver; }
					else { CurrentMode = Mode.Undefined; }

					onPlanChange?.Invoke(this, new ModeEventArgs(CurrentMode, old, CurrentMode == ExpectedMode ? ExpectedCause : new Cause(OriginType.None, "External")));
					ExpectedCause = null;

					if (DebugPower) Log.Information($"<Power/OS> Change detected: {CurrentMode.ToString()} ({newPersonality.ToString()})");

					var now = DateTimeOffset.UtcNow;
					if (CurrentMode != ExpectedMode && Behaviour == PowerBehaviour.Auto && LastExternalWarning.TimeTo(now).TotalSeconds > 30)
					{
						LastExternalWarning = now;
						Log.Warning("<Power/OS> Unexpected power mode change detected (" + GetModeName(CurrentMode) + ") while auto-adjust is enabled.");
					}

					m.Result = IntPtr.Zero;
				}
				else if (ps.PowerSetting == GUID_CONSOLE_DISPLAY_STATE)
				{
					MonitorPowerMode mode = MonitorPowerMode.Invalid;

					switch (ps.Data)
					{
						case 0x0:
							mode = MonitorPowerMode.Off;
							if (mode == ExpectedMonitorPower)
								MonitorPowerOffCounter.Start(); // only start the counter if we caused it
							break;
						case 0x1:
							mode = MonitorPowerMode.On;
							MonitorPowerOffCounter.Stop();
							break;
						case 0x2: mode = MonitorPowerMode.Standby; break;
						default: break;
					}

					m.Result = IntPtr.Zero;

					MonitorPower?.Invoke(this, new MonitorPowerEventArgs(mode));
				}
			}

			base.WndProc(ref m); // is this necessary?
		}

		public static string GetBehaviourName(PowerBehaviour behaviour)
		{
			switch (behaviour)
			{
				case PowerBehaviour.Auto:
					return HumanReadable.Hardware.Power.AutoAdjust;
				case PowerBehaviour.Manual:
					return HumanReadable.Hardware.Power.Manual;
				case PowerBehaviour.RuleBased:
					return HumanReadable.Hardware.Power.RuleBased;
				default:
					return HumanReadable.Generic.Undefined;
			}
		}

		public static string GetModeName(Mode mode)
		{
			switch (mode)
			{
				case Mode.Balanced:
					return "Balanced";
				case Mode.HighPerformance:
					return "High Performance";
				case Mode.PowerSaver:
					return "Power Saver";
				case Mode.Custom:
					return "Custom";
				default:
					return "Undefined";
			}
		}

		public static Mode GetModeByName(string name)
		{
			if (string.IsNullOrEmpty(name)) return Mode.Undefined;

			switch (name.ToLowerInvariant())
			{
				case "low":
				case "powersaver":
				case "power saver":
					return Mode.PowerSaver;
				case "average":
				case "medium":
				case "balanced":
					return Mode.Balanced;
				case "high":
				case "highperformance":
				case "high performance":
					return Mode.HighPerformance;
				default:
					return Mode.Undefined;
			}
		}

		public Mode OriginalMode { get; private set; } = Mode.Balanced;
		public Mode CurrentMode { get; private set; } = Mode.Balanced;

		Mode SavedMode = Mode.Undefined;

		static readonly Guid HighPerformance = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"); // SCHEME_MIN
		static readonly Guid Balanced = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e"); // SCHEME_BALANCED
		static readonly Guid PowerSaver = new Guid("a1841308-3541-4fab-bc81-f71556f20b4a"); // SCHEME_MAX

		public enum PowerBehaviour
		{
			Auto,
			RuleBased,
			Manual,
			Internal,
			Undefined
		}

		sealed public class PowerBehaviourEventArgs : EventArgs
		{
			public PowerBehaviour Behaviour = PowerBehaviour.Undefined;

			public PowerBehaviourEventArgs(PowerBehaviour behaviour) => Behaviour = behaviour;
		}

		public void SetRestoreMode(RestoreModeMethod method, Mode mode)
		{
			RestoreMethod = method;
			switch (method)
			{
				case RestoreModeMethod.Default:
					RestoreMode = AutoAdjust.DefaultMode;
					break;
				case RestoreModeMethod.Original:
					RestoreMode = OriginalMode;
					break;
				case RestoreModeMethod.Saved:
					RestoreMode = Mode.Undefined;
					break;
				case RestoreModeMethod.Custom:
					RestoreMode = mode;
					break;
			}
		}

		public PowerBehaviour SetBehaviour(PowerBehaviour pb)
		{
			//Debug.Assert(pb == Behaviour);
			if (pb == Behaviour) return Behaviour; // rare instance, likely caused by toggling manual mode

			bool reset = false;

			Behaviour = pb;

			LogBehaviourState();

			Restore(new Cause(OriginType.User));

			switch (Behaviour)
			{
				case PowerBehaviour.Auto:
					ResetAutoadjust();

					if (cpumonitor == null)
					{
						reset = true;
						Log.Error("<Power> CPU monitor disabled, auto-adjust not possible. Resetting to rule-based behaviour.");
					}
					break;
				default:
				case PowerBehaviour.RuleBased:
					break;
				case PowerBehaviour.Manual:
					processmanager.CancelPowerWait(); // need nicer way to do this
					Release(null).ConfigureAwait(false);
					break;
			}

			if (reset) Behaviour = PowerBehaviour.RuleBased;

			onBehaviourChange?.Invoke(this, new PowerBehaviourEventArgs(Behaviour));

			return Behaviour;
		}

		/// <summary>
		/// Restores normal power mode and frees the associated source pid from holding it.
		/// </summary>
		/// <param name="sourcePid">0 releases all locks.</param>
		public async Task Release(ProcessEx info)
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException("Release called after PowerManager was disposed.");

			int sourcePid = info == null ? -1 : info.Id;

			if (DebugPower) Log.Debug($"<Power> Releasing {(sourcePid == -1 ? "all locks" : $"#{sourcePid.ToString()}")}");

			Debug.Assert(sourcePid == -1 || !Process.Manager.SystemProcessId(sourcePid));

			await Task.Delay(0).ConfigureAwait(false);

			lock (power_lock)
			{
				try
				{
					if (sourcePid == -1)
					{
						ForceModeSourcesMap?.Clear();
					}
					else if (ForceModeSourcesMap.TryRemove(sourcePid, out _))
					{
						if (DebugPower && Trace) Log.Debug("<Power> Force mode source freed, " + ForceModeSourcesMap.Count.ToString() + " remain.");
					}
					else if (ForceModeSourcesMap.Count > 0)
					{
						if (DebugPower && Trace) Log.Debug("<Power> Force mode release for unincluded ID, " + ForceModeSourcesMap.Count.ToString() + " remain.");
					}
					else
					{
						if (DebugPower) Log.Debug("<Power> Restore mode called for object [" + sourcePid.ToString() + "] that has no forcing registered. Or waitlist was expunged.");
					}

					Forced = ForceModeSourcesMap.Count > 0;

					if (!Forced)
					{
						ReleaseFinal(new Cause(OriginType.Watchlist, $"#{sourcePid} exited; " + (Behaviour == PowerBehaviour.Auto ? "auto-adjust resumed" : "restoring.")))
							.ConfigureAwait(false);
						if (VerbosePowerRelease && info != null)
							Log.Information($"<Power> {info.Name} (#{info.Id}) quit, power forcing released.");
					}

					if (Trace && DebugPower) Log.Debug($"<Power> Released {(sourcePid == -1 ? "All" : sourcePid.ToString())}");
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
				}
			}
		}

		async Task ReleaseFinal(Cause cause=null)
		{
			await Task.Delay(0).ConfigureAwait(false);

			if (Behaviour != PowerBehaviour.Auto && PowerdownDelay.HasValue)
				await Task.Delay(PowerdownDelay.Value).ConfigureAwait(false);

			int lockCount = ForceCount;
			if (lockCount == 0)
			{
				// TODO: Restore Powerdown delay functionality here.

				if (Trace && DebugPower) Log.Debug("<Power> No power locks left.");

				Restore(cause);
				return;
			}
			else
			{
				if (DebugPower)
					Log.Debug("<Power> Forced mode still requested by " + lockCount + " sources: " + string.Join(", ", ForceModeSourcesMap.Keys.ToArray()));
			}
		}

		void Restore(Cause cause=null)
		{
			if (Behaviour == PowerBehaviour.Manual)
			{
				if (DebugPower) Log.Debug("<Power> Power restoration cancelled due to manual control.");
				return;
			}

			lock (power_lock)
			{
				if (Forced) return;

				if (RestoreMethod == RestoreModeMethod.Saved)
				{
					if (SavedMode == Mode.Undefined) SavedMode = RestoreMode;
				}
				else
					SavedMode = RestoreMode;

				if (SavedMode != CurrentMode && SavedMode != Mode.Undefined)
				{
					// if (Behaviour == PowerBehaviour.Auto) return; // this is very optimistic

					if (DebugPower) Log.Debug("<Power> Restoring power mode: " + SavedMode.ToString());

					InternalSetMode(SavedMode, cause ?? new Cause(OriginType.None, "Restoration"), verbose:false);
					SavedMode = Mode.Undefined;
				}
				else
				{
					if (DebugPower) Log.Debug("<Power> Power restoration cancelled, target mode is same as current.");
				}
			}
		}

		Mode getPowerMode()
		{
			Guid plan;
			var ptr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(IntPtr))); // is this actually necessary?

			lock (power_lock)
			{
				if (NativeMethods.PowerGetActiveScheme((IntPtr)null, out ptr) == 0)
				{
					plan = (Guid)Marshal.PtrToStructure(ptr, typeof(Guid));
					Marshal.FreeHGlobal(ptr);
					if (plan.Equals(Balanced)) { CurrentMode = Mode.Balanced; }
					else if (plan.Equals(PowerSaver)) { CurrentMode = Mode.PowerSaver; }
					else if (plan.Equals(HighPerformance)) { CurrentMode = Mode.HighPerformance; }
					else { CurrentMode = Mode.Undefined; }

					if (DebugPower) Log.Debug($"<Power> Current: {CurrentMode.ToString()}");
				}
			}

			return CurrentMode;
		}

		static readonly object power_lock = new object();

		public PowerBehaviour LaunchBehaviour { get; set; } = PowerBehaviour.RuleBased;
		public PowerBehaviour Behaviour { get; private set; } = PowerBehaviour.RuleBased;

		bool AutoAdjustSetMode(Mode mode, Cause cause)
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException("AutoAdjustSetMode called after PowerManager was disposed.");

			Debug.Assert(Behaviour == PowerBehaviour.Auto, "This is for auto adjusting only.");

			lock (power_lock)
			{
				if (SessionLocked) return false;

				if (mode == CurrentMode || Forced)
					return false;

				InternalSetMode(mode, cause, verbose: false);
			}
			return true;
		}

		/// <summary>
		/// Set power mode and lock it, preventing changes outside of manual control.
		/// </summary>
		// BUG: If user forces disparate modes, only last forcing takes effect.
		public bool Force(Mode mode, int sourcePid)
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException("Force called after PowerManager was disposed.");

			if (Behaviour == PowerBehaviour.Manual) return false;
			if (SessionLocked) return false;

			var rv = false;

			lock (power_lock)
			{
				if (DebugPower) Log.Debug("<Power> Saving current power mode for later restoration: " + CurrentMode.ToString());

				SavedMode = CurrentMode;

				if (SavedMode == Mode.Undefined) Log.Warning("<Power> Failed to get current mode for later restoration.");

				// ----

				if (!ForceModeSourcesMap.TryAdd(sourcePid, mode))
				{
					if (DebugPower && ShowInaction) Log.Debug("<Power> Forcing cancelled, source already in list.");
					return false;
				}

				if (DebugPower) Log.Debug("<Power> Lock #" + sourcePid);

				Forced = true;

				rv = mode != CurrentMode;
				if (rv)
				{
					SavedMode = RestoreMethod == RestoreModeMethod.Saved ? CurrentMode : RestoreMode;
					InternalSetMode(mode, cause: new Cause(OriginType.Watchlist, $"PID:{sourcePid}"), verbose: false);
				}
				else
				{
					if (Trace && DebugPower) Log.Debug("<Power> Force power mode for mode that is already active. Ignoring.");
				}
			}

			return rv;
		}

		public void SetMode(Mode mode, Cause cause=null, bool verbose = true)
		{
			lock (power_lock)
			{
				InternalSetMode(mode, cause, verbose:verbose);
			}
		}

		// BUG: ?? There might be odd behaviour if this is called while Paused==true
		void InternalSetMode(Mode mode, Cause cause=null, bool verbose = true)
		{
			var plan = Guid.Empty;
			switch (mode)
			{
				default:
				case Mode.Balanced:
					plan = Balanced;
					break;
				case Mode.HighPerformance:
					plan = HighPerformance;
					break;
				case Mode.PowerSaver:
					plan = PowerSaver;
					break;
			}

			if ((verbose && (CurrentMode != mode)) || DebugPower)
			{
				string extra = cause != null ? $" - Cause: {cause.ToString()}" : string.Empty;
				Log.Information("<Power> Setting mode: " + GetModeName(mode) + extra);
			}

			ExpectedMode = CurrentMode = mode;
			ExpectedCause = cause;
			NativeMethods.PowerSetActiveScheme((IntPtr)null, ref plan);
		}

		async void SetMonitorMode(MonitorPowerMode powermode)
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException("SetMonitorMode called after PowerManager was disposed.");

			Debug.Assert(powermode != MonitorPowerMode.Invalid);
			long NewPowerMode = (int)powermode; // -1 = Powering On, 1 = Low Power (low backlight, etc.), 2 = Power Off

			var Broadcast = new IntPtr(NativeMethods.HWND_BROADCAST); // unreliable
			var Topmost = new IntPtr(NativeMethods.HWND_TOPMOST);

			uint timeout = 200; // ms per window, we don't really care if they process them
			var flags = NativeMethods.SendMessageTimeoutFlags.SMTO_ABORTIFHUNG | NativeMethods.SendMessageTimeoutFlags.SMTO_NORMAL | NativeMethods.SendMessageTimeoutFlags.SMTO_NOTIMEOUTIFNOTHUNG;

			//IntPtr hWnd = Handle; // send to self works for this? seems even more unreliable
			// there's a lot of discussion on what is the correct way to do this, and many agree broadcast is not good choice even if it works
			// NEVER send it via SendMessage(), only via SendMessageTimeout()
			// PostMessage() is also valid.

			await Task.Delay(0).ConfigureAwait(false);

			ExpectedMonitorPower = powermode;
			NativeMethods.SendMessageTimeout(Broadcast, NativeMethods.WM_SYSCOMMAND, NativeMethods.SC_MONITORPOWER, NewPowerMode, flags, timeout, out _);
		}

		#region IDisposable Support
		public event EventHandler<DisposedEventArgs> OnDisposed;

		bool DisposedOrDisposing = false;
		protected override void Dispose(bool disposing)
		{
			if (DisposedOrDisposing) return;
			DisposedOrDisposing = true;

			Behaviour = PowerBehaviour.Internal;

			SystemEvents.SessionSwitch -= SessionLockEvent; // leaks if not disposed

			if (disposing)
			{
				if (Trace) Log.Verbose("Disposing power manager...");

				SessionLock = null;
				MonitorPower = null;

				if (cpumonitor != null)
					cpumonitor.onSampling -= CPULoadHandler;

				MonitorPower = null;
				MonitorSleepTimer?.Dispose();

				SessionLock = null;
				onAutoAdjustAttempt = null;
				onPlanChange = null;
				onBehaviourChange = null;
				onSuspendResume = null;

				ForceModeSourcesMap?.Clear();
				Forced = false;

				Restore(new Cause(OriginType.Internal, "Power Manager shutdown"));

				Log.Information("<Power> Auto-adjusted " + AutoAdjustCounter + " time(s).");

				SaveConfig();
			}
		}
		#endregion

		public void ShutdownEvent(object sender, EventArgs ea)
		{
			StopDisplayTimer();
		}
	}
}