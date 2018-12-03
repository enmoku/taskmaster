//
// ProcessManager.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2016-2018 M.A.
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
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Serilog;

namespace Taskmaster
{
	sealed public class InstanceEventArgs : EventArgs
	{
		public int Count { get; set; } = 0;
		public int Total { get; set; } = 0;
	}

	sealed public class InstanceHandlingArgs : EventArgs
	{
		public ProcessEx Info = null;
		public ProcessController Controller = null;
		public ProcessHandlingState State = ProcessHandlingState.Invalid;
	}

	[Serializable]
	sealed public class ProcessNotFoundException : Exception
	{
		public string Name { get; set; } = null;
		public int Id { get; set; } = -1;
	}

	sealed public class ProcessEx : IDisposable
	{
		/// <summary>
		/// Process filename without extension
		/// Cached from Process.ProcessFilename
		/// </summary>
		public string Name = string.Empty;
		/// <summary>
		/// Process fullpath, including filename with extension
		/// </summary>
		public string Path = string.Empty;
		/// <summary>
		/// Process Id.
		/// </summary>
		public int Id = -1;

		/// <summary>
		/// Process reference.
		/// </summary>
		public Process Process = null;
		/// <summary>
		/// .Process.Refresh() should be called for this if the Process needs to be accessed.
		/// </summary>
		public bool NeedsRefresh = false;

		/// <summary>
		/// Has this Process been handled already?
		/// </summary>
		public bool Handled = false;
		/// <summary>
		/// Path was matched with a rule.
		/// </summary>
		public bool PathMatched = false;

		public bool PowerWait = false;
		public bool ActiveWait = false;

		public DateTime Modified = DateTime.MinValue;
		public ProcessModification State = ProcessModification.Invalid;

		#region IDisposable Support
		private bool disposed = false; // To detect redundant calls

		void Dispose(bool disposing)
		{
			if (!disposed)
			{
				if (disposing)
				{
					Process.Dispose();
					Process = null;
				}

				disposed = true;
			}
		}

		public void Dispose()
		{
			Dispose(true);
		}
		#endregion
	}

	enum ProcessFlags
	{
		PowerWait = 1 << 6,
		ActiveWait = 1 << 7
	}

	sealed public class ProcessManager : IDisposable
	{
		/// <summary>
		/// Actively watched process images.
		/// </summary>
		List<ProcessController> watchlist = new List<ProcessController>();
		readonly object watchlist_lock = new object();

		// ctor, constructor
		public ProcessManager()
		{
			Log.Information("<CPU> Logical cores: " + CPUCount);

			AllCPUsMask = Convert.ToInt32(Math.Pow(2, CPUCount) - 1 + double.Epsilon);

			//allCPUsMask = 1;
			//for (int i = 0; i < CPUCount - 1; i++)
			//	allCPUsMask = (allCPUsMask << 1) | 1;

			Log.Information("<CPU> Full CPU mask: " + Convert.ToString(AllCPUsMask, 2) + " (" + AllCPUsMask + " = OS control)");

			loadWatchlist();

			InitWMIEventWatcher();

			if (execontrol.Count > 0 && RescanDelay > 0)
			{
				if (Taskmaster.Trace) Log.Verbose("Starting rescan timer.");
				rescanTimer = new System.Threading.Timer(RescanOnTimerTick, null, 500, RescanDelay * 1000);
			}

			ProcessDetectedEvent += ProcessTriage;

			if (RescanEverythingFrequency > 0)
			{
				RescanEverythingTimer = new System.Timers.Timer(RescanEverythingFrequency * 1000);
				RescanEverythingTimer.Elapsed += ScanEverythingRequest;
				RescanEverythingTimer.Start();
			}

			if (Taskmaster.PathCacheLimit > 0)
			{
				ProcessManagerUtility.Initialize();
			}

			BatchProcessingTimer = new System.Timers.Timer(1000 * 5);
			BatchProcessingTimer.Elapsed += BatchProcessingTick;

			ScanEverythingEndEvent += UnregisterFreeMemoryTick;

			Log.Information("<Process> Component Loaded.");
		}

		public ProcessController[] getWatchlist()
		{
			lock (watchlist_lock)
				return watchlist.ToArray();
		}

