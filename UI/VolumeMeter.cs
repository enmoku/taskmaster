//
// VolumeMeter.cs
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
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;

namespace Taskmaster.UI
{
	public sealed class VolumeMeter : UniForm
	{
		ProgressBar OutputVolume = null;
		ProgressBar InputVolume = null;

		Label OutputVolumeLabel = null;
		Label InputVolumeLabel = null;

		int VolumeOutputCap = 100;
		int VolumeInputCap = 100;
		int Frequency = 100;

		public VolumeMeter()
			: base()
		{
			Text = "Volume Meter – Taskmaster!";

			var cfg = Taskmaster.Config.Load(Taskmaster.coreconfig);
			var volsec = cfg.Config["Volume Meter"];

			bool modified = false, dirty = false;
			TopMost = volsec.GetOrSet("Topmost", true, out modified).BoolValue;
			dirty |= modified;
			Frequency = volsec.GetOrSet("Refresh", 100, out modified).IntValue.Constrain(10, 5000);
			volsec["Refresh"].Comment = "Refresh delay. Lower is faster. Milliseconds from 10 to 5000.";
			dirty |= modified;
			int? upgradeOutCap = volsec.Get("Output")?.IntValue;
			if (upgradeOutCap.HasValue) volsec["Output threshold"].IntValue = upgradeOutCap.Value;
			VolumeOutputCap = volsec.GetOrSet("Output threshold", 100, out modified).IntValue.Constrain(20, 100) * 100;
			dirty |= modified;
			int? upgradeInCap = volsec.Get("Input")?.IntValue;
			if (upgradeInCap.HasValue) volsec["Input threshold"].IntValue = upgradeInCap.Value;
			VolumeInputCap = volsec.GetOrSet("Input threshold", 100, out modified).IntValue.Constrain(20, 100) * 100;
			dirty |= modified;

			volsec.TryRemove("Cap");
			volsec.TryRemove("Output");
			volsec.TryRemove("Output cap");
			volsec.TryRemove("Input");
			volsec.TryRemove("Input cap");

			if (dirty) cfg.MarkDirty();

			var layout = new TableLayoutPanel()
			{
				ColumnCount = 1,
				RowCount = 1,
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				Dock = DockStyle.Fill,
			};

			layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

			var barlayout = new TableLayoutPanel()
			{
				ColumnCount = 3,
				RowCount = 2,
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				Dock = DockStyle.Fill,
			};

			barlayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
			barlayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			barlayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
			barlayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			barlayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

			OutputVolume = new ProgressBar()
			{
				Minimum = 0,
				Maximum = VolumeOutputCap,
				Height = 20,
				Width = 200,
				Style = ProgressBarStyle.Continuous,
				MarqueeAnimationSpeed = 30_000, // ignored by continuous
				ForeColor = System.Drawing.Color.DodgerBlue,
			};

			InputVolume = new ProgressBar()
			{
				Minimum = 0,
				Maximum = VolumeOutputCap,
				Height = 20,
				Width = 200,
				Style = ProgressBarStyle.Continuous,
				MarqueeAnimationSpeed = 30_000,
				ForeColor = System.Drawing.Color.LightGoldenrodYellow,
			};

			OutputVolumeLabel = new Label()
			{
				Text = "0.0 %",
				AutoSize = true,
				TextAlign = System.Drawing.ContentAlignment.MiddleRight,
				Dock = DockStyle.Right,
			};
			InputVolumeLabel = new Label()
			{
				Text = "0.0 %",
				AutoSize = true,
				TextAlign = System.Drawing.ContentAlignment.MiddleRight,
				Dock = DockStyle.Right,
			};

			barlayout.Controls.Add(new Label() { Text = "Output", TextAlign = System.Drawing.ContentAlignment.MiddleRight, AutoSize = true, Dock = DockStyle.Right });
			barlayout.Controls.Add(OutputVolume);
			barlayout.Controls.Add(OutputVolumeLabel);

			barlayout.Controls.Add(new Label() { Text = "Input", TextAlign = System.Drawing.ContentAlignment.MiddleRight, AutoSize = true, Dock = DockStyle.Right });
			barlayout.Controls.Add(InputVolume);
			barlayout.Controls.Add(InputVolumeLabel);

			layout.Controls.Add(barlayout);

			Controls.Add(layout);

			FormBorderStyle = FormBorderStyle.FixedDialog;
			AutoSizeMode = AutoSizeMode.GrowAndShrink;
			AutoSize = true;

			audiomanager = Taskmaster.audiomanager; // such hack

			updateTimer.Interval = Frequency;
			updateTimer.Tick += UpdateVolumeTick;
			updateTimer.Start();

			var uicfg = Taskmaster.Config.Load(MainWindow.UIConfig);
			var winsec = uicfg.Config["Windows"];

			var winpos = winsec["Volume"].IntArray;

			if (winpos != null && winpos.Length == 2)
			{
				var rectangle = new System.Drawing.Rectangle(winpos[0], winpos[1], Bounds.Width, Bounds.Height);
				if (Screen.AllScreens.Any(ø => ø.Bounds.IntersectsWith(Bounds))) // https://stackoverflow.com/q/495380
				{
					StartPosition = FormStartPosition.Manual;
					Location = new System.Drawing.Point(rectangle.Left, rectangle.Top);
					Bounds = rectangle;
				}
				else
					CenterToParent();
			}

			Show();
		}

