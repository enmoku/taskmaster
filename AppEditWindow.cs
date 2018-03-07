//
// AppEditWindow.cs
//
// Author:
//       M.A. (enmoku) <>
//
// Copyright (c) 2018 M.A. (enmoku)
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

using Serilog;
using System.Windows.Forms;
using System.Diagnostics;
using System;
using System.Collections.Generic;

namespace TaskMaster
{
	public class AppEditWindow : Form
	{
		readonly ProcessController process;
		readonly ListViewItem item;

		// Adding
		public AppEditWindow()
		{

		}

		// Editingg
		public AppEditWindow(string name, ListViewItem ri)
		{
			item = ri;

			process = TaskMaster.processmanager.watchlist.Find((tcp) => tcp.FriendlyName == name);

			if (process == null) throw new ArgumentException(string.Format("{0} not found in watchlist.", name));

			WindowState = FormWindowState.Normal;
			FormBorderStyle = FormBorderStyle.FixedDialog; // no min/max buttons as wanted
			MinimizeBox = false;
			MaximizeBox = false;

			BuildUI();
		}

		void SaveInfo(object sender, System.EventArgs ev)
		{
			Log.Warning("CONVENIENT SAVING NOT SUPPORTED YET");

			var sbs = new System.Text.StringBuilder();

			sbs.Append("[").Append(friendlyName.Text).Append("]").AppendLine();
			if (execName.Text.Length > 0)
				sbs.Append("Image = ").Append(execName.Text).AppendLine();
			if (pathName.Text.Length > 0)
				sbs.Append("Path = ").Append(pathName.Text).AppendLine();
			sbs.Append("Priority = ").Append(priorityClass.SelectedIndex).AppendLine();
			sbs.Append("Increase = ").Append(increasePrio.Checked).AppendLine();
			sbs.Append("Decrease = ").Append(decreasePrio.Checked).AppendLine();
			sbs.Append("Affinity = ").Append(affinityMask.Value).AppendLine();
			if (powerPlan.SelectedIndex != 3)
				sbs.Append("Power plan = ").Append(powerPlan.Text).AppendLine();
			if (rescanFreq.Value > 0)
				sbs.Append("Rescan = ").Append(rescanFreq.Value).AppendLine();
			sbs.Append("Allow paging = ").Append(allowPaging.Checked).AppendLine();
			sbs.Append("Foreground only = ").Append(foregroundOnly.Checked).AppendLine();

			try
			{
				Clipboard.SetText(sbs.ToString());
				Log.Information("App configuration saved to clipboard, you can now replace it in watchlist.");
			}
			catch
			{
				Log.Warning("Failure to copy configuration to clipboard.");
			}
			sbs.Clear();
		}

		TextBox friendlyName = new TextBox();
		TextBox execName = new TextBox();
		TextBox pathName = new TextBox();
		ComboBox priorityClass;
		CheckBox increasePrio = new CheckBox();
		CheckBox decreasePrio = new CheckBox();
		NumericUpDown affinityMask = new NumericUpDown();
		NumericUpDown rescanFreq = new NumericUpDown();
		CheckBox allowPaging = new CheckBox();
		ComboBox powerPlan = new ComboBox();
		CheckBox foregroundOnly = new CheckBox();

