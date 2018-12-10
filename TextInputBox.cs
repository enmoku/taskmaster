//
// TextInputBox.cs
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

using System.Windows.Forms;

namespace Taskmaster
{
	sealed public class TextInputBox : UI.UniForm
	{
		public string Value { get; private set; } = null;

		public TextInputBox(string message, string title, string input = null)
		{
			Text = title;

			DialogResult = DialogResult.Abort;

			StartPosition = FormStartPosition.CenterParent;

			AutoSizeMode = AutoSizeMode.GrowAndShrink;

			TopMost = true;
			BringToFront();

			var layout = new TableLayoutPanel()
			{
				ColumnCount = 1,
				AutoSize = true,
			};

			var textbox = new TextBox()
			{
				ShortcutsEnabled = true,
			};

			if (input != null) textbox.Text = input;

			layout.Controls.Add(new Label() { Text = message, TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
			layout.Controls.Add(textbox);

			var buttons = new TableLayoutPanel()
			{
				ColumnCount = 2,
				AutoSize = true,
			};

			var okbutton = new Button()
			{
				Text = "OK",
			};

			okbutton.Click += (s, ev) =>
			{
				DialogResult = DialogResult.OK;
				Value = textbox.Text;
				Close();
			};

			var cancelbutton = new Button()
			{
				Text = "Cancel",
			};

			cancelbutton.Click += (s, ev) =>
			{
				DialogResult = DialogResult.Cancel;
				Value = null;
				Close();
			};

			buttons.Controls.Add(okbutton);
			buttons.Controls.Add(cancelbutton);

			layout.Controls.Add(buttons);

			textbox.Width = layout.Width;

			Controls.Add(layout);
		}
	}
}
