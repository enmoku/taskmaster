﻿//
// WatchlistEditWindow.cs
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

using MKAh;
using MKAh.Logic;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;
using Serilog;
using Taskmaster;

namespace Taskmaster.UI.Config
{
	using static Taskmaster;

	sealed public class WatchlistEditWindow : UI.UniForm
	{
		public Process.Controller Controller;
		
		readonly bool newPrc = false;

		// Adding
		public WatchlistEditWindow()
			: base()
		{
			DialogResult = DialogResult.Abort;

			Controller = new Process.Controller("Unnamed") { Enabled = true };

			newPrc = true;

			WindowState = FormWindowState.Normal;
			FormBorderStyle = FormBorderStyle.FixedDialog; // no min/max buttons as wanted

			BuildUI();
		}

		// Editingg
		public WatchlistEditWindow(Process.Controller controller)
		{
			DialogResult = DialogResult.Abort;

			Controller = controller;

			StartPosition = FormStartPosition.CenterParent;

			if (Controller is null) throw new ArgumentException(Controller.FriendlyName + " not found in watchlist.");

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

			if (!newPrc) Controller.Refresh(); // make sure we don't cling to things

			// TODO: VALIDATE FOR GRIMMY'S SAKE!

			// -----------------------------------------------
			// VALIDATE

			bool fnlen = (friendlyName.Text.Length > 0);
			bool exnam = (execName.Text.Length > 0);
			bool path = (pathName.Text.Length > 0);

			if (!fnlen || friendlyName.Text.Contains("]") || friendlyName.Text.Contains("["))
			{
				MessageBox.ShowModal("Malconfigured friendly name", "Friendly name is missing or includes illegal characters (such as square brackets).", MessageBox.Buttons.OK, parent:this);
				return;
			}

			if (!path && !exnam)
			{
				MessageBox.ShowModal("Configuration error", "No path nor executable defined.", MessageBox.Buttons.OK, parent: this);
				return;
			}

			string newfriendlyname = friendlyName.Text.Trim();

			if (processmanager.GetControllerByName(newfriendlyname, out var dprc) && dprc != Controller)
			{
				MessageBox.ShowModal("Configuration error", "Friendly Name conflict.", MessageBox.Buttons.OK, parent: this);
				return;
			}

			bool hasPrio = priorityClass.SelectedIndex != 5;
			bool hasAff = affinityMask.Value != -1;
			bool hasPow = powerPlan.SelectedIndex != 3;

			if (!hasPrio && !hasAff && !hasPow)
			{
				var rv = MessageBox.ShowModal("Configuration error", "No priority, affinity, nor power plan defined.\nThis will cause matching items to be essentially ignored.", MessageBox.Buttons.AcceptCancel, parent: this);
				if (rv != MessageBox.ResultType.OK)
					return;
			}

			// -----------------------------------------------

			Controller.SetName(newfriendlyname);

			if (execName.Text.Length > 0)
			{
				var t_executables = execName.Text.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
				var f_exes = new string[t_executables.Length];
				for (int i = 0; i < t_executables.Length; i++)
					f_exes[i] = t_executables[i].Trim();
				Controller.Executables = (f_exes?.Length > 0) ? f_exes : null;
			}
			else
				Controller.Executables = null;

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

			Process.PathVisibilityOptions pvis = Process.PathVisibilityOptions.Invalid;
			switch (pathVisibility.SelectedIndex)
			{
				default:
				case 0: pvis = Process.PathVisibilityOptions.Invalid; break;
				case 1: pvis = Process.PathVisibilityOptions.Process; break;
				case 2: pvis = Process.PathVisibilityOptions.Partial; break;
				case 3: pvis = Process.PathVisibilityOptions.Full; break;
				case 4: pvis = Process.PathVisibilityOptions.Smart; break;
			}
			Controller.PathVisibility = pvis;

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

			Controller.ModifyDelay = (int)(modifyDelay.Value * 1_000);
			Controller.PowerPlan = Power.Manager.GetModeByName(powerPlan.Text);
			Controller.AllowPaging = allowPaging.Checked;
			Controller.SetForegroundMode((ForegroundMode)(ForegroundModeSelect.SelectedIndex - 1));

			if (bgPriorityClass.SelectedIndex != 5)
				Controller.BackgroundPriority = ProcessHelpers.IntToPriority(bgPriorityClass.SelectedIndex);
			else
				Controller.BackgroundPriority = null;

			if (bgAffinityMask.Value >= 0)
				Controller.BackgroundAffinity = Convert.ToInt32(bgAffinityMask.Value);
			else
				Controller.BackgroundAffinity = -1;

			if (ignorelist.Items.Count > 0 && execName.Text.Length == 0)
			{
				var ignlist = new List<string>();
				foreach (ListViewItem item in ignorelist.Items)
					ignlist.Add(item.Text);

				Controller.IgnoreList = ignlist.ToArray();
			}
			else
				Controller.IgnoreList = null;

			if (desc.Text.Length > 0)
				Controller.Description = desc.Text;

			if (AudioManagerEnabled && volumeMethod.SelectedIndex != 5)
			{
				switch (volumeMethod.SelectedIndex)
				{
					case 0: Controller.VolumeStrategy = Audio.VolumeStrategy.Increase; break;
					case 1: Controller.VolumeStrategy = Audio.VolumeStrategy.Decrease; break;
					case 2: Controller.VolumeStrategy = Audio.VolumeStrategy.IncreaseFromMute; break;
					case 3: Controller.VolumeStrategy = Audio.VolumeStrategy.DecreaseFromFull; break;
					case 4: Controller.VolumeStrategy = Audio.VolumeStrategy.Force; break;
					default: Controller.VolumeStrategy = Audio.VolumeStrategy.Ignore; break;
				}

				Controller.Volume = Convert.ToSingle(volume.Value) / 100f;
			}

			Controller.AffinityIdeal = Convert.ToInt32(idealAffinity.Value) - 1;

			if (IOPriorityEnabled)
				Controller.IOPriority = (ioPriority?.SelectedIndex ?? 0) - 1;

			Controller.LogAdjusts = logAdjusts.Checked;
			Controller.LogStartAndExit = logStartNExit.Checked;

			Controller.DeclareParent = declareParent.Checked;

			Controller.OrderPreference = Convert.ToInt32(preforder.Value).Constrain(0, 100);

			Controller.Enabled = newPrc ? true : enOrig;

			Controller.Repair();

			Controller.SaveConfig();

			Log.Information("[" + Controller.FriendlyName + "] " + (newPrc ? "Created" : "Modified"));

			DialogResult = DialogResult.OK;

			Controller.Refresh();

			Close();
		}

