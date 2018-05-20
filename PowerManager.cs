//
// PowerManager.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2018 M.A.
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
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Serilog;
using Taskmaster.PowerInfo;

namespace Taskmaster
{
	sealed public class PowerModeEventArgs : EventArgs
	{
		public PowerMode OldMode { get; set; }
		public PowerMode NewMode { get; set; }
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

		long AutoAdjustCounter = 0;

		public PowerManager()
		{
			OriginalMode = getPowerMode();

			AutoAdjust.High.Mode = PowerMode.HighPerformance;
			AutoAdjust.High.Commit.Level = 3;
			AutoAdjust.High.Commit.Threshold = 70;

			AutoAdjust.High.Backoff.Level = 3;
			AutoAdjust.High.Backoff.Low = 15;
			AutoAdjust.High.Backoff.Avg = 40;
			AutoAdjust.High.Backoff.High = 60;

			AutoAdjust.Low.Mode = PowerMode.PowerSaver;
			AutoAdjust.Low.Commit.Level = 7;
			AutoAdjust.Low.Commit.Threshold = 15;

			AutoAdjust.Low.Backoff.Level = 2;
			AutoAdjust.Low.Backoff.Low = 25;
			AutoAdjust.Low.Backoff.Avg = 35;
			AutoAdjust.Low.Backoff.High = 50;
			AutoAdjust.Low.Backoff.Level = 5;

			AutoAdjust.DefaultMode = PowerMode.Balanced;

			LoadConfig();

			// SystemEvents.PowerModeChanged += BatteryChargingEvent; // Without laptop testing this feature is difficult

			CPUCounter = new PerformanceCounterWrapper("Processor", "% Processor Time", "_Total");

			onCPUSampling += CPULoadEvent;

			CPUSamples = new float[CPUSampleCount];

			CPUTimer = new System.Timers.Timer(CPUSampleInterval * 1000);
			CPUTimer.Elapsed += CPUSampler;

			if (Behaviour == PowerBehaviour.Auto || !PauseUnneededSampler)
			{
				CPUTimer.Start();
			}

			if (Behaviour == PowerBehaviour.RuleBased && !Forced)
				Restore();

			MonitorPower += MonitorPowerEvent;

			if (SessionLockPowerOffIdleTimeout != 0)
			{
				int timeout = SessionLockPowerOffIdleTimeout * 1000;
				MonitorSleepTimer = new System.Timers.Timer(timeout)
				{
					Enabled = false,
					AutoReset = false
				};
				MonitorSleepTimer.Elapsed += MonitorSleepTimerTick;
			}
		}

		void MonitorPowerEvent(object sender, MonitorPowerEventArgs ev)
		{
			var OldPowerState = CurrentMonitorState;
			CurrentMonitorState = ev.Mode;
			if (Taskmaster.DebugMonitor)
				Log.Debug("<Monitor> Power state: {State}", CurrentMonitorState);

			if (CurrentMonitorState == MonitorPowerMode.On && SessionLocked)
			{
				MonitorSleepTimer?.Start();
			}
			else if (CurrentMonitorState == MonitorPowerMode.Off)
			{
				MonitorSleepTimer?.Stop();
			}
		}

		readonly System.Timers.Timer MonitorSleepTimer;

		void MonitorSleepTimerTick(object sender, EventArgs ev)
		{
			if (CurrentMonitorState == MonitorPowerMode.Off) return;
			if (!SessionLocked) return;

			var lastactive = UserLastActive();
			var idletime = UserIdleFor(lastactive);
			
			if (idletime >= Convert.ToDouble(SessionLockPowerOffIdleTimeout))
			{
				if (Taskmaster.ShowSessionActions || Taskmaster.DebugMonitor)
					Log.Information("<Session:Lock> User idle; Monitor power down...");
				SetMonitorMode(MonitorPowerMode.Off);
			}
			else
			{
				if (Taskmaster.ShowSessionActions || Taskmaster.DebugMonitor)
					Log.Information("<Session:Lock> User active too recently ({Seconds}s ago), delaying monitor power down...", string.Format("{0:N1}", idletime));

				MonitorSleepTimer?.Start();
			}
		}

		bool SessionLocked = false;
		MonitorPowerMode CurrentMonitorState = MonitorPowerMode.Invalid;

