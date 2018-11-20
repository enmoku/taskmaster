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
using System.Windows.Forms;
using Serilog;
using Taskmaster.PowerInfo;

namespace Taskmaster
{
	sealed public class PowerConfigWindow : UI.UniForm
	{
		public AutoAdjustSettings oldAutoAdjust = null;
		public AutoAdjustSettings newAutoAdjust = null;
		PowerManager power = null;

		public PowerManager.PowerBehaviour NewBehaviour = PowerManager.PowerBehaviour.Undefined;
		public PowerManager.RestoreModeMethod NewRestoreMethod = PowerManager.RestoreModeMethod.Default;
		public PowerMode NewRestoreMode = PowerMode.Undefined;

		public PowerConfigWindow(ComponentContainer components)
		{
			Text = "Power Configuration";

			var AutoAdjust = oldAutoAdjust = components.powermanager.AutoAdjust;
			power = components.powermanager;

			FormBorderStyle = FormBorderStyle.FixedDialog;

			WindowState = FormWindowState.Normal;
			FormBorderStyle = FormBorderStyle.FixedDialog; // no min/max buttons as wanted
			AutoSizeMode = AutoSizeMode.GrowAndShrink;

			var tooltip = new ToolTip();

			string[] powermodes = new string[] {
				PowerManager.GetModeName(PowerMode.HighPerformance), PowerManager.GetModeName(PowerMode.Balanced), PowerManager.GetModeName(PowerMode.PowerSaver)
			};

			var boldfont = new System.Drawing.Font(Font, System.Drawing.FontStyle.Bold);

			var layout = new TableLayoutPanel()
			{
				ColumnCount = 2,
				AutoSize = true,
			};

			// NORMAL POWER SETTINGS

			layout.Controls.Add(new Label() { Text = "Basic Settings", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Font = boldfont });
			layout.Controls.Add(new Label()); // empty

			var behaviour = new ComboBox()
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				Items = { "Auto-Adjust", "Rule-based", "Manual" },
				SelectedIndex = 2,
			};
			tooltip.SetToolTip(behaviour,
				"Auto-adjust = As per auto-adjust behaviour and watchlist rules\n" +
				"Rule-based = Watchlist rules\n" +
				"Manual = Fully user controlled");

			NewBehaviour = power.Behaviour;
			switch (power.Behaviour)
			{
				case PowerManager.PowerBehaviour.Auto:
				default:
					behaviour.SelectedIndex = 0;
					break;
				case PowerManager.PowerBehaviour.RuleBased:
					behaviour.SelectedIndex = 1;
					break;
				case PowerManager.PowerBehaviour.Manual:
					behaviour.SelectedIndex = 2;
					break;
			}

			behaviour.SelectedIndexChanged += (s, e) =>
			{
				switch (behaviour.SelectedIndex)
				{
					case 0:
						NewBehaviour = PowerManager.PowerBehaviour.Auto;
						break;
					default:
					case 1:
						NewBehaviour = PowerManager.PowerBehaviour.RuleBased;
						break;
					case 2:
						NewBehaviour = PowerManager.PowerBehaviour.Manual;
						break;
				}
			};

