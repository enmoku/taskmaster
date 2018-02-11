﻿//
// TaskMaster.cs
//
// Author:
//       M.A. (enmoku)
//
// Copyright (c) 2016 M.A. (enmoku)
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
using System.Linq;
using System.Runtime.CompilerServices;

namespace TaskMaster
{
	//[Guid("088f7210-51b2-4e06-9bd4-93c27a973874")]//there's no point to this, is there?
	public class TaskMaster
	{
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
			Log.Debug("{ConfigFile} added to known configurations files.", configfile);

			return retcfg;
		}

		public static MicMonitor micmonitor = null;
		public static MainWindow mainwindow = null;
		public static ProcessManager processmanager = null;
		public static TrayAccess trayaccess = null;
		public static NetMonitor netmonitor = null;
		public static DiskManager diskmanager = null;
		public static PowerManager powermanager = null;
		public static ActiveAppMonitor activeappmonitor = null;

		public static void MainWindowClose(object sender, EventArgs e)
		{
			//if (LowMemory)
			//{
			//tmw.FormCling -= MainWindowCle // unnecessary?
			mainwindow.Dispose();
			mainwindow = null;
			//}
		}

		public static bool Restart = false;
		public static void RestartRequest(object sender, EventArgs e)
		{
			Restart = true;
			ExitRequest(null, null);
		}

		public static void ExitRequest(object sender, EventArgs e)
		{
			//CLEANUP:
			//if (TaskMaster.VeryVerbose) Console.WriteLine("START:Core.ExitRequest - Exit hang expected");
			mainwindow?.Close();
			//mainwindow?.Dispose();
			//mainwindow = null;

			System.Windows.Forms.Application.Exit();
			//CLEANUP:
			//if (TaskMaster.VeryVerbose) Console.WriteLine("END:Core.ExitRequest - Exit hang averted");
		}

		public static void BuildMain()
		{
			if (mainwindow == null)
			{
				mainwindow = new MainWindow();
				TrayAccess.onExit += mainwindow.ExitRequest;

				trayaccess.RegisterMain(ref mainwindow);

				if (MicrophoneMonitorEnabled)
					mainwindow.setMicMonitor(micmonitor);

				if (ProcessMonitorEnabled)
					mainwindow.setProcControl(processmanager);

				mainwindow.Tray = trayaccess;

				mainwindow.FillLog();
				MemoryLog.onNewEvent += mainwindow.onNewLog;

				mainwindow.FormClosing += MainWindowClose;

				if (ProcessMonitorEnabled)
				{
					mainwindow.rescanRequest += processmanager.ProcessEverythingRequest;
					if (PagingEnabled)
						mainwindow.pagingRequest += processmanager.PageEverythingRequest;
				}

				if (NetworkMonitorEnabled)
					mainwindow.setNetMonitor(ref netmonitor);

				if (ActiveAppMonitorEnabled)
				{
					activeappmonitor = new ActiveAppMonitor();
					//pmn.onAdjust += gmmon.SetupEventHookEvent; //??
					activeappmonitor.ActiveChanged += mainwindow.OnActiveWindowChanged;
				}

				if (PowerManagerEnabled)
					powermanager.onResume += RestartRequest;
			}
		}
		static void Setup()
		{
			trayaccess = new TrayAccess();
			TrayAccess.onExit += ExitRequest;

			if (MicrophoneMonitorEnabled)
				micmonitor = new MicMonitor();

			if (ProcessMonitorEnabled)
				processmanager = new ProcessManager();

			if (NetworkMonitorEnabled)
			{
				netmonitor = new NetMonitor();
				netmonitor.Tray = trayaccess;
			}

			if (PowerManagerEnabled) powermanager = new PowerManager();

			BuildMain();

			if (ShowOnStart)
				mainwindow.Show();

			// Self-optimization
			if (SelfOptimize)
			{
				var self = System.Diagnostics.Process.GetCurrentProcess();
				self.PriorityClass = System.Diagnostics.ProcessPriorityClass.Idle; // should never throw
																				   // mask self to the last core
				if (SelfAffinity < 0)
				{
					int selfCPUmask = 1;
					for (int i = 0; i < Environment.ProcessorCount - 1; i++)
						selfCPUmask = (selfCPUmask << 1);
					SelfAffinity = selfCPUmask;
					//Console.WriteLine("Setting own CPU mask to: {0} ({1})", Convert.ToString(selfCPUmask, 2), selfCPUmask);
				}

				self.ProcessorAffinity = new IntPtr(SelfAffinity); // this should never throw an exception
			}

			if (MaintenanceMonitorEnabled)
				diskmanager = new DiskManager();
		}