		uint UserLastActive()
		{
			var info = new NativeMethods.LASTINPUTINFO();
			info.cbSize = (uint)Marshal.SizeOf(info);
			info.dwTime = 0;
			bool rv = NativeMethods.GetLastInputInfo(ref info);
			if (rv) return info.dwTime;

			// TODO: Throw

			return uint.MinValue;
		}

		/// <summary>
		/// Should be called in same thread as UserLastActive. Odd behaviour expected if the code runs on different core.
		/// </summary>
		/// <param name="lastActive">Last active time, as returned by UserLastActive</param>
		/// <returns>Seconds for how long user has been idle</returns>
		double UserIdleFor(uint lastActive)
		{
			double eticks = Convert.ToDouble(Environment.TickCount);
			double uticks = Convert.ToDouble(lastActive);
			return (eticks - uticks) / 1000f;
		}

		public void SetupEventHook()
		{
			NativeMethods.RegisterPowerSettingNotification(
				Handle, ref GUID_POWERSCHEME_PERSONALITY, NativeMethods.DEVICE_NOTIFY_WINDOW_HANDLE);
			NativeMethods.RegisterPowerSettingNotification(
				Handle, ref GUID_CONSOLE_DISPLAY_STATE, NativeMethods.DEVICE_NOTIFY_WINDOW_HANDLE);

			SystemEvents.SessionEnding += Taskmaster.SessionEndExitRequest;
			SystemEvents.SessionSwitch += SessionLockEvent;
		}

		public int CPUSampleInterval { get; set; } = 5;
		public int CPUSampleCount { get; set; } = 5;
		PerformanceCounterWrapper CPUCounter = null;
		readonly System.Timers.Timer CPUTimer = null;

		public event EventHandler<ProcessorEventArgs> onCPUSampling;
		float[] CPUSamples;
		int CPUSampleLoop = 0;
		float CPUAverage = 0f;

		int cpusampler_lock = 0;
		void CPUSampler(object sender, EventArgs ev)
		{
			if (!Atomic.Lock(ref cpusampler_lock)) return; // uhhh... probably should ping warning if this return is triggered

			try
			{
				float sample = CPUCounter.Value; // slowest part
				CPUAverage -= CPUSamples[CPUSampleLoop];
				CPUAverage += sample;

				CPUSamples[CPUSampleLoop] = sample;
				CPUSampleLoop = (CPUSampleLoop + 1) % CPUSampleCount; // loop offset

				float CPULow = float.MaxValue;
				float CPUHigh = float.MinValue;

				for (int i = 0; i < CPUSampleCount; i++)
				{
					var cur = CPUSamples[i];
					if (cur < CPULow) CPULow = cur;
					else if (cur > CPUHigh) CPUHigh = cur;
				}

				onCPUSampling?.Invoke(this, new ProcessorEventArgs()
				{
					Current = sample,
					Average = CPUAverage / CPUSampleCount,
					High = CPUHigh,
					Low = CPULow
				});
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				Atomic.Unlock(ref cpusampler_lock);
			}
		}

		/// <summary>
		/// Power saver on monitor sleep
		/// </summary>
		bool SaverOnMonitorSleep = false;
		/// <summary>
		/// Session lock power mode
		/// </summary>
		PowerMode SessionLockPowerMode = PowerMode.PowerSaver;
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
		int SessionLockPowerOffIdleTimeout = 300;
		/// <summary>
		/// Power off monitor directly on lock off.
		/// </summary>
		bool SessionLockPowerOff = true;

		public event EventHandler<ProcessorEventArgs> onAutoAdjustAttempt;
		public event EventHandler<PowerModeEventArgs> onPlanChange;
		public event EventHandler<PowerBehaviour> onBehaviourChange;
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
		bool Forced = false;

		int HighPressure = 0;
		int LowPressure = 0;
		PowerReaction PreviousReaction = PowerReaction.Average;

