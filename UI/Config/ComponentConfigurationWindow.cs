//
// ComponentConfigurationWindow.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2018–2019 M.A.
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

namespace Taskmaster.UI.Config
{
	sealed public class ComponentConfigurationWindow : UniForm
	{
		public ComponentConfigurationWindow(bool initial = true)
		{
			// Size = new System.Drawing.Size(220, 360); // width, height

			Text = "Component configuration";

			DialogResult = DialogResult.Abort;

			FormBorderStyle = FormBorderStyle.FixedDialog;

			WindowState = FormWindowState.Normal;
			FormBorderStyle = FormBorderStyle.FixedDialog; // no min/max buttons as wanted
			AutoSizeMode = AutoSizeMode.GrowAndShrink;

			if (initial)
				StartPosition = FormStartPosition.CenterScreen;
			else
				StartPosition = FormStartPosition.CenterParent;

			bool WMIPolling = false;
			int WMIPollDelay = 5;
			int ScanFrequency = 180;
			bool scan = true;

			if (Taskmaster.processmanager == null)
			{
				var corecfg = Taskmaster.Config.Load(Taskmaster.coreconfig);
				var perfsec = corecfg.Config["Performance"];
				WMIPolling = perfsec.TryGet("WMI event watcher")?.BoolValue ?? true;
				WMIPollDelay = perfsec.TryGet("WMI poll delay")?.IntValue ?? 2;
				ScanFrequency = perfsec.TryGet("Scan frequency")?.IntValue ?? 180;
			}
			else
			{
				WMIPolling = Taskmaster.processmanager.WMIPolling;
				WMIPollDelay = Taskmaster.processmanager.WMIPollDelay;
				if (Taskmaster.processmanager.ScanFrequency.HasValue)
					ScanFrequency = Convert.ToInt32(Taskmaster.processmanager.ScanFrequency.Value.TotalSeconds);
				else
					scan = false;
			}

			var layout = new TableLayoutPanel()
			{
				Parent = this,
				ColumnCount = 2,
				AutoSize = true,
				Padding = new Padding(3),
				Dock = DockStyle.Fill,
				//Dock = DockStyle.Top,
				//BackColor = System.Drawing.Color.Aqua,
			};

			var tooltip = new ToolTip();

			var audioman = new CheckBox()
			{
				AutoSize = true,
				Dock = DockStyle.Left,
				Checked = initial ? false : Taskmaster.AudioManagerEnabled,
			};
			tooltip.SetToolTip(audioman, "Automatically set application mixer volume.");

			layout.Controls.Add(new Label { Text = "Audio manager", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = BigPadding, Dock = DockStyle.Left });
			layout.Controls.Add(audioman);

			var micmon = new CheckBox()
			{
				AutoSize = true,
				//BackColor = System.Drawing.Color.Azure,
				Dock = DockStyle.Left,
				Checked = initial ? false : Taskmaster.MicrophoneManagerEnabled,
			};
			tooltip.SetToolTip(micmon, "Monitor default communications device and keep its volume.\nRequires audio manager to be enabled.");

			audioman.CheckedChanged += (s,e) =>
			{
				micmon.Enabled = audioman.Checked;
			};

			layout.Controls.Add(new Label { Text = "Microphone manager", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = BigPadding, Dock = DockStyle.Left });
			layout.Controls.Add(micmon);
			micmon.Click += (_, _ea) =>
			{
			};

			var netmon = new CheckBox()
			{
				AutoSize = true,
				//BackColor = System.Drawing.Color.Azure,
				Dock = DockStyle.Left
			};
			tooltip.SetToolTip(netmon, "Monitor network interface status and report online status.");
			layout.Controls.Add(new Label { Text = "Network monitor", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = BigPadding, Dock = DockStyle.Left });
			layout.Controls.Add(netmon);
			netmon.Checked = initial ? true : Taskmaster.NetworkMonitorEnabled;
			netmon.Click += (_, _ea) =>
			{
			};

			var procmon = new CheckBox()
			{
				AutoSize = true,
				//BackColor = System.Drawing.Color.Azure,
				Dock = DockStyle.Left
			};
			tooltip.SetToolTip(procmon, "Manage processes based on their name. Default feature of Taskmaster and thus can not be disabled.");
			layout.Controls.Add(new Label { Text = "Process manager", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = BigPadding, Dock = DockStyle.Left });
			layout.Controls.Add(procmon);
			procmon.Enabled = false;
			procmon.Checked = initial ? true : Taskmaster.ProcessMonitorEnabled;

			layout.Controls.Add(new Label() { Text = "Process detection", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = BigPadding, Dock = DockStyle.Left });
			var ScanOrWMI = new ComboBox()
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				Items = { "Scanning", "WMI polling", "Both" },
				SelectedIndex = 0,
				Width = 80,
			};
			layout.Controls.Add(ScanOrWMI);
			tooltip.SetToolTip(ScanOrWMI, "Scanning involves getting all procesess and going through the list, which can cause tiny CPU spiking.\nWMI polling sets up system WMI event listener.\nWMI is known to be slow and buggy, though when it performs well, it does it better than scanning in this case.\nSystem WmiPrvSE or similar process may be seen increasing in activity with WMI in use.");

