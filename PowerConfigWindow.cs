//
// PowerConfigWindow.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2018 M.A.
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
using System.Threading.Tasks;
using System.Windows.Forms;
using Serilog;
using Taskmaster.PowerInfo;

namespace Taskmaster
{
	sealed public class PowerConfigWindow : UI.UniForm
	{
		public AutoAdjustSettings oldAutoAdjust;
		public AutoAdjustSettings newAutoAdjust;

		public PowerConfigWindow()
		{
			Text = "Power auto-adjust configuration";

			var AutoAdjust = oldAutoAdjust = Taskmaster.powermanager.AutoAdjust;

			FormBorderStyle = FormBorderStyle.FixedDialog;

			WindowState = FormWindowState.Normal;
			FormBorderStyle = FormBorderStyle.FixedDialog; // no min/max buttons as wanted
			AutoSizeMode = AutoSizeMode.GrowAndShrink;

			var layout = new TableLayoutPanel()
			{
				ColumnCount = 2,
				AutoSize = true,
			};

			var defaultmode = new ComboBox()
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				AutoCompleteSource = AutoCompleteSource.ListItems,
				AutoCompleteMode = AutoCompleteMode.SuggestAppend,
				AutoSize = true,
				Dock = DockStyle.Fill,
			};

			string[] powermodes = new string[] {
				PowerManager.GetModeName(PowerMode.HighPerformance), PowerManager.GetModeName(PowerMode.Balanced), PowerManager.GetModeName(PowerMode.PowerSaver)
			};
			defaultmode.Items.AddRange(powermodes);
			defaultmode.SelectedIndex = AutoAdjust.DefaultMode == PowerMode.Balanced ? 1 : AutoAdjust.DefaultMode == PowerMode.PowerSaver ? 2 : 0;

