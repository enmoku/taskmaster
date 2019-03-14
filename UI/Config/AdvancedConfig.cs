//
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
	public sealed class AdvancedConfig : UI.UniForm
	{
		public AdvancedConfig(bool center = false)
			: base(center)
		{
			Text = "Advanced Configuration";

			var corecfg = Taskmaster.Config.Load(Taskmaster.coreconfig);

			AutoSizeMode = AutoSizeMode.GrowAndShrink;
			AutoSize = true;

			var tooltip = new ToolTip();

			var layout = new TableLayoutPanel()
			{
				ColumnCount = 2,
				Dock = DockStyle.Fill,
				AutoSize = true,
				Parent = this,
			};

			var savebutton = new Button()
			{
				Text = "Save",
			};
			savebutton.NotifyDefault(true);

			var cancelbutton = new Button()
			{
				Text = "Cancel",
			};
			cancelbutton.Click += Cancelbutton_Click;

			// USER INTERFACE
			layout.Controls.Add(new Label { Text = "User Interface", Font = boldfont, Padding = BigPadding, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			layout.Controls.Add(new Label()); // empty

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

			UIUpdateFrequency.Value = Taskmaster.mainwindow.UIUpdateFrequency;

			layout.Controls.Add(new Label { Text = "Refresh frequency", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Padding = LeftSubPadding });
			layout.Controls.Add(UIUpdateFrequency);

			// FOREGROUND

			layout.Controls.Add(new Label { Text = "Foreground", Font = boldfont, Padding = BigPadding, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });

			if (Taskmaster.ActiveAppMonitorEnabled)
				layout.Controls.Add(new Label()); // empty
			else
				layout.Controls.Add(new Label() { Text = "Disabled", Font = boldfont, Padding = BigPadding, ForeColor = System.Drawing.Color.Red, TextAlign = System.Drawing.ContentAlignment.MiddleLeft });

			var fgHysterisis = new Extensions.NumericUpDownEx()
			{
				Minimum = 200m,
				Maximum = 30000m,
				Unit = "ms",
				DecimalPlaces = 2,
				Increment = 100m,
				Value = 1500m,
			};
			tooltip.SetToolTip(fgHysterisis, "Delay for foreground swapping to take effect.\nLower values make swaps more responsive.\nHigher values make it react less to rapid app swapping.");

			if (Taskmaster.ActiveAppMonitorEnabled)
				fgHysterisis.Value = Convert.ToDecimal(Taskmaster.activeappmonitor.Hysterisis.TotalMilliseconds);
			else
			{
				var cfg = Taskmaster.Config.Load(Taskmaster.coreconfig);
				var perfsec = cfg.Config["Performance"];
				fgHysterisis.Value = perfsec.Get("Foreground hysterisis")?.IntValue.Constrain(200, 30000) ?? 1500;
			}

			layout.Controls.Add(new Label { Text = "Foreground hysterisis", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Padding = LeftSubPadding });
			layout.Controls.Add(fgHysterisis);

			// PROCESS MANAGEMENT

			layout.Controls.Add(new Label { Text = "Process management", Font = boldfont, Padding = BigPadding, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			layout.Controls.Add(new Label()); // empty

			var IgnoreRecentlyModifiedCooldown = new Extensions.NumericUpDownEx()
			{
				Minimum = 0,
				Maximum = 60,
				Unit = "mins",
				Value = Convert.ToDecimal(ProcessManager.IgnoreRecentlyModified.Value.TotalMinutes),
			};

			layout.Controls.Add(new Label { Text = "Ignore recently modified", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Padding = LeftSubPadding });
			layout.Controls.Add(IgnoreRecentlyModifiedCooldown);

			var watchlistPowerdown = new Extensions.NumericUpDownEx()
			{
				Minimum = 0m,
				Maximum = 60m,
				Unit = "s",
				DecimalPlaces = 0,
				Increment = 1m,
				Value = 5m,
				Enabled = Taskmaster.PowerManagerEnabled,
			};
			tooltip.SetToolTip(watchlistPowerdown, "Delay before rule-based power is returned to normal.\nMostly useful if you frequently close and launch apps with power mode set so there's no powerdown in-between.");

			if (Taskmaster.PowerManagerEnabled)
				watchlistPowerdown.Value = Convert.ToDecimal(Taskmaster.powermanager.PowerdownDelay.HasValue ? Taskmaster.powermanager.PowerdownDelay.Value.TotalSeconds : 0);

			layout.Controls.Add(new Label { Text = "Powerdown delay", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Padding = LeftSubPadding });
			layout.Controls.Add(watchlistPowerdown);

			// VOLUME METER

			layout.Controls.Add(new Label { Text = "Volume meter", Font = boldfont, Padding = BigPadding, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });

			if (Taskmaster.AudioManagerEnabled)
				layout.Controls.Add(new Label()); // empty
			else
				layout.Controls.Add(new Label() { Text = "Disabled", Font = boldfont, Padding = BigPadding, ForeColor = System.Drawing.Color.Red, TextAlign = System.Drawing.ContentAlignment.MiddleLeft });

			var volmeter_topmost = new CheckBox();
			var volmeter_show = new CheckBox();

			var volmeter_capout = new Extensions.NumericUpDownEx()
			{
				Unit ="%",
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

			var volsec = corecfg.Config["Volume Meter"];
			bool t_volmeter_topmost = volsec.Get("Topmost")?.BoolValue ?? true;
			int t_volmeter_frequency = volsec.Get("Refresh")?.IntValue.Constrain(10, 5000) ?? 100;
			int t_volmeter_capoutmax = volsec.Get("Output threshold")?.IntValue.Constrain(20, 100) ?? 100;
			int t_volmeter_capinmax = volsec.Get("Input threshold")?.IntValue.Constrain(20, 100) ?? 100;
			bool t_volmeter_show = volsec.Get("Show on start")?.BoolValue ?? false;

			volmeter_topmost.Checked = t_volmeter_topmost;
			volmeter_frequency.Value = t_volmeter_frequency;
			volmeter_capout.Value = t_volmeter_capoutmax;
			volmeter_capin.Value = t_volmeter_capinmax;
			volmeter_show.Checked = t_volmeter_show;

			layout.Controls.Add(new Label { Text = "Refresh", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Padding = LeftSubPadding });
			layout.Controls.Add(volmeter_frequency);
			tooltip.SetToolTip(volmeter_frequency, "Update frequency for the volume bars. Lower is faster.");

			layout.Controls.Add(new Label { Text = "Output cap", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Padding = LeftSubPadding });
			layout.Controls.Add(volmeter_capout);
			tooltip.SetToolTip(volmeter_capout, "Maximum volume for the bars. Helps make the bars more descriptive in case your software volume is very low.");
			layout.Controls.Add(new Label { Text = "Input cap", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Padding = LeftSubPadding });
			layout.Controls.Add(volmeter_capin);
			tooltip.SetToolTip(volmeter_capin, "Maximum volume for the bars. Helps make the bars more descriptive in case your software volume is very low.");

			layout.Controls.Add(new Label { Text = "Topmost", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Padding = LeftSubPadding });
			layout.Controls.Add(volmeter_topmost);
			tooltip.SetToolTip(volmeter_topmost, "Keeps the volume meter over other windows.");

			layout.Controls.Add(new Label { Text = "Show on start", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Padding = LeftSubPadding });
			layout.Controls.Add(volmeter_show);
			tooltip.SetToolTip(volmeter_show, "Show volume meter on start.");

			// ----

			savebutton.Click += (_, _ea) =>
			{
				// Record for restarts

				var cfg = corecfg.Config;

				var uisec = cfg["User Interface"];
				int uiupdatems = Convert.ToInt32(UIUpdateFrequency.Value).Constrain(100, 5000);
				uisec["Update frequency"].IntValue = uiupdatems;
				Taskmaster.mainwindow.UIUpdateFrequency = uiupdatems;

				var powsec = cfg[HumanReadable.Hardware.Power.Section];

				if (watchlistPowerdown.Value > 0m)
				{
					int powdelay = Convert.ToInt32(watchlistPowerdown.Value);
					if (Taskmaster.PowerManagerEnabled)
						Taskmaster.powermanager.PowerdownDelay = TimeSpan.FromSeconds(powdelay);
					powsec["Watchlist powerdown delay"].IntValue = powdelay;
				}
				else
				{
					powsec.TryRemove("Watchlist powerdown delay");
					if (Taskmaster.PowerManagerEnabled)
						Taskmaster.powermanager.PowerdownDelay = null;
				}

				var perfsec = cfg["Performance"];

				if (IgnoreRecentlyModifiedCooldown.Value > 0M)
				{
					ProcessManager.IgnoreRecentlyModified = TimeSpan.FromMinutes(Convert.ToDouble(IgnoreRecentlyModifiedCooldown.Value));
					perfsec["Ignore recently modified"].IntValue = Convert.ToInt32(ProcessManager.IgnoreRecentlyModified.Value.TotalMinutes);
				}
				else
				{
					ProcessManager.IgnoreRecentlyModified = null;
					perfsec.TryRemove("Ignore recently modified");
				}

				int fghys = Convert.ToInt32(fgHysterisis.Value);
				perfsec["Foreground hysterisis"].IntValue = fghys;
				if (Taskmaster.ActiveAppMonitorEnabled)
					Taskmaster.activeappmonitor.Hysterisis = TimeSpan.FromMilliseconds(fghys);

				volsec["Topmost"].BoolValue = volmeter_topmost.Checked;

				if (volmeter_capout.Value < 100)
					volsec["Output threshold"].IntValue = Convert.ToInt32(volmeter_capout.Value);
				else
					volsec.TryRemove("Output threshold");

				if (volmeter_capin.Value < 100)
					volsec["Input threshold"].IntValue = Convert.ToInt32(volmeter_capin.Value);
				else
					volsec.TryRemove("Input threshold");

				volsec["Refresh"].IntValue = Convert.ToInt32(volmeter_frequency.Value);

				volsec["Show on start"].BoolValue = volmeter_show.Checked;

				corecfg.MarkDirty();

				DialogResult = DialogResult.OK;
				Close();
			};

			savebutton.Anchor = AnchorStyles.Right;
			layout.Controls.Add(savebutton);
			layout.Controls.Add(cancelbutton);
		}

		void Cancelbutton_Click(object sender, EventArgs e)
		{
			DialogResult = DialogResult.Cancel;
			Close();
		}

		public static void Reveal(bool centerOnScreen=false)
		{
			try
			{
				//await Task.Delay(0);
				// this is really horrifying mess
				var power = Taskmaster.powermanager;
				using (var acw = new AdvancedConfig(centerOnScreen))
				{
					var res = acw.ShowDialog();
					if (acw.DialogResult == DialogResult.OK)
					{
						// NOP
					}
					else
					{
						if (Taskmaster.Trace) Log.Verbose("<<UI>> Advanced config cancelled.");
					}
				}
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}
	}
}