			layout.Controls.Add(new Label() { Text = "Scan frequency", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = BigPadding, Dock = DockStyle.Left });
			var scanfrequency = new Extensions.NumericUpDownEx()
			{
				Unit = "s",
				Minimum = 0,
				Maximum = 360,
				Dock = DockStyle.Left,
				Value = initial ? 15 : ScanFrequency,
				Width = 60,
			};
			var defaultBackColor = scanfrequency.BackColor;
			scanfrequency.ValueChanged += (_, _ea) =>
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
			layout.Controls.Add(new Label() { Text = "WMI poll rate", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = BigPadding, Dock = DockStyle.Left });
			var wmipolling = new Extensions.NumericUpDownEx()
			{
				Minimum = 1,
				Maximum = 5,
				Unit = "s",
				Value = initial ? 5 : WMIPollDelay,
				Dock = DockStyle.Left,
				Enabled = false,
				Width = 60,
			};
			layout.Controls.Add(wmipolling);
			tooltip.SetToolTip(wmipolling, "In seconds.");
			ScanOrWMI.SelectedIndexChanged += (_, _ea) =>
			{
				scanfrequency.Enabled = ScanOrWMI.SelectedIndex != 1; // 0 or 2
				wmipolling.Enabled = ScanOrWMI.SelectedIndex != 0; // 1 or 2

				if (ScanOrWMI.SelectedIndex == 0) // Not WMI-only
					scanfrequency.Value = initial ? 15 : ScanFrequency;
				else if (ScanOrWMI.SelectedIndex == 1) // Not Scan-only
					wmipolling.Value = initial ? 2 : WMIPollDelay;
				else // Both
				{
					scanfrequency.Value = initial ? 180 : ScanFrequency;
					wmipolling.Value = initial ? 2 : WMIPollDelay;
				}
			};

			ScanOrWMI.SelectedIndex = initial ? 0 : ((WMIPolling && scan) ? 2 : (WMIPolling ? 1 : 0));

			var powmon = new CheckBox()
			{
				AutoSize = true,
				//BackColor = System.Drawing.Color.Azure,
				Dock = DockStyle.Left,
				Enabled = true,
				Checked = initial ? false : Taskmaster.PowerManagerEnabled,
			};
			tooltip.SetToolTip(powmon, "Manage power mode.\nNot recommended if you already have a power manager.");

			layout.Controls.Add(new Label { Text = "Power manager", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = BigPadding, Dock = DockStyle.Left });
			layout.Controls.Add(powmon);

			var powbehaviour = new ComboBox()
			{
				Items = { HumanReadable.Hardware.Power.AutoAdjust, HumanReadable.Hardware.Power.RuleBased, HumanReadable.Hardware.Power.Manual },
				DropDownStyle = ComboBoxStyle.DropDownList,
				SelectedIndex = 1,
			};

