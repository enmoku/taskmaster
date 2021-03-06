﻿//
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
using System.Linq;
using System.Windows.Forms;

namespace Taskmaster.UI
{
	using static Application;

	public class VolumeMeter : UniForm
	{
		readonly ProgressBar OutputVolume, InputVolume;

		readonly Extensions.Label
			OutputVolumeLabel = new Extensions.Label() { Text = "0.0 %", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Right, },
			InputVolumeLabel = new Extensions.Label() { Text = "0.0 %", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Right, };

		int _volumeoutputcap = 10_000;
		public int VolumeOutputCap { get => _volumeoutputcap; set => _volumeoutputcap = value.Constrain(20, 100) * 100; }

		int _volumeinputcap = 10_000;
		public int VolumeInputCap { get => _volumeinputcap; set => _volumeinputcap = value.Constrain(20, 100) * 100; }
		public int Frequency { get; set; }

		readonly Audio.Manager audiomanager;

		public VolumeMeter(Audio.Manager manager)
		{
			Visible = false;
			SuspendLayout();

			audiomanager = manager;

			Text = "Volume Meter – Taskmaster!";

			using var cfg = Application.Config.Load(CoreConfigFilename);
			var volsec = cfg.Config["Volume Meter"];

			TopMost = volsec.GetOrSet("Topmost", true).Bool;

			Frequency = volsec.GetOrSet("Refresh", 100)
				.InitComment("Refresh delay. Lower is faster. Milliseconds from 10 to 5000.")
				.Int.Constrain(10, 5000);

			int? upgradeOutCap = volsec.Get(Audio.Constants.Output)?.Int;
			if (upgradeOutCap.HasValue) volsec[Audio.Constants.OutputThreshold].Int = upgradeOutCap.Value;
			VolumeOutputCap = volsec.GetOrSet(Audio.Constants.OutputThreshold, 100).Int;

			int? upgradeInCap = volsec.Get(Audio.Constants.Input)?.Int;
			if (upgradeInCap.HasValue) volsec[Audio.Constants.InputThreshold].Int = upgradeInCap.Value;
			VolumeInputCap = volsec.GetOrSet(Audio.Constants.InputThreshold, 100).Int;

			#region Build UI
			FormBorderStyle = FormBorderStyle.FixedDialog;
			AutoSizeMode = AutoSizeMode.GrowAndShrink;
			AutoSize = true;

			var layout = new Extensions.TableLayoutPanel()
			{
				ColumnCount = 1,
				RowCount = 1,
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				Dock = DockStyle.Fill,
			};

			layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

			var barlayout = new Extensions.TableLayoutPanel()
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
				//MarqueeAnimationSpeed = 0, // ignored by continuous
				//ForeColor = System.Drawing.Color.DodgerBlue,
			};

			InputVolume = new ProgressBar()
			{
				Minimum = 0,
				Maximum = VolumeInputCap,
				Height = 20,
				Width = 200,
				Style = ProgressBarStyle.Continuous,
				//MarqueeAnimationSpeed = 0,
				//ForeColor = System.Drawing.Color.LightGoldenrodYellow,
			};

			barlayout.Controls.Add(new Extensions.Label() { Text = "Output", TextAlign = System.Drawing.ContentAlignment.MiddleRight, AutoSize = true, Dock = DockStyle.Right });
			barlayout.Controls.Add(OutputVolume);
			barlayout.Controls.Add(OutputVolumeLabel);

			barlayout.Controls.Add(new Extensions.Label() { Text = "Input", TextAlign = System.Drawing.ContentAlignment.MiddleRight, AutoSize = true, Dock = DockStyle.Right });
			barlayout.Controls.Add(InputVolume);
			barlayout.Controls.Add(InputVolumeLabel);

			layout.Controls.Add(barlayout);

			Controls.Add(layout);
			#endregion // Build UI

			updateTimer.Interval = Frequency;
			updateTimer.Tick += UpdateVolumeTick;
			updateTimer.Start();

			using var uicfg = Application.Config.Load(UIConfigFilename);
			var winsec = uicfg.Config[Constants.Windows];
			var winpos = winsec[HumanReadable.Hardware.Audio.Volume].IntArray;

			if (winpos?.Length == 2)
			{
				StartPosition = FormStartPosition.Manual;
				Bounds = new System.Drawing.Rectangle(winpos[0], winpos[1], Bounds.Width, Bounds.Height);
				//Location = new System.Drawing.Point(Bounds.Left, Bounds.Top);

				if (!Screen.AllScreens.Any(screen => screen.Bounds.IntersectsWith(Bounds)))
					CenterToParent();
			}

			ResumeLayout(performLayout: false);

			Visible = true;
			Show();

			if (TopMost)
			{
				BringToFront();
				//Activate();
			}
		}

		float PreviousOut, PreviousIn;
		int SuspicionIn, SuspicionOut;

		void UpdateVolumeTick(object sender, EventArgs e)
		{
			if (disposed) return;

			try
			{
				var output = audiomanager.MultimediaDevice.MMDevice.AudioMeterInformation.MasterPeakValue;
				var input = audiomanager.RecordingDevice.MMDevice.AudioMeterInformation.MasterPeakValue;

				if (DebugAudio && Trace) Logging.DebugMsg($"Volume --- Output: {output:N2} --- Input: {input:N2}");

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
		public event EventHandler<DisposedEventArgs>? OnDisposed;

		bool disposed;

		protected override void Dispose(bool disposing)
		{
			if (disposed) return;
			disposed = true;

			if (disposing)
			{
				if (Trace) Log.Verbose("Disposing volume meter box...");

				updateTimer.Dispose();

				using var cfg = Application.Config.Load(UIConfigFilename);

				var saveBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;

				cfg.Config[Constants.Windows][HumanReadable.Hardware.Audio.Volume].IntArray = new int[] { saveBounds.Left, saveBounds.Top };

				OutputVolume.Dispose();
				InputVolume.Dispose();
				OutputVolumeLabel.Dispose();
				InputVolumeLabel.Dispose();

				OnDisposed?.Invoke(this, DisposedEventArgs.Empty);
				OnDisposed = null;
			}

			base.Dispose(disposing);
		}
		#endregion Dispose
	}
}
