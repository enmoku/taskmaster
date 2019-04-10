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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using MKAh;
using Serilog;

namespace Taskmaster.UI
{
	using static Taskmaster;

	sealed public class TrayShownEventArgs : EventArgs
	{
		public bool Visible = false;
	}

	/// <summary>
	///
	/// </summary>
	// Form is used for catching some system events
	sealed public class TrayAccess : UI.UniForm //, IDisposable
	{
		NotifyIcon Tray;

		public event EventHandler<TrayShownEventArgs> TrayMenuShown;

		ToolStripMenuItem power_auto;
		ToolStripMenuItem power_highperf;
		ToolStripMenuItem power_balanced;
		ToolStripMenuItem power_saving;
		ToolStripMenuItem power_manual;

		public TrayAccess()
			: base()
		{
			// BUILD UI
			IconCache = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);

			Tray = new NotifyIcon {
				Text = $"{System.Windows.Forms.Application.ProductName}!",
				Icon = IconCache,
			}; // Tooltip so people know WTF I am.

			IconCacheMap.Add(0, IconCache);

			Tray.BalloonTipText = Tray.Text;
			Tray.Disposed += (_, _ea) => Tray = null;

			if (Trace) Log.Verbose("Generating tray icon.");

			var ms = new ContextMenuStrip();
			var menu_windowopen = new ToolStripMenuItem("Open main window", null, (_, _ea) => ShowMainWindow());
			var menu_volumeopen = new ToolStripMenuItem("Open volume meter", null, (_, _ea) => BuildVolumeMeter())
			{
				Enabled = AudioManagerEnabled,
			};
			var menu_rescan = new ToolStripMenuItem(HumanReadable.System.Process.Rescan, null, (o, s) => RescanRequest?.Invoke(this, EventArgs.Empty));
			var menu_configuration = new ToolStripMenuItem("Configuration");

			var menu_runatstart_sch = new ToolStripMenuItem("Schedule at login (Admin)", null, RunAtStartMenuClick_Sch);

			bool runatstartsch = RunAtStartScheduler(enabled: false, dryrun: true);
			menu_runatstart_sch.Checked = runatstartsch;

			Log.Information("<Core> Run-at-start scheduler: " + (runatstartsch ? "Found" : "Missing"));

			menu_configuration.DropDownItems.Add(new ToolStripMenuItem("Power", null, (_, _ea) => Config.PowerConfigWindow.Reveal(powermanager, centerOnScreen:true)));
			menu_configuration.DropDownItems.Add(new ToolStripMenuItem("Advanced", null, (_, _ea) => Config.AdvancedConfig.Reveal(centerOnScreen:true))); // FIXME: MODAL
			menu_configuration.DropDownItems.Add(new ToolStripMenuItem("Components", null, (_, _ea) => Config.ComponentConfigurationWindow.Reveal(centerOnScreen:true))); // FIXME: MODAL
			menu_configuration.DropDownItems.Add(new ToolStripSeparator());
			menu_configuration.DropDownItems.Add(new ToolStripMenuItem("Experiments", null, (_,_ea) => Config.ExperimentConfig.Reveal(centerOnScreen:true))); // FIXME: MODAL
			menu_configuration.DropDownItems.Add(new ToolStripSeparator());
			menu_configuration.DropDownItems.Add(menu_runatstart_sch);
			menu_configuration.DropDownItems.Add(new ToolStripSeparator());
			menu_configuration.DropDownItems.Add(new ToolStripMenuItem("Open in file manager", null, (_, _ea) => Process.Start(DataPath)));

			var menu_restart = new ToolStripMenuItem("Restart", null, (_s, _ea) => ConfirmExit(restart: true));
			var menu_exit = new ToolStripMenuItem("Exit", null, (_s, _ea) => ConfirmExit(restart: false));

			ms.Items.Add(menu_windowopen);
			ms.Items.Add(menu_volumeopen);
			ms.Items.Add(new ToolStripSeparator());
			ms.Items.Add(menu_rescan);
			ms.Items.Add(new ToolStripSeparator());
			ms.Items.Add(menu_configuration);