			powbehaviour.Enabled = powmon.Checked;
			switch (Taskmaster.powermanager?.LaunchBehaviour ?? PowerManager.PowerBehaviour.RuleBased)
			{
				case PowerManager.PowerBehaviour.Auto:
					powbehaviour.SelectedIndex = 0;
					break;
				default:
				case PowerManager.PowerBehaviour.RuleBased:
					powbehaviour.SelectedIndex = 1;
					break;
				case PowerManager.PowerBehaviour.Manual:
					powbehaviour.SelectedIndex = 2;
					break;
			}

			tooltip.SetToolTip(powbehaviour,
				"Auto-adjust = Automatically adjust power mode based on system load or by watchlist rules\n"+
				"Rule-based = Watchlist rules can affect it\n"+
				"Manual = User control only");
			layout.Controls.Add(new Label { Text = "Power behaviour", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = BigPadding, Dock = DockStyle.Left });
			layout.Controls.Add(powbehaviour);

			var fgmon = new CheckBox()
			{
				AutoSize = true,
				//BackColor = System.Drawing.Color.Azure,
				Dock = DockStyle.Left
			};
			fgmon.Checked = initial ? true : Taskmaster.ActiveAppMonitorEnabled;
			tooltip.SetToolTip(fgmon, "Allow processes and power mode to be managed based on if a process is in the foreground.\nPOWER MODE SWITCHING NOT IMPLEMENTED.");
			layout.Controls.Add(new Label { Text = "Foreground manager", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = BigPadding, Dock = DockStyle.Left });
			layout.Controls.Add(fgmon);

			// NVM monitor
			var nvmmon = new CheckBox()
			{
				AutoSize = true,
				Dock = DockStyle.Left,
				Checked = initial ? false : Taskmaster.StorageMonitorEnabled,
				Enabled = false,
			};
			tooltip.SetToolTip(nvmmon, "Monitor non-volatile memory (HDDs, SSDs, etc.)");
			layout.Controls.Add(new Label { Text = "NVM monitor", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = BigPadding, Dock = DockStyle.Left });
			layout.Controls.Add(nvmmon);

			// TEMP monitor
			var tempmon = new CheckBox()
			{
				AutoSize = true,
				//BackColor = System.Drawing.Color.Azure,
				Dock = DockStyle.Left
			};
			tooltip.SetToolTip(tempmon, "Monitor temp folder.\nNOT YET FULLY IMPLEMENTED.");
			layout.Controls.Add(new Label { Text = "TEMP monitor", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = BigPadding, Dock = DockStyle.Left });
			layout.Controls.Add(tempmon);
			tempmon.Enabled = false;
			tempmon.Checked = initial ? false : Taskmaster.MaintenanceMonitorEnabled;

			// PAGING
			var paging = new CheckBox()
			{
				AutoSize = true,
				//BackColor = System.Drawing.Color.Azure,
				Dock = DockStyle.Left,
			};
			tooltip.SetToolTip(tempmon, "Allow paging RAM to page/swap file.\nNOT YET FULLY IMPLEMENTED.");
			layout.Controls.Add(new Label { Text = "Allow paging", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = BigPadding, Dock = DockStyle.Left });
			layout.Controls.Add(paging);
			paging.Checked = initial ? false : Taskmaster.PagingEnabled;

			// REGISTER GLOBAL HOTKEYS
			var hotkeys = new CheckBox()
			{
				AutoSize = true,
				Dock = DockStyle.Left,
				Checked = Taskmaster.GlobalHotkeys,
			};
			tooltip.SetToolTip(hotkeys, "Register globally accessible hotkeys for certain actions.");
			layout.Controls.Add(new Label { Text = "Global hotkeys", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = BigPadding, Dock = DockStyle.Left });
			layout.Controls.Add(hotkeys);

