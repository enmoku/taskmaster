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

using MKAh;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Taskmaster.UI
{
	using static Application;

	public class LoaderDisplay : UniForm
	{
		readonly Process.Manager processmanager;

		public LoaderDisplay(Process.Manager manager)
		{
			Text = "System loaders – " + ProductName;

			processmanager = manager;

			LoaderList = new Extensions.ListViewEx()
			{
				View = View.Details,
				HeaderStyle = ColumnHeaderStyle.Clickable,
				Dock = DockStyle.Fill,
				Width = 360,
				Height = 460,
				FullRowSelect = true,
			};

			var sorter = new LoaderSorter();
			sorter.TopItem = totalSystem;

			LoaderList.ListViewItemSorter = sorter;

			LoaderList.ColumnClick += (_, ea) =>
			{
				var sorter = LoaderList.ListViewItemSorter as LoaderSorter;

				if(sorter.Column == ea.Column)
				{
					sorter.Order = sorter.Order == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
				}
				else
				{
					sorter.Order = SortOrder.Descending; // default
					sorter.Column = ea.Column;
				}

				LoaderList.Sort();
			};

			usedCpuCell = totalSystem.SubItems[2];
			usedMemCell = totalSystem.SubItems[3];

			freeCpuCell = freeSystem.SubItems[2];
			freeMemCell = freeSystem.SubItems[3];

			totalSystem.BackColor = System.Drawing.SystemColors.Control;
			freeSystem.BackColor = System.Drawing.SystemColors.Control;

			LoaderList.Items.Add(totalSystem);
			LoaderList.Items.Add(freeSystem);

			AutoSize = true;
			AutoSizeMode = AutoSizeMode.GrowOnly;

			LoaderList.Columns.AddRange(new[] {
				new ColumnHeader() { Text = "Process" },
				new ColumnHeader() { Text = "Id#" },
				new ColumnHeader() { Text = "CPU" },
				new ColumnHeader() { Text = "MEM" },
				new ColumnHeader() { Text = "IO" },
			});

			using var uicfg = Application.Config.Load(UIConfigFilename);
			var winsec = uicfg.Config[Constants.Windows];
			var winpos = winsec["Loaders"].IntArray;

			Width = 480;
			Height = 180;

			if (winpos?.Length == 4)
			{
				StartPosition = FormStartPosition.Manual;
				Bounds = new System.Drawing.Rectangle(winpos[0], winpos[1], winpos[2], winpos[3]);

				Width = Bounds.Width;
				Height = Bounds.Height;

				//Location = new System.Drawing.Point(Bounds.Left, Bounds.Top);

				if (!Screen.AllScreens.Any(screen => screen.Bounds.IntersectsWith(Bounds)))
					CenterToParent();
			}

			var colsec = uicfg.Config[Constants.Columns];

			var defaultCols = new int[] { 160, 60, 60, 80, 80 };
			//var cols = colsec.GetOrSet("Loaders", defaultCols).IntArray;
			//if (cols.Length != defaultCols.Length) cols = defaultCols;

			int[] cols = Array.Empty<int>();

			try
			{
				cols = colsec["Loaders"].IntArray;
			}
			catch { /* Ignore */ }

			if (cols?.Length != 5) cols = defaultCols;

			LoaderList.Columns[0].Width = cols[0];
			LoaderList.Columns[1].Width = cols[1];
			LoaderList.Columns[2].Width = cols[2];
			LoaderList.Columns[3].Width = cols[3];
			LoaderList.Columns[4].Width = cols[4];

			LoaderList.Width = cols[0] + cols[1] + cols[2] + cols[3] + cols[4];

			Controls.Add(LoaderList);

			Visible = true;

			// TODO: get location & size

			Show();
			BringToFront();

			processmanager.LoaderActivity += LoaderActivityEvent;
			processmanager.LoaderRemoval += LoaderEndEvent;

			timer.Interval = 0_500;
			timer.Tick += UIUpdate;
			timer.Start();
		}

		System.Drawing.Color FGColor = new ListViewItem().ForeColor; // dumb

		readonly ListViewItem freeSystem = new ListViewItem(new[] { "Free", "~", "~", "~", "~" });
		readonly ListViewItem totalSystem = new ListViewItem(new[] { "Total", "~", "~", "~", "~"});
		readonly ListViewItem.ListViewSubItem usedCpuCell;
		readonly ListViewItem.ListViewSubItem usedMemCell;
		readonly ListViewItem.ListViewSubItem freeCpuCell;
		readonly ListViewItem.ListViewSubItem freeMemCell;

		void UIUpdate(object _sender, EventArgs _ea)
		{
			if (!IsHandleCreated || disposed) return;

			var now = DateTimeOffset.UtcNow;

			var removeList = new List<LoadListPair>(5);

			var idle = cpumonitor.LastIdle;
			//var totalmem = Memory.Total;
			var memfree = Memory.FreeBytes;
			var memused = Memory.Used;

			usedCpuCell.Text = $"{(1f - idle) * 100f:N1} %";
			freeCpuCell.Text = $"{idle * 100f:N1} %";

			freeMemCell.Text = $"{memfree / MKAh.Units.Binary.Giga:N2} GiB";
			usedMemCell.Text = $"{memused / MKAh.Units.Binary.Giga:N2} GiB";

			try
			{
				foreach (var pair in LoaderInfos.Values)
				{
					var load = pair.Load;
					var minutessince = now.Since(load.Last).TotalMinutes;

					if (Trace) Logging.DebugMsg("LOADER UI: " + load.Instance + " --- active last: " + $"{minutessince:N1} minutes ago");

					if (minutessince > 3d)
					{
						LoaderInfos.TryRemove(load.Instance, out _);
						removeList.Add(pair);
					}
					else if (minutessince > 1d || load.InstanceCount == 0)
					{
						pair.ListItem.ForeColor = System.Drawing.SystemColors.GrayText;
					}
					else
					{
						// restore color
						pair.ListItem.ForeColor = FGColor;
						load.Update();

						UpdateListView(pair);
					}
				}

				foreach (var loader in removeList)
				{
					LoaderList.Items.Remove(loader.ListItem);
					loader.ListItem = null;
				}

				LoaderList.Sort();
			}
			catch (InvalidOperationException)
			{
				// window closed
			}
		}

		object ListLock = new object();
		bool DirtyList = false;

		readonly System.Windows.Forms.Timer timer = new Timer();

		readonly ConcurrentDictionary<string, LoadListPair> LoaderInfos = new ConcurrentDictionary<string, LoadListPair>(1, 5);

		public void LoaderEndEvent(object sender, Process.LoaderEndEvent ea)
		{
			if (disposed || !IsHandleCreated) return;

			BeginInvoke(new Action(() => RemoveLoader(ea.Loader)));
		}

		void RemoveLoader(Process.InstanceGroupLoad group)
		{
			if (disposed || !IsHandleCreated) return;

			try
			{
				var key = group.Instance;
				if (LoaderInfos.TryRemove(key, out var pair))
				{
					var li = pair.ListItem;
					if (li != null) // should only happen if this happens too early
						LoaderList.Items.Remove(li);
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		public async void LoaderActivityEvent(object sender, Process.LoaderEvent ea)
		{
			if (!IsHandleCreated || disposed) return;

			await Task.Delay(0).ConfigureAwait(false);

			LoadListPair[] pairs = new LoadListPair[ea.Loaders.Length];

			for (int i = 0; i < ea.Loaders.Length; i++)
			{
				var load = ea.Loaders[i];

				string key = load.Instance;

				if (LoaderInfos.TryGetValue(key, out LoadListPair pair))
					pair.Load = load; // unnecessary?
				else
					pair = new LoadListPair(load, null);

				pairs[i] = pair;
			}

			BeginInvoke(new Action(() => UpdateItems(pairs)));
		}

		void UpdateItems(LoadListPair[] pairs)
		{
			if (!IsHandleCreated || disposed) return;

			for (int i = 0; i < pairs.Length; i++)
			{
				var pair = pairs[i];
				if (pair.ListItem != null) // update
				{
					var li = pair.ListItem;

					BeginInvoke(new Action(() => UpdateListView(pair)));

					LoaderList.Items.Remove(li);
					if (LoaderList.Items.Count > pair.Load.Order)
						LoaderList.Items.Insert(pair.Load.Order, li);
					else
						LoaderList.Items.Add(li);
				}
				else
				{
					var load = pair.Load;
					var instance = load.Instance;

					pair.ListItem = new ListViewItem(new[] {
						instance,
						load.LastPid.ToString(),
						$"{load.CPULoad.Average:N1} %",
						$"{load.RAMLoad.Current:N2} GiB",
						$"{load.IOLoad.Average:N1} MiB/s"
					});

					LoaderInfos.TryAdd(instance, pair);

					LoaderList.Items.Add(pair.ListItem);
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
				timer.Dispose();

				processmanager.LoaderTracking = false;
				processmanager.LoaderActivity -= LoaderActivityEvent;

				// 
				using var cfg = Application.Config.Load(UIConfigFilename);

				var saveBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;

				cfg.Config[Constants.Windows]["Loaders"].IntArray = new int[] { saveBounds.Left, saveBounds.Top, saveBounds.Width, saveBounds.Height };

				cfg.Config[Constants.Columns]["Loaders"].IntArray
					= new [] { LoaderList.Columns[0].Width, LoaderList.Columns[1].Width, LoaderList.Columns[2].Width, LoaderList.Columns[3].Width, LoaderList.Columns[4].Width };

				OnDisposed?.Invoke(this, DisposedEventArgs.Empty);
				OnDisposed = null;
			}

			base.Dispose(disposing);
		}
		#endregion Dispose
	}

	public class LoadListPair
	{
		public Process.InstanceGroupLoad Load { get; set; }

		public ListViewItem? ListItem { get; set; }

		public LoadListPair(Process.InstanceGroupLoad info, ListViewItem item)
		{
			Load = info;
			ListItem = item;
		}
	}

	public class LoaderSorter : IComparer
	{
		public int Column { get; set; } = 2;

		/// <summary>
		/// Always kept at top.
		/// </summary>
		public ListViewItem TopItem { get; set; } = null;

		public SortOrder Order { get; set; } = SortOrder.Descending;

		readonly CaseInsensitiveComparer Comparer = new CaseInsensitiveComparer();

		public int Compare(object x, object y)
		{
			var lix = (ListViewItem)x;
			var liy = (ListViewItem)y;

			if (lix == TopItem) return -1;
			else if (liy == TopItem) return 1;

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

				float lixf, liyf;

				try { lixf = Convert.ToSingle(lixs); }
				catch { lixf = float.NaN; }

				try { liyf = Convert.ToSingle(liys); }
				catch { liyf = float.NaN; }

				result = Comparer.Compare(lixf, liyf);
			}
			else
				result = Comparer.Compare(lix.SubItems[Column].Text, liy.SubItems[Column].Text);

			return Order == SortOrder.Ascending ? result : -result;
		}
	}
}
