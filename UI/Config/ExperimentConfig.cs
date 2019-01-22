//
// ExperimentConfig.cs
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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Taskmaster.UI.Config
{
	public sealed class ExperimentConfig : UI.UniForm
	{
		public ExperimentConfig()
		{
			Text = "Experiment Configuration";
			AutoSizeMode = AutoSizeMode.GrowAndShrink;
			AutoSize = true;

			var layout = new TableLayoutPanel()
			{
				ColumnCount = 2,
				Dock = DockStyle.Fill,
				AutoSize = true,
				Parent = this,
			};

			layout.Controls.Add(new Label { Text = "EXPERIMENTAL", Font = boldfont, ForeColor = System.Drawing.Color.Maroon, AutoSize = true });
			layout.Controls.Add(new Label { Text = "You've been warned", Font = boldfont, ForeColor=System.Drawing.Color.Maroon, AutoSize = true });

			var savebutton = new Button()
			{
				Text = "Save",
			};
			savebutton.NotifyDefault(true);

			var cancelbutton = new Button()
			{
				Text = "Cancel",
			};
			cancelbutton.Click += Cancelbutton_Click;

			// EXPERIMENTS

			var IgnoreRecentlyModifiedCooldown = new Extensions.NumericUpDownEx()
			{
				Minimum = 0,
				Maximum = 60,
				Unit = "mins",
				Value = Convert.ToDecimal(Taskmaster.IgnoreRecentlyModified.TotalMinutes),
			};

			layout.Controls.Add(new Label { Text = "Ignore recently modified" });
			layout.Controls.Add(IgnoreRecentlyModifiedCooldown);

			var RecordAnalysisDelay = new Extensions.NumericUpDownEx()
			{
				Minimum = 0,
				Maximum = 300,
				Unit = "secs",
				Value = Convert.ToDecimal(Taskmaster.RecordAnalysis.TotalSeconds),
			};

			layout.Controls.Add(new Label { Text = "Record analysis delay" });
			layout.Controls.Add(RecordAnalysisDelay);

			// FILL IN BOTTOM

			savebutton.Click += (_, _ea) =>
			{
				// Set to current use

				Taskmaster.IgnoreRecentlyModified = TimeSpan.FromMinutes(Convert.ToDouble(IgnoreRecentlyModifiedCooldown.Value));
				Taskmaster.RecordAnalysis = TimeSpan.FromSeconds(Convert.ToDouble(RecordAnalysisDelay.Value));

				// Record for restarts

				var corecfg = Taskmaster.Config.Load(Taskmaster.coreconfig);
				var cfg = corecfg.Config;

				var exsec = cfg["Experimental"];
				exsec["Ignore recently modified"].IntValue = Convert.ToInt32(Taskmaster.IgnoreRecentlyModified.TotalMinutes);
				exsec["Record analysis"].IntValue = Convert.ToInt32(RecordAnalysisDelay.Value);

				corecfg.MarkDirty();

				DialogResult = DialogResult.OK;
				Close();
			};

			layout.Controls.Add(savebutton);
			layout.Controls.Add(cancelbutton);
		}

		private void Cancelbutton_Click(object sender, EventArgs e)
		{
			DialogResult = DialogResult.Cancel;
			Close();
		}
	}
}