			if (PowerManagerEnabled)
			{
				power_auto = new ToolStripMenuItem(HumanReadable.Hardware.Power.AutoAdjust, null, SetAutoPower) { Checked = false, CheckOnClick = true, Enabled = false };

				power_highperf = new ToolStripMenuItem(Power.Manager.GetModeName(Power.Mode.HighPerformance), null, (s, e) => SetPower(Power.Mode.HighPerformance));
				power_balanced = new ToolStripMenuItem(Power.Manager.GetModeName(Power.Mode.Balanced), null, (s, e) => SetPower(Power.Mode.Balanced));
				power_saving = new ToolStripMenuItem(Power.Manager.GetModeName(Power.Mode.PowerSaver), null, (s, e) => SetPower(Power.Mode.PowerSaver));
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

			using (var cfg = Taskmaster.Config.Load(CoreConfigFilename).BlockUnload())
			{
				int exdelay = cfg.Config["Experimental"].Get("Explorer Restart")?.IntValue ?? 0;
				ExplorerRestartHelpDelay = exdelay > 0 ? (TimeSpan?)TimeSpan.FromSeconds(exdelay.Min(5)) : null;
			}

			RegisterExplorerExit();

			// Tray.Click += RestoreMainWindow;
			Tray.MouseClick += ShowWindow;

			// TODO: Toast Notifications. Apparently not supported by Win7, so nevermind.

			if (Tray.Icon == null)
			{
				Log.Fatal("<Tray> Icon missing, setting system default.");
				Tray.Icon = System.Drawing.SystemIcons.Application;
			}

			Microsoft.Win32.SystemEvents.SessionEnding += SessionEndingEvent; // depends on messagepump

			EnsureVisible();

			ms.VisibleChanged += MenuVisibilityChangedEvent;

			if (Trace) Log.Verbose("<Tray> Initialized");

			DisposalChute.Push(this);
		}

		System.Drawing.Icon IconCache = null;
		System.Drawing.Font IconFont = new System.Drawing.Font("Terminal", 8);
		System.Drawing.SolidBrush IconBursh = new System.Drawing.SolidBrush(System.Drawing.Color.White);

		OrderedDictionary IconCacheMap = new OrderedDictionary();

		void UpdateIcon(int count = 0)
		{
			System.Drawing.Icon nicon = null;

			nicon = IconCacheMap[count] as System.Drawing.Icon;
			if (nicon == null)
			{
				using (var bmp = new System.Drawing.Bitmap(32, 32))
				using (var graphics = System.Drawing.Graphics.FromImage(bmp))
				{
					graphics.DrawIcon(IconCache, 0, 0);
					graphics.DrawString(count.ToString(), IconFont, IconBursh, 1, 2);

					nicon = System.Drawing.Icon.FromHandle(bmp.GetHicon());

					IconCacheMap.Add(count, nicon);

					// TODO: Remove items based on least recently used with use count weight
				}
			}

			Tray.Icon = nicon;
		}

		void MenuVisibilityChangedEvent(object sender, EventArgs _ea)
		{
			if (sender is ContextMenuStrip ms)
				TrayMenuShown?.Invoke(null, new TrayShownEventArgs() { Visible = ms.Visible });
		}

		void SessionEndingEvent(object _, Microsoft.Win32.SessionEndingEventArgs ea)
		{
			ea.Cancel = true;
			// is this safe?
			Log.Information("<OS> Session end signal received.");

			ExitCleanup();
			UnifiedExit();
		}

		int hotkeymodifiers = (int)NativeMethods.KeyModifier.Control | (int)NativeMethods.KeyModifier.Shift | (int)NativeMethods.KeyModifier.Alt;

