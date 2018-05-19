//
// LicenseDialog.cs
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

using System.Linq;
using System.Windows.Forms;

namespace Taskmaster
{
	sealed class LicenseDialog : UI.UniForm
	{
		public LicenseDialog(bool initial = true)
		{
			Text = "Taskmaster License";
			AutoSizeMode = AutoSizeMode.GrowAndShrink;
			FormBorderStyle = FormBorderStyle.FixedDialog;
			if (initial)
				StartPosition = FormStartPosition.CenterScreen;

			var layout = new TableLayoutPanel()
			{
				ColumnCount = 1,
				AutoSize = true,
				Padding = new Padding(12),
			};
			var buttonlayout = new TableLayoutPanel()
			{
				ColumnCount = 2,
				AutoSize = true,
			};

			var licensebox = new TextBox()
			{
				ReadOnly = true,
				Text = "NO WARRANTY",
				Multiline = true,
				Font = new System.Drawing.Font(System.Drawing.FontFamily.GenericMonospace, DefaultFont.Size * 1.2f),
			};

			licensebox.Text = Properties.Resources.LICENSE.Replace("\t\t", "\t").TrimEnd('\n', ' ');

			licensebox.Height = (licensebox.Lines.Count() * 16);
			licensebox.Width = 640;
			licensebox.SelectionStart = 0;
			licensebox.SelectionLength = 0;

			var buttonAccept = new Button()
			{
				Text = "Accept",
				Anchor = AnchorStyles.Right,
			};

			var buttonRefuse = new Button()
			{
				Text = "Refuse",
				Anchor = AnchorStyles.Left,
			};

			if (!initial) buttonAccept.Text = "OK";

			buttonlayout.Controls.Add(buttonAccept);

			if (initial) buttonlayout.Controls.Add(buttonRefuse);

			var required = new Label()
			{
				Text = "You must accept the following license to use this application.",
				AutoSize = true,
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
				Margin = CustomPadding,
				Font = new System.Drawing.Font(System.Drawing.FontFamily.GenericSansSerif, DefaultFont.Size * 1.2f),
			};

			if (initial) layout.Controls.Add(required);
			layout.Controls.Add(licensebox);
			layout.Controls.Add(buttonlayout);

			buttonAccept.Click += (s, e) =>
			{
				DialogResult = DialogResult.Yes;
				Close();
			};

			buttonRefuse.Click += (s, e) =>
			{
				DialogResult = DialogResult.No;
				Close();
			};

			buttonRefuse.Focus();

			Controls.Add(layout);
		}
	}
}