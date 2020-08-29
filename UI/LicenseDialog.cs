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

using System;
using System.Windows.Forms;

namespace Taskmaster.UI
{
	class LicenseDialog : UniForm, IDisposable
	{
		readonly Extensions.Button buttonAccept, buttonRefuse;
		readonly Extensions.Label requiredLabel;

		internal LicenseDialog(bool initial = true, bool center = false)
			: base(centerOnScreen: initial || center)
		{
			SuspendLayout();

			Text = Application.Name + " License";
			AutoSizeMode = AutoSizeMode.GrowAndShrink;
			FormBorderStyle = FormBorderStyle.FixedDialog;
			if (initial)
				StartPosition = FormStartPosition.CenterScreen;

			#region Build UI

			var layout = new FlowLayoutPanel()
			{
				AutoSize = true,
				FlowDirection = FlowDirection.TopDown,
				Margin = BigPadding,
				Padding = BigPadding,
			};
			var buttonlayout = new FlowLayoutPanel()
			{
				FlowDirection = FlowDirection.LeftToRight,
				AutoSize = true,
			};

			var licensebox = new Extensions.TextBox()
			{
				ReadOnly = true,
				Text = "NO WARRANTY",
				Multiline = true,
				Font = new System.Drawing.Font(System.Drawing.FontFamily.GenericMonospace, DefaultFont.Size * 1.2f),
			};

			licensebox.Text = Properties.Resources.LICENSE.Replace("\t\t", "\t").TrimEnd('\n', ' ');

			licensebox.Height = (licensebox.Lines.Length * 16);
			licensebox.Width = 640;
			licensebox.SelectionStart = 0;
			licensebox.SelectionLength = 0;

			buttonAccept = new Extensions.Button()
			{
				Text = "Accept",
				Anchor = AnchorStyles.Right,
			};

			buttonRefuse = new Extensions.Button()
			{
				Text = "Refuse",
				Anchor = AnchorStyles.Left,
			};

			if (!initial) buttonAccept.Text = "OK";

			buttonlayout.Controls.Add(buttonAccept);

			if (initial) buttonlayout.Controls.Add(buttonRefuse);

			requiredLabel = new Extensions.Label()
			{
				Text = "You must accept the following license to use this application.",
				AutoSize = true,
				Padding = BigPadding,
				Font = new System.Drawing.Font(System.Drawing.FontFamily.GenericSansSerif, DefaultFont.Size * 1.2f),
			};

			if (initial) layout.Controls.Add(requiredLabel);
			layout.Controls.Add(licensebox);
			layout.Controls.Add(buttonlayout);

			buttonAccept.Click += (_, _2) =>
			{
				DialogResult = DialogResult.OK;
				Close();
			};

			buttonRefuse.Click += (_, _2) =>
			{
				DialogResult = DialogResult.Cancel;
				Close();
			};

			buttonRefuse.Focus();

			Controls.Add(layout);
			#endregion // Build UI

			ResumeLayout(performLayout: false);
		}

		#region IDisposable Support
		bool isdisposed;

		protected override void Dispose(bool disposing)
		{
			if (isdisposed) return;
			isdisposed = true;

			if (disposing)
			{
				buttonAccept.Dispose();
				buttonRefuse.Dispose();
				requiredLabel.Dispose();
			}

			base.Dispose(disposing);
		}

		public new void Dispose() => Dispose(true);
		#endregion
	}
}