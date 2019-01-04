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
	sealed public class WatchlistEditWindow : UI.UniForm
	{
		public ProcessController Controller;

		bool newPrc = false;

		// Adding
		public WatchlistEditWindow()
		{
			DialogResult = DialogResult.Abort;

			Controller = new ProcessController("Unnamed")
			{
				Enabled = true,
			};

			newPrc = true;

			WindowState = FormWindowState.Normal;
			FormBorderStyle = FormBorderStyle.FixedDialog; // no min/max buttons as wanted

			BuildUI();
		}

		// Editingg
		public WatchlistEditWindow(ProcessController controller)
		{
			DialogResult = DialogResult.Abort;

			Controller = controller;

			StartPosition = FormStartPosition.CenterParent;

			if (Controller == null) throw new ArgumentException(Controller.FriendlyName + " not found in watchlist.");

			WindowState = FormWindowState.Normal;
			FormBorderStyle = FormBorderStyle.FixedDialog; // no min/max buttons as wanted
			MinimizeBox = false;
			MaximizeBox = false;

			BuildUI();
		}

		void SaveInfo(object _, System.EventArgs _ea)
		{
			var enOrig = Controller.Enabled;
			Controller.Enabled = false;
			
			// TODO: VALIDATE FOR GRIMMY'S SAKE!

			// -----------------------------------------------
			// VALIDATE

			bool fnlen = (friendlyName.Text.Length > 0);
			bool exnam = (execName.Text.Length > 0);
			bool path = (pathName.Text.Length > 0);

			bool valid = true;

			if (!fnlen || friendlyName.Text.Contains("]") || friendlyName.Text.Contains("["))
			{
				valid = false;
				MessageBox.Show("Friendly name is missing or includes illegal characters (such as square brackets).", "Malconfigured friendly name", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
			}

			if (!path && !exnam)
			{
				valid = false;
				MessageBox.Show("No path nor executable defined.", "Configuration error", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
			}

			var dprc = Taskmaster.processmanager.getWatchedController(friendlyName.Text);
			if (dprc != null && dprc != Controller)
			{
				valid = false;
				MessageBox.Show("Friendly Name conflict.", "Configuration error", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
			}

			if (!valid)
			{
				Log.Warning("[" + friendlyName.Text + "] Can't save, configuration invalid.");
				return;
			}

			// -----------------------------------------------

			string newfriendlyname = friendlyName.Text.Trim();

			if (!newPrc && !newfriendlyname.Equals(Controller.FriendlyName))
				Controller.DeleteConfig(); // SharpConfig doesn't seem to support renaming sections, so we delete the old one instead

			Controller.FriendlyName = newfriendlyname;
			Controller.Executable = execName.Text.Length > 0 ? execName.Text.Trim() : null;
			Controller.Path = pathName.Text.Length > 0 ? pathName.Text.Trim() : null;
			if (priorityClass.SelectedIndex == 5) // ignored
			{
				Controller.Priority = null;
				Controller.PriorityStrategy = ProcessPriorityStrategy.None;
			}
			else
			{
				Controller.Priority = ProcessHelpers.IntToPriority(priorityClass.SelectedIndex); // is this right?
				Controller.PriorityStrategy = ProcessPriorityStrategy.None;
				switch (priorityClassMethod.SelectedIndex)
				{
					case 0: Controller.PriorityStrategy = ProcessPriorityStrategy.Increase; break;
					case 1: Controller.PriorityStrategy = ProcessPriorityStrategy.Decrease; break;
					default:
					case 2: Controller.PriorityStrategy = ProcessPriorityStrategy.Force; break;
				}
			}

			if (affstrategy.SelectedIndex != 0)
			{
				if (cpumask == -1)
					Controller.AffinityMask = -1;
				else
				{
					Controller.AffinityMask = cpumask;
					Controller.AffinityStrategy = affstrategy.SelectedIndex == 1 ? ProcessAffinityStrategy.Limit : ProcessAffinityStrategy.Force;
				}
			}
			else
			{
				// strategy = ignore
				Controller.AffinityMask = -1;
				Controller.AffinityStrategy = ProcessAffinityStrategy.None;
			}

			Controller.ModifyDelay = (int)(modifyDelay.Value * 1000);
			Controller.PowerPlan = PowerManager.GetModeByName(powerPlan.Text);
			Controller.AllowPaging = allowPaging.Checked;
			Controller.SetForegroundOnly(foregroundOnly.Checked);
			Controller.BackgroundPowerdown = Controller.ForegroundOnly && backgroundPowerdown.Checked;

			if (bgPriorityClass.SelectedIndex != 5)
				Controller.BackgroundPriority = ProcessHelpers.IntToPriority(bgPriorityClass.SelectedIndex);
			else
				Controller.BackgroundPriority = null;

			if (bgAffinityMask.Value >= 0)
				Controller.BackgroundAffinity = Convert.ToInt32(bgAffinityMask.Value);
			else
				Controller.BackgroundAffinity = -1;

			if (ignorelist.Items.Count > 0)
			{
				List<string> ignlist = new List<string>();
				foreach (ListViewItem item in ignorelist.Items)
					ignlist.Add(item.Text);

				Controller.IgnoreList = ignlist.ToArray();
			}
			else
				Controller.IgnoreList = null;

			if (desc.Text.Length > 0)
				Controller.Description = desc.Text;

			if (Taskmaster.AudioManagerEnabled && volumeMethod.SelectedIndex != 5)
			{
				switch (volumeMethod.SelectedIndex)
				{
					case 0: Controller.VolumeStrategy = AudioVolumeStrategy.Increase; break;
					case 1: Controller.VolumeStrategy = AudioVolumeStrategy.Decrease; break;
					case 2: Controller.VolumeStrategy = AudioVolumeStrategy.IncreaseFromMute; break;
					case 3: Controller.VolumeStrategy = AudioVolumeStrategy.DecreaseFromFull; break;
					case 4: Controller.VolumeStrategy = AudioVolumeStrategy.Force; break;
					default: Controller.VolumeStrategy = AudioVolumeStrategy.Ignore; break;
				}

				Controller.Volume = Convert.ToSingle(volume.Value) / 100f;
			}

			Controller.Enabled = enOrig;

			Controller.SanityCheck();

			Controller.SaveConfig();

			Log.Information("[" + Controller.FriendlyName + "] " + (newPrc ? "Created" : "Modified"));

			DialogResult = DialogResult.OK;

			Controller.Refresh();

			Close();
		}

		TextBox friendlyName = null;
		TextBox execName = null;
		TextBox pathName = null;
		TextBox desc = null;

		ComboBox priorityClass = null;
		ComboBox priorityClassMethod = null;
		ComboBox bgPriorityClass = null;

		ComboBox affstrategy = new ComboBox();
		NumericUpDown affinityMask = null;
		NumericUpDown bgAffinityMask = null;

		ComboBox volumeMethod = null;
		Extensions.NumericUpDownEx volume = null;

		Button allbutton = new Button();
		Button clearbutton = new Button();
		Extensions.NumericUpDownEx modifyDelay = null;
		CheckBox allowPaging = new CheckBox();
		ComboBox powerPlan = new ComboBox();
		CheckBox foregroundOnly = new CheckBox();
		CheckBox backgroundPowerdown = new CheckBox();
		ListView ignorelist = new UI.ListViewEx();
		
		int cpumask = 0;

		void BuildUI()
		{
			// Size = new System.Drawing.Size(340, 480); // width, height
			AutoSizeMode = AutoSizeMode.GrowOnly;
			AutoSize = true;

			friendlyName = new TextBox() { ShortcutsEnabled = true };
			execName = new TextBox() { ShortcutsEnabled = true };
			pathName = new TextBox() { ShortcutsEnabled = true };

			Text = Controller.FriendlyName + " (" + (Controller.Executable ?? Controller.Path) + ") – " + Application.ProductName;

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
			lt.Controls.Add(new Label { Text = HumanReadable.System.Process.Executable, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Dock = DockStyle.Left });
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
						Log.Fatal(ex.GetType().Name + " : " + ex.Message);
					}
				}
			};
			lt.Controls.Add(execName);
			lt.Controls.Add(findexecbutton);

			// PATH
			lt.Controls.Add(new Label { Text = HumanReadable.System.Process.Path, TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
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

			// DESCRIPTION
			lt.Controls.Add(new Label() { Text = HumanReadable.Generic.Description, TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
			desc = new TextBox()
			{
				Multiline = false,
				Dock = DockStyle.Top,
				ShortcutsEnabled = true,
			};
			desc.Text = Controller.Description;
			lt.Controls.Add(desc);
			lt.Controls.Add(new Label()); // empty

			// IGNORE

			ignorelist.View = View.Details;
			ignorelist.HeaderStyle = ColumnHeaderStyle.None;
			ignorelist.Width = 180;
			ignorelist.Columns.Add(HumanReadable.System.Process.Executable, -2);
			tooltip.SetToolTip(ignorelist, "Executables to ignore for matching with this rule.\nOnly exact matches work.\n\nRequires path to be defined.");

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

			lt.Controls.Add(new Label() { Text = HumanReadable.Generic.Ignore, TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
			lt.Controls.Add(ignorelist);
			lt.Controls.Add(new Label()); // empty

			var priorities = new string[] { "Low", "Below Normal", "Normal", "Above Normal", "High", HumanReadable.Generic.Ignore };

			// PRIORITY
			lt.Controls.Add(new Label { Text = HumanReadable.System.Process.PriorityClass, TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
			priorityClass = new ComboBox
			{
				Dock = DockStyle.Left,
				DropDownStyle = ComboBoxStyle.DropDownList,
			};
			priorityClass.Items.AddRange(priorities);
			priorityClass.SelectedIndex = 2;

			priorityClass.Width = 180;
			priorityClass.SelectedIndex = Controller.Priority?.ToInt32() ?? 5;
			tooltip.SetToolTip(priorityClass, "CPU priority for the application.\nIf both increase and decrease are disabled, this has no effect.");

			priorityClassMethod = new ComboBox
			{
				Dock = DockStyle.Left,
				DropDownStyle = ComboBoxStyle.DropDownList,
				Items = { "Increase only", "Decrease only", "Bidirectional" },
				SelectedIndex = 2,
			};

			switch (Controller.PriorityStrategy)
			{
				case ProcessPriorityStrategy.Increase: priorityClassMethod.SelectedIndex = 0; break;
				case ProcessPriorityStrategy.Decrease: priorityClassMethod.SelectedIndex = 1; break;
				default: priorityClassMethod.SelectedIndex = 2; break;
			}

			lt.Controls.Add(priorityClass);
			lt.Controls.Add(priorityClassMethod);

			priorityClass.SelectedIndexChanged += (s, e) => priorityClassMethod.Enabled = priorityClass.SelectedIndex != 5; // disable method selection

			// AFFINITY

			var corelist = new List<CheckBox>();

			lt.Controls.Add(new Label { Text = HumanReadable.System.Process.Affinity, TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
			affstrategy.DropDownStyle = ComboBoxStyle.DropDownList;
			affstrategy.Items.AddRange(new string[] { HumanReadable.Generic.Ignore, "Limit (Default)", "Force" });
			tooltip.SetToolTip(affstrategy, "Limit constrains cores to the defined range but does not increase used cores beyond what the app is already using.\nForce sets the affinity mask to the defined regardless of anything.");
			affstrategy.SelectedIndexChanged += (s, e) =>
			{
				bool enabled = affstrategy.SelectedIndex != 0;
				affinityMask.Enabled = enabled;
				foreach (var box in corelist)
					box.Enabled = enabled;
				allbutton.Enabled = enabled;
				clearbutton.Enabled = enabled;
			};

			lt.Controls.Add(affstrategy);
			lt.Controls.Add(new Label()); // empty, right

			lt.Controls.Add(new Label() { Text = "Affinity mask\n&& Cores", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize=true }); // left
			affinityMask = new NumericUpDown()
			{
				Width = 80,
				Maximum = ProcessManager.AllCPUsMask,
				Minimum = -1,
				Value = Controller.AffinityMask.Min(-1).Replace(ProcessManager.AllCPUsMask, 0),
			};

			tooltip.SetToolTip(affinityMask, "CPU core afffinity as integer mask.\nEnter 0 to let OS manage this as normal.\nFull affinity is same as 0, there's no difference.\nExamples:\n14 = all but first core on quadcore.\n254 = all but first core on octocore.\n-1 = Ignored");

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

			cpumask = Controller.AffinityMask.Replace(ProcessManager.AllCPUsMask, 0);
			for (int bit = 0; bit < ProcessManager.CPUCount; bit++)
			{
				var box = new CheckBox();
				var bitoff = bit;
				box.AutoSize = true;
				box.Checked = ((Math.Max(0, cpumask) & (1 << bitoff)) != 0);
				box.CheckedChanged += (sender, e) =>
				{
					if (cpumask < 0) cpumask = 0;

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
				corelist.Add(box);
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

			var affbuttonpanel = new TableLayoutPanel()
			{
				ColumnCount = 1,
				AutoSize = true,
			};
			clearbutton.Text = "None";
			clearbutton.Click += (sender, e) =>
			{
				foreach (var litem in corelist) litem.Checked = false;
			};
			allbutton.Text = "All";
			allbutton.Click += (sender, e) =>
			{
				foreach (var litem in corelist) litem.Checked = true;
			};
			affbuttonpanel.Controls.Add(allbutton);
			affbuttonpanel.Controls.Add(clearbutton);

			affinityMask.ValueChanged += (sender, e) =>
			{
				var bitoff = 0;
				try { cpumask = (int)affinityMask.Value; }
				catch { cpumask = 0; affinityMask.Value = 0; }
				foreach (var bu in corelist)
					bu.Checked = ((Math.Max(0, cpumask) & (1 << bitoff++)) != 0);
			};

			switch (Controller.AffinityStrategy)
			{
				case ProcessAffinityStrategy.Force: affstrategy.SelectedIndex = 2; break;
				default:
				case ProcessAffinityStrategy.Limit: affstrategy.SelectedIndex = 1; break;
				case ProcessAffinityStrategy.None: affstrategy.SelectedIndex = 0; break;
			}

			lt.Controls.Add(afflayout);
			lt.Controls.Add(affbuttonpanel);

			// ---------------------------------------------------------------------------------------------------------

			// FOREGROUND

			lt.Controls.Add(new Label { Text = "Foreground only", TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
			foregroundOnly.Checked = Controller.ForegroundOnly;
			tooltip.SetToolTip(foregroundOnly, "Priority and affinity are lowered when this app is not in focus.");
			lt.Controls.Add(foregroundOnly);
			lt.Controls.Add(new Label()); // empty

			// BACKGROUND PRIORITY & AFFINITY

			lt.Controls.Add(new Label { Text = "Background priority", TextAlign = System.Drawing.ContentAlignment.MiddleLeft });

			bgPriorityClass = new ComboBox
			{
				Dock = DockStyle.Left,
				DropDownStyle = ComboBoxStyle.DropDownList,
			};
			bgPriorityClass.Items.AddRange(priorities);
			bgPriorityClass.SelectedIndex = 5;

			bgPriorityClass.Width = 180;
			if (Controller.BackgroundPriority.HasValue)
				bgPriorityClass.SelectedIndex = Controller.BackgroundPriority.Value.ToInt32();
			tooltip.SetToolTip(bgPriorityClass, "Same as normal priority.\nIgnored causes priority to be untouched.");

			lt.Controls.Add(bgPriorityClass);
			lt.Controls.Add(new Label()); // empty

			lt.Controls.Add(new Label { Text = "Background affinity", TextAlign = System.Drawing.ContentAlignment.MiddleLeft });

			bgAffinityMask = new NumericUpDown()
			{
				Width = 80,
				Maximum = ProcessManager.AllCPUsMask,
				Minimum = -1,
				Value = Controller.BackgroundAffinity,
			};
			tooltip.SetToolTip(bgAffinityMask, "Same as normal affinity.\nStrategy is 'force' only for this.\n-1 causes affinity to be untouched.");

			lt.Controls.Add(bgAffinityMask); // empty
			lt.Controls.Add(new Label()); // empty

			// ---------------------------------------------------------------------------------------------------------

			// lt.Controls.Add(new Label { Text="Children"});
			// lt.Controls.Add(new Label { Text="Child priority"});

			// MODIFY DELAY

			lt.Controls.Add(new Label() { Text = "Modify delay", TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
			modifyDelay = new Extensions.NumericUpDownEx()
			{
				Unit = "s",
				DecimalPlaces = 1,
				Minimum = 0,
				Maximum = 180,
				Width = 80,
				Value = Controller.ModifyDelay / 1000.0M,
			};
			tooltip.SetToolTip(modifyDelay, "Delay before the process is actually attempted modification.\nEither to keep original priority for a short while, or to counter early self-adjustment.\nThis is also applied to foreground only limited modifications.");
			lt.Controls.Add(modifyDelay);
			lt.Controls.Add(new Label()); // empty

			// POWER
			lt.Controls.Add(new Label { Text = HumanReadable.Hardware.Power.Plan, TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
			powerPlan.Items.AddRange(new string[] {
				PowerManager.GetModeName(PowerInfo.PowerMode.HighPerformance),
				PowerManager.GetModeName(PowerInfo.PowerMode.Balanced),
				PowerManager.GetModeName(PowerInfo.PowerMode.PowerSaver),
				PowerManager.GetModeName(PowerInfo.PowerMode.Undefined)
			});
			int ppi = (int)Controller.PowerPlan;
			if (ppi <= 2) ppi = 2 - ppi;
			powerPlan.DropDownStyle = ComboBoxStyle.DropDownList;
			powerPlan.SelectedIndex = System.Math.Min(ppi, 3);
			powerPlan.Width = 180;
			tooltip.SetToolTip(powerPlan, "Power Mode to be used when this application is detected. Leaving this undefined disables it.");
			lt.Controls.Add(powerPlan);
			lt.Controls.Add(new Label()); // empty

			// POWERDOWN in background
			lt.Controls.Add(new Label() { Text = "Background powerdown", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			backgroundPowerdown.Checked = Controller.BackgroundPowerdown;
			tooltip.SetToolTip(backgroundPowerdown, "Power down any power mode when the app goes off focus.\nRequires foreground only to be enabled.");
			lt.Controls.Add(backgroundPowerdown);
			lt.Controls.Add(new Label()); // empty

			// FOREGROUND ONLY TOGGLE
			bool fge = foregroundOnly.Checked;
			bool pwe = powerPlan.SelectedIndex != 3;

			foregroundOnly.CheckedChanged += (s, e) =>
			{
				fge = foregroundOnly.Checked;
				bgPriorityClass.Enabled = fge;
				bgAffinityMask.Enabled = fge;
				backgroundPowerdown.Enabled = (pwe && fge);
			};

			bool bgpd = backgroundPowerdown.Checked;
			powerPlan.SelectionChangeCommitted += (s, e) =>
			{
				pwe = powerPlan.SelectedIndex != 3;
				backgroundPowerdown.Enabled = (pwe && fge);
			};

			bgPriorityClass.Enabled = fge;
			bgAffinityMask.Enabled = fge;
			backgroundPowerdown.Enabled = fge;

			// PAGING
			lt.Controls.Add(new Label { Text = "Allow paging", TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
			allowPaging.Checked = Controller.AllowPaging;
			tooltip.SetToolTip(allowPaging, "Allow this application to be paged when it is requested.\nNOT FULLY IMPLEMENTED.");
			lt.Controls.Add(allowPaging);
			lt.Controls.Add(new Label()); // empty

			// lt.Controls.Add(new Label { Text=""})

			// AUDIO

			lt.Controls.Add(new Label { Text = "Mixer Volume", TextAlign = System.Drawing.ContentAlignment.MiddleLeft });

			volumeMethod = new ComboBox()
			{
				Dock = DockStyle.Left,
				DropDownStyle = ComboBoxStyle.DropDownList,
				Items = { "Increase", "Decrease", "Increase from mute", "Decrease from full", "Force", HumanReadable.Generic.Ignore },
				SelectedIndex = 5,
			};

			lt.Controls.Add(volumeMethod);
			lt.Controls.Add(new Label());

			lt.Controls.Add(new Label());
			volume = new Extensions.NumericUpDownEx()
			{
				Unit = "%",
				DecimalPlaces = 1,
				Increment = 1.0M,
				Maximum = 100.0M,
				Minimum = 0.0M,
				Width = 80,
				Value = Convert.ToDecimal(Controller.Volume * 100f),
			};
			tooltip.SetToolTip(volume, "Percentage of device maximum volume.");
			lt.Controls.Add(volume);
			lt.Controls.Add(new Label());

			if (!Taskmaster.AudioManagerEnabled)
			{
				volume.Enabled = false;
				volumeMethod.Enabled = false;
			}
			else
			{
				switch (Controller.VolumeStrategy)
				{
					case AudioVolumeStrategy.Increase: volumeMethod.SelectedIndex = 0; break;
					case AudioVolumeStrategy.Decrease: volumeMethod.SelectedIndex = 1; break;
					case AudioVolumeStrategy.IncreaseFromMute: volumeMethod.SelectedIndex = 2; break;
					case AudioVolumeStrategy.DecreaseFromFull: volumeMethod.SelectedIndex = 3; break;
					case AudioVolumeStrategy.Force: volumeMethod.SelectedIndex = 4; break;
					default:
					case AudioVolumeStrategy.Ignore: volumeMethod.SelectedIndex = 5; break;
				}

				// disable volume control if method is to ignore it
				volume.Enabled = !(volumeMethod.SelectedIndex == 5);
				volumeMethod.SelectedIndexChanged += (s, e) =>
				{
					volume.Enabled = !(volumeMethod.SelectedIndex == 5);
				};
			}

			if (!Taskmaster.PowerManagerEnabled)
			{
				powerPlan.Enabled = false;
			}

			// BUTTONS

			var finalizebuttons = new TableLayoutPanel() { ColumnCount = 2, AutoSize = true };
			var saveButton = new Button() { Text = "Save" }; // SAVE
			saveButton.Click += SaveInfo;
			finalizebuttons.Controls.Add(saveButton);
			// lt.Controls.Add(saveButton);
			var cancelButton = new Button() { Text = "Cancel" }; // CLOSE
			cancelButton.Click += (sender, e) =>
			{
				DialogResult = DialogResult.Cancel;
				Close();
			};
			finalizebuttons.Controls.Add(cancelButton);

			var validatebutton = new Button() { Text = "Validate" };
			validatebutton.Click += ValidateWatchedItem;
			validatebutton.Margin = CustomPadding;

			lt.Controls.Add(validatebutton);

			lt.Controls.Add(finalizebuttons);

			// ---
		}

		void ValidateWatchedItem(object _, EventArgs _ea)
		{
			var fnlen = (friendlyName.Text.Length > 0);
			var exnam = (execName.Text.Length > 0);
			var path = (pathName.Text.Length > 0);

			var exfound = false;
			if (exnam)
			{
				var friendlyexe = System.IO.Path.GetFileNameWithoutExtension(execName.Text);
				var procs = Process.GetProcessesByName(friendlyexe);
				if (procs.Length > 0) exfound = true;
			}

			string pfound = "No";
			if (path)
			{
				try
				{
					if (System.IO.Directory.Exists(pathName.Text))
					{
						pfound = "Exact";
					}
					else
					{
						string search = System.IO.Path.GetFileName(pathName.Text);
						var di = System.IO.Directory.GetParent(pathName.Text);
						if (di != null && !string.IsNullOrEmpty(search))
						{
							var dirs = System.IO.Directory.EnumerateDirectories(di.FullName, search+"*", System.IO.SearchOption.TopDirectoryOnly);
							foreach (var dir in dirs)
							{
								pfound = "Partial Match";
								break;
							}
						}
					}
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
				sbs.Append(HumanReadable.System.Process.Executable).Append(": ").Append(exnam ? "OK" : "Fail").Append(" – Found: ").Append(exfound).AppendLine();
			if (pathName.Text.Length > 0)
				sbs.Append("Path: ").Append(path ? "OK" : "Fail").Append(" - Found: ").Append(pfound).AppendLine();

			if (!exnam && !path)
				sbs.Append("Both path and executable are missing!").AppendLine();

			if (priorityClass.SelectedIndex == 5)
				sbs.Append("Priority class is to be ignored.").AppendLine();
			if (cpumask == -1 || affstrategy.SelectedIndex == 0)
				sbs.Append("Affinity is to be ignored.").AppendLine();
			if (ignorelist.Items.Count > 0 && execName.Text.Length > 0)
				sbs.Append("Ignore list is meaningless with executable defined.").AppendLine();

			MessageBox.Show(sbs.ToString(), "Validation results", MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1);
		}
	}
}