		// TODO: Need an ID mapping
		public ProcessController getWatchedController(string name)
		{
			lock (watchlist_lock)
			{
				foreach (var item in watchlist)
				{
					if (item.FriendlyName.Equals(name, StringComparison.InvariantCultureIgnoreCase))
						return item;
				}
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
		Dictionary<string, ProcessController> execontrol = new Dictionary<string, ProcessController>();
		object execontrol_lock = new object();

		public ProcessController getController(string executable)
		{
			ProcessController rv = null;
			lock (execontrol_lock)
				execontrol.TryGetValue(executable.ToLowerInvariant(), out rv);

			return rv;
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

		void UnregisterFreeMemoryTick(object sender, EventArgs ev) => ProcessDetectedEvent -= FreeMemoryTick;

		string freememoryignore = null;
		void FreeMemoryTick(object sender, ProcessEventArgs ea)
		{
			if (IgnoreProcessID(ea.Info.Id) ||
				(!string.IsNullOrEmpty(freememoryignore) &&
				ea.Info.Name.Equals(freememoryignore, StringComparison.InvariantCultureIgnoreCase)))
				return;

			if (Taskmaster.DebugMemory) Log.Debug("<Process> Paging: " + ea.Info.Name + " (#" + ea.Info.Id + ")");

			try
			{
				NativeMethods.EmptyWorkingSet(ea.Info.Process.Handle);
			}
			catch { } // process.Handle may throw which we don't care about
		}

		HashSet<int> ignorePids = new HashSet<int>();
		public void Ignore(int processId) => ignorePids.Add(processId);

		public void Unignore(int processId) => ignorePids.Remove(processId);

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
			await FreeMemoryInternal(ignorePid).ConfigureAwait(false);
		}

		async Task FreeMemoryInternal(int ignorePid = -1)
		{
			var b1 = Taskmaster.Components.healthmonitor.FreeMemory();

			try
			{
				ScanEverythingPaused = true;

				// TODO: Pause ScanEverything until we're done
				ProcessDetectedEvent += FreeMemoryTick;

				await ScanEverything(ignorePid).ConfigureAwait(false); // TODO: Call for this to happen otherwise
				ScanEverythingPaused = false;

				Taskmaster.Components.healthmonitor.InvalidateFreeMemory(); // just in case

				// TODO: Wait a little longer to allow OS to Actually page stuff. Might not matter?
				var b2 = Taskmaster.Components.healthmonitor.FreeMemory();

				if (Taskmaster.DebugPaging)
				{
					Log.Information("<Memory> Paging complete, observed memory change: " +
						HumanInterface.ByteString((long)((b2 - b1) * 1000000), true));
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				UnregisterFreeMemoryTick(null, null);
			}
		}

		bool ScanEverythingPaused = false;
		public void ScanEverythingRequest(object sender, EventArgs e)
		{
			if (Taskmaster.Trace) Log.Verbose("Rescan requested.");
			if (ScanEverythingPaused) return;
			// this stays on UI thread for some reason

			Task.Run(async () => { await ScanEverything(); });
		}

		System.Threading.Timer rescanTimer;

		/// <summary>
		/// Event fired by ScanEverything and WMI new process
		/// </summary>
		public event EventHandler<ProcessEventArgs> ProcessDetectedEvent;
		public event EventHandler ScanEverythingStartEvent;
		public event EventHandler ScanEverythingEndEvent;

		public event EventHandler<ProcessEventArgs> ProcessModified;

		public event EventHandler<InstanceEventArgs> onInstanceHandling;
		public event EventHandler<ProcessEventArgs> onProcessHandled;
		public event EventHandler<ProcessEventArgs> onWaitForExitEvent;

		public event EventHandler<InstanceHandlingArgs> HandlingStateChange;

		int scan_lock = 0;

		public async Task ScanEverything(int ignorePid = -1)
		{
			var now = DateTime.Now;

			if (!Atomic.Lock(ref scan_lock)) return;

			LastScan = now;
			NextScan = now.AddSeconds(RescanEverythingFrequency);

			if (Taskmaster.DebugFullScan) Log.Debug("<Process> Full Scan: Start");

			await Task.Delay(0).ConfigureAwait(false); // asyncify

			ScanEverythingStartEvent?.Invoke(this, null);

			if (ignorePid > 4) Ignore(ignorePid);

			var procs = Process.GetProcesses();
			int count = procs.Length - 2; // -2 for Idle&System
			Debug.Assert(count > 0);
			SignalProcessHandled(count); // scan start

			ProcessEventArgs pea;

			var i = 0;
			foreach (var process in procs)
			{
				++i;
				pea = null;

				string name;
				int pid;

				try
				{
					name = process.ProcessName;
					pid = process.Id;
				}
				catch { continue; } // process inaccessible (InvalidOperationException or NotSupportedException)

				if (IgnoreProcessID(pid) || IgnoreProcessName(name) || pid == Process.GetCurrentProcess().Id)
					continue;

				pea = new ProcessEventArgs
				{
					Info = new ProcessEx() { Process = process, Id = pid, Name = name, Path = null },
					Control = null,
					State = ProcessRunningState.Found,
				};

				if (Taskmaster.DebugFullScan)
					Log.Verbose("<Process> Checking [" + i + "/" + count + "] " + name + " (#" + pid + ")");

				ProcessDetectedEvent?.Invoke(this, pea);
			}

			if (Taskmaster.DebugFullScan) Log.Debug("<Process> Full Scan: Complete");

			SignalProcessHandled(-count); // scan done

			ScanEverythingEndEvent?.Invoke(this, null);

			if (ignorePid > 4) Unignore(ignorePid);

			Atomic.Unlock(ref scan_lock);
		}

		static int BatchDelay = 2500;
		static int RescanDelay = 0; // 5 minutes
		public static int RescanEverythingFrequency { get; private set; } = 15; // seconds
		public static DateTime LastScan { get; private set; } = DateTime.MinValue;
		public static DateTime NextScan { get; set; } = DateTime.MinValue;
		static bool BatchProcessing; // = false;
		static int BatchProcessingThreshold = 5;
		// static bool ControlChildren = false; // = false;

		readonly System.Timers.Timer RescanEverythingTimer = null;

		public bool ValidateController(ProcessController prc)
		{
			var rv = true;

			if (prc.Priority.HasValue && prc.ForegroundOnly && prc.BackgroundPriority.Value.ToInt32() >= prc.Priority.Value.ToInt32())
			{
				prc.SetForegroundOnly(false);
				Log.Warning("[" + prc.FriendlyName + "] Background priority equal or higher than foreground priority, ignoring.");
			}

			if (prc.Rescan > 0 && string.IsNullOrEmpty(prc.ExecutableFriendlyName))
			{
				Log.Warning("[" + prc.FriendlyName + "] Configuration error, can not rescan without image name.");
				prc.Rescan = 0;
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
				lock (execontrol_lock)
					execontrol.Add(prc.ExecutableFriendlyName.ToLowerInvariant(), prc);

				if (!string.IsNullOrEmpty(prc.Path))
					WatchlistWithHybrid += 1;

				// Log.Verbose("[{ExecutableName}] Added to monitoring.", cnt.FriendlyName);
			}

			lock (watchlist_lock)
				watchlist.Add(prc);

			Log.Verbose("[" + prc.FriendlyName + "] Match: " + (prc.Executable ?? prc.Path) + ", " +
				prc.Priority.ToString() + ", Mask:" + prc.Affinity.ToString() +
				", Rescan: " + prc.Rescan + "m, Recheck: " + prc.Recheck + "s, FgOnly: " + prc.ForegroundOnly.ToString());
		}

		public void loadWatchlist()
		{
			if (Taskmaster.DebugProcesses)
				Log.Information("<Process> Loading configuration...");

			var corecfg = Taskmaster.Config.Load(Taskmaster.coreconfig);

			var coreperf = corecfg.Config["Performance"];

			bool dirtyconfig = false, modified = false;
			// ControlChildren = coreperf.GetSetDefault("Child processes", false, out tdirty).BoolValue;
			// dirtyconfig |= tdirty;
			BatchProcessing = coreperf.GetSetDefault("Batch processing", false, out modified).BoolValue;
			coreperf["Batch processing"].Comment = "Process management works in delayed batches instead of immediately.";
			dirtyconfig |= modified;
			Log.Information("Batch processing: " + (BatchProcessing ? "Enabled" : "Disabled"));
			if (BatchProcessing)
			{
				BatchDelay = coreperf.GetSetDefault("Batch processing delay", 2500, out modified).IntValue.Constrain(500, 15000);
				dirtyconfig |= modified;
				Log.Information("Batch processing delay: " + $"{BatchDelay / 1000:N1}s");
				BatchProcessingThreshold = coreperf.GetSetDefault("Batch processing threshold", 5, out modified).IntValue.Constrain(1, 30);
				dirtyconfig |= modified;
				Log.Information("<Process> Batch processing threshold: " + BatchProcessingThreshold);
			}

			RescanDelay = coreperf.GetSetDefault("Rescan frequency", 0, out modified).IntValue.Constrain(0, 60 * 6) * 60;
			coreperf["Rescan frequency"].Comment = "In minutes. How often to check for apps that want to be rescanned. Disabled if rescan everything is enabled. 0 disables.";
			dirtyconfig |= modified;

			RescanEverythingFrequency = coreperf.GetSetDefault("Rescan everything frequency", 15, out modified).IntValue.Constrain(0, 60 * 60 * 24);
			if (RescanEverythingFrequency > 0)
			{
				if (RescanEverythingFrequency < 5) RescanEverythingFrequency = 5;
				// RescanEverythingFrequency *= 1000; // to seconds
			}

			coreperf["Rescan everything frequency"].Comment = "Frequency (in seconds) at which we rescan everything. 0 disables.";
			dirtyconfig |= modified;

			if (RescanEverythingFrequency > 0)
			{
				Log.Information("<Process> Rescan everything every " + RescanEverythingFrequency + " seconds.");
				RescanDelay = 0;
			}
			else
				Log.Information("<Process> Per-app rescan frequency: " + $"{RescanDelay / 60:N1}m");

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
			string[] newIgnoreList = ignsetting.GetSetDefault("Ignored", IgnoreList, out modified)?.StringValueArray;
			ignsetting.PreComment = "Special hardcoded protection applied to: consent, winlogon, wininit, and csrss.\nThese are vital system services and messing with them can cause severe system malfunctioning.\nMess with the ignore list at your own peril.";
			if (newIgnoreList != null)
			{
				IgnoreList = newIgnoreList;
				Log.Information("<Process> Custom application ignore list loaded.");
				dirtyconfig |= modified;
			}

			IgnoreSystem32Path = ignsetting.GetSetDefault("Ignore System32", true, out modified).BoolValue;
			ignsetting["Ignore System32"].Comment = "Ignore programs in %SYSTEMROOT%/System32 folder.";
			dirtyconfig |= modified;

			// Log.Information("Child process monitoring: {ChildControl}", (ControlChildren ? "Enabled" : "Disabled"));

			// --------------------------------------------------------------------------------------------------------

			Log.Information("<Process> Loading watchlist...");
			var appcfg = Taskmaster.Config.Load(watchfile);

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

			var newsettings = coreperf.SettingCount;
			if (dirtyconfig) corecfg.MarkDirty();

			foreach (SharpConfig.Section section in appcfg.Config)
			{
				bool upgrade = false;

				if (!section.Contains("Image") && !section.Contains("Path"))
				{
					// TODO: Deal with incorrect configuration lacking image
					Log.Warning("'" + section.Name + "' has no image nor path.");
					continue;
				}

				if (!section.Contains("Priority") && !section.Contains("Affinity") && !section.Contains("Power mode"))
				{
					// TODO: Deal with incorrect configuration lacking these things
					Log.Warning("[" + section.Name + "] No priority, affinity, nor power plan. Ignoring.");
					continue;
				}

				var aff = section.TryGet("Affinity")?.IntValue ?? -1;
				if (aff > AllCPUsMask)
				{
					Log.Warning("[" + section.Name + "] Affinity(" + aff + ") is malconfigured. Ignoring.");
					//aff = Bit.And(aff, allCPUsMask); // at worst case results in 1 core used
					// TODO: Count bits, make 2^# assumption about intended cores but scale it to current core count.
					//		Shift bits to allowed range. Assume at least one core must be assigned, and in case of holes at least one core must be unassigned.
					aff = -1; // ignore
				}
				var prio = section.TryGet("Priority")?.IntValue ?? -1;
				ProcessPriorityClass? prioR = null;
				if (prio >= 0) prioR = ProcessHelpers.IntToPriority(prio);
				var pmodes = section.TryGet("Power mode")?.StringValue ?? null;
				var pmode = PowerManager.GetModeByName(pmodes);
				if (pmode == PowerInfo.PowerMode.Custom)
				{
					Log.Warning("'" + section.Name + "' has unrecognized power plan: " + pmodes);
					pmode = PowerInfo.PowerMode.Undefined;
				}

				ProcessPriorityStrategy priostrat = ProcessPriorityStrategy.None;
				if (prioR != null)
				{
					var priorityStrat = section.TryGet("Priority strategy")?.IntValue.Constrain(0, 3) ?? -1;

					if (priorityStrat > 0)
						priostrat = (ProcessPriorityStrategy)priorityStrat;
					else if (priorityStrat == -1)
					{
						// DEPRECATETD
						var increase = (section.TryGet("Increase")?.BoolValue ?? false);
						var decrease = (section.TryGet("Decrease")?.BoolValue ?? true);
						if (increase && decrease)
							priostrat = ProcessPriorityStrategy.Force;
						else if (increase)
							priostrat = ProcessPriorityStrategy.Increase;
						else if (decrease)
							priostrat = ProcessPriorityStrategy.Decrease;

						section.Remove("Increase");
						section.Remove("Decrease");
						upgrade = true;
					}
					else // 0
					{
						prioR = null;
					}
				}

				ProcessAffinityStrategy affStrat = ProcessAffinityStrategy.None;
				if (aff > 0)
				{
					int affinityStrat = section.TryGet("Affinity strategy")?.IntValue.Constrain(0, 3) ?? 2;
					affStrat = (ProcessAffinityStrategy)affinityStrat;
				}

				float volume = section.TryGet("Volume")?.FloatValue.Constrain(0.0f, 1.0f) ?? 0.5f;
				AudioVolumeStrategy volumestrategy = (AudioVolumeStrategy)(section.TryGet("Volume strategy")?.IntValue.Constrain(0, 5) ?? 0);

				int baffn = section.TryGet("Background affinity")?.IntValue ?? -1;
				IntPtr? baff = null;
				if (baffn >= 0)
					baff = new IntPtr(baffn);
				ProcessPriorityClass? bprio = null;
				int bpriot = section.TryGet("Background priority")?.IntValue ?? -1;
				if (bpriot >= 0) bprio = ProcessHelpers.IntToPriority(bpriot);

				PathVisibilityOptions pvis = PathVisibilityOptions.File;
				pvis = (PathVisibilityOptions)(section.TryGet("Path visibility")?.IntValue.Constrain(0, 3) ?? 0);

				var prc = new ProcessController(section.Name, prioR, (aff == 0 ? AllCPUsMask : aff))
				{
					Enabled = section.TryGet("Enabled")?.BoolValue ?? true,
					Executable = section.TryGet("Image")?.StringValue ?? null,
					// friendly name is filled automatically
					PriorityStrategy = priostrat,
					AffinityStrategy = affStrat,
					Rescan = (section.TryGet("Rescan")?.IntValue ?? 0),
					Path = (section.TryGet("Path")?.StringValue ?? null),
					ModifyDelay = (section.TryGet("Modify delay")?.IntValue ?? 0),
					//BackgroundIO = (section.TryGet("Background I/O")?.BoolValue ?? false), // Doesn't work
					Recheck = (section.TryGet("Recheck")?.IntValue ?? 0),
					PowerPlan = pmode,
					PathVisibility = pvis,
					BackgroundPriority = bprio,
					BackgroundAffinity = baff,
					BackgroundPowerdown = (section.TryGet("Background powerdown")?.BoolValue ?? false),
					IgnoreList = (section.TryGet("Ignore")?.StringValueArray ?? null),
					AllowPaging = (section.TryGet("Allow paging")?.BoolValue ?? false),
				};

				prc.SetForegroundOnly(section.TryGet("Foreground only")?.BoolValue ?? false);

				prc.Volume = volume;
				prc.VolumeStrategy = volumestrategy;

				// TODO: Blurp about following configuration errors
				if (!prc.Affinity.HasValue) prc.AffinityStrategy = ProcessAffinityStrategy.None;
				else if (prc.AffinityStrategy == ProcessAffinityStrategy.None) prc.Affinity = null;

				if (!prc.Priority.HasValue) prc.PriorityStrategy = ProcessPriorityStrategy.None;
				else if (prc.PriorityStrategy == ProcessPriorityStrategy.None) prc.Priority = null;

				if (string.IsNullOrEmpty(prc.Executable) && prc.Rescan > 0)
				{
					prc.Rescan = 0;
					Log.Warning("[" + prc.FriendlyName + "] Rescan defined with no executable name.");
				}

				int[] resize = section.TryGet("Resize")?.IntValueArray ?? null; // width,height
				if (resize != null && resize.Length == 4)
				{
					int resstrat = section.TryGet("Resize strategy")?.IntValue.Constrain(0, 3) ?? -1;
					if (resstrat < 0) resstrat = 0;

					prc.ResizeStrategy = (WindowResizeStrategy)resstrat;

					prc.Resize = new System.Drawing.Rectangle(resize[0], resize[1], resize[2], resize[3]);
				}

				if (upgrade) prc.SaveConfig(appcfg, section);

				AddController(prc);

				// cnt.Children &= ControlChildren;

				// cnt.delay = section.Contains("delay") ? section["delay"].IntValue : 30; // TODO: Add centralized default delay
				// cnt.delayIncrement = section.Contains("delay increment") ? section["delay increment"].IntValue : 15; // TODO: Add centralized default increment
			}

			// --------------------------------------------------------------------------------------------------------

			Log.Information("<Process> Name-based watchlist: " + (execontrol.Count - WatchlistWithHybrid) + " items");
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
			}
		}

		void ProcessModifiedProxy(object sender, ProcessEventArgs ev)
		{
			ProcessModified?.Invoke(sender, ev);
		}

		public void RemoveController(ProcessController prc)
		{
			lock (watchlist_lock)
			{
				lock (execontrol_lock)
				{
					if (!string.IsNullOrEmpty(prc.ExecutableFriendlyName))
						execontrol.Remove(prc.ExecutableFriendlyName.ToLowerInvariant());
				}

				watchlist.Remove(prc);
				prc.Modified -= ProcessModifiedProxy;
			}
		}

		readonly object waitforexit_lock = new object();
		Dictionary<int, ProcessEx> WaitForExitList = new Dictionary<int, ProcessEx>();

		void WaitForExitTriggered(ProcessEx info, ProcessRunningState state = ProcessRunningState.Exiting)
		{
			if (Taskmaster.DebugForeground || Taskmaster.DebugPower)
			{
				Log.Debug("<Process> " + info.Name + " (#" + info.Id + ") exited [Power: " + info.PowerWait + ", Active: " + info.ActiveWait + "]");
			}

			Debug.Assert(info.Id > 4);

			if (info.ActiveWait)
			{
				lock (foreground_lock)
					ForegroundWaitlist.Remove(info.Id);
			}

			if (info.PowerWait)
				Taskmaster.Components.powermanager.Release(info.Id);

			lock (waitforexit_lock)
				WaitForExitList.Remove(info.Id);

			onWaitForExitEvent?.Invoke(this, new ProcessEventArgs() { Control = null, Info = info, State = state });
		}

		public void PowerBehaviourEvent(object sender, PowerManager.PowerBehaviourEventArgs ea)
		{
			if (ea.Behaviour == PowerManager.PowerBehaviour.Manual)
				CancelPowerWait();
		}

		public void CancelPowerWait()
		{
			var cancelled = 0;

			Stack<ProcessEx> clearList = null;
			lock (waitforexit_lock)
			{
				if (WaitForExitList.Count == 0) return;

				var items = WaitForExitList.Values;
				clearList = new Stack<ProcessEx>();
				foreach (var info in items)
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
			}

			if (clearList != null)
			{
				while (clearList.Count > 0)
					WaitForExitTriggered(clearList.Pop(), ProcessRunningState.Cancel);
			}

			if (cancelled > 0)
				Log.Information("Cancelled power mode wait on " + cancelled + " process(es).");
		}

		public void WaitForExit(ProcessEx info)
		{
			bool exithooked = false;

			lock (waitforexit_lock)
			{
				if (WaitForExitList.ContainsKey(info.Id)) return;

				WaitForExitList.Add(info.Id, info);

				try
				{
					info.Process.EnableRaisingEvents = true;
					info.Process.Exited += (s, e) => { WaitForExitTriggered(info); };
					exithooked = true;
				}
				catch (InvalidOperationException) // already exited
				{
					WaitForExitList.Remove(info.Id);
				}
				catch (Exception ex) // unknown error
				{
					Logging.Stacktrace(ex);
					WaitForExitList.Remove(info.Id);
				}
			}

			if (exithooked)
				onWaitForExitEvent?.Invoke(this, new ProcessEventArgs() { Control = null, Info = info, State = ProcessRunningState.Starting });
		}

		public ProcessEx[] getExitWaitList() => WaitForExitList.Values.ToArray(); // copy is good here

		ProcessController PreviousForegroundController = null;
		ProcessEx PreviousForegroundInfo;

		readonly object foreground_lock = new object();
		Dictionary<int, ProcessController> ForegroundWaitlist = new Dictionary<int, ProcessController>(6);

		public void ForegroundAppChangedEvent(object sender, WindowChangedArgs ev)
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
						PreviousForegroundController.Pause(PreviousForegroundInfo);

						onProcessHandled?.Invoke(this, new ProcessEventArgs() { Control = PreviousForegroundController, Info = PreviousForegroundInfo, State = ProcessRunningState.Reduced });
					}
				}
				else
				{
					if (Taskmaster.ShowInaction && Taskmaster.DebugForeground)
						Log.Debug("<Foreground> Changed but the app is still the same. Curious, don't you think?");
				}
			}

