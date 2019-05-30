﻿//
// ExperimentConfig.cs
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
	public sealed class ExperimentConfig : UniForm
	{
		public ExperimentConfig(bool center=false)
			: base(centerOnScreen: center)
		{
			SuspendLayout();

			Text = "Experiment Configuration";
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

			layout.Controls.Add(new AlignedLabel { Text = "EXPERIMENTAL", AutoSize = true, Font = boldfont, ForeColor = System.Drawing.Color.Maroon, Padding = BigPadding });
			layout.Controls.Add(new AlignedLabel { Text = "You've been warned", AutoSize = true, Font = boldfont, ForeColor = System.Drawing.Color.Maroon, Padding = BigPadding });

			var savebutton = new Button() { Text = "Save", };
			savebutton.NotifyDefault(true);

			var cancelbutton = new Button() { Text = "Cancel", };
			cancelbutton.Click += Cancelbutton_Click;

			// EXPERIMENTS

			var RecordAnalysisDelay = new Extensions.NumericUpDownEx()
			{
				Minimum = 0,
				Maximum = 300,
				Unit = "secs",
				Width = 80,
				DecimalPlaces = 0,
				Value = Convert.ToDecimal(Taskmaster.RecordAnalysis.HasValue ? Taskmaster.RecordAnalysis.Value.TotalSeconds : 0),
				//Anchor = AnchorStyles.Left
			};
			tooltip.SetToolTip(RecordAnalysisDelay, "Values higher than 0 enable process analysis\nThis needs to be enabled per watchlist rule to function");

			layout.Controls.Add(new AlignedLabel { Text = "Record analysis delay" });
			layout.Controls.Add(RecordAnalysisDelay);

			var hwmon = new CheckBox()
			{
				Checked = Taskmaster.HardwareMonitorEnabled,
				//Anchor = AnchorStyles.Left,
			};
			tooltip.SetToolTip(hwmon, "Enables hardware (such as GPU) monitoring\nLimited usability currently.");

			layout.Controls.Add(new AlignedLabel { Text = "Hardware monitor" });
			layout.Controls.Add(hwmon);

			var iopriority = new CheckBox()
			{
				Checked = Taskmaster.IOPriorityEnabled,
				Enabled = MKAh.Execution.IsWin7,
				//Anchor = AnchorStyles.Left,
			};
			tooltip.SetToolTip(iopriority, "Enable I/O priority adjstment\nWARNING: This can be REALLY BAD\nTake care what you do.\nOnly supported on Windows 7.");

			layout.Controls.Add(new AlignedLabel { Text = "I/O priority" });
			layout.Controls.Add(iopriority);

			// FILL IN BOTTOM

			layout.Controls.Add(new AlignedLabel { Text = "Restart required", AutoSize=true, Font = boldfont, ForeColor = System.Drawing.Color.Maroon, Padding = BigPadding });
			layout.Controls.Add(new EmptySpace());

			savebutton.Click += (_, _ea) =>
			{
				// Set to current use

				Taskmaster.RecordAnalysis = RecordAnalysisDelay.Value != decimal.Zero ? (TimeSpan?)TimeSpan.FromSeconds(Convert.ToDouble(RecordAnalysisDelay.Value)) : null;

				// Record for restarts

				using (var corecfg = Taskmaster.Config.Load(Taskmaster.CoreConfigFilename).BlockUnload())
				{
					var cfg = corecfg.Config;

					var exsec = cfg[Constants.Experimental];
					if (RecordAnalysisDelay.Value != decimal.Zero)
						exsec["Record analysis"].Int = Convert.ToInt32(RecordAnalysisDelay.Value);
					else
						exsec.TryRemove("Record analysis");

					if (iopriority.Checked && MKAh.Execution.IsWin7)
						exsec["IO Priority"].Bool = true;
					else
						exsec.TryRemove("IO Priority");

					cfg[Constants.Components][HumanReadable.Hardware.Section].Bool = hwmon.Checked;
				}

				DialogResult = DialogResult.OK;
				Close();
			};

			savebutton.Anchor = AnchorStyles.Right;
			layout.Controls.Add(savebutton);
			layout.Controls.Add(cancelbutton);

			Controls.Add(layout);

			ResumeLayout();
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
				using (var n = new Config.ExperimentConfig(centerOnScreen))
				{
					n.ShowDialog();
					if (n.DialogOK)
					{
						Log.Information("<Experiments> Settings changed");

						Taskmaster.ConfirmExit(restart: true, message: "Restart required for experimental settings to take effect.", alwaysconfirm: true);
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
