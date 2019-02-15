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
using Serilog;
using Taskmaster.Events;

namespace Taskmaster
{
	// public class MainWindow : System.Windows.Window; // TODO: WPF
	sealed public class MainWindow : UI.UniForm
	{
		ToolTip tooltip = new ToolTip();

		// constructor
		public MainWindow()
		{
			// InitializeComponent(); // TODO: WPF
			FormClosing += WindowClose;

			BuildUI();

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

			MinimumHeight += tabLayout.MinimumSize.Height;
			MinimumHeight += loglist.MinimumSize.Height;
			MinimumHeight += menu.Height;
			MinimumHeight += statusbar.Height;
			MinimumHeight += 40; // why is this required? window deco?

			MinimumSize = new System.Drawing.Size(780, MinimumHeight);

			ShowInTaskbar = Taskmaster.ShowInTaskbar;

			// FormBorderStyle = FormBorderStyle.FixedDialog; // no min/max buttons as wanted

			if (!Taskmaster.ShowOnStart)
				Hide();

			// CenterToScreen();

			Shown += onShown;

			// TODO: WPF
			/*
			System.Windows.Shell.JumpList jumplist = System.Windows.Shell.JumpList.GetJumpList(System.Windows.Application.Current);
			//System.Windows.Shell.JumpTask task = new System.Windows.Shell.JumpTask();
			System.Windows.Shell.JumpPath jpath = new System.Windows.Shell.JumpPath();
			jpath.Path = Taskmaster.cfgpath;
			jumplist.JumpItems.Add(jpath);
			jumplist.Apply();
			*/

			FillLog();

			if (Taskmaster.Trace)
				Log.Verbose("MainWindow constructed");
		}

		void onShown(object _, EventArgs _ea)
		{
			if (!IsHandleCreated)
				return;

			if (loglist.Items.Count > 0) // needed in case of bugs or clearlog
			{
				loglist.TopItem = loglist.Items[loglist.Items.Count - 1];
				ShowLastLog();
			}
		}

		public void ShowConfigRequest(object _, EventArgs _ea)
		{
			// TODO: Introduce configuration window
		}

		public void AdvancedConfigRequest(object _, EventArgs _ea)
		{
			if (!IsHandleCreated || disposed) return;

			try
			{
				UI.Config.AdvancedConfig.Reveal();
			}
			catch (Exception ex) { Logging.Stacktrace(ex); }
		}

		public void PowerConfigRequest(object _, EventArgs _ea)
		{
			if (!IsHandleCreated || disposed) return;

			try
			{
				PowerConfigWindow.Reveal();
			}
			catch (Exception ex) { Logging.Stacktrace(ex); }
		}

		public void ExitRequest(object _, EventArgs _ea)
		{
			try
			{
				Taskmaster.ConfirmExit(restart: false);
			}
			catch (Exception ex) { Logging.Stacktrace(ex); }
		}

		void WindowClose(object _, FormClosingEventArgs ea)
		{
			try
			{
				SaveUIState();

				if (!Taskmaster.Trace) return;

				Debug.WriteLine("WindowClose = " + ea.CloseReason.ToString());
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
				Debug.WriteLine("WindowClose.Handled");
			}
			catch (Exception ex) { Logging.Stacktrace(ex); }
		}

		// this restores the main window to a place where it can be easily found if it's lost
		/// <summary>
		/// Restores the main window to the center of the screen.
		/// </summary>
		public void UnloseWindowRequest(object _, EventArgs e)
		{
			if (Taskmaster.Trace) Log.Verbose("Making sure main window is not lost.");

			if (!IsHandleCreated || disposed) return;

			CenterToScreen();
			Reveal();
		}

		public void Reveal()
		{
			WindowState = FormWindowState.Normal;
			Show();
			// shuffle to top in the most hackish way possible, these are all unreliable
			BringToFront(); // does nothing without show(), unreliable even with it
			TopMost = true;
			TopMost = false;
			Show();
			Activate();
		}

		public void ShowLastLog()
		{
			if (loglist.Items.Count > 0)
			{
				loglist.EnsureVisible(loglist.Items.Count - 1);
			}
		}

		// HOOKS
		MicManager micmon = null;
		StorageManager storagemanager = null;
		ProcessManager processmanager = null;
		ActiveAppManager activeappmonitor = null;
		PowerManager powermanager = null;
		CPUMonitor cpumonitor = null;
		NetManager netmonitor = null;

		#region Microphone control code

		string AudioInputGUID = string.Empty;

		void SetDefaultCommDevice()
		{
			BeginInvoke(new Action(() =>
			{
				try
				{
					var devname = micmon.DeviceName;

					AudioInputGUID = micmon.DeviceGuid;

					AudioInputDevice.Text = !string.IsNullOrEmpty(devname) ? devname : HumanReadable.Generic.NotAvailable;

					corCountLabel.Text = micmon.Corrections.ToString();

					AudioInputVolume.Maximum = Convert.ToDecimal(MicManager.Maximum);
					AudioInputVolume.Minimum = Convert.ToDecimal(MicManager.Minimum);
					AudioInputVolume.Value = Convert.ToInt32(micmon.Volume);

					AudioInputEnable.SelectedIndex = micmon.Control ? 0 : 1;
				}
				catch (OutOfMemoryException) { throw; }
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
				}
			}));
		}

		void UpdateAudioInputs()
		{
			AudioInputs.BeginUpdate();
			// TODO: mark default device in list
			AudioInputs.Items.Clear();
			foreach (var dev in micmon.DeviceList())
			{
				AudioInputs.Items.Add(new ListViewItem(new string[] {
					dev.Name,
					dev.GUID,
					$"{dev.Volume:N1} %",
					$"{dev.Target:N1} %",
					(dev.VolumeControl ? "Enabled" : "Disabled"),
					dev.State.ToString(),
				}));
			}
			AudioInputs.EndUpdate();
		}

		public void Hook(MicManager micmonitor)
		{
			Debug.Assert(micmonitor != null);
			try
			{
				micmon = micmonitor;

				if (Taskmaster.Trace) Log.Verbose("Hooking microphone monitor.");

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
				micmon.VolumeChanged += VolumeChangeDetected;
				micmon.StateChanged += MicrophoneStateChanged;
				micmon.DefaultChanged += MicrophoneDefaultChanged;

				FormClosing += (_, _ea) => micmon.VolumeChanged -= VolumeChangeDetected;
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				throw;
			}
		}

