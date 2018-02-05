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
using System.Collections.Specialized;
using System;

namespace TaskMaster
{
	public class AppEditWindow : Form
	{
		readonly ProcessController process;
		readonly ListViewItem item;

		public AppEditWindow(string executable, ListViewItem ri)
		{
			item = ri;
			string exekey = System.IO.Path.GetFileNameWithoutExtension(executable);
			process = TaskMaster.processmanager.getController(exekey);
			if (process == null)
			{
				Log.Fatal("'{Executable}' not found", executable);
				throw new System.ArgumentException("Invalid value.", nameof(executable));
			}

			WindowState = FormWindowState.Normal;
			FormBorderStyle = FormBorderStyle.FixedDialog; // no min/max buttons as wanted
			MinimizeBox = false;
			MaximizeBox = false;

			BuildUI();

			Show();
		}

		void SaveInfo(object sender, System.EventArgs ev)
		{
			Log.Warning("SAVING NOT SUPPORTED YET");

			Console.WriteLine("[{0}]", friendlyName.Text);
			Console.WriteLine("Image={0}", execName.Text);
			Console.WriteLine("Priority={0}", priorityClass.SelectedIndex);
			Console.WriteLine("Affinity={0}", affinityMask.Text);
			Console.WriteLine("Power Plan={0}", powerPlan.Text);
			Console.WriteLine("Rescan={0}", rescanFreq.Value);
			Console.WriteLine("Allow paging={0}", allowPaging.Checked);
			Console.WriteLine("Foreground only={0}", foregroundOnly.Checked);
		}

		TextBox friendlyName = new TextBox();
		TextBox execName = new TextBox();
		ComboBox priorityClass;
		TextBox affinityMask = new TextBox();
		NumericUpDown rescanFreq = new NumericUpDown();
		CheckBox allowPaging = new CheckBox();
		ComboBox powerPlan = new ComboBox();
		CheckBox foregroundOnly = new CheckBox();

		void BuildUI()
		{
			Size = new System.Drawing.Size(260, 300);
			AutoSizeMode = AutoSizeMode.GrowOnly;
			AutoSize = true;

			Text = string.Format("{0} ({1}) – {2}",
								 process.FriendlyName,
								 process.Executable,
								 System.Windows.Forms.Application.ProductName);
			Padding = new Padding(12);

			var tooltip = new ToolTip();

			var lt = new TableLayoutPanel
			{
				Parent = this,
				ColumnCount = 2,
				//lrows.RowCount = 10;
				Dock = DockStyle.Fill
			};

			lt.Controls.Add(new Label { Text = "Friendly name" });
			friendlyName.Text = process.FriendlyName;
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
			lt.Controls.Add(new Label { Text = "Executable" });
			execName.Text = process.Executable;
			tooltip.SetToolTip(execName, "Executable name, used to recognize these applications.\nFull filename, including extension if any.");
			lt.Controls.Add(execName);

			lt.Controls.Add(new Label { Text = "Priority class" });
			priorityClass = new ComboBox
			{
				Dock = DockStyle.Left,
				DropDownStyle = ComboBoxStyle.DropDownList,
				Items = { "Idle", "Below Normal", "Normal", "Above Normal", "High" }, // System.Enum.GetNames(typeof(ProcessPriorityClass)), 
				SelectedIndex = 2
			};
			priorityClass.SelectedIndex = process.Priority.ToInt32();
			tooltip.SetToolTip(priorityClass, "CPU priority for the application.");
			lt.Controls.Add(priorityClass);

			lt.Controls.Add(new Label { Text = "Affinity" });
			affinityMask.Text = (process.Affinity.ToInt32() == ProcessManager.allCPUsMask ? "0" : process.Affinity.ToString());
			tooltip.SetToolTip(affinityMask, "CPU core afffinity as integer mask.\nEnter 0 to let OS manage this as normal.\nFull affinity is same as 0, there's no difference.\nExamples:\n14 = all but first core on quadcore.\n254 = all but first core on octocore.");
			lt.Controls.Add(affinityMask);

			lt.Controls.Add(new Label { Text = "Rescan frequency" });
			rescanFreq.Value = process.Rescan;
			tooltip.SetToolTip(rescanFreq, "How often to rescan for this app, in minutes.\nSometimes instances slip by.");
			lt.Controls.Add(rescanFreq);

			//lt.Controls.Add(new Label { Text="Children"});
			//lt.Controls.Add(new Label { Text="Child priority"});

			lt.Controls.Add(new Label { Text = "Power plan" });
			foreach (var t in PowerManager.PowerModes)
				powerPlan.Items.Add(t);
			int ppi = System.Convert.ToInt32(process.PowerPlan);
			powerPlan.SelectedIndex = System.Math.Max(ppi, 3);
			tooltip.SetToolTip(powerPlan, "Power Mode to be used when this application is detected.");
			lt.Controls.Add(powerPlan);

			lt.Controls.Add(new Label { Text = "Foreground only" });
			foregroundOnly.Checked = process.ForegroundOnly;
			tooltip.SetToolTip(foregroundOnly, "Lower priority and power mode is restored when this app is not in focus.");
			lt.Controls.Add(foregroundOnly);

			lt.Controls.Add(new Label { Text = "Allow paging" });
			allowPaging.Checked = process.AllowPaging;
			tooltip.SetToolTip(allowPaging, "Allow this application to be paged when it is requested.");
			lt.Controls.Add(allowPaging);

			//lt.Controls.Add(new Label { Text=""})

			Button saveButton = new Button(); // SAVE
			saveButton.Text = "Save";
			saveButton.Click += SaveInfo;
			lt.Controls.Add(saveButton);
			Button closeButton = new Button(); // CLOSE
			closeButton.Text = "Close";
			closeButton.Click += (sender, e) => { Close(); };
			lt.Controls.Add(closeButton);

			// ---
		}
	}
}
