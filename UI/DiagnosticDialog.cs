//
// DiagnosticDialog.cs
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
using System.Collections.Concurrent;
using System.Windows.Forms;

namespace Taskmaster.UI
{
	public class DiagnosticMessage
	{
		public int Id { get; set; } = -1;

		public string Message { get; set; } = string.Empty;
	}

	public class DiagnosticDialog : UniForm
	{
		readonly ListView MessageList = new ListView();

		readonly AlignedLabel Status = new AlignedLabel() { Text = "n/a" };

		readonly DiagnosticSystem System = null;

		public DiagnosticDialog(DiagnosticSystem system)
		{
			if (system == null) system = new DiagnosticSystem();
			System = system;

			var layout = new TableLayoutPanel() { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, };
			Controls.Add(layout);

			layout.Controls.Add(new AlignedLabel() { Text = "Status:", Font = boldfont });
			layout.Controls.Add(Status);

			layout.Controls.Add(MessageList);
			layout.SetColumnSpan(MessageList, 2);

			FormClosing += DialogClosing;
		}

		private void DialogClosing(object sender, FormClosingEventArgs e)
		{
			//System.DiagnosisUpdate -= DiagnosisUpdateEvent;
		}

		readonly ConcurrentDictionary<int, DiagnosticMessage> Messages = new ConcurrentDictionary<int, DiagnosticMessage>();
		int LastMessage = 0;

		readonly ConcurrentDictionary<int, ListViewItem> MessageDisplay = new ConcurrentDictionary<int, ListViewItem>();

		public int RegisterMessage(string message)
		{
			int id = ++LastMessage;
			if (Messages.TryAdd(id, new DiagnosticMessage() { Id = id, Message = message }))
				return id;
			return -1;
		}
	}

	public class DiagnosticSystem
	{
		public DiagnosticSystem()
		{
			// find services
			// string q = "select * from Win32_Service where PathName LIKE \"%svchost.exe%\"";
			// ManagementObjectSearcher mos = new ManagementObjectSearcher(q);

		}

		public event EventHandler DiagnosisUpdate;
	}
}
