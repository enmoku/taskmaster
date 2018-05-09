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
	sealed public class TrayAccess : IDisposable
	{
		NotifyIcon Tray;

		ContextMenuStrip ms;
		ToolStripMenuItem menu_windowopen;
		ToolStripMenuItem menu_rescan;
		ToolStripMenuItem menu_configuration;
		ToolStripMenuItem menu_runatstart;
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
			var menu_configuration_autopower = new ToolStripMenuItem("Power auto-adjust", null, ShowPowerConfig);
			var menu_configuration_folder = new ToolStripMenuItem("Open in file manager", null, ShowConfigRequest);
			menu_configuration.DropDownItems.Add(menu_configuration_autopower);
			menu_configuration.DropDownItems.Add(new ToolStripSeparator());
			menu_configuration.DropDownItems.Add(menu_configuration_folder);

			menu_runatstart = new ToolStripMenuItem("Run at start", null, RunAtStartMenuClick);
			bool runatstart = RunAtStartRegRun(enabled: false, dryrun: true);
			menu_runatstart.Checked = runatstart;
			Log.Information("<Core> Run-at-start: {Enabled}", (runatstart ? "Enabled" : "Disabled"));

			if (Taskmaster.PowerManagerEnabled)
			{
				power_auto = new ToolStripMenuItem("Auto", null, SetAutoPower);
				power_auto.Checked = false;
				power_auto.CheckOnClick = true;
				power_auto.Enabled = false;

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
			ms.Items.Add(menu_runatstart);
			if (Taskmaster.PowerManagerEnabled)
			{
				ms.Items.Add(new ToolStripSeparator());
				var plab = new ToolStripLabel("--- Power Plan ---");
				plab.ForeColor = System.Drawing.SystemColors.GrayText;
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

			// TODO: Toast Notifications

			if (Tray.Icon == null)
			{
				Log.Fatal("<Tray> Icon missing, setting system default.");
				Tray.Icon = System.Drawing.SystemIcons.Application;
			}

			if (Taskmaster.Trace) Log.Verbose("<Tray> Initialized");
		}

		public void Enable() => ms.Enabled = true;

		public event EventHandler RescanRequest;

		ProcessManager processmanager = null;
		public void hookProcessManager(ref ProcessManager pman)
		{
			processmanager = pman;
			RescanRequest += processmanager.ScanEverythingRequest;
		}

		PowerManager powermanager = null;
		public void hookPowerManager(ref PowerManager pman)
		{
			powermanager = pman;
			powermanager.onPlanChange += HighlightPowerModeEvent;

			power_auto.Checked = powermanager.Behaviour == PowerManager.PowerBehaviour.Auto;
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

				if (Taskmaster.DebugPower)
					Log.Debug("<Power> Updating UI.");

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

			Taskmaster.mainwindow?.ShowConfigRequest(sender, e);
			// CLEANUP: Console.WriteLine("Done opening config folder.");
		}

		async void ShowPowerConfig(object sender, EventArgs e)
		{
			await PowerConfigWindow.ShowPowerConfig().ConfigureAwait(true);
		}

		int restoremainwindow_lock = 0;
		async void RestoreMainWindow(object sender, EventArgs e)
		{
			if (!Atomic.Lock(ref restoremainwindow_lock))
			{
				return; // already being done
			}

			try
			{
				using (var m = SelfAwareness.Mind(DateTime.Now.AddSeconds(10)))
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
				restoremainwindow_lock = 0;
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
					Taskmaster.mainwindow?.UnloseWindowRequest(sender, null); // null reference crash sometimes
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
					throw; // this is bad, but this is good way to force bail when this is misbehaving
				}
			}
		}

		void CompactEvent(object sender, EventArgs e)
		{
			System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
			GC.Collect(5, GCCollectionMode.Forced, true, true);
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

			// }
			// CLEANUP: Console.WriteLine("END:TrayAccess.WindowClosed");
		}

		public void hookMainWindow(ref MainWindow window)
		{
			Debug.Assert(window != null);

			Tray.MouseDoubleClick += UnloseWindow;

			window.FormClosing += WindowClosed;
			window.FormClosed += CompactEvent;
		}

		Process[] Explorer;
		async void ExplorerCrashHandler(int processId)
		{
			Log.Warning("<Tray> Explorer (#{Pid}) crash detected!", processId);

			Log.Information("<Tray> Giving explorer some time to recover on its own...");

			await Task.Delay(12000); // force async, 12 seconds

			lock (explorer_lock)
			{
				KnownExplorerInstances.Remove(processId);

				if (KnownExplorerInstances.Count > 0) return; // probably never triggers
			}

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

				await Task.Delay(1000 * 60 * 5); // wait 5 minutes
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
		System.Diagnostics.Process[] ExplorerInstances
		{
			get
			{
				return System.Diagnostics.Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension("explorer.exe"));
			}
		}

		bool RegisterExplorerExit(System.Diagnostics.Process[] procs = null)
		{
			if (Taskmaster.Trace) Log.Verbose("<Tray> Registering Explorer crash monitor.");
			// this is for dealing with notify icon disappearing on explorer.exe crash/restart

				if (procs == null) procs = ExplorerInstances;

			if (procs.Length > 0)
			{
				Explorer = procs;
				foreach (var proc in procs)
				{
					int id = proc.Id;
					lock (explorer_lock) KnownExplorerInstances.Add(id);
					proc.Exited += (s, e) => { ExplorerCrashHandler(id); };
					proc.EnableRaisingEvents = true;
					Log.Information("<Tray> Explorer (#{ExplorerProcessID}) registered.", id);
				}

				return true;
			}

			Log.Warning("<Tray> Explorer not found.");
			return false;
		}

		bool disposed; // = false;
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		void Dispose(bool disposing)
		{
			if (disposed) return;

			if (disposing)
			{
				if (Taskmaster.Trace) Log.Verbose("Disposing tray...");

				try
				{
					RescanRequest -= processmanager.ScanEverythingRequest;
				}
				catch { }

				try
				{
					if (powermanager != null)
					{
						powermanager.onPlanChange -= HighlightPowerModeEvent;
						powermanager = null;
					}
				}
				catch { } // NOP, don't care

				if (Tray != null)
				{
					Tray.Visible = false;
					Tray.Dispose();
					Tray = null;
				}

				menu_configuration?.Dispose();
				menu_exit?.Dispose();
				menu_rescan?.Dispose();
				menu_runatstart?.Dispose();
				menu_windowopen?.Dispose();
				ms?.Dispose();
				power_auto?.Dispose();
				power_balanced?.Dispose();
				power_highperf?.Dispose();
				power_manual?.Dispose();
				power_saving?.Dispose();
				// Free any other managed objects here.
				//
			}

			disposed = true;
		}

		public void Tooltip(int timeout, string message, string title, ToolTipIcon icon)
		{
			Tray.ShowBalloonTip(timeout, title, message, icon);
		}

		public void Refresh() => Tray.Visible = true;

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

					Refresh();

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

		void RunAtStartMenuClick(object sender, EventArgs ev)
		{
			var isadmin = Taskmaster.IsAdministrator();
			if (isadmin)
			{
				var rv = System.Windows.Forms.MessageBox.Show("Run at start does not support elevated privilege that you have. Is this alright?\n\nIf you absolutely need admin rights, create onlogon schedule in windows task scheduler.",
										 System.Windows.Forms.Application.ProductName + " – Run at Start normal privilege problem.",
								MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2, System.Windows.Forms.MessageBoxOptions.DefaultDesktopOnly, false);
				if (rv == DialogResult.No) return;
			}

			menu_runatstart.Checked = RunAtStartRegRun(!menu_runatstart.Checked);

			/*
			// This will solve the high privilege problem, but really? Do I want to?
			var runtime = Environment.GetCommandLineArgs()[0];
			string args = "/Create /tn MKAh-Taskmaster /tr \"" + runtime + "\" /sc onlogon /it";
			if (isadmin)
				args += " /RL HIGHEST";

			var proc = Process.Start("schtasks", args);
			var n = proc.WaitForExit(120000);

			string argsq = "/query /fo list /TN Taskmaster";
			var procq = Process.Start("schtasks", argsq);
			var rvq = procq.WaitForExit(30000);

			string argstoggle = "/change /TN MKAh-Taskmaster /ENABLE/DISABLE";

			string argsdelete = "/delete /TN MKAh-Taskmaster";
			*/
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