//
// PowerManager.cs
//
// Author:
//       M.A. (enmoku) <>
//
// Copyright (c) 2018 M.A. (enmoku)
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
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Serilog;
using Microsoft.Win32;
using System.Diagnostics.Eventing.Reader;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Diagnostics;

namespace TaskMaster
{
	public class PowerModeEventArgs : EventArgs
	{
		public PowerManager.PowerMode OldMode { get; set; }
		public PowerManager.PowerMode NewMode { get; set; }
	}

	public class PowerManager : Form // form is required for receiving messages, no other reason
	{
		//static Guid GUID_POWERSCHEME_PERSONALITY = new Guid("245d8541-3943-4422-b025-13A7-84F679B7");
		static Guid GUID_POWERSCHEME_PERSONALITY = new Guid(0x245D8541, 0x3943, 0x4422, 0xB0, 0x25, 0x13, 0xA7, 0x84, 0xF6, 0x79, 0xB7);

		long AutoAdjustCounter = 0;

		public PowerManager()
		{
			RegisterPowerSettingNotification(Handle, ref GUID_POWERSCHEME_PERSONALITY, DEVICE_NOTIFY_WINDOW_HANDLE);
			OriginalMode = getPowerMode();

			LoadConfig();

			//SystemEvents.PowerModeChanged += BatteryChargingEvent; // Without laptop testing this feature is difficult
			SystemEvents.SessionEnding += TaskMaster.ExitRequest;

			if (SaverOnSessionLock)
				SystemEvents.SessionSwitch += SessionLockEvent;

			CPUCounter = new System.Diagnostics.PerformanceCounter("Processor", "% Processor Time", "_Total");
			CPUCounter.NextValue();

			onCPUSampling += CPULoadEvent;

			CPUTimer = new System.Timers.Timer();
			CPUTimer.Interval = 1000 * CPUSampleInterval;
			CPUTimer.Elapsed += CPUSampler;

			if (Behaviour == PowerBehaviour.Auto || !PauseUnneededSampler)
				CPUTimer.Start();
		}

		public int CPUSampleInterval { get; set; } = 5;
		System.Diagnostics.PerformanceCounter CPUCounter = null;
		System.Timers.Timer CPUTimer = null;

		public event EventHandler<ProcessorEventArgs> onCPUSampling;
		float[] CPUSamples = { 0, 0, 0, 0, 0 };
		int CPUSampleLoop = 0;

		void CPUSampler(object sender, EventArgs ev)
		{
			float sample = CPUCounter.NextValue();
			CPUSamples[CPUSampleLoop++] = sample;
			if (CPUSampleLoop > 4) CPUSampleLoop = 0;
			float average = 0;
			float high = 0;
			float low = float.MaxValue;
			for (int i = 0; i < 5; i++)
			{
				float cur = CPUSamples[i];
				average += cur;
				if (cur < low) low = cur;
				if (cur > high) high = cur;
			}
			average /= 5;

			onCPUSampling?.Invoke(this, new ProcessorEventArgs() { Current = sample, Average = average, High = high, Low = low });
		}

		static bool Pause = false;

		bool SaverOnMonitorSleep = false;
		bool SaverOnSessionLock = false;
		bool SaverOnLogOff = false;
		bool SaverOnScreensaver = false;
		bool UserActiveCancel = true;
		int SaverOnUserAFK = 0;

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

