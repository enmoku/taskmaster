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
using Taskmaster.SerilogMemorySink;

namespace Taskmaster
{
	[System.Runtime.InteropServices.Guid("088f7210-51b2-4e06-9bd4-93c27a973874")]//there's no point to this, is there?
	public static class Taskmaster
	{
		public static string GitURL { get; } = "https://github.com/mkahvi/taskmaster";
		public static string ItchURL { get; } = "https://mkah.itch.io/taskmaster";

		public static SharpConfig.Configuration cfg;
		public static string datapath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MKAh", "Taskmaster");

		// TODO: Pre-allocate space for the log files.

		static Dictionary<string, SharpConfig.Configuration> Configs = new Dictionary<string, SharpConfig.Configuration>();
		static Dictionary<SharpConfig.Configuration, bool> ConfigDirty = new Dictionary<SharpConfig.Configuration, bool>();
		static Dictionary<SharpConfig.Configuration, string> ConfigPaths = new Dictionary<SharpConfig.Configuration, string>();

		public static void SaveConfig(SharpConfig.Configuration config)
		{
			if (ConfigPaths.TryGetValue(config, out string filename))
			{
				SaveConfig(filename, config);
				return;
			}

			throw new ArgumentException();
		}

		// TODO: Add error handling.
		public static void SaveConfig(string configfile, SharpConfig.Configuration config)
		{
			try
			{
				System.IO.Directory.CreateDirectory(datapath);
			}
			catch
			{
				Log.Warning("Failed to create directory: {Path}", datapath);
				return;
			}

			string targetfile = System.IO.Path.Combine(datapath, configfile);
			try
			{
				// backup, copy in case following write fails
				System.IO.File.Copy(targetfile, targetfile + ".bak", overwrite: true);
				config.SaveToFile(targetfile);
			}
			catch
			{
				Log.Warning("Failed to write: {Target}", targetfile);
			}
			// TODO: Pre-allocate some space for the config file?
		}

		public static void UnloadConfig(string configfile)
		{
			if (Configs.TryGetValue(configfile, out var retcfg))
			{
				Configs.Remove(configfile);
				ConfigPaths.Remove(retcfg);
			}
		}

		public static SharpConfig.Configuration LoadConfig(string configfile)
		{
			SharpConfig.Configuration retcfg;
			if (Configs.TryGetValue(configfile, out retcfg))
			{
				return retcfg;
			}

			var path = System.IO.Path.Combine(datapath, configfile);
			// Log.Trace("Opening: "+path);
			if (System.IO.File.Exists(path))
				retcfg = SharpConfig.Configuration.LoadFromFile(path);
			else
			{
				Log.Warning("Not found: {Path}", path);
				retcfg = new SharpConfig.Configuration();
				System.IO.Directory.CreateDirectory(datapath);
			}

			Configs.Add(configfile, retcfg);
			ConfigPaths.Add(retcfg, configfile);

			if (Taskmaster.Trace) Log.Verbose("{ConfigFile} added to known configurations files.", configfile);

			return retcfg;
		}

		public static MicManager micmonitor = null;
		public static object mainwindow_lock = new object();
		public static MainWindow mainwindow = null;
		public static ProcessManager processmanager = null;
		public static TrayAccess trayaccess = null;
		public static NetManager netmonitor = null;
		public static DiskManager diskmanager = null;
		public static PowerManager powermanager = null;
		public static ActiveAppManager activeappmonitor = null;
		public static HealthMonitor healthmonitor = null;

		public static object watchlist_lock = new object();

		public static void MainWindowClose(object sender, EventArgs e)
		{
			// Calling dispose here for mainwindow is WRONG, DON'T DO IT
			// only do it if ev.Cancel=true, I mean.

			lock (mainwindow_lock)
				mainwindow = null;
		}

		static bool RunOnce = false;
		public static bool Restart = false;
		public static bool RestartElevated = false;
		static int RestartCounter = 0;
		static int AdminCounter = 0;

		public static void AutomaticRestartRequest(object sender, EventArgs e)
		{
			Restart = true;
			UnifiedExit();
		}

		public static void ConfirmExit(bool restart = false, bool admin = false)
		{
			var rv = DialogResult.Yes;
			if (RequestExitConfirm)
				rv = MessageBox.Show("Are you sure you want to " + (restart ? "restart" : "exit") + " Taskmaster?",
									 (restart ? "Restart" : "Exit") + Application.ProductName + " ???",
									 MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1, MessageBoxOptions.DefaultDesktopOnly, false);

			if (rv != DialogResult.Yes) return;

			Restart = restart;
			RestartElevated = admin;

			UnifiedExit();
		}

