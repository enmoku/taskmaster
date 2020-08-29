//
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
using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Taskmaster.UI.Config
{
	using static Application;

	public class WatchlistEditWindow : UI.UniForm
	{
		public Process.Controller Controller;

		readonly bool newPrc = false;

		// UI elements
		readonly ToolTip tooltip;
		readonly Extensions.TableLayoutPanel layout;

		readonly ModuleManager modules;

		// Editingg
		public WatchlistEditWindow(ModuleManager modules, Process.Controller? prc = null)
		{
			this.modules = modules;

			SuspendLayout();

			DialogResult = DialogResult.Abort;

			int cores = Hardware.Utility.ProcessorCount;

			newPrc = prc is null;
			Controller = prc ?? new Process.Controller("Unnamed") { Enabled = true };

			StartPosition = FormStartPosition.CenterParent;

			WindowState = FormWindowState.Normal;
			FormBorderStyle = FormBorderStyle.FixedDialog; // no min/max buttons as wanted

			MinimizeBox = false;
			MaximizeBox = false;

			#region Build UI
			// Size = new System.Drawing.Size(340, 480); // width, height
			AutoSizeMode = AutoSizeMode.GrowOnly;
			AutoSize = true;

			Text = Controller.FriendlyName + " – " + Application.Name;

			Padding = new Padding(12);

			tooltip = new ToolTip();

			layout = new Extensions.TableLayoutPanel
			{
				Parent = this,
				ColumnCount = 3,
				//lrows.RowCount = 10;
				Dock = DockStyle.Fill,
				AutoSize = true,
			};

			layout.Controls.Add(new Extensions.Label { Text = "Friendly name" });
			friendlyName = new Extensions.TextBox()
			{
				ShortcutsEnabled = true,
				Text = Controller.FriendlyName,
				Width = 180,
				CausesValidation = true,
			};

			friendlyName.Validating += (_, ea) => ea.Cancel = !ValidateName(friendlyName, InvalidCharacters);
			tooltip.SetToolTip(friendlyName, "Human readable name, for user convenience.\nInvalid characters: ], #, and ;");
			layout.Controls.Add(friendlyName);

			layout.Controls.Add(new EmptySpace());

			// EXECUTABLE
			layout.Controls.Add(new Extensions.Label { Text = HumanReadable.System.Process.Executable });
			execName = new Extensions.TextBox()
			{
				ShortcutsEnabled = true,
				Text = Controller.Executables.Length > 0 ? string.Join("|", Controller.Executables) : string.Empty,
				Width = 180,
			};
			execName.Validating += ValidateFilename;
			tooltip.SetToolTip(execName, "Executable name, used to recognize these applications.\nFull filename, including extension if any.\nSeparate executables with pipe (|).");
			var findexecbutton = new Extensions.Button()
			{
				Text = "Running",
				AutoSize = true,
				//Dock = DockStyle.Left,
				//Width = 46,
				//Height = 20,
			};
			findexecbutton.Click += (_, _2) =>
			{
				using var exselectdialog = new ProcessSelectDialog(modules.processmanager);
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
			};
			layout.Controls.Add(execName);
			layout.Controls.Add(findexecbutton);

			// PATH
			layout.Controls.Add(new Extensions.Label { Text = HumanReadable.System.Process.Path });
			pathName = new Extensions.TextBox()
			{
				ShortcutsEnabled = true,
				Text = Controller.Path,
				Width = 180,
			};
			pathName.Validating += ValidatePathname;
			tooltip.SetToolTip(pathName, "Path name; rule will match only paths that include this, subfolders included.\nPartial matching is allowed.");
			var findpathbutton = new Extensions.Button()
			{
				Text = "Locate",
				AutoSize = true,
				//Dock = DockStyle.Left,
				//Width = 46,
				//Height = 20,
			};
			findpathbutton.Click += (_, _2) =>
			{
				try
				{
					// WinForms does not support positioning this
					using var folderdialog = new FolderBrowserDialog
					{
						ShowNewFolderButton = false,
						RootFolder = Environment.SpecialFolder.MyComputer
					};
					if (folderdialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(folderdialog.SelectedPath))
						pathName.Text = folderdialog.SelectedPath;
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
				}
			};
			layout.Controls.Add(pathName);
			layout.Controls.Add(findpathbutton);

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

			pathVisibility.SelectedIndex = Controller.PathVisibility switch
			{
				Process.PathVisibilityOptions.Process => 1,
				Process.PathVisibilityOptions.Partial => 2,
				Process.PathVisibilityOptions.Full => 3,
				Process.PathVisibilityOptions.Smart => 4,
				_ => 0,
			};
			layout.Controls.Add(new Extensions.Label { Text = "Path visibility" });
			layout.Controls.Add(pathVisibility);
			layout.Controls.Add(new EmptySpace());
			tooltip.SetToolTip(pathVisibility, "How the process is shown in logs.\nProcess name option is fastest but least descriptive.\nSmart is marginally slowest.\nAuto-select sets something depending on whether executable or path are defined.");

			// DESCRIPTION
			layout.Controls.Add(new Extensions.Label() { Text = HumanReadable.Generic.Description });
			desc = new Extensions.TextBox()
			{
				Multiline = false,
				//Anchor = AnchorStyles.Left,
				Dock = DockStyle.Top,
				ShortcutsEnabled = true,
			};
			desc.Text = Controller.Description;
			layout.Controls.Add(desc);
			layout.Controls.Add(new EmptySpace());

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
			execName.TextChanged += (_, _2) => ignorelist.Enabled = (execName.Text.Length == 0);

			if (Controller.IgnoreList.Length > 0)
			{
				foreach (string item in Controller.IgnoreList)
					ignorelist.Items.Add(item);
			}

			var ignorelistmenu = new ContextMenuStrip();
			ignorelist.ContextMenuStrip = ignorelistmenu;
			ignorelistmenu.Items.Add(new ToolStripMenuItem("Add", null, (_, _2) =>
			{
				try
				{
					using var rs = new TextInputBox("Filename:", "Ignore executable");
					if (rs.ShowDialog() == DialogResult.OK)
						ignorelist.Items.Add(rs.Value);
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
				}
			}));
			ignorelistmenu.Items.Add(new ToolStripMenuItem("Remove", null, (_, _2) =>
			{
				if (ignorelist.SelectedItems.Count == 1)
					ignorelist.Items.Remove(ignorelist.SelectedItems[0]);
			}));

			layout.Controls.Add(new Extensions.Label() { Text = HumanReadable.Generic.Ignore });
			layout.Controls.Add(ignorelist);
			layout.Controls.Add(new EmptySpace());

			var priorities = new string[] { "Low", "Below Normal", "Normal", "Above Normal", "High", HumanReadable.Generic.Ignore };

			// PRIORITY
			layout.Controls.Add(new Extensions.Label { Text = HumanReadable.System.Process.PriorityClass });
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

			priorityClassMethod.SelectedIndex = Controller.PriorityStrategy switch
			{
				Process.PriorityStrategy.Increase => 0,
				Process.PriorityStrategy.Decrease => 1,
				_ => 2,
			};

			layout.Controls.Add(priorityClass);
			layout.Controls.Add(priorityClassMethod);

			priorityClass.SelectedIndexChanged += (_, _2) => priorityClassMethod.Enabled = priorityClass.SelectedIndex != 5; // disable method selection

			// AFFINITY

			var corelist = new List<CheckBox>(cores);

			layout.Controls.Add(new Extensions.Label { Text = HumanReadable.System.Process.Affinity });
			affstrategy = new ComboBox()
			{
				//Dock = DockStyle.Left,
				//Anchor = AnchorStyles.Left,
				DropDownStyle = ComboBoxStyle.DropDownList,
				Width = 180,
			};
			affstrategy.Items.AddRange(new string[] { HumanReadable.Generic.Ignore, "Limit (Default)", "Force" });
			tooltip.SetToolTip(affstrategy, "Limit constrains cores to the defined range but does not increase used cores beyond what the app is already using.\nForce sets the affinity mask to the defined regardless of anything.");
			affstrategy.SelectedIndexChanged += (_, _2) =>
			{
				bool enabled = affstrategy.SelectedIndex != 0;
				affinityMask.Enabled = enabled;
				foreach (var box in corelist)
					box.Enabled = enabled;
				allbutton.Enabled = enabled;
				clearbutton.Enabled = enabled;
			};

			layout.Controls.Add(affstrategy);
			layout.Controls.Add(new EmptySpace());

			layout.Controls.Add(new Extensions.Label() { Text = "Affinity mask\n&& Cores" }); // left
			affinityMask = new NumericUpDown()
			{
				Width = 80,
				Maximum = Process.Utility.FullCPUMask,
				Minimum = -1,
				Value = Controller.AffinityMask.Constrain(-1, Process.Utility.FullCPUMask),
			};

			tooltip.SetToolTip(affinityMask, "CPU core afffinity as integer mask.\nEnter 0 to let OS manage this as normal.\nFull affinity is same as 0, there's no difference.\nExamples:\n14 = all but first core on quadcore.\n254 = all but first core on octocore.\n-1 = Ignored");

			// ---------------------------------------------------------------------------------------------------------

			var afflayout = new Extensions.TableLayoutPanel() { ColumnCount = 1, AutoSize = true };

			afflayout.Controls.Add(affinityMask);

			var corelayout = new Extensions.TableLayoutPanel() { ColumnCount = 8, AutoSize = true };

			cpumask = Controller.AffinityMask;
			for (int bit = 0; bit < cores; bit++)
			{
				int lbit = bit;
				var box = new CheckBox
				{
					Checked = ((Math.Max(0, cpumask) & (1 << lbit)) != 0),
					AutoSize = true,
				};
				box.CheckedChanged += (_, _2) =>
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
				corelayout.Controls.Add(new Extensions.Label { Text = (bit + 1).ToString(CultureInfo.InvariantCulture) + ":" });
				corelayout.Controls.Add(box);
			}

			afflayout.Controls.Add(corelayout);

			var affbuttonpanel = new Extensions.TableLayoutPanel() { ColumnCount = 1, AutoSize = true };
			clearbutton = new Extensions.Button() { Text = "None" };
			clearbutton.Click += (_, _2) =>
			{
				foreach (var litem in corelist) litem.Checked = false;
			};
			allbutton = new Extensions.Button() { Text = "All" };
			allbutton.Click += (_, _2) =>
			{
				foreach (var litem in corelist) litem.Checked = true;
			};
			affbuttonpanel.Controls.Add(allbutton);
			affbuttonpanel.Controls.Add(clearbutton);

			affinityMask.ValueChanged += (_, _2) =>
			{
				var bitoff = 0;
				try { cpumask = (int)affinityMask.Value; }
				catch { cpumask = 0; affinityMask.Value = 0; }
				foreach (var bu in corelist)
					bu.Checked = ((Math.Max(0, cpumask) & (1 << bitoff++)) != 0);
			};

			affstrategy.SelectedIndex = Controller.AffinityStrategy switch
			{
				Process.AffinityStrategy.Force => 2,
				Process.AffinityStrategy.Limit => 1,
				Process.AffinityStrategy.Ignore => 0,
				_ => 1,
			};

			layout.Controls.Add(afflayout);
			layout.Controls.Add(affbuttonpanel);

			// EXPERIMENTS

			if (IOPriorityEnabled)
			{
				ioPriority = new ComboBox()
				{
					DropDownStyle = ComboBoxStyle.DropDownList,
					Items = { "Ignore", "Background", "Low", "Normal" },
					SelectedIndex = ((int)Controller.IOPriority) + 1,
					AutoSize = true,
					Dock = DockStyle.Fill,
				};

				tooltip.SetToolTip(ioPriority, "EXPERIMENTAL\nDO NOT SET BACKGROUND FOR ANYTHING WITH USER INTERFACE\nBackground setting may delay I/O almost indefinitely.\nAffects HDD/SSD access and Networking\nNormal is the default.\nBackground is for things that do not interact with user.");

				layout.Controls.Add(new Extensions.Label() { Text = "I/O priority", ForeColor = System.Drawing.Color.Red });
				layout.Controls.Add(ioPriority);
				layout.Controls.Add(new Extensions.Label() { Text = "EXPERIMENTAL", ForeColor = System.Drawing.Color.Red });
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

			layout.Controls.Add(new Extensions.Label { Text = "Foreground mode" });
			layout.Controls.Add(ForegroundModeSelect);
			layout.Controls.Add(new EmptySpace());

			// BACKGROUND PRIORITY & AFFINITY

			layout.Controls.Add(new Extensions.Label { Text = "Background priority" });

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

			layout.Controls.Add(bgPriorityClass);
			layout.Controls.Add(new EmptySpace());

			layout.Controls.Add(new Extensions.Label { Text = "Background affinity" });

			bgAffinityMask = new NumericUpDown()
			{
				Width = 80,
				Maximum = Process.Utility.FullCPUMask,
				Minimum = -1,
				Value = Controller.BackgroundAffinity,
			};
			tooltip.SetToolTip(bgAffinityMask, "Same as normal affinity.\nStrategy is 'force' only for this.\n-1 causes affinity to be untouched.");

			layout.Controls.Add(bgAffinityMask);
			layout.Controls.Add(new EmptySpace());

			// ---------------------------------------------------------------------------------------------------------

			// lt.Controls.Add(new Label { Text="Children"});
			// lt.Controls.Add(new Label { Text="Child priority"});

			// MODIFY DELAY

			layout.Controls.Add(new Extensions.Label() { Text = "Modify delay" });
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
			layout.Controls.Add(modifyDelay);
			layout.Controls.Add(new EmptySpace());

			// POWER
			layout.Controls.Add(new Extensions.Label { Text = HumanReadable.Hardware.Power.Plan });
			powerPlan = new ComboBox()
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				//Dock = DockStyle.Left,
				//Anchor = AnchorStyles.Left,
				Width = 180,
			};
			powerPlan.Items.AddRange(new string[] {
				Power.Utility.GetModeName(Power.Mode.HighPerformance),
				Power.Utility.GetModeName(Power.Mode.Balanced),
				Power.Utility.GetModeName(Power.Mode.PowerSaver),
				Power.Utility.GetModeName(Power.Mode.Undefined)
			});
			int ppi = Controller.PowerPlan switch
			{
				Power.Mode.HighPerformance => 0,
				Power.Mode.Balanced => 1,
				Power.Mode.PowerSaver => 2,
				_ => 3,
			};
			powerPlan.SelectedIndex = ppi;
			tooltip.SetToolTip(powerPlan, "Power Mode to be used when this application is detected. Leaving this undefined disables it.");
			layout.Controls.Add(powerPlan);
			layout.Controls.Add(new EmptySpace());

			// FOREGROUND ONLY TOGGLE
			bool fge = ForegroundModeSelect.SelectedIndex != 0;
			//bool pwe = powerPlan.SelectedIndex != 3;

			ForegroundModeSelect.SelectedIndexChanged += (_, _2) =>
			{
				fge = ForegroundModeSelect.SelectedIndex != 0;
				bgAffinityMask.Enabled = bgPriorityClass.Enabled = fge && ForegroundModeSelect.SelectedIndex != 3;
			};

			bgAffinityMask.Enabled = bgPriorityClass.Enabled = fge && ForegroundModeSelect.SelectedIndex != 3;

			// PAGING
			layout.Controls.Add(new Extensions.Label { Text = "Allow paging" });
			allowPaging = new CheckBox() { Checked = Controller.AllowPaging };
			tooltip.SetToolTip(allowPaging, "Allow this application to be paged when it is requested.\nNOT FULLY IMPLEMENTED.");
			layout.Controls.Add(allowPaging);
			layout.Controls.Add(new EmptySpace());

			// lt.Controls.Add(new Label { Text=""})

			// AUDIO

			layout.Controls.Add(new Extensions.Label { Text = "Mixer Volume" });

			volumeMethod = new ComboBox()
			{
				//Dock = DockStyle.Left,
				//Anchor = AnchorStyles.Left,
				DropDownStyle = ComboBoxStyle.DropDownList,
				Items = { "Increase", "Decrease", "Increase from mute", "Decrease from full", "Force", HumanReadable.Generic.Ignore },
				SelectedIndex = 5,
				Width = 180,
			};

			layout.Controls.Add(volumeMethod);
			layout.Controls.Add(new EmptySpace());

			layout.Controls.Add(new EmptySpace());
			volume = new Extensions.NumericUpDownEx()
			{
				Unit = "%",
				DecimalPlaces = 1,
				Increment = 1.0M,
				Maximum = 100.0M,
				Minimum = 0.0M,
				Width = 80,
				Value = (Convert.ToDecimal(Controller.Volume) * 100M).Constrain(0M, 100M),
			};
			tooltip.SetToolTip(volume, "Percentage of device maximum volume.");
			layout.Controls.Add(volume);
			layout.Controls.Add(new EmptySpace());

			if (!AudioManagerEnabled)
			{
				volume.Enabled = false;
				volumeMethod.Enabled = false;
			}
			else
			{
				volumeMethod.SelectedIndex = Controller.VolumeStrategy switch
				{
					Audio.VolumeStrategy.Increase => 0,
					Audio.VolumeStrategy.Decrease => 1,
					Audio.VolumeStrategy.IncreaseFromMute => 2,
					Audio.VolumeStrategy.DecreaseFromFull => 3,
					Audio.VolumeStrategy.Force => 4,
					_ => 5,
				};

				// disable volume control if method is to ignore it
				volume.Enabled = volumeMethod.SelectedIndex != 5;
				volumeMethod.SelectedIndexChanged += (_, _2) => volume.Enabled = volumeMethod.SelectedIndex != 5;
			}

			if (!PowerManagerEnabled) powerPlan.Enabled = false;

			// LOG ADJUSTS

			logAdjusts = new CheckBox() { Checked = Controller.LogAdjusts };
			tooltip.SetToolTip(logAdjusts, "You can disable logging adjust events for this specific rule, from both UI and disk.\nUse Configuration > Logging > Process adjusts for all.");

			layout.Controls.Add(new Extensions.Label() { Text = "Log adjusts" });
			layout.Controls.Add(logAdjusts);
			layout.Controls.Add(new EmptySpace());

			logStartNExit = new CheckBox() { Checked = Controller.LogStartAndExit };
			tooltip.SetToolTip(logStartNExit, "You can disable logging adjust events for this specific rule, from both UI and disk.\nUse Configuration > Logging > Process adjusts for all.");

			layout.Controls.Add(new Extensions.Label() { Text = "Log start && exit" });
			layout.Controls.Add(logStartNExit);
			layout.Controls.Add(new EmptySpace());

			warning = new CheckBox() { Checked = Controller.Warn };
			tooltip.SetToolTip(warning, "Warn about this rule matching.\nSimilar to logging start and exit.");

			layout.Controls.Add(new Extensions.Label() { Text = "Warn" });
			layout.Controls.Add(warning);
			layout.Controls.Add(new EmptySpace());

			declareParent = new CheckBox() { Checked = Controller.DeclareParent };
			layout.Controls.Add(new Extensions.Label() { Text = "Log parent" });
			layout.Controls.Add(declareParent);
			if (modules.processmanager.EnableParentFinding)
				layout.Controls.Add(new EmptySpace());
			else
				layout.Controls.Add(new Extensions.Label() { Text = "Disabled" });
			tooltip.SetToolTip(declareParent, "Parent process logging slows log procedure significantly.\nMust be enabled in advanced settings also.");

			preforder = new NumericUpDown()
			{
				Minimum = 0,
				Maximum = 100,
				Value = Controller.OrderPreference.Constrain(0, 100),
			};
			tooltip.SetToolTip(preforder, "Rules with lower order are processed first.\nMost frequently modified apps should have lowest,\nbut this can also be used to control which rule is more likely to trigger.\n");

			layout.Controls.Add(new Extensions.Label() { Text = "Order preference" });
			layout.Controls.Add(preforder);
			layout.Controls.Add(new EmptySpace());

			// BUTTONS

			var finalizebuttons = new Extensions.TableLayoutPanel() { ColumnCount = 2, AutoSize = true };
			var saveButton = new Extensions.Button() { Text = "Save" }; // SAVE
			saveButton.Click += SaveInfo;
			finalizebuttons.Controls.Add(saveButton);
			// lt.Controls.Add(saveButton);
			var cancelButton = new Extensions.Button() { Text = "Cancel" }; // CLOSE
			cancelButton.Click += (_, _2) =>
			{
				DialogResult = DialogResult.Cancel;
				Close();
			};
			finalizebuttons.Controls.Add(cancelButton);

			var validatebutton = new Extensions.Button() { Text = "Validate" };
			validatebutton.Click += ValidateWatchedItem;
			validatebutton.Margin = BigPadding;

			layout.Controls.Add(validatebutton);

			layout.Controls.Add(finalizebuttons);

			// ---
			#endregion // BuildUI

			ResumeLayout(performLayout: false);
		}

		void SaveInfo(object _, System.EventArgs _2)
		{
			var enOrig = Controller.Enabled;
			Controller.Enabled = false;

			// TODO: VALIDATE FOR GRIMMY'S SAKE!

			// -----------------------------------------------
			// VALIDATE

			bool fnlen = (friendlyName.Text.Length > 0);
			bool exnam = (execName.Text.Length > 0);
			bool path = (pathName.Text.Length > 0);

			if (!fnlen || friendlyName.Text.IndexOf(']') >= 0 || friendlyName.Text.IndexOf('[') >= 0)
			{
				MessageBox.ShowModal("Malconfigured friendly name", "Friendly name is missing or includes illegal characters (such as square brackets).", MessageBox.Buttons.OK, parent: this);
				return;
			}

			if (!path && !exnam)
			{
				MessageBox.ShowModal("Configuration error", "No path nor executable defined.", MessageBox.Buttons.OK, parent: this);
				return;
			}

			string newfriendlyname = friendlyName.Text.Trim();

			if (modules.processmanager.GetControllerByName(newfriendlyname, out var dprc) && dprc != Controller)
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

			Controller.Rename(newfriendlyname);

			if (execName.Text.Length > 0)
			{
				var t_executables = execName.Text.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
				var f_exes = new string[t_executables.Length];
				for (int i = 0; i < t_executables.Length; i++)
					f_exes[i] = t_executables[i].Trim();
				Controller.Executables = (f_exes?.Length > 0) ? f_exes : Array.Empty<string>();
			}
			else
				Controller.Executables = Array.Empty<string>();

			Controller.Path = pathName.Text.Length > 0 ? pathName.Text.Trim() : null;
			if (priorityClass.SelectedIndex == 5) // ignored
			{
				Controller.Priority = null;
				Controller.PriorityStrategy = Process.PriorityStrategy.Ignore;
			}
			else
			{
				Controller.Priority = Process.Utility.IntToPriority(priorityClass.SelectedIndex); // is this right?
				Controller.PriorityStrategy = Process.PriorityStrategy.Ignore;
				Controller.PriorityStrategy = priorityClassMethod.SelectedIndex switch
				{
					0 => Process.PriorityStrategy.Increase,
					1 => Process.PriorityStrategy.Decrease,
					_ => Process.PriorityStrategy.Force,
				};
			}

			Controller.PathVisibility = pathVisibility.SelectedIndex switch
			{
				1 => Process.PathVisibilityOptions.Process,
				2 => Process.PathVisibilityOptions.Partial,
				3 => Process.PathVisibilityOptions.Full,
				4 => Process.PathVisibilityOptions.Smart,
				_ => Process.PathVisibilityOptions.Invalid,
			};
			if (affstrategy.SelectedIndex != 0)
			{
				if (cpumask == -1)
					Controller.AffinityMask = -1;
				else
				{
					Controller.AffinityMask = cpumask;
					Controller.AffinityStrategy = affstrategy.SelectedIndex == 1
						? Process.AffinityStrategy.Limit : Process.AffinityStrategy.Force;
				}
			}
			else
			{
				// strategy = ignore
				Controller.AffinityMask = -1;
				Controller.AffinityStrategy = Process.AffinityStrategy.Ignore;
			}

			Controller.ModifyDelay = (int)(modifyDelay.Value * 1_000);
			Controller.PowerPlan = Power.Utility.GetModeByName(powerPlan.Text);
			Controller.AllowPaging = allowPaging.Checked;
			Controller.SetForegroundMode((ForegroundMode)(ForegroundModeSelect.SelectedIndex - 1));

			if (bgPriorityClass.SelectedIndex != 5)
				Controller.BackgroundPriority = Process.Utility.IntToPriority(bgPriorityClass.SelectedIndex);
			else
				Controller.BackgroundPriority = null;

			if (bgAffinityMask.Value >= 0)
				Controller.BackgroundAffinity = Convert.ToInt32(bgAffinityMask.Value);
			else
				Controller.BackgroundAffinity = -1;

			if (ignorelist.Items.Count > 0 && execName.Text.Length == 0)
			{
				var ignlist = new List<string>(ignorelist.Items.Count + 1);
				foreach (ListViewItem item in ignorelist.Items)
					ignlist.Add(item.Text);

				Controller.IgnoreList = ignlist.ToArray();
			}
			else
				Controller.IgnoreList = Array.Empty<string>();

			if (desc.Text.Length > 0)
				Controller.Description = desc.Text;

			if (AudioManagerEnabled && volumeMethod.SelectedIndex != 5)
			{
				Controller.VolumeStrategy = volumeMethod.SelectedIndex switch
				{
					0 => Audio.VolumeStrategy.Increase,
					1 => Audio.VolumeStrategy.Decrease,
					2 => Audio.VolumeStrategy.IncreaseFromMute,
					3 => Audio.VolumeStrategy.DecreaseFromFull,
					4 => Audio.VolumeStrategy.Force,
					_ => Audio.VolumeStrategy.Ignore,
				};

				Controller.Volume = Convert.ToSingle(volume.Value / 100M);
			}

			if (IOPriorityEnabled)
				Controller.IOPriority = (Process.IOPriority)((ioPriority?.SelectedIndex ?? 0) - 1);

			Controller.LogAdjusts = logAdjusts.Checked;
			Controller.LogStartAndExit = logStartNExit.Checked;
			Controller.Warn = warning.Checked;

			Controller.DeclareParent = declareParent.Checked;

			Controller.OrderPreference = Convert.ToInt32(preforder.Value).Constrain(0, 100);

			Controller.Enabled = newPrc || enOrig;

			Controller.Repair();

			Controller.SaveConfig();

			Log.Information("[" + Controller.FriendlyName + "] " + (newPrc ? "Created" : "Modified"));

			DialogResult = DialogResult.OK;

			if (!newPrc) Controller.ResetInvalid();

			Close();
		}

		readonly Extensions.TextBox friendlyName, execName, pathName, desc;
		readonly ComboBox pathVisibility, priorityClass, priorityClassMethod, bgPriorityClass;

		readonly ComboBox affstrategy;
		readonly NumericUpDown affinityMask, bgAffinityMask;
		readonly ComboBox ioPriority;

		readonly ComboBox volumeMethod;
		readonly Extensions.NumericUpDownEx volume;

		readonly Extensions.Button allbutton, clearbutton;
		readonly Extensions.NumericUpDownEx modifyDelay;
		readonly CheckBox allowPaging, FullscreenMode;
		readonly ComboBox powerPlan, ForegroundModeSelect;
		readonly Extensions.ListViewEx ignorelist;
		readonly NumericUpDown preforder;

		readonly CheckBox logAdjusts, logStartNExit, declareParent, warning;

		int cpumask = 0;

		readonly static char[] InvalidCharacters = new[] { ']', '#', ';' };

		bool ValidateName(TextBox box, char[] invalidChars)
		{
			bool rv = true;
			int off;
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
			if (!IsHandleCreated || IsDisposed) return;

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
					if (box.TextLength > 0 && box.Text.IndexOf('.') < 0)
					{
						box.SelectionStart = box.TextLength;
						e.Cancel = true;
						FlashTextBox(box);
					}
				}
				else
				{
					if (box.TextLength > 0)
					{
						// Uh?
					}
				}
			}
		}

		void ValidateWatchedItem(object _sender, EventArgs _2)
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
							// is this better than enumerator?
							var results = System.IO.Directory.GetDirectories(di.FullName, search + "*", System.IO.SearchOption.TopDirectoryOnly);
							if (results.Length > 0) pfound = "Partial Match";
							/*
							var dirs = System.IO.Directory.EnumerateDirectories(di.FullName, search + "*", System.IO.SearchOption.TopDirectoryOnly);
							
							foreach (var dir in dirs) // alternate way to see if there was any results?
							{
								pfound = "Partial Match";
								break;
							}
							*/
						}
					}
				}
				catch
				{
					// NOP, don't caree
				}
			}

			var sbs = new System.Text.StringBuilder("Name: ", 256)
				.AppendLine(fnlen ? "OK" : "Fail");

			var samesection = Controller.FriendlyName.Equals(friendlyName.Text, StringComparison.InvariantCulture);
			if (!samesection && modules.processmanager.GetControllerByName(friendlyName.Text, out _)) sbs.Append("Friendly name conflict!");

			if (execName.Text.Length > 0)
			{
				sbs.Append(HumanReadable.System.Process.Executable).Append(": ").Append(exnam ? "OK" : "Fail").Append(" – Found: ").Append(exfound).AppendLine();
				string lowname = System.IO.Path.GetFileNameWithoutExtension(execName.Text);
				if (modules.processmanager.ProtectedProcessName(lowname))
					sbs.AppendLine("Defined executable is in core protected executables list. Adjustment may be denied.");

				if (modules.processmanager.IgnoreProcessName(lowname))
					sbs.AppendLine("Defined executable is in core ignore list. All changes denied.");
			}
			if (pathName.Text.Length > 0)
			{
				sbs.Append("Path: ").Append(path ? "OK" : "Fail").Append(" - Found: ").AppendLine(pfound);
				if (modules.processmanager.IgnoreSystem32Path && pathName.Text.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.System), StringComparison.InvariantCultureIgnoreCase))
					sbs.AppendLine("Path points to System32 even though core config denies it.");
			}

			if (!exnam && !path)
				sbs.AppendLine("Both path and executable are missing!");

			if (priorityClass.SelectedIndex == 5)
				sbs.AppendLine("Priority class is to be ignored.");
			if (cpumask == -1 || affstrategy.SelectedIndex == 0)
				sbs.AppendLine("Affinity is to be ignored.");
			if (ignorelist.Items.Count > 0 && execName.Text.Length > 0)
				sbs.AppendLine("Ignore list is meaningless with executable defined.");

			foreach (ListViewItem item in ignorelist.Items)
			{
				if (execName.Text.Length > 0 && item.Text.Equals(System.IO.Path.GetFileNameWithoutExtension(execName.Text), StringComparison.InvariantCultureIgnoreCase))
				{
					sbs.AppendLine("Ignore list contains the same executable as is being matched for. Stop that.");
					break;
				}
				else if (item.Text.EndsWith(".exe", StringComparison.InvariantCultureIgnoreCase))
				{
					sbs.AppendLine("Ignore list items should not include extensions.");
					break;
				}
			}

			if (ioPriority != null && ioPriority.SelectedIndex != 0)
			{
				sbs.AppendLine("Warning: I/O priority set! Be certain of what you're doing!");
				if (ioPriority.SelectedIndex == 2 && ioPriority.SelectedIndex > 3)
					sbs.AppendLine("\tUnsupported I/O mode selected. Behaviour may be unexpected.");
			}

			MessageBox.ShowModal("Validation results", sbs.ToString(), MessageBox.Buttons.OK, parent: this);
		}
	}
}