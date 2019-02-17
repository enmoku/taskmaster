//
// PowerConfigWindow.cs
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
using Serilog;
using Taskmaster.PowerInfo;

namespace Taskmaster.UI
{
	sealed public class PowerConfigWindow : UniForm
	{
		public AutoAdjustSettings oldAutoAdjust = null;
		public AutoAdjustSettings newAutoAdjust = null;
		PowerManager power = null;

		public PowerManager.PowerBehaviour NewLaunchBehaviour = PowerManager.PowerBehaviour.Undefined;
		public PowerManager.RestoreModeMethod NewRestoreMethod = PowerManager.RestoreModeMethod.Default;
		public PowerMode NewRestoreMode = PowerMode.Undefined;
		public PowerMode NewLockMode = PowerMode.Undefined;

		ComboBox behaviour = null, restore = null;

		ComboBox defaultmode = null, highmode = null, lowmode = null;
		Extensions.NumericUpDownEx highcommitthreshold = null, highbackoffhigh = null, highbackoffmean = null, highbackofflow = null;
		NumericUpDown highcommitlevel = null, highbackofflevel = null;
		Extensions.NumericUpDownEx lowcommitthreshold = null, lowbackoffhigh = null, lowbackoffmean = null, lowbackofflow = null;
		NumericUpDown lowcommitlevel = null, lowbackofflevel = null;

		NumericUpDown loQueue = null, hiQueue = null;

		bool MonitorPowerOff = false;
		ComboBox monitoroffmode = null;
		CheckBox monitorofftoggle = null;

		public PowerConfigWindow(bool center = false)
			: base(center)
		{
			Text = "Power Configuration";

			var AutoAdjust = oldAutoAdjust = Taskmaster.powermanager.AutoAdjust;
			power = Taskmaster.powermanager;

			FormBorderStyle = FormBorderStyle.FixedDialog;

			WindowState = FormWindowState.Normal;
			FormBorderStyle = FormBorderStyle.FixedDialog; // no min/max buttons as wanted
			AutoSizeMode = AutoSizeMode.GrowAndShrink;

			var tooltip = new ToolTip();

			string[] powermodes = new string[] {
				PowerManager.GetModeName(PowerMode.HighPerformance), PowerManager.GetModeName(PowerMode.Balanced), PowerManager.GetModeName(PowerMode.PowerSaver)
			};

			var layout = new TableLayoutPanel()
			{
				ColumnCount = 2,
				AutoSize = true,
			};

			//layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
			//layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize, 120));

			// NORMAL POWER SETTINGS

			layout.Controls.Add(new Label() { Text = "Basic Settings", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Font = boldfont, AutoSize = true, Dock = DockStyle.Fill });
			layout.Controls.Add(new Label()); // empty

			behaviour = new ComboBox()
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				Items = { HumanReadable.Hardware.Power.AutoAdjust, HumanReadable.Hardware.Power.RuleBased, HumanReadable.Hardware.Power.Manual },
				SelectedIndex = 2,
				AutoSize = true,
				Dock = DockStyle.Fill,
			};
			tooltip.SetToolTip(behaviour,
				"Auto-adjust = As per auto-adjust behaviour and watchlist rules\n" +
				"Rule-based = Watchlist rules\n" +
				"Manual = Fully user controlled");

			behaviour.SelectedIndexChanged += (s, e) =>
			{
				switch (behaviour.SelectedIndex)
				{
					case 0:
						NewLaunchBehaviour = PowerManager.PowerBehaviour.Auto;
						break;
					default:
					case 1:
						NewLaunchBehaviour = PowerManager.PowerBehaviour.RuleBased;
						break;
					case 2:
						NewLaunchBehaviour = PowerManager.PowerBehaviour.Manual;
						break;
				}
			};

			layout.Controls.Add(new Label() { Text = "Launch behaviour", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Fill });
			layout.Controls.Add(behaviour);

			restore = new ComboBox()
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				Items = { "Original", "Default", "Saved", powermodes[0], powermodes[1], powermodes[2] },
				SelectedIndex = 2,
				AutoSize = true,
				Dock = DockStyle.Fill,
			};

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

			layout.Controls.Add(new Label() { Text = "Restore method", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Fill });
			layout.Controls.Add(restore);

			// SESSION / MONITOR stuff

			layout.Controls.Add(new Label() { Text = "Monitor off", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Font = boldfont, AutoSize = true, Dock = DockStyle.Fill });
			layout.Controls.Add(new Label()); // empty

