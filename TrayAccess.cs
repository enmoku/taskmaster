//
// TrayAccess.cs
//
// Author:
//       M.A. (enmoku) <>
//
// Copyright (c) 2016-2018 M.A. (enmoku)
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
using System.Windows.Forms;
using Serilog;
using System.Diagnostics;
using System.Threading.Tasks;

namespace TaskMaster
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

			if (TaskMaster.Trace) Log.Verbose("Generating tray icon.");

			ms = new ContextMenuStrip();
			menu_windowopen = new ToolStripMenuItem("Open", null, RestoreMainWindow);
			menu_rescan = new ToolStripMenuItem("Rescan", null, (o, s) =>
			{
				menu_rescan.Enabled = false;
				RescanRequest?.Invoke(this, null);
				menu_rescan.Enabled = true;
			});
			menu_configuration = new ToolStripMenuItem("Configuration", null, ShowConfigRequest);
			menu_runatstart = new ToolStripMenuItem("Run at start", null, RunAtStartMenuClick);
			menu_runatstart.Checked = RunAtStartRegRun(false, true);

			if (TaskMaster.PowerManagerEnabled)
			{
				power_auto = new ToolStripMenuItem("Auto", null, SetAutoPower);
				power_auto.Checked = false;
				power_auto.CheckOnClick = true;
				power_auto.Enabled = false;

				power_highperf = new ToolStripMenuItem("Performance", null, (s, e) => { ResetPower(PowerManager.PowerMode.HighPerformance); });
				power_balanced = new ToolStripMenuItem("Balanced", null, (s, e) => { ResetPower(PowerManager.PowerMode.Balanced); });
				power_saving = new ToolStripMenuItem("Power Saving", null, (s, e) => { ResetPower(PowerManager.PowerMode.PowerSaver); });
				power_manual = new ToolStripMenuItem("Manual override", null, SetManualPower);
				power_manual.CheckOnClick = true;
			}
			ToolStripMenuItem menu_restart = null;
			menu_restart = new ToolStripMenuItem("Restart", null, (o, s) =>
			{
				menu_restart.Enabled = false;
				TaskMaster.ConfirmExit(restart: true);
				menu_restart.Enabled = true;
			});
			menu_exit = new ToolStripMenuItem("Exit", null, (o, s) =>
			{
				menu_restart.Enabled = false;
				TaskMaster.ConfirmExit(restart: false);
				menu_restart.Enabled = true;
			});
			ms.Items.Add(menu_windowopen);
			ms.Items.Add(new ToolStripSeparator());
			ms.Items.Add(menu_rescan);
			ms.Items.Add(new ToolStripSeparator());
			ms.Items.Add(menu_configuration);
			ms.Items.Add(menu_runatstart);
			if (TaskMaster.PowerManagerEnabled)
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

			if (TaskMaster.Trace) Log.Verbose("Tray menu ready");

			if (!RegisterExplorerExit())
				throw new InitFailure("Explorer registeriong failed; not running?");

			ms.Enabled = false;

			Tray.Visible = true;

			//Tray.Click += RestoreMainWindow;
			Tray.MouseClick += ShowWindow;

			// TODO: Toast Notifications

			if (TaskMaster.Trace) Log.Verbose("<Tray> Initialized");
		}

		public void Enable()
		{
			ms.Enabled = true;
		}

		public event EventHandler RescanRequest;
		public event EventHandler ManualPowerMode;

		ProcessManager processmanager = null;
		public void hookProcessManager(ref ProcessManager pman)
		{
			processmanager = pman;
			RescanRequest += processmanager.ProcessEverythingRequest;
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

		void HighlightPowerModeEvent(object sender, PowerModeEventArgs ev)
		{
			HighlightPowerMode();
		}

		void HighlightPowerMode()
		{
			switch (powermanager.CurrentMode)
			{
				case PowerManager.PowerMode.Balanced:
					power_saving.Checked = false;
					power_balanced.Checked = true;
					power_highperf.Checked = false;
					break;
				case PowerManager.PowerMode.HighPerformance:
					power_saving.Checked = false;
					power_balanced.Checked = false;
					power_highperf.Checked = true;
					break;
				case PowerManager.PowerMode.PowerSaver:
					power_saving.Checked = true;
					power_balanced.Checked = false;
					power_highperf.Checked = false;
					break;
			}
		}

		void ResetPower(PowerManager.PowerMode mode)
		{
			try
			{
				powermanager.SetBehaviour(PowerManager.PowerBehaviour.Manual);

				power_manual.Checked = (powermanager.Behaviour == PowerManager.PowerBehaviour.Manual);
				power_auto.Checked = (powermanager.Behaviour == PowerManager.PowerBehaviour.Auto);

				ManualPowerMode?.Invoke(this, null);

				//powermanager.Restore(0).Wait(); // already called by setBehaviour as necessary
				powermanager.setMode(mode);
				//powermanager.RequestMode(mode);
				HighlightPowerMode();
			}
			catch { }
		}

		void ShowConfigRequest(object sender, EventArgs e)
		{
			//CLEANUP: Console.WriteLine("Opening config folder.");
			Process.Start(TaskMaster.datapath);

			TaskMaster.mainwindow?.ShowConfigRequest(sender, e);
			//CLEANUP: Console.WriteLine("Done opening config folder.");
		}

		int restoremainwindow_lock = 0;
		async void RestoreMainWindow(object sender, EventArgs e)
		{
			if (!Atomic.Lock(ref restoremainwindow_lock))
				return; // already being done

			using (var m = SelfAwareness.Mind("RestoreMainWindow hung", DateTime.Now.AddSeconds(10)))
			{
				TaskMaster.ShowMainWindow();
			}

			if (TaskMaster.Trace)
				Log.Verbose("RestoreMainWindow done!");

			restoremainwindow_lock = 0;
		}

		async void ShowWindow(object sender, MouseEventArgs e)
		{
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
					TaskMaster.mainwindow?.UnloseWindowRequest(sender, null); // null reference crash sometimes
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
			//CLEANUP: Console.WriteLine("START:TrayAccess.WindowClosed");

			switch (e.CloseReason)
			{
				case CloseReason.ApplicationExitCall:
					//CLEANUP: Console.WriteLine("BAIL:TrayAccess.WindowClosed");
					return;
			}

			//if (TaskMaster.LowMemory)
			//{
			//CLEANUP: Console.WriteLine("DEBUG:TrayAccess.WindowClosed.SaveMemory");

			Tray.MouseDoubleClick -= UnloseWindow;

			//}
			//CLEANUP: Console.WriteLine("END:TrayAccess.WindowClosed");
		}

		public void hookMainWindow(ref MainWindow window)
		{
			Debug.Assert(window != null);

			Tray.MouseDoubleClick += UnloseWindow;
			window.FormClosing += WindowClosed;
			window.FormClosed += CompactEvent;
		}

		Process[] Explorer;
		async void ExplorerCrashHandler(object sender, EventArgs e)
		{
			Log.Warning("Explorer crash detected!");

			Log.Information("Giving explorer some time to recover on its own...");

			using (var m = SelfAwareness.Mind("Explorer crash handler hung", DateTime.Now.AddSeconds(17)))
			{
				await Task.Delay(12000); // force async
			}

			Stopwatch n = new Stopwatch();
			n.Start();
			Process[] procs;
			while ((procs = ExplorerInstances).Length == 0)
			{
				if (n.Elapsed.TotalHours >= 24)
				{
					Log.Error("Explorer has not recovered in excessive timeframe, giving up.");
					return;
				}

				using (var m = SelfAwareness.Mind("Explorer crash handler hung", DateTime.Now.AddSeconds((60 * 5) + 5)))
				{
					await Task.Delay(1000 * 60 * 5); // wait 5 minutes
				}
			}
			n.Stop();

			if (RegisterExplorerExit(procs))
			{
			}
			else
			{
				Log.Warning("Explorer registration failed.");
				return;
			}

			Tray.Visible = true; // TODO: Is this enough/necessary? Doesn't seem to be. WinForms appears to recover on its own.
		}

		System.Diagnostics.Process[] ExplorerInstances
		{
			get
			{
				return System.Diagnostics.Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension("explorer.exe"));
			}
		}

		bool RegisterExplorerExit(System.Diagnostics.Process[] procs = null)
		{
			if (TaskMaster.Trace) Log.Verbose("Registering Explorer crash monitor.");
			// this is for dealing with notify icon disappearing on explorer.exe crash/restart

			if (procs == null) procs = ExplorerInstances;

			if (procs.Length > 0)
			{
				Explorer = procs;
				foreach (var proc in procs)
				{
					proc.Exited += ExplorerCrashHandler;
					proc.EnableRaisingEvents = true;
					Log.Information("Explorer (#{ExplorerProcessID}) registered.", proc.Id);
				}
				return true;
			}

			Log.Warning("Explorer not found.");
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
			if (disposed)
				return;

			if (disposing)
			{
				if (TaskMaster.Trace) Log.Verbose("Disposing tray...");


				try
				{
					RescanRequest -= processmanager.ProcessEverythingRequest;
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
				catch { /*NOP, don't care */ }

				if (Tray != null)
				{
					Tray.Visible = false;
					Tray.Dispose();
					Tray = null;
				}

				// Free any other managed objects here.
				//
			}

			// Free any unmanaged objects here.
			//
			disposed = true;
		}

		public void Tooltip(int timeout, string message, string title, ToolTipIcon icon)
		{
			Tray.ShowBalloonTip(timeout, title, message, icon);
		}

		public void Refresh()
		{
			Tray.Visible = true;
		}


		void RunAtStartMenuClick(object sender, EventArgs ev)
		{
			bool isadmin = TaskMaster.IsAdministrator();
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
			string args = "/Create /tn Enmoku-Taskmaster /tr \"" + runtime + "\" /sc onlogon /it";
			if (isadmin)
				args += " /RL HIGHEST";

			var proc = Process.Start("schtasks", args);
			var n = proc.WaitForExit(120000);

			string argsq = "/query /fo list /TN Taskmaster";
			var procq = Process.Start("schtasks", argsq);
			var rvq = procq.WaitForExit(30000);

			string argstoggle = "/change /TN Enmoku-Taskmaster /ENABLE/DISABLE";

			string argsdelete = "/delete /TN Enmoku-Taskmaster";
			*/
		}

		bool RunAtStartRegRun(bool status, bool dryrun = false)
		{
			string runatstart_path = @"Software\Microsoft\Windows\CurrentVersion\Run";
			string runatstart_key = "Enmoku-Taskmaster";
			string runatstart;
			string runvalue = Environment.GetCommandLineArgs()[0] + " --bootdelay";
			try
			{
				Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(runatstart_path, true);
				if (key != null)
				{
					runatstart = (string)key.GetValue(runatstart_key, string.Empty);
					if (dryrun) return (runatstart == runvalue);
					if (status)
					{
						if (runatstart == runvalue) return true;
						key.SetValue(runatstart_key, runvalue);
						Log.Information("Run at OS startup enabled: " + Environment.GetCommandLineArgs()[0]);
						return true;
					}
					else if (!status)
					{
						if (runatstart != runvalue) return false;

						key.DeleteValue(runatstart_key);
						Log.Information("Run at OS startup disabled.");
						//return false;
					}
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			return false;
		}
	}
}
