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

using MKAh;
using Serilog;
using System;
using System.Windows.Forms;
using Taskmaster.Power;

namespace Taskmaster.UI.Config
{
	public class PowerConfigWindow : UniForm
	{
		readonly public AutoAdjustSettings oldAutoAdjust;

		public Power.PowerBehaviour NewLaunchBehaviour = Power.PowerBehaviour.Undefined;
		public Power.RestoreModeMethod NewRestoreMethod = Power.RestoreModeMethod.Default;
		public Mode NewRestoreMode = Mode.Undefined;
		public Mode NewLockMode = Mode.Undefined;

		readonly ComboBox behaviour, restore;

		readonly ComboBox defaultmode, highmode, lowmode;
		readonly Extensions.NumericUpDownEx highcommitthreshold, highbackoffhigh, highbackoffmean, highbackofflow;
		readonly NumericUpDown highcommitlevel, highbackofflevel;
		readonly Extensions.NumericUpDownEx lowcommitthreshold, lowbackoffhigh, lowbackoffmean, lowbackofflow;
		readonly NumericUpDown lowcommitlevel, lowbackofflevel;

		readonly NumericUpDown loQueue, hiQueue;

		bool MonitorPowerOff = false;
		readonly ComboBox monitoroffmode;
		readonly CheckBox monitorofftoggle;

		readonly Power.Manager manager;

