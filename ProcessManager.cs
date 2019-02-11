//
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
using System.Text;
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
		ProcessAnalyzer analyzer = null;

		/// <summary>
		/// Watch rules
		/// </summary>
		ConcurrentDictionary<ProcessController, int> Watchlist = new ConcurrentDictionary<ProcessController, int>();

		ConcurrentDictionary<int, DateTimeOffset> ScanBlockList = new ConcurrentDictionary<int, DateTimeOffset>();

		List<ProcessController> WatchlistCache = new List<ProcessController>();
		readonly object watchlist_lock = new object();

		public bool WMIPolling { get; private set; } = true;
		public int WMIPollDelay { get; private set; } = 5;

		public static TimeSpan? IgnoreRecentlyModified { get; set; } = TimeSpan.FromMinutes(30);

		// ctor, constructor
		public ProcessManager()
		{
			AllCPUsMask = Convert.ToInt32(Math.Pow(2, CPUCount) - 1 + double.Epsilon);

			if (Taskmaster.RecordAnalysis.HasValue) analyzer = new ProcessAnalyzer();

			//allCPUsMask = 1;
			//for (int i = 0; i < CPUCount - 1; i++)
			//	allCPUsMask = (allCPUsMask << 1) | 1;

			Log.Information("<CPU> Logical cores: " + CPUCount + ", full mask: " + Convert.ToString(AllCPUsMask, 2) + " (" + AllCPUsMask + " = OS control)");

			loadConfig();

			LoadWatchlist();

			InitWMIEventWatcher();

			var InitialScanDelay = TimeSpan.FromSeconds(5);
			NextScan = DateTimeOffset.UtcNow.Add(InitialScanDelay);
			if (ScanFrequency.HasValue)
			{
				ScanTimer = new System.Timers.Timer(ScanFrequency.Value.TotalMilliseconds);
				ScanTimer.Elapsed += TimedScan;
				Task.Run(() => Scan()).ContinueWith((_) => StartScanTimer()).ConfigureAwait(false);
			}

			MaintenanceTimer = new System.Timers.Timer(1_000 * 60 * 60 * 3); // every 3 hours
			MaintenanceTimer.Elapsed += CleanupTick;
			MaintenanceTimer.Start();

			if (Taskmaster.DebugProcesses) Log.Information("<Process> Component Loaded.");

			Taskmaster.DisposalChute.Push(this);
		}

		public ProcessController[] getWatchlist()
		{
			return Watchlist.Keys.ToArray();
		}

		// TODO: Need an ID mapping
		public ProcessController GetControllerByName(string friendlyname)
		{
			return (from prc in Watchlist.Keys where prc.FriendlyName.Equals(friendlyname, StringComparison.InvariantCultureIgnoreCase) select prc).FirstOrDefault();
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

		public bool GetController(ProcessEx info, out ProcessController prc)
		{
			if (info.Controller != null)
			{
				prc = info.Controller;
				return true;
			}

			if (ExeToController.TryGetValue(info.Name.ToLowerInvariant(), out prc))
				return true;

			if (!string.IsNullOrEmpty(info.Path) && GetPathController(info, out prc))
				return true;

			return false;
		}

		public static int CPUCount = Environment.ProcessorCount;
		public static int AllCPUsMask = Convert.ToInt32(Math.Pow(2, CPUCount) - 1 + double.Epsilon);

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

        ConcurrentDictionary<int, int> ignorePids = new ConcurrentDictionary<int, int>();

        public void Ignore(int pid) => ignorePids.TryAdd(pid, 0);

		public void Unignore(int pid) => ignorePids.TryRemove(pid, out _);

		int freemem_lock = 0;
		public async Task FreeMemory(string executable = null, bool quiet = false, int ignorePid = -1)
		{
			if (!Taskmaster.PagingEnabled) return;

			if (!Atomic.Lock(ref freemem_lock)) return;

			try
			{
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
							if (executable.Equals(prc.ProcessName, StringComparison.InvariantCultureIgnoreCase))
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
				FreeMemoryInternal(ignorePid);
				freememoryignore = string.Empty;
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				Atomic.Unlock(ref freemem_lock);
			}
		}

		void FreeMemoryInternal(int ignorePid = -1)
		{
			if (DisposedOrDisposing) return;

			MemoryManager.Update();
			ulong b1 = MemoryManager.FreeBytes;
			//var b1 = MemoryManager.Free;

			try
			{
				ScanTimer?.Stop();
				// TODO: Pause Scan until we're done

				Scan(ignorePid, freememory:true); // TODO: Call for this to happen otherwise

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

			StartScanTimer();
		}

		/// <summary>
		/// Spawn separate thread to run program scanning.
		/// </summary>
		async void TimedScan(object _, EventArgs _ea)
		{
			if (DisposedOrDisposing) return; // HACK: dumb timers be dumb

            try
            {
				if (Taskmaster.Trace) Log.Verbose("Rescan requested.");

                await Task.Delay(0).ConfigureAwait(false); // asyncify

                Scan();
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				Log.Error("Stopping periodic scans");
				ScanTimer?.Stop();
			}
		}

		//public event EventHandler ScanStartEvent;
		//public event EventHandler ScanEndEvent;

		public event EventHandler<ProcessModificationEventArgs> ProcessModified;

		public event EventHandler<ProcessingCountEventArgs> HandlingCounter;
		public event EventHandler<ProcessModificationEventArgs> ProcessStateChange;

		public event EventHandler<HandlingStateChangeEventArgs> HandlingStateChange;

		int hastenscan = 0;
		public async void HastenScan(int delay = 15, bool sort = false)
		{
			// delay is unused but should be used somehow

			if (!Atomic.Lock(ref hastenscan)) return;

			try
			{
				await Task.Delay(0).ConfigureAwait(false); // asyncify

				double nextscan = Math.Max(0, DateTimeOffset.UtcNow.TimeTo(NextScan).TotalSeconds);
				if (nextscan > 5) // skip if the next scan is to happen real soon
				{
					NextScan = DateTimeOffset.UtcNow;
					ScanTimer?.Stop();
					if (sort) SortWatchlist();

					await Task.Run(() => Scan()).ContinueWith((_) => StartScanTimer()).ConfigureAwait(false);
				}
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				Atomic.Unlock(ref hastenscan);
			}
		}

		void StartScanTimer()
		{
			// restart just in case
			ScanTimer?.Stop();
			ScanTimer?.Start();
			NextScan = DateTimeOffset.UtcNow.AddMilliseconds(ScanTimer.Interval);
		}

		int scan_lock = 0;
		bool Scan(int ignorePid = -1, bool freememory = false)
		{
			if (DisposedOrDisposing) return false;

			if (!Atomic.Lock(ref scan_lock)) return false;

			try
			{
				var now = DateTimeOffset.UtcNow;
				LastScan = now;
				NextScan = now.Add(ScanFrequency.Value);

				if (Taskmaster.DebugFullScan) Log.Debug("<Process> Full Scan: Start");

				//ScanStartEvent?.Invoke(this, null);

				if (!SystemProcessId(ignorePid)) Ignore(ignorePid);

				var procs = Process.GetProcesses();
				int foundprocs = procs.Length - 2; // -2 for Idle&System
				Debug.Assert(foundprocs > 0, "System has no running processes"); // should be impossible to fail
				SignalProcessHandled(foundprocs); // scan start

				int selfId = Process.GetCurrentProcess().Id;

				//var loaderOffload = new List<ProcessEx>();

				int count = 0;
				Parallel.ForEach(procs, (process) =>
				{
					++count;

					string name = string.Empty;
					int pid = -1;

					try
					{
						name = process.ProcessName;
						pid = process.Id;
					}
					catch
					{
						if (Taskmaster.ShowInaction && Taskmaster.DebugFullScan)
							Log.Debug($"<Process/Scan> Failed to retrieve info for process: {name} (#{pid})");
						return;
					} // process inaccessible (InvalidOperationException or NotSupportedException)

					if (ScanBlockList.TryGetValue(pid, out var found))
					{
						if (found.TimeTo(now).TotalSeconds < 5d)
							return;
						ScanBlockList.TryRemove(pid, out _);
					}

					try
					{
						if (IgnoreProcessID(pid) || IgnoreProcessName(name) || pid == selfId)
						{
							if (Taskmaster.ShowInaction && Taskmaster.DebugFullScan)
								Log.Debug($"<Process/Scan> Ignoring {name} (#{pid})");
							return;
						}

						if (Taskmaster.DebugFullScan) Log.Verbose($"<Process> Checking [{count}/{foundprocs}] {name} (#{pid})");

						ProcessEx info = null;
						if (WaitForExitList.TryGetValue(pid, out info))
						{
							Debug.WriteLine($"Re-using old ProcessEx: {info.Name} (#{info.Id})");
							info.Process.Refresh();
							if (info.Process.HasExited) // Stale, for some reason still kept
							{
								Debug.WriteLine("Re-using old ProcessEx - except not, stale");
								WaitForExitList.TryRemove(pid, out _);
								info = null;
							}
							else
								info.State = ProcessHandlingState.Triage; // still valid
						}

						if (info != null || ProcessUtility.GetInfo(pid, out info, process, null, name, null, getPath: true))
						{
							info.Timer = Stopwatch.StartNew();

							ProcessTriage(info);

							if (freememory)
							{
								MKAh.Utility.DiscardExceptions(() =>
								{
									if (!string.IsNullOrEmpty(freememoryignore) && info.Name.Equals(freememoryignore, StringComparison.InvariantCultureIgnoreCase))
										return;

									if (Taskmaster.DebugMemory) Log.Debug($"<Process> Paging: {info.Name} (#{info.Id})");

									NativeMethods.EmptyWorkingSet(info.Process.Handle); // process.Handle may throw which we don't care about
								});
							}

							HandlingStateChange?.Invoke(this, new HandlingStateChangeEventArgs(info));

							//if (!info.Exited) loaderOffload.Add(info);
						}
					}
					catch (OutOfMemoryException) { throw; }
					catch (Exception ex)
					{
						Logging.Stacktrace(ex);
					}
				});

				//SystemLoaderAnalysis(loaderOffload);

				if (Taskmaster.DebugFullScan) Log.Debug("<Process> Full Scan: Complete");

				SignalProcessHandled(-foundprocs); // scan done

				//ScanEndEvent?.Invoke(this, null);

				if (!SystemProcessId(ignorePid)) Unignore(ignorePid);
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				Atomic.Unlock(ref scan_lock);
			}

			return true;
		}

		int SystemLoader_lock = 0;
		DateTimeOffset LastLoaderAnalysis = DateTimeOffset.MinValue;

		async Task SystemLoaderAnalysis(List<ProcessEx> procs)
		{
			var now = DateTimeOffset.UtcNow;

			if (!Atomic.Lock(ref SystemLoader_lock)) return;

			// TODO: TEST IF ANY RESOURCE IS BEING DRAINED

			try
			{
				if (LastLoaderAnalysis.TimeTo(now).TotalMinutes < 1d) return;

				await Task.Delay(5_000).ConfigureAwait(false);

				long PeakWorkingSet = 0;
				ProcessEx PeakWorkingSetInfo = null;
				long HighestPrivate = 0;
				ProcessEx HighestPrivateInfo = null;

				// TODO: Combine results of multiprocess apps
				foreach (var info in procs)
				{
					try
					{
						info.Process.Refresh();
						if (info.Process.HasExited) continue;

						long ws = info.Process.PeakWorkingSet64, pm = info.Process.PrivateMemorySize64;
						if (ws > PeakWorkingSet)
						{
							PeakWorkingSet = ws;
							PeakWorkingSetInfo = info;
						}
						if (pm > HighestPrivate)
						{
							HighestPrivate = pm;
							HighestPrivateInfo = info;
						}
					}
					catch (OutOfMemoryException) { throw; }
					catch (Exception ex)
					{
						Logging.Stacktrace(ex);
					}
				}
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				LastLoaderAnalysis = DateTimeOffset.UtcNow;
				Atomic.Unlock(ref SystemLoader_lock);
			}
		}

		/// <summary>
		/// In seconds.
		/// </summary>
		public static TimeSpan? ScanFrequency { get; private set; } = TimeSpan.FromSeconds(180);
		DateTimeOffset LastScan { get; set; } = DateTimeOffset.MinValue; // UNUSED
		public DateTimeOffset NextScan { get; set; } = DateTimeOffset.MinValue;

		// static bool ControlChildren = false; // = false;

		readonly System.Timers.Timer ScanTimer = null;
		readonly System.Timers.Timer MaintenanceTimer = null;

		// move all this to prc.Validate() or prc.SanityCheck();
		bool ValidateController(ProcessController prc)
		{
			var rv = true;

			if (prc.Priority.HasValue && prc.BackgroundPriority.HasValue && prc.BackgroundPriority.Value.ToInt32() >= prc.Priority.Value.ToInt32())
			{
				prc.SetForegroundMode(ForegroundMode.Ignore);
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

		bool RecacheNeeded = true;

		void SaveController(ProcessController prc)
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
			lock (watchlist_lock) RecacheNeeded = true;

			if (Taskmaster.Trace) Log.Verbose("[" + prc.FriendlyName + "] Match: " + (prc.Executable ?? prc.Path) + ", " +
				(prc.Priority.HasValue ? Readable.ProcessPriority(prc.Priority.Value) : HumanReadable.Generic.NotAvailable) +
				", Mask:" + (prc.AffinityMask >= 0 ? prc.AffinityMask.ToString() : HumanReadable.Generic.NotAvailable) +
				", Recheck: " + prc.Recheck + "s, Foreground: " + prc.Foreground.ToString());
		}

		void loadConfig()
		{
			if (Taskmaster.DebugProcesses) Log.Information("<Process> Loading configuration...");

			var corecfg = Taskmaster.Config.Load(Taskmaster.coreconfig);

			var perfsec = corecfg.Config["Performance"];

			bool dirtyconfig = false, modified = false;
			// ControlChildren = coreperf.GetSetDefault("Child processes", false, out tdirty).BoolValue;
			// dirtyconfig |= tdirty;

			if (perfsec.Contains("Batch processing")) // DEPRECATED
			{
				perfsec.Remove("Batch processing");
				perfsec.Remove("Batch processing delay");
				perfsec.Remove("Batch processing threshold");
				dirtyconfig |= modified;
			}

			IgnoreRecentlyModified = TimeSpan.FromMinutes(perfsec.GetSetDefault("Ignore recently modified", 30, out modified).IntValue.Constrain(0, 24 * 60));
			perfsec["Ignore recently modified"].Comment = "Performance optimization. More notably this enables granting self-determination to apps that actually think they know better.";

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
			else ScanFrequency = null;
			perfsec["Scan frequency"].Comment = "Frequency (in seconds) at which we scan for processes. 0 disables.";
			dirtyconfig |= modified;

			if (ScanFrequency.HasValue)
				Log.Information("<Process> Scan every " + $"{ScanFrequency.Value.TotalSeconds:N0}" + " seconds.");

			// --------------------------------------------------------------------------------------------------------

			WMIPolling = perfsec.GetSetDefault("WMI event watcher", false, out modified).BoolValue;
			perfsec["WMI event watcher"].Comment = "Use WMI to be notified of new processes starting.\nIf disabled, only rescanning everything will cause processes to be noticed.";
			dirtyconfig |= modified;
			WMIPollDelay = perfsec.GetSetDefault("WMI poll delay", 5, out modified).IntValue.Constrain(1, 30);
			perfsec["WMI poll delay"].Comment = "WMI process watcher delay (in seconds).  Smaller gives better results but can inrease CPU usage. Accepted values: 1 to 30.";
			dirtyconfig |= modified;

			Log.Information("<Process> New instance event watcher: " + (WMIPolling ? HumanReadable.Generic.Enabled : HumanReadable.Generic.Disabled));
			if (WMIPolling) Log.Information("<Process> New instance poll delay: " + $"{WMIPollDelay}s");

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

			DefaultBackgroundAffinity = fgpausesec.GetSetDefault("Default affinity", 14, out modified).IntValue.Constrain(0, AllCPUsMask);
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

			if (IgnoreRecentlyModified.HasValue)
				Log.Information($"<Process> Ignore recently modified: {IgnoreRecentlyModified.Value.TotalMinutes:N1} minute cooldown");
			Log.Information($"<Process> Self-determination: {(IgnoreRecentlyModified.HasValue ? "Possible" : "Impossible")}");
		}

		void LoadWatchlist()
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
				if (aff > AllCPUsMask || aff < -1)
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
				if (aff >= 0)
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

				var prc = new ProcessController(section.Name, prioR, aff)
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
					Recheck = (section.TryGet("Recheck")?.IntValue ?? 0).Constrain(0, 300),
					PowerPlan = pmode,
					PathVisibility = pvis,
					BackgroundPriority = bprio,
					BackgroundAffinity = baff,
					IgnoreList = tignorelist,
					AllowPaging = (section.TryGet("Allow paging")?.BoolValue ?? false),
					Analyze = (section.TryGet("Analyze")?.BoolValue ?? false),
				};

				prc.ExclusiveMode = section.TryGet("Exclusive")?.BoolValue ?? false;

				prc.OrderPreference = section.TryGet("Preference")?.IntValue.Constrain(0, 100) ?? prc.OrderPreference;

				prc.IOPriority = section.TryGet("IO priority")?.IntValue.Constrain(0, 2) ?? int.MinValue; // 0-1 background, 2 = normal, anything else seems to have no effect
																										  //prc.MMPriority = section.TryGet("MEM priority")?.IntValue ?? int.MinValue; // unused
				bool? deprecatedFgMode = section.TryGet("Foreground only")?.BoolValue; // DEPRECATED
				bool? deprecatedBgPower = section.TryGet("Background powerdown")?.BoolValue; // DEPRECATED

				// UPGRADE
				int? foregroundMode = section.TryGet("Foreground mode")?.IntValue;
				if (foregroundMode.HasValue)
				{
					prc.SetForegroundMode((ForegroundMode)foregroundMode.Value.Constrain(-1, 2));
				}
				else
				{
					bool fgmode = false, pwmod = false;
					if (deprecatedFgMode.HasValue)
						fgmode = deprecatedFgMode.Value;
					if (deprecatedBgPower.HasValue)
						pwmod = deprecatedBgPower.Value;

					if (fgmode || pwmod)
					{
						prc.SetForegroundMode(pwmod ? (fgmode ? ForegroundMode.Full : ForegroundMode.PowerOnly) : (fgmode ? ForegroundMode.Standard : ForegroundMode.Ignore));
						prc.NeedsSaving = true;
					}
				}

				// CLEANUP DEPRECATED
				if (deprecatedFgMode.HasValue || deprecatedBgPower.HasValue)
				{
					section.Remove("Foreground only");
					section.Remove("Background powerdown");
					dirtyconfig = true;
				}

				//prc.SetForegroundMode((ForegroundMode)(section.TryGet("Foreground mode")?.IntValue.Constrain(-1, 2) ?? -1)); // NEW

				prc.AffinityIdeal = section.TryGet("Affinity ideal")?.IntValue.Constrain(-1, CPUCount - 1) ?? -1;
				if (prc.AffinityIdeal >= 0)
				{
					if (!Bit.IsSet(prc.AffinityMask, prc.AffinityIdeal))
					{
						Log.Debug("[" + prc.FriendlyName + "] Affinity ideal to mask mismatch: " + HumanInterface.BitMask(prc.AffinityMask, CPUCount) + ", ideal core: " + prc.AffinityIdeal);
						prc.AffinityIdeal = -1;
					}
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

				prc.Repair();

				AddController(prc);

				// OBSOLETE
				if (section.Contains(HumanReadable.System.Process.Rescan))
				{
					section.Remove(HumanReadable.System.Process.Rescan);
					dirtyconfig |= true;
				}

				// cnt.Children &= ControlChildren;

				// cnt.delay = section.Contains("delay") ? section["delay"].IntValue : 30; // TODO: Add centralized default delay
				// cnt.delayIncrement = section.Contains("delay increment") ? section["delay increment"].IntValue : 15; // TODO: Add centralized default increment
			}

			if (dirtyconfig) appcfg.MarkDirty();

			RecacheWatchlist();
			SortWatchlist();

			// --------------------------------------------------------------------------------------------------------

			Log.Information("<Process> Name-based watchlist: " + (ExeToController.Count - WatchlistWithHybrid) + " items");
			Log.Information("<Process> Path-based watchlist: " + WatchlistWithPath + " items");
			Log.Information("<Process> Hybrid watchlist: " + WatchlistWithHybrid + " items");
		}

		void RecacheWatchlist()
		{
			lock (watchlist_lock)
			{
				if (RecacheNeeded)
				{
					WatchlistCache = Watchlist.Keys.ToList();
					RecacheNeeded = false;
				}
			}
		}

		public void SortWatchlist()
		{
			lock (watchlist_lock)
			{
				WatchlistCache.Sort(WatchlistSorter);

				int order = 0;
				foreach (var prc in WatchlistCache)
					prc.ActualOrder = order++;
			}

			// TODO: Signal UI the actual order may have changed
			WatchlistSorted?.Invoke(this, null);
		}

		int WatchlistSorter(ProcessController x, ProcessController y)
		{
			if (x.Enabled && !y.Enabled) return -1;
			else if (!x.Enabled && y.Enabled) return 1;
			else if (x.OrderPreference < y.OrderPreference) return -1;
			else if (x.OrderPreference > y.OrderPreference) return 1;
			else if (x.Adjusts > y.Adjusts) return -1;
			else if (x.Adjusts < y.Adjusts) return 1;
			else if (string.IsNullOrEmpty(x.Path) && !string.IsNullOrEmpty(y.Path)) return -1;
			else if (!string.IsNullOrEmpty(x.Path) && string.IsNullOrEmpty(y.Path)) return 1;
			return 0;
		}

		public event EventHandler WatchlistSorted;

		public void AddController(ProcessController prc)
		{
			if (DisposedOrDisposing) return;

			if (ValidateController(prc))
			{
				prc.Modified += ProcessModifiedProxy;
				//prc.Paused += ProcessPausedProxy;
				//prc.Resumed += ProcessResumedProxy;
				prc.Paused += ProcessWaitingExitProxy;
				prc.WaitingExit += ProcessWaitingExitProxy;

				SaveController(prc);
			}
		}

		void ProcessModifiedProxy(object sender, ProcessModificationEventArgs ea)
		{
			ProcessModified?.Invoke(sender, ea);
		}

		void ProcessWaitingExitProxy(object _, ProcessModificationEventArgs ea)
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

		void ProcessResumedProxy(object _, ProcessModificationEventArgs _ea)
		{
			throw new NotImplementedException();
		}

		void ProcessPausedProxy(object _, ProcessModificationEventArgs _ea)
		{
			throw new NotImplementedException();
		}

		public void RemoveController(ProcessController prc)
		{
			if (!string.IsNullOrEmpty(prc.ExecutableFriendlyName))
				ExeToController.TryRemove(prc.ExecutableFriendlyName.ToLowerInvariant(), out _);

			lock (watchlist_lock) RecacheNeeded = true;

			prc.Modified -= ProcessModified;
			prc.Paused -= ProcessPausedProxy;
			prc.Resumed -= ProcessResumedProxy;
		}

		ConcurrentDictionary<int, ProcessEx> WaitForExitList = new ConcurrentDictionary<int, ProcessEx>();

		void WaitForExitTriggered(ProcessEx info)
		{
			Debug.Assert(info.Controller != null, "ProcessController not defined");
			Debug.Assert(!SystemProcessId(info.Id), "WaitForExitTriggered for system process");

			info.State = ProcessHandlingState.Exited;

			try
			{
				if (Taskmaster.DebugForeground || Taskmaster.DebugPower)
				{
					Log.Debug("[" + info.Controller.FriendlyName + "] " + info.Name +
						" (#" + info.Id + ") exited [Power: " + info.PowerWait + ", Active: " + info.ForegroundWait + "]");
				}

				info.ForegroundWait = false;

				WaitForExitList.TryRemove(info.Id, out _);

				info.Controller?.End(info.Process, null);

				ProcessStateChange?.Invoke(this, new ProcessModificationEventArgs(info));
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		void PowerBehaviourEvent(object _, PowerManager.PowerBehaviourEventArgs ea)
		{
			if (DisposedOrDisposing) return;

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
					if (!info.ForegroundWait)
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
				WaitForExitTriggered(clearList.Pop());

			if (cancelled > 0)
				Log.Information("Cancelled power mode wait on " + cancelled + " process(es).");
		}

		void WaitForExit(ProcessEx info)
		{
			Debug.Assert(info.Controller != null, "No controller attached");

			if (DisposedOrDisposing) return;

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
					if (info.Process.HasExited)
					{
						info.State = ProcessHandlingState.Exited;
						throw new InvalidOperationException("Exited after we registered for it?");
					}
				}
				catch (InvalidOperationException) // already exited
				{
					WaitForExitTriggered(info);
				}
				catch (Exception ex) // unknown error
				{
					Logging.Stacktrace(ex);
					WaitForExitTriggered(info);
				}

				if (exithooked) ProcessStateChange?.Invoke(this, new ProcessModificationEventArgs(info));
			}
		}

		public ProcessEx[] getExitWaitList() => WaitForExitList.Values.ToArray(); // copy is good here

		ProcessController PreviousForegroundController = null;
		ProcessEx PreviousForegroundInfo;

		void ForegroundAppChangedEvent(object _sender, WindowChangedArgs ev)
		{
			if (DisposedOrDisposing) return;

			Process process = ev.Process;
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
							if (PreviousForegroundController.Foreground != ForegroundMode.Ignore)
								PreviousForegroundController.Pause(PreviousForegroundInfo);

							ProcessStateChange?.Invoke(this, new ProcessModificationEventArgs(PreviousForegroundInfo));
						}
					}
					else
					{
						if (Taskmaster.ShowInaction && Taskmaster.DebugForeground)
							Log.Debug("<Foreground> Changed but the app is still the same. Curious, don't you think?");
					}
				}

				if (WaitForExitList.TryGetValue(ev.Id, out ProcessEx info))
				{
					process = info.Process;

					if (info.ForegroundWait)
					{
						var prc = info.Controller;
						if (Taskmaster.Trace && Taskmaster.DebugForeground)
							Log.Debug("[" + prc.FriendlyName + "] " + info.Name + " (#" + info.Id + ") on foreground!");

						if (prc.Foreground != ForegroundMode.Ignore) prc.Resume(info);

						ProcessStateChange?.Invoke(this, new ProcessModificationEventArgs(info));

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
			finally
			{
				try
				{
					if (process == null) process = Process.GetProcessById(ev.Id);
					ProcessUtility.SetIO(process, 5, out _, decrease: false);
				}
				catch (OutOfMemoryException) { throw; }
				catch (ArgumentException) { }
				catch (InvalidOperationException) { }
			}
		}

		// TODO: ADD CACHE: pid -> process name, path, process

		bool GetPathController(ProcessEx info, out ProcessController prc)
		{
			prc = null;

			if (WatchlistWithPath <= 0) return false;

			try
			{
				info.Process.Refresh();
				if (info.Process.HasExited) // can throw
				{
					info.State = ProcessHandlingState.Exited;
					if (Taskmaster.ShowInaction && Taskmaster.DebugProcesses)
						Log.Verbose(info.Name + " (#" + info.Id + ") has already exited.");
					return false; // return ProcessState.Invalid;
				}
			}
			catch (InvalidOperationException ex)
			{
				Log.Fatal("INVALID ACCESS to Process");
				Logging.Stacktrace(ex);
				return false; // return ProcessState.AccessDenied; //throw; // no point throwing
			}
			catch (System.ComponentModel.Win32Exception ex)
			{
				if (ex.NativeErrorCode != 5) // what was this?
					Log.Warning("Access error: " + info.Name + " (#" + info.Id + ")");
				return false; // return ProcessState.AccessDenied; // we don't care wwhat this error is
			}

			if (string.IsNullOrEmpty(info.Path) && !ProcessUtility.FindPath(info))
				return false; // return ProcessState.Error;

			if (IgnoreSystem32Path && info.Path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.System)))
			{
				if (Taskmaster.ShowInaction && Taskmaster.DebugProcesses)
					Log.Debug("<Process/Path> " + info.Name + " (#" + info.Id + ") in System32, ignoring");
				return false;
			}

			lock (watchlist_lock)
			{
				RecacheWatchlist();

				// TODO: This needs to be FASTER
				// Can't parallelize...
				foreach (var lprc in WatchlistCache)
				{
					if (!lprc.Enabled) continue;
					if (string.IsNullOrEmpty(lprc.Path)) continue;

					if (!string.IsNullOrEmpty(lprc.Executable))
					{
						if (lprc.ExecutableFriendlyName.Equals(info.Name, StringComparison.InvariantCultureIgnoreCase))
						{
							if (Taskmaster.DebugPaths)
								Log.Debug("[" + lprc.FriendlyName + "] Path+Exe matched.");
						}
						else
							continue; // CheckPathWatch does not handle combo path+exes
					}

					if (lprc.Path.StartsWith(info.Path, StringComparison.InvariantCultureIgnoreCase))
					{
						if (Taskmaster.DebugPaths)
							Log.Verbose("[" + lprc.FriendlyName + "] (CheckPathWatch) Matched at: " + info.Path);

						prc = lprc;
						break;
					}
				}
			}

			info.Controller = prc;

			return prc != null;
		}

		static string[] ProtectList { get; set; } = {
			"consent", // UAC, user account control prompt
			"winlogon", // core system
			"wininit", // core system
			"csrss", // client server runtime, subsystems
			"dwm", // desktop window manager
			"taskmgr", // task manager
			"LogonUI", // session lock
			"services", // service control manager
		};

		static string[] IgnoreList { get; set; } = {
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
		public bool IgnoreSystem32Path { get; private set; } = true;

		/// <summary>
		/// Tests if the process ID is core system process (0[idle] or 4[system]) that can never be valid program.
		/// </summary>
		/// <returns>true if the pid should not be used</returns>
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		public static bool SystemProcessId(int pid) => pid <= 4;

		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		public bool IgnoreProcessID(int pid) => SystemProcessId(pid) || ignorePids.ContainsKey(pid);

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
			if (DisposedOrDisposing) return;

			var prc = info.Controller;

			Debug.Assert(prc.Foreground != ForegroundMode.Ignore);

			bool keyadded = false;

			info.ForegroundWait = true;

			WaitForExit(info);

			if (Taskmaster.Trace && Taskmaster.DebugForeground)
			{
				var sbs = new StringBuilder();
				sbs.Append("[").Append(prc.FriendlyName).Append("] ").Append(info.Name).Append(" (#").Append(info.Id).Append(") ")
					.Append(!keyadded ? "already in" : "added to").Append(" foreground watchlist.");

				Log.Debug(sbs.ToString());
			}

			ProcessStateChange?.Invoke(this, new ProcessModificationEventArgs(info));
		}

		// TODO: This should probably be pushed into ProcessController somehow.
		async void ProcessTriage(ProcessEx info)
		{
			if (DisposedOrDisposing) return;

            await Task.Delay(0).ConfigureAwait(false); // asyncify

            try
            {
				info.State = ProcessHandlingState.Triage;

				HandlingStateChange?.Invoke(this, new HandlingStateChangeEventArgs(info));

				if (string.IsNullOrEmpty(info.Name))
				{
					Log.Warning($"#{info.Id.ToString()} details unaccessible, ignored.");
					info.State = ProcessHandlingState.AccessDenied;
					return; // ProcessState.AccessDenied;
				}

				ProcessController prc = null;
				if (ExeToController.TryGetValue(info.Name.ToLowerInvariant(), out prc))
					info.Controller = prc; // fill

                if (prc != null || GetPathController(info, out prc))
                {
                    if (!info.Controller.Enabled)
                    {
                        if (Taskmaster.DebugProcesses) Log.Debug("[" + info.Controller.FriendlyName + "] Matched, but rule disabled; ignoring.");
                        info.State = ProcessHandlingState.Abandoned;
                        return;
                    }

                    info.State = ProcessHandlingState.Processing;

                    await Task.Delay(0).ConfigureAwait(false); // asyncify again

                    try
                    {
                        info.Controller.Modify(info);

                        if (prc.Foreground != ForegroundMode.Ignore) ForegroundWatch(info);

                        if (prc.ExclusiveMode) ExclusiveMode(info);

                        if (Taskmaster.RecordAnalysis.HasValue && info.Controller.Analyze && info.Valid && info.State != ProcessHandlingState.Abandoned)
                            analyzer.Analyze(info);

						if (Taskmaster.WindowResizeEnabled && prc.Resize.HasValue)
							prc.TryResize(info);

						if (info.State == ProcessHandlingState.Processing)
						{
							Debug.WriteLine($"[{info.Controller.FriendlyName}] {info.Name} (#{info.Id}) correcting state to finished");
							info.State = ProcessHandlingState.Finished;
						}
                    }
                    catch (OutOfMemoryException) { throw; }
                    catch (Exception ex)
                    {
                        Logging.Stacktrace(ex);
                    }
                    finally
                    {
                        HandlingStateChange?.Invoke(this, new HandlingStateChangeEventArgs(info));
                    }
                }
                else
                    info.State = ProcessHandlingState.Abandoned;

				/*
				if (ControlChildren) // this slows things down a lot it seems
					ChildController(info);
				*/
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				HandlingStateChange?.Invoke(this, new HandlingStateChangeEventArgs(info));
			}
		}

		object Exclusive_lock = new object();
		ConcurrentDictionary<int, ProcessEx> ExclusiveList = new ConcurrentDictionary<int, ProcessEx>();

		void ExclusiveMode(ProcessEx info)
		{
			if (DisposedOrDisposing) return;
			if (!Taskmaster.IsAdministrator()) return; // sadly stopping services requires admin rights

			if (Taskmaster.DebugProcesses) Log.Debug($"[{info.Controller.FriendlyName}] {info.Name} (#{info.Id}) Exclusive mode initiating.");

			try
			{
				bool ensureExit = false;
				lock (Exclusive_lock)
				{
					if (ExclusiveList.TryAdd(info.Id, info))
					{
						if (Taskmaster.DebugProcesses) Log.Debug($"<Exclusive> [{info.Controller.FriendlyName}] {info.Name} (#{info.Id.ToString()}) starting");
						info.Process.EnableRaisingEvents = true;
						info.Process.Exited += EndExclusiveMode;
						ExclusiveStart();

						ensureExit = true;
					}
				}

				if (ensureExit)
				{
					info.Process.Refresh();
					if (info.Process.HasExited)
					{
						info.State = ProcessHandlingState.Exited;
						EndExclusiveMode(info.Process, null);
					}
				}
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		void EndExclusiveMode(object sender, EventArgs ea)
		{
			if (DisposedOrDisposing) return;

			try
			{
				lock (Exclusive_lock)
				{
					var process = (Process)sender;
					if (ExclusiveList.TryRemove(process.Id, out var info))
					{
						if (Taskmaster.DebugProcesses) Log.Debug($"<Exclusive> [{info.Controller.FriendlyName}] {info.Name} (#{info.Id.ToString()}) ending");
						if (ExclusiveList.Count == 0)
						{
							if (Taskmaster.DebugProcesses) Log.Debug("<Exclusive> Ended for all, restarting services.");
							try
							{
								ExclusiveEnd();
							}
							catch (InvalidOperationException)
							{
								Log.Warning($"<Exclusive> Failure to restart WUA after {info.Name} (#{info.Id}) exited.");
							}
						}
						else
						{
							if (Taskmaster.DebugProcesses) Log.Debug("<Exclusive> Still used by: #" + string.Join(", #", ExclusiveList.Keys));
						}
					}
				}
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		Lazy<System.ServiceProcess.ServiceController> windowsupdate = new Lazy<System.ServiceProcess.ServiceController>(
			() =>
			{
				return new System.ServiceProcess.ServiceController("wuauserv");
			});

		bool WUARunning => windowsupdate.Value.Status == System.ServiceProcess.ServiceControllerStatus.Running;

		bool ExclusiveEnabled = false;
		bool WUAWasRunning = false;
		void ExclusiveStart()
		{
			if (DisposedOrDisposing) return;

            bool cWUARunning = false;

            lock (Exclusive_lock)
			{
				try
				{
					windowsupdate.Value.Refresh();
					cWUARunning = WUARunning;
					if (ExclusiveEnabled && !cWUARunning) return;

					if (cWUARunning)
					{
						if (windowsupdate.Value.CanPauseAndContinue)
							windowsupdate.Value.Pause();
						else
							windowsupdate.Value.Stop();
					}
				}
				catch (InvalidOperationException ex) // not running
				{
                    Log.Error(ex, "<Exclusive> Failure to stop WUA");
				}
				catch (OutOfMemoryException) { throw; }
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
				}
				finally
				{
                    WUAWasRunning = cWUARunning;
                    ExclusiveEnabled = true;
				}
			}
		}

		void ExclusiveEnd()
		{
			lock (Exclusive_lock)
			{
				if (!ExclusiveEnabled || !WUAWasRunning)
				{
					ExclusiveEnabled = false;
					return;
				}

				try
				{
					windowsupdate.Value.Refresh();

					if (!WUARunning)
					{
						if (windowsupdate.Value.CanPauseAndContinue)
							windowsupdate.Value.Continue();
						else
							windowsupdate.Value.Start();
					}
				}
				catch (OutOfMemoryException) { throw; }
				finally
				{
					ExclusiveEnabled = false;
				}
			}
		}

		int Handling { get; set; } = 0; // this isn't used for much...

		void SignalProcessHandled(int adjust)
		{
			Handling += adjust;
			HandlingCounter?.Invoke(this, new ProcessingCountEventArgs(adjust, Handling));
		}

		// This needs to return faster
		void NewInstanceTriage(object _, EventArrivedEventArgs ea)
		{
			var now = DateTimeOffset.UtcNow;
			var timer = Stopwatch.StartNew();

			int pid = -1;
			string name = string.Empty;
			string path = string.Empty;
			ProcessEx info = null;
            DateTime creation = DateTime.MinValue;
            TimeSpan wmidelay = TimeSpan.Zero;

            ProcessHandlingState state = ProcessHandlingState.Invalid;

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

					ScanBlockList.TryAdd(pid, DateTimeOffset.UtcNow);

					var iname = targetInstance.Properties["Name"].Value as string;
					path = targetInstance.Properties["ExecutablePath"].Value as string;
                    creation = ManagementDateTimeConverter.ToDateTime(targetInstance.Properties["CreationDate"].Value as string);
					name = System.IO.Path.GetFileNameWithoutExtension(iname);
					if (string.IsNullOrEmpty(path))
					{
						// CommandLine sometimes has the path when executablepath does not
						var cmdl = targetInstance.Properties["CommandLine"].Value as string;
						if (!string.IsNullOrEmpty(cmdl))
						{
							int off = 0;
							string npath = "";
							if (cmdl[0] == '"')
							{
								off = cmdl.IndexOf('"', 1);
								npath = cmdl.Substring(1, off - 1);
							}
							else
							{
								off = cmdl.IndexOf(' ', 1);
								// off < 1 = no arguments
								npath = off <= 1 ? cmdl : cmdl.Substring(0, off);
							}

							if (npath.IndexOf('"', 0) >= 0) Log.Fatal("WMI.TargetInstance.CommandLine still had invalid characters: " + npath);
							path = npath;
						}
					}
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
					state = ProcessHandlingState.Invalid;
					return;
				}
				finally
				{
					Statistics.WMIPollTime += timer.Elapsed.TotalSeconds;
					Statistics.WMIPolling += 1;
				}

                wmidelay = new DateTimeOffset(creation.ToUniversalTime()).TimeTo(now);
                if (Taskmaster.Trace) Debug.WriteLine($"WMI delay (#{pid}): {wmidelay.TotalMilliseconds:N0} ms");

				if (IgnoreProcessID(pid)) return; // We just don't care

				if (string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(path))
					name = System.IO.Path.GetFileNameWithoutExtension(path);

				if (string.IsNullOrEmpty(name) && pid < 0)
				{
					// likely process exited too fast
					if (Taskmaster.DebugProcesses && Taskmaster.ShowInaction) Log.Debug("<<WMI>> Failed to acquire neither process name nor process Id");
					state = ProcessHandlingState.AccessDenied;
					return;
				}

				if (IgnoreProcessName(name)) return;

                if (ProcessUtility.GetInfo(pid, out info, path: path, getPath: true, name: name))
                {
                    info.Timer = timer;
                    info.WMIDelay = Convert.ToInt32(wmidelay.TotalMilliseconds);
                    NewInstanceTriagePhaseTwo(info, out state);
                }
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				Log.Error("Unregistering new instance triage");
				if (watcher != null) watcher.EventArrived -= NewInstanceTriage;
				watcher?.Dispose();
				watcher = null;
				timer?.Stop();
			}
			finally
			{
                if (info == null)
                {
                    info = new ProcessEx { Id = pid, Timer = timer, State = state};
                    info.WMIDelay = Convert.ToInt32(wmidelay.TotalMilliseconds);
                }
                HandlingStateChange?.Invoke(this, new HandlingStateChangeEventArgs(info));

				SignalProcessHandled(-1); // done with it
			}
		}

		void NewInstanceTriagePhaseTwo(ProcessEx info, out ProcessHandlingState state)
		{
			//await Task.Delay(0).ConfigureAwait(false);
			info.State = ProcessHandlingState.Invalid;

			try
			{
				try
				{
					info.Process = Process.GetProcessById(info.Id);
				}
				catch (ArgumentException)
				{
					state = info.State = ProcessHandlingState.Exited;
					if (Taskmaster.ShowInaction && Taskmaster.DebugProcesses)
						Log.Verbose("Caught #" + info.Id + " but it vanished.");
					return;
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
					state = info.State = ProcessHandlingState.Invalid;
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
					catch (OutOfMemoryException) { throw; }
					catch
					{
						Log.Error("Failed to retrieve name of process #" + info.Id);
						state = info.State = ProcessHandlingState.Invalid;
						return;
					}
				}

				if (Taskmaster.Trace) Log.Verbose("Caught: " + info.Name + " (#" + info.Id + ") at: " + info.Path);

				// info.Process.StartTime; // Only present if we started it

				state = info.State = ProcessHandlingState.Triage;

				ProcessTriage(info);
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				state = info.State = ProcessHandlingState.Invalid;
			}
			finally
			{
				HandlingStateChange?.Invoke(this, new HandlingStateChangeEventArgs(info));

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
				// Win32_ProcessStartTrace requires Admin rights

				if (Taskmaster.IsAdministrator())
				{
					ManagementEventWatcher w = null;
					WqlEventQuery q = new WqlEventQuery();
					q.EventClassName = "Win32_ProcessStartTrace";
					var w = new ManagementEventWatcher(scope, q);
					w.EventArrived += NewInstanceTriage2;
					w.Start();
				}
				*/

				// var tracequery = new System.Management.EventQuery("SELECT * FROM Win32_ProcessStartTrace");

				// var query = new System.Management.EventQuery("SELECT TargetInstance FROM __InstanceCreationEvent WITHIN 5 WHERE TargetInstance ISA 'Win32_Process'");
				watcher = new ManagementEventWatcher(
					new ManagementScope(@"\\.\root\CIMV2"),
					new EventQuery("SELECT * FROM __InstanceCreationEvent WITHIN " + WMIPollDelay + " WHERE TargetInstance ISA 'Win32_Process'")
					); // Avast cybersecurity causes this to throw an exception

				watcher.EventArrived += NewInstanceTriage;

				watcher.Stopped += (_, _ea) =>
				{
					if (Taskmaster.DebugWMI)
						Log.Debug("<<WMI>> New instance watcher stopped.");
					// Restart it maybe? This probably happens when WMI service is stopped or restarted.?
				};

				watcher.Start();

				if (Taskmaster.DebugWMI) Log.Debug("<<WMI>> New instance watcher initialized.");
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				throw new InitFailure("<<WMI>> Event watcher initialization failure", ex);
			}
		}

		void StopWMIEventWatcher()
		{
			watcher?.Dispose();
			watcher = null;
		}

		const string watchfile = "Watchlist.ini";

		async void CleanupTick(object _sender, EventArgs _ea)
		{
			if (DisposedOrDisposing) return;

			Cleanup();

			SortWatchlist();

			var now = DateTimeOffset.UtcNow;
			foreach (var item in ScanBlockList)
			{
				if (item.Value.TimeTo(now).TotalSeconds > 5d)
					ScanBlockList.TryRemove(item.Key, out _);
			}
		}

		int cleanup_lock = 0;
		/// <summary>
		/// Cleanup.
		/// </summary>
		/// <remarks>
		/// Locks: waitforexit_lock
		/// </remarks>
		public async void Cleanup()
		{
			if (DisposedOrDisposing) return;

			if (!Atomic.Lock(ref cleanup_lock)) return; // cleanup already in progress

			if (Taskmaster.DebugPower || Taskmaster.DebugProcesses)
				Log.Debug("<Process> Periodic cleanup");

			// TODO: Verify that this is actually useful?

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

				await Task.Delay(1_000).ConfigureAwait(false);

				triggerList = new Stack<ProcessEx>();
				foreach (var info in items)
				{
					try
					{
						info.Process.Refresh();
						info.Process.WaitForExit(20);
						if (info.Process.HasExited)
						{
							info.State = ProcessHandlingState.Exited;
							triggerList.Push(info);
						}
					}
					catch (OutOfMemoryException) { throw; }
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

				if (User.IdleTime().TotalHours > 2d)
				{
					foreach (var prc in Watchlist.Keys)
						prc.Refresh();
				}

				lock (Exclusive_lock)
				{
					foreach (var info in ExclusiveList.Values)
					{
						try
						{
							info.Process.Refresh();
							if (info.Process.HasExited)
							{
								info.State = ProcessHandlingState.Exited;
								EndExclusiveMode(info.Process, null);
							}
						}
						catch { }
					}
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

		bool DisposedOrDisposing = false;
		void Dispose(bool disposing)
		{
			DisposedOrDisposing = true;
			if (disposed) return;

			if (disposing)
			{
				if (Taskmaster.Trace) Log.Verbose("Disposing process manager...");

				//ScanStartEvent = null;
				//ScanEndEvent = null;
				ProcessModified = null;
				HandlingCounter = null;
				ProcessStateChange = null;
				HandlingStateChange = null;

				CancelPowerWait();
				WaitForExitList.Clear();

				windowsupdate.Value.Dispose();
				ExclusiveList?.Clear();
				MKAh.Utility.DiscardExceptions(() => ExclusiveEnd());

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
					MaintenanceTimer?.Dispose();
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
					// throw; // would throw but this is dispose
				}

				try
				{
					ExeToController?.Clear();
					ExeToController = null;

					var wcfg = Taskmaster.Config.Load(watchfile);

					foreach (var prc in Watchlist.Keys)
						if (prc.NeedsSaving) prc.SaveConfig(wcfg);

					Watchlist?.Clear();
					Watchlist = null;
					WatchlistCache?.Clear();
					WatchlistCache = null;
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
					// throw; // would throw but this is dispose
				}
			}

			disposed = true;
		}
	}
}