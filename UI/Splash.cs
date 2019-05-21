//
// UI.Splash.cs
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
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Taskmaster.UI
{
	using static Taskmaster;

	internal class Splash : UniForm
	{
		readonly ProgressBar coreProgress = null;
		readonly ProgressBar subProgress = null;
		readonly AlignedLabel loadMessage = null;

		readonly ListView LoadEventLog = null;

		readonly Button exitButton = null;

		int Loaded = 0;
		int MaxLoad = 1;

		int SubLoaded = 0;
		int MaxSubLoad = 1;

		public Splash(int itemsToLoad) : base(true)
		{
			StartPosition = FormStartPosition.CenterScreen;

			MaxLoad = itemsToLoad;

			Padding = new Padding(6);

			AutoSize = true;

			Text = "Loading " + Taskmaster.Name + "!!";

			var layout = new TableLayoutPanel()
			{
				Dock = DockStyle.Fill,
				AutoSize = true,
				ColumnCount = 2,
			};

			loadMessage = new AlignedLabel()
			{
				Dock = DockStyle.Top,
				Anchor = AnchorStyles.Top | AnchorStyles.Left,
				Text = "Loading...",
			};

			coreProgress = new ProgressBar()
			{
				Style = ProgressBarStyle.Continuous,
				Value = Loaded,
				Maximum = MaxLoad,
				Dock = DockStyle.Top,
			};

			subProgress = new ProgressBar()
			{
				Style = ProgressBarStyle.Continuous,
				Value = 0,
				Maximum = MaxSubLoad,
				Dock = DockStyle.Top,
			};

			exitButton = new Button()
			{
				//Text = "Cancel && Quit",
				Text = "Cancel",
				Anchor = AnchorStyles.Top,
			};

			LoadEventLog = new ListView()
			{
				View = View.Details,
				HeaderStyle = ColumnHeaderStyle.None,
				Dock = DockStyle.Fill,
				Anchor = AnchorStyles.Top | AnchorStyles.Left,
			};
			LoadEventLog.Columns.Add(string.Empty, -2, HorizontalAlignment.Left);
			LoadEventLog.Columns[0].Width = -2;
			LoadEventLog.Columns[0].Width -= 2;

			exitButton.Click += ExitButton_Click;

			layout.Controls.Add(loadMessage);
			layout.SetColumnSpan(loadMessage, 2);
			layout.Controls.Add(coreProgress);
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
			layout.Controls.Add(exitButton);
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
			layout.SetRowSpan(exitButton, 2);
			layout.Controls.Add(subProgress);

			layout.Controls.Add(LoadEventLog);
			layout.SetColumnSpan(LoadEventLog, 2);

			Controls.Add(layout);
		}

		void ExitButton_Click(object sender, EventArgs e)
			=> UnifiedExit();

		internal bool AutoClose { get; set; } = true;

		internal void LoadEvent(string message, LoadEventType type, int subItemsDone=-1, int subItemsTotal=-1)
			=> LoadEvent(null, new LoadEventArgs(message, type, subItemsDone, subItemsTotal));

		internal void LoadEvent(object sender, LoadEventArgs ea)
		{
			Debug.WriteLine("[" + Loaded + "/" + MaxLoad + "] " + ea.Message);

			if (ea.MaxSubProgress > 0) MaxSubLoad = ea.MaxSubProgress;

			switch (ea.Type)
			{
				case LoadEventType.Loaded:
					Loaded++;
					BeginInvoke(new Action(async () =>
					{
						if (DisposedOrDisposing) return;

						coreProgress.Value = Loaded;
						PushLog(ea.Message);

						if (AutoClose && Loaded >= MaxLoad)
						{
							await Task.Delay(250).ConfigureAwait(false);

							if (!IsHandleCreated) return;

							BeginInvoke(new Action(() =>
							{
								if (DisposedOrDisposing) return;

								Close();
							}));
						}

						coreProgress.Invalidate();
					}));
					break;
				case LoadEventType.SubLoaded:
					SubLoaded++;
					BeginInvoke(new Action(() =>
					{
						if (DisposedOrDisposing) return;

						subProgress.Value = SubLoaded;
						PushLog(ea.Message);
						subProgress.Invalidate();
					}));
					break;
				case LoadEventType.Info:
					BeginInvoke(new Action(() =>
					{
						if (DisposedOrDisposing) return;

						loadMessage.Text = ea.Message;
						coreProgress.Maximum = MaxLoad;
						subProgress.Maximum = MaxSubLoad;
						coreProgress.Invalidate();
						subProgress.Invalidate();
						PushLog(ea.Message);
					}));
					break;
				case LoadEventType.Reset:
					SubLoaded = 0;
					MaxSubLoad = ea.MaxSubProgress;
					BeginInvoke(new Action(() =>
					{
						if (DisposedOrDisposing) return;

						subProgress.Value = 0;
						subProgress.Maximum = MaxSubLoad;
						subProgress.Invalidate();
						PushLog(ea.Message);
					}));
					break;
			}
		}

		void PushLog(string message)
		{
			var now = DateTime.Now;
			LoadEventLog.Items.Add($"[{now.Hour:00}:{now.Minute:00}:{now.Second:00}.{now.Millisecond:N000}] {message}");
			LoadEventLog.Columns[0].Width = -2;
			LoadEventLog.EnsureVisible(LoadEventLog.Items.Count - 1);
		}

		internal void Reveal()
		{
			TopMost = true;
			Show();
			Activate();
		}

		#region IDispose
		bool DisposedOrDisposing = false;
		protected override void Dispose(bool disposing)
		{
			if (DisposedOrDisposing) return;

			base.Dispose(disposing);

			if (disposing)
			{
				DisposedOrDisposing = true;

				LoadEventLog?.Clear();
			}
		}
		#endregion Dispose
	}
}

namespace Taskmaster
{
	enum LoadEventType
	{
		Reset,
		Info,
		Loaded,
		SubLoaded,
	};

	internal class LoadEventArgs : EventArgs
	{
		internal string Message = string.Empty;

		internal LoadEventType Type = LoadEventType.Info;

		internal int SubProgress = 0;
		internal int MaxSubProgress = -1;

		internal LoadEventArgs(string message, LoadEventType type, int subItemsProgress=-1, int maxSubItems=-1)
		{
			Type = type;
			Message = message;
			SubProgress = subItemsProgress;
			MaxSubProgress = maxSubItems;
		}
	}
}
