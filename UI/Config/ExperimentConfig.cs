//
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
	public class ExperimentConfig : UniForm
	{
		readonly Button uninstallButton, installButton;
		readonly Extensions.Label imageUptodateState;

		public ExperimentConfig(bool center = false)
			: base(centerOnScreen: center)
		{
			SuspendLayout();

			Text = "Experiment Configuration";
			AutoSizeMode = AutoSizeMode.GrowAndShrink;
			AutoSize = true;

			var tooltip = new ToolTip();

			var layout = new Extensions.TableLayoutPanel()
			{
				ColumnCount = 2,
				Dock = DockStyle.Fill,
				AutoSize = true,
				Parent = this,
			};
			var experimentWarning = new Extensions.Label { Text = "EXPERIMENTAL\nYou've been warned.", AutoSize = true, Font = BoldFont, ForeColor = System.Drawing.Color.Maroon, Padding = BigPadding };
			layout.Controls.Add(experimentWarning);
			layout.SetColumnSpan(experimentWarning, 2);

			// Load configuration

			using var corecfg = Application.Config.Load(Application.CoreConfigFilename);
			var cfg = corecfg.Config;
			var exsec = cfg[Application.Constants.Experimental];

			// EXPERIMENTS

			bool loadertracking = exsec.Get(Application.Constants.LoaderTracking)?.Bool ?? false;

			var toggleLoaderTracking = new CheckBox() { Checked = loadertracking, };

			layout.Controls.Add(new Extensions.Label { Text = "Loader tracking" });
			layout.Controls.Add(toggleLoaderTracking);
			tooltip.SetToolTip(toggleLoaderTracking, "Try to track what processes are overloading the system.");

			var RecordAnalysisDelay = new Extensions.NumericUpDownEx()
			{
				Minimum = 0,
				Maximum = 300,
				Unit = "secs",
				Width = 80,
				DecimalPlaces = 0,
				Value = Convert.ToDecimal(Application.RecordAnalysis.HasValue ? Application.RecordAnalysis.Value.TotalSeconds : 0),
				//Anchor = AnchorStyles.Left
			};
			tooltip.SetToolTip(RecordAnalysisDelay, "Values higher than 0 enable process analysis\nThis needs to be enabled per watchlist rule to function");

			layout.Controls.Add(new Extensions.Label { Text = "Record analysis delay" });
			layout.Controls.Add(RecordAnalysisDelay);

			bool hwMonLibPresent = System.IO.File.Exists(
				System.IO.Path.Combine(
					System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName),
					"OpenHardwareMonitorLib.dll"));

			var hwmon = new CheckBox() { Checked = Application.HardwareMonitorEnabled, Enabled = hwMonLibPresent, };
			tooltip.SetToolTip(hwmon, "Enables hardware (such as GPU) monitoring\nLimited usability currently.\nRequires OpenHardwareMonitorLib.dll to be present.");

			layout.Controls.Add(new Extensions.Label { Text = "Hardware monitor" });
			layout.Controls.Add(hwmon);

			var iopriority = new CheckBox() { Checked = Application.IOPriorityEnabled, Enabled = MKAh.Execution.IsWin7, };
			tooltip.SetToolTip(iopriority, "Enable I/O priority adjstment\nWARNING: This can be REALLY BAD\nTake care what you do.\nOnly supported on Windows 7.");

			layout.Controls.Add(new Extensions.Label { Text = "I/O priority" });
			layout.Controls.Add(iopriority);

			// NGEN Native Image

			var process = System.Diagnostics.Process.GetCurrentProcess();
			bool nativeImageLoaded = MKAh.Program.NativeImage.Exists(process);

			var ngenLabel = new Extensions.Label
			{
				Text = "Native Image (NI)",
				Font = BoldFont,
				Padding = BigPadding
			};
			layout.Controls.Add(ngenLabel);
			var ngenLink = new LinkLabel
			{
				Text = "(What's this?)",
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
				Anchor = System.Windows.Forms.AnchorStyles.Left,
				LinkBehavior = LinkBehavior.HoverUnderline,
			};
			ngenLink.Links.Add(1, ngenLink.Text.Length - 2, "https://docs.microsoft.com/en-us/dotnet/framework/tools/ngen-exe-native-image-generator");
			ngenLink.LinkClicked += (_, ea) =>
			{
				try
				{
					if (ea.Link.LinkData is string link && link.StartsWith("http", StringComparison.InvariantCultureIgnoreCase))
						System.Diagnostics.Process.Start(link)?.Dispose();
				}
				catch (Exception ex) when (ex is OutOfMemoryException)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
				}
			};
			layout.Controls.Add(ngenLink);

			if (!MKAh.Execution.IsAdministrator)
			{
				var adminWarning = new Extensions.Label { Text = "Admin rights required!", Font = BoldFont, };
				layout.Controls.Add(adminWarning);
				layout.SetColumnSpan(adminWarning, 2);
			}

			var imageUptodateLabel = new Extensions.Label { Text = "Image present && up-to-date", };
			layout.Controls.Add(imageUptodateLabel);

			imageUptodateState = new Extensions.Label();
			UpdateNGenState(nativeImageLoaded);
			layout.Controls.Add(imageUptodateState);

			installButton = new Extensions.Button
			{
				Text = "Install/Update",
				AutoSizeMode = AutoSizeMode.GrowOnly,
				AutoSize = true,
				Anchor = AnchorStyles.Right,
				Enabled = !nativeImageLoaded,
			};
			installButton.Click += InstallButton_Click;

			layout.Controls.Add(installButton);

			uninstallButton = new Extensions.Button
			{
				Text = "Uninstall",
				AutoSizeMode = AutoSizeMode.GrowOnly,
				AutoSize = true,
				Enabled = nativeImageLoaded,
			};
			uninstallButton.Click += UninstallButton_Click;
			layout.Controls.Add(uninstallButton);

			var autoUpdateNgenLabel = new Extensions.Label { Text = "Auto-update native image", };

			var autoUpdateNgen = new CheckBox { Checked = exsec.Get(Application.Constants.AutoNGEN)?.Bool ?? false, };

			layout.Controls.Add(autoUpdateNgenLabel);
			layout.Controls.Add(autoUpdateNgen);

			// FILL IN BOTTOM BUTTONS AND SUCH

			var hzLine = new Label
			{
				Height = 2,
				BorderStyle = BorderStyle.Fixed3D,
				Width = ClientRectangle.Width,
				AutoSize = false,
			};
			layout.Controls.Add(hzLine);
			layout.SetColumnSpan(hzLine, 2);

			var restartWarning = new Extensions.Label { Text = "Experimental features require restart.", AutoSize = true, Font = BoldFont, ForeColor = System.Drawing.Color.Maroon, Padding = BigPadding };
			layout.Controls.Add(restartWarning);
			layout.SetColumnSpan(restartWarning, 2);

			var savebutton = new Extensions.Button() { Text = "Save", Anchor = AnchorStyles.Right };
			savebutton.NotifyDefault(true);

			var cancelbutton = new Extensions.Button() { Text = "Cancel", };
			cancelbutton.Click += Cancelbutton_Click;

			savebutton.Click += (_, _2) =>
			{
				// Set to current use

				Application.RecordAnalysis = RecordAnalysisDelay.Value != decimal.Zero ? (TimeSpan?)TimeSpan.FromSeconds(Convert.ToDouble(RecordAnalysisDelay.Value)) : null;

				// Record for restarts

				using var corecfg = Application.Config.Load(Application.CoreConfigFilename);
				var cfg = corecfg.Config;
				var exsec = cfg[Application.Constants.Experimental];

				if (toggleLoaderTracking.Checked)
					exsec[Application.Constants.LoaderTracking].Bool = true;
				else
					exsec.TryRemove(Application.Constants.LoaderTracking);

				if (RecordAnalysisDelay.Value != decimal.Zero)
					exsec["Record analysis"].Int = Convert.ToInt32(RecordAnalysisDelay.Value);
				else
					exsec.TryRemove("Record analysis");

				if (iopriority.Checked && MKAh.Execution.IsWin7)
					exsec["IO Priority"].Bool = true;
				else
					exsec.TryRemove("IO Priority");

				if (autoUpdateNgen.Checked)
					exsec[Application.Constants.AutoNGEN].Bool = true;
				else
					exsec.TryRemove(Application.Constants.AutoNGEN);

				cfg[Application.Constants.Components][HumanReadable.Hardware.Section].Bool = hwmon.Checked;

				DialogResult = DialogResult.OK;
				Close();
			};

			layout.Controls.Add(savebutton);
			layout.Controls.Add(cancelbutton);

			Controls.Add(layout);

			ResumeLayout(performLayout: false);
		}

		void InstallButton_Click(object sender, EventArgs e)
		{
			try
			{
				using var proc = MKAh.Program.NativeImage.InstallOrUpdateCurrent(withWindow: true);

				installButton.Enabled = uninstallButton.Enabled = false;
				proc.WaitForExit(15_000);
				if (proc.HasExited)
				{
					bool goodExit = proc.ExitCode == 0;

					(goodExit ? uninstallButton : installButton).Enabled = true;

					UpdateNGenState(goodExit);
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		void UpdateNGenState(bool installed) => imageUptodateState.Text = installed ? "Installed" : "Not present";

		void UninstallButton_Click(object sender, EventArgs e)
		{
			try
			{
				using var proc = MKAh.Program.NativeImage.RemoveCurrent(withWindow: true);
				installButton.Enabled = false;
				uninstallButton.Enabled = false;
				proc?.WaitForExit(15_000);
				if (proc?.HasExited ?? false)
				{
					bool goodExit = proc.ExitCode == 0;

					(!goodExit ? uninstallButton : installButton).Enabled = true;

					UpdateNGenState(!goodExit);
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
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
				using var n = new Config.ExperimentConfig(centerOnScreen);
				n.ShowDialog();
				if (n.DialogOK)
				{
					Log.Information("<Experiments> Settings changed");

					Application.ConfirmExit(restart: true, message: "Restart required for experimental settings to take effect.", alwaysconfirm: true);
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
