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
using System.Windows.Controls;
using System.Net;
using SharpConfig;
using System.Linq;
using System.Windows;

namespace TaskMaster
{
	using System;
	using System.Collections.Generic;
	using System.Windows.Forms;
	using System.Net.NetworkInformation;

	// public class MainWindow : System.Windows.Window; // TODO: WPF
	public class MainWindow : System.Windows.Forms.Form
	{
		static NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

		public void ShowConfigRequest(object sender, System.EventArgs e)
		{
			Log.Warn("No config window available.");
		}

		void WindowClose(object sender, FormClosingEventArgs e)
		{
			switch (e.CloseReason)
			{
				case CloseReason.UserClosing:
					// X was pressed or similar, we're just hiding to tray.
					if (!lowmemory)
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
					goto Cleanup;
				case CloseReason.TaskManagerClosing:
					Log.Info("Exit: Task manager told us to close.");
					goto Cleanup;
				case CloseReason.ApplicationExitCall:
					Log.Info("Exit: User asked to close.");
					break;
				default:
					Log.Warn(System.String.Format("Exit: Unidentified close reason: {0}", e.CloseReason));
				Cleanup:
					break;
			}
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

		public void ShowWindowRequest(object sender, EventArgs e)
		{
			Show(); // FIXME: Gets triggered when menuitem is clicked
			AutoSize = true;
		}

		#region Microphone control code
		public void setMicMonitor(MicMonitor micmonitor)
		{
			Log.Trace("Hooking microphone monitor.");
			micName.Text = micmonitor.DeviceName;
			corCountLabel.Text = micmonitor.getCorrections().ToString();
			micmonitor.VolumeChanged += volumeChangeDetected;
			micVol.Value = System.Convert.ToInt32(micmonitor.Volume);

			foreach (KeyValuePair<string, string> dev in micmonitor.enumerate())
				micList.Items.Add(new ListViewItem(new string[] { dev.Value, dev.Key }));
			
			// TODO: Hook device changes
		}

		public void ProcAdjust(object sender, ProcessEventArgs e)
		{
			Log.Trace("Process adjust received.");
			ListViewItem item;
			if (appc.TryGetValue(e.Control.Executable, out item))
			{
				item.SubItems[5].Text = e.Control.Adjusts.ToString();
				item.SubItems[6].Text = e.Control.lastSeen.ToString();
			}
			else
				Log.Error(System.String.Format("{0} not found in app list.", e.Control.Executable));
		}

		public void OnActiveWindowChanged(object sender, WindowChangedArgs e)
		{
			activeLabel.Text = "Active window: " + e.title;
		}

		ProcessManager procCntrl;
		public void setProcControl(ProcessManager control)
		{
			procCntrl = control;
			//control.onProcAdjust += ProcAdjust;
			foreach (ProcessControl item in control.images)
			{

				ListViewItem litem = new ListViewItem(new string[] {
					item.FriendlyName.ToString(),
					item.Executable,
					item.Priority.ToString(),
						(item.Affinity.ToInt32() == ProcessManager.allCPUsMask ? "OS controlled" : Convert.ToString(item.Affinity.ToInt32(), 2)),
					item.Boost.ToString(),
					item.Adjusts.ToString(),
					    (item.lastSeen != System.DateTime.MinValue ? item.lastSeen.ToString() : "Never")
				});
				appc.Add(item.Executable, litem);
				appList.Items.Add(litem);
			}

			foreach (PathControl path in procCntrl.ActivePaths())
			{
				ListViewItem ni = new ListViewItem(new string[] { path.FriendlyName, path.Path, path.Adjusts.ToString() });
				appw.Add(path, ni);
				pathList.Items.Add(ni);
			}

			PathControl.onLocate += PathLocatedEvent;
			PathControl.onTouch += PathAdjustEvent;
		}

		public void PathAdjustEvent(object sender, PathControlEventArgs e)
		{
			ListViewItem ni;
			PathControl pc = (PathControl)sender;
			if (appw.TryGetValue(pc, out ni))
				ni.SubItems[2].Text = pc.Adjusts.ToString();
		}

		public void PathLocatedEvent(object sender, PathControlEventArgs e)
		{
			PathControl pc = (PathControl)sender;
			Log.Trace(pc.FriendlyName + " // " + pc.Path);
			ListViewItem ni = new ListViewItem(new string[] { pc.FriendlyName, pc.Path, "0" });
			try
			{
				appw.Add(pc, ni);
				pathList.Items.Add(ni);
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
			if(e.Corrected)
			{
				corCountLabel.Text = e.Corrections.ToString();
				//corCountLabel.Refresh();
			}
		}
		#endregion // Microphone control code

		#region Game Monitor
		Label activeLabel;
		#endregion

		#region Internet handling functionality

		bool netAvailable;
		bool inetAvailable;
		Label netstatuslabel;
		Label inetstatuslabel;
		void NetworkChanged(object sender, EventArgs e)
		{
			bool oldNetAvailable = netAvailable;
			netAvailable = NetworkInterface.GetIsNetworkAvailable();

			// do stuff only if this is different from last time
			if (oldNetAvailable != netAvailable)
			{
				Log.Debug("Network status changed: " + (netAvailable?"Connected":"Disconnected"));
				netstatuslabel.Text = netAvailable.ToString();
				netstatuslabel.BackColor = netAvailable ? System.Drawing.Color.LightGoldenrodYellow : System.Drawing.Color.Red;
				CheckInet();
			}
		}

		int uptimeSamples = 0;
		double uptimeTotal = 0;
		List<double> upTime = new List<double>();
		System.DateTime lastUptimeStart;

		void ReportCurrentUptime()
		{
			Log.Info(System.String.Format("Current internet uptime: {0:1} minutes", (lastUptimeStart - System.DateTime.Now).TotalMinutes));
		}

		void ReportUptime()
		{
			if (uptimeSamples > 3)
			{
				double uptimeLast3 = upTime.GetRange(upTime.Count - 3, 3).Sum();
				Log.Info(System.String.Format("Average uptime: {0:1} minutes ({1:1 minutes} for last 3 samples).", (uptimeTotal / uptimeSamples), (uptimeLast3 / 3)));
			}
			else
				Log.Info(System.String.Format("Average uptime: {0:1} minutes.", (uptimeTotal / uptimeSamples)));

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
					lastUptimeStart = System.DateTime.Now;

					if (System.Threading.Interlocked.CompareExchange(ref upstateTesting, 1, 0) == 1)
					{
						System.Threading.Tasks.Task.Run(async () =>
						{
							Console.WriteLine("Debug: Queued internet uptime report");
							await System.Threading.Tasks.Task.Delay(new TimeSpan(0, 5, 0)); // wait 5 minutes

							ReportCurrentUptime();
							upstateTesting = 0;
						});
					}
				}
				else // went offline
				{
					double newUptime = (System.DateTime.Now - lastUptimeStart).TotalMinutes;
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
				Console.WriteLine("Debug: Address changed but internet connectivity unaffected.");
				ReportCurrentUptime();
			}
		}

		bool CheckInet(bool address_changed=false)
		{
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
				inetstatuslabel.Text = inetAvailable.ToString();
				inetstatuslabel.BackColor = inetAvailable ? System.Drawing.Color.LightGoldenrodYellow : System.Drawing.Color.Red;

				if (Tray != null)
					Tray.Tooltip(2000, "Internet " + (inetAvailable ? "available" : "unavailable"), "TaskMaster", inetAvailable ? ToolTipIcon.Info : ToolTipIcon.Warning);

				Log.Info("Network status: " + (netAvailable ? "Up" : "Down") + ", Inet status: " + (inetAvailable ? "Connected" : "Disconnected"));
			}

			return inetAvailable;
		}