		int HighPressure = 0;
		int LowPressure = 0;
		PowerReaction PreviousReaction = PowerReaction.Average;
		// TODO: Simplify this mess
		public void CPULoadEvent(object sender, ProcessorEventArgs ev)
		{
			if (Behaviour != PowerBehaviour.Auto) return;

			PowerReaction Reaction = PowerReaction.Average;
			PowerMode ReactionaryPlan = DefaultMode;

			ev.Pressure = 0;
			ev.Handled = false;
			bool ReadyToChange = false;
			if (PreviousReaction == PowerReaction.High)
			{
				//Console.WriteLine("Downgrade to Mid?");
				if (ev.High <= HighBackoffThresholds[(int)ThresholdLevel.High]
					|| ev.Average <= HighBackoffThresholds[(int)ThresholdLevel.Average]
					|| ev.Low <= HighBackoffThresholds[(int)ThresholdLevel.Average])
				{
					Reaction = PowerReaction.Average;
					ReactionaryPlan = DefaultMode;

					BackoffCounter++;

					ReadyToChange |= BackoffCounter >= HighBackoffLevel;

					ev.Pressure = ((float)BackoffCounter) / ((float)HighBackoffLevel);
					//Console.WriteLine("Downgrade to Mid: " + BackoffCounter + " / " + HighBackoffLevel + " = " + ev.Pressure);
				}
				//else
				//	Console.WriteLine("Thresholds not met");
			}
			else if (PreviousReaction == PowerReaction.Low)
			{
				//Console.WriteLine("Upgrade to Mid?");
				if (ev.High >= LowBackoffThresholds[(int)ThresholdLevel.High]
					|| ev.Average >= LowBackoffThresholds[(int)ThresholdLevel.Average]
					|| ev.Low >= LowBackoffThresholds[(int)ThresholdLevel.Average])
				{
					Reaction = PowerReaction.Average;
					ReactionaryPlan = DefaultMode;

					BackoffCounter++;

					ReadyToChange |= BackoffCounter >= LowBackoffLevel;

					ev.Pressure = ((float)BackoffCounter) / ((float)LowBackoffLevel);
					//Console.WriteLine("Upgrade to Mid: " + BackoffCounter + " / " + LowBackoffLevel + " = " + ev.Pressure);
				}
				//else
				//	Console.WriteLine("Thresholds not met");
			}
			else
			{
				if (ev.Low > HighThreshold) // Low CPU is above threshold for High mode
				{
					Reaction = PowerReaction.High;
					ReactionaryPlan = HighMode;

					LowPressure = 0; // reset
					HighPressure++;

					ReadyToChange |= HighPressure >= HighCommitLevel;

					ev.Pressure = ((float)HighPressure) / ((float)HighCommitLevel);
					//Console.WriteLine("Upgrade to Low: " + HighPressure + " / " + HighCommitLevel + " = " + ev.Pressure);
				}
				else if (ev.High < LowThreshold) // High CPU is below threshold for Low mode
				{
					Reaction = PowerReaction.Low;
					ReactionaryPlan = LowMode;

					HighPressure = 0; // reset
					LowPressure++;

					ReadyToChange |= LowPressure >= LowCommitLevel;

					ev.Pressure = ((float)LowPressure) / ((float)LowCommitLevel);
					//Console.WriteLine("Downgrade to Low: " + LowPressure + " / " + LowCommitLevel + " = " + ev.Pressure);
				}
				else
				{
					//Console.WriteLine("NOP");

					Reaction = PowerReaction.Average;
					ReactionaryPlan = DefaultMode;

					BackoffCounter = HighPressure = LowPressure = 0;
					ev.Pressure = 0f;

					ReadyToChange = true; // Only time this should cause actual power mode change is when something else changes power mode
				}
			}

			if (ReactionaryPlan != CurrentMode && ReadyToChange && !ForcedMode)
			{
				if (TaskMaster.DebugPower)
					Log.Debug("<Power Mode> Auto-adjust: {Mode}", Reaction.ToString());
				if (RequestMode(ReactionaryPlan))
				{
					AutoAdjustCounter++;
					ev.Handled = true;
					PreviousReaction = Reaction;
				}
				else
				{
					if (TaskMaster.DebugPower)
						Log.Warning("<Power Mode> Failed to auto-adjust power.");
				}

				BackoffCounter = HighPressure = LowPressure = 0;
			}

			//Console.WriteLine("Cause: " + ev.Cause.ToString() + ", ReadyToChange: " + readyToChange + ", Reaction: " + Reaction.ToString()
			//				  + ", Enacted: __" + ev.Handled.ToString() + "__, Pause: " + Pause + ", Mode: " + Behaviour.ToString());

			ev.Mode = ReactionaryPlan;
			//ev.Pressure = BackoffCounter;
			onAutoAdjustAttempt?.Invoke(this, ev);
		}

		bool PauseUnneededSampler = false;

