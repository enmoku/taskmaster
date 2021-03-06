﻿//
// AdvancedConfig.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2019 M.A.
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

using MKAh;
using Serilog;
using System;
using System.Windows.Forms;

namespace Taskmaster.UI.Config
{
	using static Application;

	public class AdvancedConfig : UI.UniForm
	{
		readonly Extensions.TableLayoutPanel layout;
		readonly ToolTip tooltip;

		AdvancedConfig(ModuleManager modules, bool center = false)
			: base(centerOnScreen: center)
		{
			SuspendLayout();

			Text = "Advanced Configuration";

			var pad = Padding;
			pad.Right = 6;
			Padding = pad;

			using var corecfg = Config.Load(CoreConfigFilename);
			AutoSizeMode = AutoSizeMode.GrowAndShrink;
			AutoSize = true;

			tooltip = new ToolTip();

			layout = new Extensions.TableLayoutPanel()
			{
				ColumnCount = 2,
				Dock = DockStyle.Fill,
				AutoSize = true,
				Parent = this,
			};

			var savebutton = new Extensions.Button()
			{
				Text = "Save",
			};
			savebutton.NotifyDefault(true);

			var cancelbutton = new Extensions.Button()
			{
				Text = "Cancel",
			};
			cancelbutton.Click += Cancelbutton_Click;

			// IGNORE list
			layout.Controls.Add(new Extensions.Label { Text = "Ignore list", Font = BoldFont, Padding = BigPadding });
			layout.Controls.Add(new EmptySpace());

			var ignoreList = new Extensions.TextBox() { ReadOnly = true, Multiline = true, Dock = DockStyle.Top, Padding = LeftSubPadding, Anchor = AnchorStyles.Top | AnchorStyles.Left, ScrollBars = ScrollBars.Vertical, Height = Font.Height * 4 };
			ignoreList.Text = string.Join(", ", modules.processmanager.IgnoreList);
			tooltip.SetToolTip(ignoreList, "These process names are flat out ignored if encoutnered to protect the system.");
			layout.Controls.Add(ignoreList);
			layout.SetColumnSpan(ignoreList, 2);
			ignoreList.Font = Font;

			// PROTECT list
			layout.Controls.Add(new Extensions.Label { Text = "Protected list", Font = BoldFont, Padding = BigPadding });
			layout.Controls.Add(new EmptySpace());

			var protectList = new Extensions.TextBox() { ReadOnly = true, Multiline = true, Dock = DockStyle.Top, Padding = LeftSubPadding, Anchor = AnchorStyles.Top | AnchorStyles.Left, ScrollBars = ScrollBars.Vertical, Height = Font.Height * 4 };
			protectList.Text = string.Join(", ", modules.processmanager.ProtectList);
			tooltip.SetToolTip(protectList, "These process names are denied full control over to protect the system.");
			layout.Controls.Add(protectList);
			layout.SetColumnSpan(protectList, 2);

			// USER INTERFACE
			layout.Controls.Add(new Extensions.Label { Text = "User Interface", Font = BoldFont, Padding = BigPadding });
			layout.Controls.Add(new EmptySpace());

			var UIUpdateFrequency = new Extensions.NumericUpDownEx()
			{
				Minimum = 100m,
				Maximum = 5000m,
				Unit = "ms",
				DecimalPlaces = 0,
				Increment = 100m,
				Value = 2000m,
			};
			tooltip.SetToolTip(UIUpdateFrequency, "How frequently main UI update happens\nLower values increase CPU usage while UI is visible.");

			int uiupdatems;
			if (modules.mainwindow is null)
				uiupdatems = corecfg.Config["User Interface"].Get(Constants.UpdateFrequency)?.Int.Constrain(100, 5000) ?? 2000;
			else
				uiupdatems = modules.mainwindow?.UIUpdateFrequency ?? 2000;
			UIUpdateFrequency.Value = uiupdatems;

			layout.Controls.Add(new Extensions.Label { Text = "Refresh frequency", Padding = LeftSubPadding });
			layout.Controls.Add(UIUpdateFrequency);

			// FOREGROUND

			layout.Controls.Add(new Extensions.Label { Text = "Foreground", Font = BoldFont, Padding = BigPadding });

			if (ActiveAppMonitorEnabled)
				layout.Controls.Add(new EmptySpace());
			else
				layout.Controls.Add(new Extensions.Label() { Text = "Disabled", Font = BoldFont, Padding = BigPadding, ForeColor = System.Drawing.Color.Red });

			var fgHysterisis = new Extensions.NumericUpDownEx()
			{
				Minimum = 200m,
				Maximum = 30000m,
				Unit = "ms",
				DecimalPlaces = 0,
				Increment = 100m,
				Value = 1500m,
			};
			tooltip.SetToolTip(fgHysterisis, "Delay for foreground swapping to take effect.\nLower values make swaps more responsive.\nHigher values make it react less to rapid app swapping.");

			if (ActiveAppMonitorEnabled)
				fgHysterisis.Value = Convert.ToDecimal(modules.activeappmonitor.Hysterisis.TotalMilliseconds);
			else
			{
				using var cfg = Config.Load(CoreConfigFilename);
				var perfsec = cfg.Config[Constants.Performance];
				fgHysterisis.Value = perfsec.Get(Process.Constants.ForegroundHysterisis)?.Int.Constrain(200, 30000) ?? 1500;
			}

			layout.Controls.Add(new Extensions.Label { Text = "Foreground hysterisis", Padding = LeftSubPadding });
			layout.Controls.Add(fgHysterisis);

			// PROCESS MANAGEMENT

			layout.Controls.Add(new Extensions.Label { Text = "Process management", Font = BoldFont, Padding = BigPadding });
			layout.Controls.Add(new EmptySpace());

			var IgnoreRecentlyModifiedCooldown = new Extensions.NumericUpDownEx()
			{
				Minimum = 0,
				Maximum = 60,
				Unit = "mins",
				Value = Convert.ToDecimal(Process.Manager.IgnoreRecentlyModified.Value.TotalMinutes),
			};

			layout.Controls.Add(new Extensions.Label { Text = "Ignore recently modified", Padding = LeftSubPadding });
			layout.Controls.Add(IgnoreRecentlyModifiedCooldown);

			var watchlistPowerdown = new Extensions.NumericUpDownEx()
			{
				Minimum = 0m,
				Maximum = 300m,
				Unit = "s",
				DecimalPlaces = 0,
				Increment = 1m,
				Value = 5m,
				Enabled = PowerManagerEnabled,
			};
			tooltip.SetToolTip(watchlistPowerdown, "Delay before rule-based power is returned to normal.\nMostly useful if you frequently close and launch apps with power mode set so there's no powerdown in-between.");

			if (PowerManagerEnabled)
				watchlistPowerdown.Value = Convert.ToDecimal(modules.powermanager.PowerdownDelay.HasValue ? modules.powermanager.PowerdownDelay.Value.TotalSeconds : 0);

			layout.Controls.Add(new Extensions.Label { Text = "Powerdown delay", Padding = LeftSubPadding });
			layout.Controls.Add(watchlistPowerdown);

			// VOLUME METER

			layout.Controls.Add(new Extensions.Label { Text = Constants.VolumeMeter, Font = BoldFont, Padding = BigPadding });

			if (AudioManagerEnabled)
				layout.Controls.Add(new EmptySpace());
			else
				layout.Controls.Add(new Extensions.Label() { Text = "Disabled", Font = BoldFont, Padding = BigPadding, ForeColor = System.Drawing.Color.Red });

			var volmeter_topmost = new CheckBox();
			var volmeter_show = new CheckBox();

			var volmeter_capout = new Extensions.NumericUpDownEx()
			{
				Unit = "%",
				Maximum = 100,
				Minimum = 20,
				Value = 100,
			};

			var volmeter_capin = new Extensions.NumericUpDownEx()
			{
				Unit = "%",
				Maximum = 100,
				Minimum = 20,
				Value = 100,
			};

			var volmeter_frequency = new Extensions.NumericUpDownEx()
			{
				Unit = "ms",
				Increment = 20,
				Minimum = 10,
				Maximum = 5000,
				Value = 100,
			};

			var volsec = corecfg.Config[Constants.VolumeMeter];
			bool t_volmeter_topmost = volsec.Get("Topmost")?.Bool ?? true;
			int t_volmeter_frequency = volsec.Get("Refresh")?.Int.Constrain(10, 5000) ?? 100;
			int t_volmeter_capoutmax = volsec.Get(Audio.Constants.OutputThreshold)?.Int.Constrain(20, 100) ?? 100;
			int t_volmeter_capinmax = volsec.Get(Audio.Constants.InputThreshold)?.Int.Constrain(20, 100) ?? 100;
			bool t_volmeter_show = volsec.Get(Constants.ShowOnStart)?.Bool ?? false;

			volmeter_topmost.Checked = t_volmeter_topmost;
			volmeter_frequency.Value = t_volmeter_frequency;
			volmeter_capout.Value = t_volmeter_capoutmax;
			volmeter_capin.Value = t_volmeter_capinmax;
			volmeter_show.Checked = t_volmeter_show;

			layout.Controls.Add(new Extensions.Label { Text = "Refresh", Padding = LeftSubPadding });
			layout.Controls.Add(volmeter_frequency);
			tooltip.SetToolTip(volmeter_frequency, "Update frequency for the volume bars. Lower is faster.");

			layout.Controls.Add(new Extensions.Label { Text = "Output cap", Padding = LeftSubPadding });
			layout.Controls.Add(volmeter_capout);
			tooltip.SetToolTip(volmeter_capout, "Maximum volume for the bars. Helps make the bars more descriptive in case your software volume is very low.");
			layout.Controls.Add(new Extensions.Label { Text = "Input cap", Padding = LeftSubPadding });
			layout.Controls.Add(volmeter_capin);
			tooltip.SetToolTip(volmeter_capin, "Maximum volume for the bars. Helps make the bars more descriptive in case your software volume is very low.");

			layout.Controls.Add(new Extensions.Label { Text = "Topmost", Padding = LeftSubPadding });
			layout.Controls.Add(volmeter_topmost);
			tooltip.SetToolTip(volmeter_topmost, "Keeps the volume meter over other windows.");

			layout.Controls.Add(new Extensions.Label { Text = Constants.ShowOnStart, Padding = LeftSubPadding });
			layout.Controls.Add(volmeter_show);
			tooltip.SetToolTip(volmeter_show, "Show volume meter on start.");

			// Network

			/*
			layout.Controls.Add(new Label { Text = "Network", Font = boldfont, Padding = BigPadding, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });

			if (NetworkMonitorEnabled)
				layout.Controls.Add(new Label()); // empty
			else
				layout.Controls.Add(new Label() { Text = "Disabled", Font = boldfont, Padding = BigPadding, ForeColor = System.Drawing.Color.Red, TextAlign = System.Drawing.ContentAlignment.MiddleLeft });

			var netpoll_frequency = new Extensions.NumericUpDownEx()
			{
				Unit = "s",
				Increment = 1,
				Minimum = 1,
				Maximum = 15,
				Value = 15,
			};

			layout.Controls.Add(new Label { Text = "Poll interval", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Padding = LeftSubPadding });
			layout.Controls.Add(netpoll_frequency);
			tooltip.SetToolTip(netpoll_frequency, "Update frequency network device list.");
			*/

			var healthmonitor = new Extensions.Label { Text = "Health monitor", Font = BoldFont, Padding = BigPadding };
			layout.Controls.Add(healthmonitor);
			layout.SetColumnSpan(healthmonitor, 2);

			layout.Controls.Add(new Extensions.Label { Text = "Check frequency", Padding = LeftSubPadding });
			var healthmonfrequency = new Extensions.NumericUpDownEx()
			{
				Minimum = 1M,
				Maximum = 1440M,
				Value = 5M,
				Unit = "mins",
				Enabled = HealthMonitorEnabled,
			};
			layout.Controls.Add(healthmonfrequency);
			tooltip.SetToolTip(healthmonfrequency, "How often to check for system health.");

			layout.Controls.Add(new Extensions.Label { Text = "Memory auto-page threshold", Padding = LeftSubPadding });
			var memoryautopage = new Extensions.NumericUpDownEx()
			{
				Minimum = 0M,
				Maximum = 8000M,
				Value = 1500M,
				Unit = "MB",
				Enabled = PagingEnabled,
			};
			layout.Controls.Add(memoryautopage);
			tooltip.SetToolTip(memoryautopage, "Automatically try to page non-active apps when free memory goes below this threshold.\nRequires paging is enabled in component configuration.\n0 disables.");

			using var hmcfg = Application.Config.Load(HealthMonitor.HealthConfigFilename);
			var hmgensec = hmcfg.Config["General"];
			healthmonfrequency.Value = hmgensec.Get("Frequency")?.Int.Constrain(1, 1440) ?? 5;
			var freememsec = hmcfg.Config["Free Memory"];
			memoryautopage.Value = freememsec.Get("Threshold")?.Int.Constrain(0, 8000) ?? 1000;

			var extrafeatures = new Extensions.Label { Text = "Extra features", Font = BoldFont, Padding = BigPadding };
			layout.Controls.Add(extrafeatures);
			layout.SetColumnSpan(extrafeatures, 2);

			var parentoption = new CheckBox() { Checked = modules.processmanager.EnableParentFinding, };

			layout.Controls.Add(new Extensions.Label { Text = "Enable parent declaration", Padding = LeftSubPadding });
			layout.Controls.Add(parentoption);
			tooltip.SetToolTip(parentoption, "Allows parent process declaration in logs. Enabled per-rule.\nThis slows down logging noticeably.");

			// ---- SAVE --------------------------------------------------------------------------------------------------------

			savebutton.Click += (_, _2) =>
			{
				// Record for restarts
				using var corecfg = Config.Load(CoreConfigFilename);
				var cfg = corecfg.Config;

				var uisec = cfg["User Interface"];
				int uiupdatems = Convert.ToInt32(UIUpdateFrequency.Value).Constrain(100, 5000);
				uisec["Update frequency"].Int = uiupdatems;
				modules.mainwindow?.SetUIUpdateFrequency(uiupdatems);

				var powsec = cfg[HumanReadable.Hardware.Power.Section];

				if (watchlistPowerdown.Value > 0m)
				{
					int powdelay = Convert.ToInt32(watchlistPowerdown.Value);
					modules.powermanager?.SetPowerdownDelay(TimeSpan.FromSeconds(powdelay));
					powsec["Watchlist powerdown delay"].Int = powdelay;
				}
				else
				{
					powsec.TryRemove("Watchlist powerdown delay");
					modules.powermanager?.SetPowerdownDelay(null);
				}

				var perfsec = cfg["Performance"];

				if (IgnoreRecentlyModifiedCooldown.Value > 0M)
				{
					Process.Manager.IgnoreRecentlyModified = TimeSpan.FromMinutes(Convert.ToDouble(IgnoreRecentlyModifiedCooldown.Value));
					perfsec["Ignore recently modified"].Int = Convert.ToInt32(Process.Manager.IgnoreRecentlyModified.Value.TotalMinutes);
				}
				else
				{
					Process.Manager.IgnoreRecentlyModified = null;
					perfsec.TryRemove("Ignore recently modified");
				}

				int fghys = Convert.ToInt32(fgHysterisis.Value);
				perfsec["Foreground hysterisis"].Int = fghys;
				modules.activeappmonitor?.SetHysterisis(TimeSpan.FromMilliseconds(fghys));

				// Volume Meter
				volsec["Topmost"].Bool = volmeter_topmost.Checked;

				int voloutcap_t = Convert.ToInt32(volmeter_capout.Value);
				if (voloutcap_t < 100)
					volsec[Audio.Constants.OutputThreshold].Int = voloutcap_t;
				else
					volsec.TryRemove(Audio.Constants.OutputThreshold);

				int volincap_t = Convert.ToInt32(volmeter_capin.Value);
				if (volincap_t < 100)
					volsec[Audio.Constants.InputThreshold].Int = volincap_t;
				else
					volsec.TryRemove(Audio.Constants.InputThreshold);

				var volfreq_t = Convert.ToInt32(volmeter_frequency.Value);
				volsec["Refresh"].Int = volfreq_t;
				if (modules.volumemeter != null)
				{
					modules.volumemeter.Frequency = volfreq_t;
					modules.volumemeter.TopMost = volmeter_topmost.Checked;
					modules.volumemeter.VolumeOutputCap = voloutcap_t;
					modules.volumemeter.VolumeInputCap = volincap_t;
				}

				volsec[Constants.ShowOnStart].Bool = volmeter_show.Checked;

				// Extra features
				var logsec = corecfg.Config[HumanReadable.Generic.Logging];
				logsec["Enable parent finding"].Bool = parentoption.Checked;
				modules.processmanager.EnableParentFinding = parentoption.Checked;

				// Health Monitor
				using var hmcfg = Application.Config.Load(HealthMonitor.HealthConfigFilename);
				var hmgensec = hmcfg.Config["General"];
				hmgensec["Frequency"].Int = Convert.ToInt32(healthmonfrequency.Value);
				var freememsec = hmcfg.Config["Free Memory"];
				freememsec["Threshold"].Int = Convert.ToInt32(memoryautopage.Value);

				modules.healthmonitor?.SetCheckInterval(Convert.ToInt32(healthmonfrequency.Value));
				modules.healthmonitor?.SetMemFreeThreshold(Convert.ToInt32(memoryautopage.Value));

				DialogResult = DialogResult.OK;
				Close();
			};

			savebutton.Anchor = AnchorStyles.Right;
			layout.Controls.Add(savebutton);
			layout.Controls.Add(cancelbutton);

			ResumeLayout(performLayout: false);
		}

		void Cancelbutton_Click(object sender, EventArgs e)
		{
			DialogResult = DialogResult.Cancel;
			Close();
		}

		public static void Reveal(bool centerOnScreen = false)
		{
			try
			{
				//await Task.Delay(0);
				// this is really horrifying mess
				//var power = Taskmaster.powermanager;
				using var acw = new AdvancedConfig(globalmodules, centerOnScreen);
				acw.ShowDialog();
				if (!acw.DialogOK && Trace) Log.Verbose("<<UI>> Advanced config cancelled.");
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}
	}
}