		bool HotkeysRegistered = false;
		// TODO: Move this off elsewhere
		public void RegisterGlobalHotkeys()
		{
			Debug.Assert(MKAh.Execution.IsMainThread, "RegisterGlobalHotkeys must be called from main thread");

			if (HotkeysRegistered) return;

			try
			{
				NativeMethods.RegisterHotKey(Handle, 0, hotkeymodifiers, Keys.M.GetHashCode());

				Log.Information("<Global> Registered hotkey: ctrl-alt-shift-m = free memory [ignore foreground]");

				NativeMethods.RegisterHotKey(Handle, 1, hotkeymodifiers, Keys.R.GetHashCode());

				Log.Information("<Global> Registered hotkey: ctrl-alt-shift-r = scan");

				HotkeysRegistered = true;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		void UnregisterGlobalHotkeys()
		{
			//Debug.Assert(Taskmaster.IsMainThread(), "UnregisterGlobalHotkeys must be called from main thread");

			if (HotkeysRegistered)
			{
				NativeMethods.UnregisterHotKey(Handle, 0);
				NativeMethods.UnregisterHotKey(Handle, 1);
			}
		}

		const int WM_QUERYENDSESSION = 0x0011;
		const int WM_ENDSESSION = 0x0016;

		protected override void WndProc(ref Message m)
		{
			if (DisposingOrDisposed) return;

			if (m.Msg == NativeMethods.WM_HOTKEY)
			{
				Keys key = (Keys)(((int)m.LParam >> 16) & 0xFFFF);
				//NativeMethods.KeyModifier modifiers = (NativeMethods.KeyModifier)((int)m.LParam & 0xFFFF);
				//int modifiers =(int)m.LParam & 0xFFFF;
				int hotkeyId = m.WParam.ToInt32();

				//if (modifiers != hotkeymodifiers)
					//Log.Debug($"<Global> Received unexpected modifier keys: {modifiers} instead of {hotkeymodifiers}");

				switch (hotkeyId)
				{
					case 0:
						if (Trace) Log.Verbose("<Global> Hotkey ctrl-alt-shift-m detected!!!");
						Task.Run(new Action(async () =>
						{
							int ignorepid = activeappmonitor?.Foreground ?? -1;
							Log.Information("<Global> Hotkey detected; Freeing memory while ignoring foreground"+
								(ignorepid > 4 ? $" (#{ignorepid})" : string.Empty) + " if possible.");
							await processmanager.FreeMemory(ignorePid: ignorepid).ConfigureAwait(false);
						})).ConfigureAwait(false);
						m.Result = IntPtr.Zero;
						break;
					case 1:
						if (Trace) Log.Verbose("<Global> Hotkey ctrl-alt-shift-r detected!!!");
						Log.Information("<Global> Hotkey detected; Hastening next scan.");
						processmanager?.HastenScan(5);
						m.Result = IntPtr.Zero;
						break;
					default:
						Log.Debug("<Global> Received unexpected key event: " + key.ToString());
						break;
				}
			}
			else if (m.Msg == NativeMethods.WM_COMPACTING)
			{
				Log.Debug("<System> WM_COMPACTING received");
				// wParam = The ratio of central processing unit(CPU) time currently spent by the system compacting memory to CPU time currently spent by the system performing other operations.For example, 0x8000 represents 50 percent of CPU time spent compacting memory.
				// lParam = This parameter is not used.
				System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
				GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, false, true);
				m.Result = IntPtr.Zero;
			}
			else if (m.Msg == WM_QUERYENDSESSION || m.Msg == WM_ENDSESSION)
			{
				ShutdownBlockReasonCreate(Handle, "Cleaning up");

				Log.Information("<OS> Session end signal received.");

				Task.Run(() => {
					try
					{
						ExitCleanup();
					}
					finally
					{
						try
						{
							ShutdownBlockReasonDestroy(Handle);
						}
						finally
						{
							UnifiedExit();
						}
					}
				});
				return; // block
			}

			base.WndProc(ref m); // is this necessary?
		}

		public event EventHandler RescanRequest;

		ProcessManager processmanager = null;
		public void Hook(ProcessManager pman)
		{
			processmanager = pman;
			RescanRequest += (_,_ea) => processmanager?.HastenScan();
		}

		Power.Manager powermanager = null;
		public void Hook(Power.Manager pman)
		{
			powermanager = pman;
			powermanager.onPlanChange += HighlightPowerModeEvent;

			power_auto.Checked = powermanager.Behaviour == Power.Manager.PowerBehaviour.Auto;
			power_manual.Checked = powermanager.Behaviour == Power.Manager.PowerBehaviour.Manual;
			power_auto.Enabled = true;
			HighlightPowerMode();
		}

		void SetAutoPower(object _, EventArgs _ea)
		{
			if (power_auto.Checked)
			{
				powermanager.SetBehaviour(Power.Manager.PowerBehaviour.Auto);
				power_manual.Checked = false;
			}
			else
			{
				powermanager.SetBehaviour(Power.Manager.PowerBehaviour.RuleBased);
			}
		}

		void SetManualPower(object _, EventArgs _ea)
		{
			if (power_manual.Checked)
			{
				powermanager.SetBehaviour(Power.Manager.PowerBehaviour.Manual);
				power_auto.Checked = false;
			}
			else
				powermanager.SetBehaviour(Power.Manager.PowerBehaviour.RuleBased);
		}

		void HighlightPowerModeEvent(object _, Power.ModeEventArgs _ea) => HighlightPowerMode();

		void HighlightPowerMode()
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
		}

