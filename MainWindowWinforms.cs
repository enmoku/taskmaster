//
// MainWindow.cs
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

using System.Diagnostics;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using Serilog;
using System.Runtime.InteropServices;
using System.Collections;
using System.Linq;

namespace TaskMaster
{
	// public class MainWindow : System.Windows.Window; // TODO: WPF
	sealed public class MainWindow : Form
	{
		public void ShowConfigRequest(object sender, EventArgs e)
		{
			// TODO: Introduce configuration window
		}

		public async void PowerConfigRequest(object sender, EventArgs e)
		{
			PowerConfigWindow.ShowPowerConfig().ConfigureAwait(true); // true for UI thread
		}

		public void ExitRequest(object sender, EventArgs e)
		{
			TaskMaster.ConfirmExit(restart: false);
		}

		void WindowClose(object sender, FormClosingEventArgs e)
		{
			saveUIState();

			if (!TaskMaster.Trace) return;

			//Console.WriteLine("WindowClose = " + e.CloseReason);
			switch (e.CloseReason)
			{
				case CloseReason.UserClosing:
					// X was pressed or similar, we're just hiding to tray.
					/*
					if (!TaskMaster.LowMemory)
					{
						Log.Verbose("Hiding window, keeping in memory.");
						e.Cancel = true;
						Hide();
					}
					else
					{
						Log.Verbose("Closing window, freeing memory.");
					}
					*/
					break;
				case CloseReason.WindowsShutDown:
					Log.Debug("Exit: Windows shutting down.");
					break;
				case CloseReason.TaskManagerClosing:
					Log.Debug("Exit: Task manager told us to close.");
					break;
				case CloseReason.ApplicationExitCall:
					Log.Debug("Exit: User asked to close.");
					break;
				default:
					Log.Debug("Exit: Unidentified close reason: {CloseReason}", e.CloseReason);
					break;
			}
			//CLEANUP: Console.WriteLine("WindowClose.Handled");
		}

		// this restores the main window to a place where it can be easily found if it's lost
		/// <summary>
		/// Restores the main window to the center of the screen.
		/// </summary>
		public void UnloseWindowRequest(object sender, EventArgs e)
		{
			if (TaskMaster.Trace) Log.Verbose("Making sure main window is not lost.");

			CenterToScreen();
			Reveal();
		}

		public void Reveal()
		{
			BringToFront(); // does nothing without show(), unreliable even with it
			TopMost = true;
			TopMost = false;
			Show();
		}

		public void ShowLastLog()
		{
			if (loglist.Items.Count > 0)
			{
				loglist.EnsureVisible(loglist.Items.Count - 1);
			}
		}

		// HOOKS
		MicManager micmon = null;
		DiskManager diskmanager = null;
		ProcessManager processmanager = null;
		ActiveAppManager activeappmonitor = null;
		PowerManager powermanager = null;
		NetManager netmonitor = null;

		#region Microphone control code
		public void hookMicMonitor(MicManager micmonitor)
		{
			Debug.Assert(micmonitor != null);
			micmon = micmonitor;

			if (TaskMaster.Trace) Log.Verbose("Hooking microphone monitor.");

			micName.Text = micmon.DeviceName;
			corCountLabel.Text = micmon.Corrections.ToString();
			micVol.Maximum = Convert.ToDecimal(micmon.Maximum);
			micVol.Minimum = Convert.ToDecimal(micmon.Minimum);
			micVol.Value = Convert.ToInt32(micmon.Volume);

			foreach (var dev in micmon.enumerate())
				micList.Items.Add(new ListViewItem(new string[] { dev.Name, dev.GUID }));

			// TODO: Hook device changes
			micmon.VolumeChanged += volumeChangeDetected;
		}

		public void ProcAdjust(object sender, ProcessEventArgs ev)
		{
			//Log.Verbose("Process adjust received for '{FriendlyName}'.", e.Control.FriendlyName);

			ListViewItem item;
			lock (appw_lock)
			{
				try
				{
					if (WatchlistMap.TryGetValue(ev.Control, out item))
					{
						item.SubItems[AdjustColumn].Text = ev.Control.Adjusts.ToString();
						//item.SubItems[SeenColumn].Text = e.Control.LastSeen.ToLocalTime().ToString();
					}
					else
						Log.Error("{FriendlyName} not found in app list.", ev.Control.FriendlyName);
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
				}
			}
		}

		public void OnActiveWindowChanged(object sender, WindowChangedArgs windowchangeev)
		{
			try
			{
				//int maxlength = 70;
				//string cutstring = e.Title.Substring(0, Math.Min(maxlength, e.Title.Length)) + (e.Title.Length > maxlength ? "..." : "");
				//activeLabel.Text = cutstring;
				activeLabel.Text = windowchangeev.Title;
				activeExec.Text = windowchangeev.Executable;
				activeFullscreen.Text = windowchangeev.Fullscreen.True() ? "Full" : windowchangeev.Fullscreen.False() ? "Window" : "Unknown";
				activePID.Text = string.Format("{0}", windowchangeev.Id);
			}
			catch { }
		}

		public event EventHandler rescanRequest;
		public event EventHandler pagingRequest;

		public void hookDiskManager(ref DiskManager diskman)
		{
			diskmanager = diskman;
			diskmanager.onTempScan += TempScanStats;
		}

		public void hookProcessManager(ref ProcessManager control)
		{
			Debug.Assert(control != null);
			processmanager = control;

			processmanager.onInstanceHandling += ProcessNewInstanceCount;
			processmanager.PathCacheUpdate += PathCacheUpdate;
			processmanager.onActiveHandled += ExitWaitListHandler;
			processmanager.onWaitForExitEvent += ExitWaitListHandler;
			PathCacheUpdate(null, null);

			foreach (var prc in processmanager.getWatchlist()) AddToWatchlistList(prc);
			WatchlistColor();

			rescanRequest += processmanager.ScanEverythingRequest;
			if (TaskMaster.PagingEnabled)
				pagingRequest += processmanager.PageEverythingRequest;

			ProcessController.onLocate += WatchlistPathLocatedEvent;
			ProcessController.onTouch += ProcAdjust;

			lock (processingCountLock)
				processingCount.Value = ProcessManager.Handling;

			var items = processmanager.getExitWaitList();

			foreach (var bu in items)
			{
				ExitWaitListHandler(this, new ProcessEventArgs() { Control = null, State = ProcessEventArgs.ProcessState.Starting, Info = bu });
			}

			if (TaskMaster.ActiveAppMonitorEnabled)
			{
				var items2 = processmanager.getExitWaitList();
				foreach (var bu in items2)
				{
					ExitWaitListHandler(this, new ProcessEventArgs() { Control = null, Info = bu, State = ProcessEventArgs.ProcessState.Found });
				}
			}
		}

		void ProcessNewInstanceCount(object sender, InstanceEventArgs e)
		{
			lock (processingCountLock)
			{
				try
				{
					processingCount.Value += e.Count;
				}
				catch
				{
					Log.Error("Processing counter over/underflow");
					processingCount.Value = ProcessManager.Handling;
				}
			}
		}

		System.Drawing.Color GrayText = System.Drawing.Color.FromArgb(130, 130, 130);
		System.Drawing.Color AlterColor = System.Drawing.Color.FromArgb(245, 245, 245);

		void WatchlistItemColor(ListViewItem li, ProcessController prc)
		{
			bool alter = (li.Index + 1) % 2 == 0; // every even line

			try
			{
				bool noprio = (prc.Increase == false && prc.Decrease == false);

				li.UseItemStyleForSubItems = false;

				foreach (ListViewItem.ListViewSubItem si in li.SubItems)
				{
					if (prc.Enabled)
						si.ForeColor = System.Drawing.SystemColors.WindowText;
					else
						si.ForeColor = GrayText;

					if (alter)
						si.BackColor = AlterColor;
				}

				alter = !alter;

				if (noprio)
					li.SubItems[PrioColumn].ForeColor = GrayText;
				if (string.IsNullOrEmpty(prc.Path))
					li.SubItems[PathColumn].ForeColor = GrayText;
				if (prc.PowerPlan == PowerInfo.PowerMode.Undefined)
					li.SubItems[PowerColumn].ForeColor = GrayText;
				if (prc.Affinity.ToInt32() == ProcessManager.allCPUsMask)
					li.SubItems[AffColumn].ForeColor = GrayText;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				throw;
			}
		}

		void WatchlistColor()
		{
			lock (watchlistrules_lock)
			{
				bool alter = false;

				foreach (var item in WatchlistMap)
				{
					WatchlistItemColor(item.Value, item.Key);
					alter = !alter;
				}
			}
		}

		bool alterColor = false;
		int num = 1;
		void AddToWatchlistList(ProcessController prc)
		{
			bool noprio = (prc.Increase == false && prc.Decrease == false);
			string prio = noprio ? "--- Any --- " : prc.Priority.ToString();

			var litem = new ListViewItem(new string[] {
				(num++).ToString(),
					prc.FriendlyName, //.ToString(),
					prc.Executable,
					prio,
						(prc.Affinity.ToInt32() == ProcessManager.allCPUsMask ? "--- Any ---" : Convert.ToString(prc.Affinity.ToInt32(), 2).PadLeft(ProcessManager.CPUCount, '0')),
						(prc.PowerPlan != PowerInfo.PowerMode.Undefined? prc.PowerPlan.ToString() : "--- Any ---"),
				//(pc.Rescan>0 ? pc.Rescan.ToString() : "n/a"),
					prc.Adjusts.ToString(),
						//(pc.LastSeen != DateTime.MinValue ? pc.LastSeen.ToLocalTime().ToString() : "Never"),
				(string.IsNullOrEmpty(prc.Path) ? "--- Any ---" : prc.Path)
					});

			lock (watchlistrules_lock)
			{
				watchlistRules.Items.Add(litem); // add calls sort()???
			}


			lock (appw_lock)
				WatchlistMap.Add(prc, litem);
		}

