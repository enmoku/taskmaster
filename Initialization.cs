//
// IPC.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2019 M.A.
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

using MKAh;
using Serilog;
using Serilog.Events;
using System;
using System.Threading.Tasks;

namespace Taskmaster
{
	public static partial class Taskmaster
	{
		public const string CoreConfigFilename = "Core.ini";

		static void InitialConfiguration()
		{
			// INITIAL CONFIGURATIONN
			using (var tcfg = Config.Load(CoreConfigFilename).BlockUnload())
			{
				var sec = tcfg.Config.Get(Constants.Core)?.Get(Constants.Version)?.Value ?? null;
				if (sec == null || sec != ConfigVersion)
				{
					using (var initialconfig = new UI.Config.ComponentConfigurationWindow())
					{
						initialconfig.ShowDialog();
						if (!initialconfig.DialogOK)
							throw new InitFailure("Component configuration cancelled");
					}
				}
			}
		}

		static void LoadCoreConfig()
		{
			if (Trace) Log.Debug("<Core> Loading configuration...");

			bool isadmin = false;

			using (var corecfg = Config.Load(CoreConfigFilename).BlockUnload())
			{
				var cfg = corecfg.Config;

				const string Hello = "Hello", Hi = "Hi";

				if (cfg.TryGet(Constants.Core, out var core) && core.TryGet(Hello, out var hello) && hello.Value.Equals(Hi))
				{ }
				else
				{
					cfg[Constants.Core][Hello].Value = Hi;
					corecfg.MarkDirty();
				}

				var compsec = cfg[Constants.Components];
				var optsec = cfg[Constants.Options];
				var perfsec = cfg[Constants.Performance];

				bool modified = false, dirtyconfig = false;
				cfg[Constants.Core].GetOrSet(Constants.License, Constants.Refused, out modified).Value = Constants.Accepted;
				dirtyconfig |= modified;

				// [Components]
				var processmonitorenabled_t = compsec.GetOrSet(HumanReadable.System.Process.Section, true, out modified);
				ProcessMonitorEnabled = processmonitorenabled_t.BoolValue;
				processmonitorenabled_t.Comment = "Monitor starting processes based on their name. Configure in Apps.ini";
				dirtyconfig |= modified;

				if (!ProcessMonitorEnabled)
				{
					Log.Warning("<Core> Process monitor disabled: state not supported, forcing enabled.");
					ProcessMonitorEnabled = true;
				}

				var audiomanagerenabled_t = compsec.GetOrSet(HumanReadable.Hardware.Audio.Section, true, out modified);
				AudioManagerEnabled = audiomanagerenabled_t.BoolValue;
				audiomanagerenabled_t.Comment = "Monitor audio sessions and set their volume as per user configuration.";
				dirtyconfig |= modified;
				MicrophoneManagerEnabled = compsec.GetOrSet("Microphone", false, out modified).BoolValue;
				compsec[Constants.Microphone].Comment = "Monitor and force-keep microphone volume.";
				dirtyconfig |= modified;

				if (!AudioManagerEnabled && MicrophoneManagerEnabled)
				{
					Log.Warning("<Core> Audio manager disabled, disabling microphone manager.");
					MicrophoneManagerEnabled = false;
				}

				// MediaMonitorEnabled = compsec.GetSetDefault("Media", true, out modified).BoolValue;
				// compsec["Media"].Comment = "Unused";
				// dirtyconfig |= modified;
				ActiveAppMonitorEnabled = compsec.GetOrSet(HumanReadable.System.Process.Foreground, true, out modified).BoolValue;
				compsec[HumanReadable.System.Process.Foreground].Comment = "Game/Foreground app monitoring and adjustment.";
				dirtyconfig |= modified;
				NetworkMonitorEnabled = compsec.GetOrSet("Network", true, out modified).BoolValue;
				compsec[Constants.Network].Comment = "Monitor network uptime and current IP addresses.";
				dirtyconfig |= modified;
				PowerManagerEnabled = compsec.GetOrSet(HumanReadable.Hardware.Power.Section, true, out modified).BoolValue;
				compsec[HumanReadable.Hardware.Power.Section].Comment = "Enable power plan management.";
				dirtyconfig |= modified;

				if (compsec.TryGet("Paging", out var pagingsetting))
				{
					compsec.Remove(pagingsetting);
					dirtyconfig = true;
				}

				StorageMonitorEnabled = compsec.GetOrSet(Constants.Storage, false, out modified).BoolValue;
				compsec[Constants.Storage].Comment = "Enable NVM storage monitoring functionality.";
				dirtyconfig |= modified;
				MaintenanceMonitorEnabled = compsec.GetOrSet(Constants.Maintenance, false, out modified).BoolValue;
				compsec[Constants.Maintenance].Comment = "Enable basic maintenance monitoring functionality.";
				dirtyconfig |= modified;

				HealthMonitorEnabled = compsec.GetOrSet(Constants.Health, false, out modified).BoolValue;
				compsec[Constants.Health].Comment = "General system health monitoring suite.";
				dirtyconfig |= modified;

				HardwareMonitorEnabled = compsec.GetOrSet(HumanReadable.Hardware.Section, false, out modified).BoolValue;
				compsec[HumanReadable.Hardware.Section].Comment = "Temperature, fan, etc. monitoring via OpenHardwareMonitor.";
				dirtyconfig |= modified;

				var qol = cfg[HumanReadable.Generic.QualityOfLife];
				ExitConfirmation = qol.GetOrSet("Exit confirmation", true, out modified).BoolValue;
				dirtyconfig |= modified;
				GlobalHotkeys = qol.GetOrSet("Register global hotkeys", false, out modified).BoolValue;
				dirtyconfig |= modified;
				AffinityStyle = qol.GetOrSet(HumanReadable.Hardware.CPU.Settings.AffinityStyle, 0, out modified).IntValue.Constrain(0, 1);
				dirtyconfig |= modified;

				var logsec = cfg[HumanReadable.Generic.Logging];
				var Verbosity = logsec.GetOrSet(Constants.Verbosity, 0, out modified).IntValue;
				logsec[Constants.Verbosity].Comment = "0 = Information, 1 = Debug, 2 = Verbose/Trace, 3 = Excessive; 2 and higher are available on debug builds only";
				switch (Verbosity)
				{
					default:
					case 0:
						loglevelswitch.MinimumLevel = LogEventLevel.Information;
						break;
					case 2:
#if DEBUG
						loglevelswitch.MinimumLevel = LogEventLevel.Verbose;
						break;
#endif
					case 3:
#if DEBUG
						loglevelswitch.MinimumLevel = LogEventLevel.Verbose;
						Trace = true;
						break;
#endif
					case 1:
						loglevelswitch.MinimumLevel = LogEventLevel.Debug;
						break;
				}
				dirtyconfig |= modified;

				var uniquecrashlogs_t = logsec.GetOrSet("Unique crash logs", false, out modified);
				UniqueCrashLogs = uniquecrashlogs_t.BoolValue;
				uniquecrashlogs_t.Comment = "On crash instead of creating crash.log in Logs, create crash-YYYYMMDD-HHMMSS-FFF.log instead. These are not cleaned out automatically!";
				dirtyconfig |= modified;

				var showinaction_t = logsec.GetOrSet("Show inaction", false, out modified);
				ShowInaction = showinaction_t.BoolValue;
				showinaction_t.Comment = "Log lack of action taken on processes.";
				dirtyconfig |= modified;

				var showagency_t = logsec.GetOrSet("Show agency", false, out modified);
				ShowAgency = showagency_t.BoolValue;
				showagency_t.Comment = "Log changes in agency, such as processes being left to decide their own fate.";
				dirtyconfig |= modified;

				var showprocessadjusts_t = logsec.GetOrSet("Show process adjusts", true, out modified);
				ShowProcessAdjusts = showprocessadjusts_t.BoolValue;
				showprocessadjusts_t.Comment = "Show blurbs about adjusted processes.";
				dirtyconfig |= modified;

				var showasessionactions_t = logsec.GetOrSet("Show session actions", true, out modified);
				ShowSessionActions = showasessionactions_t.BoolValue;
				showasessionactions_t.Comment = "Show blurbs about actions taken relating to sessions.";

				var uisec = cfg[Constants.UserInterface];
				ShowOnStart = uisec.GetOrSet(Constants.ShowOnStart, ShowOnStart, out modified).BoolValue;
				dirtyconfig |= modified;

				var volsec = cfg[Constants.VolumeMeter];
				ShowVolOnStart = volsec.GetOrSet(Constants.ShowOnStart, ShowVolOnStart, out modified).BoolValue;
				dirtyconfig |= modified;

				// [Performance]
				SelfOptimize = perfsec.GetOrSet("Self-optimize", true, out modified).BoolValue;
				dirtyconfig |= modified;
				var selfpriority_t = perfsec.GetOrSet("Self-priority", 1, out modified);
				SelfPriority = ProcessHelpers.IntToPriority(selfpriority_t.IntValue.Constrain(0, 2));
				selfpriority_t.Comment = "Process priority to set for TM itself. Restricted to 0 (Low) to 2 (Normal).";
				dirtyconfig |= modified;
				var selfaffinity_t = perfsec.GetOrSet("Self-affinity", 0, out modified);
				SelfAffinity = selfaffinity_t.IntValue.Constrain(0, ProcessManager.AllCPUsMask);
				selfaffinity_t.Comment = "Core mask as integer. 0 is for default OS control.";
				dirtyconfig |= modified;
				if (SelfAffinity > Convert.ToInt32(Math.Pow(2, Environment.ProcessorCount) - 1 + double.Epsilon)) SelfAffinity = 0;

				var selfoptimizebgio_t = perfsec.GetOrSet("Background I/O mode", false, out modified);
				SelfOptimizeBGIO = selfoptimizebgio_t.BoolValue;
				selfoptimizebgio_t.Comment = "Sets own priority exceptionally low. Warning: This can make TM's UI and functionality quite unresponsive.";
				dirtyconfig |= modified;

				if (perfsec.TryGet("WMI queries", out var wmiqsetting))
				{
					perfsec.Remove(wmiqsetting);
					dirtyconfig = true;
				}

				//perfsec.GetSetDefault("Child processes", false, out modified); // unused here
				//perfsec["Child processes"].Comment = "Enables controlling process priority based on parent process if nothing else matches. This is slow and unreliable.";
				//dirtyconfig |= modified;
				var temprescanthreshold_t = perfsec.GetOrSet("Temp rescan threshold", 1000, out modified);
				TempRescanThreshold = temprescanthreshold_t.IntValue;
				temprescanthreshold_t.Comment = "How many changes we wait to temp folder before expediting rescanning it.";
				dirtyconfig |= modified;
				var temprescandelay_t = perfsec.GetOrSet("Temp rescan delay", 60, out modified);
				TempRescanDelay = temprescandelay_t.IntValue * 60_000;
				temprescandelay_t.Comment = "How many minutes to wait before rescanning temp after crossing the threshold.";
				dirtyconfig |= modified;

				var pathcachelimit_t = perfsec.GetOrSet("Path cache", 60, out modified);
				PathCacheLimit = pathcachelimit_t.IntValue.Constrain(20, 200);
				pathcachelimit_t.Comment = "Path searching is very heavy process; this configures how many processes to remember paths for. The cache is allowed to occasionally overflow for half as much.";
				dirtyconfig |= modified;

				var pathcachemaxage_t = perfsec.GetOrSet("Path cache max age", 15, out modified);
				PathCacheMaxAge = new TimeSpan(0, pathcachemaxage_t.IntValue.Constrain(1, 1440), 0);
				pathcachemaxage_t.Comment = "Maximum age, in minutes, of cached objects. Min: 1 (1min), Max: 1440 (1day). These will be removed even if the cache is appropriate size.";
				dirtyconfig |= modified;

				// OPTIONS
				if (optsec.TryGet(Constants.ShowOnStart, out var sosv)) // REPRECATED
				{
					ShowOnStart = sosv.BoolValue;
					optsec.Remove(sosv);
					dirtyconfig = true;
				}

				var pagingenabled_t = optsec.GetOrSet("Paging", true, out modified);
				PagingEnabled = pagingenabled_t.BoolValue;
				pagingenabled_t.Comment = "Enable paging of apps as per their configuration.";
				dirtyconfig |= modified;

				//
				cfg.Get(Constants.Maintenance)?.TryRemove("Cleanup interval"); // DEPRECATRED

				if (dirtyconfig) corecfg.MarkDirty();

				MonitorCleanShutdown();

				Log.Information("<Core> Verbosity: " + MemoryLog.MemorySink.LevelSwitch.MinimumLevel.ToString());

				if (Trace) Log.Debug("<Core> Self-optimize: " + (SelfOptimize ? HumanReadable.Generic.Enabled : HumanReadable.Generic.Disabled));

				// PROTECT USERS FROM TOO HIGH PERMISSIONS
				isadmin = MKAh.Execution.IsAdministrator;
				const string Hell = "Hell";
				var adminwarning = ((cfg[Constants.Core].Get(Hell)?.Value ?? null) != Constants.No);
				if (isadmin && adminwarning)
				{
					var rv = SimpleMessageBox.ShowModal(
						Name+"! – admin access!!??",
						"You're starting TM with admin rights, is this right?\n\nYou can cause bad system operation, such as complete system hang, if you configure or configured TM incorrectly.",
						SimpleMessageBox.Buttons.AcceptCancel);

					if (rv == SimpleMessageBox.ResultType.OK)
					{
						cfg[Constants.Core][Hell].Value = Constants.No;
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
				var dbgsec = cfg[HumanReadable.Generic.Debug];
				DebugAudio = dbgsec.Get(HumanReadable.Hardware.Audio.Section)?.BoolValue ?? false;

				DebugForeground = dbgsec.Get(HumanReadable.System.Process.Foreground)?.BoolValue ?? false;

				DebugPower = dbgsec.Get(HumanReadable.Hardware.Power.Section)?.BoolValue ?? false;
				DebugMonitor = dbgsec.Get(HumanReadable.Hardware.Monitor.Section)?.BoolValue ?? false;

				DebugSession = dbgsec.Get("Session")?.BoolValue ?? false;
				DebugResize = dbgsec.Get("Resize")?.BoolValue ?? false;

				DebugMemory = dbgsec.Get(HumanReadable.Hardware.Memory)?.BoolValue ?? false;

				var exsec = cfg[Constants.Experimental];
				LastModifiedList = exsec.Get("Last Modified")?.BoolValue ?? false;
				TempMonitorEnabled = exsec.Get("Temp Monitor")?.BoolValue ?? false;
				int trecanalysis = exsec.Get("Record analysis")?.IntValue ?? 0;
				RecordAnalysis = trecanalysis > 0 ? (TimeSpan?)TimeSpan.FromSeconds(trecanalysis.Constrain(0, 180)) : null;
				IOPriorityEnabled = exsec.Get("IO Priority")?.BoolValue ?? false;
				if (!MKAh.Execution.IsWin7)
				{
					Log.Warning("<Core> I/O priority was enabled. Requires Win7 which you don't appear to be running.");
					IOPriorityEnabled = false;
				}

#if DEBUG
				Trace = dbgsec.Get(Constants.Trace)?.BoolValue ?? false;
#endif
			}

			// END DEBUG

			if (Trace)
			{
				Log.Debug($"<Core> Privilege level: {(isadmin ? "Admin" : "User")}");
				Log.Debug($"<Core> Path cache: {(PathCacheLimit == 0 ? HumanReadable.Generic.Disabled : PathCacheLimit.ToString())} items");
				Log.Debug($"<Core> Paging: {(PagingEnabled ? HumanReadable.Generic.Enabled : HumanReadable.Generic.Disabled)}");
			}

			return;
		}

		static void InitializeComponents()
		{
			if (Trace) Log.Debug("<Core> Loading components...");

			var timer = System.Diagnostics.Stopwatch.StartNew();

			var cts = new System.Threading.CancellationTokenSource();

			ProcessUtility.InitializeCache();

			Task PowMan, CpuMon, ProcMon, FgMon, NetMon, StorMon, HpMon, HwMon, AlMan;

			// Parallel loading, cuts down startup time some.
			// This is really bad if something fails
			Task[] init =
			{
				(PowMan = PowerManagerEnabled ? Task.Run(() => powermanager = new Power.Manager(), cts.Token) : Task.CompletedTask),
				(CpuMon = PowerManagerEnabled ? Task.Run(()=> cpumonitor = new CPUMonitor(), cts.Token) : Task.CompletedTask),
				(ProcMon = ProcessMonitorEnabled ? Task.Run(() => processmanager = new ProcessManager(), cts.Token) : Task.CompletedTask),
				(FgMon = ActiveAppMonitorEnabled ? Task.Run(()=> activeappmonitor = new ActiveAppManager(eventhook:false), cts.Token) : Task.CompletedTask),
				(NetMon = NetworkMonitorEnabled ? Task.Run(() => netmonitor = new Network.Manager(), cts.Token) : Task.CompletedTask),
				(StorMon = StorageMonitorEnabled ? Task.Run(() => storagemanager = new StorageManager(), cts.Token) : Task.CompletedTask),
				(HpMon = HealthMonitorEnabled ? Task.Run(() => healthmonitor = new HealthMonitor(), cts.Token) : Task.CompletedTask),
				(HwMon = HardwareMonitorEnabled ? Task.Run(() => hardware = new HardwareMonitor(), cts.Token) : Task.CompletedTask),
				(AlMan = AlertManagerEnabled ? Task.Run(() => alerts = new AlertManager(), cts.Token) : Task.CompletedTask),
			};

			Task SelfMaint = Task.Run(() => selfmaintenance = new SelfMaintenance());
			SelfMaint.ConfigureAwait(false);

			// MMDEV requires main thread
			try
			{
				if (AudioManagerEnabled)
				{
					audiomanager = new Audio.Manager();
					audiomanager.OnDisposed += (_, _ea) => audiomanager = null;

					if (MicrophoneManagerEnabled)
					{
						micmonitor = new Audio.MicManager();
						micmonitor.Hook(audiomanager);
						micmonitor.OnDisposed += (_, _ea) => micmonitor = null;
					}
				}
			}
			catch (InitFailure)
			{
				micmonitor?.Dispose();
				micmonitor = null;
				audiomanager?.Dispose();
				audiomanager = null;
			}

			// WinForms makes the following components not load nicely if not done here.
			trayaccess = new UI.TrayAccess();
			trayaccess.TrayMenuShown += (_, ea) => OptimizeResponsiviness(ea.Visible);

			if (PowerManagerEnabled)
			{
				Task.WhenAll(new Task[] { PowMan, CpuMon, ProcMon }).ContinueWith((_) => {
					if (powermanager == null) throw new InitFailure("Power Manager has failed to initialize");
					if (cpumonitor == null) throw new InitFailure("CPU Monitor has failed to initialize");
					if (processmanager == null) throw new InitFailure("Process Manager has failed to initialize");

					cpumonitor?.Hook(processmanager);

					trayaccess.Hook(powermanager);
					powermanager.onSuspendResume += PowerSuspendEnd; // HACK
					powermanager.Hook(cpumonitor);
				});
			}

			ProcMon.ContinueWith((x) => trayaccess?.Hook(processmanager));

			//if (HardwareMonitorEnabled)
			//	Task.WhenAll(HwMon).ContinueWith((x) => hardware.Start()); // this is slow

			NetMon.ContinueWith((x) =>
			{
				if (netmonitor != null)
				{
					netmonitor.SetupEventHooks();
					netmonitor.Tray = trayaccess;
				}
			});

			if (AudioManagerEnabled) ProcMon.ContinueWith((x) => audiomanager?.Hook(processmanager));

			bool warned = false;
			try
			{
				// WAIT for component initialization
				if (!Task.WaitAll(init, 5_000))
				{
					warned = true;
					Log.Warning($"<Core> Components still loading ({timer.ElapsedMilliseconds} ms and ongoing)");
					if (!Task.WaitAll(init, 115_000)) // total wait time of 120 seconds
						throw new InitFailure($"Component initialization taking excessively long ({timer.ElapsedMilliseconds} ms), aborting.");
				}
			}
			catch (AggregateException ex)
			{
				foreach (var iex in ex.InnerExceptions)
					Logging.Stacktrace(iex);

				System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
				throw; // because compiler is dumb and doesn't understand the above
			}

			if (processmanager != null)
				processmanager.OnDisposed += (_, _ea) => processmanager = null;
			if (activeappmonitor != null)
				activeappmonitor.OnDisposed += (_, _ea) => activeappmonitor = null;
			if (powermanager != null)
				powermanager.OnDisposed += (_, _ea) => powermanager = null;
			if (hardware != null)
				hardware.OnDisposed += (_, _ea) => hardware = null;
			if (healthmonitor != null)
				healthmonitor.OnDisposed += (_, _ea) => healthmonitor = null;
			if (storagemanager != null)
				storagemanager.OnDisposed += (_, _ea) => storagemanager = null;
			if (netmonitor != null)
				netmonitor.OnDisposed += (_, _ea) => netmonitor = null;
			if (processmanager != null)
				processmanager.OnDisposed += (_, _ea) => processmanager = null;

			// HOOKING
			// Probably should transition to weak events

			Log.Information($"<Core> Components loaded ({timer.ElapsedMilliseconds} ms); Hooking event handlers.");

			if (activeappmonitor != null)
			{
				processmanager.Hook(activeappmonitor);
				activeappmonitor.SetupEventHook();
			}

			if (powermanager != null)
			{
				powermanager.SetupEventHook();
				processmanager?.Hook(powermanager);
			}

			if (GlobalHotkeys)
				trayaccess.RegisterGlobalHotkeys();

			// UI
			if (State == Runstate.Normal)
			{
				if (ShowOnStart) BuildMainWindow(reveal: true);
				if (ShowVolOnStart) BuildVolumeMeter();
			}

			// Self-optimization
			if (SelfOptimize)
			{
				OptimizeResponsiviness();

				var self = System.Diagnostics.Process.GetCurrentProcess();
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
				Log.Information($"<Core> Self-optimizing – Priority: {SelfPriority.ToString()}; Affinity: {HumanInterface.BitMask(selfAffMask, ProcessManager.CPUCount)}");

				self.ProcessorAffinity = new IntPtr(selfAffMask); // this should never throw an exception
				self.PriorityClass = SelfPriority;
			}

			if (Trace) Log.Verbose("Displaying Tray Icon");

			trayaccess?.RefreshVisibility();

			timer.Stop();
			Log.Information($"<Core> Component loading finished ({timer.ElapsedMilliseconds} ms). {DisposalChute.Count.ToString()} initialized.");
		}
	}
}
