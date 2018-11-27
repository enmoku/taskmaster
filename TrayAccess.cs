//
// TrayAccess.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2016-2018 M.A.
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
using System.Threading.Tasks;
using System.Windows.Forms;
using Serilog;

namespace Taskmaster
{
	sealed public class TrayAccess : UI.UniForm //, IDisposable
	{
		NotifyIcon Tray;

		ContextMenuStrip ms;
		ToolStripMenuItem menu_windowopen;
		ToolStripMenuItem menu_rescan;
		ToolStripMenuItem menu_configuration;
		ToolStripMenuItem menu_runatstart_reg;
		ToolStripMenuItem menu_runatstart_sch;
		ToolStripMenuItem menu_exit;
		ToolStripMenuItem power_auto;
		ToolStripMenuItem power_highperf;
		ToolStripMenuItem power_balanced;
		ToolStripMenuItem power_saving;
		ToolStripMenuItem power_manual;

		public TrayAccess()
		{
			// BUILD UI
			Tray = new NotifyIcon
			{
				Text = System.Windows.Forms.Application.ProductName + "!", // Tooltip so people know WTF I am.
				Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location) // is this really the best way?
			};
			Tray.BalloonTipText = Tray.Text;
			Tray.Disposed += (object sender, EventArgs e) => { Tray = null; };
			
			if (Taskmaster.Trace) Log.Verbose("Generating tray icon.");

			ms = new ContextMenuStrip();
			menu_windowopen = new ToolStripMenuItem("Open", null, RestoreMainWindow);
			menu_rescan = new ToolStripMenuItem("Rescan", null, (o, s) =>
			{
				menu_rescan.Enabled = false;
				RescanRequest?.Invoke(this, null);
				menu_rescan.Enabled = true;
			});
			menu_configuration = new ToolStripMenuItem("Configuration");
			var menu_configuration_autopower = new ToolStripMenuItem("Power configuration", null, ShowPowerConfig);
			var menu_configuration_folder = new ToolStripMenuItem("Open in file manager", null, ShowConfigRequest);
			menu_configuration.DropDownItems.Add(menu_configuration_autopower);
			menu_configuration.DropDownItems.Add(new ToolStripSeparator());
			menu_configuration.DropDownItems.Add(menu_configuration_folder);

			menu_runatstart_reg = new ToolStripMenuItem("Run at start (RegRun)", null, RunAtStartMenuClick_Reg);
			menu_runatstart_sch = new ToolStripMenuItem("Schedule at login (Admin)", null, RunAtStartMenuClick_Sch);

			bool runatstartreg = RunAtStartRegRun(enabled: false, dryrun: true);
			bool runatstartsch = RunAtStartScheduler(enabled: false, dryrun: true);
			menu_runatstart_reg.Checked = runatstartreg;
			menu_runatstart_sch.Checked = runatstartsch;
			Log.Information("<Core> Run-at-start – Registry: {Enabled}, Scheduler: {Found}",
				(runatstartreg ? "Enabled" : "Disabled"), (runatstartsch ? "Found" : "Missing"));

			if (Taskmaster.PowerManagerEnabled)
			{
				power_auto = new ToolStripMenuItem("Auto", null, SetAutoPower) { Checked = false, CheckOnClick = true, Enabled = false };

				power_highperf = new ToolStripMenuItem(PowerManager.GetModeName(PowerInfo.PowerMode.HighPerformance), null, (s, e) => { ResetPower(PowerInfo.PowerMode.HighPerformance); });
				power_balanced = new ToolStripMenuItem(PowerManager.GetModeName(PowerInfo.PowerMode.Balanced), null, (s, e) => { ResetPower(PowerInfo.PowerMode.Balanced); });
				power_saving = new ToolStripMenuItem(PowerManager.GetModeName(PowerInfo.PowerMode.PowerSaver), null, (s, e) => { ResetPower(PowerInfo.PowerMode.PowerSaver); });
				power_manual = new ToolStripMenuItem("Manual override", null, SetManualPower) { CheckOnClick = true };
			}