		void LoadConfig()
		{
			var power = TaskMaster.cfg["Power"];
			bool modified = false, dirtyconfig = false;

			RestoreOriginal = power.GetSetDefault("Restore original", false, out modified).BoolValue;
			power["Restore original"].Comment = "Restore original power mode instead of the one saved before changing it (only when changing with watchlist rules).";
			dirtyconfig |= modified;

			bool AutoAdjust = power.GetSetDefault("Auto-adjust", false, out modified).BoolValue;
			power["Auto-adjust"].Comment = "Automatically adjust power mode based on the criteria here.";
			dirtyconfig |= modified;
			if (AutoAdjust)
				Behaviour = PowerBehaviour.Auto;

			PauseUnneededSampler = power.GetSetDefault("Pause unneeded CPU sampler", false, out modified).BoolValue;
			power["Pause unneeded CPU sampler"].Comment = "Pausing the sampler causes re-enabling it to have a delay in proper behaviour much like at TM's startup.";
			dirtyconfig |= modified;

			// BACKOFF
			LowBackoffLevel = power.GetSetDefault("Low backoff level", 2, out modified).IntValue.Constrain(0, 10);
			power["Low backoff level"].Comment = "1 to 10. Consequent backoff reactions that is required before it actually triggers.";
			dirtyconfig |= modified;
			HighBackoffLevel = power.GetSetDefault("High backoff level", 3, out modified).IntValue.Constrain(0, 10);
			power["High backoff level"].Comment = "1 to 10. Consequent backoff reactions that is required before it actually triggers.";
			dirtyconfig |= modified;

			// COMMIT
			LowCommitLevel = power.GetSetDefault("Low commit level", 2, out modified).IntValue.Constrain(1, 10);
			power["Low commit level"].Comment = "1 to 10. Consequent commit reactions that is required before it actually triggers.";
			dirtyconfig |= modified;
			HighCommitLevel = power.GetSetDefault("High commit level", 3, out modified).IntValue.Constrain(1, 10);
			power["High commit level"].Comment = "1 to 10. Consequent commit reactions that is required before it actually triggers.";
			dirtyconfig |= modified;

			// THRESHOLDS
			HighThreshold = power.GetSetDefault("High threshold", 70, out modified).FloatValue;
			power["High threshold"].Comment = "If low CPU value keeps over this, we swap to high mode.";
			dirtyconfig |= modified;
			var hbtt = power.GetSetDefault("High backoff thresholds", new float[] { 60, 40, 20 }, out modified).FloatValueArray;
			if (hbtt != null && hbtt.Length == 3) HighBackoffThresholds = hbtt;
			power["High backoff thresholds"].Comment = "High, Average and Low CPU usage values, any of which is enough to break away from high power mode.";
			dirtyconfig |= modified;

			LowThreshold = power.GetSetDefault("Low threshold", 25, out modified).FloatValue;
			power["Low threshold"].Comment = "If high CPU value keeps under this, we swap to low mode.";
			dirtyconfig |= modified;
			var lbtt = power.GetSetDefault("Low backoff thresholds", new float[] { 50, 40, 25 }, out modified).FloatValueArray;
			if (lbtt != null && lbtt.Length == 3) LowBackoffThresholds = lbtt;
			power["Low backoff thresholds"].Comment = "High, Average and Low CPU uage values, any of which is enough to break away from low mode.";
			dirtyconfig |= modified;

			// POWER MODES
			string defaultmode = power.GetSetDefault("Default mode", "Balanced", out modified).StringValue;
			power["Default mode"].Comment = "This is what power plan we fall back on when nothing else is considered.";
			DefaultMode = GetModeByName(defaultmode);
			dirtyconfig |= modified;
			string lowmode = power.GetSetDefault("Low mode", "Power Saver", out modified).StringValue;
			LowMode = GetModeByName(lowmode);
			dirtyconfig |= modified;
			string highmode = power.GetSetDefault("High mode", "High Performance", out modified).StringValue;
			HighMode = GetModeByName(highmode);
			dirtyconfig |= modified;

			var saver = TaskMaster.cfg["Power Save"];
			saver.Comment = "All these options control when to enforce power save mode regardless of any other options.";
			SaverOnSessionLock = saver.GetSetDefault("Session lock", true, out modified).BoolValue;
			dirtyconfig |= modified;
			SaverOnLogOff = saver.GetSetDefault("Log off", true, out modified).BoolValue;
			dirtyconfig |= modified;
			SaverOnMonitorSleep = saver.GetSetDefault("Monitor sleep", true, out modified).BoolValue;
			dirtyconfig |= modified;
			SaverOnScreensaver = saver.GetSetDefault("Screensaver", true, out modified).BoolValue;
			dirtyconfig |= modified;

			SaverOnUserAFK = saver.GetSetDefault("User idle", 30, out modified).IntValue;
			dirtyconfig |= modified;
			UserActiveCancel = saver.GetSetDefault("Cancel on activity", true, out modified).BoolValue;

			dirtyconfig |= modified;

			// --------------------------------------------------------------------------------------------------------

			// CPU SAMPLING
			// this really should be elsewhere
			var hwsec = TaskMaster.cfg["Hardware"];
			CPUSampleInterval = hwsec.GetSetDefault("CPU sample interval", 5, out modified).IntValue.Constrain(1, 15);
			hwsec["CPU sample interval"].Comment = "1 to 15, in seconds. Frequency at which CPU usage is sampled. Recommended value: 1 to 5.";
			dirtyconfig |= modified;

			// --------------------------------------------------------------------------------------------------------

			LogState();

			if (dirtyconfig)
				TaskMaster.MarkDirtyINI(TaskMaster.cfg);
		}