		void SetPower(Power.Mode mode)
		{
			try
			{
				if (DebugPower) Log.Debug("<Power> Setting behaviour to manual.");

				powermanager.SetBehaviour(Power.Manager.PowerBehaviour.Manual);

				power_manual.Checked = true;
				power_auto.Checked = false;

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
				ShowMainWindow();
		}

		void UnloseWindow(object _, MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				ShowMainWindow();

				try
				{
					mainwindow?.UnloseWindowRequest(this, EventArgs.Empty); // null reference crash sometimes
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

			Tray.MouseDoubleClick -= UnloseWindow;

			mainwindow = null;
		}

		MainWindow mainwindow = null;
		public void Hook(MainWindow window)
		{
			Debug.Assert(window != null, "Hooking null main window");

			mainwindow = window;

			Tray.MouseDoubleClick += UnloseWindow;

			window.FormClosing += WindowClosed;
		}

		TimeSpan? ExplorerRestartHelpDelay { get; set; } = null;

		async void ExplorerCrashEvent(object sender, EventArgs _ea)
		{
			var proc = (Process)sender;
			ExplorerCrashHandler(proc.Id);
		}

		Process[] Explorer;
		async Task ExplorerCrashHandler(int processId)
		{
			try
			{
				KnownExplorerInstances.TryRemove(processId, out _);

				if (KnownExplorerInstances.Count > 0)
				{
					if (Trace) Log.Verbose($"<Tray> Explorer (#{processId}) exited but is not the last known explorer instance.");
					return;
				}

				Log.Warning($"<Tray> Explorer (#{processId}) crash detected!");

				Log.Information("<Tray> Giving explorer some time to recover on its own...");

				await Task.Delay(TimeSpan.FromSeconds(12)).ConfigureAwait(false); // force async, 12 seconds

				var ExplorerRestartTimer = Stopwatch.StartNew();
				bool startAttempt = true;
				Process[] procs;
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
						Process.Start(new ProcessStartInfo
						{
							FileName = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows), "explorer.exe"),
							UseShellExecute = true
						});
						startAttempt = false;
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

				EnsureVisible();
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				throw;
			}
		}

		ConcurrentDictionary<int, int> KnownExplorerInstances = new ConcurrentDictionary<int, int>();
		Process[] ExplorerInstances => Process.GetProcessesByName("explorer");

