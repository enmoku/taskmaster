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
	using static Taskmaster;

	public sealed class VolumeMeter : UniForm
	{
		readonly ProgressBar OutputVolume = null;
		readonly ProgressBar InputVolume = null;

		readonly AlignedLabel OutputVolumeLabel = new AlignedLabel() { Text = "0.0 %", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Right, };
		readonly AlignedLabel InputVolumeLabel = new AlignedLabel() { Text = "0.0 %", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Right, };

		int _volumeoutputcap = 10_000;
		public int VolumeOutputCap { get => _volumeoutputcap; set => _volumeoutputcap = value.Constrain(20, 100) * 100; }

		int _volumeinputcap = 10_000;
		public int VolumeInputCap { get => _volumeinputcap; set => _volumeinputcap = value.Constrain(20, 100) * 100; }
		public int Frequency { get; set; } = 100;

		readonly Audio.Manager audiomanager = null;

		public VolumeMeter(Audio.Manager manager)
			: base()
		{
			audiomanager = manager;

			Text = "Volume Meter – Taskmaster!";

			using (var cfg = Taskmaster.Config.Load(CoreConfigFilename).BlockUnload())
			{
				var volsec = cfg.Config["Volume Meter"];

				TopMost = volsec.GetOrSet("Topmost", true).Bool;
				if (TopMost)
				{
					Show();
					BringToFront();
					//Activate();
				}

				Frequency = volsec.GetOrSet("Refresh", 100)
					.InitComment("Refresh delay. Lower is faster. Milliseconds from 10 to 5000.")
					.Int.Constrain(10, 5000);

				int? upgradeOutCap = volsec.Get("Output")?.Int;
				if (upgradeOutCap.HasValue) volsec["Output threshold"].Int = upgradeOutCap.Value;
				VolumeOutputCap = volsec.GetOrSet("Output threshold", 100).Int;

				int? upgradeInCap = volsec.Get("Input")?.Int;
				if (upgradeInCap.HasValue) volsec["Input threshold"].Int = upgradeInCap.Value;
				VolumeInputCap = volsec.GetOrSet("Input threshold", 100).Int;

				// DEPRECATED
				volsec.TryRemove("Cap");
				volsec.TryRemove("Output");
				volsec.TryRemove("Output cap");
				volsec.TryRemove("Input");
				volsec.TryRemove("Input cap");
			}

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
				Maximum = VolumeInputCap,
				Height = 20,
				Width = 200,
				Style = ProgressBarStyle.Continuous,
				MarqueeAnimationSpeed = 30_000,
				ForeColor = System.Drawing.Color.LightGoldenrodYellow,
			};

			barlayout.Controls.Add(new AlignedLabel() { Text = "Output", TextAlign = System.Drawing.ContentAlignment.MiddleRight, AutoSize = true, Dock = DockStyle.Right });
			barlayout.Controls.Add(OutputVolume);
			barlayout.Controls.Add(OutputVolumeLabel);

			barlayout.Controls.Add(new AlignedLabel() { Text = "Input", TextAlign = System.Drawing.ContentAlignment.MiddleRight, AutoSize = true, Dock = DockStyle.Right });
			barlayout.Controls.Add(InputVolume);
			barlayout.Controls.Add(InputVolumeLabel);

			layout.Controls.Add(barlayout);

			Controls.Add(layout);

			FormBorderStyle = FormBorderStyle.FixedDialog;
			AutoSizeMode = AutoSizeMode.GrowAndShrink;
			AutoSize = true;

			updateTimer.Interval = Frequency;
			updateTimer.Tick += UpdateVolumeTick;
			updateTimer.Start();

			using (var uicfg = Taskmaster.Config.Load(MainWindow.UIConfigFilename).BlockUnload())
			{
				var winsec = uicfg.Config["Windows"];
				var winpos = winsec[HumanReadable.Hardware.Audio.Volume].IntArray;

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
			if (audiomanager is null) throw new NullReferenceException(nameof(audiomanager));

			try
			{
				var output = audiomanager.MultimediaDevice.MMDevice.AudioMeterInformation.MasterPeakValue;
				var input = audiomanager.RecordingDevice.MMDevice.AudioMeterInformation.MasterPeakValue;

				if (DebugAudio && Trace) Debug.WriteLine($"Volume --- Output: {output:N2} --- Input: {input:N2}");

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

		#region IDispose
		public event EventHandler<DisposedEventArgs> OnDisposed;

		bool DisposedOrDisposing = false;
		protected override void Dispose(bool disposing)
		{
			if (DisposedOrDisposing) return;

			base.Dispose(disposing);

			if (disposing)
			{
				DisposedOrDisposing = true;

				if (Trace) Log.Verbose("Disposing volume meter box...");

				updateTimer.Dispose();

				using (var cfg = Taskmaster.Config.Load(MainWindow.UIConfigFilename).BlockUnload())
				cfg.Config["Windows"][HumanReadable.Hardware.Audio.Volume].IntArray = new int[] { Bounds.Left, Bounds.Top };
			}

			OnDisposed?.Invoke(this, DisposedEventArgs.Empty);
			OnDisposed = null;
		}
		#endregion Dispose
	}
}