			ToolStripMenuItem menu_restart = null;
			menu_restart = new ToolStripMenuItem("Restart", null, (o, s) =>
			{
				menu_restart.Enabled = false;
				Taskmaster.ConfirmExit(restart: true);
				menu_restart.Enabled = true;
			});
			menu_exit = new ToolStripMenuItem("Exit", null, (o, s) =>
			{
				menu_restart.Enabled = false;
				Taskmaster.ConfirmExit(restart: false);
				menu_restart.Enabled = true;
			});
			ms.Items.Add(menu_windowopen);
			ms.Items.Add(new ToolStripSeparator());
			ms.Items.Add(menu_rescan);
			ms.Items.Add(new ToolStripSeparator());
			ms.Items.Add(menu_configuration);
			ms.Items.Add(menu_runatstart_reg);
			ms.Items.Add(menu_runatstart_sch);
			if (Taskmaster.PowerManagerEnabled)
			{
				ms.Items.Add(new ToolStripSeparator());
				var plab = new ToolStripLabel("--- Power Plan ---")
				{
					ForeColor = System.Drawing.SystemColors.GrayText
				};
				ms.Items.Add(plab);
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

			if (Taskmaster.Trace) Log.Verbose("Tray menu ready");

			if (!RegisterExplorerExit())
				throw new InitFailure("<Tray> Explorer registeriong failed; not running?");

			ms.Enabled = false;

			// Tray.Click += RestoreMainWindow;
			Tray.MouseClick += ShowWindow;

			// TODO: Toast Notifications. Apparently not supported by Win7, so nevermind.

			if (Tray.Icon == null)
			{
				Log.Fatal("<Tray> Icon missing, setting system default.");
				Tray.Icon = System.Drawing.SystemIcons.Application;
			}

			Microsoft.Win32.SystemEvents.SessionEnding += SessionEndingEvent; // depends on messagepump

			if (Taskmaster.Trace) Log.Verbose("<Tray> Initialized");
		}

		public void SessionEndingEvent(object sender, EventArgs ev)
		{
			// queue exit
			Log.Information("<Session:Ending> Exiting...");
			BeginInvoke(new Action(() => {
				Microsoft.Win32.SystemEvents.SessionEnding -= SessionEndingEvent;
				Taskmaster.UnifiedExit();
			}));
		}

