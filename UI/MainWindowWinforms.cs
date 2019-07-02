//
// MainWindowWinforms.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2016–2019 M.A.
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MKAh;
using Serilog;

namespace Taskmaster.UI
{
	using System.Text;
	using static Taskmaster;

	// public class MainWindow : System.Windows.Window; // TODO: WPF
	sealed public class MainWindow : UniForm
	{
		readonly ToolTip tooltip = new ToolTip();

		System.Drawing.Color WarningColor = System.Drawing.Color.Red;
		System.Drawing.Color AlterColor = System.Drawing.Color.FromArgb(245, 245, 245); // ignores user styles

		System.Drawing.Color DefaultLIBGColor = new ListViewItem().BackColor; // HACK

		bool AlternateRowColorsLog { get; set; } = true;
		bool AlternateRowColorsWatchlist { get; set; } = true;
		bool AlternateRowColorsDevices { get; set; } = true;

		bool AutoOpenMenus { get; set; } = true;

		// constructor
		public MainWindow()
			: base()
		{
			_ = Handle; // HACK

			Visible = false;
			SuspendLayout();

			// InitializeComponent(); // TODO: WPF
			FormClosing += WindowClose;

			ShowInTaskbar = true;

			#region Load Configuration
			// DEPRECATED
			using var corecfg = Taskmaster.Config.Load(CoreConfigFilename);
			if (corecfg.Config.TryGet(HumanReadable.Generic.QualityOfLife, out var qolsec))
			{
				if (qolsec.TryGet("Show in taskbar", out var sitb))
				{
					ShowInTaskbar = sitb.Bool;
					qolsec.Remove(sitb);
				}
				if (qolsec.TryGet("Auto-open menus", out var aomn))
				{
					AutoOpenMenus = aomn.Bool;
					qolsec.Remove(aomn);
				}
			}

			using var uicfg = Taskmaster.Config.Load(UIConfigFilename);
			var qol = uicfg.Config[HumanReadable.Generic.QualityOfLife];
			ShowInTaskbar = qol.GetOrSet("Show in taskbar", ShowInTaskbar).Bool;
			AutoOpenMenus = qol.GetOrSet("Auto-open menus", true).Bool;
			#endregion // Load Configuration

			#region Build UI
			BuildUI();
			#endregion

			tooltip.IsBalloon = true;
			tooltip.InitialDelay = 2000;
			tooltip.ShowAlways = true;

			WatchlistSearchTimer.Interval = 250;
			WatchlistSearchTimer.Tick += WatchlistSearchTimer_Tick;

			// TODO: Detect mic device changes
			// TODO: Delay fixing by 5 seconds to prevent fix diarrhea

			// the form itself
			WindowState = FormWindowState.Normal;

			FormBorderStyle = FormBorderStyle.Sizable;
			SizeGripStyle = SizeGripStyle.Auto;

			AutoSizeMode = AutoSizeMode.GrowOnly;
			AutoSize = false;

			MaximizeBox = true;
			MinimizeBox = true;

			MinimumHeight += tabLayout.MinimumSize.Height
				+ LogList.MinimumSize.Height
				+ menu.Height
				+ statusbar.Height
				+ 40; // why is this required? window deco?

			MinimumSize = new System.Drawing.Size(780, MinimumHeight);

			// FormBorderStyle = FormBorderStyle.FixedDialog; // no min/max buttons as wanted

			if (!ShowOnStart)
			{
				Logging.DebugMsg("<Main Window> Show on start disabled, hiding.");
				Hide();
			}

			// CenterToScreen();

			Shown += onShown;

			// TODO: WPF
			/*
			System.Windows.Shell.JumpList jumplist = System.Windows.Shell.JumpList.GetJumpList(System.Windows.Application.Current);
			//System.Windows.Shell.JumpTask task = new System.Windows.Shell.JumpTask();
			System.Windows.Shell.JumpPath jpath = new System.Windows.Shell.JumpPath();
			jpath.Path = cfgpath;
			jumplist.JumpItems.Add(jpath);
			jumplist.Apply();
			*/

			FillLog();

			if (Trace) Log.Verbose("MainWindow constructed");

			ResumeLayout();
			Visible = true;
		}

		void onShown(object _, EventArgs _ea)
		{
			Logging.DebugMsg("<Main Window> Showing");

			if (!IsHandleCreated) return;

			int count = LogList.Items.Count;
			if (count > 0) // needed in case of bugs or clearlog
			{
				LogList.TopItem = LogList.Items[count - 1];
				ShowLastLog();
			}
		}

		public void ExitRequest(object _, EventArgs _ea) => ConfirmExit(restart: false);

		void WindowClose(object _, FormClosingEventArgs ea)
		{
			try
			{
				SaveUIState();

				if (!Trace) return;

				Logging.DebugMsg("WindowClose = " + ea.CloseReason.ToString());
				switch (ea.CloseReason)
				{
					case CloseReason.UserClosing:
						// X was pressed or similar
						break;
					case CloseReason.WindowsShutDown:
						Log.Debug("Exit: Windows shutting down.");
						break;
					case CloseReason.TaskManagerClosing:
						Log.Debug("Exit: Task manager told us to close.");
						break;
					case CloseReason.ApplicationExitCall:
						Log.Debug("Exit: User asked to close.");
						break;
					default:
						Log.Debug("Exit: Unidentified close reason: " + ea.CloseReason.ToString());
						break;
				}
				Logging.DebugMsg("WindowClose.Handled");
			}
			catch (Exception ex) { Logging.Stacktrace(ex); }
		}

		// this restores the main window to a place where it can be easily found if it's lost
		/// <summary>
		/// Restores the main window to the center of the screen.
		/// </summary>
		public void UnloseWindowRequest(object _, EventArgs e)
		{
			if (Trace) Log.Verbose("Making sure main window is not lost.");

			if (!IsHandleCreated || DisposedOrDisposing) return;

			Reveal(activate: true);
			CenterToScreen();
		}

		public void Reveal(bool activate = false)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			if (InvokeRequired)
				BeginInvoke(new Action(() => Reveal_Invoke(activate)));
			else
				Reveal_Invoke(activate);
		}

		void Reveal_Invoke(bool activate)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			try
			{
				WindowState = FormWindowState.Normal;
				// shuffle to top in the most hackish way possible, these are all unreliable
				// does nothing without show(), unreliable even with it
				if (activate)
				{
					//TopMost = true;
					//TopMost = false;
					Activate();
				}
				Show();
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		public void ShowLastLog()
		{
			int count = LogList.Items.Count;
			if (count > 0) LogList.EnsureVisible(count - 1);
		}

		// HOOKS
		Audio.MicManager micmanager = null;
		StorageManager storagemanager = null;
		Process.Manager processmanager = null;
		Process.ForegroundManager activeappmonitor = null;
		Power.Manager powermanager = null;
		CPUMonitor cpumonitor = null;
		Network.Manager netmonitor = null;

		#region Microphone control code
		Audio.Device DefaultAudioInput = null;

		void SetDefaultCommDevice()
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			if (InvokeRequired)
				BeginInvoke(new Action(SetDefaultCommDevice_Invoke));
			else
				SetDefaultCommDevice_Invoke();
		}

		void SetDefaultCommDevice_Invoke()
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			try
			{
				// TODO: less direct access to mic manager

				var devname = micmanager.Device.Name;

				AudioInputDevice.Text = !string.IsNullOrEmpty(devname) ? devname : HumanReadable.Generic.NotAvailable;

				corCountLabel.Text = micmanager.Corrections.ToString();

				AudioInputVolume.Maximum = Convert.ToDecimal(Audio.MicManager.Maximum);
				AudioInputVolume.Minimum = Convert.ToDecimal(Audio.MicManager.Minimum);
				AudioInputVolume.Value = Convert.ToInt32(micmanager.Volume);

				AudioInputEnable.SelectedIndex = micmanager.Control ? 0 : 1;
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		void AddAudioInput(Audio.Device device)
		{
			if (micmanager is null) return;

			try
			{
				var li = new ListViewItem(new string[] {
					device.Name,
					device.GUID.ToString(),
					$"{device.Volume * 100d:N1} %",
					$"{device.Target:N1} %",
					(device.VolumeControl ? HumanReadable.Generic.Enabled : HumanReadable.Generic.Disabled),
					device.State.ToString(),
				});

				AudioInputs.Items.Add(li);
				MicGuidToAudioInputs.TryAdd(device.GUID, li);
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		void AlternateListviewRowColors(ListView lv, bool alternate = false)
		{
			bool alter = true;
			foreach (ListViewItem li in lv.Items)
				li.BackColor = (alternate && (alter = !alter)) ? AlterColor : DefaultLIBGColor;
		}

		void RemoveAudioInput(Guid guid)
		{
			if (micmanager is null) return;

			if (MicGuidToAudioInputs.TryRemove(guid, out var li))
				AudioInputs.Items.Remove(li);
		}

		void UpdateAudioInputs()
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			if (micmanager is null) return;

			// TODO: mark default device in list
			AudioInputs.Items.Clear();

			foreach (var dev in micmanager.Devices)
				AddAudioInput(dev);

			AlternateListviewRowColors(AudioInputs, AlternateRowColorsDevices);
		}

		Audio.Manager audiomanager = null;

		public void Hook(Audio.Manager manager)
		{
			Debug.Assert(manager != null);

			try
			{
				audiomanager = manager;
				audiomanager.StateChanged += AudioDeviceStateChanged;
				audiomanager.Removed += AudioDeviceRemoved;
				audiomanager.Added += AudioDeviceAdded;
				audiomanager.OnDisposed += (_, _ea) => audiomanager = null;
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				throw;
			}
		}

		public void Hook(Audio.MicManager manager)
		{
			Debug.Assert(manager != null);

			try
			{
				micmanager = manager;
				micmanager.OnDisposed += (_, _ea) => micmanager = null;

				if (Trace) Log.Verbose("Hooking microphone monitor.");

				BeginInvoke(new Action(() =>
				{
					if (IsDisposed || !IsHandleCreated) return;

					try
					{
						SetDefaultCommDevice();
						UpdateAudioInputs();
					}
					catch (OutOfMemoryException) { throw; }
					catch (Exception ex)
					{
						Logging.Stacktrace(ex);
					}
				}));

				// TODO: Hook all device changes
				micmanager.VolumeChanged += VolumeChangeDetected;
				micmanager.DefaultChanged += MicrophoneDefaultChanged;

				FormClosing += (_, _ea) => micmanager.VolumeChanged -= VolumeChangeDetected;
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				throw;
			}
		}

		void AudioDeviceAdded(object sender, Audio.DeviceEventArgs ea)
		{
			if (IsDisposed || !IsHandleCreated) return;

			switch (ea.Device.Flow)
			{
				case NAudio.CoreAudioApi.DataFlow.Capture:
					if (micmanager is null) return;
					AddAudioInput(ea.Device);
					AlternateListviewRowColors(AudioInputs, AlternateRowColorsDevices);
					break;
				case NAudio.CoreAudioApi.DataFlow.Render:
					break;
			}
		}

		void AudioDeviceRemoved(object sender, Audio.DeviceEventArgs ea)
		{
			if (IsDisposed || !IsHandleCreated) return;

			if (micmanager is null) return;

			RemoveAudioInput(ea.GUID);
			AlternateListviewRowColors(AudioInputs, AlternateRowColorsDevices);
		}

		void MicrophoneDefaultChanged(object sender, Audio.DefaultDeviceEventArgs ea)
		{
			if (IsDisposed || !IsHandleCreated) return;

			DefaultAudioInput = ea.Device;

			if (InvokeRequired)
				BeginInvoke(new Action(MicrophoneDefaultChanged_Update));
			else
				MicrophoneDefaultChanged_Update();
		}

		void MicrophoneDefaultChanged_Update()
		{
			if (IsDisposed || !IsHandleCreated) return;

			try
			{
				if (DefaultAudioInput is null)
				{
					AudioInputEnable.Text = HumanReadable.Generic.Uninitialized;
					AudioInputDevice.Text = HumanReadable.Generic.Uninitialized;
				}
				else
				{
					//AudioInputEnable.SelectedIndex = micmon.Control ? 0 : 1;
					//AudioInputEnable.Text = HumanReadable.Generic.Ellipsis;
					//AudioInputDevice.Text = HumanReadable.Generic.Ellipsis;
					SetDefaultCommDevice();
				}

				UpdateAudioInputs();
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		readonly ConcurrentDictionary<Guid, ListViewItem> MicGuidToAudioInputs = new ConcurrentDictionary<Guid, ListViewItem>();

		void AudioDeviceStateChanged(object sender, Audio.DeviceStateEventArgs ea)
		{
			if (IsDisposed || !IsHandleCreated) return;

			if (InvokeRequired)
				BeginInvoke(new Action(() => AudioDeviceStateChanged_Update(ea)));
			else
				AudioDeviceStateChanged_Update(ea);
		}

		void AudioDeviceStateChanged_Update(Audio.DeviceStateEventArgs ea)
		{
			if (IsDisposed || !IsHandleCreated) return;

			if (MicGuidToAudioInputs.TryGetValue(ea.GUID, out ListViewItem li))
				li.SubItems[5].Text = ea.State.ToString();
		}

		void UserMicVol(object _, EventArgs _ea)
		{
			// TODO: Handle volume changes. Not really needed. Give presets?
			// micMonitor.setVolume(micVol.Value);
		}

		void VolumeChangeDetected(object _, VolumeChangedEventArgs ea)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			if (InvokeRequired)
				BeginInvoke(new Action(() => VolumeChangeDetected_Update(ea)));
			else
				VolumeChangeDetected_Update(ea);
		}

		void VolumeChangeDetected_Update(VolumeChangedEventArgs ea)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			AudioInputVolume.Value = Convert.ToInt32(ea.New); // this could throw ArgumentOutOfRangeException, but we trust the source
			corCountLabel.Text = ea.Corrections.ToString();
		}
		#endregion // Microphone control code

		public void ProcessTouchEvent(object _, ProcessModificationEventArgs ea)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			if (InvokeRequired)
				BeginInvoke(new Action(() => ProcessTouchEvent_Update(ea)));
			else
				ProcessTouchEvent_Update(ea);
		}

		void ProcessTouchEvent_Update(ProcessModificationEventArgs ea)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			//adjustcounter.Text = Statistics.TouchCount.ToString();

			var prc = ea.Info.Controller; // cache

			try
			{
				if (WatchlistMap.TryGetValue(prc, out ListViewItem item))
				{
					item.SubItems[AdjustColumn].Text = prc.Adjusts.ToString();
					// item.SubItems[SeenColumn].Text = prc.LastSeen.ToLocalTime().ToString();
				}
				else
					Log.Error(prc.FriendlyName + " not found in UI watchlist list.");
			}
			catch (Exception ex) { Logging.Stacktrace(ex); }

			if (LastModifiedList)
			{
				try
				{
					var mi = new ListViewItem(new string[] {
							DateTime.Now.ToLongTimeString(),
							ea.Info.Name,
							prc.FriendlyName,
							(ea.PriorityNew.HasValue ? MKAh.Readable.ProcessPriority(ea.PriorityNew.Value) : HumanReadable.Generic.NotAvailable),
							(ea.AffinityNew >= 0 ? HumanInterface.BitMask(ea.AffinityNew, Process.Utility.CPUCount) : HumanReadable.Generic.NotAvailable),
							ea.Info.Path
						});
					lastmodifylist.Items.Add(mi);
					if (lastmodifylist.Items.Count > 5) lastmodifylist.Items.RemoveAt(0);
				}
				catch (Exception ex) { Logging.Stacktrace(ex); }
			}
		}

		public void OnActiveWindowChanged(object _, Process.WindowChangedArgs windowchangeev)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;
			if (windowchangeev.Process is null) return;