		public static bool CaseSensitive = false;

		public static bool ProcessMonitorEnabled { get; private set; } = true;
		public static bool PathMonitorEnabled { get; private set; } = true;
		public static bool MicrophoneMonitorEnabled { get; private set; } = false;
		public static bool MediaMonitorEnabled { get; private set; } = true;
		public static bool NetworkMonitorEnabled { get; private set; } = true;
		public static bool PagingEnabled { get; private set; } = true;
		public static bool ActiveAppMonitorEnabled { get; private set; } = true;
		public static bool PowerManagerEnabled { get; private set; } = true;
		public static bool MaintenanceMonitorEnabled { get; private set; } = true;

		public static bool ShowOnStart { get; private set; } = true;

		public static bool SelfOptimize { get; private set; } = true;
		public static int SelfAffinity { get; private set; } = -1;

		//public static bool LowMemory { get; private set; } = true; // low memory mode; figure out way to auto-enable this when system is low on memory

		public static int LoopSleep = 0;

		public static int TempRescanDelay = 60 * 60 * 1000;
		public static int TempRescanThreshold = 1000;

		public static int PathCacheLimit = 200;

		/// <summary>
		/// Whether to use WMI queries for investigating failed path checks to determine if an application was launched in watched path.
		/// </summary>
		/// <value><c>true</c> if WMI queries are enabled; otherwise, <c>false</c>.</value>
		public static bool WMIQueries { get; private set; }

		public static void MarkDirtyINI(SharpConfig.Configuration dirtiedcfg)
		{
			bool unused;
			if (ConfigDirty.TryGetValue(dirtiedcfg, out unused))
				ConfigDirty.Remove(dirtiedcfg);
			ConfigDirty.Add(dirtiedcfg, true);
		}

		public static string ConfigVersion = "alpha.1";

		static string coreconfig = "Core.ini";
		static void LoadCoreConfig()
		{
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
			MediaMonitorEnabled = compsec.GetSetDefault("Media", true, out modified).BoolValue;
			compsec["Media"].Comment = "Unused";
			dirtyconfig |= modified;
			ActiveAppMonitorEnabled = compsec.GetSetDefault("Foreground", true, out modified).BoolValue;
			compsec["Foreground"].Comment = "Game/Foreground app monitoring and adjustment.";
			dirtyconfig |= modified;
			NetworkMonitorEnabled = compsec.GetSetDefault("Network", true, out modified).BoolValue;
			compsec["Network"].Comment = "Monitor network uptime and current IP addresses.";
			dirtyconfig |= modified;
			MaintenanceMonitorEnabled = compsec.GetSetDefault("Power", true, out modified).BoolValue;
			compsec["Power"].Comment = "Enable power plan management.";
			dirtyconfig |= modified;
			PagingEnabled = compsec.GetSetDefault("Paging", true, out modified).BoolValue;
			compsec["Paging"].Comment = "Enable paging of apps as per their configuration.";
			dirtyconfig |= modified;
			MaintenanceMonitorEnabled = compsec.GetSetDefault("Maintenance", false, out modified).BoolValue;
			compsec["Maintenance"].Comment = "Enable basic maintenance monitoring functionality.";
			dirtyconfig |= modified;

			var Verbosity = optsec.GetSetDefault("Verbosity", 0, out modified).IntValue;
			optsec["Verbosity"].Comment = "0 = Information, 1 = Debug, 2 = Verbose/Trace";
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
			};
			dirtyconfig |= modified;

