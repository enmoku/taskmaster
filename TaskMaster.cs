//
// Taskmaster.cs
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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Taskmaster
{
	public static class Taskmaster
	{
		public static string GitURL { get; } = "https://github.com/mkahvi/taskmaster";
		public static string ItchURL { get; } = "https://mkah.itch.io/taskmaster";

		//public static SharpConfig.Configuration cfg;
		public static string datapath = System.IO.Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MKAh", "Taskmaster");

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

		public static Stack<IDisposable> DisposalChute = new Stack<IDisposable>();

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
			var rv = DialogResult.Yes;
			if (Taskmaster.ExitConfirmation)
			{
				if (ExitConfirmation)
					rv = MessageBox.Show("Are you sure you want to " + (restart ? "restart" : "exit") + " Taskmaster?",
										 (restart ? "Restart" : "Exit") + Application.ProductName + " ???",
										 MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1, MessageBoxOptions.DefaultDesktopOnly, false);
				if (rv != DialogResult.Yes) return;
			}

			RestartElevated = admin;

			UnifiedExit(restart);
		}

		public static void ExitCleanup()
		{
			if (!mainwindow?.IsDisposed ?? false) mainwindow.Enabled = false;
			if (!trayaccess?.IsDisposed ?? false) trayaccess.Enabled = false;

			while (DisposalChute.Count > 0)
			{
				try
				{
					DisposalChute.Pop().Dispose();
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
				}
			}
		}

		public static void UnifiedExit(bool restart = false)
		{
			State = restart ? Runstate.Restart : Runstate.Exit;

			if (System.Windows.Forms.Application.MessageLoop)
				Application.Exit();
			// nothing else should be needed.
		}

		/// <summary>
		/// Call any supporting functions to re-evaluate current situation.
		/// </summary>
		public static void Evaluate()
		{
			try
			{
				processmanager?.ScanRequest(null);
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
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
				mainwindow.FormClosed += (s, e) => mainwindow = null;

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
				self.PriorityClass = ProcessPriorityClass.AboveNormal;

				if (SelfOptimizeBGIO)
				{
					try { ProcessController.SetIOPriority(self, NativeMethods.PriorityTypes.PROCESS_MODE_BACKGROUND_END); }
					catch { }
				}
			}
			else
			{
				self.PriorityClass = SelfPriority;

				if (SelfOptimizeBGIO)
				{
					try { ProcessController.SetIOPriority(self, NativeMethods.PriorityTypes.PROCESS_MODE_BACKGROUND_BEGIN); }
					catch { }
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
				try
				{
					using (var initialconfig = new ComponentConfigurationWindow())
						initialconfig.ShowDialog();
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
					throw;
				}

				if (ComponentConfigurationDone == false)
				{
					Log.CloseAndFlush();
					throw new InitFailure("Component configuration cancelled");
				}
			}

			tcfg = null;
			sec = null;
		}

		static void SetupComponents()
		{
			Log.Information("<Core> Loading components...");

			// Parallel loading, cuts down startup time some.
			// This is really bad if something fails
			Task[] init =
			{
				PowerManagerEnabled ? (Task.Run(() => powermanager = new PowerManager())) : Task.CompletedTask,
				PowerManagerEnabled ? (Task.Run(()=> cpumonitor = new CPUMonitor())) : Task.CompletedTask,
				ProcessMonitorEnabled ? (Task.Run(() => processmanager = new ProcessManager())) : Task.CompletedTask,
				(ActiveAppMonitorEnabled && ProcessMonitorEnabled) ? (Task.Run(()=> activeappmonitor = new ActiveAppManager(eventhook:false))) : Task.CompletedTask,
				NetworkMonitorEnabled ? (Task.Run(() => netmonitor = new NetManager())) : Task.CompletedTask,
				MaintenanceMonitorEnabled ? (Task.Run(() => storagemanager = new StorageManager())) : Task.CompletedTask,
				HealthMonitorEnabled ? (Task.Run(() => healthmonitor = new HealthMonitor())) : Task.CompletedTask,
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

			if (AudioManagerEnabled) audiomanager = new AudioManager(); // EXPERIMENTAL

			// WinForms makes the following components not load nicely if not done here.
			trayaccess = new TrayAccess();
			trayaccess.TrayMenuShown += TrayMenuShownEvent;

			Log.Information("<Core> Waiting for component loading.");

			try
			{
				Task.WaitAll(init);
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

			if (PowerManagerEnabled)
			{
				trayaccess.Hook(powermanager);
				powermanager.onBatteryResume += RestartRequest; // HACK
				powermanager.Hook(cpumonitor);
			}

			if (NetworkMonitorEnabled)
			{
				netmonitor.SetupEventHooks();
				netmonitor.Tray = trayaccess;
			}

			if (processmanager != null)
				trayaccess?.Hook(processmanager);

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

				if (SelfAffinity < 0)
				{
					// mask self to the last core
					var selfCPUmask = 1;
					for (int i = 0; i < Environment.ProcessorCount - 1; i++)
						selfCPUmask = (selfCPUmask << 1);
					SelfAffinity = selfCPUmask;
					// Console.WriteLine("Setting own CPU mask to: {0} ({1})", Convert.ToString(selfCPUmask, 2), selfCPUmask);
				}

				self.ProcessorAffinity = new IntPtr(SelfAffinity); // this should never throw an exception

				selfmaintenance = new SelfMaintenance();
			}

			if (Taskmaster.Trace)
				Log.Verbose("Displaying Tray Icon");
			trayaccess?.RefreshVisibility();

			Log.Information("<Core> Component loading finished. " + DisposalChute.Count + " initialized.");
		}

		public static bool ShowProcessAdjusts { get; set; } = true;
		public static bool ShowForegroundTransitions { get; set; } = false;
		public static bool ShowSessionActions { get; set; } = true;
		public static bool ShowNetworkErrors { get; set; } = true;

		public static bool DebugProcesses { get; set; } = false;

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
		public static bool ShowInaction { get; set; } = false;

		public static bool ProcessMonitorEnabled { get; private set; } = true;
		public static bool PathMonitorEnabled { get; private set; } = true;
		public static bool MicrophoneMonitorEnabled { get; private set; } = false;
		// public static bool MediaMonitorEnabled { get; private set; } = true;
		public static bool NetworkMonitorEnabled { get; private set; } = true;
		public static bool PagingEnabled { get; private set; } = true;
		public static bool ActiveAppMonitorEnabled { get; private set; } = true;
		public static bool PowerManagerEnabled { get; private set; } = true;
		public static bool MaintenanceMonitorEnabled { get; private set; } = true;
		public static bool HealthMonitorEnabled { get; private set; } = true;
		public static bool AudioManagerEnabled { get; private set; } = true;

		// EXPERIMENTAL FEATURES
		public static bool TempMonitorEnabled { get; private set; } = false;
		public static bool LastModifiedList { get; private set; } = false;
		public static bool WindowResizeEnabled { get; private set; } = false;
		public static bool IgnoreRecentlyModified { get; private set; } = false;

		// DEBUG INFO
		public static bool DebugCache { get; private set; } = false;

		public static bool ShowOnStart { get; private set; } = true;

		public static bool SelfOptimize { get; private set; } = true;
		public static ProcessPriorityClass SelfPriority { get; private set; } = ProcessPriorityClass.BelowNormal;
		public static bool SelfOptimizeBGIO { get; private set; } = false;
		public static int SelfAffinity { get; private set; } = -1;

		public static bool PersistentWatchlistStats { get; private set; }  = false;

		// public static bool LowMemory { get; private set; } = true; // low memory mode; figure out way to auto-enable this when system is low on memory

		public static int TempRescanDelay { get; set; } = 60 * 60_000; // 60 minutes
		public static int TempRescanThreshold { get; set; } = 1000;

		public static int PathCacheLimit { get; set; } = 200;
		public static int PathCacheMaxAge { get; set; } = 1800;

		public static int CleanupInterval { get; set; } = 15;

		/// <summary>
		/// Whether to use WMI queries for investigating failed path checks to determine if an application was launched in watched path.
		/// </summary>
		/// <value><c>true</c> if WMI queries are enabled; otherwise, <c>false</c>.</value>
		public static bool WMIQueries { get; private set; } = false;
		public static bool WMIPolling { get; private set; } = false;
		public static int WMIPollDelay { get; private set; } = 5;

		public static string ConfigVersion = "alpha.2";

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
			PathMonitorEnabled = compsec.GetSetDefault("Process paths", true, out modified).BoolValue;
			compsec["Process paths"].Comment = "Monitor starting processes based on their location. Configure in Paths.ini";
			dirtyconfig |= modified;
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
			MaintenanceMonitorEnabled = compsec.GetSetDefault("Maintenance", false, out modified).BoolValue;
			compsec["Maintenance"].Comment = "Enable basic maintenance monitoring functionality.";
			dirtyconfig |= modified;

			HealthMonitorEnabled = compsec.GetSetDefault("Health", false, out modified).BoolValue;
			compsec["Health"].Comment = "General system health monitoring suite.";
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
			ShowInaction = logsec.GetSetDefault("Show inaction", false, out modified).BoolValue;
			logsec["Show inaction"].Comment = "Shows lack of action take on processes.";
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
			SelfAffinity = perfsec.GetSetDefault("Self-affinity", -1, out modified).IntValue;
			perfsec["Self-affinity"].Comment = "Core mask as integer. 0 is for default OS control. -1 is for last core. Limiting to single core recommended.";
			dirtyconfig |= modified;
			if (SelfAffinity > Convert.ToInt32(Math.Pow(2, Environment.ProcessorCount) - 1 + double.Epsilon)) SelfAffinity = -1;

			PersistentWatchlistStats = perfsec.GetSetDefault("Persistent watchlist statistics", true, out modified).BoolValue;
			dirtyconfig |= modified;


			SelfOptimizeBGIO = perfsec.GetSetDefault("Background I/O mode", false, out modified).BoolValue;
			perfsec["Background I/O mode"].Comment = "Sets own priority exceptionally low. Warning: This can make TM's UI and functionality quite unresponsive.";
			dirtyconfig |= modified;

			WMIQueries = perfsec.GetSetDefault("WMI queries", true, out modified).BoolValue;
			perfsec["WMI queries"].Comment = "WMI is considered buggy and slow. Unfortunately necessary for some functionality.";
			dirtyconfig |= modified;
			WMIPolling = perfsec.GetSetDefault("WMI event watcher", false, out modified).BoolValue;
			perfsec["WMI event watcher"].Comment = "Use WMI to be notified of new processes starting.\nIf disabled, only rescanning everything will cause processes to be noticed.";
			dirtyconfig |= modified;
			WMIPollDelay = perfsec.GetSetDefault("WMI poll delay", 5, out modified).IntValue.Constrain(1, 30);
			perfsec["WMI poll delay"].Comment = "WMI process watcher delay (in seconds).  Smaller gives better results but can inrease CPU usage. Accepted values: 1 to 30.";
			dirtyconfig |= modified;

			perfsec.GetSetDefault("Child processes", false, out modified); // unused here
			perfsec["Child processes"].Comment = "Enables controlling process priority based on parent process if nothing else matches. This is slow and unreliable.";
			dirtyconfig |= modified;
			TempRescanThreshold = perfsec.GetSetDefault("Temp rescan threshold", 1000, out modified).IntValue;
			perfsec["Temp rescan threshold"].Comment = "How many changes we wait to temp folder before expediting rescanning it.";
			dirtyconfig |= modified;
			TempRescanDelay = perfsec.GetSetDefault("Temp rescan delay", 60, out modified).IntValue * 60_000;
			perfsec["Temp rescan delay"].Comment = "How many minutes to wait before rescanning temp after crossing the threshold.";
			dirtyconfig |= modified;

			PathCacheLimit = perfsec.GetSetDefault("Path cache", 60, out modified).IntValue;
			perfsec["Path cache"].Comment = "Path searching is very heavy process; this configures how many processes to remember paths for.\nThe cache is allowed to occasionally overflow for half as much.";
			dirtyconfig |= modified;
			if (PathCacheLimit < 0) PathCacheLimit = 0;
			if (PathCacheLimit > 0 && PathCacheLimit < 20) PathCacheLimit = 20;

			PathCacheMaxAge = perfsec.GetSetDefault("Path cache max age", 15, out modified).IntValue;
			perfsec["Path cache max age"].Comment = "Maximum age, in minutes, of cached objects. Min: 1 (1min), Max: 1440 (1day).\nThese will be removed even if the cache is appropriate size.";
			if (PathCacheMaxAge < 1) PathCacheMaxAge = 1;
			if (PathCacheMaxAge > 1440) PathCacheMaxAge = 1440;
			dirtyconfig |= modified;

			// 
			var maintsec = cfg["Maintenance"];
			CleanupInterval = maintsec.GetSetDefault("Cleanup interval", 15, out modified).IntValue.Constrain(1, 1440);
			maintsec["Cleanup interval"].Comment = "In minutes, 1 to 1440. How frequently to perform general sanitation of TM itself.";
			dirtyconfig |= modified;

			var newsettings = optsec?.SettingCount ?? 0 + compsec?.SettingCount ?? 0 + perfsec?.SettingCount ?? 0;

			if (dirtyconfig || (oldsettings != newsettings)) // really unreliable, but meh
				corecfg.MarkDirty();

			monitorCleanShutdown();

			Log.Information("<Core> Verbosity: "+ MemoryLog.MemorySink.LevelSwitch.MinimumLevel.ToString());
			Log.Information("<Core> Self-optimize: "+ (SelfOptimize ? HumanReadable.Generic.Enabled : HumanReadable.Generic.Disabled));
			// Log.Information("Low memory mode: {LowMemory}", (LowMemory ? "Enabled." : "Disabled."));
			Log.Information("<<WMI>> Event watcher: " + (WMIPolling ? HumanReadable.Generic.Enabled : HumanReadable.Generic.Disabled) + " (Rate: " + WMIPollDelay + "s)");
			Log.Information("<<WMI>> Queries: " + (WMIQueries ? HumanReadable.Generic.Enabled : HumanReadable.Generic.Disabled));

			// PROTECT USERS FROM TOO HIGH PERMISSIONS
			var isadmin = IsAdministrator();
			var adminwarning = ((cfg["Core"].TryGet("Hell")?.StringValue ?? null) != "No");
			if (isadmin && adminwarning)
			{
				var rv = MessageBox.Show("You're starting TM with admin rights, is this right?\n\nYou can cause bad system operation, such as complete system hang, if you configure or configured TM incorrectly.",
										 Application.ProductName + " – admin access detected!!??", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2, MessageBoxOptions.DefaultDesktopOnly, false);
				if (rv == DialogResult.Yes)
				{
					cfg["Core"]["Hell"].StringValue = "No";
					corecfg.MarkDirty();
				}
				else
				{
					Log.Warning("<Core> Admin rights detected, user rejected proceeding.");
					UnifiedExit();
					throw new RunstateException("Admin rights rejected", Runstate.QuickExit);
					return;
				}
			}
			// STOP IT

			// DEBUG
			var dbgsec = cfg["Debug"];
			DebugProcesses = dbgsec.TryGet("Processes")?.BoolValue ?? false;
			DebugPaths = dbgsec.TryGet("Paths")?.BoolValue ?? false;
			DebugFullScan = dbgsec.TryGet("Full scan")?.BoolValue ?? false;
			DebugAudio = dbgsec.TryGet(HumanReadable.Hardware.Audio.Section)?.BoolValue ?? false;

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
			IgnoreRecentlyModified = exsec.TryGet("Ignore recently modified")?.BoolValue ?? false;
			LastModifiedList = exsec.TryGet("Last Modified")?.BoolValue ?? false;
			TempMonitorEnabled = exsec.TryGet("Temp Monitor")?.BoolValue ?? false;


#if DEBUG
			Trace = dbgsec.TryGet("Trace")?.BoolValue ?? false;
#endif

			// END DEBUG

			Log.Information("<Core> Privilege level: " + (isadmin ? "Admin" : "User"));

			Log.Information("<Core> Path cache: " + (PathCacheLimit == 0 ? HumanReadable.Generic.Disabled : PathCacheLimit + " items"));

			Log.Information("<Core> Paging: " + (PagingEnabled ? HumanReadable.Generic.Enabled : HumanReadable.Generic.Disabled));

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

		static string corestatfile = "Core.Statistics.ini";
		static void monitorCleanShutdown()
		{
			var corestats = Config.Load(corestatfile);

			var running = corestats.Config.TryGet("Core")?.TryGet("Running")?.BoolValue ?? false;
			if (running) Log.Warning("Unclean shutdown.");

			corestats.Config["Core"]["Running"].BoolValue = true;
			corestats.Save(force:true);
		}

		static void CleanShutdown()
		{
			var corestats = Config.Load(corestatfile);

			var wmi = corestats.Config["WMI queries"];
			string timespent = "Time", querycount = "Queries";
			bool modified = false, dirtyconfig = false;

			wmi[timespent].DoubleValue = wmi.GetSetDefault(timespent, 0d, out modified).DoubleValue + Statistics.WMIquerytime;
			dirtyconfig |= modified;
			wmi[querycount].IntValue = wmi.GetSetDefault(querycount, 0, out modified).IntValue + Statistics.WMIqueries;
			dirtyconfig |= modified;
			var ps = corestats.Config["Parent seeking"];
			ps[timespent].DoubleValue = ps.GetSetDefault(timespent, 0d, out modified).DoubleValue + Statistics.Parentseektime;
			dirtyconfig |= modified;
			ps[querycount].IntValue = ps.GetSetDefault(querycount, 0, out modified).IntValue + Statistics.ParentSeeks;
			dirtyconfig |= modified;

			corestats.Config["Core"]["Running"].BoolValue = false;
			corestats.Save(force:true);
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
					Log.Debug("<Core> Pre-allocated file: " + fullpath + " (" + (oldsize / boundary) + "kB -> " + allockb + "kB)");
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

		public static bool ComponentConfigurationDone = false;

		static void ParseArguments(string[] args)
		{
			var StartDelay = 0;
			var uptime = TimeSpan.Zero;

			for (int i = 0; i < args.Length; i++)
			{
				switch (args[i])
				{
					case "--bootdelay":
						if (args.Length > i+1 && !args[i+1].StartsWith("--"))
						{
							try
							{
								StartDelay = Convert.ToInt32(args[++i]).Constrain(1, 60*5);
							}
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
						catch
						{
							uptime = TimeSpan.Zero;
						}

						break;
					case "--restart":
						if (args.Length > i+1 && !args[i+1].StartsWith("--"))
						{
							try
							{
								RestartCounter = Convert.ToInt32(args[++i]);
							}
							catch { }
						}

						break;
					case "--admin":
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
									info.Arguments = "--admin "+ ++AdminCounter;
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
							MessageBox.Show("", "Failure to elevate privileges, resuming as normal.", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
						}

						break;
					default:
						break;
				}
			}

			if (StartDelay > 0 && uptime.TotalSeconds < 300)
			{
				//Console.WriteLine("Delaying proper startup for " + uptimeMinSeconds + " seconds.");

				var remainingdelay = StartDelay - uptime.TotalSeconds;
				if (remainingdelay > 5)
				{
					Log.Information("Delaying start by " + remainingdelay + " seconds");
					System.Threading.Thread.Sleep(Convert.ToInt32(remainingdelay) * 1000);
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
					return;
				}
			}
		}

		public static bool IsMainThread()
		{
			return (System.Threading.Thread.CurrentThread.IsThreadPoolThread == false &&
					System.Threading.Thread.CurrentThread.ManagedThreadId == 1);
		}

		// Useful for figuring out multi-threading related problems
		// From StarOverflow: https://stackoverflow.com/q/22579206
		[Conditional("DEBUG")]
		public static void ThreadIdentity(string message = "")
		{
			var thread = System.Threading.Thread.CurrentThread;
			string name = thread.IsThreadPoolThread
				? "Thread pool" : thread.Name;
			if (string.IsNullOrEmpty(name))
				name = "#" + thread.ManagedThreadId;
			Console.WriteLine("Continuation on: " + name + " --- " + message);
		}

		static void PreallocLastLog()
		{
			string logpath = System.IO.Path.Combine(Taskmaster.datapath, "Logs");

			DateTime lastDate = DateTime.MinValue;
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

		// entry point to the application
		[STAThread] // supposedly needed to avoid shit happening with the WinForms GUI and other GUI toolkits
		static public int Main(string[] args)
		{
			System.Threading.Mutex singleton = null;

			System.Windows.Forms.Application.SetUnhandledExceptionMode(UnhandledExceptionMode.Automatic);
			System.Windows.Forms.Application.ThreadException += UnhandledUIException;
			System.Windows.Forms.Application.EnableVisualStyles(); // required by shortcuts and high dpi-awareness
			System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false); // required by high dpi-awareness

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
				try
				{
					Config = new ConfigManager(datapath);

					LicenseBoiler();

					// INIT LOGGER
					var logswitch = new LoggingLevelSwitch(LogEventLevel.Information);

					var logpathtemplate = System.IO.Path.Combine(datapath, "Logs", "taskmaster-{Date}.log");
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

					if (Taskmaster.Trace) Log.Verbose("Testing for single instance.");

					// Singleton
					bool mutexgained = false;
					singleton = new System.Threading.Mutex(true, "088f7210-51b2-4e06-9bd4-93c27a973874.taskmaster", out mutexgained);
					if (!mutexgained)
					{
						// already running, signal original process
						var rv = MessageBox.Show(
							"Already operational.\n\nAbort to kill running instance and exit this.\nRetry to try to recover running instance.\nIgnore to exit this.",
							System.Windows.Forms.Application.ProductName + "!",
							MessageBoxButtons.AbortRetryIgnore, MessageBoxIcon.Exclamation, MessageBoxDefaultButton.Button2);
						return -1;
					}

					/*
					var builddate = Convert.ToDateTime(Properties.Resources.BuildDate);

					Log.Information("Taskmaster! (#{ProcessID}) {Admin}– Version: {Version} [{Date} {Time}] – START!",
									Process.GetCurrentProcess().Id, (IsAdministrator() ? "[ADMIN] " : ""),
									System.Reflection.Assembly.GetExecutingAssembly().GetName().Version,
									builddate.ToShortDateString(), builddate.ToShortTimeString());
					*/

					Log.Information("Taskmaster! (#" + Process.GetCurrentProcess().Id + ") " +
						(IsAdministrator() ? "[ADMIN] " : "") +
						"– Version: " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version +
						" – START!");
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
					throw;
				}

				//PreallocLastLog();

				try
				{
					PreSetup();
					LoadCoreConfig();
					SetupComponents();
				}
				catch (RunstateException)
				{
					throw;
				}
				catch (Exception ex) // this seems to happen only when Avast cybersecurity is scanning TM
				{
					Log.Fatal("Exiting due to initialization failure.");
					Logging.Stacktrace(ex); // this doesn't work for some reason?
					throw new RunstateException("Initialization failure", Runstate.CriticalFailure, ex);
				}

				try
				{
					Config.Flush(); // early save of configs

					if (RestartCounter > 0) Log.Information("<Core> Restarted " + RestartCounter + " time(s)");
					Log.Information("<Core> Initialization complete...");

					/*
					Console.WriteLine("Embedded Resources");
					foreach (var name in System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceNames())
						Console.WriteLine(" - " + name);
					*/
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
					throw;
				}

				try
				{
					if (Taskmaster.ProcessMonitorEnabled)
						Evaluate(); // internally async always

					if (State == Runstate.Normal)
					{
						System.Windows.Forms.Application.Run(); // WinForms

						// System.Windows.Application.Current.Run(); // WPF
					}
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
					throw new RunstateException("Unhandled", Runstate.CriticalFailure, ex);
				}

				if (SelfOptimize) // return decent processing speed to quickly exit
				{
					var self = Process.GetCurrentProcess();
					self.PriorityClass = ProcessPriorityClass.AboveNormal;
					if (Taskmaster.SelfOptimizeBGIO)
					{
						try
						{
							ProcessController.SetIOPriority(self, NativeMethods.PriorityTypes.PROCESS_MODE_BACKGROUND_END);
						}
						catch { }
					}
				}

				Log.Information("Exiting...");

				// CLEANUP for exit

				ExitCleanup();

				Log.Information("WMI queries: " + $"{Statistics.WMIquerytime:N2}s [" + Statistics.WMIqueries + "]");
				Log.Information("Self-maintenance: " + $"{Statistics.MaintenanceTime:N2}s [" + Statistics.MaintenanceCount + "]");
				Log.Information("Path cache: " + Statistics.PathCacheHits + " hits, " + Statistics.PathCacheMisses + " misses");
				Log.Information("Path finding: " + Statistics.PathFindAttempts + " total attempts; " + Statistics.PathFindViaModule +
					" via module info, " + Statistics.PathFindViaC + " via C call, " + Statistics.PathFindViaWMI + " via WMI");
				Log.Information("Processes modified: " + Statistics.TouchCount + "; Ignored for remodification: " + Statistics.TouchIgnore);

				CleanShutdown();

				Utility.Dispose(ref Config);

				Log.Information("Taskmaster! (#" + Process.GetCurrentProcess().Id + ") END! [Clean]");

				if (State == Runstate.Restart) // happens only on power resume (waking from hibernation) or when manually set
				{
					ExitCleanup();

					singleton?.Dispose();

					Log.Information("Restarting...");
					try
					{
						if (!System.IO.File.Exists(Application.ExecutablePath))
							Log.Fatal("Executable missing: " + Application.ExecutablePath); // this should be "impossible"

						var info = Process.GetCurrentProcess().StartInfo;
						//info.FileName = Process.GetCurrentProcess().ProcessName;
						info.WorkingDirectory = System.IO.Path.GetDirectoryName(Application.ExecutablePath);
						info.FileName = System.IO.Path.GetFileName(Application.ExecutablePath);

						List<string> nargs = new List<string> { "--restart " + ++RestartCounter };  // has no real effect
						if (RestartElevated)
						{
							nargs.Add("--admin " + ++AdminCounter);
							info.Verb = "runas"; // elevate privileges
						}

						info.Arguments = string.Join(" ", nargs);

						Log.CloseAndFlush();

						var proc = Process.Start(info);
					}
					catch (Exception ex)
					{
						Logging.Stacktrace(ex, crashsafe: true);
					}
				}
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
				ExitCleanup();

				Logging.Stacktrace(ex, crashsafe: true);

				return 1; // should trigger finally block
			}
			catch (Exception ex)
			{
				ExitCleanup();

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

		/// <summary>
		/// Process unhandled WinForms exceptions.
		/// </summary>
		private static void UnhandledUIException(object _, System.Threading.ThreadExceptionEventArgs ea)
		{
			Logging.Stacktrace(ea.Exception);
		}
	}
}