		public void LogState()
		{
			Log.Information("<Power Mode> Behaviour: {State}",
				(Behaviour == PowerBehaviour.Auto ? "Automatic" : Behaviour == PowerBehaviour.RuleBased ? "Rule-controlled" : "Manual"));
		}

		int BackoffCounter { get; set; } = 0;
		int LowBackoffLevel { get; set; } = 2;
		int LowCommitLevel { get; set; } = 2;
		int HighBackoffLevel { get; set; } = 2;
		int HighCommitLevel { get; set; } = 3;

		float HighThreshold { get; set; } = 75;
		float[] HighBackoffThresholds { get; set; } = { 60, 50, 40 }; // High, Average, Low
		PowerMode HighMode { get; set; }
		float LowThreshold { get; set; } = 15;
		float[] LowBackoffThresholds { get; set; } = { 30, 25, 20 }; // High, Average, Low
		PowerMode LowMode { get; set; }
		PowerMode DefaultMode { get; set; }

		void SessionLockEvent(object sender, SessionSwitchEventArgs ev)
		{
			switch (ev.Reason)
			{
				case SessionSwitchReason.SessionLogoff:
				case SessionSwitchReason.SessionLogon:
					if (!SaverOnLogOff)
						return;
					break;
			}

			switch (ev.Reason)
			{
				case SessionSwitchReason.SessionLogoff:
				case SessionSwitchReason.SessionLock:
					// SET POWER SAVER
					if (SaverOnSessionLock)
					{
						if (CurrentMode != PowerMode.PowerSaver)
						{
							Pause = true;
							Log.Information("<Power Mode> Session locked, enforcing low power plan.");
							setMode(PowerMode.PowerSaver, true);
						}
					}
					break;
				case SessionSwitchReason.SessionLogon:
				case SessionSwitchReason.SessionUnlock:
					// RESTORE POWER MODE
					if (SaverOnSessionLock)
					{
						if (CurrentMode == PowerMode.PowerSaver && Pause)
						{
							Log.Information("<Power Mode> Session unlocked, restoring normal power.");
							setMode(DefaultMode, true);
							Pause = false;
							TaskMaster.Evaluate().ConfigureAwait(false);
						}
					}
					break;
				default:
					// HANDS OFF MODE
					break;
			}
		}