			layout.Controls.Add(new Label() { Text = "Default mode", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			layout.Controls.Add(defaultmode);

			var highmode = new ComboBox()
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				AutoCompleteSource = AutoCompleteSource.ListItems,
				AutoCompleteMode = AutoCompleteMode.SuggestAppend,
				AutoSize = true,
			};
			highmode.Items.AddRange(powermodes);
			highmode.SelectedIndex = AutoAdjust.High.Mode == PowerMode.Balanced ? 1 : AutoAdjust.High.Mode == PowerMode.PowerSaver ? 2 : 0;

			layout.Controls.Add(new Label() { Text = "High mode", TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
			layout.Controls.Add(highmode);

			layout.Controls.Add(new Label() { Text = "Commit CPU% threshold", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			var highcommitthreshold = new NumericUpDown() { Maximum = 95, Minimum = 5, Value = 50 };
			highcommitthreshold.Value = Convert.ToDecimal(AutoAdjust.High.Commit.Threshold).Constrain(5, 95);
			layout.Controls.Add(highcommitthreshold);
			layout.Controls.Add(new Label() { Text = "Commit level", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			var highcommitlevel = new NumericUpDown() { Maximum = 15, Minimum = 0, Value = 3 };
			highcommitlevel.Value = AutoAdjust.High.Commit.Level.Constrain(0, 15);
			layout.Controls.Add(highcommitlevel);
			layout.Controls.Add(new Label() { Text = "Backoff high CPU%", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			var highbackoffhigh = new NumericUpDown() { Maximum = 95, Minimum = 5, Value = 50 };
			highbackoffhigh.Value = Convert.ToDecimal(AutoAdjust.High.Backoff.High).Constrain(5, 95);
			layout.Controls.Add(highbackoffhigh);
			layout.Controls.Add(new Label() { Text = "Backoff average CPU%", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			var highbackoffavg = new NumericUpDown() { Maximum = 95, Minimum = 5, Value = 50 };
			highbackoffavg.Value = Convert.ToDecimal(AutoAdjust.High.Backoff.Avg).Constrain(5, 95);
			layout.Controls.Add(highbackoffavg);
			layout.Controls.Add(new Label() { Text = "Backoff low CPU%", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			var highbackofflow = new NumericUpDown() { Maximum = 95, Minimum = 5, Value = 50 };
			highbackofflow.Value = Convert.ToDecimal(AutoAdjust.High.Backoff.Low).Constrain(5, 95);
			layout.Controls.Add(highbackofflow);

			layout.Controls.Add(new Label() { Text = "Backoff level", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			var highbackofflevel = new NumericUpDown() { Maximum = 95, Minimum = 5, Value = 50 };
			highbackofflevel.Value = Convert.ToDecimal(AutoAdjust.High.Backoff.Level).Constrain(5, 95);
			layout.Controls.Add(highbackofflevel);

			var lowmode = new ComboBox()
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				AutoCompleteSource = AutoCompleteSource.ListItems,
				AutoCompleteMode = AutoCompleteMode.SuggestAppend,
				AutoSize = true,
			};
			lowmode.Items.AddRange(powermodes);
			lowmode.SelectedIndex = AutoAdjust.Low.Mode == PowerMode.Balanced ? 1 : AutoAdjust.Low.Mode == PowerMode.PowerSaver ? 2 : 0;

			layout.Controls.Add(new Label() { Text = "Low mode", TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
			layout.Controls.Add(lowmode);

			layout.Controls.Add(new Label() { Text = "Commit CPU% threshold", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			var lowcommitthreshold = new NumericUpDown() { Maximum = 95, Minimum = 5, Value = 50 };
			lowcommitthreshold.Value = Convert.ToDecimal(AutoAdjust.Low.Commit.Threshold).Constrain(5, 95);
			layout.Controls.Add(lowcommitthreshold);
			layout.Controls.Add(new Label() { Text = "Commit level", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			var lowcommitlevel = new NumericUpDown() { Maximum = 15, Minimum = 0, Value = 3 };
			lowcommitlevel.Value = AutoAdjust.Low.Commit.Level.Constrain(0, 15);
			layout.Controls.Add(lowcommitlevel);
			layout.Controls.Add(new Label() { Text = "Backoff high CPU%", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			var lowbackoffhigh = new NumericUpDown() { Maximum = 95, Minimum = 5, Value = 50 };
			lowbackoffhigh.Value = Convert.ToDecimal(AutoAdjust.Low.Backoff.High).Constrain(5, 95);
			layout.Controls.Add(lowbackoffhigh);
			layout.Controls.Add(new Label() { Text = "Backoff average CPU%", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			var lowbackoffavg = new NumericUpDown() { Maximum = 95, Minimum = 5, Value = 50 };
			lowbackoffavg.Value = Convert.ToDecimal(AutoAdjust.Low.Backoff.Avg).Constrain(5, 95);
			layout.Controls.Add(lowbackoffavg);
			layout.Controls.Add(new Label() { Text = "Backoff low CPU%", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			var lowbackofflow = new NumericUpDown() { Maximum = 95, Minimum = 5, Value = 50 };
			lowbackofflow.Value = Convert.ToDecimal(AutoAdjust.Low.Backoff.Low).Constrain(5, 95);
			layout.Controls.Add(lowbackofflow);
			layout.Controls.Add(new Label() { Text = "Backoff level", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			var lowbackofflevel = new NumericUpDown() { Maximum = 95, Minimum = 5, Value = 50 };
			lowbackofflevel.Value = Convert.ToDecimal(AutoAdjust.Low.Backoff.Level).Constrain(5, 95);
			layout.Controls.Add(lowbackofflevel);

			var savebutton = new Button() { Text = "Save", Anchor = AnchorStyles.Right };
			savebutton.Click += (sender, e) =>
			{
				DialogResult = DialogResult.OK;

				newAutoAdjust = new AutoAdjustSettings();

				newAutoAdjust.DefaultMode = PowerManager.GetModeByName(defaultmode.Text);

				newAutoAdjust.Low.Mode = PowerManager.GetModeByName(lowmode.Text);
				newAutoAdjust.High.Mode = PowerManager.GetModeByName(highmode.Text);

				newAutoAdjust.High.Commit.Level = Convert.ToInt32(highcommitlevel.Value);
				newAutoAdjust.High.Commit.Threshold = Convert.ToSingle(highcommitthreshold.Value);

				newAutoAdjust.High.Backoff.Level = Convert.ToInt32(highbackofflevel.Value);
				newAutoAdjust.High.Backoff.Low = Convert.ToSingle(highbackofflow.Value);
				newAutoAdjust.High.Backoff.Avg = Convert.ToSingle(highbackoffavg.Value);
				newAutoAdjust.High.Backoff.High = Convert.ToSingle(highbackoffhigh.Value);

				newAutoAdjust.Low.Commit.Level = Convert.ToInt32(lowcommitlevel.Value);
				newAutoAdjust.Low.Commit.Threshold = Convert.ToSingle(lowcommitthreshold.Value);

				newAutoAdjust.Low.Backoff.Level = Convert.ToInt32(lowbackofflevel.Value);
				newAutoAdjust.Low.Backoff.Low = Convert.ToSingle(lowbackofflow.Value);
				newAutoAdjust.Low.Backoff.Avg = Convert.ToSingle(lowbackoffavg.Value);
				newAutoAdjust.Low.Backoff.High = Convert.ToSingle(lowbackoffhigh.Value);

				Close();
			};
			var cancelbutton = new Button() { Text = "Cancel", Anchor = AnchorStyles.Left };
			cancelbutton.Click += (sender, e) =>
			{
				DialogResult = DialogResult.Cancel;
				Close();
			};
			cancelbutton.NotifyDefault(true);
			UpdateDefaultButton();
			layout.Controls.Add(savebutton);
			layout.Controls.Add(cancelbutton);

			Controls.Add(layout);

			AutoSizeMode = AutoSizeMode.GrowAndShrink;

			// Height = layout.Height;
			// Width = layout.Width;
		}

		static PowerConfigWindow pcw = null;
		static int PowerConfigVisible = 0;
		public static async Task ShowPowerConfig()
		{
			//await Task.Delay(0);

			try
			{
				if (Atomic.Lock(ref PowerConfigVisible))
				{
					try
					{
						using (pcw = new PowerConfigWindow())
						{

							var res = pcw.ShowDialog();
							if (pcw.DialogResult == DialogResult.OK)
							{
								Taskmaster.powermanager.AutoAdjust = pcw.newAutoAdjust;
								Log.Information("<<UI>> Power auto-adjust config changed.");
								// TODO: Call reset on power manager?
							}
							else
							{
								if (Taskmaster.Trace) Log.Verbose("<<UI>> Power auto-adjust config cancelled.");
							}
						}
						pcw = null;
					}
					finally
					{
						Atomic.Unlock(ref PowerConfigVisible);
					}
				}
				else
				{
					pcw.BringToFront();
					pcw.Show();
					pcw.TopMost = true;
					pcw.TopMost = false;
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}
	}
}