		// User logs outt
		public static void SessionEndExitRequest(object sender, EventArgs e)
		{
			UnifiedExit();
			// CLEANUP:
			// if (Taskmaster.VeryVerbose) Console.WriteLine("END:Core.ExitRequest - Exit hang averted");
		}

		delegate void EmptyFunction();

		public static void UnifiedExit()
		{
			Application.Exit(); // if this throws, it deserves to break everything
		}

		/// <summary>
		/// Call any supporting functions to re-evaluate current situation.
		/// </summary>
		public static async Task Evaluate()
		{
			// await EvaluateDispatch().ConfigureAwait(false);
			Task.Factory.StartNew(EvaluateDispatch, TaskCreationOptions.RunContinuationsAsynchronously);
		}

		static async Task EvaluateDispatch()
		{
			try
			{
				processmanager?.ScanEverythingRequest(null, null);
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		public static async void ShowMainWindow()
		{
			//await Task.Delay(0);

			try
			{
				using (var m = SelfAwareness.Mind(DateTime.Now.AddSeconds(30)))
				{
					// Log.Debug("Bringing to front");
					BuildMainWindow();
					mainwindow?.Reveal();
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		/// <summary>
		/// Constructs and hooks the main window
		/// </summary>
		public static void BuildMainWindow()
		{
			lock (mainwindow_lock)
			{
				if (mainwindow == null)
				{
					if (Taskmaster.Trace)
						Console.WriteLine("Building MainWindow");
					mainwindow = new MainWindow();
					mainwindow.FormClosed += MainWindowClose;
					try
					{
						if (diskmanager != null)
						{
							if (Taskmaster.Trace)
								Console.WriteLine("... hooking NVM manager");
							mainwindow.hookDiskManager(ref diskmanager);
						}

						if (processmanager != null)
						{
							if (Taskmaster.Trace)
								Console.WriteLine("... hooking PROC manager");
							mainwindow.hookProcessManager(ref processmanager);
						}

						if (micmonitor != null)
						{
							if (Taskmaster.Trace)
								Console.WriteLine("... hooking MIC monitor");
							mainwindow.hookMicMonitor(micmonitor);
						}

						if (netmonitor != null)
						{
							if (Taskmaster.Trace)
								Console.WriteLine("... hooking NET monitor");
							mainwindow.hookNetMonitor(ref netmonitor);
						}

						if (activeappmonitor != null)
						{
							if (Taskmaster.Trace)
								Console.WriteLine("... hooking APP manager");
							mainwindow.hookActiveAppMonitor(ref activeappmonitor);
						}

						if (powermanager != null)
						{
							if (Taskmaster.Trace)
								Console.WriteLine("... hooking POW manager");
							mainwindow.hookPowerManager(ref powermanager);
						}
					}
					catch (Exception ex)
					{
						Logging.Stacktrace(ex);
					}

					if (Taskmaster.Trace)
						Console.WriteLine("... hooking to TRAY");
					trayaccess.hookMainWindow(ref mainwindow);

					if (Taskmaster.Trace)
						Console.WriteLine("MainWindow built");
				}
			}
		}

		static SelfAwareness selfaware;

		static void PreSetup()
		{
			// INITIAL CONFIGURATIONN
			var tcfg = LoadConfig("Core.ini");
			var sec = tcfg.TryGet("Core")?.TryGet("Version")?.StringValue ?? null;
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
					// singleton.ReleaseMutex();
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

			bool tray = true;
			bool aware = true;

			if (RunOnce)
			{
				tray = false;
				aware = false;
				PowerManagerEnabled = false;
				MicrophoneMonitorEnabled = false;
				MaintenanceMonitorEnabled = false;
				HealthMonitorEnabled = false;
				NetworkMonitorEnabled = false;
				ActiveAppMonitorEnabled = false;

				WMIPolling = false;
				
				SelfOptimize = false;
			}

			// Parallel loading, cuts down startup time some.
			// This is really bad if something fails
			Task[] init =
			{
				aware ? Task.Run(() => { selfaware = new SelfAwareness(); }) : Task.CompletedTask,
				PowerManagerEnabled ? (Task.Run(() => { powermanager = new PowerManager(); })) : Task.CompletedTask,
				ProcessMonitorEnabled ? (Task.Run(() => { processmanager = new ProcessManager(); })) : Task.CompletedTask,
				(ActiveAppMonitorEnabled && ProcessMonitorEnabled) ? (Task.Run(()=> {activeappmonitor = new ActiveAppManager(eventhook:false); })) : Task.CompletedTask,
				MicrophoneMonitorEnabled ? (Task.Run(() => { micmonitor = new MicManager(); })) : Task.CompletedTask,
				NetworkMonitorEnabled ? (Task.Run(() => { netmonitor = new NetManager(); })) : Task.CompletedTask,
				MaintenanceMonitorEnabled ? (Task.Run(() => { diskmanager = new DiskManager(); })) : Task.CompletedTask,
				HealthMonitorEnabled ? (Task.Run(() => { healthmonitor = new HealthMonitor(); })) : Task.CompletedTask,
			};

			// WinForms makes the following components not load nicely if not done here.
			if (tray) trayaccess = new TrayAccess();

			Log.Information("<Core> Waiting for component loading.");
			Task.WaitAll(init);

			// HOOKING
			// Probably should transition to weak events

			Log.Information("<Core> Components loaded; Hooking event handlers.");

			if (PowerManagerEnabled)
			{
				trayaccess.hookPowerManager(ref powermanager);
				powermanager.onBatteryResume += AutomaticRestartRequest;
			}

			if (ProcessMonitorEnabled && PowerManagerEnabled)
				powermanager.onBehaviourChange += processmanager.PowerBehaviourEvent;

			if (NetworkMonitorEnabled)
				netmonitor.Tray = trayaccess;

			if (processmanager != null)
				trayaccess?.hookProcessManager(ref processmanager);

			if (ActiveAppMonitorEnabled && ProcessMonitorEnabled)
			{
				processmanager.hookActiveAppManager(ref activeappmonitor);
				activeappmonitor.SetupEventHook();
			}

			if (PowerManagerEnabled)
			{
				powermanager.SetupEventHook();
			}

			// UI

			if (ShowOnStart && !RunOnce)
			{
				BuildMainWindow();
				mainwindow?.Reveal();
			}

			// Self-optimization
			if (SelfOptimize)
			{
				var self = Process.GetCurrentProcess();
				self.PriorityClass = SelfPriority; // should never throw
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

				if (SelfOptimizeBGIO)
				{
					try { ProcessController.SetIOPriority(self, NativeMethods.PriorityTypes.PROCESS_MODE_BACKGROUND_BEGIN); }
					catch { Log.Warning("Failed to set self to background mode."); }
				}
			}

			if (Taskmaster.Trace)
				Console.WriteLine("Displaying Tray Icon");
			trayaccess?.Refresh();
		}

		public static bool ShowProcessAdjusts { get; set; } = true;
		public static bool ShowSessionActions { get; set; } = true;

		public static bool DebugProcesses { get; set; } = false;
		public static bool DebugPaths { get; set; } = false;
		public static bool DebugFullScan { get; set; } = false;

		public static bool DebugForeground { get; set; } = false;

		public static bool DebugPower { get; set; } = false;
		public static bool DebugAutoPower { get; set; } = false;
		public static bool DebugPowerRules { get; set; } = false;
		public static bool DebugMonitor { get; set; } = false;

		public static bool DebugSession { get; set; } = false;

		public static bool DebugWMI { get; set; } = false;

		public static bool DebugMemory { get; set; } = false;
		public static bool DebugPaging { get; set; } = false;

		public static bool DebugNet { get; set; } = false;
		public static bool DebugMic { get; set; } = false;

		public static bool Trace = false;
		public static bool ShowInaction = false;

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

		public static bool ShowOnStart { get; private set; } = true;

		public static bool SelfOptimize { get; private set; } = true;
		public static ProcessPriorityClass SelfPriority { get; private set; } = ProcessPriorityClass.BelowNormal;
		public static bool SelfOptimizeBGIO { get; private set; } = false;
		public static int SelfAffinity { get; private set; } = -1;

		// public static bool LowMemory { get; private set; } = true; // low memory mode; figure out way to auto-enable this when system is low on memory

		public static int TempRescanDelay = 60 * 60 * 1000;
		public static int TempRescanThreshold = 1000;

		public static int PathCacheLimit = 200;
		public static int PathCacheMaxAge = 1800;

		public static int CleanupInterval = 15;

		/// <summary>
		/// Whether to use WMI queries for investigating failed path checks to determine if an application was launched in watched path.
		/// </summary>
		/// <value><c>true</c> if WMI queries are enabled; otherwise, <c>false</c>.</value>
		public static bool WMIQueries { get; private set; } = false;
		public static bool WMIPolling { get; private set; } = false;
		public static int WMIPollDelay { get; private set; } = 5;

		public static void MarkDirtyINI(SharpConfig.Configuration dirtiedcfg)
		{
			if (ImmediateSave)
			{
				SaveConfig(dirtiedcfg);
			}
			else
			{
				try
				{
					ConfigDirty.Add(dirtiedcfg, true);
				}
				catch { } // NOP, already in
			}
		}

		public static string ConfigVersion = "alpha.2";

		public static bool RequestExitConfirm = true;
		public static bool AutoOpenMenus = true;
		public static bool ShowInTaskbar = false;
		public static bool ImmediateSave = true;

		static string coreconfig = "Core.ini";
		static void LoadCoreConfig()
		{
			Log.Information("<Core> Loading configuration...");

			cfg = LoadConfig(coreconfig);

			if (cfg.TryGet("Core")?.TryGet("Hello")?.RawValue != "Hi")
			{
				Log.Information("Hello");
				cfg["Core"]["Hello"].SetValue("Hi");
				cfg["Core"]["Hello"].PreComment = "Heya";
				MarkDirtyINI(cfg);
			}

			var compsec = cfg["Components"];
			var optsec = cfg["Options"];
			var perfsec = cfg["Performance"];

			bool modified = false, dirtyconfig = false;
			cfg["Core"].GetSetDefault("License", "Refused", out modified).StringValue = "Accepted";
			dirtyconfig |= modified;

			var oldsettings = optsec?.SettingCount ?? 0 + compsec?.SettingCount ?? 0 + perfsec?.SettingCount ?? 0;

			// [Components]
			ProcessMonitorEnabled = compsec.GetSetDefault("Process", true, out modified).BoolValue;
			compsec["Process"].Comment = "Monitor starting processes based on their name. Configure in Apps.ini";
			dirtyconfig |= modified;
			PathMonitorEnabled = compsec.GetSetDefault("Process paths", true, out modified).BoolValue;
			compsec["Process paths"].Comment = "Monitor starting processes based on their location. Configure in Paths.ini";
			dirtyconfig |= modified;
			MicrophoneMonitorEnabled = compsec.GetSetDefault("Microphone", true, out modified).BoolValue;
			compsec["Microphone"].Comment = "Monitor and force-keep microphone volume.";
			dirtyconfig |= modified;
			// MediaMonitorEnabled = compsec.GetSetDefault("Media", true, out modified).BoolValue;
			// compsec["Media"].Comment = "Unused";
			// dirtyconfig |= modified;
			ActiveAppMonitorEnabled = compsec.GetSetDefault("Foreground", true, out modified).BoolValue;
			compsec["Foreground"].Comment = "Game/Foreground app monitoring and adjustment.";
			dirtyconfig |= modified;
			NetworkMonitorEnabled = compsec.GetSetDefault("Network", true, out modified).BoolValue;
			compsec["Network"].Comment = "Monitor network uptime and current IP addresses.";
			dirtyconfig |= modified;
			PowerManagerEnabled = compsec.GetSetDefault("Power", true, out modified).BoolValue;
			compsec["Power"].Comment = "Enable power plan management.";
			dirtyconfig |= modified;
			PagingEnabled = compsec.GetSetDefault("Paging", true, out modified).BoolValue;
			compsec["Paging"].Comment = "Enable paging of apps as per their configuration.";
			dirtyconfig |= modified;
			MaintenanceMonitorEnabled = compsec.GetSetDefault("Maintenance", false, out modified).BoolValue;
			compsec["Maintenance"].Comment = "Enable basic maintenance monitoring functionality.";
			dirtyconfig |= modified;

			HealthMonitorEnabled = compsec.GetSetDefault("Health", true, out modified).BoolValue;
			compsec["Health"].Comment = "General system health monitoring suite.";
			dirtyconfig |= modified;

			var qol = cfg["Quality of Life"];
			RequestExitConfirm = qol.GetSetDefault("Confirm exit", true, out modified).BoolValue;
			dirtyconfig |= modified;
			AutoOpenMenus = qol.GetSetDefault("Auto-open menus", true, out modified).BoolValue;
			dirtyconfig |= modified;
			ShowInTaskbar = qol.GetSetDefault("Show in taskbar", true, out modified).BoolValue;
			dirtyconfig |= modified;

			var logsec = cfg["Logging"];
			var Verbosity = logsec.GetSetDefault("Verbosity", 0, out modified).IntValue;
			logsec["Verbosity"].Comment = "0 = Information, 1 = Debug, 2 = Verbose/Trace, 3 = Excessive";
			switch (Verbosity)
			{
				default:
				case 0:
					MemoryLog.LevelSwitch.MinimumLevel = LogEventLevel.Information;
					break;
				case 1:
					MemoryLog.LevelSwitch.MinimumLevel = LogEventLevel.Debug;
					break;
				case 2:
					MemoryLog.LevelSwitch.MinimumLevel = LogEventLevel.Verbose;
					break;
				case 3:
					MemoryLog.LevelSwitch.MinimumLevel = LogEventLevel.Verbose;
					Trace = true;
					break;
			}

			dirtyconfig |= modified;
			ShowInaction = logsec.GetSetDefault("Show inaction", false, out modified).BoolValue;
			logsec["Show inaction"].Comment = "Shows lack of action take on processes.";
			dirtyconfig |= modified;

			ShowProcessAdjusts = logsec.GetSetDefault("Show process adjusts", true, out modified).BoolValue;
			logsec["Show process adjusts"].Comment = "Show blurbs about adjusted processes.";
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
			TempRescanDelay = perfsec.GetSetDefault("Temp rescan delay", 60, out modified).IntValue * 60 * 1000;
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

			ImmediateSave = perfsec.GetSetDefault("Immediate configuration saving", false, out modified).BoolValue;
			perfsec["Immediate configuration saving"].Comment = "Immediately save configuration files instead of at exit.";
			dirtyconfig |= modified;

			var newsettings = optsec?.SettingCount ?? 0 + compsec?.SettingCount ?? 0 + perfsec?.SettingCount ?? 0;

			if (dirtyconfig || (oldsettings != newsettings)) // really unreliable, but meh
				MarkDirtyINI(cfg);

			monitorCleanShutdown();

			Log.Information("<Core> Verbosity: {Verbosity}", MemoryLog.LevelSwitch.MinimumLevel.ToString());
			Log.Information("<Core> Self-optimize: {SelfOptimize}", (SelfOptimize ? "Enabled" : "Disabled"));
			// Log.Information("Low memory mode: {LowMemory}", (LowMemory ? "Enabled." : "Disabled."));
			Log.Information("<<WMI>> Event watcher: {WMIPolling} (Rate: {WMIRate}s)", (WMIPolling ? "Enabled" : "Disabled"), WMIPollDelay);
			Log.Information("<<WMI>> Queries: {WMIQueries}", (WMIQueries ? "Enabled" : "Disabled"));

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
					MarkDirtyINI(cfg);
				}
				else
				{
					Environment.Exit(2);
				}
			}
			// STOP IT

			// DEBUG
			var dbgsec = cfg["Debug"];
			DebugProcesses = dbgsec.TryGet("Processes")?.BoolValue ?? false;
			DebugPaths = dbgsec.TryGet("Paths")?.BoolValue ?? false;
			DebugFullScan = dbgsec.TryGet("Full scan")?.BoolValue ?? false;

			DebugForeground = dbgsec.TryGet("Foreground")?.BoolValue ?? false;

			DebugPower = dbgsec.TryGet("Power")?.BoolValue ?? false;
			DebugAutoPower = dbgsec.TryGet("Auto-adjust")?.BoolValue ?? false;
			//DebugPowerRules = dbgsec.TryGet("Paths")?.BoolValue ?? false;
			DebugMonitor = dbgsec.TryGet("Monitor")?.BoolValue ?? false;

			DebugSession = dbgsec.TryGet("Session")?.BoolValue ?? false;

			DebugWMI = dbgsec.TryGet("WMI")?.BoolValue ?? false;

			DebugMemory = dbgsec.TryGet("Memory")?.BoolValue ?? false;
			DebugPaging = dbgsec.TryGet("Paging")?.BoolValue ?? true;

			DebugNet = dbgsec.TryGet("Network")?.BoolValue ?? false;
			DebugMic = dbgsec.TryGet("Microphone")?.BoolValue ?? false;

			Trace = dbgsec.TryGet("Trace")?.BoolValue ?? false;

			// END DEBUG

			Log.Information("<Core> Privilege level: {Privilege}", isadmin ? "Admin" : "User");

			Log.Information("<Core> Path cache: " + (PathCacheLimit == 0 ? "Disabled" : PathCacheLimit + " items"));
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

		static SharpConfig.Configuration corestats;
		static string corestatfile = "Core.Statistics.ini";
		static void monitorCleanShutdown()
		{
			if (corestats == null)
				corestats = LoadConfig(corestatfile);

			var running = corestats.TryGet("Core")?.TryGet("Running")?.BoolValue ?? false;
			if (running) Log.Warning("Unclean shutdown.");

			corestats["Core"]["Running"].BoolValue = true;
			SaveConfig(corestats);
		}

		static void CleanShutdown()
		{
			if (corestats == null) corestats = LoadConfig(corestatfile);

			var wmi = corestats["WMI queries"];
			string timespent = "Time", querycount = "Queries";
			bool modified = false, dirtyconfig = false;

			wmi[timespent].DoubleValue = wmi.GetSetDefault(timespent, 0d, out modified).DoubleValue + Statistics.WMIquerytime;
			dirtyconfig |= modified;
			wmi[querycount].IntValue = wmi.GetSetDefault(querycount, 0, out modified).IntValue + Statistics.WMIqueries;
			dirtyconfig |= modified;
			var ps = corestats["Parent seeking"];
			ps[timespent].DoubleValue = ps.GetSetDefault(timespent, 0d, out modified).DoubleValue + Statistics.Parentseektime;
			dirtyconfig |= modified;
			ps[querycount].IntValue = ps.GetSetDefault(querycount, 0, out modified).IntValue + Statistics.ParentSeeks;
			dirtyconfig |= modified;

			corestats["Core"]["Running"].BoolValue = false;

			SaveConfig(corestats);
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
					Console.WriteLine("Pre-allocated file: " + fullpath + " (" + (oldsize / boundary) + "kB -> " + allockb + "kB)");
				}
			}
			catch (System.IO.FileNotFoundException)
			{
				Console.WriteLine("Failed to open file: " + fullpath);
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

				if (rv)
				{
					fs.SetLength(allockb * 1024);
				}
			}
			catch { } // ignore, no access probably
			finally
			{
				fs?.Close();
			}
		}

		public static bool ComponentConfigurationDone = false;

		public static async void Cleanup(object sender, EventArgs ev)
		{
			if (Taskmaster.Trace)
				Log.Verbose("Running periodic cleanup");

			// TODO: This starts getting weird if cleanup interval is smaller than total delay of testing all items.
			// (15*60) / 2 = item limit, and -1 or -2 for safety margin. Unlikely, but should probably be covered anyway.

			var time = Stopwatch.StartNew();

			if (processmanager != null)
			{
				using (var m = SelfAwareness.Mind(DateTime.Now.AddSeconds(30)))
					await processmanager.Cleanup().ConfigureAwait(false);

			}

			time.Stop();

			Statistics.Cleanups++;
			Statistics.CleanupTime += time.Elapsed.TotalSeconds;

			if (Taskmaster.Trace)
				Log.Verbose("Cleanup took: {Time}s", string.Format("{0:N2}", time.Elapsed.TotalSeconds));
		}

		public static System.Timers.Timer CleanupTimer = null;

		public static System.Threading.Mutex singleton = null;

		public static bool SingletonLock()
		{
			if (Taskmaster.Trace) Log.Verbose("Testing for single instance.");

			var mutexgained = false;
			singleton = new System.Threading.Mutex(true, "088f7210-51b2-4e06-9bd4-93c27a973874.taskmaster", out mutexgained);
			if (!mutexgained)
			{
				// already running, signal original process
				System.Windows.Forms.MessageBox.Show("Already operational.", System.Windows.Forms.Application.ProductName + "!");
				Log.Warning("Exiting (#{ProcessID}); already running.", System.Diagnostics.Process.GetCurrentProcess().Id);
			}

			return mutexgained;
		}

		static void Cleanup()
		{
			try
			{
				if (mainwindow != null)
				{
					if (!mainwindow.IsDisposed) mainwindow.Dispose();
					mainwindow = null;
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}

			try
			{
				processmanager?.Dispose();
				processmanager = null;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}

			try
			{
				powermanager?.Dispose();
				powermanager = null;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}

			try
			{
				micmonitor?.Dispose();
				micmonitor = null;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}

			try
			{
				trayaccess?.Dispose();
				trayaccess = null;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}

			try
			{
				netmonitor?.Dispose();
				netmonitor = null;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}

			try
			{
				activeappmonitor?.Dispose();
				activeappmonitor = null;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}

			try
			{
				healthmonitor?.Dispose();
				healthmonitor = null;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		static void ParseArguments(string[] args)
		{
			var StartDelay = 0;

			for (int i = 0; i < args.Length; i++)
			{
				switch (args[i])
				{
					case "--bootdelay":
						if (args.Length > i && !args[i].StartsWith("--"))
						{
							try
							{
								StartDelay = Convert.ToInt32(args[++i]);
							}
							catch
							{
								StartDelay = 30;
							}
						}

						var uptimeMin = 30;
						if (args.Length > 1)
						{
							try
							{
								var nup = Convert.ToInt32(args[1]);
								uptimeMin = nup.Constrain(5, 360);
							}
							catch { } // NOP
						}

						var uptime = TimeSpan.Zero;
						try
						{
							using (var uptimecounter = new PerformanceCounter("System", "System Up Time"))
							{
								uptimecounter.NextValue();
								uptime = TimeSpan.FromSeconds(uptimecounter.NextValue());
							}
						}
						catch { }

						if (uptime.TotalSeconds < uptimeMin)
						{
							Console.WriteLine("Delaying proper startup for " + uptimeMin + " seconds.");
							System.Threading.Thread.Sleep(Math.Max(0, uptimeMin - Convert.ToInt32(uptime.TotalSeconds)) * 1000);
						}

						break;
					case "--restart":
						if (args.Length > i && !args[i].StartsWith("--"))
						{
							try
							{
								RestartCounter = Convert.ToInt32(args[++i]);
							}
							catch { }
						}

						break;
					case "--admin":
						if (args.Length > i && !args[i].StartsWith("--"))
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
									info.Arguments = string.Format("--admin {0}", ++AdminCounter);
									info.Verb = "runas"; // elevate privileges
									var proc = Process.Start(info);
								}
								finally
								{
									Environment.Exit(0);
								}
							}
						}
						else
						{
							MessageBox.Show("", "Failure to elevate privileges, resuming as normal.", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
						}

						break;
					case "--once":
						RunOnce = true;
						break;
					default:
						break;
				}
			}
		}

		static void LicenseBoiler()
		{
			var cfg = LoadConfig(coreconfig);

			if (cfg.TryGet("Core")?.TryGet("License")?.RawValue.Equals("Accepted") ?? false) return;

			using (var license = new LicenseDialog())
			{
				license.ShowDialog();
				if (license.DialogResult != DialogResult.Yes)
				{
					Environment.Exit(-1);
				}
			}
		}

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

		static void UpgradeAppSettings()
		{
			/*
			if (Properties.Settings.Default.UpgradeNeeded)
			{
				Properties.Settings.Default.Upgrade();
				Properties.Settings.Default.UpgradeNeeded = false;
				Properties.Settings.Default.Save();
				//Properties.Settings.Default.Reload(); // part of .Upgrade()
			}
			*/
		}

		// entry point to the application
		[STAThread] // supposedly needed to avoid shit happening with the WinForms GUI and other GUI toolkits
		static public int Main(string[] args)
		{
			UpgradeAppSettings();

			LicenseBoiler();

			// INIT LOGGER
			MemoryLog.LevelSwitch = new LoggingLevelSwitch(LogEventLevel.Information);

			var logpathtemplate = System.IO.Path.Combine(datapath, "Logs", "taskmaster-{Date}.log");
			Serilog.Log.Logger = new Serilog.LoggerConfiguration()
				.MinimumLevel.Verbose()
#if DEBUG
				.WriteTo.Console(levelSwitch: new LoggingLevelSwitch(LogEventLevel.Verbose))
#endif
				.WriteTo.RollingFile(logpathtemplate, outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
									 levelSwitch: new LoggingLevelSwitch(Serilog.Events.LogEventLevel.Debug), retainedFileCountLimit: 3)
				.WriteTo.MemorySink(levelSwitch: MemoryLog.LevelSwitch)
							 .CreateLogger();

			// COMMAND-LINE ARGUMENTS
			ParseArguments(args);

			// STARTUP

			if (!SingletonLock())
				return -1;

			Log.Information("Taskmaster! (#{ProcessID}) {Admin}– Version: {Version} – START!",
							Process.GetCurrentProcess().Id, (IsAdministrator() ? "[ADMIN] " : ""),
							System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);

			//PreallocLastLog();

			try
			{
				PreSetup();
				LoadCoreConfig();
				SetupComponents();
			}
			catch (Exception ex) // this seems to happen only when Avast cybersecurity is scanning TM
			{
				Log.Fatal("Exiting due to initialization failure.");
				Logging.Stacktrace(ex);
				Log.CloseAndFlush();
				singleton.Dispose();
				singleton = null;
				return 1;
			}

			// IS THIS OF ANY USE?
			// GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
			// GC.WaitForPendingFinalizers();

			// early save of configs
			foreach (var dcfg in ConfigDirty)
				if (dcfg.Value) SaveConfig(dcfg.Key);
			ConfigDirty.Clear();

			// Properties.Settings.Default.Save();

			CleanupTimer = new System.Timers.Timer(1000 * 60 * CleanupInterval);
			CleanupTimer.Elapsed += Taskmaster.Cleanup;
			CleanupTimer.Start();

			Log.Information("<Core> Initialization complete...");

			Console.WriteLine("Embedded Resources");
			foreach (var name in System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceNames())
				Console.WriteLine(" - " + name);

			try
			{
				if (Taskmaster.ProcessMonitorEnabled)
				{
					System.Threading.ManualResetEvent re = null;
					EventHandler end = delegate { re?.Set(); };

					if (RunOnce)
					{
						re = new System.Threading.ManualResetEvent(false);
						processmanager.ScanEverythingEndEvent += end;
					}

					Task.Factory.StartNew(Evaluate, TaskCreationOptions.LongRunning);

					if (re != null)
					{
						re.WaitOne();
						processmanager.ScanEverythingEndEvent -= end;
						re.Dispose();
					}
				}

				if (!RunOnce)
				{
					trayaccess.EnsureVisible();

					System.Windows.Forms.Application.Run(); // WinForms

					// System.Windows.Application.Current.Run(); // WPF
				}
			}
			catch (Exception ex)
			{
				Log.Fatal("Unhandled exception! Dying.");
				Logging.Stacktrace(ex);
				// TODO: ACTUALLY DIE
			}
			finally
			{
				if (mainwindow != null)
					mainwindow.FormClosed -= MainWindowClose;
			}

			if (SelfOptimize && !RunOnce) // return decent processing speed to quickly exit
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

			// TODO: Save Config, do this better. Maybe data bindings.
			bool dirty = cfg["Quality of Life"]["Auto-open menus"].Set(AutoOpenMenus);
			if (dirty) MarkDirtyINI(cfg);

			// CLEANUP for exit

			Cleanup();

			Log.Information("WMI queries: {QueryTime}s [{QueryCount}]", string.Format("{0:N2}", Statistics.WMIquerytime), Statistics.WMIqueries);
			Log.Information("Cleanups: {CleanupTime}s [{CleanupCount}]", string.Format("{0:N2}", Statistics.CleanupTime), Statistics.Cleanups);

			foreach (var dcfg in ConfigDirty)
				if (dcfg.Value) SaveConfig(dcfg.Key);
			ConfigDirty.Clear();

			CleanShutdown();

			Log.Information("Taskmaster! (#{ProcessID}) END! [Clean]", System.Diagnostics.Process.GetCurrentProcess().Id);

			singleton.Close();
			singleton = null;

			if (Restart) // happens only on power resume (waking from hibernation) or when manually set
			{
				Log.Information("Restarting...");
				try
				{
					if (!System.IO.File.Exists(Application.ExecutablePath))
						Log.Fatal("Executable missing: {Path}", Application.ExecutablePath);

					Log.CloseAndFlush();

					Restart = false; // pointless probably

					var info = Process.GetCurrentProcess().StartInfo;
					//info.FileName = Process.GetCurrentProcess().ProcessName;
					info.WorkingDirectory = System.IO.Path.GetDirectoryName(Application.ExecutablePath);
					info.FileName = System.IO.Path.GetFileName(Application.ExecutablePath);

					List<string> nargs = new List<string>
					{
						string.Format("--restart {0}", ++RestartCounter)  // has no real effect
					};
					if (RestartElevated)
					{
						nargs.Add(string.Format("--admin {0}", ++AdminCounter));
						info.Verb = "runas"; // elevate privileges
					}

					info.Arguments = string.Join(" ", nargs);

					var proc = Process.Start(info);
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex, oob: true);
				}
			}

			return 0;
		}
	}
}