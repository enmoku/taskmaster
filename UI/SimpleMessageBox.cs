﻿//
// SimpleMessageBox.cs
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
	public class SimpleMessageBox : UI.UniForm
	{
		public enum Buttons : int
		{
			OK,
			AcceptCancel,
			RetryEndCancel,
		};

		public enum ResultType
		{
			OK,
			Cancel,
			End,
			Retry,
		};

		public ResultType Result { get; private set; } = ResultType.Cancel;

		public static ResultType ShowModal(string title, string message, Buttons buttons)
		{
			using (var msg = new SimpleMessageBox(title, message, buttons))
			{
				msg.ShowDialog();

				return msg.Result;
			}
		}

		public SimpleMessageBox(string title, string message, Buttons buttons)
		{
			Text = title;

			AutoSize = true;
			AutoSizeMode = AutoSizeMode.GrowAndShrink;

			FormBorderStyle = FormBorderStyle.FixedDialog;

			var layout = new TableLayoutPanel()
			{
				ColumnCount = 1,
				AutoSize = true,
				Parent = this,
				Dock = DockStyle.Fill,
			};

			var buttonlayout = new TableLayoutPanel()
			{
				RowCount = 1,
				ColumnCount = 3,
				AutoSize = true,
				Dock = DockStyle.Top,
			};

			buttonlayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
			buttonlayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
			buttonlayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));

			var okbutton = new Button() { Text = "OK", Margin = CustomPadding };
			okbutton.Click += (_, _ea) => { Result = ResultType.OK; Close();  };

			var cancelbutton = new Button() { Text = "Cancel", Margin = CustomPadding };
			cancelbutton.Click += (_, _ea) => { Result = ResultType.Cancel; Close();  };

			var retrybutton = new Button() { Text = "Retry", Margin = CustomPadding };
			retrybutton.Click += (_, _ea) => { Result = ResultType.Retry; Close(); };

			var endbutton = new Button() { Text = "End", Margin = CustomPadding };
			endbutton.Click += (_, _ea) => { Result = ResultType.End; Close(); };

			switch (buttons)
			{
				case Buttons.OK:
					okbutton.Anchor = AnchorStyles.Top;
					buttonlayout.Controls.Add(new Label()); // empty
					buttonlayout.Controls.Add(okbutton);
					okbutton.NotifyDefault(true);
					break;
				case Buttons.AcceptCancel:
					okbutton.Text = "Accept";
					okbutton.Anchor = AnchorStyles.Right;
					buttonlayout.Controls.Add(okbutton);
					buttonlayout.Controls.Add(new Label()); // empty
					buttonlayout.Controls.Add(cancelbutton);
					okbutton.NotifyDefault(true);
					break;
				case Buttons.RetryEndCancel:
					retrybutton.Anchor = AnchorStyles.Right;
					endbutton.Anchor = AnchorStyles.Top;
					buttonlayout.Controls.Add(retrybutton);
					buttonlayout.Controls.Add(endbutton);
					buttonlayout.Controls.Add(cancelbutton);
					retrybutton.NotifyDefault(true);
					break;
			}

			layout.Controls.Add(new Label() { Text = message, AutoSize = true, Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.TopLeft, Padding = CustomPadding });
			layout.Controls.Add(buttonlayout);

			StartPosition = FormStartPosition.CenterParent;
		}
	}
}