			layout.Controls.Add(new Label() { Text = "Launch behaviour", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			layout.Controls.Add(behaviour);

			var restore = new ComboBox()
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				Items = { "Original", "Default", "Saved", powermodes[0], powermodes[1], powermodes[2] },
				SelectedIndex = 2,
			};

			switch (power.RestoreMethod)
			{
				default:
				case PowerManager.RestoreModeMethod.Default:
					NewRestoreMethod = PowerManager.RestoreModeMethod.Default;
					NewRestoreMode = PowerMode.Undefined;
					restore.SelectedIndex = 1;
					break;
				case PowerManager.RestoreModeMethod.Original:
					NewRestoreMethod = PowerManager.RestoreModeMethod.Original;
					NewRestoreMode = PowerMode.Undefined;
					restore.SelectedIndex = 0;
					break;
				case PowerManager.RestoreModeMethod.Saved:
					NewRestoreMethod = PowerManager.RestoreModeMethod.Saved;
					NewRestoreMode = PowerMode.Undefined;
					restore.SelectedIndex = 2;
					break;
				case PowerManager.RestoreModeMethod.Custom:
					NewRestoreMethod = PowerManager.RestoreModeMethod.Custom;
					NewRestoreMode = power.RestoreMode;
					switch (power.RestoreMode)
					{
						case PowerMode.HighPerformance:
							restore.SelectedIndex = 3;
							break;
						default:
						case PowerMode.Balanced:
							restore.SelectedIndex = 4;
							break;
						case PowerMode.PowerSaver:
							restore.SelectedIndex = 5;
							break;
					}
					break;
			}

			tooltip.SetToolTip(restore,
				"Which power mode to restore when cancelling rule-based power modes.\n\n" +
				"Original = the mode thatw was detected on launch\n" +
				"Default = auto-adjust default mode [Default]\n" +
				"Saved = as detected before new mode is set");

			// "Original", "Default", "Saved", powermodes[0], powermodes[1], powermodes[2] },
			restore.SelectedIndexChanged += (s, e) =>
			{
				switch (restore.SelectedIndex)
				{
					case 0:
						NewRestoreMethod = PowerManager.RestoreModeMethod.Original;
						NewRestoreMode = PowerMode.Undefined;
						break;
					default:
					case 1:
						NewRestoreMethod = PowerManager.RestoreModeMethod.Default;
						NewRestoreMode = PowerMode.Undefined;
						break;
					case 2:
						NewRestoreMethod = PowerManager.RestoreModeMethod.Saved;
						NewRestoreMode = PowerMode.Undefined;
						break;
					case 3:
						NewRestoreMethod = PowerManager.RestoreModeMethod.Custom;
						NewRestoreMode = PowerMode.HighPerformance;
						break;
					case 4:
						NewRestoreMethod = PowerManager.RestoreModeMethod.Custom;
						NewRestoreMode = PowerMode.Balanced;
						break;
					case 5:
						NewRestoreMethod = PowerManager.RestoreModeMethod.Custom;
						NewRestoreMode = PowerMode.PowerSaver;
						break;
				}
			};

			layout.Controls.Add(new Label() { Text = "Restore method", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			layout.Controls.Add(restore);

			// AUTO-ADJUST

			var defaultmode = new ComboBox()
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				AutoCompleteSource = AutoCompleteSource.ListItems,
				AutoCompleteMode = AutoCompleteMode.SuggestAppend,
				AutoSize = true,
				Dock = DockStyle.Fill,
			};

			defaultmode.Items.AddRange(powermodes);
			defaultmode.SelectedIndex = AutoAdjust.DefaultMode == PowerMode.Balanced ? 1 : AutoAdjust.DefaultMode == PowerMode.PowerSaver ? 2 : 0;

			layout.Controls.Add(new Label() { Text = "Auto-Adjust", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Font = boldfont });

			layout.Controls.Add(new Label()); // empty

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
			var highcommitthreshold = new Extensions.NumericUpDownEx() { Unit = "%", Maximum = 95, Minimum = 5, Value = 50 };
			highcommitthreshold.Value = Convert.ToDecimal(AutoAdjust.High.Commit.Threshold).Constrain(5, 95);
			layout.Controls.Add(highcommitthreshold);
			layout.Controls.Add(new Label() { Text = "Commit level", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			var highcommitlevel = new NumericUpDown() { Maximum = 15, Minimum = 0, Value = 3 };
			highcommitlevel.Value = AutoAdjust.High.Commit.Level.Constrain(0, 15);
			layout.Controls.Add(highcommitlevel);
			layout.Controls.Add(new Label() { Text = "Backoff high CPU%", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			var highbackoffhigh = new Extensions.NumericUpDownEx() { Unit = "%", Maximum = 95, Minimum = 5, Value = 50 };
			highbackoffhigh.Value = Convert.ToDecimal(AutoAdjust.High.Backoff.High).Constrain(5, 95);
			layout.Controls.Add(highbackoffhigh);
			layout.Controls.Add(new Label() { Text = "Backoff average CPU%", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			var highbackoffavg = new Extensions.NumericUpDownEx() { Unit = "%", Maximum = 95, Minimum = 5, Value = 50 };
			highbackoffavg.Value = Convert.ToDecimal(AutoAdjust.High.Backoff.Avg).Constrain(5, 95);
			layout.Controls.Add(highbackoffavg);
			layout.Controls.Add(new Label() { Text = "Backoff low CPU%", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			var highbackofflow = new Extensions.NumericUpDownEx() { Unit = "%", Maximum = 95, Minimum = 5, Value = 50 };
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
			var lowcommitthreshold = new Extensions.NumericUpDownEx() { Unit = "%", Maximum = 95, Minimum = 5, Value = 50 };
			lowcommitthreshold.Value = Convert.ToDecimal(AutoAdjust.Low.Commit.Threshold).Constrain(5, 95);
			layout.Controls.Add(lowcommitthreshold);
			layout.Controls.Add(new Label() { Text = "Commit level", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			var lowcommitlevel = new NumericUpDown() { Maximum = 15, Minimum = 0, Value = 3 };
			lowcommitlevel.Value = AutoAdjust.Low.Commit.Level.Constrain(0, 15);
			layout.Controls.Add(lowcommitlevel);
			layout.Controls.Add(new Label() { Text = "Backoff high CPU%", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			var lowbackoffhigh = new Extensions.NumericUpDownEx() { Unit = "%", Maximum = 95, Minimum = 5, Value = 50 };
			lowbackoffhigh.Value = Convert.ToDecimal(AutoAdjust.Low.Backoff.High).Constrain(5, 95);
			layout.Controls.Add(lowbackoffhigh);
			layout.Controls.Add(new Label() { Text = "Backoff average CPU%", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			var lowbackoffavg = new Extensions.NumericUpDownEx() { Unit = "%", Maximum = 95, Minimum = 5, Value = 50 };
			lowbackoffavg.Value = Convert.ToDecimal(AutoAdjust.Low.Backoff.Avg).Constrain(5, 95);
			layout.Controls.Add(lowbackoffavg);
			layout.Controls.Add(new Label() { Text = "Backoff low CPU%", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			var lowbackofflow = new Extensions.NumericUpDownEx() { Unit = "%", Maximum = 95, Minimum = 5, Value = 50 };
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

				// TODO: Sanity check the settings

				newAutoAdjust = new AutoAdjustSettings
				{
					DefaultMode = PowerManager.GetModeByName(defaultmode.Text),
					Low =
					{
						Mode = PowerManager.GetModeByName(lowmode.Text),
						Commit =
						{
							Level = Convert.ToInt32(lowcommitlevel.Value),
							Threshold = Convert.ToSingle(lowcommitthreshold.Value),
						},
						Backoff =
						{
							High=Convert.ToSingle(lowbackoffhigh.Value),
							Avg = Convert.ToSingle(lowbackoffavg.Value),
							Low = Convert.ToSingle(lowbackofflow.Value),
							Level = Convert.ToInt32(lowbackofflevel.Value),
						}
					},
					High =
					{
						Mode = PowerManager.GetModeByName(highmode.Text),
						Commit =
						{
							Level =Convert.ToInt32(highcommitlevel.Value),
							Threshold = Convert.ToSingle(highcommitthreshold.Value),
						},
						Backoff =
						{
							High = Convert.ToSingle(highbackoffhigh.Value),
							Avg = Convert.ToSingle(highbackoffavg.Value),
							Low = Convert.ToSingle(highbackofflow.Value),
							Level = Convert.ToInt32(highbackofflevel.Value),
						}
					}
				};

				// passing the new config is done elsewhere

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
		static object PowerConfig_lock = new object();

		public static void Show()
		{
			//await Task.Delay(0);
			if (pcw != null)
			{
				try
				{
					pcw?.BringToFront();
					return;
				}
				catch { } // don't care, if the above fails, the window doesn't exist and we can follow through after
			}

			lock (PowerConfig_lock)
			{
				try
				{
					// this is really horrifying mess
					var power = Taskmaster.Components.powermanager;
					using (pcw = new PowerConfigWindow(Taskmaster.Components))
					{
						var res = pcw.ShowDialog();
						if (pcw.DialogResult == DialogResult.OK)
						{
							power.SetAutoAdjust(pcw.newAutoAdjust);

							power.SetRestoreMode(pcw.NewRestoreMethod, pcw.NewRestoreMode);
							power.SetBehaviour(pcw.NewBehaviour);
							power.SaveNeeded(behaviour:true);

							Log.Information("<<UI>> Power config changed.");
						}
						else
						{
							if (Taskmaster.Trace) Log.Verbose("<<UI>> Power config cancelled.");
						}
					}
				}
				catch { } // finally might not be executed otherwise
				finally
				{
					pcw?.Dispose();
					pcw = null;
				}
			}
		}
	}
}