		bool RegisterExplorerExit(Process[] procs = null)
		{
			try
			{
				if (Trace) Log.Verbose("<Tray> Registering Explorer crash monitor.");
				// this is for dealing with notify icon disappearing on explorer.exe crash/restart

				if (procs == null || procs.Length == 0) procs = ExplorerInstances;

				if (procs.Length > 0)
				{
					Explorer = procs;
					foreach (var proc in procs)
					{

						if (!ProcessUtility.GetInfo(proc.Id, out var info, process: proc, name: "explorer", getPath: true)) continue; // things failed, move on

						if (!string.IsNullOrEmpty(info.Path))
						{
							if (!info.Path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.InvariantCultureIgnoreCase))
							{
								if (Taskmaster.Trace) Log.Verbose($"<Tray> Explorer (#{info.Id}) not in system root.");
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

		bool DisposingOrDisposed = false;
		protected override void Dispose(bool disposing)
		{
			if (DisposingOrDisposed) return;

			Microsoft.Win32.SystemEvents.SessionEnding -= SessionEndingEvent; // leaks if not disposed

			if (disposing)
			{
				DisposingOrDisposed = true;

				if (Trace) Log.Verbose("Disposing tray...");

				UnregisterGlobalHotkeys();

				RescanRequest = null;

				if (powermanager != null)
				{
					powermanager.onPlanChange -= HighlightPowerModeEvent;
					powermanager = null;
				}

				if (Tray != null)
				{
					Tray.Visible = false;
					Utility.Dispose(ref Tray);
				}

				// Free any other managed objects here.
				//
			}

			base.Dispose(disposing);
		}

		public event EventHandler TrayTooltipClicked;

		public void Tooltip(int timeout, string message, string title, ToolTipIcon icon)
		{
			Tray.ShowBalloonTip(timeout, title, message, icon);
			Tray.BalloonTipClicked += TrayTooltipClicked; // does this actually work for proxying?
		}

		// does this do anything really?
		public void RefreshVisibility()
		{
			Tray.Visible = false;
			Tray.Visible = true;
		}

		int ensuringvisibility = 0;
		public async void EnsureVisible()
		{
			if (!Atomic.Lock(ref ensuringvisibility)) return;

			try
			{
				RefreshVisibility();

				int attempts = 0;
				while (!Tray.Visible)
				{
					Log.Debug("<Tray> Not visible, fixing...");

					RefreshVisibility();

					await Task.Delay(TimeSpan.FromSeconds(15)).ConfigureAwait(true);

					if (++attempts >= 5)
					{
						Log.Fatal("<Tray> Failure to become visible after 5 attempts. Exiting to avoid ghost status.");
						UnifiedExit();
					}
				}
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
						SimpleMessageBox.ShowModal("Taskmaster! – run at login", "Scheduler can not be modified without admin rights.", SimpleMessageBox.Buttons.OK);
						return;
					}

					if (!menu_runatstart_sch.Checked)
					{
						if (SimpleMessageBox.ShowModal("Taskmaster! – run at login", "This will add on-login scheduler to run TM as admin, is this right?", SimpleMessageBox.Buttons.AcceptCancel)
							== SimpleMessageBox.ResultType.Cancel) return;
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
		bool RunAtStartScheduler(bool enabled, bool dryrun = false)
		{
			bool found = false;
			bool created = false;
			bool deleted = false;
			bool toggled = false;

			const string schexe = "schtasks";

			string argsq = "/query /fo list /TN MKAh-Taskmaster";

			try
			{
				var info = new ProcessStartInfo(schexe)
				{
					WindowStyle = ProcessWindowStyle.Hidden
				};
				info.Arguments = argsq;

				var procfind = Process.Start(info);
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

				if (Trace) Log.Debug($"<Tray> Scheduled task " + (found ? "" : "NOT ") + "found.");

				if (dryrun) return found; // this is bad, but fits the following logic

				if (found && enabled)
				{
					string argstoggle = "/change /TN MKAh-Taskmaster /" + (enabled ? "ENABLE" : "DISABLE");
					info.Arguments = argstoggle;
					var proctoggle = Process.Start(info);
					toggled = proctoggle.WaitForExit(3000); // this will succeed as long as the task is there
					if (toggled && proctoggle.ExitCode == 0) Log.Information("<Tray> Scheduled task found and enabled");
					else Log.Error("<Tray> Scheduled task NOT toggled.");
					if (!proctoggle.HasExited) proctoggle.Kill();

					return true;
				}

				if (enabled)
				{
					// This will solve the high privilege problem, but really? Do I want to?
					var runtime = Environment.GetCommandLineArgs()[0];
					string argscreate = "/Create /tn MKAh-Taskmaster /tr \"\\\"" + runtime + "\\\"\" /sc onlogon /it /RL HIGHEST";
					info.Arguments = argscreate;
					var procnew = Process.Start(info);
					created = procnew.WaitForExit(3000);

					if (created && procnew.ExitCode == 0) Log.Information("<Tray> Scheduled task created.");
					else Log.Error("<Tray> Scheduled task NOT created.");

					if (!procnew.HasExited) procnew.Kill();
				}
				else
				{
					string argsdelete = "/Delete /TN MKAh-Taskmaster /F";
					info.Arguments = argsdelete;

					var procdel = Process.Start(info);
					deleted = procdel.WaitForExit(3000);

					if (deleted && procdel.ExitCode == 0) Log.Information("<Tray> Scheduled task deleted.");
					else Log.Error("<Tray> Scheduled task NOT deleted.");

					if (!procdel.HasExited) procdel.Kill();
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

		~TrayAccess()
		{
			if (!DisposingOrDisposed && Tray != null) Tray.Visible = false;
			Tray?.Dispose();
			Tray = null;
		}

		// Vista or later required
		[DllImport("user32.dll")]
		internal extern static bool ShutdownBlockReasonCreate(IntPtr hWnd, [MarshalAs(UnmanagedType.LPWStr)] string pwszReason);

		// Vista or later required
		[DllImport("user32.dll")]
		internal extern static bool ShutdownBlockReasonDestroy(IntPtr hWnd);
	}
}