			ProcessController prc = null;
			lock (foreground_lock)
				ForegroundWaitlist.TryGetValue(ev.Id, out prc);

			if (prc != null)
			{
				ProcessEx info = null;
				WaitForExitList.TryGetValue(ev.Id, out info);
				if (info != null)
				{
					if (Taskmaster.Trace && Taskmaster.DebugForeground)
						Log.Debug("[" + prc.FriendlyName + "] " + info.Name + " (#" + info.Id + ") on foreground!");

					if (prc.ForegroundOnly) prc.Resume(info);

					onProcessHandled?.Invoke(this, new ProcessEventArgs() { Control = prc, Info = info, State = ProcessRunningState.Restored });

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

		readonly object systemlock = new object();

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

			if (string.IsNullOrEmpty(info.Path) && !ProcessManagerUtility.FindPath(info))
				return null; // return ProcessState.Error;

			if (IgnoreSystem32Path && info.Path.Contains(Environment.GetFolderPath(Environment.SpecialFolder.System)))
			{
				if (Taskmaster.ShowInaction && Taskmaster.DebugProcesses)
					Log.Debug("<Process> " + info.Name + " (#" + info.Id + ") in System32, ignoring");
				return null;
			}

			// TODO: This needs to be FASTER
			lock (watchlist_lock)
			{
				Debug.Assert(watchlist != null);
				foreach (ProcessController prc in watchlist)
				{
					if (!prc.Enabled) continue;
					if (string.IsNullOrEmpty(prc.Path)) continue;

					if (!string.IsNullOrEmpty(prc.Executable))
					{
						if (prc.Executable.Equals(info.Name, StringComparison.InvariantCultureIgnoreCase))
						{
							if (Taskmaster.DebugPaths)
								Log.Debug("<Process> [" + prc.FriendlyName + "] Path+Exe matched.");
						}
						else
							continue; // CheckPathWatch does not handle combo path+exes
					}

					// Log.Debug("with: "+ pc.Path);
					if (prc.MatchPath(info.Path))
					{
						// if (cacheGet)
						// 	Log.Debug("[{FriendlyName}] {Exec} (#{Pid}) – PATH CACHE GET!! :D", pc.FriendlyName, name, pid);
						if (Taskmaster.DebugPaths)
							Log.Verbose("[" + prc.FriendlyName + "] (CheckPathWatch) Matched at: " + info.Path);

						info.PathMatched = true;
						matchedprc = prc;
						break;
					}
				}
			}

			return matchedprc;
		}

		async void CheckPathWatch(ProcessEx info)
		{
			Debug.Assert(info.Process != null);

			await Task.Delay(0).ConfigureAwait(false);

			ProcessController matchedprc = null;

			matchedprc = getWatchedPath(info);

			if (matchedprc != null)
			{
				try
				{
					matchedprc.Touch(info);
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
					return;
				}

				info.Handled = true;
				info.Modified = DateTime.Now;

				ForegroundWatch(info, matchedprc); // already called?
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

		bool IgnoreSystem32Path = true;

		const int LowestInvalidPid = 4;
		bool IgnoreProcessID(int pid) => (pid <= LowestInvalidPid || ignorePids.Contains(pid));

		public static bool IgnoreProcessName(string name) => IgnoreList.Contains(name, StringComparer.InvariantCultureIgnoreCase);

		public static bool ProtectedProcessName(string name) => ProtectList.Contains(name, StringComparer.InvariantCultureIgnoreCase);
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
		void ForegroundWatch(ProcessEx info, ProcessController prc)
		{
			if (!prc.ForegroundOnly) return;

			var keyexists = true;
			lock (foreground_lock)
			{
				if ((keyexists = ForegroundWaitlist.ContainsKey(info.Id)) == false)
					ForegroundWaitlist.Add(info.Id, prc);
			}

			if (!keyexists) WaitForExit(info);

			if (Taskmaster.Trace && Taskmaster.DebugForeground)
			{
				if (!keyexists)
					Log.Debug("[" + prc.FriendlyName + "] " + info.Name + " (#" + info.Id + ") added to foreground watchlist.");
				else
					Log.Debug("[" + prc.FriendlyName + "] " + info.Name + " (#" + info.Id + ") already in foreground watchlist.");
			}

			onProcessHandled?.Invoke(this, new ProcessEventArgs() { Control = prc, Info = info, State = ProcessRunningState.Found });
		}

		// TODO: This should probably be pushed into ProcessController somehow.
		async void ProcessTriage(object sender, ProcessEventArgs ea)
		{
			Debug.Assert(!string.IsNullOrEmpty(ea.Info.Name), "CheckProcess received null process name.");
			Debug.Assert(ea.Info != null);
			Debug.Assert(ea.Info.Process != null, "CheckProcess received null process.");
			Debug.Assert(!IgnoreProcessID(ea.Info.Id), "CheckProcess received invalid process ID: " + ea.Info.Id);
			//Debug.Assert(execontrol != null); // triggers only if this function is running when the app is closing

			HandlingStateChange?.Invoke(this, new InstanceHandlingArgs()
			{
				State = ProcessHandlingState.Triage,
				Info = ea.Info,
				Controller = ea.Control,
			});

			await Task.Delay(0).ConfigureAwait(false);

			if (IgnoreProcessID(ea.Info.Id) || IgnoreProcessName(ea.Info.Name))
			{
				if (Taskmaster.Trace) Log.Verbose("Ignoring process: " + ea.Info.Name + " (#" + ea.Info.Id + ")");
				return; // ProcessState.Ignored;
			}

			if (string.IsNullOrEmpty(ea.Info.Name))
			{
				Log.Warning("#" + ea.Info.Id + " details unaccessible, ignored.");
				return; // ProcessState.AccessDenied;
			}

			ProcessController prc = null;

			lock (execontrol_lock)
				execontrol.TryGetValue(ea.Info.Name.ToLowerInvariant(), out prc);

			if (prc != null)
			{
				if (!prc.Enabled)
				{
					if (Taskmaster.DebugProcesses)
						Log.Debug("[" + prc.FriendlyName + "] Matched, but rule disabled; ignoring.");
					ea.Info.State = ProcessModification.Ignored;
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
				finally
				{
					HandlingStateChange?.Invoke(this, new InstanceHandlingArgs()
					{
						State = ProcessHandlingState.Finished,
						Info = ea.Info,
						Controller = ea.Control,
					});
				}

				ForegroundWatch(ea.Info, prc);
				return;
			}

			// Log.Verbose("{AppName} not in executable control list.", info.Name);

			if (WatchlistWithPath > 0 && !ea.Info.Handled)
			{
				// Log.Verbose("Checking paths for '{ProcessName}' (#{ProcessID})", info.Name, info.Id);
				CheckPathWatch(ea.Info);
				return;
			}

			/*
			if (ControlChildren) // this slows things down a lot it seems
				ChildController(info);
			*/
		}

		readonly object batchprocessing_lock = new object();
		int processListLockRestart = 0;
		List<ProcessEx> ProcessBatch = new List<ProcessEx>();
		readonly System.Timers.Timer BatchProcessingTimer = null;

		void BatchProcessingTick(object sender, EventArgs ev)
		{
			lock (batchprocessing_lock)
			{
				if (ProcessBatch.Count == 0)
				{
					BatchProcessingTimer.Stop();

					if (Taskmaster.DebugProcesses)
						Log.Debug("<Process> New instance timer stopped.");

					return;
				}
			}

			Task.Run(new Action(() => { NewInstanceBatchProcessing(); }));
		}

		void NewInstanceBatchProcessing()
		{
			//await Task.Delay(0).ConfigureAwait(false);

			List<ProcessEx> list = new List<ProcessEx>();

			lock (batchprocessing_lock)
			{
				if (ProcessBatch.Count == 0) return;

				BatchProcessingTimer.Stop();

				processListLockRestart = 0;
				Utility.Swap(ref list, ref ProcessBatch);
			}

			foreach (var info in list)
				ProcessDetectedEvent?.Invoke(this, new ProcessEventArgs { Info = info });

			SignalProcessHandled(-(list.Count)); // batch done
		}

		public int Handling { get; private set; }

		void SignalProcessHandled(int adjust)
		{
			Handling += adjust;
			onInstanceHandling?.Invoke(this, new InstanceEventArgs { Count = adjust, Total = Handling });
		}

		// This needs to return faster
		async void NewInstanceTriage(object sender, System.Management.EventArrivedEventArgs e)
		{
			SignalProcessHandled(1); // wmi new instance

			var wmiquerytime = Stopwatch.StartNew();

			// TODO: Instance groups?
			var pid = -1;
			var name = string.Empty;
			var path = string.Empty;
			System.Management.ManagementBaseObject targetInstance;

			try
			{
				targetInstance = (System.Management.ManagementBaseObject)e.NewEvent.Properties["TargetInstance"].Value;
				pid = Convert.ToInt32((string)targetInstance.Properties["Handle"].Value);
				path = (string)(targetInstance.Properties["ExecutablePath"].Value);
			}
			catch { }
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
				SignalProcessHandled(-1); // done early
				return;
			}

			ProcessEx info = ProcessManagerUtility.GetInfo(pid, path: path, getPath: true);
			if (info != null) NewInstanceTriagePhaseTwo(info);

			SignalProcessHandled(-1); // done with it
		}

		async Task NewInstanceTriagePhaseTwo(ProcessEx info)
		{
			//await Task.Delay(0).ConfigureAwait(false);

			try
			{
				info.Process = Process.GetProcessById(info.Id);
			}
			catch (ArgumentException)
			{
				info.State = ProcessModification.Exited;
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

				HandlingStateChange?.Invoke(this, new InstanceHandlingArgs()
				{
					Info = info,
					Controller = null,
					State = ProcessHandlingState.Delayed
				});
			}
			else
			{
				ProcessDetectedEvent?.Invoke(this, new ProcessEventArgs { Info = info });
			}
		}

		void RescanOnTimerTick(object state) => Task.Run(RescanOnTimer);

		int rescan_lock = 0;

		async Task RescanOnTimer()
		{
			if (!Atomic.Lock(ref rescan_lock)) return;

			await Task.Delay(0).ConfigureAwait(false); // async

			try
			{
				// Log.Verbose("Rescanning...");

				var nextscan = 1;
				var tnext = 0;
				var rescanrequests = 0;
				string nextscanfor = null;

				lock (execontrol_lock)
				{
					var pcs = execontrol.Values;
					foreach (ProcessController prc in pcs)
					{
						if (prc.Rescan == 0) continue;

						rescanrequests++;

						tnext = prc.TryScan();

						if (tnext > nextscan)
						{
							nextscan = tnext;
							nextscanfor = prc.FriendlyName;
						}
					}
				}

				if (rescanrequests == 0)
				{
					if (Taskmaster.Trace) Log.Verbose("No apps have requests to rescan, stopping rescanning.");
					Utility.Dispose(ref rescanTimer);
				}
				else
				{
					try
					{
						rescanTimer.Change(500, nextscan.Constrain(1, 360) * (1000 * 60));
					}
					catch (Exception ex)
					{
						Log.Error("Failed to set rescan timer based on scheduled next scan.");
						rescanTimer.Change(500, 5 * (1000 * 60));

						Logging.Stacktrace(ex);
					}

					Log.Verbose("Rescan set to occur after " + nextscan + " minutes; Next in line: " + nextscanfor);
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				Atomic.Unlock(ref rescan_lock);
			}
		}

		System.Management.ManagementEventWatcher watcher = null;
		void InitWMIEventWatcher()
		{
			if (!Taskmaster.WMIPolling) return;

			// FIXME: doesn't seem to work when lots of new processes start at the same time.
			try
			{
				// Transition to permanent event listener?
				// https://msdn.microsoft.com/en-us/library/windows/desktop/aa393014(v=vs.85).aspx

				var scope = new System.Management.ManagementScope(
					new System.Management.ManagementPath(@"\\.\root\CIMV2")
				);

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
				var query = new System.Management.EventQuery(
					"SELECT * FROM __InstanceCreationEvent WITHIN " + Taskmaster.WMIPollDelay + " WHERE TargetInstance ISA 'Win32_Process'");
				watcher = new System.Management.ManagementEventWatcher(scope, query); // Avast cybersecurity causes this to throw an exception
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				throw new InitFailure("<<WMI>> Event watcher initialization failure");
			}

			if (watcher != null)
			{
				watcher.EventArrived += NewInstanceTriage;

				if (BatchProcessing) BatchProcessingTimer.Start();

				watcher.Stopped += (object sender, System.Management.StoppedEventArgs e) =>
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

			try
			{
				Stack<ProcessEx> triggerList;
				lock (waitforexit_lock)
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

				ProcessDetectedEvent -= ProcessTriage;

				ProcessDetectedEvent = null;
				ScanEverythingStartEvent = null;
				ScanEverythingEndEvent = null;
				ProcessModified = null;
				onInstanceHandling = null;
				onProcessHandled = null;
				onWaitForExitEvent = null;

				CancelPowerWait();
				WaitForExitList.Clear();

				try
				{
					//watcher.EventArrived -= NewInstanceTriage;
					Utility.Dispose(ref watcher);

					if (activeappmonitor != null)
					{
						activeappmonitor.ActiveChanged -= ForegroundAppChangedEvent;
						activeappmonitor = null;
					}


					Utility.Dispose(ref rescanTimer);
					RescanEverythingTimer?.Dispose();
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
					lock (execontrol_lock)
					{
						execontrol?.Clear();
						execontrol = null;
					}

					lock (watchlist_lock)
					{
						var wcfg = Taskmaster.Config.Load(watchfile);

						foreach (var prc in watchlist)
							if (prc.NeedsSaving) prc.SaveConfig(wcfg);

						watchlist?.Clear();
						watchlist = null;
					}
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

			lock (watchlist_lock)
			{
				foreach (ProcessController prc in watchlist)
					prc.SaveStats();
			}
		}
	}

	sealed public class ProcessorEventArgs : EventArgs
	{
		public float Current;
		public float Average;
		public float Low;
		public float High;

		public PowerInfo.PowerMode Mode;

		public float Pressure;

		public bool Handled;
	}
}