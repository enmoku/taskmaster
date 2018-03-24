//
// ProcessSelectDialog.cs
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
using System.Collections.Generic;
using System.Linq;

namespace Taskmaster
{
	public class ProcessSelectDialog : Form
	{
		public string Selection { get; private set; } = null;

		ComboBox selection = null;

		public ProcessSelectDialog()
		{
			WindowState = FormWindowState.Normal;
			FormBorderStyle = FormBorderStyle.FixedDialog; // no min/max buttons as wanted
			MinimizeBox = false;
			MaximizeBox = false;

			Padding = new Padding(6);

			Width = 260;
			Height = 100;

			Text = "Choose Executable – " + System.Windows.Forms.Application.ProductName;

			AutoSize = true;

			var rowlayout = new TableLayoutPanel()
			{
				ColumnCount = 1,
				AutoSize = true,
				Dock = DockStyle.Top
			};

			selection = new ComboBox()
			{
				DropDownStyle = ComboBoxStyle.DropDown,
				AutoCompleteMode = AutoCompleteMode.SuggestAppend,
				AutoSize = true,
				Dock = DockStyle.Top,
				//Width = 160,
			};

			var procs = System.Diagnostics.Process.GetProcesses(); // TODO: Hook to ProcessManager.ScanEverything somehow
			var procnames = new HashSet<string>();
			foreach (var proc in procs)
			{
				try
				{
					if (proc.Id <= 4) continue;
					procnames.Add(proc.ProcessName);
				}
				catch
				{
					// NOP,don't care
				}
			}

			selection.Items.AddRange(procnames.ToArray());
			selection.AutoCompleteSource = AutoCompleteSource.ListItems;

			rowlayout.Controls.Add(selection);

			var buttonlayout = new TableLayoutPanel()
			{
				ColumnCount = 2,
				AutoSize = true,
				Dock = DockStyle.Top,
			};

			var selectbutton = new Button()
			{
				Text = "Select",
				AutoSize = true,
			};
			selectbutton.Click += SaveSelection;
			selection.KeyDown += (sender, e) =>
			{
				if (e.KeyCode == Keys.Enter)
					SaveSelection(this, e);
			};
			var cancelbutton = new Button()
			{
				Text = "Cancel",
				AutoSize = true,
			};
			cancelbutton.Click += (sender, e) =>
			{
				DialogResult = DialogResult.Abort;
				Close();
			};
			buttonlayout.Controls.Add(selectbutton);
			buttonlayout.Controls.Add(cancelbutton);

			rowlayout.Controls.Add(buttonlayout);

			Controls.Add(rowlayout);
		}

		void SaveSelection(object sender, EventArgs ev)
		{
			Selection = selection.Text;
			if (!string.IsNullOrEmpty(Selection))
			{
				DialogResult = DialogResult.OK;
			}
			Close();
		}
	}
}
