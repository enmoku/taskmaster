//
// UI.VolumeControl.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2020 M.A.
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

namespace Taskmaster.UI
{
	public class VolumeControlDialog : UI.UniForm
	{
		Audio.MicManager manager;

		readonly Extensions.TableLayoutPanel layout;

		readonly Extensions.Button savebutton;
		readonly Extensions.NumericUpDownEx volume;
		readonly Extensions.Label label;

		readonly Audio.Device audiodevice;

		public VolumeControlDialog(Audio.Device device, Audio.MicManager micmanager)
		{
			audiodevice = device;
			manager = micmanager;

			SuspendLayout();

			WindowState = FormWindowState.Normal;
			FormBorderStyle = FormBorderStyle.FixedDialog; // no min/max buttons as wanted

			StartPosition = FormStartPosition.CenterParent;

			AutoSize = true;
			AutoSizeMode = AutoSizeMode.GrowAndShrink;

			Text = device + " – Volume Control";

			// LAYOUT

			layout = new Extensions.TableLayoutPanel()
			{
				RowCount = 1,
				ColumnCount = 3,
				AutoSize = true,
				Dock = DockStyle.Fill
			};

			label = new Extensions.Label()
			{
				Text = "Set desired target volume to:",
			};

			volume = new Extensions.NumericUpDownEx()
			{
				Minimum = 0,
				Maximum = 100,
				DecimalPlaces = 1,
				Increment = 5,
				Unit = "%",
				Value = Convert.ToDecimal(device.Target),
			};

			savebutton = new Extensions.Button()
			{
				Text = "Save",
				AutoSize = true,
				Dock = DockStyle.Top,
				Enabled = false,
			};
			savebutton.Click += SaveSelection;

			layout.Controls.Add(label);
			layout.Controls.Add(volume);
			layout.Controls.Add(savebutton);

			Controls.Add(layout);

			ResumeLayout();
		}

		private void SaveSelection(object sender, EventArgs e)
		{
			audiodevice.Target = Convert.ToSingle(volume.Value);
		}

		#region IDisposable
		bool disposed;

		protected override void Dispose(bool disposing)
		{
			if (disposed) return;
			disposed = true;

			if (disposing)
			{
				layout.Dispose();
				savebutton.Dispose();
			}

			base.Dispose(disposing);
		}
		#endregion
	}
}