		// TODO: Simplify this mess
		public async void CPULoadEvent(object sender, ProcessorEventArgs ev)
		{
			if (Behaviour != PowerBehaviour.Auto) return;

			await Task.Delay(0).ConfigureAwait(false);

			// TODO: Asyncify. Mostly math so probably unnecessary.

			var Reaction = PowerReaction.Average;
			var ReactionaryPlan = AutoAdjust.DefaultMode;

			var Ready = false;

			ev.Pressure = 0;
			ev.Handled = false;
			if (PreviousReaction == PowerReaction.High)
			{
				// Downgrade to MEDIUM power level
				// Console.WriteLine("Downgrade to Mid?");
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
					// Console.WriteLine("Downgrade to Mid: " + BackoffCounter + " / " + HighBackoffLevel + " = " + ev.Pressure
					// 				  + " : Blocks:" + Convert.ToString(AutoAdjustBlocks, 2).PadLeft(4, '0'));
				}
				// else
				// 	Console.WriteLine("High backoff thresholds not met");
			}
			else if (PreviousReaction == PowerReaction.Low)
			{
				// Upgrade to MEDIUM power level
				// Console.WriteLine("Upgrade to Mid?");
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
					// Console.WriteLine("Upgrade to Mid: " + BackoffCounter + " / " + LowBackoffLevel + " = " + ev.Pressure
					// 				  + " : Blocks:" + Convert.ToString(AutoAdjustBlocks, 2).PadLeft(4, '0'));
				}
				// else
				// 	Console.WriteLine("Low backoff thresholds not met");
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

					// Console.WriteLine("Upgrade to High: " + HighPressure + " / " + HighCommitLevel + " = " + ev.Pressure
					// 				  + " : Blocks:" + Convert.ToString(AutoAdjustBlocks, 2).PadLeft(4, '0'));
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