		void BatteryChargingEvent(object sender, PowerModeChangedEventArgs ev)
		{
			switch (ev.Mode)
			{
				case Microsoft.Win32.PowerModes.StatusChange:
					Log.Information("Undefined battery/AC change detected.");
					//System.Windows.Forms.PowerStatus
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
			if (m.Msg == WM_POWERBROADCAST && m.WParam.ToInt32() == PBT_POWERSETTINGCHANGE)
			{
				POWERBROADCAST_SETTING ps = (POWERBROADCAST_SETTING)Marshal.PtrToStructure(m.LParam, typeof(POWERBROADCAST_SETTING));

				if (ps.PowerSetting == GUID_POWERSCHEME_PERSONALITY && ps.DataLength == Marshal.SizeOf(typeof(Guid)))
				{
					IntPtr pData = (IntPtr)(m.LParam.ToInt32() + Marshal.SizeOf(ps));  // (*1)
					Guid newPersonality = (Guid)Marshal.PtrToStructure(pData, typeof(Guid));
					PowerMode old = CurrentMode;
					if (newPersonality == Balanced) { CurrentMode = PowerMode.Balanced; }
					else if (newPersonality == HighPerformance) { CurrentMode = PowerMode.HighPerformance; }
					else if (newPersonality == PowerSaver) { CurrentMode = PowerMode.PowerSaver; }
					else { CurrentMode = PowerMode.Undefined; }

					onPlanChange?.Invoke(this, new PowerModeEventArgs { OldMode = old, NewMode = CurrentMode });

					if (TaskMaster.DebugPower)
						Log.Debug("<Power Mode> Change detected: {PlanName} ({PlanGuid})", CurrentMode.ToString(), newPersonality.ToString());
				}
			}

			base.WndProc(ref m); // is this necessary
		}

		public static string[] PowerModes { get; } = { "Power Saver", "Balanced", "High Performance", "Undefined" };

		public static string GetModeName(PowerMode mode)
		{
			if (mode == PowerMode.Custom) return null;
			return PowerModes[(int)mode];
		}

		public static PowerMode GetModeByName(string name)
		{
			if (string.IsNullOrEmpty(name)) return PowerMode.Undefined;

			switch (name)
			{
				case "Power Saver":
					return PowerMode.PowerSaver;
				case "Balanced":
					return PowerMode.Balanced;
				case "High Performance":
					return PowerMode.HighPerformance;
				default:
					return PowerMode.Custom;
			}
		}

		public enum PowerMode
		{
			PowerSaver = 0,
			Balanced = 1,
			HighPerformance = 2,
			Custom = 9,
			Undefined = 3
		};

		public PowerMode OriginalMode { get; private set; } = PowerMode.Balanced;
		public PowerMode CurrentMode { get; private set; } = PowerMode.Balanced;

		PowerMode SavedMode = PowerMode.Undefined;

		static Guid HighPerformance = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"); // SCHEME_MIN
		static Guid Balanced = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e"); // SCHEME_BALANCED
		static Guid PowerSaver = new Guid("a1841308-3541-4fab-bc81-f71556f20b4a"); // SCHEME_MAX

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
			LogState();

			if (Behaviour == PowerBehaviour.Auto)
			{
				if (PauseUnneededSampler)
				{
					CPUSamples = new float[] { 0, 0, 0, 0, 0 }; // reset samples
					CPUTimer.Start();
					Log.Debug("CPU sampler restarted.");
				}
			}
			else if (Behaviour == PowerBehaviour.RuleBased)
			{
				if (PauseUnneededSampler)
					CPUTimer.Stop();
			}
			else // MANUAL
			{
				if (PauseUnneededSampler)
					CPUTimer.Stop();
				ProcessController.CancelPowerControlEvent();
			}

			onBehaviourChange?.Invoke(this, Behaviour);

			return Behaviour;
		}

		public void SaveMode()
		{
			if (Behaviour == PowerBehaviour.Auto) return;

			if (SavedMode != PowerMode.Undefined) Log.Warning("<Power Mode> Saved mode is being overriden.");

			if (TaskMaster.DebugPower)
				Log.Debug("<Power Mode> Saving current power mode for later restoration: {Mode}", CurrentMode.ToString());

			lock (powerLock)
			{

				SavedMode = CurrentMode;

				if (SavedMode == PowerMode.Undefined)
				{
					Log.Warning("<Power Mode> Failed to get current mode, defafulting to balanced as restore option.");
					SavedMode = PowerMode.Balanced;
				}
			}
		}

		bool RestoreOriginal = false;
		public void RestoreMode()
		{
			if (Pause) return; // TODO: What to do in the unlikely event of this being called while paused?

			lock (powerLock)
			{
				if (RestoreOriginal)
					SavedMode = OriginalMode;

				ForcedMode = false;
				if (SavedMode != PowerMode.Undefined && SavedMode != CurrentMode)
				{
					if (Behaviour == PowerBehaviour.Auto) return;

					setMode(SavedMode);
					SavedMode = PowerMode.Undefined;

					if (TaskMaster.DebugPower)
						Log.Debug("<Power Mode> Restored to: {PowerMode}", CurrentMode.ToString());
				}
			}
		}

