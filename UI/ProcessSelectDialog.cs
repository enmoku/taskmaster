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
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Taskmaster
{
	sealed public class ProcessSelectDialog : UI.UniForm
	{
		public string Executable { get; private set; } = null;

		public ProcessEx Info { get; private set; } = null;

		ComboBox selection = null;
		Button selectbutton = null, cancelbutton = null;

		List<ProcessEx> InfoList = new List<ProcessEx>();

		public ProcessSelectDialog(string message = "")
			: base()
		{
			WindowState = FormWindowState.Normal;
			FormBorderStyle = FormBorderStyle.FixedDialog; // no min/max buttons as wanted

			StartPosition = FormStartPosition.CenterParent;

			AutoSize = true;
			AutoSizeMode = AutoSizeMode.GrowAndShrink;

			Text = "Choose Executable – " + System.Windows.Forms.Application.ProductName;

			var layout = new TableLayoutPanel()
			{
				ColumnCount = 1,
				AutoSize = true,
				Dock = DockStyle.Fill
			};

			selection = new ComboBox()
			{
				DropDownStyle = ComboBoxStyle.DropDown,
				AutoCompleteMode = AutoCompleteMode.SuggestAppend,
				AutoSize = true,
				Dock = DockStyle.Top,
				//Width = 160,
			};

			if (!string.IsNullOrEmpty(message))
			{
				layout.Controls.Add(new Label() { Text = message, AutoSize = true, Dock = DockStyle.Fill, Padding = BigPadding });
			}

			layout.Controls.Add(selection);

			var buttonlayout = new TableLayoutPanel()
			{
				ColumnCount = 2,
				AutoSize = true,
				Dock = DockStyle.Top,
			};

			selectbutton = new Button()
			{
				Text = "Select",
				AutoSize = true,
				Dock = DockStyle.Top,
				Enabled	= false,
			};
			selectbutton.Click += SaveSelection;
			selection.KeyDown += (sender, e) =>
			{
				if (e.KeyCode == Keys.Enter)
					SaveSelection(this, e);
			};
			cancelbutton = new Button()
			{
				Text = "Cancel",
				AutoSize = true,
				Dock = DockStyle.Top,
			};
			cancelbutton.Click += (sender, e) =>
			{
				DialogResult = DialogResult.Abort;
				Close();
			};
			buttonlayout.Controls.Add(selectbutton);
			buttonlayout.Controls.Add(cancelbutton);
			buttonlayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
			buttonlayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

			layout.Controls.Add(buttonlayout);

			Controls.Add(layout);

			selection.Text = "[[ Scanning ]]";
			selection.Enabled = false;

			Populate().ConfigureAwait(false);
		}

		async Task Populate()
		{
			await Task.Delay(0).ConfigureAwait(false);

			try
			{
				var procs = System.Diagnostics.Process.GetProcesses(); // TODO: Hook to ProcessManager.ScanEverything somehow

				foreach (var proc in procs)
				{
					try
					{
						int pid = proc.Id;
						string name = proc.ProcessName;

						if (ProcessManager.SystemProcessId(pid)) continue;
						if (ProcessManager.ProtectedProcessName(name)) continue;
						if (ProcessManager.IgnoreProcessName(name)) continue;

						if (ProcessUtility.GetInfo(pid, out var info, proc, name: proc.ProcessName, getPath: true))
							InfoList.Add(info);
					}
					catch (InvalidOperationException)
					{
						// Already exited
						continue;
					}
					catch (Exception ex)
					{
						Logging.Stacktrace(ex); // throws only if proc.processname fails
												// NOP,don't care
					}
				}

				InfoList = InfoList.OrderBy(inam => inam.Name).ThenBy(iid => iid.Id).ToList();
				var output = InfoList.ConvertAll(x => $"{System.IO.Path.GetFileName(x.Path ?? x.Name)} #{x.Id}");

				if (IsDisposed) return;

				BeginInvoke(new Action(() =>
				{
					selection.Text = "";
					selection.Enabled = true;

					selection.Items.AddRange(output.ToArray());
					selection.AutoCompleteSource = AutoCompleteSource.ListItems;
					selectbutton.Enabled = true;
				}));
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				DialogResult = DialogResult.Abort;
				BeginInvoke(new Action(() => { Close(); }));
			}
		}

		void SaveSelection(object _, EventArgs _ea)
		{
			try
			{
				int off = selection.SelectedIndex;
				var info = InfoList[off];

				Executable = info.Name;
				Info = info;
				DialogResult = DialogResult.OK;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}

			Close();
		}
	}
}