			// SHOW ON START
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
				Padding = BigPadding,
				Dock = DockStyle.Left
			});
			layout.Controls.Add(showonstart);
			showonstart.Checked = initial ? false : Taskmaster.ShowOnStart;

			var autodoc = new CheckBox()
			{
				AutoSize = true,
				//BackColor = System.Drawing.Color.Azure,
				Dock = DockStyle.Left,
				Checked = initial ? false : Taskmaster.HealthMonitorEnabled,
			};
			layout.Controls.Add(new Label()
			{
				Text = "Health monitor",
				AutoSize = true,
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
				Padding = BigPadding,
				Dock = DockStyle.Left
			});
			tooltip.SetToolTip(autodoc, "Variety of other health & problem monitoring.\nCurrently includes low memory detection and attempting to page apps to free some of it.");
			layout.Controls.Add(autodoc);

			// BUTTONS
			var savebutton = new Button()
			{
				Text = "Save",
				//AutoSize = true,
				Width = 80,
				Height = 20,
				//BackColor = System.Drawing.Color.Azure,
				Dock = DockStyle.Right
			};
			// l.Controls.Add(savebutton);
			savebutton.Click += (_, _ea) =>
			{
				var cfg = Taskmaster.Config.Load(Taskmaster.coreconfig);
				var mainsec = cfg.Config["Core"];
				var opt = mainsec["Version"];
				opt.StringValue = Taskmaster.ConfigVersion;
				opt.Comment = "Magical";

				var compsec = cfg.Config["Components"];
				compsec[HumanReadable.System.Process.Section].BoolValue = procmon.Checked;
				compsec[HumanReadable.Hardware.Audio.Section].BoolValue = audioman.Checked;
				compsec["Microphone"].BoolValue = micmon.Checked;
				// compsec["Media"].BoolValue = mediamon.Checked;
				compsec[HumanReadable.System.Process.Foreground].BoolValue = fgmon.Checked;
				compsec["Network"].BoolValue = netmon.Checked;
				compsec[HumanReadable.Hardware.Power.Section].BoolValue = powmon.Checked;
				compsec["Paging"].BoolValue = paging.Checked;
				compsec["Maintenance"].BoolValue = tempmon.Checked;
				compsec["Health"].BoolValue = autodoc.Checked;

				var powsec = cfg.Config[HumanReadable.Hardware.Power.Section];
				if (powmon.Checked) powsec["Behaviour"].StringValue = powbehaviour.Text.ToLower();

				var optsec = cfg.Config["Options"];
				optsec["Show on start"].BoolValue = showonstart.Checked;

				var perf = cfg.Config["Performance"];
				var freq = (int)scanfrequency.Value;
				if (freq < 5 && freq != 0) freq = 5;
				perf["Scan frequency"].IntValue = (ScanOrWMI.SelectedIndex == 1 ? 0 : freq);
				perf["WMI event watcher"].BoolValue = (ScanOrWMI.SelectedIndex != 0);
				perf["WMI poll delay"].IntValue = ((int)wmipolling.Value);
				perf["WMI queries"].BoolValue = (ScanOrWMI.SelectedIndex != 0);

				var qol = cfg.Config["Quality of Life"];
				qol["Register global hotkeys"].BoolValue = hotkeys.Checked;

				cfg.Save(force:true);

				DialogResult = DialogResult.OK;

				Close();
			};

			// l.Controls.Add(new Label());

			var endbutton = new Button()
			{
				Text = "Cancel",
				//AutoSize = true,
				Width = 80,
				Height = 26,
				//BackColor = System.Drawing.Color.Azure,
				Dock = DockStyle.Left
			};

			// l.Controls.Add(endbutton);
			endbutton.Click += EndButtonClick;

			layout.Controls.Add(savebutton);
			layout.Controls.Add(endbutton);

			AcceptButton = savebutton;
			CancelButton = endbutton;
			savebutton.NotifyDefault(true);
			endbutton.NotifyDefault(false);
			UpdateDefaultButton();

			// Cross-componenty checkbox functionality

			// fgmon.Enabled is bound to procmon.Checked, procmon however is always in use and checkbox disabled so this doesn't matter
			//fgmon.DataBindings.Add("Enabled", procmon, "Checked", false, DataSourceUpdateMode.Never);
		}

		void EndButtonClick(object _, EventArgs _ea)
		{
			DialogResult = DialogResult.Abort;
			Close();
		}
	}
}