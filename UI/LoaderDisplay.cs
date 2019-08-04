//
// UI.LoaderDisplay.cs
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
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using MKAh;

namespace Taskmaster.UI
{
	using static Taskmaster;

	public class LoaderDisplay : UniForm
	{
		readonly Process.Manager processmanager = null;

		public LoaderDisplay(Process.Manager manager)
		{
			Text = "System loaders – " + ProductName;

			processmanager = manager;

			LoaderList = new Extensions.ListViewEx()
			{
				View = View.Details,
				HeaderStyle = ColumnHeaderStyle.Nonclickable,
				Dock = DockStyle.Fill,
				Width = 320,
				Height = 460,
			};

			LoaderList.ListViewItemSorter = new LoaderSorter();

			AutoSize = true;
			AutoSizeMode = AutoSizeMode.GrowOnly;

			LoaderList.Columns.AddRange(new[] {
				new ColumnHeader() { Text = "Process" },
				new ColumnHeader() { Text = "Id#" },
				new ColumnHeader() { Text = "CPU" },
				new ColumnHeader() { Text = "MEM" },
				new ColumnHeader() { Text = "IO" },
			});

			using var uicfg = Taskmaster.Config.Load(UIConfigFilename);
			var winsec = uicfg.Config[Constants.Windows];
			var winpos = winsec["Loaders"].IntArray;

			if (winpos?.Length == 2)
			{
				StartPosition = FormStartPosition.Manual;
				Bounds = new System.Drawing.Rectangle(winpos[0], winpos[1], Bounds.Width, Bounds.Height);
				//Location = new System.Drawing.Point(Bounds.Left, Bounds.Top);

				if (!Screen.AllScreens.Any(screen => screen.Bounds.IntersectsWith(Bounds)))
					CenterToParent();
			}

			var colsec = uicfg.Config[Constants.Columns];

			var defaultCols = new int[] { 160, 60, 60, 80, 80 };
			//var cols = colsec.GetOrSet("Loaders", defaultCols).IntArray;
			var cols = defaultCols;
			//if (cols.Length != defaultCols.Length) cols = defaultCols;

			int width = 480;
			if (cols?.Length == 5)
			{
				LoaderList.Columns[0].Width = cols[0];
				LoaderList.Columns[1].Width = cols[1];
				LoaderList.Columns[2].Width = cols[2];
				LoaderList.Columns[3].Width = cols[3];
				LoaderList.Columns[4].Width = cols[4];

				width = cols[0] + cols[1] + cols[2] + cols[3] + cols[4] + 20;
			}

			Width = width;
			Height = 180;

			Controls.Add(LoaderList);

			Visible = true;

			// TODO: get location & size

			Show();
			BringToFront();

			processmanager.LoaderDetection += LoaderEvent;

			timer.Interval = 0_500;
			timer.Elapsed += UIUpdate;
			timer.Start();
		}

		System.Drawing.Color FGColor = new ListViewItem().ForeColor; // dumb

		void UIUpdate(object _sender, System.Timers.ElapsedEventArgs _ea)
		{
			if (!IsHandleCreated || disposed) return;

			var now = DateTimeOffset.UtcNow;

			var removeList = new List<LoadListPair>(5);

			try
			{
				foreach (var pair in LoaderInfos.Values)
				{
					var minutessince = now.Since(pair.Load.Last).TotalMinutes;

					if (Trace) Logging.DebugMsg("LOADER UI: " + pair.Load.Instance + " --- active last: " + $"{minutessince:N1} minutes ago");

					if (minutessince > 3d)
					{
						LoaderInfos.TryRemove(pair.Load.Instance, out _);
						removeList.Add(pair);
					}
					else if (minutessince > 1d)
					{
						pair.ListItem.ForeColor = System.Drawing.SystemColors.GrayText;
					}
					else
					{
						// restore color
						pair.ListItem.ForeColor = FGColor;
						pair.Load.Update();
						BeginInvoke(new Action(() => UpdateListView(pair)));
					}
				}

				BeginInvoke(new Action(() =>
				{
					if (!IsHandleCreated || disposed) return;

					lock (ListLock)
					{
						foreach (var loader in removeList)
						{
							LoaderList.Items.Remove(loader.ListItem);
							loader.ListItem = null;
						}

						if (DirtyList)
						{
							LoaderList.Sort();

							DirtyList = false;
						}
					}
				}));
			}
			catch (InvalidOperationException)
			{
				// window closed
			}
		}

		object ListLock = new object();
		bool DirtyList = false;

		readonly System.Timers.Timer timer = new System.Timers.Timer();
		
