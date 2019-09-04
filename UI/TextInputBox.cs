//
// TextInputBox.cs
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

using System.Windows.Forms;

namespace Taskmaster.UI
{
	public class TextInputBox : UI.UniForm
	{
		public string Value { get; private set; }

		public TextInputBox(string message, string title, string input = "")
		{
			Text = title;
			Value = string.Empty;

			DoubleBuffered = true;

			SetStyle(
				ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.EnableNotifyMessage,
				true);
			UpdateStyles();

			DialogResult = DialogResult.Abort;

			StartPosition = FormStartPosition.CenterParent;

			AutoSizeMode = AutoSizeMode.GrowAndShrink;


			#region Build UI
			var layout = new Extensions.TableLayoutPanel()
			{
				ColumnCount = 1,
				AutoSize = true,
			};

			var textbox = new Extensions.TextBox()
			{
				ShortcutsEnabled = true,
			};

			textbox.Text = input ?? string.Empty;

			layout.Controls.Add(new Extensions.Label() { Text = message });
			layout.Controls.Add(textbox);

			var buttons = new Extensions.TableLayoutPanel()
			{
				ColumnCount = 2,
				AutoSize = true,
			};

			var okbutton = new Extensions.Button()
			{
				Text = "OK",
			};

			okbutton.Click += (_, _ea) =>
			{
				DialogResult = DialogResult.OK;
				Value = textbox.Text;
				Close();
			};

			var cancelbutton = new Extensions.Button()
			{
				Text = "Cancel",
			};

			cancelbutton.Click += (_, _ea) =>
			{
				DialogResult = DialogResult.Cancel;
				Value = string.Empty;
				Close();
			};

			buttons.Controls.Add(okbutton);
			buttons.Controls.Add(cancelbutton);

			layout.Controls.Add(buttons);

			textbox.Width = layout.Width;

			Controls.Add(layout);
			#endregion // Build UI

			/*
			TopMost = true;
			Activate();
			BringToFront();
			*/
		}
	}
}
