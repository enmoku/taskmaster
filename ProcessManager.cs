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

using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Serilog;

namespace Taskmaster
{
	public class InstanceEventArgs : EventArgs
	{
		public int Count { get; set; } = 0;
		public int Total { get; set; } = 0;
	}

	public class ProcessNotFoundException : Exception
	{
		public string Name { get; set; } = null;
		public int Id { get; set; } = -1;
	}

	public class ProcessEx
	{
		public string Name;
		public string Path;
		public int Id;
		public Process Process;

		public int Flags;
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

		public ProcessController[] getWatchlist()
		{
			lock (watchlist_lock)
			{
				return watchlist.ToArray();
			}
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

		/// <summary>
		/// Paths not yet properly initialized.
		/// </summary>
		public List<ProcessController> pathinit;
		readonly object pathwatchlock = new object();
		/// <summary>
		/// Executable name to ProcessControl mapping.
		/// </summary>
		Dictionary<string, ProcessController> execontrol = new Dictionary<string, ProcessController>();
		object execontrol_lock = new object();

		public ProcessController getController(string executable)
		{
			ProcessController rv = null;
			lock (execontrol_lock)
			{
				execontrol.TryGetValue(LowerCase(executable), out rv);
			}
			return rv;
		}

		public static int allCPUsMask = 1;
		public static int CPUCount = Environment.ProcessorCount;

		int ProcessModifyDelay = 4800;

		public static bool RestoreOriginal = false;
		public static int OffFocusPriority = 1;
		public static int OffFocusAffinity = 0;
		public static bool OffFocusPowerCancel = true;

		/// <summary>
		/// Gets the control class instance of the executable if it exists.
		/// </summary>
		/// <returns>ProcessControl </returns>
		/// <param name="executable">Executable.</param>
		public ProcessController getExecutableController(string executable)
		{
			ProcessController prc = null;
			lock (watchlist_lock)
			{
				prc = watchlist.Find((ctrl) => ctrl.Executable.Equals(executable, Taskmaster.CaseSensitive ? StringComparison.InvariantCulture : StringComparison.InvariantCultureIgnoreCase));
			}

			if (prc == null)
				Log.Error("{ExecutableName} was not found!", executable);

			return prc;
		}

		/// <summary>
		/// Updates the path watch, trying to locate any watched directories.
		/// </summary>
		void UpdatePathWatch()
		{
			if (pathinit == null) return;

			if (Taskmaster.Trace) Log.Verbose("Locating watched paths.");

			lock (pathwatchlock)
			{
				if (pathinit.Count > 0)
				{
					for (int i = pathinit.Count - 1; i != 0; i--)
					{
						if (pathinit[i].Locate())
						{
							lock (watchlist_lock)
							{
								watchlist.Add(pathinit[i]);
							}
							WatchlistWithPath += 1;
							pathinit.RemoveAt(i);
						}
					}
				}

				if (pathinit.Count == 0)
					pathinit = null;
			}

			if (Taskmaster.Trace) Log.Verbose("Path location complete.");
		}

		ActiveAppManager activeappmonitor = null;
		public void hookActiveAppManager(ref ActiveAppManager aamon)
		{
			activeappmonitor = aamon;
			activeappmonitor.ActiveChanged += ForegroundAppChangedEvent;
		}

		string freememoryignore = null;
		async void FreeMemoryTick(object sender, ProcessEx info)
		{
			if (!string.IsNullOrEmpty(freememoryignore) && info.Name.Equals(freememoryignore))
				return;

			try
			{
				NativeMethods.EmptyWorkingSet(info.Process.Handle);
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		HashSet<int> ignorePids = new HashSet<int>();
		public void Ignore(int processId)
		{
			ignorePids.Add(processId);
		}

		public void Unignore(int processId)
		{
			ignorePids.Remove(processId);
		}

		public async Task FreeMemory(string executable = null)
		{
			if (!Taskmaster.PagingEnabled) return;


			if (!string.IsNullOrEmpty(executable))
			{
				var procs = Process.GetProcessesByName(executable); // unnecessary maybe?
				if (procs.Length == 0)
				{
					Log.Error("{Exec} not found, not freeing memory for it.", executable);
					return;
				}

				Log.Information("Freeing memory for: {Exec}", executable);
			}

			freememoryignore = executable;

			ProcessDetectedEvent += FreeMemoryTick;
			ScanEverything(); // TODO: Call for this to happen otherwise
			ProcessDetectedEvent -= FreeMemoryTick;
		}

		public async void PageEverythingRequest(object sender, EventArgs e)
		{
			if (Taskmaster.Trace) Log.Verbose("Paging requested.");

			FreeMemory(null);

			if (Taskmaster.Trace) Log.Verbose("Paging complete.");
		}

		public async void ScanEverythingRequest(object sender, EventArgs e)
		{
			if (Taskmaster.Trace) Log.Verbose("Rescan requested.");

			LastRescan = DateTime.Now;
			NextRescan = DateTime.Now.AddSeconds(RescanEverythingFrequency);

			try
			{
				using (var m = SelfAwareness.Mind(DateTime.Now.AddSeconds(30)))
				{
					ScanEverything();
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				return; //throw; // event handler, no can throw
			}
		}

		System.Threading.Timer rescanTimer;

		/// <summary>
		/// Event fired by ScanEverything and WMI new process
		/// </summary>
		public event EventHandler<ProcessEx> ProcessDetectedEvent;
		public event EventHandler ScanEverythingStartEvent;
		public event EventHandler ScanEverythingEndEvent;

		int scaninprogress = 0;
		public async void ScanEverything()
		{
			if (!Atomic.Lock(ref scaninprogress))
			{
				Log.Error("Scan request received while old scan was still in progress. Previous scan started at: {Date}", LastRescan.ToString());
				return;
			}

			if (Taskmaster.DebugFullScan)
				Log.Verbose("Processing everything.");

			ScanEverythingStartEvent?.Invoke(this, null);

			int count = 0;
			try
			{
				var procs = Process.GetProcesses();
				count = procs.Length - 2; // -2 for Idle&System

				SignalProcessHandled(count);

				using (var m = SelfAwareness.Mind(DateTime.Now.AddSeconds(5)))
				{
					int i = 0;
					foreach (var process in procs)
					{
						++i;
						try
						{
							string name = process.ProcessName;
							int pid = process.Id;

							if (IgnoreProcessID(pid) || IgnoreProcessName(name))
								continue;

							if (Taskmaster.DebugFullScan)
								Log.Verbose("Checking [{Iter}/{Count}] {Proc} (#{Pid})", i, count, name, pid);

							ProcessDetectedEvent?.Invoke(this, new ProcessEx() { Process = process, Id = pid, Name = name, Flags = 0, Path = null });
						}
						catch (Exception ex)
						{
							Logging.Stacktrace(ex);
						}
					}
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				Atomic.Unlock(ref scaninprogress);
			}

			SignalProcessHandled(-count);

			if (Taskmaster.DebugFullScan)
				Log.Verbose("Full scan: DONE.");

			ScanEverythingEndEvent?.Invoke(this, null);
		}

		public async void ProcessTriage(object sender, ProcessEx info)
		{
			try
			{
				CheckProcess(info, schedule_next: false);
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		void EndScanUpdatePathWatch(object sender, EventArgs ev)
		{
			UpdatePathWatch();
		}

		static int BatchDelay = 2500;
		static int RescanDelay = 0; // 5 minutes
		public static int RescanEverythingFrequency { get; private set; } = 15; // seconds
		public static DateTime LastRescan { get; private set; } = DateTime.MinValue;
		public static DateTime NextRescan { get; set; } = DateTime.MinValue;
		static bool BatchProcessing; // = false;
		static int BatchProcessingThreshold = 5;
		//static bool ControlChildren = false; // = false;

		static System.Timers.Timer RescanEverythingTimer = null;

		public bool ValidateController(ProcessController prc)
		{
			bool rv = true;

			if (prc.ForegroundOnly && prc.BackgroundPriority.ToInt32() >= prc.Priority.ToInt32())
			{
				prc.ForegroundOnly = false;
				Log.Warning("[{Friendly}] Background priority equal or higher than foreground priority, ignoring.", prc.FriendlyName);
			}

			if (prc.Rescan > 0 && prc.ExecutableFriendlyName == null)
			{
				Log.Warning("[{FriendlyName}] Configuration error, can not rescan without image name.");
				prc.Rescan = 0;
			}

			if (prc.Executable == null && prc.Path == null)
			{
				if (prc.Subpath == null)
				{
					Log.Warning("[{FriendlyName}] Executable, Path and Subpath missing; ignoring.");
					rv = false;
				}
			}

			// SANITY CHECKING
			if (prc.ExecutableFriendlyName != null)
			{
				if (IgnoreProcessName(prc.ExecutableFriendlyName))
				{
					if (Taskmaster.ShowInaction)
						Log.Warning("{Exec} in ignore list; all changes denied.", prc.ExecutableFriendlyName);

					// rv = false; // We'll leave the config in.
				}
				else if (ProtectedProcessName(prc.ExecutableFriendlyName))
				{
					if (Taskmaster.ShowInaction)
						Log.Warning("{Exec} in protected list; priority changing denied.");
				}
			}

			return (prc.Valid = rv);
		}

		public void SaveController(ProcessController prc)
		{
			if (string.IsNullOrEmpty(prc.Executable))
			{
				if (prc.Locate())
				{
					WatchlistWithPath += 1;

					// TODO: Add "Path" to config
					//if (stats.Contains(cnt.Path))
					//	cnt.Adjusts = stats[cnt.Path].TryGet("Adjusts")?.IntValue ?? 0;
				}
				else
				{
					prc.Enabled = false;

					if (prc.Subpath != null)
					{
						if (pathinit == null) pathinit = new List<ProcessController>();
						pathinit.Add(prc);
						Log.Verbose("[{FriendlyName}] ({Subpath}) waiting to be located.", prc.FriendlyName, prc.Subpath);
					}
					else
					{
						Log.Warning("[{FriendlyName}] Malconfigured. Path not found.", prc.FriendlyName);
					}
				}
			}
			else
			{
				lock (execontrol_lock)
				{
					execontrol.Add(LowerCase(prc.ExecutableFriendlyName), prc);
				}

				//Log.Verbose("[{ExecutableName}] Added to monitoring.", cnt.FriendlyName);
			}

			lock (watchlist_lock)
			{
				watchlist.Add(prc);
			}

			Log.Verbose("[{FriendlyName}] Match: {MatchName}, {TargetPriority}, Mask:{Affinity}, Rescan: {Rescan}m, Recheck: {Recheck}s, FgOnly: {Fg}",
						prc.FriendlyName, (prc.Executable ?? prc.Path), prc.Priority, prc.Affinity,
						prc.Rescan, prc.Recheck, prc.ForegroundOnly);
		}

		public void loadWatchlist()
		{
			Log.Information("<Process Manager> Loading configuration...");

			var coreperf = Taskmaster.cfg["Performance"];

			bool dirtyconfig = false, modified = false;
			//ControlChildren = coreperf.GetSetDefault("Child processes", false, out tdirty).BoolValue;
			//dirtyconfig |= tdirty;
			BatchProcessing = coreperf.GetSetDefault("Batch processing", false, out modified).BoolValue;
			coreperf["Batch processing"].Comment = "Process management works in delayed batches instead of immediately.";
			dirtyconfig |= modified;
			Log.Information("Batch processing: {BatchProcessing}", (BatchProcessing ? "Enabled" : "Disabled"));
			if (BatchProcessing)
			{
				BatchDelay = coreperf.GetSetDefault("Batch processing delay", 2500, out modified).IntValue.Constrain(500, 15000);
				dirtyconfig |= modified;
				Log.Information("Batch processing delay: {BatchProcessingDelay:N1}s", BatchDelay / 1000);
				BatchProcessingThreshold = coreperf.GetSetDefault("Batch processing threshold", 5, out modified).IntValue.Constrain(1, 30);
				dirtyconfig |= modified;
				Log.Information("Batch processing threshold: {BatchProcessingThreshold}", BatchProcessingThreshold);
			}
			RescanDelay = coreperf.GetSetDefault("Rescan frequency", 0, out modified).IntValue.Constrain(0, 60 * 6) * 60;
			coreperf["Rescan frequency"].Comment = "In minutes. How often to check for apps that want to be rescanned. Disabled if rescan everything is enabled. 0 disables.";
			dirtyconfig |= modified;

			RescanEverythingFrequency = coreperf.GetSetDefault("Rescan everything frequency", 15, out modified).IntValue.Constrain(0, 60 * 60 * 24);
			if (RescanEverythingFrequency > 0)
			{
				if (RescanEverythingFrequency < 5) RescanEverythingFrequency = 5;
				//RescanEverythingFrequency *= 1000; // to seconds
			}
			coreperf["Rescan everything frequency"].Comment = "Frequency (in seconds) at which we rescan everything. 0 disables.";
			dirtyconfig |= modified;

			if (RescanEverythingFrequency > 0)
			{
				Log.Information("Rescan everything every {Frequency} seconds.", RescanEverythingFrequency);
				RescanDelay = 0;
			}
			else
				Log.Information("Per-app rescan frequency: {RescanDelay:N1}m", RescanDelay / 60);

			// --------------------------------------------------------------------------------------------------------

			var fgpausesec = Taskmaster.cfg["Foreground Focus Lost"];
			//RestoreOriginal = fgpausesec.GetSetDefault("Restore original", false, out modified).BoolValue;
			//dirtyconfig |= modified;
			OffFocusPriority = fgpausesec.GetSetDefault("Default priority", 2, out modified).IntValue.Constrain(0, 4);
			fgpausesec["Default priority"].Comment = "Default is normal to avoid excessive loading times while user is alt-tabbed.";
			dirtyconfig |= modified;
			//OffFocusAffinity = fgpausesec.GetSetDefault("Affinity", 0, out modified).IntValue;
			//dirtyconfig |= modified;
			//OffFocusPowerCancel = fgpausesec.GetSetDefault("Power mode cancel", true, out modified).BoolValue;
			//dirtyconfig |= modified;

			// --------------------------------------------------------------------------------------------------------

			//Taskmaster.cfg["Applications"]["Ignored"].StringValueArray = IgnoreList;
			var ignsetting = Taskmaster.cfg["Applications"];
			string[] newIgnoreList = ignsetting.GetSetDefault("Ignored", IgnoreList, out modified)?.StringValueArray;
			ignsetting.PreComment = "Special hardcoded protection applied to: consent, winlogon, wininit, and csrss.\nThese are vital system services and messing with them can cause severe system malfunctioning.\nMess with the ignore list at your own peril.";
			if (newIgnoreList != null)
			{
				IgnoreList = newIgnoreList;
				Log.Information("Custom application ignore list loaded.");
			}
			else
				Taskmaster.saveConfig(Taskmaster.cfg);
			dirtyconfig |= modified;

			if (dirtyconfig) Taskmaster.MarkDirtyINI(Taskmaster.cfg);

			//Log.Information("Child process monitoring: {ChildControl}", (ControlChildren ? "Enabled" : "Disabled"));

			// --------------------------------------------------------------------------------------------------------

			Log.Information("<Process Manager> Loading watchlist...");
			SharpConfig.Configuration appcfg = Taskmaster.loadConfig(watchfile);

			if (appcfg.Count() == 0)
			{
				Taskmaster.unloadConfig(watchfile);

				// DEFAULT CONFIGURATION
				foreach (var name in System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceNames())
					Console.WriteLine("Resource: " + name);

				using (var rs = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Taskmaster.Resources.Watchlist.ini"))
				{
					string path = System.IO.Path.Combine(Taskmaster.datapath, watchfile);
					using (var file = new System.IO.FileStream(path, System.IO.FileMode.Create, System.IO.FileAccess.Write))
					{
						rs.CopyTo(file);
					}
				}

				appcfg = Taskmaster.loadConfig(watchfile);
			}

			// --------------------------------------------------------------------------------------------------------

			int newsettings = coreperf.SettingCount;
			if (dirtyconfig) Taskmaster.MarkDirtyINI(Taskmaster.cfg);

			foreach (SharpConfig.Section section in appcfg)
			{
				if (!section.Contains("Image") && !section.Contains("Path"))
				{
					// TODO: Deal with incorrect configuration lacking image
					Log.Warning("'{SectionName}' has no image nor path.", section.Name);
					continue;
				}
				if (!section.Contains("Priority") && !section.Contains("Affinity"))
				{
					// TODO: Deal with incorrect configuration lacking image
					Log.Warning("'{SectionName}' has no priority nor affinity.", section.Name);
					continue;
				}

				int aff = section.TryGet("Affinity")?.IntValue ?? allCPUsMask;
				int prio = section.TryGet("Priority")?.IntValue ?? 2;
				string pmodes = section.TryGet("Power mode")?.StringValue ?? null;
				PowerInfo.PowerMode pmode = PowerManager.GetModeByName(pmodes);
				if (pmode == PowerInfo.PowerMode.Custom)
				{
					Log.Warning("'{SectionName}' has unrecognized power plan: {PowerPlan}", section.Name, pmodes);
					pmode = PowerInfo.PowerMode.Undefined;
				}

				var prc = new ProcessController(section.Name, ProcessHelpers.IntToPriority(prio), (aff != 0 ? aff : allCPUsMask))
				{
					Enabled = section.TryGet("Enabled")?.BoolValue ?? true,
					Executable = section.TryGet("Image")?.StringValue ?? null,
					// friendly name is filled automatically
					Increase = (section.TryGet("Increase")?.BoolValue ?? false),
					Decrease = (section.TryGet("Decrease")?.BoolValue ?? true),
					AllowedCores = (section.TryGet("Allowed cores")?.BoolValue ?? false),
					Rescan = (section.TryGet("Rescan")?.IntValue ?? 0),
					Path = (section.TryGet("Path")?.StringValue ?? null),
					Subpath = (section.TryGet("Subpath")?.StringValue ?? null),
					//BackgroundIO = (section.TryGet("Background I/O")?.BoolValue ?? false), // Doesn't work
					ForegroundOnly = (section.TryGet("Foreground only")?.BoolValue ?? false),
					Recheck = (section.TryGet("Recheck")?.IntValue ?? 0),
					PowerPlan = pmode,
					BackgroundPriority = ProcessHelpers.IntToPriority((section.TryGet("Background priority")?.IntValue ?? OffFocusPriority).Constrain(1, 3)),
					BackgroundPowerdown = (section.TryGet("Background powerdown")?.BoolValue ?? true),
					IgnoreList = (section.TryGet("Ignore")?.StringValueArray ?? null),
					//Children = (section.TryGet("Children")?.BoolValue ?? false),
					//ChildPriority = ProcessHelpers.IntToPriority(section.TryGet("Child priority")?.IntValue ?? prio),
					//ChildPriorityReduction = section.TryGet("Child priority reduction")?.BoolValue ?? false,
					AllowPaging = (section.TryGet("Allow paging")?.BoolValue ?? false),
				};

				addController(prc);

				//cnt.Children &= ControlChildren;

				//cnt.delay = section.Contains("delay") ? section["delay"].IntValue : 30; // TODO: Add centralized default delay
				//cnt.delayIncrement = section.Contains("delay increment") ? section["delay increment"].IntValue : 15; // TODO: Add centralized default increment
			}

			// --------------------------------------------------------------------------------------------------------

			Log.Information("Name-based watchlist: {Items} items", execontrol.Count);
			Log.Information("Path-based watchlist: {Items} items", WatchlistWithPath);
			Log.Information("Path init list: {Items} items", (pathinit?.Count ?? 0));
		}

		public void addController(ProcessController prc)
		{
			if (ValidateController(prc))
			{
				prc.LoadStats();
				SaveController(prc);
			}
		}

		public void removeController(ProcessController prc)
		{
			lock (watchlist_lock)
			{
				watchlist.Remove(prc);
			}
		}

		string LowerCase(string str)
		{
			Debug.Assert(!string.IsNullOrEmpty(str));
			return Taskmaster.CaseSensitive ? str : str.ToLower();
		}

		readonly object waitforexit_lock = new object();
		Dictionary<int, ProcessEx> WaitForExitList = new Dictionary<int, ProcessEx>();

		void WaitForExitTriggered(ProcessEx info, ProcessEventArgs.ProcessState state = ProcessEventArgs.ProcessState.Exiting)
		{
			if (Taskmaster.DebugForeground || Taskmaster.DebugPower)
				Log.Debug("{Exec} exited", info.Name);

			try
			{
				if (Bit.IsSet(info.Flags, (int)ProcessFlags.ActiveWait))
				{
					lock (foreground_lock)
					{
						ForegroundWaitlist.Remove(info.Id);
					}
				}

				if (Bit.IsSet(info.Flags, (int)ProcessFlags.PowerWait))
					Taskmaster.powermanager.Restore(info.Id).Wait();

				lock (waitforexit_lock)
				{
					WaitForExitList.Remove(info.Id);
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}

			onWaitForExitEvent?.Invoke(this, new ProcessEventArgs() { Control = null, Info = info, State = state });
		}

		public void PowerBehaviourEvent(object sender, PowerManager.PowerBehaviour behaviour)
		{
			if (behaviour == PowerManager.PowerBehaviour.Manual)
			{
				CancelPowerWait();
			}
		}

		public void CancelPowerWait()
		{
			int cancelled = 0;

			Stack<ProcessEx> clearList = null;
			lock (waitforexit_lock)
			{
				if (WaitForExitList.Count == 0) return;

				var items = WaitForExitList.Values;
				clearList = new Stack<ProcessEx>();
				foreach (var info in items)
				{
					if (Bit.IsSet(info.Flags, (int)ProcessFlags.PowerWait))
					{
						if (!Bit.IsSet(info.Flags, (int)ProcessFlags.ActiveWait))
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
					WaitForExitTriggered(clearList.Pop(), ProcessEventArgs.ProcessState.Cancel);
			}

			if (cancelled > 0)
				Log.Information("Cancelled power mode wait on {Count} process(es).", cancelled);
		}

		public bool WaitForExit(ProcessEx info)
		{
			bool rv = false;
			try
			{
				lock (waitforexit_lock)
				{
					if (!WaitForExitList.ContainsKey(info.Id))
					{
						WaitForExitList.Add(info.Id, info);
						info.Process.EnableRaisingEvents = true;
						info.Process.Exited += (s, e) => { WaitForExitTriggered(info); };
						rv = true;

						onWaitForExitEvent?.Invoke(this, new ProcessEventArgs() { Control = null, Info = info, State = ProcessEventArgs.ProcessState.Starting });
					}
					else if (!Bit.IsSet(WaitForExitList[info.Id].Flags, info.Flags))
					{
						WaitForExitList[info.Id].Flags |= info.Flags;
						rv = true;
					}
				}
			}
			catch (InvalidOperationException)
			{
				// already exited
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			return rv;
		}

		public ProcessEx[] getExitWaitList()
		{
			return WaitForExitList.Values.ToArray(); // copy is good here
		}

		ProcessController PreviousForegroundController = null;
		ProcessEx PreviousForegroundInfo;

		readonly object foreground_lock = new object();
		Dictionary<int, ProcessController> ForegroundWaitlist = new Dictionary<int, ProcessController>(6);

		public void ForegroundAppChangedEvent(object sender, WindowChangedArgs ev)
		{
			//if (Taskmaster.DebugForeground)
			//	Log.Debug("Process Manager - Foreground Received: {Title}", ev.Title);

			if (PreviousForegroundInfo != null)
			{
				if (PreviousForegroundInfo.Id != ev.Id) // testing previous to current might be superfluous
				{
					if (PreviousForegroundController != null)
					{
						//Log.Debug("PUTTING PREVIOUS FOREGROUND APP to BACKGROUND");
						PreviousForegroundController.Quell(PreviousForegroundInfo);
						onActiveHandled?.Invoke(this, new ProcessEventArgs() { Control = PreviousForegroundController, Info = PreviousForegroundInfo, State = ProcessEventArgs.ProcessState.Reduced });
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
			{
				ForegroundWaitlist.TryGetValue(ev.Id, out prc);
			}

			if (prc != null)
			{
				ProcessEx info = null;
				WaitForExitList.TryGetValue(ev.Id, out info);
				if (info != null)
				{
					if (Taskmaster.DebugForeground)
						Log.Debug("[{FriendlyName}] {Exec} (#{Pid}) on foreground!", prc.FriendlyName, info.Name, info.Id);

					prc.Resume(info);

					onActiveHandled?.Invoke(this, new ProcessEventArgs() { Control = prc, Info = info, State = ProcessEventArgs.ProcessState.Restored });

					PreviousForegroundInfo = info;
					PreviousForegroundController = prc;

					return;
				}
			}

			PreviousForegroundInfo = null;
			PreviousForegroundController = null;
		}

		readonly object systemlock = new object();

		// TODO: ADD CACHE: pid -> process name, path, process

		public event EventHandler<CacheEventArgs> PathCacheUpdate;

		ProcessState CheckPathWatch(ProcessEx info)
		{
			Debug.Assert(info.Process != null);

			try
			{
				if (info.Process.HasExited) // can throw
				{
					if (Taskmaster.ShowInaction)
						Log.Verbose("{ProcessName} (#{ProcessID}) has already exited.", info.Name, info.Id);
					return ProcessState.Invalid;
				}
			}
			catch (InvalidOperationException ex)
			{
				Log.Fatal("INVALID ACCESS to Process");
				Logging.Stacktrace(ex);
				return ProcessState.AccessDenied; //throw; // no point throwing
			}
			catch (System.ComponentModel.Win32Exception ex)
			{
				if (ex.NativeErrorCode != 5) // what was this?
					Log.Warning("Access error: {ProcessName} (#{ProcessID})", info.Name, info.Id);
				return ProcessState.AccessDenied; // we don't care wwhat this error is
			}

			if (!ProcessManagerUtility.FindPath(info))
				return ProcessState.Error;

			// TODO: This needs to be FASTER
			lock (watchlist_lock)
			{
				Debug.Assert(watchlist != null);
				foreach (ProcessController prc in watchlist)
				{
					if (!prc.Enabled) continue;
					if (prc.Path == null) continue;

					if (!string.IsNullOrEmpty(prc.Executable))
					{
						if (prc.Executable.Equals(info.Name, Taskmaster.CaseSensitive ? StringComparison.InvariantCulture : StringComparison.InvariantCultureIgnoreCase))
						{
							if (Taskmaster.DebugPaths)
								Log.Debug("[{FriendlyName}] Path+Exe matched.", prc.FriendlyName);
						}
						else
							continue; // CheckPathWatch does not handle combo path+exes
					}

					//Log.Debug("with: "+ pc.Path);
					if (info.Path.StartsWith(prc.Path, StringComparison.InvariantCultureIgnoreCase)) // TODO: make this compatible with OSes that aren't case insensitive?
					{
						//if (cacheGet)
						//	Log.Debug("[{FriendlyName}] {Exec} (#{Pid}) – PATH CACHE GET!! :D", pc.FriendlyName, name, pid);
						if (Taskmaster.DebugPaths)
							Log.Verbose("[{PathFriendlyName}] Matched at: {Path}", prc.FriendlyName, info.Path);

						ProcessState rv = ProcessState.Invalid;
						try
						{
							rv = prc.Touch(info, foreground: activeappmonitor?.isForeground(info.Id) ?? true);
						}
						catch (Exception ex)
						{
							Log.Fatal("[{FriendlyName}] '{Exec}' (#{Pid}) MASSIVE FAILURE!!!", prc.FriendlyName, info.Name, info.Id);
							Logging.Stacktrace(ex);
							rv = ProcessState.Error;
						}

						ForegroundWatch(info, prc);

						return rv;
					}
				}
			}

			PathCacheUpdate?.Invoke(this, null /* new CacheEventArgs() { Objects = pathCache.Count, Hits = pathCache.Hits, Misses = pathCache.Misses }*/);

			return ProcessState.Invalid;
		}

		public static string[] ProtectList { get; private set; } = { "consent", "winlogon", "wininit", "csrss", "dwm", "taskmgr" };
		public static string[] IgnoreList { get; private set; } = { "dllhost", "svchost", "taskeng", "consent", "taskhost", "rundll32", "conhost", "dwm", "wininit", "csrss", "winlogon", "services", "explorer", "taskmgr", "audiodg" };

		const int LowestInvalidPid = 4;
		bool IgnoreProcessID(int pid)
		{
			return (pid <= LowestInvalidPid || ignorePids.Contains(pid));
		}

		public static bool IgnoreProcessName(string name)
		{
			return IgnoreList.Contains(name, Taskmaster.CaseSensitive ? StringComparer.InvariantCulture : StringComparer.InvariantCultureIgnoreCase);
		}

		public static bool ProtectedProcessName(string name)
		{
			if (Taskmaster.CaseSensitive)
				return ProtectList.Contains(name);

			return ProtectList.Contains(name, StringComparer.InvariantCultureIgnoreCase);
		}

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
					if (execontrol.TryGetValue(LowerCase(ci.Process.ProcessName), out parent))
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

		void ForegroundWatch(ProcessEx info, ProcessController prc)
		{
			if (!prc.ForegroundOnly) return;

			bool keyexists = true;
			lock (foreground_lock)
			{
				if ((keyexists = ForegroundWaitlist.ContainsKey(info.Id)) == false)
					ForegroundWaitlist.Add(info.Id, prc);
			}

			if (keyexists)
			{
				if (Taskmaster.DebugForeground)
					Log.Debug("[{FriendlyName}] {Exec} (#{Pid}) already in foreground watchlist.", prc.FriendlyName, info.Name, info.Id);
			}
			else
			{
				info.Flags |= (int)ProcessFlags.ActiveWait;
				WaitForExit(info);

				if (Taskmaster.DebugForeground)
					Log.Debug("[{FriendlyName}] {Exec} (#{Pid}) added to foreground watchlist.", prc.FriendlyName, info.Name, info.Id);
			}

			onActiveHandled?.Invoke(this, new ProcessEventArgs() { Control = prc, Info = info, State = ProcessEventArgs.ProcessState.Found });
		}

		// TODO: This should probably be pushed into ProcessController somehow.
		ProcessState CheckProcess(ProcessEx info, bool schedule_next = true)
		{
			Debug.Assert(!string.IsNullOrEmpty(info.Name), "CheckProcess received null process name.");
			Debug.Assert(info.Process != null, "CheckProcess received null process.");
			Debug.Assert(!IgnoreProcessID(info.Id), "CheckProcess received invalid process ID: " + info.Id);

			ProcessState state = ProcessState.Invalid;

			if (IgnoreProcessID(info.Id) || IgnoreProcessName(info.Name))
			{
				if (Taskmaster.Trace) Log.Verbose("Ignoring process: {ProcessName} (#{ProcessID})", info.Name, info.Id);
				return ProcessState.Ignored;
			}

			if (string.IsNullOrEmpty(info.Name))
			{
				Log.Warning("#{AppId} details unaccessible, ignored.", info.Id);
				return ProcessState.AccessDenied;
			}

			if (info.Id == Process.GetCurrentProcess().Id) return ProcessState.OK; // IGNORE SELF

			// TODO: check proc.processName for presence in images.
			ProcessController prc = null;
			Debug.Assert(execontrol != null);
			Debug.Assert(info != null);

			lock (execontrol_lock)
			{
				execontrol.TryGetValue(LowerCase(info.Name), out prc);
			}

			if (prc != null)
			{
				if (!prc.Enabled)
				{
					Log.Debug("[{FriendlyName}] Matched but rule disabled; ignoring.");
					return ProcessState.Ignored;
				}

				//await System.Threading.Tasks.Task.Delay(ProcessModifyDelay).ConfigureAwait(false);
				ForegroundWatch(info, prc);

				try
				{
					state = prc.Touch(info, schedule_next, foreground: activeappmonitor?.isForeground(info.Id) ?? true);
				}
				catch (Exception ex)
				{
					Log.Fatal("[{FriendlyName}] '{Exec}' (#{Pid}) MASSIVE FAILURE!!!", prc.FriendlyName, info.Name, info.Id);
					Logging.Stacktrace(ex);
					state = ProcessState.Error;
				}
				return state; // execontrol had this, we don't care about anything else for this.
			}

			//Log.Verbose("{AppName} not in executable control list.", info.Name);

			if (WatchlistWithPath > 0)
			{
				//Log.Verbose("Checking paths for '{ProcessName}' (#{ProcessID})", info.Name, info.Id);
				state = CheckPathWatch(info);
			}

			/*
			if (ControlChildren) // this slows things down a lot it seems
				ChildController(info);
			*/

			return state;
		}

		readonly object BatchProcessingLock = new object();
		int processListLockRestart = 0;
		List<ProcessEx> ProcessBatch = new List<ProcessEx>();
		System.Threading.Timer BatchProcessingTimer;

		void StartBatchProcessingTimer()
		{
			BatchProcessingTimer = new System.Threading.Timer(BatchProcessingTick, null, 500, 1000 * 5);
		}

		void StopBatchProcessingTimer()
		{
			BatchProcessingTimer.Dispose();
			BatchProcessingTimer = null;
		}

		async void BatchProcessingTick(object state)
		{
			lock (BatchProcessingLock)
			{
				if (ProcessBatch.Count == 0)
				{
					StopBatchProcessingTimer();
					if (Taskmaster.DebugProcesses)
						Log.Debug("New instance timer stopped.");
				}
			}

			NewInstanceBatchProcessing().ConfigureAwait(false);
		}

		async Task NewInstanceBatchProcessing()
		{
			if (ProcessBatch.Count == 0) return;

			List<ProcessEx> list = new List<ProcessEx>();

			lock (BatchProcessingLock)
			{
				StopBatchProcessingTimer();
				processListLockRestart = 0;
				Utility.Swap(ref list, ref ProcessBatch);
			}

			foreach (var info in list)
			{
				ProcessDetectedEvent?.Invoke(this, info);
			}

			SignalProcessHandled(-(list.Count));
		}

		public static int Handling { get; private set; }

		void SignalProcessHandled(int adjust)
		{
			onInstanceHandling?.Invoke(this, new InstanceEventArgs { Count = adjust });
		}

		public event EventHandler<InstanceEventArgs> onInstanceHandling;
		public event EventHandler<ProcessEventArgs> onActiveHandled;
		public event EventHandler<ProcessEventArgs> onWaitForExitEvent;

		// This needs to return faster
		async void NewInstanceTriage(object sender, System.Management.EventArrivedEventArgs e)
		{
			SignalProcessHandled(1);

			Stopwatch wmiquerytime = Stopwatch.StartNew();

			// TODO: Instance groups?
			int pid = -1;
			string name = string.Empty;
			string path = string.Empty;
			System.Management.ManagementBaseObject targetInstance;
			try
			{
				targetInstance = (System.Management.ManagementBaseObject)e.NewEvent.Properties["TargetInstance"].Value;
				pid = Convert.ToInt32((string)targetInstance.Properties["Handle"].Value);
				path = (string)(targetInstance.Properties["ExecutablePath"].Value);
				name = System.IO.Path.GetFileNameWithoutExtension(path);
				if (string.IsNullOrEmpty(name))
				{
					// this happens when we have insufficient permissions.
					// as such, NOP.. shouldn't bother testing it here even.
				}
			}
			catch (Exception ex)
			{
				Log.Error("<<WMI>> Failed to extract process ID.");
				Logging.Stacktrace(ex);
				SignalProcessHandled(-1);
				return; // would throw but this is eventhandler
			}
			finally
			{
				wmiquerytime.Stop();
				Statistics.WMIquerytime += wmiquerytime.Elapsed.TotalSeconds;
				Statistics.WMIqueries += 1;
			}

			if (IgnoreProcessID(pid)) return; // We just don't care

			if (string.IsNullOrEmpty(name) && pid <= LowestInvalidPid)
			{
				Log.Error("<<WMI>> Failed to acquire neither process name nor process Id");
				return;
			}

			await NewInstanceTriagePhaseTwo(new ProcessEx()
			{
				Process = null,
				Name = name,
				Path = path,
				Id = pid,
				Flags = 0,
			});
		}

		async Task NewInstanceTriagePhaseTwo(ProcessEx info)
		{
			try
			{
				info.Process = Process.GetProcessById(info.Id);
			}
			catch
			{
				if (Taskmaster.ShowInaction)
					Log.Verbose("Caught #{Pid} but it vanished.", info.Id);
				return;
			}

			if (string.IsNullOrEmpty(info.Name))
			{
				try
				{
					// This happens only when encountering a process with elevated privileges, e.g. admin
					// TODO: Mark as admin process
					info.Name = info.Process.ProcessName;
				}
				catch
				{
					Log.Error("Failed to retrieve name of process #{Pid}", info.Id);
					return;
				}
				finally
				{
					SignalProcessHandled(-1);
				}
			}

			if (Taskmaster.Trace) Log.Verbose("Caught: {ProcessName} (#{ProcessID}) at: {Path}", info.Name, info.Id, info.Path);

			DateTime start = DateTime.MinValue;
			try
			{
				start = info.Process.StartTime;
			}
			catch { /* NOP */ }
			finally
			{
				if (start == DateTime.MinValue)
					start = DateTime.Now;
			}

			if (BatchProcessing)
			{
				lock (BatchProcessingLock)
				{
					ProcessBatch.Add(info);

					// Delay process timer a few times.
					if (BatchProcessingTimer != null)
					{
						processListLockRestart += 1;
						if (processListLockRestart < BatchProcessingThreshold)
							BatchProcessingTimer.Change(BatchDelay, BatchDelay);
					}
					else
						StartBatchProcessingTimer();
				}
			}
			else
			{
				ProcessDetectedEvent?.Invoke(this, info);
				SignalProcessHandled(-1);
			}
		}

		void UpdateHandling(object sender, InstanceEventArgs ev)
		{
			Handling += ev.Count;
			if (Handling < 0)
			{
				if (Taskmaster.Trace)
					Log.Fatal("Handling counter underflow");
				Handling = 0;
			}
		}

		async void RescanOnTimerTick(object state)
		{
			await RescanOnTimer().ConfigureAwait(false);
		}

		async Task RescanOnTimer()
		{
			await Task.Delay(0); // async

			try
			{
				//Log.Verbose("Rescanning...");

				int nextscan = 1;
				int tnext = 0;
				int rescanrequests = 0;
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
					rescanTimer.Dispose();
					rescanTimer = null;
				}
				else
				{
					try
					{
						rescanTimer.Change(500, nextscan.Constrain(1, 360) * (1000 * 60));
						//rescanTimer.Interval = nextscan.Constrain(1, 360) * (1000 * 60);
					}
					catch
					{
						Log.Error("Failed to set rescan timer based on scheduled next scan.");
						rescanTimer.Change(500, 5 * (1000 * 60));
						//rescanTimer.Interval = 5 * (1000 * 60);
					}

					Log.Verbose("Rescan set to occur after {Scan} minutes, next in line: {Name}. Waiting {0}.", nextscan, nextscanfor);
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
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
					new System.Management.ManagementPath(@"\\.\root\CIMV2")); // @"\\.\root\CIMV2"

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
				//var tracequery = new System.Management.EventQuery("SELECT * FROM Win32_ProcessStartTrace");

				//var query = new System.Management.EventQuery("SELECT TargetInstance FROM __InstanceCreationEvent WITHIN 5 WHERE TargetInstance ISA 'Win32_Process'");
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

				if (BatchProcessing)
				{
					StartBatchProcessingTimer();
				}

				watcher.Stopped += (object sender, System.Management.StoppedEventArgs e) =>
				{
					Log.Debug("<<WMI>> New instance watcher stopped.");
					// Restart it maybe? This probably happens when WMI service is stopped or restarted.?
				};

				try
				{
					watcher.Start();
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

		// ctor, constructor
		public ProcessManager()
		{
			Log.Information("CPU/Core count: {Cores}", CPUCount);

			allCPUsMask = 1;
			for (int i = 0; i < CPUCount - 1; i++)
				allCPUsMask = (allCPUsMask << 1) | 1;

			Log.Information("Full CPU mask: {ProcessorBitMask} ({ProcessorMask}) (OS control)",
							Convert.ToString(allCPUsMask, 2), allCPUsMask);

			loadWatchlist();

			InitWMIEventWatcher();

			if (execontrol.Count > 0)
			{
				if (Taskmaster.Trace) Log.Verbose("Starting rescan timer.");

				if (RescanDelay > 0)
				{
					rescanTimer = new System.Threading.Timer(RescanOnTimerTick, null, 500, RescanDelay * 1000);
				}
			}

			onInstanceHandling += UpdateHandling;
			ProcessDetectedEvent += ProcessTriage;
			if (Taskmaster.PathMonitorEnabled)
				ScanEverythingEndEvent += EndScanUpdatePathWatch;

			if (RescanEverythingFrequency > 0)
			{
				RescanEverythingTimer = new System.Timers.Timer();
				RescanEverythingTimer.Interval = RescanEverythingFrequency * 1000;
				RescanEverythingTimer.Elapsed += ScanEverythingRequest;
				RescanEverythingTimer.Start();
			}

			if (Taskmaster.PathCacheLimit > 0)
			{
				ProcessManagerUtility.Initialize();
			}

			Log.Information("<Process Manager> Loaded.");
		}

		/// <summary>
		/// Cleanup.
		/// </summary>
		/// <remarks>
		/// Locks: waitforexit_lock
		/// </remarks>
		public async Task Cleanup()
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
					catch { }
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
						{
							triggerList.Push(info);
						}
					}
					catch (Exception ex)
					{
						Logging.Stacktrace(ex);
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

		bool disposed; // = false;
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		void Dispose(bool disposing)
		{
			if (disposed) return;

			if (disposing)
			{
				if (Taskmaster.Trace) Log.Verbose("Disposing process manager...");

				CancelPowerWait();
				WaitForExitList.Clear();

				try
				{
					if (activeappmonitor != null)
					{
						activeappmonitor.ActiveChanged -= ForegroundAppChangedEvent;
						activeappmonitor = null;
					}
					if (RescanEverythingTimer != null)
					{
						RescanEverythingTimer.Stop(); // shouldn't be necessary
						RescanEverythingTimer.Dispose();
						RescanEverythingTimer = null;
					}
					//watcher.EventArrived -= NewInstanceTriage;
					if (watcher != null)
					{
						watcher.Stop(); // shouldn't be necessary
						watcher.Dispose();
						watcher = null;
					}
					if (rescanTimer != null)
					{
						rescanTimer.Dispose();
						rescanTimer = null;
					}
					if (BatchProcessingTimer != null)
					{
						StopBatchProcessingTimer();
					}
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
					//throw; // would throw but this is dispose
				}

				saveStats();

				try
				{
					lock (execontrol_lock)
					{
						execontrol?.Clear();
						execontrol = null;
					}

					lock (watchlist_lock)
					{
						watchlist?.Clear();
						watchlist = null;
					}
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
					//throw; // would throw but this is dispose 
				}
			}

			disposed = true;
		}

		void saveStats()
		{
			if (Taskmaster.Trace)
				Log.Verbose("Saving stats...");

			lock (watchlist_lock)
			{
				foreach (ProcessController prc in watchlist)
				{
					prc.SaveStats();
				}
			}
		}
	}

	public class ProcessorEventArgs : EventArgs
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
