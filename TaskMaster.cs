//
// Taskmaster.cs
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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Taskmaster
{
	public static class Taskmaster
	{
		public static string GitURL => "https://github.com/mkahvi/taskmaster";
		public static string ItchURL => "https://mkah.itch.io/taskmaster";

		//public static SharpConfig.Configuration cfg;
		public static string datapath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MKAh", "Taskmaster");
		public static string logpath = Path.Combine(datapath, "Logs");

		public static ConfigManager Config = null;

		public static MicManager micmonitor = null;
		public static MainWindow mainwindow = null;
		public static ProcessManager processmanager = null;
		public static TrayAccess trayaccess = null;
		public static NetManager netmonitor = null;
		public static StorageManager storagemanager = null;
		public static PowerManager powermanager = null;
		public static ActiveAppManager activeappmonitor = null;
		public static HealthMonitor healthmonitor = null;
		public static SelfMaintenance selfmaintenance = null;
		public static AudioManager audiomanager = null;
		public static CPUMonitor cpumonitor = null;
		public static HardwareMonitor hardware = null;

		public static Stack<IDisposable> DisposalChute = new Stack<IDisposable>();

		public static OS.HiddenWindow hiddenwindow; // depends on chute

		static Runstate State = Runstate.Normal;
		static bool RestartElevated { get; set; } = false;
		static int RestartCounter { get; set; } = 0;
		static int AdminCounter { get; set; } = 0;

		public static void RestartRequest(object _, EventArgs _ea)
		{
			UnifiedExit(restart: true);
		}

		public static void ConfirmExit(bool restart = false, bool admin = false)
		{
			var rv = SimpleMessageBox.ResultType.OK;
			if (Taskmaster.ExitConfirmation)
			{
				if (ExitConfirmation)
				{
					rv = SimpleMessageBox.ShowModal(
						(restart ? "Restart" : "Exit") + Application.ProductName + " ???",
						"Are you sure you want to " + (restart ? "restart" : "exit") + " Taskmaster?",
						SimpleMessageBox.Buttons.AcceptCancel);
				}
				if (rv != SimpleMessageBox.ResultType.OK) return;
			}

			RestartElevated = admin;

			UnifiedExit(restart);
		}

		static bool CleanedUp = false;
		public static void ExitCleanup()
		{
			if (CleanedUp) return;

			try
			{
				if (!mainwindow?.IsDisposed ?? false) mainwindow.Enabled = false;
				if (!trayaccess?.IsDisposed ?? false) trayaccess.Enabled = false;

				while (DisposalChute.Count > 0)
					MKAh.Utility.LogAndDiscardException(() => DisposalChute.Pop().Dispose());

				pipe = null; // disposing the pipe seems to just cause problems
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex, crashsafe: true);
			}
			finally
			{
				CleanedUp = true;
			}
		}

		public static void UnifiedExit(bool restart = false)
		{
			State = restart ? Runstate.Restart : Runstate.Exit;

			//if (System.Windows.Forms.Application.MessageLoop) // fails if called from another thread
			Application.Exit();

			// nothing else should be needed.
		}

		public static void ShowMainWindow()
		{
			//await Task.Delay(0);

			try
			{
				// Log.Debug("Bringing to front");
				BuildMainWindow();
				mainwindow?.Reveal();
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		public static object mainwindow_creation_lock = new object();
		/// <summary>
		/// Constructs and hooks the main window
		/// </summary>
		public static void BuildMainWindow()
		{
			lock (mainwindow_creation_lock)
			{
				if (mainwindow != null) return;

				mainwindow = new MainWindow();
				mainwindow.FormClosed += (_, _ea) => mainwindow = null;

				try
				{
					if (storagemanager != null)
						mainwindow.Hook(storagemanager);

					if (processmanager != null)
						mainwindow.Hook(processmanager);

					if (micmonitor != null)
						mainwindow.Hook(micmonitor);

					if (netmonitor != null)
						mainwindow.Hook(netmonitor);

					if (activeappmonitor != null)
						mainwindow.Hook(activeappmonitor);

					if (powermanager != null)
						mainwindow.Hook(powermanager);

					if (cpumonitor != null)
						mainwindow.Hook(cpumonitor);

					if (hardware != null)
						mainwindow.Hook(hardware);

					if (healthmonitor != null)
						mainwindow.Hook(healthmonitor);
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
				}

				trayaccess.Hook(mainwindow);

				// .GotFocus and .LostFocus are apparently unreliable as per the API
				mainwindow.Activated += WindowActivatedEvent;
				mainwindow.Deactivate += WindowDeactivatedEvent;
			}
		}

		static bool MainWindowFocus = false;
		static bool TrayShown = false;

		static void WindowActivatedEvent(object _, EventArgs _ea)
		{
			MainWindowFocus = true;

			OptimizeResponsiviness();
		}

		static void WindowDeactivatedEvent(object _, EventArgs _ea)
		{
			MainWindowFocus = false;

			OptimizeResponsiviness();
		}

		static void TrayMenuShownEvent(object _, TrayShownEventArgs e)
		{
			TrayShown = e.Visible;

			OptimizeResponsiviness();
		}

		static void OptimizeResponsiviness()
		{
			var self = Process.GetCurrentProcess();

			if (MainWindowFocus || TrayShown)
			{
				if (SelfOptimizeBGIO)
				{
					MKAh.Utility.DiscardExceptions(() => ProcessUtility.UnsetBackground(self));
				}

				self.PriorityClass = ProcessPriorityClass.AboveNormal;
			}
			else
			{
				self.PriorityClass = SelfPriority;

				if (SelfOptimizeBGIO)
				{
					MKAh.Utility.DiscardExceptions(() => ProcessUtility.SafeSetBackground(self));
				}
			}

		}

		static void PreSetup()
		{
			// INITIAL CONFIGURATIONN
			var tcfg = Config.Load(Taskmaster.coreconfig);
			var sec = tcfg.Config.TryGet("Core")?.TryGet("Version")?.StringValue ?? null;
			if (sec == null || sec != ConfigVersion)
			{
				using (var initialconfig = new ComponentConfigurationWindow())
				{
					initialconfig.ShowDialog();
					if (initialconfig.DialogResult != DialogResult.OK)
						throw new InitFailure("Component configuration cancelled");
				}
			}
		}

		static void SetupComponents()
		{
			Log.Information("<Core> Loading components...");

			ProcessUtility.InitializeCache();

			Task PowMan = Task.CompletedTask,
				CpuMon = Task.CompletedTask,
				ProcMon = Task.CompletedTask,
				FgMon = Task.CompletedTask,
				NetMon = Task.CompletedTask,
				StorMon = Task.CompletedTask,
				HpMon = Task.CompletedTask,
				HwMon = Task.CompletedTask,
				VolMan = Task.CompletedTask;

			// Parallel loading, cuts down startup time some.
			// This is really bad if something fails
			Task[] init =
			{
				PowerManagerEnabled ? (PowMan = Task.Run(() => powermanager = new PowerManager())) : Task.CompletedTask,
				PowerManagerEnabled ? (CpuMon = Task.Run(()=> cpumonitor = new CPUMonitor())) : Task.CompletedTask,
				ProcessMonitorEnabled ? (ProcMon = Task.Run(() => processmanager = new ProcessManager())) : Task.CompletedTask,
				(ActiveAppMonitorEnabled && ProcessMonitorEnabled) ? (FgMon = Task.Run(()=> activeappmonitor = new ActiveAppManager(eventhook:false))) : Task.CompletedTask,
				NetworkMonitorEnabled ? (NetMon = Task.Run(() => netmonitor = new NetManager())) : Task.CompletedTask,
				StorageMonitorEnabled ? (StorMon = Task.Run(() => storagemanager = new StorageManager())) : Task.CompletedTask,
				HealthMonitorEnabled ? (HpMon = Task.Run(() => healthmonitor = new HealthMonitor())) : Task.CompletedTask,
				HardwareMonitorEnabled ? (HwMon = Task.Run(() => hardware = new HardwareMonitor())) : Task.CompletedTask,
				AudioManagerEnabled ? ( VolMan = Task.Run(() => audiomanager = new AudioManager()) ) : Task.CompletedTask,
			};

			// MMDEV requires main thread
			try
			{
				if (MicrophoneMonitorEnabled) micmonitor = new MicManager();
			}
			catch (InitFailure)
			{
				micmonitor = null;
			}

			// WinForms makes the following components not load nicely if not done here.
			trayaccess = new TrayAccess();
			trayaccess.TrayMenuShown += TrayMenuShownEvent;

			if (PowerManagerEnabled)
			{
				Task.WhenAll(new Task[] { PowMan, CpuMon }).ContinueWith((_) => {
					trayaccess.Hook(powermanager);
					powermanager.onBatteryResume += RestartRequest; // HACK
					powermanager.Hook(cpumonitor);
				});
			}

			Task.WhenAll(ProcMon).ContinueWith(
				(x) => trayaccess?.Hook(processmanager));

			if (PowerManagerEnabled)
			{
				Task.WhenAll(CpuMon).ContinueWith(
					(x) => cpumonitor.Hook(processmanager));
			}

			if (HardwareMonitorEnabled)
				Task.WhenAll(HwMon).ContinueWith((x) => hardware.Start()); // this is slow

			if (NetworkMonitorEnabled)
			{
				Task.WhenAll(NetMon).ContinueWith((x) =>
				{
					netmonitor.SetupEventHooks();
					netmonitor.Tray = trayaccess;
				});
			}

			if (AudioManagerEnabled)
			{
				Task.WhenAll(new Task[] { ProcMon, VolMan }).ContinueWith(
					(x) => audiomanager.Hook(processmanager));
			}

			try
			{
				// WAIT for component initialization
				if (!Task.WaitAll(init, 5_000))
				{
					Log.Warning("<Core> Components still loading.");
					if (!Task.WaitAll(init, 115_000)) // total wait time of 120 seconds
						throw new InitFailure("Component initialization taking excessively long, aborting.");
				}
			}
			catch (AggregateException ex)
			{
				foreach (var iex in ex.InnerExceptions)
					Logging.Stacktrace(iex);

				System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
				throw; // because compiler is dumb and doesn't understand the above
			}

			// HOOKING
			// Probably should transition to weak events

			Log.Information("<Core> Components loaded; Hooking event handlers.");

			if (ActiveAppMonitorEnabled && ProcessMonitorEnabled)
			{
				processmanager.Hook(activeappmonitor);
				activeappmonitor.SetupEventHook();
			}

			if (PowerManagerEnabled)
			{
				powermanager.SetupEventHook();
				processmanager.Hook(powermanager);
			}

			if (GlobalHotkeys)
			{
				trayaccess.RegisterGlobalHotkeys();
			}

			// UI

			var secInit = new Task[] { PowMan, CpuMon, ProcMon, FgMon, NetMon, StorMon, HpMon, HwMon, VolMan };
			if (!Task.WaitAll(secInit, 5_000))
			{
				Log.Warning("<Core> Component secondary loading still in progress.");
				if (!Task.WaitAll(secInit, 25_000))
					throw new InitFailure("Component secondary initialization taking excessively long, aborting.");
			}

			secInit = null;
			init = null;

			if (ShowOnStart && State == Runstate.Normal)
			{
				BuildMainWindow();
				mainwindow?.Reveal();
			}

			// Self-optimization
			if (SelfOptimize)
			{
				OptimizeResponsiviness();

				var self = Process.GetCurrentProcess();
				System.Threading.Thread currentThread = System.Threading.Thread.CurrentThread;
				currentThread.Priority = self.PriorityClass.ToThreadPriority(); // is this useful?

				/*
				if (SelfAffinity < 0)
				{
					// mask self to the last core
					int selfCPUmask = 1;
					for (int i = 0; i < Environment.ProcessorCount - 1; i++)
						selfCPUmask = (selfCPUmask << 1);
					SelfAffinity = selfCPUmask;
				}
				*/

				int selfAffMask = SelfAffinity.Replace(0, ProcessManager.AllCPUsMask);
				Log.Information($"<Core> Self-optimizing, priority: {SelfPriority.ToString()}, affinity: {HumanInterface.BitMask(selfAffMask, ProcessManager.CPUCount)}");

				self.ProcessorAffinity = new IntPtr(selfAffMask); // this should never throw an exception
				self.PriorityClass = SelfPriority;

				selfmaintenance = new SelfMaintenance();
			}

			if (Taskmaster.Trace)
				Log.Verbose("Displaying Tray Icon");
			trayaccess?.RefreshVisibility();

			Log.Information($"<Core> Component loading finished. {DisposalChute.Count.ToString()} initialized.");
		}

		public static bool ShowProcessAdjusts { get; set; } = true;
		public static bool ShowForegroundTransitions { get; set; } = false;
		public static bool ShowSessionActions { get; set; } = true;
		public static bool ShowNetworkErrors { get; set; } = true;

		public static bool DebugProcesses { get; set; } = false;

		public static bool DebugAdjustDelay { get; set; } = false;

		public static bool DebugPaths { get; set; } = false;
		public static bool DebugFullScan { get; set; } = false;

		public static bool DebugAudio { get; set; } = false;

		public static bool DebugForeground { get; set; } = false;

		public static bool DebugPower { get; set; } = false;
		public static bool DebugAutoPower { get; set; } = false;
		public static bool DebugPowerRules { get; set; } = false;
		public static bool DebugMonitor { get; set; } = false;

		public static bool DebugSession { get; set; } = false;
		public static bool DebugResize { get; set; } = false;

		public static bool DebugWMI { get; set; } = false;

		public static bool DebugMemory { get; set; } = false;
		public static bool DebugPaging { get; set; } = false;
		public static bool DebugStorage { get; set; } = false;

		public static bool DebugHealth { get; set; } = false;

		public static bool DebugNet { get; set; } = false;
		public static bool DebugMic { get; set; } = false;

		public static bool Trace { get; set; } = false;
		public static bool UniqueCrashLogs { get; set; } = false;
		public static bool ShowInaction { get; set; } = false;
		public static bool ShowAgency { get; set; } = false;

		public static bool ProcessMonitorEnabled { get; private set; } = true;
		public static bool MicrophoneMonitorEnabled { get; private set; } = false;
		// public static bool MediaMonitorEnabled { get; private set; } = true;
		public static bool NetworkMonitorEnabled { get; private set; } = true;
		public static bool PagingEnabled { get; private set; } = true;
		public static bool ActiveAppMonitorEnabled { get; private set; } = true;
		public static bool PowerManagerEnabled { get; private set; } = true;
		public static bool MaintenanceMonitorEnabled { get; private set; } = true;
		public static bool StorageMonitorEnabled { get; private set; } = true;
		public static bool HealthMonitorEnabled { get; private set; } = true;
		public static bool AudioManagerEnabled { get; private set; } = true;
		public static bool HardwareMonitorEnabled { get; private set; } = false;

		// EXPERIMENTAL FEATURES
		public static bool TempMonitorEnabled { get; private set; } = false;
		public static bool LastModifiedList { get; private set; } = false;
		public static bool WindowResizeEnabled { get; private set; } = false;
		public static TimeSpan? RecordAnalysis { get; set; } = null;
		public static bool IOPriorityEnabled { get; private set; } = false;

		// DEBUG INFO
		public static bool DebugCache { get; private set; } = false;

		public static bool ShowOnStart { get; private set; } = true;

		public static bool SelfOptimize { get; private set; } = true;
		public static ProcessPriorityClass SelfPriority { get; private set; } = ProcessPriorityClass.BelowNormal;
		public static bool SelfOptimizeBGIO { get; private set; } = false;
		public static int SelfAffinity { get; private set; } = 0;

		// public static bool LowMemory { get; private set; } = true; // low memory mode; figure out way to auto-enable this when system is low on memory

		public static int TempRescanDelay { get; set; } = 60 * 60_000; // 60 minutes
		public static int TempRescanThreshold { get; set; } = 1000;

		public static int PathCacheLimit { get; set; } = 200;
		public static TimeSpan PathCacheMaxAge { get; set; } = new TimeSpan(30, 0, 0);

		public static string ConfigVersion = "alpha.3";

		public static bool ExitConfirmation { get; set; } = true;
		public static bool AutoOpenMenus { get; set; } = true;
		public static bool ShowInTaskbar { get; set; } = false;
		public static int AffinityStyle { get; set; } = 0;
		public static bool GlobalHotkeys { get; set; } = false;

		public static string coreconfig = "Core.ini";
		static void LoadCoreConfig()
		{
			Log.Information("<Core> Loading configuration...");

			var corecfg = Config.Load(coreconfig);
			var cfg = corecfg.Config;

			if (cfg.TryGet("Core")?.TryGet("Hello")?.RawValue != "Hi")
			{
				cfg["Core"]["Hello"].SetValue("Hi");
				cfg["Core"]["Hello"].PreComment = "Heya";
				corecfg.MarkDirty();
			}

			var compsec = cfg["Components"];
			var optsec = cfg["Options"];
			var perfsec = cfg["Performance"];

			bool modified = false, dirtyconfig = false;
			cfg["Core"].GetSetDefault("License", "Refused", out modified).StringValue = "Accepted";
			dirtyconfig |= modified;

			var oldsettings = optsec?.SettingCount ?? 0 + compsec?.SettingCount ?? 0 + perfsec?.SettingCount ?? 0;

			// [Components]
			ProcessMonitorEnabled = compsec.GetSetDefault(HumanReadable.System.Process.Section, true, out modified).BoolValue;
			compsec[HumanReadable.System.Process.Section].Comment = "Monitor starting processes based on their name. Configure in Apps.ini";
			dirtyconfig |= modified;

			compsec.Remove("Process paths"); // DEPRECATED

			AudioManagerEnabled = compsec.GetSetDefault(HumanReadable.Hardware.Audio.Section, true, out modified).BoolValue;
			compsec[HumanReadable.Hardware.Audio.Section].Comment = "Monitor audio sessions and set their volume as per user configuration.";
			dirtyconfig |= modified;
			MicrophoneMonitorEnabled = compsec.GetSetDefault("Microphone", false, out modified).BoolValue;
			compsec["Microphone"].Comment = "Monitor and force-keep microphone volume.";
			dirtyconfig |= modified;
			// MediaMonitorEnabled = compsec.GetSetDefault("Media", true, out modified).BoolValue;
			// compsec["Media"].Comment = "Unused";
			// dirtyconfig |= modified;
			ActiveAppMonitorEnabled = compsec.GetSetDefault(HumanReadable.System.Process.Foreground, true, out modified).BoolValue;
			compsec[HumanReadable.System.Process.Foreground].Comment = "Game/Foreground app monitoring and adjustment.";
			dirtyconfig |= modified;
			NetworkMonitorEnabled = compsec.GetSetDefault("Network", true, out modified).BoolValue;
			compsec["Network"].Comment = "Monitor network uptime and current IP addresses.";
			dirtyconfig |= modified;
			PowerManagerEnabled = compsec.GetSetDefault(HumanReadable.Hardware.Power.Section, true, out modified).BoolValue;
			compsec[HumanReadable.Hardware.Power.Section].Comment = "Enable power plan management.";
			dirtyconfig |= modified;
			PagingEnabled = compsec.GetSetDefault("Paging", true, out modified).BoolValue;
			compsec["Paging"].Comment = "Enable paging of apps as per their configuration.";
			dirtyconfig |= modified;
			StorageMonitorEnabled = compsec.GetSetDefault("Storage", false, out modified).BoolValue;
			compsec["Storage"].Comment = "Enable NVM storage monitoring functionality.";
			dirtyconfig |= modified;
			MaintenanceMonitorEnabled = compsec.GetSetDefault("Maintenance", false, out modified).BoolValue;
			compsec["Maintenance"].Comment = "Enable basic maintenance monitoring functionality.";
			dirtyconfig |= modified;

			HealthMonitorEnabled = compsec.GetSetDefault("Health", false, out modified).BoolValue;
			compsec["Health"].Comment = "General system health monitoring suite.";
			dirtyconfig |= modified;

			HardwareMonitorEnabled = compsec.GetSetDefault(HumanReadable.Hardware.Section, false, out modified).BoolValue;
			compsec[HumanReadable.Hardware.Section].Comment = "Temperature, fan, etc. monitoring via OpenHardwareMonitor.";
			dirtyconfig |= modified;

			var qol = cfg[HumanReadable.Generic.QualityOfLife];
			ExitConfirmation = qol.GetSetDefault("Exit confirmation", true, out modified).BoolValue;
			dirtyconfig |= modified;
			AutoOpenMenus = qol.GetSetDefault("Auto-open menus", true, out modified).BoolValue;
			dirtyconfig |= modified;
			ShowInTaskbar = qol.GetSetDefault("Show in taskbar", true, out modified).BoolValue;
			dirtyconfig |= modified;
			GlobalHotkeys = qol.GetSetDefault("Register global hotkeys", false, out modified).BoolValue;
			dirtyconfig |= modified;
			AffinityStyle = qol.GetSetDefault(HumanReadable.Hardware.CPU.Settings.AffinityStyle, 0, out modified).IntValue.Constrain(0, 1);
			dirtyconfig |= modified;

			var logsec = cfg["Logging"];
			var Verbosity = logsec.GetSetDefault("Verbosity", 0, out modified).IntValue;
			logsec["Verbosity"].Comment = "0 = Information, 1 = Debug, 2 = Verbose/Trace, 3 = Excessive; 2 and higher are available on debug builds only";
			switch (Verbosity)
			{
				default:
				case 0:
					MemoryLog.MemorySink.LevelSwitch.MinimumLevel = LogEventLevel.Information;
					break;
				case 1:
					MemoryLog.MemorySink.LevelSwitch.MinimumLevel = LogEventLevel.Debug;
					break;
#if DEBUG
				case 2:
					MemoryLog.MemorySink.LevelSwitch.MinimumLevel = LogEventLevel.Verbose;
					break;
				case 3:
					MemoryLog.MemorySink.LevelSwitch.MinimumLevel = LogEventLevel.Verbose;
					Trace = true;
					break;
#endif
			}
			dirtyconfig |= modified;

			UniqueCrashLogs = logsec.GetSetDefault("Unique crash logs", false, out modified).BoolValue;
			logsec["Unique crash logs"].Comment = "On crash instead of creating crash.log in Logs, create crash-YYYYMMDD-HHMMSS-FFF.log instead. These are not cleaned out automatically!";
			dirtyconfig |= modified;

			ShowInaction = logsec.GetSetDefault("Show inaction", false, out modified).BoolValue;
			logsec["Show inaction"].Comment = "Log lack of action taken on processes.";
			dirtyconfig |= modified;

			ShowAgency = logsec.GetSetDefault("Show agency", false, out modified).BoolValue;
			logsec["Show agency"].Comment = "Log changes in agency, such as processes being left to decide their own fate.";
			dirtyconfig |= modified;

			ShowProcessAdjusts = logsec.GetSetDefault("Show process adjusts", true, out modified).BoolValue;
			logsec["Show process adjusts"].Comment = "Show blurbs about adjusted processes.";
			dirtyconfig |= modified;

			ShowNetworkErrors = logsec.GetSetDefault("Show network errors", true, out modified).BoolValue;
			logsec["Show network errors"].Comment = "Show network errors on each sampling.";
			dirtyconfig |= modified;

			ShowSessionActions = logsec.GetSetDefault("Show session actions", true, out modified).BoolValue;
			logsec["Show session actions"].Comment = "Show blurbs about actions taken relating to sessions.";
			dirtyconfig |= modified;

			ShowOnStart = optsec.GetSetDefault("Show on start", true, out modified).BoolValue;
			dirtyconfig |= modified;

			// [Performance]
			SelfOptimize = perfsec.GetSetDefault("Self-optimize", true, out modified).BoolValue;
			dirtyconfig |= modified;
			SelfPriority = ProcessHelpers.IntToPriority(perfsec.GetSetDefault("Self-priority", 1, out modified).IntValue.Constrain(0, 2));
			perfsec["Self-priority"].Comment = "Process priority to set for TM itself. Restricted to 0 (Low) to 2 (Normal).";
			dirtyconfig |= modified;
			SelfAffinity = perfsec.GetSetDefault("Self-affinity", 0, out modified).IntValue.Constrain(0, ProcessManager.AllCPUsMask);
			perfsec["Self-affinity"].Comment = "Core mask as integer. 0 is for default OS control.";
			dirtyconfig |= modified;
			if (SelfAffinity > Convert.ToInt32(Math.Pow(2, Environment.ProcessorCount) - 1 + double.Epsilon)) SelfAffinity = 0;

			// DEPRECATED
			if (perfsec.Contains("Persistent watchlist statistics"))
			{
				perfsec.Remove("Persistent watchlist statistics");
				dirtyconfig |= modified;
			}

			SelfOptimizeBGIO = perfsec.GetSetDefault("Background I/O mode", false, out modified).BoolValue;
			perfsec["Background I/O mode"].Comment = "Sets own priority exceptionally low. Warning: This can make TM's UI and functionality quite unresponsive.";
			dirtyconfig |= modified;

			if (perfsec.Contains("WMI queries"))
			{
				perfsec.Remove("WMI queries");
				dirtyconfig |= modified;
			}

			//perfsec.GetSetDefault("Child processes", false, out modified); // unused here
			//perfsec["Child processes"].Comment = "Enables controlling process priority based on parent process if nothing else matches. This is slow and unreliable.";
			//dirtyconfig |= modified;
			TempRescanThreshold = perfsec.GetSetDefault("Temp rescan threshold", 1000, out modified).IntValue;
			perfsec["Temp rescan threshold"].Comment = "How many changes we wait to temp folder before expediting rescanning it.";
			dirtyconfig |= modified;
			TempRescanDelay = perfsec.GetSetDefault("Temp rescan delay", 60, out modified).IntValue * 60_000;
			perfsec["Temp rescan delay"].Comment = "How many minutes to wait before rescanning temp after crossing the threshold.";
			dirtyconfig |= modified;

			PathCacheLimit = perfsec.GetSetDefault("Path cache", 60, out modified).IntValue.Constrain(20, 200);
			perfsec["Path cache"].Comment = "Path searching is very heavy process; this configures how many processes to remember paths for.\nThe cache is allowed to occasionally overflow for half as much.";
			dirtyconfig |= modified;

			PathCacheMaxAge = new TimeSpan(0, perfsec.GetSetDefault("Path cache max age", 15, out modified).IntValue.Constrain(1, 1440), 0);
			perfsec["Path cache max age"].Comment = "Maximum age, in minutes, of cached objects. Min: 1 (1min), Max: 1440 (1day).\nThese will be removed even if the cache is appropriate size.";
			dirtyconfig |= modified;

			//
			var maintsec = cfg["Maintenance"];
			maintsec.Remove("Cleanup interval"); // DEPRECATRED

			var newsettings = optsec?.SettingCount ?? 0 + compsec?.SettingCount ?? 0 + perfsec?.SettingCount ?? 0;

			if (dirtyconfig || (oldsettings != newsettings)) // really unreliable, but meh
				corecfg.MarkDirty();

			MonitorCleanShutdown();

			Log.Information("<Core> Verbosity: " + MemoryLog.MemorySink.LevelSwitch.MinimumLevel.ToString());
			Log.Information("<Core> Self-optimize: " + (SelfOptimize ? HumanReadable.Generic.Enabled : HumanReadable.Generic.Disabled));

			// PROTECT USERS FROM TOO HIGH PERMISSIONS
			var isadmin = IsAdministrator();
			var adminwarning = ((cfg["Core"].TryGet("Hell")?.StringValue ?? null) != "No");
			if (isadmin && adminwarning)
			{
				var rv = SimpleMessageBox.ShowModal(
					"Taskmaster! – admin access!!??",
					"You're starting TM with admin rights, is this right?\n\nYou can cause bad system operation, such as complete system hang, if you configure or configured TM incorrectly.",
					SimpleMessageBox.Buttons.AcceptCancel);

				if (rv == SimpleMessageBox.ResultType.OK)
				{
					cfg["Core"]["Hell"].StringValue = "No";
					corecfg.MarkDirty();
				}
				else
				{
					Log.Warning("<Core> Admin rights detected, user rejected proceeding.");
					UnifiedExit();
					throw new RunstateException("Admin rights rejected", Runstate.QuickExit);
				}
			}
			// STOP IT

			// DEBUG
			var dbgsec = cfg["Debug"];
			DebugProcesses = dbgsec.TryGet("Processes")?.BoolValue ?? false;
			DebugPaths = dbgsec.TryGet("Paths")?.BoolValue ?? false;
			DebugFullScan = dbgsec.TryGet("Full scan")?.BoolValue ?? false;
			DebugAudio = dbgsec.TryGet(HumanReadable.Hardware.Audio.Section)?.BoolValue ?? false;

			DebugAdjustDelay = dbgsec.TryGet("Adjust Delay")?.BoolValue ?? false;

			DebugForeground = dbgsec.TryGet(HumanReadable.System.Process.Foreground)?.BoolValue ?? false;

			DebugPower = dbgsec.TryGet(HumanReadable.Hardware.Power.Section)?.BoolValue ?? false;
			DebugAutoPower = dbgsec.TryGet(HumanReadable.Hardware.Power.AutoAdjust)?.BoolValue ?? false;
			//DebugPowerRules = dbgsec.TryGet("Paths")?.BoolValue ?? false;
			DebugMonitor = dbgsec.TryGet(HumanReadable.Hardware.Monitor.Section)?.BoolValue ?? false;

			DebugSession = dbgsec.TryGet("Session")?.BoolValue ?? false;
			DebugResize = dbgsec.TryGet("Resize")?.BoolValue ?? false;

			DebugWMI = dbgsec.TryGet("WMI")?.BoolValue ?? false;

			DebugMemory = dbgsec.TryGet("Memory")?.BoolValue ?? false;
			DebugPaging = dbgsec.TryGet("Paging")?.BoolValue ?? true;
			DebugStorage = dbgsec.TryGet("Storage")?.BoolValue ?? false;

			DebugNet = dbgsec.TryGet("Network")?.BoolValue ?? false;
			DebugMic = dbgsec.TryGet("Microphone")?.BoolValue ?? false;

			var exsec = cfg["Experimental"];
			WindowResizeEnabled = exsec.TryGet("Window Resize")?.BoolValue ?? false;
			LastModifiedList = exsec.TryGet("Last Modified")?.BoolValue ?? false;
			TempMonitorEnabled = exsec.TryGet("Temp Monitor")?.BoolValue ?? false;
			int trecanalysis = exsec.TryGet("Record analysis")?.IntValue ?? 0;
			RecordAnalysis = trecanalysis > 0 ? (TimeSpan?)TimeSpan.FromSeconds(trecanalysis.Constrain(0, 180)) : null;
			IOPriorityEnabled = exsec.TryGet("IO Priority")?.BoolValue ?? false;

#if DEBUG
			Trace = dbgsec.TryGet("Trace")?.BoolValue ?? false;
#endif

			// END DEBUG

			Log.Information($"<Core> Privilege level: {(isadmin ? "Admin" : "User")}");

			Log.Information($"<Core> Path cache: {(PathCacheLimit == 0 ? HumanReadable.Generic.Disabled : PathCacheLimit.ToString())} items");

			Log.Information($"<Core> Paging: {(PagingEnabled ? HumanReadable.Generic.Enabled : HumanReadable.Generic.Disabled)}");

			return;
		}

		static int isAdmin = -1;
		public static bool IsAdministrator()
		{
			if (isAdmin != -1) return (isAdmin == 1);

			// https://stackoverflow.com/a/10905713
			var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
			var principal = new System.Security.Principal.WindowsPrincipal(identity);
			var rv = principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

			isAdmin = rv ? 1 : 0;

			return rv;
		}

		static DirectoryInfo TempRunningDir = null;

		static void MonitorCleanShutdown()
		{
			TempRunningDir = new System.IO.DirectoryInfo(System.IO.Path.Combine(datapath, ".running-TM0"));

			if (!TempRunningDir.Exists)
			{
				TempRunningDir.Create();
				TempRunningDir.Attributes = FileAttributes.Directory | FileAttributes.Hidden; // this doesn't appear to work
			}
			else
				Log.Warning("Unclean shutdown.");
		}

		static readonly Finalizer finalizer = new Finalizer();
		sealed class Finalizer
		{
			~Finalizer()
			{
				// Debug.WriteLine("Core static finalization");
			}
		}

		static void CleanShutdown()
		{
			TempRunningDir?.Delete();
			TempRunningDir = null;
		}

		/// <summary>
		/// Pre-allocates a file.
		/// Not recommended for files that are written with append mode due to them simply adding to the end of the size set by this.
		/// </summary>
		/// <param name="fullpath">Full path to the file.</param>
		/// <param name="allockb">Size in kB to allocate.</param>
		/// <param name="kib">kiB * 1024; kB * 1000</param>
		/// <param name="writeonebyte">Writes one byte at the new end.</param>
		static public void Prealloc(string fullpath, long allockb=64, bool kib=true, bool writeonebyte=false)
		{
			Debug.Assert(allockb >= 0);
			Debug.Assert(string.IsNullOrEmpty(fullpath));

			long boundary = kib ? 1024 : 1000;

			System.IO.FileStream fs = null;
			try
			{
				fs = System.IO.File.Open(fullpath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
				var oldsize = fs.Length;

				if (fs.Length < (boundary * allockb))
				{
					// TODO: Make sparse. Unfortunately requires P/Invoke.
					fs.SetLength(boundary * allockb); // 1024 was dumb for HDDs but makes sense for SSDs again.
					if (writeonebyte)
					{
						fs.Seek((boundary * allockb) - 1, SeekOrigin.Begin);
						byte[] nullarray = { 0 };
						fs.Write(nullarray, 0, 1);
					}

					Log.Debug($"<Core> Pre-allocated file: {fullpath} ({(oldsize / boundary).ToString()} kB -> {allockb.ToString()} kB)");
				}
			}
			catch (System.IO.FileNotFoundException)
			{
				Log.Error("<Core> Failed to open file: " + fullpath);
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				fs?.Close();
			}
		}

		const int FSCTL_SET_SPARSE = 0x000900c4;

		static public void SparsePrealloc(string fullpath, long allockb = 64, bool ssd = true)
		{
			int brv = 0;
			System.Threading.NativeOverlapped ol = new System.Threading.NativeOverlapped();

			FileStream fs = null;
			try
			{
				fs = File.Open(fullpath, FileMode.Open, FileAccess.Write);

				bool rv = NativeMethods.DeviceIoControl(
					fs.SafeFileHandle, FSCTL_SET_SPARSE,
					IntPtr.Zero, 0, IntPtr.Zero, 0, ref brv, ref ol);
				// if (result == false) return false;

				if (rv) fs.SetLength(allockb * 1024);
			}
			catch { } // ignore, no access probably
			finally
			{
				fs?.Dispose();
			}
		}

		public const string BootDelayArg = "--bootdelay";
		public const string AdminArg = "--admin";
		public const string RestartArg = "--restart";

		static void ParseArguments(string[] args)
		{
			var StartDelay = 0;
			var uptime = TimeSpan.Zero;

			for (int i = 0; i < args.Length; i++)
			{
				switch (args[i])
				{
					case BootDelayArg:
						if (args.Length > i+1 && !args[i+1].StartsWith("--"))
						{
							try
							{
								StartDelay = Convert.ToInt32(args[++i]).Constrain(1, 60*5);
							}
							catch (OutOfMemoryException) { throw; }
							catch
							{
								StartDelay = 30;
							}
						}

						try
						{
							using (var uptimecounter = new PerformanceCounter("System", "System Up Time"))
							{
								uptimecounter.NextValue();
								uptime = TimeSpan.FromSeconds(uptimecounter.NextValue());
								uptimecounter.Close();
							}
						}
						catch (OutOfMemoryException) { throw; }
						catch
						{
							uptime = TimeSpan.Zero;
						}

						break;
					case RestartArg:
						if (args.Length > i+1 && !args[i+1].StartsWith("--"))
						{
							try
							{
								RestartCounter = Convert.ToInt32(args[++i]);
							}
							catch { }
						}

						break;
					case AdminArg:
						if (args.Length > i+1 && !args[i+1].StartsWith("--"))
						{
							try
							{
								AdminCounter = Convert.ToInt32(args[++i]);
							}
							catch { }
						}

						if (AdminCounter <= 1)
						{
							if (!IsAdministrator())
							{
								Log.Information("Restarting with elevated privileges.");
								try
								{
									var info = Process.GetCurrentProcess().StartInfo;
									info.FileName = Process.GetCurrentProcess().ProcessName;
									info.Arguments = $"{AdminArg} {(++AdminCounter).ToString()}";
									info.Verb = "runas"; // elevate privileges
									Log.CloseAndFlush();
									var proc = Process.Start(info);
								}
								catch { } // without finally block might not execute
								finally
								{
									UnifiedExit(restart: true);
									throw new RunstateException("Quick exit to restart", Runstate.Restart);
								}
							}
						}
						else
						{
							SimpleMessageBox.ShowModal("Taskmaster launch error", "Failure to elevate privileges, resuming as normal.", SimpleMessageBox.Buttons.OK);
						}

						break;
					default:
						break;
				}
			}

			if (StartDelay > 0 && uptime.TotalSeconds < 300)
			{
				Debug.WriteLine($"Delaying proper startup for {uptime.TotalSeconds:N1} seconds.");

				var remainingdelay = StartDelay - uptime.TotalSeconds;
				if (remainingdelay > 5)
				{
					Log.Information($"Delaying start by {remainingdelay:N0} seconds");
					System.Threading.Thread.Sleep(TimeSpan.FromSeconds(remainingdelay));
				}
			}
		}

		static void LicenseBoiler()
		{
			var cfg = Config.Load(coreconfig);

			if (cfg.Config.TryGet("Core")?.TryGet("License")?.RawValue.Equals("Accepted") ?? false) return;

			using (var license = new LicenseDialog())
			{
				license.ShowDialog();
				if (license.DialogResult != DialogResult.Yes)
				{
					UnifiedExit();
					throw new RunstateException("License not accepted.", Runstate.QuickExit);
				}
			}
		}

		public static bool IsMainThread() => System.Threading.Thread.CurrentThread.IsThreadPoolThread == false && System.Threading.Thread.CurrentThread.ManagedThreadId == 1;

		// Useful for figuring out multi-threading related problems
		// From StarOverflow: https://stackoverflow.com/q/22579206
		[Conditional("DEBUG")]
		public static void ThreadIdentity(string message = "")
		{
			var thread = System.Threading.Thread.CurrentThread;
			string name = thread.IsThreadPoolThread ? "Thread pool" : (string.IsNullOrEmpty(thread.Name) ? $"#{thread.ManagedThreadId.ToString()}" : thread.Name);
			Debug.WriteLine($"Continuation on: {name} --- {message}");
		}

		static void PreallocLastLog()
		{
			DateTimeOffset lastDate = DateTimeOffset.MinValue;
			FileInfo lastFile = null;
			string lastPath = null;

			var files = System.IO.Directory.GetFiles(logpath, "*", System.IO.SearchOption.AllDirectories);
			foreach (var filename in files)
			{
				var path = System.IO.Path.Combine(logpath, filename);
				var fi = new System.IO.FileInfo(path);
				if (fi.LastWriteTime > lastDate)
				{
					lastDate = fi.LastWriteTime;
					lastFile = fi;
					lastPath = path;
				}
			}

			if (lastFile != null)
			{
				SparsePrealloc(lastPath);
			}
		}

		public static bool Portable { get; internal set; } = false;
		static void TryPortableMode()
		{
			string portpath = Path.Combine(Path.GetDirectoryName(Environment.GetCommandLineArgs()[0]), "Config");
			if (Directory.Exists(portpath))
			{
				datapath = portpath;
				Portable = true;
			}
			else if (!File.Exists(Path.Combine(datapath, "Core.ini")))
			{
				if (SimpleMessageBox.ShowModal("Taskmaster setup", "Setup portable installation?", SimpleMessageBox.Buttons.AcceptCancel)
					== SimpleMessageBox.ResultType.OK)
				{
					datapath = portpath;
					Portable = true;
					System.IO.Directory.CreateDirectory(datapath); // this might fail, but we don't really care.
				}
			}
		}

		const string PipeName = @"\\.\MKAh\Taskmaster\Pipe";
		const string PipeRestart = "TM...RESTART";
		const string PipeTerm = "TM...TERMINATE";
		static System.IO.Pipes.NamedPipeServerStream pipe = null;

		static void PipeCleaner(IAsyncResult result)
		{
			if (pipe == null) return;
			if (result.IsCompleted) return; // for some reason empty completion appears on exit

			try
			{
				var lp = pipe;
				//pipe = null;
				lp.EndWaitForConnection(result);

				if (!result.IsCompleted) return;
				if (!pipe.IsConnected) return;
				if (!pipe.IsMessageComplete) return;

				byte[] buffer = new byte[16];

				lp.ReadAsync(buffer, 0, 16).ContinueWith(delegate
				{
					try
					{
						var str = System.Text.UTF8Encoding.UTF8.GetString(buffer, 0, 16);
						if (str.StartsWith(PipeRestart))
						{
							Log.Warning("<IPC> Restart request received.");
							UnifiedExit(restart: true);
						}
						else if (str.StartsWith(PipeTerm))
						{
							Log.Warning("<IPC> Termination request received.");
							UnifiedExit(restart: false);
						}

						lp.Disconnect();
						lp.BeginWaitForConnection(PipeCleaner, null);
					}
					catch (Exception ex)
					{
						Logging.Stacktrace(ex);
					}
					finally
					{
						//PipeDream(); // restart listening pipe
					}
				});
			}
			catch (ObjectDisposedException)
			{
				return;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		static System.IO.Pipes.NamedPipeServerStream PipeDream()
		{
			try
			{
				var ps = new System.IO.Pipes.PipeSecurity();
				ps.AddAccessRule(new System.IO.Pipes.PipeAccessRule("Users", System.IO.Pipes.PipeAccessRights.Write, System.Security.AccessControl.AccessControlType.Allow));
				ps.AddAccessRule(new System.IO.Pipes.PipeAccessRule(System.Security.Principal.WindowsIdentity.GetCurrent().Name, System.IO.Pipes.PipeAccessRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));
				ps.AddAccessRule(new System.IO.Pipes.PipeAccessRule("SYSTEM", System.IO.Pipes.PipeAccessRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));

				pipe = new System.IO.Pipes.NamedPipeServerStream(PipeName, System.IO.Pipes.PipeDirection.In, 1, System.IO.Pipes.PipeTransmissionMode.Message, System.IO.Pipes.PipeOptions.Asynchronous, 16, 8);

				//DisposalChute.Push(pipe);

				pipe.BeginWaitForConnection(PipeCleaner, null);

				return pipe;
			}
			catch (IOException) // no pipes available?
			{

			}

			return null;
		}

		static void PipeExplorer(bool restart=true)
		{
			Debug.WriteLine("Attempting to communicate with running instance of TM.");

			try
			{
				using (var pe = new System.IO.Pipes.NamedPipeClientStream(".", PipeName, System.IO.Pipes.PipeAccessRights.Write, System.IO.Pipes.PipeOptions.WriteThrough, System.Security.Principal.TokenImpersonationLevel.Impersonation, HandleInheritability.None))
				{
					pe.Connect(5_000);
					if (pe.IsConnected && pe.CanWrite)
					{
						byte[] buffer = System.Text.UTF8Encoding.UTF8.GetBytes(restart ? PipeRestart : PipeTerm);
						//pe.WriteTimeout = 5_000;
						pe.Write(buffer, 0, buffer.Length);
						pe.WaitForPipeDrain();
					}
					//System.Threading.Thread.Sleep(100); // HACK: async pipes don't like things happening too fast.
					pe.Close();
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex, crashsafe: true);
			}
		}

		const string SingletonID = "088f7210-51b2-4e06-9bd4-93c27a973874.taskmaster";

		// entry point to the application
		[STAThread] // supposedly needed to avoid shit happening with the WinForms GUI and other GUI toolkits
		static public int Main(string[] args)
		{
			//Debug.Listeners.Add(new TextWriterTraceListener(System.Console.Out));

			NativeMethods.SetErrorMode(NativeMethods.SetErrorMode(NativeMethods.ErrorModes.SEM_SYSTEMDEFAULT) | NativeMethods.ErrorModes.SEM_NOGPFAULTERRORBOX | NativeMethods.ErrorModes.SEM_FAILCRITICALERRORS);

			System.Threading.Mutex singleton = null;

			System.Windows.Forms.Application.SetUnhandledExceptionMode(UnhandledExceptionMode.Automatic);
			System.Windows.Forms.Application.ThreadException += UnhandledUIException;
			System.Windows.Forms.Application.EnableVisualStyles(); // required by shortcuts and high dpi-awareness
			System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false); // required by high dpi-awareness

			AppDomain.CurrentDomain.UnhandledException += UnhandledException;

			//hiddenwindow = new OS.HiddenWindow();

			TryPortableMode();
			logpath = Path.Combine(datapath, "Logs");

			// Singleton
			singleton = new System.Threading.Mutex(true, SingletonID, out bool mutexgained);
			if (!mutexgained)
			{
				SimpleMessageBox.ResultType rv = SimpleMessageBox.ResultType.Cancel;

				// already running, signal original process
				using (var msg = new SimpleMessageBox("Taskmaster!",
					"Already operational.\n\nRetry to try to recover [restart] running instance.\nEnd to kill running instance and exit this.\nCancel to exit this.",
					SimpleMessageBox.Buttons.RetryEndCancel))
				{
					msg.ShowDialog();
					rv = msg.Result;
				}

				switch (rv)
				{
					case SimpleMessageBox.ResultType.Retry:
						PipeExplorer(restart:true);
						break;
					case SimpleMessageBox.ResultType.End:
						PipeExplorer(restart: false);
						break;
					case SimpleMessageBox.ResultType.Cancel:
						break;
				}

				return -1;
			}

			pipe = PipeDream(); // IPC with other instances of TM

			// Multi-core JIT
			// https://docs.microsoft.com/en-us/dotnet/api/system.runtime.profileoptimization
			{
				var cachepath = System.IO.Path.Combine(datapath, "Cache");
				if (!System.IO.Directory.Exists(cachepath)) System.IO.Directory.CreateDirectory(cachepath);
				System.Runtime.ProfileOptimization.SetProfileRoot(cachepath);
				System.Runtime.ProfileOptimization.StartProfile("jit.profile");
			}

			try
			{
					Config = new ConfigManager(datapath);

					LicenseBoiler();

					// INIT LOGGER
					var logswitch = new LoggingLevelSwitch(LogEventLevel.Information);

					var logpathtemplate = System.IO.Path.Combine(logpath, "taskmaster-{Date}.log");
					Serilog.Log.Logger = new Serilog.LoggerConfiguration()
#if DEBUG
						.MinimumLevel.Verbose()
						.WriteTo.Console(levelSwitch: new LoggingLevelSwitch(LogEventLevel.Verbose))
#else
						.MinimumLevel.Debug()
#endif
						.WriteTo.RollingFile(logpathtemplate, outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
							levelSwitch: new LoggingLevelSwitch(Serilog.Events.LogEventLevel.Debug), retainedFileCountLimit: 3)
						.WriteTo.MemorySink(levelSwitch: logswitch)
						.CreateLogger();

					// COMMAND-LINE ARGUMENTS
					ParseArguments(args);

					// STARTUP

					var builddate = BuildDate();

					var now = DateTime.Now;
					var age = (now - builddate).TotalDays;

					var sbs = new StringBuilder();
					sbs.Append("Taskmaster! (#").Append(Process.GetCurrentProcess().Id).Append(")")
						.Append(IsAdministrator() ? " [ADMIN]" : "").Append(Portable ? " [PORTABLE]" : "")
						.Append(" – Version: ").Append(System.Reflection.Assembly.GetExecutingAssembly().GetName().Version)
						.Append(" – Built: ").Append(builddate.ToString("yyyy/MM/dd HH:mm")).Append($" [{age:N0} days old]");
					Log.Information(sbs.ToString());

				//PreallocLastLog();

				PreSetup();
				LoadCoreConfig();
				SetupComponents();

				Config.Flush(); // early save of configs

				if (RestartCounter > 0) Log.Information("<Core> Restarted " + RestartCounter.ToString() + " time(s)");
				Log.Information("<Core> Initialization complete...");

				if (Debug.Listeners.Count > 0)
				{
					Debug.WriteLine("Embedded Resources");
					foreach (var name in System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceNames())
						Debug.WriteLine(" - " + name);
				}

				if (State == Runstate.Normal)
				{
					System.Windows.Forms.Application.Run(); // WinForms

					// System.Windows.Application.Current.Run(); // WPF
				}

				if (SelfOptimize) // return decent processing speed to quickly exit
				{
					var self = Process.GetCurrentProcess();
					self.PriorityClass = ProcessPriorityClass.AboveNormal;
					if (Taskmaster.SelfOptimizeBGIO)
					{
						MKAh.Utility.DiscardExceptions(() => ProcessUtility.SafeSetBackground(self));
					}
				}

				Log.Information("Exiting...");

				ExitCleanup();

				PrintStats();

				CleanShutdown();

				Utility.Dispose(ref Config);

				Log.Information($"Taskmaster! (#{Process.GetCurrentProcess().Id.ToString()}) END! [Clean]");

				if (State == Runstate.Restart) // happens only on power resume (waking from hibernation) or when manually set
				{
					singleton?.Dispose();
					Restart();
					return 0;
				}
			}
			catch (InitFailure ex)
			{
				Logging.Stacktrace(ex, crashsafe: true);
				return -1; // should trigger finally block
			}
			catch (RunstateException ex)
			{
				switch (ex.State)
				{
					case Runstate.CriticalFailure:
						Logging.Stacktrace(ex.InnerException ?? ex, crashsafe:true);
						System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException ?? ex).Throw();
						throw;
					case Runstate.Normal:
					case Runstate.Exit:
					case Runstate.QuickExit:
					case Runstate.Restart:
						break;
				}

				return -1; // should trigger finally block
			}
			catch (OutOfMemoryException ex)
			{
				Logging.Stacktrace(ex, crashsafe: true);

				return 1; // should trigger finally block
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex, crashsafe: true);

				return 1; // should trigger finally block
			}
			finally
			{
				ExitCleanup();

				Config?.Dispose();
				singleton?.Dispose();

				Log.CloseAndFlush();
			}

			return 0;
		}

		static void PrintStats()
		{
			Log.Information($"<Stat> WMI polling: {Statistics.WMIPollTime:N2}s [{Statistics.WMIPolling.ToString()}]");
			Log.Information($"<Stat> Self-maintenance: {Statistics.MaintenanceTime:N2}s [{Statistics.MaintenanceCount.ToString()}]");
			Log.Information($"<Stat> Path cache: {Statistics.PathCacheHits.ToString()} hits, {Statistics.PathCacheMisses.ToString()} misses");
			var sbs = new StringBuilder();
			sbs.Append("<Stat> Path finding: ").Append(Statistics.PathFindAttempts).Append(" total attempts; ")
				.Append(Statistics.PathFindViaModule).Append(" via module info, ")
				.Append(Statistics.PathFindViaC).Append(" via C call, ")
				.Append(Statistics.PathFindViaWMI).Append(" via WMI, ")
				.Append(Statistics.PathNotFound).Append(" not found");
			Log.Information(sbs.ToString());
			Log.Information($"<Stat> Processes modified: {Statistics.TouchCount.ToString()}; Ignored for remodification: {Statistics.TouchIgnore.ToString()}");
		}

		static void Restart()
		{

			Log.Information("Restarting...");
			try
			{
				if (!System.IO.File.Exists(Application.ExecutablePath))
					Log.Fatal("Executable missing: " + Application.ExecutablePath); // this should be "impossible"

				var info = Process.GetCurrentProcess().StartInfo;
				//info.FileName = Process.GetCurrentProcess().ProcessName;
				info.WorkingDirectory = System.IO.Path.GetDirectoryName(Application.ExecutablePath);
				info.FileName = System.IO.Path.GetFileName(Application.ExecutablePath);

				var nargs = new List<string> { RestartArg, (++RestartCounter).ToString() };  // has no real effect
				if (RestartElevated)
				{
					nargs.Add(AdminArg);
					nargs.Add((++AdminCounter).ToString());
					info.Verb = "runas"; // elevate privileges
				}

				info.Arguments = string.Join(" ", nargs);

				Log.CloseAndFlush();

				Process.Start(info);
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex, crashsafe: true);
			}
		}

		static void UnhandledException(object sender, UnhandledExceptionEventArgs ea)
		{
			var ex = (Exception)ea.ExceptionObject;
			Log.Fatal(ex, "Unhandled exception!!! Writing to crash log.");
			Logging.Stacktrace(ex, crashsafe:true);

			if (ea.IsTerminating)
			{
				ExitCleanup();
				Config?.Dispose();
				MKAh.Utility.LogAndDiscardException(() => trayaccess?.Dispose()); // possibly bad, but...
				Log.CloseAndFlush();
			}
		}

		public static DateTime BuildDate() => DateTime.ParseExact(Properties.Resources.BuildDate.Trim(), "yyyy/MM/dd HH:mm:ss K", null, System.Globalization.DateTimeStyles.None);

		/// <summary>
		/// Process unhandled WinForms exceptions.
		/// </summary>
		static void UnhandledUIException(object _, System.Threading.ThreadExceptionEventArgs ea) => Logging.Stacktrace(ea.Exception);
	}
}