		private void MicrophoneDefaultChanged(object sender, AudioDefaultDeviceEventArgs ea)
		{
			if (IsDisposed || !IsHandleCreated) return;

			AudioInputGUID = ea.GUID;

			BeginInvoke(new Action(() =>
			{
				if (IsDisposed || !IsHandleCreated) return;

				try
				{
					if (string.IsNullOrEmpty(AudioInputGUID))
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
				catch(Exception ex)
				{
					Logging.Stacktrace(ex);
				}
			}));
		}

		private void MicrophoneStateChanged(object sender, Events.AudioDeviceStateEventArgs ea)
		{
			if (IsDisposed || !IsHandleCreated) return;

			BeginInvoke(new Action(() =>
			{
				if (IsDisposed || !IsHandleCreated) return;

				var li = (from ListViewItem lxi
						  in AudioInputs.Items
						  where lxi.SubItems[1].Text.Equals(ea.GUID, StringComparison.OrdinalIgnoreCase)
						  select lxi)
						  .FirstOrDefault();

				if (li != null)
				{
					li.SubItems[5].Text = ea.State.ToString();
				}
			}));
		}

		void UserMicVol(object _, EventArgs _ea)
		{
			// TODO: Handle volume changes. Not really needed. Give presets?
			// micMonitor.setVolume(micVol.Value);
		}

		void VolumeChangeDetected(object _, VolumeChangedEventArgs ea)
		{
			if (!IsHandleCreated || disposed) return;

			BeginInvoke(new Action(() =>
			{
				AudioInputVolume.Value = Convert.ToInt32(ea.New); // this could throw ArgumentOutOfRangeException, but we trust the source
				corCountLabel.Text = ea.Corrections.ToString();
			}));
		}
		#endregion // Microphone control code

		public async void ProcessTouchEvent(object _, ProcessModificationEventArgs ea)
		{
			if (!IsHandleCreated || disposed) return;

			var prc = ea.Info.Controller; // cache
			BeginInvoke(new Action(() =>
			{
				//adjustcounter.Text = Statistics.TouchCount.ToString();

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

				if (Taskmaster.LastModifiedList)
				{
					lastmodifylist.BeginUpdate();

					try
					{
						var mi = new ListViewItem(new string[] {
							DateTime.Now.ToLongTimeString(),
							ea.Info.Name,
							prc.FriendlyName,
							(ea.PriorityNew.HasValue ? MKAh.Readable.ProcessPriority(ea.PriorityNew.Value) : HumanReadable.Generic.NotAvailable),
							(ea.AffinityNew >= 0 ? HumanInterface.BitMask(ea.AffinityNew, ProcessManager.CPUCount) : HumanReadable.Generic.NotAvailable),
							ea.Info.Path
						});
						lastmodifylist.Items.Add(mi);
						if (lastmodifylist.Items.Count > 5) lastmodifylist.Items.RemoveAt(0);
					}
					catch (Exception ex) { Logging.Stacktrace(ex); }
					finally
					{
						lastmodifylist.EndUpdate();
					}
				}
			}));
		}

		public async void OnActiveWindowChanged(object _, WindowChangedArgs windowchangeev)
		{
			if (!IsHandleCreated || disposed) return;
			if (windowchangeev.Process == null) return;

			BeginInvoke(new Action(() =>
			{
				// int maxlength = 70;
				// string cutstring = e.Title.Substring(0, Math.Min(maxlength, e.Title.Length)) + (e.Title.Length > maxlength ? "..." : "");
				// activeLabel.Text = cutstring;
				activeLabel.Text = windowchangeev.Title;
				activeExec.Text = windowchangeev.Executable;
				activeFullscreen.Text = windowchangeev.Fullscreen.True() ? "Full" : windowchangeev.Fullscreen.False() ? "Window" : "Unknown";
				activePID.Text = windowchangeev.Id.ToString();
			}));
		}

		public event EventHandler rescanRequest;

		public void Hook(StorageManager nvmman)
		{
			storagemanager = nvmman;
			storagemanager.onTempScan += TempScanStats;
		}

		public void Hook(ProcessManager control)
		{
			Debug.Assert(control != null);

			processmanager = control;

			processmanager.HandlingCounter += ProcessNewInstanceCount;
			processmanager.ProcessStateChange += ExitWaitListHandler;
			if (Taskmaster.DebugCache) PathCacheUpdate(null, null);

			ProcessNewInstanceCount(this, new ProcessingCountEventArgs(0, 0));

			BeginInvoke(new Action(() => {
				WatchlistRules.BeginUpdate();

				foreach (var prc in processmanager.getWatchlist())
					AddToWatchlistList(prc);

				WatchlistColor();
				WatchlistRules.EndUpdate();
			}));

			if (control.ScanFrequency.HasValue)
				UItimer.Tick += UpdateRescanCountdown;

			processmanager.WatchlistSorted += UpdateWatchlist;

			rescanRequest += RescanRequestEvent;

			processmanager.ProcessModified += ProcessTouchEvent;

			foreach (var info in processmanager.getExitWaitList())
				ExitWaitListHandler(this, new ProcessModificationEventArgs(info));
		}

		void UpdateWatchlist(object _, EventArgs _ea)
		{
			if (!IsHandleCreated) return;
			if (disposed) return;

			BeginInvoke(new Action(() =>
			{
				if (!IsHandleCreated) return;
				if (disposed) return;

				WatchlistRules.BeginUpdate();

				foreach (var li in WatchlistMap)
				{
					li.Value.SubItems[0].Text = (li.Key.ActualOrder + 1).ToString();
					WatchlistItemColor(li.Value, li.Key);
				}

				// re-sort if user is not interacting?

				WatchlistRules.EndUpdate();
			}));
		}

		void RescanRequestEvent(object _, EventArgs _ea)
		{
			processmanager?.HastenScan(0);
		}

		void RestartRequestEvent(object sender, EventArgs _ea)
		{
			Taskmaster.ConfirmExit(restart: true, admin: sender == menu_action_restartadmin);
		}

		void ProcessNewInstanceCount(object _, ProcessingCountEventArgs e)
		{
			if (!IsHandleCreated || disposed) return;

			BeginInvoke(new Action(() =>
			{
				processingcount.Text = e.Total.ToString();
			}));
		}

		readonly System.Drawing.Color GrayText = System.Drawing.Color.FromArgb(130, 130, 130); // ignores user styles
		readonly System.Drawing.Color AlterColor = System.Drawing.Color.FromArgb(245, 245, 245); // ignores user styles

		readonly System.Drawing.Color DefaultLIBGColor = new ListViewItem().BackColor; // HACK

		/// <summary>
		///
		/// </summary>
		/// <remarks>No locks</remarks>
		void WatchlistItemColor(ListViewItem li, ProcessController prc)
		{
			var alter = (li.Index + 1) % 2 == 0; // every even line

			try
			{
				li.UseItemStyleForSubItems = false;

				foreach (ListViewItem.ListViewSubItem si in li.SubItems)
				{
					if (prc.Enabled)
						si.ForeColor = System.Drawing.SystemColors.WindowText;
					else
						si.ForeColor = GrayText;

					if (alter) si.BackColor = AlterColor;
					else si.BackColor = DefaultLIBGColor;
				}

				alter = !alter;

				if (prc.PriorityStrategy == ProcessPriorityStrategy.None)
					li.SubItems[PrioColumn].ForeColor = GrayText;
				if (string.IsNullOrEmpty(prc.Path))
					li.SubItems[PathColumn].ForeColor = GrayText;
				if (prc.PowerPlan == PowerInfo.PowerMode.Undefined)
					li.SubItems[PowerColumn].ForeColor = GrayText;
				if (prc.AffinityMask < 0)
					li.SubItems[AffColumn].ForeColor = GrayText;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		void WatchlistColor()
		{
			if (Taskmaster.Trace) Debug.WriteLine("COLORING LINES");
			int i = 0;
			foreach (var item in WatchlistMap)
			{
				if (Taskmaster.Trace) Debug.WriteLine($"{i++} --- {item.Value.Index} : {item.Value.Index % 2 == 0} --- {item.Key.FriendlyName}");
				WatchlistItemColor(item.Value, item.Key);
			}
		}

		void AddToWatchlistList(ProcessController prc)
		{
			string aff = string.Empty;
			if (prc.AffinityMask > 0)
			{
				if (Taskmaster.AffinityStyle == 0)
					aff = HumanInterface.BitMask(prc.AffinityMask, ProcessManager.CPUCount);
				else
					aff = prc.AffinityMask.ToString();
			}

			var litem = new ListViewItem(new string[] {
				(prc.ActualOrder+1).ToString(),
				prc.FriendlyName,
				prc.Executable,
				string.Empty,
				aff,
				string.Empty,
				prc.Adjusts.ToString(),
				string.Empty
			});

			WatchlistRules.BeginUpdate();

			WatchlistRules.Items.Add(litem);
			WatchlistMap.TryAdd(prc, litem);

			FormatWatchlist(litem, prc);
			WatchlistItemColor(litem, prc);

			WatchlistRules.EndUpdate();
		}

		void FormatWatchlist(ListViewItem litem, ProcessController prc)
		{
			// 0 = ID
			// 1 = Friendly Name
			// 2 = Executable
			// 3 = Priority
			// 4 = Affinity
			// 5 = Power
			// 6 = Adjusts
			// 7 = Path

			BeginInvoke(new Action(() =>
			{
				WatchlistRules.BeginUpdate();

				litem.SubItems[NameColumn].Text = prc.FriendlyName;
				litem.SubItems[ExeColumn].Text = prc.Executable;
				litem.SubItems[PrioColumn].Text = prc.Priority.HasValue ? MKAh.Readable.ProcessPriority(prc.Priority.Value) : string.Empty;
				string aff = string.Empty;
				if (prc.AffinityMask >= 0)
				{
					if (prc.AffinityMask == ProcessManager.AllCPUsMask || prc.AffinityMask == 0)
						aff = "Full/OS";
					else if (Taskmaster.AffinityStyle == 0)
						aff = HumanInterface.BitMask(prc.AffinityMask, ProcessManager.CPUCount);
					else
						aff = prc.AffinityMask.ToString();
				}
				litem.SubItems[AffColumn].Text = aff;
				litem.SubItems[PowerColumn].Text = (prc.PowerPlan != PowerInfo.PowerMode.Undefined ? PowerManager.GetModeName(prc.PowerPlan) : string.Empty);
				litem.SubItems[PathColumn].Text = (string.IsNullOrEmpty(prc.Path) ? string.Empty : prc.Path);

				WatchlistRules.EndUpdate();
			}));
		}

		public void UpdateWatchlistRule(ProcessController prc)
		{
			if (WatchlistMap.TryGetValue(prc, out ListViewItem litem))
			{
				BeginInvoke(new Action(() =>
				{
					WatchlistRules.BeginUpdate();

					FormatWatchlist(litem, prc);
					WatchlistItemColor(litem, prc);

					WatchlistRules.EndUpdate();
				}));
			}
		}

		Label AudioInputDevice = null;
		Extensions.NumericUpDownEx AudioInputVolume = null;
		ListView AudioInputs = null;
		ListView WatchlistRules = null;

		ConcurrentDictionary<ProcessController, ListViewItem> WatchlistMap = new ConcurrentDictionary<ProcessController, ListViewItem>();

		Label corCountLabel = null;
		ComboBox AudioInputEnable = null;

		ListView lastmodifylist = null;
		ListView powerbalancerlog = null;

		Label powerbalancer_behaviour = null;
		Label powerbalancer_plan = null;
		Label powerbalancer_forcedcount = null;

		ListView exitwaitlist = null;
		ListView processinglist = null;
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

		int PathCacheUpdateSkips = 3;

		public void PathCacheUpdate(object _, EventArgs _ea)
		{
			if (!IsHandleCreated || disposed) return;

			Debug.Assert(Taskmaster.DebugCache);

			if (PathCacheUpdateSkips++ == 4)
				PathCacheUpdateSkips = 0;
			else
				return;

			cacheObjects.Text = Statistics.PathCacheCurrent.ToString();
			var ratio = (Statistics.PathCacheMisses > 0 ? (Statistics.PathCacheHits / Statistics.PathCacheMisses) : 1);
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

		int _uiupdatefrequency = 500;
		public int UIUpdateFrequency
		{
			get => _uiupdatefrequency;
			set
			{
				int freq = value.Constrain(100, 5000);
				_uiupdatefrequency = freq;
				UItimer.Interval = freq;
			}
		}

		readonly System.Windows.Forms.Timer UItimer = new System.Windows.Forms.Timer();

		void StartUIUpdates(object _, EventArgs _ea)
		{
			if (!IsHandleCreated) StopUIUpdates(null, null);
			else if (!UItimer.Enabled) UItimer.Start();
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

			// Rescan Countdown
			if (processmanager.ScanFrequency.HasValue)
				processingtimer.Text = $"{DateTimeOffset.UtcNow.TimeTo(processmanager.NextScan).TotalSeconds:N0}s";
			else
				processingtimer.Text = HumanReadable.Generic.NotAvailable;
		}

		void UpdateNetwork(object _, EventArgs _ea)
		{
			if (!IsHandleCreated) return;
			if (netmonitor == null) return;

			uptimestatuslabel.Text = HumanInterface.TimeString(netmonitor.Uptime);
			var mean = netmonitor.UptimeMean();
			if (double.IsInfinity(mean))
				uptimeMeanLabel.Text = "Infinite";
			else
				uptimeMeanLabel.Text = HumanInterface.TimeString(TimeSpan.FromMinutes(mean));

			var delta = netmonitor.GetTraffic();
			float netTotal = delta.Input + delta.Output;
			netTransmit.Text = $"{delta.Input/1000:N1} kB In, {delta.Output/1000:N1} kB Out [{delta.Queue:N0} queued]";
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
			{
				try
				{
					var li = NetworkDevices.SelectedItems[0];
					var ipv6addr = "[" + li.SubItems[IPv6Column].Text + "]";
					Clipboard.SetText(ipv6addr, TextDataFormat.UnicodeText);
				}
				catch (Exception ex) { Logging.Stacktrace(ex); }
			}
		}

		void CopyIfaceToClipboard(object _, EventArgs _ea)
		{
			if (NetworkDevices.SelectedItems.Count == 1)
			{
				string data = netmonitor.GetDeviceData(NetworkDevices.SelectedItems[0].SubItems[0].Text);
				Clipboard.SetText(data, TextDataFormat.UnicodeText);
			}
		}

		void CopyLogToClipboard(object _, EventArgs _ea)
		{
			try
			{
				var sbs = new System.Text.StringBuilder(256);

				foreach (ListViewItem item in loglist.SelectedItems)
					sbs.Append(item.SubItems[0].Text);

				if (sbs.Length > 0)
				{
					Clipboard.SetText(sbs.ToString(), TextDataFormat.UnicodeText);
				}
			}
			catch (Exception ex) { Logging.Stacktrace(ex); }
		}

		TabControl tabLayout = null;

		// TODO: Easier column access somehow than this?
		//int OrderColumn = 0;
		int NameColumn = 1;
		int ExeColumn = 2;
		int PrioColumn = 3;
		int AffColumn = 4;
		int PowerColumn = 5;
		int AdjustColumn = 6;
		int PathColumn = 7;

		TabPage infoTab = null;
		TabPage watchTab = null;

		TabPage micTab = null;
		TabPage powerDebugTab = null;
		TabPage ProcessDebugTab = null;

		EventHandler ResizeLogList;

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

		void BuildUI()
		{
			Text = $"{Application.ProductName} ({Application.ProductVersion})"
#if DEBUG
				+ " DEBUG"
#endif
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

			loglist = new ListView
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

			menu = new MenuStrip() { Dock = DockStyle.Top, Parent = this };

			BuildStatusbar();

			// LAYOUT ITEM CONFIGURATION

			var menu_action = new ToolStripMenuItem("Actions");
			// Sub Items
			var menu_action_rescan = new ToolStripMenuItem(HumanReadable.System.Process.Rescan, null, RescanRequestEvent)
			{
				Enabled = Taskmaster.ProcessMonitorEnabled,
			};
			var menu_action_memoryfocus = new ToolStripMenuItem("Free memory for...", null, FreeMemoryRequest)
			{
				Enabled = Taskmaster.PagingEnabled,
			};
			menu_action_restart = new ToolStripMenuItem("Restart", null, RestartRequestEvent);
			menu_action_restartadmin = new ToolStripMenuItem("Restart as admin", null, RestartRequestEvent)
			{
				Enabled = !MKAh.System.IsAdministrator()
			};

			var menu_action_exit = new ToolStripMenuItem("Exit", null, ExitRequest);
			menu_action.DropDownItems.Add(menu_action_rescan);
			menu_action.DropDownItems.Add(menu_action_memoryfocus);
			menu_action.DropDownItems.Add(new ToolStripSeparator());
			menu_action.DropDownItems.Add(menu_action_restart);
			menu_action.DropDownItems.Add(menu_action_restartadmin);
			menu_action.DropDownItems.Add(menu_action_exit);

			// CONFIG menu item
			var menu_config = new ToolStripMenuItem("Configuration");
			// Sub Items
			var menu_config_behaviour = new ToolStripMenuItem("Behaviour");
			var menu_config_logging = new ToolStripMenuItem("Logging");
			var menu_config_bitmaskstyle = new ToolStripMenuItem("Bitmask style");
			//var menu_config_power = new ToolStripMenuItem("Power");// this submenu is no longer used

			// Sub Sub Items
			var menu_config_behaviour_autoopen = new ToolStripMenuItem("Auto-open menus")
			{
				Checked = Taskmaster.AutoOpenMenus,
				CheckOnClick = true,
			};
			menu_config_behaviour_autoopen.Click += (_, _ea) =>
			{
				Taskmaster.AutoOpenMenus = menu_config_behaviour_autoopen.Checked;

				var corecfg = Taskmaster.Config.Load(Taskmaster.coreconfig);
				corecfg.Config[HumanReadable.Generic.QualityOfLife]["Auto-open menus"].BoolValue = Taskmaster.AutoOpenMenus;
				corecfg.MarkDirty();
			};

			var menu_config_behaviour_taskbar = new ToolStripMenuItem("Show in taskbar")
			{
				Checked = Taskmaster.ShowInTaskbar,
				CheckOnClick = true,
			};
			menu_config_behaviour_taskbar.Click += (_, _ea) =>
			{
				Taskmaster.ShowInTaskbar = ShowInTaskbar = menu_config_behaviour_taskbar.Checked;

				var corecfg = Taskmaster.Config.Load(Taskmaster.coreconfig);
				corecfg.Config[HumanReadable.Generic.QualityOfLife]["Show in taskbar"].BoolValue = Taskmaster.ShowInTaskbar;
				corecfg.MarkDirty();
			};

			var menu_config_behaviour_exitconfirm = new ToolStripMenuItem("Exit confirmation")
			{
				Checked = Taskmaster.ExitConfirmation,
				CheckOnClick = true,
			};
			menu_config_behaviour_exitconfirm.Click += (_, _ea) =>
			{
				Taskmaster.ExitConfirmation = menu_config_behaviour_exitconfirm.Checked;

				var corecfg = Taskmaster.Config.Load(Taskmaster.coreconfig);
				corecfg.Config[HumanReadable.Generic.QualityOfLife]["Exit confirmation"].BoolValue = Taskmaster.ExitConfirmation;
				corecfg.MarkDirty();
			};

			menu_config_behaviour.DropDownItems.Add(menu_config_behaviour_autoopen);
			menu_config_behaviour.DropDownItems.Add(menu_config_behaviour_taskbar);
			menu_config_behaviour.DropDownItems.Add(menu_config_behaviour_exitconfirm);

			var menu_config_logging_adjusts = new ToolStripMenuItem("Process adjusts")
			{
				Checked = Taskmaster.ShowProcessAdjusts,
				CheckOnClick = true,
			};
			menu_config_logging_adjusts.Click += (_, _ea) =>
			{
				Taskmaster.ShowProcessAdjusts = menu_config_logging_adjusts.Checked;

				var corecfg = Taskmaster.Config.Load(Taskmaster.coreconfig);
				corecfg.Config["Logging"]["Show process adjusts"].BoolValue = Taskmaster.ShowProcessAdjusts;
				corecfg.MarkDirty();
			};

			var menu_config_logging_session = new ToolStripMenuItem("Session actions")
			{
				Checked = Taskmaster.ShowSessionActions,
				CheckOnClick = true,
			};
			menu_config_logging_session.Click += (_, _ea) =>
			{
				Taskmaster.ShowSessionActions = menu_config_logging_session.Checked;

				var corecfg = Taskmaster.Config.Load(Taskmaster.coreconfig);
				corecfg.Config["Logging"]["Show session actions"].BoolValue = Taskmaster.ShowSessionActions;
				corecfg.MarkDirty();
			};

			var menu_config_logging_neterrors = new ToolStripMenuItem("Network errors")
			{
				Checked = Taskmaster.ShowNetworkErrors,
				CheckOnClick = true,
			};
			menu_config_logging_neterrors.Click += (_, _ea) =>
			{
				Taskmaster.ShowNetworkErrors = menu_config_logging_neterrors.Checked;

				var corecfg = Taskmaster.Config.Load(Taskmaster.coreconfig);
				corecfg.Config["Logging"]["Show network errors"].BoolValue = Taskmaster.ShowNetworkErrors;
				corecfg.MarkDirty();
			};

			menu_config_logging.DropDownItems.Add(menu_config_logging_adjusts);
			menu_config_logging.DropDownItems.Add(menu_config_logging_session);
			menu_config_logging.DropDownItems.Add(menu_config_logging_neterrors);

			var menu_config_bitmaskstyle_bitmask = new ToolStripMenuItem("Bitmask")
			{
				Checked = Taskmaster.AffinityStyle == 0,
			};
			var menu_config_bitmaskstyle_decimal = new ToolStripMenuItem("Decimal")
			{
				Checked = Taskmaster.AffinityStyle == 1,
			};
			menu_config_bitmaskstyle_bitmask.Click += (_, _ea) =>
			{
				Taskmaster.AffinityStyle = 0;
				menu_config_bitmaskstyle_bitmask.Checked = true;
				menu_config_bitmaskstyle_decimal.Checked = false;
				// TODO: re-render watchlistRules

				var corecfg = Taskmaster.Config.Load(Taskmaster.coreconfig);
				corecfg.Config[HumanReadable.Generic.QualityOfLife][HumanReadable.Hardware.CPU.Settings.AffinityStyle].IntValue = 0;
				corecfg.MarkDirty();
			};
			menu_config_bitmaskstyle_decimal.Click += (_, _ea) =>
			{
				Taskmaster.AffinityStyle = 1;
				menu_config_bitmaskstyle_bitmask.Checked = false;
				menu_config_bitmaskstyle_decimal.Checked = true;
				// TODO: re-render watchlistRules

				var corecfg = Taskmaster.Config.Load(Taskmaster.coreconfig);
				corecfg.Config[HumanReadable.Generic.QualityOfLife][HumanReadable.Hardware.CPU.Settings.AffinityStyle].IntValue = 1;
				corecfg.MarkDirty();
			};
			//var menu_config_bitmaskstyle_both = new ToolStripMenuItem("Decimal [Bitmask]");

			menu_config_bitmaskstyle.DropDownItems.Add(menu_config_bitmaskstyle_bitmask);
			menu_config_bitmaskstyle.DropDownItems.Add(menu_config_bitmaskstyle_decimal);
			//menu_config_bitmaskstyle.DropDownItems.Add(menu_config_bitmaskstyle_both);

			var menu_config_advanced = new ToolStripMenuItem("Advanced", null, AdvancedConfigRequest);

			var menu_config_powermanagement = new ToolStripMenuItem("Power management", null, PowerConfigRequest);
			//menu_config_power.DropDownItems.Add(menu_config_power_autoadjust); // sub-menu removed

			//

			var menu_config_log = new ToolStripMenuItem("Logging");
			var menu_config_log_power = new ToolStripMenuItem("Power mode changes", null, (_, _ea) => { });
			menu_config_log.DropDownItems.Add(menu_config_log_power);

			var menu_config_components = new ToolStripMenuItem("Components", null, ShowComponentConfig);

			var menu_config_experiments = new ToolStripMenuItem("Experiments", null, ShowExperimentConfig);

			var menu_config_folder = new ToolStripMenuItem("Open in file manager", null, (_, _ea) => Process.Start(Taskmaster.datapath));
			// menu_config.DropDownItems.Add(menu_config_log);
			menu_config.DropDownItems.Add(menu_config_behaviour);
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

			// DEBUG menu item
			var menu_debug = new ToolStripMenuItem("Debug");
			// Sub Items
			var menu_debug_loglevel = new ToolStripMenuItem("UI log level");

			LogIncludeLevel = MemoryLog.MemorySink.LevelSwitch; // HACK

			menu_debug_loglevel_info = new ToolStripMenuItem("Info", null,
			(_, _ea) =>
			{
				LogIncludeLevel.MinimumLevel = Serilog.Events.LogEventLevel.Information;
				Taskmaster.Trace = false;
				UpdateLogLevelSelection();
			})
			{
				CheckOnClick = true,
				Checked = (LogIncludeLevel.MinimumLevel == Serilog.Events.LogEventLevel.Information),
			};
			menu_debug_loglevel_debug = new ToolStripMenuItem("Debug", null,
			(_, _ea) =>
			{
				LogIncludeLevel.MinimumLevel = Serilog.Events.LogEventLevel.Debug;
				Taskmaster.Trace = false;
				UpdateLogLevelSelection();
			})
			{
				CheckOnClick = true,
				Checked = (LogIncludeLevel.MinimumLevel == Serilog.Events.LogEventLevel.Debug),
			};
#if DEBUG
			menu_debug_loglevel_trace = new ToolStripMenuItem("Trace", null,
			(_, _ea) =>
			{
				LogIncludeLevel.MinimumLevel = Serilog.Events.LogEventLevel.Verbose;
				Taskmaster.Trace = true;
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

			var menu_debug_inaction = new ToolStripMenuItem("Show inaction") { Checked = Taskmaster.ShowInaction, CheckOnClick = true };
			menu_debug_inaction.Click += (_, _ea) => Taskmaster.ShowInaction = menu_debug_inaction.Checked;
			var menu_debug_agency = new ToolStripMenuItem("Show agency") { Checked = Taskmaster.ShowAgency, CheckOnClick = true };
			menu_debug_agency.Click += (_, _ea) => Taskmaster.ShowAgency = menu_debug_agency.Checked;
			var menu_debug_scanning = new ToolStripMenuItem("Scanning")
			{
				Checked = Taskmaster.DebugFullScan,
				CheckOnClick = true,
				Enabled = Taskmaster.ProcessMonitorEnabled,
			};
			menu_debug_scanning.Click += (_, _ea) =>
			{
				Taskmaster.DebugFullScan = menu_debug_scanning.Checked;
				if (Taskmaster.DebugFullScan) EnsureVerbosityLevel();
			};

			var menu_debug_procs = new ToolStripMenuItem("Processes")
			{
				Checked = Taskmaster.DebugProcesses,
				CheckOnClick = true,
				Enabled = Taskmaster.ProcessMonitorEnabled,
			};
			menu_debug_procs.Click += (_, _ea) =>
			{
				Taskmaster.DebugProcesses = menu_debug_procs.Checked;
				if (Taskmaster.DebugProcesses)
					StartProcessDebug();
				else
					StopProcessDebug();
			};
			var menu_debug_foreground = new ToolStripMenuItem(HumanReadable.System.Process.Foreground)
			{
				Checked = Taskmaster.DebugForeground,
				CheckOnClick = true,
				Enabled = Taskmaster.ActiveAppMonitorEnabled,
			};
			menu_debug_foreground.Click += (_, _ea) =>
			{
				Taskmaster.DebugForeground = menu_debug_foreground.Checked;
				if (Taskmaster.DebugForeground)
					StartProcessDebug();
				else
					StopProcessDebug();
			};

			var menu_debug_paths = new ToolStripMenuItem("Paths")
			{
				Checked = Taskmaster.DebugPaths,
				CheckOnClick = true,
			};
			menu_debug_paths.Click += (_, _ea) =>
			{
				Taskmaster.DebugPaths = menu_debug_paths.Checked;
				if (Taskmaster.DebugPaths) EnsureVerbosityLevel();
			};
			var menu_debug_power = new ToolStripMenuItem(HumanReadable.Hardware.Power.Section)
			{
				Checked = Taskmaster.DebugPower,
				CheckOnClick = true,
				Enabled = Taskmaster.PowerManagerEnabled,
			};
			menu_debug_power.Click += (_, _ea) =>
			{
				Taskmaster.DebugPower = menu_debug_power.Checked;
				if (Taskmaster.DebugPower)
				{
					var pev = new PowerModeEventArgs(powermanager.CurrentMode);
					PowerPlanDebugEvent(this, pev); // populates powerbalancer_plan
					powermanager.onPlanChange += PowerPlanDebugEvent;
					powermanager.onAutoAdjustAttempt += PowerLoadHandler;

					tabLayout.Controls.Add(powerDebugTab);
					EnsureVerbosityLevel();
				}
				else
				{
					powermanager.onPlanChange -= PowerPlanDebugEvent;
					powermanager.onAutoAdjustAttempt -= PowerLoadHandler;
					bool refocus = tabLayout.SelectedTab.Equals(powerDebugTab);
					tabLayout.Controls.Remove(powerDebugTab);
					if (refocus) tabLayout.SelectedIndex = 1; // watchlist
				}
			};

			var menu_debug_session = new ToolStripMenuItem("Session")
			{
				Checked = Taskmaster.DebugSession,
				CheckOnClick = true,
				Enabled = Taskmaster.PowerManagerEnabled,
			};
			menu_debug_session.Click += (_, _ea) =>
			{
				Taskmaster.DebugSession = menu_debug_session.Checked;
				if (Taskmaster.DebugSession) EnsureVerbosityLevel();
			};
			var menu_debug_monitor = new ToolStripMenuItem(HumanReadable.Hardware.Monitor.Section)
			{
				Checked = Taskmaster.DebugMonitor,
				CheckOnClick = true,
				Enabled = Taskmaster.PowerManagerEnabled,
			};
			menu_debug_monitor.Click += (_, _ea) =>
			{
				Taskmaster.DebugMonitor = menu_debug_monitor.Checked;
				if (Taskmaster.DebugMonitor) EnsureVerbosityLevel();
			};

			var menu_debug_audio = new ToolStripMenuItem(HumanReadable.Hardware.Audio.Section)
			{
				Checked = Taskmaster.DebugAudio,
				CheckOnClick = true,
				Enabled = Taskmaster.AudioManagerEnabled,
			};
			menu_debug_audio.Click += (_, _ea) =>
			{
				Taskmaster.DebugAudio = menu_debug_audio.Checked;
				if (Taskmaster.DebugAudio) EnsureVerbosityLevel();
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
			menu_debug.DropDownItems.Add(menu_debug_foreground);
			//menu_debug.DropDownItems.Add(menu_debug_paths);
			menu_debug.DropDownItems.Add(menu_debug_power);
			menu_debug.DropDownItems.Add(menu_debug_session);
			menu_debug.DropDownItems.Add(menu_debug_monitor);
			menu_debug.DropDownItems.Add(menu_debug_audio);
			menu_debug.DropDownItems.Add(new ToolStripSeparator());
			menu_debug.DropDownItems.Add(menu_debug_clear);

			// INFO menu
			var menu_info = new ToolStripMenuItem("Info");
			// Sub Items

			menu_info.DropDownItems.Add(new ToolStripMenuItem("Github", null, (_, _ea) => Process.Start(Taskmaster.GitURL)));
			menu_info.DropDownItems.Add(new ToolStripMenuItem("Itch.io", null, (_, _ea) => Process.Start(Taskmaster.ItchURL)));
			menu_info.DropDownItems.Add(new ToolStripSeparator());
			menu_info.DropDownItems.Add(new ToolStripMenuItem("License", null, (_, _ea) => OpenLicenseDialog()));
			menu_info.DropDownItems.Add(new ToolStripSeparator());
			menu_info.DropDownItems.Add(new ToolStripMenuItem("About", null, ShowAboutDialog));

			menu.Items.AddRange(new[] { menu_action, menu_config, menu_debug, menu_info });

			// no simpler way?

			menu_action.MouseEnter += ToolStripMenuAutoOpen;
			menu_config.MouseEnter += ToolStripMenuAutoOpen;
			menu_debug.MouseEnter += ToolStripMenuAutoOpen;
			menu_info.MouseEnter += ToolStripMenuAutoOpen;

			menu_action.DropDown.AutoClose = true;
			menu_config.DropDown.AutoClose = true;
			menu_debug.DropDown.AutoClose = true;
			menu_info.DropDown.AutoClose = true;

			infoTab = new TabPage("Info") { Padding = BigPadding };
			tabLayout.Controls.Add(infoTab);

			watchTab = new TabPage("Watchlist") { Padding = BigPadding };
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

			LoadUIConfiguration(out int opentab, out int[] appwidths, out int[] micwidths, out int[] ifacewidths);

			if (Taskmaster.MicrophoneMonitorEnabled) BuildMicrophonePanel(micwidths);

			// Main Window row 4-5, internet status
			TableLayoutPanel netstatus = null;
			if (Taskmaster.NetworkMonitorEnabled) netstatus = BuildNetworkStatusUI(infopanel, ifacewidths);
			// End: Inet status

			GotFocus += UpdateNetwork;
			GotFocus += StartUIUpdates;

			FormClosing += StopUIUpdates;
			VisibleChanged += (sender, ea) =>
			{
				if (Visible)
				{
					UpdateNetwork(sender, ea);
					StartUIUpdates(sender, ea);
				}
				else
				{
					StopUIUpdates(sender, ea);
				}
			};

			UItimer.Interval = UIUpdateFrequency;
			if (Taskmaster.NetworkMonitorEnabled)
				UItimer.Tick += UpdateNetwork;

			UItimer.Tick += UpdateMemoryStats;
			//UItimer.Tick += Cleanup;

			if (Taskmaster.PathCacheLimit > 0)
			{
				if (Taskmaster.DebugCache) UItimer.Tick += PathCacheUpdate;
			}

			// End: Settings

			BuildWatchlist(appwidths);

			// UI Log
			// -1 = contents, -2 = heading
			loglist.Columns.Add("Event Log", -2, HorizontalAlignment.Left); // 2
			ResizeLogList = delegate
			{
				loglist.BeginUpdate();
				loglist.Columns[0].Width = -2;

				// HACK: Enable visual styles causes horizontal bar to always be present without the following.
				loglist.Columns[0].Width = loglist.Columns[0].Width - 2;

				//loglist.Height = -2;
				//loglist.Width = -2;
				loglist.Height = ClientSize.Height - tabLayout.Height - statusbar.Height - menu.Height;
				ShowLastLog();
				loglist.EndUpdate();
			};
			//ResizeEnd += ResizeLogList;
			//Resize += ResizeLogList;

			SizeChanged += ResizeLogList;
			Shown += ResizeLogList;

			loglistms = new ContextMenuStrip();
			var logcopy = new ToolStripMenuItem("Copy to clipboard", null, CopyLogToClipboard);
			loglistms.Items.Add(logcopy);
			loglist.ContextMenuStrip = loglistms;

			var cfg = Taskmaster.Config.Load(Taskmaster.coreconfig);
			bool modified, tdirty = false;
			MaxLogSize = cfg.Config["Logging"].GetSetDefault("UI max items", 200, out modified).IntValue;
			tdirty |= modified;
			UIUpdateFrequency = cfg.Config["User Interface"].GetSetDefault("Update frequency", 2000, out modified).IntValue.Constrain(100, 5000);
			tdirty |= modified;
			if (tdirty)
			{
				cfg.Config["Logging"]["UI max items"].Comment = "Maximum number of items/lines to retain on UI level.";
				cfg.Config["User Interface"]["Update frequency"].Comment = "In milliseconds. Frequency of controlled UI updates. Affects visual accuracy of timers and such. Valid range: 100 to 5000.";
				cfg.MarkDirty();
			}

			TableLayoutPanel cachePanel = Taskmaster.DebugCache ? BuildCachePanel() : null;

			TableLayoutPanel tempmonitorpanel = Taskmaster.TempMonitorEnabled ? BuildTempMonitorPanel() : null;

			TableLayoutPanel corepanel = new TableLayoutPanel()
			{
				ColumnCount = 2,
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowOnly,
				//Dock = DockStyle.Fill,
				Dock = DockStyle.Fill,
			};

			if (Taskmaster.PowerManagerEnabled)
			{
				corepanel.Controls.Add(new Label() { Text = "CPU", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Left });
				cpuload = new Label() { Text = HumanReadable.Generic.Uninitialized, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Left };
				corepanel.Controls.Add(cpuload);
			}
			// TODO: Add high, low and average

			corepanel.Controls.Add(new Label() { Text = "RAM", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Left });
			ramload = new Label() { Text = HumanReadable.Generic.Uninitialized, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Left };
			corepanel.Controls.Add(ramload);

			TableLayoutPanel gpupanel = null;
			if (Taskmaster.HardwareMonitorEnabled)
			{
				gpupanel = new TableLayoutPanel()
				{
					ColumnCount = 2,
					AutoSize = true,
					AutoSizeMode = AutoSizeMode.GrowOnly,
					//Dock = DockStyle.Fill,
					Dock = DockStyle.Fill,
				};

				gpupanel.Controls.Add(new Label() { Text = "VRAM", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Left });
				gpuvram = new Label() { Text = HumanReadable.Generic.Uninitialized, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Left };
				gpupanel.Controls.Add(gpuvram);

				gpupanel.Controls.Add(new Label() { Text = "Load", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Left });
				gpuload = new Label() { Text = HumanReadable.Generic.Uninitialized, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Left };
				gpupanel.Controls.Add(gpuload);

				gpupanel.Controls.Add(new Label() { Text = "Temp", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Left });
				gputemp = new Label() { Text = HumanReadable.Generic.Uninitialized, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Left };
				gpupanel.Controls.Add(gputemp);

				gpupanel.Controls.Add(new Label() { Text = "Fan", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Left });
				gpufan = new Label() { Text = HumanReadable.Generic.Uninitialized, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Left };
				gpupanel.Controls.Add(gpufan);
			}

			TableLayoutPanel nvmpanel = null;
			if (Taskmaster.HealthMonitorEnabled) BuildNVMPanel(out nvmpanel);

			TableLayoutPanel powerpanel = null;
			if (Taskmaster.PowerManagerEnabled) BuildPowerPanel(out powerpanel);

			TableLayoutPanel lastmodifypanel = null;
			if (Taskmaster.LastModifiedList) lastmodifypanel = BuildLastModifiedPanel(appwidths);

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
				coresystems.Controls.Add(new Label() { Text = "Core", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Left, Font = boldfont });
				coresystems.Controls.Add(corepanel);
				coresystems.Controls.Add(new Label() { Text = "GPU", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Left, Font = boldfont });
				coresystems.Controls.Add(gpupanel);
			}
			if (powerpanel != null)
			{
				additionalsystems.Controls.Add(new Label { Text = HumanReadable.Hardware.Power.Section, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Left, Font = boldfont });
				additionalsystems.Controls.Add(powerpanel);
			}
			if (nvmpanel != null)
			{
				additionalsystems.Controls.Add(new Label { Text = "Non-Volatile Memory", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Left, Font = boldfont });
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

			if (Taskmaster.DebugPower) BuildPowerDebugPanel();

			// -------------------------------------------------------------------------------------------------------

			if (Taskmaster.DebugProcesses || Taskmaster.DebugForeground)
				BuildProcessDebug();

			// End Process Debug

			tabLayout.SelectedIndex = opentab >= tabLayout.TabCount ? 0 : opentab;
		}

		void ToolStripMenuAutoOpen(object sender, EventArgs _)
		{
			var mi = sender as ToolStripMenuItem;
			if (!ContainsFocus || !Taskmaster.AutoOpenMenus) return;
			mi?.ShowDropDown();
		}

		private static void OpenLicenseDialog()
		{
			try { using (var n = new LicenseDialog(initial: false)) { n.ShowDialog(); } }
			catch (Exception ex) { Logging.Stacktrace(ex); }
		}

		private void BuildProcessDebug()
		{
			exitwaitlist = new UI.ListViewEx()
			{
				AutoSize = true,
				//Height = 180,
				//Width = tabLayout.Width - 12, // FIXME: 3 for the bevel, but how to do this "right"?
				FullRowSelect = true,
				View = View.Details,
				MinimumSize = new System.Drawing.Size(-2, 80),
				Dock = DockStyle.Fill,
			};

			exitwaitlist.Columns.Add("Id", 50);
			exitwaitlist.Columns.Add(HumanReadable.System.Process.Executable, 280);
			exitwaitlist.Columns.Add("State", 160);
			exitwaitlist.Columns.Add(HumanReadable.Hardware.Power.Section, 80);

			var processlayout = new TableLayoutPanel()
			{
				ColumnCount = 1,
				AutoSize = true,
				Dock = DockStyle.Fill,
			};

			if (Taskmaster.ActiveAppMonitorEnabled)
			{
				var foregroundapppanel = new FlowLayoutPanel
				{
					Dock = DockStyle.Fill,
					FlowDirection = FlowDirection.LeftToRight,
					WrapContents = false,
					AutoSize = true,
					//Width = tabLayout.Width - 3,
				};

				activeLabel = new Label()
				{
					AutoSize = true,
					Dock = DockStyle.Left,
					Text = "no active window found",
					TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
					AutoEllipsis = true,
				};
				activeExec = new Label() { Text = HumanReadable.Generic.Uninitialized, Width = 100, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
				activeFullscreen = new Label() { Text = HumanReadable.Generic.Uninitialized, Width = 60, TextAlign = System.Drawing.ContentAlignment.MiddleCenter };
				activePID = new Label() { Text = HumanReadable.Generic.Uninitialized, Width = 60, TextAlign = System.Drawing.ContentAlignment.MiddleCenter };

				foregroundapppanel.Controls.Add(new Label() { Text = "Active window:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Width = 80 });
				foregroundapppanel.Controls.Add(activeLabel);
				foregroundapppanel.Controls.Add(activeExec);
				foregroundapppanel.Controls.Add(activeFullscreen);
				foregroundapppanel.Controls.Add(new Label { Text = "Id:", Width = 20, TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
				foregroundapppanel.Controls.Add(activePID);

				processlayout.Controls.Add(foregroundapppanel);
			}

			processlayout.Controls.Add(new Label()
			{
				AutoSize = true,
				Text = "Exit wait list...",
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
				Dock = DockStyle.Left,
				Padding = BigPadding
			});

			processlayout.Controls.Add(exitwaitlist);

			processlayout.Controls.Add(new Label()
			{
				AutoSize = true,
				Text = "Processing list",
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
				Dock = DockStyle.Left,
				//Padding = new Padding(3),
			});

			processinglist = new UI.ListViewEx()
			{
				AutoSize = true,
				FullRowSelect = true,
				View = View.Details,
				MinimumSize = new System.Drawing.Size(-2, 120),
				Dock = DockStyle.Fill,
			};

			processinglist.Columns.Add("Id", 50);
			processinglist.Columns.Add(HumanReadable.System.Process.Executable, 280);
			processinglist.Columns.Add("State", 160);
			processinglist.Columns.Add("Time", 80);

			processlayout.Controls.Add(processinglist);

			ExitWaitlistMap = new ConcurrentDictionary<int, ListViewItem>();

			ProcessDebugTab = new TabPage("Process Debug") { Padding = BigPadding };

			ProcessDebugTab.Controls.Add(processlayout);

			tabLayout.Controls.Add(ProcessDebugTab);
		}

		private void BuildWatchlist(int[] appwidths)
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
				WatchlistRules.BeginUpdate();
				WatchlistRules.Sort();
				WatchlistColor();
				WatchlistRules.EndUpdate();
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
			WatchlistRules.Columns.Add("Name", appwidths[1]);
			WatchlistRules.Columns.Add(HumanReadable.System.Process.Executable, appwidths[2]);
			WatchlistRules.Columns.Add(HumanReadable.System.Process.Priority, appwidths[3]);
			WatchlistRules.Columns.Add(HumanReadable.System.Process.Affinity, appwidths[4]);
			WatchlistRules.Columns.Add(HumanReadable.Hardware.Power.Plan, appwidths[5]);
			WatchlistRules.Columns.Add("Adjusts", appwidths[6]);
			WatchlistRules.Columns.Add(HumanReadable.System.Process.Path, appwidths[7]);
			WatchlistRules.Scrollable = true;
			WatchlistRules.Alignment = ListViewAlignment.Left;

			WatchlistRules.DoubleClick += EditWatchlistRule; // for in-app editing

			watchTab.Controls.Add(WatchlistRules);
		}

		private void LoadUIConfiguration(out int opentab, out int[] appwidths, out int[] micwidths, out int[] ifacewidths)
		{
			var uicfg = Taskmaster.Config.Load(uiconfig);
			var wincfg = uicfg.Config["Windows"];
			var colcfg = uicfg.Config["Columns"];

			opentab = uicfg.Config["Tabs"].TryGet("Open")?.IntValue ?? 0;
			appwidths = null;
			int[] appwidthsDefault = new int[] { 20, 120, 140, 82, 60, 76, 46, 160 };
			appwidths = colcfg.GetSetDefault("Apps", appwidthsDefault).IntValueArray;
			if (appwidths.Length != appwidthsDefault.Length) appwidths = appwidthsDefault;

			micwidths = null;
			if (Taskmaster.MicrophoneMonitorEnabled)
			{
				int[] micwidthsDefault = new int[] { 200, 220, 60, 60, 60, 120 };
				micwidths = colcfg.GetSetDefault("Mics", micwidthsDefault).IntValueArray;
				if (micwidths.Length != micwidthsDefault.Length) micwidths = micwidthsDefault;
			}

			ifacewidths = null;
			if (Taskmaster.NetworkMonitorEnabled)
			{
				int[] ifacewidthsDefault = new int[] { 110, 60, 50, 70, 90, 192, 60, 60, 40 };
				ifacewidths = colcfg.GetSetDefault("Interfaces", ifacewidthsDefault).IntValueArray;
				if (ifacewidths.Length != ifacewidthsDefault.Length) ifacewidths = ifacewidthsDefault;
			}

			var winpos = wincfg["Main"].IntValueArray;

			if (winpos != null && winpos.Length == 4)
			{
				var rectangle = new System.Drawing.Rectangle(winpos[0], winpos[1], winpos[2], winpos[3]);
				if (Screen.AllScreens.Any(ø => ø.Bounds.IntersectsWith(Bounds))) // https://stackoverflow.com/q/495380
				{
					StartPosition = FormStartPosition.Manual;
					Location = new System.Drawing.Point(rectangle.Left, rectangle.Top);
					Bounds = rectangle;
				}
			}
		}

		private void BuildMicrophonePanel(int[] micwidths)
		{
			var micpanel = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				//Padding = DefaultPadding,
				//Width = tabLayout.Width - 12
			};
			micpanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20)); // this is dumb

			AudioInputDevice = new Label { Text = HumanReadable.Generic.Uninitialized, Dock = DockStyle.Left, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, AutoEllipsis = true };
			var micNameRow = new TableLayoutPanel
			{
				RowCount = 1,
				ColumnCount = 2,
				Dock = DockStyle.Top,
				//AutoSize = true // why not?
			};
			micNameRow.Controls.Add(new Label { Text = "Default communications device:", Dock = DockStyle.Left, /*Width = 180,*/ TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
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

			miccntrl.Controls.Add(new Label { Text = "Volume", Dock = DockStyle.Left, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			miccntrl.Controls.Add(AudioInputVolume);

			corCountLabel = new Label { Text = "0", Dock = DockStyle.Left, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, };

			miccntrl.Controls.Add(new Label { Text = "Correction count:", Dock = DockStyle.Left, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			miccntrl.Controls.Add(corCountLabel);

			AudioInputEnable = new ComboBox()
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				Items = { "Enabled", "Disabled" },
				SelectedIndex = 1,
				Enabled = false,
			};

			miccntrl.Controls.Add(new Label { Text = "Control:", Dock = DockStyle.Left, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, });
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

		private TableLayoutPanel BuildTempMonitorPanel()
		{
			TableLayoutPanel tempmonitorpanel;
			tempObjectCount = new Label()
			{
				Width = 40,
				//Dock = DockStyle.Left,
				Text = HumanReadable.Generic.Uninitialized,
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
			};

			tempObjectSize = new Label()
			{
				Width = 40,
				//Dock = DockStyle.Left,
				Text = HumanReadable.Generic.Uninitialized,
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
			};

			tempmonitorpanel = new TableLayoutPanel
			{
				Dock = DockStyle.Top,
				RowCount = 1,
				ColumnCount = 5,
				Height = 40,
				AutoSize = true
			};
			tempmonitorpanel.Controls.Add(new Label
			{
				Text = "Temp",
				Dock = DockStyle.Left,
				TextAlign = System.Drawing.ContentAlignment.MiddleRight,
				AutoSize = true
			});
			tempmonitorpanel.Controls.Add(new Label
			{
				Text = "Objects",
				Dock = DockStyle.Left,
				TextAlign = System.Drawing.ContentAlignment.MiddleRight,
				AutoSize = true
			});
			tempmonitorpanel.Controls.Add(tempObjectCount);
			tempmonitorpanel.Controls.Add(new Label
			{
				Text = "Size (MB)",
				Dock = DockStyle.Left,
				TextAlign = System.Drawing.ContentAlignment.MiddleRight,
				AutoSize = true
			});
			tempmonitorpanel.Controls.Add(tempObjectSize);
			return tempmonitorpanel;
		}

		private TableLayoutPanel BuildCachePanel()
		{
			var cachePanel = new TableLayoutPanel()
			{
				ColumnCount = 5,
				AutoSize = true,
				Dock = DockStyle.Fill,
			};
			cachePanel.Controls.Add(new Label()
			{
				Text = "Path cache:",
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
				AutoSize = true
			});
			cachePanel.Controls.Add(new Label()
			{
				Text = "Objects",
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
				AutoSize = true
			});
			cacheObjects = new Label()
			{
				AutoSize = true,
				Width = 40,
				Text = HumanReadable.Generic.Uninitialized,
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft
			};
			cachePanel.Controls.Add(cacheObjects);
			cachePanel.Controls.Add(new Label()
			{
				Text = "Ratio",
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
				AutoSize = true
			});
			cacheRatio = new Label()
			{
				AutoSize = true,
				Width = 40,
				Text = HumanReadable.Generic.Uninitialized,
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft
			};
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
			powerlayout.Controls.Add(new Label()
			{
				Text = "Power mode autobalancing tracker...",
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
				AutoSize = true,
				Dock = DockStyle.Left,
				Padding = BigPadding,
			});

			powerbalancerlog = new UI.ListViewEx()
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
			powerbalancerstatus.Controls.Add(new Label() { Text = "Behaviour:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			powerbalancer_behaviour = new Label() { Text = HumanReadable.Generic.Uninitialized, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true };
			powerbalancerstatus.Controls.Add(powerbalancer_behaviour);
			powerbalancerstatus.Controls.Add(new Label() { Text = "| Plan:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			powerbalancer_plan = new Label() { Text = HumanReadable.Generic.Uninitialized, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true };
			powerbalancerstatus.Controls.Add(powerbalancer_plan);
			powerbalancerstatus.Controls.Add(new Label() { Text = "Forced by:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });
			powerbalancer_forcedcount = new Label() { Text = HumanReadable.Generic.Uninitialized, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true };
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
			lastmodifypanel.Controls.Add(new Label() { Text = "Last process modifications", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Left });
			lastmodifylist = new UI.ListViewEx()
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
			lastmodifyms.Opened += (_, _ea) =>
			{
				lastmodifycopy.Enabled = (lastmodifylist.SelectedItems.Count == 1);
			};
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

			pwmode = new Label { Text = HumanReadable.Generic.Uninitialized, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Left };
			pwcause = new Label { Text = HumanReadable.Generic.Uninitialized, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Left };
			pwbehaviour = new Label { Text = HumanReadable.Generic.Uninitialized, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Left };
			powerpanel.Controls.Add(new Label { Text = "Behaviour:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Left });
			powerpanel.Controls.Add(pwbehaviour);
			powerpanel.Controls.Add(new Label { Text = "Mode:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Left });
			powerpanel.Controls.Add(pwmode);
			powerpanel.Controls.Add(new Label { Text = "Cause:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Left });
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

			nvmtransfers = new Label { Text = HumanReadable.Generic.Uninitialized, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Top };
			nvmsplitio = new Label { Text = HumanReadable.Generic.Uninitialized, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Top };
			nvmdelay = new Label { Text = HumanReadable.Generic.Uninitialized, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Top };
			nvmqueued = new Label { Text = HumanReadable.Generic.Uninitialized, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Top };
			//hardfaults = new Label { Text = HumanReadable.Generic.Uninitialized, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Left };

			nvmpanel.Controls.Add(new Label { Text = "Transfers", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Left });
			nvmpanel.Controls.Add(nvmtransfers);
			nvmpanel.Controls.Add(new Label { Text = "Split I/O", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Left });
			nvmpanel.Controls.Add(nvmsplitio);
			nvmpanel.Controls.Add(new Label { Text = "Delay", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Left });
			nvmpanel.Controls.Add(nvmdelay);
			nvmpanel.Controls.Add(new Label { Text = "Queued", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Left });
			nvmpanel.Controls.Add(nvmqueued);
			//nvmpanel.Controls.Add(new Label { Text = "Hard faults", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Left });
			//nvmpanel.Controls.Add(hardfaults);
		}

		TableLayoutPanel BuildNetworkStatusUI(FlowLayoutPanel infopanel, int[] ifacewidths)
		{
			TableLayoutPanel netstatus;
			netstatuslabel = new Label() { Dock = DockStyle.Left, Text = HumanReadable.Generic.Uninitialized, AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
			inetstatuslabel = new Label() { Dock = DockStyle.Left, Text = HumanReadable.Generic.Uninitialized, AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
			uptimeMeanLabel = new Label() { Dock = DockStyle.Left, Text = HumanReadable.Generic.Uninitialized, AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
			netTransmit = new Label() { Dock = DockStyle.Left, Text = HumanReadable.Generic.Uninitialized, AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
			netQueue = new Label() { Dock = DockStyle.Left, Text = HumanReadable.Generic.Uninitialized, AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
			uptimestatuslabel = new Label
			{
				Dock = DockStyle.Left,
				Text = HumanReadable.Generic.Uninitialized,
				AutoSize = true,
				TextAlign = System.Drawing.ContentAlignment.MiddleLeft
			};

			netstatus = new TableLayoutPanel
			{
				ColumnCount = 6,
				RowCount = 1,
				Dock = DockStyle.Top,
				AutoSize = true,
			};

			// first row
			netstatus.Controls.Add(new Label() { Text = "Network", Dock = DockStyle.Left, AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
			netstatus.Controls.Add(netstatuslabel);

			netstatus.Controls.Add(new Label() { Text = "Uptime", Dock = DockStyle.Left, AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
			netstatus.Controls.Add(uptimestatuslabel);

			netstatus.Controls.Add(new Label() { Text = "Transmission", Dock = DockStyle.Left, AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
			netstatus.Controls.Add(netTransmit);

			// second row
			netstatus.Controls.Add(new Label() { Text = "Internet", Dock = DockStyle.Left, AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
			netstatus.Controls.Add(inetstatuslabel);

			netstatus.Controls.Add(new Label { Text = "Average", Dock = DockStyle.Left, AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
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
			using (var n = new UI.Config.ExperimentConfig())
			{
				n.ShowDialog();
				if (n.DialogResult == DialogResult.OK)
				{
					Log.Information("<Experiments> Settings changed");

					Taskmaster.ConfirmExit(restart: true, message: "Restart required for experimental settings to take effect.", alwaysconfirm:true);
				}
			}
		}

		void ShowAboutDialog(object sender, EventArgs ea)
		{
			var builddate = Taskmaster.BuildDate();

			var now = DateTime.Now;
			var age = (now - builddate).TotalDays;

			SimpleMessageBox.ShowModal("About Taskmaster!",
					Application.ProductName +
					"\nVersion: " + Application.ProductVersion +
					"\nBuilt: " + $"{builddate.ToString("yyyy/MM/dd HH:mm")} [{age:N0} days old]" +
					"\n\nCreated by M.A., 2016–2019" +
					"\n\nAt Github: " + Taskmaster.GitURL +
					"\nAt Itch.io: " + Taskmaster.ItchURL +
					"\n\nFree system maintenance and de-obnoxifying app.\n\nAvailable under MIT license.",
					SimpleMessageBox.Buttons.OK);
		}

		void ShowComponentConfig(object sender, EventArgs ea)
		{
			try
			{
				using (var comps = new ComponentConfigurationWindow(initial: false))
				{
					comps.ShowDialog();
					if (comps.DialogResult == DialogResult.OK)
					{
						if (SimpleMessageBox.ShowModal("Restart needed", "TM needs to be restarted for changes to take effect.\n\nCancel to do so manually later.", SimpleMessageBox.Buttons.AcceptCancel) == SimpleMessageBox.ResultType.OK)
						{
							Log.Information("<UI> Restart request");
							Taskmaster.UnifiedExit(restart: true);
						}
					}
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				//throw; // bad idea
			}
		}

		Stopwatch WatchlistSearchInputTimer = new Stopwatch();
		readonly System.Windows.Forms.Timer WatchlistSearchTimer = new System.Windows.Forms.Timer();
		string SearchString = string.Empty;
		private void WatchlistRulesKeyboardSearch(object _, KeyPressEventArgs ea)
		{
			bool ctrlchar = char.IsControl(ea.KeyChar);

			// RESET
			if (WatchlistSearchInputTimer.ElapsedMilliseconds > 2_700) // previous input too long ago
				SearchString = string.Empty;

			if (string.IsNullOrEmpty(SearchString)) // catches above and initial state
				WatchlistSearchTimer.Start();

			WatchlistSearchInputTimer.Restart();

			if (Taskmaster.Trace) Debug.WriteLine($"INPUT: {((int)ea.KeyChar):X}");
			if (char.IsControl(ea.KeyChar))
			{
				if (Taskmaster.Trace) Debug.WriteLine("CONTROL CHARACTER!");
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
				WatchlistRules.ClientSize.Width/3, WatchlistRules.ClientSize.Height,
				string.IsNullOrEmpty(SearchString) ? 500 : 2_500);
		}

		private void WatchlistSearchTimer_Tick(object sender, EventArgs e)
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
			if (!IsHandleCreated) return;

			BeginInvoke(new Action(() =>
			{
				float vramTotal = ea.Data.MemTotal / 1024;
				float vramUsed = vramTotal * (ea.Data.MemLoad / 100);
				float vramFree = vramTotal - vramUsed;

				gpuvram.Text = $"{vramFree:N2} of {vramTotal:N1} GiB free ({ea.Data.MemLoad:N1} % usage) [Controller: {ea.Data.MemCtrl:N1} %]";
				gpuload.Text = $"{ea.Data.Load:N1} %";
				gpufan.Text = $"{ea.Data.FanLoad:N1} % [{ea.Data.FanSpeed} RPM]";
				gputemp.Text = $"{ea.Data.Temperature:N1} C";
			}));
		}

		void StartProcessDebug()
		{
			bool enabled = Taskmaster.DebugProcesses || Taskmaster.DebugForeground;
			if (!enabled) return;

			if (Taskmaster.DebugProcesses) processmanager.HandlingStateChange += ProcessHandlingStateChangeEvent;

			if (enabled && ProcessDebugTab == null)
				BuildProcessDebug();

			if (activeappmonitor != null && Taskmaster.DebugForeground)
				activeappmonitor.ActiveChanged += OnActiveWindowChanged;

			EnsureVerbosityLevel();
		}

		/// <summary>
		/// Process ID to processinglist mapping.
		/// </summary>
		ConcurrentDictionary<int, ListViewItem> ProcessEventMap = new ConcurrentDictionary<int, ListViewItem>();

		async void ProcessHandlingStateChangeEvent(object _, HandlingStateChangeEventArgs ea)
		{
			if (!IsHandleCreated || disposed) return;

			if (!Taskmaster.DebugProcesses && !Taskmaster.DebugForeground) return;

			try
			{
				ListViewItem item = null;

				int key = ea.Info.Id;
				bool newitem = false;
				if (!ProcessEventMap.TryGetValue(key, out item))
				{
					item = new ListViewItem(new string[] { key.ToString(), ea.Info.Name, string.Empty, string.Empty});
					newitem = true;
					ProcessEventMap.TryAdd(key, item);
				}

				BeginInvoke(new Action(() =>
				{
					try
					{
						processinglist.BeginUpdate();

						// 0 = Id, 1 = Name, 2 = State
						item.SubItems[0].Text = ea.Info.Id.ToString();
						item.SubItems[2].Text = ea.Info.State.ToString();
						item.SubItems[3].Text = DateTime.Now.ToLongTimeString();

						if (newitem) processinglist.Items.Insert(0, item);

						if (ea.Info.Handled) RemoveOldProcessingEntry(key);

						processinglist.EndUpdate();
					}
					catch (System.ObjectDisposedException)
					{
						// bah
					}
					catch (Exception ex)
					{
						Logging.Stacktrace(ex);
					}
				}));
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		async Task RemoveOldProcessingEntry(int key)
		{
			await Task.Delay(TimeSpan.FromSeconds(15)).ConfigureAwait(false);

			BeginInvoke(new Action(() =>
			{
				if (!IsHandleCreated) return;

				try
				{
					if (ProcessEventMap.TryRemove(key, out ListViewItem item))
						processinglist.Items.Remove(item);
				}
				catch { }
			}));
		}

		void StopProcessDebug()
		{
			if (!Taskmaster.DebugForeground && activeappmonitor != null) activeappmonitor.ActiveChanged -= OnActiveWindowChanged;
			if (!Taskmaster.DebugProcesses) processmanager.HandlingStateChange -= ProcessHandlingStateChangeEvent;

			bool enabled = Taskmaster.DebugProcesses || Taskmaster.DebugForeground;
			if (enabled) return;

			if (activeappmonitor != null && Taskmaster.DebugForeground)
				activeappmonitor.ActiveChanged -= OnActiveWindowChanged;

			bool refocus = tabLayout.SelectedTab.Equals(ProcessDebugTab);
			if (!enabled)
			{
				activePID.Text = HumanReadable.Generic.Undefined;
				activeFullscreen.Text = HumanReadable.Generic.Undefined;
				activeFullscreen.Text = HumanReadable.Generic.Undefined;

				tabLayout.Controls.Remove(ProcessDebugTab);
				processinglist.Items.Clear();
				exitwaitlist.Items.Clear();
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
			processingcount = new ToolStripStatusLabel("["+ HumanReadable.Generic.Uninitialized + "]") { AutoSize=false };
			statusbar.Items.Add(processingcount); // not truly useful for anything but debug to show if processing is hanging Somewhere
			statusbar.Items.Add("Next scan in:");
			processingtimer = new ToolStripStatusLabel("["+ HumanReadable.Generic.Uninitialized + "]") { AutoSize = false };
			statusbar.Items.Add(processingtimer);
			statusbar.Items.Add(new ToolStripStatusLabel() { Alignment = ToolStripItemAlignment.Right, Width = -2, Spring = true });
			//verbositylevel = new ToolStripStatusLabel(HumanReadable.Generic.Uninitialized);
			//statusbar.Items.Add(verbositylevel);

			statusbar.Items.Add(new ToolStripStatusLabel("Power plan:") { Alignment = ToolStripItemAlignment.Right});
			powermodestatusbar = new ToolStripStatusLabel(PowerManager.GetModeName(powermanager?.CurrentMode ?? PowerInfo.PowerMode.Undefined)) { Alignment = ToolStripItemAlignment.Right };
			statusbar.Items.Add(powermodestatusbar);
		}

		async void FreeMemoryRequest(object _, EventArgs _ea)
		{
			try
			{
				using (var exsel = new ProcessSelectDialog("Select nothing to try free memory in general."))
				{
					if (exsel.ShowDialog(this) == DialogResult.OK)
					{
						await Taskmaster.processmanager?.FreeMemory(exsel.Executable);
					}
				}
			}
			catch (Exception ex) { Logging.Stacktrace(ex); }
		}

		public async void ExitWaitListHandler(object _discard, ProcessModificationEventArgs ea)
		{
			if (activeappmonitor == null) return;
			if (!IsHandleCreated) return;

			BeginInvoke(new Action(() =>
			{
				try
				{
					bool fgonly = ea.Info.Controller.Foreground != ForegroundMode.Ignore;
					bool fg = (ea.Info.Id == (activeappmonitor?.Foreground ?? ea.Info.Id));

					ListViewItem li = null;
					if (ExitWaitlistMap?.TryGetValue(ea.Info.Id, out li) ?? false)
					{
						if (fgonly)
							li.SubItems[2].Text = fg ? HumanReadable.System.Process.Foreground : HumanReadable.System.Process.Background;
						else
							li.SubItems[2].Text = "ACTIVE";

						if (Taskmaster.Trace && Taskmaster.DebugForeground) Log.Debug("WaitlistHandler: " + ea.Info.Name + " = " + ea.Info.State.ToString());
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
								exitwaitlist?.Items.Remove(li);
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
							(fgonly ? (fg ? HumanReadable.System.Process.Foreground : HumanReadable.System.Process.Background) : "ACTIVE"),
							(ea.Info.PowerWait ? "FORCED" : HumanReadable.Generic.NotAvailable)
						});

						ExitWaitlistMap?.TryAdd(ea.Info.Id, li);
						exitwaitlist?.Items.Insert(0, li);
						li.EnsureVisible();
					}
				}
				catch (Exception ex) { Logging.Stacktrace(ex); }
			}));
		}

		// Called by UI update timer, should be UI thread by default
		async void UpdateMemoryStats(object _, EventArgs _ea)
		{
			if (!IsHandleCreated || disposed) return;
			if (!ramload.Visible) return;

			MemoryManager.Update(); // TODO: this is kinda dumb way to do things
			double freegb = (double)MemoryManager.FreeBytes / 1_073_741_824d;
			double totalgb = (double)MemoryManager.Total / 1_073_741_824d;
			double usage = 1 - (freegb / totalgb);
			ramload.Text = $"{freegb:N2} of {totalgb:N1} GiB free ({usage * 100d:N1} % usage), {MemoryManager.Pressure * 100:N1} % pressure";

			// TODO: Print warning if MemoryManager.Pressure > 100%

			//vramload.Text = $"{Taskmaster.healthmonitor.VRAM()/1_048_576:N0} MB"; // this returns total, not free or used
		}

		// called by cpumonitor, not in UI thread by default
		// TODO: Reverse this design, make the UI poll instead
		public async void CPULoadHandler(object _, ProcessorLoadEventArgs ea)
		{
			if (!IsHandleCreated || disposed) return;
			if (!cpuload.Visible) return;

			BeginInvoke(new Action(() =>
			{
				cpuload.Text = $"{ea.Current:N1} %, Low: {ea.Low:N1} %, Mean: {ea.Mean:N1} %, High: {ea.High:N1} %; Queue: {ea.Queue:N0}";
				// 50 %, Low: 33.2 %, Mean: 52.1 %, High: 72.8 %, Queue: 1
			}));
		}

		public async void PowerLoadHandler(object _, AutoAdjustReactionEventArgs ea)
		{
			if (!IsHandleCreated || disposed) return;

			BeginInvoke(new Action(() =>
			{
				powerbalancerlog.BeginUpdate();

				try
				{
					var li = new ListViewItem(new string[] {
						$"{ea.Current:N2} %",
						$"{ea.Mean:N2} %",
						$"{ea.High:N2} %",
						$"{ea.Low:N2} %",
						ea.Reaction.ToString(),
						PowerManager.GetModeName(ea.Mode),
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

					if (ea.Mode == PowerInfo.PowerMode.HighPerformance)
						li.SubItems[3].BackColor = System.Drawing.Color.FromArgb(255, 230, 230);
					else if (ea.Mode == PowerInfo.PowerMode.PowerSaver)
						li.SubItems[2].BackColor = System.Drawing.Color.FromArgb(240, 255, 230);
					else
					{
						li.SubItems[3].BackColor = System.Drawing.Color.FromArgb(255, 250, 230);
						li.SubItems[2].BackColor = System.Drawing.Color.FromArgb(255, 250, 230);
					}

					// this tends to throw if this event is being handled while the window is being closed
					if (powerbalancerlog.Items.Count > 7)
						powerbalancerlog.Items.RemoveAt(0);
					powerbalancerlog.Items.Add(li);

					powerbalancer_forcedcount.Text = powermanager.ForceCount.ToString();
				}
				catch (Exception ex) { Logging.Stacktrace(ex); }
				finally
				{
					powerbalancerlog.EndUpdate();
				}
			}));
		}

		void WatchlistContextMenuOpen(object _, EventArgs _ea)
		{
			bool oneitem = true;
			oneitem = WatchlistRules.SelectedItems.Count == 1;

			try
			{
				foreach (ToolStripItem lsi in watchlistms.Items)
				{
					if (lsi.Text.Contains("Create")) continue;
					lsi.Enabled = oneitem;
				}

				if (oneitem)
				{
					var li = WatchlistRules.SelectedItems[0];
					var prc = Taskmaster.processmanager.GetControllerByName(li.SubItems[NameColumn].Text);
					if (prc != null)
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
				var oneitem = WatchlistRules.SelectedItems.Count == 1;
				if (oneitem)
				{
					WatchlistRules.BeginUpdate();

					var li = WatchlistRules.SelectedItems[0];
					var prc = Taskmaster.processmanager.GetControllerByName(li.SubItems[NameColumn].Text);
					if (prc != null)
					{
						watchlistenable.Enabled = true;
						watchlistenable.Checked = prc.Enabled = !watchlistenable.Checked;

						Log.Information("[" + prc.FriendlyName + "] " + (prc.Enabled ? HumanReadable.Generic.Enabled : HumanReadable.Generic.Disabled));

						prc.SaveConfig();

						WatchlistItemColor(li, prc);

						processmanager?.HastenScan(20);
					}

					WatchlistRules.EndUpdate();
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
					var li = WatchlistRules.SelectedItems[0];
					var name = li.SubItems[NameColumn].Text;
					var prc = Taskmaster.processmanager.GetControllerByName(name);

					using (var editdialog = new WatchlistEditWindow(prc)) // 1 = executable
					{
						var rv = editdialog.ShowDialog();
						// WatchlistEditLock = 0;

						if (rv == DialogResult.OK)
						{
							UpdateWatchlistRule(prc);
							processmanager?.HastenScan(60, sort:true);
							prc.Refresh();
						}
					}
				}
				catch (Exception ex) { Logging.Stacktrace(ex); }
			}
		}

		void AddWatchlistRule(object _, EventArgs _ea)
		{
			try
			{
				var ew = new WatchlistEditWindow();
				var rv = ew.ShowDialog();
				if (rv == DialogResult.OK)
				{
					var prc = ew.Controller;

					WatchlistRules.BeginUpdate();

					processmanager.AddController(prc);
					AddToWatchlistList(prc);

					WatchlistRules.EndUpdate();

					processmanager?.HastenScan(60, sort:true);
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

					var prc = Taskmaster.processmanager.GetControllerByName(li.SubItems[NameColumn].Text);
					if (prc != null)
					{

						if (SimpleMessageBox.ShowModal("Remove watchlist item", $"Really remove '{prc.FriendlyName}'", SimpleMessageBox.Buttons.AcceptCancel)
							== SimpleMessageBox.ResultType.OK)
						{
							processmanager.RemoveController(prc);

							prc.DeleteConfig();
							Log.Information("[" + prc.FriendlyName + "] Rule removed");
							WatchlistMap.TryRemove(prc, out ListViewItem _);
							WatchlistRules.Items.Remove(li);
						}
					}
				}
				catch (Exception ex) { Logging.Stacktrace(ex); }

				WatchlistColor();
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
					ProcessController prc = null;

					prc = processmanager.GetControllerByName(name);

					if (prc == null)
					{
						Log.Error("[" + name + "] Not found. Something's terribly wrong.");
						return;
					}

					var sbs = new System.Text.StringBuilder();

					sbs.Append("[").Append(prc.FriendlyName).Append("]").AppendLine();
					if (!string.IsNullOrEmpty(prc.Executable))
						sbs.Append("Image = ").Append(prc.Executable).AppendLine();
					if (!string.IsNullOrEmpty(prc.Path))
						sbs.Append("Path = ").Append(prc.Path).AppendLine();

					if (!string.IsNullOrEmpty(prc.Description))
						sbs.Append("Description = " + prc.Description);

					if (prc.IgnoreList != null)
						sbs.Append("Ignore = { ").Append(string.Join(", ", prc.IgnoreList)).Append(" }").AppendLine();
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

					if (prc.AffinityIdeal >= 0)
						sbs.Append("Affinity ideal = ").Append(prc.AffinityIdeal).AppendLine();

					if (prc.IOPriority >= 0)
						sbs.Append("IO priority = ").Append(prc.IOPriority).AppendLine();

					if (prc.PowerPlan != PowerInfo.PowerMode.Undefined)
					{
						sbs.Append(HumanReadable.Hardware.Power.Plan).Append(" = ").Append(PowerManager.GetModeName(prc.PowerPlan)).AppendLine();
						if (prc.BackgroundPowerdown)
							sbs.Append("Background powerdown = ").Append(prc.BackgroundPowerdown).AppendLine();
					}
					if (prc.Recheck > 0)
						sbs.Append("Recheck = ").Append(prc.Recheck).AppendLine();
					if (prc.AllowPaging)
						sbs.Append("Allow paging = ").Append(prc.AllowPaging).AppendLine();
					if (prc.Foreground != ForegroundMode.Ignore)
					{
						sbs.Append("Foreground mode = ").Append((int)prc.Foreground).AppendLine();
						if (prc.BackgroundPriority.HasValue)
							sbs.Append("Background priority = ").Append(prc.BackgroundPriority.Value.ToInt32()).AppendLine();
						if (prc.BackgroundAffinity >= 0)
							sbs.Append("Background affinity = ").Append(prc.BackgroundAffinity).AppendLine();
					}

					if (prc.ModifyDelay > 0)
						sbs.Append("Modify delay = ").Append(prc.ModifyDelay).AppendLine();

					if (prc.PathVisibility != PathVisibilityOptions.Invalid)
						sbs.Append("Path visibility = ").Append((int)prc.PathVisibility).AppendLine();

					if (prc.VolumeStrategy != AudioVolumeStrategy.Ignore)
					{
						sbs.Append("Volume = ").Append($"{prc.Volume:N2}").AppendLine();
						sbs.Append("Volume strategy = ").Append((int)prc.VolumeStrategy).AppendLine();
					}

					sbs.Append("Preference = ").Append(prc.OrderPreference).AppendLine();

					if (!prc.LogAdjusts)
						sbs.Append("Logging = false").AppendLine();

					if (!prc.Enabled)
						sbs.Append("Enabled = false").AppendLine();

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

		public async void TempScanStats(object _, StorageEventArgs ea)
		{
			if (!IsHandleCreated) return;
			BeginInvoke(new Action(() =>
			{
				tempObjectSize.Text = (ea.Stats.Size / 1_000_000).ToString();
				tempObjectCount.Text = (ea.Stats.Dirs + ea.Stats.Files).ToString();
			}));
		}

		ListView loglist = null;
		MenuStrip menu = null;

		public void FillLog()
		{
			MemoryLog.MemorySink.onNewEvent += NewLogReceived;

			// Log.Verbose("Filling GUI log.");
			loglist.BeginUpdate();
			foreach (var evmsg in MemoryLog.MemorySink.ToArray())
			{
				var li = loglist.Items.Add(evmsg.Message);
				if ((int)evmsg.Level >= (int)Serilog.Events.LogEventLevel.Error)
					li.ForeColor = System.Drawing.Color.Red;
			}
			loglist.EndUpdate();

			ShowLastLog();

			ResizeLogList(this, null);
		}

		public void Hook(ActiveAppManager aamon)
		{
			if (aamon == null) return;

			if (Taskmaster.Trace) Log.Verbose("Hooking active app manager.");

			activeappmonitor = aamon;

			if (Taskmaster.DebugForeground || Taskmaster.DebugProcesses)
				StartProcessDebug();
		}

		public void Hook(PowerManager pman)
		{
			if (pman == null) return;

			if (Taskmaster.Trace) Log.Verbose("Hooking power manager.");

			powermanager = pman;
			if (Taskmaster.DebugPower)
				powermanager.onAutoAdjustAttempt += PowerLoadHandler;

			powermanager.onBehaviourChange += PowerBehaviourDebugEvent;

			powermanager.onPlanChange += PowerPlanEvent;
			powermanager.onBehaviourChange += PowerBehaviourEvent;

			var bev = new PowerManager.PowerBehaviourEventArgs { Behaviour = powermanager.Behaviour };
			PowerBehaviourDebugEvent(this, bev); // populates powerbalancer_behaviourr
			PowerBehaviourEvent(this, bev); // populates pwbehaviour
			var pev = new PowerModeEventArgs(powermanager.CurrentMode);
			PowerPlanEvent(this, pev); // populates pwplan and pwcause

			if (Taskmaster.DebugPower)
			{
				PowerPlanDebugEvent(this, pev); // populates powerbalancer_plan
				powermanager.onPlanChange += PowerPlanDebugEvent;
			}
		}

		private async void PowerBehaviourEvent(object sender, PowerManager.PowerBehaviourEventArgs e)
		{
			if (!IsHandleCreated || disposed) return;

			BeginInvoke(new Action(() =>
			{
				pwbehaviour.Text = PowerManager.GetBehaviourName(e.Behaviour);
			}));
		}

		DateTimeOffset LastCauseTime = DateTimeOffset.MinValue;
		private void PowerPlanEvent(object sender, PowerModeEventArgs e)
		{
			if (!IsHandleCreated || disposed) return;

			BeginInvoke(new Action(() =>
			{
				powermodestatusbar.Text = pwmode.Text = PowerManager.GetModeName(e.NewMode);
				pwcause.Text = e.Cause != null ? e.Cause.ToString() : HumanReadable.Generic.Undefined;
				LastCauseTime = DateTimeOffset.UtcNow;
			}));
		}

		HardwareMonitor hw = null;
		public void Hook(HardwareMonitor hardware)
		{
			hw = hardware;
			hw.GPUPolling += GPULoadEvent;
		}

		public void Hook(CPUMonitor monitor)
		{
			cpumonitor = monitor;
			cpumonitor.onSampling += CPULoadHandler;
		}

		HealthMonitor healthmonitor = null;
		public void Hook(HealthMonitor hmon)
		{
			healthmonitor = hmon;

			UItimer.Tick += UpdateHealthMon;

			oldHealthReport = healthmonitor.Poll();
		}

		HealthReport oldHealthReport = null;

		int skipTransfers = 0;
		int skipSplits = 0;
		int skipDelays = 0;
		int skipQueues = 0;

		async void UpdateHealthMon(object sender, EventArgs e)
		{
			if (!IsHandleCreated || disposed) return;
			if (!nvmtransfers.Visible) return;

			try
			{
				var health = healthmonitor.Poll();

				float impact_transfers = (health.NVMTransfers / 500).Max(3); // expected to cause 0 to 2, and up to 4
				float impact_splits = health.SplitIO / 125; // expected to cause 0 to 2
				float impact_delay = health.NVMDelay / 12; // should cause 0 to 4 usually
				float impact_queue = (health.NVMQueue / 2).Max(4);
				float impact = impact_transfers + impact_splits + impact_delay + impact_queue;
				//float impact_faults = health.PageFaults;

				if (health.NVMTransfers >= float.Epsilon)
				{
					nvmtransfers.Text = $"{health.NVMTransfers:N1}{(health.NVMTransfers > 250 ? (health.NVMTransfers > 500 ? " extreme" : " high") : "")}";
					nvmtransfers.ForeColor = DefaultForeColor;
					skipTransfers = 0;
				}
				else
				{
					if (skipTransfers++ == 0)
						nvmtransfers.ForeColor = System.Drawing.SystemColors.InactiveCaptionText;
					else
						nvmtransfers.Text = "0.0";
				}

				if (health.SplitIO >= float.Epsilon)
				{
					nvmsplitio.Text = $"{health.SplitIO:N1}{(health.SplitIO > 20 ? (health.SplitIO >= health.NVMTransfers*0.5 ? " extreme" :  " high") : "")}";
					nvmsplitio.ForeColor = DefaultForeColor;
					skipSplits = 0;
				}
				else
				{
					if (skipSplits++ == 0)
						nvmsplitio.ForeColor = System.Drawing.SystemColors.InactiveCaptionText;
					else
						nvmsplitio.Text = "0.0";
				}

				if (health.NVMDelay >= float.Epsilon)
				{
					float delay = health.NVMDelay * 1000;
					nvmdelay.Text = $"{delay:N1} ms{(delay > 20 ? (health.NVMDelay > 50 ? " extreme" : " high") : "")}";
					nvmdelay.ForeColor = DefaultForeColor;
					skipDelays = 0;
				}
				else
				{
					if (skipDelays++ == 0)
						nvmdelay.ForeColor = System.Drawing.SystemColors.InactiveCaptionText;
					else
						nvmdelay.Text = "0 ms";
				}

				if (health.NVMQueue >= float.Epsilon)
				{
					nvmqueued.Text = $"{health.NVMQueue:N0}{(health.NVMQueue > 2 ? (health.NVMQueue > 8 ? " extreme" : " high") : "")}";
					nvmqueued.ForeColor = DefaultForeColor;
					skipQueues = 0;
				}
				else
				{
					if (skipQueues++ == 0)
						nvmqueued.ForeColor = System.Drawing.SystemColors.InactiveCaptionText;
					else
						nvmqueued.Text = "0";
				}

				//hardfaults.Text = !float.IsNaN(health.PageInputs) ? $"{health.PageInputs / health.PageFaults:N1} %" : HumanReadable.Generic.NotAvailable;

				oldHealthReport = health;
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		public async void PowerBehaviourDebugEvent(object _, PowerManager.PowerBehaviourEventArgs ea)
		{
			if (!IsHandleCreated || disposed) return;

			if (!Taskmaster.DebugPower) return;

			BeginInvoke(new Action(() =>
			{
				powerbalancer_behaviour.Text = PowerManager.GetBehaviourName(ea.Behaviour);
				if (ea.Behaviour != PowerManager.PowerBehaviour.Auto)
					powerbalancerlog.Items.Clear();
			}));
		}

		public async void PowerPlanDebugEvent(object _, PowerModeEventArgs ea)
		{
			if (!IsHandleCreated || disposed) return;

			if (!Taskmaster.DebugPower) return;

			BeginInvoke(new Action(() =>
			{
				powerbalancer_plan.Text = PowerManager.GetModeName(ea.NewMode);
			}));
		}

		public void UpdateNetworkDevices(object _, EventArgs _ea)
		{
			if (!IsHandleCreated || disposed) return;

			InetStatusLabel(netmonitor.InternetAvailable);
			NetStatusLabel(netmonitor.NetworkAvailable);

			BeginInvoke(new Action(() =>
			{
				NetworkDevices.BeginUpdate();
				try
				{
					NetworkDevices.Items.Clear();

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
						NetworkDevices.Items.Add(li);
					}
				}
				finally
				{
					NetworkDevices.EndUpdate();
				}
			}));

			// Tray?.Tooltip(2000, "Internet " + (net.InternetAvailable ? "available" : "unavailable"), "Taskmaster", net.InternetAvailable ? ToolTipIcon.Info : ToolTipIcon.Warning);
		}

		public void Hook(NetManager net)
		{
			if (net == null) return; // disabled

			if (Taskmaster.Trace) Log.Verbose("Hooking network monitor.");

			netmonitor = net;

			UpdateNetworkDevices(this, null);

			netmonitor.InternetStatusChange += InetStatus;
			netmonitor.IPChanged += UpdateNetworkDevices;
			netmonitor.NetworkStatusChange += NetStatus;
			netmonitor.onSampling += NetSampleHandler;
		}

		void NetSampleHandler(object _, NetDeviceTrafficEventArgs ea)
		{
			if (!IsHandleCreated || disposed) return;

			BeginInvoke(new Action(() =>
			{
				NetworkDevices.BeginUpdate();

				try
				{
					NetworkDevices.Items[ea.Traffic.Index].SubItems[PacketDeltaColumn].Text = "+" + ea.Traffic.Delta.Unicast;
					NetworkDevices.Items[ea.Traffic.Index].SubItems[ErrorDeltaColumn].Text = "+" + ea.Traffic.Delta.Errors;
					if (ea.Traffic.Delta.Errors > 0)
						NetworkDevices.Items[ea.Traffic.Index].SubItems[ErrorDeltaColumn].ForeColor = System.Drawing.Color.OrangeRed;
					else
						NetworkDevices.Items[ea.Traffic.Index].SubItems[ErrorDeltaColumn].ForeColor = System.Drawing.SystemColors.ControlText;

					NetworkDevices.Items[ea.Traffic.Index].SubItems[ErrorTotalColumn].Text = ea.Traffic.Total.Errors.ToString();
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
				}

				NetworkDevices.EndUpdate();
			}));
		}

		int PacketDeltaColumn = 6;
		int ErrorDeltaColumn = 7;
		int ErrorTotalColumn = 8;

		void InetStatusLabel(bool available)
		{
			if (!IsHandleCreated || disposed) return;

			BeginInvoke(new Action(() =>
			{
				inetstatuslabel.Text = available ? HumanReadable.Hardware.Network.Connected : HumanReadable.Hardware.Network.Disconnected;
				// inetstatuslabel.BackColor = available ? System.Drawing.Color.LightGoldenrodYellow : System.Drawing.Color.Red;
				//inetstatuslabel.BackColor = available ? System.Drawing.SystemColors.Menu : System.Drawing.Color.Red;
			}));
		}

		public async void InetStatus(object _, InternetStatus ea)
		{
			InetStatusLabel(ea.Available);
		}

		public async void IPChange(object _, EventArgs ea)
		{

		}

		async void NetStatusLabel(bool available)
		{
			if (!IsHandleCreated || disposed) return;

			BeginInvoke(new Action(() =>
			{
				netstatuslabel.Text = available ? HumanReadable.Hardware.Network.Connected : HumanReadable.Hardware.Network.Disconnected;
				// netstatuslabel.BackColor = available ? System.Drawing.Color.LightGoldenrodYellow : System.Drawing.Color.Red;
				//netstatuslabel.BackColor = available ? System.Drawing.SystemColors.Menu : System.Drawing.Color.Red;
			}));
		}

		public async void NetStatus(object _, NetworkStatus ea)
		{
			NetStatusLabel(ea.Available);
		}

		// BUG: DO NOT LOG INSIDE THIS FOR FUCKS SAKE
		// it creates an infinite log loop
		public int MaxLogSize { get => MemoryLog.MemorySink.Max; private set => MemoryLog.MemorySink.Max = value; }

		void ClearLog()
		{
			loglist.BeginUpdate();
			//loglist.Clear();
			loglist.Items.Clear();
			MemoryLog.MemorySink.Clear();
			loglist.EndUpdate();
		}

		public async void NewLogReceived(object _, LogEventArgs ea)
		{
			if (!IsHandleCreated || disposed) return;

			if (LogIncludeLevel.MinimumLevel > ea.Level) return;

			if (!IsHandleCreated) return;
			BeginInvoke(new Action(() =>
			{
				loglist.BeginUpdate();

				var excessitems = Math.Max(0, (loglist.Items.Count - MaxLogSize));
				while (excessitems-- > 0)
					loglist.Items.RemoveAt(0);

				var li = loglist.Items.Add(ea.Message);
				if ((int)ea.Level >= (int)Serilog.Events.LogEventLevel.Error)
					li.ForeColor = System.Drawing.Color.Red;
				li.EnsureVisible();

				loglist.EndUpdate();
			}));
		}

		const string uiconfig = "UI.ini";

		void SaveUIState()
		{
			if (!IsHandleCreated) return;

			Invoke(new Action(() =>
			{
				try
				{
					if (WatchlistRules.Columns.Count == 0) return;

					var cfg = Taskmaster.Config.Load(uiconfig);
					var cols = cfg.Config["Columns"];

					List<int> appWidths = new List<int>(WatchlistRules.Columns.Count);
					for (int i = 0; i < WatchlistRules.Columns.Count; i++)
						appWidths.Add(WatchlistRules.Columns[i].Width);
					cols["Apps"].IntValueArray = appWidths.ToArray();

					if (Taskmaster.NetworkMonitorEnabled)
					{
						List<int> ifaceWidths = new List<int>(NetworkDevices.Columns.Count);
						for (int i = 0; i < NetworkDevices.Columns.Count; i++)
							ifaceWidths.Add(NetworkDevices.Columns[i].Width);
						cols["Interfaces"].IntValueArray = ifaceWidths.ToArray();
					}

					if (Taskmaster.MicrophoneMonitorEnabled)
					{
						List<int> micWidths = new List<int>(AudioInputs.Columns.Count);
						for (int i = 0; i < AudioInputs.Columns.Count; i++)
							micWidths.Add(AudioInputs.Columns[i].Width);
						cols["Mics"].IntValueArray = micWidths.ToArray();
					}

					var uistate = cfg.Config["Tabs"];
					uistate["Open"].IntValue = tabLayout.SelectedIndex;

					var windows = cfg.Config["Windows"];
					windows["Main"].IntValueArray = new int[] { Bounds.Left, Bounds.Top, Bounds.Width, Bounds.Height };

					cfg.MarkDirty();
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
				}
			}));
		}

		bool disposed = false;
		protected override void Dispose(bool disposing)
		{
			if (disposed) return;

			base.Dispose(disposing);

			if (disposing)
			{
				if (Taskmaster.Trace) Log.Verbose("Disposing main window...");

				if (MemoryLog.MemorySink != null)
					MemoryLog.MemorySink.onNewEvent -= NewLogReceived; // unnecessary?

				rescanRequest = null;

				try
				{
					if (powermanager != null)
					{
						powermanager.onAutoAdjustAttempt -= PowerLoadHandler;
						powermanager.onBehaviourChange -= PowerBehaviourDebugEvent;
						powermanager.onPlanChange -= PowerPlanDebugEvent;
						powermanager = null;
					}
				}
				catch { }

				try
				{
					if (cpumonitor != null)
					{
						cpumonitor.onSampling -= CPULoadHandler;
						cpumonitor = null;
					}
				}
				catch { }

				try
				{
					if (hw != null)
					{
						hw.GPUPolling -= GPULoadEvent;
						hw = null;
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
						storagemanager.onTempScan -= TempScanStats;
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
						netmonitor.InternetStatusChange -= InetStatus;
						netmonitor.NetworkStatusChange -= NetStatus;
						netmonitor.IPChanged -= UpdateNetworkDevices;
						netmonitor.onSampling -= NetSampleHandler;
						netmonitor = null;
					}
				}
				catch { }

				try
				{
					if (micmon != null)
					{
						micmon.VolumeChanged -= VolumeChangeDetected;
						micmon = null;
					}
				}
				catch { }

				WatchlistSearchTimer.Dispose();
				UItimer.Dispose();
				exitwaitlist?.Dispose();
				ExitWaitlistMap?.Clear();
			}

			disposed = true;
		}
	}
}