//
// TaskMaster.cs
//
// Author:
//       M.A. (enmoku)
//
// Copyright (c) 2016-2018 M.A. (enmoku)
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
// FITNESS FOR A PARTICULAR PURPE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.


/*
 * TODO: Add process IO priority modification.
 * TODO: Detect full screen or GPU accelerated apps and adjust their priorities.
 * TODO: Detect if the above apps hang and lower their processing priorities as result.
 * 
 * MAYBE:
 *  - Monitor [MFT] fragmentation?
 *  - Detect which apps are making noise?
 *  - Detect high disk usage.
 *  - Clean %TEMP% with same design goals as the OS builtin disk cleanup utility.
 *  - SMART stats? seems pointless...
 *  - Action logging
 * 
 * CONFIGURATION:
 * TODO: Ini file? Yaml?
 * TODO: Config in app directory
 * TODO: Config in %APPDATA% or %LOCALAPPDATA%
 * 
 * Other:
 *  - Multiple windows or tabbed window?
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using TaskMaster.SerilogMemorySink;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TaskMaster
{
	//[Guid("088f7210-51b2-4e06-9bd4-93c27a973874")]//there's no point to this, is there?
	public class TaskMaster
	{
		public static string URL { get; } = "https://github.com/enmoku/taskmaster";

		public static SharpConfig.Configuration cfg;
		public static string datapath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Enmoku", "Taskmaster");

		// TODO: Pre-allocate space for the log files.

		static Dictionary<string, SharpConfig.Configuration> Configs = new Dictionary<string, SharpConfig.Configuration>();
		static Dictionary<SharpConfig.Configuration, bool> ConfigDirty = new Dictionary<SharpConfig.Configuration, bool>();
		static Dictionary<SharpConfig.Configuration, string> ConfigPaths = new Dictionary<SharpConfig.Configuration, string>();

		public static void saveConfig(SharpConfig.Configuration config)
		{
			string filename;
			if (ConfigPaths.TryGetValue(config, out filename))
			{
				saveConfig(filename, config);
				return;
			}
			throw new ArgumentException();
		}

		public static void saveConfig(string configfile, SharpConfig.Configuration config)
		{
			//Console.WriteLine("Saving: " + configfile);
			System.IO.Directory.CreateDirectory(datapath);
			string targetfile = System.IO.Path.Combine(datapath, configfile);
			if (System.IO.File.Exists(targetfile))
				System.IO.File.Copy(targetfile, targetfile + ".bak", true); // backup
			config.SaveToFile(targetfile);
			// TODO: Pre-allocate some space for the config file?
		}

		public static SharpConfig.Configuration loadConfig(string configfile)
		{
			SharpConfig.Configuration retcfg;
			if (Configs.TryGetValue(configfile, out retcfg))
			{
				return retcfg;
			}

			string path = System.IO.Path.Combine(datapath, configfile);
			//Log.Trace("Opening: "+path);
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

			if (TaskMaster.Trace) Log.Verbose("{ConfigFile} added to known configurations files.", configfile);

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

		public static void MainWindowClose(object sender, EventArgs e)
		{
			// Calling dispose here for mainwindow is WRONG, DON'T DO IT
			// only do it if ev.Cancel=true, I mean.

			lock (mainwindow_lock)
			{
				mainwindow = null;
			}
		}

		public static bool Restart = false;
		public static void AutomaticRestartRequest(object sender, EventArgs e)
		{
			Restart = true;
			UnifiedExit();
		}

		public static void ConfirmExit(bool restart = false)
		{
			DialogResult rv = DialogResult.Yes;
			if (RequestExitConfirm)
				rv = MessageBox.Show("Are you sure you want to " + (restart ? "restart" : "exit") + " Taskmaster?",
									 (restart ? "Restart" : "Exit") + Application.ProductName + " ???",
									 MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1, MessageBoxOptions.DefaultDesktopOnly, false);

			if (rv != DialogResult.Yes)
				return;

			Restart = restart;

			UnifiedExit();
		}

		// User logs outt
		public static void SessionEndExitRequest(object sender, EventArgs e)
		{
			UnifiedExit();
			//CLEANUP:
			//if (TaskMaster.VeryVerbose) Console.WriteLine("END:Core.ExitRequest - Exit hang averted");
		}

		delegate void EmptyFunction();

		public static void UnifiedExit()
		{
			/*
			try
			{
				lock (mainwindow_lock)
				{
					if (mainwindow != null)
					{
						//mainwindow.BeginInvoke(new MethodInvoker(mainwindow.Close));
						//mainwindow.Close(); // causes crashes relating to .Dispose()
						//mainwindow = null;
					}
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			*/

			Application.Exit(); // if this throws, it deserves to break everything
		}

		/// <summary>
		/// Call any supporting functions to re-evaluate current situation.
		/// </summary>
		public static async Task Evaluate()
		{
			await Task.Yield();

			processmanager.ProcessEverythingRequest(null, null);
		}

		public static void ShowMainWindow()
		{
			BuildMainWindow();
			lock (mainwindow_lock)
			{
				mainwindow.Show();
			}
		}

		public static void BuildMainWindow()
		{
			lock (mainwindow_lock)
			{
				if (mainwindow == null)
				{
					mainwindow = new MainWindow();
					mainwindow.FormClosed += MainWindowClose;

					trayaccess.hookMainWindow(ref mainwindow);

					if (diskmanager != null)
						mainwindow.hookDiskManager(ref diskmanager);

					if (processmanager != null)
						mainwindow.hookProcessManager(ref processmanager);

					if (micmonitor != null)
						mainwindow.hookMicMonitor(micmonitor);

					mainwindow.FillLog();

					if (netmonitor != null)
						mainwindow.hookNetMonitor(ref netmonitor);

					if (activeappmonitor != null)
						mainwindow.hookActiveAppMonitor(ref activeappmonitor);

					if (powermanager != null)
						mainwindow.hookPowerManager(ref powermanager);
				}
			}
		}

		static void Setup()
		{
			Log.Information("<Core> Loading components...");

			trayaccess = new TrayAccess();

			if (PowerManagerEnabled)
			{
				powermanager = new PowerManager();
				trayaccess.hookPowerManager(ref powermanager);
				powermanager.onBatteryResume += AutomaticRestartRequest;
			}

			if (ProcessMonitorEnabled)
				processmanager = new ProcessManager();

			if (MicrophoneMonitorEnabled)
				micmonitor = new MicManager();

			if (NetworkMonitorEnabled)
			{
				netmonitor = new NetManager();
				netmonitor.Tray = trayaccess;
			}

			if (processmanager != null)
				trayaccess.hookProcessManager(ref processmanager);

			if (MaintenanceMonitorEnabled)
				diskmanager = new DiskManager();

			if (ActiveAppMonitorEnabled && ProcessMonitorEnabled)
			{
				activeappmonitor = new ActiveAppManager();
				processmanager.hookActiveAppManager(ref activeappmonitor);
			}

			lock (mainwindow_lock)
			{
				if (ShowOnStart)
				{
					BuildMainWindow();
					mainwindow.Show();
				}
			}

			// Self-optimization
			if (SelfOptimize)
			{
				var self = System.Diagnostics.Process.GetCurrentProcess();
				self.PriorityClass = SelfPriority; // should never throw
				if (SelfAffinity < 0)
				{
					// mask self to the last core
					int selfCPUmask = 1;
					for (int i = 0; i < Environment.ProcessorCount - 1; i++)
						selfCPUmask = (selfCPUmask << 1);
					SelfAffinity = selfCPUmask;
					//Console.WriteLine("Setting own CPU mask to: {0} ({1})", Convert.ToString(selfCPUmask, 2), selfCPUmask);
				}

				self.ProcessorAffinity = new IntPtr(SelfAffinity); // this should never throw an exception

				if (SelfOptimizeBGIO)
				{
					try { ProcessController.SetIOPriority(self, ProcessController.PriorityTypes.PROCESS_MODE_BACKGROUND_BEGIN); }
					catch { Log.Warning("Failed to set self to background mode."); }
				}
			}
		}

		public static bool LogPower = false;

		public static bool DebugProcesses = false;
		public static bool DebugPaths = false;
		public static bool DebugFullScan = false;
		public static bool DebugPower = false;
		public static bool DebugNetMonitor = false;
		public static bool DebugForeground = false;
		public static bool DebugMic = false;

		public static bool CaseSensitive = false;

		public static bool Trace = false;
		public static bool ShowInaction = false;

		public static bool ProcessMonitorEnabled { get; private set; } = true;
		public static bool PathMonitorEnabled { get; private set; } = true;
		public static bool MicrophoneMonitorEnabled { get; private set; } = false;
		//public static bool MediaMonitorEnabled { get; private set; } = true;
		public static bool NetworkMonitorEnabled { get; private set; } = true;
		public static bool PagingEnabled { get; private set; } = true;
		public static bool ActiveAppMonitorEnabled { get; private set; } = true;
		public static bool PowerManagerEnabled { get; private set; } = true;
		public static bool MaintenanceMonitorEnabled { get; private set; } = true;

		public static bool ShowOnStart { get; private set; } = true;

		public static bool SelfOptimize { get; private set; } = true;
		public static ProcessPriorityClass SelfPriority { get; private set; } = ProcessPriorityClass.BelowNormal;
		public static bool SelfOptimizeBGIO { get; private set; } = false;
		public static int SelfAffinity { get; private set; } = -1;

		//public static bool LowMemory { get; private set; } = true; // low memory mode; figure out way to auto-enable this when system is low on memory

		public static int LoopSleep = 0;

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
			bool unused;
			if (ConfigDirty.TryGetValue(dirtiedcfg, out unused))
				ConfigDirty.Remove(dirtiedcfg);
			ConfigDirty.Add(dirtiedcfg, true);
		}

		public static string ConfigVersion = "alpha.1";

		public static bool RequestExitConfirm = true;
		public static bool AutoOpenMenus = true;

		public static bool SaveConfigOnExit = false;

		static string coreconfig = "Core.ini";
		static void LoadCoreConfig()
		{
			Log.Information("<Core> Loading configuration...");

			cfg = loadConfig(coreconfig);

			if (cfg.TryGet("Core")?.TryGet("Hello")?.RawValue != "Hi")
			{
				Log.Information("Hello");
				cfg["Core"]["Hello"].SetValue("Hi");
				cfg["Core"]["Hello"].PreComment = "Heya";
				MarkDirtyINI(cfg);
			}

			SharpConfig.Section compsec = cfg["Components"];
			SharpConfig.Section optsec = cfg["Options"];
			SharpConfig.Section perfsec = cfg["Performance"];

			bool modified = false, dirtyconfig = false;

			int oldsettings = optsec?.SettingCount ?? 0 + compsec?.SettingCount ?? 0 + perfsec?.SettingCount ?? 0;

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
			//MediaMonitorEnabled = compsec.GetSetDefault("Media", true, out modified).BoolValue;
			//compsec["Media"].Comment = "Unused";
			//dirtyconfig |= modified;
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

			var qol = cfg["Quality of Life"];
			RequestExitConfirm = qol.GetSetDefault("Confirm exit", true, out modified).BoolValue;
			dirtyconfig |= modified;
			AutoOpenMenus = qol.GetSetDefault("Auto-open menus", true, out modified).BoolValue;
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

			CaseSensitive = optsec.GetSetDefault("Case sensitive", false, out modified).BoolValue;
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

			int newsettings = optsec?.SettingCount ?? 0 + compsec?.SettingCount ?? 0 + perfsec?.SettingCount ?? 0;

			if (dirtyconfig || (oldsettings != newsettings)) // really unreliable, but meh
				MarkDirtyINI(cfg);

			monitorCleanShutdown();

			Log.Information("<Core> Verbosity: {Verbosity}", MemoryLog.LevelSwitch.MinimumLevel.ToString());
			Log.Information("<Core> Self-optimize: {SelfOptimize}", (SelfOptimize ? "Enabled" : "Disabled"));
			//Log.Information("Low memory mode: {LowMemory}", (LowMemory ? "Enabled." : "Disabled."));
			Log.Information("<<WMI>> Event watcher: {WMIPolling} (Rate: {WMIRate}s)", (WMIPolling ? "Enabled" : "Disabled"), WMIPollDelay);
			Log.Information("<<WMI>> Queries: {WMIQueries}", (WMIQueries ? "Enabled" : "Disabled"));

			// PROTECT USERS FROM TOO HIGH PERMISSIONS
			bool isadmin = IsAdministrator();
			bool adminwarning = ((cfg["Core"].TryGet("Hell")?.StringValue ?? null) != "No");
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

			Log.Information("Privilege level: {Privilege}", isadmin ? "Admin" : "User");

			Log.Information("Path cache: " + (PathCacheLimit == 0 ? "Disabled" : PathCacheLimit + " items"));
		}

		public static bool IsAdministrator()
		{
			// https://stackoverflow.com/a/10905713
			var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
			var principal = new System.Security.Principal.WindowsPrincipal(identity);
			return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
		}

		static SharpConfig.Configuration corestats;
		static string corestatfile = "Core.Statistics.ini";
		static void monitorCleanShutdown()
		{
			if (corestats == null)
				corestats = loadConfig(corestatfile);

			bool running = corestats.TryGet("Core")?.TryGet("Running")?.BoolValue ?? false;
			if (running)
				Log.Warning("Unclean shutdown.");

			corestats["Core"]["Running"].BoolValue = true;
			saveConfig(corestats);
		}

		static void CleanShutdown()
		{
			if (corestats == null) corestats = loadConfig(corestatfile);

			SharpConfig.Section wmi = corestats["WMI queries"];
			string timespent = "Time", querycount = "Queries";
			bool modified = false, dirtyconfig = false;

			wmi[timespent].DoubleValue = wmi.GetSetDefault(timespent, 0d, out modified).DoubleValue + Statistics.WMIquerytime;
			dirtyconfig |= modified;
			wmi[querycount].IntValue = wmi.GetSetDefault(querycount, 0, out modified).IntValue + Statistics.WMIqueries;
			dirtyconfig |= modified;
			SharpConfig.Section ps = corestats["Parent seeking"];
			ps[timespent].DoubleValue = ps.GetSetDefault(timespent, 0d, out modified).DoubleValue + Statistics.Parentseektime;
			dirtyconfig |= modified;
			ps[querycount].IntValue = ps.GetSetDefault(querycount, 0, out modified).IntValue + Statistics.ParentSeeks;
			dirtyconfig |= modified;

			corestats["Core"]["Running"].BoolValue = false;

			saveConfig(corestats);
		}

		static public void Prealloc(string filename, long minkB)
		{
			Debug.Assert(minkB >= 0);

			string path = System.IO.Path.Combine(datapath, filename);
			try
			{
				FileStream fs = System.IO.File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
				long oldsize = fs.Length;
				if (fs.Length < (1024 * minkB))
				{
					// TODO: Make sparse. Unfortunately requires P/Invoke.
					fs.SetLength(1024 * minkB);
					Console.WriteLine("Pre-allocated file: " + filename + " (" + oldsize / 1024 + "kB -> " + minkB + "kB)");
				}
				fs.Close();
			}
			catch (System.IO.FileNotFoundException)
			{
				Console.WriteLine("Failed to open file: " + filename);
			}
		}

		public static bool ComponentConfigurationDone = false;

		public static async void Cleanup(object sender, EventArgs ev)
		{
			Log.Verbose("Running periodic cleanup");

			// TODO: This starts getting weird if cleanup interval is smaller than total delay of testing all items.
			// (15*60) / 2 = item limit, and -1 or -2 for safety margin. Unlikely, but should probably be covered anyway.

			await Task.Yield();

			var time = Stopwatch.StartNew();

			if (processmanager != null)
				await processmanager.Cleanup();

			time.Stop();

			Statistics.Cleanups++;
			Statistics.CleanupTime += time.Elapsed.TotalSeconds;

			Log.Verbose("Cleanup took: {Time}s", string.Format("{0:N2}", time.Elapsed.TotalSeconds));
		}

		public static System.Timers.Timer CleanupTimer;

		public static System.Threading.Mutex singleton = null;

		public static bool SingletonLock()
		{
			if (TaskMaster.Trace) Log.Verbose("Testing for single instance.");

			bool mutexgained = false;
			singleton = new System.Threading.Mutex(true, "088f7210-51b2-4e06-9bd4-93c27a973874.taskmaster", out mutexgained);
			if (!mutexgained)
			{
				// already running, signal original process
				System.Windows.Forms.MessageBox.Show("Already operational.", System.Windows.Forms.Application.ProductName + "!");
				Log.Warning("Exiting (#{ProcessID}); already running.", System.Diagnostics.Process.GetCurrentProcess().Id);
			}
			return mutexgained;
		}

		// entry point to the application
		[STAThread] // supposedly needed to avoid shit happening with the WinForms GUI and other GUI toolkits
		static public int Main(string[] args)
		{
			try
			{
				if (args.Length > 0 && args[0] == "--bootdelay")
				{
					int uptimeMin = 30;
					if (args.Length > 1)
					{
						try
						{
							int nup = Convert.ToInt32(args[1]);
							uptimeMin = nup.Constrain(5, 360);
						}
						catch { /* NOP */ }
					}

					TimeSpan uptime = TimeSpan.Zero;
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
				}

				MemoryLog.LevelSwitch = new LoggingLevelSwitch(LogEventLevel.Information);

				string logpathtemplate = System.IO.Path.Combine(datapath, "Logs", "taskmaster-{Date}.log");
				Serilog.Log.Logger = new Serilog.LoggerConfiguration()
					.MinimumLevel.Verbose()
#if DEBUG
					.WriteTo.Console(levelSwitch: new LoggingLevelSwitch(LogEventLevel.Verbose))
#endif
					.WriteTo.RollingFile(logpathtemplate, outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
										 levelSwitch: new LoggingLevelSwitch(Serilog.Events.LogEventLevel.Debug), retainedFileCountLimit: 3)
					.WriteTo.MemorySink(levelSwitch: MemoryLog.LevelSwitch)
								 .CreateLogger();

				/*
				// Append as used by the logger fucks this up.
				// Unable to mark as sparse file easily.
				Prealloc("Logs/debug.log", 256);
				Prealloc("Logs/error.log", 2);
				Prealloc("Logs/info.log", 32);
				*/

				if (!SingletonLock())
					return -1;

				Log.Information("TaskMaster! (#{ProcessID}) – Version: {Version} – START!",
								Process.GetCurrentProcess().Id, System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);

				{ // INITIAL CONFIGURATIONN
					var tcfg = loadConfig("Core.ini");
					string sec = tcfg.TryGet("Core")?.TryGet("Version")?.StringValue ?? null;
					if (sec == null || sec != ConfigVersion)
					{
						try
						{
							var initialconfig = new InitialConfigurationWindow();
							initialconfig.Show();
							System.Windows.Forms.Application.Run(initialconfig);
							initialconfig.Dispose();
							initialconfig = null;
						}
						catch (Exception ex)
						{
							Logging.Stacktrace(ex);
							throw;
						}

						if (ComponentConfigurationDone == false)
						{
							//singleton.ReleaseMutex();
							Log.CloseAndFlush();
							return 4;
						}
					}
					tcfg = null;
					sec = null;
				}

				LoadCoreConfig();

				try
				{
					Setup();
				}
				catch (Exception ex) // this seems to happen only when Avast cybersecurity is scanning TM
				{
					Log.Fatal("Exiting due to initialization failure.");
					Logging.Stacktrace(ex);
					//singleton.ReleaseMutex();
					Log.CloseAndFlush();
					return 1;
				}

				// IS THIS OF ANY USE?
				//GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
				//GC.WaitForPendingFinalizers();

				// early save of configs
				foreach (var dcfg in ConfigDirty)
					if (dcfg.Value) saveConfig(dcfg.Key);
				ConfigDirty.Clear();

				CleanupTimer = new System.Timers.Timer();
				CleanupTimer.Interval = 1000 * 60 * CleanupInterval; // 15 minutes
				CleanupTimer.Elapsed += TaskMaster.Cleanup;
				CleanupTimer.Start();

				Log.Information("<Core> Initialization complete...");

				if (TaskMaster.ProcessMonitorEnabled)
				{
					Task.Run(new Func<Task>(async () =>
					{
						await Evaluate();
					}));
				}

				try
				{
					trayaccess.Enable();
					System.Windows.Forms.Application.Run(); // WinForms
															//System.Windows.Application.Current.Run(); // WPF
				}
				catch (Exception ex)
				{
					Log.Fatal("Unhandled exception! Dying.");
					Logging.Stacktrace(ex);
					// TODO: ACTUALLY DIE
				}

				try
				{
					if (SelfOptimize)
					{
						var self = Process.GetCurrentProcess();
						self.PriorityClass = ProcessPriorityClass.Normal;
						try
						{
							ProcessController.SetIOPriority(self, ProcessController.PriorityTypes.PROCESS_MODE_BACKGROUND_END);
						}
						catch
						{
						}
					}

					Log.Information("Exiting...");

					// TODO: Save Config
					if (TaskMaster.SaveConfigOnExit)
					{
						cfg["Quality of Life"]["Auto-open menus"].BoolValue = AutoOpenMenus;
						MarkDirtyINI(cfg);
					}

					// CLEANUP for exit

					try
					{
						if (mainwindow != null)
						{
							mainwindow.FormClosed -= MainWindowClose;
							if (!mainwindow.IsDisposed)
								mainwindow.Dispose();
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
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
					//throw; // throwing is kinda pointless at this point
				}

				Log.Information("WMI queries: {QueryTime}s [{QueryCount}]", string.Format("{0:N2}", Statistics.WMIquerytime), Statistics.WMIqueries);
				Log.Information("Cleanups: {CleanupTime}s [{CleanupCount}]", string.Format("{0:N2}", Statistics.CleanupTime), Statistics.Cleanups);

				foreach (var dcfg in ConfigDirty)
					if (dcfg.Value) saveConfig(dcfg.Key);

				CleanShutdown();

				singleton.ReleaseMutex();

				Log.Information("TaskMaster! (#{ProcessID}) END! [Clean]", System.Diagnostics.Process.GetCurrentProcess().Id);

				if (Restart) // happens only on power resume (waking from hibernation) or when manually set
				{
					// for some reason .ReleaseMutex() was not enough?
					singleton.Close();
					singleton.Dispose();
					singleton = null;

					Log.CloseAndFlush();

					Restart = false; // poinless probably
					ProcessStartInfo info = Process.GetCurrentProcess().StartInfo;
					info.FileName = Process.GetCurrentProcess().ProcessName;
					Process.Start(info);
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				//throw; // no point
			}
			finally // no catch, because this is only to make sure the log is written
			{
				if (singleton != null)
					singleton.Close();
				Log.CloseAndFlush();
			}

			return 0;
		}
	}
}