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
	using System.Net;
	using System.Linq;
	using System.Collections.Generic;
	using System.Windows.Forms;
	using System.Net.NetworkInformation;

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

		public void ShowWindowRequest(object sender, EventArgs e)
		{
			Show(); // FIXME: Gets triggered when menuitem is clicked
			AutoSize = true;
			Size = SetSizeDefault;
		}

		#region Microphone control code
		public void setMicMonitor(MicMonitor micmonitor)
		{
			Log.Trace("Hooking microphone monitor.");
			micName.Text = micmonitor.DeviceName;
			corCountLabel.Text = micmonitor.Corrections.ToString();
			micmonitor.VolumeChanged += volumeChangeDetected;
			micVol.Value = System.Convert.ToInt32(micmonitor.Volume);

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

		public void setProcControl(ProcessManager control)
		{
			//control.onProcAdjust += ProcAdjust;
			lock (appc)
			{
				foreach (ProcessController item in control.images)
				{
					var litem = new ListViewItem(new string[] {
					item.FriendlyName.ToString(),
					item.Executable,
					item.Priority.ToString(),
						(item.Affinity.ToInt32() == ProcessManager.allCPUsMask ? "OS" : Convert.ToString(item.Affinity.ToInt32(), 2)),
					item.Boost.ToString(),
						(item.Children ? item.ChildPriority.ToString() : "n/a"),
					item.Adjusts.ToString(),
						(item.LastSeen != System.DateTime.MinValue ? item.LastSeen.ToString() : "Never"),
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

		void UserMicVol(object sender, System.EventArgs e)
		{
			// TODO: Handle volume changes. Not really needed. Give presets?
			//micMonitor.setVolume(micVol.Value);
		}

		void volumeChangeDetected(object sender, VolumeChangedEventArgs e)
		{
			micVol.Value = System.Convert.ToInt32(e.New);
			corCountLabel.Text = e.Corrections.ToString();
		}
		#endregion // Microphone control code

		#region Game Monitor
		//Label activeLabel;
		#endregion

		#region Internet handling functionality

		bool netAvailable = false;
		bool inetAvailable = false;
		Label netstatuslabel = new Label { Dock = DockStyle.Top, Text = "Uninitialized", AutoSize = true, BackColor = System.Drawing.Color.LightGoldenrodYellow };
		Label inetstatuslabel = new Label { Dock = DockStyle.Top, Text = "Uninitialized", AutoSize = true, BackColor = System.Drawing.Color.LightGoldenrodYellow };
		Label uptimestatuslabel = new Label { Dock = DockStyle.Top, Text = "Uninitialized", AutoSize = true, BackColor = System.Drawing.Color.LightGoldenrodYellow };

		async void NetworkChanged(object sender, EventArgs e)
		{
			bool oldNetAvailable = netAvailable;
			netAvailable = NetworkInterface.GetIsNetworkAvailable();

			// do stuff only if this is different from last time
			if (oldNetAvailable != netAvailable)
			{
				Log.Debug("Network status changed: " + (netAvailable ? "Connected" : "Disconnected"));
				netstatuslabel.Text = netAvailable.ToString();
				netstatuslabel.BackColor = netAvailable ? System.Drawing.Color.LightGoldenrodYellow : System.Drawing.Color.Red;

				await System.Threading.Tasks.Task.Delay(200);

				await CheckInet();
			}
		}

		int uptimeSamples = 0;
		double uptimeTotal = 0;
		List<double> upTime = new List<double>();
		DateTime lastUptimeStart;

		/// <summary>
		/// Current uptime in minutes.
		/// </summary>
		/// <value>The uptime.</value>
		double Uptime
		{
			get
			{
				if (TaskMaster.NetworkMonitorEnabled)
					return (DateTime.Now - lastUptimeStart).TotalMinutes;
				return double.NaN;
			}
		}

		void ReportCurrentUptime()
		{
			Log.Info(string.Format("Current internet uptime: {0:1} minutes", Uptime));
		}

		void ReportUptime()
		{
			var ups = new System.Text.StringBuilder();

			ups.Append("Average uptime: ").Append(string.Format("{0:N1}", (uptimeTotal / uptimeSamples))).Append(" minutes");

			if (uptimeSamples > 3)
				ups.Append(" (").Append(string.Format("{0:N1}", upTime.GetRange(upTime.Count - 3, 3).Sum() / 3)).Append(" minutes for last 3 samples");
			
			ups.Append(".");

			Log.Info(ups.ToString());

			ReportCurrentUptime();
		}

		bool lastOnlineState = false;
		static int upstateTesting = 0;
		void RecordSample(bool online_state, bool address_changed)
		{
			if (online_state != lastOnlineState)
			{
				lastOnlineState = online_state;

				if (online_state)
				{
					lastUptimeStart = DateTime.Now;

					if (System.Threading.Interlocked.CompareExchange(ref upstateTesting, 1, 0) == 1)
					{
						System.Threading.Tasks.Task.Run(async () =>
						{
							//CLEANUP: Console.WriteLine("Debug: Queued internet uptime report");
							await System.Threading.Tasks.Task.Delay(new TimeSpan(0, 5, 0)); // wait 5 minutes

							ReportCurrentUptime();
							upstateTesting = 0;
						});
					}
				}
				else // went offline
				{
					double newUptime = (DateTime.Now - lastUptimeStart).TotalMinutes;
					upTime.Add(newUptime);
					uptimeTotal += newUptime;
					uptimeSamples += 1;
					if (uptimeSamples > 20)
					{
						uptimeTotal -= upTime[0];
						uptimeSamples -= 1;
						upTime.RemoveAt(0);
					}

					ReportUptime();
				}
			}
			else if (address_changed)
			{
				// same state but address change was detected
				Console.WriteLine("DEBUG: Address changed but internet connectivity unaffected.");
				ReportCurrentUptime();
			}
		}

		int checking_inet = 0;
		async System.Threading.Tasks.Task CheckInet(bool address_changed = false)
		{
			// TODO: Figure out how to get Actual start time of internet connectivity.

			if (System.Threading.Interlocked.CompareExchange(ref checking_inet, 1, 0) == 0)
				return;

			if (TaskMaster.Verbose)
				Log.Trace("Checking internet connectivity...");

			await System.Threading.Tasks.Task.Delay(100);

			bool oldInetAvailable = inetAvailable;
			if (netAvailable)
			{
				try
				{
					System.Net.Dns.GetHostEntry("www.google.com"); // FIXME: don't rely on Google existing
					inetAvailable = true;
				}
				catch (System.Net.Sockets.SocketException)
				{
					inetAvailable = false;
				}
			}
			else
				inetAvailable = false;

			RecordSample(inetAvailable, address_changed);

			if (oldInetAvailable != inetAvailable)
			{
				inetstatuslabel.Text = inetAvailable ? "Connected" : "Disconnected";
				inetstatuslabel.BackColor = inetAvailable ? System.Drawing.Color.LightGoldenrodYellow : System.Drawing.Color.Red;

				if (Tray != null)
					Tray.Tooltip(2000, "Internet " + (inetAvailable ? "available" : "unavailable"), "TaskMaster", inetAvailable ? ToolTipIcon.Info : ToolTipIcon.Warning);

				Log.Info("Network status: " + (netAvailable ? "Up" : "Down") + ", Inet status: " + (inetAvailable ? "Connected" : "Disconnected"));
			}

			checking_inet = 0;
		}

		TrayAccess tray;
		public TrayAccess Tray { private get { return tray; } set { tray = value; } }

		IPAddress IPv4Address = IPAddress.None;
		IPAddress IPv6Address = IPAddress.IPv6None;
		ListView ifaceList;
		int enumerating_inet = 0;
		void NetEnum()
		{
			if (System.Threading.Interlocked.CompareExchange(ref enumerating_inet, 1, 0) == 1)
				return; // bail if we were already doing this

			if (TaskMaster.Verbose)
				Log.Trace("Enumerating network interfaces...");

			ifaceList.Items.Clear();
			NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
			foreach (NetworkInterface n in adapters)
			{
				if (n.NetworkInterfaceType == NetworkInterfaceType.Loopback || n.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
					continue;

				foreach (UnicastIPAddressInformation ip in n.GetIPProperties().UnicastAddresses)
				{
					// TODO: Maybe figure out better way and early bailout from the foreach
					switch (ip.Address.AddressFamily)
					{
						case System.Net.Sockets.AddressFamily.InterNetwork:
							IPv4Address = ip.Address;
							break;
						case System.Net.Sockets.AddressFamily.InterNetworkV6:
							IPv6Address = ip.Address;
							break;
					}
				}

				ifaceList.Items.Add(new ListViewItem(new string[] {
					n.Name,
					n.NetworkInterfaceType.ToString(),
					n.OperationalStatus.ToString(),
					Utility.ByterateString(n.Speed),
					IPv4Address?.ToString() ?? "n/a",
					IPv6Address?.ToString() ?? "n/a"
				}));
			}

			enumerating_inet = 0;
		}

		void NetAddrChanged(object sender, EventArgs e)
		{
			IPAddress oldV6Address = IPv6Address;
			IPAddress oldV4Address = IPv4Address;

			NetEnum();
			CheckInet(address_changed: true).Wait();

			if (inetAvailable)
			{
				//CLEANUP: Console.WriteLine("DEBUG: AddrChange: " + oldV4Address + " -> " + IPv4Address);
				//CLEANUP: Console.WriteLine("DEBUG: AddrChange: " + oldV6Address + " -> " + IPv6Address);

				bool ipv4changed = false, ipv6changed = false;
				ipv4changed = !oldV4Address.Equals(IPv4Address);
#if DEBUG
				if (ipv4changed)
				{
					var outstr4 = new System.Text.StringBuilder();
					outstr4.Append("IPv4 address changed: ");
					outstr4.Append(oldV4Address).Append(" -> ").Append(IPv4Address);
					Log.Debug(outstr4.ToString());
					Tray.Tooltip(2000, outstr4.ToString(), "TaskMaster", ToolTipIcon.Info);
				}
#endif
				ipv6changed = !oldV6Address.Equals(IPv6Address);
#if DEBUG
				if (ipv6changed)
				{
					var outstr6 = new System.Text.StringBuilder();
					outstr6.Append("IPv6 address changed: ");
					outstr6.Append(oldV6Address).Append(" -> ").Append(IPv6Address);
					Log.Debug(outstr6.ToString());
				}
#endif

				if (!ipv4changed && !ipv6changed)
					Log.Warn("Unstable internet connectivity detected.");
			}

			//NetworkChanged(null,null);
		}

		void NetworkSetup()
		{
			NetworkChanged(null, null);
			netAvailable = NetworkInterface.GetIsNetworkAvailable();
			netstatuslabel.Text = netAvailable ? "Up" : "Down";

			NetEnum();

			NetworkChange.NetworkAvailabilityChanged += NetworkChanged;
			NetworkChange.NetworkAddressChanged += NetAddrChanged;

			CheckInet().Wait();
		}
		#endregion

		CheckBox logcheck_warn;
		bool log_include_warn = true;
		CheckBox logcheck_debug;
		bool log_include_debug = true;

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

		void UpdateUptime(object sender, EventArgs e)
		{
			uptimestatuslabel.Text = string.Format("{0:N1} min(s)", Uptime);
		}

		Timer timer = new Timer { Interval = 5000 };
		void StartUIUpdates(object sender, EventArgs e)
		{
			if (!timer.Enabled) timer.Start();
		}

		void StopUIUpdates(object sender, EventArgs e)
		{
			if (timer.Enabled) timer.Stop();
		}

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
				ColumnCount = 4,
				Height = 40,
				AutoSize = true
			};
			var loglabel_warn = new Label
			{
				Text = "Warnings",
				Dock = DockStyle.Left,
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft
			};
			logcheck_warn = new CheckBox { Dock = DockStyle.Left, Checked = log_include_warn };
			logcheck_warn.CheckedChanged += (sender, e) => {
				log_include_warn = (logcheck_warn.CheckState == CheckState.Checked);
			};
			logpanel.Controls.Add(loglabel_warn);
			logpanel.Controls.Add(logcheck_warn);
			var loglabel_debug = new Label { Text = "Debug", Dock = DockStyle.Left, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
			logcheck_debug = new CheckBox { Dock = DockStyle.Left, Checked = log_include_debug };
			logcheck_debug.CheckedChanged += (sender, e) => {
				log_include_debug = (logcheck_debug.CheckState == CheckState.Checked);
			};
			logpanel.Controls.Add(loglabel_debug);
			logpanel.Controls.Add(logcheck_debug);

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
			lastUptimeStart = System.DateTime.Now;

			FormClosing += WindowClose;

			//MakeTrayIcon();

			BuildUI();

			NetworkSetup();

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

		bool disposed = false;
		protected override void Dispose(bool disposing)
		{
			if (disposed) return;

			base.Dispose(disposing);

			if (disposing)
			{
				timer.Dispose();
				ReportUptime();
			}

			disposed = true;
		}
	}
}

