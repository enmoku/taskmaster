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

namespace TaskMaster
{
	using System;
	using System.Linq;
	using System.Collections.Generic;
	using System.Windows.Forms;

	// public class MainWindow : System.Windows.Window; // TODO: WPF
	sealed public class MainWindow : System.Windows.Forms.Form, WindowInterface
	{
		static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

		public void ShowConfigRequest(object sender, EventArgs e)
		{
			Log.Warn("No config window available.");
		}

		public void ExitRequest(object sender, EventArgs e)
		{
			//CLEANUP: Console.WriteLine("START:Window.ExitRequest");
			// nothing
			//CLEANUP: Console.WriteLine("END:Window.ExitRequest");
		}

		void WindowClose(object sender, FormClosingEventArgs e)
		{
			//Console.WriteLine("WindowClose = " + e.CloseReason);
			switch (e.CloseReason)
			{
				case CloseReason.UserClosing:
					// X was pressed or similar, we're just hiding to tray.
					if (!TaskMaster.LowMemory)
					{
						Log.Trace("Hiding window, keeping in memory.");
						e.Cancel = true;
						Hide();
					}
					else
						Log.Trace("Closing window, freeing memory.");
					break;
				case CloseReason.WindowsShutDown:
					Log.Info("Exit: Windows shutting down.");
					break;
				case CloseReason.TaskManagerClosing:
					Log.Info("Exit: Task manager told us to close.");
					break;
				case CloseReason.ApplicationExitCall:
					Log.Info("Exit: User asked to close.");
					break;
				default:
					Log.Warn(string.Format("Exit: Unidentified close reason: {0}", e.CloseReason));
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

		static readonly System.Drawing.Size SetSizeDefault = new System.Drawing.Size(720, 760); // width, height

		public void ShowLastLog()
		{
			loglist.EnsureVisible(loglist.Items.Count - 1);
		}

		public void ShowWindowRequest(object sender, EventArgs e)
		{
			Show(); // FIXME: Gets triggered when menuitem is clicked
			AutoSize = true;
			Size = SetSizeDefault; // shouldn't be done always, but ensures the window is right size iff desktop was resized to something smaller.
		}

		#region Microphone control code
		public void setMicMonitor(MicMonitor micmonitor)
		{
			Log.Trace("Hooking microphone monitor.");
			micName.Text = micmonitor.DeviceName;
			corCountLabel.Text = micmonitor.Corrections.ToString();
			micmonitor.VolumeChanged += volumeChangeDetected;
			micVol.Maximum = Convert.ToDecimal(micmonitor.Maximum);
			micVol.Minimum = Convert.ToDecimal(micmonitor.Minimum);
			micVol.Value = Convert.ToInt32(micmonitor.Volume);

			micmonitor.enumerate().ForEach((dev) => micList.Items.Add(new ListViewItem(new string[] { dev.Value, dev.Key })));

			// TODO: Hook device changes
		}

		public void ProcAdjust(object sender, ProcessEventArgs e)
		{
			if (TaskMaster.VeryVerbose)
				Log.Debug("Process adjust received.");
			ListViewItem item;
			if (appc.TryGetValue(e.Control.Executable, out item))
			{
				item.SubItems[6].Text = e.Control.Adjusts.ToString();
				item.SubItems[7].Text = e.Control.LastSeen.ToString();
			}
			else
				Log.Error(string.Format("{0} not found in app list.", e.Control.Executable));
		}

		public void OnActiveWindowChanged(object sender, WindowChangedArgs e)
		{
			//activeLabel.Text = "Active window: " + e.title;
		}

		public event EventHandler rescanRequest;

		public void setProcControl(ProcessManager control)
		{
			//control.onProcAdjust += ProcAdjust;
			lock (appc)
			{
				foreach (ProcessController item in control.images)
				{
					var litem = new ListViewItem(new string[] {
					item.FriendlyName, //.ToString(),
					item.Executable,
					item.Priority.ToString(),
						(item.Affinity.ToInt32() == ProcessManager.allCPUsMask ? "OS" : Convert.ToString(item.Affinity.ToInt32(), 2)),
					item.Boost.ToString(),
						(item.Children ? item.ChildPriority.ToString() : "n/a"),
					item.Adjusts.ToString(),
						(item.LastSeen != DateTime.MinValue ? item.LastSeen.ToString() : "Never"),
						(item.Rescan>0?item.Rescan.ToString():"n/a")
					});
					appc.Add(item.Executable, litem);
				}
			}

			lock (appList) appList.Items.AddRange(appc.Values.ToArray());

			lock (appw)
			{
				control.ActivePaths().ForEach(
					(PathControl path) =>
					appw.Add(path, new ListViewItem(new string[] { path.FriendlyName, path.Path, path.Adjusts.ToString() }))
				);

				pathList.Items.AddRange(appw.Values.ToArray());
			}

			PathControl.onLocate += PathLocatedEvent;
			PathControl.onTouch += PathAdjustEvent;
		}

		public void PathAdjustEvent(object sender, PathControlEventArgs e)
		{
			ListViewItem ni;
			var pc = sender as PathControl;
			lock (appw)
			{
				if (appw.TryGetValue(pc, out ni))
					ni.SubItems[2].Text = pc.Adjusts.ToString();
			}
		}

		public void PathLocatedEvent(object sender, PathControlEventArgs e)
		{
			var pc = sender as PathControl;
			Log.Trace(pc.FriendlyName + " // " + pc.Path);
			var ni = new ListViewItem(new string[] { pc.FriendlyName, pc.Path, "0" });
			try
			{
				lock (appw) appw.Add(pc, ni);
				lock (pathList) pathList.Items.Add(ni);
			}
			catch (Exception)
			{
				// FIXME: This happens mostly because Application.Run() is triggered after we do ProcessEverything() and the events are processed only after
				Log.Warn("[Expected] Superfluous path watch update: " + pc.FriendlyName);
			}
		}

		Label micName;
		NumericUpDown micVol;
		ListView micList;
		ListView appList;
		ListView pathList;
		Dictionary<PathControl, ListViewItem> appw = new Dictionary<PathControl, ListViewItem>();
		Dictionary<string, ListViewItem> appc = new Dictionary<string, ListViewItem>();
		Label corCountLabel;

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
		//Label activeLabel;
		#endregion

		Label netstatuslabel = new Label { Dock = DockStyle.Top, Text = "Uninitialized", AutoSize = true, BackColor = System.Drawing.Color.LightGoldenrodYellow };
		Label inetstatuslabel = new Label { Dock = DockStyle.Top, Text = "Uninitialized", AutoSize = true, BackColor = System.Drawing.Color.LightGoldenrodYellow };
		Label uptimestatuslabel = new Label { Dock = DockStyle.Top, Text = "Uninitialized", AutoSize = true, BackColor = System.Drawing.Color.LightGoldenrodYellow };

		TrayAccess tray;
		public TrayAccess Tray { private get { return tray; } set { tray = value; } }

		CheckBox logcheck_warn;
		bool log_include_warn = true;
		CheckBox logcheck_debug;
		bool log_include_debug = true;
		CheckBox logcheck_trace;
		bool log_include_trace = false;

		void ChangeUIVisibility(object sender, EventArgs e)
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
		}

		readonly Timer timer = new Timer { Interval = 5000 };
		void StartUIUpdates(object sender, EventArgs e)
		{
			if (!timer.Enabled) timer.Start();
		}

		void StopUIUpdates(object sender, EventArgs e)
		{
			if (timer.Enabled) timer.Stop();
		}

		void UpdateUptime(object sender, EventArgs e)
		{
			uptimestatuslabel.Text = HumanInterface.TimeString(net.Uptime);
			//string.Format("{0:N1} min(s)", net.Uptime.TotalMinutes);
		}

		ListView ifaceList;
		Button rescanbutton;

		void BuildUI()
		{
			Size = SetSizeDefault;
			AutoSizeMode = AutoSizeMode.GrowOnly;
			AutoSize = true;

			Text = Application.ProductName;
			Padding = new Padding(12);
			//margin

			var lrows = new TableLayoutPanel
			{
				Parent = this,
				ColumnCount = 1,
				//lrows.RowCount = 10;
				Dock = DockStyle.Fill
			};
			#region Main Window Row 1, microphone device
			var micDevLbl = new Label
			{
				Text = "Default communications device:",
				Dock = DockStyle.Left,
				AutoSize = true
			};
			micName = new Label
			{
				Dock = DockStyle.Left,
				AutoSize = true
			};
			var micNameRow = new TableLayoutPanel
			{
				Dock = DockStyle.Top,
				RowCount = 1,
				ColumnCount = 2,
				BackColor = System.Drawing.Color.BlanchedAlmond, // DEBUG
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
				BackColor = System.Drawing.Color.Azure, // DEBUG
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
			micList.Columns.Add("Name", 200);
			micList.Columns.Add("GUID", 220);

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
			VisibleChanged += ChangeUIVisibility;

			if (TaskMaster.NetworkMonitorEnabled)
				timer.Tick += UpdateUptime;

			/*
			activeLabel = new Label();
			activeLabel.Dock = DockStyle.Top;
			activeLabel.Text = "no active window found";
			activeLabel.AutoSize = true;
			activeLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			activeLabel.BackColor = Color.Aquamarine;
			lrows.Controls.Add(activeLabel, 0, 4);
			*/

			ifaceList = new ListView
			{
				Dock = DockStyle.Top,
				Width = lrows.Width - 3, // FIXME: why does 3 work? can't we do this automatically?
				Height = 60,
				View = View.Details,
				FullRowSelect = true
			};
			ifaceList.Columns.Add("Device", 120);
			ifaceList.Columns.Add("Type", 60);
			ifaceList.Columns.Add("Status", 50);
			ifaceList.Columns.Add("Link speed", 70);
			ifaceList.Columns.Add("IPv4", 90);
			ifaceList.Columns.Add("IPv6", 200);
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
			pathList.Columns.Add("Name", 120);
			pathList.Columns.Add("Path", 300);
			pathList.Columns.Add("Adjusts", 60);
			pathList.Scrollable = true;
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
			appList.Columns.Add("Name", 100);
			appList.Columns.Add("Executable", 90);
			appList.Columns.Add("Priority", 72);
			appList.Columns.Add("Affinity", 40);
			appList.Columns.Add("Boost", 40);
			appList.Columns.Add("Children", 72);
			appList.Columns.Add("Adjusts", 60);
			appList.Columns.Add("Last seen", 120);
			appList.Columns.Add("Rescan", 40);
			appList.Scrollable = true;
			appList.Alignment = ListViewAlignment.Left;
			//appList.DoubleClick += appEditEvent; // for in-app editing, probably not going to actually do that
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

			var logpanel = new TableLayoutPanel
			{
				Dock = DockStyle.Top,
				RowCount = 1,
				ColumnCount = 7,
				Height = 40,
				AutoSize = true
			};
			var loglabel_warn = new Label
			{
				Text = "Warnings",
				Dock = DockStyle.Left,
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
				Width = 60
			};
			logcheck_warn = new CheckBox { Dock = DockStyle.Left, Checked = log_include_warn };
			logcheck_warn.CheckedChanged += (sender, e) =>
			{
				log_include_warn = (logcheck_warn.CheckState == CheckState.Checked);
			};
			logpanel.Controls.Add(loglabel_warn);
			logpanel.Controls.Add(logcheck_warn);
			var loglabel_debug = new Label { Text = "Debug", Dock = DockStyle.Left, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Width = 60 };
			logcheck_debug = new CheckBox { Dock = DockStyle.Left, Checked = log_include_debug };
			logcheck_debug.CheckedChanged += (sender, e) =>
			{
				log_include_debug = (logcheck_debug.CheckState == CheckState.Checked);
			};
			logpanel.Controls.Add(loglabel_debug);
			logpanel.Controls.Add(logcheck_debug);
			var loglabel_trace = new Label { Text = "Trace", Dock = DockStyle.Left, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Width = 60 };
			logcheck_trace = new CheckBox { Dock = DockStyle.Left, Checked = false };
			logcheck_trace.Enabled = false;
			logcheck_trace.CheckedChanged += (sender, e) =>
			{
				log_include_trace = (logcheck_trace.CheckState == CheckState.Checked);
			};
			logpanel.Controls.Add(loglabel_trace);
			logpanel.Controls.Add(logcheck_trace);

			rescanbutton = new Button { Text = "Rescan", Dock = DockStyle.Right, FlatStyle=FlatStyle.Flat };
			rescanbutton.Click += async (object sender, EventArgs e) =>
			{
				rescanbutton.Enabled = false;
				rescanRequest?.Invoke(this, new EventArgs());
				rescanbutton.Enabled = true;
			};
			logpanel.Controls.Add(rescanbutton);

			lrows.Controls.Add(logpanel);



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
		}

		ListView loglist;
		public void setLog(MemLog log)
		{
			if (TaskMaster.VeryVerbose)
				Log.Debug("Filling GUI log.");

			foreach (string msg in log.Logs.ToArray())
				loglist.Items.Add(msg);

			ShowLastLog();
		}

		NetMonitor net;
		public void setNet(ref NetMonitor net)
		{
			this.net = net;

			foreach (var items in net.Interfaces()) ifaceList.Items.Add(new ListViewItem(items));

			net.InternetStatusChange += InetStatus;
			net.NetworkStatusChange += NetStatus;

			InetStatusLabel(net.InternetAvailable);
			NetStatusLabel(net.NetworkAvailable);

			//Tray?.Tooltip(2000, "Internet " + (net.InternetAvailable ? "available" : "unavailable"), "TaskMaster", net.InternetAvailable ? ToolTipIcon.Info : ToolTipIcon.Warning);
		}

		void InetStatusLabel(bool available)
		{
			inetstatuslabel.Text = available ? "Connected" : "Disconnected";
			inetstatuslabel.BackColor = available ? System.Drawing.Color.LightGoldenrodYellow : System.Drawing.Color.Red;
		}

		public void InetStatus(object sender, InternetStatus e)
		{
			InetStatusLabel(e.Available);
		}

		void NetStatusLabel(bool available)
		{
			netstatuslabel.Text = available ? "Up" : "Down";
			netstatuslabel.BackColor = available ? System.Drawing.Color.LightGoldenrodYellow : System.Drawing.Color.Red;
		}

		public void NetStatus(object sender, NetworkStatus e)
		{
			NetStatusLabel(e.Available);
		}

		// DO NOT LOG INSIDE THIS FOR FUCKS SAKE
		// it creates an infinite log loop
		int MaxLogSize = 20;
		public void onNewLog(object sender, LogEventArgs e)
		{
			int excessitems = (loglist.Items.Count - MaxLogSize).Min(0);

			while (excessitems-- > 0)
				loglist.Items.RemoveAt(0);

			if ((!log_include_debug && e.Info.Level == NLog.LogLevel.Debug) || (!log_include_warn && e.Info.Level == NLog.LogLevel.Warn))
				return;

			loglist.Items.Add(e.Message).EnsureVisible();
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

		bool disposed; // = false;
		protected override void Dispose(bool disposing)
		{
			if (disposed) return;

			base.Dispose(disposing);

			if (disposing)
			{
				timer.Dispose();
			}

			disposed = true;
		}
	}
}

