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
using System.Windows.Forms;
using MKAh;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Taskmaster
{
	public static partial class Taskmaster
	{
		public static string GitURL => "https://github.com/mkahvi/taskmaster";
		public static string ItchURL => "https://mkah.itch.io/taskmaster";

		public static readonly string Name = (System.Reflection.Assembly.GetExecutingAssembly()).GetName().Name;
		public static readonly string Version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();

		public static string DataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MKAh", Name);
		const string LogFolder = "Logs";
		public static string LogPath = Path.Combine(DataPath, LogFolder);

		public static string ConfigVersion = "alpha.3";

		public static Configuration.Manager Config = null;

		/// <summary>
		/// For making sure disposal happens and that it does so in main thread.
		/// </summary>
		internal static Stack<IDisposable> DisposalChute = new Stack<IDisposable>();

		public static OS.HiddenWindow hiddenwindow;

		static Runstate State = Runstate.Normal;

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
					(restart ? HumanReadable.System.Process.Restart : HumanReadable.System.Process.Exit) + Name + " ???",
					(string.IsNullOrEmpty(message) ? "" : message + "\n\n") +
					"Are you sure?",
					SimpleMessageBox.Buttons.AcceptCancel);
			}
			if (rv != SimpleMessageBox.ResultType.OK) return;

			UnifiedExit(restart, elevate:admin);
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


		public static void UnifiedExit(bool restart = false, bool elevate = false)
		{
			State = restart ? Runstate.Restart : Runstate.Exit;
			if (elevate && restart) RestartElevated = true;

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
				volumemeter = new UI.VolumeMeter(audiomanager);
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

		public static event EventHandler OnStart;

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

		static void LicenseBoiler()
		{
			using (var cfg = Config.Load(CoreConfigFilename).BlockUnload())
			{
				if (cfg.Config.Get(Constants.Core)?.Get(Constants.License)?.Value.Equals(Constants.Accepted) ?? false) return;
			}

			using (var license = new LicenseDialog())
			{
				license.ShowDialog();
				if (!license.DialogOK)
				{
					UnifiedExit();
					throw new RunstateException("License not accepted.", Runstate.QuickExit);
				}
			}
		}

		public static bool Portable { get; internal set; } = false;
		static void TryPortableMode()
		{
			string portpath = Path.Combine(Path.GetDirectoryName(Environment.GetCommandLineArgs()[0]), Constants.Config);
			if (Directory.Exists(portpath))
			{
				DataPath = portpath;
				Portable = true;
			}
			else if (!File.Exists(Path.Combine(DataPath, CoreConfigFilename)))
			{
				if (SimpleMessageBox.ShowModal(Name + " setup", "Set up PORTABLE installation?", SimpleMessageBox.Buttons.AcceptCancel)
					== SimpleMessageBox.ResultType.OK)
				{
					DataPath = portpath;
					Portable = true;
					System.IO.Directory.CreateDirectory(DataPath); // this might fail, but we don't really care.
				}
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
				{
					var startTimer = Stopwatch.StartNew();

					bool ni = MKAh.Program.NativeImage.Exists();

					//Debug.Listeners.Add(new TextWriterTraceListener(System.Console.Out));

					NativeMethods.SetErrorMode(NativeMethods.SetErrorMode(NativeMethods.ErrorModes.SEM_SYSTEMDEFAULT) | NativeMethods.ErrorModes.SEM_NOGPFAULTERRORBOX | NativeMethods.ErrorModes.SEM_FAILCRITICALERRORS);

					System.Windows.Forms.Application.SetUnhandledExceptionMode(UnhandledExceptionMode.Automatic);
					System.Windows.Forms.Application.ThreadException += UnhandledUIException;
					System.Windows.Forms.Application.EnableVisualStyles(); // required by shortcuts and high dpi-awareness
					System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false); // required by high dpi-awareness

					AppDomain.CurrentDomain.UnhandledException += UnhandledException;

					//hiddenwindow = new OS.HiddenWindow();

					TryPortableMode();
					LogPath = Path.Combine(DataPath, LogFolder);

					// Singleton
					singleton = new System.Threading.Mutex(true, SingletonID, out bool mutexgained);
					if (!mutexgained)
					{
						SimpleMessageBox.ResultType rv = SimpleMessageBox.ResultType.Cancel;

						// already running, signal original process
						using (var msg = new SimpleMessageBox(Name+"!",
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
					{
						var logswitch = new LoggingLevelSwitch(LogEventLevel.Information);

#if DEBUG
						loglevelswitch.MinimumLevel = LogEventLevel.Debug;
						if (Trace) loglevelswitch.MinimumLevel = LogEventLevel.Verbose;
#endif

						var logpathtemplate = System.IO.Path.Combine(LogPath, Name+"-{Date}.log");
						Serilog.Log.Logger = new Serilog.LoggerConfiguration()
							.MinimumLevel.ControlledBy(loglevelswitch)
							.WriteTo.Console(levelSwitch: new LoggingLevelSwitch(LogEventLevel.Verbose))
							.WriteTo.RollingFile(logpathtemplate, outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
								levelSwitch: new LoggingLevelSwitch(Serilog.Events.LogEventLevel.Debug), retainedFileCountLimit: 3)
							.WriteTo.MemorySink(levelSwitch: logswitch)
							.CreateLogger();
					}

					// COMMAND-LINE ARGUMENTS
					ParseArguments(args);
					args = null; // silly

					// STARTUP

					{
						var builddate = BuildDate();

						var now = DateTime.Now;
						var age = (now - builddate).TotalDays;
						
						var sbs = new StringBuilder()
							.Append(Name).Append("! (#").Append(Process.GetCurrentProcess().Id).Append(")")
							.Append(MKAh.Execution.IsAdministrator ? " [ADMIN]" : "").Append(Portable ? " [PORTABLE]" : "")
							.Append(" – Version: ").Append(Version)
							.Append(" – Built: ").Append(builddate.ToString("yyyy/MM/dd HH:mm")).Append($" [{age:N0} days old]");
						Log.Information(sbs.ToString());
					}

					Log.Information("<NGen> Native Image: " + (ni ? "Yes :D" : "No :("));

					//PreallocLastLog();

					InitialConfiguration();
					LoadCoreConfig();
					InitializeComponents();

					Config.Flush(); // early save of configs

					if (RestartCounter > 0 && Trace) Log.Debug($"<Core> Restarted {RestartCounter.ToString()} time(s)");
					startTimer.Stop();

					Log.Information($"<Core> Initialization complete ({startTimer.ElapsedMilliseconds} ms)...");
					startTimer = null;
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

				Log.Information(Name + "! (#" + Process.GetCurrentProcess().Id + ") END! [Clean]");

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