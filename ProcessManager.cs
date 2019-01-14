﻿//
// ProcessManager.cs
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
using System.Management;
using System.Threading.Tasks;
using MKAh;
using Serilog;

namespace Taskmaster
{
	sealed public class ProcessingCountEventArgs : EventArgs
	{
		/// <summary>
		/// Adjustment to previous total.
		/// </summary>
		public int Adjust { get; set; } = 0;
		/// <summary>
		/// Total items being processed.
		/// </summary>
		public int Total { get; set; } = 0;

		public ProcessingCountEventArgs(int count, int total)
		{
			Adjust = count;
			Total = total;
		}
	}

	sealed public class HandlingStateChangeEventArgs : EventArgs
	{
		public ProcessEx Info { get; set; } = null;

		public HandlingStateChangeEventArgs(ProcessEx info)
		{
			Debug.Assert(info != null, "ProcessEx is not assigned");
			Info = info;
		}
	}

	sealed public class ProcessManager : IDisposable
	{
		/// <summary>
		/// Watch rules
		/// </summary>
		ConcurrentDictionary<ProcessController, int> Watchlist = new ConcurrentDictionary<ProcessController, int>();

		public static bool WMIPolling { get; private set; } = true;
		public static int WMIPollDelay { get; private set; } = 5;

		// ctor, constructor
		public ProcessManager()
		{
			Log.Information("<CPU> Logical cores: " + CPUCount);

			AllCPUsMask = Convert.ToInt32(Math.Pow(2, CPUCount) - 1 + double.Epsilon);

			//allCPUsMask = 1;
			//for (int i = 0; i < CPUCount - 1; i++)
			//	allCPUsMask = (allCPUsMask << 1) | 1;

			Log.Information("<CPU> Full CPU mask: " + Convert.ToString(AllCPUsMask, 2) + " (" + AllCPUsMask + " = OS control)");

			loadConfig();

			loadWatchlist();

			InitWMIEventWatcher();

			ProcessDetectedEvent += ProcessTriage;

			if (BatchProcessing)
			{
				BatchProcessingTimer = new System.Timers.Timer(1000 * 5);
				BatchProcessingTimer.Elapsed += BatchProcessingTick;
			}

			HandlingStateChange += CollectProcessHandlingStatistics;

			if (ScanFrequency != TimeSpan.Zero) ScanTimer = new System.Threading.Timer(TimedScan, null, TimeSpan.FromSeconds(5), ScanFrequency);

			if (Taskmaster.DebugProcesses) Log.Information("<Process> Component Loaded.");

			Taskmaster.DisposalChute.Push(this);
		}

		public ProcessController[] getWatchlist()
		{
			return Watchlist.Keys.ToArray();
		}

		// TODO: Need an ID mapping
		public ProcessController getWatchedController(string name)
		{
			foreach (var item in Watchlist.Keys)
			{
				if (item.FriendlyName.Equals(name, StringComparison.InvariantCultureIgnoreCase))
					return item;
			}

			return null;
		}

		/// <summary>
		/// Number of watchlist items with path restrictions.
		/// </summary>
		int WatchlistWithPath = 0;
		int WatchlistWithHybrid = 0;

		/// <summary>
		/// Executable name to ProcessControl mapping.
		/// </summary>
		ConcurrentDictionary<string, ProcessController> ExeToController = new ConcurrentDictionary<string, ProcessController>();

		public ProcessController getController(ProcessEx info)
		{
			if (info.Controller != null) return info.Controller; // unnecessary, but...

			if (ExeToController.TryGetValue(info.Name.ToLowerInvariant(), out ProcessController rv))
				return info.Controller = rv;

			if (!string.IsNullOrEmpty(info.Path))
				return info.Controller = getWatchedPath(info);

			return null;
		}

		public static int CPUCount = Environment.ProcessorCount;
		public static int AllCPUsMask = Convert.ToInt32(Math.Pow(2, CPUCount) - 1 + double.Epsilon);

		//int ProcessModifyDelay = 4800;

		public int DefaultBackgroundPriority = 1;
		public int DefaultBackgroundAffinity = 0;

		ActiveAppManager activeappmonitor = null;
		public void Hook(ActiveAppManager aamon)
		{
			activeappmonitor = aamon;
			activeappmonitor.ActiveChanged += ForegroundAppChangedEvent;
		}

		PowerManager powermanager = null;
		public void Hook(PowerManager power)
		{
			powermanager = power;
			powermanager.onBehaviourChange += PowerBehaviourEvent;
		}

		string freememoryignore = null;
		void FreeMemoryTick(object _, ProcessModificationEventArgs ea)
		{
			try
			{
				if (!string.IsNullOrEmpty(freememoryignore) && ea.Info.Name.Equals(freememoryignore, StringComparison.InvariantCultureIgnoreCase))
					return;

				if (Taskmaster.DebugMemory) Log.Debug("<Process> Paging: " + ea.Info.Name + " (#" + ea.Info.Id + ")");

				NativeMethods.EmptyWorkingSet(ea.Info.Process.Handle);
			}
			catch { } // process.Handle may throw which we don't care about
		}

		List<int> ignorePids = new List<int>(6);

		public void Ignore(int pid)
		{
			ignorePids.Add(pid);
			if (ignorePids.Count > 5) ignorePids.RemoveAt(0);
		}

		public void Unignore(int pid)
		{
			ignorePids.Remove(pid);
		}

		public async Task FreeMemory(string executable = null, bool quiet = false, int ignorePid = -1)
		{
			if (!Taskmaster.PagingEnabled) return;

			if (string.IsNullOrEmpty(executable))
			{
				if (Taskmaster.DebugPaging && !quiet)
					Log.Debug("<Process> Paging applications to free memory...");
			}
			else
			{
				var procs = Process.GetProcessesByName(executable); // unnecessary maybe?
				if (procs.Length == 0)
				{
					Log.Error(executable + " not found, not freeing memory for it.");
					return;
				}

				foreach (var prc in procs)
				{
					try
					{
						if (executable.Equals(prc.ProcessName))
						{
							ignorePid = prc.Id;
							break;
						}
					}
					catch { } // ignore
				}

				if (Taskmaster.DebugPaging && !quiet)
					Log.Debug("<Process> Paging applications to free memory for: " + executable);
			}

			//await Task.Delay(0).ConfigureAwait(false);

			freememoryignore = executable;
			await FreeMemoryInternal(ignorePid).ConfigureAwait(false);
			freememoryignore = string.Empty;
		}

