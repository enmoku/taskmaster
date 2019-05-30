//
// UI.ChangeLog.cs
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

using System;
using System.Windows.Forms;

namespace Taskmaster.UI
{
	using static Taskmaster;

	public class ChangeLog : UniForm
	{
		readonly Button ShowFullLogButton = new Button() { Text = "Full log" };
		readonly Button OKButton = new Button() { Text = "OK" };
		readonly TextBox LogBox = new TextBox() { Dock = DockStyle.Fill };

		readonly string LogData = null;

		readonly AlignedLabel Title = null;

		public ChangeLog(string logdata)
			: base(false)
		{
			SuspendLayout();

			LogData = logdata;

			//TopMost = true;

			Text = "Changelog for " + Taskmaster.Name;

			var layout = new TableLayoutPanel()
			{
				ColumnCount = 2,
				Dock = DockStyle.Fill,
				AutoSize = true,
			};

			Title = new AlignedLabel() { Text = "Changes since "  };

			layout.Controls.Add(Title);
			layout.SetColumnSpan(Title, 2);

			LogBox.Text = "";
			layout.Controls.Add(LogBox);
			layout.SetColumnSpan(LogBox, 2);

			ShowFullLogButton.Anchor = AnchorStyles.Top | AnchorStyles.Left;
			ShowFullLogButton.Click += ShowFullLogButton_Click;
			layout.Controls.Add(ShowFullLogButton);

			OKButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
			OKButton.Click += OKButton_Click;
			layout.Controls.Add(OKButton);

			Controls.Add(layout);

			ResumeLayout();
		}

		private void OKButton_Click(object sender, EventArgs e)
		{
			// TODO: Mark last log
			throw new NotImplementedException();
		}

		private void ShowFullLogButton_Click(object sender, EventArgs e)
		{
			LogBox.Text = LogData;
		}
	}
}
