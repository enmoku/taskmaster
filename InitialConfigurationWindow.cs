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

using System;
using System.Windows.Forms;

namespace TaskMaster
{
	public class InitialConfigurationWindow : Form
	{
		public InitialConfigurationWindow()
		{
			Size = new System.Drawing.Size(220, 360); // width, height
			Text = "TaskMaster component configuration";
			AutoSize = false;

			FormBorderStyle = FormBorderStyle.FixedDialog;

			WindowState = FormWindowState.Normal;
			FormBorderStyle = FormBorderStyle.FixedDialog; // no min/max buttons as wanted
			MinimizeBox = false;
			MaximizeBox = false;

			var l = new TableLayoutPanel()
			{
				Parent = this,
				Dock = DockStyle.Fill,
				ColumnCount = 2,
			};

			var tooltip = new ToolTip();
			var padding = new Padding(6);

			Padding = padding;

			var micmon = new CheckBox();
			tooltip.SetToolTip(micmon, "Monitor default communications device.");
			l.Controls.Add(new Label { Text = "Microphone monitor", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = padding });
			l.Controls.Add(micmon);
			micmon.Click += (sender, e) =>
			{

			};

			var netmon = new CheckBox();
			tooltip.SetToolTip(netmon, "Monitor network interface status.");
			l.Controls.Add(new Label { Text = "Network monitor", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = padding });
			l.Controls.Add(netmon);
			netmon.Checked = true;
			netmon.Click += (sender, e) =>
			{

			};

			var procmon = new CheckBox();
			tooltip.SetToolTip(procmon, "Manage processes based on their name.");
			l.Controls.Add(new Label { Text = "Process/name manager", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = padding });
			l.Controls.Add(procmon);
			procmon.Enabled = false;
			procmon.Checked = true;

			var pathmon = new CheckBox();
			tooltip.SetToolTip(pathmon, "Manage processes based on their location.\nThese are processed only if by name matching does not catch something.");
			l.Controls.Add(new Label { Text = "Process/path manager", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = padding });
			l.Controls.Add(pathmon);
			pathmon.Checked = true;
			pathmon.Click += (sender, e) =>
			{
			};

			var powmon = new CheckBox();
			var powauto = new CheckBox();

			tooltip.SetToolTip(powmon, "Manage power mode.\nNot recommended if you already have a power manager, e.g. ASUS EPU.");
			l.Controls.Add(new Label { Text = "Power manager", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = padding });
			l.Controls.Add(powmon);
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
			l.Controls.Add(new Label { Text = "Power auto-adjust", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = padding });
			l.Controls.Add(powauto);
			powauto.Click += (sender, e) =>
			{
				if (powauto.Checked)
					powmon.Checked = true;
			};

			var fgmon = new CheckBox();
			tooltip.SetToolTip(fgmon, "Allow processes and power mode to be managed based on if a process is in the foreground.");
			l.Controls.Add(new Label { Text = "Foreground manager", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = padding });
			l.Controls.Add(fgmon);
			fgmon.Enabled = false;
			fgmon.Click += (sender, e) =>
			{
			};

			var tempmon = new CheckBox();
			tooltip.SetToolTip(tempmon, "Monitor temp folder.");
			l.Controls.Add(new Label { Text = "TEMP monitor", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = padding });
			l.Controls.Add(tempmon);
			tempmon.Enabled = false;
			tempmon.Checked = false;

			var paging = new CheckBox();
			tooltip.SetToolTip(tempmon, "Allow paging RAM to page/swap file.");
			l.Controls.Add(new Label { Text = "Allow paging", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = padding });
			l.Controls.Add(paging);
			paging.Checked = false;
			paging.Click += (sender, e) =>
			{

			};

			var showonstart = new CheckBox();
			tooltip.SetToolTip(showonstart, "Show main window on start.");
			l.Controls.Add(new Label { Text = "Show on start", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = padding });
			l.Controls.Add(showonstart);
			showonstart.Checked = false;
			showonstart.Click += (sender, e) =>
			{

			};

			var savebutton = new Button();
			savebutton.Text = "Save";
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

			var endbutton = new Button();
			endbutton.Text = "Cancel";
			//l.Controls.Add(endbutton);
			endbutton.Click += (sender, e) =>
			{
				TaskMaster.ComponentConfigurationDone = false;
				Close();
			};

			var buttonpanel = new TableLayoutPanel();
			buttonpanel.ColumnCount = 2;
			//buttonpanel.Dock = DockStyle.Fill;
			//buttonpanel.AutoSize = true;
			buttonpanel.Width = 160;

			buttonpanel.Controls.Add(savebutton);
			buttonpanel.Controls.Add(endbutton);

			l.Controls.Add(buttonpanel);

			Show();
		}
	}
}
