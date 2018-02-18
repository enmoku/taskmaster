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
			tooltip.SetToolTip(micmon, "Monitor default communications device.");
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
			tooltip.SetToolTip(netmon, "Monitor network interface status.");
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
			tooltip.SetToolTip(procmon, "Manage processes based on their name.");
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
			tooltip.SetToolTip(pathmon, "Manage processes based on their location.\nThese are processed only if by name matching does not catch something.");
			layout.Controls.Add(new Label { Text = "Process/path manager", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = padding, Dock = DockStyle.Left });
			layout.Controls.Add(pathmon);
			pathmon.Checked = true;
			pathmon.Click += (sender, e) =>
			{
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
			tooltip.SetToolTip(fgmon, "Allow processes and power mode to be managed based on if a process is in the foreground.");
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
			tooltip.SetToolTip(tempmon, "Monitor temp folder.");
			layout.Controls.Add(new Label { Text = "TEMP monitor", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = padding, Dock = DockStyle.Left });
			layout.Controls.Add(tempmon);
			tempmon.Enabled = false;
			tempmon.Checked = false;

			var paging = new CheckBox()
			{
				AutoSize = true,
				//BackColor = System.Drawing.Color.Azure,
				Dock = DockStyle.Left
			};
			tooltip.SetToolTip(tempmon, "Allow paging RAM to page/swap file.");
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
