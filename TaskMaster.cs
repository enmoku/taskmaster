//
// Taskmaster.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2016-2020 M.A.
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

using Serilog;
using Serilog.Core;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Taskmaster
{
	public static partial class Application
	{
		internal static System.Uri GitURL => new System.Uri("https://github.com/mkahvi/taskmaster", UriKind.Absolute);
		internal static System.Uri ItchURL => new System.Uri("https://mkah.itch.io/taskmaster", UriKind.Absolute);

		internal static string Name => (System.Reflection.Assembly.GetExecutingAssembly()).GetName().Name;
		internal static string Version => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();

		internal static string DataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MKAh", Name);
		internal const string LogFolder = "Logs";
		internal static string LogPath = string.Empty;

		internal static string ConfigVersion = "alpha.3";

		internal static Configuration.Manager Config = new Configuration.Manager.Null();

		/// <summary>
		/// For making sure disposal happens and that it does so in main thread.
		/// </summary>
		internal static Stack<IDisposable> DisposalChute = new Stack<IDisposable>();

		static Runstate State = Runstate.Normal;

		internal static void ConfirmExit(bool restart = false, bool admin = false, string message = null, bool alwaysconfirm = false)
		{
			var rv = UI.MessageBox.ResultType.OK;

			if (alwaysconfirm || ExitConfirmation)
			{
				rv = UI.MessageBox.ShowModal(
					(restart ? HumanReadable.System.Process.Restart : HumanReadable.System.Process.Exit) + Name + " ???",
					(string.IsNullOrEmpty(message) ? "" : message + "\n\n") +
					"Are you sure?",
					UI.MessageBox.Buttons.AcceptCancel);
			}
			if (rv != UI.MessageBox.ResultType.OK) return;

			UnifiedExit(restart, elevate: admin);
		}

		static bool CleanedUp = false;

		internal static void ExitCleanup(ModuleManager modules)
		{
			if (CleanedUp) return;

			try
			{
				if (!(modules.mainwindow?._Disposed ?? true)) modules.mainwindow.Enabled = false;
				if (!(modules.trayaccess?.IsDisposed ?? true)) modules.trayaccess?.Close();

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

				GlobalIPC.Dispose();
			}
			catch
			{
				throw; // to make sure finally is run
			}
			finally
			{
				CleanedUp = true;
			}
		}

		public class ShutDownEventArgs : EventArgs { }

		static event EventHandler<ShutDownEventArgs> ShuttingDown;

		internal static void RegisterForExit(IShutdownEventReceiver disposable)
		{
			ShuttingDown += disposable.ShutdownEvent;
		}

		internal static void UnifiedExit(bool restart = false, bool elevate = false)
		{
			State = restart ? Runstate.Restart : Runstate.Exit;
			if (elevate && restart) RestartElevated = true;

			ShuttingDown?.Invoke(null, new ShutDownEventArgs());

			// OPTIOANAL: Start a new thread that checks if this completes within a timeframe and if it does not, terminate the entire process.

			//if (System.Windows.Forms.Application.MessageLoop) // fails if called from another thread
			System.Windows.Forms.Application.Exit();

			// nothing else should be needed.
		}

		internal static void BuildVolumeMeter(ModuleManager modules)
		{
			if (!AudioManagerEnabled) return;

			lock (window_creation_lock)
			{
				if (modules.volumemeter is null)
				{
					modules.volumemeter = new UI.VolumeMeter(modules.audiomanager);
					modules.volumemeter.OnDisposed += (_, _2) => modules.volumemeter = null;
				}
			}
		}

		internal static readonly object window_creation_lock = new object();

		/// <summary>
		/// Constructs and hooks the main window
		/// </summary>
		internal static void BuildMainWindow(ModuleManager modules, bool reveal = false, bool top = false)
		{
			var mainwindow = modules.mainwindow;
			Logging.DebugMsg("<Main Window> Building: " + !(mainwindow is null));

			try
			{
				lock (window_creation_lock)
				{
					if (mainwindow != null)
					{
						Logging.DebugMsg("<Main Window> Already exists.");
						mainwindow.Reveal(activate: true);
						return;
					}

					mainwindow = modules.mainwindow = mainwindow = new UI.MainWindow(modules);

					//mainwindow = new UI.MainWindow();

					mainwindow.FormClosed += (_, _2) => modules.mainwindow = null;

					try
					{
						if (modules.storagemanager != null)
							mainwindow.Hook(modules.storagemanager);

						if (modules.processmanager != null)
							mainwindow.Hook(modules.processmanager);

						if (modules.audiomanager != null)
						{
							mainwindow.Hook(modules.audiomanager);
							if (modules.micmonitor != null)
								mainwindow.Hook(modules.micmonitor);
						}

						if (modules.netmonitor != null)
							mainwindow.Hook(modules.netmonitor);

						if (modules.activeappmonitor != null)
							mainwindow.Hook(modules.activeappmonitor);

						if (modules.powermanager != null)
							mainwindow.Hook(modules.powermanager);

						if (modules.cpumonitor != null)
							mainwindow.Hook(modules.cpumonitor);

						if (modules.hardware != null)
							mainwindow.Hook(modules.hardware);

						if (modules.healthmonitor != null)
							mainwindow.Hook(modules.healthmonitor);

						modules.trayaccess.Hook(mainwindow);

						// .GotFocus and .LostFocus are apparently unreliable as per the API
						mainwindow.Activated += (_, _2) => OptimizeResponsiviness(true);
						mainwindow.Deactivate += (_, _2) => OptimizeResponsiviness(false);
					}
					catch (Exception ex)
					{
						Logging.Stacktrace(ex);
						if (ex is NullReferenceException) throw;
					}
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				if (reveal) mainwindow?.Reveal(activate: top);
			}
		}

		internal static void OptimizeResponsiviness(bool shown = false)
		{
			var self = System.Diagnostics.Process.GetCurrentProcess();

			if (shown)
			{
				if (SelfOptimizeBGIO)
					MKAh.Utility.DiscardExceptions(() => Process.Utility.UnsetBackground(self));

				self.PriorityClass = ProcessPriorityClass.AboveNormal;
			}
			else
			{
				self.PriorityClass = SelfPriority;

				if (SelfOptimizeBGIO)
					MKAh.Utility.DiscardExceptions(() => Process.Utility.SetBackground(self));
			}
		}

		public static event EventHandler OnStart;

		static DirectoryInfo? TempRunningDir = null;

		const string RunningTestFile = ".running-TM0";

		static void MonitorCleanShutdown()
		{
			TempRunningDir = new System.IO.DirectoryInfo(System.IO.Path.Combine(DataPath, RunningTestFile));

			if (!TempRunningDir.Exists)
			{
				TempRunningDir.Create();
				TempRunningDir.Attributes = FileAttributes.Directory | FileAttributes.Hidden; // this doesn't appear to work
			}
			else
				Log.Warning("Unclean shutdown.");
		}

		static void CleanShutdown() => TempRunningDir?.Delete();

		static void LicenseBoiler()
		{
			using var cfg = Config.Load(CoreConfigFilename);
			if (cfg.Config.Get(Constants.Core)?.Get(Constants.License)?.String.Equals(Constants.Accepted, StringComparison.InvariantCulture) ?? false) return;

			using var license = new UI.LicenseDialog();
			license.ShowDialog();
			if (!license.DialogOK)
			{
				UnifiedExit();
				throw new RunstateException("License not accepted.", Runstate.QuickExit);
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
				if (UI.MessageBox.ShowModal(Name + " setup", "Set up PORTABLE installation?", UI.MessageBox.Buttons.AcceptCancel)
					== UI.MessageBox.ResultType.OK)
				{
					DataPath = portpath;
					Portable = true;
					System.IO.Directory.CreateDirectory(DataPath); // this might fail, but we don't really care.
				}
			}
		}

		const string SingletonID = "088f7210-51b2-4e06-9bd4-93c27a973874.taskmaster"; // garbage

		internal static LoggingLevelSwitch
			loglevelswitch = new LoggingLevelSwitch(LogEventLevel.Information),
			uiloglevelswitch = new LoggingLevelSwitch(LogEventLevel.Information);

		static internal event EventHandler<LoadEventArgs>? LoadEvent;

		//readonly static System.Threading.ManualResetEvent UIWaiter = new System.Threading.ManualResetEvent(false); // for splash

		internal static IPC GlobalIPC;

		internal static ModuleManager globalmodules;

		// entry point to the application
		[LoaderOptimization(LoaderOptimization.SingleDomain)] // lol, what's the point of this anyway?
		[STAThread] // supposedly needed to avoid shit happening with the WinForms GUI and other GUI toolkits
		static public int Main(string[] args)
		{
			var modules = globalmodules = new ModuleManager();
			GlobalIPC = new IPC();

			AppDomain.CurrentDomain.ProcessExit += (_, _2) => ExitCleanup(modules);

			try
			{
				using var singleton = Initialize(ref args, modules);

				CheckNGEN();

				if (State == Runstate.Normal)
				{
					OnStart?.Invoke(null, EventArgs.Empty);
					OnStart = null;

					// UI
					//trayaccess.RefreshVisibility();
					//UIWaiter.WaitOne();

					System.Windows.Forms.Application.Run(); // WinForms

					// System.Windows.Application.Current.Run(); // WPF
				}

				if (SelfOptimize) // return decent processing speed to quickly exit
				{
					var self = System.Diagnostics.Process.GetCurrentProcess();
					self.PriorityClass = ProcessPriorityClass.AboveNormal;
					if (SelfOptimizeBGIO)
						MKAh.Utility.DiscardExceptions(() => Process.Utility.SetBackground(self));
				}

				Log.Information("Exiting...");

				ExitCleanup(modules);

				PrintStats();

				CleanShutdown();

				Config.Dispose();

				Log.Information(Name + "! #" + System.Diagnostics.Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) + " END! [Clean]");

				if (State == Runstate.Restart) // happens only on power resume (waking from hibernation) or when manually set
				{
					singleton?.Dispose();
					Restart();
					return 0;
				}
			}
			catch (InitFailure ex)
			{
				Log.Error("<Init> Error: " + ex.Message);

				if (!ex.Voluntary) Logging.Stacktrace(ex, crashsafe: true);

				var sbs = new StringBuilder("Initialization failed!\n\nError: ");
				sbs.AppendLine(ex.Message);
				if (ex.InnerExceptions?.Length > 0)
				{
					foreach (var subex in ex.InnerExceptions)
					{
						sbs.AppendLine("--- Inner Exception ---")
							.AppendLine(subex.Message);
						sbs.AppendLine(Logging.PruneStacktrace(subex.StackTrace));

						if (subex.InnerException != null)
						{
							sbs.AppendLine("--- Deep Inner Exception ---")
								.AppendLine(subex.InnerException.Message);
							sbs.AppendLine(Logging.PruneStacktrace(subex.InnerException.StackTrace));
						}
					}
				}
				sbs.Append("\nRetry?");

				var msg = UI.MessageBox.ShowModal("Taskmaster – Initialization failed", sbs.ToString(), UI.MessageBox.Buttons.AcceptCancel, UI.MessageBox.Type.Plain);
				if (msg == UI.MessageBox.ResultType.OK)
				{
					Log.Information("<Init> User requested restart.");
					Close(modules);
					Application.Restart();
				}

				return -1; // should trigger finally block
			}
			catch (RunstateException ex)
			{
				if (Trace) Log.Debug("<Core> Exit trigger: " + ex.State.ToString());

				switch (ex.State)
				{
					case Runstate.CriticalFailure:
						Logging.Stacktrace(ex.InnerException ?? ex, crashsafe: true);
						System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException ?? ex).Throw();
						// throw;
						return -1;
					case Runstate.Normal:
					case Runstate.Exit:
					case Runstate.QuickExit:
					case Runstate.Restart:
						return 0;
				}

				return -1; // should trigger finally block
			}
			catch (Exception ex)
			{
				Logging.DebugMsg(ex.Message);
				Logging.DebugMsg(ex.StackTrace);
				Logging.Stacktrace(ex, crashsafe: true);

				return 1; // should trigger finally block
			}
			finally
			{
				try
				{
					Close(modules);
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex, crashsafe: true);
					throw;
				}
			}

			return 0;
		}

		static void PrintStats()
		{
			Log.Information($"<Stat> WMI polling: {Statistics.WMIPollTime:0.##}s [{Statistics.WMIPolling.ToString(CultureInfo.InvariantCulture)}]");
			Log.Information($"<Stat> Self-maintenance: {Statistics.MaintenanceTime:0.##}s [{Statistics.MaintenanceCount.ToString(CultureInfo.InvariantCulture)}]");
			Log.Information($"<Stat> Path cache: {Statistics.PathCacheHits.ToString(CultureInfo.InvariantCulture)} hits, {Statistics.PathCacheMisses.ToString(CultureInfo.InvariantCulture)} misses");
			var sbs = new StringBuilder("<Stat> Path finding: ", 256)
				.Append(Statistics.PathFindAttempts).Append(" total attempts; ")
				.Append(Statistics.PathFindViaModule).Append(" via module info, ")
				.Append(Statistics.PathFindViaC).Append(" via C call, ")
				.Append(Statistics.PathNotFound).Append(" not found");
			Log.Information(sbs.ToString());
			Log.Information($"<Stat> Processes modified: {Statistics.TouchCount.ToString(CultureInfo.InvariantCulture)}; Ignored for remodification: {Statistics.TouchIgnore.ToString(CultureInfo.InvariantCulture)}");
		}

		public static void Refresh(ModuleManager modules)
		{
			if (State != Runstate.Normal) return;

			try
			{
				hiddenwindow?.InvokeAsync(new Action(async () =>
				{
					await modules.trayaccess.EnsureVisible().ConfigureAwait(true);
				}));
				Config.Flush();
			}
			catch (InvalidOperationException ex) // Should only happen if hidden window handle is missing.
			{
				Log.Error("<Core> Exception: " + ex.Message);
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
				if (!System.IO.File.Exists(System.Windows.Forms.Application.ExecutablePath))
					Log.Fatal("Executable missing: " + System.Windows.Forms.Application.ExecutablePath); // this should be "impossible"

				CommandLine.NewProcessInfo(out var info, RestartElevated);

				Log.CloseAndFlush();

				System.Diagnostics.Process.Start(info);
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
			Logging.Stacktrace(ex, crashsafe: true);

			if (ea.IsTerminating)
			{
				Log.Warning("Runtime terminating!");

				try
				{
					Close(globalmodules);
				}
				catch { }

				Log.CloseAndFlush();
			}
		}

		static void Close(ModuleManager modules)
		{
			try
			{
				ExitCleanup(modules);
				Config.Dispose();
				Log.CloseAndFlush();
			}
			catch (Exception ex) when (!(ex is OutOfMemoryException))
			{
				Logging.Stacktrace(ex, crashsafe: true);
			}
		}

		public static DateTime BuildDate() => DateTime.ParseExact(Properties.Resources.BuildDate.Trim(), "yyyy/MM/dd HH:mm:ss K", null, System.Globalization.DateTimeStyles.None).ToUniversalTime();

		internal static void UpdateStyling()
			=> System.Windows.Forms.Application.VisualStyleState = VisualStyling ? System.Windows.Forms.VisualStyles.VisualStyleState.ClientAndNonClientAreasEnabled : System.Windows.Forms.VisualStyles.VisualStyleState.NoneEnabled;

		/// <summary>
		/// Process unhandled WinForms exceptions.
		/// </summary>
		static void UnhandledUIException(object _, System.Threading.ThreadExceptionEventArgs ea) => Logging.Stacktrace(ea.Exception);
	}
}