		TextBox friendlyName = null;
		TextBox execName = null;
		TextBox pathName = null;
		ComboBox pathVisibility = null;
		TextBox desc = null;

		ComboBox priorityClass = null;
		ComboBox priorityClassMethod = null;
		ComboBox bgPriorityClass = null;

		ComboBox affstrategy = null;
		NumericUpDown affinityMask = null;
		NumericUpDown bgAffinityMask = null;
		NumericUpDown idealAffinity = null;
		ComboBox ioPriority = null;

		ComboBox volumeMethod = null;
		Extensions.NumericUpDownEx volume = null;

		Button allbutton = null;
		Button clearbutton = null;
		Extensions.NumericUpDownEx modifyDelay = null;
		CheckBox allowPaging = null;
		ComboBox powerPlan = null;
		ComboBox ForegroundModeSelect = null;
		ComboBox FullscreenMode = null;
		ListView ignorelist = null;
		NumericUpDown preforder = null;

		CheckBox logAdjusts = null, logStartNExit=null, declareParent=null;

		int cpumask = 0;

		static char[] InvalidCharacters = new[] { ']', '#', ';' };

		void BuildUI()
		{
			// Size = new System.Drawing.Size(340, 480); // width, height
			AutoSizeMode = AutoSizeMode.GrowOnly;
			AutoSize = true;

			Text = Controller.FriendlyName + " – " + Taskmaster.Name;

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

			lt.Controls.Add(new AlignedLabel { Text = "Friendly name" });
			friendlyName = new TextBox()
			{
				ShortcutsEnabled = true,
				Text = Controller.FriendlyName,
				Width = 180,
				CausesValidation = true,
			};

			friendlyName.Validating += (_, e) =>
			{
				e.Cancel = !ValidateName(friendlyName, InvalidCharacters);
			};
			tooltip.SetToolTip(friendlyName, "Human readable name, for user convenience.\nInvalid characters: ], #, and ;");
			lt.Controls.Add(friendlyName);

			lt.Controls.Add(new EmptySpace());

			// EXECUTABLE
			lt.Controls.Add(new AlignedLabel { Text = HumanReadable.System.Process.Executable });
			execName = new TextBox()
			{
				ShortcutsEnabled = true,
				Text =  Controller.Executables?.Length > 0 ? string.Join("|", Controller.Executables) : string.Empty,
				Width = 180,
			};
			execName.Validating += ValidateFilename;
			tooltip.SetToolTip(execName, "Executable name, used to recognize these applications.\nFull filename, including extension if any.\nSeparate executables with pipe (|).");
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
							var info = exselectdialog.Info;
							// SANITY CHECK: exselectdialog.Selection;
							execName.Text = info.Name; // Append?
							if (!string.IsNullOrEmpty(info.Path))
							{
								if (string.IsNullOrEmpty(pathName.Text))
									pathName.Text = System.IO.Path.GetDirectoryName(info.Path);
								execName.Text = System.IO.Path.GetFileName(info.Path);
							}
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
			lt.Controls.Add(new AlignedLabel { Text = HumanReadable.System.Process.Path });
			pathName = new TextBox()
			{
				ShortcutsEnabled = true,
				Text = Controller.Path,
				Width = 180,
			};
			pathName.Validating += ValidatePathname;
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
					// WinForms does not support positioning this
					using (var folderdialog = new FolderBrowserDialog())
					{
						folderdialog.ShowNewFolderButton = false;
						folderdialog.RootFolder = Environment.SpecialFolder.MyComputer;
						if (folderdialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(folderdialog.SelectedPath))
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

			// PATH VISIBILITY
			pathVisibility = new ComboBox()
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				//Dock = DockStyle.Left,
				//Anchor = AnchorStyles.Left,
				Width = 180,
			};
			pathVisibility.Items.AddRange(new string[] {
				"Auto-select",
				"Process name (run)",
				@"Partial (...\app\run.exe)",
				@"Full (c:\programs\brand\app\v2.4\bin\run.exe)",
				@"Smart (...\brand\...\run.exe)"
			});

			switch (Controller.PathVisibility)
			{
				default:
				case Process.PathVisibilityOptions.Invalid: pathVisibility.SelectedIndex = 0; break;
				case Process.PathVisibilityOptions.Process: pathVisibility.SelectedIndex = 1; break;
				case Process.PathVisibilityOptions.Partial: pathVisibility.SelectedIndex = 2; break;
				case Process.PathVisibilityOptions.Full: pathVisibility.SelectedIndex = 3; break;
				case Process.PathVisibilityOptions.Smart: pathVisibility.SelectedIndex = 4; break;
			}
			lt.Controls.Add(new AlignedLabel { Text = "Path visibility" });
			lt.Controls.Add(pathVisibility);
			lt.Controls.Add(new EmptySpace());
			tooltip.SetToolTip(pathVisibility, "How the process is shown in logs.\nProcess name option is fastest but least descriptive.\nSmart is marginally slowest.\nAuto-select sets something depending on whether executable or path are defined.");

			// DESCRIPTION
			lt.Controls.Add(new AlignedLabel() { Text = HumanReadable.Generic.Description });
			desc = new TextBox()
			{
				Multiline = false,
				//Anchor = AnchorStyles.Left,
				Dock = DockStyle.Top,
				ShortcutsEnabled = true,
			};
			desc.Text = Controller.Description;
			lt.Controls.Add(desc);
			lt.Controls.Add(new EmptySpace());

			// IGNORE

			ignorelist = new Extensions.ListViewEx()
			{
				BorderStyle = BorderStyle.Fixed3D, // doesn't work with EnableVisualStyles
				View = View.Details,
				HeaderStyle = ColumnHeaderStyle.None,
				//Dock = DockStyle.Left,
				Dock = DockStyle.Top,
				//Anchor = AnchorStyles.Left,
				FullRowSelect = true,
				Width = 180,
				Height = 80,
				Enabled = (execName.Text.Length == 0),
			};
			ignorelist.Columns.Add(HumanReadable.System.Process.Executable, ignorelist.Width - 24); // arbitrary -24 to eliminate horizontal scrollbar

			tooltip.SetToolTip(ignorelist, "Executables to ignore for matching with this rule.\nOnly exact matches work.\n\nRequires path to be defined.\nHas no effect if executable is defined.");
			execName.TextChanged += (_, _ea) => ignorelist.Enabled = (execName.Text.Length == 0);

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
						if (rs.ShowDialog() == DialogResult.OK)
							ignorelist.Items.Add(rs.Value);
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

			lt.Controls.Add(new AlignedLabel() { Text = HumanReadable.Generic.Ignore });
			lt.Controls.Add(ignorelist);
			lt.Controls.Add(new EmptySpace());

			var priorities = new string[] { "Low", "Below Normal", "Normal", "Above Normal", "High", HumanReadable.Generic.Ignore };

			// PRIORITY
			lt.Controls.Add(new AlignedLabel { Text = HumanReadable.System.Process.PriorityClass });
			priorityClass = new ComboBox
			{
				//Dock = DockStyle.Left,
				//Anchor = AnchorStyles.Left,
				DropDownStyle = ComboBoxStyle.DropDownList,
				Width = 180,
			};
			priorityClass.Items.AddRange(priorities);
			priorityClass.SelectedIndex = Controller.Priority?.ToInt32() ?? 5;

			tooltip.SetToolTip(priorityClass, "CPU priority for the application.\nIf both increase and decrease are disabled, this has no effect.");

			priorityClassMethod = new ComboBox
			{
				//Dock = DockStyle.Left,
				//Anchor = AnchorStyles.Left,
				//Width = 100,
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

			lt.Controls.Add(new AlignedLabel { Text = HumanReadable.System.Process.Affinity });
			affstrategy = new ComboBox()
			{
				//Dock = DockStyle.Left,
				//Anchor = AnchorStyles.Left,
				DropDownStyle = ComboBoxStyle.DropDownList,
				Width = 180,
			};
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
			lt.Controls.Add(new EmptySpace());

			lt.Controls.Add(new AlignedLabel() { Text = "Affinity mask\n&& Cores" }); // left
			affinityMask = new NumericUpDown()
			{
				Width = 80,
				Maximum = Process.Manager.AllCPUsMask,
				Minimum = -1,
				Value = Controller.AffinityMask.Constrain(-1, Process.Manager.AllCPUsMask),
			};

			tooltip.SetToolTip(affinityMask, "CPU core afffinity as integer mask.\nEnter 0 to let OS manage this as normal.\nFull affinity is same as 0, there's no difference.\nExamples:\n14 = all but first core on quadcore.\n254 = all but first core on octocore.\n-1 = Ignored");

			// ---------------------------------------------------------------------------------------------------------

			var afflayout = new TableLayoutPanel() { ColumnCount = 1, AutoSize = true };

			afflayout.Controls.Add(affinityMask);

			var corelayout = new TableLayoutPanel() { ColumnCount = 8, AutoSize = true };

			cpumask = Controller.AffinityMask;
			for (int bit = 0; bit < Process.Manager.CPUCount; bit++)
			{
				int lbit = bit;
				var box = new CheckBox
				{
					Checked = ((Math.Max(0, cpumask) & (1 << lbit)) != 0),
					AutoSize = true,
				};
				box.CheckedChanged += (sender, e) =>
				{
					if (cpumask < 0) cpumask = 0;

					if (box.Checked)
					{
						cpumask |= (1 << lbit);
						affinityMask.Value = cpumask;
					}
					else
					{
						cpumask &= ~(1 << lbit);
						affinityMask.Value = cpumask;
					}
				};
				corelist.Add(box);
				corelayout.Controls.Add(new AlignedLabel { Text = (bit + 1) + ":" });
				corelayout.Controls.Add(box);
			}

			afflayout.Controls.Add(corelayout);

			var affbuttonpanel = new TableLayoutPanel() { ColumnCount = 1, AutoSize = true };
			clearbutton = new Button() { Text = "None" };
			clearbutton.Click += (sender, e) =>
			{
				foreach (var litem in corelist) litem.Checked = false;
			};
			allbutton = new Button() { Text = "All" };
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

			// EXPERIMENTS

			idealAffinity = new NumericUpDown()
			{
				Width = 80,
				Maximum = Process.Manager.CPUCount,
				Minimum = 0,
				Value = (Controller.AffinityIdeal + 1).Constrain(0, Process.Manager.CPUCount),
			};
			tooltip.SetToolTip(idealAffinity, "EXPERIMENTAL\nTell the OS to favor this particular core for the primary thread.\nMay not have any perceivable effect.\n0 disables this feature.");

			lt.Controls.Add(new AlignedLabel() { Text = "Ideal affinity", ForeColor = System.Drawing.Color.Red });
			lt.Controls.Add(idealAffinity);
			lt.Controls.Add(new AlignedLabel() { Text = "EXPERIMENTAL", ForeColor = System.Drawing.Color.Red });

			if (IOPriorityEnabled)
			{
				ioPriority = new ComboBox()
				{
					DropDownStyle = ComboBoxStyle.DropDownList,
					Items = { "Ignore", "Background", "Low", "Normal" },
					SelectedIndex = Controller.IOPriority + 1,
					AutoSize = true,
					Dock = DockStyle.Fill,
				};

				tooltip.SetToolTip(ioPriority, "EXPERIMENTAL\nDO NOT SET BACKGROUND FOR ANYTHING WITH USER INTERFACE\nBackground setting may delay I/O almost indefinitely.\nAffects HDD/SSD access and Networking\nNormal is the default.\nBackground is for things that do not interact with user.");

				lt.Controls.Add(new AlignedLabel() { Text = "I/O priority", ForeColor = System.Drawing.Color.Red });
				lt.Controls.Add(ioPriority);
				lt.Controls.Add(new AlignedLabel() { Text = "EXPERIMENTAL", ForeColor = System.Drawing.Color.Red });
			}

			// ---------------------------------------------------------------------------------------------------------

			// FOREGROUND

			ForegroundModeSelect = new ComboBox()
			{
				//Dock = DockStyle.Left,
				//Anchor = AnchorStyles.Left,
				DropDownStyle = ComboBoxStyle.DropDownList,
				Width = 180,
			};

			ForegroundModeSelect.Items.AddRange(new string[] { "Ignore", "Priority and Affinity", "Priority, Affinity, and Power", "Power only" });
			ForegroundModeSelect.SelectedIndex = ((int)Controller.Foreground) + 1;
			tooltip.SetToolTip(ForegroundModeSelect, "Select which factors are lowered when this app is not in focus.");

			lt.Controls.Add(new AlignedLabel { Text = "Foreground mode" });
			lt.Controls.Add(ForegroundModeSelect);
			lt.Controls.Add(new EmptySpace());

			// BACKGROUND PRIORITY & AFFINITY

			lt.Controls.Add(new AlignedLabel { Text = "Background priority" });

			bgPriorityClass = new ComboBox
			{
				//Dock = DockStyle.Left,
				//Anchor = AnchorStyles.Left,
				DropDownStyle = ComboBoxStyle.DropDownList,
				Width = 180,
			};
			bgPriorityClass.Items.AddRange(priorities);
			bgPriorityClass.SelectedIndex = 5;

			if (Controller.BackgroundPriority.HasValue)
				bgPriorityClass.SelectedIndex = Controller.BackgroundPriority.Value.ToInt32();
			tooltip.SetToolTip(bgPriorityClass, "Same as normal priority.\nIgnored causes priority to be untouched.");

			lt.Controls.Add(bgPriorityClass);
			lt.Controls.Add(new EmptySpace());

			lt.Controls.Add(new AlignedLabel { Text = "Background affinity" });

			bgAffinityMask = new NumericUpDown()
			{
				Width = 80,
				Maximum = Process.Manager.AllCPUsMask,
				Minimum = -1,
				Value = Controller.BackgroundAffinity,
			};
			tooltip.SetToolTip(bgAffinityMask, "Same as normal affinity.\nStrategy is 'force' only for this.\n-1 causes affinity to be untouched.");

			lt.Controls.Add(bgAffinityMask);
			lt.Controls.Add(new EmptySpace());

			// ---------------------------------------------------------------------------------------------------------

			// lt.Controls.Add(new Label { Text="Children"});
			// lt.Controls.Add(new Label { Text="Child priority"});

			// MODIFY DELAY

			lt.Controls.Add(new AlignedLabel() { Text = "Modify delay" });
			modifyDelay = new Extensions.NumericUpDownEx()
			{
				Unit = "s",
				DecimalPlaces = 1,
				Minimum = 0,
				Maximum = 180,
				Width = 80,
				Value = Controller.ModifyDelay / 1_000.0M,
			};
			tooltip.SetToolTip(modifyDelay, "Delay before the process is actually attempted modification.\nEither to keep original priority for a short while, or to counter early self-adjustment.\nThis is also applied to foreground only limited modifications.");
			lt.Controls.Add(modifyDelay);
			lt.Controls.Add(new EmptySpace());

			// POWER
			lt.Controls.Add(new AlignedLabel { Text = HumanReadable.Hardware.Power.Plan });
			powerPlan = new ComboBox()
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				//Dock = DockStyle.Left,
				//Anchor = AnchorStyles.Left,
				Width = 180,
			};
			powerPlan.Items.AddRange(new string[] {
				Power.Manager.GetModeName(Power.Mode.HighPerformance),
				Power.Manager.GetModeName(Power.Mode.Balanced),
				Power.Manager.GetModeName(Power.Mode.PowerSaver),
				Power.Manager.GetModeName(Power.Mode.Undefined)
			});
			int ppi = 3;
			switch (Controller.PowerPlan)
			{
				case Power.Mode.HighPerformance: ppi = 0; break;
				case Power.Mode.Balanced: ppi = 1; break;
				case Power.Mode.PowerSaver: ppi = 2; break;
				default: ppi = 3; break;
			}
			powerPlan.SelectedIndex = ppi;
			tooltip.SetToolTip(powerPlan, "Power Mode to be used when this application is detected. Leaving this undefined disables it.");
			lt.Controls.Add(powerPlan);
			lt.Controls.Add(new EmptySpace());

			// FOREGROUND ONLY TOGGLE
			bool fge = ForegroundModeSelect.SelectedIndex != 0;
			bool pwe = powerPlan.SelectedIndex != 3;

			ForegroundModeSelect.SelectedIndexChanged += (s, e) =>
			{
				fge = ForegroundModeSelect.SelectedIndex != 0;
				bgPriorityClass.Enabled = fge && ForegroundModeSelect.SelectedIndex != 3;
				bgAffinityMask.Enabled = fge && ForegroundModeSelect.SelectedIndex != 3;
			};

			bgPriorityClass.Enabled = fge && ForegroundModeSelect.SelectedIndex != 3;
			bgAffinityMask.Enabled = fge && ForegroundModeSelect.SelectedIndex != 3;

			// PAGING
			lt.Controls.Add(new AlignedLabel { Text = "Allow paging" });
			allowPaging = new CheckBox() { Checked = Controller.AllowPaging };
			tooltip.SetToolTip(allowPaging, "Allow this application to be paged when it is requested.\nNOT FULLY IMPLEMENTED.");
			lt.Controls.Add(allowPaging);
			lt.Controls.Add(new EmptySpace());

			// lt.Controls.Add(new Label { Text=""})

			// AUDIO

			lt.Controls.Add(new AlignedLabel { Text = "Mixer Volume" });

			volumeMethod = new ComboBox()
			{
				//Dock = DockStyle.Left,
				//Anchor = AnchorStyles.Left,
				DropDownStyle = ComboBoxStyle.DropDownList,
				Items = { "Increase", "Decrease", "Increase from mute", "Decrease from full", "Force", HumanReadable.Generic.Ignore },
				SelectedIndex = 5,
				Width = 180,
			};

			lt.Controls.Add(volumeMethod);
			lt.Controls.Add(new EmptySpace());

			lt.Controls.Add(new EmptySpace());
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
			lt.Controls.Add(new EmptySpace());

			if (!AudioManagerEnabled)
			{
				volume.Enabled = false;
				volumeMethod.Enabled = false;
			}
			else
			{
				switch (Controller.VolumeStrategy)
				{
					case Audio.VolumeStrategy.Increase: volumeMethod.SelectedIndex = 0; break;
					case Audio.VolumeStrategy.Decrease: volumeMethod.SelectedIndex = 1; break;
					case Audio.VolumeStrategy.IncreaseFromMute: volumeMethod.SelectedIndex = 2; break;
					case Audio.VolumeStrategy.DecreaseFromFull: volumeMethod.SelectedIndex = 3; break;
					case Audio.VolumeStrategy.Force: volumeMethod.SelectedIndex = 4; break;
					default:
					case Audio.VolumeStrategy.Ignore: volumeMethod.SelectedIndex = 5; break;
				}

				// disable volume control if method is to ignore it
				volume.Enabled = !(volumeMethod.SelectedIndex == 5);
				volumeMethod.SelectedIndexChanged += (s, e) =>
				{
					volume.Enabled = !(volumeMethod.SelectedIndex == 5);
				};
			}

			if (!PowerManagerEnabled) powerPlan.Enabled = false;

			// LOG ADJUSTS

			logAdjusts = new CheckBox() { Checked = Controller.LogAdjusts };
			tooltip.SetToolTip(logAdjusts, "You can disable logging adjust events for this specific rule, from both UI and disk.\nUse Configuration > Logging > Process adjusts for all.");

			lt.Controls.Add(new AlignedLabel() { Text = "Log adjusts" });
			lt.Controls.Add(logAdjusts);
			lt.Controls.Add(new EmptySpace());

			logStartNExit = new CheckBox() { Checked = Controller.LogStartAndExit };
			tooltip.SetToolTip(logStartNExit, "You can disable logging adjust events for this specific rule, from both UI and disk.\nUse Configuration > Logging > Process adjusts for all.");

			lt.Controls.Add(new AlignedLabel() { Text = "Log start && exit" });
			lt.Controls.Add(logStartNExit);
			lt.Controls.Add(new EmptySpace());

			declareParent = new CheckBox() { Checked = Controller.DeclareParent };
			lt.Controls.Add(new AlignedLabel() { Text = "Log parent" });
			lt.Controls.Add(declareParent);
			if (processmanager.EnableParentFinding)
				lt.Controls.Add(new EmptySpace());
			else
				lt.Controls.Add(new AlignedLabel() { Text = "Disabled" });
			tooltip.SetToolTip(declareParent, "Parent process logging slows log procedure significantly.\nMust be enabled in advanced settings also.");

			preforder = new NumericUpDown()
			{
				Minimum = 0,
				Maximum = 100,
				Value = Controller.OrderPreference.Constrain(0, 100),
			};
			tooltip.SetToolTip(preforder, "Rules with lower order are processed first.\nMost frequently modified apps should have lowest,\nbut this can also be used to control which rule is more likely to trigger.\n");

			lt.Controls.Add(new AlignedLabel() { Text = "Order preference" });
			lt.Controls.Add(preforder);
			lt.Controls.Add(new EmptySpace());

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
			validatebutton.Margin = BigPadding;

			lt.Controls.Add(validatebutton);

			lt.Controls.Add(finalizebuttons);

			// ---
		}

