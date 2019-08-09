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
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Taskmaster.Process;

namespace Taskmaster.UI
{
	using static Taskmaster;
	public class ProcessSelectDialog : UI.UniForm
	{
		public ProcessEx? Info { get; private set; } = null;

		readonly ComboBox selection;
		readonly Extensions.Button selectbutton, cancelbutton, refreshbutton;

		List<ProcessEx> InfoList = new List<ProcessEx>();

		public ProcessSelectDialog(string message = "", string title = null)
			: base()
		{
			SuspendLayout();

			WindowState = FormWindowState.Normal;
			FormBorderStyle = FormBorderStyle.FixedDialog; // no min/max buttons as wanted

			StartPosition = FormStartPosition.CenterParent;

			AutoSize = true;
			AutoSizeMode = AutoSizeMode.GrowAndShrink;

			if (string.IsNullOrEmpty(title))
				Text = "Choose running process – " + Taskmaster.Name;
			else
				Text = title;

			#region Build UI
			var layout = new Extensions.TableLayoutPanel()
			{
				ColumnCount = 1,
				AutoSize = true,
				Dock = DockStyle.Fill
			};

			selection = new ComboBox()
			{
				DropDownStyle = ComboBoxStyle.DropDown,
				AutoCompleteSource = AutoCompleteSource.ListItems,
				AutoCompleteMode = AutoCompleteMode.SuggestAppend,
				AutoSize = true,
				Dock = DockStyle.Top,
				//Width = 160,
			};

			if (!string.IsNullOrEmpty(message))
			{
				layout.Controls.Add(new UI.AlignedLabel() { Text = message, AutoSize = true, Dock = DockStyle.Fill, Padding = BigPadding });
			}

			layout.Controls.Add(selection);

			var buttonlayout = new Extensions.TableLayoutPanel()
			{
				ColumnCount = 3,
				RowCount = 1,
				AutoSize = true,
				Dock = DockStyle.Top,
			};

			selectbutton = new Extensions.Button()
			{
				Text = "Select",
				AutoSize = true,
				Dock = DockStyle.Top,
				Enabled = false,
			};
			selectbutton.Click += SaveSelection;
			selection.KeyDown += (_, ea) =>
			{
				if (ea.KeyCode == Keys.Enter)
					SaveSelection(this, ea);
			};
			cancelbutton = new Extensions.Button()
			{
				Text = "Cancel",
				AutoSize = true,
				Dock = DockStyle.Top,
			};
			cancelbutton.Click += (_, _ea) =>
			{
				DialogResult = DialogResult.Abort;
				Close();
			};

			refreshbutton = new Extensions.Button()
			{
				Text = "Refresh",
				AutoSize = true,
				Enabled = false,
				Dock = DockStyle.Top,
			};
			refreshbutton.Click += (_, _ea) =>
			{
				selectbutton.Enabled = false;
				refreshbutton.Enabled = false;
				selection.Enabled = false;
				selection.Items.Clear();
				selection.Text = "[[ Refreshing ]]";

				Populate().ConfigureAwait(false);
			};
			selection.Text = "[[ Scanning ]]";
			selection.Enabled = false;

			buttonlayout.Controls.Add(selectbutton);
			buttonlayout.Controls.Add(refreshbutton);
			buttonlayout.Controls.Add(cancelbutton);
			buttonlayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
			buttonlayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
			buttonlayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));

			layout.Controls.Add(buttonlayout);

			Controls.Add(layout);
			#endregion // Build UI

			Shown += (_, _ea) => Populate().ConfigureAwait(false);

			ResumeLayout();
		}

		async Task Populate()
		{
			await Task.Delay(100).ConfigureAwait(false);

			try
			{
				var procs = System.Diagnostics.Process.GetProcesses(); // TODO: Hook to ProcessManager.ScanEverything somehow

				foreach (var proc in procs)
				{
					try
					{
						int pid = proc.Id;
						string name = proc.ProcessName;

						if (Process.Utility.SystemProcessId(pid)) continue;
						if (processmanager.IgnoreProcessName(name)) continue;

						if (Process.Utility.GetInfo(pid, out var info, proc, name: proc.ProcessName, getPath: true))
						{
							info.PriorityProtected = processmanager.ProtectedProcess(info.Name, info.Path);
							info.AffinityProtected = (info.PriorityProtected && processmanager.ProtectionLevel >= 2);
							InfoList.Add(info);
						}
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

				InfoList = InfoList.OrderBy(info => info.Name).ThenBy(info => info.Id).ToList();
				var output = InfoList.ConvertAll(info => $"{System.IO.Path.GetFileName(info.Path ?? info.Name)} #{info.Id}{(info.PriorityProtected ? " [Protected]" : "")}");

				if (IsDisposed) return;

				BeginInvoke(new Action(() =>
				{
					selection.Text = "";
					selection.Enabled = true;

					selection.Items.AddRange(output.ToArray());
					selectbutton.Enabled = true;
					refreshbutton.Enabled = true;
				}));
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				DialogResult = DialogResult.Abort;
				BeginInvoke(new Action(() => Close()));
			}
		}

		void SaveSelection(object _, EventArgs _ea)
		{
			try
			{
				int off = selection.SelectedIndex;
				var info = InfoList[off];

				Info = info;
				DialogResult = DialogResult.OK;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}

			Close();
		}

		#region IDisposable
		bool disposed = false;

		protected void Dispose(bool disposing)
		{
			if (disposed) return;
			disposed = true;

			if (disposing)
			{

				cancelbutton.Dispose();
				refreshbutton.Dispose();
				selectbutton.Dispose();
				selection.Dispose();
			}

			base.Dispose(disposing);
		}
		#endregion
	}
}