			CaseSensitive = optsec.GetSetDefault("Case sensitive", false, out modified).BoolValue;
			dirtyconfig |= modified;
			ShowOnStart = optsec.GetSetDefault("Show on start", true, out modified).BoolValue;
			dirtyconfig |= modified;

			// [Performance]
			SelfOptimize = perfsec.GetSetDefault("Self-optimize", true, out modified).BoolValue;
			dirtyconfig |= modified;
			SelfAffinity = perfsec.GetSetDefault("Self-affinity", -1, out modified).IntValue;
			perfsec["Self-affinity"].Comment = "Core mask as integer. 0 is for default OS control. -1 is for last core. Limiting to single core recommended.";
			dirtyconfig |= modified;

			WMIQueries = perfsec.GetSetDefault("WMI queries", true, out modified).BoolValue;
			perfsec["WMI queries"].Comment = "WMI is considered buggy and slow. Unfortunately necessary for some functionality.";
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
			perfsec["Path cache"].Comment = "Path searching is very heavy process; this configures how many processes to remember paths for.";
			dirtyconfig |= modified;

			int newsettings = optsec?.SettingCount ?? 0 + compsec?.SettingCount ?? 0 + perfsec?.SettingCount ?? 0;

			if (dirtyconfig || (oldsettings != newsettings)) // really unreliable, but meh
				MarkDirtyINI(cfg);

			monitorCleanShutdown();

			//SharpConfig.Section logsec = cfg["Logging"];

			Log.Information("Verbosity: {Verbosity}", MemoryLog.LevelSwitch.MinimumLevel.ToString());
			Log.Information("Self-optimize: {SelfOptimize}", (SelfOptimize ? "Enabled" : "Disabled"));
			//Log.Information("Low memory mode: {LowMemory}", (LowMemory ? "Enabled." : "Disabled."));
			Log.Information("WMI queries: {WMIQueries}", (WMIQueries ? "Enabled" : "Disabled"));

