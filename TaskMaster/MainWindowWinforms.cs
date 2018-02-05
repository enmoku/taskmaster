//
// MainWindow.cs
//
// Author:
//       M.A. (enmoku) <>
//
// Copyright (c) 2016 M.A. (enmoku)
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
using Serilog.Sinks.File;

namespace TaskMaster
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
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
			//CLEANUP: Console.WriteLine("START:Window.ExitRequest");
			// nothing
			//CLEANUP: Console.WriteLine("END:Window.ExitRequest");
		}

		void WindowClose(object sender, FormClosingEventArgs e)
		{
			saveColumns();

			//Console.WriteLine("WindowClose = " + e.CloseReason);
			switch (e.CloseReason)
			{
				case CloseReason.UserClosing:
					// X was pressed or similar, we're just hiding to tray.
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

		static readonly System.Drawing.Size SetSizeDefault = new System.Drawing.Size(720, 800); // width, height

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
			AutoSize = true;
			Size = SetSizeDefault; // shouldn't be done always, but ensures the window is right size iff desktop was resized to something smaller.
		}

		#region Microphone control code
		MicMonitor micmon;
		public void setMicMonitor(MicMonitor micmonitor)
		{
			Debug.Assert(micmonitor != null);
			micmon = micmonitor;

			Log.Verbose("Hooking microphone monitor.");
			micName.Text = micmon.DeviceName;
			corCountLabel.Text = micmon.Corrections.ToString();
			micmon.VolumeChanged += volumeChangeDetected;
			micVol.Maximum = Convert.ToDecimal(micmon.Maximum);
			micVol.Minimum = Convert.ToDecimal(micmon.Minimum);
			micVol.Value = Convert.ToInt32(micmon.Volume);

			micmon.enumerate().ForEach((dev) => micList.Items.Add(new ListViewItem(new string[] { dev.Name, dev.GUID })));

			// TODO: Hook device changes
		}

		public void ProcAdjust(object sender, ProcessEventArgs e)
		{
			Log.Verbose("Process adjust received for '{ProcessName}'.", e.Control.FriendlyName);

			ListViewItem item;
			lock (appc_lock)
			{
				if (appc.TryGetValue(e.Control.Executable, out item))
				{
					item.SubItems[5].Text = e.Control.Adjusts.ToString();
					item.SubItems[6].Text = e.Control.LastSeen.ToLocalTime().ToString();
				}
				else
					Log.Error(string.Format("{0} not found in app list.", e.Control.Executable));
			}
		}

		public void OnActiveWindowChanged(object sender, WindowChangedArgs e)
		{
			int maxlength = 70;
			string cutstring = e.Title.Substring(0, Math.Min(maxlength, e.Title.Length)) + (e.Title.Length > maxlength ? "..." : "");
			activeLabel.Text = cutstring;
			activeExec.Text = e.Executable;
			activeFullscreen.Text = e.Fullscreen.True() ? "Full" : e.Fullscreen.False() ? "Window" : "Unknown";
			activePID.Text = string.Format("{0}", e.Id);
		}

		public event EventHandler rescanRequest;
		public event EventHandler pagingRequest;

		public void setProcControl(ProcessManager control)
		{
			Debug.Assert(control != null);

			ProcessManager.onInstanceHandling += ProcessNewInstanceCount;

			//control.onProcAdjust += ProcAdjust;
			lock (appc_lock)
			{
				foreach (ProcessController item in control.images)
				{
					var litem = new ListViewItem(new string[] {
					item.FriendlyName, //.ToString(),
					item.Executable,
					item.Priority.ToString(),
						(item.Affinity.ToInt32() == ProcessManager.allCPUsMask ? "OS" : Convert.ToString(item.Affinity.ToInt32(), 2).PadLeft(ProcessManager.CPUCount, '0'),
					//item.Boost.ToString(),
						(item.Children ? item.ChildPriority.ToString() : "n/a"),
					item.Adjusts.ToString(),
						(item.LastSeen != DateTime.MinValue ? item.LastSeen.ToLocalTime().ToString() : "Never"),
						(item.Rescan>0?item.Rescan.ToString():"n/a")
					});
					appc.Add(item.Executable, litem);
				}
			}

			lock (appList)
			{
				appList.Items.AddRange(appc.Values.ToArray());
			}

			lock (appw)
			{
				control.ActivePaths().ForEach(
					(PathControl path) =>
				{
					appw.Add(path, new ListViewItem(new string[] {
						path.FriendlyName,
						path.Path,
						path.Adjusts.ToString(),
						path.Priority.ToString(),
						(path.Affinity.ToInt32() == ProcessManager.allCPUsMask ? "OS" : Convert.ToString(path.Affinity.ToInt32(), 2).PadLeft(ProcessManager.CPUCount, '0')),
						(path.PowerPlan != PowerManager.PowerMode.Undefined ? path.PowerPlan.ToString() : "n/a")
				}));
				});

				lock (pathList_lock)
				{
					pathList.Items.AddRange(appw.Values.ToArray());
				}
			}

			PathControl.onLocate += PathLocatedEvent;
			PathControl.onTouch += PathAdjustEvent;
			processingCount.Value = ProcessManager.Handling;
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

		public void PathAdjustEvent(object sender, PathControlEventArgs e)
		{
			var pc = sender as PathControl;
			ListViewItem ni;
			lock (appw)
			{
				if (appw.TryGetValue(pc, out ni))
					ni.SubItems[2].Text = pc.Adjusts.ToString();
			}
		}

		public void PathLocatedEvent(object sender, PathControlEventArgs e)
		{
			var pc = sender as PathControl;
			Log.Verbose("{PathName} // {Path}", pc.FriendlyName, pc.Path);
			var ni = new ListViewItem(new string[] {
				pc.FriendlyName,
				pc.Path,
				"0",
				pc.Priority.ToString(),
				(pc.Affinity.ToInt32() == ProcessManager.allCPUsMask ? "OS" : Convert.ToString(pc.Affinity.ToInt32(), 2).PadLeft(ProcessManager.CPUCount, '0')),
				(pc.PowerPlan != PowerManager.PowerMode.Undefined ? pc.PowerPlan.ToString() : "n/a")
			});
			try
			{
				lock (appw) appw.Add(pc, ni);
				lock (pathList) pathList.Items.Add(ni);
			}
			catch (Exception)
			{
				// FIXME: This happens mostly because Application.Run() is triggered after we do ProcessEverything() and the events are processed only after
				Log.Warning("[Expected] Superfluous path watch update: {PathName}", pc.FriendlyName);
			}
		}

		Label micName;
		NumericUpDown micVol;
		object micList_lock = new object();
		ListView micList;
		object appList_lock = new object();
		ListView appList;
		object pathList_lock = new object();
		ListView pathList;
		object appw_lock = new object();
		Dictionary<PathControl, ListViewItem> appw = new Dictionary<PathControl, ListViewItem>();
		object appc_lock = new object();
		Dictionary<string, ListViewItem> appc = new Dictionary<string, ListViewItem>();
		Label corCountLabel;
		object processingCountLock = new object();
		NumericUpDown processingCount;

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

		// BackColor = System.Drawing.Color.LightGoldenrodYellow
		Label netstatuslabel = new Label { Dock = DockStyle.Top, Text = "Uninitialized", AutoSize = true, BackColor = System.Drawing.Color.Transparent };
		Label inetstatuslabel = new Label { Dock = DockStyle.Top, Text = "Uninitialized", AutoSize = true, BackColor = System.Drawing.Color.Transparent };
		Label uptimestatuslabel = new Label { Dock = DockStyle.Top, Text = "Uninitialized", AutoSize = true, BackColor = System.Drawing.Color.Transparent };

		TrayAccess tray;
		public TrayAccess Tray { private get { return tray; } set { tray = value; } }

		ComboBox logcombo_level;
		public static Serilog.Core.LoggingLevelSwitch LogIncludeLevel;

		readonly Timer UItimer = new Timer { Interval = 5000 };

		void StartUIUpdates(object sender, EventArgs e)
		{
			if (!UItimer.Enabled) UItimer.Start();
		}

		void StopUIUpdates(object sender, EventArgs e)
		{
			if (UItimer.Enabled) UItimer.Stop();
		}

		void UpdateUptime(object sender, EventArgs e)
		{
			if (net != null)
				uptimestatuslabel.Text = HumanInterface.TimeString(net.Uptime);

			//string.Format("{0:N1} min(s)", net.Uptime.TotalMinutes);
		}

		ListView ifaceList;
		Button rescanbutton;
		Button crunchbutton;

		ContextMenuStrip ifacems;

		void InterfaceContextMenuOpen(object sender, EventArgs e)
		{
			foreach (ToolStripItem msi in ifacems.Items)
			{
				msi.Enabled = (ifaceList.SelectedItems.Count == 1);
			}
		}

		void CopyIPv4AddressToClipboard(object sender, EventArgs ea)
		{
			if (ifaceList.SelectedItems.Count == 1)
			{
				var li = ifaceList.SelectedItems[0];
				string ipv4addr = li.SubItems[4].Text;
				Clipboard.SetText(ipv4addr);
			}
		}

		void CopyIPv6AddressToClipboard(object sender, EventArgs ea)
		{
			if (ifaceList.SelectedItems.Count == 1)
			{
				var li = ifaceList.SelectedItems[0];
				string ipv6addr = string.Format("[{0}]", li.SubItems[5].Text);
				Clipboard.SetText(ipv6addr);
			}
		}

		ContextMenuStrip loglistms;

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

		void BuildUI()
		{
			Size = SetSizeDefault;
			AutoSizeMode = AutoSizeMode.GrowOnly;
			AutoSize = true;

			Text = string.Format("{0} ({1})", System.Windows.Forms.Application.ProductName, System.Windows.Forms.Application.ProductVersion);
#if DEBUG
			Text = Text + " DEBUG";
#endif
			Padding = new Padding(12);
			// margin

			/*
TabControl tabLayout = new TabControl();
tabLayout.Parent = this;
tabLayout.Padding = new System.Drawing.Point(3, 3);

TabPage infoTab = new TabPage("Info");
TabPage appTab = new TabPage("Processes");
TabPage micTab = new TabPage("Microphone");
TabPage netTab = new TabPage("Network");
TabPage logTab = new TabPage("Info log");

tabLayout.Controls.Add(infoTab);
tabLayout.Controls.Add(appTab);
tabLayout.Controls.Add(micTab);
tabLayout.Controls.Add(netTab);
tabLayout.Controls.Add(logTab);

Controls.Add(tabLayout);
			*/

			var lrows = new TableLayoutPanel
			{
				Parent = this,
				ColumnCount = 1,
				//lrows.RowCount = 10;
				Dock = DockStyle.Fill
			};

			#region Main Window Row 0, game monitor / active window monitor
			var gamepanel = new TableLayoutPanel
			{
				Dock = DockStyle.Top,
				RowCount = 1,
				ColumnCount = 6,
				AutoSize = true,
				//BackColor = System.Drawing.Color.LightYellow,
				Width = lrows.Width - 3,
			};

			var activeLabelUX = new Label();
			activeLabelUX.Text = "Active:";
			activeLabelUX.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			activeLabelUX.Width = 40;
			//activeLabelUX.BackColor = System.Drawing.Color.GreenYellow;
			activeLabel = new Label();
			activeLabel.Dock = DockStyle.Top;
			activeLabel.Text = "no active window found";
			activeLabel.Width = lrows.Width - 3 - 40 - 3 - 80 - 3 - 100 - 3 - 60 - 3 - 20 - 3;
			activeLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			//activeLabel.BackColor = System.Drawing.Color.MediumAquamarine;
			activeExec = new Label();
			activeLabel.Dock = DockStyle.Top;
			activeExec.Text = "n/a";
			activeExec.Width = 100;
			activeExec.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			//activeExec.BackColor = System.Drawing.Color.Aquamarine;
			activeFullscreen = new Label();
			activeFullscreen.Dock = DockStyle.Top;
			activeFullscreen.Text = "n/a";
			activeFullscreen.Width = 60;
			activeFullscreen.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
			//activeFullscreen.BackColor = System.Drawing.Color.DarkOrange;
			activePID = new Label();
			activePID.Text = "n/a";
			activePID.Width = 60;
			activePID.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
			//activePID.BackColor = System.Drawing.Color.LightCyan;
			//gamepanel.Padding = new Padding(3);
			gamepanel.Controls.Add(activeLabelUX);
			gamepanel.Controls.Add(activeLabel);
			gamepanel.Controls.Add(activeExec);
			gamepanel.Controls.Add(activeFullscreen);
			gamepanel.Controls.Add(new Label { Text = "Id:", Width = 20, TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
			gamepanel.Controls.Add(activePID);
			lrows.Controls.Add(gamepanel);
			#endregion

			#region Load UI config
			var uicfg = TaskMaster.loadConfig("UI.ini");
			var colcfg = uicfg["Columns"];

			int[] appwidthsDefault = new int[] { 100, 90, 72, 40, 62, 54, 120, 40 };
			var appwidths = colcfg.GetSetDefault("Apps", appwidthsDefault).IntValueArray;
			if (appwidths.Length != appwidthsDefault.Length) appwidths = appwidthsDefault;

			int[] pathwidthsDefault = new int[] { 120, 300, 60, 80, 40, 80 };
			var pathwidths = colcfg.GetSetDefault("Paths", pathwidthsDefault).IntValueArray;
			if (pathwidths.Length != pathwidthsDefault.Length) pathwidths = pathwidthsDefault;

			int[] micwidthsDefault = new int[] { 200, 220 };
			var micwidths = colcfg.GetSetDefault("Mics", micwidthsDefault).IntValueArray;
			if (micwidths.Length != micwidthsDefault.Length) micwidths = micwidthsDefault;

			int[] ifacewidthsDefault = new int[] { 120, 60, 50, 70, 90, 200 };
			var ifacewidths = colcfg.GetSetDefault("Interfaces", ifacewidthsDefault).IntValueArray;
			if (ifacewidths.Length != ifacewidthsDefault.Length) ifacewidths = ifacewidthsDefault;
			#endregion

			#region Main Window Row 1, microphone device
			var micDevLbl = new Label
			{
				Text = "Default communications device:",
				Dock = DockStyle.Left,
				AutoSize = true
			};
			micName = new Label
			{
				Text = "N/A",
				Dock = DockStyle.Left,
				AutoSize = true
			};
			var micNameRow = new TableLayoutPanel
			{
				Dock = DockStyle.Top,
				RowCount = 1,
				ColumnCount = 2,
				//BackColor = System.Drawing.Color.BlanchedAlmond, // DEBUG
				AutoSize = true
			};
			micNameRow.Controls.Add(micDevLbl);
			micNameRow.Controls.Add(micName);
			lrows.Controls.Add(micNameRow);
			#endregion

			// uhh???
			// Main Window Row 2, volume control
			var miccntrl = new TableLayoutPanel
			{
				ColumnCount = 5,
				RowCount = 1,
				// BackColor = System.Drawing.Color.Azure, // DEBUG
				Dock = DockStyle.Top,
				AutoSize = true,
				//miccntrl.Location = new System.Drawing.Point(0, 0);
			};
			miccntrl.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			miccntrl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			lrows.Controls.Add(miccntrl);

			var micVolLabel = new Label
			{
				Text = "Mic volume",
				Dock = DockStyle.Left,
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft
			};

			var micVolLabel2 = new Label
			{
				Text = "%",
				Dock = DockStyle.Left,
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft
			};

			micVol = new NumericUpDown
			{
				Maximum = 100,
				Minimum = 0,
				Width = 60,
				ReadOnly = true,
				Enabled = false,
				Dock = DockStyle.Left
			};
			micVol.ValueChanged += UserMicVol;

			miccntrl.Controls.Add(micVolLabel);
			miccntrl.Controls.Add(micVol);
			miccntrl.Controls.Add(micVolLabel2);

			var corLbll = new Label
			{
				Text = "Correction count:",
				Dock = DockStyle.Fill,
				AutoSize = true,
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft
			};

			corCountLabel = new Label
			{
				Dock = DockStyle.Left,
				Text = "0",
				AutoSize = true,
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft
			};
			miccntrl.Controls.Add(corLbll);
			miccntrl.Controls.Add(corCountLabel);
			// End: Volume control

			// Main Window row 3, microphone device enumeration
			micList = new ListView
			{
				Dock = DockStyle.Top,
				Width = lrows.Width - 3, // FIXME: 3 for the bevel, but how to do this "right"?
				Height = 60,
				View = View.Details,
				FullRowSelect = true
			};
			micList.Columns.Add("Name", micwidths[0]);
			micList.Columns.Add("GUID", micwidths[1]);

			lrows.Controls.Add(micList);
			// End: Microphone enumeration

			// Main Window row 4-5, internet status
			var netLabel = new Label { Text = "Network status:", Dock = DockStyle.Left, AutoSize = true };
			var inetLabel = new Label { Text = "Internet status:", Dock = DockStyle.Left, AutoSize = true };
			var uptimeLabel = new Label { Text = "Uptime:", Dock = Dock = DockStyle.Left, AutoSize = true };

			var netlayout = new TableLayoutPanel { ColumnCount = 6, RowCount = 1, Dock = DockStyle.Top, AutoSize = true };
			netlayout.Controls.Add(netLabel);
			netlayout.Controls.Add(netstatuslabel);
			netlayout.Controls.Add(inetLabel);
			netlayout.Controls.Add(inetstatuslabel);
			netlayout.Controls.Add(uptimeLabel);
			netlayout.Controls.Add(uptimestatuslabel);

			lrows.Controls.Add(netlayout);

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

			if (TaskMaster.NetworkMonitorEnabled)
			{
				UItimer.Tick += UpdateUptime;
			}

			ifaceList = new ListView
			{
				Dock = DockStyle.Top,
				Width = lrows.Width - 3, // FIXME: why does 3 work? can't we do this automatically?
				Height = 60,
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

			ifaceList.Columns.Add("Device", ifacewidths[0]);
			ifaceList.Columns.Add("Type", ifacewidths[1]);
			ifaceList.Columns.Add("Status", ifacewidths[2]);
			ifaceList.Columns.Add("Link speed", ifacewidths[3]);
			ifaceList.Columns.Add("IPv4", ifacewidths[4]);
			ifaceList.Columns.Add("IPv6", ifacewidths[5]);
			ifaceList.Scrollable = true;
			lrows.Controls.Add(ifaceList);
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
			lrows.Controls.Add(settingList, 0, 6);
			*/
			// End: Settings

			// Main Window, Path list
			pathList = new ListView
			{
				View = View.Details,
				Dock = DockStyle.Top,
				Width = lrows.Width - 3,
				Height = 80,
				FullRowSelect = true
			};
			pathList.Columns.Add("Name", pathwidths[0]);
			pathList.Columns.Add("Path", pathwidths[1]);
			pathList.Columns.Add("Adjusts", pathwidths[2]);
			pathList.Columns.Add("Priority", pathwidths[3]);
			pathList.Columns.Add("Affinity", pathwidths[4]);
			pathList.Columns.Add("Power Plan", pathwidths[5]);
			pathList.Scrollable = true;

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

			lrows.Controls.Add(pathList);
			// End: Path list

			// Main Window row 7, app list
			appList = new ListView
			{
				View = View.Details,
				Dock = DockStyle.Top,
				Width = lrows.Width - 3, // FIXME: why does 3 work? can't we do this automatically?
				Height = 140, // FIXME: Should use remaining space
				FullRowSelect = true
			};
			appList.Columns.Add("Name", appwidths[0]);
			appList.Columns.Add("Executable", appwidths[1]);
			appList.Columns.Add("Priority", appwidths[2]);
			appList.Columns.Add("Affinity", appwidths[3]);
			//appList.Columns.Add("Boost", 40);
			appList.Columns.Add("Children", appwidths[4]);
			appList.Columns.Add("Adjusts", appwidths[5]);
			appList.Columns.Add("Last seen", appwidths[6]);
			appList.Columns.Add("Rescan", appwidths[7]);
			appList.Scrollable = true;
			appList.Alignment = ListViewAlignment.Left;

			appList.DoubleClick += appEditEvent; // for in-app editing
			lrows.Controls.Add(appList);
			// End: App list

			// UI Log
			loglist = new ListView
			{
				Dock = DockStyle.Fill,
				View = View.Details,
				FullRowSelect = true,
				HeaderStyle = ColumnHeaderStyle.None,
				Scrollable = true,
				Height = 200
			};
			loglist.Columns.Add("Log content");
			loglist.Columns[0].Width = lrows.Width - 25;
			lrows.Controls.Add(loglist);

			loglistms = new ContextMenuStrip();
			loglistms.Opened += LogContextMenuOpen;
			var logcopy = new ToolStripMenuItem("Copy to clipboard", null, CopyLogToClipboard);
			loglistms.Items.Add(logcopy);
			loglist.ContextMenuStrip = loglistms;


			var cfg = TaskMaster.loadConfig("Core.ini");
			bool tdirty;
			MaxLogSize = cfg.TryGet("Logging").GetSetDefault("Count", 80, out tdirty).IntValue;
			if (tdirty) TaskMaster.MarkDirtyINI(cfg);

			var logpanel = new TableLayoutPanel
			{
				Dock = DockStyle.Top,
				RowCount = 1,
				ColumnCount = 11,
				Height = 40,
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

			logpanel.Controls.Add(loglabel_level);
			logpanel.Controls.Add(logcombo_level);

			lrows.Controls.Add(logpanel);

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
				Margin = new Padding(3 + 3),
				Dock = DockStyle.Right,
			};

			rescanbutton = new Button { Text = "Rescan", Dock = DockStyle.Right, Margin = new Padding(3 + 3), FlatStyle = FlatStyle.Flat };
			rescanbutton.Click += async (object sender, EventArgs e) =>
			{
				rescanbutton.Enabled = false;
				rescanRequest?.Invoke(this, new EventArgs());
				await System.Threading.Tasks.Task.Delay(100).ConfigureAwait(false);
				rescanbutton.Enabled = true;
			};
			commandpanel.Controls.Add(new Label { Text = "Processing", Dock = DockStyle.Right, TextAlign = System.Drawing.ContentAlignment.MiddleRight, AutoSize = true });
			commandpanel.Controls.Add(processingCount);
			commandpanel.Controls.Add(rescanbutton);
			rescanbutton.Enabled = TaskMaster.ProcessMonitorEnabled;

			crunchbutton = new Button { Text = "Page", Dock = DockStyle.Right, FlatStyle = FlatStyle.Flat, Margin = new Padding(3 + 3), Enabled = TaskMaster.PagingEnabled };
			crunchbutton.Click += async (object sender, EventArgs e) =>
			{
				crunchbutton.Enabled = false;
				await System.Threading.Tasks.Task.Delay(100).ConfigureAwait(true);
				pagingRequest?.Invoke(this, new EventArgs());
				crunchbutton.Enabled = true;
			};

			commandpanel.Controls.Add(crunchbutton);

			tempObjectCount = new NumericUpDown()
			{
				Minimum = 0,
				Maximum = uint.MaxValue,
				Width = 64,
				ReadOnly = true,
				Enabled = false,
				Margin = new Padding(3 + 3),
				Dock = DockStyle.Right,
			};

			tempObjectSize = new NumericUpDown()
			{
				Minimum = 0,
				Maximum = uint.MaxValue,
				Width = 64,
				ReadOnly = true,
				Enabled = false,
				Margin = new Padding(3 + 3),
				Dock = DockStyle.Right,
			};

			commandpanel.Controls.Add(new Label { Text = "Temp", Dock = DockStyle.Right, TextAlign = System.Drawing.ContentAlignment.MiddleRight, AutoSize = true });
			commandpanel.Controls.Add(new Label { Text = "Objects", Dock = DockStyle.Right, TextAlign = System.Drawing.ContentAlignment.MiddleRight, AutoSize = true });
			commandpanel.Controls.Add(tempObjectCount);
			commandpanel.Controls.Add(new Label { Text = "Size (MB)", Dock = DockStyle.Right, TextAlign = System.Drawing.ContentAlignment.MiddleRight, AutoSize = true });
			commandpanel.Controls.Add(tempObjectSize);

			lrows.Controls.Add(commandpanel);

			// End: UI Log

			//layout.Visible = true;

			/*
			Label micVolLabel = new Label();
			micVolLabel.Parent = micPanel;
			micVolLabel.AutoSize = true;
			//micVolLabel.Location = Location.
			*/

			/*
			TrackBar tb = new TrackBar();
			tb.Parent = micPanel;
			tb.TickStyle = TickStyle.Both;
			//tb.Size = new Size(150, 25);
			//tb.Height 
			tb.Dock = DockStyle.Fill; // fills parent
									  //tb.Location = new Point(0, 0); // insert point
			*/

			DiskManager.onTempScan += TempScanStats;
		}

		int appEditLock = 0;
		void appEditEvent(object sender, EventArgs ev)
		{
			if (System.Threading.Interlocked.CompareExchange(ref appEditLock, 1, 0) == 1)
			{
				Log.Warning("Only one item can be edited at a time.");
				return;
			}
			//Log.Verbose("Opening edit window.");

			ListViewItem ri = appList.SelectedItems[0];
			var t = new AppEditWindow(ri.SubItems[1].Text, ri);
			t.FormClosed += (ns, evs) =>
			{
				appEditLock = 0;
				//Log.Verbose("Edit window closed.");
			};
		}

		NumericUpDown tempObjectCount;
		NumericUpDown tempObjectSize;

		public void TempScanStats(object sender, DiskEventArgs ev)
		{
			tempObjectSize.Value = ev.Stats.Size / 1000 / 1000;
			tempObjectCount.Value = ev.Stats.Dirs + ev.Stats.Files;
		}

		object loglistLock = new object();
		ListView loglist;
		public void FillLog()
		{
			lock (loglistLock)
			{
#if DEBUG
				Log.Verbose("Filling GUI log.");
#endif
				var logcopy = MemoryLog.Copy();
				foreach (LogEventArgs evmsg in logcopy)
				{
					loglist.Items.Add(evmsg.Message);
				}
			}

			ShowLastLog();
		}

		NetMonitor net;
		public void setNetMonitor(ref NetMonitor net)
		{
			if (net == null) return; // disabled

			Log.Verbose("Hooking network monitor.");

			this.net = net;

			foreach (NetDevice dev in net.Interfaces())
			{
				ifaceList.Items.Add(new ListViewItem(new string[] {
					dev.Name,
					dev.Type.ToString(),
					dev.Status.ToString(),
					HumanInterface.ByteRateString(dev.Speed),
					dev.IPv4Address.ToString() ?? "n/a",
					dev.IPv6Address.ToString() ?? "n/a"
				}));
			}

			net.InternetStatusChange += InetStatus;
			net.NetworkStatusChange += NetStatus;

			InetStatusLabel(net.InternetAvailable);
			NetStatusLabel(net.NetworkAvailable);

			//Tray?.Tooltip(2000, "Internet " + (net.InternetAvailable ? "available" : "unavailable"), "TaskMaster", net.InternetAvailable ? ToolTipIcon.Info : ToolTipIcon.Warning);
		}

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
		int MaxLogSize = 20;
		public void onNewLog(object sender, LogEventArgs e)
		{
			if (LogIncludeLevel.MinimumLevel > e.Level) return;

			lock (loglistLock)
			{
				int excessitems = (loglist.Items.Count - MaxLogSize).Min(0);
				while (excessitems-- > 0)
					loglist.Items.RemoveAt(0);
				loglist.Items.Add(e.Message).EnsureVisible();
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

			ProcessController.onTouch += ProcAdjust;

			this.Shown += (object sender, EventArgs e) => { ShowLastLog(); };

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

		void saveColumns()
		{
			if (appList.Columns.Count == 0) return;

			List<int> appWidths = new List<int>(appList.Columns.Count);
			for (int i = 0; i < appList.Columns.Count; i++)
				appWidths.Add(appList.Columns[i].Width);
			List<int> pathWidths = new List<int>(pathList.Columns.Count);
			for (int i = 0; i < pathList.Columns.Count; i++)
				pathWidths.Add(pathList.Columns[i].Width);
			List<int> ifaceWidths = new List<int>(ifaceList.Columns.Count);
			for (int i = 0; i < pathList.Columns.Count; i++)
				ifaceWidths.Add(ifaceList.Columns[i].Width);
			List<int> micWidths = new List<int>(micList.Columns.Count);
			for (int i = 0; i < micList.Columns.Count; i++)
				micWidths.Add(micList.Columns[i].Width);

			var cfg = TaskMaster.loadConfig("UI.ini");
			var cols = cfg["Columns"];
			cols["Apps"].IntValueArray = appWidths.ToArray();
			cols["Paths"].IntValueArray = pathWidths.ToArray();
			cols["Mics"].IntValueArray = micWidths.ToArray();
			cols["Interfaces"].IntValueArray = ifaceWidths.ToArray();
			TaskMaster.MarkDirtyINI(cfg);
		}

		bool disposed; // = false;
		protected override void Dispose(bool disposing)
		{
			if (disposed) return;

			base.Dispose(disposing);

			if (disposing)
			{
				Log.Verbose("Disposing...");

				DiskManager.onTempScan -= TempScanStats;
				ProcessController.onTouch -= ProcAdjust;
				PathControl.onLocate -= PathLocatedEvent;
				PathControl.onTouch -= PathAdjustEvent;
				ProcessManager.onInstanceHandling -= ProcessNewInstanceCount;

				UItimer.Stop();
				if (micmon != null)
				{
					micmon.VolumeChanged -= volumeChangeDetected;
					micmon = null;
				}
				if (net != null)
				{
					net.InternetStatusChange -= InetStatus;
					net.NetworkStatusChange -= NetStatus;
					net = null;
				}
				UItimer.Dispose();
			}

			disposed = true;
		}
	}
}