		void BuildUI()
		{
			//Size = new System.Drawing.Size(340, 480); // width, height
			AutoSizeMode = AutoSizeMode.GrowOnly;
			AutoSize = true;

			Text = string.Format("{0} ({1}) – {2}",
								 process.FriendlyName,
								 (process.Executable ?? process.Path),
								 System.Windows.Forms.Application.ProductName);
			Padding = new Padding(12);

			var tooltip = new ToolTip();

			var lt = new TableLayoutPanel
			{
				Parent = this,
				ColumnCount = 2,
				//lrows.RowCount = 10;
				Dock = DockStyle.Fill,
				AutoSize = true,
			};

			lt.Controls.Add(new Label { Text = "Friendly name", TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
			friendlyName.Text = process.FriendlyName;
			friendlyName.Width = 180;
			friendlyName.CausesValidation = true;
			friendlyName.Validating += (sender, e) =>
			{
				if (friendlyName.Text.Contains("]") || friendlyName.Text.Length == 0)
				{
					e.Cancel = true;
					friendlyName.Select(0, friendlyName.Text.Length);
				}
			};
			tooltip.SetToolTip(friendlyName, "Human readable name, for user convenience.");
			lt.Controls.Add(friendlyName);
			lt.Controls.Add(new Label { Text = "Executable", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Dock = DockStyle.Left });
			var execpanel = new TableLayoutPanel() { ColumnCount = 2, AutoSize = true };
			execName.Text = process.Executable;
			execName.Width = 134;
			tooltip.SetToolTip(execName, "Executable name, used to recognize these applications.\nFull filename, including extension if any.");
			execpanel.Controls.Add(execName);
			var findexecbutton = new Button()
			{
				Text = "Find",
				//AutoSize = true,
				Dock = DockStyle.Left,
				Width = 36,
				Height = 20,
			};
			findexecbutton.Click += (sender, e) =>
			{
				using (var exselectdialog = new ProcessSelectDialog())
				{
					try
					{
						if (exselectdialog.ShowDialog(this) == DialogResult.OK)
						{
							// SANITY CHECK: exselectdialog.Selection;
							execName.Text = exselectdialog.Selection;
						}
					}
					catch (Exception ex)
					{
						Log.Fatal("{Type} : {Message}", ex.GetType().Name, ex.Message);
					}
				}
			};
			execpanel.Controls.Add(findexecbutton);
			lt.Controls.Add(execpanel);

			// PATH
			lt.Controls.Add(new Label { Text = "Path", TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
			pathName.Text = process.Path;
			pathName.Width = 180;
			tooltip.SetToolTip(pathName, "Path name; rule will match only paths that include this, subfolders included.\nPartial matching is allowed.");
			lt.Controls.Add(pathName);
			// system.windows.forms.folderbrowserdialog

			lt.Controls.Add(new Label { Text = "Priority class", TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
			var priopanel = new TableLayoutPanel()
			{
				ColumnCount = 1,
				AutoSize = true
			};
			priorityClass = new ComboBox
			{
				Dock = DockStyle.Left,
				DropDownStyle = ComboBoxStyle.DropDownList,
				Items = { "Idle", "Below Normal", "Normal", "Above Normal", "High" }, // System.Enum.GetNames(typeof(ProcessPriorityClass)), 
				SelectedIndex = 2
			};
			priorityClass.Width = 180;
			priorityClass.SelectedIndex = process.Priority.ToInt32();
			tooltip.SetToolTip(priorityClass, "CPU priority for the application.\nIf both increase and decrease are disabled, this has no effect.");
			var incdecpanel = new TableLayoutPanel()
			{
				ColumnCount = 4,
				AutoSize = true,
			};
			incdecpanel.Controls.Add(new Label() { Text = "Increase:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			increasePrio.Checked = process.Increase;
			increasePrio.AutoSize = true;
			incdecpanel.Controls.Add(increasePrio);
			incdecpanel.Controls.Add(new Label() { Text = "Decrease:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			decreasePrio.Checked = process.Decrease;
			decreasePrio.AutoSize = true;
			incdecpanel.Controls.Add(decreasePrio);
			priopanel.Controls.Add(priorityClass);
			priopanel.Controls.Add(incdecpanel);
			lt.Controls.Add(priopanel);
			//lt.Controls.Add(priorityClass);

			lt.Controls.Add(new Label { Text = "Affinity", TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
			affinityMask.Width = 80;
			affinityMask.Maximum = ProcessManager.allCPUsMask;
			affinityMask.Minimum = 0;
			try
			{
				affinityMask.Value = (process.Affinity.ToInt32() == ProcessManager.allCPUsMask ? 0 : process.Affinity.ToInt32());
			}
			catch
			{
				affinityMask.Value = 0;
			}

			tooltip.SetToolTip(affinityMask, "CPU core afffinity as integer mask.\nEnter 0 to let OS manage this as normal.\nFull affinity is same as 0, there's no difference.\nExamples:\n14 = all but first core on quadcore.\n254 = all but first core on octocore.");

			//lt.Controls.Add(affinityMask);

			// ---------------------------------------------------------------------------------------------------------

			var layout = new TableLayoutPanel()
			{
				ColumnCount = 1,
				AutoSize = true,
			};

			layout.Controls.Add(affinityMask);

			var corelayout = new TableLayoutPanel()
			{
				ColumnCount = 8,
				AutoSize = true,
			};

			var list = new List<CheckBox>();

			int cpumask = process.Affinity.ToInt32();
			for (int bit = 0; bit < ProcessManager.CPUCount; bit++)
			{
				var box = new CheckBox();
				int bitoff = bit;
				box.AutoSize = true;
				box.Checked = ((cpumask & (1 << bitoff)) != 0);
				box.CheckedChanged += (sender, e) =>
								{
									if (box.Checked)
									{
										cpumask |= (1 << bitoff);
										affinityMask.Value = cpumask;
									}
									else
									{
										cpumask &= ~(1 << bitoff);
										affinityMask.Value = cpumask;
									}
								};
				list.Add(box);
				corelayout.Controls.Add(new Label
				{
					Text = (bit + 1) + ":",
					AutoSize = true,
					//BackColor = System.Drawing.Color.LightPink,
					TextAlign = System.Drawing.ContentAlignment.MiddleLeft
				});
				corelayout.Controls.Add(box);
			}

			layout.Controls.Add(corelayout);

			var buttonpanel = new TableLayoutPanel()
			{
				ColumnCount = 2,
				AutoSize = true,
			};
			var clearbutton = new Button() { Text = "None" };
			clearbutton.Click += (sender, e) =>
						{
							foreach (var litem in list) litem.Checked = false;
						};
			var allbutton = new Button() { Text = "All" };
			allbutton.Click += (sender, e) =>
						{
							foreach (var litem in list) litem.Checked = true;
						};
			buttonpanel.Controls.Add(clearbutton);
			buttonpanel.Controls.Add(allbutton);
			layout.Controls.Add(buttonpanel);

			affinityMask.ValueChanged += (sender, e) =>
			{
				int bitoff = 0;
				try { cpumask = (int)affinityMask.Value; }
				catch { cpumask = 0; affinityMask.Value = 0; }
				foreach (var bu in list)
					bu.Checked = ((cpumask & (1 << bitoff++)) != 0);
			};

			lt.Controls.Add(layout);

			// ---------------------------------------------------------------------------------------------------------

			lt.Controls.Add(new Label { Text = "Rescan frequency", TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
			rescanFreq.Value = process.Rescan;
			rescanFreq.Width = 80;
			tooltip.SetToolTip(rescanFreq, "How often to rescan for this app, in minutes.\nSometimes instances slip by.");
			lt.Controls.Add(rescanFreq);

			//lt.Controls.Add(new Label { Text="Children"});
			//lt.Controls.Add(new Label { Text="Child priority"});

			lt.Controls.Add(new Label { Text = "Power plan", TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
			foreach (var t in PowerManager.PowerModes)
				powerPlan.Items.Add(t);
			int ppi = System.Convert.ToInt32(process.PowerPlan);
			powerPlan.DropDownStyle = ComboBoxStyle.DropDownList;
			powerPlan.SelectedIndex = System.Math.Max(ppi, 3);
			powerPlan.Width = 180;
			tooltip.SetToolTip(powerPlan, "Power Mode to be used when this application is detected.");
			lt.Controls.Add(powerPlan);

			lt.Controls.Add(new Label { Text = "Foreground only", TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
			foregroundOnly.Checked = process.ForegroundOnly;
			tooltip.SetToolTip(foregroundOnly, "Lower priority and power mode is restored when this app is not in focus.");
			lt.Controls.Add(foregroundOnly);

			lt.Controls.Add(new Label { Text = "Allow paging", TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
			allowPaging.Checked = process.AllowPaging;
			tooltip.SetToolTip(allowPaging, "Allow this application to be paged when it is requested.");
			lt.Controls.Add(allowPaging);

			//lt.Controls.Add(new Label { Text=""})

			var finalizebuttons = new TableLayoutPanel() { ColumnCount = 2, AutoSize = true };
			var saveButton = new Button(); // SAVE
			saveButton.Text = "Save";
			saveButton.Click += SaveInfo;
			finalizebuttons.Controls.Add(saveButton);
			//lt.Controls.Add(saveButton);
			var closeButton = new Button(); // CLOSE
			closeButton.Text = "Close";
			closeButton.Click += (sender, e) => { Close(); };
			finalizebuttons.Controls.Add(closeButton);

			var validatebutton = new Button();
			validatebutton.Text = "Validate";
			validatebutton.Click += ValidateWatchedItem;
			validatebutton.Margin = new Padding(6);

			lt.Controls.Add(validatebutton);

			lt.Controls.Add(finalizebuttons);

			// ---
		}

		void ValidateWatchedItem(object sender, EventArgs ev)
		{
			bool fnlen = (friendlyName.Text.Length > 0);
			bool exnam = (execName.Text.Length > 0);
			bool path = (pathName.Text.Length > 0);

			bool exfound = false;
			if (exnam)
			{
				var procs = Process.GetProcessesByName(execName.Text);

				if (procs.Length > 0)
					exfound = true;
			}

			bool pfound = false;
			if (path)
			{
				try
				{
					pfound = System.IO.Directory.Exists(pathName.Text);
				}
				catch
				{
					// NOP, don't caree
				}
			}

			var sbs = new System.Text.StringBuilder();
			sbs.Append("Name: ").Append(fnlen ? "OK" : "Fail").AppendLine();
			if (execName.Text.Length > 0)
				sbs.Append("Executable: ").Append(exnam ? "OK" : "Fail").Append(" – Found: ").Append(exfound).AppendLine();
			if (pathName.Text.Length > 0)
				sbs.Append("Path: ").Append(path ? "OK" : "Fail").Append(" - Found: ").Append(pfound).AppendLine();
			if ((rescanFreq.Value > 0) && !exnam)
				sbs.Append("Rescan frequency REQUIRES executable to be defined.").AppendLine();
			if (increasePrio.Checked && decreasePrio.Checked)
				sbs.Append("Priority class is to be ignored.").AppendLine();

			MessageBox.Show(sbs.ToString(), "Validation results", MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, MessageBoxOptions.DefaultDesktopOnly, false);
		}
	}
}
