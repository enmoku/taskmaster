//
// UI.LoaderDisplay.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2019–2020 M.A.
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
		readonly Hardware.CPUMonitor cpumonitor;

		readonly System.Diagnostics.Process Self = System.Diagnostics.Process.GetCurrentProcess();
		readonly Process.CpuUsage SelfCPU;

		readonly LoaderListPairSorter sorter;

		public LoaderDisplay(ModuleManager modules)
		{
			Text = "System loaders – " + ProductName;

			cpumonitor = modules.cpumonitor;
			processmanager = modules.processmanager;
			processmanager.ScanEnd += ScanEndEventHandler;

			SelfCPU = new Process.CpuUsage(Self);

			MinimumSize = new System.Drawing.Size(420, 260);

			LoaderList = new Extensions.ListViewEx()
			{
				View = View.Details,
				HeaderStyle = ColumnHeaderStyle.Clickable,
				Dock = DockStyle.Fill,
				Width = 360,
				Height = 460,
				FullRowSelect = true,
				//ListViewItemSorter = sorter,
				VirtualMode = true,
			};

			LoaderList.RetrieveVirtualItem += LoaderListRetrieveItem;
			LoaderList.CacheVirtualItems += LoaderListCacheItem;
			LoaderList.SearchForVirtualItem += LoaderListSearchItem;
			LoaderList.VirtualItemsSelectionRangeChanged += LoaderListSelectionChanged;

			LoaderList.Columns.AddRange(new[] {
				new ColumnHeader() { Text = "Process" },
				new ColumnHeader() { Text = "Count", TextAlign = HorizontalAlignment.Right },
				new ColumnHeader() { Text = "< Id", TextAlign = HorizontalAlignment.Right },
				new ColumnHeader() { Text = "Load", TextAlign = HorizontalAlignment.Right },
				new ColumnHeader() { Text = "CPU", TextAlign = HorizontalAlignment.Right },
				new ColumnHeader() { Text = "MEM", TextAlign = HorizontalAlignment.Right },
				new ColumnHeader() { Text = "IO", TextAlign = HorizontalAlignment.Right },
				new ColumnHeader() { Text = "Interest", TextAlign = HorizontalAlignment.Left },
			});

			var totalSystem = new LoadListPair(usedLoad, totalSystemLI);

			sorter = new LoaderListPairSorter()
			{
				TopItem = totalSystem,
				Column = InstanceCpuColumn,
				Order = SortOrder.Descending,
			};

			LoaderList.ColumnClick += (_, ea) =>
			{
				//var sorter = LoaderList.ListViewItemSorter as LoaderSorter;

				if (sorter.Column == ea.Column)
				{
					sorter.Order = sorter.Order == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
				}
				else
				{
					sorter.Order = SortOrder.Descending; // default
					sorter.Column = ea.Column;
				}

				LoaderListData.Sort(sorter);

				//LoaderList.Sort();
			};

			usedCpuCell = totalSystemLI.SubItems[InstanceCpuColumn];
			usedMemCell = totalSystemLI.SubItems[InstanceMemColumn];

			freeCpuCell = freeSystemLI.SubItems[InstanceCpuColumn];
			freeMemCell = freeSystemLI.SubItems[InstanceMemColumn];

			sysPrcCountCell = totalSystemLI.SubItems[InstanceCountColumn];

			totalSystemLI.BackColor = System.Drawing.SystemColors.Control;
			freeSystemLI.BackColor = System.Drawing.SystemColors.Control;
			selfLoadLI.BackColor = System.Drawing.SystemColors.Control;
			if (TrackUntracked) untrackedLoadLI.BackColor = System.Drawing.SystemColors.Control;

			selfLoadLI.SubItems[InstancePidColumn].Text = Self.Id.ToString();
			selfLoadLI.SubItems[InstanceCpuColumn].Text = "? %";
			selfLoadLI.SubItems[InstanceMemColumn].Text = "0 MiB";

			/*
			LoaderList.Items.Add(totalSystemLI);
			LoaderList.Items.Add(freeSystemLI);
			LoaderList.Items.Add(selfLoadLI);
			if (TrackUntracked) LoaderList.Items.Add(untrackedLoadLI);
			*/

			usedLoad = new Process.InstanceGroupLoad("[Used]", Process.LoadType.Volatile);
			freeLoad = new Process.InstanceGroupLoad("[Free]", Process.LoadType.Volatile);
			selfLoad = new Process.InstanceGroupLoad("[Self]", Process.LoadType.Volatile);
			if (TrackUntracked) untrackedLoad = new Process.InstanceGroupLoad("[Untracked]", Process.LoadType.Volatile);

			LoaderListData.Add(totalSystem);
			LoaderListData.Add(new LoadListPair(freeLoad, freeSystemLI));
			LoaderListData.Add(new LoadListPair(selfLoad, selfLoadLI));
			if (TrackUntracked) LoaderListData.Add(new LoadListPair(untrackedLoad, untrackedLoadLI));

			LoaderList.VirtualListSize = LoaderListData.Count;

			/*
			ReverseMap.TryAdd(totalSystemLI, usedLoad);
			ReverseMap.TryAdd(freeSystemLI, freeLoad);
			ReverseMap.TryAdd(selfLoadLI, selfLoad);
			if (TrackUntracked) ReverseMap.TryAdd(untrackedLoadLI, untrackedLoad);
			*/

			AutoSize = true;
			AutoSizeMode = AutoSizeMode.GrowOnly;

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

			//160, 50, 49, 48, 48, 69, 53, 122
			var defaultCols = new int[] { 160, 50, 50, 50, 50, 70, 50, 120 };
			//var cols = colsec.GetOrSet("Loaders", defaultCols).IntArray;
			//if (cols.Length != defaultCols.Length) cols = defaultCols;

			int[] cols;
			try
			{
				cols = colsec["Loaders"].IntArray;
				if (cols?.Length != defaultCols.Length) cols = defaultCols;

			}
			catch { cols = defaultCols; }

			LoaderList.Columns[0].Width = cols[0];
			LoaderList.Columns[1].Width = cols[1];
			LoaderList.Columns[2].Width = cols[2];
			LoaderList.Columns[3].Width = cols[3];
			LoaderList.Columns[4].Width = cols[4];
			LoaderList.Columns[5].Width = cols[5];
			LoaderList.Columns[6].Width = cols[6];
			LoaderList.Columns[7].Width = cols[7];

			LoaderList.Width = cols[0] + cols[1] + cols[2] + cols[3] + cols[4] + cols[5] + cols[6] + cols[7];

			Controls.Add(LoaderList);

			status.Items.AddRange(new[] {
				new ToolStripStatusLabel("Tracking: "), trackingLabel,
				new ToolStripStatusLabel("Active: "), activeLabel,
				new ToolStripStatusLabel("Ignored: "), ignoredLabel
			});

			Controls.Add(status);

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

		int LoaderListFirst = 0;

		readonly List<LoadListPair> LoaderListData = new List<LoadListPair>(80);

		void LoaderListSelectionChanged(object sender, ListViewVirtualItemsSelectionRangeChangedEventArgs e)
		{
			// NOP
		}

		void LoaderListSearchItem(object sender, SearchForVirtualItemEventArgs e)
		{
			// NOP
		}

		void LoaderListCacheItem(object sender, CacheVirtualItemsEventArgs e)
		{
			// Confirm necessity of update
			if (e.StartIndex >= LoaderListFirst && e.EndIndex <= LoaderListFirst + LoaderListData.Count)
				return; // Subset of old cache

			// Build cache
			LoaderListFirst = e.StartIndex;
			int newVisibleLength = e.EndIndex - e.StartIndex + 1; // inclusive range

			//Fill the cache with the appropriate ListViewItems.
			for (int i = 0; i < newVisibleLength; i++)
			{
				var item = LoaderListData[i];

				//if (!LoaderListCache.Get(item.Load, out _))
				//	LogListGenerateItem(item);
			}

			//LogList.VirtualListSize = LogListData.Count;
		}

		void LoaderListRetrieveItem(object sender, RetrieveVirtualItemEventArgs e)
		{
			var item = LoaderListData[e.ItemIndex - LoaderListFirst];

			e.Item = item.ListItem;

			//e.Item = LoaderListCache.Get(item.Load, out var li) ? li : LogListGenerateItem(item);
		}

		readonly StatusStrip status = new StatusStrip() { Dock = DockStyle.Bottom };
		readonly ToolStripStatusLabel
			trackingLabel = new ToolStripStatusLabel("n/a"),
			activeLabel = new ToolStripStatusLabel("n/a"),
			ignoredLabel = new ToolStripStatusLabel("n/a");

		private void ScanEndEventHandler(object sender, Process.ScanEndEventArgs e)
		{
			if (!IsHandleCreated || IsDisposed) return;

			BeginInvoke(new Action(() =>
			{
				sysPrcCountCell.Text = e.Found.ToString();
			}));
		}

		readonly ListViewItem
			freeSystemLI = new ListViewItem(new[] { "[Free]", "~", "~", "~", "~", "~", "~", "~" }),
			totalSystemLI = new ListViewItem(new[] { "[Total]", "~", "~", "~", "~", "~", "~", "~" }),
			selfLoadLI = new ListViewItem(new[] { "[Self]", "~", "~", "~", "~", "~", "~", "~" }),
			untrackedLoadLI = new ListViewItem(new[] { "[Untracked]", "~", "~", "~", "~", "~", "~", "~" });

		readonly ListViewItem.ListViewSubItem usedCpuCell, usedMemCell, freeCpuCell, freeMemCell, sysPrcCountCell;

		readonly Process.InstanceGroupLoad freeLoad, usedLoad, selfLoad, untrackedLoad;

		void UIUpdate(object _sender, EventArgs _2)
		{
			if (!IsHandleCreated || disposed) return;

			if (Trace) Logging.DebugMsg("<LoaderDisplay> Updating");

			var now = DateTimeOffset.UtcNow;

			var removeList = new List<LoadListPair>(5);

			LoaderList.BeginUpdate();

			//float untrackedCpu = 0.0f, untrackedMem = 0.0f;

			float totalCpu = 0.0f;
			float totalMem = 0.0f;

			int nTotalInstances = 0, nActiveInstances = 0, nIgnoredInstances = 0, nActiveGroups = 0, nIgnoredGroups = 0, nTotalGroups = 0;

			try
			{
				foreach (var pair in LoaderInfos.Values)
				{
					var loader = pair.Load;
					var timeSinceLastUpdate = now.Since(loader.Last).TotalSeconds;

					if (Trace) Logging.DebugMsg("LOADER UI: " + loader.Instance + " --- active last: " + $"{timeSinceLastUpdate:0.#} seconds ago");

					if (timeSinceLastUpdate > 120d)
					{
						removeList.Add(pair);
						continue;
					}

					if (loader.InstanceCount == 0 && loader.Disinterest > 3)
						continue;

					int total = loader.InstanceCount, ignored = loader.UninterestingInstances;
					nTotalInstances += total;
					nIgnoredInstances += ignored;
					nActiveInstances += total - ignored;

					nTotalGroups++;

					bool oldInterest = loader.Interesting;
					if (loader.Interesting || loader.Disinterest > loader.MaxDisinterest || loader.Disinterest % 10 == 0)
					{
						if (loader.Disinterest < 5) loader.Interest++;
						loader.Disinterest = 0;

						loader.Update();

						totalCpu += loader.CPULoad.Current;
						totalMem += loader.RAMLoad.Current;

						UpdateListView(pair);

						if (!oldInterest) loader.MaxDisinterest = (loader.MaxDisinterest + 1).Max(120);
						nActiveGroups++;
					}
					else
					{
						loader.Disinterest++;
						nIgnoredGroups++;

						// pretend old values are still valid
						totalCpu += loader.CPULoad.Current;
						totalMem += loader.RAMLoad.Current;

						if (loader.Disinterest > 7 && loader.Disinterest > loader.Interest)
						{
							pair.Displayed = false;
							//LoaderList.Items.Remove(pair.ListItem);
							removeList.Add(pair);
						}
					}
				}

				foreach (var loader in removeList)
				{
					loader.Active = false;

					Logging.DebugMsg("<Loader> Removing inactive item: " + loader.Load.Instance);

					RemoveLoader(loader);
				}

				LoaderList.VirtualListSize = LoaderListData.Count;

				LoaderListData.Sort(sorter);
				//LoaderList.Sort();

				ignoredInstances = nIgnoredInstances;
				activeInstances = nActiveInstances;
				totalInstances = nTotalInstances;

				activeGroups = nActiveGroups;
				ignoredGroups = nIgnoredGroups;

				totalGroups = nTotalGroups;

				UpdateSystemStats(totalCpu, totalMem);

				LoaderList.EndUpdate();
			}
			catch (InvalidOperationException) { /* window closed */ }
			catch (Exception ex) { Logging.Stacktrace(ex); }
		}

		int ignoredInstances = 0, ignoredGroups = 0, activeInstances = 0, activeGroups = 0, totalInstances = 0, totalGroups = 0;

		readonly System.Windows.Forms.Timer timer = new Timer();

		readonly ConcurrentDictionary<string, LoadListPair> LoaderInfos = new ConcurrentDictionary<string, LoadListPair>(1, 5);

		public void LoaderEndEvent(object sender, Process.LoaderEndEvent ea)
		{
			if (!IsHandleCreated || IsDisposed) return;

			BeginInvoke(new Action(() => RemoveLoader(ea.Loader)));
		}

		void RemoveLoader(LoadListPair pair)
		{
			pair.Active = pair.Displayed = false;
			pair.ListItem = null;

			LoaderListData.Remove(pair);
			LoaderInfos.TryRemove(pair.Load.Instance, out _);
			//LoaderListData.Add(pair);
			//ReverseMap.TryRemove(li, out _);

			//var li = loader.ListItem;
			//LoaderList.Items.Remove(li);
			LoaderListData.Remove(pair);

			//ReverseMap.TryRemove(li, out _);

			LoaderList.VirtualListSize--;
		}

		void RemoveLoader(Process.InstanceGroupLoad group)
		{
			if (disposed || !IsHandleCreated) return;

			if (LoaderInfos.TryGetValue(group.Instance, out var pair))
				RemoveLoader(pair);
		}

		public async void LoaderActivityEvent(object sender, Process.LoaderEvent ea)
		{
			await Task.Delay(5).ConfigureAwait(false);

			if (!IsHandleCreated || IsDisposed) return;

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

			try
			{
				LoaderList.BeginUpdate();

				for (int i = 0; i < pairs.Length; i++)
				{
					var pair = pairs[i];

					if (!pair.Active) continue;

					if (pair.ListItem != null) // update
					{
						//var li = pair.ListItem;

						UpdateListView(pair);

						/*
						LoaderListData.Remove(pair);
						//LoaderList.Items.Remove(li);

						if (LoaderList.Items.Count > pair.Load.Order)
						{
							//LoaderList.Items.Insert(pair.Load.Order, li);
							LoaderListData.Insert(pair.Load.Order, pair);
						}
						else
						{
							//LoaderList.Items.Add(li);
							LoaderListData.Add(pair);
						}
						*/
					}
					else
					{
						var load = pair.Load;
						var instance = load.Instance;

						var li = pair.ListItem = new ListViewItem(new[] {
							instance,
							load.InstanceCount.ToString(),
							load.LastPid.ToString(),
							load.Load.ToString("N1"),
							$"{load.CPULoad.Average:0.#} %",
							HumanInterface.ByteString(Convert.ToInt64(load.RAMLoad.Current * MKAh.Units.Binary.Giga), iec:true),
							$"{load.IOLoad.Average:N0}/s",
							"Active"
						});

						pair.Displayed = true;
						//LoaderList.Items.Add(li);
						LoaderListData.Add(pair);
						LoaderList.VirtualListSize++;
						LoaderInfos.TryAdd(instance, pair);

						//ReverseMap.TryAdd(li, pair.Load);
					}
				}

				UpdateSystemStats(float.NaN, float.NaN);

				LoaderList.VirtualListSize = LoaderListData.Count;

				LoaderList.EndUpdate();
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		private void UpdateSystemStats(float totalCpu, float totalMem)
		{
			var idle = cpumonitor.LastIdle * 100f;
			//var totalmem = Memory.Total;
			var memfree = Memory.Free;
			var memused = Memory.Used;

			var curCpu = (100.0f - idle);

			usedLoad.CPULoad.Update(curCpu);
			usedLoad.RAMLoad.Update(memused);

			freeLoad.CPULoad.Update(idle);
			freeLoad.RAMLoad.Update(memfree);

			var selfcpu = 100f * SelfCPU.Sample();
			selfLoad.CPULoad.Update(Convert.ToSingle(selfcpu));

			var selfram = GC.GetTotalMemory(false);
			selfLoad.RAMLoad.Update(selfram);

			usedCpuCell.Text = $"{curCpu:0.#} %";
			freeCpuCell.Text = $"{idle:0.#} %";

			freeMemCell.Text = HumanInterface.ByteString(memfree, iec: true);
			usedMemCell.Text = HumanInterface.ByteString(memused, iec: true);

			//sysPrcCountCell.Text = "?";

			selfLoadLI.SubItems[InstanceCpuColumn].Text = $"{selfcpu:0.#} %";
			selfLoadLI.SubItems[InstanceMemColumn].Text = HumanInterface.ByteString(selfram, iec: true);

			if (TrackUntracked)
			{
				if (!float.IsNaN(totalCpu))
				{
					var untracked = untrackedLoadLI.SubItems;
					untracked[InstanceCpuColumn].Text = $"{curCpu - totalCpu:N0} %";
					untracked[InstanceMemColumn].Text = HumanInterface.ByteString(Convert.ToInt64(memused - totalMem), iec: true);
				}
			}

			trackingLabel.Text = $"{totalInstances} [{totalGroups}]";
			activeLabel.Text = $"{activeInstances} [{activeGroups}]";
			ignoredLabel.Text = $"{ignoredInstances} [{ignoredGroups}]";
		}

		const bool TrackUntracked = false;

		void UpdateListView(LoadListPair pair)
		{
			if (!IsHandleCreated || disposed) return;

			var li = pair.ListItem;

			var load = pair.Load;

			//value.ListItem.SubItems[0] = key
			var subItems = li.SubItems;
			subItems[InstancePidColumn].Text = load.LastPid.ToString();
			subItems[InstanceLoadColumn].Text = load.Load.ToString("N1");
			subItems[InstanceCountColumn].Text = load.InstanceCount.ToString();
			subItems[InstanceCpuColumn].Text = $"{load.CPULoad.Average:0.#} %";
			subItems[InstanceMemColumn].Text = HumanInterface.ByteString(Convert.ToInt64(load.RAMLoad.Current), iec: true);
			subItems[InstanceIOColumn].Text = $"{load.IOLoad.Average:N0}/s";

			bool slowed = load.UninterestingInstances > 0;

			var oldInterest = load.InterestType;
			load.InterestType = load.Interesting
				? (slowed ? Process.InstanceGroupLoad.InterestLevel.Partial : Process.InstanceGroupLoad.InterestLevel.Active)
				: Process.InstanceGroupLoad.InterestLevel.Ignoring;

			if (oldInterest != load.InterestType) // update only if something has changed
			{
				string interestText;
				switch (load.InterestType)
				{
					default:
						//case Process.InstanceGroupLoad.InterestLevel.Active:
						interestText = "Active";
						break;
					case Process.InstanceGroupLoad.InterestLevel.Partial:
						interestText = "Partial";
						break;
					case Process.InstanceGroupLoad.InterestLevel.Ignoring:
						interestText = "Ignoring";
						break;
				}
				subItems[InstanceStateColumn].Text = interestText;
				li.ForeColor = load.Interesting ? System.Drawing.SystemColors.ControlText : System.Drawing.SystemColors.GrayText;
			}

			if (load.Interesting && !pair.Displayed)
			{
				LoaderListData.Add(pair);
				LoaderList.VirtualListSize++;
				//LoaderList.Items.Add(pair.ListItem);
				pair.Displayed = true;
			}
		}

		internal const int InstanceNameColumn = 0;
		internal const int InstanceCountColumn = 1;
		internal const int InstancePidColumn = 2;
		internal const int InstanceLoadColumn = 3;
		internal const int InstanceCpuColumn = 4;
		internal const int InstanceMemColumn = 5;
		internal const int InstanceIOColumn = 6;
		internal const int InstanceStateColumn = 7;

		readonly Extensions.ListViewEx LoaderList;

		#region IDispose
		public event EventHandler<DisposedEventArgs>? OnDisposed;

		bool disposed = false;

		protected override void Dispose(bool disposing)
		{
			if (disposed) return;
			disposed = true;

			if (disposing)
			{
				timer.Dispose();

				processmanager.ScanEnd -= ScanEndEventHandler;

				//processmanager.LoaderTracking = false;
				processmanager.LoaderActivity -= LoaderActivityEvent;
				processmanager.LoaderRemoval -= LoaderEndEvent;

				// 
				using var cfg = Application.Config.Load(UIConfigFilename);

				var saveBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;

				cfg.Config[Constants.Windows]["Loaders"].IntArray = new int[] { saveBounds.Left, saveBounds.Top, saveBounds.Width, saveBounds.Height };

				cfg.Config[Constants.Columns]["Loaders"].IntArray
					= new[] { LoaderList.Columns[0].Width, LoaderList.Columns[1].Width, LoaderList.Columns[2].Width, LoaderList.Columns[3].Width, LoaderList.Columns[4].Width, LoaderList.Columns[5].Width, LoaderList.Columns[6].Width, LoaderList.Columns[7].Width };

				OnDisposed?.Invoke(this, DisposedEventArgs.Empty);
				OnDisposed = null;
			}

			base.Dispose(disposing);
		}
		#endregion Dispose
	}

	public class LoadListPair
	{
		public bool Displayed { get; set; } = false;

		public Process.InstanceGroupLoad Load { get; set; }

		public ListViewItem? ListItem { get; set; }

		public bool Active { get; set; } = true;

		public LoadListPair(Process.InstanceGroupLoad info, ListViewItem item)
		{
			Load = info;
			ListItem = item;
		}
	}

	public class LoaderSorter : IComparer
	{
		readonly ConcurrentDictionary<ListViewItem, Process.InstanceGroupLoad> revMap;

		public LoaderSorter(ConcurrentDictionary<ListViewItem, Process.InstanceGroupLoad> map) => revMap = map;

		public int Column { get; set; } = 2;

		/// <summary>
		/// Always kept at top.
		/// </summary>
		public ListViewItem? TopItem { get; set; } = null;

		public SortOrder Order { get; set; } = SortOrder.Descending;

		readonly CaseInsensitiveComparer Comparer = new CaseInsensitiveComparer();

		public int Compare(object x, object y)
		{
			var lix = (ListViewItem)x;
			var liy = (ListViewItem)y;

			if (lix == TopItem) return -1;
			else if (liy == TopItem) return 1;

			int result;

			if (Column == LoaderDisplay.InstanceStateColumn || Column == LoaderDisplay.InstancePidColumn)
			{
				result = 0;
			}
			else if (Column != 0)
			{
				revMap.TryGetValue(lix, out var xgroup);
				revMap.TryGetValue(liy, out var ygroup);

				if (xgroup == ygroup) return 0; // null == null or same instance
				else if (xgroup is null) return -1;
				else if (ygroup is null) return 1;

				switch (Column)
				{
					case LoaderDisplay.InstanceNameColumn:
						result = xgroup.Instance.CompareTo(ygroup.Instance);
						break;
					case LoaderDisplay.InstanceCpuColumn: // CPU
						result = xgroup.CPULoad.Average.CompareTo(ygroup.CPULoad.Average);
						break;
					case LoaderDisplay.InstanceMemColumn: // MEM
						result = xgroup.RAMLoad.Current.CompareTo(ygroup.RAMLoad.Current);
						break;
					case LoaderDisplay.InstanceIOColumn: // IO
						result = xgroup.IOLoad.Average.CompareTo(ygroup.IOLoad.Average);
						break;
					case LoaderDisplay.InstanceCountColumn: // Instance count
						result = xgroup.InstanceCount.CompareTo(ygroup.InstanceCount);
						break;
					case LoaderDisplay.InstanceLoadColumn:
						result = xgroup.Load.CompareTo(ygroup.Load);
						break;
					default:
						result = 0;
						break;
				}

				/*
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
				*/
			}
			else
				result = Comparer.Compare(lix.SubItems[Column].Text, liy.SubItems[Column].Text);

			return Order == SortOrder.Ascending ? result : -result;
		}
	}

	public class LoaderListPairSorter : IComparer<LoadListPair>
	{
		public int Column { get; set; } = 2;

		/// <summary>
		/// Always kept at top.
		/// </summary>
		public LoadListPair? TopItem { get; set; } = null;

		public SortOrder Order { get; set; } = SortOrder.Descending;

		readonly CaseInsensitiveComparer Comparer = new CaseInsensitiveComparer();

		public int Compare(LoadListPair xgroup, LoadListPair ygroup)
		{
			if (xgroup == TopItem) return -1;
			else if (ygroup == TopItem) return 1;

			if (xgroup == ygroup) return 0;  // null == null or same instance
			else if (xgroup is null) return -1;
			else if (ygroup is null) return 1;

			int result;

			var xload = xgroup.Load;
			var yload = ygroup.Load;

			if (Column == LoaderDisplay.InstanceStateColumn || Column == LoaderDisplay.InstancePidColumn)
			{
				result = 0; //  don't sort these
			}
			else if (Column != 0)
			{
				if (Column != LoaderDisplay.InstanceNameColumn)
				{
					// quick sorting for ignored rules when not sorting by name
					if (xload.InterestType == Process.InstanceGroupLoad.InterestLevel.Ignoring && yload.InterestType == Process.InstanceGroupLoad.InterestLevel.Ignoring)
						return 0; // both ignored
					else if (xload.InterestType == Process.InstanceGroupLoad.InterestLevel.Ignoring)
						return 1; // y not ignored
					else if (yload.InterestType == Process.InstanceGroupLoad.InterestLevel.Ignoring)
						return -1; // x not ignored
				}

				switch (Column)
				{
					case LoaderDisplay.InstanceNameColumn:
						result = xload.Instance.CompareTo(yload.Instance);
						break;
					case LoaderDisplay.InstanceCpuColumn: // CPU
						result = xload.CPULoad.Average.CompareTo(yload.CPULoad.Average);
						break;
					case LoaderDisplay.InstanceMemColumn: // MEM
						result = xload.RAMLoad.Current.CompareTo(yload.RAMLoad.Current);
						break;
					case LoaderDisplay.InstanceIOColumn: // IO
						result = xload.IOLoad.Average.CompareTo(yload.IOLoad.Average);
						break;
					case LoaderDisplay.InstanceCountColumn: // Instance count
						result = xload.InstanceCount.CompareTo(yload.InstanceCount);
						break;
					case LoaderDisplay.InstanceLoadColumn:
						result = xload.Load.CompareTo(yload.Load);
						break;
					default:
						result = 0;
						break;
				}
			}
			else
				result = Comparer.Compare(xload.Instance, yload.Instance);

			return Order == SortOrder.Ascending ? result : -result;
		}
	}
}
