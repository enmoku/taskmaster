//
// InitialConfigurationWindow.cs
//
// Author:
//       M.A. (enmoku) <>
//
// Copyright (c) 2018 M.A. (enmoku)
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

using System.Windows.Forms;

namespace TaskMaster
{
	public class InitialConfigurationWindow : Form
	{
		public InitialConfigurationWindow()
		{
			//Size = new System.Drawing.Size(220, 360); // width, height
			Text = "TaskMaster component configuration";

			FormBorderStyle = FormBorderStyle.FixedDialog;

			WindowState = FormWindowState.Normal;
			FormBorderStyle = FormBorderStyle.FixedDialog; // no min/max buttons as wanted
			MinimizeBox = false;
			MaximizeBox = false;
			AutoSizeMode = AutoSizeMode.GrowAndShrink;

			var baselayout = new TableLayoutPanel()
			{
				Parent = this,
				ColumnCount = 1,
				AutoSize = true,
				//Dock = DockStyle.Fill,
				//Dock = DockStyle.Top,
				//BackColor = System.Drawing.Color.Aqua,
			};

			var layout = new TableLayoutPanel()
			{
				Parent = baselayout,
				Dock = DockStyle.Left,
				ColumnCount = 2,
				AutoSize = true,
				//BackColor = System.Drawing.Color.YellowGreen,
			};

			var tooltip = new ToolTip();
			var padding = new Padding(6);

			Padding = padding;

			var micmon = new CheckBox()
			{
				AutoSize = true,
				//BackColor = System.Drawing.Color.Azure,
				Dock = DockStyle.Left
			};
			tooltip.SetToolTip(micmon, "Monitor default communications device and keep its volume.");
			layout.Controls.Add(new Label { Text = "Microphone monitor", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = padding, Dock = DockStyle.Left });
			layout.Controls.Add(micmon);
			micmon.Click += (sender, e) =>
			{

			};

			var netmon = new CheckBox()
			{
				AutoSize = true,
				//BackColor = System.Drawing.Color.Azure,
				Dock = DockStyle.Left
			};
			tooltip.SetToolTip(netmon, "Monitor network interface status and report online status.");
			layout.Controls.Add(new Label { Text = "Network monitor", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = padding, Dock = DockStyle.Left });
			layout.Controls.Add(netmon);
			netmon.Checked = true;
			netmon.Click += (sender, e) =>
			{

			};

			var procmon = new CheckBox()
			{
				AutoSize = true,
				//BackColor = System.Drawing.Color.Azure,
				Dock = DockStyle.Left
			};
			tooltip.SetToolTip(procmon, "Manage processes based on their name. Default feature of Taskmaster and thus can not be disabled.");
			layout.Controls.Add(new Label { Text = "Process/name manager", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = padding, Dock = DockStyle.Left });
			layout.Controls.Add(procmon);
			procmon.Enabled = false;
			procmon.Checked = true;

			var pathmon = new CheckBox()
			{
				AutoSize = true,
				//BackColor = System.Drawing.Color.Azure,
				Dock = DockStyle.Left
			};
			tooltip.SetToolTip(pathmon, "Manage processes based on their location.\nThese are processed only if by name matching does not catch something.\nPath-based processing is lenghtier process but should not cause significant resource drain.");
			layout.Controls.Add(new Label { Text = "Process/path manager", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = padding, Dock = DockStyle.Left });
			layout.Controls.Add(pathmon);
			pathmon.Checked = true;
			pathmon.Click += (sender, e) =>
			{
			};

			layout.Controls.Add(new Label() { Text = "Process detection", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = padding, Dock = DockStyle.Left });
			var ScanOrWMI = new ComboBox()
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				Items = { "Scanning", "WMI polling", "Both" },
				SelectedIndex = 0,
				Width = 80,
			};
			layout.Controls.Add(ScanOrWMI);
			tooltip.SetToolTip(ScanOrWMI, "Scanning involves getting all procesess and going through the list, which can cause tiny CPU spiking.\nWMI polling sets up system WMI event listener.\nWMI is known to be slow and buggy, though when it performs well, it does it better than scanning in this case.\nSystem WmiPrvSE or similar process may be seen increasing in activity with WMI in use.");