		float PreviousOut = 0.0f;

		float PreviousIn = 0.0f;
		int SuspicionIn = 0;
		int SuspicionOut = 0;

		void UpdateVolumeTick(object sender, EventArgs e)
		{
			if (DisposedOrDisposing) return;

			try
			{
				var output = audiomanager.MultimediaDevice.MMDevice.AudioMeterInformation.MasterPeakValue;
				var input = audiomanager.RecordingDevice.MMDevice.AudioMeterInformation.MasterPeakValue;

				if (Taskmaster.DebugAudio && Taskmaster.Trace)
					Debug.WriteLine($"Volume --- Output: {output:N2} --- Input: {input:N2}");

				OutputVolume.Value = Convert.ToInt32(output * 10000f).Constrain(0, OutputVolume.Maximum);
				OutputVolumeLabel.Text = $"{output * 100f:N2} %";
				InputVolume.Value = Convert.ToInt32(input * 10000f).Constrain(0, InputVolume.Maximum);
				InputVolumeLabel.Text = $"{input * 100f:N2} %";

				// Check for suspicious volume activity

				if (input.RoughlyEqual(PreviousIn))
				{
					if (SuspicionIn++ > 10)
						InputVolumeLabel.ForeColor = System.Drawing.Color.Red;
				}
				else
				{
					SuspicionIn = 0;
					InputVolumeLabel.ForeColor = DefaultForeColor;
				}

				if (output.RoughlyEqual(PreviousOut))
				{
					if (SuspicionOut++ > 10)
						OutputVolumeLabel.ForeColor = System.Drawing.Color.Red;
				}
				else
				{
					SuspicionOut = 0;
					OutputVolumeLabel.ForeColor = DefaultForeColor;
				}

				PreviousIn = input;
				PreviousOut = output;
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				// Expected to crash if we lose either of the devices.
				Logging.Stacktrace(ex);
			}
		}

		readonly Timer updateTimer = new Timer();

		AudioManager audiomanager = null;
		void Hook(AudioManager manager)
		{
			audiomanager = manager;
		}

		#region IDispose
		bool DisposedOrDisposing = false;
		protected override void Dispose(bool disposing)
		{
			if (DisposedOrDisposing) return;

			base.Dispose(disposing);

			if (disposing)
			{
				DisposedOrDisposing = true;

				if (Taskmaster.Trace) Log.Verbose("Disposing volume meter box...");

				updateTimer.Dispose();

				var cfg = Taskmaster.Config.Load(MainWindow.UIConfig);
				var winsec = cfg.Config["Windows"];

				winsec["Volume"].IntArray = new int[] { Bounds.Left, Bounds.Top };

				cfg.MarkDirty();
			}

			Taskmaster.volumemeter = null; // HACK
		}
		#endregion Dispose
	}
}
