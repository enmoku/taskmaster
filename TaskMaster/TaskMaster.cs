//
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
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.


/*
 * TODO: Fix process IO priority
 * TODO: Detect full screen or GPU accelerated apps and adjust their priorities.
 * TODO: Detect if the above apps hang and lower their processing priorities.
 * 
 * MAYBE:
 *  - Monitor [MFT] fragmentation?
 *  - Detect which apps are making noise?
 *  - Detect high disk usage
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

namespace TaskMaster
{
	using System;

	//[Guid("088f7210-51b2-4e06-9bd4-93c27a973874")]//there's no point to this, is there?
	public class TaskMaster
	{
		public static SharpConfig.Configuration cfg;
		public static string cfgpath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Taskmaster");

		static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();
		static MemLog memlog;

		public static void saveConfig(string configfile, SharpConfig.Configuration config)
		{
			//Console.WriteLine("Saving: " + configfile);
			System.IO.Directory.CreateDirectory(cfgpath);
			string targetfile = System.IO.Path.Combine(cfgpath, configfile);
			if (System.IO.File.Exists(targetfile))
				System.IO.File.Copy(targetfile, targetfile + ".bak", true); // backup
			config.SaveToFile(targetfile);
		}

		public static SharpConfig.Configuration loadConfig(string configfile)
		{
			string path = System.IO.Path.Combine(cfgpath, configfile);
			//Log.Trace("Opening: "+path);
			SharpConfig.Configuration retcfg;
			if (System.IO.File.Exists(path))
				retcfg = SharpConfig.Configuration.LoadFromFile(path);
		    else
			{
				Log.Warn("Not found: " + path);
				retcfg = new SharpConfig.Configuration();
				System.IO.Directory.CreateDirectory(cfgpath);
			}

			return retcfg;
		}

		public static MicMonitor mon;
		public static MainWindow tmw;
		public static ProcessManager pmn;
		public static TrayAccess tri;

		public static void MainWindowClose(object sender, EventArgs e)
		{
			if (LowMemory)
			{
				//tmw.FormClosing -= MainWindowClose // unnecessary?
				tmw.Dispose();
				tmw = null;
			}
		}

		public static void ExitRequest(object sender, EventArgs e)
		{
			//CLEANUP: Console.WriteLine("START:Core.ExitRequest - Exit hang expected");
			System.Windows.Forms.Application.Exit();
			//CLEANUP: Console.WriteLine("END:Core.ExitRequest - Exit hang averted");
		}

		public static void HookMainWindow()
		{
			TrayAccess.onExit += tmw.ExitRequest;

			tri.RegisterMain(ref tmw);

			if (MicrophoneMonitorEnabled)
				tmw.setMicMonitor(mon);

			if (ProcessMonitorEnabled)
				tmw.setProcControl(pmn);
			
			tmw.setLog(memlog);
			tmw.Tray = tri;
			memlog.OnNewLog += tmw.onNewLog;

			tmw.FormClosing += MainWindowClose;

			/*
			GameMonitor gmmon = new GameMonitor();
			pmn.onAdjust += gmmon.SetupEventHookEvent;
			gmmon.ActiveChanged += tmw.OnActiveWindowChanged;
			*/
		}

		public static void BuildMain()
		{
			if (tmw == null)
			{
				tmw = new MainWindow();
				HookMainWindow();
			}
		}
		static void Setup()
		{
			tri = new TrayAccess();

			if (MicrophoneMonitorEnabled)
				mon = new MicMonitor();
			
			if (ProcessMonitorEnabled)
				pmn = new ProcessManager();
			
			BuildMain();
			#if DEBUG
			tmw.Show();
			#endif

			TrayAccess.onExit += ExitRequest;

			// Self-optimization
			if (SelfOptimize)
			{
				var self = System.Diagnostics.Process.GetCurrentProcess();
				self.PriorityClass = System.Diagnostics.ProcessPriorityClass.Idle;
				//self.ProcessorAffinity = 1; // run this only on the first processor/core; not needed, this app is not time critical
			}
		}

		public static bool Verbose = true;
		public static bool VeryVerbose = false;
		public static bool CaseSensitive = false;

		static bool ProcessMonitorEnabled = true;
		static bool MicrophoneMonitorEnabled = true;
		static bool MediaMonitorEnabled = true;
		static bool _NetworkMonitorEnabled = true;
		public static bool NetworkMonitorEnabled
		{
			get { return _NetworkMonitorEnabled; }
			private set { _NetworkMonitorEnabled = value; }
		}
			
		static bool _SelfOptimize = true;
		public static bool SelfOptimize
		{
			get { return _SelfOptimize; }
			private set { _SelfOptimize = value; }
		}

		static bool lowmemory = false; // low memory mode; figure out way to auto-enable this when system is low on memory
		public static bool LowMemory { get { return lowmemory; } private set { lowmemory = value; } }

		static bool wmiqueries = true;
		/// <summary>
		/// Whether to use WMI queries for investigating failed path checks to determine if an application was launched in watched path.
		/// </summary>
		/// <value><c>true</c> if WMI queries are enabled; otherwise, <c>false</c>.</value>
		public static bool WMIqueries { get { return wmiqueries; } private set { wmiqueries = value; } }

		static string coreconfig = "Core.ini";
		static void LoadCoreConfig()
		{
			cfg = loadConfig(coreconfig);

			cfg["Core"]["Hello"].SetValue("Hi");

			bool coreconfigdirty = false;

			SharpConfig.Section compsec = cfg["Components"];
			SharpConfig.Section optsec = cfg["Options"];
			SharpConfig.Section perfsec = cfg["Performance"];

			int oldsettings = optsec?.SettingCount ?? 0 + compsec?.SettingCount ?? 0 + perfsec?.SettingCount ?? 0;

			ProcessMonitorEnabled = compsec.GetSetDefault("Process", true).BoolValue;
			MicrophoneMonitorEnabled = compsec.GetSetDefault("Microphone", true).BoolValue;
			MediaMonitorEnabled = compsec.GetSetDefault("Media", true).BoolValue;
			NetworkMonitorEnabled = compsec.GetSetDefault("Network", true).BoolValue;

			Verbose = optsec.GetSetDefault("Verbose", false).BoolValue;
			VeryVerbose = optsec.GetSetDefault("Very verbose", false).BoolValue;
			CaseSensitive = optsec.GetSetDefault("Case sensitive", false).BoolValue;

			SelfOptimize = perfsec.GetSetDefault("Self-optimize", true).BoolValue;
			LowMemory = perfsec.GetSetDefault("Low memory", false).BoolValue;
			WMIqueries = perfsec.GetSetDefault("WMI queries", false).BoolValue;
			perfsec.GetSetDefault("Child processes", false); // unused here

			int newsettings = optsec?.SettingCount ?? 0 + compsec?.SettingCount ?? 0 + perfsec?.SettingCount ?? 0;

			if (coreconfigdirty || (oldsettings!=newsettings)) // really unreliable, but meh
				saveConfig(coreconfig, cfg);

			monitorCleanShutdown();

			Verbose |= VeryVerbose;

			Log.Info("Verbosity: " + (VeryVerbose ? "Extreme" : (Verbose ? "High" : "Normal")));
			Log.Info("Self-optimize: " + (SelfOptimize ? "Enabled." : "Disabled."));
			Log.Info("Low memory mode: " + (LowMemory ? "Enabled." : "Disabled."));
			Log.Info("WMI queries: " + (WMIqueries ? "Enabled." : "Disabled."));
		}

		static SharpConfig.Configuration corestats;
		static string corestatfile = "Core.Statistics.ini";
		static void monitorCleanShutdown()
		{
			if (corestats == null)
				corestats = loadConfig(corestatfile);
			
			bool running = corestats.TryGet("Core")?.TryGet("Running")?.BoolValue ?? false;
			if (running)
				Log.Warn("Unclean shutdown.");
			
			corestats["Core"]["Running"].BoolValue = true;
			saveConfig(corestatfile, corestats);
		}

		static void CleanShutdown()
		{
			if (corestats == null)
				corestats = loadConfig(corestatfile);

			SharpConfig.Section wmi = corestats["WMI queries"];
			string timespent = "Time", querycount = "Queries";
			wmi[timespent].DoubleValue = wmi.GetSetDefault(timespent, 0d).DoubleValue + Statistics.WMIquerytime;
			wmi[querycount].IntValue = wmi.GetSetDefault(querycount, 0).IntValue + Statistics.WMIqueries;
			SharpConfig.Section ps = corestats["Parent seeking"];
			ps[timespent].DoubleValue = ps.GetSetDefault(timespent, 0d).DoubleValue + Statistics.Parentseektime;
			ps[querycount].IntValue = ps.GetSetDefault(querycount, 0).IntValue + Statistics.ParentSeeks;

			corestats["Core"]["Running"].BoolValue = false;

			saveConfig(corestatfile, corestats);
		}

		static public void CrossInstanceMessageHandler(object sender, EventArgs e)
		{
			Log.Info("Cross-instance message! Pid:" + System.Diagnostics.Process.GetCurrentProcess().Id);
		}

		// entry point to the application
		[STAThread] // supposedly needed to avoid shit happening with the WinForms GUI
		static public void Main(string[] args)
		{
			if (args.Length == 1 && args[0] == "-delay")
			{
				Console.WriteLine("Delaying proper startup for 30 seconds.");
				System.Threading.Thread.Sleep(30 * 1000);
			}

			memlog = new MemLog();
			NLog.LogManager.ThrowExceptions = true;
			NLog.LogManager.Configuration.AddTarget("MemLog", memlog);
			NLog.LogManager.Configuration.LoggingRules.Add(new NLog.Config.LoggingRule("*", NLog.LogLevel.Debug, memlog));
			NLog.LogManager.ReconfigExistingLoggers(); // better than reload since we didn't modify the files

			#region SINGLETON
			if (VeryVerbose) Log.Trace("Testing for single instance.");

			System.Threading.Mutex singleton = null;
			{
				bool mutexgained = false;
				singleton = new System.Threading.Mutex(true, "088f7210-51b2-4e06-9bd4-93c27a973874.taskmaster", out mutexgained);
				if (!mutexgained)
				{
					// already running
					// signal original process
					System.Windows.Forms.MessageBox.Show("Already operational.", System.Windows.Forms.Application.ProductName + "!");
					Log.Warn(string.Format("Exiting (pid:{0}); already running.", System.Diagnostics.Process.GetCurrentProcess().Id));
					return;
				}
			}
			#endregion

			Log.Info(string.Format("TaskMaster! (#{0}) START!", System.Diagnostics.Process.GetCurrentProcess().Id));

			LoadCoreConfig();

			Setup();

			Log.Info("Startup complete...");

			pmn.ProcessEverything();

			System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
			GC.Collect(5, GCCollectionMode.Forced, true, true);

			NLog.LogManager.Flush();
			try
			{
				System.Windows.Forms.Application.Run();
			}
			catch (Exception ex)
			{
				Log.Fatal("Unhandled exception! Dying." + Environment.NewLine + ex);
				throw;
			}
			finally
			{
				Log.Info("Exiting...");
				pmn.Dispose();
				mon.Dispose();
				tri.Dispose();
				singleton.ReleaseMutex();

				Log.Info(string.Format("WMI queries: {0:N1}s [{1}]; Parent seeking: {2:N1}s [{3}]",
				                       Statistics.WMIquerytime, Statistics.WMIqueries,
				                       Statistics.Parentseektime, Statistics.ParentSeeks));
			}

			//tmw.Dispose();//already disposed by App.Exit?

			CleanShutdown();

			Log.Info(string.Format("TaskMaster! (#{0}) END! [Clean]", System.Diagnostics.Process.GetCurrentProcess().Id));
			//NLog.LogManager.Flush(); // 
		}
	}
}