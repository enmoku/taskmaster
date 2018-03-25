//
// WatchlistEditWindow.cs
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
using System.Windows.Forms;
using Serilog;

namespace Taskmaster
{
	sealed public class WatchlistEditWindow : Form
	{
		public ProcessController Controller;

		bool newPrc = false;

		// Adding
		public WatchlistEditWindow()
		{
			DialogResult = DialogResult.Abort;

			Controller = new ProcessController("Unnamed", ProcessPriorityClass.Normal, 0)
			{
				Enabled = true,
				Valid = true,
			};

			newPrc = true;

			WindowState = FormWindowState.Normal;
			FormBorderStyle = FormBorderStyle.FixedDialog; // no min/max buttons as wanted
			MinimizeBox = false;
			MaximizeBox = false;

			BuildUI();
		}

		// Editingg
		public WatchlistEditWindow(ProcessController controller)
		{
			DialogResult = DialogResult.Abort;

			Controller = controller;

			if (Controller == null) throw new ArgumentException(string.Format("{0} not found in watchlist.", Controller.FriendlyName));

			WindowState = FormWindowState.Normal;
			FormBorderStyle = FormBorderStyle.FixedDialog; // no min/max buttons as wanted
			MinimizeBox = false;
			MaximizeBox = false;

			BuildUI();
		}