		PowerMode getPowerMode()
		{
			Guid plan;
			IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(IntPtr)));
			lock (powerLock)
			{
				if (PowerGetActiveScheme((IntPtr)null, out ptr) == 0)
				{
					plan = (Guid)Marshal.PtrToStructure(ptr, typeof(Guid));
					Marshal.FreeHGlobal(ptr);

					if (plan == Balanced) { CurrentMode = PowerMode.Balanced; }
					else if (plan == PowerSaver) { CurrentMode = PowerMode.PowerSaver; }
					else if (plan == HighPerformance) { CurrentMode = PowerMode.HighPerformance; }
					else { CurrentMode = PowerMode.Undefined; }

					Log.Information("<Power Mode> Current: {Plan} ({Guid})", CurrentMode.ToString(), plan.ToString());
				}
			}
			return CurrentMode;
		}

		public static object powerLock = new object();
		public static object powerLockI = new object();

		public PowerBehaviour Behaviour { get; private set; } = PowerBehaviour.RuleBased;

		public bool RequestMode(PowerMode mode)
		{
			Debug.Assert(Behaviour == PowerBehaviour.Auto, "RequestMode is for auto adjusting.");
			if (Pause) return false;

			lock (powerLock)
			{
				if (mode != CurrentMode && ForcedMode == false)
				{
					setMode(mode, verbose: false);
					return true;
				}
			}
			return false;
		}

		static bool ForcedMode = false;

		public bool ForceMode(PowerMode mode)
		{
			if (Behaviour == PowerBehaviour.Manual) return false;
			if (Pause) return false;

			lock (powerLock)
			{
				if (mode != CurrentMode)
				{
					setMode(mode);
					if (TaskMaster.DebugPower)
						Log.Debug("<Power Mode> Forced to: {PowerMode}", CurrentMode);
					ForcedMode = true;
					return true;
				}
				return false;
			}
		}

		public static void setMode(PowerMode mode, bool verbose = true)
		{
			Guid plan = Guid.Empty;
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

			if (verbose)
				Log.Information("<Power Mode> Setting to: {Mode} ({Guid})", mode.ToString(), plan.ToString());
			lock (powerLockI)
				PowerSetActiveScheme((IntPtr)null, ref plan);
		}

		bool disposed; // = false;
		protected override void Dispose(bool disposing)
		{
			if (disposed) return;

			base.Dispose(disposing);

			if (disposing)
			{
				Log.Verbose("Disposing power manager...");

				if (CPUTimer != null)
				{
					CPUTimer.Dispose();
					CPUTimer = null;
				}

				if (CPUCounter != null)
				{
					CPUCounter.Close(); // unnecessary?
					CPUCounter.Dispose();
					CPUCounter = null;
				}

				if (RestoreOriginal)
					SavedMode = OriginalMode;

				if (SavedMode != PowerMode.Undefined)
					RestoreMode();
				else
					setMode(OriginalMode, true);
				Log.Information("<Power Mode> Restored.");
				Log.Information("<Power Mode> Auto-adjusted {Counter} time(s).", AutoAdjustCounter);
			}

			disposed = true;
		}

		// UserPowerKey is reserved for future functionality and must always be null
		[DllImport("powrprof.dll", EntryPoint = "PowerSetActiveScheme")]
		static extern uint PowerSetActiveScheme(IntPtr UserPowerKey, ref Guid PowerPlanGuid);

		[DllImport("powrprof.dll", EntryPoint = "PowerGetActiveScheme")]
		static extern uint PowerGetActiveScheme(IntPtr UserPowerKey, out IntPtr PowerPlanGuid);

		const int DEVICE_NOTIFY_WINDOW_HANDLE = 0x00000000;
		[DllImport("user32.dll", SetLastError = true, EntryPoint = "RegisterPowerSettingNotification", CallingConvention = CallingConvention.StdCall)]
		static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid PowerSettingGuid, Int32 Flags);

		[StructLayout(LayoutKind.Sequential, Pack = 4)]
		internal struct POWERBROADCAST_SETTING
		{
			public Guid PowerSetting;
			public uint DataLength;
			//public byte Data;
		}

		const int WM_SYSCOMMAND = 0x0112;
		const int WM_POWERBROADCAST = 0x218;
		const int SC_MONITORPOWER = 0xF170;
		const int PBT_POWERSETTINGCHANGE = 0x8013;
		const int HWND_BROADCAST = 0xFFFF;

		[Flags]
		enum SendMessageTimeoutFlags : uint
		{
			SMTO_NORMAL = 0x0,
			SMTO_BLOCK = 0x1,
			SMTO_ABORTIFHUNG = 0x2,
			SMTO_NOTIMEOUTIFNOTHUNG = 0x8,
			SMTO_ERRORONEXIT = 0x2
		}
	}
}