			layout.Controls.Add(new Label() { Text = "Scan frequency", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = padding, Dock = DockStyle.Left });
			var scanfrequency = new NumericUpDown()
			{
				Minimum = 0,
				Maximum = 360,
				Dock = DockStyle.Left,
				Value = 15,
				Width = 60,
			};
			var defaultBackColor = scanfrequency.BackColor;
			scanfrequency.ValueChanged += (sender, e) =>
			{
				if (ScanOrWMI.SelectedIndex == 0 && scanfrequency.Value == 0)
					scanfrequency.Value = 1;

				if (ScanOrWMI.SelectedIndex != 1 && scanfrequency.Value <= 5)
					scanfrequency.BackColor = System.Drawing.Color.LightPink;
				else
					scanfrequency.BackColor = defaultBackColor;
			};
			layout.Controls.Add(scanfrequency);
			tooltip.SetToolTip(scanfrequency, "In seconds. 0 disables. 1-4 are considered invalid values.");
			layout.Controls.Add(new Label() { Text = "WMI poll rate", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = padding, Dock = DockStyle.Left });
			var wmipolling = new NumericUpDown()
			{
				Minimum = 1,
				Maximum = 5,
				Value = 5,
				Dock = DockStyle.Left,
				Enabled = false,
				Width = 60,
			};
			layout.Controls.Add(wmipolling);
			tooltip.SetToolTip(wmipolling, "In seconds.");
			ScanOrWMI.SelectedIndexChanged += (sender, e) =>
			{
				if (ScanOrWMI.SelectedIndex == 0) // SCan only
				{
					scanfrequency.Enabled = true;
					scanfrequency.Value = 15;
					//wmipolling.Value = 5;
					wmipolling.Enabled = false;
				}
				else if (ScanOrWMI.SelectedIndex == 1) // WMI only
				{
					scanfrequency.Value = 0;
					scanfrequency.Enabled = false;
					wmipolling.Value = 5;
					wmipolling.Enabled = true;
				}
				else
				{
					scanfrequency.Enabled = true; // Both
					scanfrequency.Value = 60 * 5;
					wmipolling.Enabled = true;
					wmipolling.Value = 5;
				}
			};

			var powmon = new CheckBox()
			{
				AutoSize = true,
				//BackColor = System.Drawing.Color.Azure,
				Dock = DockStyle.Left
			};
			var powauto = new CheckBox()
			{
				AutoSize = true,
				//BackColor = System.Drawing.Color.Azure,
				Dock = DockStyle.Left
			};

			tooltip.SetToolTip(powmon, "Manage power mode.\nNot recommended if you already have a power manager, e.g. ASUS EPU.");
			layout.Controls.Add(new Label
			{
				Text = "Power manager",
				AutoSize = true,
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
				Padding = padding,
				Dock = DockStyle.Left
			});
			layout.Controls.Add(powmon);
			powmon.Enabled = true;
			powmon.Checked = true;
			powmon.Click += (sender, e) =>
			{
				if (powmon.Checked == false)
				{
					powauto.Checked = false;
					powauto.Enabled = false;
				}
				else
					powauto.Enabled = true;
			};

			tooltip.SetToolTip(powauto, "Automatically adjust power mode based on system load.");
			layout.Controls.Add(new Label { Text = "Power auto-adjust", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = padding, Dock = DockStyle.Left });
			layout.Controls.Add(powauto);
			powauto.Click += (sender, e) =>
			{
				if (powauto.Checked)
					powmon.Checked = true;
			};

			var fgmon = new CheckBox()
			{
				AutoSize = true,
				//BackColor = System.Drawing.Color.Azure,
				Dock = DockStyle.Left
			};
			tooltip.SetToolTip(fgmon, "Allow processes and power mode to be managed based on if a process is in the foreground.\nNOT YET FULLY IMPLEMENTED.");
			layout.Controls.Add(new Label { Text = "Foreground manager", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = padding, Dock = DockStyle.Left });
			layout.Controls.Add(fgmon);
			fgmon.Enabled = false;
			fgmon.Click += (sender, e) =>
			{
			};

			var tempmon = new CheckBox()
			{
				AutoSize = true,
				//BackColor = System.Drawing.Color.Azure,
				Dock = DockStyle.Left
			};
			tooltip.SetToolTip(tempmon, "Monitor temp folder.\nNOT YET FULLY IMPLEMENTED.");
			layout.Controls.Add(new Label { Text = "TEMP monitor", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = padding, Dock = DockStyle.Left });
			layout.Controls.Add(tempmon);
			tempmon.Enabled = false;
			tempmon.Checked = false;