		public async Task FreeMemory(int ignorePid = -1)
		{
			try
			{
				await FreeMemoryInternal(ignorePid).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		async Task FreeMemoryInternal(int ignorePid = -1)
		{
			MemoryManager.Update();
			ulong b1 = MemoryManager.FreeBytes;
			//var b1 = MemoryManager.Free;

			try
			{
				ScanPaused = true;

				// TODO: Pause Scan until we're done
				ProcessDetectedEvent += FreeMemoryTick;

				await Scan(ignorePid).ConfigureAwait(false); // TODO: Call for this to happen otherwise
				ScanPaused = false;

				// TODO: Wait a little longer to allow OS to Actually page stuff. Might not matter?
				//var b2 = MemoryManager.Free;
				MemoryManager.Update();
				ulong b2 = MemoryManager.FreeBytes;

				Log.Information("<Memory> Paging complete, observed memory change: " +
					HumanInterface.ByteString((long)(b2 - b1), true, iec: true));
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				ProcessDetectedEvent -= FreeMemoryTick;
			}
		}

		bool ScanPaused = false;
		/// <summary>
		/// Spawn separate thread to run program scanning.
		/// </summary>
		async void TimedScan(object _)
		{
			try
			{
				if (disposed) // HACK: dumb timers be dumb
				{
					try
					{
						ScanTimer?.Dispose();
					}
					catch (ObjectDisposedException) { }
					return;
				}

				if (Taskmaster.Trace) Log.Verbose("Rescan requested.");
				if (ScanPaused) return;
				// this stays on UI thread for some reason

				Task.Run(() => Scan()).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				Log.Error("Stopping periodic scans");
				ScanTimer.Change(System.Threading.Timeout.InfiniteTimeSpan, System.Threading.Timeout.InfiniteTimeSpan);
			}
		}

		/// <summary>
		/// Event fired by Scan and WMI new process
		/// </summary>
		public event EventHandler<ProcessModificationEventArgs> ProcessDetectedEvent;
		public event EventHandler ScanStartEvent;
		public event EventHandler ScanEndEvent;

		public event EventHandler<ProcessModificationEventArgs> ProcessModified;

		public event EventHandler<ProcessingCountEventArgs> HandlingCounter;
		public event EventHandler<ProcessModificationEventArgs> ProcessStateChange;

		public event EventHandler<HandlingStateChangeEventArgs> HandlingStateChange;

		int scan_lock = 0;

		public async void HastenScan()
		{
			if (DateTimeOffset.UtcNow.TimeTo(NextScan).TotalSeconds > 3) // skip if the next scan is to happen real soon
				ScanTimer.Change(TimeSpan.Zero, ScanFrequency);
		}

		public async Task Scan(int ignorePid = -1)
		{
			var now = DateTimeOffset.UtcNow;

			CleanWaitForExitList(); // HACK

			if (!Atomic.Lock(ref scan_lock)) return;

			LastScan = now;
			NextScan = now.Add(ScanFrequency);

			if (Taskmaster.DebugFullScan) Log.Debug("<Process> Full Scan: Start");

			await Task.Delay(0).ConfigureAwait(false); // asyncify

			ScanStartEvent?.Invoke(this, null);

			if (!SystemProcessId(ignorePid)) Ignore(ignorePid);

			var procs = Process.GetProcesses();
			int count = procs.Length - 2; // -2 for Idle&System
			Debug.Assert(count > 0, "System has no running processes"); // should be impossible to fail
			SignalProcessHandled(count); // scan start

			var i = 0;
			foreach (var process in procs)
			{
				++i;

				string name = string.Empty;
				int pid = 0;

				try
				{
					name = process.ProcessName;
					pid = process.Id;
				}
				catch { continue; } // process inaccessible (InvalidOperationException or NotSupportedException)

				if (IgnoreProcessID(pid) || IgnoreProcessName(name) || pid == Process.GetCurrentProcess().Id)
					continue;

				if (Taskmaster.DebugFullScan)
					Log.Verbose("<Process> Checking [" + i + "/" + count + "] " + name + " (#" + pid + ")");

				var timer = Stopwatch.StartNew();

				ProcessDetectedEvent?.Invoke(this, new ProcessModificationEventArgs
				{
					Info = new ProcessEx() { Process = process, Id = pid, Name = name, Path = null, Timer = timer },
					State = ProcessRunningState.Found,
				});
			}

			if (Taskmaster.DebugFullScan) Log.Debug("<Process> Full Scan: Complete");

			SignalProcessHandled(-count); // scan done

			ScanEndEvent?.Invoke(this, null);

			if (!SystemProcessId(ignorePid)) Unignore(ignorePid);

			Atomic.Unlock(ref scan_lock);
		}

		static int BatchDelay = 2500;
		/// <summary>
		/// In seconds.
		/// </summary>
		public static TimeSpan ScanFrequency { get; private set; } = TimeSpan.Zero;
		public static DateTimeOffset LastScan { get; private set; } = DateTimeOffset.MinValue;
		public static DateTimeOffset NextScan { get; set; } = DateTimeOffset.MinValue;
		static bool BatchProcessing; // = false;
		static int BatchProcessingThreshold = 5;
		// static bool ControlChildren = false; // = false;

		readonly System.Threading.Timer ScanTimer = null;

		public bool ValidateController(ProcessController prc)
		{
			var rv = true;

			if (prc.Priority.HasValue && prc.ForegroundOnly && prc.BackgroundPriority.Value.ToInt32() >= prc.Priority.Value.ToInt32())
			{
				prc.SetForegroundOnly(false);
				Log.Warning("[" + prc.FriendlyName + "] Background priority equal or higher than foreground priority, ignoring.");
			}

			if (string.IsNullOrEmpty(prc.Executable) && string.IsNullOrEmpty(prc.Path))
			{
				Log.Warning("[" + prc.FriendlyName + "] Executable and Path missing; ignoring.");
				rv = false;
			}

			// SANITY CHECKING
			if (!string.IsNullOrEmpty(prc.ExecutableFriendlyName))
			{
				if (IgnoreProcessName(prc.ExecutableFriendlyName))
				{
					if (Taskmaster.ShowInaction && Taskmaster.DebugProcesses)
						Log.Warning(prc.Executable ?? prc.ExecutableFriendlyName + " in ignore list; all changes denied.");

					// rv = false; // We'll leave the config in.
				}
				else if (ProtectedProcessName(prc.ExecutableFriendlyName))
				{
					if (Taskmaster.ShowInaction && Taskmaster.DebugProcesses)
						Log.Warning(prc.Executable ?? prc.ExecutableFriendlyName + " in protected list; priority changing denied.");
				}
			}

			return rv;
		}

		public void SaveController(ProcessController prc)
		{
			if (string.IsNullOrEmpty(prc.Executable))
			{
				WatchlistWithPath += 1;
			}
			else
			{
				ExeToController.TryAdd(prc.ExecutableFriendlyName.ToLowerInvariant(), prc);

				if (!string.IsNullOrEmpty(prc.Path))
					WatchlistWithHybrid += 1;
			}

			Watchlist.TryAdd(prc, 0);

			if (Taskmaster.Trace) Log.Verbose("[" + prc.FriendlyName + "] Match: " + (prc.Executable ?? prc.Path) + ", " +
				(prc.Priority.HasValue ? Readable.ProcessPriority(prc.Priority.Value) : HumanReadable.Generic.NotAvailable) +
				", Mask:" + (prc.AffinityMask >= 0 ? prc.AffinityMask.ToString() : HumanReadable.Generic.NotAvailable) +
				", Recheck: " + prc.Recheck + "s, FgOnly: " + prc.ForegroundOnly.ToString());
		}

		void loadConfig()
		{
			if (Taskmaster.DebugProcesses) Log.Information("<Process> Loading configuration...");

			var corecfg = Taskmaster.Config.Load(Taskmaster.coreconfig);

			var perfsec = corecfg.Config["Performance"];

			bool dirtyconfig = false, modified = false;
			// ControlChildren = coreperf.GetSetDefault("Child processes", false, out tdirty).BoolValue;
			// dirtyconfig |= tdirty;
			BatchProcessing = perfsec.GetSetDefault("Batch processing", false, out modified).BoolValue;
			perfsec["Batch processing"].Comment = "Process management works in delayed batches instead of immediately.";
			dirtyconfig |= modified;
			Log.Information("<Process> Batch processing: " + (BatchProcessing ? HumanReadable.Generic.Enabled : HumanReadable.Generic.Disabled));
			if (BatchProcessing)
			{
				BatchDelay = perfsec.GetSetDefault("Batch processing delay", 2500, out modified).IntValue.Constrain(500, 15000);
				dirtyconfig |= modified;
				Log.Information("<Process> Batch processing delay: " + $"{BatchDelay / 1000:N1}s");
				BatchProcessingThreshold = perfsec.GetSetDefault("Batch processing threshold", 5, out modified).IntValue.Constrain(1, 30);
				dirtyconfig |= modified;
				Log.Information("<Process> Batch processing threshold: " + BatchProcessingThreshold);
			}

			// OBSOLETE
			if (perfsec.Contains("Rescan frequency"))
			{
				perfsec.Remove("Rescan frequency");
				dirtyconfig |= true;
				Log.Debug("<Process> Obsoleted INI cleanup: Rescan frequency");
			}
			// DEPRECATED
			if (perfsec.Contains("Rescan everything frequency"))
			{
				try
				{
					perfsec.GetSetDefault("Scan frequency", perfsec.TryGet("Rescan everything frequency")?.IntValue ?? 15, out _);
				}
				catch { } // non int value
				perfsec.Remove("Rescan everything frequency");
				dirtyconfig |= true;
				Log.Debug("<Process> Deprecated INI cleanup: Rescan everything frequency");
			}

			var tscan = perfsec.GetSetDefault("Scan frequency", 15, out modified).IntValue.Constrain(0, 360);
			if (tscan > 0) ScanFrequency = TimeSpan.FromSeconds(tscan.Constrain(5, 360));
			perfsec["Scan frequency"].Comment = "Frequency (in seconds) at which we scan for processes. 0 disables.";
			dirtyconfig |= modified;

			if (ScanFrequency != TimeSpan.Zero)
				Log.Information("<Process> Scan every " + ScanFrequency + " seconds.");

			// --------------------------------------------------------------------------------------------------------

			WMIPolling = perfsec.GetSetDefault("WMI event watcher", false, out modified).BoolValue;
			perfsec["WMI event watcher"].Comment = "Use WMI to be notified of new processes starting.\nIf disabled, only rescanning everything will cause processes to be noticed.";
			dirtyconfig |= modified;
			WMIPollDelay = perfsec.GetSetDefault("WMI poll delay", 5, out modified).IntValue.Constrain(1, 30);
			perfsec["WMI poll delay"].Comment = "WMI process watcher delay (in seconds).  Smaller gives better results but can inrease CPU usage. Accepted values: 1 to 30.";
			dirtyconfig |= modified;

			Log.Information("<Process> New instance event watcher: " + (WMIPolling ? HumanReadable.Generic.Enabled : HumanReadable.Generic.Disabled));
			if (WMIPolling)
				Log.Information("<Process> New instance poll delay: " + $"{WMIPollDelay}s");

			// --------------------------------------------------------------------------------------------------------

			var fgpausesec = corecfg.Config["Foreground Focus Lost"];
			// RestoreOriginal = fgpausesec.GetSetDefault("Restore original", false, out modified).BoolValue;
			// dirtyconfig |= modified;
			DefaultBackgroundPriority = fgpausesec.GetSetDefault("Default priority", 2, out modified).IntValue.Constrain(0, 4);
			fgpausesec["Default priority"].Comment = "Default is normal to avoid excessive loading times while user is alt-tabbed.";
			dirtyconfig |= modified;
			// OffFocusAffinity = fgpausesec.GetSetDefault("Affinity", 0, out modified).IntValue;
			// dirtyconfig |= modified;
			// OffFocusPowerCancel = fgpausesec.GetSetDefault("Power mode cancel", true, out modified).BoolValue;
			// dirtyconfig |= modified;

			DefaultBackgroundAffinity = fgpausesec.GetSetDefault("Default affinity", 14, out modified).IntValue.Constrain(0, 254);
			dirtyconfig |= modified;

			// --------------------------------------------------------------------------------------------------------

			// Taskmaster.cfg["Applications"]["Ignored"].StringValueArray = IgnoreList;
			var ignsetting = corecfg.Config["Applications"];
			if (!ignsetting.Contains(HumanReadable.Generic.Ignore)) // DEPRECATED UPGRADE PATH
			{
				string[] tnewIgnoreList = ignsetting["Ignored"].StringValueArray;
				ignsetting[HumanReadable.Generic.Ignore].StringValueArray = tnewIgnoreList;
				dirtyconfig = true;
				ignsetting.Remove("Ignored");
			}
			string[] newIgnoreList = ignsetting.GetSetDefault(HumanReadable.Generic.Ignore, IgnoreList, out modified)?.StringValueArray;

			ignsetting.PreComment = "Special hardcoded protection applied to: consent, winlogon, wininit, and csrss.\nThese are vital system services and messing with them can cause severe system malfunctioning.\nMess with the ignore list at your own peril.";

			if (newIgnoreList != null)
			{
				IgnoreList = newIgnoreList;
				Log.Information("<Process> Custom ignore list loaded.");
				dirtyconfig |= modified;
			}
			if (Taskmaster.DebugProcesses) Log.Debug("<Process> Ignore list: " + string.Join(", ", IgnoreList));

			IgnoreSystem32Path = ignsetting.GetSetDefault("Ignore System32", true, out modified).BoolValue;
			ignsetting["Ignore System32"].Comment = "Ignore programs in %SYSTEMROOT%/System32 folder.";
			dirtyconfig |= modified;

			if (dirtyconfig) corecfg.MarkDirty();
		}

		void loadWatchlist()
		{
			Log.Information("<Process> Loading watchlist...");

			var appcfg = Taskmaster.Config.Load(watchfile);

			bool dirtyconfig = false;

			if (appcfg.Config.SectionCount == 0)
			{
				Taskmaster.Config.Unload(appcfg);

				Log.Warning("<Process> Watchlist empty; copying example list.");

				// DEFAULT CONFIGURATION
				var tpath = System.IO.Path.Combine(Taskmaster.datapath, watchfile);
				try
				{
					System.IO.File.WriteAllText(tpath, Properties.Resources.Watchlist);
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
					throw;
				}

				appcfg = Taskmaster.Config.Load(watchfile);
			}

			// --------------------------------------------------------------------------------------------------------

			foreach (SharpConfig.Section section in appcfg.Config)
			{
				if (!section.Contains("Image") && !section.Contains(HumanReadable.System.Process.Path))
				{
					// TODO: Deal with incorrect configuration lacking image
					Log.Warning("'" + section.Name + "' has no image nor path.");
					continue;
				}

				if (!section.Contains(HumanReadable.System.Process.Priority) && !section.Contains(HumanReadable.System.Process.Affinity) && !section.Contains(HumanReadable.Hardware.Power.Mode))
				{
					// TODO: Deal with incorrect configuration lacking these things
					Log.Warning("[" + section.Name + "] No priority, affinity, nor power plan. Ignoring.");
					continue;
				}

				var aff = section.TryGet(HumanReadable.System.Process.Affinity)?.IntValue ?? -1;
				if (aff > AllCPUsMask)
				{
					Log.Warning("[" + section.Name + "] Affinity(" + aff + ") is malconfigured. Ignoring.");
					//aff = Bit.And(aff, allCPUsMask); // at worst case results in 1 core used
					// TODO: Count bits, make 2^# assumption about intended cores but scale it to current core count.
					//		Shift bits to allowed range. Assume at least one core must be assigned, and in case of holes at least one core must be unassigned.
					aff = -1; // ignore
				}
				var prio = section.TryGet(HumanReadable.System.Process.Priority)?.IntValue ?? -1;
				ProcessPriorityClass? prioR = null;
				if (prio >= 0) prioR = ProcessHelpers.IntToPriority(prio);

				var pmodes = section.TryGet(HumanReadable.Hardware.Power.Mode)?.StringValue ?? null;
				var pmode = PowerManager.GetModeByName(pmodes);
				if (pmode == PowerInfo.PowerMode.Custom)
				{
					Log.Warning("'" + section.Name + "' has unrecognized power plan: " + pmodes);
					pmode = PowerInfo.PowerMode.Undefined;
				}

				ProcessPriorityStrategy priostrat = ProcessPriorityStrategy.None;
				if (prioR != null)
				{
					var priorityStrat = section.TryGet(HumanReadable.System.Process.PriorityStrategy)?.IntValue.Constrain(0, 3) ?? -1;

					if (priorityStrat > 0)
						priostrat = (ProcessPriorityStrategy)priorityStrat;
					else // 0
						prioR = null; // invalid data
				}

				ProcessAffinityStrategy affStrat = ProcessAffinityStrategy.None;
				if (aff > 0)
				{
					int affinityStrat = section.TryGet(HumanReadable.System.Process.AffinityStrategy)?.IntValue.Constrain(0, 3) ?? 2;
					affStrat = (ProcessAffinityStrategy)affinityStrat;
				}

				float volume = section.TryGet("Volume")?.FloatValue.Constrain(0.0f, 1.0f) ?? 0.5f;
				AudioVolumeStrategy volumestrategy = (AudioVolumeStrategy)(section.TryGet("Volume strategy")?.IntValue.Constrain(0, 5) ?? 0);

				int baff = section.TryGet("Background affinity")?.IntValue ?? -1;
				ProcessPriorityClass? bprio = null;
				int bpriot = section.TryGet("Background priority")?.IntValue ?? -1;
				if (bpriot >= 0) bprio = ProcessHelpers.IntToPriority(bpriot);

				PathVisibilityOptions pvis = PathVisibilityOptions.Process;
				pvis = (PathVisibilityOptions)(section.TryGet("Path visibility")?.IntValue.Constrain(-1, 3) ?? 0);

				string[] tignorelist = (section.TryGet(HumanReadable.Generic.Ignore)?.StringValueArray ?? null);
				if (tignorelist != null && tignorelist.Length > 0)
				{
					for (int i = 0; i < tignorelist.Length; i++)
						tignorelist[i] = tignorelist[i].ToLowerInvariant();
				}
				else
					tignorelist = null;

				var prc = new ProcessController(section.Name, prioR, (aff == 0 ? AllCPUsMask : aff))
				{
					Enabled = section.TryGet(HumanReadable.Generic.Enabled)?.BoolValue ?? true,
					Executable = section.TryGet("Image")?.StringValue ?? null,
					Description = section.TryGet(HumanReadable.Generic.Description)?.StringValue ?? null,
					// friendly name is filled automatically
					PriorityStrategy = priostrat,
					AffinityStrategy = affStrat,
					Path = (section.TryGet(HumanReadable.System.Process.Path)?.StringValue ?? null),
					ModifyDelay = (section.TryGet("Modify delay")?.IntValue ?? 0),
					//BackgroundIO = (section.TryGet("Background I/O")?.BoolValue ?? false), // Doesn't work
					Recheck = (section.TryGet("Recheck")?.IntValue ?? 0),
					PowerPlan = pmode,
					PathVisibility = pvis,
					BackgroundPriority = bprio,
					BackgroundAffinity = baff,
					BackgroundPowerdown = (section.TryGet("Background powerdown")?.BoolValue ?? false),
					IgnoreList = tignorelist,
					AllowPaging = (section.TryGet("Allow paging")?.BoolValue ?? false),
				};

				prc.SetForegroundOnly(section.TryGet("Foreground only")?.BoolValue ?? false);

				if (!prc.ForegroundOnly)
				{
					// sanity checking for bad config
					prc.BackgroundAffinity = -1;
					prc.BackgroundPriority = null;
					prc.BackgroundPowerdown = false;
				}

				prc.LogAdjusts = section.TryGet("Logging")?.BoolValue ?? true;

				prc.Volume = volume.Constrain(0f, 1f);
				prc.VolumeStrategy = volumestrategy;

				// TODO: Blurp about following configuration errors
				if (prc.AffinityMask < 0) prc.AffinityStrategy = ProcessAffinityStrategy.None;
				else if (prc.AffinityStrategy == ProcessAffinityStrategy.None) prc.AffinityMask = -1;

				if (!prc.Priority.HasValue) prc.PriorityStrategy = ProcessPriorityStrategy.None;
				else if (prc.PriorityStrategy == ProcessPriorityStrategy.None) prc.Priority = null;

				int[] resize = section.TryGet("Resize")?.IntValueArray ?? null; // width,height
				if (resize != null && resize.Length == 4)
				{
					int resstrat = section.TryGet("Resize strategy")?.IntValue.Constrain(0, 3) ?? -1;
					if (resstrat < 0) resstrat = 0;

					prc.ResizeStrategy = (WindowResizeStrategy)resstrat;

					prc.Resize = new System.Drawing.Rectangle(resize[0], resize[1], resize[2], resize[3]);
				}

				prc.SanityCheck();

				AddController(prc);

				// OBSOLETE
				if (section.Contains("Rescan"))
				{
					section.Remove("Rescan");
					dirtyconfig |= true;
				}

				// cnt.Children &= ControlChildren;

				// cnt.delay = section.Contains("delay") ? section["delay"].IntValue : 30; // TODO: Add centralized default delay
				// cnt.delayIncrement = section.Contains("delay increment") ? section["delay increment"].IntValue : 15; // TODO: Add centralized default increment
			}

			if (dirtyconfig) appcfg.MarkDirty();

			// --------------------------------------------------------------------------------------------------------

			Log.Information("<Process> Name-based watchlist: " + (ExeToController.Count - WatchlistWithHybrid) + " items");
			Log.Information("<Process> Path-based watchlist: " + WatchlistWithPath + " items");
			Log.Information("<Process> Hybrid watchlist: " + WatchlistWithHybrid + " items");
		}

		public void AddController(ProcessController prc)
		{
			if (ValidateController(prc))
			{
				if (Taskmaster.PersistentWatchlistStats) prc.LoadStats();
				SaveController(prc);
				prc.Modified += ProcessModifiedProxy;
				//prc.Paused += ProcessPausedProxy;
				//prc.Resumed += ProcessResumedProxy;
				prc.Paused += ProcessWaitingExitProxy;
				prc.WaitingExit += ProcessWaitingExitProxy;
			}
		}

		private void ProcessWaitingExitProxy(object _, ProcessModificationEventArgs ea)
		{
			try
			{
				WaitForExit(ea.Info);
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				Log.Error("Unregistering '" + ea.Info.Controller.FriendlyName + "' exit wait proxy");
				ea.Info.Controller.Paused -= ProcessWaitingExitProxy;
				ea.Info.Controller.WaitingExit -= ProcessWaitingExitProxy;
			}
		}

		private void ProcessResumedProxy(object _, ProcessModificationEventArgs _ea)
		{
			throw new NotImplementedException();
		}

		private void ProcessPausedProxy(object _, ProcessModificationEventArgs _ea)
		{
			throw new NotImplementedException();
		}

		void ProcessModifiedProxy(object _, ProcessModificationEventArgs ea)
		{
			try
			{
				ProcessModified?.Invoke(_, ea);
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				Log.Error("Unregistering '" + ea.Info.Controller.FriendlyName + "' process modified proxy");
				ea.Info.Controller.Modified -= ProcessModifiedProxy;
			}
		}

		public void RemoveController(ProcessController prc)
		{
			if (!string.IsNullOrEmpty(prc.ExecutableFriendlyName))
				ExeToController.TryRemove(prc.ExecutableFriendlyName.ToLowerInvariant(), out _);

			Watchlist.TryRemove(prc, out _);

			prc.Modified -= ProcessModifiedProxy;
			prc.Paused -= ProcessPausedProxy;
			prc.Resumed -= ProcessResumedProxy;
		}

		ConcurrentDictionary<int, ProcessEx> WaitForExitList = new ConcurrentDictionary<int, ProcessEx>();

		void WaitForExitTriggered(ProcessEx info, ProcessRunningState state = ProcessRunningState.Exiting)
		{
			Debug.Assert(info.Controller != null, "ProcessController not defined");
			Debug.Assert(!SystemProcessId(info.Id), "WaitForExitTriggered for system process");

			try
			{
				if (Taskmaster.DebugForeground || Taskmaster.DebugPower)
				{
					Log.Debug("[" + info.Controller.FriendlyName + "] " + info.Name +
						" (#" + info.Id + ") exited [Power: " + info.PowerWait + ", Active: " + info.ActiveWait + "]");
				}

				if (info.ActiveWait)
					ForegroundWaitlist.TryRemove(info.Id, out _);

				WaitForExitList.TryRemove(info.Id, out _);

				info.Controller?.End(info);

				ProcessStateChange?.Invoke(this, new ProcessModificationEventArgs() { Info = info, State = state });

				CleanWaitForExitList(); // HACK
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		int CleanWaitForExitList_lock = 0;
		async void CleanWaitForExitList()
		{
			if (!Atomic.Lock(ref CleanWaitForExitList_lock)) return;

			await Task.Delay(TimeSpan.FromSeconds(15)).ConfigureAwait(false);

			Task.Run(async () =>
			{
				try
				{
					foreach (var info in WaitForExitList.Values)
					{
						try
						{
							if (!info.Process.HasExited) continue; // only reason we keep this
						}
						catch { }

						Log.Warning("[" + info.Controller.FriendlyName + "] " + info.Name + " (#" + info.Id + ") exited without notice; Cleaning.");
						WaitForExitTriggered(info);
					}
				}
				finally
				{
					Atomic.Unlock(ref CleanWaitForExitList_lock);
				}
			}).ConfigureAwait(false);
		}

		public void PowerBehaviourEvent(object _, PowerManager.PowerBehaviourEventArgs ea)
		{
			try
			{
				if (ea.Behaviour == PowerManager.PowerBehaviour.Manual)
					CancelPowerWait();
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				Log.Error("Unregistering power behaviour event");
				powermanager.onBehaviourChange -= PowerBehaviourEvent;
			}
		}

		public void CancelPowerWait()
		{
			var cancelled = 0;

			Stack<ProcessEx> clearList = null;

			if (WaitForExitList.Count == 0) return;

			clearList = new Stack<ProcessEx>();
			foreach (var info in WaitForExitList.Values)
			{
				if (info.PowerWait)
				{
					// don't clear if we're still waiting for foreground
					if (!info.ActiveWait)
					{
						try
						{
							info.Process.EnableRaisingEvents = false;
						}
						catch { } // nope, this throwing just verifies we're doing the right thing

						clearList.Push(info);
						cancelled++;
					}
				}
			}

			while (clearList.Count > 0)
				WaitForExitTriggered(clearList.Pop(), ProcessRunningState.Cancel);

			if (cancelled > 0)
				Log.Information("Cancelled power mode wait on " + cancelled + " process(es).");
		}

		public void WaitForExit(ProcessEx info)
		{
			Debug.Assert(info.Controller != null, "No controller attached");

			if (WaitForExitList.TryAdd(info.Id, info))
			{
				bool exithooked = false;

				try
				{
					info.Process.EnableRaisingEvents = true;
					info.Process.Exited += (_, _ea) => WaitForExitTriggered(info);
					// TODO: Just in case check if it exited while we were doing this.
					exithooked = true;

					info.Process.Refresh();
					if (info.Process.HasExited) throw new InvalidOperationException("Exited after we registered for it?");
				}
				catch (InvalidOperationException) // already exited
				{
					WaitForExitList.TryRemove(info.Id, out _);
				}
				catch (Exception ex) // unknown error
				{
					Logging.Stacktrace(ex);
					WaitForExitList.TryRemove(info.Id, out _);
				}

				if (exithooked)
					ProcessStateChange?.Invoke(this, new ProcessModificationEventArgs() { Info = info, State = ProcessRunningState.Undefined });
			}
		}

		public ProcessEx[] getExitWaitList() => WaitForExitList.Values.ToArray(); // copy is good here

		ProcessController PreviousForegroundController = null;
		ProcessEx PreviousForegroundInfo;

		ConcurrentDictionary<int, ProcessController> ForegroundWaitlist = new ConcurrentDictionary<int, ProcessController>(1, 6);

		public void ForegroundAppChangedEvent(object _, WindowChangedArgs ev)
		{
			try
			{
				if (Taskmaster.DebugForeground)
					Log.Verbose("<Process> Foreground Received: #" + ev.Id);

				if (PreviousForegroundInfo != null)
				{
					if (PreviousForegroundInfo.Id != ev.Id) // testing previous to current might be superfluous
					{
						if (PreviousForegroundController != null)
						{
							//Log.Debug("PUTTING PREVIOUS FOREGROUND APP to BACKGROUND");
							if (PreviousForegroundController.ForegroundOnly)
								PreviousForegroundController.Pause(PreviousForegroundInfo);

							ProcessStateChange?.Invoke(this, new ProcessModificationEventArgs() { Info = PreviousForegroundInfo, State = ProcessRunningState.Paused });
						}
					}
					else
					{
						if (Taskmaster.ShowInaction && Taskmaster.DebugForeground)
							Log.Debug("<Foreground> Changed but the app is still the same. Curious, don't you think?");
					}
				}

				if (ForegroundWaitlist.TryGetValue(ev.Id, out ProcessController prc))
				{
					if (WaitForExitList.TryGetValue(ev.Id, out ProcessEx info))
					{
						if (Taskmaster.Trace && Taskmaster.DebugForeground)
							Log.Debug("[" + prc.FriendlyName + "] " + info.Name + " (#" + info.Id + ") on foreground!");

						if (prc.ForegroundOnly) prc.Resume(info);

						ProcessStateChange?.Invoke(this, new ProcessModificationEventArgs() { Info = info, State = ProcessRunningState.Resumed });

						PreviousForegroundInfo = info;
						PreviousForegroundController = prc;

						return;
					}
				}

				if (Taskmaster.DebugForeground && Taskmaster.Trace)
					Log.Debug("<Process> NULLING PREVIOUS FOREGRDOUND");

				PreviousForegroundInfo = null;
				PreviousForegroundController = null;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				Log.Error("Unregistering foreground changed event");
				activeappmonitor.ActiveChanged -= ForegroundAppChangedEvent;
			}
		}

		// TODO: ADD CACHE: pid -> process name, path, process

		public ProcessController getWatchedPath(ProcessEx info)
		{
			ProcessController matchedprc = null;

			try
			{
				if (info.Process.HasExited) // can throw
				{
					if (Taskmaster.ShowInaction && Taskmaster.DebugProcesses)
						Log.Verbose(info.Name + " (#" + info.Id + ") has already exited.");
					return null; // return ProcessState.Invalid;
				}
			}
			catch (InvalidOperationException ex)
			{
				Log.Fatal("INVALID ACCESS to Process");
				Logging.Stacktrace(ex);
				return null; // return ProcessState.AccessDenied; //throw; // no point throwing
			}
			catch (System.ComponentModel.Win32Exception ex)
			{
				if (ex.NativeErrorCode != 5) // what was this?
					Log.Warning("Access error: " + info.Name + " (#" + info.Id + ")");
				return null; // return ProcessState.AccessDenied; // we don't care wwhat this error is
			}

			if (string.IsNullOrEmpty(info.Path) && !ProcessUtility.FindPath(info))
				return null; // return ProcessState.Error;

			if (IgnoreSystem32Path && info.Path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.System)))
			{
				if (Taskmaster.ShowInaction && Taskmaster.DebugProcesses)
					Log.Debug("<Process/Path> " + info.Name + " (#" + info.Id + ") in System32, ignoring");
				return null;
			}

			// TODO: This needs to be FASTER
			foreach (ProcessController prc in Watchlist.Keys)
			{
				if (!prc.Enabled) continue;
				if (string.IsNullOrEmpty(prc.Path)) continue;

				if (!string.IsNullOrEmpty(prc.Executable))
				{
					if (prc.Executable.Equals(info.Name, StringComparison.InvariantCultureIgnoreCase))
					{
						if (Taskmaster.DebugPaths)
							Log.Debug("[" + prc.FriendlyName + "] Path+Exe matched.");
					}
					else
						continue; // CheckPathWatch does not handle combo path+exes
				}

				if (prc.MatchPath(info.Path))
				{
					if (Taskmaster.DebugPaths)
						Log.Verbose("[" + prc.FriendlyName + "] (CheckPathWatch) Matched at: " + info.Path);

					matchedprc = prc;
					break;
				}
			}

			return matchedprc;
		}

		async void CheckPathWatch(ProcessEx info)
		{
			Debug.Assert(info.Process != null, "No Process attached");
			Debug.Assert(info.Controller == null, "CheckPathWatch received a process that already was matched");

			await Task.Delay(0).ConfigureAwait(false);

			if ((info.Controller = getWatchedPath(info)) != null)
			{
				try
				{
					info.Controller.Touch(info);
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
					return;
				}

				ForegroundWatch(info); // already called?
			}
		}

		public static string[] ProtectList { get; private set; } = {
			"consent", // UAC, user account control prompt
			"winlogon", // core system
			"wininit", // core system
			"csrss", // client server runtime, subsystems
			"dwm", // desktop window manager
			"taskmgr", // task manager
			"LogonUI", // session lock
			"services", // service control manager
		};
		public static string[] IgnoreList { get; private set; } = {
			"svchost", // service host
			"taskeng", // task scheduler
			"consent", // UAC, user account control prompt
			"taskhost", // task scheduler process host
			"rundll32", // 
			"dllhost", //
			//"conhost", // console host, hosts command prompts (cmd.exe)
			"dwm", // desktop window manager
			"wininit", // core system
			"csrss", // client server runtime, subsystems
			"winlogon", // core system
			"services", // service control manager
			"explorer", // file manager
			"taskmgr", // task manager
			"audiodg" // audio device isolation
		};

		// %SYSTEMROOT%\System32 (Environment.SpecialFolder.System)
		bool IgnoreSystem32Path { get; set; } = true;

		/// <summary>
		/// Tests if the process ID is core system process (0[idle] or 4[system]) that can never be valid program.
		/// </summary>
		/// <returns>true if the pid should not be used</returns>
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		public static bool SystemProcessId(int pid) => pid <= 4;

		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		bool IgnoreProcessID(int pid) => SystemProcessId(pid) || ignorePids.Contains(pid);

		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		public static bool IgnoreProcessName(string name) => IgnoreList.Any(item => item.Equals(name, StringComparison.InvariantCultureIgnoreCase));

		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		public static bool ProtectedProcessName(string name) => ProtectList.Any(item => item.Equals(name, StringComparison.InvariantCultureIgnoreCase));
		// %SYSTEMROOT%

		/*
		void ChildController(ProcessEx ci)
		{
			//await System.Threading.Tasks.Task.Yield();
			// TODO: Cache known children so we don't look up the parent? Reliable mostly with very unique/long executable names.

			Debug.Assert(ci.Process != null, "ChildController was given a null process.");

			// TODO: Cache known children so we don't look up the parent? Reliable mostly with very unique/long executable names.
			Stopwatch n = Stopwatch.StartNew();
			int ppid = -1;
			try
			{
				// TODO: Deal with intermediary processes (double parent)
				if (ci.Process == null) ci.Process = Process.GetProcessById(ci.Id);
				ppid = ci.Process.ParentProcessId();
			}
			catch // PID not found
			{
				Log.Warning("Couldn't get parent process for {ChildProcessName} (#{ChildProcessID})", ci.Name, ci.Id);
				return;
			}

			if (!IgnoreProcessID(ppid)) // 0 and 4 are system processes, we don't care about their children
			{
				Process parentproc;
				try
				{
					parentproc = Process.GetProcessById(ppid);
				}
				catch // PID not found
				{
					Log.Verbose("Parent PID(#{ProcessID}) not found", ppid);
					return;
				}

				if (IgnoreProcessName(parentproc.ProcessName)) return;
				bool denyChange = ProtectedProcessName(parentproc.ProcessName);

				ProcessController parent = null;
				if (!denyChange)
				{
					if (execontrol.TryGetValue(ci.Process.ProcessName.ToLower()), out parent))
					{
						try
						{
							if (!parent.ChildPriorityReduction && (ProcessHelpers.PriorityToInt(ci.Process.PriorityClass) > ProcessHelpers.PriorityToInt(parent.ChildPriority)))
							{
								Log.Verbose(ci.Name + " (#" + ci.Id + ") has parent " + parent.FriendlyName + " (#" + parentproc.Id + ") has non-reductable higher than target priority.");
							}
							else if (parent.Children
									 && ProcessHelpers.PriorityToInt(ci.Process.PriorityClass) != ProcessHelpers.PriorityToInt(parent.ChildPriority))
							{
								ProcessPriorityClass oldprio = ci.Process.PriorityClass;
								try
								{
									ci.Process.SetLimitedPriority(parent.ChildPriority, true, true);
								}
								catch (Exception e)
								{
									Console.WriteLine(e.StackTrace);
									Log.Warning("Uncaught exception; Failed to modify priority for '{ProcessName}'", ci.Process.ProcessName);
								}
								Log.Information("{ChildProcessName} (#{ChildProcessID}) child of {ParentFriendlyName} (#{ParentProcessID}) Priority({OldChildPriority} -> {NewChildPriority})",
												ci.Name, ci.Id, parent.FriendlyName, ppid, oldprio, ci.Process.PriorityClass);
							}
							else
							{
								Log.Verbose(ci.Name + " (#" + ci.Id + ") has parent " + parent.FriendlyName + " (#" + parentproc.Id + ")");
							}
						}
						catch
						{
							Log.Warning("[{FriendlyName}] {Exe} (#{Pid}) access failure.", parent.FriendlyName, ci.Name, ci.Id);
						}
					}
				}
			}
			n.Stop();
			Statistics.Parentseektime += n.Elapsed.TotalSeconds;
			Statistics.ParentSeeks += 1;
		}
		*/

		/// <summary>
		/// Add to foreground watch list if necessary.
		/// </summary>
		void ForegroundWatch(ProcessEx info)
		{
			var prc = info.Controller;

			if (!prc.ForegroundOnly) return;

			var keyexists = false;

			if (ForegroundWaitlist.TryAdd(info.Id, prc))
				WaitForExit(info);
			else
				keyexists = true;

			if (Taskmaster.Trace && Taskmaster.DebugForeground)
			{
				if (!keyexists)
					Log.Debug("[" + prc.FriendlyName + "] " + info.Name + " (#" + info.Id + ") added to foreground watchlist.");
				else
					Log.Debug("[" + prc.FriendlyName + "] " + info.Name + " (#" + info.Id + ") already in foreground watchlist.");
			}

			ProcessStateChange?.Invoke(this, new ProcessModificationEventArgs() { Info = info, State = keyexists ? ProcessRunningState.Found : ProcessRunningState.Starting });
		}

		async void CollectProcessHandlingStatistics(object _, HandlingStateChangeEventArgs ea)
		{
			try
			{
				if (ea.Info.Handled || ea.Info.Exited)
				{
					//case ProcessHandlingState.Unmodified:
					ea.Info.Timer.Stop();
					long elapsed = ea.Info.Timer.ElapsedMilliseconds;
					int delay = ea.Info.Controller?.ModifyDelay ?? 0;
					ulong time = Convert.ToUInt64(elapsed - Math.Min(delay, elapsed)); // to avoid overflows
					if (Taskmaster.Trace) Debug.WriteLine("Modify time: " + $"{time} ms + {delay} ms delay");
					Statistics.TouchTime = time;
					Statistics.TouchTimeLongest = Math.Max(time, Statistics.TouchTimeLongest);
					Statistics.TouchTimeShortest = Math.Min(time, Statistics.TouchTimeShortest);
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				Log.Error("Disabling collecting of process handling statistics");
				HandlingStateChange -= CollectProcessHandlingStatistics;
			}
		}

		// TODO: This should probably be pushed into ProcessController somehow.
		async void ProcessTriage(object _, ProcessModificationEventArgs ea)
		{
			try
			{
				await Task.Delay(0).ConfigureAwait(false);

				ea.Info.State = ProcessHandlingState.Triage;

				HandlingStateChange?.Invoke(this, new HandlingStateChangeEventArgs(ea.Info));

				if (string.IsNullOrEmpty(ea.Info.Name))
				{
					Log.Warning("#" + ea.Info.Id + " details unaccessible, ignored.");
					return; // ProcessState.AccessDenied;
				}

				if (ExeToController.TryGetValue(ea.Info.Name.ToLowerInvariant(), out var prc))
				{
					ea.Info.Controller = prc; // fill

					if (!prc.Enabled)
					{
						if (Taskmaster.DebugProcesses)
							Log.Debug("[" + prc.FriendlyName + "] Matched, but rule disabled; ignoring.");
						ea.Info.State = ProcessHandlingState.Abandoned;
						return;
					}

					// await System.Threading.Tasks.Task.Delay(ProcessModifyDelay).ConfigureAwait(false);

					try
					{
						prc.Modify(ea.Info);
					}
					catch (Exception ex)
					{
						Log.Fatal("[" + prc.FriendlyName + "] " + ea.Info.Name + " (#" + ea.Info.Id + ") MASSIVE FAILURE!!!");
						Logging.Stacktrace(ex);
						return; // ProcessState.Error;
					}

					ForegroundWatch(ea.Info);
					return;
				}

				if (WatchlistWithPath > 0 && ea.Info.Controller == null)
				{
					CheckPathWatch(ea.Info);
					return;
				}

				/*
				if (ControlChildren) // this slows things down a lot it seems
					ChildController(info);
				*/
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				Log.Error("Unregistering process triage");
				ProcessDetectedEvent -= ProcessTriage;
			}
			finally
			{
				HandlingStateChange?.Invoke(this, new HandlingStateChangeEventArgs(ea.Info));
			}
		}

		readonly object batchprocessing_lock = new object();
		int processListLockRestart = 0;
		List<ProcessEx> ProcessBatch = new List<ProcessEx>();
		readonly System.Timers.Timer BatchProcessingTimer = null;

		void BatchProcessingTick(object _, EventArgs _ea)
		{
			try
			{
				lock (batchprocessing_lock)
				{
					if (ProcessBatch.Count == 0)
					{
						BatchProcessingTimer.Stop();

						if (Taskmaster.DebugProcesses) Log.Debug("<Process> New instance timer stopped.");

						return;
					}
				}

				Task.Run(new Action(() => NewInstanceBatchProcessing())).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				Log.Error("Unregistering batch processing");
				BatchProcessingTimer.Elapsed -= BatchProcessingTick;
			}
		}

		void NewInstanceBatchProcessing()
		{
			//await Task.Delay(0).ConfigureAwait(false);
			try
			{
				List<ProcessEx> list = new List<ProcessEx>();

				lock (batchprocessing_lock)
				{
					if (ProcessBatch.Count == 0) return;

					BatchProcessingTimer.Stop();

					processListLockRestart = 0;
					Utility.Swap(ref list, ref ProcessBatch);
				}

				foreach (var info in list)
					ProcessDetectedEvent?.Invoke(this, new ProcessModificationEventArgs { Info = info });

				SignalProcessHandled(-(list.Count)); // batch done
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		public int Handling { get; private set; } = 0; // this isn't used for much...

		void SignalProcessHandled(int adjust)
		{
			Handling += adjust;
			HandlingCounter?.Invoke(this, new ProcessingCountEventArgs(adjust, Handling));
		}

		// This needs to return faster
		async void NewInstanceTriage(object _, EventArrivedEventArgs ea)
		{
			var now = DateTimeOffset.UtcNow;
			var timer = Stopwatch.StartNew();

			int pid = -1;
			string name = string.Empty;
			string path = string.Empty;
			ProcessEx info = null;

			try
			{
				SignalProcessHandled(1); // wmi new instance

				var wmiquerytime = Stopwatch.StartNew();
				// TODO: Instance groups?
				try
				{
					var targetInstance = ea.NewEvent.Properties["TargetInstance"].Value as ManagementBaseObject;
					//var tpid = targetInstance.Properties["Handle"].Value as int?; // doesn't work for some reason
					pid = Convert.ToInt32(targetInstance.Properties["Handle"].Value as string);
					path = targetInstance.Properties["ExecutablePath"].Value as string;
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
					return;
				}
				finally
				{
					wmiquerytime.Stop();
					Statistics.WMIPollTime += wmiquerytime.Elapsed.TotalSeconds;
					Statistics.WMIPolling += 1;
				}

				if (IgnoreProcessID(pid)) return; // We just don't care

				if (!string.IsNullOrEmpty(path))
					name = System.IO.Path.GetFileNameWithoutExtension(path);

				if (string.IsNullOrEmpty(name))
				{
					// likely process exited too fast
					if (Taskmaster.DebugProcesses && Taskmaster.ShowInaction) Log.Debug("<<WMI>> Failed to acquire neither process name nor process Id");
					return;
				}

				if (IgnoreProcessName(name)) return;

				if (ProcessUtility.GetInfo(pid, out info, path: path, getPath: true))
				{
					info.Timer = timer;
					NewInstanceTriagePhaseTwo(info);
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				Log.Error("Unregistering new instance triage");
				watcher.EventArrived -= NewInstanceTriage;
			}
			finally
			{
				HandlingStateChange?.Invoke(this, new HandlingStateChangeEventArgs(info ?? new ProcessEx { Id = pid, Timer = timer, State = ProcessHandlingState.Invalid }));

				SignalProcessHandled(-1); // done with it
			}
		}

		void NewInstanceTriagePhaseTwo(ProcessEx info)
		{
			//await Task.Delay(0).ConfigureAwait(false);

			try
			{
				info.Process = Process.GetProcessById(info.Id);
			}
			catch (ArgumentException)
			{
				info.State = ProcessHandlingState.Exited;
				if (Taskmaster.ShowInaction && Taskmaster.DebugProcesses)
					Log.Verbose("Caught #" + info.Id + " but it vanished.");
				return;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				return;
			}

			if (string.IsNullOrEmpty(info.Name))
			{
				try
				{
					// This happens only when encountering a process with elevated privileges, e.g. admin
					// TODO: Mark as admin process?
					info.Name = info.Process.ProcessName;
				}
				catch
				{
					Log.Error("Failed to retrieve name of process #" + info.Id);
					return;
				}
			}

			if (Taskmaster.Trace) Log.Verbose("Caught: " + info.Name + " (#" + info.Id + ") at: " + info.Path);

			// info.Process.StartTime; // Only present if we started it

			if (BatchProcessing)
			{
				lock (batchprocessing_lock)
				{
					try
					{
						ProcessBatch.Add(info);

						// Delay process timer a few times.
						if (BatchProcessingTimer.Enabled &&
							(++processListLockRestart < BatchProcessingThreshold))
							BatchProcessingTimer.Stop();
						BatchProcessingTimer.Start();
					}
					catch (Exception ex)
					{
						Logging.Stacktrace(ex);
					}
				}

				info.State = ProcessHandlingState.Batching;

				HandlingStateChange?.Invoke(this, new HandlingStateChangeEventArgs(info));
			}
			else
			{
				ProcessDetectedEvent?.Invoke(this, new ProcessModificationEventArgs { Info = info });
			}
		}

		ManagementEventWatcher watcher = null;
		void InitWMIEventWatcher()
		{
			if (!WMIPolling) return;

			// FIXME: doesn't seem to work when lots of new processes start at the same time.
			try
			{
				// Transition to permanent event listener?
				// https://msdn.microsoft.com/en-us/library/windows/desktop/aa393014(v=vs.85).aspx

				/*
				// Causes access denied error?
				ManagementEventWatcher w = null;
				WqlEventQuery q = new WqlEventQuery();
				q.EventClassName = "Win32_ProcessStartTrace";
				w = new ManagementEventWatcher(scope, q);
				w.EventArrived += NewInstanceTriage2;
				w.Start();
				*/

				// Test if listening for Win32_ProcessStartTrace is any better?
				// var tracequery = new System.Management.EventQuery("SELECT * FROM Win32_ProcessStartTrace");

				// var query = new System.Management.EventQuery("SELECT TargetInstance FROM __InstanceCreationEvent WITHIN 5 WHERE TargetInstance ISA 'Win32_Process'");
				watcher = new ManagementEventWatcher(
					new ManagementScope(@"\\.\root\CIMV2"),
					new EventQuery("SELECT * FROM __InstanceCreationEvent WITHIN " + WMIPollDelay + " WHERE TargetInstance ISA 'Win32_Process'")
					); // Avast cybersecurity causes this to throw an exception
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				throw new InitFailure("<<WMI>> Event watcher initialization failure");
			}

			if (watcher != null)
			{
				watcher.EventArrived += NewInstanceTriage;

				if (BatchProcessing) lock (batchprocessing_lock) BatchProcessingTimer.Start();

				watcher.Stopped += (_, _ea) =>
				{
					if (Taskmaster.DebugWMI)
						Log.Debug("<<WMI>> New instance watcher stopped.");
					// Restart it maybe? This probably happens when WMI service is stopped or restarted.?
				};

				try
				{
					watcher.Start();
					if (Taskmaster.DebugWMI)
						Log.Debug("<<WMI>> New instance watcher initialized.");
				}
				catch
				{
					Log.Fatal("<<WMI>> New instance watcher failed to initialize.");
					throw new InitFailure("New instance watched failed to initialize");
				}
			}
			else
			{
				Log.Error("Failed to initialize new instance watcher.");
				throw new InitFailure("New instance watcher not initialized");
			}
		}

		const string watchfile = "Watchlist.ini";

		int cleanup_lock = 0;
		/// <summary>
		/// Cleanup.
		/// </summary>
		/// <remarks>
		/// Locks: waitforexit_lock
		/// </remarks>
		public void Cleanup()
		{
			if (!Atomic.Lock(ref cleanup_lock)) return; // cleanup already in progress

			// TODO: Verify that this is actually useful?

			if (User.IdleTime().TotalHours > 2d)
			{
				foreach (var prc in Watchlist.Keys)
					prc.Refresh();
			}

			Stack<ProcessEx> triggerList = null;
			try
			{
				var items = WaitForExitList.Values;
				foreach (var info in items)
				{
					try
					{
						info.Process.Refresh();
						info.Process.WaitForExit(20);
					}
					catch { } // ignore
				}

				System.Threading.Thread.Sleep(1000); // Meh

				triggerList = new Stack<ProcessEx>();
				foreach (var info in items)
				{
					try
					{
						info.Process.Refresh();
						info.Process.WaitForExit(20);
						if (info.Process.HasExited)
							triggerList.Push(info);
					}
					catch
					{
						//Logging.Stacktrace(ex);
						triggerList.Push(info);// potentially unwanted behaviour, but it's better this way
					}
				}

				if (triggerList != null)
				{
					while (triggerList.Count > 0)
						WaitForExitTriggered(triggerList.Pop()); // causes removal so can't be done in above loop
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				Atomic.Unlock(ref cleanup_lock);
				triggerList?.Clear();
			}
		}

		bool disposed; // = false;
		public void Dispose()
		{
			Dispose(true);
		}

		void Dispose(bool disposing)
		{
			if (disposed) return;

			if (disposing)
			{
				if (Taskmaster.Trace) Log.Verbose("Disposing process manager...");

				ProcessDetectedEvent = null;
				ScanStartEvent = null;
				ScanEndEvent = null;
				ProcessModified = null;
				HandlingCounter = null;
				ProcessStateChange = null;
				HandlingStateChange = null;

				CancelPowerWait();
				WaitForExitList.Clear();

				if (powermanager != null)
				{
					powermanager.onBehaviourChange -= PowerBehaviourEvent;
					powermanager = null;
				}

				try
				{
					//watcher.EventArrived -= NewInstanceTriage;
					Utility.Dispose(ref watcher);

					if (activeappmonitor != null)
					{
						activeappmonitor.ActiveChanged -= ForegroundAppChangedEvent;
						activeappmonitor = null;
					}

					ScanTimer?.Dispose();
					BatchProcessingTimer?.Dispose();
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
					// throw; // would throw but this is dispose
				}

				SaveStats();

				try
				{
					ExeToController?.Clear();
					ExeToController = null;

					var wcfg = Taskmaster.Config.Load(watchfile);

					foreach (var prc in Watchlist.Keys)
						if (prc.NeedsSaving) prc.SaveConfig(wcfg);

					Watchlist?.Clear();
					Watchlist = null;
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
					// throw; // would throw but this is dispose
				}
			}

			disposed = true;
		}

		void SaveStats()
		{
			if (!Taskmaster.PersistentWatchlistStats) return;

			if (Taskmaster.Trace) Log.Verbose("Saving stats...");

			foreach (ProcessController prc in Watchlist.Keys)
				prc.SaveStats();
		}
	}
}