		bool registered = false;
		public void RegisterGlobalHotkeys()
		{
			if (registered) return;

			try
			{
				int hotkeyId = 0;
				int modifiers = (int)NativeMethods.KeyModifier.Control | (int)NativeMethods.KeyModifier.Shift | (int)NativeMethods.KeyModifier.Alt;

				NativeMethods.RegisterHotKey(Handle, hotkeyId, modifiers, Keys.M.GetHashCode());

				Log.Information("<Tray> Registered global hotkey: ctrl-alt-shift-m = free memory [for foreground]");

				registered = true;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		void UnregisterGlobalHotkeys()
		{
			NativeMethods.UnregisterHotKey(Handle, 0);
		}

		int hotkeyinprogress = 0;

		protected override void WndProc(ref Message m)
		{
			if (m.Msg == NativeMethods.WM_HOTKEY)
			{
				//Keys key = (Keys)(((int)m.LParam >> 16) & 0xFFFF);
				//NativeMethods.KeyModifier modifier = (NativeMethods.KeyModifier)((int)m.LParam & 0xFFFF);
				int id = m.WParam.ToInt32(); // registered hotkey id

				if (Atomic.Lock(ref hotkeyinprogress))
				{
					if (Taskmaster.Trace) Log.Verbose("<Tray> Hotkey ctrl-alt-shift-m detected!!!");
					Task.Run(new Action(async () =>
					{
						try
						{
							int ignorepid = Taskmaster.Components.activeappmonitor?.Foreground ?? -1;
							Log.Information("<Tray> Hotkey detected, freeing memory while ignoring foreground{Ign} if possible.",
								ignorepid > 4 ? (" (#" + ignorepid + ")") : string.Empty);
							await processmanager.FreeMemory(ignorepid).ConfigureAwait(false);
						}
						catch (Exception ex)
						{
							Logging.Stacktrace(ex);
						}
						finally
						{
							Atomic.Unlock(ref hotkeyinprogress);
						}
					}));
				}
			}

			base.WndProc(ref m); // is this necessary?
		}

		public void Enable() => ms.Enabled = true;

		public event EventHandler RescanRequest;

		ProcessManager processmanager = null;
		public void Hook(ProcessManager pman)
		{
			processmanager = pman;
			RescanRequest += processmanager.ScanEverythingRequest;
		}

		PowerManager powermanager = null;
		public void Hook(PowerManager pman)
		{
			powermanager = pman;
			powermanager.onPlanChange += HighlightPowerModeEvent;

			power_auto.Checked = powermanager.Behaviour == PowerManager.PowerBehaviour.Auto;
			power_manual.Checked = powermanager.Behaviour == PowerManager.PowerBehaviour.Manual;
			power_auto.Enabled = true;
			HighlightPowerMode();
		}

		void SetAutoPower(object sender, EventArgs ev)
		{
			if (power_auto.Checked)
			{
				powermanager.SetBehaviour(PowerManager.PowerBehaviour.Auto);
				power_manual.Checked = false;
			}
			else
			{
				powermanager.SetBehaviour(PowerManager.PowerBehaviour.RuleBased);
			}
		}

		void SetManualPower(object sender, EventArgs e)
		{
			if (power_manual.Checked)
			{
				powermanager.SetBehaviour(PowerManager.PowerBehaviour.Manual);
				power_auto.Checked = false;
			}
			else
				powermanager.SetBehaviour(PowerManager.PowerBehaviour.RuleBased);
		}

		void HighlightPowerModeEvent(object sender, PowerModeEventArgs ev) => HighlightPowerMode();

		void HighlightPowerMode()
		{
			switch (powermanager.CurrentMode)
			{
				case PowerInfo.PowerMode.Balanced:
					power_saving.Checked = false;
					power_balanced.Checked = true;
					power_highperf.Checked = false;
					break;
				case PowerInfo.PowerMode.HighPerformance:
					power_saving.Checked = false;
					power_balanced.Checked = false;
					power_highperf.Checked = true;
					break;
				case PowerInfo.PowerMode.PowerSaver:
					power_saving.Checked = true;
					power_balanced.Checked = false;
					power_highperf.Checked = false;
					break;
			}
		}

		void ResetPower(PowerInfo.PowerMode mode)
		{
			try
			{
				if (Taskmaster.DebugPower)
					Log.Debug("<Power> Setting behaviour to manual.");

				powermanager.SetBehaviour(PowerManager.PowerBehaviour.Manual);

				power_manual.Checked = true;
				power_auto.Checked = false;

				if (Taskmaster.DebugPower)
					Log.Debug("<Power> Setting manual mode: {Mode}", mode.ToString());

				// powermanager.Restore(0).Wait(); // already called by setBehaviour as necessary
				powermanager?.SetMode(mode);

				// powermanager.RequestMode(mode);
				HighlightPowerMode();
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		void ShowConfigRequest(object sender, EventArgs e)
		{
			// CLEANUP: Console.WriteLine("Opening config folder.");
			Process.Start(Taskmaster.datapath);

			mainwindow?.ShowConfigRequest(sender, e);
			// CLEANUP: Console.WriteLine("Done opening config folder.");
		}

		void ShowPowerConfig(object sender, EventArgs e)
		{
			if (!IsHandleCreated) return;
			BeginInvoke(new Action(() =>
			{
				try
				{
					PowerConfigWindow.Show();
				}
				catch (Exception ex) { Logging.Stacktrace(ex); }
			}));
		}

		int restoremainwindow_lock = 0;
		void RestoreMainWindow(object sender, EventArgs e)
		{
			if (!Atomic.Lock(ref restoremainwindow_lock)) return; // already being done

			try
			{
				Taskmaster.ShowMainWindow();

				if (Taskmaster.Trace)
					Log.Verbose("RestoreMainWindow done!");
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				throw;
			}
			finally
			{
				Atomic.Unlock(ref restoremainwindow_lock);
			}
		}

		void ShowWindow(object sender, MouseEventArgs e)
		{
			if (Taskmaster.Trace)
				Console.WriteLine("Tray Click");

			if (e.Button == MouseButtons.Left)
			{
				RestoreMainWindow(sender, null);
			}
		}

		async void UnloseWindow(object sender, MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				RestoreMainWindow(sender, null);
				try
				{
					mainwindow?.UnloseWindowRequest(sender, null); // null reference crash sometimes
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
					throw; // this is bad, but this is good way to force bail when this is misbehaving
				}
			}
		}

		void WindowClosed(object sender, FormClosingEventArgs e)
		{
			// CLEANUP: Console.WriteLine("START:TrayAccess.WindowClosed");

			switch (e.CloseReason)
			{
				case CloseReason.ApplicationExitCall:
					// CLEANUP: Console.WriteLine("BAIL:TrayAccess.WindowClosed");
					return;
			}

			// if (Taskmaster.LowMemory)
			// {
			// CLEANUP: Console.WriteLine("DEBUG:TrayAccess.WindowClosed.SaveMemory");

			Tray.MouseDoubleClick -= UnloseWindow;

			mainwindow = null;

			// }
			// CLEANUP: Console.WriteLine("END:TrayAccess.WindowClosed");
		}

		MainWindow mainwindow = null;
		public void Hook(MainWindow window)
		{
			Debug.Assert(window != null);

			mainwindow = window;

			Tray.MouseDoubleClick += UnloseWindow;

			window.FormClosing += WindowClosed;
		}

		Process[] Explorer;
		async void ExplorerCrashHandler(int processId)
		{
			lock (explorer_lock)
			{
				KnownExplorerInstances.Remove(processId);

				if (KnownExplorerInstances.Count > 0)
				{
					if (Taskmaster.Trace) Log.Verbose("<Tray> Explorer (#{Id}) exited but is not the last known explorer instance.", processId);
					return;
				}
			}

			Log.Warning("<Tray> Explorer (#{Pid}) crash detected!", processId);

			Log.Information("<Tray> Giving explorer some time to recover on its own...");

			await Task.Delay(12000).ConfigureAwait(false); // force async, 12 seconds

			var n = new Stopwatch();
			n.Start();
			Process[] procs;
			while ((procs = ExplorerInstances).Length == 0)
			{
				if (n.Elapsed.TotalHours >= 24)
				{
					Log.Error("<Tray> Explorer has not recovered in excessive timeframe, giving up.");
					return;
				}

				await Task.Delay(1000 * 60 * 5).ConfigureAwait(false); // wait 5 minutes
			}

			n.Stop();

			if (!RegisterExplorerExit(procs))
			{
				Log.Warning("<Tray> Explorer registration failed.");
				return;
			}

			EnsureVisible();
		}

		object explorer_lock = new object();
		HashSet<int> KnownExplorerInstances = new HashSet<int>();
		Process[] ExplorerInstances
		{
			get
			{
				return Process.GetProcessesByName("explorer");
			}
		}

		bool RegisterExplorerExit(Process[] procs = null)
		{
			if (Taskmaster.Trace) Log.Verbose("<Tray> Registering Explorer crash monitor.");
			// this is for dealing with notify icon disappearing on explorer.exe crash/restart

			if (procs == null || procs.Length == 0) procs = ExplorerInstances;

			if (procs.Length > 0)
			{
				Explorer = procs;
				foreach (var proc in procs)
				{
					var info = ProcessManagerUtility.GetInfo(proc.Id, process: proc, name:"explorer", getPath: true);

					if (info == null) continue; // things failed, move on

					if (!string.IsNullOrEmpty(info.Path))
					{
						if (!info.Path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.InvariantCultureIgnoreCase))
						{
							if (Taskmaster.Trace) Log.Verbose("<Tray> Explorer (#{Pid}) not in system root.", info.Id);
							continue;
						}
					}

					bool added = false;
					lock (explorer_lock)
					{
						added = KnownExplorerInstances.Add(info.Id);
					}

					if (added)
					{
						proc.Exited += (s, e) => { ExplorerCrashHandler(info.Id); };
						proc.EnableRaisingEvents = true;
					}

					Log.Information("<Tray> Explorer (#{ExplorerProcessID}) registered.", info.Id);
				}

				return true;
			}

			Log.Warning("<Tray> Explorer not found.");
			return false;
		}

		bool disposed = false;
		protected override void Dispose(bool disposing)
		{
			if (disposed) return;

			try
			{
				Microsoft.Win32.SystemEvents.SessionEnding -= SessionEndingEvent; // leaks if not disposed
			}
			catch { }

			if (disposing)
			{
				if (Taskmaster.Trace) Log.Verbose("Disposing tray...");

				UnregisterGlobalHotkeys();

				RescanRequest -= processmanager.ScanEverythingRequest;
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

				Utility.Dispose(ref menu_configuration);
				Utility.Dispose(ref menu_exit);
				Utility.Dispose(ref menu_rescan);
				Utility.Dispose(ref menu_runatstart_reg);
				Utility.Dispose(ref menu_windowopen);
				Utility.Dispose(ref ms);
				Utility.Dispose(ref power_auto);
				Utility.Dispose(ref power_balanced);
				Utility.Dispose(ref power_highperf);
				Utility.Dispose(ref power_manual);
				Utility.Dispose(ref power_saving);
				// Free any other managed objects here.
				//
			}

			disposed = true;
		}

		public event EventHandler TrayTooltipClicked;

		public void Tooltip(int timeout, string message, string title, ToolTipIcon icon)
		{
			Tray.ShowBalloonTip(timeout, title, message, icon);
			Tray.BalloonTipClicked += TrayTooltipClicked; // does this actually work for proxying?
		}

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
				Enable();

				int attempts = 0;
				while (!Tray.Visible)
				{
					Log.Debug("<Tray> Not visible, fixing...");

					RefreshVisibility();

					await Task.Delay(15 * 1000).ConfigureAwait(true);

					if (++attempts >= 5)
					{
						Log.Fatal("<Tray> Failure to become visible after 5 attempts. Exiting to avoid ghost status.");
						Taskmaster.UnifiedExit();
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

		void RunAtStartMenuClick_Reg(object sender, EventArgs ev)
		{
			try
			{
				if (!menu_runatstart_reg.Checked)
				{
					var isadmin = Taskmaster.IsAdministrator();
					if (isadmin)
					{
						var rv = System.Windows.Forms.MessageBox.Show("Run at start does not support elevated privilege that you have. Is this alright?\n\nIf you absolutely need admin rights, create on logon scheduled task.",
												 System.Windows.Forms.Application.ProductName + " – Run at Start normal privilege problem.",
										MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2, System.Windows.Forms.MessageBoxOptions.DefaultDesktopOnly, false);
						if (rv == DialogResult.No) return;
					}
				}

				menu_runatstart_reg.Checked = RunAtStartRegRun(!menu_runatstart_reg.Checked);
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		void RunAtStartMenuClick_Sch(object sender, EventArgs ev)
		{
			try
			{
				var isadmin = Taskmaster.IsAdministrator();

				if (!isadmin)
				{
					var rv = MessageBox.Show("Scheduler can not be modified without admin rights.",
												 System.Windows.Forms.Application.ProductName + " – run with scheduler at login",
										MessageBoxButtons.OK, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1, System.Windows.Forms.MessageBoxOptions.DefaultDesktopOnly, false);
					return;
				}

				if (!menu_runatstart_sch.Checked)
				{
					if (isadmin)
					{
						var rv = System.Windows.Forms.MessageBox.Show("This will add on-login scheduler to run TM as admin, is this right?",
												 System.Windows.Forms.Application.ProductName + " – run at login with scheduler",
										MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2, System.Windows.Forms.MessageBoxOptions.DefaultDesktopOnly, false);
						if (rv == DialogResult.No) return;
					}
				}

				menu_runatstart_sch.Checked = RunAtStartScheduler(!menu_runatstart_sch.Checked);

				if (menu_runatstart_sch.Checked && menu_runatstart_reg.Checked)
				{
					// don't have both enabled
					RunAtStartMenuClick_Reg(this, null);
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
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
			ProcessStartInfo info = new ProcessStartInfo(schexe)
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

				if (!procfind.HasExited)
				{
					if (!warned)
					{
						Log.Debug("<Tray> Task Scheduler is taking long time to respond.");
						warned = true;
					}
				}
				else
					break;
			}

			if (rvq && procfind.ExitCode == 0) found = true;
			else if (!procfind.HasExited) procfind.Kill();

			Log.Information("<Tray> Scheduled task {Found}found.", (found ? "" : "NOT "));

			if (dryrun) return found; // this is bad, but fits the following logic

			if (found && enabled)
			{
				string argstoggle = "/change /TN MKAh-Taskmaster /" + (enabled ? "ENABLE" : "DISABLE");
				info.Arguments = argstoggle;
				var proctoggle = Process.Start(info);
				toggled = proctoggle.WaitForExit(3000); // this will succeed as long as the task is there
				if (toggled && proctoggle.ExitCode == 0) Log.Debug("<Tray> Scheduled task found and enabled");
				else Log.Debug("<Tray> Scheduled task NOT toggled.");
				if (!proctoggle.HasExited) proctoggle.Kill();

				return true;
			}

			if (enabled)
			{
				// This will solve the high privilege problem, but really? Do I want to?
				var runtime = Environment.GetCommandLineArgs()[0];
				string argscreate = "/Create /tn MKAh-Taskmaster /tr \"\\\"" + runtime + "\\\" --scheduler --bootdelay\" /sc onlogon /delay 0:30 /it /RL HIGHEST";
				info.Arguments = argscreate;
				var procnew = Process.Start(info);
				created = procnew.WaitForExit(3000);

				if (created && procnew.ExitCode == 0) Log.Debug("<Tray> Scheduled task created.");
				else Log.Debug("<Tray> Scheduled task NOT created.");

				if (!procnew.HasExited) procnew.Kill();
			}
			else
			{
				string argsdelete = "/Delete /TN MKAh-Taskmaster /F";
				info.Arguments = argsdelete;

				var procdel = Process.Start(info);
				deleted = procdel.WaitForExit(3000);

				if (deleted && procdel.ExitCode == 0) Log.Debug("<Tray> Scheduled task deleted.");
				else Log.Debug("<Tray> Scheduled task NOT deleted.");

				if (!procdel.HasExited) procdel.Kill();
			}

			//if (toggled) Log.Debug("<Tray> Scheduled task toggled.");

			return enabled;
		}

		bool RunAtStartRegRun(bool enabled, bool dryrun = false)
		{
			var runatstart_path = @"Software\Microsoft\Windows\CurrentVersion\Run";
			var runatstart_key = "MKAh-Taskmaster";
			string runatstart;
			var runvalue = Environment.GetCommandLineArgs()[0] + " --bootdelay";
			try
			{
				var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(runatstart_path, true);
				if (key != null)
				{
					runatstart = (string)key.GetValue(runatstart_key, string.Empty);

					if (dryrun)
					{
						bool rv = (runatstart.Equals(runvalue, StringComparison.InvariantCultureIgnoreCase));
						return rv;
					}

					if (enabled)
					{
						if (runatstart.Equals(runvalue, StringComparison.InvariantCultureIgnoreCase))
							return true;

						key.SetValue(runatstart_key, runvalue);
						Log.Information("Run at OS startup enabled: " + Environment.GetCommandLineArgs()[0]);
						return true;
					}
					else if (!enabled)
					{
						//if (!runatstart.ToLowerInvariant().Equals(runvalue.ToLowerInvariant()))
						//	return false;

						key.DeleteValue(runatstart_key);
						Log.Information("Run at OS startup disabled.");
						// return false;
					}
				}
				else
					Log.Debug("Registry run at startup key not found.");
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}

			return false;
		}
	}
}