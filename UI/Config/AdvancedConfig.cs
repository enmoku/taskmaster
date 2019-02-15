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

using Serilog;
using System;
using System.Windows.Forms;

namespace Taskmaster.UI.Config
{
	public sealed class AdvancedConfig : UI.UniForm
	{
		public AdvancedConfig()
		{
			Text = "Advanced Configuration";
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
			layout.Controls.Add(new Label()); // empty

			var fgHysterisis = new Extensions.NumericUpDownEx()
			{
				Minimum = 200m,
				Maximum = 30000m,
				Unit = "ms",
				DecimalPlaces = 2,
				Increment = 100m,
				Value = 1500m,
				Enabled = Taskmaster.ActiveAppMonitorEnabled,
			};
			tooltip.SetToolTip(fgHysterisis, "Delay for foreground swapping to take effect.\nLower values make swaps more responsive.\nHigher values make it react less to rapid app swapping.");

			if (Taskmaster.ActiveAppMonitorEnabled)
				fgHysterisis.Value = Convert.ToDecimal(Taskmaster.activeappmonitor.Hysterisis.TotalMilliseconds);

			layout.Controls.Add(new Label { Text = "Foreground hysterisis", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Padding = LeftSubPadding });
			layout.Controls.Add(fgHysterisis);

			// PROCESS MANAGEMENT

			layout.Controls.Add(new Label { Text = "Process management", Font = boldfont, Padding = BigPadding, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			layout.Controls.Add(new Label()); // empty

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

			//

			savebutton.Click += (_, _ea) =>
			{
				// Record for restarts

				var corecfg = Taskmaster.Config.Load(Taskmaster.coreconfig);
				var cfg = corecfg.Config;

				var uisec = cfg["User Interface"];
				uisec["Update frequency"].IntValue = Convert.ToInt32(UIUpdateFrequency.Value);
				Taskmaster.mainwindow.UIUpdateFrequency = Convert.ToInt32(UIUpdateFrequency.Value);

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
					powsec.Remove("Watchlist powerdown delay");
					if (Taskmaster.PowerManagerEnabled)
						Taskmaster.powermanager.PowerdownDelay = null;
				}

				var perfsec = cfg["Performance"];

				int fghys = Convert.ToInt32(fgHysterisis.Value);
				perfsec["Foreground hysterisis"].IntValue = fghys;
				if (Taskmaster.ActiveAppMonitorEnabled)
					Taskmaster.activeappmonitor.Hysterisis = TimeSpan.FromMilliseconds(fghys);

				corecfg.MarkDirty();

				DialogResult = DialogResult.OK;
				Close();
			};

			savebutton.Anchor = AnchorStyles.Right;
			layout.Controls.Add(savebutton);
			layout.Controls.Add(cancelbutton);
		}

		private void Cancelbutton_Click(object sender, EventArgs e)
		{
			DialogResult = DialogResult.Cancel;
			Close();
		}

		public static void Reveal()
		{
			try
			{
				//await Task.Delay(0);
				// this is really horrifying mess
				var power = Taskmaster.powermanager;
				using (var acw = new AdvancedConfig())
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