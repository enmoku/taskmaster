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
using System.ComponentModel;

namespace TaskMaster
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Threading.Tasks;
	using System.Windows.Forms;
	using Serilog;

	// public class MainWindow : System.Windows.Window; // TODO: WPF
	sealed public class MainWindow : System.Windows.Forms.Form, WindowInterface
	{
		public void ShowConfigRequest(object sender, EventArgs e)
		{
			// TODO: Introduce configuration window
		}

		public void ExitRequest(object sender, EventArgs e)
		{
			TaskMaster.ConfirmExit(restart: false);
		}

		void WindowClose(object sender, FormClosingEventArgs e)
		{
			saveUIState();

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
					Log.Information("Exit: Windows shutting down.");
					break;
				case CloseReason.TaskManagerClosing:
					Log.Information("Exit: Task manager told us to close.");
					break;
				case CloseReason.ApplicationExitCall:
					Log.Information("Exit: User asked to close.");
					break;
				default:
					Log.Warning("Exit: Unidentified close reason: {CloseReason}", e.CloseReason);
					break;
			}
			//CLEANUP: Console.WriteLine("WindowClose.Handled");
		}

		// this restores the main window to a place where it can be easily found if it's lost
		public void RestoreWindowRequest(object sender, EventArgs e)
		{
			CenterToScreen();
			SetTopLevel(true); // this doesn't Keep it topmost, does it?
			TopMost = true;
			// toggle because we don't want to keep it there
			TopMost = false;
		}

		public void ShowLastLog()
		{
			if (loglist.Items.Count > 0)
			{
				loglist.EnsureVisible(loglist.Items.Count - 1);
			}
		}

		public void ShowWindowRequest(object sender, EventArgs e)
		{
			Show(); // FIXME: Gets triggered when menuitem is clicked

			//AutoSize = true;
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

			Log.Verbose("Hooking microphone monitor.");
			micName.Text = micmon.DeviceName;
			corCountLabel.Text = micmon.Corrections.ToString();
			micVol.Maximum = Convert.ToDecimal(micmon.Maximum);
			micVol.Minimum = Convert.ToDecimal(micmon.Minimum);
			micVol.Value = Convert.ToInt32(micmon.Volume);

			micmon.enumerate().ForEach((dev) => micList.Items.Add(new ListViewItem(new string[] { dev.Name, dev.GUID })));

			// TODO: Hook device changes
			micmon.VolumeChanged += volumeChangeDetected;
		}

		public void ProcAdjust(object sender, ProcessEventArgs ev)
		{
			//Log.Verbose("Process adjust received for '{FriendlyName}'.", e.Control.FriendlyName);

			ListViewItem item;
			lock (appw_lock)
			{
				if (appw.TryGetValue(ev.Control, out item))
				{
					item.SubItems[AdjustColumn].Text = ev.Control.Adjusts.ToString();
					//item.SubItems[SeenColumn].Text = e.Control.LastSeen.ToLocalTime().ToString();
				}
				else
					Log.Error("{FriendlyName} not found in app list.", ev.Control.FriendlyName);
			}
		}

		public void OnActiveWindowChanged(object sender, WindowChangedArgs e)
		{
			//int maxlength = 70;
			//string cutstring = e.Title.Substring(0, Math.Min(maxlength, e.Title.Length)) + (e.Title.Length > maxlength ? "..." : "");
			//activeLabel.Text = cutstring;
			activeLabel.Text = e.Title;
			activeExec.Text = e.Executable;
			activeFullscreen.Text = e.Fullscreen.True() ? "Full" : e.Fullscreen.False() ? "Window" : "Unknown";
			activePID.Text = string.Format("{0}", e.Id);
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
			PathCacheUpdate(null, null);

			foreach (ProcessController item in processmanager.watchlist)
			{
				AddToProcessList(item);
			}

			rescanRequest += processmanager.ProcessEverythingRequest;
			if (TaskMaster.PagingEnabled)
				pagingRequest += processmanager.PageEverythingRequest;

			ProcessController.onLocate += PathLocatedEvent;
			ProcessController.onTouch += ProcAdjust;
			ProcessController.PowermodeExitWaitEvent += ExitWaitListHandler;

			processingCount.Value = ProcessManager.Handling;

			var items = ProcessController.getWaitingExit();
			foreach (var bu in items)
			{
				ExitWaitListHandler(null, new ProcessEventArgs() { Control = null, State = ProcessEventArgs.ProcessState.Starting, Info = bu });
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

		bool alterColor = false;
		void AddToProcessList(ProcessController pc)
		{
			bool noprio = (pc.Increase == false && pc.Decrease == false);
			string prio = noprio ? "--- Any --- " : pc.Priority.ToString();

			var litem = new ListViewItem(new string[] {
					pc.FriendlyName, //.ToString(),
					pc.Executable,
					prio,
						(pc.Affinity.ToInt32() == ProcessManager.allCPUsMask ? "--- Any ---" : Convert.ToString(pc.Affinity.ToInt32(), 2).PadLeft(ProcessManager.CPUCount, '0')),
						(pc.PowerPlan != PowerManager.PowerMode.Undefined? pc.PowerPlan.ToString() : "--- Any ---"),
				//(pc.Rescan>0 ? pc.Rescan.ToString() : "n/a"),
					pc.Adjusts.ToString(),
						//(pc.LastSeen != DateTime.MinValue ? pc.LastSeen.ToLocalTime().ToString() : "Never"),
				(string.IsNullOrEmpty(pc.Path) ? "--- Any ---" : pc.Path)
					});

			if (noprio)
				litem.SubItems[PrioColumn].ForeColor = System.Drawing.SystemColors.GrayText;
			if (string.IsNullOrEmpty(pc.Path))
				litem.SubItems[PathColumn].ForeColor = System.Drawing.SystemColors.GrayText;
			if (pc.PowerPlan == PowerManager.PowerMode.Undefined)
				litem.SubItems[PowerColumn].ForeColor = System.Drawing.SystemColors.GrayText;

			lock (watchlistRules)
			{
				watchlistRules.Items.Add(litem);
				if (alterColor == true)
					litem.BackColor = System.Drawing.Color.FromArgb(245, 245, 245);

				appw.Add(pc, litem);
				alterColor = !alterColor;
			}
		}

		public void PathLocatedEvent(object sender, PathControlEventArgs e)
		{
			var pc = (ProcessController)sender;
			AddToProcessList(pc);
		}

		Label micName;
		NumericUpDown micVol;
		object micList_lock = new object();
		ListView micList;
		object appList_lock = new object();
		ListView watchlistRules;
		//object pathList_lock = new object();
		//ListView pathList;
		object appw_lock = new object();
		Dictionary<ProcessController, ListViewItem> appw = new Dictionary<ProcessController, ListViewItem>();
		Label corCountLabel;
		object processingCountLock = new object();
		NumericUpDown processingCount;
		Label processingCountdown;

		ListView powerbalancerlog;
		Label powerbalancer_behaviour;
		Label powerbalancer_plan;

		object powerbalancerlog_lock = new object();

		ListView exitwaitlist;
		Dictionary<int, ListViewItem> exitwaitlistw;

		void UserMicVol(object sender, EventArgs e)
		{
			// TODO: Handle volume changes. Not really needed. Give presets?
			//micMonitor.setVolume(micVol.Value);
		}

		void volumeChangeDetected(object sender, VolumeChangedEventArgs e)
		{
			micVol.Value = Convert.ToInt32(e.New);
			corCountLabel.Text = e.Corrections.ToString();
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
			cacheObjects.Text = Statistics.PathCacheCurrent.ToString();
			double ratio = (Statistics.PathCacheMisses > 0 ? (Statistics.PathCacheHits / Statistics.PathCacheMisses) : 1);
			if (ratio <= 99.99f)
				cacheRatio.Text = string.Format("{0:N2}", ratio);
			else
				cacheRatio.Text = ">99.99"; // let's just not overflow the UI
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
			if (TaskMaster.ProcessMonitorEnabled)
			{
				var t = (ProcessManager.LastRescan.Unixstamp() + (ProcessManager.RescanEverythingFrequency / 1000)) - DateTime.Now.Unixstamp();
				processingCountdown.Text = string.Format("{0:N0}s", t);
			}
		}

		void UpdateUptime(object sender, EventArgs e)
		{
			if (netmonitor != null)
				uptimestatuslabel.Text = HumanInterface.TimeString(netmonitor.Uptime);

			//string.Format("{0:N1} min(s)", net.Uptime.TotalMinutes);
		}

		ListView ifaceList;
		Button rescanbutton;
		Button crunchbutton;

		ContextMenuStrip ifacems;
		ContextMenuStrip loglistms;
		ContextMenuStrip watchlistms;

		void InterfaceContextMenuOpen(object sender, EventArgs e)
		{
			foreach (ToolStripItem msi in ifacems.Items)
			{
				msi.Enabled = (ifaceList.SelectedItems.Count == 1);
			}
		}

		int IPv4Column = 4;
		int IPv6Column = 5;

		void CopyIPv4AddressToClipboard(object sender, EventArgs ea)
		{
			if (ifaceList.SelectedItems.Count == 1)
			{
				var li = ifaceList.SelectedItems[0];
				string ipv4addr = li.SubItems[IPv4Column].Text;
				Clipboard.SetText(ipv4addr);
			}
		}

		void CopyIPv6AddressToClipboard(object sender, EventArgs ea)
		{
			if (ifaceList.SelectedItems.Count == 1)
			{
				var li = ifaceList.SelectedItems[0];
				string ipv6addr = string.Format("[{0}]", li.SubItems[IPv6Column].Text);
				Clipboard.SetText(ipv6addr);
			}
		}

		void LogContextMenuOpen(object sender, EventArgs ea)
		{
			foreach (ToolStripItem lsi in loglistms.Items)
			{
				lsi.Enabled = (loglist.SelectedItems.Count == 1);
			}
		}

		void CopyLogToClipboard(object sender, EventArgs ea)
		{
			if (loglist.SelectedItems.Count == 1)
			{
				var li = loglist.SelectedItems[0];
				string rv = li.SubItems[0].Text;
				if (rv.Length > 0)
					Clipboard.SetText(rv);
			}
		}

		TabControl tabLayout = null;

		int NameColumn = 0;
		int ExeColumn = 1;
		int PrioColumn = 2;
		int AffColumn = 3;
		int PowerColumn = 4;
		int RescanColumn = 5;
		int AdjustColumn = 5;
		int SeenColumn = 7;
		int PathColumn = 6;

		void EnsureDebugLog()
		{
			if (logcombo_level.SelectedIndex == 0)
				logcombo_level.SelectedIndex = 1;
		}

		void BuildUI()
		{
			//Size = new System.Drawing.Size(760, 640); // width, heigh
			Width = 760;

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
			menu_action.MouseHover += (sender, e) => { menu_action.ShowDropDown(); };
			// Sub Items
			var menu_action_rescan = new ToolStripMenuItem("Rescan", null, (o, s) =>
			{
				rescanRequest?.Invoke(this, null);
			});
			var menu_action_memoryfocus = new ToolStripMenuItem("Free memory for...", null, FreeMemoryRequest);
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

			var menu_config = new ToolStripMenuItem("Configuration");
			menu_config.MouseHover += (sender, e) => { menu_config.ShowDropDown(); };
			// Sub Items
			var menu_config_folder = new ToolStripMenuItem("Open directory", null, (s, e) => { Process.Start(TaskMaster.datapath); });
			menu_config.DropDownItems.Add(menu_config_folder);

			var menu_debug = new ToolStripMenuItem("Debug");
			menu_debug.MouseHover += (sender, e) => { menu_debug.ShowDropDown(); };
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

			menu_debug.DropDownItems.Add(menu_debug_inaction);
			menu_debug.DropDownItems.Add(new ToolStripSeparator());
			menu_debug.DropDownItems.Add(menu_debug_scanning);
			menu_debug.DropDownItems.Add(menu_debug_procs);
			menu_debug.DropDownItems.Add(menu_debug_paths);
			menu_debug.DropDownItems.Add(menu_debug_power);
			menu_debug.DropDownItems.Add(new ToolStripSeparator());
			menu_debug.DropDownItems.Add(menu_debug_clear);

			var menu_info = new ToolStripMenuItem("Info");
			menu_info.MouseHover += (sender, e) => { menu_info.ShowDropDown(); };

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
			Controls.Add(menu);

			tabLayout = new TabControl()
			{
				Parent = lrows,
				Height = 300,
				Padding = new System.Drawing.Point(6, 6),
				Dock = DockStyle.Fill,
				//Padding = new System.Drawing.Point(3, 3),
			};

			TabPage infoTab = new TabPage("Info");
			TabPage procTab = new TabPage("Processes");
			TabPage micTab = new TabPage("Microphone");
			if (!TaskMaster.MicrophoneMonitorEnabled) micTab.Hide();
			TabPage netTab = new TabPage("Network");
			if (!TaskMaster.NetworkMonitorEnabled) netTab.Hide();
			TabPage bugTab = new TabPage("Debug");

			tabLayout.Controls.Add(infoTab);
			tabLayout.Controls.Add(procTab);
			tabLayout.Controls.Add(micTab);
			tabLayout.Controls.Add(netTab);
			tabLayout.Controls.Add(bugTab);

			var infopanel = new TableLayoutPanel
			{
				Dock = DockStyle.Top,
				Width = tabLayout.Width - 12,
			};

			//Controls.Add(tabLayout);

			#region Main Window Row 0, game monitor / active window monitor
			var gamepanel = new TableLayoutPanel
			{
				Dock = DockStyle.Top,
				RowCount = 1,
				ColumnCount = 6,
				AutoSize = true,
				Width = tabLayout.Width - 3,
			};

			var activeLabelUX = new Label() { Text = "Active:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Width = 40 };
			activeLabel = new Label()
			{
				Dock = DockStyle.Top,
				Text = "no active window found",
				Width = tabLayout.Width - 3 - 40 - 3 - 80 - 3 - 100 - 3 - 60 - 3 - 20 - 3, // TODO: Simplify
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
				AutoEllipsis = true,
			};
			activeExec = new Label() { Dock = DockStyle.Top, Text = "n/a", Width = 100, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
			activeFullscreen = new Label() { Dock = DockStyle.Top, Text = "n/a", Width = 60, TextAlign = System.Drawing.ContentAlignment.MiddleCenter };
			activePID = new Label() { Text = "n/a", Width = 60, TextAlign = System.Drawing.ContentAlignment.MiddleCenter };
			//gamepanel.Padding = new Padding(3);
			gamepanel.Controls.Add(activeLabelUX);
			gamepanel.Controls.Add(activeLabel);
			gamepanel.Controls.Add(activeExec);
			gamepanel.Controls.Add(activeFullscreen);
			gamepanel.Controls.Add(new Label { Text = "Id:", Width = 20, TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
			gamepanel.Controls.Add(activePID);

			infopanel.Controls.Add(gamepanel);
			//infoTab.Controls.Add(infopanel);
			#endregion

			#region Load UI config
			var uicfg = TaskMaster.loadConfig("UI.ini");
			var colcfg = uicfg["Columns"];
			int opentab = uicfg["Tabs"].TryGet("Open")?.IntValue ?? 0;

			int[] appwidthsDefault = new int[] { 120, 140, 82, 60, 76, 60, 140 };
			var appwidths = colcfg.GetSetDefault("Apps", appwidthsDefault).IntValueArray;
			if (appwidths.Length != appwidthsDefault.Length) appwidths = appwidthsDefault;

			int[] micwidthsDefault = new int[] { 200, 220 };
			var micwidths = colcfg.GetSetDefault("Mics", micwidthsDefault).IntValueArray;
			if (micwidths.Length != micwidthsDefault.Length) micwidths = micwidthsDefault;

			int[] ifacewidthsDefault = new int[] { 120, 60, 50, 70, 90, 200, 60, 60 };
			var ifacewidths = colcfg.GetSetDefault("Interfaces", ifacewidthsDefault).IntValueArray;
			if (ifacewidths.Length != ifacewidthsDefault.Length) ifacewidths = ifacewidthsDefault;
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

			if (ProcessManager.RescanEverythingFrequency > 0)
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

			// TODO: Add context menu
			watchlistms = new ContextMenuStrip();
			watchlistms.Opened += WatchlistContextMenuOpen;
			var watchlistedit = new ToolStripMenuItem("Edit", null, EditWatchlistRule);
			var watchlistadd = new ToolStripMenuItem("Create new", null, AddWatchlistRule);
			var watchlistdel = new ToolStripMenuItem("Remove", null, DeleteWatchlistRule);
			var watchlistclip = new ToolStripMenuItem("Copy to clipboard", null, CopyRuleToClipboard);

			watchlistms.Items.Add(watchlistedit);
			watchlistms.Items.Add(watchlistadd);
			watchlistms.Items.Add(new ToolStripSeparator());
			watchlistms.Items.Add(watchlistdel);
			watchlistms.Items.Add(new ToolStripSeparator());
			watchlistms.Items.Add(watchlistclip);
			watchlistRules.ContextMenuStrip = watchlistms;

			NameColumn = 0;
			ExeColumn = 1;
			PrioColumn = 2;
			AffColumn = 3;
			PowerColumn = 4;
			AdjustColumn = 5;
			PathColumn = 6;

			watchlistRules.Columns.Add("Name", appwidths[0]);
			watchlistRules.Columns.Add("Executable", appwidths[1]);
			watchlistRules.Columns.Add("Priority", appwidths[2]);
			watchlistRules.Columns.Add("Affinity", appwidths[3]);
			watchlistRules.Columns.Add("Power Plan", appwidths[4]);
			watchlistRules.Columns.Add("Adjusts", appwidths[5]);
			watchlistRules.Columns.Add("Path", appwidths[6]);
			watchlistRules.Scrollable = true;
			watchlistRules.Alignment = ListViewAlignment.Left;

			watchlistRules.DoubleClick += EditWatchlistRule; // for in-app editing
			watchlistRules.Click += UpdateInfoPanel;

			//proclayout.Controls.Add(pathList);
			proclayout.Controls.Add(watchlistRules);
			procTab.Controls.Add(proclayout);

			// End: App list

			// UI Log
			loglist = new ListView
			{
				//Dock = DockStyle.Fill,
				View = View.Details,
				FullRowSelect = true,
				HeaderStyle = ColumnHeaderStyle.None,
				Scrollable = true,
				Height = 210,
				Width = tabLayout.Width - 16,
			};

			loglist_stamp = new List<DateTime>();

			loglist.Columns.Add("Log content");
			loglist.Columns[0].Width = loglist.Width - 32;

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
				Parent = lrows,
				//Dock = DockStyle.Fill,
				RowCount = 2,
				ColumnCount = 1,
				Width = lrows.Width,
				AutoSize = true
			};

			var loglevelpanel = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				RowCount = 1,
				ColumnCount = 2,
				AutoSize = true
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
			rescanbutton.Click += async (object sender, EventArgs e) =>
						{
							rescanbutton.Enabled = false;
							await Task.Yield();
							rescanRequest?.Invoke(this, null);
							rescanbutton.Enabled = true;
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
			crunchbutton.Click += async (object sender, EventArgs e) =>
			{
				crunchbutton.Enabled = false;
				await Task.Yield();
				pagingRequest?.Invoke(this, new EventArgs());
				crunchbutton.Enabled = true;
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

			// DEBUG TAB

			var buglayout = new TableLayoutPanel()
			{
				ColumnCount = 1,
				AutoSize = true,
				Dock = DockStyle.Top
			};

			exitwaitlist = new ListView()
			{
				AutoSize = true,
				Height = 80,
				Width = tabLayout.Width - 12, // FIXME: 3 for the bevel, but how to do this "right"?
				FullRowSelect = true,
				View = View.Details,
			};
			exitwaitlistw = new Dictionary<int, ListViewItem>();

			exitwaitlist.Columns.Add("Id", 50);
			exitwaitlist.Columns.Add("Executable", 280);

			buglayout.Controls.Add(new Label()
			{
				Text = "Power mode exit wait list...",
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
				AutoSize = true,
				Dock = DockStyle.Left,
				Padding = new Padding(6)
			});
			buglayout.Controls.Add(exitwaitlist);

			powerbalancerlog = new ListView()
			{
				AutoSize = true,
				Height = 80,
				Width = tabLayout.Width - 12, // FIXME: 3 for the bevel, but how to do this "right"?
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

			buglayout.Controls.Add(new Label()
			{
				Text = "Power mode autobalancing tracker...",
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
				AutoSize = true,
				Dock = DockStyle.Left,
				Padding = new Padding(6)
			});
			buglayout.Controls.Add(powerbalancerlog);

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

			buglayout.Controls.Add(powerbalancerstatus);
			bugTab.Controls.Add(buglayout);

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

			AutoSizeMode = AutoSizeMode.GrowOnly;
			AutoSize = true;
		}

		void FreeMemoryRequest(object sender, EventArgs ev)
		{
			using (var exsel = new ProcessSelectDialog())
			{
				if (exsel.ShowDialog(this) == DialogResult.OK)
				{
					Log.Information("Freeing memory for: {Exec}", exsel.Selection);
					TaskMaster.processmanager.FreeMemoryFor(exsel.Selection);
				}
			}
		}

		object exitwaitlist_lock = new object();
		public void ExitWaitListHandler(object sender, ProcessEventArgs ev)
		{
			lock (exitwaitlist_lock)
			{
				if (ev.State == ProcessEventArgs.ProcessState.Starting)
				{
					var li = new ListViewItem(new string[] { ev.Info.Id.ToString(), ev.Info.Name });
					//li.Name = ev.Info.Id.ToString();
					try
					{
						exitwaitlistw.Add(ev.Info.Id, li);
						exitwaitlist.Items.Add(li);
					}
					catch { /* NOP, System.ArgumentException, already in list */ }
				}
				else if (ev.State == ProcessEventArgs.ProcessState.Exiting || ev.State == ProcessEventArgs.ProcessState.Cancel)
				{
					ListViewItem li = null;
					if (exitwaitlistw.TryGetValue(ev.Info.Id, out li))
					{
						exitwaitlist.Items.Remove(li);
						exitwaitlistw.Remove(ev.Info.Id);
					}
				}
			}
		}

		public void CPULoadHandler(object sender, ProcessorEventArgs ev)
		{
			if (!UIOpen) return;

			PowerManager.PowerMode powerreact = ev.Mode;

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

			if (powerreact == PowerManager.PowerMode.HighPerformance)
				li.SubItems[3].BackColor = System.Drawing.Color.FromArgb(255, 230, 230);
			else if (powerreact == PowerManager.PowerMode.PowerSaver)
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
				catch // this tends to happen close to exit
				{ }
			}

			if (ev.Handled)
			{
				powerbalancer_plan.Text = reactionary;
			}
		}

		void WatchlistContextMenuOpen(object sender, EventArgs ea)
		{
			foreach (ToolStripItem lsi in watchlistms.Items)
			{
				if (lsi.Text.Contains("Create")) continue;
				lsi.Enabled = (watchlistRules.SelectedItems.Count == 1);
			}
		}

		int WatchlistEditLock = 0; // TODO: Transition lock to critical parts instead of outside of them.

		void EditWatchlistRule(object sender, EventArgs ea)
		{
			if (watchlistRules.SelectedItems.Count == 1)
			{
				if (System.Threading.Interlocked.CompareExchange(ref WatchlistEditLock, 1, 0) == 1)
				{
					Log.Warning("Only one item can be edited at a time.");
					return;
				}

				ListViewItem li = watchlistRules.SelectedItems[0];
				using (var editdialog = new AppEditWindow(li.SubItems[NameColumn].Text, li)) // 1 = executable
				{
					var rv = editdialog.ShowDialog();
					WatchlistEditLock = 0;
				}
			}
		}

		void AddWatchlistRule(object sender, EventArgs ea)
		{
			Log.Warning("Adding new watchlist rules in the UI is not supported yet.");
		}

		void DeleteWatchlistRule(object sender, EventArgs ea)
		{
			if (watchlistRules.SelectedItems.Count == 1)
			{
				var li = watchlistRules.SelectedItems[0];
				Log.Warning("[{Rule}] Rule removed", li.SubItems[NameColumn].Text);
				Log.Warning("Removing watchlist rules in the UI is not actually supported yet.");
			}
		}

		void CopyRuleToClipboard(object sender, EventArgs ea)
		{
			if (watchlistRules.SelectedItems.Count == 1)
			{
				var li = watchlistRules.SelectedItems[0];
				string name = li.SubItems[NameColumn].Text;
				ProcessController pc = null;
				foreach (var tpc in TaskMaster.processmanager.watchlist)
				{
					if (name == tpc.FriendlyName)
					{
						pc = tpc;
						break;
					}
				}

				if (pc == null)
				{
					Log.Error("[{Rule}] Not found. Something's terribly wrong.", name);
					return;
				}

				var sbs = new System.Text.StringBuilder();

				sbs.Append("[").Append(pc.FriendlyName).Append("]").AppendLine();
				if (!string.IsNullOrEmpty(pc.ExecutableFriendlyName))
					sbs.Append("Image = ").Append(pc.ExecutableFriendlyName).AppendLine();
				if (!string.IsNullOrEmpty(pc.Path))
					sbs.Append("Path = ").Append(pc.Path).AppendLine();
				sbs.Append("Priority = ").Append(pc.Priority.ToInt32()).AppendLine();
				sbs.Append("Increase = ").Append(pc.Increase).AppendLine();
				sbs.Append("Decrease = ").Append(pc.Decrease).AppendLine();
				sbs.Append("Affinity = ").Append(pc.Affinity.ToInt32()).AppendLine();
				if (pc.PowerPlan != PowerManager.PowerMode.Undefined)
					sbs.Append("Power plan = ").Append(PowerManager.PowerModes[(int)pc.PowerPlan]).AppendLine();
				if (pc.Rescan > 0)
					sbs.Append("Rescan = ").Append(pc.Rescan).AppendLine();
				if (pc.AllowPaging)
					sbs.Append("Allow paging = ").Append(pc.AllowPaging).AppendLine();
				if (pc.ForegroundOnly)
					sbs.Append("Foreground only = ").Append(pc.ForegroundOnly).AppendLine();

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

		void UpdateInfoPanel(object sender, EventArgs e)
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

		Label tempObjectCount;
		Label tempObjectSize;

		public void TempScanStats(object sender, DiskEventArgs ev)
		{
			tempObjectSize.Text = (ev.Stats.Size / 1000 / 1000).ToString();
			tempObjectCount.Text = (ev.Stats.Dirs + ev.Stats.Files).ToString();
		}

		object loglistLock = new object();
		ListView loglist;
		List<DateTime> loglist_stamp;

		public void FillLog()
		{
			MemoryLog.onNewEvent += onNewLog;

			lock (loglistLock)
			{
				//Log.Verbose("Filling GUI log.");
				var logcopy = MemoryLog.Copy();
				foreach (LogEventArgs evmsg in logcopy)
				{
					loglist.Items.Add(evmsg.Message);
					loglist_stamp.Add(DateTime.Now);
				}
			}

			ShowLastLog();
		}

		public void hookActiveAppMonitor(ref ActiveAppManager aamon)
		{
			activeappmonitor = aamon;
			activeappmonitor.ActiveChanged += OnActiveWindowChanged;
		}

		public void hookPowerManager(ref PowerManager pman)
		{
			powermanager = pman;
			powermanager.onAutoAdjustAttempt += CPULoadHandler;
			powermanager.onBehaviourChange += PowerBehaviourDebugEvent;
			powermanager.onPlanChange += PowerPlanDebugEvent;

			PowerBehaviourDebugEvent(this, powermanager.Behaviour); // populates powerbalancer_behaviourr
			PowerPlanDebugEvent(this, new PowerModeEventArgs() { NewMode = powermanager.CurrentMode }); // populates powerbalancer_plan
		}

		public void PowerBehaviourDebugEvent(object sender, PowerManager.PowerBehaviour behaviour)
		{
			powerbalancer_behaviour.Text = (behaviour == PowerManager.PowerBehaviour.Auto) ? "Automatic" : ((behaviour == PowerManager.PowerBehaviour.Manual) ? "Manual" : "Rule-based");
			if (behaviour != PowerManager.PowerBehaviour.Auto)
				powerbalancerlog.Items.Clear();
		}

		public void PowerPlanDebugEvent(object sender, PowerModeEventArgs ev)
		{
			powerbalancer_plan.Text = PowerManager.GetModeName(ev.NewMode);
		}

		public void hookNetMonitor(ref NetManager net)
		{
			if (net == null) return; // disabled

			Log.Verbose("Hooking network monitor.");

			this.netmonitor = net;

			foreach (NetDevice dev in net.Interfaces())
			{
				var li = new ListViewItem(new string[] {
					dev.Name,
					dev.Type.ToString(),
					dev.Status.ToString(),
					HumanInterface.ByteRateString(dev.Speed),
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
			catch
			{
			}
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
			InetStatusLabel(e.Available);
		}

		void NetStatusLabel(bool available)
		{
			netstatuslabel.Text = available ? "Up" : "Down";
			//netstatuslabel.BackColor = available ? System.Drawing.Color.LightGoldenrodYellow : System.Drawing.Color.Red;
			netstatuslabel.BackColor = available ? System.Drawing.Color.Transparent : System.Drawing.Color.Red;
		}

		public void NetStatus(object sender, NetworkStatus e)
		{
			NetStatusLabel(e.Available);
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

		public void onNewLog(object sender, LogEventArgs e)
		{
			if (LogIncludeLevel.MinimumLevel > e.Level) return;

			lock (loglistLock)
			{
				DateTime t = DateTime.Now;

				int excessitems = (loglist.Items.Count - MaxLogSize).Min(0);
				while (excessitems-- > 0)
				{
					loglist.Items.RemoveAt(0);
					loglist_stamp.RemoveAt(0);
				}

				loglist.Items.Add(e.Message).EnsureVisible();
				loglist_stamp.Add(DateTime.Now);
			}
		}

		// constructor
		public MainWindow()
		{
			//InitializeComponent(); // TODO: WPF
			FormClosing += WindowClose;

			//MakeTrayIcon();

			BuildUI();

			// TODO: Detect mic device changes
			// TODO: Delay fixing by 5 seconds to prevent fix diarrhea

			// the form itself
			WindowState = FormWindowState.Normal;
			FormBorderStyle = FormBorderStyle.FixedDialog; // no min/max buttons as wanted
			MinimizeBox = false;
			MaximizeBox = false;
			Hide();
			//CenterToScreen();

			Shown += (object sender, EventArgs e) =>
			{
				loglist.TopItem = loglist.Items[loglist.Items.Count - 1];
				ShowLastLog();
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

			TaskMaster.MarkDirtyINI(cfg);
		}

		bool disposed; // = false;
		protected override void Dispose(bool disposing)
		{
			if (disposed) return;

			base.Dispose(disposing);

			if (disposing)
			{
				Log.Verbose("Disposing main window...");
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
					ProcessController.onLocate -= PathLocatedEvent;
					ProcessController.PowermodeExitWaitEvent -= ExitWaitListHandler;
					rescanRequest -= processmanager.ProcessEverythingRequest;
					processmanager.onInstanceHandling -= ProcessNewInstanceCount;
					processmanager.PathCacheUpdate -= PathCacheUpdate;
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
			}

			disposed = true;
		}
	}
}