		TrayAccess Tray;
		public void setTray(TrayAccess tray)
		{
			Tray = tray;
		}

		string netSpeed(long speed)
		{
			if (speed >= 1000000000d)
				return Math.Round(speed / 1000000000d, 2) + " Gb/s";
			if (speed >= 1000000d)
				return Math.Round(speed / 1000000d, 2) + " Mb/s";
			if (speed >= 1000d)
				return Math.Round(speed / 1000d, 2) + " kb/s";
			return speed + " b/s";
		}

		System.Net.IPAddress IPv4Address;
		System.Net.IPAddress IPv6Address;

		ListView ifaceList;
		void NetEnum()
		{
			ifaceList.Items.Clear();
			NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
			foreach (NetworkInterface n in adapters)
			{
				if (n.NetworkInterfaceType == NetworkInterfaceType.Loopback || n.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
					continue;

				foreach (UnicastIPAddressInformation ip in n.GetIPProperties().UnicastAddresses)
				{
					if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork || ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
					{
						if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
							IPv4Address = ip.Address;
						else if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
							IPv6Address = ip.Address;
					}
				}
				ifaceList.Items.Add(new ListViewItem(new string[] {
					n.Name,
					n.NetworkInterfaceType.ToString(),
					n.OperationalStatus.ToString(),
					netSpeed(n.Speed),
					IPv4Address!=null?IPv4Address.ToString():"n/a",
					IPv6Address!=null?IPv6Address.ToString():"n/a"
				}));
			}
		}

		void NetAddrChanged(object sender, System.EventArgs e)
		{
			IPAddress oldV6Address = IPv6Address;
			IPAddress oldV4Address = IPv4Address;

			NetEnum();
			CheckInet(address_changed:true);

			if (inetAvailable)
			{
				Console.WriteLine("DEBUG: AddrChange: " + oldV4Address + " -> " + IPv4Address);
				Console.WriteLine("DEBUG: AddrChange: " + oldV6Address + " -> " + IPv6Address);
				bool ipv4changed = !oldV4Address.Equals(IPv4Address);
				if (ipv4changed)
				{
					System.Text.StringBuilder outstr4 = new System.Text.StringBuilder();
					outstr4.Append("IPv4 address changed: ");
					outstr4.Append(oldV4Address).Append(" -> ").Append(IPv4Address);
					Log.Debug(outstr4.ToString());
					Tray.Tooltip(2000, outstr4.ToString(), "TaskMaster", ToolTipIcon.Info);
				}
				bool ipv6changed = !oldV6Address.Equals(IPv6Address);
				if (ipv6changed)
				{
					System.Text.StringBuilder outstr6 = new System.Text.StringBuilder();
					outstr6.Append("IPv6 address changed: ");
					outstr6.Append(oldV6Address).Append(" -> ").Append(IPv6Address);
					Log.Debug(outstr6.ToString());
				}

				if (!ipv4changed && !ipv6changed)
					Log.Warn("Unstable internet connectivity detected.");
			}

			//NetworkChanged(null,null);
		}

		void NetworkSetup()
		{
			NetworkChanged(null, null);
			netAvailable = NetworkInterface.GetIsNetworkAvailable();
			netstatuslabel.Text = netAvailable.ToString();

			NetEnum();

			NetworkChange.NetworkAvailabilityChanged += NetworkChanged;
			NetworkChange.NetworkAddressChanged += NetAddrChanged;
		}
		#endregion

		/*
		ProcessControl editproc;
		void saveAppData(Object sender, EventArgs e)
		{
			Log.Debug("Save button pressed!");
		}
		*/

		//void appEditEvent(Object sender, System.Windows.Input.MouseButtonEventArgs e)
		/*
	void appEditEvent(Object sender, EventArgs e)
	{
		if (appList.SelectedItems.Count == 1)
		{
			//ListView.SelectedListViewItemCollection items = appList.SelectedItems;
			//ListViewItem itm = items[0];
			ListViewItem.ListViewSubItemCollection sit = appList.SelectedItems[0].SubItems;

			// TODO: Find the actuall ProcessControl for this item
			editproc = procCntrl.getControl(sit[1].Text);
			if (editproc == null)
			{
				Log.Warn(sit[1].Text + " not found in process manager!");
				return;
			}

			Log.Trace(System.String.Format("[{0}] {1}", editproc.FriendlyName, editproc.Executable));

			Form f = new Form();
			f.Padding = new Padding(12);
			TableLayoutPanel lay = new TableLayoutPanel();
			lay.Parent = f;
			lay.ColumnCount = 2;
			lay.RowCount = 6;
			lay.Dock = DockStyle.Fill;
			lay.AutoSize = true;

			Label e_appname = new Label();
			e_appname.Text = "Application";
			e_appname.TextAlign = ContentAlignment.MiddleLeft;
			e_appname.Width = 120;
			lay.Controls.Add(e_appname, 0, 0);
			TextBox i_appname = new TextBox();
			i_appname.Text = editproc.FriendlyName;
			lay.Controls.Add(i_appname, 1, 0);

			Label e_exename = new Label();
			e_exename.Text = "Executable";
			e_exename.TextAlign = ContentAlignment.MiddleLeft;
			e_exename.Width = 120;
			lay.Controls.Add(e_exename, 0, 1);
			TextBox i_exename = new TextBox();
			i_exename.Text = editproc.Executable;
			lay.Controls.Add(i_exename, 1, 1);

			Label e_priority = new Label();
			e_priority.Text = "Priority";
			e_priority.TextAlign = ContentAlignment.MiddleLeft;
			lay.Controls.Add(e_priority, 0, 2);
			ComboBox i_priority = new ComboBox();
			i_priority.Items.Add(ProcessPriorityClass.AboveNormal);
			i_priority.Items.Add(ProcessPriorityClass.Normal);
			i_priority.Items.Add(ProcessPriorityClass.BelowNormal);
			i_priority.Items.Add(ProcessPriorityClass.Idle);
			i_priority.SelectedText = editproc.Priority.ToString();
			lay.Controls.Add(i_priority, 1, 2);

			Label e_affinity = new Label();
			e_affinity.Text = "Affinity";
			e_affinity.TextAlign = ContentAlignment.MiddleLeft;
			lay.Controls.Add(e_affinity, 0, 3);


			Label e_boost = new Label();
			e_boost.Text = "Boost";
			e_boost.TextAlign = ContentAlignment.MiddleLeft;
			lay.Controls.Add(e_boost, 0, 4);
			CheckBox i_boost = new CheckBox();
			i_boost.Checked = editproc.Boost;
			//Log.Debug("Boost:"+sit[4]);
			lay.Controls.Add(i_boost, 1, 4);

			Button savebut = new Button();
			savebut.Text = "Save";
			lay.Controls.Add(savebut, 1, 5);

			savebut.Click += saveAppData;

			f.MinimumSize = new Size(200,120);
			f.ShowDialog(this);
		}
	}
		*/

		CheckBox logcheck_warn;
		bool log_include_warn = true;
		CheckBox logcheck_debug;
		bool log_include_debug = true;

		void SetWindowSize()
		{
			Size = new System.Drawing.Size(720, 720); // width, height
			AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowOnly;
			AutoSize = true;
		}

		void BuildUI()
		{
			SetWindowSize();

			Text = Application.ProductName;
			Padding = new Padding(12);
			//margin

			TableLayoutPanel lrows = new TableLayoutPanel();
			lrows.Parent = this;
			lrows.ColumnCount = 1;
			//lrows.RowCount = 10;
			lrows.Dock = DockStyle.Fill;

			#region Main Window Row 1, microphone device
			Label micDevLbl = new Label();
			micDevLbl.Text = "Default communications device:";
			micDevLbl.Dock = DockStyle.Left;
			micDevLbl.AutoSize = true;
			TableLayoutPanel micNameRow = new TableLayoutPanel();
			micNameRow.Dock = DockStyle.Top;
			micNameRow.RowCount = 1;
			micNameRow.ColumnCount = 2;
			micNameRow.BackColor = System.Drawing.Color.BlanchedAlmond; // DEBUG
			micNameRow.Controls.Add(micDevLbl);
			micName = new Label();
			micName.Dock = DockStyle.Left;
			micName.AutoSize = true;
			micNameRow.Controls.Add(micName);
			micNameRow.Dock = DockStyle.Fill;
			micNameRow.AutoSize = true;
			lrows.Controls.Add(micNameRow);
			#endregion

			// uhh???
			// Main Window Row 2, volume control
			TableLayoutPanel miccntrl = new TableLayoutPanel();
			miccntrl.ColumnCount = 5;
			miccntrl.RowCount = 1;
			miccntrl.BackColor = System.Drawing.Color.Azure; // DEBUG
			miccntrl.Dock = DockStyle.Top;
			miccntrl.AutoSize = true;
			//miccntrl.Location = new System.Drawing.Point(0, 0);
			miccntrl.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			miccntrl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			lrows.Controls.Add(miccntrl);

			Label micVolLabel = new Label();
			micVolLabel.Text = "Mic volume";
			micVolLabel.Dock = DockStyle.Left;
			micVolLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

			Label micVolLabel2 = new Label();
			micVolLabel2.Text = "%";
			micVolLabel2.Dock = DockStyle.Left;
			micVolLabel2.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

			micVol = new NumericUpDown();
			micVol.Maximum = 100;
			micVol.Minimum = 0;
			micVol.Width = 60;
			micVol.ReadOnly = true;
			micVol.Enabled = false;
			micVol.ValueChanged += UserMicVol;
			micVol.Dock = DockStyle.Left;

			miccntrl.Controls.Add(micVolLabel);
			miccntrl.Controls.Add(micVol);
			miccntrl.Controls.Add(micVolLabel2);

			Label corLbll = new Label();
			corLbll.Text = "Correction count:";
			corLbll.Dock = DockStyle.Fill;
			corLbll.AutoSize = true;
			corLbll.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			corCountLabel = new Label();
			corCountLabel.Dock = DockStyle.Left;
			corCountLabel.Text = "0";
			corCountLabel.AutoSize = true;
			corCountLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			miccntrl.Controls.Add(corLbll);
			miccntrl.Controls.Add(corCountLabel);
			// End: Volume control

			// Main Window row 3, microphone device enumeration
			micList = new ListView();
			micList.Dock = DockStyle.Top;
			micList.Width = lrows.Width - 3; // FIXME: 3 for the bevel, but how to do this "right"?
			micList.Height = 60;
			micList.View = View.Details;
			micList.FullRowSelect = true;
			micList.Columns.Add("Name", 200);
			micList.Columns.Add("GUID", 220);

			lrows.Controls.Add(micList);
			// End: Microphone enumeration

			// Main Window row 4-5, internet status
			Label netLabel = new Label();
			netLabel.Text = "Network status:";
			netLabel.Dock = DockStyle.Left;
			Label inetLabel = new Label();
			inetLabel.Text = "Internet status:";
			inetLabel.Dock = DockStyle.Left;

			netstatuslabel = new Label();
			netstatuslabel.Dock = DockStyle.Top;
			inetstatuslabel = new Label();
			inetstatuslabel.Dock = DockStyle.Top;

			netstatuslabel.Text = "Uninitialized";
			inetstatuslabel.Text = "Uninitialized";

			netstatuslabel.AutoSize = true;
			inetstatuslabel.AutoSize = true;
			netLabel.AutoSize = true;
			inetLabel.AutoSize = true;

			netstatuslabel.BackColor = System.Drawing.Color.LightGoldenrodYellow;
			inetstatuslabel.BackColor = System.Drawing.Color.LightGoldenrodYellow;

			TableLayoutPanel netlayout = new TableLayoutPanel();
			netlayout.ColumnCount = 4;
			netlayout.RowCount = 1;
			netlayout.Dock = DockStyle.Top;
			netlayout.AutoSize = true;
			netlayout.Controls.Add(netLabel);
			netlayout.Controls.Add(netstatuslabel);
			netlayout.Controls.Add(inetLabel);
			netlayout.Controls.Add(inetstatuslabel);

			lrows.Controls.Add(netlayout);

			/*
			activeLabel = new Label();
			activeLabel.Dock = DockStyle.Top;
			activeLabel.Text = "no active window found";
			activeLabel.AutoSize = true;
			activeLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			activeLabel.BackColor = Color.Aquamarine;
			lrows.Controls.Add(activeLabel, 0, 4);
			*/

			ifaceList = new ListView();
			ifaceList.Dock = DockStyle.Top;
			ifaceList.Width = lrows.Width - 3; // FIXME: why does 3 work? can't we do this automatically?
			ifaceList.Height = 60;
			ifaceList.View = View.Details;
			ifaceList.FullRowSelect = true;
			ifaceList.Columns.Add("Device",120);
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
			pathList = new ListView();
			pathList.View = View.Details;
			pathList.Dock = DockStyle.Top;
			pathList.Width = lrows.Width - 3;
			pathList.Height = 60;
			pathList.FullRowSelect = true;
			pathList.Columns.Add("Name", 120);
			pathList.Columns.Add("Path", 300);
			pathList.Columns.Add("Adjusts", 60);
			pathList.Scrollable = true;
			lrows.Controls.Add(pathList);
			// End: Path list

			// Main Window row 7, app list
			appList = new ListView();
			appList.View = View.Details;
			appList.Dock = DockStyle.Top;
			appList.Width = lrows.Width - 3; // FIXME: why does 3 work? can't we do this automatically?
			appList.Height = 140; // FIXME: Should use remaining space
			appList.FullRowSelect = true;
			appList.Columns.Add("Name", 100);
			appList.Columns.Add("Executable", 140);
			appList.Columns.Add("Priority", 80);
			appList.Columns.Add("Affinity", 80);
			appList.Columns.Add("Boost", 40);
			appList.Columns.Add("Adjusts", 60);
			appList.Columns.Add("Last seen", 120);
			appList.Scrollable = true;
			appList.Alignment = ListViewAlignment.Left;
			//appList.DoubleClick += appEditEvent; // for in-app editing, probably not going to actually do that
			lrows.Controls.Add(appList);
			// End: App list

			// UI Log
			loglist = new ListView();
			loglist.Dock = DockStyle.Fill;
			loglist.View = View.Details;
			loglist.FullRowSelect = true;
			loglist.Columns.Add("Log content");
			loglist.Columns[0].Width = lrows.Width - 25;
			loglist.HeaderStyle = ColumnHeaderStyle.None;
			loglist.Scrollable = true;
			loglist.Height = 200;
			lrows.Controls.Add(loglist);

			TableLayoutPanel logpanel = new TableLayoutPanel();
			logpanel.Dock = DockStyle.Top;
			logpanel.RowCount = 1;
			logpanel.ColumnCount = 4;
			logpanel.Height = 40;
			Label loglabel_warn = new Label();
			loglabel_warn.Text = "Warnings";
			loglabel_warn.Dock = DockStyle.Left;
			loglabel_warn.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			logcheck_warn = new CheckBox();
			logcheck_warn.Dock = DockStyle.Left;
			logcheck_warn.Checked = log_include_warn;
			logcheck_warn.CheckedChanged += (sender, e) => {
				log_include_warn = (logcheck_warn.CheckState == CheckState.Checked);
			};
			logpanel.Controls.Add(loglabel_warn);
			logpanel.Controls.Add(logcheck_warn);
			Label loglabel_debug = new Label();
			loglabel_debug.Text = "Debug";
			loglabel_debug.Dock = DockStyle.Left;
			loglabel_debug.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			logcheck_debug = new CheckBox();
			logcheck_debug.Dock = DockStyle.Left;
			logcheck_debug.Checked = log_include_debug;
			logcheck_debug.CheckedChanged += (sender, e) => {
				log_include_debug = (logcheck_debug.CheckState == CheckState.Checked);
			};
			logpanel.Controls.Add(loglabel_debug);
			logpanel.Controls.Add(logcheck_debug);
			logpanel.AutoSize = true;

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
			Log.Trace("Filling GUI log.");
			foreach (string msg in log.Logs.ToArray())
				loglist.Items.Add(msg);
		}

		// DO NOT LOG INSIDE THIS FOR FUCKS SAKE
		// it creates an infinite log loop
		int MaxLogSize = 20;
		public void onNewLog(object sender, LogEventArgs e)
		{
			try
			{
				int excessitems = loglist.Items.Count - MaxLogSize;
				while (excessitems-- > 0)
					loglist.Items.RemoveAt(0);
			}
			catch (System.NullReferenceException) // this shouldn't happen
			{
				Log.Warn("Couldn't remove old log entries from GUI."); // POSSIBLY REALLY BAD IDEA
				System.Console.WriteLine("ERROR: Null reference");
			}

			if ((!log_include_debug && e.Info.Level == NLog.LogLevel.Debug) || (!log_include_warn && e.Info.Level == NLog.LogLevel.Warn))
				return;

			loglist.Items.Add(e.Message).EnsureVisible();
		}

		static bool onetime = false;
		static bool lowmemory = false; // low memory mode; figure out way to auto-enable this
		public bool LowMemory { get { return lowmemory; } }

		// constructor
		public MainWindow()
		{
			//InitializeComponent(); // TODO: WPF
			lastUptimeStart = System.DateTime.Now;

			if (!onetime)
			{
				SharpConfig.Configuration cfg = TaskMaster.loadConfig("Core.ini");
				if (cfg.Contains("Options") && cfg["Options"].Contains("Low memory"))
				{
					lowmemory = cfg["Options"]["Low memory"].BoolValue;
					Log.Info("Low memory mode: " + (lowmemory ? "Enabled." : "Disabled."));
				}
				onetime = true;
			}

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

			ProcessControl.onTouch += ProcAdjust;

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

		/*
		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
		}
		*/

		//string cfgfile = "GUI.ini";
		//SharpConfig.Configuration guicfg;

		~MainWindow()
		{
			//TaskMaster.saveConfig(cfgfile, guicfg);

			//base.Dispose();
		}
	}
}