		public void WatchlistPathLocatedEvent(object sender, PathControlEventArgs e)
		{
			try
			{
				var pc = (ProcessController)sender;
				if (pc != null)
				{
					ListViewItem li = null;
					if (WatchlistMap.TryGetValue(pc, out li))
					{
						WatchlistItemColor(li, pc);
					}
				}
			}
			catch { }
		}

		Label micName;
		NumericUpDown micVol;
		readonly object micList_lock = new object();
		ListView micList;
		readonly object appList_lock = new object();
		ListView watchlistRules;
		readonly object watchlistrules_lock = new object();
		//object pathList_lock = new object();
		//ListView pathList;
		readonly object appw_lock = new object();
		Dictionary<ProcessController, ListViewItem> WatchlistMap = new Dictionary<ProcessController, ListViewItem>();
		Label corCountLabel;
		readonly object processingCountLock = new object();
		NumericUpDown processingCount;
		Label processingCountdown;

		ListView powerbalancerlog;
		Label powerbalancer_behaviour;
		Label powerbalancer_plan;

		readonly object powerbalancerlog_lock = new object();

		ListView exitwaitlist;
		Dictionary<int, ListViewItem> ExitWaitlistMap;

		void UserMicVol(object sender, EventArgs e)
		{
			// TODO: Handle volume changes. Not really needed. Give presets?
			//micMonitor.setVolume(micVol.Value);
		}

		void volumeChangeDetected(object sender, VolumeChangedEventArgs e)
		{
			try
			{
				micVol.Value = Convert.ToInt32(e.New);
				corCountLabel.Text = e.Corrections.ToString();
			}
			catch { }
		}
		#endregion // Microphone control code

		#region Game Monitor
		Label activeLabel;
		Label activeExec;
		Label activeFullscreen;
		Label activePID;
		#endregion

		#region Path Cache
		Label cacheObjects;
		Label cacheRatio;
		#endregion

		public void PathCacheUpdate(object sender, CacheEventArgs ev)
		{
			try
			{
				cacheObjects.Text = Statistics.PathCacheCurrent.ToString();
				double ratio = (Statistics.PathCacheMisses > 0 ? (Statistics.PathCacheHits / Statistics.PathCacheMisses) : 1);
				if (ratio <= 99.99f)
					cacheRatio.Text = string.Format("{0:N2}", ratio);
				else
					cacheRatio.Text = ">99.99"; // let's just not overflow the UI
			}
			catch { }
		}

		// BackColor = System.Drawing.Color.LightGoldenrodYellow
		Label netstatuslabel;
		Label inetstatuslabel;
		Label uptimestatuslabel;

		ComboBox logcombo_level;
		public static Serilog.Core.LoggingLevelSwitch LogIncludeLevel;

		int UIUpdateFrequency = 500;
		Timer UItimer;

		static bool UIOpen = true;
		void StartUIUpdates(object sender, EventArgs e)
		{
			if (!UItimer.Enabled) UItimer.Start();
			UIOpen = true;
		}

		void StopUIUpdates(object sender, EventArgs e)
		{
			if (UItimer.Enabled) UItimer.Stop();
			UIOpen = false;
		}

		void UpdateRescanCountdown(object sender, EventArgs ev)
		{
			var t = ProcessManager.NextRescan.Unixstamp() - DateTime.Now.Unixstamp();
			try
			{
				processingCountdown.Text = string.Format("{0:N0}s", t);
			}
			catch { }
		}

		void UpdateUptime(object sender, EventArgs e)
		{
			try
			{
				if (netmonitor != null)
					uptimestatuslabel.Text = HumanInterface.TimeString(netmonitor.Uptime);

				//string.Format("{0:N1} min(s)", net.Uptime.TotalMinutes);
			}
			catch { }
		}

		ListView ifaceList;
		Button rescanbutton;
		Button crunchbutton;

		ContextMenuStrip ifacems;
		ContextMenuStrip loglistms;
		ContextMenuStrip watchlistms;
		ToolStripMenuItem watchlistenable;

		void InterfaceContextMenuOpen(object sender, EventArgs e)
		{
			try
			{
				foreach (ToolStripItem msi in ifacems.Items)
				{
					msi.Enabled = (ifaceList.SelectedItems.Count == 1);
				}
			}
			catch { }
		}

		int IPv4Column = 4;
		int IPv6Column = 5;

		void CopyIPv4AddressToClipboard(object sender, EventArgs ea)
		{
			try
			{
				if (ifaceList.SelectedItems.Count == 1)
				{
					var li = ifaceList.SelectedItems[0];
					string ipv4addr = li.SubItems[IPv4Column].Text;
					Clipboard.SetText(ipv4addr);
				}
			}
			catch { }
		}

		void CopyIPv6AddressToClipboard(object sender, EventArgs ea)
		{
			try
			{
				if (ifaceList.SelectedItems.Count == 1)
				{
					var li = ifaceList.SelectedItems[0];
					string ipv6addr = string.Format("[{0}]", li.SubItems[IPv6Column].Text);
					Clipboard.SetText(ipv6addr);
				}
			}
			catch { }
		}

		void LogContextMenuOpen(object sender, EventArgs ea)
		{
			try
			{
				foreach (ToolStripItem lsi in loglistms.Items)
				{
					lsi.Enabled = (loglist.SelectedItems.Count == 1);
				}
			}
			catch { }
		}

		void CopyLogToClipboard(object sender, EventArgs ea)
		{
			try
			{
				if (loglist.SelectedItems.Count == 1)
				{
					var li = loglist.SelectedItems[0];
					string rv = li.SubItems[0].Text;
					if (rv.Length > 0)
						Clipboard.SetText(rv);
				}
			}
			catch { }
		}

		TabControl tabLayout = null;

		int OrderColumn = 0;
		int NameColumn = 1;
		int ExeColumn = 2;
		int PrioColumn = 3;
		int AffColumn = 4;
		int PowerColumn = 5;
		int RescanColumn = 6;
		int AdjustColumn = 7;
		int SeenColumn = 8;
		int PathColumn = 9;

		void EnsureDebugLog()
		{
			if (logcombo_level.SelectedIndex == 0)
				logcombo_level.SelectedIndex = 1;
		}