			var paging = new CheckBox()
			{
				AutoSize = true,
				//BackColor = System.Drawing.Color.Azure,
				Dock = DockStyle.Left,
				Enabled = false,
			};
			tooltip.SetToolTip(tempmon, "Allow paging RAM to page/swap file.\nNOT YET FULLY IMPLEMENTED.");
			layout.Controls.Add(new Label { Text = "Allow paging", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = padding, Dock = DockStyle.Left });
			layout.Controls.Add(paging);
			paging.Checked = false;
			paging.Click += (sender, e) =>
			{

			};

			var showonstart = new CheckBox()
			{
				AutoSize = true,
				//BackColor = System.Drawing.Color.Azure,
				Dock = DockStyle.Left
			};
			tooltip.SetToolTip(showonstart, "Show main window on start.");
			layout.Controls.Add(new Label
			{
				Text = "Show on start",
				AutoSize = true,
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
				Padding = padding,
				Dock = DockStyle.Left
			});
			layout.Controls.Add(showonstart);
			showonstart.Checked = false;
			showonstart.Click += (sender, e) =>
			{

			};

			var savebutton = new Button()
			{
				Text = "Save",
				//AutoSize = true,
				Width = 80,
				Height = 20,
				//BackColor = System.Drawing.Color.Azure,
				Dock = DockStyle.Right
			};
			//l.Controls.Add(savebutton);
			savebutton.Click += (sender, e) =>
			{
				TaskMaster.ComponentConfigurationDone = true;

				var cfg = TaskMaster.loadConfig("Core.ini");
				var mainsec = cfg["Core"];
				var opt = mainsec["Version"];
				opt.StringValue = TaskMaster.ConfigVersion;
				opt.Comment = "Magical";

				var compsec = cfg["Components"];
				compsec["Process"].BoolValue = procmon.Checked;
				compsec["Process paths"].BoolValue = pathmon.Checked;
				compsec["Microphone"].BoolValue = micmon.Checked;
				//compsec["Media"].BoolValue = mediamon.Checked;
				compsec["Foreground"].BoolValue = fgmon.Checked;
				compsec["Network"].BoolValue = netmon.Checked;
				compsec["Power"].BoolValue = powmon.Checked;
				compsec["Paging"].BoolValue = paging.Checked;
				compsec["Maintenance"].BoolValue = tempmon.Checked;

				var powsec = cfg["Power"];
				powsec["Auto-adjust"].BoolValue = powauto.Checked;

				var optsec = cfg["Options"];
				optsec["Show on start"].BoolValue = showonstart.Checked;

				var perf = cfg["Performance"];
				int freq = (int)scanfrequency.Value;
				if (freq < 5 && freq != 0) freq = 5;
				perf["Rescan everything frequency"].IntValue = (ScanOrWMI.SelectedIndex != 1 ? freq : 0);
				perf["WMI event watcher"].BoolValue = (ScanOrWMI.SelectedIndex != 0);
				perf["WMI poll rate"].IntValue = ((int)wmipolling.Value);
				perf["WMI queries"].BoolValue = (ScanOrWMI.SelectedIndex != 0);

				TaskMaster.saveConfig(cfg);

				Close();
			};

			//l.Controls.Add(new Label());

			var endbutton = new Button()
			{
				Text = "Cancel",
				//AutoSize = true,
				Width = 80,
				Height = 20,
				//BackColor = System.Drawing.Color.Azure,
				Dock = DockStyle.Right
			};

			//l.Controls.Add(endbutton);
			endbutton.Click += (sender, e) =>
			{
				TaskMaster.ComponentConfigurationDone = false;
				Close();
			};

			var buttonpanel = new TableLayoutPanel()
			{
				AutoSize = true,
				//BackColor = System.Drawing.Color.LimeGreen
			};
			buttonpanel.ColumnCount = 2;
			//buttonpanel.Dock = DockStyle.Fill;
			//buttonpanel.AutoSize = true;
			//buttonpanel.Width = 160;

			buttonpanel.Controls.Add(savebutton);
			buttonpanel.Controls.Add(endbutton);

			baselayout.Controls.Add(layout);
			baselayout.Controls.Add(buttonpanel);
			Controls.Add(baselayout);
			AutoSize = true;

			Show();
		}
	}
}