		void SaveInfo(object sender, System.EventArgs ev)
		{
			var enOrig = Controller.Enabled;
			Controller.Enabled = false;

			// TODO: VALIDATE FOR GRIMMY'S SAKE!
			// TODO: Foreground/Powermode need to be informed of any relevant changes.

			// -----------------------------------------------
			// VALIDATE

			var fnlen = (friendlyName.Text.Length > 0);
			var exnam = (execName.Text.Length > 0);
			var path = (pathName.Text.Length > 0);

			if (!path && !exnam)
			{
				Controller.Valid = false;
				MessageBox.Show("No path nor executable defined.", "Configuration error", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
			}

			if ((rescanFreq.Value > 0) && !exnam)
			{
				Controller.Valid = false;
				MessageBox.Show("Rescan requires executable to be defined.", "Configuration error", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
			}

			var dprc = Taskmaster.processmanager.getWatchedController(friendlyName.Text);
			if (dprc != null && dprc != Controller)
			{
				Controller.Valid = false;
				MessageBox.Show("Friendly Name conflict.", "Configuration error", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
			}

			if (!Controller.Valid)
			{
				Log.Warning("[{FriendlyName}] Can't save, configuration invalid.", friendlyName.Text);
				return;
			}

			// -----------------------------------------------

			if (!newPrc)
				Controller.DeleteConfig();

			Controller.FriendlyName = friendlyName.Text.Trim();
			Controller.Executable = execName.Text.Length > 0 ? execName.Text.Trim() : null;
			Controller.Path = pathName.Text.Length > 0 ? pathName.Text.Trim() : null;
			Controller.Priority = ProcessHelpers.IntToPriority(priorityClass.SelectedIndex); // is this right?
			Controller.Increase = increasePrio.Checked;
			Controller.Decrease = decreasePrio.Checked;
			Controller.Affinity = new IntPtr(cpumask == 0 ? ProcessManager.allCPUsMask : cpumask);
			Controller.PowerPlan = PowerManager.GetModeByName(powerPlan.Text);
			if (Controller.PowerPlan == PowerInfo.PowerMode.Custom) Controller.PowerPlan = PowerInfo.PowerMode.Undefined;
			Controller.Rescan = Convert.ToInt32(rescanFreq.Value);
			Controller.AllowPaging = allowPaging.Checked;
			Controller.ForegroundOnly = foregroundOnly.Checked;

			if (ignorelist.Items.Count > 0)
			{
				List<string> ignlist = new List<string>();
				foreach (ListViewItem item in ignorelist.Items)
					ignlist.Add(item.Text);


				Controller.IgnoreList = ignlist.ToArray();
			}
			else
				Controller.IgnoreList = null;

			Controller.Enabled = enOrig;
			Controller.SaveConfig();

			DialogResult = DialogResult.OK;

			Close();
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
		ListView ignorelist = new ListView();
		int cpumask = 0;

		void BuildUI()
		{
			// Size = new System.Drawing.Size(340, 480); // width, height
			AutoSizeMode = AutoSizeMode.GrowOnly;
			AutoSize = true;

			Text = string.Format("{0} ({1}) – {2}",
								 Controller.FriendlyName,
								 (Controller.Executable ?? Controller.Path),
								 System.Windows.Forms.Application.ProductName);
			Padding = new Padding(12);

			var tooltip = new ToolTip();

			var lt = new TableLayoutPanel
			{
				Parent = this,
				ColumnCount = 3,
				//lrows.RowCount = 10;
				Dock = DockStyle.Fill,
				AutoSize = true,
			};

			lt.Controls.Add(new Label { Text = "Friendly name", TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
			friendlyName.Text = Controller.FriendlyName;
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

			lt.Controls.Add(new Label()); // empty

			// EXECUTABLE
			lt.Controls.Add(new Label { Text = "Executable", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Dock = DockStyle.Left });
			execName.Text = Controller.Executable;
			execName.Width = 180;
			tooltip.SetToolTip(execName, "Executable name, used to recognize these applications.\nFull filename, including extension if any.");
			var findexecbutton = new Button()
			{
				Text = "Running",
				AutoSize = true,
				//Dock = DockStyle.Left,
				//Width = 46,
				//Height = 20,
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
			lt.Controls.Add(execName);
			lt.Controls.Add(findexecbutton);

			// PATH
			lt.Controls.Add(new Label { Text = "Path", TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
			pathName.Text = Controller.Path;
			pathName.Width = 180;
			tooltip.SetToolTip(pathName, "Path name; rule will match only paths that include this, subfolders included.\nPartial matching is allowed.");
			var findpathbutton = new Button()
			{
				Text = "Locate",
				AutoSize = true,
				//Dock = DockStyle.Left,
				//Width = 46,
				//Height = 20,
			};
			findpathbutton.Click += (sender, e) =>
			{
				try
				{
					using (var folderdialog = new FolderBrowserDialog())
					{
						folderdialog.ShowNewFolderButton = false;
						folderdialog.RootFolder = Environment.SpecialFolder.MyComputer;
						var result = folderdialog.ShowDialog();
						if (result == DialogResult.OK && !string.IsNullOrEmpty(folderdialog.SelectedPath))
						{
							pathName.Text = folderdialog.SelectedPath;
						}
					}
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
				}
			};
			lt.Controls.Add(pathName);
			lt.Controls.Add(findpathbutton);

			// IGNORE

			ignorelist.View = View.Details;
			ignorelist.HeaderStyle = ColumnHeaderStyle.None;
			ignorelist.Width = 180;
			ignorelist.Columns.Add("Executable", -2);

			if (Controller.IgnoreList != null)
			{
				foreach (string item in Controller.IgnoreList)
					ignorelist.Items.Add(item);

			}

			var ignorelistmenu = new ContextMenuStrip();
			ignorelist.ContextMenuStrip = ignorelistmenu;
			ignorelistmenu.Items.Add(new ToolStripMenuItem("Add", null, (s, ev) =>
			{
				try
				{
					using (var rs = new TextInputBox("Filename:", "Ignore executable"))
					{
						rs.ShowDialog();
						if (rs.DialogResult == DialogResult.OK)
						{
							ignorelist.Items.Add(rs.Value);
						}
					}
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
				}
			}));
			ignorelistmenu.Items.Add(new ToolStripMenuItem("Remove", null, (s, ev) =>
			{
				if (ignorelist.SelectedItems.Count == 1)
					ignorelist.Items.Remove(ignorelist.SelectedItems[0]);
			}));

			lt.Controls.Add(new Label() { Text = "Ignore", TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
			lt.Controls.Add(ignorelist);
			lt.Controls.Add(new Label()); // empty

			// PRIORITY
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
			priorityClass.SelectedIndex = Controller.Priority.ToInt32();
			tooltip.SetToolTip(priorityClass, "CPU priority for the application.\nIf both increase and decrease are disabled, this has no effect.");
			var incdecpanel = new TableLayoutPanel()
			{
				ColumnCount = 4,
				AutoSize = true,
			};
			incdecpanel.Controls.Add(new Label() { Text = "Increase:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			increasePrio.Checked = Controller.Increase;
			increasePrio.AutoSize = true;
			incdecpanel.Controls.Add(increasePrio);
			incdecpanel.Controls.Add(new Label() { Text = "Decrease:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			decreasePrio.Checked = Controller.Decrease;
			decreasePrio.AutoSize = true;
			incdecpanel.Controls.Add(decreasePrio);
			priopanel.Controls.Add(priorityClass);
			priopanel.Controls.Add(incdecpanel);
			lt.Controls.Add(priopanel);
			lt.Controls.Add(new Label()); // empty

			// lt.Controls.Add(priorityClass);

			// AFFINITY
			lt.Controls.Add(new Label { Text = "Affinity", TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
			affinityMask.Width = 80;
			affinityMask.Maximum = ProcessManager.allCPUsMask;
			affinityMask.Minimum = 0;
			try
			{
				affinityMask.Value = (Controller.Affinity.ToInt32() == ProcessManager.allCPUsMask ? 0 : Controller.Affinity.ToInt32());
			}
			catch
			{
				affinityMask.Value = 0;
			}

			tooltip.SetToolTip(affinityMask, "CPU core afffinity as integer mask.\nEnter 0 to let OS manage this as normal.\nFull affinity is same as 0, there's no difference.\nExamples:\n14 = all but first core on quadcore.\n254 = all but first core on octocore.");

			// lt.Controls.Add(affinityMask);

			// ---------------------------------------------------------------------------------------------------------

			var afflayout = new TableLayoutPanel()
			{
				ColumnCount = 1,
				AutoSize = true,
			};

			afflayout.Controls.Add(affinityMask);

			var corelayout = new TableLayoutPanel()
			{
				ColumnCount = 8,
				AutoSize = true,
			};

			var list = new List<CheckBox>();

			cpumask = Controller.Affinity.ToInt32();
			for (int bit = 0; bit < ProcessManager.CPUCount; bit++)
			{
				var box = new CheckBox();
				var bitoff = bit;
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

			afflayout.Controls.Add(corelayout);

			var buttonpanel = new TableLayoutPanel()
			{
				ColumnCount = 1,
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
			buttonpanel.Controls.Add(allbutton);
			buttonpanel.Controls.Add(clearbutton);

			affinityMask.ValueChanged += (sender, e) =>
			{
				var bitoff = 0;
				try { cpumask = (int)affinityMask.Value; }
				catch { cpumask = 0; affinityMask.Value = 0; }
				foreach (var bu in list)
					bu.Checked = ((cpumask & (1 << bitoff++)) != 0);
			};

			lt.Controls.Add(afflayout);
			lt.Controls.Add(buttonpanel);

			// ---------------------------------------------------------------------------------------------------------

			// RESCAN
			lt.Controls.Add(new Label { Text = "Rescan frequency", TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
			rescanFreq.Minimum = 0;
			rescanFreq.Maximum = 60 * 24;
			rescanFreq.Value = Controller.Rescan;
			rescanFreq.Width = 80;
			tooltip.SetToolTip(rescanFreq, "How often to rescan for this app, in minutes.\nSometimes instances slip by.");
			lt.Controls.Add(rescanFreq);
			lt.Controls.Add(new Label()); // empty

			// lt.Controls.Add(new Label { Text="Children"});
			// lt.Controls.Add(new Label { Text="Child priority"});

			// POWER
			lt.Controls.Add(new Label { Text = "Power plan", TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
			foreach (string t in PowerManager.PowerModes)
				powerPlan.Items.Add(t);
			var ppi = System.Convert.ToInt32(Controller.PowerPlan);
			powerPlan.DropDownStyle = ComboBoxStyle.DropDownList;
			powerPlan.SelectedIndex = System.Math.Min(ppi, 3);
			powerPlan.Width = 180;
			tooltip.SetToolTip(powerPlan, "Power Mode to be used when this application is detected. Leaving this undefined disables it.");
			lt.Controls.Add(powerPlan);
			lt.Controls.Add(new Label()); // empty

			// FOREGROUND
			lt.Controls.Add(new Label { Text = "Foreground only", TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
			foregroundOnly.Checked = Controller.ForegroundOnly;
			tooltip.SetToolTip(foregroundOnly, "Lower priority and power mode is restored when this app is not in focus.");
			lt.Controls.Add(foregroundOnly);
			lt.Controls.Add(new Label()); // empty

			// TODO: Add modifying background priority

			// PAGING
			lt.Controls.Add(new Label { Text = "Allow paging", TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
			allowPaging.Checked = Controller.AllowPaging;
			tooltip.SetToolTip(allowPaging, "Allow this application to be paged when it is requested.");
			lt.Controls.Add(allowPaging);
			lt.Controls.Add(new Label()); // empty

			// lt.Controls.Add(new Label { Text=""})

			var finalizebuttons = new TableLayoutPanel() { ColumnCount = 2, AutoSize = true };
			var saveButton = new Button(); // SAVE
			saveButton.Text = "Save";
			saveButton.Click += SaveInfo;
			finalizebuttons.Controls.Add(saveButton);
			// lt.Controls.Add(saveButton);
			var cancelButton = new Button(); // CLOSE
			cancelButton.Text = "Cancel";
			cancelButton.Click += (sender, e) =>
			{
				DialogResult = DialogResult.Cancel;
				Close();
			};
			finalizebuttons.Controls.Add(cancelButton);

			var validatebutton = new Button()
			{
				Text = "Validate"
			};
			validatebutton.Click += ValidateWatchedItem;
			validatebutton.Margin = new Padding(6);

			lt.Controls.Add(validatebutton);

			lt.Controls.Add(finalizebuttons);

			// ---
		}

		void ValidateWatchedItem(object sender, EventArgs ev)
		{
			var fnlen = (friendlyName.Text.Length > 0);
			var exnam = (execName.Text.Length > 0);
			var path = (pathName.Text.Length > 0);

			var exfound = false;
			if (exnam)
			{
				var procs = Process.GetProcessesByName(execName.Text);

				if (procs.Length > 0) exfound = true;
			}

			var pfound = false;
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

			var samesection = Controller.FriendlyName.Equals(friendlyName.Text);
			if (!samesection)
			{
				var dprc = Taskmaster.processmanager.getWatchedController(friendlyName.Text);
				if (dprc != null)
				{
					sbs.Append("Friendly name conflict!");
				}
			}

			if (execName.Text.Length > 0)
				sbs.Append("Executable: ").Append(exnam ? "OK" : "Fail").Append(" – Found: ").Append(exfound).AppendLine();
			if (pathName.Text.Length > 0)
				sbs.Append("Path: ").Append(path ? "OK" : "Fail").Append(" - Found: ").Append(pfound).AppendLine();

			if (!exnam && !path)
				sbs.Append("Both path and executable are missing!").AppendLine();

			if ((rescanFreq.Value > 0) && !exnam)
				sbs.Append("Rescan frequency REQUIRES executable to be defined.").AppendLine();
			if (increasePrio.Checked && decreasePrio.Checked)
				sbs.Append("Priority class is to be ignored.").AppendLine();
			if (ignorelist.Items.Count > 0 && execName.Text.Length > 0)
				sbs.Append("Ignore list is meaningless with executable defined.").AppendLine();

			MessageBox.Show(sbs.ToString(), "Validation results", MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, MessageBoxOptions.DefaultDesktopOnly, false);
		}
	}
}