			if (InvokeRequired)
				BeginInvoke(new Action(() => OnActiveWindowChanged_Update(windowchangeev)));
			else
				OnActiveWindowChanged_Update(windowchangeev);
		}

		void OnActiveWindowChanged_Update(Process.WindowChangedArgs windowchangeev)
		{
			// int maxlength = 70;
			// string cutstring = e.Title.Substring(0, Math.Min(maxlength, e.Title.Length)) + (e.Title.Length > maxlength ? "..." : "");
			// activeLabel.Text = cutstring;
			activeLabel.Text = windowchangeev.Title;
			activeExec.Text = windowchangeev.Executable;
			activeFullscreen.Text = windowchangeev.Fullscreen ? "Full" : "Window";
			activePID.Text = windowchangeev.Id.ToString();
		}

		public event EventHandler rescanRequest;

		public void Hook(StorageManager manager)
		{
			storagemanager = manager;
			storagemanager.TempScan += TempScanStats;
			storagemanager.OnDisposed += (_, _ea) => storagemanager = null;
		}

		public void Hook(Process.Manager manager)
		{
			Debug.Assert(manager != null);

			processmanager = manager;
			processmanager.OnDisposed += (_, _ea) => processmanager = null;

			processmanager.HandlingCounter += ProcessNewInstanceCount;
			processmanager.ProcessStateChange += ExitWaitListHandler;
			if (DebugCache) PathCacheUpdate(null, EventArgs.Empty);

			ProcessNewInstanceCount(this, new Process.ProcessingCountEventArgs(0, 0));

			WatchlistRules.VisibleChanged += (_, _ea) => { if (WatchlistRules.Visible) WatchlistColor(); };

			BeginInvoke(new Action(() =>
			{
				foreach (var prc in processmanager.GetWatchlist())
					AddToWatchlistList(prc);

				WatchlistColor();
			}));

			if (manager.ScanFrequency.HasValue)
			{
				UItimer.Tick += UpdateRescanCountdown;
				GotFocus += UpdateRescanCountdown;
				UpdateRescanCountdown(this, EventArgs.Empty);
			}

			processmanager.WatchlistSorted += UpdateWatchlist;

			rescanRequest += RescanRequestEvent;

			processmanager.ProcessModified += ProcessTouchEvent;

			foreach (var info in processmanager.GetExitWaitList())
				ExitWaitListHandler(this, new ProcessModificationEventArgs(info));
		}

		void UpdateWatchlist(object _, EventArgs _ea)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			if (InvokeRequired)
				BeginInvoke(new Action(UpdateWatchlist_Invoke));
			else
				UpdateWatchlist_Invoke();
		}

		void UpdateWatchlist_Invoke()
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			foreach (var li in WatchlistMap)
			{
				li.Value.SubItems[0].Text = (li.Key.ActualOrder + 1).ToString();
				WatchlistItemColor(li.Value, li.Key);
			}

			// re-sort if user is not interacting?
		}

		void RescanRequestEvent(object _, EventArgs _ea) => processmanager?.HastenScan(0);

		void RestartRequestEvent(object sender, EventArgs _ea) => ConfirmExit(restart: true, admin: sender == menu_action_restartadmin);

		void ProcessNewInstanceCount(object _, Process.ProcessingCountEventArgs e)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			if (InvokeRequired)
				BeginInvoke(new Action(() => ProcessNewInstanceCount_Invoke(e)));
			else
				ProcessNewInstanceCount_Invoke(e);
		}

		void ProcessNewInstanceCount_Invoke(Process.ProcessingCountEventArgs e)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			processingcount.Text = e.Total.ToString();
		}

		/// <summary>
		///
		/// </summary>
		/// <remarks>No locks</remarks>
		void WatchlistItemColor(ListViewItem li, Process.Controller prc)
		{
			var alter = AlternateRowColorsWatchlist && (li.Index + 1) % 2 == 0; // every even line

			try
			{
				li.UseItemStyleForSubItems = false;
				foreach (ListViewItem.ListViewSubItem si in li.SubItems)
				{
					if (prc.Enabled)
						si.ForeColor = System.Drawing.SystemColors.ControlText;
					else
						si.ForeColor = System.Drawing.SystemColors.GrayText;

					if (alter) si.BackColor = AlterColor;
					else si.BackColor = DefaultLIBGColor;
				}

				if (prc.PriorityStrategy == ProcessPriorityStrategy.None)
					li.SubItems[PrioColumn].ForeColor = System.Drawing.SystemColors.GrayText;
				if (string.IsNullOrEmpty(prc.Path))
					li.SubItems[PathColumn].ForeColor = System.Drawing.SystemColors.GrayText;
				if (prc.PowerPlan == Power.Mode.Undefined)
					li.SubItems[PowerColumn].ForeColor = System.Drawing.SystemColors.GrayText;
				if (prc.AffinityMask < 0)
					li.SubItems[AffColumn].ForeColor = System.Drawing.SystemColors.GrayText;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		int watchlistcolor_i = 0;

		void WatchlistColor()
		{
			if (Trace) Logging.DebugMsg("COLORING LINES");

			System.Threading.Interlocked.Increment(ref watchlistcolor_i);

			lock (watchlist_lock)
			{
				try
				{
					int i = 0;
					foreach (var item in WatchlistMap)
					{
						if (watchlistcolor_i > 1) return;

						if (Trace) Logging.DebugMsg($"{i++:00} --- {item.Value.Index:00} : {(item.Value.Index + 1) % 2 == 0} --- {item.Key.FriendlyName}");
						WatchlistItemColor(item.Value, item.Key);
					}
				}
				finally
				{
					System.Threading.Interlocked.Decrement(ref watchlistcolor_i);
				}
			}
		}

		void AddToWatchlistList(Process.Controller prc)
		{
			string aff = string.Empty;
			if (prc.AffinityMask > 0)
			{
				if (AffinityStyle == 0)
					aff = HumanInterface.BitMask(prc.AffinityMask, Process.Utility.CPUCount);
				else
					aff = prc.AffinityMask.ToString();
			}

			var litem = new ListViewItem(new string[] {
				(prc.ActualOrder+1).ToString(),
				prc.OrderPreference.ToString(),
				prc.FriendlyName,
				prc.Executables?.Length > 0 ? string.Join(", ", prc.Executables) : string.Empty,
				string.Empty,
				aff,
				string.Empty,
				prc.Adjusts.ToString(),
				string.Empty
			});

			WatchlistRules.Items.Add(litem);
			WatchlistMap.TryAdd(prc, litem);

			FormatWatchlist(litem, prc);
			WatchlistUpdateTooltip(litem, prc);
			WatchlistItemColor(litem, prc);
		}

		void WatchlistUpdateTooltip(ListViewItem li, Process.Controller prc)
		{
			// BUG: Doens't work for some reason. Gets set but is never shown.
			//li.ToolTipText = prc.ToDetailedString();
		}

		void FormatWatchlist(ListViewItem litem, Process.Controller prc)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			if (InvokeRequired)
				BeginInvoke(new Action(() => FormatWatchlist_Invoke(litem, prc)));
			else
				FormatWatchlist_Invoke(litem, prc);
		}

		void FormatWatchlist_Invoke(ListViewItem litem, Process.Controller prc)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			litem.SubItems[PrefColumn].Text = prc.OrderPreference.ToString();
			litem.SubItems[NameColumn].Text = prc.FriendlyName;
			litem.SubItems[ExeColumn].Text = (prc.Executables?.Length > 0) ? string.Join(", ", prc.Executables) : string.Empty;
			litem.SubItems[PrioColumn].Text = prc.Priority.HasValue ? MKAh.Readable.ProcessPriority(prc.Priority.Value) : string.Empty;
			string aff = string.Empty;
			if (prc.AffinityMask >= 0)
			{
				if (prc.AffinityMask == Process.Utility.FullCPUMask || prc.AffinityMask == 0)
					aff = "Full/OS";
				else if (AffinityStyle == 0)
					aff = HumanInterface.BitMask(prc.AffinityMask, Process.Utility.CPUCount);
				else
					aff = prc.AffinityMask.ToString();
			}
			litem.SubItems[AffColumn].Text = aff;
			litem.SubItems[PowerColumn].Text = (prc.PowerPlan != Power.Mode.Undefined ? Power.Utility.GetModeName(prc.PowerPlan) : string.Empty);
			litem.SubItems[PathColumn].Text = (string.IsNullOrEmpty(prc.Path) ? string.Empty : prc.Path);
		}

		public void UpdateWatchlistRule(Process.Controller prc)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			if (WatchlistMap.TryGetValue(prc, out ListViewItem litem))
			{
				if (InvokeRequired)
					BeginInvoke(new Action(() => UpdateWatchlistRule_Invoke(litem, prc)));
				else
					UpdateWatchlistRule_Invoke(litem, prc);
			}
		}

		void UpdateWatchlistRule_Invoke(ListViewItem litem, Process.Controller prc)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			FormatWatchlist(litem, prc);

			WatchlistUpdateTooltip(litem, prc);

			WatchlistItemColor(litem, prc);
		}

		Label AudioInputDevice = null;
		Extensions.NumericUpDownEx AudioInputVolume = null;
		ListView AudioInputs = null;
		ListView WatchlistRules = null;

		readonly ConcurrentDictionary<Process.Controller, ListViewItem> WatchlistMap = new ConcurrentDictionary<Process.Controller, ListViewItem>();
		readonly object watchlist_lock = new object();

		Label corCountLabel = null;
		ComboBox AudioInputEnable = null;

		ListView lastmodifylist = null;
		ListView powerbalancerlog = null;

		Label powerbalancer_behaviour = null;
		Label powerbalancer_plan = null;
		Label powerbalancer_forcedcount = null;

		ListView ExitWaitList = null;
		ListView ProcessingList = null;
		ConcurrentDictionary<int, ListViewItem> ExitWaitlistMap = null;

		#region Foreground Monitor
		Label activeLabel = null;
		Label activeExec = null;
		Label activeFullscreen = null;
		Label activePID = null;
		#endregion

		#region Path Cache
		Label cacheObjects = null;
		Label cacheRatio = null;
		#endregion

		int PathCacheUpdate_Lock = 0;

		public async void PathCacheUpdate(object _, EventArgs _ea)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			if (!Atomic.Lock(ref PathCacheUpdate_Lock)) return;

			try
			{
				await Task.Delay(5_000).ConfigureAwait(false);

				if (InvokeRequired)
					BeginInvoke(new Action(PathCacheUpdate_Invoke));
				else
					PathCacheUpdate_Invoke();
			}
			finally
			{
				Atomic.Unlock(ref PathCacheUpdate_Lock);
			}
		}

		void PathCacheUpdate_Invoke()
		{
			cacheObjects.Text = Statistics.PathCacheCurrent.ToString();
			double ratio = (Statistics.PathCacheMisses > 0 ? ((double)Statistics.PathCacheHits / (double)Statistics.PathCacheMisses) : 1d);
			cacheRatio.Text = ratio <= 99.99f ? $"{ratio:N2}" : ">99.99"; // let's just not overflow the UI
		}

		// BackColor = System.Drawing.Color.LightGoldenrodYellow
		Label netstatuslabel;
		Label inetstatuslabel;
		Label uptimestatuslabel;
		Label uptimeMeanLabel;
		Label netTransmit;
		Label netQueue;

		public static Serilog.Core.LoggingLevelSwitch LogIncludeLevel;

		public int UIUpdateFrequency
		{
			get => UItimer.Interval;
			set
			{
				int freq = value.Constrain(100, 5000);
				UItimer.Interval = freq;
			}
		}

		public void SetUIUpdateFrequency(int freq) => UIUpdateFrequency = freq;

		readonly System.Windows.Forms.Timer UItimer = new System.Windows.Forms.Timer();

		void StartUIUpdates(object sender, EventArgs _ea)
		{
			if (!IsHandleCreated) StopUIUpdates(this, EventArgs.Empty);
			else if (!UItimer.Enabled)
			{
				UpdateMemoryStats(sender, EventArgs.Empty);
				UpdateHealthMon(sender, EventArgs.Empty);
				UpdateNetwork(sender, EventArgs.Empty);
				UItimer.Start();
			}
		}

		void StopUIUpdates(object _, EventArgs _ea)
		{
			if (UItimer.Enabled) UItimer.Stop();
		}

		void Cleanup(object _, EventArgs _ea)
		{
			if (!IsHandleCreated) return;

			if (LastCauseTime.TimeTo(DateTimeOffset.UtcNow).TotalMinutes >= 3d)
			{
				pwcause.Text = HumanReadable.Generic.NotAvailable;
			}
		}

		void UpdateRescanCountdown(object _, EventArgs _ea)
		{
			if (!IsHandleCreated) return;
			if (processmanager is null) return; // not yet assigned

			// Rescan Countdown
			if (processmanager.ScanFrequency.HasValue)
				processingtimer.Text = $"{DateTimeOffset.UtcNow.TimeTo(processmanager.NextScan).TotalSeconds:N0}s";
			else
				processingtimer.Text = HumanReadable.Generic.NotAvailable;
		}

		void UpdateNetwork(object _, EventArgs _ea)
		{
			if (!IsHandleCreated) return;
			if (netmonitor is null) return;

			uptimestatuslabel.Text = HumanInterface.TimeString(netmonitor.Uptime);
			var mean = netmonitor.UptimeMean;
			if (double.IsInfinity(mean))
				uptimeMeanLabel.Text = "Infinite";
			else
				uptimeMeanLabel.Text = HumanInterface.TimeString(TimeSpan.FromMinutes(mean));

			var delta = netmonitor.GetTraffic;
			float netTotal = delta.Input + delta.Output;
			netTransmit.Text = $"{delta.Input / 1000:N1} kB In, {delta.Output / 1000:N1} kB Out [{delta.Packets:N0} packets; {delta.Queue:N0} queued]";
		}

		ListView NetworkDevices;

		ContextMenuStrip ifacems;
		ContextMenuStrip loglistms;
		ContextMenuStrip watchlistms;
		ToolStripMenuItem watchlistenable;

		void InterfaceContextMenuOpen(object _, EventArgs _ea)
		{
			try
			{
				foreach (ToolStripItem msi in ifacems.Items)
					msi.Enabled = (NetworkDevices.SelectedItems.Count == 1);
			}
			catch { } // discard
		}

		int IPv4Column = 4;
		int IPv6Column = 5;

		void CopyIPv4AddressToClipboard(object _, EventArgs _ea)
		{
			if (NetworkDevices.SelectedItems.Count == 1)
			{
				try
				{
					var li = NetworkDevices.SelectedItems[0];
					var ipv4addr = li.SubItems[IPv4Column].Text;
					Clipboard.SetText(ipv4addr, TextDataFormat.UnicodeText);
				}
				catch (Exception ex) { Logging.Stacktrace(ex); }
			}
		}

		void CopyIPv6AddressToClipboard(object _, EventArgs _ea)
		{
			if (NetworkDevices.SelectedItems.Count == 1)
				Clipboard.SetText($"[{NetworkDevices.SelectedItems[0].SubItems[IPv6Column].Text}]", TextDataFormat.UnicodeText);
		}

		void CopyIfaceToClipboard(object _, EventArgs _ea)
		{
			if (NetworkDevices.SelectedItems.Count == 1)
				Clipboard.SetText(netmonitor.GetDeviceData(NetworkDevices.SelectedItems[0].SubItems[0].Text), TextDataFormat.UnicodeText);
		}

		void CopyLogToClipboard(object _, EventArgs _ea)
		{
			if (LogList.SelectedItems.Count == 0) return;

			var sbs = new StringBuilder(256);

			foreach (ListViewItem item in LogList.SelectedItems)
				sbs.Append(item.SubItems[0].Text);

			Clipboard.SetText(sbs.ToString(), TextDataFormat.UnicodeText);
		}

		TabControl tabLayout = null;

		// TODO: Easier column access somehow than this?
		//int OrderColumn = 0;
		const int PrefColumn = 1;
		const int NameColumn = 2;
		const int ExeColumn = 3;
		const int PrioColumn = 4;
		const int AffColumn = 5;
		const int PowerColumn = 6;
		const int AdjustColumn = 7;
		const int PathColumn = 8;

		TabPage infoTab = null;
		TabPage watchTab = null;

		TabPage micTab = null;
		TabPage powerDebugTab = null;
		TabPage ProcessDebugTab = null;

		ToolStripMenuItem menu_debug_loglevel_info = null;
		ToolStripMenuItem menu_debug_loglevel_debug = null;
#if DEBUG
		ToolStripMenuItem menu_debug_loglevel_trace = null;