		void BuildUI()
		{
			MinimumSize = new System.Drawing.Size(750, 620); // width, height

			Text = string.Format("{0} ({1})", System.Windows.Forms.Application.ProductName, System.Windows.Forms.Application.ProductVersion);
#if DEBUG
			Text = Text + " DEBUG";
#endif
			//Padding = new Padding(6);
			// margin

			var padding = new Padding(6);

			var lrows = new TableLayoutPanel
			{
				AutoSize = true,
				Parent = this,
				ColumnCount = 1,
				Margin = padding,
				//lrows.RowCount = 10;
				Dock = DockStyle.Top,
			};

			var menu = new MenuStrip() { Dock = DockStyle.Top };

			var menu_action = new ToolStripMenuItem("Actions");
			menu_action.DropDown.AutoClose = true;
			// Sub Items
			var menu_action_rescan = new ToolStripMenuItem("Rescan", null, (o, s) =>
			{
				rescanRequest?.Invoke(this, null);
			});
			var menu_action_memoryfocus = new ToolStripMenuItem("Free memory for...", null, FreeMemoryRequest);
			menu_action_memoryfocus.Enabled = TaskMaster.PagingEnabled;
			ToolStripMenuItem menu_action_restart = null;
			menu_action_restart = new ToolStripMenuItem("Restart", null, (s, e) =>
			{
				menu_action_restart.Enabled = false;
				TaskMaster.ConfirmExit(restart: true);
				menu_action_restart.Enabled = true;
			});
			var menu_action_exit = new ToolStripMenuItem("Exit", null, ExitRequest);
			menu_action.DropDownItems.Add(menu_action_rescan);
			menu_action.DropDownItems.Add(menu_action_memoryfocus);
			menu_action.DropDownItems.Add(new ToolStripSeparator());
			menu_action.DropDownItems.Add(menu_action_restart);
			menu_action.DropDownItems.Add(menu_action_exit);

			// CONFIG menu item
			var menu_config = new ToolStripMenuItem("Configuration");
			menu_config.DropDown.AutoClose = true;
			// Sub Items
			var menu_config_behaviour = new ToolStripMenuItem("Behaviour");
			var menu_config_power = new ToolStripMenuItem("Power");
			var menu_config_saveonexit = new ToolStripMenuItem("Save on exit");
			menu_config_saveonexit.Checked = TaskMaster.SaveConfigOnExit;
			menu_config_saveonexit.CheckOnClick = true;
			menu_config_saveonexit.Click += (sender, e) =>
			{
				TaskMaster.SaveConfigOnExit = !TaskMaster.SaveConfigOnExit;
			};

			// Sub Sub Items
			var menu_config_behaviour_autoopen = new ToolStripMenuItem("Auto-open menus");
			menu_config_behaviour_autoopen.Checked = TaskMaster.AutoOpenMenus;
			menu_config_behaviour_autoopen.CheckOnClick = true;
			menu_config_behaviour_autoopen.Click += (sender, e) => { TaskMaster.AutoOpenMenus = !TaskMaster.AutoOpenMenus; };

			menu_config_behaviour.DropDownItems.Add(menu_config_behaviour_autoopen);

			var menu_config_power_autoadjust = new ToolStripMenuItem("Auto-adjust tuning");
			menu_config_power_autoadjust.Click += PowerConfigRequest;
			menu_config_power.DropDownItems.Add(menu_config_power_autoadjust);

			//

			var menu_config_log = new ToolStripMenuItem("Logging");
			var menu_config_log_power = new ToolStripMenuItem("Power mode changes", null, (sender, e) => { });
			menu_config_log.DropDownItems.Add(menu_config_log_power);

			var menu_config_folder = new ToolStripMenuItem("Open in file manager", null, (s, e) => { Process.Start(TaskMaster.datapath); });
			//menu_config.DropDownItems.Add(menu_config_log);
			menu_config.DropDownItems.Add(menu_config_behaviour);
			menu_config.DropDownItems.Add(new ToolStripSeparator());
			menu_config.DropDownItems.Add(menu_config_power);
			menu_config.DropDownItems.Add(new ToolStripSeparator());
			menu_config.DropDownItems.Add(menu_config_folder);
			menu_config.DropDownItems.Add(new ToolStripSeparator());
			menu_config.DropDownItems.Add(menu_config_saveonexit);

			// DEBUG menu item
			var menu_debug = new ToolStripMenuItem("Debug");
			menu_debug.DropDown.AutoClose = true;
			// Sub Items
			var menu_debug_inaction = new ToolStripMenuItem("Show inaction") { Checked = TaskMaster.ShowInaction, CheckOnClick = true };
			menu_debug_inaction.Click += (sender, e) => { TaskMaster.ShowInaction = menu_debug_inaction.Checked; };
			var menu_debug_scanning = new ToolStripMenuItem("Scanning") { Checked = TaskMaster.DebugFullScan, CheckOnClick = true };
			menu_debug_scanning.Click += (sender, e) =>
			{
				TaskMaster.DebugFullScan = menu_debug_scanning.Checked;
				if (TaskMaster.DebugFullScan) EnsureDebugLog();
			};

			var menu_debug_procs = new ToolStripMenuItem("Processes") { Checked = TaskMaster.DebugProcesses, CheckOnClick = true };
			menu_debug_procs.Click += (sender, e) =>
			{
				TaskMaster.DebugProcesses = menu_debug_procs.Checked;
				if (TaskMaster.DebugProcesses) EnsureDebugLog();
			};
			var menu_debug_foreground = new ToolStripMenuItem("Foreground") { Checked = TaskMaster.DebugForeground, CheckOnClick = true };
			menu_debug_foreground.Click += (sender, e) =>
			{
				TaskMaster.DebugForeground = menu_debug_foreground.Checked;
				if (TaskMaster.DebugForeground) EnsureDebugLog();
			};

			var menu_debug_paths = new ToolStripMenuItem("Paths") { Checked = TaskMaster.DebugPaths, CheckOnClick = true };
			menu_debug_paths.Click += (sender, e) =>
			{
				TaskMaster.DebugPaths = menu_debug_paths.Checked;
				if (TaskMaster.DebugPaths) EnsureDebugLog();
			};
			var menu_debug_power = new ToolStripMenuItem("Power") { Checked = TaskMaster.DebugPower, CheckOnClick = true };
			menu_debug_power.Click += (sender, e) =>
			{
				TaskMaster.DebugPower = menu_debug_power.Checked;
				if (TaskMaster.DebugPower) EnsureDebugLog();
			};
			var menu_debug_clear = new ToolStripMenuItem("Clear UI log", null, (sender, e) => { ClearLog(); });

			// TODO: This menu needs to be clearer
			menu_debug.DropDownItems.Add(menu_debug_inaction);
			menu_debug.DropDownItems.Add(new ToolStripSeparator());
			menu_debug.DropDownItems.Add(menu_debug_scanning);
			menu_debug.DropDownItems.Add(menu_debug_procs);
			menu_debug.DropDownItems.Add(menu_debug_foreground);
			menu_debug.DropDownItems.Add(menu_debug_paths);
			menu_debug.DropDownItems.Add(menu_debug_power);
			menu_debug.DropDownItems.Add(new ToolStripSeparator());
			menu_debug.DropDownItems.Add(menu_debug_clear);

			// INFO menu
			var menu_info = new ToolStripMenuItem("Info");
			menu_info.DropDown.AutoClose = true;
			// Sub Items
			var menu_info_github = new ToolStripMenuItem("Github", null, (sender, e) => { Process.Start(TaskMaster.URL); });
			var menu_info_about = new ToolStripMenuItem("About", null, (s, e) =>
			{
				MessageBox.Show(Application.ProductName + " (" + Application.ProductVersion + ")\n\nCreated by M.A., 2016-2018\n\nFree system maintenance and de-obnoxifying app.\n\nAvailable under MIT license.",
								"About Taskmaster!", MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, MessageBoxOptions.DefaultDesktopOnly, false);
			});
			menu_info.DropDownItems.Add(menu_info_github);
			menu_info.DropDownItems.Add(new ToolStripSeparator());
			menu_info.DropDownItems.Add(menu_info_about);

			menu.Items.Add(menu_action);
			menu.Items.Add(menu_config);
			menu.Items.Add(menu_debug);
			menu.Items.Add(menu_info);

			// no simpler way?

			menu_action.MouseEnter += (s, e) => { if (TaskMaster.AutoOpenMenus) menu_action.ShowDropDown(); };
			menu_config.MouseEnter += (s, e) => { if (TaskMaster.AutoOpenMenus) menu_config.ShowDropDown(); };
			menu_debug.MouseEnter += (s, e) => { if (TaskMaster.AutoOpenMenus) menu_debug.ShowDropDown(); };
			menu_info.MouseEnter += (s, e) => { if (TaskMaster.AutoOpenMenus) menu_info.ShowDropDown(); };

			Controls.Add(menu);

			tabLayout = new TabControl()
			{
				//Parent = lrows,
				Height = 300,
				Padding = new System.Drawing.Point(6, 6),
				Dock = DockStyle.Fill,
				//Padding = new System.Drawing.Point(3, 3),
			};

			TabPage infoTab = new TabPage("Info");
			TabPage watchTab = new TabPage("Watchlist");
			TabPage micTab = new TabPage("Microphone");
			if (!TaskMaster.MicrophoneMonitorEnabled) micTab.Hide();
			TabPage netTab = new TabPage("Network");
			if (!TaskMaster.NetworkMonitorEnabled) netTab.Hide();
			TabPage powerDebugTab = new TabPage("Power Debug");
			TabPage ProcessDebugTab = new TabPage("Process Debug");

			tabLayout.Controls.Add(infoTab);
			tabLayout.Controls.Add(watchTab);
			tabLayout.Controls.Add(micTab);
			tabLayout.Controls.Add(netTab);
			tabLayout.Controls.Add(powerDebugTab);
			tabLayout.Controls.Add(ProcessDebugTab);

			var infopanel = new TableLayoutPanel
			{
				Dock = DockStyle.Top,
				Width = tabLayout.Width - 12,
				AutoSize = true,
			};

			//Controls.Add(tabLayout);

			#region Main Window Row 0, game monitor / active window monitor
			var activepanel = new TableLayoutPanel
			{
				Dock = DockStyle.Top,
				RowCount = 1,
				ColumnCount = 6,
				AutoSize = true,
				//Width = tabLayout.Width - 3,
			};

			var activeLabelUX = new Label() { Text = "Active:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Width = 40 };
			activeLabel = new Label()
			{
				Dock = DockStyle.Top,
				Text = "no active window found",
				AutoSize = true,
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
				AutoEllipsis = true,
			};
			activeExec = new Label() { Dock = DockStyle.Top, Text = "n/a", Width = 100, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
			activeFullscreen = new Label() { Dock = DockStyle.Top, Text = "n/a", Width = 60, TextAlign = System.Drawing.ContentAlignment.MiddleCenter };
			activePID = new Label() { Text = "n/a", Width = 60, TextAlign = System.Drawing.ContentAlignment.MiddleCenter };

			activepanel.Controls.Add(activeLabelUX);
			activepanel.Controls.Add(activeLabel);
			activepanel.Controls.Add(activeExec);
			activepanel.Controls.Add(activeFullscreen);
			activepanel.Controls.Add(new Label { Text = "Id:", Width = 20, TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
			activepanel.Controls.Add(activePID);

			//infopanel.Controls.Add(activepanel);
			//infoTab.Controls.Add(infopanel);
			#endregion

			#region Load UI config
			var uicfg = TaskMaster.loadConfig("UI.ini");
			var wincfg = uicfg["Windows"];
			var colcfg = uicfg["Columns"];

			int opentab = uicfg["Tabs"].TryGet("Open")?.IntValue ?? 0;

			int[] appwidthsDefault = new int[] { 20, 120, 140, 82, 60, 76, 46, 140 };
			var appwidths = colcfg.GetSetDefault("Apps", appwidthsDefault).IntValueArray;
			if (appwidths.Length != appwidthsDefault.Length) appwidths = appwidthsDefault;

			int[] micwidthsDefault = new int[] { 200, 220 };
			var micwidths = colcfg.GetSetDefault("Mics", micwidthsDefault).IntValueArray;
			if (micwidths.Length != micwidthsDefault.Length) micwidths = micwidthsDefault;

			int[] ifacewidthsDefault = new int[] { 120, 60, 50, 70, 90, 200, 60, 60 };
			var ifacewidths = colcfg.GetSetDefault("Interfaces", ifacewidthsDefault).IntValueArray;
			if (ifacewidths.Length != ifacewidthsDefault.Length) ifacewidths = ifacewidthsDefault;

			int[] winsizedefault = new int[] { 760, 0 };
			var winsize = wincfg.GetSetDefault("Main", winsizedefault).IntValueArray;
			if (winsize.Length == 2)
			{
				if (winsize[0] > 0)
					Width = winsize[0];
				if (winsize[1] > 0)
					Height = winsize[1];
			}

			#endregion

			#region Main Window Row 1, microphone device
			var micpanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Width = tabLayout.Width - 12 };
			micpanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
			micpanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

			var micDevLbl = new Label
			{
				Text = "Default communications device:",
				Dock = DockStyle.Left,
				Width = 180,
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
				//AutoSize = true // why not?
			};
			micName = new Label { Text = "N/A", Dock = DockStyle.Left, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, AutoEllipsis = true };
			var micNameRow = new TableLayoutPanel
			{
				Dock = DockStyle.Top,
				RowCount = 1,
				ColumnCount = 2,
				//AutoSize = true // why not?
			};
			micNameRow.Controls.Add(micDevLbl);
			micNameRow.Controls.Add(micName);
			#endregion

			var miccntrl = new TableLayoutPanel() { ColumnCount = 5, RowCount = 1, Dock = DockStyle.Top };
			miccntrl.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			miccntrl.RowStyles.Add(new RowStyle(SizeType.AutoSize));

			var micVolLabel = new Label
			{
				Text = "Mic volume",
				Dock = DockStyle.Top,
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
				//AutoSize = true // why not?
			};

			var micVolLabel2 = new Label
			{
				Text = "%",
				Dock = DockStyle.Top,
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
				//AutoSize = true // why not?
			};

			micVol = new NumericUpDown { Maximum = 100, Minimum = 0, Width = 60, ReadOnly = true, Enabled = false, Dock = DockStyle.Top };
			micVol.ValueChanged += UserMicVol;

			miccntrl.Controls.Add(micVolLabel);
			miccntrl.Controls.Add(micVol);
			miccntrl.Controls.Add(micVolLabel2);

			var corLbll = new Label
			{
				Text = "Correction count:",
				Dock = DockStyle.Top,
				//AutoSize = true, // why not?
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
			};

			corCountLabel = new Label
			{
				Dock = DockStyle.Top,
				Text = "0",
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
				//AutoSize = true, // why not?
			};
			miccntrl.Controls.Add(corLbll);
			miccntrl.Controls.Add(corCountLabel);
			// End: Volume control

			// Main Window row 3, microphone device enumeration
			micList = new ListView
			{
				Dock = DockStyle.Top,
				//Width = tabLayout.Width - 12, // FIXME: 3 for the bevel, but how to do this "right"?
				Height = 120,
				View = View.Details,
				AutoSize = true,
				FullRowSelect = true
			};
			micList.Columns.Add("Name", micwidths[0]);
			micList.Columns.Add("GUID", micwidths[1]);

			micpanel.Controls.Add(micNameRow);
			micpanel.Controls.Add(miccntrl);
			micpanel.Controls.Add(micList);
			micTab.Controls.Add(micpanel);

			// End: Microphone enumeration

			// Main Window row 4-5, internet status
			var netlayout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true };

			var netLabel = new Label() { Text = "Network status:", Dock = DockStyle.Left, AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
			var inetLabel = new Label() { Text = "Internet status:", Dock = DockStyle.Left, AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
			var uptimeLabel = new Label() { Text = "Uptime:", Dock = DockStyle.Left, AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };

			netstatuslabel = new Label() { Dock = DockStyle.Left, Text = "Uninitialized", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
			inetstatuslabel = new Label() { Dock = DockStyle.Left, Text = "Uninitialized", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
			uptimestatuslabel = new Label
			{
				Dock = DockStyle.Left,
				Text = "Uninitialized",
				AutoSize = true,
				BackColor = System.Drawing.Color.Transparent,
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft
			};


			var netstatus = new TableLayoutPanel
			{
				ColumnCount = 6,
				RowCount = 1,
				Dock = DockStyle.Top,
				//AutoSize = true
			};
			netstatus.Controls.Add(netLabel);
			netstatus.Controls.Add(netstatuslabel);
			netstatus.Controls.Add(inetLabel);
			netstatus.Controls.Add(inetstatuslabel);
			netstatus.Controls.Add(uptimeLabel);
			netstatus.Controls.Add(uptimestatuslabel);

			GotFocus += UpdateUptime;

			GotFocus += StartUIUpdates;
			FormClosing += StopUIUpdates;
			VisibleChanged += (sender, e) =>
			{
				if (Visible)
				{
					UpdateUptime(sender, e);
					StartUIUpdates(sender, e);
				}
				else
				{
					StopUIUpdates(sender, e);
				}
			};

			UItimer = new Timer { Interval = UIUpdateFrequency };
			if (TaskMaster.NetworkMonitorEnabled)
				UItimer.Tick += UpdateUptime;

			if (TaskMaster.ProcessMonitorEnabled && ProcessManager.RescanEverythingFrequency > 0)
				UItimer.Tick += UpdateRescanCountdown;

			ifaceList = new ListView
			{
				Dock = DockStyle.Top,
				AutoSize = true,
				//Width = tabLayout.Width - 3, // FIXME: why does 3 work? can't we do this automatically?
				Height = 180,
				View = View.Details,
				FullRowSelect = true
			};
			ifacems = new ContextMenuStrip();
			ifacems.Opened += InterfaceContextMenuOpen;
			var ifaceip4copy = new ToolStripMenuItem("Copy IPv4 address", null, CopyIPv4AddressToClipboard);
			var ifaceip6copy = new ToolStripMenuItem("Copy IPv6 address", null, CopyIPv6AddressToClipboard);
			ifacems.Items.Add(ifaceip4copy);
			ifacems.Items.Add(ifaceip6copy);
			ifaceList.ContextMenuStrip = ifacems;

			ifaceList.Columns.Add("Device", ifacewidths[0]); // 0
			ifaceList.Columns.Add("Type", ifacewidths[1]); // 1
			ifaceList.Columns.Add("Status", ifacewidths[2]); // 2
			ifaceList.Columns.Add("Link speed", ifacewidths[3]); // 3
			ifaceList.Columns.Add("IPv4", ifacewidths[4]); // 4
			ifaceList.Columns.Add("IPv6", ifacewidths[5]); // 5
			ifaceList.Columns.Add("Packet Δ", ifacewidths[6]); // 6
			ifaceList.Columns.Add("Error Δ", ifacewidths[7]); // 7
			PacketColumn = 6;
			ErrorColumn = 7;

			ifaceList.Scrollable = true;

			IPv4Column = 4;
			IPv6Column = 5;

			netlayout.Controls.Add(netstatus);
			netlayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); // why?
			netlayout.Controls.Add(ifaceList);
			netTab.Controls.Add(netlayout);

			// End: Inet status

			// Main Window row 6, settings
			/*
			ListView settingList = new ListView();
			settingList.Dock = DockStyle.Top;
			settingList.Width = lrows.Width - 3; // FIXME: why does 3 work? can't we do this automatically?
			settingList.View = View.Details;
			settingList.FullRowSelect = true;
			settingList.Columns.Add("Key", 80);
			settingList.Columns.Add("Value", 220);
			settingList.Scrollable = true;
			Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
			if (config.AppSettings.Settings.Count > 0)
			{
				foreach (var key in config.AppSettings.Settings.AllKeys)
				{
					settingList.Items.Add(new ListViewItem(new string[] { key, config.AppSettings.Settings[key].Value }));
				}
			}
			*/
			// End: Settings

			// Main Window, Path list
			var proclayout = new TableLayoutPanel
			{
				// BackColor = System.Drawing.Color.Azure, // DEBUG
				Dock = DockStyle.Top,
				Width = tabLayout.Width - 12,
				AutoSize = true,
			};

			/*
			// TODO: ADD SORTING

			int sortColumn = -1;
			SortOrder sortOrder = SortOrder.Ascending;

			pathList.ListViewItemSorter
			pathList.Sorting = SortOrder.Ascending;

			pathList.ColumnClick += (object sender, ColumnClickEventArgs e) =>
			{
				if (e.Column == sortColumn)
				{
					sortOrder = sortOrder == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
				}

				sortColumn = e.Column;

				pathList.Sort();
			};
			*/

			// Rule Listing
			watchlistRules = new ListView
			{
				View = View.Details,
				Dock = DockStyle.Top,
				AutoSize = true,
				Width = tabLayout.Width - 52, // FIXME: why does 3 work? can't we do this automatically?
				Height = 260, // FIXME: Should use remaining space
				FullRowSelect = true
			};

			var numberColumns = new int[] { 0, AdjustColumn };
			var watchlistSorter = new WatchlistSorter(numberColumns);
			watchlistRules.ListViewItemSorter = watchlistSorter; // what's the point of this?
			watchlistRules.ColumnClick += (sender, e) =>
			{
				if (watchlistSorter.Column == e.Column)
				{
					// flip order
					watchlistSorter.Order = watchlistSorter.Order == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
				}
				else
				{
					watchlistSorter.Order = SortOrder.Ascending;
					watchlistSorter.Column = e.Column;
				}

				// deadlock if locked while adding
				watchlistRules.Sort();
			};

			// TODO: Add context menu
			watchlistms = new ContextMenuStrip();
			watchlistms.Opened += WatchlistContextMenuOpen;
			watchlistenable = new ToolStripMenuItem("Enabled", null, EnableWatchlistRule);
			var watchlistedit = new ToolStripMenuItem("Edit", null, EditWatchlistRule);
			var watchlistadd = new ToolStripMenuItem("Create new", null, AddWatchlistRule);
			var watchlistdel = new ToolStripMenuItem("Remove", null, DeleteWatchlistRule);
			var watchlistclip = new ToolStripMenuItem("Copy to clipboard", null, CopyRuleToClipboard);

			watchlistms.Items.Add(watchlistenable);
			watchlistms.Items.Add(new ToolStripSeparator());
			watchlistms.Items.Add(watchlistedit);
			watchlistms.Items.Add(watchlistadd);
			watchlistms.Items.Add(new ToolStripSeparator());
			watchlistms.Items.Add(watchlistdel);
			watchlistms.Items.Add(new ToolStripSeparator());
			watchlistms.Items.Add(watchlistclip);
			watchlistRules.ContextMenuStrip = watchlistms;

			OrderColumn = 0;
			NameColumn = 1;
			ExeColumn = 2;
			PrioColumn = 3;
			AffColumn = 4;
			PowerColumn = 5;
			AdjustColumn = 6;
			PathColumn = 7;

			watchlistRules.Columns.Add("#", appwidths[0]);
			watchlistRules.Columns.Add("Name", appwidths[1]);
			watchlistRules.Columns.Add("Executable", appwidths[2]);
			watchlistRules.Columns.Add("Priority", appwidths[3]);
			watchlistRules.Columns.Add("Affinity", appwidths[4]);
			watchlistRules.Columns.Add("Power Plan", appwidths[5]);
			watchlistRules.Columns.Add("Adjusts", appwidths[6]);
			watchlistRules.Columns.Add("Path", appwidths[7]);
			watchlistRules.Scrollable = true;
			watchlistRules.Alignment = ListViewAlignment.Left;

			watchlistRules.DoubleClick += EditWatchlistRule; // for in-app editing
			watchlistRules.Click += UpdateInfoPanel;

			//proclayout.Controls.Add(pathList);
			proclayout.Controls.Add(watchlistRules);
			watchTab.Controls.Add(proclayout);

			// End: App list

			// UI Log
			loglist = new ListView
			{
				Dock = DockStyle.Top,
				AutoSize = true,
				View = View.Details,
				FullRowSelect = true,
				HeaderStyle = ColumnHeaderStyle.Nonclickable,
				Scrollable = true,
				MinimumSize = new System.Drawing.Size(700, 180),
			};

			loglist.Columns.Add("Event Log", -2, HorizontalAlignment.Left); // 2
			loglist.Resize += (sender, e) => { loglist.Columns[0].Width = -2; };

			loglistms = new ContextMenuStrip();
			loglistms.Opened += LogContextMenuOpen;
			var logcopy = new ToolStripMenuItem("Copy to clipboard", null, CopyLogToClipboard);
			loglistms.Items.Add(logcopy);
			loglist.ContextMenuStrip = loglistms;

			var cfg = TaskMaster.loadConfig("Core.ini");
			bool modified, tdirty = false;
			MaxLogSize = cfg["Logging"].GetSetDefault("UI max items", 200, out modified).IntValue;
			tdirty |= modified;
			UIUpdateFrequency = cfg["User Interface"].GetSetDefault("Update frequency", 2000, out modified).IntValue.Constrain(100, 5000);
			tdirty |= modified;
			if (tdirty)
			{
				cfg["Logging"]["UI max items"].Comment = "Maximum number of items/lines to retain on UI level.";
				cfg["User Interface"]["Update frequency"].Comment = "In milliseconds. Frequency of controlled UI updates. Affects visual accuracy of timers and such. Valid range: 100 to 5000.";
				TaskMaster.MarkDirtyINI(cfg);
			}

			var logpanel = new TableLayoutPanel
			{
				//Parent = lrows,
				//Dock = DockStyle.Fill,
				Dock = DockStyle.Top,

				RowCount = 2,
				ColumnCount = 1,
				//Width = lrows.Width,
				AutoSize = true,
				MinimumSize = new System.Drawing.Size(700, 180),
			};

			var loglevelpanel = new TableLayoutPanel
			{
				Dock = DockStyle.Top,
				RowCount = 1,
				ColumnCount = 2,
				AutoSize = true,
			};

			var loglabel_level = new Label
			{
				Text = "Verbosity",
				Dock = DockStyle.Left,
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
				Width = 60
			};

			logcombo_level = new ComboBox
			{
				Dock = DockStyle.Left,
				DropDownStyle = ComboBoxStyle.DropDownList,
				Items = { "Information", "Debug", "Trace" },
				Width = 80,
				SelectedIndex = 0
			};

			logcombo_level.SelectedIndexChanged += (sender, e) =>
			{
				switch (logcombo_level.SelectedIndex)
				{
					default:
					case 0:
						LogIncludeLevel.MinimumLevel = Serilog.Events.LogEventLevel.Information;
						break;
					case 1:
						LogIncludeLevel.MinimumLevel = Serilog.Events.LogEventLevel.Debug;
						break;
					case 2:
						LogIncludeLevel.MinimumLevel = Serilog.Events.LogEventLevel.Verbose;
						Log.Warning("Trace events enabled. UI may become unresponsive due to their volume.");
						break;
				}
				Debug.WriteLine("GUI log level changed: {0} ({1})", LogIncludeLevel.MinimumLevel, logcombo_level.SelectedIndex);
			};

			LogIncludeLevel = MemoryLog.LevelSwitch; // HACK
			switch (LogIncludeLevel.MinimumLevel)
			{
				default:
				case Serilog.Events.LogEventLevel.Information:
					logcombo_level.SelectedIndex = 0;
					break;
				case Serilog.Events.LogEventLevel.Debug:
					logcombo_level.SelectedIndex = 1;
					break;
				case Serilog.Events.LogEventLevel.Verbose:
					logcombo_level.SelectedIndex = 2;
					break;
			}

			loglevelpanel.Controls.Add(loglabel_level);
			loglevelpanel.Controls.Add(logcombo_level);
			logpanel.Controls.Add(loglist);
			logpanel.Controls.Add(loglevelpanel);
			//logpanel.Controls.Add(loglist);

			var commandpanel = new TableLayoutPanel
			{
				Dock = DockStyle.Top,
				RowCount = 1,
				ColumnCount = 12,
				Height = 40,
				AutoSize = true
			};

			processingCount = new NumericUpDown()
			{
				Minimum = 0,
				Maximum = ushort.MaxValue,
				Width = 48,
				ReadOnly = true,
				Enabled = false,
				Margin = new Padding(9),
				Dock = DockStyle.Left,
			};

			rescanbutton = new Button
			{
				Text = "Rescan",
				Dock = DockStyle.Top,
				Margin = new Padding(3 + 3),
				FlatStyle = FlatStyle.Flat
			};
			rescanbutton.Click += (object sender, EventArgs e) =>
			{
				try
				{
					rescanbutton.Enabled = false;

					rescanRequest?.Invoke(this, null);

					rescanbutton.Enabled = true;
				}
				catch { }
			};
			commandpanel.Controls.Add(new Label
			{
				Text = "Processing",
				Dock = DockStyle.Right,
				TextAlign = System.Drawing.ContentAlignment.MiddleRight,
				Padding = new Padding(6),
				AutoSize = true
			});
			commandpanel.Controls.Add(processingCount);
			processingCountdown = new Label()
			{
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
				Width = 40,
				AutoSize = false,
				Dock = DockStyle.Right,
				Anchor = AnchorStyles.Left
			};
			processingCountdown.Text = "00.0s";
			commandpanel.Controls.Add(processingCountdown);
			commandpanel.Controls.Add(rescanbutton);
			rescanbutton.Enabled = TaskMaster.ProcessMonitorEnabled;

			crunchbutton = new Button
			{
				Text = "Page",
				Dock = DockStyle.Top,
				FlatStyle = FlatStyle.Flat,
				Margin = new Padding(3 + 3),
				Enabled = TaskMaster.PagingEnabled
			};
			crunchbutton.Click += (object sender, EventArgs e) =>
			{
				try
				{
					crunchbutton.Enabled = false;

					pagingRequest?.Invoke(this, new EventArgs());

					crunchbutton.Enabled = true;
				}
				catch { }
			};

			commandpanel.Controls.Add(crunchbutton);

			var cachePanel = new TableLayoutPanel()
			{
				ColumnCount = 5,
				AutoSize = true
			};

			cachePanel.Controls.Add(new Label()
			{
				Text = "Path cache:",
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
				AutoSize = true
			});
			cachePanel.Controls.Add(new Label()
			{
				Text = "Objects",
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
				AutoSize = true
			});
			cacheObjects = new Label()
			{
				AutoSize = true,
				Width = 40,
				Text = "n/a",
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft
			};
			cachePanel.Controls.Add(cacheObjects);
			cachePanel.Controls.Add(new Label()
			{
				Text = "Ratio",
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
				AutoSize = true
			});
			cacheRatio = new Label()
			{
				AutoSize = true,
				Width = 40,
				Text = "n/a",
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft
			};
			cachePanel.Controls.Add(cacheRatio);

			infopanel.Controls.Add(cachePanel);

			tempObjectCount = new Label()
			{
				Width = 40,
				//Dock = DockStyle.Left,
				Text = "n/a",
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
			};

			tempObjectSize = new Label()
			{
				Width = 40,
				//Margin = new Padding(3 + 3),
				//Dock = DockStyle.Left,
				Text = "n/a",
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
			};

			var tempmonitorpanel = new TableLayoutPanel
			{
				Dock = DockStyle.Top,
				RowCount = 1,
				ColumnCount = 5,
				Height = 40,
				AutoSize = true
			};
			tempmonitorpanel.Controls.Add(new Label
			{
				Text = "Temp",
				Dock = DockStyle.Left,
				TextAlign = System.Drawing.ContentAlignment.MiddleRight,
				AutoSize = true
			});
			tempmonitorpanel.Controls.Add(new Label
			{
				Text = "Objects",
				Dock = DockStyle.Left,
				TextAlign = System.Drawing.ContentAlignment.MiddleRight,
				AutoSize = true
			});
			tempmonitorpanel.Controls.Add(tempObjectCount);
			tempmonitorpanel.Controls.Add(new Label
			{
				Text = "Size (MB)",
				Dock = DockStyle.Left,
				TextAlign = System.Drawing.ContentAlignment.MiddleRight,
				AutoSize = true
			});
			tempmonitorpanel.Controls.Add(tempObjectSize);

			//infopanel.Controls.Add(commandpanel);
			infopanel.Controls.Add(tempmonitorpanel);
			infoTab.Controls.Add(infopanel);

			lrows.Controls.Add(tabLayout);
			lrows.Controls.Add(commandpanel);
			lrows.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			lrows.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

			lrows.Controls.Add(logpanel);

			// POWER DEBUG TAB

			var powerlayout = new TableLayoutPanel()
			{
				ColumnCount = 1,
				AutoSize = true,
				Dock = DockStyle.Top
			};

			powerlayout.Controls.Add(new Label()
			{
				Text = "Power mode autobalancing tracker...",
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
				AutoSize = true,
				Dock = DockStyle.Left,
				Padding = new Padding(6)
			});

			powerbalancerlog = new ListView()
			{
				Dock = DockStyle.Top,
				AutoSize = true,
				//Height = 80,
				//Width = tabLayout.Width - 12, // FIXME: 3 for the bevel, but how to do this "right"?
				MinimumSize = new System.Drawing.Size(700, 80),
				FullRowSelect = true,
				View = View.Details,
			};
			powerbalancerlog.Columns.Add("Current", 60);
			powerbalancerlog.Columns.Add("Average", 60);
			powerbalancerlog.Columns.Add("High", 60);
			powerbalancerlog.Columns.Add("Low", 60);
			powerbalancerlog.Columns.Add("Reactionary Plan", 120);
			powerbalancerlog.Columns.Add("Enacted", 60);
			powerbalancerlog.Columns.Add("Pressure", 60);

			powerlayout.Controls.Add(powerbalancerlog);

			var powerbalancerstatus = new TableLayoutPanel()
			{
				ColumnCount = 4,
				AutoSize = true,
				Dock = DockStyle.Top
			};
			powerbalancerstatus.Controls.Add(new Label() { Text = "Behaviour:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			powerbalancer_behaviour = new Label() { Text = "n/a", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true };
			powerbalancerstatus.Controls.Add(powerbalancer_behaviour);
			powerbalancerstatus.Controls.Add(new Label() { Text = "| Plan:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			powerbalancer_plan = new Label() { Text = "n/a", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true };
			powerbalancerstatus.Controls.Add(powerbalancer_plan);

			powerlayout.Controls.Add(powerbalancerstatus);
			powerDebugTab.Controls.Add(powerlayout);

			// -------------------------------------------------------------------------------------------------------
			var exitlayout = new TableLayoutPanel()
			{
				ColumnCount = 1,
				AutoSize = true,
				Dock = DockStyle.Top
			};

			exitlayout.Controls.Add(activepanel);

			exitlayout.Controls.Add(new Label()
			{
				Text = "Exit wait list...",
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
				AutoSize = true,
				Dock = DockStyle.Left,
				Padding = new Padding(6)
			});

			exitwaitlist = new ListView()
			{
				AutoSize = true,
				Height = 180,
				Width = tabLayout.Width - 12, // FIXME: 3 for the bevel, but how to do this "right"?
				FullRowSelect = true,
				View = View.Details,
			};
			ExitWaitlistMap = new Dictionary<int, ListViewItem>();

			exitwaitlist.Columns.Add("Id", 50);
			exitwaitlist.Columns.Add("Executable", 280);
			exitwaitlist.Columns.Add("State", 160);
			exitwaitlist.Columns.Add("Power", 80);

			exitlayout.Controls.Add(exitwaitlist);
			ProcessDebugTab.Controls.Add(exitlayout);

			// End: UI Log

			/*
			TrackBar tb = new TrackBar();
			tb.Parent = micPanel;
			tb.TickStyle = TickStyle.Both;
			//tb.Size = new Size(150, 25);
			//tb.Height 
			tb.Dock = DockStyle.Fill; // fills parent
									  //tb.Location = new Point(0, 0); // insert point
			*/

			tabLayout.SelectedIndex = opentab >= tabLayout.TabCount ? 0 : opentab;

			AutoSize = true;
		}

		async void FreeMemoryRequest(object sender, EventArgs ev)
		{
			try
			{
				using (var exsel = new ProcessSelectDialog())
				{
					if (exsel.ShowDialog(this) == DialogResult.OK)
					{
						TaskMaster.processmanager.FreeMemory(exsel.Selection).ConfigureAwait(false);
					}
				}
			}
			catch { }
		}

		readonly object exitwaitlist_lock = new object();

		public async void ExitWaitListHandler(object sender, ProcessEventArgs ev)
		{
			// TODO: Proper async?

			lock (exitwaitlist_lock)
			{
				ListViewItem li = null;
				if (ExitWaitlistMap.TryGetValue(ev.Info.Id, out li))
				{
					//Log.Debug("WaitlistHandler: {Name} = {State}", ev.Info.Name, ev.State.ToString());
					try
					{
						switch (ev.State)
						{
							case ProcessEventArgs.ProcessState.Exiting:
								exitwaitlist.Items.Remove(li);
								ExitWaitlistMap.Remove(ev.Info.Id);
								break;
							case ProcessEventArgs.ProcessState.Found:
							case ProcessEventArgs.ProcessState.Reduced:
								li.SubItems[2].Text = "Background";
								break;
							case ProcessEventArgs.ProcessState.Restored:
								exitwaitlist.Items.RemoveAt(li.Index);
								li.SubItems[2].Text = "Foreground";
								exitwaitlist.Items.Insert(0, li);
								li.EnsureVisible();
								break;
							default:
								Log.Debug("Received unhandled process state: {State}", ev.State.ToString());
								break;
						}
					}
					catch (Exception ex)
					{
						Logging.Stacktrace(ex);
					}
				}
				else
				{
					if (ev.State == ProcessEventArgs.ProcessState.Starting)
					{
						li = new ListViewItem(new string[] {
							ev.Info.Id.ToString(),
							ev.Info.Name,
							"Unknown",
							(Bit.IsSet(ev.Info.Flags, (int)ProcessFlags.PowerWait) ? "FORCED" : "n/a")
						});

						try
						{
							exitwaitlist.Items.Add(li);
							ExitWaitlistMap.Add(ev.Info.Id, li);
							li.EnsureVisible();
						}
						catch { }
					}
				}
			}
		}

		public async void CPULoadHandler(object sender, ProcessorEventArgs ev) // Event Handler
		{
			if (!UIOpen) return;

			// TODO: Asyncify

			string reactionary = PowerManager.GetModeName(ev.Mode);

			var li = new ListViewItem(new string[] {
				string.Format("{0:N2}%", ev.Current),
				string.Format("{0:N2}%", ev.Average),
				string.Format("{0:N2}%", ev.High),
				string.Format("{0:N2}%", ev.Low),
				reactionary,
				ev.Handled.ToString(),
				string.Format("{0:N1}%", ev.Pressure*100f)
			});

			li.UseItemStyleForSubItems = false;
			if (ev.Mode == PowerInfo.PowerMode.HighPerformance)
				li.SubItems[3].BackColor = System.Drawing.Color.FromArgb(255, 230, 230);
			else if (ev.Mode == PowerInfo.PowerMode.PowerSaver)
				li.SubItems[2].BackColor = System.Drawing.Color.FromArgb(240, 255, 230);
			else
			{
				li.SubItems[3].BackColor = System.Drawing.Color.FromArgb(255, 250, 230);
				li.SubItems[2].BackColor = System.Drawing.Color.FromArgb(255, 250, 230);
			}

			lock (powerbalancerlog_lock)
			{

				try
				{
					if (powerbalancerlog.Items.Count > 3)
						powerbalancerlog.Items.RemoveAt(0);
					powerbalancerlog.Items.Add(li);
				}
				catch // this tends to happen if this event is being handled while the window is being closed
				{ }
			}
		}

		void WatchlistContextMenuOpen(object sender, EventArgs ea)
		{
			bool oneitem = watchlistRules.SelectedItems.Count == 1;
			try
			{
				foreach (ToolStripItem lsi in watchlistms.Items)
				{
					if (lsi.Text.Contains("Create")) continue;
					lsi.Enabled = oneitem;
				}
			}
			catch { }

			if (oneitem)
			{
				ListViewItem li = watchlistRules.SelectedItems[0];
				var prc = TaskMaster.processmanager.getWatchedController(li.SubItems[NameColumn].Text);
				if (prc != null)
				{
					watchlistenable.Enabled = true;
					watchlistenable.Checked = prc.Enabled;
				}
			}
			else
				watchlistenable.Enabled = false;
		}

		int WatchlistEditLock = 0; // TODO: Transition lock to critical parts instead of outside of them.

		void EnableWatchlistRule(object sender, EventArgs ea)
		{
			bool oneitem = watchlistRules.SelectedItems.Count == 1;
			if (oneitem)
			{
				ListViewItem li = watchlistRules.SelectedItems[0];
				var prc = TaskMaster.processmanager.getWatchedController(li.SubItems[NameColumn].Text);
				if (prc != null)
				{
					watchlistenable.Enabled = true;
					watchlistenable.Checked = prc.Enabled = !watchlistenable.Checked;
					// TODO: Signal toggling, in case cleanup is necessary.

					WatchlistItemColor(li, prc);
				}
			}
			else
				watchlistenable.Enabled = false;
		}

		void EditWatchlistRule(object sender, EventArgs ea)
		{
			try
			{
				if (watchlistRules.SelectedItems.Count == 1)
				{
					if (!Atomic.Lock(ref WatchlistEditLock))
					{
						Log.Warning("Only one item can be edited at a time.");
						return;
					}

					ListViewItem li = watchlistRules.SelectedItems[0];
					using (var editdialog = new WatchlistEditWindow(li.SubItems[NameColumn].Text, li)) // 1 = executable
					{
						var rv = editdialog.ShowDialog();
						WatchlistEditLock = 0;

						if (rv == DialogResult.OK)
						{
							// TODO: Signal .ini save
						}
					}
				}
			}
			catch { }
		}

		void AddWatchlistRule(object sender, EventArgs ea)
		{
			Log.Warning("Adding new watchlist rules in the UI is not supported yet.");
		}

		void DeleteWatchlistRule(object sender, EventArgs ea)
		{
			try
			{
				if (watchlistRules.SelectedItems.Count == 1)
				{
					var li = watchlistRules.SelectedItems[0];
					Log.Warning("[{Rule}] Rule removed", li.SubItems[NameColumn].Text);
					Log.Warning("Removing watchlist rules in the UI is not actually supported yet.");
				}
			}
			catch { }
		}

		void CopyRuleToClipboard(object sender, EventArgs ea)
		{
			try
			{
				if (watchlistRules.SelectedItems.Count == 1)
				{
					var li = watchlistRules.SelectedItems[0];
					string name = li.SubItems[NameColumn].Text;
					ProcessController prc = null;

					prc = processmanager.getWatchedController(name);

					if (prc == null)
					{
						Log.Error("[{Rule}] Not found. Something's terribly wrong.", name);
						return;
					}

					var sbs = new System.Text.StringBuilder();

					sbs.Append("[").Append(prc.FriendlyName).Append("]").AppendLine();
					if (!string.IsNullOrEmpty(prc.ExecutableFriendlyName))
						sbs.Append("Image = ").Append(prc.ExecutableFriendlyName).AppendLine();
					if (!string.IsNullOrEmpty(prc.Path))
						sbs.Append("Path = ").Append(prc.Path).AppendLine();
					sbs.Append("Priority = ").Append(prc.Priority.ToInt32()).AppendLine();
					sbs.Append("Increase = ").Append(prc.Increase).AppendLine();
					sbs.Append("Decrease = ").Append(prc.Decrease).AppendLine();
					sbs.Append("Affinity = ").Append(prc.Affinity.ToInt32()).AppendLine();
					if (prc.PowerPlan != PowerInfo.PowerMode.Undefined)
						sbs.Append("Power plan = ").Append(PowerManager.PowerModes[(int)prc.PowerPlan]).AppendLine();
					if (prc.Rescan > 0)
						sbs.Append("Rescan = ").Append(prc.Rescan).AppendLine();
					if (prc.AllowPaging)
						sbs.Append("Allow paging = ").Append(prc.AllowPaging).AppendLine();
					if (prc.ForegroundOnly)
						sbs.Append("Foreground only = ").Append(prc.ForegroundOnly).AppendLine();

					try
					{
						Clipboard.SetText(sbs.ToString());
						Log.Information("[{Rule}] Configuration saved to clipboard.", name);
					}
					catch
					{
						Log.Warning("[{Rule}] Failed to copy configuration to clipboard.", name);
					}
					sbs.Clear(); // Unnecessary?
				}
			}
			catch { }
		}

		void UpdateInfoPanel(object sender, EventArgs e)
		{
			try
			{
				ListViewItem ri = watchlistRules.SelectedItems[0];
				string name = ri.SubItems[0].Text;
				/*
				//Log.Debug("'{RowName}' selected in UI", ri.SubItems[0]);
				// TODO: Add info panel for selected item.
				ProcessController pc = null;
				foreach (var tpc in TaskMaster.processmanager.watchlist)
				{
					if (name == tpc.FriendlyName)
					{
						pc = tpc;
						break;
					}
				}
				if (pc == null) throw new ArgumentException(string.Format("{0} not found in watchlist.", name));

				Log.Information("[{FriendlyName}] Last seen: {Date} {Time}",
								pc.FriendlyName, pc.LastSeen.ToShortDateString(), pc.LastSeen.ToShortTimeString());
				*/
			}
			catch { }
		}

		Label tempObjectCount;
		Label tempObjectSize;

		public void TempScanStats(object sender, DiskEventArgs ev)
		{
			try
			{
				tempObjectSize.Text = (ev.Stats.Size / 1000 / 1000).ToString();
				tempObjectCount.Text = (ev.Stats.Dirs + ev.Stats.Files).ToString();
			}
			catch { }
		}

		readonly object loglistLock = new object();
		ListView loglist;

		public void FillLog()
		{
			MemoryLog.onNewEvent += onNewLog;

			lock (loglistLock)
			{
				//Log.Verbose("Filling GUI log.");
				foreach (var evmsg in MemoryLog.ToArray())
				{
					loglist.Items.Add(evmsg.Message);
				}
			}

			ShowLastLog();
		}

		public void hookActiveAppMonitor(ref ActiveAppManager aamon)
		{
			if (aamon == null) return;

			if (TaskMaster.Trace) Log.Verbose("Hooking active app manager.");

			activeappmonitor = aamon;
			activeappmonitor.ActiveChanged += OnActiveWindowChanged;
		}

		public void hookPowerManager(ref PowerManager pman)
		{
			if (pman == null) return;

			if (TaskMaster.Trace) Log.Verbose("Hooking power manager.");

			powermanager = pman;
			powermanager.onAutoAdjustAttempt += CPULoadHandler;
			powermanager.onBehaviourChange += PowerBehaviourDebugEvent;
			powermanager.onPlanChange += PowerPlanDebugEvent;

			PowerBehaviourDebugEvent(this, powermanager.Behaviour); // populates powerbalancer_behaviourr
			PowerPlanDebugEvent(this, new PowerModeEventArgs() { NewMode = powermanager.CurrentMode }); // populates powerbalancer_plan
		}

		public void PowerBehaviourDebugEvent(object sender, PowerManager.PowerBehaviour behaviour)
		{
			try
			{
				powerbalancer_behaviour.Text = (behaviour == PowerManager.PowerBehaviour.Auto) ? "Automatic" : ((behaviour == PowerManager.PowerBehaviour.Manual) ? "Manual" : "Rule-based");
				if (behaviour != PowerManager.PowerBehaviour.Auto)
					powerbalancerlog.Items.Clear();
			}
			catch { }
		}

		public void PowerPlanDebugEvent(object sender, PowerModeEventArgs ev)
		{
			try
			{
				powerbalancer_plan.Text = PowerManager.GetModeName(ev.NewMode);
			}
			catch { }
		}

		public void hookNetMonitor(ref NetManager net)
		{
			if (net == null) return; // disabled

			if (TaskMaster.Trace) Log.Verbose("Hooking network monitor.");

			this.netmonitor = net;

			foreach (var dev in net.Interfaces())
			{
				var li = new ListViewItem(new string[] {
					dev.Name,
					dev.Type.ToString(),
					dev.Status.ToString(),
					HumanInterface.ByteString(dev.Speed),
					dev.IPv4Address.ToString() ?? "n/a",
					dev.IPv6Address.ToString() ?? "n/a",
						"n/a",
						"n/a",
				});
				li.UseItemStyleForSubItems = false;
				ifaceList.Items.Add(li);
			}

			InetStatusLabel(net.InternetAvailable);
			NetStatusLabel(net.NetworkAvailable);

			//Tray?.Tooltip(2000, "Internet " + (net.InternetAvailable ? "available" : "unavailable"), "TaskMaster", net.InternetAvailable ? ToolTipIcon.Info : ToolTipIcon.Warning);

			net.InternetStatusChange += InetStatus;
			net.NetworkStatusChange += NetStatus;
			net.onSampling += NetSampleHandler;
		}

		void NetSampleHandler(object sender, NetDeviceTraffic ev)
		{
			try
			{
				ifaceList.Items[ev.Index].SubItems[PacketColumn].Text = string.Format("+{0}", ev.Traffic.Unicast);
				ifaceList.Items[ev.Index].SubItems[ErrorColumn].Text = string.Format("+{0}", ev.Traffic.Errors);
				if (ev.Traffic.Errors > 0)
					ifaceList.Items[ev.Index].SubItems[ErrorColumn].ForeColor = System.Drawing.Color.OrangeRed;
				else
					ifaceList.Items[ev.Index].SubItems[ErrorColumn].ForeColor = System.Drawing.SystemColors.ControlText;
			}
			catch { }
		}

		int PacketColumn = 6;
		int ErrorColumn = 7;

		void InetStatusLabel(bool available)
		{
			inetstatuslabel.Text = available ? "Connected" : "Disconnected";
			//inetstatuslabel.BackColor = available ? System.Drawing.Color.LightGoldenrodYellow : System.Drawing.Color.Red;
			inetstatuslabel.BackColor = available ? System.Drawing.Color.Transparent : System.Drawing.Color.Red;
		}

		public void InetStatus(object sender, InternetStatus e)
		{
			try
			{
				InetStatusLabel(e.Available);
			}
			catch { }
		}

		void NetStatusLabel(bool available)
		{
			netstatuslabel.Text = available ? "Up" : "Down";
			//netstatuslabel.BackColor = available ? System.Drawing.Color.LightGoldenrodYellow : System.Drawing.Color.Red;
			netstatuslabel.BackColor = available ? System.Drawing.Color.Transparent : System.Drawing.Color.Red;
		}

		public void NetStatus(object sender, NetworkStatus e)
		{
			try
			{
				NetStatusLabel(e.Available);
			}
			catch { }
		}

		// BUG: DO NOT LOG INSIDE THIS FOR FUCKS SAKE
		// it creates an infinite log loop
		public int MaxLogSize { get { return MemoryLog.Max; } private set { MemoryLog.Max = value; } }

		void ClearLog()
		{
			lock (loglistLock)
			{
				loglist.Clear();
				MemoryLog.Clear();
			}
		}

		public void onNewLog(object sender, LogEventArgs evmsg)
		{
			if (LogIncludeLevel.MinimumLevel > evmsg.Level) return;

			DateTime t = DateTime.Now;

			lock (loglistLock)
			{
				try
				{
					int excessitems = Math.Min(0, (loglist.Items.Count - MaxLogSize));
					while (excessitems-- > 0)
					{
						loglist.Items.RemoveAt(0);
					}

					loglist.Items.Add(evmsg.Message).EnsureVisible();
				}
				catch { }
			}
		}

		// constructor
		public MainWindow()
		{
			//InitializeComponent(); // TODO: WPF
			FormClosing += WindowClose;

			DoubleBuffered = true;

			//MakeTrayIcon();

			BuildUI();

			// TODO: Detect mic device changes
			// TODO: Delay fixing by 5 seconds to prevent fix diarrhea

			// the form itself
			WindowState = FormWindowState.Normal;
			FormBorderStyle = FormBorderStyle.Sizable;
			ShowInTaskbar = true;

			//FormBorderStyle = FormBorderStyle.FixedDialog; // no min/max buttons as wanted
			MinimizeBox = false;
			MaximizeBox = false;

			Hide();
			//CenterToScreen();

			Shown += (object sender, EventArgs e) =>
			{
				try
				{
					loglist.TopItem = loglist.Items[loglist.Items.Count - 1];
					ShowLastLog();
				}
				catch { }
			};

			// TODO: WPF
			/*
			System.Windows.Shell.JumpList jumplist = System.Windows.Shell.JumpList.GetJumpList(System.Windows.Application.Current);
			//System.Windows.Shell.JumpTask task = new System.Windows.Shell.JumpTask();
			System.Windows.Shell.JumpPath jpath = new System.Windows.Shell.JumpPath();
			jpath.Path = TaskMaster.cfgpath;
			jumplist.JumpItems.Add(jpath);
			jumplist.Apply();
			*/

			if (TaskMaster.Trace)
				Log.Verbose("MainWindow constructed");
		}

		void saveUIState()
		{
			if (watchlistRules.Columns.Count == 0) return;

			List<int> appWidths = new List<int>(watchlistRules.Columns.Count);
			for (int i = 0; i < watchlistRules.Columns.Count; i++)
				appWidths.Add(watchlistRules.Columns[i].Width);

			/*
			List<int> pathWidths = new List<int>(pathList.Columns.Count);
			for (int i = 0; i < pathList.Columns.Count; i++)
				pathWidths.Add(pathList.Columns[i].Width);
			*/

			List<int> ifaceWidths = new List<int>(ifaceList.Columns.Count);
			for (int i = 0; i < ifaceList.Columns.Count; i++)
				ifaceWidths.Add(ifaceList.Columns[i].Width);

			List<int> micWidths = new List<int>(micList.Columns.Count);
			for (int i = 0; i < micList.Columns.Count; i++)
				micWidths.Add(micList.Columns[i].Width);

			var cfg = TaskMaster.loadConfig("UI.ini");
			var cols = cfg["Columns"];
			cols["Apps"].IntValueArray = appWidths.ToArray();
			//cols["Paths"].IntValueArray = pathWidths.ToArray();
			cols["Mics"].IntValueArray = micWidths.ToArray();
			cols["Interfaces"].IntValueArray = ifaceWidths.ToArray();

			var uistate = cfg["Tabs"];
			uistate["Open"].IntValue = tabLayout.SelectedIndex;

			var windows = cfg["Windows"];
			windows["Main"].IntValueArray = new int[] { Width, Height };

			TaskMaster.MarkDirtyINI(cfg);
		}

		bool disposed = false;
		protected override void Dispose(bool disposing)
		{
			if (disposed) return;

			base.Dispose(disposing);

			if (disposing)
			{
				if (TaskMaster.Trace) Log.Verbose("Disposing main window...");

				MemoryLog.onNewEvent -= onNewLog;

				try
				{
					if (powermanager != null)
					{
						powermanager.onAutoAdjustAttempt -= CPULoadHandler;
						powermanager = null;
					}
				}
				catch { }

				try
				{
					activeappmonitor.ActiveChanged -= OnActiveWindowChanged;
					activeappmonitor = null;
				}
				catch { }

				try
				{
					if (diskmanager != null)
					{
						diskmanager.onTempScan -= TempScanStats;
						diskmanager = null;
					}
				}
				catch { }
				try
				{
					ProcessController.onTouch -= ProcAdjust;
					ProcessController.onLocate -= WatchlistPathLocatedEvent;
					processmanager.onWaitForExitEvent -= ExitWaitListHandler; //ExitWaitListHandler;
					rescanRequest -= processmanager.ScanEverythingRequest;
					processmanager.onInstanceHandling -= ProcessNewInstanceCount;
					processmanager.PathCacheUpdate -= PathCacheUpdate;
					processmanager.onActiveHandled -= ExitWaitListHandler;
					if (TaskMaster.PagingEnabled)
						pagingRequest -= processmanager.PageEverythingRequest;
					processmanager = null;
				}
				catch { }

				try
				{
					if (netmonitor != null)
					{
						netmonitor.InternetStatusChange -= InetStatus;
						netmonitor.NetworkStatusChange -= NetStatus;
						netmonitor.onSampling -= NetSampleHandler;
						netmonitor = null;
					}
				}
				catch { }

				try
				{
					if (micmon != null)
					{
						micmon.VolumeChanged -= volumeChangeDetected;
						micmon = null;
					}
				}
				catch { }

				UItimer.Stop();
				UItimer.Dispose();
				UItimer = null;

				exitwaitlist.Dispose();
				ExitWaitlistMap.Clear();
			}

			disposed = true;
		}
	}

	public class WatchlistSorter : IComparer
	{
		public int Column { get; set; } = 0;
		public SortOrder Order { get; set; } = SortOrder.Ascending;
		public bool Number { get; set; } = false;

		readonly CaseInsensitiveComparer Comparer = new CaseInsensitiveComparer();

		readonly int[] NumberColumns = new int[] { };

		public WatchlistSorter(int[] numberColumns = null)
		{
			if (numberColumns != null)
				NumberColumns = numberColumns;
		}

		public int Compare(object x, object y)
		{
			ListViewItem lix = (ListViewItem)x;
			ListViewItem liy = (ListViewItem)y;
			int result = 0;

			Number = NumberColumns.Contains(Column);

			if (!Number)
				result = Comparer.Compare(lix.SubItems[Column].Text, liy.SubItems[Column].Text);
			else
				result = Comparer.Compare(Convert.ToInt64(lix.SubItems[Column].Text), Convert.ToInt64(liy.SubItems[Column].Text));

			return Order == SortOrder.Ascending ? result : -result;
		}
	}
}