		public PowerConfigWindow(Power.Manager powerManager, bool center = false)
			: base(centerOnScreen: center)
		{
			SuspendLayout();

			manager = powerManager;

			Text = "Power Configuration";

			var AutoAdjust = oldAutoAdjust = manager.AutoAdjust;

			FormBorderStyle = FormBorderStyle.FixedDialog;

			WindowState = FormWindowState.Normal;
			FormBorderStyle = FormBorderStyle.FixedDialog; // no min/max buttons as wanted
			AutoSizeMode = AutoSizeMode.GrowAndShrink;

			var tooltip = new ToolTip();

			string[] powermodes = new string[] {
				Power.Utility.GetModeName(Mode.HighPerformance), Power.Utility.GetModeName(Mode.Balanced), Power.Utility.GetModeName(Mode.PowerSaver)
			};

			var layout = new Extensions.TableLayoutPanel()
			{
				ColumnCount = 2,
				AutoSize = true,
			};

			layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

			//layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
			//layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize, 120));

			// NORMAL POWER SETTINGS

			layout.Controls.Add(new Extensions.Label() { Text = "Basic Settings", Font = BoldFont, Dock = DockStyle.Fill });
			layout.Controls.Add(new EmptySpace());

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

			behaviour.SelectedIndexChanged += (_, _ea) =>
			{
				switch (behaviour.SelectedIndex)
				{
					case 0:
						NewLaunchBehaviour = Power.PowerBehaviour.Auto;
						break;
					default:
					case 1:
						NewLaunchBehaviour = Power.PowerBehaviour.RuleBased;
						break;
					case 2:
						NewLaunchBehaviour = Power.PowerBehaviour.Manual;
						break;
				}
			};

			layout.Controls.Add(new Extensions.Label() { Text = "Launch behaviour", Dock = DockStyle.Fill });
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
			restore.SelectedIndexChanged += (_, _ea) =>
			{
				switch (restore.SelectedIndex)
				{
					case 0:
						NewRestoreMethod = Power.RestoreModeMethod.Original;
						NewRestoreMode = Mode.Undefined;
						break;
					default:
					case 1:
						NewRestoreMethod = Power.RestoreModeMethod.Default;
						NewRestoreMode = Mode.Undefined;
						break;
					case 2:
						NewRestoreMethod = Power.RestoreModeMethod.Saved;
						NewRestoreMode = Mode.Undefined;
						break;
					case 3:
						NewRestoreMethod = Power.RestoreModeMethod.Custom;
						NewRestoreMode = Mode.HighPerformance;
						break;
					case 4:
						NewRestoreMethod = Power.RestoreModeMethod.Custom;
						NewRestoreMode = Mode.Balanced;
						break;
					case 5:
						NewRestoreMethod = Power.RestoreModeMethod.Custom;
						NewRestoreMode = Mode.PowerSaver;
						break;
				}
			};

			layout.Controls.Add(new Extensions.Label() { Text = "Restore method" });
			layout.Controls.Add(restore);

			// SESSION / MONITOR stuff

			layout.Controls.Add(new Extensions.Label() { Text = "Monitor off", Font = BoldFont });
			layout.Controls.Add(new EmptySpace());

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

			layout.Controls.Add(new Extensions.Label() { Text = HumanReadable.Hardware.Power.Mode });
			layout.Controls.Add(monitoroffmode);

			tooltip.SetToolTip(monitoroffmode, "Power mode to set when monitor is off.");

			MonitorPowerOff = manager.SessionLockPowerOff;
			monitorofftoggle = new CheckBox()
			{
				Dock = DockStyle.Left,
			};

			layout.Controls.Add(new Extensions.Label() { Text = "Session lock" });
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

			layout.Controls.Add(new Extensions.Label() { Text = HumanReadable.Hardware.Power.AutoAdjust, Font = BoldFont });
			layout.Controls.Add(new EmptySpace());

			layout.Controls.Add(new Extensions.Label() { Text = "Sample frequency (sec)" });
			layout.Controls.Add(new Extensions.Label() { Text = $"{Application.cpumonitor.SampleInterval.TotalSeconds:N1}" });

			layout.Controls.Add(new Extensions.Label() { Text = "Default mode" });
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

			layout.Controls.Add(new Extensions.Label() { Text = "High mode", Font = BoldFont });
			layout.Controls.Add(highmode);

			layout.Controls.Add(new Extensions.Label() { Text = "Commit CPU% threshold" });
			highcommitthreshold = new Extensions.NumericUpDownEx() { Unit = "%", Maximum = 95, Minimum = 5, Value = 50 };
			tooltip.SetToolTip(highcommitthreshold, "Low CPU use % that must be maintained for this operation mode to be enacted.");
			layout.Controls.Add(highcommitthreshold);
			layout.Controls.Add(new Extensions.Label() { Text = "Commit level" });
			highcommitlevel = new NumericUpDown() { Maximum = 15, Minimum = 0, Value = 3 };
			tooltip.SetToolTip(highcommitlevel, "How many consequent samples must match threshold to commit to it.");
			layout.Controls.Add(highcommitlevel);
			layout.Controls.Add(new Extensions.Label() { Text = "Backoff high CPU%" });
			highbackoffhigh = new Extensions.NumericUpDownEx() { Unit = "%", Maximum = 95, Minimum = 5, Value = 50 };
			layout.Controls.Add(highbackoffhigh);
			layout.Controls.Add(new Extensions.Label() { Text = "Backoff average CPU%" });
			highbackoffmean = new Extensions.NumericUpDownEx() { Unit = "%", Maximum = 95, Minimum = 5, Value = 50 };
			layout.Controls.Add(highbackoffmean);
			layout.Controls.Add(new Extensions.Label() { Text = "Backoff low CPU%" });
			highbackofflow = new Extensions.NumericUpDownEx() { Unit = "%", Maximum = 95, Minimum = 5, Value = 50 };
			layout.Controls.Add(highbackofflow);

			layout.Controls.Add(new Extensions.Label() { Text = "Backoff level" });
			highbackofflevel = new NumericUpDown() { Maximum = 10, Minimum = 1, Value = 5 };
			layout.Controls.Add(highbackofflevel);

			layout.Controls.Add(new Extensions.Label() { Text = "Queue barrier" });
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

			layout.Controls.Add(new Extensions.Label() { Text = "Low mode", Font = BoldFont });
			layout.Controls.Add(lowmode);
			layout.Controls.Add(new Extensions.Label() { Text = "Commit CPU% threshold" });
			lowcommitthreshold = new Extensions.NumericUpDownEx() { Unit = "%", Maximum = 95, Minimum = 5, Value = 50 };
			tooltip.SetToolTip(lowcommitthreshold, "High CPU use % that must not be maintained for this operation mode to be enacted.");
			layout.Controls.Add(lowcommitthreshold);
			layout.Controls.Add(new Extensions.Label() { Text = "Commit level" });
			lowcommitlevel = new NumericUpDown() { Maximum = 15, Minimum = 0, Value = 3 };
			tooltip.SetToolTip(lowcommitlevel, "How many consequent samples must match threshold to commit to it.");
			layout.Controls.Add(lowcommitlevel);
			layout.Controls.Add(new Extensions.Label() { Text = "Backoff high CPU%" });
			lowbackoffhigh = new Extensions.NumericUpDownEx() { Unit = "%", Maximum = 95, Minimum = 5, Value = 50 };
			layout.Controls.Add(lowbackoffhigh);
			layout.Controls.Add(new Extensions.Label() { Text = "Backoff average CPU%" });
			lowbackoffmean = new Extensions.NumericUpDownEx() { Unit = "%", Maximum = 95, Minimum = 5, Value = 50 };
			layout.Controls.Add(lowbackoffmean);
			layout.Controls.Add(new Extensions.Label() { Text = "Backoff low CPU%" });
			lowbackofflow = new Extensions.NumericUpDownEx() { Unit = "%", Maximum = 95, Minimum = 5, Value = 50 };
			layout.Controls.Add(lowbackofflow);
			layout.Controls.Add(new Extensions.Label() { Text = "Backoff level" });
			lowbackofflevel = new NumericUpDown() { Maximum = 10, Minimum = 1, Value = 5 };
			layout.Controls.Add(lowbackofflevel);

			layout.Controls.Add(new Extensions.Label() { Text = "Queue barrier" });
			loQueue = new NumericUpDown() { Maximum = 100, Minimum = 0, Value = 5 };
			layout.Controls.Add(loQueue);
			tooltip.SetToolTip(loQueue, "If there are at least this many queued threads, low mode is disallowed.");

			var savebutton = new Extensions.Button() { Text = "Save", Anchor = AnchorStyles.Right };
			savebutton.Click += Save;
			var cancelbutton = new Extensions.Button() { Text = "Cancel", Anchor = AnchorStyles.Left };
			cancelbutton.Click += Cancel;
			savebutton.NotifyDefault(true);
			cancelbutton.NotifyDefault(false);
			UpdateDefaultButton();
			var resetbutton = new Extensions.Button() { Text = "Reset", Anchor = AnchorStyles.Right };
			resetbutton.Click += Reset;

			layout.Controls.Add(savebutton);
			layout.Controls.Add(cancelbutton);
			layout.Controls.Add(resetbutton);

			Controls.Add(layout);

			AutoSizeMode = AutoSizeMode.GrowAndShrink;

			// Height = layout.Height;
			// Width = layout.Width;

			FillAutoAdjust(AutoAdjust);

			ResumeLayout(performLayout: false);
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

			var newAutoAdjust = new AutoAdjustSettings
			{
				DefaultMode = Power.Utility.GetModeByName(defaultmode.Text),
				Low =
					{
						Mode = Power.Utility.GetModeByName(lowmode.Text),
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
						Mode = Power.Utility.GetModeByName(highmode.Text),
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

			NewLockMode = monitoroffmode.SelectedIndex switch
			{
				//0 => Mode.Undefined,
				1 => Mode.HighPerformance,
				2 => Mode.Balanced,
				3 => Mode.PowerSaver,
				_ => Mode.Undefined,
			};

			MonitorPowerOff = monitorofftoggle.Checked;

			manager.SetAutoAdjust(newAutoAdjust);

			manager.LaunchBehaviour = NewLaunchBehaviour;

			manager.SessionLockPowerOff = MonitorPowerOff;
			manager.SessionLockPowerMode = NewLockMode;

			manager.SetRestoreMode(NewRestoreMethod, NewRestoreMode);

			manager.SaveConfig();

			Log.Information("<<UI>> Power config changed.");

			Close();
		}

		void Reset(object _, EventArgs _ea)
		{
			NewLaunchBehaviour = Power.PowerBehaviour.RuleBased;
			NewRestoreMethod = Power.RestoreModeMethod.Default;
			NewRestoreMode = Mode.Balanced;
			monitorofftoggle.Checked = true;
			monitoroffmode.SelectedIndex = 3; // power saver

			var preset = PowerAutoadjustPresets.Default();
			FillAutoAdjust(preset);
		}

		void FillAutoAdjust(AutoAdjustSettings AutoAdjust)
		{
			NewLaunchBehaviour = manager.LaunchBehaviour;
			switch (manager.LaunchBehaviour)
			{
				case Power.PowerBehaviour.Auto:
				default:
					behaviour.SelectedIndex = 0;
					break;
				case Power.PowerBehaviour.RuleBased:
					behaviour.SelectedIndex = 1;
					break;
				case Power.PowerBehaviour.Manual:
					behaviour.SelectedIndex = 2;
					break;
			}

			NewRestoreMethod = manager.RestoreMethod;
			NewRestoreMode = manager.RestoreMode;
			switch (NewRestoreMethod)
			{
				default:
				case Power.RestoreModeMethod.Default:
					NewRestoreMethod = Power.RestoreModeMethod.Default;
					NewRestoreMode = Mode.Undefined;
					restore.SelectedIndex = 1;
					break;
				case Power.RestoreModeMethod.Original:
					NewRestoreMethod = Power.RestoreModeMethod.Original;
					NewRestoreMode = Mode.Undefined;
					restore.SelectedIndex = 0;
					break;
				case Power.RestoreModeMethod.Saved:
					NewRestoreMethod = Power.RestoreModeMethod.Saved;
					NewRestoreMode = Mode.Undefined;
					restore.SelectedIndex = 2;
					break;
				case Power.RestoreModeMethod.Custom:
					NewRestoreMethod = Power.RestoreModeMethod.Custom;
					NewRestoreMode = manager.RestoreMode;
					restore.SelectedIndex = manager.RestoreMode switch
					{
						Mode.HighPerformance => 3,
						Mode.Balanced => 4,
						Mode.PowerSaver => 5,
						_ => 4,
					};
					break;
			}

			var SessionLockPowerMode = manager.SessionLockPowerMode;

			monitoroffmode.SelectedIndex = SessionLockPowerMode switch
			{
				Mode.HighPerformance => 1,
				Mode.Balanced => 2,
				Mode.PowerSaver => 3,
				_ => 0,
			};

			monitorofftoggle.Checked = MonitorPowerOff;

			defaultmode.SelectedIndex = AutoAdjust.DefaultMode == Mode.Balanced ? 1 : AutoAdjust.DefaultMode == Mode.PowerSaver ? 2 : 0;

			highmode.SelectedIndex = AutoAdjust.High.Mode == Mode.Balanced ? 1 : AutoAdjust.High.Mode == Mode.PowerSaver ? 2 : 0;
			highcommitthreshold.Value = Convert.ToDecimal(AutoAdjust.High.Commit.Threshold).Constrain(5, 95);
			highcommitlevel.Value = AutoAdjust.High.Commit.Level.Constrain(0, 15);
			highbackoffhigh.Value = Convert.ToDecimal(AutoAdjust.High.Backoff.High).Constrain(5, 95);
			highbackoffmean.Value = Convert.ToDecimal(AutoAdjust.High.Backoff.Mean).Constrain(5, 95);
			highbackofflow.Value = Convert.ToDecimal(AutoAdjust.High.Backoff.Low).Constrain(5, 95);
			highbackofflevel.Value = Convert.ToDecimal(AutoAdjust.High.Backoff.Level).Constrain(1, 100);
			hiQueue.Value = Convert.ToDecimal(AutoAdjust.Queue.High).Constrain(0, 100);

			lowmode.SelectedIndex = AutoAdjust.Low.Mode == Mode.Balanced ? 1 : AutoAdjust.Low.Mode == Mode.PowerSaver ? 2 : 0;
			lowcommitthreshold.Value = Convert.ToDecimal(AutoAdjust.Low.Commit.Threshold).Constrain(5, 95);
			lowcommitlevel.Value = AutoAdjust.Low.Commit.Level.Constrain(0, 15);
			lowbackoffhigh.Value = Convert.ToDecimal(AutoAdjust.Low.Backoff.High).Constrain(5, 95);
			lowbackoffmean.Value = Convert.ToDecimal(AutoAdjust.Low.Backoff.Mean).Constrain(5, 95);
			lowbackofflow.Value = Convert.ToDecimal(AutoAdjust.Low.Backoff.Low).Constrain(5, 95);
			lowbackofflevel.Value = Convert.ToDecimal(AutoAdjust.Low.Backoff.Level).Constrain(1, 100);
			loQueue.Value = Convert.ToDecimal(AutoAdjust.Queue.Low).Constrain(0, 100);
		}

		public static void Reveal(Power.Manager powerManager, bool centerOnScreen = false)
		{
			try
			{
				using var pcw = new PowerConfigWindow(powerManager, centerOnScreen);
				/* var res = */ pcw.ShowDialog();
				if (pcw.DialogOK)
				{
					// NOP
				}
				else
				{
					if (Application.Trace) Log.Verbose("<<UI>> Power config cancelled.");
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