#endif

		void EnsureVerbosityLevel()
		{
			if (LogIncludeLevel.MinimumLevel == Serilog.Events.LogEventLevel.Information)
				LogIncludeLevel.MinimumLevel = Serilog.Events.LogEventLevel.Debug;
			UpdateLogLevelSelection();
		}

		void UpdateLogLevelSelection()
		{
			var level = LogIncludeLevel.MinimumLevel;

			menu_debug_loglevel_info.Checked = (level == Serilog.Events.LogEventLevel.Information);
			menu_debug_loglevel_debug.Checked = (level == Serilog.Events.LogEventLevel.Debug);
#if DEBUG
			menu_debug_loglevel_trace.Checked = (level == Serilog.Events.LogEventLevel.Verbose);
#endif
		}

		int MinimumHeight = 0;
		//int MinimumWidth = 0;

		ToolStripMenuItem menu_action_restart = null;
		ToolStripMenuItem menu_action_restartadmin = null;
		const string UpdateFrequencyName = "Update frequency";
		const string TopmostName = "Topmost";
		const string InfoName = "Info";
		const string TraceName = "Trace";
		const string ShowUnmodifiedPortionsName = "Unmodified portions";
		const string WatchlistName = "Watchlist";

		ToolStripMenuItem power_auto;
		ToolStripMenuItem power_highperf;
		ToolStripMenuItem power_balanced;
		ToolStripMenuItem power_saving;
		ToolStripMenuItem power_manual;

		void BuildUI()
		{
			Text = Taskmaster.Name
				/*
				+ " " + Version
#if DEBUG
				+ " DEBUG"
#endif
				*/
				;

			// Padding = new Padding(6);
			// margin

			// CORE LAYOUT ITEMS

			tabLayout = new TabControl()
			{
				Parent = this,
				//Height = 300,
				Padding = new System.Drawing.Point(6, 3),
				Dock = DockStyle.Top,
				MinimumSize = new System.Drawing.Size(-2, 360),
				SizeMode = TabSizeMode.Normal,
			};

			LogList = new ListView
			{
				Parent = this,
				Dock = DockStyle.Bottom,
				AutoSize = true,
				View = View.Details,
				FullRowSelect = true,
				HeaderStyle = ColumnHeaderStyle.Nonclickable,
				Scrollable = true,
				MinimumSize = new System.Drawing.Size(-2, 140),
				//MinimumSize = new System.Drawing.Size(-2, -2), // doesn't work
			};

			var imglist = new ImageList();
			imglist.Images.Add(Properties.Resources.OkayIcon);
			imglist.Images.Add(Properties.Resources.InfoIcon);
			imglist.Images.Add(Properties.Resources.ErrorIcon);
			LogList.SmallImageList = imglist;

			menu = new MenuStrip() { Dock = DockStyle.Top, Parent = this };

			BuildStatusbar();

			// LAYOUT ITEM CONFIGURATION

			#region Actions toolstrip menu
			var menu_action = new ToolStripMenuItem("Actions");
			// Sub Items
			var menu_action_rescan = new ToolStripMenuItem(HumanReadable.System.Process.Rescan, null, RescanRequestEvent)
			{
				Enabled = ProcessMonitorEnabled,
			};
			var menu_action_memoryfocus = new ToolStripMenuItem("Free memory for...", null, FreeMemoryRequest)
			{
				Enabled = PagingEnabled,
			};
			menu_action_restart = new ToolStripMenuItem(HumanReadable.System.Process.Restart, null, RestartRequestEvent);
			menu_action_restartadmin = new ToolStripMenuItem("Restart as admin", null, RestartRequestEvent)
			{
				Enabled = !MKAh.Execution.IsAdministrator
			};

			var menu_action_exit = new ToolStripMenuItem("Exit", null, ExitRequest);
			menu_action.DropDownItems.Add(menu_action_rescan);
			menu_action.DropDownItems.Add(menu_action_memoryfocus);
			menu_action.DropDownItems.Add(new ToolStripSeparator());
			menu_action.DropDownItems.Add(menu_action_restart);
			menu_action.DropDownItems.Add(menu_action_restartadmin);
			menu_action.DropDownItems.Add(menu_action_exit);
			#endregion // Actions toolstrip menu

			#region Miscellaneous toolstrip menu
			var menu_view = new ToolStripMenuItem("View");
			var menu_view_volume = new ToolStripMenuItem(HumanReadable.Hardware.Audio.Volume, null, ShowVolumeBox)
			{
				Enabled = AudioManagerEnabled,
			};

			menu_view.DropDownItems.Add(menu_view_volume);
			#endregion

			// POWER menu item

			var menu_power = new ToolStripMenuItem("Power")
			{
				Enabled = PowerManagerEnabled,
			};

			if (PowerManagerEnabled)
			{
				power_auto = new ToolStripMenuItem(HumanReadable.Hardware.Power.AutoAdjust, null, SetAutoPower) { Checked = false, CheckOnClick = true, Enabled = false };
				power_highperf = new ToolStripMenuItem(Power.Utility.GetModeName(Power.Mode.HighPerformance), null, (_, _ea) => SetPower(Power.Mode.HighPerformance));
				power_balanced = new ToolStripMenuItem(Power.Utility.GetModeName(Power.Mode.Balanced), null, (_, _ea) => SetPower(Power.Mode.Balanced));
				power_saving = new ToolStripMenuItem(Power.Utility.GetModeName(Power.Mode.PowerSaver), null, (_, _ea) => SetPower(Power.Mode.PowerSaver));
				power_manual = new ToolStripMenuItem("Manual override", null, SetManualPower) { CheckOnClick = true };

				menu_power.DropDownItems.Add(power_auto);
				menu_power.DropDownItems.Add(new ToolStripSeparator());
				menu_power.DropDownItems.Add(power_highperf);
				menu_power.DropDownItems.Add(power_balanced);
				menu_power.DropDownItems.Add(power_saving);
				menu_power.DropDownItems.Add(new ToolStripSeparator());
				menu_power.DropDownItems.Add(power_manual);

				if (powermanager != null)
					UpdatePowerBehaviourHighlight(powermanager.Behaviour);
			}

			// CONFIG menu item
			#region Config toolstrip menu
			var menu_config = new ToolStripMenuItem("Configuration");
			// Sub Items
			var menu_config_behaviour = new ToolStripMenuItem("Behaviour");
			var menu_config_visual = new ToolStripMenuItem("Visuals");
			var menu_config_logging = new ToolStripMenuItem(HumanReadable.Generic.Logging);
			var menu_config_bitmaskstyle = new ToolStripMenuItem("Bitmask style");
			//var menu_config_power = new ToolStripMenuItem("Power");// this submenu is no longer used

			// Sub Sub Items
			var menu_config_behaviour_autoopen = new ToolStripMenuItem("Auto-open menus")
			{
				Checked = AutoOpenMenus,
				CheckOnClick = true,
			};
			menu_config_behaviour_autoopen.Click += (_, _ea) =>
			{
				AutoOpenMenus = menu_config_behaviour_autoopen.Checked;

				using var corecfg = Taskmaster.Config.Load(CoreConfigFilename);
				corecfg.Config[HumanReadable.Generic.QualityOfLife]["Auto-open menus"].Bool = AutoOpenMenus;
			};

			var menu_config_behaviour_taskbar = new ToolStripMenuItem("Show in taskbar")
			{
				Checked = ShowInTaskbar,
				CheckOnClick = true,
			};
			menu_config_behaviour_taskbar.Click += (_, _ea) =>
			{
				ShowInTaskbar = menu_config_behaviour_taskbar.Checked;

				using var corecfg = Taskmaster.Config.Load(CoreConfigFilename);
				corecfg.Config[HumanReadable.Generic.QualityOfLife]["Show in taskbar"].Bool = ShowInTaskbar;
			};

			var menu_config_behaviour_exitconfirm = new ToolStripMenuItem("Exit confirmation")
			{
				Checked = ExitConfirmation,
				CheckOnClick = true,
			};
			menu_config_behaviour_exitconfirm.Click += (_, _ea) =>
			{
				ExitConfirmation = menu_config_behaviour_exitconfirm.Checked;

				using var corecfg = Taskmaster.Config.Load(CoreConfigFilename);
				corecfg.Config[HumanReadable.Generic.QualityOfLife]["Exit confirmation"].Bool = ExitConfirmation;
			};

			menu_config_behaviour.DropDownItems.Add(menu_config_behaviour_autoopen);
			menu_config_behaviour.DropDownItems.Add(menu_config_behaviour_taskbar);
			menu_config_behaviour.DropDownItems.Add(menu_config_behaviour_exitconfirm);

			// CONFIG -> VISUALS
			var menu_config_visuals_rowalternate = new ToolStripMenuItem("Alternate row colors");

			var menu_config_visuals_rowalternate_log = new ToolStripMenuItem("Log entries")
			{
				Checked = AlternateRowColorsLog,
				CheckOnClick = true,
			};
			var menu_config_visuals_rowalternate_watchlist = new ToolStripMenuItem(WatchlistName)
			{
				Checked = AlternateRowColorsWatchlist,
				CheckOnClick = true,
			};
			var menu_config_visuals_rowalternate_devices = new ToolStripMenuItem("Devices")
			{
				Checked = AlternateRowColorsDevices,
				CheckOnClick = true,
			};

			menu_config_visuals_rowalternate.DropDownItems.Add(menu_config_visuals_rowalternate_log);
			menu_config_visuals_rowalternate.DropDownItems.Add(menu_config_visuals_rowalternate_watchlist);
			menu_config_visuals_rowalternate.DropDownItems.Add(menu_config_visuals_rowalternate_devices);

			menu_config_visuals_rowalternate_log.Click += (_, _ea) =>
			{
				AlternateRowColorsLog = menu_config_visuals_rowalternate_log.Checked;

				using var uicfg = Taskmaster.Config.Load(UIConfigFilename);
				uicfg.Config[Constants.Visuals]["Alternate log row colors"].Bool = AlternateRowColorsLog;

				AlternateListviewRowColors(LogList, AlternateRowColorsLog);
			};
			menu_config_visuals_rowalternate_watchlist.Click += (_, _ea) =>
			{
				AlternateRowColorsWatchlist = menu_config_visuals_rowalternate_watchlist.Checked;

				using var uicfg = Taskmaster.Config.Load(UIConfigFilename);
				uicfg.Config[Constants.Visuals]["Alternate watchlist row colors"].Bool = AlternateRowColorsWatchlist;

				WatchlistColor();
			};
			menu_config_visuals_rowalternate_devices.Click += (_, _ea) =>
			{
				AlternateRowColorsDevices = menu_config_visuals_rowalternate_devices.Checked;

				using var uicfg = Taskmaster.Config.Load(UIConfigFilename);
				uicfg.Config[Constants.Visuals]["Alternate device row colors"].Bool = AlternateRowColorsDevices;

				if (AudioInputs != null)
					AlternateListviewRowColors(AudioInputs, AlternateRowColorsDevices);
				if (NetworkDevices != null)
					AlternateListviewRowColors(NetworkDevices, AlternateRowColorsDevices);
			};

			//
			var menu_config_visuals_topmost = new ToolStripMenuItem(Constants.StayOnTop);

			var menu_config_visuals_topmost_volume = new ToolStripMenuItem(Constants.VolumeMeter)
			{
				Checked = false,
				CheckOnClick = true,
			};
			menu_config_visuals_topmost_volume.Click += (_, _ea) =>
			{
				using var corecfg = Taskmaster.Config.Load(CoreConfigFilename);
				corecfg.Config[Constants.VolumeMeter][TopmostName].Bool = menu_config_visuals_topmost_volume.Checked;

				if (volumemeter != null)
					volumemeter.TopMost = menu_config_visuals_topmost_volume.Checked;
			};
			/*
			var menu_config_visuals_topmost_main = new ToolStripMenuItem("Main window")
			{
				Checked = TopMost,
				CheckOnClick = true,
			};
			*/

			menu_config_visuals_topmost.DropDownItems.Add(menu_config_visuals_topmost_volume);

			var menu_config_visuals_styling = new ToolStripMenuItem("Styling")
			{
				Checked = VisualStyling,
				CheckOnClick = true,
			};
			menu_config_visuals_styling.Click += (_, _ea) =>
			{
				using var uicfg = Taskmaster.Config.Load(UIConfigFilename);
				uicfg.Config[Constants.Windows]["Styling"].Bool = menu_config_visuals_styling.Checked;

				VisualStyling = menu_config_visuals_styling.Checked;
				UpdateStyling();
			};

			menu_config_visual.DropDownItems.Add(menu_config_visuals_styling);
			menu_config_visual.DropDownItems.Add(menu_config_visuals_rowalternate);
			menu_config_visual.DropDownItems.Add(menu_config_visuals_topmost);

			// CONFIG -> LOGGING
			var menu_config_logging_adjusts = new ToolStripMenuItem("Process adjusts")
			{
				Checked = ShowProcessAdjusts,
				CheckOnClick = true,
			};
			menu_config_logging_adjusts.Click += (_, _ea) =>
			{
				ShowProcessAdjusts = menu_config_logging_adjusts.Checked;

				using var corecfg = Taskmaster.Config.Load(CoreConfigFilename);
				corecfg.Config[HumanReadable.Generic.Logging]["Show process adjusts"].Bool = ShowProcessAdjusts;
			};

			var menu_config_logging_session = new ToolStripMenuItem("Session actions")
			{
				Checked = ShowSessionActions,
				CheckOnClick = true,
			};
			menu_config_logging_session.Click += (_, _ea) =>
			{
				ShowSessionActions = menu_config_logging_session.Checked;

				using var corecfg = Taskmaster.Config.Load(CoreConfigFilename);
				corecfg.Config[HumanReadable.Generic.Logging]["Show session actions"].Bool = ShowSessionActions;
			};

			var menu_config_logging_showunmodified = new ToolStripMenuItem(ShowUnmodifiedPortionsName)
			{
				Checked = Process.Manager.ShowUnmodifiedPortions,
				CheckOnClick = true,
			};
			menu_config_logging_showunmodified.Click += (_, _ea) =>
			{
				Process.Manager.ShowUnmodifiedPortions = menu_config_logging_showunmodified.Checked;

				using var corecfg = Taskmaster.Config.Load(CoreConfigFilename);
				corecfg.Config[HumanReadable.Generic.Logging][ShowUnmodifiedPortionsName].Bool = Process.Manager.ShowUnmodifiedPortions;
			};

			var menu_config_logging_showonlyfinal = new ToolStripMenuItem("Final state only")
			{
				Checked = Process.Manager.ShowOnlyFinalState,
				CheckOnClick = true,
			};
			menu_config_logging_showonlyfinal.Click += (_, _ea) =>
			{
				Process.Manager.ShowOnlyFinalState = menu_config_logging_showonlyfinal.Checked;
				using var corecfg = Taskmaster.Config.Load(CoreConfigFilename);
				corecfg.Config[HumanReadable.Generic.Logging]["Final state only"].Bool = Process.Manager.ShowOnlyFinalState;
			};

			var menu_config_logging_neterrors = new ToolStripMenuItem("Network errors")
			{
				Checked = Network.Manager.ShowNetworkErrors,
				CheckOnClick = true,
			};
			menu_config_logging_neterrors.Click += (_, _ea) =>
			{
				Network.Manager.ShowNetworkErrors = menu_config_logging_neterrors.Checked;

				using var corecfg = Taskmaster.Config.Load(CoreConfigFilename);
				corecfg.Config[HumanReadable.Generic.Logging]["Show network errors"].Bool = Network.Manager.ShowNetworkErrors;
			};

			var menu_config_logging_info = new ToolStripMenuItem("Information")
			{
				Checked = loglevelswitch.MinimumLevel == Serilog.Events.LogEventLevel.Information,
				CheckOnClick = true,
			};
			var menu_config_logging_debug = new ToolStripMenuItem(HumanReadable.Generic.Debug)
			{
				Checked = loglevelswitch.MinimumLevel == Serilog.Events.LogEventLevel.Debug,
				CheckOnClick = true,
			};
			var menu_config_logging_trace = new ToolStripMenuItem(TraceName)
			{
				Checked = loglevelswitch.MinimumLevel == Serilog.Events.LogEventLevel.Verbose,
				CheckOnClick = true,
			};
			menu_config_logging_info.Click += (_, _ea) =>
			{
				menu_config_logging_debug.Checked = false;
				menu_config_logging_trace.Checked = false;
				loglevelswitch.MinimumLevel = Serilog.Events.LogEventLevel.Information;
			};
			menu_config_logging_debug.Click += (_, _ea) =>
			{
				menu_config_logging_info.Checked = false;
				menu_config_logging_trace.Checked = false;
				loglevelswitch.MinimumLevel = Serilog.Events.LogEventLevel.Debug;
			};
			menu_config_logging_trace.Click += (_, _ea) =>
			{
				menu_config_logging_info.Checked = false;
				menu_config_logging_debug.Checked = false;
				loglevelswitch.MinimumLevel = Serilog.Events.LogEventLevel.Verbose;
			};

			menu_config_logging.DropDownItems.Add(menu_config_logging_adjusts);
			menu_config_logging_adjusts.DropDownItems.Add(menu_config_logging_showunmodified);
			menu_config_logging_adjusts.DropDownItems.Add(menu_config_logging_showonlyfinal);
			menu_config_logging.DropDownItems.Add(menu_config_logging_session);
			menu_config_logging.DropDownItems.Add(menu_config_logging_neterrors);
			menu_config_logging.DropDownItems.Add(new ToolStripSeparator());
			menu_config_logging.DropDownItems.Add(new ToolStripLabel("– NVM log Level –") { ForeColor = System.Drawing.SystemColors.GrayText });

			menu_config_logging.DropDownItems.Add(menu_config_logging_info);
			menu_config_logging.DropDownItems.Add(menu_config_logging_debug);
			menu_config_logging.DropDownItems.Add(menu_config_logging_trace);

			var menu_config_bitmaskstyle_bitmask = new ToolStripMenuItem("Bitmask")
			{
				Checked = AffinityStyle == 0,
			};
			var menu_config_bitmaskstyle_decimal = new ToolStripMenuItem("Decimal")
			{
				Checked = (AffinityStyle == 1),
			};
			menu_config_bitmaskstyle_bitmask.Click += (_, _ea) =>
			{
				AffinityStyle = 0;
				menu_config_bitmaskstyle_bitmask.Checked = true;
				menu_config_bitmaskstyle_decimal.Checked = false;
				// TODO: re-render watchlistRules

				using var corecfg = Taskmaster.Config.Load(CoreConfigFilename);
				corecfg.Config[HumanReadable.Generic.QualityOfLife][HumanReadable.Hardware.CPU.Settings.AffinityStyle].Int = 0;
			};
			menu_config_bitmaskstyle_decimal.Click += (_, _ea) =>
			{
				AffinityStyle = 1;
				menu_config_bitmaskstyle_bitmask.Checked = false;
				menu_config_bitmaskstyle_decimal.Checked = true;
				// TODO: re-render watchlistRules

				using var corecfg = Taskmaster.Config.Load(CoreConfigFilename);
				corecfg.Config[HumanReadable.Generic.QualityOfLife][HumanReadable.Hardware.CPU.Settings.AffinityStyle].Int = 1;
			};
			//var menu_config_bitmaskstyle_both = new ToolStripMenuItem("Decimal [Bitmask]");

			menu_config_bitmaskstyle.DropDownItems.Add(menu_config_bitmaskstyle_bitmask);
			menu_config_bitmaskstyle.DropDownItems.Add(menu_config_bitmaskstyle_decimal);
			//menu_config_bitmaskstyle.DropDownItems.Add(menu_config_bitmaskstyle_both);

			var menu_config_advanced = new ToolStripMenuItem("Advanced", null, (_, _ea) => Config.AdvancedConfig.Reveal());

			var menu_config_powermanagement = new ToolStripMenuItem("Power management", null, (_, _ea) => Config.PowerConfigWindow.Reveal(powermanager));
			//menu_config_power.DropDownItems.Add(menu_config_power_autoadjust); // sub-menu removed

			//

			var menu_config_log = new ToolStripMenuItem(HumanReadable.Generic.Logging);
			var menu_config_log_power = new ToolStripMenuItem("Power mode changes", null, (_, _ea) => { });
			menu_config_log.DropDownItems.Add(menu_config_log_power);

			var menu_config_components = new ToolStripMenuItem("Components", null, (_, _ea) => Config.ComponentConfigurationWindow.Reveal()); // MODAL
			var menu_config_experiments = new ToolStripMenuItem("Experiments", null, (_, _ea) => Config.ExperimentConfig.Reveal()); // MODAL

			var menu_config_folder = new ToolStripMenuItem("Open in file manager", null, (_, _ea) => System.Diagnostics.Process.Start(DataPath));
			// menu_config.DropDownItems.Add(menu_config_log);
			menu_config.DropDownItems.Add(menu_config_behaviour);
			menu_config.DropDownItems.Add(menu_config_visual);
			menu_config.DropDownItems.Add(menu_config_logging);
			menu_config.DropDownItems.Add(menu_config_bitmaskstyle);
			menu_config.DropDownItems.Add(new ToolStripSeparator());
			menu_config.DropDownItems.Add(menu_config_advanced);
			menu_config.DropDownItems.Add(menu_config_powermanagement);
			menu_config.DropDownItems.Add(menu_config_components);
			menu_config.DropDownItems.Add(new ToolStripSeparator());
			menu_config.DropDownItems.Add(menu_config_experiments);
			menu_config.DropDownItems.Add(new ToolStripSeparator());
			menu_config.DropDownItems.Add(menu_config_folder);
			#endregion // Config toolstrip menu

			// DEBUG menu item
			#region Debug toolstrip menu
			var menu_debug = new ToolStripMenuItem(HumanReadable.Generic.Debug);
			// Sub Items
			var menu_debug_loglevel = new ToolStripMenuItem("UI log level");

			LogIncludeLevel = MemoryLog.MemorySink.LevelSwitch; // HACK

			menu_debug_loglevel_info = new ToolStripMenuItem(InfoName, null,
			(_, _ea) =>
			{
				LogIncludeLevel.MinimumLevel = Serilog.Events.LogEventLevel.Information;
				Trace = false;
				UpdateLogLevelSelection();
			})
			{
				CheckOnClick = true,
				Checked = (LogIncludeLevel.MinimumLevel == Serilog.Events.LogEventLevel.Information),
			};
			menu_debug_loglevel_debug = new ToolStripMenuItem(HumanReadable.Generic.Debug, null,
			(_, _ea) =>
			{
				LogIncludeLevel.MinimumLevel = Serilog.Events.LogEventLevel.Debug;
				Trace = false;
				UpdateLogLevelSelection();
			})
			{
				CheckOnClick = true,
				Checked = (LogIncludeLevel.MinimumLevel == Serilog.Events.LogEventLevel.Debug),
			};
#if DEBUG
			menu_debug_loglevel_trace = new ToolStripMenuItem(TraceName, null,
			(_, _ea) =>
			{
				LogIncludeLevel.MinimumLevel = Serilog.Events.LogEventLevel.Verbose;
				Trace = true;
				UpdateLogLevelSelection();
				Log.Warning("Trace events enabled. UI may become unresponsive due to their volume.");
			})
			{
				CheckOnClick = true,
				Checked = (LogIncludeLevel.MinimumLevel == Serilog.Events.LogEventLevel.Verbose),
			};
#endif
			menu_debug_loglevel.DropDownItems.Add(menu_debug_loglevel_info);
			menu_debug_loglevel.DropDownItems.Add(menu_debug_loglevel_debug);
#if DEBUG
			menu_debug_loglevel.DropDownItems.Add(menu_debug_loglevel_trace);
#endif

			UpdateLogLevelSelection();

			var menu_debug_inaction = new ToolStripMenuItem("Show inaction") { Checked = ShowInaction, CheckOnClick = true };
			menu_debug_inaction.Click += (_, _ea) => ShowInaction = menu_debug_inaction.Checked;
			var menu_debug_agency = new ToolStripMenuItem("Show agency") { Checked = ShowAgency, CheckOnClick = true };
			menu_debug_agency.Click += (_, _ea) => ShowAgency = menu_debug_agency.Checked;
			var menu_debug_scanning = new ToolStripMenuItem("Scanning")
			{
				Checked = Process.Manager.DebugScan,
				CheckOnClick = true,
				Enabled = ProcessMonitorEnabled,
			};
			menu_debug_scanning.Click += (_, _ea) =>
			{
				Process.Manager.DebugScan = menu_debug_scanning.Checked;
				if (Process.Manager.DebugScan) EnsureVerbosityLevel();
			};

			var menu_debug_procs = new ToolStripMenuItem("Processes")
			{
				Checked = Process.Manager.DebugProcesses,
				CheckOnClick = true,
				Enabled = ProcessMonitorEnabled,
			};

			menu_debug_procs.Click += (_, _ea) =>
			{
				Process.Manager.DebugProcesses = menu_debug_procs.Checked;
				if (Process.Manager.DebugProcesses)
					StartProcessDebug();
				else
					StopProcessDebug();
			};

			var menu_debug_adjustdelay = new ToolStripMenuItem("Adjust delay")
			{
				Checked = Process.Manager.DebugAdjustDelay,
				CheckOnClick = true,
				Enabled = ProcessMonitorEnabled,
			};
			menu_debug_adjustdelay.Click += (_, _ea) => Process.Manager.DebugAdjustDelay = menu_debug_adjustdelay.Checked;

			var menu_debug_foreground = new ToolStripMenuItem(HumanReadable.System.Process.Foreground)
			{
				Checked = DebugForeground,
				CheckOnClick = true,
				Enabled = ActiveAppMonitorEnabled,
			};
			menu_debug_foreground.Click += (_, _ea) =>
			{
				DebugForeground = menu_debug_foreground.Checked;
				if (DebugForeground)
					StartProcessDebug();
				else
					StopProcessDebug();
			};

			var menu_debug_paths = new ToolStripMenuItem("Paths")
			{
				Checked = Process.Manager.DebugPaths,
				CheckOnClick = true,
			};
			menu_debug_paths.Click += (_, _ea) =>
			{
				Process.Manager.DebugPaths = menu_debug_paths.Checked;
				if (Process.Manager.DebugPaths) EnsureVerbosityLevel();
			};
			var menu_debug_power = new ToolStripMenuItem(HumanReadable.Hardware.Power.Section)
			{
				Checked = DebugPower,
				CheckOnClick = true,
				Enabled = PowerManagerEnabled,
			};
			menu_debug_power.Click += (_, _ea) =>
			{
				DebugPower = menu_debug_power.Checked;
				if (DebugPower)
				{
					var pev = new Power.ModeEventArgs(powermanager.CurrentMode);
					PowerPlanDebugEvent(this, pev); // populates powerbalancer_plan
					powermanager.PlanChange += PowerPlanDebugEvent;
					powermanager.AutoAdjustAttempt += PowerLoadDebugHandler;

					if (powerDebugTab is null) BuildPowerDebugPanel();
					else tabLayout.Controls.Add(powerDebugTab);
					EnsureVerbosityLevel();
				}
				else
				{
					powermanager.AutoAdjustAttempt -= PowerLoadDebugHandler;
					powermanager.PlanChange -= PowerPlanDebugEvent;
					//powermanager.onAutoAdjustAttempt -= PowerLoadHandler;
					bool refocus = tabLayout.SelectedTab.Equals(powerDebugTab);
					tabLayout.Controls.Remove(powerDebugTab);
					if (refocus) tabLayout.SelectedIndex = 1; // watchlist
				}
			};

			var menu_debug_session = new ToolStripMenuItem("Session")
			{
				Checked = DebugSession,
				CheckOnClick = true,
				Enabled = PowerManagerEnabled,
			};
			menu_debug_session.Click += (_, _ea) =>
			{
				DebugSession = menu_debug_session.Checked;
				if (DebugSession) EnsureVerbosityLevel();
			};
			var menu_debug_monitor = new ToolStripMenuItem(HumanReadable.Hardware.Monitor.Section)
			{
				Checked = DebugMonitor,
				CheckOnClick = true,
				Enabled = PowerManagerEnabled,
			};
			menu_debug_monitor.Click += (_, _ea) =>
			{
				DebugMonitor = menu_debug_monitor.Checked;
				if (DebugMonitor) EnsureVerbosityLevel();
			};

			var menu_debug_audio = new ToolStripMenuItem(HumanReadable.Hardware.Audio.Section)
			{
				Checked = DebugAudio,
				CheckOnClick = true,
				Enabled = AudioManagerEnabled,
			};
			menu_debug_audio.Click += (_, _ea) =>
			{
				DebugAudio = menu_debug_audio.Checked;
				if (DebugAudio) EnsureVerbosityLevel();
			};

			var menu_debug_clear = new ToolStripMenuItem("Clear UI log", null, (_, _ea) => ClearLog());

			// TODO: This menu needs to be clearer
			menu_debug.DropDownItems.Add(menu_debug_loglevel);
			menu_debug.DropDownItems.Add(new ToolStripSeparator());
			menu_debug.DropDownItems.Add(menu_debug_inaction);
			menu_debug.DropDownItems.Add(menu_debug_agency);
			menu_debug.DropDownItems.Add(new ToolStripSeparator());
			//menu_debug.DropDownItems.Add(menu_debug_scanning);
			menu_debug.DropDownItems.Add(menu_debug_procs);
			menu_debug.DropDownItems.Add(menu_debug_adjustdelay);
			menu_debug.DropDownItems.Add(menu_debug_foreground);
			//menu_debug.DropDownItems.Add(menu_debug_paths);
			menu_debug.DropDownItems.Add(menu_debug_power);
			menu_debug.DropDownItems.Add(menu_debug_session);
			menu_debug.DropDownItems.Add(menu_debug_monitor);
			menu_debug.DropDownItems.Add(menu_debug_audio);
			menu_debug.DropDownItems.Add(new ToolStripSeparator());
			menu_debug.DropDownItems.Add(menu_debug_clear);
			#endregion // Debug toolstrip menu

			// INFO menu
			#region Info toolstrip menu
			var menu_info = new ToolStripMenuItem(InfoName);
			// Sub Items

			//menu_info.DropDownItems.Add(new ToolStripMenuItem("Changelog", null, OpenChangelog));
			//menu_info.DropDownItems.Add(new ToolStripSeparator());
			menu_info.DropDownItems.Add(new ToolStripMenuItem("Github", null, (_, _ea) => System.Diagnostics.Process.Start(GitURL)));
			menu_info.DropDownItems.Add(new ToolStripMenuItem("Itch.io", null, (_, _ea) => System.Diagnostics.Process.Start(ItchURL)));
			menu_info.DropDownItems.Add(new ToolStripSeparator());
			menu_info.DropDownItems.Add(new ToolStripMenuItem(Constants.License, null, (_, _ea) => OpenLicenseDialog()));
			menu_info.DropDownItems.Add(new ToolStripMenuItem("3rd party licenses", null, ShowExternalLicenses));
			menu_info.DropDownItems.Add(new ToolStripSeparator());
			menu_info.DropDownItems.Add(new ToolStripMenuItem("About", null, ShowAboutDialog));
			#endregion

			menu.Items.AddRange(new[] { menu_action, menu_view, menu_power, menu_config, menu_debug, menu_info });

			// no simpler way?

			menu_action.MouseEnter += ToolStripMenuAutoOpen;
			menu_view.MouseEnter += ToolStripMenuAutoOpen;
			menu_config.MouseEnter += ToolStripMenuAutoOpen;
			menu_debug.MouseEnter += ToolStripMenuAutoOpen;
			menu_info.MouseEnter += ToolStripMenuAutoOpen;

			menu_action.DropDown.AutoClose = true;
			menu_view.DropDown.AutoClose = true;
			menu_config.DropDown.AutoClose = true;
			menu_debug.DropDown.AutoClose = true;
			menu_info.DropDown.AutoClose = true;

			infoTab = new TabPage(InfoName) { Padding = BigPadding };
			tabLayout.Controls.Add(infoTab);

			watchTab = new TabPage(WatchlistName) { Padding = BigPadding };
			tabLayout.Controls.Add(watchTab);

			var infopanel = new FlowLayoutPanel
			{
				Anchor = AnchorStyles.Top,
				Dock = DockStyle.Fill,
				FlowDirection = FlowDirection.TopDown,
				WrapContents = false,
				AutoSize = true,
				//Padding = DefaultPadding,
			};

			LoadUIConfiguration(out int opentab, out int[] appwidths, out int[] apporder, out int[] micwidths, out int[] ifacewidths);

			if (MicrophoneManagerEnabled) BuildMicrophonePanel(micwidths);

			// Main Window row 4-5, internet status
			TableLayoutPanel netstatus = null;
			if (NetworkMonitorEnabled) netstatus = BuildNetworkStatusUI(infopanel, ifacewidths);
			// End: Inet status

			GotFocus += StartUIUpdates;

			FormClosing += StopUIUpdates;

			//UItimer.Tick += Cleanup;

			// End: Settings

			BuildWatchlist(appwidths, apporder);

			// UI Log
			// -1 = contents, -2 = heading
			LogList.Columns.Add("Event Log", -2, HorizontalAlignment.Left); // 2

			//ResizeEnd += ResizeLogList;
			//Resize += ResizeLogList;

			SizeChanged += ResizeLogList;
			Shown += ResizeLogList;

			loglistms = new ContextMenuStrip();
			var logcopy = new ToolStripMenuItem("Copy to clipboard", null, CopyLogToClipboard);
			loglistms.Items.Add(logcopy);
			LogList.ContextMenuStrip = loglistms;

			using var cfg = Taskmaster.Config.Load(CoreConfigFilename);
			MaxLogSize = cfg.Config[HumanReadable.Generic.Logging].GetOrSet("UI max items", 200)
				.InitComment("Maximum number of items/lines to retain on UI level.")
				.Int;

			UItimer.Interval = cfg.Config[Constants.UserInterface].GetOrSet(UpdateFrequencyName, 2000)
				.InitComment("In milliseconds. Frequency of controlled UI updates. Affects visual accuracy of timers and such. Valid range: 100 to 5000.")
				.Int.Constrain(100, 5000);

			if (AudioManagerEnabled)
			{
				menu_config_visuals_topmost_volume.Checked = cfg.Config.Get(Constants.VolumeMeter)?.Get(TopmostName)?.Bool ?? true;
			}

			TableLayoutPanel cachePanel = DebugCache ? BuildCachePanel() : null;

			TableLayoutPanel tempmonitorpanel = TempMonitorEnabled ? BuildTempMonitorPanel() : null;

			var corepanel = new TableLayoutPanel()
			{
				ColumnCount = 2,
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowOnly,
				//Dock = DockStyle.Fill,
				Dock = DockStyle.Fill,
			};

			if (PowerManagerEnabled)
			{
				corepanel.Controls.Add(new AlignedLabel() { Text = "CPU" });
				cpuload = new AlignedLabel() { Text = HumanReadable.Generic.Uninitialized };
				corepanel.Controls.Add(cpuload);
			}
			// TODO: Add high, low and average

			corepanel.Controls.Add(new AlignedLabel() { Text = "RAM" });
			ramload = new AlignedLabel() { Text = HumanReadable.Generic.Uninitialized };
			corepanel.Controls.Add(ramload);

			TableLayoutPanel gpupanel = null;
			if (HardwareMonitorEnabled)
			{
				gpupanel = new TableLayoutPanel()
				{
					ColumnCount = 2,
					AutoSize = true,
					AutoSizeMode = AutoSizeMode.GrowOnly,
					//Dock = DockStyle.Fill,
					Dock = DockStyle.Fill,
				};

				gpupanel.Controls.Add(new AlignedLabel() { Text = "VRAM" });
				gpuvram = new AlignedLabel() { Text = HumanReadable.Generic.Uninitialized };
				gpupanel.Controls.Add(gpuvram);

				gpupanel.Controls.Add(new AlignedLabel() { Text = "Load" });
				gpuload = new AlignedLabel() { Text = HumanReadable.Generic.Uninitialized };
				gpupanel.Controls.Add(gpuload);

				gpupanel.Controls.Add(new AlignedLabel() { Text = "Temp" });
				gputemp = new AlignedLabel() { Text = HumanReadable.Generic.Uninitialized };
				gpupanel.Controls.Add(gputemp);

				gpupanel.Controls.Add(new AlignedLabel() { Text = "Fan" });
				gpufan = new AlignedLabel() { Text = HumanReadable.Generic.Uninitialized };
				gpupanel.Controls.Add(gpufan);
			}

			TableLayoutPanel nvmpanel = null;
			if (HealthMonitorEnabled) BuildNVMPanel(out nvmpanel);

			TableLayoutPanel powerpanel = null;
			if (PowerManagerEnabled) BuildPowerPanel(out powerpanel);

			TableLayoutPanel lastmodifypanel = null;
			if (LastModifiedList) lastmodifypanel = BuildLastModifiedPanel(appwidths);

			var coresystems = new FlowLayoutPanel()
			{
				FlowDirection = FlowDirection.TopDown,
				WrapContents = false,
				Dock = DockStyle.Fill,
				AutoSizeMode = AutoSizeMode.GrowOnly,
				AutoSize = true,
			};

			var additionalsystems = new FlowLayoutPanel()
			{
				FlowDirection = FlowDirection.TopDown,
				WrapContents = false,
				Dock = DockStyle.Fill,
				AutoSizeMode = AutoSizeMode.GrowOnly,
				AutoSize = true,
			};

			var systemlayout = new TableLayoutPanel()
			{
				ColumnCount = 2,
				RowCount = 1,
				Dock = DockStyle.Fill,
				AutoSizeMode = AutoSizeMode.GrowOnly,
				AutoSize = true,
			};

			// Insert info panel/tab contents
			if (corepanel != null)
			{
				coresystems.Controls.Add(new AlignedLabel() { Text = Constants.Core, Font = BoldFont });
				coresystems.Controls.Add(corepanel);
			}
			if (gpupanel != null)
			{
				coresystems.Controls.Add(new AlignedLabel() { Text = "GPU", Font = BoldFont });
				coresystems.Controls.Add(gpupanel);
			}
			if (powerpanel != null)
			{
				additionalsystems.Controls.Add(new AlignedLabel { Text = HumanReadable.Hardware.Power.Section, Font = BoldFont });
				additionalsystems.Controls.Add(powerpanel);
			}
			if (nvmpanel != null)
			{
				additionalsystems.Controls.Add(new AlignedLabel { Text = "Non-Volatile Memory", Font = BoldFont });
				additionalsystems.Controls.Add(nvmpanel);
			}
			systemlayout.Controls.Add(coresystems);
			systemlayout.Controls.Add(additionalsystems);
			systemlayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
			systemlayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f)); // surprisingly not redundant
			infopanel.Controls.Add(systemlayout);

			if (netstatus != null && NetworkDevices != null)
			{
				infopanel.Controls.Add(netstatus);
				infopanel.Controls.Add(NetworkDevices);
			}
			if (cachePanel != null) infopanel.Controls.Add(cachePanel);
			if (tempmonitorpanel != null) infopanel.Controls.Add(tempmonitorpanel);
			if (lastmodifypanel != null) infopanel.Controls.Add(lastmodifypanel);

			infoTab.Controls.Add(infopanel);

			// POWER DEBUG TAB

			if (DebugPower) BuildPowerDebugPanel();

			// -------------------------------------------------------------------------------------------------------

			if (Process.Manager.DebugProcesses || DebugForeground)
				BuildProcessDebug();

			// End Process Debug

			tabLayout.SelectedIndex = opentab >= tabLayout.TabCount ? 0 : opentab;

			// HANDLE TIMERS

			UItimer.Tick += UpdateMemoryStats;
			GotFocus += UpdateMemoryStats;
			UpdateMemoryStats(this, EventArgs.Empty);

			if (DebugCache && PathCacheLimit > 0)
			{
				UItimer.Tick += PathCacheUpdate;
				GotFocus += PathCacheUpdate;
				PathCacheUpdate(this, EventArgs.Empty);
			}
		}

		void ResizeLogList(object sender, EventArgs ev)
		{
			LogList.Columns[0].Width = -2;
			// HACK: Enable visual styles causes horizontal bar to always be present without the following.
			LogList.Columns[0].Width -= 2;

			//loglist.Height = -2;
			//loglist.Width = -2;
			LogList.Height = ClientSize.Height - tabLayout.Height - statusbar.Height - menu.Height;
			ShowLastLog();
		}

		ChangeLog changelog = null;

		void OpenChangelog(object sender, EventArgs e)
		{
			if (changelog is null)
			{
				(changelog = new ChangeLog("test"))
					.FormClosing += (_, _ea) => changelog = null;
			}
			else
			{
				// push window to top
				changelog.Show();
				// TODO: flash and make sure the window is actually not outside of display bounds?
			}
		}

		void SetAutoPower(object _, EventArgs _ea)
		{
			if (powermanager.Behaviour != Power.PowerBehaviour.Auto)
				powermanager.SetBehaviour(Power.PowerBehaviour.Auto);
			else
				powermanager.SetBehaviour(Power.PowerBehaviour.RuleBased);
		}

		void SetManualPower(object _, EventArgs _ea)
		{
			if (powermanager.Behaviour != Power.PowerBehaviour.Manual)
				powermanager.SetBehaviour(Power.PowerBehaviour.Manual);
			else
				powermanager.SetBehaviour(Power.PowerBehaviour.RuleBased);
		}

		void HighlightPowerMode()
		{
			switch (powermanager.CurrentMode)
			{
				case Power.Mode.Balanced:
					power_saving.Checked = false;
					power_balanced.Checked = true;
					power_highperf.Checked = false;
					break;
				case Power.Mode.HighPerformance:
					power_saving.Checked = false;
					power_balanced.Checked = false;
					power_highperf.Checked = true;
					break;
				case Power.Mode.PowerSaver:
					power_saving.Checked = true;
					power_balanced.Checked = false;
					power_highperf.Checked = false;
					break;
			}
		}

		private void SetPower(Power.Mode mode)
		{
			try
			{
				if (DebugPower) Log.Debug("<Power> Setting behaviour to manual.");

				powermanager.SetBehaviour(Power.PowerBehaviour.Manual);

				if (DebugPower) Log.Debug("<Power> Setting manual mode: " + mode.ToString());

				// powermanager.Restore(0).Wait(); // already called by setBehaviour as necessary
				powermanager?.SetMode(mode, new Cause(OriginType.User));

				// powermanager.RequestMode(mode);
				HighlightPowerMode();
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		private void ShowExternalLicenses(object sender, EventArgs e)
		{
			MessageBox.ShowModal("Third Party Licenses for " + Taskmaster.Name,
				Properties.Resources.ExternalLicenses,
				MessageBox.Buttons.OK, MessageBox.Type.Rich, parent: this);
		}

		void ShowVolumeBox(object sender, EventArgs e) => BuildVolumeMeter();

		void ToolStripMenuAutoOpen(object sender, EventArgs _)
		{
			var mi = sender as ToolStripMenuItem;
			if (!ContainsFocus || !AutoOpenMenus) return;
			mi?.ShowDropDown();
		}

		static void OpenLicenseDialog()
		{
			try
			{
				using var n = new LicenseDialog(initial: false);
				n.ShowDialog();
			}
			catch (Exception ex) { Logging.Stacktrace(ex); }
		}

		void BuildProcessDebug()
		{
			ExitWaitlistMap = new ConcurrentDictionary<int, ListViewItem>();

			ExitWaitList = new Extensions.ListViewEx()
			{
				AutoSize = true,
				//Height = 180,
				//Width = tabLayout.Width - 12, // FIXME: 3 for the bevel, but how to do this "right"?
				FullRowSelect = true,
				View = View.Details,
				MinimumSize = new System.Drawing.Size(-2, 80),
				Dock = DockStyle.Fill,
			};

			ExitWaitList.Columns.Add("Id", 50);
			ExitWaitList.Columns.Add(HumanReadable.System.Process.Executable, 280);
			ExitWaitList.Columns.Add("State", 160);
			ExitWaitList.Columns.Add(HumanReadable.Hardware.Power.Section, 80);

			var waitlist = processmanager?.GetExitWaitList();
			if ((waitlist?.Length ?? 0) > 0)
				foreach (var info in waitlist)
					ExitWaitListHandler(null, new ProcessModificationEventArgs(info));

			var processlayout = new TableLayoutPanel()
			{
				ColumnCount = 1,
				AutoSize = true,
				Dock = DockStyle.Fill,
			};

			if (ActiveAppMonitorEnabled)
			{
				var foregroundapppanel = new FlowLayoutPanel
				{
					Dock = DockStyle.Fill,
					FlowDirection = FlowDirection.LeftToRight,
					WrapContents = false,
					AutoSize = true,
					//Width = tabLayout.Width - 3,
				};

				activeLabel = new AlignedLabel() { Text = "no active window found", AutoEllipsis = true };
				activeExec = new AlignedLabel() { Text = HumanReadable.Generic.Uninitialized, Width = 100 };
				activeFullscreen = new AlignedLabel() { Text = HumanReadable.Generic.Uninitialized, Width = 60 };
				activePID = new AlignedLabel() { Text = HumanReadable.Generic.Uninitialized, Width = 60 };

				foregroundapppanel.Controls.Add(new AlignedLabel() { Text = "Active window:", Width = 80 });
				foregroundapppanel.Controls.Add(activeLabel);
				foregroundapppanel.Controls.Add(activeExec);
				foregroundapppanel.Controls.Add(activeFullscreen);
				foregroundapppanel.Controls.Add(new AlignedLabel { Text = "Id:", Width = 20 });
				foregroundapppanel.Controls.Add(activePID);

				processlayout.Controls.Add(foregroundapppanel);
			}

			processlayout.Controls.Add(new AlignedLabel() { Text = "Exit wait list...", Padding = BigPadding });
			processlayout.Controls.Add(ExitWaitList);

			processlayout.Controls.Add(new AlignedLabel() { Text = "Processing list" });

			ProcessingList = new Extensions.ListViewEx()
			{
				AutoSize = true,
				FullRowSelect = true,
				View = View.Details,
				MinimumSize = new System.Drawing.Size(-2, 120),
				Dock = DockStyle.Fill,
			};

			ProcessingList.Columns.Add("Id", 50);
			ProcessingList.Columns.Add(HumanReadable.System.Process.Executable, 280);
			ProcessingList.Columns.Add("State", 160);
			ProcessingList.Columns.Add("Time", 80);

			processlayout.Controls.Add(ProcessingList);

			ProcessDebugTab = new TabPage("Process Debug") { Padding = BigPadding };

			ProcessDebugTab.Controls.Add(processlayout);

			tabLayout.Controls.Add(ProcessDebugTab);
		}

		void BuildWatchlist(int[] appwidths, int[] apporder)
		{
			WatchlistRules = new ListView
			{
				Parent = this,
				View = View.Details,
				Dock = DockStyle.Fill,
				AutoSize = true,
				//Width = tabLayout.Width - 52,
				//Height = 260, // FIXME: Should use remaining space
				FullRowSelect = true,
				MinimumSize = new System.Drawing.Size(-2, -2),
				AllowColumnReorder = true,
				ShowItemToolTips = true,
			};

			WatchlistRules.KeyPress += WatchlistRulesKeyboardSearch;

			var numberColumns = new int[] { 0, AdjustColumn };
			var watchlistSorter = new WatchlistSorter(numberColumns, PrioColumn, PowerColumn);

			WatchlistRules.ListViewItemSorter = watchlistSorter; // what's the point of this?

			WatchlistRules.ColumnClick += (_, ea) =>
			{
				if (watchlistSorter.Column == ea.Column)
				{
					// flip order
					watchlistSorter.Order = watchlistSorter.Order == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
				}
				else
				{
					watchlistSorter.Order = SortOrder.Ascending;
					watchlistSorter.Column = ea.Column;
				}

				// deadlock if locked while adding
				WatchlistRules.Sort();
				WatchlistColor();
			};

			watchlistms = new ContextMenuStrip();
			watchlistms.Opened += WatchlistContextMenuOpen;
			watchlistenable = new ToolStripMenuItem(HumanReadable.Generic.Enabled, null, EnableWatchlistRule);
			var watchlistedit = new ToolStripMenuItem("Edit", null, EditWatchlistRule);
			var watchlistadd = new ToolStripMenuItem("Create new", null, AddWatchlistRule);
			var watchlistdel = new ToolStripMenuItem("Remove", null, DeleteWatchlistRule);
			var watchlistclip = new ToolStripMenuItem("Copy to clipboard", null, CopyRuleToClipboard);

			watchlistms.Items.Add(watchlistenable);
			watchlistms.Items.Add(new ToolStripSeparator());
			watchlistms.Items.Add(watchlistedit);
			watchlistms.Items.Add(watchlistadd);
			watchlistms.Items.Add(new ToolStripSeparator());
			watchlistms.Items.Add(watchlistdel);
			watchlistms.Items.Add(new ToolStripSeparator());
			watchlistms.Items.Add(watchlistclip);
			WatchlistRules.ContextMenuStrip = watchlistms;

			WatchlistRules.Columns.Add("#", appwidths[0]);
			WatchlistRules.Columns.Add("Pref.", appwidths[PrefColumn]);
			WatchlistRules.Columns.Add("Name", appwidths[NameColumn]);
			WatchlistRules.Columns.Add(HumanReadable.System.Process.Executable, appwidths[ExeColumn]);
			WatchlistRules.Columns.Add(HumanReadable.System.Process.Priority, appwidths[PrioColumn]);
			WatchlistRules.Columns.Add(HumanReadable.System.Process.Affinity, appwidths[AffColumn]);
			WatchlistRules.Columns.Add(HumanReadable.Hardware.Power.Plan, appwidths[PowerColumn]);
			WatchlistRules.Columns.Add("Adjusts", appwidths[AdjustColumn]);
			WatchlistRules.Columns.Add(HumanReadable.System.Process.Path, appwidths[PathColumn]);

			for (int i = 0; i < 8; i++)
				WatchlistRules.Columns[i].DisplayIndex = apporder[i];

			WatchlistRules.Scrollable = true;
			WatchlistRules.Alignment = ListViewAlignment.Left;

			WatchlistRules.DoubleClick += EditWatchlistRule; // for in-app editing

			watchTab.Controls.Add(WatchlistRules);
		}

		void LoadUIConfiguration(out int opentab, out int[] appwidths, out int[] apporder, out int[] micwidths, out int[] ifacewidths)
		{
			using var uicfg = Taskmaster.Config.Load(UIConfigFilename);

			var wincfg = uicfg.Config[Constants.Windows];
			var colcfg = uicfg.Config[Constants.Columns];
			var gencfg = uicfg.Config[Constants.Visuals];

			opentab = uicfg.Config[Constants.Tabs].Get("Open")?.Int ?? 0;
			appwidths = null;
			var appwidthsDefault = new int[] { 20, 20, 120, 140, 82, 60, 76, 46, 160 };
			appwidths = colcfg.GetOrSet(Constants.Apps, appwidthsDefault).IntArray;
			if (appwidths.Length != appwidthsDefault.Length) appwidths = appwidthsDefault;

			var appOrderDefault = new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 };
			apporder = colcfg.GetOrSet("App order", appOrderDefault).IntArray;
			var unqorder = new HashSet<int>(appOrderDefault.Length);
			foreach (var i in apporder) unqorder.Add(i);
			if (unqorder.Count != appOrderDefault.Length || unqorder.Max() != 7 || unqorder.Min() != 0) apporder = appOrderDefault;

			micwidths = null;
			if (MicrophoneManagerEnabled)
			{
				int[] micwidthsDefault = new int[] { 200, 220, 60, 60, 60, 120 };
				micwidths = colcfg.GetOrSet("Mics", micwidthsDefault).IntArray;
				if (micwidths.Length != micwidthsDefault.Length) micwidths = micwidthsDefault;
			}

			ifacewidths = null;
			if (NetworkMonitorEnabled)
			{
				int[] ifacewidthsDefault = new int[] { 110, 60, 50, 70, 90, 192, 60, 60, 40 };
				ifacewidths = colcfg.GetOrSet("Interfaces", ifacewidthsDefault).IntArray;
				if (ifacewidths.Length != ifacewidthsDefault.Length) ifacewidths = ifacewidthsDefault;
			}

			int[] winpos = wincfg.Get("Main")?.IntArray ?? null;
			if (winpos?.Length == 4)
			{
				var rectangle = new System.Drawing.Rectangle(winpos[0], winpos[1], winpos[2], winpos[3]);
				if (Screen.AllScreens.Any(ø => ø.Bounds.IntersectsWith(Bounds))) // https://stackoverflow.com/q/495380
				{
					StartPosition = FormStartPosition.Manual;
					Location = new System.Drawing.Point(rectangle.Left, rectangle.Top);
					Bounds = rectangle;
				}
			}

			//var alternateRowColor = gencfg.GetSetDefault("Alternate row color", new[] { 1 }, out modified).IntArray;

			DefaultLIBGColor = new ListViewItem().BackColor; // HACK; gets current color scheme default color

			AutocalcAlterColor();

			WarningColor = System.Drawing.Color.Red; // no decent way to autocalculate good warning color in case it blends with background

			//GrayText = System.Drawing.Color.FromArgb(130, 130, 130); // ignores user styles
			//AlterColor = System.Drawing.Color.FromArgb(245, 245, 245); // ignores user styles

			AlternateRowColorsDevices = gencfg.GetOrSet("Alternate device row colors", false).Bool;
			AlternateRowColorsWatchlist = gencfg.GetOrSet("Alternate watchlist row colors", true).Bool;
			AlternateRowColorsLog = gencfg.GetOrSet("Alternate log row colors", true).Bool;
		}

		void AutocalcAlterColor()
		{
			var defcolor = new ListViewItem().BackColor; // HACK; gets current color scheme default color

			int red = defcolor.R, green = defcolor.G, blue = defcolor.B;

			//int totalRGB = blue + green + red;
			//int highest = Math.Max(Math.Max(blue, green), red);
			int lowest = Math.Min(Math.Min(blue, green), red);

			if (lowest > 200) // bright = darken
			{
				red = (red - Math.Max(Convert.ToInt32(red * 0.04), 6)).Constrain(0, 255);
				green = (green - Math.Max(Convert.ToInt32(green * 0.04), 6)).Constrain(0, 255);
				blue = (blue - Math.Max(Convert.ToInt32(blue * 0.04), 6)).Constrain(0, 255);
				AlterColor = System.Drawing.Color.FromArgb(red, green, blue);
			}
			else // dark/midtone = brighten
			{
				red = (red + Math.Max(Convert.ToInt32(red * 0.04), 6)).Constrain(0, 255);
				green = (green + Math.Max(Convert.ToInt32(green * 0.04), 6)).Constrain(0, 255);
				blue = (blue + Math.Max(Convert.ToInt32(blue * 0.04), 6)).Constrain(0, 255);
				AlterColor = System.Drawing.Color.FromArgb(red, green, blue);
			}

			Logging.DebugMsg($"ALTER COLOR: {AlterColor.R}, {AlterColor.G}, {AlterColor.B}");
		}

		void BuildMicrophonePanel(int[] micwidths)
		{
			var micpanel = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				//Padding = DefaultPadding,
				//Width = tabLayout.Width - 12
			};
			micpanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20)); // this is dumb

			AudioInputDevice = new AlignedLabel { Text = HumanReadable.Generic.Uninitialized, AutoEllipsis = true };
			var micNameRow = new TableLayoutPanel
			{
				RowCount = 1,
				ColumnCount = 2,
				Dock = DockStyle.Top,
				//AutoSize = true // why not?
			};
			micNameRow.Controls.Add(new AlignedLabel { Text = "Default communications device:" });
			micNameRow.Controls.Add(AudioInputDevice);

			var miccntrl = new TableLayoutPanel()
			{
				RowCount = 1,
				ColumnCount = 6,
				Dock = DockStyle.Fill,
				AutoSize = true,
			};

			AudioInputVolume = new Extensions.NumericUpDownEx
			{
				Unit = "%",
				Increment = 1.0M,
				Maximum = 100.0M,
				Minimum = 0.0M,
				Width = 60,
				ReadOnly = true,
				Enabled = false,
				Dock = DockStyle.Top
			};
			AudioInputVolume.ValueChanged += UserMicVol;

			miccntrl.Controls.Add(new AlignedLabel { Text = "Volume" });
			miccntrl.Controls.Add(AudioInputVolume);

			corCountLabel = new AlignedLabel { Text = "0" };

			miccntrl.Controls.Add(new AlignedLabel { Text = "Correction count:" });
			miccntrl.Controls.Add(corCountLabel);

			AudioInputEnable = new ComboBox()
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				Items = { HumanReadable.Generic.Enabled, HumanReadable.Generic.Disabled },
				SelectedIndex = 1,
				Enabled = false,
			};

			miccntrl.Controls.Add(new AlignedLabel { Text = "Control:" });
			miccntrl.Controls.Add(AudioInputEnable);

			// End: Volume control

			// Main Window row 3, microphone device enumeration
			AudioInputs = new ListView
			{
				Dock = DockStyle.Top,
				//Width = tabLayout.Width - 12, // FIXME: 3 for the bevel, but how to do this "right"?
				Height = 120,
				View = View.Details,
				AutoSize = true,
				MinimumSize = new System.Drawing.Size(-2, -2),
				FullRowSelect = true
			};
			AudioInputs.Columns.Add("Name", micwidths[0]);
			AudioInputs.Columns.Add("GUID", micwidths[1]);
			AudioInputs.Columns.Add("Volume", micwidths[2]);
			AudioInputs.Columns.Add("Target", micwidths[3]);
			AudioInputs.Columns.Add("Control", micwidths[4]);
			AudioInputs.Columns.Add("State", micwidths[5]);

			micpanel.SizeChanged += (_, _ea) => AudioInputs.Width = micpanel.Width - micpanel.Margin.Horizontal - micpanel.Padding.Horizontal;

			micpanel.Controls.Add(micNameRow);
			micpanel.Controls.Add(miccntrl);
			micpanel.Controls.Add(AudioInputs);

			micTab = new TabPage("Microphone") { Padding = BigPadding };

			micTab.Controls.Add(micpanel);

			tabLayout.Controls.Add(micTab);
		}

		TableLayoutPanel BuildTempMonitorPanel()
		{
			TableLayoutPanel tempmonitorpanel;
			tempObjectCount = new AlignedLabel() { Width = 40, Text = HumanReadable.Generic.Uninitialized };

			tempObjectSize = new AlignedLabel() { Width = 40, Text = HumanReadable.Generic.Uninitialized };

			tempmonitorpanel = new TableLayoutPanel
			{
				Dock = DockStyle.Top,
				RowCount = 1,
				ColumnCount = 5,
				Height = 40,
				AutoSize = true
			};
			tempmonitorpanel.Controls.Add(new AlignedLabel { Text = "Temp" });
			tempmonitorpanel.Controls.Add(new AlignedLabel { Text = "Objects" });
			tempmonitorpanel.Controls.Add(tempObjectCount);
			tempmonitorpanel.Controls.Add(new AlignedLabel { Text = "Size (MB)" });
			tempmonitorpanel.Controls.Add(tempObjectSize);
			return tempmonitorpanel;
		}

		TableLayoutPanel BuildCachePanel()
		{
			var cachePanel = new TableLayoutPanel()
			{
				ColumnCount = 5,
				AutoSize = true,
				Dock = DockStyle.Fill,
			};
			cachePanel.Controls.Add(new AlignedLabel() { Text = "Path cache:" });
			cachePanel.Controls.Add(new AlignedLabel() { Text = "Objects" });
			cacheObjects = new AlignedLabel() { Width = 40, Text = HumanReadable.Generic.Uninitialized };
			cachePanel.Controls.Add(cacheObjects);
			cachePanel.Controls.Add(new AlignedLabel() { Text = "Ratio" });
			cacheRatio = new AlignedLabel() { Width = 40, Text = HumanReadable.Generic.Uninitialized };
			cachePanel.Controls.Add(cacheRatio);
			return cachePanel;
		}

		void BuildPowerDebugPanel()
		{
			var powerlayout = new TableLayoutPanel()
			{
				ColumnCount = 1,
				AutoSize = true,
				Dock = DockStyle.Fill
			};
			powerlayout.Controls.Add(new AlignedLabel() { Text = "Power mode autobalancing tracker...", Padding = BigPadding });

			powerbalancerlog = new Extensions.ListViewEx()
			{
				Parent = this,
				Dock = DockStyle.Top,
				AutoSize = true,
				//Height = 80,
				//Width = tabLayout.Width - 12, // FIXME: 3 for the bevel, but how to do this "right"?
				MinimumSize = new System.Drawing.Size(-2, 180),
				FullRowSelect = true,
				View = View.Details,
			};
			powerbalancerlog.Columns.Add("Current", 60);
			powerbalancerlog.Columns.Add("Mean", 60);
			powerbalancerlog.Columns.Add("High", 60);
			powerbalancerlog.Columns.Add("Low", 60);
			powerbalancerlog.Columns.Add("Reaction", 80);
			powerbalancerlog.Columns.Add("Reactionary Plan", 120);
			powerbalancerlog.Columns.Add("Enacted", 60);
			powerbalancerlog.Columns.Add("Pressure", 60);

			powerlayout.Controls.Add(powerbalancerlog);

			var powerbalancerstatus = new FlowLayoutPanel()
			{
				FlowDirection = FlowDirection.LeftToRight,
				WrapContents = false,
				AutoSize = true,
				Dock = DockStyle.Top
			};
			powerbalancerstatus.Controls.Add(new AlignedLabel() { Text = "Behaviour:" });
			powerbalancer_behaviour = new AlignedLabel() { Text = HumanReadable.Generic.Uninitialized };
			powerbalancerstatus.Controls.Add(powerbalancer_behaviour);
			powerbalancerstatus.Controls.Add(new AlignedLabel() { Text = "| Plan:" });
			powerbalancer_plan = new AlignedLabel() { Text = HumanReadable.Generic.Uninitialized };
			powerbalancerstatus.Controls.Add(powerbalancer_plan);
			powerbalancerstatus.Controls.Add(new AlignedLabel() { Text = "Forced by:" });
			powerbalancer_forcedcount = new AlignedLabel() { Text = HumanReadable.Generic.Uninitialized };
			powerbalancerstatus.Controls.Add(powerbalancer_forcedcount);

			powerlayout.Controls.Add(powerbalancerstatus);

			powerDebugTab = new TabPage("Power Debug") { Padding = BigPadding };
			powerDebugTab.Controls.Add(powerlayout);
			tabLayout.Controls.Add(powerDebugTab);
		}

		TableLayoutPanel BuildLastModifiedPanel(int[] appwidths)
		{
			var lastmodifypanel = new TableLayoutPanel
			{
				Dock = DockStyle.Top,
				ColumnCount = 1,
				Height = 40,
				AutoSize = true
			};
			lastmodifypanel.Controls.Add(new AlignedLabel() { Text = "Last process modifications" });
			lastmodifylist = new Extensions.ListViewEx()
			{
				Parent = this,
				Dock = DockStyle.Top,
				AutoSize = true,
				View = View.Details,
				FullRowSelect = true,
				HeaderStyle = ColumnHeaderStyle.Nonclickable,
				//Scrollable = true,
				MinimumSize = new System.Drawing.Size(-2, 60),
				//MinimumSize = new System.Drawing.Size(-2, -2), // doesn't work
			};

			lastmodifylist.Columns.Add("Time", 60);
			lastmodifylist.Columns.Add(HumanReadable.System.Process.Executable, appwidths[2]);
			lastmodifylist.Columns.Add("Rule", appwidths[1]);
			lastmodifylist.Columns.Add(HumanReadable.System.Process.Priority, appwidths[3]);
			lastmodifylist.Columns.Add(HumanReadable.System.Process.Affinity, appwidths[4]);
			lastmodifylist.Columns.Add(HumanReadable.System.Process.Path, -2);

			lastmodifypanel.Controls.Add(lastmodifylist);
			var lastmodifyms = new ContextMenuStrip();
			var lastmodifycopy = new ToolStripMenuItem("Copy path to clipboard", null, (_, _ea) =>
			{
				if (lastmodifylist.SelectedItems.Count > 0)
				{
					string path = lastmodifylist.SelectedItems[0].SubItems[5].Text;
					if (!string.IsNullOrEmpty(path))
						Clipboard.SetText(path, TextDataFormat.UnicodeText);
				}
			});
			lastmodifyms.Opened += (_, _ea) => lastmodifycopy.Enabled = (lastmodifylist.SelectedItems.Count == 1);
			lastmodifyms.Items.Add(lastmodifycopy);
			lastmodifylist.ContextMenuStrip = lastmodifyms;
			return lastmodifypanel;
		}

		void BuildPowerPanel(out TableLayoutPanel powerpanel)
		{
			powerpanel = new TableLayoutPanel()
			{
				ColumnCount = 2,
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowOnly,
				Dock = DockStyle.Fill,
			};

			pwmode = new AlignedLabel { Text = HumanReadable.Generic.Uninitialized };
			pwcause = new AlignedLabel { Text = HumanReadable.Generic.Uninitialized };
			pwbehaviour = new AlignedLabel { Text = HumanReadable.Generic.Uninitialized };
			powerpanel.Controls.Add(new AlignedLabel { Text = "Behaviour:" });
			powerpanel.Controls.Add(pwbehaviour);
			powerpanel.Controls.Add(new AlignedLabel { Text = "Mode:" });
			powerpanel.Controls.Add(pwmode);
			powerpanel.Controls.Add(new AlignedLabel { Text = "Cause:" });
			powerpanel.Controls.Add(pwcause);
		}

		void BuildNVMPanel(out TableLayoutPanel nvmpanel)
		{
			nvmpanel = new TableLayoutPanel()
			{
				ColumnCount = 2,
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowOnly,
				Dock = DockStyle.Fill,
			};

			nvmtransfers = new AlignedLabel { Text = HumanReadable.Generic.Uninitialized };
			nvmsplitio = new AlignedLabel { Text = HumanReadable.Generic.Uninitialized };
			nvmdelay = new AlignedLabel { Text = HumanReadable.Generic.Uninitialized };
			nvmqueued = new AlignedLabel { Text = HumanReadable.Generic.Uninitialized };
			//hardfaults = new Label { Text = HumanReadable.Generic.Uninitialized, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Left };

			nvmpanel.Controls.Add(new AlignedLabel { Text = "Transfers" });
			nvmpanel.Controls.Add(nvmtransfers);
			nvmpanel.Controls.Add(new AlignedLabel { Text = "Split I/O" });
			nvmpanel.Controls.Add(nvmsplitio);
			nvmpanel.Controls.Add(new AlignedLabel { Text = "Delay" });
			nvmpanel.Controls.Add(nvmdelay);
			nvmpanel.Controls.Add(new AlignedLabel { Text = "Queued" });
			nvmpanel.Controls.Add(nvmqueued);
			//nvmpanel.Controls.Add(new Label { Text = "Hard faults", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Left });
			//nvmpanel.Controls.Add(hardfaults);
		}

		TableLayoutPanel BuildNetworkStatusUI(FlowLayoutPanel infopanel, int[] ifacewidths)
		{
			TableLayoutPanel netstatus;
			netstatuslabel = new AlignedLabel() { Text = HumanReadable.Generic.Uninitialized };
			inetstatuslabel = new AlignedLabel() { Text = HumanReadable.Generic.Uninitialized };
			uptimeMeanLabel = new AlignedLabel() { Text = HumanReadable.Generic.Uninitialized };
			netTransmit = new AlignedLabel() { Text = HumanReadable.Generic.Uninitialized };
			netQueue = new AlignedLabel() { Text = HumanReadable.Generic.Uninitialized };
			uptimestatuslabel = new AlignedLabel { Text = HumanReadable.Generic.Uninitialized };

			netstatus = new TableLayoutPanel
			{
				ColumnCount = 6,
				RowCount = 1,
				Dock = DockStyle.Top,
				AutoSize = true,
			};

			// first row
			netstatus.Controls.Add(new AlignedLabel() { Text = "Network" });
			netstatus.Controls.Add(netstatuslabel);

			netstatus.Controls.Add(new AlignedLabel() { Text = "Uptime" });
			netstatus.Controls.Add(uptimestatuslabel);

			netstatus.Controls.Add(new AlignedLabel() { Text = "Transmission" });
			netstatus.Controls.Add(netTransmit);

			// second row
			netstatus.Controls.Add(new AlignedLabel() { Text = "Internet" });
			netstatus.Controls.Add(inetstatuslabel);

			netstatus.Controls.Add(new AlignedLabel { Text = "Average" });
			netstatus.Controls.Add(uptimeMeanLabel);

			//netstatus.Controls.Add(new Label() { Text = "??", Dock = DockStyle.Left, AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
			//netstatus.Controls.Add(netQueue);

			NetworkDevices = new ListView
			{
				AutoSize = true,
				MinimumSize = new System.Drawing.Size(-2, 40),
				View = View.Details,
				FullRowSelect = true,
				Height = 64,
			};

			infopanel.SizeChanged += (_, _ea) => NetworkDevices.Width = infopanel.ClientSize.Width - infopanel.Padding.Horizontal - infopanel.Margin.Vertical;

			ifacems = new ContextMenuStrip();
			ifacems.Opened += InterfaceContextMenuOpen;
			var ifaceip4copy = new ToolStripMenuItem("Copy IPv4 address", null, CopyIPv4AddressToClipboard);
			var ifaceip6copy = new ToolStripMenuItem("Copy IPv6 address", null, CopyIPv6AddressToClipboard);
			var ifacecopy = new ToolStripMenuItem("Copy full information", null, CopyIfaceToClipboard);
			ifacems.Items.Add(ifaceip4copy);
			ifacems.Items.Add(ifaceip6copy);
			ifacems.Items.Add(ifacecopy);
			NetworkDevices.ContextMenuStrip = ifacems;

			NetworkDevices.Columns.Add("Device", ifacewidths[0]); // 0
			NetworkDevices.Columns.Add("Type", ifacewidths[1]); // 1
			NetworkDevices.Columns.Add("Status", ifacewidths[2]); // 2
			NetworkDevices.Columns.Add("Link speed", ifacewidths[3]); // 3
			NetworkDevices.Columns.Add("IPv4", ifacewidths[4]); // 4
			NetworkDevices.Columns.Add("IPv6", ifacewidths[5]); // 5
			NetworkDevices.Columns.Add("Packet Δ", ifacewidths[6]); // 6
			NetworkDevices.Columns.Add("Error Δ", ifacewidths[7]); // 7
			NetworkDevices.Columns.Add("Errors", ifacewidths[8]); // 8
			PacketDeltaColumn = 6;
			ErrorDeltaColumn = 7;
			ErrorTotalColumn = 8;

			NetworkDevices.Scrollable = true;

			netstatus.RowStyles.Add(new RowStyle(SizeType.AutoSize, 32));

			IPv4Column = 4;
			IPv6Column = 5;
			return netstatus;
		}

		void ShowExperimentConfig(object sender, EventArgs ea)
		{
			Config.ExperimentConfig.Reveal();
		}

		void ShowAboutDialog(object sender, EventArgs ea)
		{
			var builddate = BuildDate();

			var now = DateTime.Now;
			var age = (now - builddate).TotalDays;

			var sbs = new StringBuilder()
				.AppendLine(Taskmaster.Name)
				.Append("Version: ").AppendLine(Version)
				.Append("Built: ").Append(builddate.ToString("yyyy/MM/dd HH:mm")).Append(" [").AppendFormat("{0:N0}", age).AppendLine(" days old]")
				.AppendLine()
				.AppendLine("Created by M.A., 2016–2019")
				.AppendLine()
				.Append("At Github: ").AppendLine(GitURL)
				.Append("At Itch.io: ").AppendLine(ItchURL)
				.AppendLine()
				.AppendLine("Free system maintenance and de-obnoxifying app.")
				.AppendLine()
				.AppendLine("Available under MIT license.");

			MessageBox.ShowModal("About " + Taskmaster.Name + "!", sbs.ToString(), MessageBox.Buttons.OK, parent: this);
		}

		readonly Stopwatch WatchlistSearchInputTimer = new Stopwatch();
		readonly System.Windows.Forms.Timer WatchlistSearchTimer = new System.Windows.Forms.Timer();
		string SearchString = string.Empty;

		void WatchlistRulesKeyboardSearch(object _, KeyPressEventArgs ea)
		{
			bool ctrlchar = char.IsControl(ea.KeyChar);

			// RESET
			if (WatchlistSearchInputTimer.ElapsedMilliseconds > 2_700) // previous input too long ago
				SearchString = string.Empty;

			if (string.IsNullOrEmpty(SearchString)) // catches above and initial state
				WatchlistSearchTimer.Start();

			WatchlistSearchInputTimer.Restart();

			if (Trace) Logging.DebugMsg($"INPUT: {((int)ea.KeyChar):X}");

			if (char.IsControl(ea.KeyChar))
			{
				if (Trace) Logging.DebugMsg("CONTROL CHARACTER!");

				if (ea.KeyChar == (char)Keys.Back && SearchString.Length > 0) // BACKSPACE
					SearchString = SearchString.Remove(SearchString.Length - 1); // ugly and probably slow
				else if (ea.KeyChar == 0x7F && SearchString.Length > 0) // 0x7F is ctrl-backspace (delete)
					SearchString = SearchString.Remove(SearchString.LastIndexOfAny(new[] { ' ', '\t', '\r', '\n' }).Min(0));
				else if (ea.KeyChar == (char)Keys.Escape)
				{
					SearchString = string.Empty;
					WatchlistSearchTimer.Stop();
					return;
				}
				// ignore control characters otherwise
				//else
				//	SearchString += ea.KeyChar;
			}
			else
				SearchString += ea.KeyChar;

			if (!WatchlistSearchTimer.Enabled) WatchlistSearchTimer.Start();

			ea.Handled = true;

			tooltip.Show("Searching: " + SearchString, WatchlistRules,
				WatchlistRules.ClientSize.Width / 3, WatchlistRules.ClientSize.Height,
				string.IsNullOrEmpty(SearchString) ? 500 : 2_500);
		}

		void WatchlistSearchTimer_Tick(object sender, EventArgs e)
		{
			bool foundprimary = false, found = false;

			if (!string.IsNullOrEmpty(SearchString))
			{
				var search = SearchString.ToLowerInvariant();

				foreach (ListViewItem item in WatchlistRules.Items)
				{
					found = false;

					if (item.SubItems[NameColumn].Text.ToLower().Contains(search))
						found = true;
					else if (item.SubItems[ExeColumn].Text.ToLower().Contains(search))
						found = true;
					else if (item.SubItems[PathColumn].Text.ToLower().Contains(search))
						found = true;

					if (found)
					{
						if (!foundprimary)
						{
							foundprimary = true;
							WatchlistRules.FocusedItem = item;
							item.Focused = true;
							item.EnsureVisible();
						}
					}

					item.Selected = found;
				}
			}

			if (found || WatchlistSearchInputTimer.ElapsedMilliseconds > 1_000)
				WatchlistSearchTimer.Stop();
		}

		public void CPULoadEvent(object _, CPUSensorEventArgs ea)
		{
			if (!IsHandleCreated) return;

			BeginInvoke(new Action(() =>
			{
				//
			}));
		}

		public void GPULoadEvent(object _, GPUSensorEventArgs ea)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			if (InvokeRequired)
				BeginInvoke(new Action(() => GPULoadEvent_Invoke(ea)));
			else
				GPULoadEvent_Invoke(ea);
		}

		void GPULoadEvent_Invoke(GPUSensorEventArgs ea)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			try
			{
				GPUSensorUpdate(ea.Data);
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		void GPULoadPoller(object sender, EventArgs e)
		{
			try
			{
				var sensors = hardwaremonitor.GPUSensorData();
				GPUSensorUpdate(sensors);
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		void GPUSensorUpdate(GPUSensors sensors)
		{
			try
			{
				float vramTotal = sensors.MemTotal / 1024;
				float vramUsed = vramTotal * (sensors.MemLoad / 100);
				float vramFree = vramTotal - vramUsed;

				// gpuvram.Text = $"{vramFree:N2} of {vramTotal:N1} GiB free ({ea.Data.MemLoad:N1} % usage) [Controller: {ea.Data.MemCtrl:N1} %]";
				gpuvram.Text = $"{vramFree:N2} GiB free ({sensors.MemLoad:N1} % usage) [Controller: {sensors.MemCtrl:N1} %]";
				gpuload.Text = $"{sensors.Load:N1} %";
				gpufan.Text = $"{sensors.FanLoad:N1} % [{sensors.FanSpeed} RPM]";
				gputemp.Text = $"{sensors.Temperature:N1} C";
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		void StartProcessDebug()
		{
			bool enabled = Process.Manager.DebugProcesses || DebugForeground;
			if (!enabled) return;

			if (Process.Manager.DebugProcesses) processmanager.HandlingStateChange += ProcessHandlingStateChangeEvent;

			if (enabled && ProcessDebugTab is null)
				BuildProcessDebug();

			if (activeappmonitor != null && DebugForeground)
				activeappmonitor.ActiveChanged += OnActiveWindowChanged;

			EnsureVerbosityLevel();
		}

		/// <summary>
		/// Process ID to processinglist mapping.
		/// </summary>
		readonly ConcurrentDictionary<int, ListViewItem> ProcessEventMap = new ConcurrentDictionary<int, ListViewItem>();

		void ProcessHandlingStateChangeEvent(object _, Process.HandlingStateChangeEventArgs ea)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			if (!Process.Manager.DebugProcesses && !DebugForeground) return;

			try
			{
				int key = ea.Info.Id;
				bool newitem = false;
				if (!ProcessEventMap.TryGetValue(key, out ListViewItem item))
				{
					item = new ListViewItem(new string[] { key.ToString(), ea.Info.Name, string.Empty, string.Empty });
					newitem = true;
					ProcessEventMap.TryAdd(key, item);
				}

				if (InvokeRequired)
					BeginInvoke(new Action(() => ProcessHandlingStateChangeEvent_Invoke(ea, item, newitem)));
				else
					ProcessHandlingStateChangeEvent_Invoke(ea, item, newitem);
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		void ProcessHandlingStateChangeEvent_Invoke(Process.HandlingStateChangeEventArgs ea, ListViewItem item, bool newitem = false)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			int key = ea.Info.Id;

			try
			{
				// 0 = Id, 1 = Name, 2 = State
				item.SubItems[0].Text = ea.Info.Id.ToString();
				item.SubItems[2].Text = ea.Info.State.ToString();
				item.SubItems[3].Text = DateTime.Now.ToLongTimeString();

				if (newitem) ProcessingList.Items.Insert(0, item);

				if (ea.Info.Handled) RemoveOldProcessingEntry(key);
			}
			catch (System.ObjectDisposedException)
			{
				// bah
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		void RemoveOldProcessingEntry(int key)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			if (InvokeRequired)
				BeginInvoke(new Action(async () => await RemoveOldProcessingEntry_Invoke(key).ConfigureAwait(true)));
			else
				RemoveOldProcessingEntry_Invoke(key).ConfigureAwait(true);
		}

		async Task RemoveOldProcessingEntry_Invoke(int key)
		{
			await Task.Delay(TimeSpan.FromSeconds(15)).ConfigureAwait(true);

			if (!IsHandleCreated || DisposedOrDisposing) return;

			try
			{
				if (ProcessEventMap.TryRemove(key, out ListViewItem item))
					ProcessingList.Items.Remove(item);
			}
			catch { }
		}

		void StopProcessDebug()
		{
			if (!DebugForeground && activeappmonitor != null) activeappmonitor.ActiveChanged -= OnActiveWindowChanged;
			if (!Process.Manager.DebugProcesses) processmanager.HandlingStateChange -= ProcessHandlingStateChangeEvent;

			bool enabled = Process.Manager.DebugProcesses || DebugForeground;
			if (enabled) return;

			if (activeappmonitor != null && DebugForeground)
				activeappmonitor.ActiveChanged -= OnActiveWindowChanged;

			bool refocus = tabLayout.SelectedTab.Equals(ProcessDebugTab);
			if (!enabled)
			{
				activePID.Text = HumanReadable.Generic.Undefined;
				activeFullscreen.Text = HumanReadable.Generic.Undefined;
				activeFullscreen.Text = HumanReadable.Generic.Undefined;

				tabLayout.Controls.Remove(ProcessDebugTab);
				ProcessingList.Items.Clear();
				ExitWaitList.Items.Clear();
			}

			// TODO: unlink events
			if (refocus) tabLayout.SelectedIndex = 1; // watchlist
		}

		StatusStrip statusbar;
		ToolStripStatusLabel processingcount;
		ToolStripStatusLabel processingtimer;
		//ToolStripStatusLabel adjustcounter;
		ToolStripStatusLabel powermodestatusbar;

		void BuildStatusbar()
		{
			statusbar = new StatusStrip()
			{
				Parent = this,
				Dock = DockStyle.Bottom,
			};

			statusbar.Items.Add("Processing:");
			processingcount = new ToolStripStatusLabel("[" + HumanReadable.Generic.Uninitialized + "]") { AutoSize = false };
			statusbar.Items.Add(processingcount); // not truly useful for anything but debug to show if processing is hanging Somewhere
			statusbar.Items.Add("Next scan in:");
			processingtimer = new ToolStripStatusLabel("[" + HumanReadable.Generic.Uninitialized + "]") { AutoSize = false };
			statusbar.Items.Add(processingtimer);
			statusbar.Items.Add(new ToolStripStatusLabel() { Alignment = ToolStripItemAlignment.Right, Width = -2, Spring = true });
			//verbositylevel = new ToolStripStatusLabel(HumanReadable.Generic.Uninitialized);
			//statusbar.Items.Add(verbositylevel);

			statusbar.Items.Add(new ToolStripStatusLabel("Power plan:") { Alignment = ToolStripItemAlignment.Right });
			powermodestatusbar = new ToolStripStatusLabel(Power.Utility.GetModeName(powermanager?.CurrentMode ?? Power.Mode.Undefined)) { Alignment = ToolStripItemAlignment.Right };
			statusbar.Items.Add(powermodestatusbar);
		}

		async void FreeMemoryRequest(object _, EventArgs _ea)
		{
			try
			{
				using var exsel = new ProcessSelectDialog(
					"WARNING: This Can be a Bad idea." +
					"\nAll application memory is pushed to page file. This will temporarily increase available RAM," +
					"\nbut increases NVM usage significantly until apps have paged back the memory they actively need." +
					"\n\nSelection omits chosen app from paging. Select nothing to try free memory in general.");

				if (exsel.ShowDialog(this) == DialogResult.OK)
					await processmanager?.FreeMemory(exsel.Info.Name);
			}
			catch (Exception ex) { Logging.Stacktrace(ex); }
		}

		public void ExitWaitListHandler(object _discard, ProcessModificationEventArgs ea)
		{
			if (activeappmonitor is null) return;
			if (!IsHandleCreated || DisposedOrDisposing) return;

			if (InvokeRequired)
				BeginInvoke(new Action(() => ExitWaitListHandler_Invoke(ea)));
			else
				ExitWaitListHandler_Invoke(ea);
		}

		void ExitWaitListHandler_Invoke(ProcessModificationEventArgs ea)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			try
			{
				bool fgonly = ea.Info.Controller.Foreground != ForegroundMode.Ignore;
				bool fg = (ea.Info.Id == (activeappmonitor?.ForegroundId ?? ea.Info.Id));

				ListViewItem li = null;
				string text = fgonly ? (fg ? HumanReadable.System.Process.Foreground : HumanReadable.System.Process.Background) : "ACTIVE";

				if (ExitWaitlistMap?.TryGetValue(ea.Info.Id, out li) ?? false)
				{
					li.SubItems[2].Text = text;

					if (Trace && DebugForeground) Log.Debug($"WaitlistHandler: {ea.Info.Name} = {ea.Info.State.ToString()}");

					switch (ea.Info.State)
					{
						case ProcessHandlingState.Paused:
							break;
						case ProcessHandlingState.Resumed:
							// move item to top
							//exitwaitlist.Items.Remove(li);
							//exitwaitlist.Items.Insert(0, li);
							//li.EnsureVisible();
							break;
						case ProcessHandlingState.Exited:
							ExitWaitList?.Items.Remove(li);
							ExitWaitlistMap?.TryRemove(ea.Info.Id, out _);
							break;
						default:
							break;
					}
				}
				else
				{
					li = new ListViewItem(new string[] {
							ea.Info.Id.ToString(),
							ea.Info.Name,
							text,
							(ea.Info.PowerWait ? "FORCED" : HumanReadable.Generic.NotAvailable)
						});

					ExitWaitlistMap?.TryAdd(ea.Info.Id, li);
					ExitWaitList?.Items.Insert(0, li);
					li.EnsureVisible();
				}
			}
			catch (Exception ex) { Logging.Stacktrace(ex); }
		}

		// Called by UI update timer, should be UI thread by default
		void UpdateMemoryStats(object _, EventArgs _ea)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;
			if (!ramload.Visible) return;

			Memory.Update(); // TODO: this is kinda dumb way to do things
			double freegb = (double)Memory.FreeBytes / 1_073_741_824d;
			double totalgb = (double)Memory.Total / 1_073_741_824d;
			double usage = 1 - (freegb / totalgb);
			//ramload.Text = $"{freegb:N2} of {totalgb:N1} GiB free ({usage * 100d:N1} % usage), {MemoryManager.Pressure * 100:N1} % pressure";
			ramload.Text = $"{freegb:N2} GiB free ({usage * 100d:N1} % usage), {Memory.Pressure * 100:N1} % pressure";

			// TODO: Print warning if MemoryManager.Pressure > 100%

			//vramload.Text = $"{healthmonitor.VRAM()/1_048_576:N0} MB"; // this returns total, not free or used
		}

		// called by cpumonitor, not in UI thread by default
		// TODO: Reverse this design, make the UI poll instead
		void CPULoadHandler(object _, ProcessorLoadEventArgs ea)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;
			if (!cpuload.Visible) return;

			if (InvokeRequired)
				BeginInvoke(new Action(() => CPULoadHandler_Invoke(ea)));
			else
				CPULoadHandler_Invoke(ea);
		}

		void CPULoadHandler_Invoke(ProcessorLoadEventArgs ea)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			var load = ea.Load;

			cpuload.Text = $"{load.Current:N1} %, Low: {load.Low:N1} %, Mean: {load.Mean:N1} %, High: {load.High:N1} %; Queue: {load.Queue:N0}";
			// 50 %, Low: 33.2 %, Mean: 52.1 %, High: 72.8 %, Queue: 1
		}

		readonly System.Drawing.Color Reddish = System.Drawing.Color.FromArgb(255, 230, 230);
		readonly System.Drawing.Color Greenish = System.Drawing.Color.FromArgb(240, 255, 230);
		readonly System.Drawing.Color Orangeish = System.Drawing.Color.FromArgb(255, 250, 230);

		public void PowerLoadDebugHandler(object _, Power.AutoAdjustReactionEventArgs ea)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			if (InvokeRequired)
				BeginInvoke(new Action(() => PowerLoadDebugHandler_Invoke(ea)));
			else
				PowerLoadDebugHandler_Invoke(ea);
		}

		void PowerLoadDebugHandler_Invoke(Power.AutoAdjustReactionEventArgs ea)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			try
			{
				var load = ea.Load;

				var li = new ListViewItem(new string[] {
					$"{load.Current:N2} %",
					$"{load.Mean:N2} %",
					$"{load.High:N2} %",
					$"{load.Low:N2} %",
					ea.Reaction.ToString(),
					Power.Utility.GetModeName(ea.Mode),
					ea.Enacted.ToString(),
					$"{ea.Pressure * 100f:N1} %"
				})
				{
					UseItemStyleForSubItems = false
				};

				if (ea.Enacted)
				{
					li.SubItems[4].BackColor =
						li.SubItems[5].BackColor =
						li.SubItems[6].BackColor = System.Drawing.SystemColors.ActiveCaption;
				}

				if (ea.Mode == Power.Mode.HighPerformance)
					li.SubItems[3].BackColor = Reddish;
				else if (ea.Mode == Power.Mode.PowerSaver)
					li.SubItems[2].BackColor = Greenish;
				else
					li.SubItems[3].BackColor = li.SubItems[2].BackColor = Orangeish;

				// this tends to throw if this event is being handled while the window is being closed
				if (powerbalancerlog.Items.Count > 7)
					powerbalancerlog.Items.RemoveAt(0);
				powerbalancerlog.Items.Add(li);

				powerbalancer_forcedcount.Text = powermanager.ForceCount.ToString();
			}
			catch (Exception ex) { Logging.Stacktrace(ex); }
		}

		void WatchlistContextMenuOpen(object _, EventArgs _ea)
		{
			bool oneitem = (WatchlistRules.SelectedItems.Count == 1);

			try
			{
				foreach (ToolStripItem lsi in watchlistms.Items)
				{
					if (lsi.Text.Contains("Create")) continue;
					lsi.Enabled = oneitem;
				}

				if (oneitem)
				{
					if (processmanager.GetControllerByName(WatchlistRules.SelectedItems[0].SubItems[NameColumn].Text, out var prc))
					{
						watchlistenable.Enabled = true;
						watchlistenable.Checked = prc.Enabled;
					}
				}
				else
					watchlistenable.Enabled = false;
			}
			catch (Exception ex) { Logging.Stacktrace(ex); }
		}

		void EnableWatchlistRule(object _, EventArgs _ea)
		{
			try
			{
				if (WatchlistRules.SelectedItems.Count == 1)
				{
					var li = WatchlistRules.SelectedItems[0];
					if (processmanager.GetControllerByName(li.SubItems[NameColumn].Text, out var prc))
					{
						watchlistenable.Enabled = true;
						watchlistenable.Checked = prc.Enabled = !watchlistenable.Checked;

						Log.Information("[" + prc.FriendlyName + "] " + (prc.Enabled ? HumanReadable.Generic.Enabled : HumanReadable.Generic.Disabled));

						prc.SaveConfig();

						prc.Refresh();

						WatchlistItemColor(li, prc);

						processmanager?.HastenScan(20);
					}
				}
				else
					watchlistenable.Enabled = false;
			}
			catch (Exception ex) { Logging.Stacktrace(ex); }
		}

		void EditWatchlistRule(object _, EventArgs _ea)
		{
			if (WatchlistRules.SelectedItems.Count == 1)
			{
				try
				{
					processmanager.GetControllerByName(WatchlistRules.SelectedItems[0].SubItems[NameColumn].Text, out var prc);

					using var editdialog = new Config.WatchlistEditWindow(prc); // 1 = executable
					if (editdialog.ShowDialog() == DialogResult.OK)
					{
						UpdateWatchlistRule(prc);
						processmanager?.HastenScan(60, forceSort: true);
						prc.Refresh();
					}
				}
				catch (Exception ex) { Logging.Stacktrace(ex); }
			}
		}

		void AddWatchlistRule(object _, EventArgs _ea)
		{
			try
			{
				var ew = new Config.WatchlistEditWindow();
				var rv = ew.ShowDialog();
				if (rv == DialogResult.OK)
				{
					var prc = ew.Controller;

					if (processmanager.AddController(prc))
					{
						AddToWatchlistList(prc);

						processmanager?.HastenScan(60, forceSort: true);
					}
					else
						prc.Dispose();
				}
			}
			catch (Exception ex) { Logging.Stacktrace(ex); }
		}

		void DeleteWatchlistRule(object _, EventArgs _ea)
		{
			if (WatchlistRules.SelectedItems.Count == 1)
			{
				try
				{
					var li = WatchlistRules.SelectedItems[0];

					if (processmanager.GetControllerByName(li.SubItems[NameColumn].Text, out var prc))
					{
						if (MessageBox.ShowModal("Remove watchlist item", $"Really remove '{prc.FriendlyName}'", MessageBox.Buttons.AcceptCancel, parent: this)
							== MessageBox.ResultType.OK)
						{
							processmanager.RemoveController(prc);
							prc.DeleteConfig();
							Log.Information("[" + prc.FriendlyName + "] Rule removed");

							lock (watchlist_lock)
							{
								WatchlistMap.TryRemove(prc, out ListViewItem _);
								WatchlistRules.Items.Remove(li);
							}
						}
					}
				}
				catch (Exception ex) { Logging.Stacktrace(ex); }

				WatchlistColor(); // not necessary if the item removed was the last. Sadly not significant enough to give special case.
			}
		}

		// This should be somewhere else
		void CopyRuleToClipboard(object _, EventArgs _ea)
		{
			if (WatchlistRules.SelectedItems.Count == 1)
			{
				try
				{
					var li = WatchlistRules.SelectedItems[0];
					var name = li.SubItems[NameColumn].Text;

					if (!processmanager.GetControllerByName(name, out Process.Controller prc))
					{
						Log.Error("[" + name + "] Not found. Something's terribly wrong.");
						return;
					}

					var sbs = new StringBuilder().Append("[").Append(prc.FriendlyName).AppendLine("]");

					if (prc.Executables?.Length > 0) sbs.Append("Executables = { ").Append(string.Join(", ", prc.Executables)).AppendLine(" }");
					if (!string.IsNullOrEmpty(prc.Path)) sbs.Append("Path = ").AppendLine(prc.Path);
					if (!string.IsNullOrEmpty(prc.Description)) sbs.Append("Description = ").Append(prc.Description);
					if (prc.IgnoreList != null) sbs.Append("Ignore = { ").Append(string.Join(", ", prc.IgnoreList)).AppendLine(" }");

					if (prc.Priority.HasValue)
					{
						sbs.Append(HumanReadable.System.Process.Priority).Append(" = ").Append(prc.Priority.Value.ToInt32()).AppendLine();
						sbs.Append(HumanReadable.System.Process.PriorityStrategy).Append(" = ").Append((int)prc.PriorityStrategy).AppendLine();
					}
					if (prc.AffinityMask >= 0)
					{
						sbs.Append(HumanReadable.System.Process.Affinity).Append(" = ").Append(prc.AffinityMask).AppendLine();
						sbs.Append(HumanReadable.System.Process.AffinityStrategy).Append(" = ").Append((int)prc.AffinityStrategy).AppendLine();
					}

					if (prc.AffinityIdeal >= 0) sbs.Append("Affinity ideal = ").Append(prc.AffinityIdeal).AppendLine();

					if (prc.IOPriority != Process.IOPriority.Ignore) sbs.Append("IO priority = ").Append((int)prc.IOPriority).AppendLine();

					if (prc.PowerPlan != Power.Mode.Undefined)
						sbs.Append(HumanReadable.Hardware.Power.Plan).Append(" = ").AppendLine(Power.Utility.GetModeName(prc.PowerPlan));
					if (prc.Recheck > 0) sbs.Append("Recheck = ").Append(prc.Recheck).AppendLine();
					if (prc.AllowPaging) sbs.Append("Allow paging = ").Append(prc.AllowPaging).AppendLine();

					if (prc.Foreground != ForegroundMode.Ignore)
					{
						sbs.Append("Foreground mode = ").Append((int)prc.Foreground).AppendLine();
						if (prc.BackgroundPriority.HasValue)
							sbs.Append("Background priority = ").Append(prc.BackgroundPriority.Value.ToInt32()).AppendLine();
						if (prc.BackgroundAffinity >= 0)
							sbs.Append("Background affinity = ").Append(prc.BackgroundAffinity).AppendLine();
					}

					if (prc.ModifyDelay > 0) sbs.Append("Modify delay = ").Append(prc.ModifyDelay).AppendLine();

					if (prc.PathVisibility != Process.PathVisibilityOptions.Invalid)
						sbs.Append("Path visibility = ").Append((int)prc.PathVisibility).AppendLine();

					if (prc.VolumeStrategy != Audio.VolumeStrategy.Ignore)
					{
						sbs.Append("Volume = ").AppendFormat("{0:N2}", prc.Volume).AppendLine();
						sbs.Append("Volume strategy = ").Append((int)prc.VolumeStrategy).AppendLine();
					}

					sbs.Append("Preference = ").Append(prc.OrderPreference).AppendLine();

					if (!prc.LogAdjusts) sbs.AppendLine("Logging = false");
					if (prc.LogStartAndExit) sbs.AppendLine("Log start and exit = true");
					if (!prc.Enabled) sbs.AppendLine("Enabled = false");

					// TODO: Add Resize and Modify Delay

					try
					{
						Clipboard.SetText(sbs.ToString(), TextDataFormat.UnicodeText);
						Log.Information("[" + name + "] Configuration saved to clipboard.");
					}
					catch (OutOfMemoryException) { throw; }
					catch
					{
						Log.Warning("[" + name + "] Failed to copy configuration to clipboard.");
					}
				}
				catch (Exception ex) { Logging.Stacktrace(ex); }
			}
		}

		Label tempObjectCount = null;
		Label tempObjectSize = null;

		Label cpuload = null;
		Label ramload = null;

		Label pwmode = null;
		Label pwcause = null;
		Label pwbehaviour = null;

		Label nvmtransfers = null;
		Label nvmsplitio = null;
		Label nvmdelay = null;
		Label nvmqueued = null;
		Label hardfaults = null;

		Label gpuvram = null;
		Label gpuload = null;
		Label gputemp = null;
		Label gpufan = null;

		public void TempScanStats(object _, StorageEventArgs ea)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			if (InvokeRequired)
				BeginInvoke(new Action(() => TempScanStats_Invoke(ea)));
			else
				TempScanStats_Invoke(ea);
		}

		void TempScanStats_Invoke(StorageEventArgs ea)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			tempObjectSize.Text = (ea.Stats.Size / 1_000_000).ToString();
			tempObjectCount.Text = (ea.Stats.Dirs + ea.Stats.Files).ToString();
		}

		ListView LogList = null;
		MenuStrip menu = null;

		public void FillLog()
		{
			MemoryLog.MemorySink.onNewEvent += NewLogReceived;

			// Log.Verbose("Filling GUI log.");
			foreach (var evmsg in MemoryLog.MemorySink.ToArray())
				AddLog(evmsg);

			ShowLastLog();

			ResizeLogList(this, EventArgs.Empty);
		}

		public void Hook(Process.ForegroundManager manager)
		{
			if (manager is null) return;

			if (Trace) Log.Verbose("Hooking active app manager.");

			activeappmonitor = manager;
			activeappmonitor.OnDisposed += (_, _ea) => activeappmonitor = null;

			if (DebugForeground || Process.Manager.DebugProcesses)
				StartProcessDebug();
		}

		public void Hook(Power.Manager manager)
		{
			if (manager is null) return;

			if (Trace) Log.Verbose("Hooking power manager.");

			powermanager = manager;
			powermanager.OnDisposed += (_, _ea) => powermanager = null;

			powermanager.BehaviourChange += PowerBehaviourEvent;
			powermanager.PlanChange += PowerPlanEvent;

			var bev = new Power.PowerBehaviourEventArgs(powermanager.Behaviour);
			PowerBehaviourEvent(this, bev); // populates pwbehaviour
			var pev = new Power.ModeEventArgs(powermanager.CurrentMode);
			PowerPlanEvent(this, pev); // populates pwplan and pwcause

			if (DebugPower)
			{
				PowerBehaviourDebugEvent(this, bev); // populates powerbalancer_behaviour
				PowerPlanDebugEvent(this, pev); // populates powerbalancer_plan
				powermanager.PlanChange += PowerPlanDebugEvent;
				powermanager.BehaviourChange += PowerBehaviourDebugEvent;
				powermanager.AutoAdjustAttempt += PowerLoadDebugHandler;
			}

			power_auto.Enabled = true;
			UpdatePowerBehaviourHighlight(powermanager.Behaviour);
			HighlightPowerMode();
		}

		void UpdatePowerBehaviourHighlight(Power.PowerBehaviour behaviour)
		{
			switch (behaviour)
			{
				case Power.PowerBehaviour.Manual:
					power_auto.Checked = false;
					power_manual.Checked = true;
					break;
				case Power.PowerBehaviour.Auto:
					power_auto.Checked = true;
					power_manual.Checked = false;
					break;
				default:
					power_auto.Checked = false;
					power_manual.Checked = false;
					break;
			}
		}

		void PowerBehaviourEvent(object sender, Power.PowerBehaviourEventArgs e)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			if (InvokeRequired)
				BeginInvoke(new Action(() => PowerBehaviourEvent_Invoke(e)));
			else
				PowerBehaviourEvent_Invoke(e);
		}

		void PowerBehaviourEvent_Invoke(Power.PowerBehaviourEventArgs e)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			UpdatePowerBehaviourHighlight(e.Behaviour);
			pwbehaviour.Text = Power.Utility.GetBehaviourName(e.Behaviour);
		}

		DateTimeOffset LastCauseTime = DateTimeOffset.MinValue;

		void PowerPlanEvent(object sender, Power.ModeEventArgs e)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			if (InvokeRequired)
				BeginInvoke(new Action(() => PowerPlanEvent_Invoke(e)));
			else
				PowerPlanEvent_Invoke(e);
		}

		void PowerPlanEvent_Invoke(Power.ModeEventArgs e)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			HighlightPowerMode();

			powermodestatusbar.Text = pwmode.Text = Power.Utility.GetModeName(e.NewMode);
			pwcause.Text = e.Cause != null ? e.Cause.ToString() : HumanReadable.Generic.Undefined;
			LastCauseTime = DateTimeOffset.UtcNow;
		}

		HardwareMonitor hardwaremonitor = null;

		public void Hook(HardwareMonitor monitor)
		{
			hardwaremonitor = monitor;
			hardwaremonitor.OnDisposed += (_, _ea) => hardwaremonitor = null;

			//hw.GPUPolling += GPULoadEvent;
			UItimer.Tick += GPULoadPoller;

			GPULoadPoller(this, EventArgs.Empty);
		}

		public void Hook(CPUMonitor monitor)
		{
			cpumonitor = monitor;
			cpumonitor.Sampling += CPULoadHandler;
			cpumonitor.OnDisposed += (_, _ea) => cpumonitor = null;

			CPULoadHandler(this, new ProcessorLoadEventArgs() { Load = cpumonitor.GetLoad });
		}

		HealthMonitor healthmonitor = null;

		public void Hook(HealthMonitor monitor)
		{
			healthmonitor = monitor;
			healthmonitor.OnDisposed += (_, _ea) => healthmonitor = null;

			oldHealthReport = healthmonitor.Poll;

			UpdateMemoryStats(this, EventArgs.Empty);
			UItimer.Tick += UpdateHealthMon;
			GotFocus += UpdateHealthMon;

			UpdateHealthMon(this, EventArgs.Empty);
		}

		HealthReport oldHealthReport = null;

		int skipTransfers = 0;
		int skipSplits = 0;
		int skipDelays = 0;
		int skipQueues = 0;

		int updatehealthmon_lock = 0;

		async void UpdateHealthMon(object sender, EventArgs e)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;
			if (healthmonitor is null) return;
			if (!nvmtransfers.Visible) return;
			if (powermanager?.SessionLocked ?? false) return;

			if (!Atomic.Lock(ref updatehealthmon_lock)) return;

			await Task.Delay(100).ConfigureAwait(true);

			try
			{
				var health = healthmonitor.Poll;

				float impact_transfers = (health.NVMTransfers / 500).Max(3); // expected to cause 0 to 2, and up to 4
				float impact_splits = health.SplitIO / 125; // expected to cause 0 to 2
				float impact_delay = health.NVMDelay / 12; // should cause 0 to 4 usually
				float impact_queue = (health.NVMQueue / 2).Max(4);
				float impact = impact_transfers + impact_splits + impact_delay + impact_queue;
				//float impact_faults = health.PageFaults;

				if (health.NVMTransfers >= float.Epsilon)
				{
					nvmtransfers.Text = $"{health.NVMTransfers:N1} {WarningLevelString((int)health.NVMTransfers, 200, 320, 480)}";
					nvmtransfers.ForeColor = System.Drawing.SystemColors.WindowText;
					skipTransfers = 0;
				}
				else
				{
					if (skipTransfers++ == 0)
						nvmtransfers.ForeColor = System.Drawing.SystemColors.GrayText;
					else
						nvmtransfers.Text = "0.0";
				}

				if (health.SplitIO >= float.Epsilon)
				{
					nvmsplitio.Text = $"{health.SplitIO:N1} {WarningLevelString((int)health.SplitIO, 20, 80, Math.Max(120, (int)(health.NVMTransfers * 0.5)))}";
					nvmsplitio.ForeColor = System.Drawing.SystemColors.WindowText;
					skipSplits = 0;
				}
				else
				{
					if (skipSplits++ == 0)
						nvmsplitio.ForeColor = System.Drawing.SystemColors.GrayText;
					else
						nvmsplitio.Text = "0.0";
				}

				if (health.NVMDelay >= float.Epsilon)
				{
					float delay = health.NVMDelay * 1000;
					nvmdelay.Text = $"{delay:N1} ms {WarningLevelString((int)delay, 22, 52, 70)}";
					nvmdelay.ForeColor = System.Drawing.SystemColors.WindowText;
					skipDelays = 0;
				}
				else
				{
					if (skipDelays++ == 0)
						nvmdelay.ForeColor = System.Drawing.SystemColors.GrayText;
					else
						nvmdelay.Text = "0 ms";
				}

				if (health.NVMQueue >= float.Epsilon)
				{
					nvmqueued.Text = $"{health.NVMQueue:N0} {WarningLevelString((int)health.NVMQueue, 2, 8, 22)}";
					nvmqueued.ForeColor = System.Drawing.SystemColors.WindowText;
					skipQueues = 0;
				}
				else
				{
					if (skipQueues++ == 0)
						nvmqueued.ForeColor = System.Drawing.SystemColors.GrayText;
					else
						nvmqueued.Text = "0";
				}

				//hardfaults.Text = !float.IsNaN(health.PageInputs) ? $"{health.PageInputs / health.PageFaults:N1} %" : HumanReadable.Generic.NotAvailable;

				oldHealthReport = health;
			}
			catch (OutOfMemoryException) { throw; }
			catch (ObjectDisposedException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				Atomic.Unlock(ref updatehealthmon_lock);
			}
		}

		string WarningLevelString(int value, int high, int vhigh, int extreme)
		{
			if (value >= extreme)
				return "extreme";
			else if (value >= vhigh)
				return "very high";
			else if (value >= high)
				return "high";
			else
				return string.Empty;
		}

		public void PowerBehaviourDebugEvent(object _, Power.PowerBehaviourEventArgs ea)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			if (!DebugPower) return;

			if (InvokeRequired)
				BeginInvoke(new Action(() => PowerBehaviourDebugEvent_Invoke(ea)));
			else
				PowerBehaviourDebugEvent_Invoke(ea);
		}

		void PowerBehaviourDebugEvent_Invoke(Power.PowerBehaviourEventArgs ea)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			powerbalancer_behaviour.Text = Power.Utility.GetBehaviourName(ea.Behaviour);
			if (ea.Behaviour != Power.PowerBehaviour.Auto)
				powerbalancerlog.Items.Clear();
		}

		public void PowerPlanDebugEvent(object _, Power.ModeEventArgs ea)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			if (!DebugPower) return;

			if (InvokeRequired)
				BeginInvoke(new Action(() => PowerPlanDebugEvent_Invoke(ea)));
			else
				PowerPlanDebugEvent_Invoke(ea);
		}

		void PowerPlanDebugEvent_Invoke(Power.ModeEventArgs ea)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			powerbalancer_plan.Text = Power.Utility.GetModeName(ea.NewMode);
		}

		public void UpdateNetworkDevices(object _, EventArgs _ea)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			if (InvokeRequired)
				BeginInvoke(new Action(UpdateNetworkDevices_Invoke));
			else
				UpdateNetworkDevices_Invoke();

			// Tray?.Tooltip(2000, "Internet " + (net.InternetAvailable ? "available" : "unavailable"), "Taskmaster", net.InternetAvailable ? ToolTipIcon.Info : ToolTipIcon.Warning);
		}

		void UpdateNetworkDevices_Invoke()
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			InetStatusLabel(netmonitor.InternetAvailable);
			NetStatusLabelUpdate(netmonitor.NetworkAvailable);

			NetworkDevices.Items.Clear();

			var niclist = new List<ListViewItem>();

			foreach (var dev in netmonitor.GetInterfaces())
			{
				var li = new ListViewItem(new string[] {
					dev.Name,
					dev.Type.ToString(),
					dev.Status.ToString(),
					HumanInterface.ByteString(dev.Speed),
					dev.IPv4Address?.ToString() ?? HumanReadable.Generic.NotAvailable,
					dev.IPv6Address?.ToString() ?? HumanReadable.Generic.NotAvailable,
					HumanReadable.Generic.NotAvailable, // traffic delta
					HumanReadable.Generic.NotAvailable, // error delta
					HumanReadable.Generic.NotAvailable, // total errors
				})
				{
					UseItemStyleForSubItems = false
				};

				niclist.Add(li);
			}

			NetworkDevices.Items.AddRange(niclist.ToArray());

			AlternateListviewRowColors(NetworkDevices, AlternateRowColorsDevices);
		}

		public void Hook(Network.Manager manager)
		{
			if (manager is null) return; // disabled

			if (Trace) Log.Verbose("Hooking network monitor.");

			netmonitor = manager;
			netmonitor.OnDisposed += (_, _ea) => netmonitor = null;

			UpdateNetworkDevices(this, EventArgs.Empty);

			netmonitor.InternetStatusChange += InetStatusChangeEvent;
			netmonitor.IPChanged += UpdateNetworkDevices;
			netmonitor.NetworkStatusChange += NetStatusChangeEvent;
			netmonitor.DeviceSampling += NetSampleHandler;

			NetSampleHandler(this, new Network.DeviceTrafficEventArgs() { Traffic = new Network.DeviceTraffic(netmonitor.GetCurrentTraffic) });
			NetStatusChangeEvent(this, new Network.Status() { Available = netmonitor.NetworkAvailable });

			UItimer.Tick += UpdateNetwork;
			GotFocus += UpdateNetwork;

			UpdateNetwork(this, EventArgs.Empty);
		}

		void NetSampleHandler(object _, Network.DeviceTrafficEventArgs ea)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			if (InvokeRequired)
				BeginInvoke(new Action(() => NetSampleHandler_Invoke(ea)));
			else
				NetSampleHandler_Invoke(ea);
		}

		void NetSampleHandler_Invoke(Network.DeviceTrafficEventArgs ea)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			try
			{
				var item = NetworkDevices.Items[ea.Traffic.Index];
				item.SubItems[PacketDeltaColumn].Text = "+" + ea.Traffic.Delta.Unicast;
				item.SubItems[ErrorDeltaColumn].Text = "+" + ea.Traffic.Delta.Errors;
				if (ea.Traffic.Delta.Errors > 0)
					item.SubItems[ErrorDeltaColumn].ForeColor = System.Drawing.Color.OrangeRed;
				else
					item.SubItems[ErrorDeltaColumn].ForeColor = System.Drawing.SystemColors.ControlText;

				item.SubItems[ErrorTotalColumn].Text = ea.Traffic.Total.Errors.ToString();
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		int PacketDeltaColumn = 6;
		int ErrorDeltaColumn = 7;
		int ErrorTotalColumn = 8;

		void InetStatusLabel(bool available)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			if (InvokeRequired)
				BeginInvoke(new Action(() => InetStatusLabel_Invoke(available)));
			else
				InetStatusLabel_Invoke(available);
		}

		void InetStatusLabel_Invoke(bool available)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			inetstatuslabel.Text = available ? HumanReadable.Hardware.Network.Connected : HumanReadable.Hardware.Network.Disconnected;
			// inetstatuslabel.BackColor = available ? System.Drawing.Color.LightGoldenrodYellow : System.Drawing.Color.Red;
			//inetstatuslabel.BackColor = available ? System.Drawing.SystemColors.Menu : System.Drawing.Color.Red;
		}

		public void InetStatusChangeEvent(object _, Network.InternetStatus ea)
		{
			InetStatusLabel(ea.Available);
		}

		public void IPChange(object _, EventArgs ea)
		{
			// ??
		}

		void NetStatusLabelUpdate(bool available)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			if (InvokeRequired)
				BeginInvoke(new Action(() => NetStatusLabelUpdate_Invoke(available)));
			else
				NetStatusLabelUpdate_Invoke(available);
		}

		void NetStatusLabelUpdate_Invoke(bool available)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			netstatuslabel.Text = available ? HumanReadable.Hardware.Network.Connected : HumanReadable.Hardware.Network.Disconnected;
			// netstatuslabel.BackColor = available ? System.Drawing.Color.LightGoldenrodYellow : System.Drawing.Color.Red;
			//netstatuslabel.BackColor = available ? System.Drawing.SystemColors.Menu : System.Drawing.Color.Red;
		}

		void NetStatusChangeEvent(object _, Network.Status ea)
		{
			NetStatusLabelUpdate(ea.Available);
		}

		// BUG: DO NOT LOG INSIDE THIS FOR FUCKS SAKE
		// it creates an infinite log loop
		public int MaxLogSize { get => MemoryLog.MemorySink.Max; private set => MemoryLog.MemorySink.Max = value; }

		void ClearLog()
		{
			//loglist.Clear();
			LogList.Items.Clear();
			MemoryLog.MemorySink.Clear();
		}

		void NewLogReceived(object _, LogEventArgs ea)
		{
			if (!IsHandleCreated || DisposedOrDisposing
				|| (LogIncludeLevel.MinimumLevel > ea.Level)) return;

			if (InvokeRequired)
				BeginInvoke(new Action(() => NewLogReceived_Invoke(ea)));
			else
				NewLogReceived_Invoke(ea);
		}

		void NewLogReceived_Invoke(LogEventArgs ea)
		{
			if (!IsHandleCreated || DisposedOrDisposing) return;

			var excessitems = Math.Max(0, (LogList.Items.Count - MaxLogSize));
			while (excessitems-- > 0)
				LogList.Items.RemoveAt(0);

			AddLog(ea);
		}

		bool alterStep = true;

		void AddLog(LogEventArgs ea)
		{
			var msg = new ListViewItem(ea.Message);
			switch (ea.Level)
			{
				case Serilog.Events.LogEventLevel.Verbose:
				case Serilog.Events.LogEventLevel.Information:
					msg.ImageIndex = 0;
					break;
				case Serilog.Events.LogEventLevel.Debug:
				case Serilog.Events.LogEventLevel.Warning:
					msg.ImageIndex = 1;
					break;
				case Serilog.Events.LogEventLevel.Error:
				case Serilog.Events.LogEventLevel.Fatal:
					msg.ImageIndex = 2;
					break;
			}

			LogList.Items.Add(msg);
			//LogList.Columns[0].Width = -2;

			// color errors and worse red
			if ((int)ea.Level >= (int)Serilog.Events.LogEventLevel.Error)
				msg.ForeColor = System.Drawing.Color.Red;

			// alternate back color
			if (AlternateRowColorsLog && (alterStep = !alterStep))
				msg.BackColor = AlterColor;

			msg.EnsureVisible();
		}

		void SaveUIState()
		{
			if (!IsHandleCreated) return;

			try
			{
				if (WatchlistRules.Columns.Count == 0) return;

				using var cfg = Taskmaster.Config.Load(UIConfigFilename);

				var cols = cfg.Config[Constants.Columns];

				var appWidths = new List<int>(WatchlistRules.Columns.Count);
				var apporder = new List<int>(WatchlistRules.Columns.Count);
				for (int i = 0; i < WatchlistRules.Columns.Count; i++)
				{
					appWidths.Add(WatchlistRules.Columns[i].Width);
					apporder.Add(WatchlistRules.Columns[i].DisplayIndex);
				}

				cols[Constants.Apps].IntArray = appWidths.ToArray();
				cols["App order"].IntArray = apporder.ToArray();

				if (NetworkMonitorEnabled)
				{
					var ifaceWidths = new List<int>(NetworkDevices.Columns.Count);
					for (int i = 0; i < NetworkDevices.Columns.Count; i++)
						ifaceWidths.Add(NetworkDevices.Columns[i].Width);
					cols["Interfaces"].IntArray = ifaceWidths.ToArray();
				}

				if (MicrophoneManagerEnabled)
				{
					var micWidths = new List<int>(AudioInputs.Columns.Count);
					for (int i = 0; i < AudioInputs.Columns.Count; i++)
						micWidths.Add(AudioInputs.Columns[i].Width);
					cols["Mics"].IntArray = micWidths.ToArray();
				}

				var uistate = cfg.Config[Constants.Tabs];
				uistate["Open"].Int = tabLayout.SelectedIndex;

				var windows = cfg.Config[Constants.Windows];
				windows["Main"].IntArray = new int[] { Bounds.Left, Bounds.Top, Bounds.Width, Bounds.Height };
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
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

				if (Trace) Log.Verbose("Disposing main window...");

				if (MemoryLog.MemorySink != null)
					MemoryLog.MemorySink.onNewEvent -= NewLogReceived; // unnecessary?

				rescanRequest = null;

				try
				{
					if (powermanager != null)
					{
						powermanager.BehaviourChange -= PowerBehaviourEvent;
						powermanager.PlanChange -= PowerPlanEvent;

						powermanager.AutoAdjustAttempt -= PowerLoadDebugHandler;

						powermanager.BehaviourChange -= PowerBehaviourDebugEvent;
						powermanager.PlanChange -= PowerPlanDebugEvent;

						powermanager = null;
					}
				}
				catch { }

				try
				{
					if (cpumonitor != null)
					{
						cpumonitor.Sampling -= CPULoadHandler;
						cpumonitor = null;
					}
				}
				catch { }

				try
				{
					if (hardwaremonitor != null)
					{
						hardwaremonitor.GPUPolling -= GPULoadEvent;
						hardwaremonitor = null;
					}
				}
				catch { }

				try
				{
					if (activeappmonitor != null)
					{
						activeappmonitor.ActiveChanged -= OnActiveWindowChanged;
						activeappmonitor = null;
					}
				}
				catch { }

				try
				{
					if (storagemanager != null)
					{
						storagemanager.TempScan -= TempScanStats;
						storagemanager = null;
					}
				}
				catch { }

				try
				{
					if (processmanager != null)
					{
						processmanager.ProcessModified -= ProcessTouchEvent;
						processmanager.HandlingCounter -= ProcessNewInstanceCount;
						processmanager.ProcessStateChange -= ExitWaitListHandler;
						processmanager.HandlingStateChange -= ProcessHandlingStateChangeEvent;
						processmanager = null;
					}
				}
				catch { }

				try
				{
					if (netmonitor != null)
					{
						netmonitor.InternetStatusChange -= InetStatusChangeEvent;
						netmonitor.NetworkStatusChange -= NetStatusChangeEvent;
						netmonitor.IPChanged -= UpdateNetworkDevices;
						netmonitor.DeviceSampling -= NetSampleHandler;
						netmonitor = null;
					}
				}
				catch { }

				try
				{
					if (micmanager != null)
					{
						micmanager.VolumeChanged -= VolumeChangeDetected;
						micmanager = null;
					}
				}
				catch { }

				WatchlistSearchTimer.Dispose();
				UItimer.Dispose();
				ExitWaitList?.Dispose();
				ExitWaitlistMap?.Clear();
			}
		}
		#endregion Dispose
		public void ShutdownEvent(object sender, EventArgs ea)
		{
			UItimer?.Stop();
		}
	}
}