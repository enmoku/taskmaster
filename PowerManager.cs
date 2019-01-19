//
// PowerManager.cs
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
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using MKAh;
using Serilog;
using Taskmaster.PowerInfo;

namespace Taskmaster
{
	sealed public class PowerModeEventArgs : EventArgs
	{
		public PowerModeEventArgs(PowerMode newmode, PowerMode oldmode = PowerMode.Undefined, Cause cause = null)
		{
			NewMode = newmode;
			OldMode = oldmode;
			Cause = cause;
		}

		public PowerMode OldMode { get; set; } = PowerMode.Undefined;
		public PowerMode NewMode { get; set; } = PowerMode.Undefined;
		public Cause Cause { get; set; } = null;
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
		public MonitorPowerEventArgs(MonitorPowerMode mode) { Mode = mode; }
	}

	public class SessionLockEventArgs : EventArgs
	{
		public bool Locked = false;
		public SessionLockEventArgs(bool locked = false) { Locked = locked; }
	}

	sealed public class PowerManager : Form // form is required for receiving messages, no other reason
	{
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

		public PowerManager()
		{
			OriginalMode = getPowerMode();
			ExpectedMode = OriginalMode;

			AutoAdjust = PowerAutoadjustPresets.Default();

			LoadConfig();

			// SystemEvents.PowerModeChanged += BatteryChargingEvent; // Without laptop testing this feature is difficult

			if (Behaviour == PowerBehaviour.RuleBased && !Forced)
				Restore();

			if (SessionLockPowerOffIdleTimeout != TimeSpan.Zero)
			{
				var stopped = System.Threading.Timeout.InfiniteTimeSpan;
				MonitorSleepTimer = new System.Threading.Timer(MonitorSleepTimerTick, null, stopped, stopped);

				MonitorPower += MonitorPowerEvent;
			}

			Taskmaster.DisposalChute.Push(this);
		}

		public int ForceCount => ForceModeSourcesMap.Count;
		ConcurrentDictionary<int, int> ForceModeSourcesMap = new ConcurrentDictionary<int, int>();

		CPUMonitor cpumonitor = null;

		public void Hook(CPUMonitor monitor)
		{
			cpumonitor = monitor;
			cpumonitor.onSampling += CPULoadHandler;
		}