		bool ValidateName(TextBox box, char[] invalidChars)
		{
			bool rv = true;
			int off = -1;
			if ((off = box.Text.IndexOfAny(invalidChars)) >= 0)
			{
				rv = false;
				if (off >= 0)
					box.Select(off, 1);
				else
					box.Select(0, 0);

				FlashTextBox(box);
			}
			return rv;
		}

		void FlashTextBox(TextBox box)
		{
			// FLASH HACK
			var bgc = box.BackColor;
			box.BackColor = System.Drawing.Color.OrangeRed;
			BeginInvoke(new Action(async () =>
			{
				await Task.Delay(50).ConfigureAwait(true);
				box.BackColor = bgc;
			}));
		}

		void ValidatePathname(object sender, CancelEventArgs e)
		{
			if (sender is TextBox box && box.Text.Length > 0)
				e.Cancel = !ValidateName(box, System.IO.Path.GetInvalidPathChars());
		}

		void ValidateFilename(object sender, CancelEventArgs e)
		{
			if (sender is TextBox box)
			{
				if (execName.TextLength == 0 && pathName.TextLength == 0) return;

				//e.Cancel = !ValidateName(box, System.IO.Path.GetInvalidFileNameChars());
				if (box == execName)
				{
					if (box.TextLength > 0)
					{
						if (!box.Text.Contains("."))
						{
							box.SelectionStart = box.TextLength;
							e.Cancel = true;
							FlashTextBox(box);
						}
					}
				}
				else
				{
					if (box.TextLength > 0)
					{

					}
				}
			}
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
				var procs = System.Diagnostics.Process.GetProcessesByName(friendlyexe);
				exfound |= (procs.Length > 0);
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
			if (!samesection && processmanager.GetControllerByName(friendlyName.Text, out var dprc)) sbs.Append("Friendly name conflict!");

			if (execName.Text.Length > 0)
			{
				sbs.Append(HumanReadable.System.Process.Executable).Append(": ").Append(exnam ? "OK" : "Fail").Append(" – Found: ").Append(exfound).AppendLine();
				string lowname = System.IO.Path.GetFileNameWithoutExtension(execName.Text);
				if (processmanager.ProtectedProcessName(lowname.ToLowerInvariant()))
					sbs.Append("Defined executable is in core protected executables list. Priority adjustment denied.").AppendLine();

				if (processmanager.IgnoreProcessName(lowname))
					sbs.Append("Defined executable is in core ignore list. All changes denied.").AppendLine();
			}
			if (pathName.Text.Length > 0)
			{
				sbs.Append("Path: ").Append(path ? "OK" : "Fail").Append(" - Found: ").Append(pfound).AppendLine();
				if (processmanager.IgnoreSystem32Path && pathName.Text.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.System), StringComparison.InvariantCultureIgnoreCase))
					sbs.Append("Path points to System32 even though core config denies it.").AppendLine();
			}

