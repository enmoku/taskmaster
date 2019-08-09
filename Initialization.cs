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
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Taskmaster
{
	public static partial class Taskmaster
	{
		public const string CoreConfigFilename = "Core.ini";
		public const string UIConfigFilename = "UI.ini";

		static System.Threading.Mutex Initialize(ref string[] args)
		{
			var startTimer = Stopwatch.StartNew();

			//Debug.Listeners.Add(new TextWriterTraceListener(System.Console.Out));

			NativeMethods.SetErrorMode(NativeMethods.SetErrorMode(NativeMethods.ErrorModes.SEM_SYSTEMDEFAULT) | NativeMethods.ErrorModes.SEM_NOGPFAULTERRORBOX | NativeMethods.ErrorModes.SEM_FAILCRITICALERRORS);

			System.Windows.Forms.Application.SetUnhandledExceptionMode(System.Windows.Forms.UnhandledExceptionMode.Automatic);
			System.Windows.Forms.Application.ThreadException += UnhandledUIException;
			System.Windows.Forms.Application.EnableVisualStyles(); // required by shortcuts and high dpi-awareness
			System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false); // required by high dpi-awareness

			AppDomain.CurrentDomain.UnhandledException += UnhandledException;

			hiddenwindow = new HiddenWindow();

			TryPortableMode();
			LogPath = System.IO.Path.Combine(DataPath, LogFolder);

			// Singleton
			var singleton = new System.Threading.Mutex(true, SingletonID, out bool mutexgained);
			if (!mutexgained)
			{
				// already running, signal original process
				MessageBox.ResultType rv = MessageBox.ShowModal(Name + "!",
					"Already operational.\n\nRetry to try to recover [restart] running instance.\nEnd to kill running instance and exit this.\nCancel to simply request refresh.",
					MessageBox.Buttons.RetryEndCancel);

				switch (rv)
				{
					case MessageBox.ResultType.Retry:
						IPC.Transmit(IPC.RestartMessage);
						break;
					case MessageBox.ResultType.End:
						IPC.Transmit(IPC.TerminationMessage);
						break;
					case MessageBox.ResultType.Cancel:
						IPC.Transmit(IPC.RefreshMessage);
						break;
				}

				throw new RunstateException("Already running", Runstate.CriticalFailure);
			}

			IPC.Listen();

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

			UI.Splash splash = null;

			/*
			Task.Run(() => {
				{
					splash = new UI.Splash(6); // splash screen
					LoadEvent += splash.LoadEvent;
					UIWaiter.Set();
				}

				Application.Run();
				UIWaiter.Set();
			});

			UIWaiter.WaitOne();
			UIWaiter.Reset();
			*/

			// INIT LOGGER
#if DEBUG
			uiloglevelswitch.MinimumLevel = loglevelswitch.MinimumLevel = LogEventLevel.Debug;
			if (Trace) uiloglevelswitch.MinimumLevel = loglevelswitch.MinimumLevel = LogEventLevel.Verbose;
#endif

			// COMMAND-LINE ARGUMENTS
			CommandLine.ParseArguments(args);

			var logconf = new Serilog.LoggerConfiguration()
				.MinimumLevel.ControlledBy(loglevelswitch)
#if DEBUG
				.WriteTo.Console(levelSwitch: new Serilog.Core.LoggingLevelSwitch(LogEventLevel.Verbose))
#endif
				.WriteTo.MemorySink(levelSwitch: uiloglevelswitch);

			if (!NoLogging)
			{
				var logpathtemplate = System.IO.Path.Combine(LogPath, Name + "-{Date}.log");

				logconf = logconf.WriteTo.RollingFile(logpathtemplate, outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
					levelSwitch: new Serilog.Core.LoggingLevelSwitch(Serilog.Events.LogEventLevel.Debug), retainedFileCountLimit: 3);
			}

			Serilog.Log.Logger = logconf.CreateLogger();

			AppDomain.CurrentDomain.ProcessExit += (_, _ea) => Log.CloseAndFlush();

			LoadEvent?.Invoke(null, new LoadEventArgs("Logger initialized.", LoadEventType.Loaded));

			args = null; // silly

			// STARTUP
			var builddate = BuildDate();

			var now = DateTime.UtcNow;
			var age = (now - builddate).TotalDays;

			var sbs = new StringBuilder(512)
				.Append(Name).Append("! #").Append(System.Diagnostics.Process.GetCurrentProcess().Id)
				.Append(MKAh.Execution.IsAdministrator ? " [ADMIN]" : "").Append(Portable ? " [PORTABLE]" : "")
				.Append(" – Version: ").Append(Version)
				.Append(" – Built: ").Append(builddate.ToString("yyyy/MM/dd HH:mm"))
				.Append(" [").AppendFormat("{0:N0}", age).Append(" days old]");
			Log.Information(sbs.ToString());

			//PreallocLastLog();

			InitialConfiguration();

			LoadCoreConfig();

			UpdateStyling();

			//if (ShowSplash) splash.Invoke(new Action(() => splash.Show()));

			InitializeComponents();

			Config.Flush(); // early save of configs

			if (RestartCounter > 0 && Trace) Log.Debug($"<Core> Restarted {RestartCounter.ToString()} time(s)");
			startTimer.Stop();

			Log.Information($"<Core> Initialization complete ({startTimer.ElapsedMilliseconds} ms)...");

			LoadEvent?.Invoke(null, new LoadEventArgs("Core loading finished", LoadEventType.Loaded));
			LoadEvent = null;

			splash?.Invoke(new Action(() => splash.Dispose()));
			splash = null;

			return singleton;
		}

		static void InitialConfiguration()
		{
			// INITIAL CONFIGURATIONN
			using var tcfg = Config.Load(CoreConfigFilename);
			var sec = tcfg.Config.Get(Constants.Core)?.Get(Constants.Version)?.Value ?? null;
			if (sec?.Equals(ConfigVersion) ?? false) return;

			using var initialconfig = new UI.Config.ComponentConfigurationWindow();
			initialconfig.ShowDialog();
			if (!initialconfig.DialogOK)
				throw new InitFailure("Component configuration cancelled");

			LoadEvent?.Invoke(null, new LoadEventArgs("Initial configuration confirmed.", LoadEventType.Loaded));
		}

		static void LoadCoreConfig()
		{
			if (Trace) Log.Debug("<Core> Loading configuration...");

			using var corecfg = Config.Load(CoreConfigFilename);
			var cfg = corecfg.Config;

			const string Hello = "Hello", Hi = "Hi";

			if (!cfg.TryGet(Constants.Core, out var core) || !core.TryGet(Hello, out var hello) || !hello.Value.Equals(Hi))
				cfg[Constants.Core][Hello].Value = Hi;

			var compsec = cfg[Constants.Components];
			var optsec = cfg[Constants.Options];
			var perfsec = cfg[Constants.Performance];

			cfg[Constants.Core].GetOrSet(Constants.License, Constants.Refused).Value = Constants.Accepted;

			// [Components]
			ProcessMonitorEnabled = compsec.GetOrSet(HumanReadable.System.Process.Section, true)
				.InitComment("Monitor starting processes based on their name. Configure in Apps.ini")
				.Bool;

			if (!ProcessMonitorEnabled)
			{
				Log.Warning("<Core> Process monitor disabled: state not supported, forcing enabled.");
				ProcessMonitorEnabled = true;
			}

			AudioManagerEnabled = compsec.GetOrSet(HumanReadable.Hardware.Audio.Section, true)
				.InitComment("Monitor audio sessions and set their volume as per user configuration.")
				.Bool;

			MicrophoneManagerEnabled = compsec.GetOrSet(HumanReadable.Hardware.Audio.Microphone, false)
				.InitComment("Monitor and force-keep microphone volume.")
				.Bool;

			if (!AudioManagerEnabled && MicrophoneManagerEnabled)
			{
				Log.Warning("<Core> Audio manager disabled, disabling microphone manager.");
				MicrophoneManagerEnabled = false;
			}

			// MediaMonitorEnabled = compsec.GetSetDefault("Media", true, out modified).Bool;
			// compsec["Media"].Comment = "Unused";
			// dirtyconfig |= modified;
			ActiveAppMonitorEnabled = compsec.GetOrSet(HumanReadable.System.Process.Foreground, true)
				.InitComment("Game/Foreground app monitoring and adjustment.")
				.Bool;

			NetworkMonitorEnabled = compsec.GetOrSet(Network.Constants.Network, true)
				.InitComment("Monitor network uptime and current IP addresses.")
				.Bool;

			PowerManagerEnabled = compsec.GetOrSet(HumanReadable.Hardware.Power.Section, true)
				.InitComment("Enable power plan management.")
				.Bool;

			if (compsec.TryGet(Constants.Paging, out var pagingsetting))
				compsec.Remove(pagingsetting);

			StorageMonitorEnabled = compsec.GetOrSet(Constants.Storage, false)
				.InitComment("Enable NVM storage monitoring functionality.")
				.Bool;

			MaintenanceMonitorEnabled = compsec.GetOrSet(Constants.Maintenance, false)
				.InitComment("Enable basic maintenance monitoring functionality.")
				.Bool;

			HealthMonitorEnabled = compsec.GetOrSet(Constants.Health, false)
				.InitComment("General system health monitoring suite.")
				.Bool;

			HardwareMonitorEnabled = compsec.GetOrSet(HumanReadable.Hardware.Section, false)
				.InitComment("Temperature, fan, etc. monitoring via OpenHardwareMonitor.")
				.Bool;

			var qol = cfg[HumanReadable.Generic.QualityOfLife];
			ExitConfirmation = qol.GetOrSet("Exit confirmation", true).Bool;
			GlobalHotkeys = qol.GetOrSet("Register global hotkeys", false).Bool;
			AffinityStyle = qol.GetOrSet(HumanReadable.Hardware.CPU.Settings.AffinityStyle, 0).Int.Constrain(0, 1);

			var logsec = cfg[HumanReadable.Generic.Logging];
			var Verbosity = logsec.GetOrSet(Constants.Verbosity, 0)
				.InitComment("0 = Information, 1 = Debug, 2 = Verbose/Trace, 3 = Excessive; 2 and higher are available on debug builds only.")
				.Int;
			switch (Verbosity)
			{
				default:
				case 0:
					uiloglevelswitch.MinimumLevel = loglevelswitch.MinimumLevel = LogEventLevel.Information;
					break;
				case 2:
#if DEBUG
					uiloglevelswitch.MinimumLevel = loglevelswitch.MinimumLevel = LogEventLevel.Verbose;
					break;
#endif
				case 3:
#if DEBUG
					uiloglevelswitch.MinimumLevel = loglevelswitch.MinimumLevel = LogEventLevel.Verbose;
					Trace = true;
					break;
#endif
				case 1:
					uiloglevelswitch.MinimumLevel = loglevelswitch.MinimumLevel = LogEventLevel.Debug;
					break;
			}

			UniqueCrashLogs = logsec.GetOrSet("Unique crash logs", false)
				.InitComment("On crash instead of creating crash.log in Logs, create crash-YYYYMMDD-HHMMSS-FFF.log instead. These are not cleaned out automatically!")
				.Bool;

			ShowInaction = logsec.GetOrSet("Show inaction", false)
				.InitComment("Log lack of action taken on processes.")
				.Bool;

			ShowAgency = logsec.GetOrSet("Show agency", false)
				.InitComment("Log changes in agency, such as processes being left to decide their own fate.")
				.Bool;

			ShowProcessAdjusts = logsec.GetOrSet("Show process adjusts", true)
				.InitComment("Show blurbs about adjusted processes.")
				.Bool;

			ShowSessionActions = logsec.GetOrSet("Show session actions", true)
				.InitComment("Show blurbs about actions taken relating to sessions.")
				.Bool;

			var uisec = cfg[Constants.UserInterface];
			ShowOnStart = uisec.GetOrSet(Constants.ShowOnStart, ShowOnStart).Bool;

			var volsec = cfg[Constants.VolumeMeter];
			ShowVolOnStart = volsec.GetOrSet(Constants.ShowOnStart, ShowVolOnStart).Bool;

			// [Performance]
			SelfOptimize = perfsec.GetOrSet("Self-optimize", true).Bool;

			var selfpriority_t = perfsec.GetOrSet("Self-priority", 1)
				.InitComment("Process priority to set for TM itself. Restricted to 0 (Low) to 2 (Normal).")
				.Int.Constrain(0, 2);
			SelfPriority = Process.Utility.IntToPriority(selfpriority_t);

			SelfAffinity = perfsec.GetOrSet("Self-affinity", 0)
				.InitComment("Core mask as integer. 0 is for default OS control.")
				.Int.Constrain(0, Process.Utility.FullCPUMask);

			if (SelfAffinity > Process.Utility.FullCPUMask) SelfAffinity = 0;

			SelfOptimizeBGIO = perfsec.GetOrSet("Background I/O mode", false)
				.InitComment("Sets own priority exceptionally low. Warning: This can make TM's UI and functionality quite unresponsive.")
				.Bool;

			if (perfsec.TryGet("WMI queries", out var wmiqsetting))
				perfsec.Remove(wmiqsetting);

			//perfsec.GetSetDefault("Child processes", false, out modified); // unused here
			//perfsec["Child processes"].Comment = "Enables controlling process priority based on parent process if nothing else matches. This is slow and unreliable.";
			//dirtyconfig |= modified;
			TempRescanThreshold = perfsec.GetOrSet("Temp rescan threshold", 1000)
				.InitComment("How many changes we wait to temp folder before expediting rescanning it.")
				.Int;

			TempRescanDelay = perfsec.GetOrSet("Temp rescan delay", 60)
				.InitComment("How many minutes to wait before rescanning temp after crossing the threshold.")
				.Int * 60_000;

			PathCacheLimit = perfsec.GetOrSet("Path cache", 60)
				.InitComment("Path searching is very heavy process; this configures how many processes to remember paths for. The cache is allowed to occasionally overflow for half as much.")
				.Int.Constrain(20, 200);

			var pathcachemaxage_t = perfsec.GetOrSet("Path cache max age", 15)
				.InitComment("Maximum age, in minutes, of cached objects. Min: 1 (1min), Max: 1440 (1day). These will be removed even if the cache is appropriate size.")
				.Int;
			PathCacheMaxAge = new TimeSpan(0, pathcachemaxage_t.Constrain(1, 1440), 0);

			// OPTIONS
			if (optsec.TryGet(Constants.ShowOnStart, out var sosv)) // REPRECATED
			{
				ShowOnStart = sosv.Bool;
				optsec.Remove(sosv);
			}

			PagingEnabled = optsec.GetOrSet("Paging", true)
				.InitComment("Enable paging of apps as per their configuration.")
				.Bool;

			MonitorCleanShutdown();

			Log.Information("<Core> Verbosity: " + MemoryLog.MemorySink.LevelSwitch.MinimumLevel.ToString());

			if (Trace) Log.Debug("<Core> Self-optimize: " + (SelfOptimize ? HumanReadable.Generic.Enabled : HumanReadable.Generic.Disabled));

			// PROTECT USERS FROM TOO HIGH PERMISSIONS
			bool isadmin = MKAh.Execution.IsAdministrator;
			const string Hell = "Hell";
			var adminwarning = ((cfg[Constants.Core].Get(Hell)?.Value ?? null) != Constants.No);
			if (isadmin && adminwarning)
			{
				var rv = MessageBox.ShowModal(
					Name + "! – admin access!!??",
					"You're starting TM with admin rights, is this right?\n\nYou can cause bad system operation, such as complete system hang, if you configure or configured TM incorrectly.",
					MessageBox.Buttons.AcceptCancel);

				if (rv == MessageBox.ResultType.OK)
				{
					cfg[Constants.Core][Hell].Value = Constants.No;
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
			DebugAudio = dbgsec.Get(HumanReadable.Hardware.Audio.Section)?.Bool ?? false;

			DebugForeground = dbgsec.Get(HumanReadable.System.Process.Foreground)?.Bool ?? false;

			DebugPower = dbgsec.Get(HumanReadable.Hardware.Power.Section)?.Bool ?? false;
			DebugMonitor = dbgsec.Get(HumanReadable.Hardware.Monitor.Section)?.Bool ?? false;

			DebugSession = dbgsec.Get(Constants.Session)?.Bool ?? false;
			DebugResize = dbgsec.Get(Process.Constants.Resize)?.Bool ?? false;

			DebugMemory = dbgsec.Get(HumanReadable.Hardware.Memory)?.Bool ?? false;

			DebugCache = dbgsec.Get(Constants.Cache)?.Bool ?? false;

			var exsec = cfg[Constants.Experimental];
			LastModifiedList = exsec.Get(Constants.LastModified)?.Bool ?? false;
			TempMonitorEnabled = exsec.Get("Temp Monitor")?.Bool ?? false;
			int trecanalysis = exsec.Get("Record analysis")?.Int ?? 0;
			RecordAnalysis = trecanalysis > 0 ? (TimeSpan?)TimeSpan.FromSeconds(trecanalysis.Constrain(0, 180)) : null;
			IOPriorityEnabled = exsec.Get(Process.Constants.IOPriority)?.Bool ?? false;
			if (!MKAh.Execution.IsWin7)
			{
				Log.Warning("<Core> I/O priority was enabled. Requires Win7 which you don't appear to be running.");
				IOPriorityEnabled = false;
			}

#if DEBUG
			Trace = dbgsec.Get(Constants.Trace)?.Bool ?? false;
#endif

			using var uicfg = Config.Load(UIConfigFilename);
			VisualStyling = uicfg.Config[Constants.Windows].GetOrSet("Styling", true).Bool;

			// END DEBUG

			if (Trace)
			{
				Log.Debug($"<Core> Privilege level: {(isadmin ? "Admin" : "User")}");
				Log.Debug($"<Core> Path cache: {(PathCacheLimit == 0 ? HumanReadable.Generic.Disabled : PathCacheLimit.ToString())} items");
				Log.Debug($"<Core> Paging: {(PagingEnabled ? HumanReadable.Generic.Enabled : HumanReadable.Generic.Disabled)}");
			}

			LoadEvent?.Invoke(null, new LoadEventArgs("Core configuration loaded.", LoadEventType.Loaded));
		}

		static void InitializeComponents()
		{
			if (Trace) Log.Debug("<Core> Loading components...");

			var timer = System.Diagnostics.Stopwatch.StartNew();

			// This deals with a ReflectionTypeLoadException in the following LINQ, because wooo exceptions are so much better than returning something I can parse
			static Type[] GetTypes(System.Reflection.Assembly asm)
			{
				try
				{
					return asm.GetTypes();
				}
				catch (System.Reflection.ReflectionTypeLoadException ex)
				{
					return ex.Types.Where(t => t != null).ToArray();
				}
			}

			// Reflection. Really silly way to find out how many components we have.
			int componentsToLoad = (from asm in AppDomain.CurrentDomain.GetAssemblies().AsParallel()
									from t in GetTypes(asm)
									let len = t.GetCustomAttributes(typeof(ComponentAttribute), false).Length
									where len > 0
									select len).Sum();

			componentsToLoad++; // cache
			// above should result in 11

			LoadEvent?.Invoke(null, new LoadEventArgs("Component loading starting.", LoadEventType.Info, 0, componentsToLoad));


			using var cts = new System.Threading.CancellationTokenSource();
			Process.Utility.InitializeCache(); // TODO: Cache initialization needs to be better and more contextual
			LoadEvent?.Invoke(null, new LoadEventArgs("Cache loaded.", LoadEventType.SubLoaded));

			// Parallel loading, cuts down startup time some. C# ensures parallelism is not enforced.
			// This is really bad if something fails

			Task
				PowMan = (PowerManagerEnabled ? Task.Run(() => powermanager = new Power.Manager(), cts.Token) : Task.CompletedTask)
					.ContinueWith(_ => LoadEvent?.Invoke(null, new LoadEventArgs("Power manager processed.", LoadEventType.SubLoaded)), TaskContinuationOptions.OnlyOnRanToCompletion),
				CpuMon = (PowerManagerEnabled ? Task.Run(() => cpumonitor = new CPUMonitor(), cts.Token) : Task.CompletedTask)
					.ContinueWith(_ => LoadEvent?.Invoke(null, new LoadEventArgs("CPU monitor processed.", LoadEventType.SubLoaded)), TaskContinuationOptions.OnlyOnRanToCompletion),
				ProcMon = (ProcessMonitorEnabled ? Task.Run(() => processmanager = new Process.Manager(), cts.Token) : Task.CompletedTask)
					.ContinueWith(_ => LoadEvent?.Invoke(null, new LoadEventArgs("Process manager processed.", LoadEventType.SubLoaded)), TaskContinuationOptions.OnlyOnRanToCompletion),
				FgMon = (ActiveAppMonitorEnabled ? Task.Run(() => activeappmonitor = new Process.ForegroundManager(), cts.Token) : Task.CompletedTask)
					.ContinueWith(_ => LoadEvent?.Invoke(null, new LoadEventArgs("Foreground manager processed.", LoadEventType.SubLoaded)), TaskContinuationOptions.OnlyOnRanToCompletion),
				NetMon = (NetworkMonitorEnabled ? Task.Run(() => netmonitor = new Network.Manager(), cts.Token) : Task.CompletedTask)
					.ContinueWith(_ => LoadEvent?.Invoke(null, new LoadEventArgs("Network monitor processed.", LoadEventType.SubLoaded)), TaskContinuationOptions.OnlyOnRanToCompletion),
				StorMon = (StorageMonitorEnabled ? Task.Run(() => storagemanager = new StorageManager(), cts.Token) : Task.CompletedTask)
					.ContinueWith(_ => LoadEvent?.Invoke(null, new LoadEventArgs("Storage monitor processed.", LoadEventType.SubLoaded)), TaskContinuationOptions.OnlyOnRanToCompletion),
				HpMon = (HealthMonitorEnabled ? Task.Run(() => healthmonitor = new HealthMonitor(), cts.Token) : Task.CompletedTask)
					.ContinueWith(_ => LoadEvent?.Invoke(null, new LoadEventArgs("Health monitor processed.", LoadEventType.SubLoaded)), TaskContinuationOptions.OnlyOnRanToCompletion),
				HwMon = (HardwareMonitorEnabled ? Task.Run(() => hardware = new HardwareMonitor(), cts.Token) : Task.CompletedTask)
					.ContinueWith(_ => LoadEvent?.Invoke(null, new LoadEventArgs("Hardware monitor processed.", LoadEventType.SubLoaded)), TaskContinuationOptions.OnlyOnRanToCompletion),
				//AlMan = (AlertManagerEnabled ? Task.Run(() => alerts = new AlertManager(), cts.Token) : Task.CompletedTask)
				//	.ContinueWith(_ => LoadEvent?.Invoke(null, new LoadEventArgs("Alert manager processed.", LoadEventType.SubLoaded)), TaskContinuationOptions.OnlyOnRanToCompletion),
				SelfMaint = (Task.Run(() => selfmaintenance = new SelfMaintenance(), cts.Token))
					.ContinueWith(_ => LoadEvent?.Invoke(null, new LoadEventArgs("Self-maintenance manager processed.", LoadEventType.SubLoaded)), TaskContinuationOptions.OnlyOnRanToCompletion);

			Task[] init = new[] { PowMan, CpuMon, ProcMon, FgMon, NetMon, StorMon, HpMon, HwMon, /*AlMan,*/ SelfMaint };

			Exception[]? cex = null;
			if (cts.IsCancellationRequested) throw new InitFailure("Cancelled?", (cex?[0]), cex);
			foreach (var t in init) t.ContinueWith(t =>
			{
				cex = t.Exception?.InnerExceptions.ToArray() ?? null;
				Logging.DebugMsg("Module loading failed");
				cts.Cancel();
			}, TaskContinuationOptions.OnlyOnFaulted);

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
						micmonitor.Hook(audiomanager).ConfigureAwait(true);
						micmonitor.OnDisposed += (_, _ea) => micmonitor = null;
					}
				}
			}
			catch (InitFailure)
			{
				if (micmonitor != null)
				{
					micmonitor.Dispose();
					micmonitor = null;
				}
				if (audiomanager != null)
				{
					audiomanager.Dispose();
					audiomanager = null;
				}
				Logging.DebugMsg("AudioManager initialization failed");
				cts.Cancel(throwOnFirstException: false);
				throw;
			}

			if (cts.IsCancellationRequested) throw new InitFailure("Cancelled at 557", (cex?[0]), cex);

			// WinForms makes the following components not load nicely if not done here (main thread).
			//hiddenwindow.BeginInvoke(new Action(() => { trayaccess = new UI.TrayAccess(); })); // is there a point to this?
			trayaccess = new UI.TrayAccess();
			trayaccess.TrayMenuShown = visible => OptimizeResponsiviness(visible);

			ProcMon.ContinueWith((_, _discard) => trayaccess.Hook(processmanager), TaskContinuationOptions.OnlyOnRanToCompletion, cts.Token);

			Task.WhenAll(ProcMon, FgMon).ContinueWith(_ => activeappmonitor?.Hook(processmanager), TaskContinuationOptions.OnlyOnRanToCompletion);
			if (cts.IsCancellationRequested) throw new InitFailure("Cancelled at 565", (cex?[0]), cex);

			try
			{
				if (PowerManagerEnabled)
				{
					Task.WhenAll(PowMan, CpuMon, ProcMon).ContinueWith((_, _discard) =>
					  {
						  if (processmanager is null) throw new TaskCanceledException();

						  if (cpumonitor != null)
						  {
							  cpumonitor.Hook(processmanager);
							  powermanager?.Hook(cpumonitor);
						  }

						  if (powermanager != null)
						  {
							  trayaccess.Hook(powermanager);
							  processmanager.Hook(powermanager);
						  }
					  }, TaskContinuationOptions.OnlyOnRanToCompletion, cts.Token);

					var tr = Task.WhenAny(init);
					if (tr.IsFaulted)
					{
						cex = tr.Exception?.InnerExceptions.ToArray() ?? null;
						Logging.DebugMsg("Unknown initialization failed");
						cts.Cancel(true);
					}
				}

				//if (HardwareMonitorEnabled)
				//	Task.WhenAll(HwMon).ContinueWith((x) => hardware.Start()); // this is slow

				NetMon.ContinueWith((_, _discard) => netmonitor.Tray = trayaccess, TaskContinuationOptions.OnlyOnRanToCompletion, cts.Token);

				if (AudioManagerEnabled) ProcMon.ContinueWith((_, _discard) => audiomanager?.Hook(processmanager), TaskContinuationOptions.OnlyOnRanToCompletion, cts.Token);

				// WAIT for component initialization
				if (!Task.WaitAll(init, 5_000))
				{
					Log.Warning($"<Core> Components still loading ({timer.ElapsedMilliseconds} ms and ongoing)");
					if (!Task.WaitAll(init, 115_000)) // total wait time of 120 seconds
						throw new InitFailure($"Component initialization taking excessively long ({timer.ElapsedMilliseconds} ms), aborting.");
				}
			}
			catch (AggregateException ex)
			{
				foreach (var iex in ex.InnerExceptions)
					Logging.Stacktrace(iex, crashsafe: true);

				throw new InitFailure("Initialization failure", ex.InnerExceptions[0]);
				//System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
				//throw; // because compiler is dumb and doesn't understand the above
			}

			LoadEvent?.Invoke(null, new LoadEventArgs("Components loaded.", LoadEventType.Loaded));

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

			activeappmonitor?.SetupEventHooks();
			powermanager?.SetupEventHooks();
			audiomanager?.SetupEventHooks();
			netmonitor?.SetupEventHooks();

			if (GlobalHotkeys) trayaccess.RegisterGlobalHotkeys();

			LoadEvent?.Invoke(null, new LoadEventArgs("Events hooked.", LoadEventType.Loaded));

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

				int selfAffMask = SelfAffinity.Replace(0, Process.Utility.FullCPUMask);
				Log.Information($"<Core> Self-optimizing – Priority: {SelfPriority.ToString()}; Affinity: {HumanInterface.BitMask(selfAffMask, Process.Utility.CPUCount)}");

				self.ProcessorAffinity = new IntPtr(selfAffMask); // this should never throw an exception
				self.PriorityClass = SelfPriority;
			}

			if (Trace) Log.Verbose("Displaying Tray Icon");

			// UI
			if (State == Runstate.Normal)
			{
				if (ShowOnStart) BuildMainWindow(reveal: true, top: false);
				if (ShowVolOnStart) BuildVolumeMeter();
			}

			timer.Stop();

			LoadEvent?.Invoke(null, new LoadEventArgs("UI loaded.", LoadEventType.Loaded));

			Log.Information($"<Core> Component loading finished ({timer.ElapsedMilliseconds} ms). {DisposalChute.Count.ToString()} initialized.");
		}

		public static void CheckNGEN()
		{
			bool ni = MKAh.Program.NativeImage.Exists();
			Log.Information("<NGen> Native Image: " + (ni ? "Yes :D" : "No :("));
			if (!ni)
			{
				System.Threading.Tasks.Task.Run(new Action(() =>
				{
					bool ngen = Config.Load(CoreConfigFilename).Config[Constants.Experimental].Get(Constants.AutoNGEN)?.Bool ?? false;
					if (ngen)
					{
						if (MKAh.Execution.IsAdministrator)
						{
							using var proc = MKAh.Program.NativeImage.InstallOrUpdateCurrent();
							proc?.WaitForExit(15_000);
							if (proc?.ExitCode == 0)
								Log.Warning("<NGen> Native Image re-generated; please restart.");
						}
						else
							Log.Warning("<NGen> Native Image regeneration needed, unable to proceed without admin rights.");
					}
				})).ConfigureAwait(false);
			}
		}
	}
}
