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
using MKAh;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Taskmaster
{
	public static class Taskmaster
	{
		public static string GitURL => "https://github.com/mkahvi/taskmaster";
		public static string ItchURL => "https://mkah.itch.io/taskmaster";

		public static string DataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MKAh", "Taskmaster");
		public static string LogPath = Path.Combine(DataPath, "Logs");

		public static Configuration.Manager Config = null;

		public static MicManager micmonitor = null;
		public static UI.MainWindow mainwindow = null;
		public static UI.VolumeMeter volumemeter = null;
		public static ProcessManager processmanager = null;
		public static UI.TrayAccess trayaccess = null;
		public static NetManager netmonitor = null;
		public static StorageManager storagemanager = null;
		public static PowerManager powermanager = null;
		public static ActiveAppManager activeappmonitor = null;
		public static HealthMonitor healthmonitor = null;
		public static SelfMaintenance selfmaintenance = null;
		public static AudioManager audiomanager = null;
		public static CPUMonitor cpumonitor = null;
		public static HardwareMonitor hardware = null;
		public static AlertManager alerts = null;

		/// <summary>
		/// For making sure disposal happens and that it does so in main thread.
		/// </summary>
		internal static Stack<IDisposable> DisposalChute = new Stack<IDisposable>();

		public static OS.HiddenWindow hiddenwindow;

		static Runstate State = Runstate.Normal;

		static bool RestartElevated { get; set; } = false;
		static int RestartCounter { get; set; } = 0;
		static int AdminCounter { get; set; } = 0;

		public static void PowerSuspendEnd(object _, EventArgs _ea)
		{
			Log.Information("<Power> Suspend/hibernate ended. Restarting to avoid problems.");
			UnifiedExit(restart: true);
		}

		public static void ConfirmExit(bool restart = false, bool admin = false, string message = null, bool alwaysconfirm=false)
		{
			var rv = SimpleMessageBox.ResultType.OK;

			if (alwaysconfirm || ExitConfirmation)
			{
				rv = SimpleMessageBox.ShowModal(
					(restart ? "Restart" : "Exit") + Application.ProductName + " ???",
					(string.IsNullOrEmpty(message) ? "" : message + "\n\n") +
					"Are you sure you want to " + (restart ? "restart" : "exit") + " Taskmaster?",
					SimpleMessageBox.Buttons.AcceptCancel);
			}
			if (rv != SimpleMessageBox.ResultType.OK) return;

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
				{
					try
					{
						DisposalChute.Pop().Dispose();
					}
					catch (Exception ex)
					{
						Logging.Stacktrace(ex);
						if (ex is NullReferenceException) throw;
					}
				}

				pipe = null; // disposing the pipe seems to just cause problems
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex, crashsafe: true);
				if (ex is NullReferenceException) throw;
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

		static int restoremainwindow_lock = 0;
		public static void ShowMainWindow()
		{
			//await Task.Delay(0);

			if (!Atomic.Lock(ref restoremainwindow_lock)) return; // already being done

			try
			{
				// Log.Debug("Bringing to front");
				BuildMainWindow();
				mainwindow?.Reveal();
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				if (ex is NullReferenceException) throw;
			}
			finally
			{
				Atomic.Unlock(ref restoremainwindow_lock);
			}
		}

		public static void BuildVolumeMeter()
		{
			if (!AudioManagerEnabled) return;

			if (volumemeter == null)
			{
				volumemeter = new UI.VolumeMeter();
				volumemeter.OnDisposed += (_, _ea) => volumemeter = null;
			}
		}

		public static object mainwindow_creation_lock = new object();
		/// <summary>
		/// Constructs and hooks the main window
		/// </summary>
		public static void BuildMainWindow(bool reveal=false)
		{
			lock (mainwindow_creation_lock)
			{
				if (mainwindow != null) return;

				mainwindow = new UI.MainWindow();
				mainwindow.FormClosed += (_, _ea) => mainwindow = null;

				try
				{
					if (storagemanager != null)
						mainwindow.Hook(storagemanager);

					if (processmanager != null)
						mainwindow.Hook(processmanager);

					if (audiomanager != null)
					{
						mainwindow.Hook(audiomanager);
						if (micmonitor != null)
							mainwindow.Hook(micmonitor);
					}

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
					if (ex is NullReferenceException) throw;
				}

				trayaccess.Hook(mainwindow);

				// .GotFocus and .LostFocus are apparently unreliable as per the API
				mainwindow.Activated += (_,_ea) => OptimizeResponsiviness(true);
				mainwindow.Deactivate += (_, _ea) => OptimizeResponsiviness(false);
			}

			if (reveal) mainwindow?.Reveal();
		}

		static void OptimizeResponsiviness(bool shown=false)
		{
			var self = Process.GetCurrentProcess();

			if (shown)
			{
				if (SelfOptimizeBGIO)
					MKAh.Utility.DiscardExceptions(() => ProcessUtility.UnsetBackground(self));

				self.PriorityClass = ProcessPriorityClass.AboveNormal;
			}
			else
			{
				self.PriorityClass = SelfPriority;

				if (SelfOptimizeBGIO)
					MKAh.Utility.DiscardExceptions(() => ProcessUtility.SetBackground(self));
			}

		}

		static void InitialConfiguration()
		{
			// INITIAL CONFIGURATIONN
			using (var tcfg = Config.Load(CoreConfigFilename).BlockUnload())
			{
				var sec = tcfg.Config.Get("Core")?.Get("Version")?.Value ?? null;
				if (sec == null || sec != ConfigVersion)
				{
					using (var initialconfig = new UI.Config.ComponentConfigurationWindow())
					{
						initialconfig.ShowDialog();
						if (initialconfig.DialogResult != DialogResult.OK)
							throw new InitFailure("Component configuration cancelled");
					}
				}
			}
		}

		public static event EventHandler OnStart;

		static void InitializeComponents()
		{
			Log.Information("<Core> Loading components...");

			var timer = Stopwatch.StartNew();

			var cts = new System.Threading.CancellationTokenSource();

			ProcessUtility.InitializeCache();

			Task PowMan, CpuMon, ProcMon, FgMon, NetMon, StorMon, HpMon, HwMon, AlMan;

			// Parallel loading, cuts down startup time some.
			// This is really bad if something fails
			Task[] init =
			{
				(PowMan = PowerManagerEnabled ? Task.Run(() => powermanager = new PowerManager(), cts.Token) : Task.CompletedTask),
				(CpuMon = PowerManagerEnabled ? Task.Run(()=> cpumonitor = new CPUMonitor(), cts.Token) : Task.CompletedTask),
				(ProcMon = ProcessMonitorEnabled ? Task.Run(() => processmanager = new ProcessManager(), cts.Token) : Task.CompletedTask),
				(FgMon = ActiveAppMonitorEnabled ? Task.Run(()=> activeappmonitor = new ActiveAppManager(eventhook:false), cts.Token) : Task.CompletedTask),
				(NetMon = NetworkMonitorEnabled ? Task.Run(() => netmonitor = new NetManager(), cts.Token) : Task.CompletedTask),
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
					audiomanager = new AudioManager();
					audiomanager.OnDisposed += (_, _ea) => audiomanager = null;

					if (MicrophoneManagerEnabled)
					{
						micmonitor = new MicManager();
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
				if (ShowOnStart) BuildMainWindow(reveal:true);
				if (ShowVolOnStart) BuildVolumeMeter();
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
			}

			if (Trace) Log.Verbose("Displaying Tray Icon");

			trayaccess?.RefreshVisibility();

			timer.Stop();
			Log.Information($"<Core> Component loading finished ({timer.ElapsedMilliseconds} ms). {DisposalChute.Count.ToString()} initialized.");
		}

		public static bool ShowProcessAdjusts { get; set; } = true;
		public static bool ShowSessionActions { get; set; } = true;

		public static bool DebugAudio { get; set; } = false;

		public static bool DebugForeground { get; set; } = false;

		public static bool DebugPower { get; set; } = false;
		public static bool DebugMonitor { get; set; } = false;

		public static bool DebugSession { get; set; } = false;
		public static bool DebugResize { get; set; } = false;

		public static bool DebugMemory { get; set; } = false;

		public static bool Trace { get; set; } = false;
		public static bool UniqueCrashLogs { get; set; } = false;
		public static bool ShowInaction { get; set; } = false;
		public static bool ShowAgency { get; set; } = false;

		public static bool ProcessMonitorEnabled { get; private set; } = true;
		public static bool MicrophoneManagerEnabled { get; private set; } = false;
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
		public static bool AlertManagerEnabled { get; private set; } = false;

		// EXPERIMENTAL FEATURES
		public static bool TempMonitorEnabled { get; private set; } = false;
		public static bool LastModifiedList { get; private set; } = false;
		public static TimeSpan? RecordAnalysis { get; set; } = null;
		public static bool IOPriorityEnabled { get; private set; } = false;

		// DEBUG INFO
		public static bool DebugCache { get; private set; } = false;

		public static bool ShowOnStart { get; private set; } = true;
		public static bool ShowVolOnStart { get; private set; } = false;

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
		public static int AffinityStyle { get; set; } = 0;
		public static bool GlobalHotkeys { get; set; } = false;

		public const string CoreConfigFilename = "Core.ini";
		static void LoadCoreConfig()
		{
			Log.Information("<Core> Loading configuration...");

			bool isadmin = false;

			using (var corecfg = Config.Load(CoreConfigFilename).BlockUnload())
			{
				var cfg = corecfg.Config;

				if (cfg.TryGet("Core", out var core) && core.TryGet("Hello", out var hello) && hello.Value.Equals("Hi"))
				{ }
				else
				{
					cfg["Core"]["Hello"].Value = "Hi";
					corecfg.MarkDirty();
				}

				var compsec = cfg["Components"];
				var optsec = cfg["Options"];
				var perfsec = cfg["Performance"];

				bool modified = false, dirtyconfig = false;
				cfg["Core"].GetOrSet("License", "Refused", out modified).Value = "Accepted";
				dirtyconfig |= modified;

				// [Components]
				ProcessMonitorEnabled = compsec.GetOrSet(HumanReadable.System.Process.Section, true, out modified).BoolValue;
				compsec[HumanReadable.System.Process.Section].Comment = "Monitor starting processes based on their name. Configure in Apps.ini";
				dirtyconfig |= modified;

				if (!ProcessMonitorEnabled)
				{
					Log.Warning("<Core> Process monitor disabled: state not supported, forcing enabled.");
					ProcessMonitorEnabled = true;
				}

				AudioManagerEnabled = compsec.GetOrSet(HumanReadable.Hardware.Audio.Section, true, out modified).BoolValue;
				compsec[HumanReadable.Hardware.Audio.Section].Comment = "Monitor audio sessions and set their volume as per user configuration.";
				dirtyconfig |= modified;
				MicrophoneManagerEnabled = compsec.GetOrSet("Microphone", false, out modified).BoolValue;
				compsec["Microphone"].Comment = "Monitor and force-keep microphone volume.";
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
				compsec["Network"].Comment = "Monitor network uptime and current IP addresses.";
				dirtyconfig |= modified;
				PowerManagerEnabled = compsec.GetOrSet(HumanReadable.Hardware.Power.Section, true, out modified).BoolValue;
				compsec[HumanReadable.Hardware.Power.Section].Comment = "Enable power plan management.";
				dirtyconfig |= modified;

				if (compsec.TryGet("Paging", out var pagingsetting))
				{
					compsec.Remove(pagingsetting);
					dirtyconfig = true;
				}

				StorageMonitorEnabled = compsec.GetOrSet("Storage", false, out modified).BoolValue;
				compsec["Storage"].Comment = "Enable NVM storage monitoring functionality.";
				dirtyconfig |= modified;
				MaintenanceMonitorEnabled = compsec.GetOrSet("Maintenance", false, out modified).BoolValue;
				compsec["Maintenance"].Comment = "Enable basic maintenance monitoring functionality.";
				dirtyconfig |= modified;

				HealthMonitorEnabled = compsec.GetOrSet("Health", false, out modified).BoolValue;
				compsec["Health"].Comment = "General system health monitoring suite.";
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

				var logsec = cfg["Logging"];
				var Verbosity = logsec.GetOrSet("Verbosity", 0, out modified).IntValue;
				logsec["Verbosity"].Comment = "0 = Information, 1 = Debug, 2 = Verbose/Trace, 3 = Excessive; 2 and higher are available on debug builds only";
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

				UniqueCrashLogs = logsec.GetOrSet("Unique crash logs", false, out modified).BoolValue;
				logsec["Unique crash logs"].Comment = "On crash instead of creating crash.log in Logs, create crash-YYYYMMDD-HHMMSS-FFF.log instead. These are not cleaned out automatically!";
				dirtyconfig |= modified;

				ShowInaction = logsec.GetOrSet("Show inaction", false, out modified).BoolValue;
				logsec["Show inaction"].Comment = "Log lack of action taken on processes.";
				dirtyconfig |= modified;

				ShowAgency = logsec.GetOrSet("Show agency", false, out modified).BoolValue;
				logsec["Show agency"].Comment = "Log changes in agency, such as processes being left to decide their own fate.";
				dirtyconfig |= modified;

				ShowProcessAdjusts = logsec.GetOrSet("Show process adjusts", true, out modified).BoolValue;
				logsec["Show process adjusts"].Comment = "Show blurbs about adjusted processes.";
				dirtyconfig |= modified;

				ShowSessionActions = logsec.GetOrSet("Show session actions", true, out modified).BoolValue;
				logsec["Show session actions"].Comment = "Show blurbs about actions taken relating to sessions.";

				var winsec = cfg["Show on start"];
				ShowOnStart = winsec.GetOrSet("Show on start", ShowOnStart, out modified).BoolValue;
				dirtyconfig |= modified;

				var volsec = cfg["Volume Meter"];
				ShowVolOnStart = volsec.GetOrSet("Show on start", ShowVolOnStart, out modified).BoolValue;
				dirtyconfig |= modified;

				// [Performance]
				SelfOptimize = perfsec.GetOrSet("Self-optimize", true, out modified).BoolValue;
				dirtyconfig |= modified;
				SelfPriority = ProcessHelpers.IntToPriority(perfsec.GetOrSet("Self-priority", 1, out modified).IntValue.Constrain(0, 2));
				perfsec["Self-priority"].Comment = "Process priority to set for TM itself. Restricted to 0 (Low) to 2 (Normal).";
				dirtyconfig |= modified;
				SelfAffinity = perfsec.GetOrSet("Self-affinity", 0, out modified).IntValue.Constrain(0, ProcessManager.AllCPUsMask);
				perfsec["Self-affinity"].Comment = "Core mask as integer. 0 is for default OS control.";
				dirtyconfig |= modified;
				if (SelfAffinity > Convert.ToInt32(Math.Pow(2, Environment.ProcessorCount) - 1 + double.Epsilon)) SelfAffinity = 0;

				SelfOptimizeBGIO = perfsec.GetOrSet("Background I/O mode", false, out modified).BoolValue;
				perfsec["Background I/O mode"].Comment = "Sets own priority exceptionally low. Warning: This can make TM's UI and functionality quite unresponsive.";
				dirtyconfig |= modified;

				if (perfsec.TryGet("WMI queries", out var wmiqsetting))
				{
					perfsec.Remove(wmiqsetting);
					dirtyconfig = true;
				}

				//perfsec.GetSetDefault("Child processes", false, out modified); // unused here
				//perfsec["Child processes"].Comment = "Enables controlling process priority based on parent process if nothing else matches. This is slow and unreliable.";
				//dirtyconfig |= modified;
				TempRescanThreshold = perfsec.GetOrSet("Temp rescan threshold", 1000, out modified).IntValue;
				perfsec["Temp rescan threshold"].Comment = "How many changes we wait to temp folder before expediting rescanning it.";
				dirtyconfig |= modified;
				TempRescanDelay = perfsec.GetOrSet("Temp rescan delay", 60, out modified).IntValue * 60_000;
				perfsec["Temp rescan delay"].Comment = "How many minutes to wait before rescanning temp after crossing the threshold.";
				dirtyconfig |= modified;

				PathCacheLimit = perfsec.GetOrSet("Path cache", 60, out modified).IntValue.Constrain(20, 200);
				perfsec["Path cache"].Comment = "Path searching is very heavy process; this configures how many processes to remember paths for. The cache is allowed to occasionally overflow for half as much.";
				dirtyconfig |= modified;

				PathCacheMaxAge = new TimeSpan(0, perfsec.GetOrSet("Path cache max age", 15, out modified).IntValue.Constrain(1, 1440), 0);
				perfsec["Path cache max age"].Comment = "Maximum age, in minutes, of cached objects. Min: 1 (1min), Max: 1440 (1day). These will be removed even if the cache is appropriate size.";
				dirtyconfig |= modified;

				// OPTIONS
				if (optsec.TryGet("Show on start", out var sosv)) // REPRECATED
				{
					ShowOnStart = sosv.BoolValue;
					optsec.Remove(sosv);
					dirtyconfig = true;
				}

				PagingEnabled = optsec.GetOrSet("Paging", true, out modified).BoolValue;
				optsec["Paging"].Comment = "Enable paging of apps as per their configuration.";
				dirtyconfig |= modified;

				//
				var maintsec = cfg["Maintenance"];
				maintsec.TryRemove("Cleanup interval"); // DEPRECATRED

				if (dirtyconfig) corecfg.MarkDirty();

				MonitorCleanShutdown();

				Log.Information("<Core> Verbosity: " + MemoryLog.MemorySink.LevelSwitch.MinimumLevel.ToString());
				Log.Information("<Core> Self-optimize: " + (SelfOptimize ? HumanReadable.Generic.Enabled : HumanReadable.Generic.Disabled));

				// PROTECT USERS FROM TOO HIGH PERMISSIONS
				isadmin = MKAh.Execution.IsAdministrator;
				var adminwarning = ((cfg["Core"].Get("Hell")?.Value ?? null) != "No");
				if (isadmin && adminwarning)
				{
					var rv = SimpleMessageBox.ShowModal(
						"Taskmaster! – admin access!!??",
						"You're starting TM with admin rights, is this right?\n\nYou can cause bad system operation, such as complete system hang, if you configure or configured TM incorrectly.",
						SimpleMessageBox.Buttons.AcceptCancel);

					if (rv == SimpleMessageBox.ResultType.OK)
					{
						cfg["Core"]["Hell"].Value = "No";
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
				DebugAudio = dbgsec.Get(HumanReadable.Hardware.Audio.Section)?.BoolValue ?? false;

				DebugForeground = dbgsec.Get(HumanReadable.System.Process.Foreground)?.BoolValue ?? false;

				DebugPower = dbgsec.Get(HumanReadable.Hardware.Power.Section)?.BoolValue ?? false;
				DebugMonitor = dbgsec.Get(HumanReadable.Hardware.Monitor.Section)?.BoolValue ?? false;

				DebugSession = dbgsec.Get("Session")?.BoolValue ?? false;
				DebugResize = dbgsec.Get("Resize")?.BoolValue ?? false;

				DebugMemory = dbgsec.Get("Memory")?.BoolValue ?? false;

				var exsec = cfg["Experimental"];
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
				Trace = dbgsec.Get("Trace")?.BoolValue ?? false;
#endif
			}

			// END DEBUG

			Log.Information($"<Core> Privilege level: {(isadmin ? "Admin" : "User")}");

			Log.Information($"<Core> Path cache: {(PathCacheLimit == 0 ? HumanReadable.Generic.Disabled : PathCacheLimit.ToString())} items");

			Log.Information($"<Core> Paging: {(PagingEnabled ? HumanReadable.Generic.Enabled : HumanReadable.Generic.Disabled)}");

			return;
		}

		static DirectoryInfo TempRunningDir = null;

		static void MonitorCleanShutdown()
		{
			TempRunningDir = new System.IO.DirectoryInfo(System.IO.Path.Combine(DataPath, ".running-TM0"));

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
				if (ex is NullReferenceException) throw;
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
			catch (Exception ex) when (ex is NullReferenceException || ex is OutOfMemoryException) { throw; }
			catch { } // ignore, no access probably
			finally
			{
				fs?.Dispose();
			}
		}

		public const string AdminArg = "--admin";
		public const string RestartArg = "--restart";

		static void ParseArguments(string[] args)
		{
			for (int i = 0; i < args.Length; i++)
			{
				if (!args[i].StartsWith("--"))
				{
					Log.Error("<Start> Unrecognized command-line parameter: " + args[i]);
					continue;
				}

				switch (args[i])
				{
					case RestartArg:
						if (args.Length > i+1 && !args[i+1].StartsWith("--"))
							RestartCounter =  Convert.ToInt32(args[++i]);
						break;
					case AdminArg:
						if (args.Length > i+1 && !args[i+1].StartsWith("--"))
						{
							try
							{
								AdminCounter = Convert.ToInt32(args[++i]);
							}
							catch (Exception ex) when (ex is NullReferenceException || ex is OutOfMemoryException) { throw; }
							catch (Exception ex)
							{
								Logging.Stacktrace(ex, crashsafe: true);
							}
						}

						if (AdminCounter <= 1)
						{
							if (!MKAh.Execution.IsAdministrator)
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
								catch (Exception ex) when (ex is NullReferenceException || ex is OutOfMemoryException) { throw; }
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
		}

		static void LicenseBoiler()
		{
			using (var cfg = Config.Load(CoreConfigFilename).BlockUnload())
			{
				if (cfg.Config.Get("Core")?.Get("License")?.Value.Equals("Accepted") ?? false) return;
			}

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

		static void PreallocLastLog()
		{
			DateTimeOffset lastDate = DateTimeOffset.MinValue;
			FileInfo lastFile = null;
			string lastPath = null;

			var files = System.IO.Directory.GetFiles(LogPath, "*", System.IO.SearchOption.AllDirectories);
			foreach (var filename in files)
			{
				var path = System.IO.Path.Combine(LogPath, filename);
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
				DataPath = portpath;
				Portable = true;
			}
			else if (!File.Exists(Path.Combine(DataPath, CoreConfigFilename)))
			{
				if (SimpleMessageBox.ShowModal("Taskmaster setup", "Set up PORTABLE installation?", SimpleMessageBox.Buttons.AcceptCancel)
					== SimpleMessageBox.ResultType.OK)
				{
					DataPath = portpath;
					Portable = true;
					System.IO.Directory.CreateDirectory(DataPath); // this might fail, but we don't really care.
				}
			}
		}

		const string PipeName = @"\\.\MKAh\Taskmaster\Pipe";
		const string PipeRestart = "TM...RESTART";
		const string PipeTerm = "TM...TERMINATE";
		const string PipeRefresh = "TM...REFRESH";
		static System.IO.Pipes.NamedPipeServerStream pipe = null;

		static void PipeCleaner(IAsyncResult result)
		{
			Debug.WriteLine("<IPC> Activity");

			if (pipe == null) return;

			try
			{
				var lp = pipe;
				//pipe = null;
				lp.EndWaitForConnection(result);

				if (!result.IsCompleted) return;
				if (!pipe.IsConnected) return;
				if (!pipe.IsMessageComplete) return;

				using (var sr = new StreamReader(lp))
				{
					var line = sr.ReadLine();
					if (line.StartsWith(PipeRestart))
					{
						Log.Warning("<IPC> Restart request received.");
						UnifiedExit(restart: true);
						return;
					}
					else if (line.StartsWith(PipeTerm))
					{
						Log.Warning("<IPC> Termination request received.");
						UnifiedExit(restart: false);
						return;
					}
					else if (line.StartsWith(PipeRefresh))
					{
						Log.Information("<IPC> Refresh.");
						Refresh();
						return;
					}
					else
					{
						Log.Error("<IPC> Unknown message: " + line);
					}
				}

				if (lp.CanRead) lp?.BeginWaitForConnection(PipeCleaner, null);
			}
			catch (ObjectDisposedException) { Statistics.DisposedAccesses++; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				if (ex is NullReferenceException) throw;
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
				Debug.WriteLine("Failed to set up pipe server.");
			}

			return null;
		}

		static void PipeExplorer(string message)
		{
			Debug.WriteLine("Attempting to communicate with running instance of TM.");

			try
			{
				using (var pe = new System.IO.Pipes.NamedPipeClientStream(".", PipeName, System.IO.Pipes.PipeAccessRights.Write, System.IO.Pipes.PipeOptions.WriteThrough, System.Security.Principal.TokenImpersonationLevel.Impersonation, HandleInheritability.None))
				using (var sw = new StreamWriter(pe))
				{
					if (!pe.IsConnected) pe.Connect(5_000);

					if (pe.IsConnected && pe.CanWrite)
					{
						sw.WriteLine(message);
						sw.Flush();
					}

					System.Threading.Thread.Sleep(100); // HACK: async pipes don't like things happening too fast.
				}
			}
			catch (IOException ex)
			{
				MessageBox.Show("Timeout communicating with existing Taskmaster instance.");
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex, crashsafe: true);
				if (ex is NullReferenceException) throw;
			}
		}

		const string SingletonID = "088f7210-51b2-4e06-9bd4-93c27a973874.taskmaster"; // garbage

		public static LoggingLevelSwitch loglevelswitch = new LoggingLevelSwitch(LogEventLevel.Information);

		// entry point to the application
		[STAThread] // supposedly needed to avoid shit happening with the WinForms GUI and other GUI toolkits
		static public int Main(string[] args)
		{
			System.Threading.Mutex singleton = null;

			try
			{
				var startTimer = Stopwatch.StartNew();

				//Debug.Listeners.Add(new TextWriterTraceListener(System.Console.Out));

				NativeMethods.SetErrorMode(NativeMethods.SetErrorMode(NativeMethods.ErrorModes.SEM_SYSTEMDEFAULT) | NativeMethods.ErrorModes.SEM_NOGPFAULTERRORBOX | NativeMethods.ErrorModes.SEM_FAILCRITICALERRORS);

				System.Windows.Forms.Application.SetUnhandledExceptionMode(UnhandledExceptionMode.Automatic);
				System.Windows.Forms.Application.ThreadException += UnhandledUIException;
				System.Windows.Forms.Application.EnableVisualStyles(); // required by shortcuts and high dpi-awareness
				System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false); // required by high dpi-awareness

				AppDomain.CurrentDomain.UnhandledException += UnhandledException;

				//hiddenwindow = new OS.HiddenWindow();

				TryPortableMode();
				LogPath = Path.Combine(DataPath, "Logs");

				// Singleton
				singleton = new System.Threading.Mutex(true, SingletonID, out bool mutexgained);
				if (!mutexgained)
				{
					SimpleMessageBox.ResultType rv = SimpleMessageBox.ResultType.Cancel;

					// already running, signal original process
					using (var msg = new SimpleMessageBox("Taskmaster!",
						"Already operational.\n\nRetry to try to recover [restart] running instance.\nEnd to kill running instance and exit this.\nCancel to simply request refresh.",
						SimpleMessageBox.Buttons.RetryEndCancel))
					{
						msg.ShowDialog();
						rv = msg.Result;
					}

					switch (rv)
					{
						case SimpleMessageBox.ResultType.Retry:
							PipeExplorer(PipeRestart);
							break;
						case SimpleMessageBox.ResultType.End:
							PipeExplorer(PipeTerm);
							break;
						case SimpleMessageBox.ResultType.Cancel:
							PipeExplorer(PipeRefresh);
							break;
					}

					return -1;
				}

				pipe = PipeDream(); // IPC with other instances of TM

				// Multi-core JIT
				// https://docs.microsoft.com/en-us/dotnet/api/system.runtime.profileoptimization
				{
					var cachepath = System.IO.Path.Combine(DataPath, "Cache");
					if (!System.IO.Directory.Exists(cachepath)) System.IO.Directory.CreateDirectory(cachepath);
					System.Runtime.ProfileOptimization.SetProfileRoot(cachepath);
					System.Runtime.ProfileOptimization.StartProfile("jit.profile");
				}

				Config = new Configuration.Manager(DataPath);

				LicenseBoiler();

				// INIT LOGGER
				var logswitch = new LoggingLevelSwitch(LogEventLevel.Information);

#if DEBUG
				loglevelswitch.MinimumLevel = LogEventLevel.Debug;
				if (Trace) loglevelswitch.MinimumLevel = LogEventLevel.Verbose;
#endif

				var logpathtemplate = System.IO.Path.Combine(LogPath, "taskmaster-{Date}.log");
				Serilog.Log.Logger = new Serilog.LoggerConfiguration()
					.MinimumLevel.ControlledBy(loglevelswitch)
					.WriteTo.Console(levelSwitch: new LoggingLevelSwitch(LogEventLevel.Verbose))
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

				var sbs = new StringBuilder()
					.Append("Taskmaster! (#").Append(Process.GetCurrentProcess().Id).Append(")")
					.Append(MKAh.Execution.IsAdministrator ? " [ADMIN]" : "").Append(Portable ? " [PORTABLE]" : "")
					.Append(" – Version: ").Append(System.Reflection.Assembly.GetExecutingAssembly().GetName().Version)
					.Append(" – Built: ").Append(builddate.ToString("yyyy/MM/dd HH:mm")).Append($" [{age:N0} days old]");
				Log.Information(sbs.ToString());

				//PreallocLastLog();

				InitialConfiguration();
				LoadCoreConfig();
				InitializeComponents();

				Config.Flush(); // early save of configs

				if (RestartCounter > 0) Log.Information($"<Core> Restarted {RestartCounter.ToString()} time(s)");
				startTimer.Stop();
				Log.Information($"<Core> Initialization complete ({startTimer.ElapsedMilliseconds} ms)...");
				startTimer = null;

				if (Debug.Listeners.Count > 0)
				{
					Debug.WriteLine("Embedded Resources");
					foreach (var name in System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceNames())
						Debug.WriteLine(" - " + name);
				}

				if (State == Runstate.Normal)
				{
					OnStart?.Invoke(null, EventArgs.Empty);
					OnStart = null;

					System.Windows.Forms.Application.Run(); // WinForms

					// System.Windows.Application.Current.Run(); // WPF
				}

				if (SelfOptimize) // return decent processing speed to quickly exit
				{
					var self = Process.GetCurrentProcess();
					self.PriorityClass = ProcessPriorityClass.AboveNormal;
					if (SelfOptimizeBGIO)
						MKAh.Utility.DiscardExceptions(() => ProcessUtility.SetBackground(self));
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
						Logging.Stacktrace(ex.InnerException ?? ex, crashsafe: true);
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
			catch (Exception ex)
			{
				Logging.Stacktrace(ex, crashsafe: true);

				return 1; // should trigger finally block
			}
			finally
			{
				try
				{
					ExitCleanup();

					Config?.Dispose();
					singleton?.Dispose();

					Log.CloseAndFlush();
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex, crashsafe: true);
					if (ex is NullReferenceException) throw;
				}
			}

			return 0;
		}

		static void PrintStats()
		{
			Log.Information($"<Stat> WMI polling: {Statistics.WMIPollTime:N2}s [{Statistics.WMIPolling.ToString()}]");
			Log.Information($"<Stat> Self-maintenance: {Statistics.MaintenanceTime:N2}s [{Statistics.MaintenanceCount.ToString()}]");
			Log.Information($"<Stat> Path cache: {Statistics.PathCacheHits.ToString()} hits, {Statistics.PathCacheMisses.ToString()} misses");
			var sbs = new StringBuilder()
				.Append("<Stat> Path finding: ").Append(Statistics.PathFindAttempts).Append(" total attempts; ")
				.Append(Statistics.PathFindViaModule).Append(" via module info, ")
				.Append(Statistics.PathFindViaC).Append(" via C call, ")
				.Append(Statistics.PathNotFound).Append(" not found");
			Log.Information(sbs.ToString());
			Log.Information($"<Stat> Processes modified: {Statistics.TouchCount.ToString()}; Ignored for remodification: {Statistics.TouchIgnore.ToString()}");
		}

		public static void Refresh()
		{
			if (State != Runstate.Normal) return;

			try
			{
				trayaccess?.EnsureVisible();
				Config.Flush();
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex, crashsafe: true);
			}
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
				if (ex is NullReferenceException) throw;
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