		async void MonitorPowerEvent(object _, MonitorPowerEventArgs ev)
		{
			try
			{
				var OldPowerState = CurrentMonitorState;
				CurrentMonitorState = ev.Mode;

				if (Taskmaster.DebugMonitor)
				{
					var idle = User.IdleTime();
					double sidletime = Time.Simplify(idle, out Time.Timescale scale);
					var timename = Time.TimescaleString(scale, !sidletime.RoughlyEqual(1d));

					//uint lastact = User.LastActive();
					//var idle = User.LastActiveTimespan(lastact);
					//if (lastact == uint.MinValue) idle = TimeSpan.Zero; // HACK

					Log.Debug("<Monitor> Power state: " + ev.Mode.ToString() + " (last user activity " + $"{sidletime:N1} {timename}" + " ago)");
				}

				if (OldPowerState == CurrentMonitorState)
				{
					Log.Debug("Received monitor power event: " + OldPowerState.ToString() + " → " + ev.Mode.ToString());
					return; //
				}

				if (ev.Mode == MonitorPowerMode.On && SessionLocked)
				{
					if (SessionLockPowerMode != PowerMode.Undefined)
					{
						lock (power_lock)
						{
							if (CurrentMode == SessionLockPowerMode)
								InternalSetMode(PowerMode.Balanced, new Cause(OriginType.Session, "User activity"), verbose: false);
						}
					}

					StartDisplayTimer();
					MonitorOffLastLock?.Stop();

					if (Taskmaster.DebugMonitor)
						DebugMonitorWake();
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

		async void DebugMonitorWake()
		{
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
			using (var proc = Process.Start(info))
			{
				Debug.WriteLine(info.FileName + " " + info.Arguments);
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
			using (var proc = Process.Start(info))
			{
				Debug.WriteLine(info.FileName + " " + info.Arguments);
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
			using (var proc = Process.Start(info))
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
			var stopped = System.Threading.Timeout.InfiniteTimeSpan;
			MonitorSleepTimer?.Change(stopped, stopped);
		}

		void StartDisplayTimer()
		{
			if (SleepTickCount < 0) SleepTickCount = 0; // reset
			var minute = TimeSpan.FromMinutes(1);
			MonitorSleepTimer?.Change(minute, minute);
		}

		System.Threading.Timer MonitorSleepTimer;

		int SleepTickCount = -1;
		int SleepGivenUp = 0;
		int monitorsleeptimer_lock = 0;
		async void MonitorSleepTimerTick(object _)
		{
			var idle = User.IdleTime();

			if (SessionLockPowerMode != PowerMode.Undefined)
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

					if ((Taskmaster.ShowSessionActions && SleepGivenUp <= 1) || Taskmaster.DebugMonitor)
						Log.Information("<Session:Lock> User idle (" + $"{sidletime:N1} {timename}" + "); Monitor power down, attempt " +
							SleepTickCount + "...");

					SetMonitorMode(MonitorPowerMode.Off);
				}
				else
				{
					if (Taskmaster.ShowSessionActions || Taskmaster.DebugMonitor)
						Log.Information("<Session:Lock> User active too recently (" + $"{idle.TotalSeconds:N1}s" + " ago), delaying monitor power down...");

					StartDisplayTimer(); // TODO: Make this happen sooner if user was not active recently
				}
			}
			finally
			{
				Atomic.Unlock(ref monitorsleeptimer_lock);
			}
		}

		MonitorPowerMode CurrentMonitorState = MonitorPowerMode.Invalid;

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
		public PowerMode SessionLockPowerMode { get; set; } = PowerMode.PowerSaver;
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
		TimeSpan SessionLockPowerOffIdleTimeout = TimeSpan.FromSeconds(120);
		/// <summary>
		/// Power off monitor directly on lock off.
		/// </summary>
		public bool SessionLockPowerOff { get; set; } = true;

		public event EventHandler<AutoAdjustReactionEventArgs> onAutoAdjustAttempt;
		public event EventHandler<PowerModeEventArgs> onPlanChange;
		public event EventHandler<PowerBehaviourEventArgs> onBehaviourChange;
		public event EventHandler onBatteryResume;

		enum ThresholdLevel
		{
			High = 0,
			Average = 1,
			Low = 2
		}

		public enum PowerReaction
		{
			High = 1,
			Average = 0,
			Low = -1,
			Steady = 2
		}

		bool Paused = false;
		bool SessionLocked = false;
		/// <summary>
		/// Power mode is forced, auto-adjust and other automatic changes are disabled.
		/// </summary>
		bool Forced = false;

		long AutoAdjustCounter = 0;

		public AutoAdjustSettings AutoAdjust { get; private set; } = new AutoAdjustSettings();
		readonly object autoadjust_lock = new object();

		public void SetAutoAdjust(AutoAdjustSettings settings)
		{
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
		PowerReaction PreviousReaction = PowerReaction.Average;

		// TODO: Simplify this mess
		public void CPULoadHandler(object _, ProcessorLoadEventArgs pev)
		{
			if (Behaviour != PowerBehaviour.Auto) return;

			var ev = AutoAdjustReactionEventArgs.From(pev);

			lock (autoadjust_lock)
			{
				var Reaction = PowerReaction.Average;
				var ReactionaryPlan = AutoAdjust.DefaultMode;

				var Ready = false;

				ev.Pressure = 0;
				ev.Enacted = false;

				if (PreviousReaction == PowerReaction.High)
				{
					// Downgrade to MEDIUM power level
					if (ev.High <= AutoAdjust.High.Backoff.High
						|| ev.Average <= AutoAdjust.High.Backoff.Avg
						|| ev.Low <= AutoAdjust.High.Backoff.Low)
					{
						Reaction = PowerReaction.Average;
						ReactionaryPlan = AutoAdjust.DefaultMode;

						BackoffCounter++;

						if (BackoffCounter >= AutoAdjust.High.Backoff.Level)
							Ready = true;

						ev.Pressure = ((float)BackoffCounter) / ((float)AutoAdjust.High.Backoff.Level);
					}
					else
						Reaction = PowerReaction.Steady;
				}
				else if (PreviousReaction == PowerReaction.Low)
				{
					// Upgrade to MEDIUM power level
					if (ev.High >= AutoAdjust.Low.Backoff.High
						|| ev.Average >= AutoAdjust.Low.Backoff.Avg
						|| ev.Low >= AutoAdjust.Low.Backoff.Low)
					{
						Reaction = PowerReaction.Average;
						ReactionaryPlan = AutoAdjust.DefaultMode;

						BackoffCounter++;

						if (BackoffCounter >= AutoAdjust.Low.Backoff.Level)
							Ready = true;

						ev.Pressure = ((float)BackoffCounter) / ((float)AutoAdjust.Low.Backoff.Level);
					}
					else
						Reaction = PowerReaction.Steady;
				}
				else // Currently at medium power
				{
					if (ev.Low > AutoAdjust.High.Commit.Threshold && AutoAdjust.High.Mode != AutoAdjust.DefaultMode) // Low CPU is above threshold for High mode
					{
						// Downgrade to LOW power levell
						Reaction = PowerReaction.High;
						ReactionaryPlan = AutoAdjust.High.Mode;

						LowPressure = 0; // reset
						HighPressure++;

						if (HighPressure >= AutoAdjust.High.Commit.Level)
							Ready = true;

						ev.Pressure = ((float)HighPressure) / ((float)AutoAdjust.High.Commit.Level);
					}
					else if (ev.High < AutoAdjust.Low.Commit.Threshold && AutoAdjust.Low.Mode != AutoAdjust.DefaultMode) // High CPU is below threshold for Low mode
					{
						// Upgrade to HIGH power levele
						Reaction = PowerReaction.Low;
						ReactionaryPlan = AutoAdjust.Low.Mode;

						HighPressure = 0; // reset
						LowPressure++;

						if (LowPressure >= AutoAdjust.Low.Commit.Level)
							Ready = true;

						ev.Pressure = ((float)LowPressure) / ((float)AutoAdjust.Low.Commit.Level);
					}
					else // keep power at medium
					{
						if (Taskmaster.DebugAutoPower) Debug.WriteLine("Auto-adjust NOP");

						Reaction = PowerReaction.Steady;
						ReactionaryPlan = AutoAdjust.DefaultMode;

						ResetAutoadjust();

						// Only time this should cause actual power mode change is when something else changes power mode
						Ready = true;
						//ev.Pressure = 1; // required for actually changing mode
					}
				}
				
				var ReadyToAdjust = (Ready && !Forced && !SessionLocked);

				if (ReadyToAdjust && ReactionaryPlan != CurrentMode)
				{
					if (Taskmaster.DebugPower) Log.Debug("<Power> Auto-adjust: " + Reaction.ToString());

					if (AutoAdjustSetMode(ReactionaryPlan, new Cause(OriginType.AutoAdjust, $"{Reaction}, CPU: {pev.Current:N1}%")))
					{
						AutoAdjustCounter++;
						ev.Enacted = true;
					}
					else
					{
						if (Taskmaster.DebugPower && Taskmaster.Trace)
							Log.Warning("<Power> Failed to auto-adjust power.");
						// should reset
					}

					ResetAutoadjust();
					PreviousReaction = Reaction;
				}
				else
				{
					if (Forced)
					{
						if (Taskmaster.DebugPower && Taskmaster.ShowInaction && !WarnedForceMode)
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
							if (Taskmaster.DebugPower) Log.Debug("<Power> Something went wrong. Resetting auto-adjust.");

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

		bool WarnedForceMode = false;

		void ResetAutoadjust()
		{
			BackoffCounter = HighPressure = LowPressure = 0;
			PreviousReaction = PowerReaction.Average;
			WarnedForceMode = false;
		}

		TimeSpan PowerdownDelay { get; set; } = TimeSpan.Zero;

		void LoadConfig()
		{
			var corecfg = Taskmaster.Config.Load(Taskmaster.coreconfig);

			var power = corecfg.Config[HumanReadable.Hardware.Power.Section];
			bool modified = false, dirtyconfig = false;

			var behaviourstring = power.GetSetDefault("Behaviour", HumanReadable.Hardware.Power.RuleBased, out modified).StringValue;
			power["Behaviour"].Comment = "auto, manual, or rule-based";
			if (behaviourstring.StartsWith("auto", StringComparison.InvariantCultureIgnoreCase))
				LaunchBehaviour = PowerBehaviour.Auto;
			else if (behaviourstring.StartsWith("manual", StringComparison.InvariantCultureIgnoreCase))
				LaunchBehaviour = PowerBehaviour.Manual;
			else
				LaunchBehaviour = PowerBehaviour.RuleBased;
			Behaviour = LaunchBehaviour;

			var defaultmode = power.GetSetDefault("Default mode", GetModeName(PowerMode.Balanced), out modified).StringValue;
			power["Default mode"].Comment = "This is what power plan we fall back on when nothing else is considered.";
			AutoAdjust.DefaultMode = GetModeByName(defaultmode);
			if (AutoAdjust.DefaultMode == PowerMode.Undefined)
			{
				Log.Warning("<Power> Default mode malconfigured, defaulting to balanced.");
				AutoAdjust.DefaultMode = PowerMode.Balanced;
			}
			dirtyconfig |= modified;

			var restoremode = power.GetSetDefault("Restore mode", "Default", out modified).StringValue;
			power["Restore mode"].Comment = "Default, Original, Saved, or specific power mode. Power mode to restore with rule-based behaviour.";
			dirtyconfig |= modified;
			RestoreModeMethod newmodemethod = RestoreModeMethod.Default;
			PowerMode newrestoremode = PowerMode.Undefined;

			switch (restoremode.ToLowerInvariant())
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
					newrestoremode = PowerMode.Undefined;
					break;
				default:
					newmodemethod = RestoreModeMethod.Custom;
					newrestoremode = GetModeByName(restoremode);
					if (RestoreMode == PowerMode.Undefined)
					{
						// TODO: Complain about bad config
						Log.Warning("<Power> Restore mode name unintelligible.");
						newrestoremode = AutoAdjust.DefaultMode;
					}
					break;
			}

			SetRestoreMode(newmodemethod, newrestoremode);

			var tdelay = power.GetSetDefault("Watchlist powerdown delay", 0, out modified).IntValue.Constrain(0, 60);
			power["Watchlist powerdown delay"].Comment = "Delay, in seconds (0 to 60, 0 disables), for when to wind down power mode set by watchlist.";
			dirtyconfig |= modified;
			if (tdelay > 0) PowerdownDelay = TimeSpan.FromSeconds(tdelay);

			var autopower = corecfg.Config["Power / Auto"];

			// DEPRECATED
			if (autopower.Contains(HumanReadable.Hardware.Power.AutoAdjust))
			{
				bool bautoadjust = autopower.TryGet(HumanReadable.Hardware.Power.AutoAdjust)?.BoolValue ?? false;

				if (bautoadjust)
				{
					power["Behaviour"].StringValue = HumanReadable.Hardware.Power.AutoAdjust;
					dirtyconfig = true;
					LaunchBehaviour = PowerBehaviour.Auto;
					Behaviour = PowerBehaviour.Auto;
				}

				autopower.Remove(HumanReadable.Hardware.Power.AutoAdjust);
				Log.Debug("<Power> Deprecated INI cleanup: Auto-adjust");
			}

			// BACKOFF
			AutoAdjust.Low.Backoff.Level = autopower.GetSetDefault("Low backoff level", 1, out modified).IntValue.Constrain(0, 10);
			autopower["Low backoff level"].Comment = "1 to 10. Consequent backoff reactions that is required before it actually triggers.";
			dirtyconfig |= modified;
			AutoAdjust.High.Backoff.Level = autopower.GetSetDefault("High backoff level", 3, out modified).IntValue.Constrain(0, 10);
			autopower["High backoff level"].Comment = "1 to 10. Consequent backoff reactions that is required before it actually triggers.";
			dirtyconfig |= modified;

			// COMMIT
			AutoAdjust.Low.Commit.Level = autopower.GetSetDefault("Low commit level", 7, out modified).IntValue.Constrain(1, 10);
			autopower["Low commit level"].Comment = "1 to 10. Consequent commit reactions that is required before it actually triggers.";
			dirtyconfig |= modified;
			AutoAdjust.High.Commit.Level = autopower.GetSetDefault("High commit level", 3, out modified).IntValue.Constrain(1, 10);
			autopower["High commit level"].Comment = "1 to 10. Consequent commit reactions that is required before it actually triggers.";
			dirtyconfig |= modified;

			// THRESHOLDS
			AutoAdjust.High.Commit.Threshold = autopower.GetSetDefault("High threshold", 70, out modified).FloatValue;
			autopower["High threshold"].Comment = "If low CPU value keeps over this, we swap to high mode.";
			dirtyconfig |= modified;
			var hbtt = autopower.GetSetDefault("High backoff thresholds", new float[] { AutoAdjust.High.Backoff.High, AutoAdjust.High.Backoff.Avg, AutoAdjust.High.Backoff.Low }, out modified).FloatValueArray;
			if (hbtt != null && hbtt.Length == 3)
			{
				AutoAdjust.High.Backoff.Low = hbtt[2];
				AutoAdjust.High.Backoff.Avg = hbtt[1];
				AutoAdjust.High.Backoff.High = hbtt[0];
			}

			autopower["High backoff thresholds"].Comment = "High, Average and Low CPU usage values, any of which is enough to break away from high power mode.";
			dirtyconfig |= modified;

			AutoAdjust.Low.Commit.Threshold = autopower.GetSetDefault("Low threshold", 15, out modified).FloatValue;
			autopower["Low threshold"].Comment = "If high CPU value keeps under this, we swap to low mode.";
			dirtyconfig |= modified;
			var lbtt = autopower.GetSetDefault("Low backoff thresholds", new float[] { AutoAdjust.Low.Backoff.High, AutoAdjust.Low.Backoff.Avg, AutoAdjust.Low.Backoff.Low }, out modified).FloatValueArray;
			if (lbtt != null && lbtt.Length == 3)
			{
				AutoAdjust.Low.Backoff.Low = lbtt[2];
				AutoAdjust.Low.Backoff.Avg = lbtt[1];
				AutoAdjust.Low.Backoff.High = lbtt[0];
			}

			autopower["Low backoff thresholds"].Comment = "High, Average and Low CPU uage values, any of which is enough to break away from low mode.";
			dirtyconfig |= modified;

			// POWER MODES
			var lowmode = power.GetSetDefault("Low mode", GetModeName(PowerMode.PowerSaver), out modified).StringValue;
			AutoAdjust.Low.Mode = GetModeByName(lowmode);
			dirtyconfig |= modified;
			var highmode = power.GetSetDefault("High mode", GetModeName(PowerMode.HighPerformance), out modified).StringValue;
			AutoAdjust.High.Mode = GetModeByName(highmode);
			dirtyconfig |= modified;

			var saver = corecfg.Config["AFK Power"];
			saver.Comment = "All these options control when to enforce power save mode regardless of any other options.";

			var sessionlockmodename = saver.GetSetDefault("Session lock", GetModeName(PowerMode.PowerSaver), out modified).StringValue;
			saver["Session lock"].Comment = "Power mode to set when session is locked, such as by pressing winkey+L. Unrecognizable values disable this.";
			dirtyconfig |= modified;
			SessionLockPowerMode = GetModeByName(sessionlockmodename);

			// SaverOnMonitorSleep = saver.GetSetDefault("Monitor sleep", true, out modified).BoolValue;
			// dirtyconfig |= modified;

			// SaverOnUserAFK = saver.GetSetDefault("User idle", 30, out modified).IntValue;
			// dirtyconfig |= modified;
			// UserActiveCancel = saver.GetSetDefault("Cancel on activity", true, out modified).BoolValue;
			// dirtyconfig |= modified;

			int monoffidletime = saver.GetSetDefault("Monitor power off idle timeout", 180, out modified).IntValue;
			SessionLockPowerOffIdleTimeout = monoffidletime > 0 ? TimeSpan.FromSeconds(monoffidletime.Constrain(30, 600)) : TimeSpan.Zero;
			saver["Monitor power off idle timeout"].Comment = "User needs to be this many seconds idle before we power down monitors when session is locked. 0 disables. Less than 30 is rounded up to 30.";
			dirtyconfig |= modified;

			SessionLockPowerOff = saver.GetSetDefault("Monitor power off on lock", true, out modified).BoolValue;
			saver["Monitor power off on lock"].Comment = "Power off monitor instantly on session lock.";
			dirtyconfig |= modified;

			// --------------------------------------------------------------------------------------------------------

			Log.Information("<Power> Watchlist powerdown delay: " + (PowerdownDelay == TimeSpan.Zero ? HumanReadable.Generic.Disabled : (PowerdownDelay + "s")));

			// --------------------------------------------------------------------------------------------------------

			LogBehaviourState();

			Log.Information("<Power> Session lock: " + (SessionLockPowerMode == PowerMode.Undefined ? HumanReadable.Generic.Ignore : SessionLockPowerMode.ToString()));
			Log.Information("<Power> Restore mode: " + RestoreMethod.ToString() + " [" + RestoreMode.ToString() + "]");

			Log.Information("<Session> User AFK timeout: " + (SessionLockPowerOffIdleTimeout == TimeSpan.Zero ? HumanReadable.Generic.Disabled : $"{SessionLockPowerOffIdleTimeout.TotalSeconds:N0}s"));
			Log.Information("<Session> Immediate power off on lock: " + (SessionLockPowerOff ? HumanReadable.Generic.Enabled : HumanReadable.Generic.Disabled));

			if (dirtyconfig) corecfg.MarkDirty();
		}

		// TODO: Should detect if saving is ACTUALLY needed
		public void SaveConfig()
		{
			var corecfg = Taskmaster.Config.Load(Taskmaster.coreconfig);

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
					power["Behaviour"].StringValue = sbehaviour;

					power["Default mode"].StringValue = AutoAdjust.DefaultMode.ToString();

					power["Restore mode"].StringValue = (RestoreMethod == RestoreModeMethod.Custom ? RestoreMode.ToString() : RestoreMethod.ToString());
					if (PowerdownDelay != TimeSpan.Zero)
						power["Watchlist powerdown delay"].IntValue = Convert.ToInt32(PowerdownDelay.TotalSeconds);
					else
						power.Remove("Watchlist powerdown delay");
					var autopower = corecfg.Config["Power / Auto"];

					// BACKOFF
					autopower["Low backoff level"].IntValue = AutoAdjust.Low.Backoff.Level;
					autopower["High backoff level"].IntValue = AutoAdjust.High.Backoff.Level;

					// COMMIT
					autopower["Low commit level"].IntValue = AutoAdjust.Low.Commit.Level;
					autopower["High commit level"].IntValue = AutoAdjust.High.Commit.Level;

					// THRESHOLDS
					autopower["High threshold"].FloatValue = AutoAdjust.High.Commit.Threshold;
					autopower["High backoff thresholds"].FloatValueArray = new float[] { AutoAdjust.High.Backoff.High, AutoAdjust.High.Backoff.Avg, AutoAdjust.High.Backoff.Low };

					autopower["Low threshold"].FloatValue = AutoAdjust.Low.Commit.Threshold;
					autopower["Low backoff thresholds"].FloatValueArray = new float[] { AutoAdjust.Low.Backoff.High, AutoAdjust.Low.Backoff.Avg, AutoAdjust.Low.Backoff.Low };

					// POWER MODES
					power["Low mode"].StringValue = GetModeName(AutoAdjust.Low.Mode);
					power["High mode"].StringValue = GetModeName(AutoAdjust.High.Mode);

					var saver = corecfg.Config["AFK Power"];
					saver["Session lock"].StringValue = GetModeName(SessionLockPowerMode);

					saver["Monitor power off idle timeout"].IntValue = Convert.ToInt32(SessionLockPowerOffIdleTimeout.TotalSeconds);
					saver["Monitor power off on lock"].BoolValue = SessionLockPowerOff;

					// --------------------------------------------------------------------------------------------------------
				}
			}
			corecfg.MarkDirty();
		}

		public void LogBehaviourState()
		{
			string mode = string.Empty;
			switch (Behaviour)
			{
				case PowerBehaviour.Auto:
					mode = HumanReadable.Hardware.Power.AutoAdjust;
					break;
				case PowerBehaviour.RuleBased:
					mode = HumanReadable.Hardware.Power.RuleBased;
					break;
				case PowerBehaviour.Manual:
					mode = HumanReadable.Hardware.Power.Manual;
					break;
				default:
					mode = HumanReadable.Generic.Undefined;
					break;
			}

			Log.Information("<Power> Behaviour: " + mode);
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
		public PowerMode RestoreMode { get; private set; } = PowerMode.Balanced;

		Stopwatch SessionLockCounter = null;
		Stopwatch MonitorOffLastLock = null;
		Stopwatch MonitorPowerOffCounter = new Stopwatch(); // unused?

		async void SessionLockEvent(object _, SessionSwitchEventArgs ev)
		{
			// BUG: ODD BEHAVIOUR ON ACCOUNT SWAP
			switch (ev.Reason)
			{
				case SessionSwitchReason.SessionLogoff:
					Log.Information("<Session> Logoff detected. Exiting.");
					Taskmaster.UnifiedExit(restart:false);
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

			if (Taskmaster.DebugSession)
				Log.Debug("<Session> State: " + (SessionLocked ? "Locked" : "Unlocked"));

			await Task.Delay(0).ConfigureAwait(false); // async

			if (SessionLocked)
			{
				if (CurrentMonitorState != MonitorPowerMode.Off)
				{
					if (SessionLockPowerOff)
					{
						if (Taskmaster.ShowSessionActions || Taskmaster.DebugSession || Taskmaster.DebugMonitor)
							Log.Information("<Session:Lock> Instant monitor power off.");

						SetMonitorMode(MonitorPowerMode.Off);
					}
					else
					{
						if (Taskmaster.ShowSessionActions || Taskmaster.DebugSession || Taskmaster.DebugMonitor)
							Log.Information("<Session:Lock> Instant monitor power off disabled, waiting for user idle.");

						StartDisplayTimer();
					}
				}
				else
				{
					if (Taskmaster.ShowSessionActions || Taskmaster.DebugSession || Taskmaster.DebugMonitor)
						Log.Information("<Session:Lock> Monitor already off, leaving it be.");
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

					Log.Information("<Session:Unlock> Monitor off time: " + $"{off.TotalMinutes:N1} / {total.TotalMinutes:N1} minutess ({percentage * 100d:N1} %)");
				}
			}

			if (SessionLockPowerMode == PowerMode.Undefined) return;

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
						if (Taskmaster.DebugSession)
							Log.Debug("<Session:Lock> Enforcing power plan: " + SessionLockPowerMode.ToString());

						if (SessionLockPowerMode != PowerMode.Undefined)
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
						if (Taskmaster.DebugSession || Taskmaster.ShowSessionActions || Taskmaster.DebugPower)
							Log.Information("<Session:Unlock> Restoring normal power.");

						SleepGivenUp = 0;

						lock (power_lock)
						{
							// TODO: Add configuration for this
							ResetPower(new Cause(OriginType.Session, "Unlock"), true);
						}
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
		}

		void ResetPower(Cause cause = null, bool verbose = false)
		{
			PowerMode mode = RestoreMode;
			if (mode == PowerMode.Undefined && SavedMode != PowerMode.Undefined)
				mode = SavedMode;
			else
				mode = PowerMode.Balanced;

			InternalSetMode(mode, cause, verbose: verbose);

			Behaviour = LaunchBehaviour;
		}

		void BatteryChargingEvent(object _, PowerModeChangedEventArgs ev)
		{
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
					onBatteryResume?.Invoke(this, null);
					// Invoke whatever is necessary to restore functionality after suspend breaking shit.
					break;
			}
		}

		Cause ExpectedCause = new Cause(OriginType.None);
		PowerMode ExpectedMode = PowerMode.Undefined;
		MonitorPowerMode ExpectedMonitorPower = MonitorPowerMode.On;

		protected override void WndProc(ref Message m)
		{
			if (m.Msg == NativeMethods.WM_POWERBROADCAST &&
				m.WParam.ToInt32() == NativeMethods.PBT_POWERSETTINGCHANGE)
			{
				var ps = (NativeMethods.POWERBROADCAST_SETTING)Marshal.PtrToStructure(m.LParam, typeof(NativeMethods.POWERBROADCAST_SETTING));
				
				if (ps.PowerSetting == GUID_POWERSCHEME_PERSONALITY && ps.DataLength == Marshal.SizeOf(typeof(Guid)))
				{
					var pData = (IntPtr)(m.LParam.ToInt32() + Marshal.SizeOf(ps) - 4); // -4 is to align to the ps.Data
					var newPersonality = (Guid)Marshal.PtrToStructure(pData, typeof(Guid));
					var old = CurrentMode;
					if (newPersonality == Balanced) { CurrentMode = PowerMode.Balanced; }
					else if (newPersonality == HighPerformance) { CurrentMode = PowerMode.HighPerformance; }
					else if (newPersonality == PowerSaver) { CurrentMode = PowerMode.PowerSaver; }
					else { CurrentMode = PowerMode.Undefined; }

					onPlanChange?.Invoke(this, new PowerModeEventArgs(CurrentMode, old, CurrentMode == ExpectedMode ? ExpectedCause : new Cause(OriginType.None, "External")));
					ExpectedCause = null;

					if (Taskmaster.DebugPower)
						Log.Information("<Power/OS> Change detected: " + CurrentMode.ToString() + " (" + newPersonality.ToString() + ")");

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

		public static string GetModeName(PowerMode mode)
		{
			switch (mode)
			{
				case PowerMode.Balanced:
					return "Balanced";
				case PowerMode.HighPerformance:
					return "High Performance";
				case PowerMode.PowerSaver:
					return "Power Saver";
				case PowerMode.Custom:
					return "Custom";
				default:
					return "Undefined";
			}
		}

		public static PowerMode GetModeByName(string name)
		{
			if (string.IsNullOrEmpty(name)) return PowerMode.Undefined;

			switch (name.ToLowerInvariant())
			{
				case "low":
				case "powersaver":
				case "power saver":
					return PowerMode.PowerSaver;
				case "average":
				case "medium":
				case "balanced":
					return PowerMode.Balanced;
				case "high":
				case "highperformance":
				case "high performance":
					return PowerMode.HighPerformance;
				default:
					return PowerMode.Undefined;
			}
		}

		public PowerMode OriginalMode { get; private set; } = PowerMode.Balanced;
		public PowerMode CurrentMode { get; private set; } = PowerMode.Balanced;

		PowerMode SavedMode = PowerMode.Undefined;

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
		}

		public void SetRestoreMode(RestoreModeMethod method, PowerMode mode)
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
					RestoreMode = PowerMode.Undefined;
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

			Restore();

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
					Taskmaster.processmanager.CancelPowerWait(); // need nicer way to do this
					Release(-1);
					break;
			}

			if (reset) Behaviour = PowerBehaviour.RuleBased;

			onBehaviourChange?.Invoke(this, new PowerBehaviourEventArgs { Behaviour = Behaviour });

			return Behaviour;
		}

		public void SaveMode()
		{
			if (Behaviour == PowerBehaviour.Auto) return;
			if (SavedMode != PowerMode.Undefined) return;

			lock (power_lock)
			{
				if (SavedMode != PowerMode.Undefined) return;

				if (Taskmaster.DebugPower)
					Log.Debug("<Power> Saving current power mode for later restoration: " + CurrentMode.ToString());

				SavedMode = CurrentMode;

				if (SavedMode == PowerMode.Undefined) Log.Warning("<Power> Failed to get current mode for later restoration.");
			}
		}

		/// <summary>
		/// Restores normal power mode and frees the associated source pid from holding it.
		/// </summary>
		/// <param name="sourcePid">0 releases all locks.</param>
		public async void Release(int sourcePid)
		{
			if (Taskmaster.DebugPower) Log.Debug("<Power> Releasing " + (sourcePid == -1 ? "all locks" : $"#{sourcePid}"));

			Debug.Assert(sourcePid == -1 || !ProcessManager.SystemProcessId(sourcePid));

			try
			{
				if (sourcePid == -1)
				{
					ForceModeSourcesMap.Clear();
				}
				else if (ForceModeSourcesMap.TryRemove(sourcePid, out _))
				{
					if (Taskmaster.DebugPower && Taskmaster.Trace)
						Log.Debug("<Power> Force mode source freed, " + ForceModeSourcesMap.Count.ToString() + " remain.");
				}
				else if (ForceModeSourcesMap.Count > 0)
				{
					if (Taskmaster.DebugPower && Taskmaster.Trace)
						Log.Debug("<Power> Force mode release for unincluded ID, " + ForceModeSourcesMap.Count.ToString() + " remain.");
				}
				else
				{
					if (Taskmaster.DebugPower)
						Log.Debug("<Power> Restore mode called for object [" + sourcePid.ToString() + "] that has no forcing registered. Or waitlist was expunged.");
				}

				Forced = ForceModeSourcesMap.Count > 0;

				if (Taskmaster.Trace && Taskmaster.DebugPower)
					Log.Debug("<Power> Released " + (sourcePid == -1 ? "All" : $"#{sourcePid.ToString()}"));

				Task.Run(async () =>
				{
					if (Behaviour != PowerBehaviour.Auto && PowerdownDelay != TimeSpan.Zero)
						await Task.Delay(PowerdownDelay).ConfigureAwait(false);

					ReleaseFinal();
				}).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		/// <remarks>uses: forceModeSources_lock</remarks>
		void ReleaseFinal()
		{
			int lockCount = ForceModeSourcesMap.Count;
			if (lockCount == 0)
			{
				// TODO: Restore Powerdown delay functionality here.

				if (Taskmaster.Trace && Taskmaster.DebugPower) Log.Debug("<Power> No power locks left.");

				Restore();
			}
			else
			{
				if (Taskmaster.DebugPower)
					Log.Debug("<Power> Forced mode still requested by " + lockCount + " sources: " + string.Join(", ", ForceModeSourcesMap.Keys.ToArray()));
			}
		}

		public void Restore()
		{
			if (Behaviour == PowerBehaviour.Manual)
			{
				if (Taskmaster.DebugPower) Log.Debug("<Power> Power restoration cancelled due to manual control.");
				return;
			}

			lock (power_lock)
			{
				if (RestoreMethod == RestoreModeMethod.Saved)
				{
					if (SavedMode == PowerMode.Undefined) SavedMode = RestoreMode;
				}
				else
					SavedMode = RestoreMode;

				if (SavedMode != CurrentMode && SavedMode != PowerMode.Undefined)
				{
					// if (Behaviour == PowerBehaviour.Auto) return; // this is very optimistic

					if (Taskmaster.DebugPower) Log.Debug("<Power> Restoring power mode: " + SavedMode.ToString());

					InternalSetMode(SavedMode, new Cause(OriginType.None, "Restoration"), verbose:false);
					SavedMode = PowerMode.Undefined;
				}
				else
				{
					if (Taskmaster.DebugPower) Log.Debug("<Power> Power restoration cancelled, target mode is same as current.");
				}
			}
		}

		PowerMode getPowerMode()
		{
			Guid plan;
			var ptr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(IntPtr))); // is this actually necessary?

			lock (power_lock)
			{
				if (NativeMethods.PowerGetActiveScheme((IntPtr)null, out ptr) == 0)
				{
					plan = (Guid)Marshal.PtrToStructure(ptr, typeof(Guid));
					Marshal.FreeHGlobal(ptr);
					if (plan.Equals(Balanced)) { CurrentMode = PowerMode.Balanced; }
					else if (plan.Equals(PowerSaver)) { CurrentMode = PowerMode.PowerSaver; }
					else if (plan.Equals(HighPerformance)) { CurrentMode = PowerMode.HighPerformance; }
					else { CurrentMode = PowerMode.Undefined; }

					Log.Information("<Power> Current: " + CurrentMode.ToString());
				}
			}

			return CurrentMode;
		}

		static readonly object power_lock = new object();

		public PowerBehaviour LaunchBehaviour { get; set; } = PowerBehaviour.RuleBased;
		public PowerBehaviour Behaviour { get; private set; } = PowerBehaviour.RuleBased;

		bool AutoAdjustSetMode(PowerMode mode, Cause cause)
		{
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
		public bool Force(PowerMode mode, int sourcePid)
		{
			if (Behaviour == PowerBehaviour.Manual) return false;
			if (SessionLocked) return false;

			var rv = false;

			SaveMode();

			if (!ForceModeSourcesMap.TryAdd(sourcePid, 0))
			{
				if (Taskmaster.DebugPower && Taskmaster.ShowInaction)
					Log.Debug("<Power> Forcing cancelled, source already in list.");
				return false;
			}

			if (Taskmaster.DebugPower) Log.Debug("<Power> Lock #" + sourcePid);

			Forced = true;

			lock (power_lock)
			{
				rv = mode != CurrentMode;
				if (rv)
				{
					SavedMode = RestoreMethod == RestoreModeMethod.Saved ? CurrentMode : RestoreMode;
					InternalSetMode(mode, cause: new Cause(OriginType.Watchlist, $"PID:{sourcePid}"), verbose:false);
				}
				else
				{
					if (Taskmaster.Trace && Taskmaster.DebugPower) Log.Debug("<Power> Force power mode for mode that is already active. Ignoring.");
				}
			}

			return rv;
		}

		public void SetMode(PowerMode mode, Cause cause=null, bool verbose = true)
		{
			lock (power_lock)
			{
				InternalSetMode(mode, cause, verbose:verbose);
			}
		}

		// BUG: ?? There might be odd behaviour if this is called while Paused==true
		void InternalSetMode(PowerMode mode, Cause cause=null, bool verbose = true)
		{
			var plan = Guid.Empty;
			switch (mode)
			{
				default:
				case PowerMode.Balanced:
					plan = Balanced;
					break;
				case PowerMode.HighPerformance:
					plan = HighPerformance;
					break;
				case PowerMode.PowerSaver:
					plan = PowerSaver;
					break;
			}

			if ((verbose && (CurrentMode != mode)) || Taskmaster.DebugPower)
			{
				string extra = cause != null ? " - Cause: " + cause.ToString() : string.Empty;
				Log.Information("<Power> Setting mode: " + GetModeName(mode) + extra);
			}

			ExpectedMode = CurrentMode = mode;
			ExpectedCause = cause;
			NativeMethods.PowerSetActiveScheme((IntPtr)null, ref plan);
		}

		bool disposed; // = false;
		protected override void Dispose(bool disposing)
		{
			if (disposed) return;

			Behaviour = PowerBehaviour.Internal;

			SystemEvents.SessionSwitch -= SessionLockEvent; // leaks if not disposed

			if (disposing)
			{
				if (Taskmaster.Trace) Log.Verbose("Disposing power manager...");

				SessionLock = null;
				MonitorPower = null;

				if (cpumonitor != null)
				{
					try
					{
						cpumonitor.onSampling -= CPULoadHandler;
					}
					catch { }
				}

				MonitorPower = null;
				MonitorSleepTimer?.Dispose();

				SessionLock = null;
				onAutoAdjustAttempt = null;
				onPlanChange = null;
				onBehaviourChange = null;
				onBatteryResume = null;

				Restore();

				Log.Information("<Power> Auto-adjusted " + AutoAdjustCounter + " time(s).");

				SaveConfig();
			}

			disposed = true;
		}

		async void SetMonitorMode(MonitorPowerMode powermode)
		{
			Debug.Assert(powermode != MonitorPowerMode.Invalid);
			long NewPowerMode = (int)powermode; // -1 = Powering On, 1 = Low Power (low backlight, etc.), 2 = Power Off

			IntPtr Broadcast = new IntPtr(NativeMethods.HWND_BROADCAST); // unreliable
			IntPtr Topmost = new IntPtr(NativeMethods.HWND_TOPMOST);

			uint timeout = 200; // ms per window, we don't really care if they process them
			var flags = NativeMethods.SendMessageTimeoutFlags.SMTO_ABORTIFHUNG|NativeMethods.SendMessageTimeoutFlags.SMTO_NORMAL|NativeMethods.SendMessageTimeoutFlags.SMTO_NOTIMEOUTIFNOTHUNG;

			//IntPtr hWnd = Handle; // send to self works for this? seems even more unreliable
			// there's a lot of discussion on what is the correct way to do this, and many agree broadcast is not good choice even if it works
			// NEVER send it via SendMessage(), only via SendMessageTimeout()
			// PostMessage() is also valid.

			await Task.Delay(0).ConfigureAwait(false);

			ExpectedMonitorPower = powermode;
			NativeMethods.SendMessageTimeout(Broadcast, NativeMethods.WM_SYSCOMMAND, NativeMethods.SC_MONITORPOWER, NewPowerMode, flags, timeout, out _);
		}
	}
}