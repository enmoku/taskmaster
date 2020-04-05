//
// TrayAccess.cs
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

using MKAh;
using MKAh.Synchronize;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Taskmaster.UI
{
	using static Application;

	public delegate void UIVisibleDelegate(bool visible);

	public class TrayShownEventArgs : EventArgs
	{
		public bool Visible { get; set; } = false;
	}

	/// <summary>
	/// Tray icon and main app control interface.
	/// </summary>
	// Form is used for catching some system events
	public class TrayAccess : IDisposable
	{
		readonly NotifyIcon Tray;
		readonly TrayWndProcProxy WndProcEventProxy;

		readonly ContextMenuStrip ms = new ContextMenuStrip();

		public event EventHandler<DisposedEventArgs>? OnDisposed;

		public UIVisibleDelegate? TrayMenuShown;

		readonly ToolStripMenuItem power_auto, power_highperf, power_balanced, power_saving, power_manual;

		// TODO: Remove phantom icons from previous crashed instances?
		public TrayAccess()
		{
			#region Build UI
			var IconCache = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);

			Tray = new NotifyIcon
			{
				Text = Application.Name + "!",
				Icon = IconCache,
			}; // Tooltip so people know WTF I am.

			//IconCacheMap.Add(0, IconCache);

			Tray.BalloonTipText = Tray.Text;

			#region Build UI
			if (Trace) Log.Verbose("Generating tray icon.");

			_ = ms.Handle;

			var menu_windowopen = new ToolStripMenuItem("Open main window", null, (_, _ea) => BuildMainWindow(reveal: true, top: true));
			var menu_volumeopen = new ToolStripMenuItem("Open volume meter", null, (_, _ea) => BuildVolumeMeter())
			{
				Enabled = AudioManagerEnabled,
			};
			var menu_rescan = new ToolStripMenuItem(HumanReadable.System.Process.Rescan, null, (_, _ea) => RescanRequest?.Invoke(this, EventArgs.Empty));
			var menu_configuration = new ToolStripMenuItem("Configuration");

			var menu_runatstart_sch = new ToolStripMenuItem("Schedule at login (Admin)", null, RunAtStartMenuClick_Sch);

			bool runatstartsch = RunAtStartScheduler(enabled: false, dryrun: true);
			menu_runatstart_sch.Checked = runatstartsch;

			Log.Information("<Core> Run-at-start scheduler: " + (runatstartsch ? "Found" : "Missing"));

			var powercfg = new ToolStripMenuItem(HumanReadable.Hardware.Power.Section, null, (_, _ea) => Config.PowerConfigWindow.Reveal(powermanager, centerOnScreen: true));
			powercfg.Enabled = Application.PowerManagerEnabled;
			menu_configuration.DropDownItems.Add(powercfg);
			menu_configuration.DropDownItems.Add(new ToolStripMenuItem("Advanced", null, (_, _ea) => Config.AdvancedConfig.Reveal(centerOnScreen: true)));
			menu_configuration.DropDownItems.Add(new ToolStripMenuItem("Components", null, (_, _ea) => Config.ComponentConfigurationWindow.Reveal(centerOnScreen: true))); // FIXME: MODAL
			menu_configuration.DropDownItems.Add(new ToolStripSeparator());
			menu_configuration.DropDownItems.Add(new ToolStripMenuItem("Experiments", null, (_, _ea) => Config.ExperimentConfig.Reveal(centerOnScreen: true)));
			menu_configuration.DropDownItems.Add(new ToolStripSeparator());
			menu_configuration.DropDownItems.Add(menu_runatstart_sch);
			menu_configuration.DropDownItems.Add(new ToolStripSeparator());
			menu_configuration.DropDownItems.Add(new ToolStripMenuItem("Open in file manager", null, (_, _ea) => System.Diagnostics.Process.Start(DataPath)));

			var menu_restart = new ToolStripMenuItem(HumanReadable.System.Process.Restart, null, (_s, _ea) => ConfirmExit(restart: true));
			var menu_exit = new ToolStripMenuItem(HumanReadable.System.Process.Exit, null, (_s, _ea) => ConfirmExit(restart: false));

			ms.Items.Add(menu_windowopen);
			ms.Items.Add(menu_volumeopen);
			ms.Items.Add(new ToolStripSeparator());
			ms.Items.Add(menu_rescan);
			ms.Items.Add(new ToolStripSeparator());
			ms.Items.Add(menu_configuration);

			if (PowerManagerEnabled)
			{
				power_auto = new ToolStripMenuItem(HumanReadable.Hardware.Power.AutoAdjust, null, SetAutoPower) { Checked = false, CheckOnClick = true, Enabled = false };

				power_highperf = new ToolStripMenuItem(Power.Utility.GetModeName(Power.Mode.HighPerformance), null, (_, _ea) => SetPower(Power.Mode.HighPerformance));
				power_balanced = new ToolStripMenuItem(Power.Utility.GetModeName(Power.Mode.Balanced), null, (_, _ea) => SetPower(Power.Mode.Balanced));
				power_saving = new ToolStripMenuItem(Power.Utility.GetModeName(Power.Mode.PowerSaver), null, (_, _ea) => SetPower(Power.Mode.PowerSaver));
				power_manual = new ToolStripMenuItem("Manual override", null, SetManualPower) { CheckOnClick = true };

				ms.Items.Add(new ToolStripSeparator());
				ms.Items.Add(new ToolStripLabel("– Power Plan –") { ForeColor = System.Drawing.SystemColors.GrayText });
				ms.Items.Add(power_auto);
				ms.Items.Add(power_highperf);
				ms.Items.Add(power_balanced);
				ms.Items.Add(power_saving);
				ms.Items.Add(power_manual);
			}

			ms.Items.Add(new ToolStripSeparator());
			ms.Items.Add(menu_restart);
			ms.Items.Add(menu_exit);
			Tray.ContextMenuStrip = ms;

			if (Trace) Log.Verbose("Tray menu ready");
			#endregion // Build UI

			// TODO: Toast Notifications. Apparently not supported by Win7, so nevermind.

			if (Tray.Icon is null)
			{
				Log.Fatal("<Tray> Icon missing, setting system default.");
				Tray.Icon = System.Drawing.SystemIcons.Application;
			}

			EnsureVisible().ConfigureAwait(true);
			#endregion // Build UI

			using var cfg = Application.Config.Load(CoreConfigFilename);
			int exdelay = cfg.Config[Constants.Experimental].Get("Explorer Restart")?.Int ?? 0;
			ExplorerRestartHelpDelay = exdelay > 0 ? (TimeSpan?)TimeSpan.FromSeconds(exdelay.Min(5)) : null;

			// Tray.Click += RestoreMainWindow;
			Tray.MouseClick += ShowWindow;

			//Microsoft.Win32.SystemEvents.SessionEnding += SessionEndingEvent; // depends on messagepump

			ms.VisibleChanged += MenuVisibilityChangedEvent;

			Tray.MouseDoubleClick += UnloseWindow;

			if (Trace) Log.Verbose("<Tray> Initialized");

			WndProcEventProxy = new TrayWndProcProxy();

			DisposalChute.Push(this); // nothing else seems to work for removing the tray icon
		}

		internal void Close()
		{
			try
			{
				Tray.ContextMenuStrip?.Close();
				Tray.ContextMenuStrip = null;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		/*
		System.Drawing.Icon IconCache = null;
		System.Drawing.Font IconFont = new System.Drawing.Font("Terminal", 8);
		System.Drawing.SolidBrush IconBrush = new System.Drawing.SolidBrush(System.Drawing.Color.White);

		readonly SortedDictionary<int,System.Drawing.Icon> IconCacheMap = new SortedDictionary<int, System.Drawing.Icon>();

		void UpdateIcon(int count = 0)
		{
			System.Drawing.Icon nicon = IconCacheMap[count];
			if (nicon is null)
			{
				using var bmp = new System.Drawing.Bitmap(32, 32);
				using var graphics = System.Drawing.Graphics.FromImage(bmp);
				graphics.DrawIcon(IconCache, 0, 0);
				graphics.DrawString(count.ToString(), IconFont, IconBrush, 1, 2);

				nicon = System.Drawing.Icon.FromHandle(bmp.GetHicon());

				IconCacheMap.Add(count, nicon);

				// TODO: Remove items based on least recently used with use count weight
			}

			Tray.Icon = nicon;
		}
		*/

		void MenuVisibilityChangedEvent(object sender, EventArgs _ea)
		{
			if (sender is ContextMenuStrip ms)
				TrayMenuShown?.Invoke(ms.Visible);
		}

		static void SessionEndingEvent(object _, Microsoft.Win32.SessionEndingEventArgs ea)
		{
			ea.Cancel = true;
			// is this safe?
			Log.Information("<OS> Session end signal received; Reason: " + ea.Reason.ToString());
			ExitCleanup();
			UnifiedExit();
			ea.Cancel = false;
		}

		public event EventHandler? RescanRequest;

		Process.Manager? processmanager = null;

		public void Hook(Process.Manager pman)
		{
			processmanager = pman;
			RescanRequest += (_, _ea) => processmanager?.HastenScan(TimeSpan.FromSeconds(15));

			RegisterExplorerExit();
		}

		Power.Manager? powermanager = null;

		public void Hook(Power.Manager pman)
		{
			powermanager = pman;

			PowerBehaviourEvent(this, new Power.PowerBehaviourEventArgs(powermanager.Behaviour));

			powermanager.PlanChange += HighlightPowerModeEvent;
			powermanager.BehaviourChange += PowerBehaviourEvent;

			bool pwauto = powermanager.Behaviour == Power.PowerBehaviour.Auto;
			bool pwmanual = powermanager.Behaviour == Power.PowerBehaviour.Manual;

			ms.BeginInvoke(new Action(() =>
			{
				power_auto.Checked = pwauto;
				power_manual.Checked = pwmanual;
				power_auto.Enabled = true;
			}));

			HighlightPowerMode();
		}

		void SetAutoPower(object _, EventArgs _ea)
		{
			if (powermanager.Behaviour != Power.PowerBehaviour.Auto)
				powermanager.SetBehaviour(Power.PowerBehaviour.Auto);
			else
				powermanager.SetBehaviour(Power.PowerBehaviour.RuleBased);
		}

		void SetManualPower(object _, EventArgs _ea)
		{
			if (powermanager.Behaviour != Power.PowerBehaviour.Manual)
				powermanager.SetBehaviour(Power.PowerBehaviour.Manual);
			else
				powermanager.SetBehaviour(Power.PowerBehaviour.RuleBased);
		}

		void HighlightPowerModeEvent(object _, Power.ModeEventArgs _ea) => HighlightPowerMode();

		void PowerBehaviourEvent(object sender, Power.PowerBehaviourEventArgs ea)
		{
			if (!ms.IsHandleCreated || IsDisposed) return;

			ms.BeginInvoke(new Action(() =>
			{
				switch (ea.Behaviour)
				{
					case Power.PowerBehaviour.Auto:
						power_auto.Checked = true;
						power_manual.Checked = false;
						break;
					case Power.PowerBehaviour.Manual:
						power_auto.Checked = false;
						power_manual.Checked = true;
						break;
					default:
						power_auto.Checked = false;
						power_manual.Checked = false;
						break;
				}
			}));
		}

		void HighlightPowerMode()
		{
			if (!ms.IsHandleCreated || IsDisposed) return;

			ms.BeginInvoke(new Action(() =>
			{
				switch (powermanager.CurrentMode)
				{
					case Power.Mode.Balanced:
						power_saving.Checked = false;
						power_balanced.Checked = true;
						power_highperf.Checked = false;
						break;
					case Power.Mode.HighPerformance:
						power_saving.Checked = false;
						power_balanced.Checked = false;
						power_highperf.Checked = true;
						break;
					case Power.Mode.PowerSaver:
						power_saving.Checked = true;
						power_balanced.Checked = false;
						power_highperf.Checked = false;
						break;
				}
			}));
		}

		void SetPower(Power.Mode mode)
		{
			try
			{
				if (DebugPower) Log.Debug("<Power> Setting behaviour to manual.");

				powermanager.SetBehaviour(Power.PowerBehaviour.Manual);

				if (DebugPower) Log.Debug("<Power> Setting manual mode: " + mode.ToString());

				// powermanager.Restore(0).Wait(); // already called by setBehaviour as necessary
				powermanager?.SetMode(mode, new Cause(OriginType.User));

				// powermanager.RequestMode(mode);
				HighlightPowerMode();
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		void ShowWindow(object _, MouseEventArgs e)
		{
			if (Trace) Log.Verbose("Tray Click");

			if (e.Button == MouseButtons.Left)
				BuildMainWindow(reveal: true, top: true);
		}

		MainWindow? mainwindow { get; set; } = null;

		void UnloseWindow(object _, MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				BuildMainWindow(reveal: true, top: true);

				try
				{
					mainwindow?.UnloseWindowRequest(); // null reference crash sometimes
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
					throw; // this is bad, but this is good way to force bail when this is misbehaving
				}
			}
		}

		void WindowClosed(object _, FormClosingEventArgs e)
		{
			switch (e.CloseReason)
			{
				case CloseReason.ApplicationExitCall:
					// CLEANUP: Console.WriteLine("BAIL:TrayAccess.WindowClosed");
					return;
			}

			mainwindow = null;
		}

		public void Hook(MainWindow window)
		{
			Debug.Assert(window != null, "Hooking null main window");

			mainwindow = window;

			window.FormClosing += WindowClosed;
		}

		TimeSpan? ExplorerRestartHelpDelay { get; } = null;

		async void ExplorerCrashEvent(object sender, EventArgs _ea)
		{
			await ExplorerCrashHandler((sender as System.Diagnostics.Process).Id).ConfigureAwait(false);
		}

		async Task ExplorerCrashHandler(int processId)
		{
			try
			{
				KnownExplorerInstances.TryRemove(processId, out _);

				if (KnownExplorerInstances.Count > 0)
				{
					if (Trace) Log.Verbose($"<Tray> Explorer #{processId} exited but is not the last known explorer instance.");
					return;
				}

				Log.Warning($"<Tray> Explorer #{processId} crash detected!");

				Log.Information("<Tray> Giving explorer some time to recover on its own...");

				await Task.Delay(TimeSpan.FromSeconds(12)).ConfigureAwait(false); // force async, 12 seconds

				var ExplorerRestartTimer = Stopwatch.StartNew();
				bool startAttempt = true;
				System.Diagnostics.Process[] procs;
				do
				{
					if (ExplorerRestartTimer.Elapsed.TotalHours >= 24)
					{
						Log.Error("<Tray> Explorer has not recovered in excessive timeframe, giving up.");
						return;
					}
					else if (startAttempt && ExplorerRestartHelpDelay.HasValue && ExplorerRestartTimer.Elapsed >= ExplorerRestartHelpDelay.Value)
					{
						// TODO: This shouldn't happen if the session is exiting.
						Log.Information("<Tray> Restarting explorer as per user configured timer.");
						var proc = System.Diagnostics.Process.Start(new ProcessStartInfo
						{
							FileName = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows), "explorer.exe"),
							UseShellExecute = true
						});
						startAttempt = false;
						proc?.Dispose(); // running process should be unaffected
					}

					await Task.Delay(TimeSpan.FromMinutes(5)).ConfigureAwait(false); // wait 5 minutes

					procs = ExplorerInstances;
				}
				while (procs.Length == 0);

				ExplorerRestartTimer.Stop();

				if (!RegisterExplorerExit(procs))
				{
					Log.Warning("<Tray> Explorer registration failed.");
					return;
				}

				EnsureVisible().ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				throw;
			}
		}

		readonly ConcurrentDictionary<int, int> KnownExplorerInstances = new ConcurrentDictionary<int, int>();
		static System.Diagnostics.Process[] ExplorerInstances => System.Diagnostics.Process.GetProcessesByName("explorer");

		bool RegisterExplorerExit(System.Diagnostics.Process[]? procs = null)
		{
			try
			{
				if (Trace) Log.Verbose("<Tray> Registering Explorer crash monitor.");
				// this is for dealing with notify icon disappearing on explorer.exe crash/restart

				if ((procs?.Length ?? 0) == 0) procs = ExplorerInstances;

				if (procs.Length > 0)
				{
					foreach (var proc in procs)
					{
						Process.ProcessEx? info;

						if (processmanager.GetCachedProcess(proc.Id, out info) || Process.Utility.Construct(proc.Id, out info))
						{
							// NOP
						}
						else
							continue;

						if (!string.IsNullOrEmpty(info.Path))
						{
							if (!info.Path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.InvariantCultureIgnoreCase))
							{
								if (Trace) Log.Verbose($"<Tray> Explorer #{info.Id} not in system root.");
								continue;
							}
						}

						if (KnownExplorerInstances.TryAdd(info.Id, 0))
						{
							proc.Exited += ExplorerCrashEvent;
							proc.EnableRaisingEvents = true;
						}
					}

					if (KnownExplorerInstances.Count > 0)
					{
						Log.Information("<Tray> Explorer (#" + string.Join(", #", KnownExplorerInstances.Keys) + ") being monitored for crashes.");
					}
					else
					{
						Log.Warning("<Tray> Explorer not found for monitoring.");
					}

					return true;
				}

				Log.Warning("<Tray> Explorer not found.");
				return false;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				throw;
			}
		}

		//public event EventHandler TrayTooltipClicked;

		public void Tooltip(int timeout, string message, string title, ToolTipIcon icon)
		{
			Tray.ShowBalloonTip(timeout, title, message, icon);
			//Tray.BalloonTipClicked += TrayTooltipClicked; // does this actually work for proxying?
		}

		// does this do anything really?
		public void RefreshVisibility()
		{
			hiddenwindow?.InvokeAsync(new Action(() =>
			{
				if (IsDisposed) return;

				Logging.DebugMsg("<Tray:Icon> Refreshing visibility");

				Tray.Visible = false;
				Tray.Visible = true;
			}));
		}

		int ensuringvisibility = 0;

		public async Task EnsureVisible()
		{
			if (!Atomic.Lock(ref ensuringvisibility)) return;

			try
			{
				int attempts = 0;
				Log.Debug("<Tray> Not visible, fixing...");

				do
				{
					Logging.DebugMsg("++ Tray visibility fix attempt #" + attempts.ToString());

					RefreshVisibility();

					if (attempts++ > 4)
					{
						Log.Fatal("<Tray> Failure to become visible after 5 attempts. Exiting to avoid ghost status.");
						UnifiedExit();
					}

					await Task.Delay(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
				}
				while (!Tray.Visible);
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				Atomic.Unlock(ref ensuringvisibility);
			}
		}

		void RunAtStartMenuClick_Sch(object sender, EventArgs _ea)
		{
			if (sender is ToolStripMenuItem menu_runatstart_sch)
			{
				try
				{
					if (!MKAh.Execution.IsAdministrator)
					{
						MessageBox.ShowModal(Application.Name + "! – run at login", "Scheduler can not be modified without admin rights.", MessageBox.Buttons.OK);
						return;
					}

					if (!menu_runatstart_sch.Checked)
					{
						if (MessageBox.ShowModal(Application.Name + "! – run at login", "This will add on-login scheduler to run TM as admin, is this right?", MessageBox.Buttons.AcceptCancel)
							== MessageBox.ResultType.Cancel) return;
					}
					// can't be disabled without admin rights?

					menu_runatstart_sch.Checked = RunAtStartScheduler(!menu_runatstart_sch.Checked);
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
				}
			}
		}

		/// <summary>
		/// This can run for a long time
		/// </summary>
		static bool RunAtStartScheduler(bool enabled, bool dryrun = false)
		{
			bool found = false;

			const string schexe = "schtasks";
			const string argsq = "/query /fo list /TN MKAh-Taskmaster";

			try
			{
				var info = new ProcessStartInfo(schexe)
				{
					WindowStyle = ProcessWindowStyle.Hidden
				};
				info.Arguments = argsq;

				var procfind = System.Diagnostics.Process.Start(info);
				bool rvq = false;
				bool warned = false;
				for (int i = 0; i < 3; i++)
				{
					rvq = procfind.WaitForExit(30_000);

					procfind.Refresh(); // unnecessary?
					if (procfind.HasExited) break;

					if (!warned)
					{
						Log.Debug("<Tray> Task Scheduler is taking long time to respond.");
						warned = true;
					}
				}

				if (rvq && procfind.ExitCode == 0) found = true;
				else if (!procfind.HasExited) procfind.Kill();
				procfind?.Dispose();

				if (Trace) Log.Debug($"<Tray> Scheduled task " + (found ? "" : "NOT ") + "found.");

				if (dryrun) return found; // this is bad, but fits the following logic

				if (found && enabled)
				{
					string argstoggle = "/change /TN MKAh-Taskmaster /" + (enabled ? "ENABLE" : "DISABLE");
					info.Arguments = argstoggle;
					var proctoggle = System.Diagnostics.Process.Start(info);
					bool toggled = proctoggle.WaitForExit(3000); // this will succeed as long as the task is there
					if (toggled && proctoggle.ExitCode == 0) Log.Information("<Tray> Scheduled task found and enabled");
					else Log.Error("<Tray> Scheduled task NOT toggled.");
					if (!proctoggle.HasExited) proctoggle.Kill();
					proctoggle?.Dispose();

					return true;
				}

				if (enabled)
				{
					// This will solve the high privilege problem, but really? Do I want to?
					var runtime = Environment.GetCommandLineArgs()[0];
					string argscreate = "/Create /tn MKAh-Taskmaster /tr \"\\\"" + runtime + "\\\"\" /sc onlogon /it /RL HIGHEST";
					info.Arguments = argscreate;
					var procnew = System.Diagnostics.Process.Start(info);
					bool created = procnew.WaitForExit(3000);

					if (created && procnew.ExitCode == 0) Log.Information("<Tray> Scheduled task created.");
					else Log.Error("<Tray> Scheduled task NOT created.");

					if (!procnew.HasExited) procnew.Kill();
					procnew?.Dispose();
				}
				else
				{
					const string argsdelete = "/Delete /TN MKAh-Taskmaster /F";
					info.Arguments = argsdelete;

					var procdel = System.Diagnostics.Process.Start(info);
					bool deleted = procdel.WaitForExit(3000);

					if (deleted && procdel.ExitCode == 0) Log.Information("<Tray> Scheduled task deleted.");
					else Log.Error("<Tray> Scheduled task NOT deleted.");

					if (!procdel.HasExited) procdel.Kill();
					procdel?.Dispose();
				}

				//if (toggled) Log.Debug("<Tray> Scheduled task toggled.");

				return enabled;
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				throw;
			}
		}

		~TrayAccess() => Dispose(false);

		// Vista or later required
		[DllImport("user32.dll")]
		internal extern static bool ShutdownBlockReasonCreate(IntPtr hWnd, [MarshalAs(UnmanagedType.LPWStr)] string pwszReason);

		// Vista or later required
		[DllImport("user32.dll")]
		internal extern static bool ShutdownBlockReasonDestroy(IntPtr hWnd);

		#region IDisposable Support
		public bool IsDisposed { get; internal set; } = false;

		protected virtual void Dispose(bool disposing)
		{
			if (IsDisposed) return;
			IsDisposed = true;

			//Microsoft.Win32.SystemEvents.SessionEnding -= SessionEndingEvent; // leaks if not disposed

			if (disposing)
			{
				if (Trace) Log.Verbose("Disposing tray...");

				ms.Dispose();

				WndProcEventProxy.Dispose();

				RescanRequest = null;

				if (powermanager != null)
				{
					powermanager.PlanChange -= HighlightPowerModeEvent;
					powermanager = null;
				}

				Tray.Visible = false;
				Tray.Dispose();

				power_auto?.Dispose();
				power_highperf?.Dispose();
				power_balanced?.Dispose();
				power_saving?.Dispose();
				power_manual?.Dispose();

				//base.Dispose();

				OnDisposed?.Invoke(this, new DisposedEventArgs());
				OnDisposed = null;
			}
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		internal void RegisterGlobalHotkeys() => WndProcEventProxy?.RegisterGlobalHotkeys();
		#endregion
	}
}