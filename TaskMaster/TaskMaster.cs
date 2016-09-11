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
 * TODO: Empty working set, detect if it does anything and report it. Probably not worth doing.
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

using System;
using System.Security.Policy;
using NAudio.MediaFoundation;

namespace TaskMaster
{
	//[Guid("088f7210-51b2-4e06-9bd4-93c27a973874")]//there's no point to this, is there?
	public class TaskMaster
	{
		public static SharpConfig.Configuration cfg;
		public static string cfgpath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Taskmaster");

		static NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();
		static MemLog memlog;

		public static void saveConfig(string configfile, SharpConfig.Configuration config)
		{
			//Console.WriteLine("Saving: " + configfile);
			System.IO.Directory.CreateDirectory(cfgpath);
			string targetfile = System.IO.Path.Combine(cfgpath, configfile);
			if (System.IO.File.Exists(targetfile))
				System.IO.File.Copy(targetfile, targetfile + ".bak.1", true); // backup
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
			if (tmw.LowMemory)
			{
				//tmw.FormClosing -= MainWindowClose // unnecessary?
				tmw.Enabled = false;
				tmw = null;
			}
		}

		public static void HookMainWindow()
		{
			tri.RegisterMain(ref tmw);

			tmw.setMicMonitor(mon);
			tmw.setProcControl(pmn);
			tmw.setLog(memlog);
			tmw.setTray(tri);
			memlog.OnNewLog += tmw.onNewLog;

			tmw.FormClosing += MainWindowClose;

			/*
			GameMonitor gmmon = new GameMonitor();
			pmn.onAdjust += gmmon.SetupEventHookEvent;
			gmmon.ActiveChanged += tmw.OnActiveWindowChanged;
			*/
		}
		static void Setup()
		{
			mon = new MicMonitor();
			tri = new TrayAccess();
			pmn = new ProcessManager();
			tmw = new MainWindow();
			HookMainWindow();
			#if DEBUG
			tmw.Show();
			#endif

			// Self-optimization
			var self = System.Diagnostics.Process.GetCurrentProcess();
			self.PriorityClass = System.Diagnostics.ProcessPriorityClass.Idle;
			//self.ProcessorAffinity = 1; // run this only on the first processor/core; not needed, this app is not time critical
		}

		static bool ProcessMonitorEnabled = true;
		static bool MicrophoneMonitorEnabled = true;
		static bool MediaMonitorEnabled = true;
		static bool NetworkMonitorEnabled = true;

		static string coreconfig = "Core.ini";
		static bool coreconfigdirty = false;
		static void defaultConfig()
		{
			cfg["Core"]["Hello"].SetValue("Hi");

			if (cfg.Contains("Components"))
			{
				ProcessMonitorEnabled = cfg["Components"]["Process"].BoolValue;
				MicrophoneMonitorEnabled = cfg["Components"]["Microphone"].BoolValue;
				MediaMonitorEnabled = cfg["Components"]["Media"].BoolValue;
				NetworkMonitorEnabled = cfg["Components"]["Network"].BoolValue;
			}
			else
			{
				cfg["Components"]["Process"].BoolValue = true;
				cfg["Components"]["Microphone"].BoolValue = true;
				cfg["Components"]["Media"].BoolValue = true;
				cfg["Components"]["Network"].BoolValue = true;
				coreconfigdirty = true;
			}

			ProcessMonitorEnabled = true;
			MicrophoneMonitorEnabled = true;
			MediaMonitorEnabled = true;
			NetworkMonitorEnabled = true;

			if (!cfg.Contains("Options"))
				cfg["Options"]["Self-optimize"].BoolValue = true;

			monitorCleanShutdown();
		}

		static SharpConfig.Configuration corestats;
		static string corestatfile = "Core.Statistics.ini";
		static void monitorCleanShutdown()
		{
			if (corestats == null)
				corestats = loadConfig(corestatfile);
			if (corestats.Contains("Core") && corestats["Core"].Contains("Running"))
			{
				bool running = corestats["Core"]["Running"].BoolValue;
				if (running)
					Log.Warn("Unclean shutdown.");
			}
			corestats["Core"]["Running"].BoolValue = true;
			saveConfig(corestatfile, corestats);
		}

		static void CleanShutdown()
		{
			if (corestats == null)
				corestats = loadConfig(corestatfile);
			corestats["Core"]["Running"].BoolValue = false;
			saveConfig(corestatfile, corestats);
			if (coreconfigdirty)
				saveConfig(coreconfig, cfg);
		}

		static public void CrossInstanceMessageHandler(object sender, EventArgs e)
		{
			Log.Info("Cross-instance message! Pid:" + System.Diagnostics.Process.GetCurrentProcess().Id);
		}

		// entry point to the application
		//[STAThread] // supposedly needed to avoid shit happening with the WinForms GUI. Haven't noticed any of that shit.
		static public void Main()
		{
			if (memlog == null)
				memlog = new MemLog();
			NLog.LogManager.Configuration.AddTarget("MemLog", memlog);
			NLog.LogManager.Configuration.LoggingRules.Add(new NLog.Config.LoggingRule("*", NLog.LogLevel.Debug, memlog));
			memlog.Layout = @"[${date:format=HH\:mm\:ss.fff}] [${level}] ${message}"; ;
			NLog.LogManager.Configuration.Reload();

			#region SINGLETON
			Log.Trace("Testing for single instance.");
			System.Threading.Mutex singleton = null;
			{
				bool mutexgained = false;
				singleton = new System.Threading.Mutex(true, "088f7210-51b2-4e06-9bd4-93c27a973874.taskmaster", out mutexgained);
				if (!mutexgained)
				{
					// already running
					// signal original process
					System.Windows.Forms.MessageBox.Show("Already operational.", System.Windows.Forms.Application.ProductName + "!");
					Log.Warn(System.String.Format("Exiting (pid:{0}); already running.", System.Diagnostics.Process.GetCurrentProcess().Id));
					return;
				}
			}
			#endregion

			Log.Info(System.String.Format("{0} (#{1}) START!", System.Windows.Forms.Application.ProductName, System.Diagnostics.Process.GetCurrentProcess().Id));

			cfg = loadConfig(coreconfig);
			defaultConfig();

			TaskMaster.Setup();

			Log.Info("Startup complete...");

			pmn.ProcessEverything();
			try
			{
				System.Windows.Forms.Application.Run();
			}
			catch (Exception ex)
			{
				Log.Error("Unhandled exception! Dying.");
				Log.Fatal("Unhandled exception! Dying." + System.Environment.NewLine + ex);
				NLog.LogManager.Flush();
				throw;
			}
			finally
			{
				Log.Info("Exiting...");
				if (mon != null)
				{
					mon.Dispose();
					mon = null;
				}
				if (tmw != null)
				{
					//tmw.Dispose();
					tmw = null;
				}
				if (pmn != null)
				{
					pmn.Dispose();
					pmn = null;
				}
				if (tri != null)
				{
					tri.Dispose();
					tri = null;
				}
			}

			singleton.ReleaseMutex();
			CleanShutdown();
			Log.Info(System.String.Format("{0} (#{1}) END! [Clean]", System.Windows.Forms.Application.ProductName, System.Diagnostics.Process.GetCurrentProcess().Id));
		}
	}
}