		readonly ConcurrentDictionary<string, LoadListPair> LoaderInfos = new ConcurrentDictionary<string, LoadListPair>(1, 5);

		public void LoaderEvent(object sender, Process.LoaderEvent ea)
		{
			if (!IsHandleCreated || disposed) return;

			string key = ea.Load.Instance;

			if (LoaderInfos.TryGetValue(key, out LoadListPair pair))
				pair.Load = ea.Load; // unnecessary?
			else
				pair = new LoadListPair(ea.Load, null);

			BeginInvoke(new Action(() => UpdateItem(pair)));
		}

		void UpdateItem(LoadListPair pair)
		{
			if (!IsHandleCreated || disposed) return;

			if (pair.ListItem != null) // update
			{
				var li = pair.ListItem;

				BeginInvoke(new Action(() => UpdateListView(pair)));

				lock (ListLock)
				{
					LoaderList.Items.Remove(li);
					if (LoaderList.Items.Count > pair.Load.Order)
						LoaderList.Items.Insert(pair.Load.Order, li);
					else
						LoaderList.Items.Add(li);

					DirtyList = true;
				}
			}
			else
			{
				var load = pair.Load;
				var instance = pair.Load.Instance;

				pair.ListItem = new ListViewItem(new[] {
					instance,
					load.LastPid.ToString(),
					$"{load.CPULoad.Average:N1} %",
					$"{load.RAMLoad.Current:N2} GiB",
					$"{load.IOLoad.Average:N1} MiB/s"
				});

				lock (ListLock)
				{
					LoaderInfos.TryAdd(instance, pair);

					LoaderList.Items.Add(pair.ListItem);

					DirtyList = true;
				}
			}
		}

		void UpdateListView(LoadListPair pair)
		{
			if (!IsHandleCreated || disposed) return;

			var li = pair.ListItem;
			var load = pair.Load;

			//value.ListItem.SubItems[0] = key
			li.SubItems[1].Text = load.LastPid.ToString();
			li.SubItems[2].Text = $"{load.CPULoad.Average:N1} %";
			li.SubItems[3].Text = $"{load.RAMLoad.Current:N2} GiB";
			li.SubItems[4].Text = $"{load.IOLoad.Average:N1} MiB/s";
		}

		readonly Extensions.ListViewEx LoaderList;

		#region IDispose
		public event EventHandler<DisposedEventArgs> OnDisposed;

		bool disposed = false;

		protected override void Dispose(bool disposing)
		{
			if (disposed) return;
			disposed = true;

			if (disposing)
			{
				timer?.Dispose();

				processmanager.LoaderTracking = false;
				processmanager.LoaderDetection -= LoaderEvent;

				// 
				using var cfg = Taskmaster.Config.Load(UIConfigFilename);

				var saveBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;

				cfg.Config[Constants.Windows]["Loaders"].IntArray = new int[] { saveBounds.Left, saveBounds.Top };

				OnDisposed?.Invoke(this, DisposedEventArgs.Empty);
				OnDisposed = null;
			}

			base.Dispose(disposing);
		}
		#endregion Dispose
	}

	public class LoadListPair
	{
		public Process.LoadInfo Load { get; set; }

		public ListViewItem ListItem { get; set;  }

		public LoadListPair(Process.LoadInfo info, ListViewItem item)
		{
			Load = info;
			ListItem = item;
		}
	}

	public class LoaderSorter : IComparer
	{
		public int Column { get; set; } = 2;

		public SortOrder Order { get; set; } = SortOrder.Ascending;

		readonly CaseInsensitiveComparer Comparer = new CaseInsensitiveComparer();

		public int Compare(object x, object y)
		{
			var lix = (ListViewItem)x;
			var liy = (ListViewItem)y;

			int result;

			if (Column != 0)
			{
				string lixs = lix.SubItems[Column].Text;
				int off = lixs.IndexOf(' ');
				if (off > 0) lixs = lixs.Substring(0, off);
				//lixs = lix.SubItems[Column].Text.Split(delimiter, 1, StringSplitOptions.None)[0];
				string liys = liy.SubItems[Column].Text;
				off = liys.IndexOf(' ');
				if (off > 0) liys = liys.Substring(0, off);

				if (Trace) Logging.DebugMsg("SORTING: " + lixs + " // " + liys);

				result = Comparer.Compare(Convert.ToSingle(lixs), Convert.ToSingle(liys));
			}
			else
				result = Comparer.Compare(lix.SubItems[Column].Text, liy.SubItems[Column].Text);

			return Order == SortOrder.Ascending ? result : -result;
		}
	}
}