					// Console.WriteLine("Downgrade to Low: " + LowPressure + " / " + LowCommitLevel + " = " + ev.Pressure
					// 				  + " : Blocks:" + Convert.ToString(AutoAdjustBlocks, 2).PadLeft(4, '0'));
				}
				else
				{
					// Console.WriteLine("NOP");

					Reaction = PowerReaction.Average;
					ReactionaryPlan = AutoAdjust.DefaultMode;

					ResetAutoadjust();

					// Only time this should cause actual power mode change is when something else changes power mode
					Ready = true;
				}
			}

			var ReadyToAdjust = (Ready && !Forced && !Paused);

			if (ReadyToAdjust && ReactionaryPlan != CurrentMode)
			{
				if (Taskmaster.DebugPower) Log.Debug("<Power> Auto-adjust: {Mode}", Reaction.ToString());

				if (AutoAdjustSetMode(ReactionaryPlan))
				{
					AutoAdjustCounter++;
					ev.Handled = true;
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
				if (forceModeSources.Count != 0)
				{
					if (Taskmaster.DebugPower && Taskmaster.ShowInaction)
						Log.Debug("<Power> Can't override forced power mode.");
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

			ev.Mode = ReactionaryPlan;
			onAutoAdjustAttempt?.Invoke(this, ev);
		}

		void ResetAutoadjust()
		{
			BackoffCounter = HighPressure = LowPressure = 0;
			PreviousReaction = PowerReaction.Average;
		}

		bool PauseUnneededSampler = false;
		public static int PowerdownDelay { get; set; } = 0;

		void LoadConfig()
		{
			var power = Taskmaster.cfg["Power"];
			bool modified = false, dirtyconfig = false;

			var defaultmode = power.GetSetDefault("Default mode", GetModeName(PowerMode.Balanced), out modified).StringValue;
			power["Default mode"].Comment = "This is what power plan we fall back on when nothing else is considered.";
			AutoAdjust.DefaultMode = GetModeByName(defaultmode);
			if (AutoAdjust.DefaultMode == PowerMode.Custom)
			{
				Log.Warning("<Power> Default mode malconfigured, defaulting to balanced.");
				AutoAdjust.DefaultMode = PowerMode.Balanced;
			}
			dirtyconfig |= modified;

			var restoremode = power.GetSetDefault("Restore mode", "Default", out modified).StringValue;
			power["Restore mode"].Comment = "Default, Original, Saved, or specific power mode.";
			dirtyconfig |= modified;
			switch (restoremode.ToLowerInvariant())
			{
				case "original":
					RestoreModeMethod = ModeMethod.Original;
					RestoreMode = OriginalMode;
					break;
				case "default":
					RestoreModeMethod = ModeMethod.Default;
					RestoreMode = AutoAdjust.DefaultMode;
					break;
				case "saved":
					RestoreModeMethod = ModeMethod.Saved;
					RestoreMode = PowerMode.Undefined;
					break;
				default:
					RestoreModeMethod = ModeMethod.Custom;
					RestoreMode = GetModeByName(restoremode);
					if (RestoreMode == PowerMode.Custom)
					{
						// TODO: Complain about bad config
						RestoreMode = AutoAdjust.DefaultMode;
					}
					break;
			}

			PowerdownDelay = power.GetSetDefault("Watchlist powerdown delay", 0, out modified).IntValue.Constrain(0, 60);
			power["Watchlist powerdown delay"].Comment = "Delay, in seconds (0 to 60, 0 disables), for when to wind down power mode set by watchlist.";
			dirtyconfig |= modified;

			var autopower = Taskmaster.cfg["Power / Auto"];
			var bAutoAdjust = autopower.GetSetDefault("Auto-adjust", false, out modified).BoolValue;
			autopower["Auto-adjust"].Comment = "Automatically adjust power mode based on the criteria here.";
			dirtyconfig |= modified;
			if (bAutoAdjust)
			{
				Behaviour = PowerBehaviour.Auto;
				if (PowerdownDelay > 0)
				{
					PowerdownDelay = 0;
					Log.Warning("<Power> Powerdown delay is not compatible with auto-adjust, powerdown delay disabled.");
				}
			}

			// should probably be in hardware/cpu section
			PauseUnneededSampler = autopower.GetSetDefault("Pause unneeded CPU sampler", false, out modified).BoolValue;
			autopower["Pause unneeded CPU sampler"].Comment = "Pausing the sampler causes re-enabling it to have a delay in proper behaviour much like at TM's startup.";
			dirtyconfig |= modified;

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

			var saver = Taskmaster.cfg["AFK Power"];
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

			SessionLockPowerOffIdleTimeout = saver.GetSetDefault("Monitor power off idle timeout", 300, out modified).IntValue.Constrain(15, 600);
			saver["Monitor power off idle timeout"].Comment = "User needs to be this many seconds idle before we power down monitors when session is locked. 0 disables.";
			dirtyconfig |= modified;

			SessionLockPowerOff = saver.GetSetDefault("Monitor power off on lock", true, out modified).BoolValue;
			saver["Monitor power off on lock"].Comment = "Power off monitor instantly on session lock.";
			dirtyconfig |= modified;

			// --------------------------------------------------------------------------------------------------------

			// CPU SAMPLING
			// this really should be elsewhere
			var hwsec = Taskmaster.cfg["Hardware"];
			CPUSampleInterval = hwsec.GetSetDefault("CPU sample interval", 2, out modified).IntValue.Constrain(1, 15);
			hwsec["CPU sample interval"].Comment = "1 to 15, in seconds. Frequency at which CPU usage is sampled. Recommended value: 1 to 5 seconds.";
			dirtyconfig |= modified;
			CPUSampleCount = hwsec.GetSetDefault("CPU sample count", 5, out modified).IntValue.Constrain(3, 30);
			hwsec["CPU sample count"].Comment = "3 to 30. Number of CPU samples to keep. Recommended value is: Count * Interval <= 30 seconds";
			dirtyconfig |= modified;

			Log.Information("<CPU> CPU sampler: {Interval}s × {Count} = {Period}s observation period",
							CPUSampleInterval, CPUSampleCount, CPUSampleCount * CPUSampleInterval);
			Log.Information("<Power> Watchlist powerdown delay: {Delay}", (PowerdownDelay == 0 ? "Disabled" : (PowerdownDelay + "s")));

			// --------------------------------------------------------------------------------------------------------

			LogBehaviourState();

			Log.Information("<Power> Session lock: {Mode}", (SessionLockPowerMode == PowerMode.Custom ? "Ignored" : SessionLockPowerMode.ToString()));
			Log.Information("<Power> Restore mode: {Method} [{Mode}]", RestoreModeMethod.ToString(), RestoreMode.ToString());

			Log.Information("<Session> User AFK timeout: {Timeout}", SessionLockPowerOffIdleTimeout == 0 ? "Disabled" : string.Format("{0}s", SessionLockPowerOffIdleTimeout));
			Log.Information("<Session> Immediate power off on lock: {Toggle}", SessionLockPowerOff ? "Enabled" : "Disabled");

			if (dirtyconfig)
				Taskmaster.Config.MarkDirtyINI(Taskmaster.cfg);
		}

		public void LogBehaviourState()
		{
			Log.Information("<Power> Behaviour: {State}",
				(Behaviour == PowerBehaviour.Auto ? "Automatic" : Behaviour == PowerBehaviour.RuleBased ? "Rule-controlled" : "Manual"));
		}

		public AutoAdjustSettings AutoAdjust { get; set; } = new AutoAdjustSettings();

		int BackoffCounter { get; set; } = 0;

		enum ModeMethod
		{
			Original,
			Saved,
			Default,
			Custom
		};

		ModeMethod RestoreModeMethod = ModeMethod.Saved;
		PowerMode RestoreMode { get; set; } = PowerMode.Balanced;

		void SessionLockEvent(object sender, SessionSwitchEventArgs ev)
		{
			switch (ev.Reason)
			{
				case SessionSwitchReason.SessionLock:
					SessionLocked = true;
					break;
				case SessionSwitchReason.SessionUnlock:
					SessionLocked = false;
					break;
			}

			if (Taskmaster.DebugSession)
				Log.Debug("<Session> State: {State}", SessionLocked ? "Locked" : "Unlocked");

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

						MonitorSleepTimer?.Start();
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
				MonitorSleepTimer?.Stop();

				// should be unnecessary, but...
				if (CurrentMonitorState != MonitorPowerMode.On)
				{
					if (Taskmaster.DebugMonitor || Taskmaster.DebugSession)
						Log.Debug("<Session:Unlock> Monitor still not on... Odd, isn't it?");

					SetMonitorMode(MonitorPowerMode.On);
				}
			}

			if (SessionLockPowerMode == PowerMode.Custom) return;

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
						if (SessionLockPowerMode != PowerMode.Custom)
						{
							Paused = true;

							if (Taskmaster.DebugSession)
								Log.Debug("<Session:Lock> Enforcing power plan: {Plan}", SessionLockPowerMode.ToString());

							if (PauseUnneededSampler) CPUTimer.Stop();

							if (CurrentMode != SessionLockPowerMode)
								InternalSetMode(PowerMode.PowerSaver, true);
						}
						break;
					case SessionSwitchReason.SessionLogon:
					case SessionSwitchReason.SessionUnlock:
						// RESTORE POWER MODE
						if (SessionLockPowerMode != PowerMode.Custom)
						{
							if (Taskmaster.DebugSession || Taskmaster.ShowSessionActions || Taskmaster.DebugPower)
								Log.Information("<Session:Unlock> Restoring normal power.");

							if (CurrentMode == SessionLockPowerMode)
							{
								InternalSetMode(RestoreMode, true);
							}

							Paused = false;

							if (PauseUnneededSampler) CPUTimer.Start();
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

		void BatteryChargingEvent(object sender, PowerModeChangedEventArgs ev)
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

					onPlanChange?.Invoke(this, new PowerModeEventArgs { OldMode = old, NewMode = CurrentMode });

					if (Taskmaster.DebugPower)
						Log.Information("<Power/OS> Change detected: {PlanName} ({PlanGuid})", CurrentMode.ToString(), newPersonality.ToString());
				}
				else if (ps.PowerSetting == GUID_CONSOLE_DISPLAY_STATE)
				{
					switch (ps.Data)
					{
						case 0x0: // power off
							MonitorPower?.Invoke(this, new MonitorPowerEventArgs(MonitorPowerMode.Off));
							break;
						case 0x1: // power on
							MonitorPower?.Invoke(this, new MonitorPowerEventArgs(MonitorPowerMode.On));
							break;
						case 0x2: // standby
							MonitorPower?.Invoke(this, new MonitorPowerEventArgs(MonitorPowerMode.Standby));
							break;
					}
				}
			}

			base.WndProc(ref m); // is this necessary?
		}

		public static readonly string[] PowerModes = { "Power Saver", "Balanced", "High Performance", "Undefined" };

		public static string GetModeName(PowerMode mode)
		{
			if ((int)mode > 3) return string.Empty;
			return PowerModes[(int)mode];
		}

		public static PowerMode GetModeByName(string name)
		{
			if (string.IsNullOrEmpty(name)) return PowerMode.Undefined;

			switch (name.ToLowerInvariant())
			{
				case "power saver":
					return PowerMode.PowerSaver;
				case "balanced":
					return PowerMode.Balanced;
				case "high performance":
					return PowerMode.HighPerformance;
				default:
					return PowerMode.Custom;
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
			Manual
		}

		public PowerBehaviour SetBehaviour(PowerBehaviour pb)
		{
			if (pb == Behaviour) return Behaviour; // this shouldn't happen

			Behaviour = pb;
			LogBehaviourState();

			if (Behaviour == PowerBehaviour.Auto)
			{
				ResetAutoadjust();

				if (PauseUnneededSampler)
				{
					CPUSamples = new float[CPUSampleCount]; // reset samples
					CPUTimer.Start();
					Log.Debug("CPU sampler restarted.");
				}
			}
			else if (Behaviour == PowerBehaviour.RuleBased)
			{
				if (PauseUnneededSampler) CPUTimer.Stop();
			}
			else // MANUAL
			{
				if (PauseUnneededSampler) CPUTimer.Stop();

				Taskmaster.processmanager.CancelPowerWait(); // need nicer way to do this

				Release(0).ConfigureAwait(false);
			}

			onBehaviourChange?.Invoke(this, Behaviour);

			return Behaviour;
		}

		public void SaveMode()
		{
			if (Behaviour == PowerBehaviour.Auto) return;
			if (SavedMode != PowerMode.Undefined) return;

			if (Taskmaster.DebugPower)
				Log.Debug("<Power> Saving current power mode for later restoration: {Mode}", CurrentMode.ToString());

			lock (power_lock)
			{
				SavedMode = CurrentMode;

				if (SavedMode == PowerMode.Undefined)
				{
					Log.Warning("<Power> Failed to get current mode, defafulting to balanced as restore option.");
					SavedMode = PowerMode.Balanced;
				}
			}
		}

		/// <summary>
		/// Restores normal power mode and frees the associated source pid from holding it.
		/// </summary>
		/// <param name="sourcePid">0 releases all locks.</param>
		/// <remarks>
		/// 
		/// </remarks>
		public async Task Release(int sourcePid = -1)
		{
			if (Taskmaster.DebugPower)
			{
				if (sourcePid == 0)
					Log.Debug("<Power> Release – clearing all locks");
				else
					Log.Debug("<Power> Release(#{Source})", sourcePid);
			}

			Debug.Assert(sourcePid == 0 || sourcePid > 4);
			if (Paused) return; // TODO: What to do in the unlikely event of this being called while paused?

			try
			{
				lock (forceModeSources_lock)
				{
					if (sourcePid == 0)
					{
						forceModeSources.Clear();
						if (Taskmaster.DebugPower)
							Log.Debug("<Power> Cleared forced list.");
					}
					else if (forceModeSources.Contains(sourcePid))
					{
						forceModeSources.Remove(sourcePid);
						if (Taskmaster.DebugPower && Taskmaster.Trace)
							Log.Debug("<Power> Force mode source freed, {Count} remain.", forceModeSources.Count);
					}
					else
					{
						if (Taskmaster.DebugPower)
							Log.Debug("<Power> Restore mode called for object [{Source}] that has no forcing registered. Or waitlist was expunged.", sourcePid);
					}
				}

				if (PowerdownDelay > 0)
				{
					if (Taskmaster.DebugPower)
						Log.Debug("<Power> Powerdown delay: {Delay}s", PowerdownDelay);

					await Task.Delay(PowerdownDelay * 1000).ConfigureAwait(false);
				}

				lock (forceModeSources_lock)
				{
					if (forceModeSources.Count == 0)
					{
						// TODO: Restore Powerdown delay functionality here.

						Restore();

						Forced = false;
					}
					else
					{
						if (Taskmaster.DebugPower)
						{
							Log.Debug("<Power> Forced mode still requested by {sources} sources.", forceModeSources.Count);
							if (forceModeSources.Count > 0)
							{
								Log.Debug("<Power> Sources: {Sources}", string.Join(", ", forceModeSources.ToArray()));
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}

			if (Taskmaster.DebugPower)
				Log.Debug("<Power> Released ({Val}).", sourcePid == 0 ? "All" : sourcePid.ToString());

			return;
		}

		public void Restore()
		{
			if (Taskmaster.DebugPower)
				Log.Debug("<Power> Restoring power mode!");

			lock (power_lock)
			{
				if (RestoreModeMethod != ModeMethod.Saved)
					SavedMode = RestoreMode;

				if (Taskmaster.DebugPower)
					Log.Debug("Restoring mode: {Mode} [{Method}]", SavedMode.ToString(), RestoreModeMethod.ToString());

				if (SavedMode != CurrentMode && SavedMode != PowerMode.Undefined)
				{
					// if (Behaviour == PowerBehaviour.Auto) return; // this is very optimistic

					InternalSetMode(SavedMode, verbose: true);
					SavedMode = PowerMode.Undefined;

					// Log.Information("<Power> Restored to: {PowerMode}", CurrentMode.ToString());
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

					Log.Information("<Power> Current: {Plan} ({Guid})", CurrentMode.ToString(), plan.ToString());
				}
			}

			return CurrentMode;
		}

		static readonly object power_lock = new object();
		static readonly object powerLockI = new object();

		public PowerBehaviour Behaviour { get; private set; } = PowerBehaviour.RuleBased;

		bool AutoAdjustSetMode(PowerMode mode)
		{
			Debug.Assert(Behaviour == PowerBehaviour.Auto, "This is for auto adjusting only.");

			if (Paused) return false;

			if (mode == CurrentMode || forceModeSources.Count > 0)
				return false;

			InternalSetMode(mode, verbose: false);
			return true;
		}
		
		public int ForceCount => forceModeSources.Count;

		HashSet<int> forceModeSources = new HashSet<int>();
		readonly object forceModeSources_lock = new object();

		public bool Force(PowerMode mode, int sourcePid)
		{
			if (Behaviour == PowerBehaviour.Manual) return false;
			if (Paused) return false;

			var rv = false;

			lock (forceModeSources_lock)
			{
				if (forceModeSources.Contains(sourcePid))
				{
					if (Taskmaster.ShowInaction)
						Log.Debug("<Power> Forcing cancelled, source already in list.");
					return false;
				}

				forceModeSources.Add(sourcePid);
			}

			Forced = true;

			rv = mode != CurrentMode;
			if (rv)
			{
				SavedMode = RestoreModeMethod == ModeMethod.Saved ? CurrentMode : RestoreMode;
				InternalSetMode(mode, verbose:true);

				if (Taskmaster.DebugPower) Log.Debug("<Power> Forced to: {PowerMode}", CurrentMode);
			}
			else
			{
				if (Taskmaster.DebugPower) Log.Debug("<Power> Force power mode for mode that is already active. Ignoring.");
			}

			return rv;
		}

		public void SetMode(PowerMode mode, bool verbose = true)
		{
			lock (power_lock)
			{
				InternalSetMode(mode, verbose);
			}
		}

		void InternalSetMode(PowerMode mode, bool verbose = true)
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
				Log.Information("<Power> Setting to: {Mode} ({Guid})", mode.ToString(), plan.ToString());

			CurrentMode = mode;
			NativeMethods.PowerSetActiveScheme((IntPtr)null, ref plan);
		}

		bool disposed; // = false;
		protected override void Dispose(bool disposing)
		{
			if (disposed) return;

			if (disposing)
			{
				if (Taskmaster.Trace) Log.Verbose("Disposing power manager...");

				CPUTimer?.Dispose();
				CPUCounter?.Dispose();
				MonitorSleepTimer?.Dispose();

				var finalmode = RestoreModeMethod == ModeMethod.Saved ? SavedMode : RestoreMode;
				if (finalmode != CurrentMode)
				{
					InternalSetMode(finalmode, true);
					Log.Information("<Power> Restored.");
				}
				Log.Information("<Power> Auto-adjusted {Counter} time(s).", AutoAdjustCounter);
			}

			disposed = true;
		}

		public const int WM_SYSCOMMAND = 0x0112;
		public const int WM_POWERBROADCAST = 0x218;
		public const int SC_MONITORPOWER = 0xF170;
		public const int PBT_POWERSETTINGCHANGE = 0x8013;
		public const int HWND_BROADCAST = 0xFFFF;

		readonly IntPtr MonitorPowerP = new IntPtr(SC_MONITORPOWER);
		void SetMonitorMode(MonitorPowerMode powermode)
		{
			IntPtr NewPowerMode = new IntPtr((int)powermode); // -1 = Powering On, 1 = Low Power (low backlight, etc.), 2 = Power Off
			IntPtr Handle = new IntPtr(HWND_BROADCAST);
			IntPtr result = new IntPtr(-1); // unused, but necessary
			uint timeout = 200; // ms per window, we don't really care if they process them
			var flags = NativeMethods.SendMessageTimeoutFlags.SMTO_ABORTIFHUNG;
			NativeMethods.SendMessageTimeout(Handle, WM_SYSCOMMAND, MonitorPowerP, NewPowerMode, flags, timeout, out result);
		}
	}
}