			if (!exnam && !path)
				sbs.Append("Both path and executable are missing!").AppendLine();

			if (priorityClass.SelectedIndex == 5)
				sbs.Append("Priority class is to be ignored.").AppendLine();
			if (cpumask == -1 || affstrategy.SelectedIndex == 0)
				sbs.Append("Affinity is to be ignored.").AppendLine();
			if (ignorelist.Items.Count > 0 && execName.Text.Length > 0)
				sbs.Append("Ignore list is meaningless with executable defined.").AppendLine();

			foreach (ListViewItem item in ignorelist.Items)
			{
				if (execName.Text.Length > 0 && item.Text.Equals(System.IO.Path.GetFileNameWithoutExtension(execName.Text), StringComparison.InvariantCultureIgnoreCase))
				{
					sbs.Append("Ignore list contains the same executable as is being matched for. Stop that.").AppendLine();
					break;
				}
				else if (item.Text.EndsWith(".exe", StringComparison.InvariantCultureIgnoreCase))
				{
					sbs.Append("Ignore list items should not include extensions.").AppendLine();
					break;
				}
			}

			if (affinityMask.Value >= 0 && idealAffinity.Value > 0)
			{
				if (!Bit.IsSet(Convert.ToInt32(affinityMask.Value).Replace(0, Process.Manager.AllCPUsMask), Convert.ToInt32(idealAffinity.Value) - 1))
					sbs.Append("Affinity ideal is not within defined affinity.").AppendLine();
			}

			if (ioPriority != null && ioPriority.SelectedIndex != 0)
			{
				sbs.Append("Warning: I/O priority set! Be certain of what you're doing!").AppendLine();
				if (ioPriority.SelectedIndex == 2 && ioPriority.SelectedIndex > 3)
					sbs.Append("\tUnsupported I/O mode selected. Behaviour may be unexpected.").AppendLine();
			}

			MessageBox.ShowModal("Validation results", sbs.ToString(), MessageBox.Buttons.OK, parent: this);
		}
	}
}