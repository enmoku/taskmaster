//
// UI.MessageBox.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2018–2019 M.A.
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
	public class MessageBox : UI.UniForm
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

		public static ResultType ShowModal(string title, string message, Buttons buttons, bool rich=false, Control parent=null)
		{
			using (var msg = new MessageBox(title, message, buttons, rich, parent))
			{
				msg.CenterToParent();
				msg.ShowDialog();
				
				return msg.Result;
			}
		}

		readonly Label Message = null;
		readonly RichTextBox RichMessage = null;

		public MessageBox(string title, string message, Buttons buttons, bool rich=false, Control parent = null)
			: base()
		{
			//if (!(parent is null)) Parent = parent;

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

			var okbutton = new Button() { Text = "OK", Margin = BigPadding };
			okbutton.Click += (_, _ea) => { Result = ResultType.OK; Close();  };

			var cancelbutton = new Button() { Text = "Cancel", Margin = BigPadding };
			cancelbutton.Click += (_, _ea) => { Result = ResultType.Cancel; Close();  };

			var retrybutton = new Button() { Text = "Retry", Margin = BigPadding };
			retrybutton.Click += (_, _ea) => { Result = ResultType.Retry; Close(); };

			var endbutton = new Button() { Text = "End", Margin = BigPadding };
			endbutton.Click += (_, _ea) => { Result = ResultType.End; Close(); };

			switch (buttons)
			{
				case Buttons.OK:
					okbutton.Anchor = AnchorStyles.Top;
					buttonlayout.Controls.Add(new UI.EmptySpace());
					buttonlayout.Controls.Add(okbutton);
					okbutton.NotifyDefault(true);
					break;
				case Buttons.AcceptCancel:
					okbutton.Text = "Accept";
					okbutton.Anchor = AnchorStyles.Right;
					buttonlayout.Controls.Add(okbutton);
					buttonlayout.Controls.Add(new UI.EmptySpace());
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

			if (rich)
			{
				RichMessage = new RichTextBox() { Rtf = message, ReadOnly = true, Dock = DockStyle.Fill, Width = 600, Height = 400 };
				layout.Controls.Add(RichMessage);
			}
			else
			{
				Message = new Label() { Text = message, AutoSize = true, Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.TopLeft, Padding = BigPadding };
				layout.Controls.Add(Message);
			}
			layout.Controls.Add(buttonlayout);

			StartPosition = parent != null ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen;
		}
	}
}