			monitoroffmode = new ComboBox()
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				AutoCompleteSource = AutoCompleteSource.ListItems,
				AutoCompleteMode = AutoCompleteMode.SuggestAppend,
				AutoSize = true,
				Dock = DockStyle.Fill,
			};

			monitoroffmode.Items.Add(HumanReadable.Generic.Ignore);
			monitoroffmode.Items.AddRange(powermodes);

			layout.Controls.Add(new Label() { Text = HumanReadable.Hardware.Power.Mode, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Fill });
			layout.Controls.Add(monitoroffmode);

			tooltip.SetToolTip(monitoroffmode, "Power mode to set when monitor is off.");

			MonitorPowerOff = power.SessionLockPowerOff;
			monitorofftoggle = new CheckBox()
			{
				Dock = DockStyle.Left,
			};

			layout.Controls.Add(new Label() { Text = "Session lock", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Fill });
			layout.Controls.Add(monitorofftoggle);

			tooltip.SetToolTip(monitorofftoggle, "Power down monitor when session is locked (e.g. WinKey+L).\nNormal power is restored once lock is lifted.");

			// AUTO-ADJUST

			defaultmode = new ComboBox()
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				AutoCompleteSource = AutoCompleteSource.ListItems,
				AutoCompleteMode = AutoCompleteMode.SuggestAppend,
				AutoSize = true,
				Dock = DockStyle.Fill,
			};

			defaultmode.Items.AddRange(powermodes);

			layout.Controls.Add(new Label() { Text = HumanReadable.Hardware.Power.AutoAdjust, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Font = boldfont, AutoSize = true, Dock = DockStyle.Fill });
			layout.Controls.Add(new Label()); // empty

			layout.Controls.Add(new Label() { Text = "Sample frequency (sec)", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Fill });
			layout.Controls.Add(new Label() { Text = $"{Taskmaster.cpumonitor.SampleInterval.TotalSeconds:N1}", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Fill });

			layout.Controls.Add(new Label() { Text = "Default mode", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Fill });
			layout.Controls.Add(defaultmode);
			tooltip.SetToolTip(defaultmode, "The default power mode to use when neither high nor low mode match");

			highmode = new ComboBox()
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				AutoCompleteSource = AutoCompleteSource.ListItems,
				AutoCompleteMode = AutoCompleteMode.SuggestAppend,
				AutoSize = true,
				Dock = DockStyle.Fill,
			};
			highmode.Items.AddRange(powermodes);

			layout.Controls.Add(new Label() { Text = "High mode", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Font = boldfont, AutoSize = true, Dock = DockStyle.Fill });
			layout.Controls.Add(highmode);

			layout.Controls.Add(new Label() { Text = "Commit CPU% threshold", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Fill });
			highcommitthreshold = new Extensions.NumericUpDownEx() { Unit = "%", Maximum = 95, Minimum = 5, Value = 50 };
			tooltip.SetToolTip(highcommitthreshold,"Low CPU use % that must be maintained for this operation mode to be enacted.");
			layout.Controls.Add(highcommitthreshold);
			layout.Controls.Add(new Label() { Text = "Commit level", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Fill });
			highcommitlevel = new NumericUpDown() { Maximum = 15, Minimum = 0, Value = 3 };
			tooltip.SetToolTip(highcommitlevel, "How many consequent samples must match threshold to commit to it.");
			layout.Controls.Add(highcommitlevel);
			layout.Controls.Add(new Label() { Text = "Backoff high CPU%", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Fill });
			highbackoffhigh = new Extensions.NumericUpDownEx() { Unit = "%", Maximum = 95, Minimum = 5, Value = 50 };
			layout.Controls.Add(highbackoffhigh);
			layout.Controls.Add(new Label() { Text = "Backoff average CPU%", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Fill });
			highbackoffmean = new Extensions.NumericUpDownEx() { Unit = "%", Maximum = 95, Minimum = 5, Value = 50 };
			layout.Controls.Add(highbackoffmean);
			layout.Controls.Add(new Label() { Text = "Backoff low CPU%", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Fill });
			highbackofflow = new Extensions.NumericUpDownEx() { Unit = "%", Maximum = 95, Minimum = 5, Value = 50 };
			layout.Controls.Add(highbackofflow);

			layout.Controls.Add(new Label() { Text = "Backoff level", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Fill });
			highbackofflevel = new NumericUpDown() { Maximum = 10, Minimum = 1, Value = 5 };
			layout.Controls.Add(highbackofflevel);

			layout.Controls.Add(new Label() { Text = "Queue barrier", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Fill });
			hiQueue = new NumericUpDown() { Maximum = 100, Minimum = 0, Value = 12 };
			layout.Controls.Add(hiQueue);
			tooltip.SetToolTip(hiQueue, "If there are at least this many queued threads, lower power modes are disallowed.");

			lowmode = new ComboBox()
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				AutoCompleteSource = AutoCompleteSource.ListItems,
				AutoCompleteMode = AutoCompleteMode.SuggestAppend,
				AutoSize = true,
				Dock = DockStyle.Fill,
			};
			lowmode.Items.AddRange(powermodes);

			layout.Controls.Add(new Label() { Text = "Low mode", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Font=boldfont, AutoSize = true, Dock = DockStyle.Fill });
			layout.Controls.Add(lowmode);
			layout.Controls.Add(new Label() { Text = "Commit CPU% threshold", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Fill });
			lowcommitthreshold = new Extensions.NumericUpDownEx() { Unit = "%", Maximum = 95, Minimum = 5, Value = 50 };
			tooltip.SetToolTip(lowcommitthreshold, "High CPU use % that must not be maintained for this operation mode to be enacted.");
			layout.Controls.Add(lowcommitthreshold);
			layout.Controls.Add(new Label() { Text = "Commit level", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Fill });
			lowcommitlevel = new NumericUpDown() { Maximum = 15, Minimum = 0, Value = 3 };
			tooltip.SetToolTip(lowcommitlevel, "How many consequent samples must match threshold to commit to it.");
			layout.Controls.Add(lowcommitlevel);
			layout.Controls.Add(new Label() { Text = "Backoff high CPU%", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Fill });
			lowbackoffhigh = new Extensions.NumericUpDownEx() { Unit = "%", Maximum = 95, Minimum = 5, Value = 50 };
			layout.Controls.Add(lowbackoffhigh);
			layout.Controls.Add(new Label() { Text = "Backoff average CPU%", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Fill });
			lowbackoffmean = new Extensions.NumericUpDownEx() { Unit = "%", Maximum = 95, Minimum = 5, Value = 50 };
			layout.Controls.Add(lowbackoffmean);
			layout.Controls.Add(new Label() { Text = "Backoff low CPU%", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Fill });
			lowbackofflow = new Extensions.NumericUpDownEx() { Unit = "%", Maximum = 95, Minimum = 5, Value = 50 };
			layout.Controls.Add(lowbackofflow);
			layout.Controls.Add(new Label() { Text = "Backoff level", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Fill });
			lowbackofflevel = new NumericUpDown() { Maximum = 10, Minimum = 1, Value = 5 };
			layout.Controls.Add(lowbackofflevel);

			layout.Controls.Add(new Label() { Text = "Queue barrier", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Fill });
			loQueue = new NumericUpDown() { Maximum = 100, Minimum = 0, Value = 5 };
			layout.Controls.Add(loQueue);
			tooltip.SetToolTip(loQueue, "If there are at least this many queued threads, low mode is disallowed.");

			var savebutton = new Button() { Text = "Save", Anchor = AnchorStyles.Right };
			savebutton.Click += Save;
			var cancelbutton = new Button() { Text = "Cancel", Anchor = AnchorStyles.Left };
			cancelbutton.Click += Cancel;
			savebutton.NotifyDefault(true);
			cancelbutton.NotifyDefault(false);
			UpdateDefaultButton();
			var resetbutton = new Button() { Text = "Reset", Anchor = AnchorStyles.Right };
			resetbutton.Click += Reset;

			layout.Controls.Add(savebutton);
			layout.Controls.Add(cancelbutton);
			layout.Controls.Add(resetbutton);

			Controls.Add(layout);

			AutoSizeMode = AutoSizeMode.GrowAndShrink;

			// Height = layout.Height;
			// Width = layout.Width;

			FillAutoAdjust(AutoAdjust);
		}

		void Cancel(object _, EventArgs _ea)
		{
			DialogResult = DialogResult.Cancel;
			Close();
		}

		void Save(object _, EventArgs _ea)
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
							Mean = Convert.ToSingle(lowbackoffmean.Value),
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
							Mean = Convert.ToSingle(highbackoffmean.Value),
							Low = Convert.ToSingle(highbackofflow.Value),
							Level = Convert.ToInt32(highbackofflevel.Value),
						}
					},
				Queue =
					{
						High = Convert.ToInt32(hiQueue.Value),
						Low = Convert.ToInt32(loQueue.Value),
					}
			};

			// passing the new config is done elsewhere

			switch (monitoroffmode.SelectedIndex)
			{
				case 0: // ignore
					NewLockMode = PowerMode.Undefined;
					break;
				case 1: // high
					NewLockMode = PowerMode.HighPerformance;
					break;
				case 2: // balanced
					NewLockMode = PowerMode.Balanced;
					break;
				case 3: // saver
					NewLockMode = PowerMode.PowerSaver;
					break;
			}

			MonitorPowerOff = monitorofftoggle.Checked;

			power.SetAutoAdjust(newAutoAdjust);

			power.LaunchBehaviour = NewLaunchBehaviour;

			power.SessionLockPowerOff = MonitorPowerOff;
			power.SessionLockPowerMode = NewLockMode;

			power.SetRestoreMode(NewRestoreMethod, NewRestoreMode);

			power.SaveConfig();

			Log.Information("<<UI>> Power config changed.");

			Close();
		}

		void Reset(object _, EventArgs _ea)
		{
			NewLaunchBehaviour = PowerManager.PowerBehaviour.RuleBased;
			NewRestoreMethod = PowerManager.RestoreModeMethod.Default;
			NewRestoreMode = PowerMode.Balanced;
			monitorofftoggle.Checked = true;
			monitoroffmode.SelectedIndex = 3; // power saver

			var preset = PowerAutoadjustPresets.Default();
			FillAutoAdjust(preset);
		}

		void FillAutoAdjust(AutoAdjustSettings AutoAdjust)
		{
			NewLaunchBehaviour = power.LaunchBehaviour;
			switch (power.LaunchBehaviour)
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

			NewRestoreMethod = power.RestoreMethod;
			NewRestoreMode = power.RestoreMode;
			switch (NewRestoreMethod)
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

			var SessionLockPowerMode = power.SessionLockPowerMode;
			switch (SessionLockPowerMode)
			{
				default:
					monitoroffmode.SelectedIndex = 0;
					break;
				case PowerMode.HighPerformance:
					monitoroffmode.SelectedIndex = 1;
					break;
				case PowerMode.Balanced:
					monitoroffmode.SelectedIndex = 2;
					break;
				case PowerMode.PowerSaver:
					monitoroffmode.SelectedIndex = 3;
					break;
			}
			monitorofftoggle.Checked = MonitorPowerOff;

			defaultmode.SelectedIndex = AutoAdjust.DefaultMode == PowerMode.Balanced ? 1 : AutoAdjust.DefaultMode == PowerMode.PowerSaver ? 2 : 0;

			highmode.SelectedIndex = AutoAdjust.High.Mode == PowerMode.Balanced ? 1 : AutoAdjust.High.Mode == PowerMode.PowerSaver ? 2 : 0;
			highcommitthreshold.Value = Convert.ToDecimal(AutoAdjust.High.Commit.Threshold).Constrain(5, 95);
			highcommitlevel.Value = AutoAdjust.High.Commit.Level.Constrain(0, 15);
			highbackoffhigh.Value = Convert.ToDecimal(AutoAdjust.High.Backoff.High).Constrain(5, 95);
			highbackoffmean.Value = Convert.ToDecimal(AutoAdjust.High.Backoff.Mean).Constrain(5, 95);
			highbackofflow.Value = Convert.ToDecimal(AutoAdjust.High.Backoff.Low).Constrain(5, 95);
			highbackofflevel.Value = Convert.ToDecimal(AutoAdjust.High.Backoff.Level).Constrain(1, 100);
			hiQueue.Value = Convert.ToDecimal(AutoAdjust.Queue.High).Constrain(0, 100);

			lowmode.SelectedIndex = AutoAdjust.Low.Mode == PowerMode.Balanced ? 1 : AutoAdjust.Low.Mode == PowerMode.PowerSaver ? 2 : 0;
			lowcommitthreshold.Value = Convert.ToDecimal(AutoAdjust.Low.Commit.Threshold).Constrain(5, 95);
			lowcommitlevel.Value = AutoAdjust.Low.Commit.Level.Constrain(0, 15);
			lowbackoffhigh.Value = Convert.ToDecimal(AutoAdjust.Low.Backoff.High).Constrain(5, 95);
			lowbackoffmean.Value = Convert.ToDecimal(AutoAdjust.Low.Backoff.Mean).Constrain(5, 95);
			lowbackofflow.Value = Convert.ToDecimal(AutoAdjust.Low.Backoff.Low).Constrain(5, 95);
			lowbackofflevel.Value = Convert.ToDecimal(AutoAdjust.Low.Backoff.Level).Constrain(1, 100);
			loQueue.Value = Convert.ToDecimal(AutoAdjust.Queue.Low).Constrain(0, 100);
		}

		public static void Reveal(bool centerOnScreen=false)
		{
			try
			{
				using (var pcw = new PowerConfigWindow(centerOnScreen))
				{
					var res = pcw.ShowDialog();
					if (pcw.DialogResult == DialogResult.OK)
					{
						// NOP
					}
					else
					{
						if (Taskmaster.Trace) Log.Verbose("<<UI>> Power config cancelled.");
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