			Log.Information("Privilege level: {Privilege}", (IsAdministrator() ? "Admin" : "User"));
		}

		static bool IsAdministrator()
		{
			// https://stackoverflow.com/a/10905713
			System.Security.Principal.WindowsIdentity identity = System.Security.Principal.WindowsIdentity.GetCurrent();
			System.Security.Principal.WindowsPrincipal principal = new System.Security.Principal.WindowsPrincipal(identity);
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

		// entry point to the application
		[STAThread] // supposedly needed to avoid shit happening with the WinForms GUI
		static public void Main(string[] args)
		{
			if (args.Length == 1 && args[0] == "-bootdelay")
			{
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

				if (uptime.TotalSeconds < 30)
				{
					Console.WriteLine("Delaying proper startup for 30 seconds.");
					System.Threading.Thread.Sleep(Convert.ToInt32(System.Math.Max(0, 30 - uptime.TotalSeconds)) * 1000);
				}
			}

			MemoryLog.LevelSwitch = new LoggingLevelSwitch(LogEventLevel.Information);

			string logpathtemplate = System.IO.Path.Combine(datapath, "Logs", "serilog-{Date}.log");
			Serilog.Log.Logger = new Serilog.LoggerConfiguration()
				.MinimumLevel.Verbose()
				.WriteTo.Console(levelSwitch: new LoggingLevelSwitch(LogEventLevel.Verbose))
				.WriteTo.RollingFile(logpathtemplate, outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}", levelSwitch: new LoggingLevelSwitch(Serilog.Events.LogEventLevel.Debug), retainedFileCountLimit: 7)
				.WriteTo.MemorySink(levelSwitch: MemoryLog.LevelSwitch)
							 .CreateLogger();

			/*
			// Append as used by the logger fucks this up.
			// Unable to mark as sparse file easily.
			Prealloc("Logs/debug.log", 256);
			Prealloc("Logs/error.log", 2);
			Prealloc("Logs/info.log", 32);
			*/

			#region SINGLETON
			Log.Verbose("Testing for single instance.");

			System.Threading.Mutex singleton = null;
			{
				bool mutexgained = false;
				singleton = new System.Threading.Mutex(true, "088f7210-51b2-4e06-9bd4-93c27a973874.taskmaster", out mutexgained);
				if (!mutexgained)
				{
					// already running, signal original process
					System.Windows.Forms.MessageBox.Show("Already operational.", System.Windows.Forms.Application.ProductName + "!");
					Log.Warning("Exiting (#{ProcessID}); already running.", System.Diagnostics.Process.GetCurrentProcess().Id);
					return;
				}
			}
			#endregion

			Log.Information("TaskMaster! (#{ProcessID}) START!", System.Diagnostics.Process.GetCurrentProcess().Id);

			{
				var tcfg = loadConfig("Core.ini");
				string sec = tcfg.TryGet("Core")?.TryGet("Version")?.StringValue ?? null;
				if (sec == null || sec != ConfigVersion)
				{
					try
					{
						var iconf = new InitialConfigurationWindow();
						System.Windows.Forms.Application.Run(iconf);
						iconf.Dispose();
						iconf = null;
					}
					finally
					{
					}

					if (ComponentConfigurationDone == false)
					{
						singleton.ReleaseMutex();
						return;
					}
				}
				tcfg = null;
				sec = null;
			}

			LoadCoreConfig();

			Setup();

			// IS THIS OF ANY USE?
			GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
			GC.WaitForPendingFinalizers();

			Log.Information("Startup complete...");

			if (TaskMaster.ProcessMonitorEnabled)
				processmanager.ProcessEverything();

			//System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
			//GC.Collect(5, GCCollectionMode.Forced, true, true);

			try
			{
				System.Windows.Forms.Application.Run(); // WinForms
														//System.Windows.Application.Current.Run(); // WPF
			}
			catch (Exception ex)
			{
				Log.Fatal("Unhandled exception! Dying.");
				Log.Fatal(ex.StackTrace);
			}
			finally
			{
				Log.Information("Exiting...");

				if (mainwindow != null)
				{
					mainwindow.rescanRequest -= processmanager.ProcessEverythingRequest;
					if (TaskMaster.PagingEnabled)
						mainwindow.pagingRequest -= processmanager.PageEverythingRequest;
					mainwindow.FormClosing -= MainWindowClose;
					MemoryLog.onNewEvent -= mainwindow.onNewLog;
					TrayAccess.onExit -= mainwindow.ExitRequest;
				}

				TrayAccess.onExit -= ExitRequest;

				mainwindow?.Dispose();
				processmanager?.Dispose();
				micmonitor?.Dispose();
				trayaccess?.Dispose();
				netmonitor?.Dispose();
				activeappmonitor?.Dispose();
				powermanager?.Dispose();
				mainwindow = null; processmanager = null; micmonitor = null; trayaccess = null; netmonitor = null; activeappmonitor = null; powermanager = null;

				Log.Information("WMI queries: {QueryTime:1}s [{QueryCount}]; Parent seeking: {ParentSeekTime:1}s [{ParentSeekCount}]",
									   Statistics.WMIquerytime, Statistics.WMIqueries,
									   Statistics.Parentseektime, Statistics.ParentSeeks);

				ProcessManager.PathCacheStats();

				//tmw.Dispose();//already disposed by App.Exit?
				foreach (var dcfg in ConfigDirty)
				{
					if (dcfg.Value) saveConfig(dcfg.Key);
				}

				CleanShutdown();

				singleton.ReleaseMutex();

				Log.Information("TaskMaster! (#{ProcessID}) END! [Clean]", System.Diagnostics.Process.GetCurrentProcess().Id);
				Log.CloseAndFlush();
			}

			if (Restart) // happens only on power resume (waking from hibernation)
			{
				Restart = false; // poinless probably
				ProcessStartInfo info = Process.GetCurrentProcess().StartInfo;
				info.FileName = Process.GetCurrentProcess().ProcessName;
				Process.Start(info);
			}
		}
	}
}