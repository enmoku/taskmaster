//
// ProcessManager.cs
//
// Author:
//       M.A. (enmoku) <>
//
// Copyright (c) 2016-2018 M.A. (enmoku)
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

using System.Management;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Serilog;
using System.Runtime.InteropServices;

namespace TaskMaster
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

	public class BasicProcessInfo
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
					if (item.FriendlyName == name)
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
				prc = watchlist.Find((ctrl) => ctrl.Executable == executable);
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

			if (TaskMaster.Trace) Log.Verbose("Locating watched paths.");

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

			if (TaskMaster.Trace) Log.Verbose("Path location complete.");
		}

		ActiveAppManager activeappmonitor = null;
		public void hookActiveAppManager(ref ActiveAppManager aamon)
		{
			activeappmonitor = aamon;
			activeappmonitor.ActiveChanged += ForegroundAppChangedEvent;
		}

		public async Task FreeMemoryFor(string executable)
		{
			var procs = Process.GetProcessesByName(executable); // unnecessary maybe?
			if (procs.Length == 0)
			{
				Log.Error("{Exec} not found, not freeing memory for it.", executable);
				return;
			}

			await Task.Yield();

			long saved = 0;
			var allprocs = Process.GetProcesses();
			foreach (var prc in allprocs)
			{
				int pid = -1;
				string name = null;
				try
				{
					pid = prc.Id;
					name = prc.ProcessName;
				}
				catch
				{
					continue;
				}

				if (IgnoreProcessID(pid) || IgnoreProcessName(name))
					continue;

				if (name.Equals(executable)) // ignore the one we're freeing stuff for
					continue;

				//  TODO: Add ignore other processes

				try
				{
					long ns = prc.WorkingSet64;
					EmptyWorkingSet(prc.Handle);
					prc.Refresh();
					long mns = (ns - prc.WorkingSet64);
					saved += mns;
				}
				catch
				{
					continue;
				}
			}
		}

		public async void PageEverythingRequest(object sender, EventArgs e)
		{
			if (TaskMaster.Trace) Log.Verbose("Paging requested.");

			if (!TaskMaster.PagingEnabled) return; // shouldn't happen, but here we have it anyway

			long saved = 0;
			var ws = Process.GetCurrentProcess().WorkingSet64;
			EmptyWorkingSet(Process.GetCurrentProcess().Handle);
			long nws = Process.GetCurrentProcess().WorkingSet64;
			saved += (ws - nws);
			Log.Verbose("Self-paged {PagedMB}.", HumanInterface.ByteString(saved));

			Process[] procs = Process.GetProcesses();

			//Log.Verbose("Scanning {ProcessCount} processes for paging.", procs.Length);

			// DEBUG: if (TaskMaster.VeryVerbose) Console.WriteLine(string.Format("Handling: +{0} = {1} --- PageEverythingRequest", procs.Length, Handling));
			onInstanceHandling?.Invoke(this, new InstanceEventArgs { Count = procs.Length - 2 });

			try
			{
				foreach (Process process in procs)
				{
					int pid = -1;
					string name = null;
					try
					{
						pid = process.Id;
						name = process.ProcessName;
						if (IgnoreProcessID(pid)) continue;
					}
					catch { continue; }

					ProcessController control;
					lock (execontrol_lock)
					{
						if (execontrol.TryGetValue(LowerCase(name), out control))
							if (!control.AllowPaging) continue;
					}

					try
					{
						long ns = process.WorkingSet64;
						EmptyWorkingSet(process.Handle);
						process.Refresh();
						long mns = (ns - process.WorkingSet64);
						saved += mns;
						if (TaskMaster.Trace) Log.Verbose("Paged: {ProcessName} (#{ProcessID}) – {PagedMBs:N1} MBs.", name, pid, mns / 1000000);
					}
					catch
					{
						// NOP
					}
				}
			}
			catch (Exception ex)
			{
				Log.Fatal("Uncaught exception while paging");
				Logging.Stacktrace(ex);
				return;//throw; // event handler, throwing is a nogo 
			}
			finally
			{
				// DEBUG: if (TaskMaster.VeryVerbose) Console.WriteLine(string.Format("Handling: -{0} = {1} --- PageEverythingRequest", procs.Length, Handling));
				onInstanceHandling?.Invoke(this, new InstanceEventArgs { Count = -procs.Length - 2 });
			}

			Log.Information("Paged total of {PagedMBs:N1} MBs.", saved / 1000000);

			await Task.Yield();

			if (TaskMaster.Trace) Log.Verbose("Paging complete.");
		}

		public async void ProcessEverythingRequest(object sender, EventArgs e)
		{
			if (TaskMaster.Trace) Log.Verbose("Rescan requested.");

			try
			{
				await ProcessEverything();
			}
			catch (Exception ex)
			{
				Log.Warning("Scan everything failure.");
				Logging.Stacktrace(ex);
				return; //throw; // event handler, no can throw
			}
		}

		System.Threading.Timer rescanTimer;

		/// <summary>
		/// Processes everything. Pointlessly thorough, but there's no nicer way around for now.
		/// </summary>
		int scaninprogress = 0;
		public async Task ProcessEverything()
		{
			if (!Atomic.Lock(ref scaninprogress))
			{
				Log.Error("Scan request received while old scan was still in progress. Previous scan started at: {Date}", LastRescan.ToString());
				return;
			}

			await Task.Yield();

			LastRescan = DateTime.Now;
			NextRescan = DateTime.Now.AddSeconds(ProcessManager.RescanEverythingFrequency / 1000);

			try
			{
				if (TaskMaster.DebugFullScan)
					Log.Verbose("Processing everything.");

				// TODO: Cache Pids of protected system services to skip them faster.

				var procs = Process.GetProcesses();

				//Log.Debug("Scanning {ProcessCount} processes for changes.", procs.Length);

				// DEBUG: if (TaskMaster.VeryVerbose) Console.WriteLine(string.Format("Handling: +{0} = {1} --- ProcessEverything", procs.Length, Handling));
				onInstanceHandling?.Invoke(this, new InstanceEventArgs { Count = procs.Length - 2 });

				int i = 0;
				foreach (Process process in procs)
				{
					string name = null;
					int pid = 0;
					try
					{
						name = process.ProcessName;
						pid = process.Id;
					}
					catch
					{
						continue; // shouldn't happen
					}

					if (IgnoreProcessID(pid)) continue; // Ignore Idle&System

					if (TaskMaster.DebugFullScan)
						Log.Verbose("Checking [{Iter}/{Count}] {Proc} (#{Pid})", ++i, procs.Length - 2, name, pid); // -2 for Idle&System

					CheckProcess(new BasicProcessInfo { Process = process, Name = name, Id = pid, Path = null, Flags = 0 }, schedule_next: false);
				}

				if (TaskMaster.DebugFullScan)
					Log.Verbose("Full scan: Notifying of work done.");

				// DEBUG: if (TaskMaster.VeryVerbose) Console.WriteLine(string.Format("Handling: -{0} = {1} --- ProcessEverything", procs.Length, Handling));
				onInstanceHandling?.Invoke(this, new InstanceEventArgs { Count = -(procs.Length - 2), Total = Handling });

				if (TaskMaster.DebugFullScan)
					Log.Verbose("Full scan: updating path watch.");

				if (TaskMaster.PathMonitorEnabled)
					UpdatePathWatch();

				if (TaskMaster.DebugFullScan)
					Log.Verbose("Full scan: DONE.");

				//if (TaskMaster.ShowInaction)
				//Log.Information("Scanned {ProcessCount} processes.", procs.Length);
				//if (TaskMaster.PathCacheLimit > 0)
				//	PathCacheStats();
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				scaninprogress = 0;
			}
		}

		static int BatchDelay = 2500;
		static int RescanDelay = 0; // 5 minutes
		public static int RescanEverythingFrequency { get; private set; } = 15; // seconds
		public static DateTime LastRescan { get; private set; } = DateTime.MinValue;
		public static DateTime NextRescan { get; set; } = DateTime.MinValue;
		static bool BatchProcessing; // = false;
		static int BatchProcessingThreshold = 5;
		//static bool ControlChildren = false; // = false;
		SharpConfig.Configuration stats;

		static System.Timers.Timer RescanEverythingTimer = null;

		public void loadWatchlist()
		{
			Log.Information("<Process Manager> Loading configuration...");

			var coreperf = TaskMaster.cfg["Performance"];

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
			RescanDelay = coreperf.GetSetDefault("Rescan frequency", 0, out modified).IntValue.Constrain(0, 60 * 6) * 1000 * 60;
			coreperf["Rescan frequency"].Comment = "In minutes. How often to check for apps that want to be rescanned. Disabled if rescan everything is enabled. 0 disables.";
			dirtyconfig |= modified;

			RescanEverythingFrequency = coreperf.GetSetDefault("Rescan everything frequency", 15, out modified).IntValue.Constrain(0, 60 * 60 * 24);
			if (RescanEverythingFrequency > 0)
			{
				if (RescanEverythingFrequency < 5) RescanEverythingFrequency = 5;
				RescanEverythingFrequency *= 1000; // to seconds
			}
			coreperf["Rescan everything frequency"].Comment = "Frequency (in seconds) at which we rescan everything. 0 disables.";
			dirtyconfig |= modified;

			if (RescanEverythingFrequency > 0)
			{
				Log.Information("Rescan everything every {Frequency} seconds.", RescanEverythingFrequency / 1000);
				RescanDelay = 0;
			}
			else
				Log.Information("Per-app rescan frequency: {RescanDelay:N1}m", RescanDelay / 1000 / 60);

			// --------------------------------------------------------------------------------------------------------

			var fgpausesec = TaskMaster.cfg["Foreground Focus Lost"];
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

			//TaskMaster.cfg["Applications"]["Ignored"].StringValueArray = IgnoreList;
			var ignsetting = TaskMaster.cfg["Applications"];
			string[] newIgnoreList = ignsetting.GetSetDefault("Ignored", IgnoreList, out modified)?.StringValueArray;
			ignsetting.PreComment = "Special hardcoded protection applied to: consent, winlogon, wininit, and csrss.\nThese are vital system services and messing with them can cause severe system malfunctioning.\nMess with the ignore list at your own peril.";
			if (newIgnoreList != null)
			{
				IgnoreList = newIgnoreList;
				Log.Information("Custom application ignore list loaded.");
			}
			else
				TaskMaster.saveConfig(TaskMaster.cfg);
			dirtyconfig |= modified;

			if (dirtyconfig) TaskMaster.MarkDirtyINI(TaskMaster.cfg);

			//Log.Information("Child process monitoring: {ChildControl}", (ControlChildren ? "Enabled" : "Disabled"));

			// --------------------------------------------------------------------------------------------------------

			Log.Information("<Process Manager> Loading watchlist...");
			SharpConfig.Configuration appcfg = TaskMaster.loadConfig(watchfile);
			if (stats == null)
				stats = TaskMaster.loadConfig(statfile);

			if (appcfg.Count() == 0)
			{
				{
					var exsec = appcfg["Internet Explorer"];
					var t1 = exsec.GetSetDefault("Image", "iexplore.exe").StringValue;
					exsec["Image"].Comment = "Process filename";
					var t2 = exsec.GetSetDefault("Priority", 1).IntValue;
					exsec["Priority"].Comment = "0 = low, 1 = below normal, 2 = normal, 3 = above normal, 4 = high";
					var t3 = exsec.GetSetDefault("Rescan", 30).IntValue;
					exsec["Rescan"].Comment = "How often to check for additional processes of this type, just in case.";
					//var t4 = exsec.GetSetDefault("Children", true).BoolValue;
					//exsec["Children"].Comment = "Allow modifying processes started by this.";
					var t5 = exsec.GetSetDefault("Allow paging", false).BoolValue;
					exsec["Allow paging"].Comment = "Allows this process to be pushed to paging/swap file.";

					var stsec = appcfg["SteamApps"];
					var s1 = stsec.GetSetDefault("Search", "steam.exe").StringValue;
					var s2 = stsec.GetSetDefault("Priority", 3).IntValue;
					var st2 = stsec.GetSetDefault("Power mode", "High Performance").StringValue;
					var s4 = stsec.GetSetDefault("Increase", true).BoolValue; // 
					var st3 = stsec.GetSetDefault("Decrease", false).BoolValue; // 
					var s3 = stsec.GetSetDefault("Subpath", "steamapps").StringValue;
					stsec["Subpath"].Comment = "This is used to locate actual path we want to monitor.";
					var s7 = stsec.GetSetDefault("Allow paging", false).BoolValue;

					var gsec = appcfg["Games"];
					var g1 = gsec.GetSetDefault("Path", "C:\\Games").StringValue;
					var g3 = gsec.GetSetDefault("Decrease", false).BoolValue;
					var g4 = gsec.GetSetDefault("Priority", 3).IntValue;

					var wsec = appcfg["Programs"];
					var w1 = wsec.GetSetDefault("Priority", 2).IntValue;
					var w2 = wsec.GetSetDefault("Affinity", 3).IntValue;
					wsec["Affinity"].Comment = "3 = first two cores.";
					var w3 = wsec.GetSetDefault("Path", "C:\\Program Files").StringValue;

					TaskMaster.saveConfig(appcfg);
				}
			}

			// --------------------------------------------------------------------------------------------------------

			int newsettings = coreperf.SettingCount;
			if (dirtyconfig) TaskMaster.MarkDirtyINI(TaskMaster.cfg);

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
				PowerManager.PowerMode pmode = PowerManager.GetModeByName(pmodes);
				if (pmode == PowerManager.PowerMode.Custom)
				{
					Log.Warning("'{SectionName}' has unrecognized power plan: {PowerPlan}", section.Name, pmodes);
					pmode = PowerManager.PowerMode.Undefined;
				}
				var cnt = new ProcessController(section.Name, ProcessHelpers.IntToPriority(prio), (aff != 0 ? aff : allCPUsMask))
				{
					Executable = section.TryGet("Image")?.StringValue ?? null,
					// friendly name is filled automatically
					Increase = (section.TryGet("Increase")?.BoolValue ?? false),
					Decrease = (section.TryGet("Decrease")?.BoolValue ?? true),
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
				if (cnt.ForegroundOnly && cnt.BackgroundPriority.ToInt32() >= cnt.Priority.ToInt32())
				{
					cnt.ForegroundOnly = false;
					Log.Error("[{Friendly}] Background priority equal or higher than foreground priority, ignoring.", cnt.FriendlyName);
				}

				if (cnt.Rescan > 0 && cnt.ExecutableFriendlyName == null)
				{
					Log.Warning("[{FriendlyName}] Configuration error, can not rescan without image name.");
					cnt.Rescan = 0;
				}

				dirtyconfig |= modified;

				//cnt.Children &= ControlChildren;

				Log.Verbose("[{FriendlyName}] Match: {MatchName}, {TargetPriority}, Mask:{Affinity}, Rescan: {Rescan}m, Recheck: {Recheck}s, FgOnly: {Fg}",
						cnt.FriendlyName, (cnt.Executable ?? cnt.Path), cnt.Priority, cnt.Affinity, cnt.Rescan, cnt.Recheck, cnt.ForegroundOnly);

				//cnt.delay = section.Contains("delay") ? section["delay"].IntValue : 30; // TODO: Add centralized default delay
				//cnt.delayIncrement = section.Contains("delay increment") ? section["delay increment"].IntValue : 15; // TODO: Add centralized default increment
				string statkey = null;
				if (cnt.Executable != null)
					statkey = cnt.Executable;
				else if (cnt.Path != null)
					statkey = cnt.Path;

				if (statkey != null)
				{
					cnt.Adjusts = stats[statkey].TryGet("Adjusts")?.IntValue ?? 0;

					var ls = stats[statkey].TryGet("Last seen");
					if (null != ls && !ls.IsEmpty)
					{
						long stamp = long.MinValue;
						try
						{
							stamp = ls.GetValue<long>();
							cnt.LastSeen = stamp.Unixstamp();
						}
						catch { /* NOP */ }
					}
				}

				if (cnt.Path != null || cnt.Subpath != null)
				{
					if (cnt.Locate())
					{
						lock (watchlist_lock)
						{
							watchlist.Add(cnt);
						}

						WatchlistWithPath += 1;

						// TODO: Add "Path" to config
						//if (stats.Contains(cnt.Path))
						//	cnt.Adjusts = stats[cnt.Path].TryGet("Adjusts")?.IntValue ?? 0;
					}
					else
					{
						if (cnt.Subpath != null)
						{
							if (pathinit == null) pathinit = new List<ProcessController>();
							pathinit.Add(cnt);
							Log.Verbose("[{FriendlyName}] ({Subpath}) waiting to be located.", cnt.FriendlyName, cnt.Subpath);
						}
						else
						{
							Log.Warning("[{FriendlyName}] Malconfigured. Insufficient or wrong information.", cnt.FriendlyName);
						}
					}
				}
				else
				{
					lock (watchlist_lock)
					{
						watchlist.Add(cnt);
					}

					lock (execontrol_lock)
					{
						execontrol.Add(LowerCase(cnt.ExecutableFriendlyName), cnt);
					}

					//Log.Verbose("[{ExecutableName}] Added to monitoring.", cnt.FriendlyName);
				}

				// SANITY CHECKING
				if (cnt.ExecutableFriendlyName != null)
				{
					if (IgnoreProcessName(cnt.ExecutableFriendlyName))
					{
						if (TaskMaster.ShowInaction)
							Log.Warning("{Exec} in ignore list; all changes denied.", cnt.ExecutableFriendlyName);
					}
					else if (ProtectedProcessName(cnt.ExecutableFriendlyName))
					{
						if (TaskMaster.ShowInaction)
							Log.Warning("{Exec} in protected list; priority changing denied.");
					}
				}
			}

			// --------------------------------------------------------------------------------------------------------

			Log.Information("Name-based watchlist: {Items} items", execontrol.Count);
			Log.Information("Path-based watchlist: {Items} items", WatchlistWithPath);
		}

		string LowerCase(string str)
		{
			Debug.Assert(!string.IsNullOrEmpty(str));
			return TaskMaster.CaseSensitive ? str : str.ToLower();
		}

		readonly object waitforexit_lock = new object();
		Dictionary<int, BasicProcessInfo> WaitForExitList = new Dictionary<int, BasicProcessInfo>();

		void WaitForExitTriggered(BasicProcessInfo info, ProcessEventArgs.ProcessState state = ProcessEventArgs.ProcessState.Exiting)
		{
			if (TaskMaster.DebugForeground || TaskMaster.DebugPower)
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
					TaskMaster.powermanager.Restore(info.Id).Wait();

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

		public void CancelPowerWait()
		{
			int cancelled = 0;

			Stack<BasicProcessInfo> clearList = null;
			lock (waitforexit_lock)
			{
				if (WaitForExitList.Count == 0) return;

				var items = WaitForExitList.Values;
				clearList = new Stack<BasicProcessInfo>();
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

		public bool WaitForExit(BasicProcessInfo info)
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
			catch (InvalidOperationException ex)
			{
				// already exited
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			return rv;
		}

		public BasicProcessInfo[] getExitWaitList()
		{
			return WaitForExitList.Values.ToArray(); // copy is good here
		}

		int PreviousForegroundId = -1;
		ProcessController PreviousForegroundController = null;
		BasicProcessInfo PreviousForegroundInfo;

		readonly object foreground_lock = new object();
		Dictionary<int, ProcessController> ForegroundWaitlist = new Dictionary<int, ProcessController>(6);

		public void ForegroundAppChangedEvent(object sender, WindowChangedArgs ev)
		{
			//if (TaskMaster.DebugForeground)
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
				BasicProcessInfo info = null;
				WaitForExitList.TryGetValue(ev.Id, out info);
				if (info != null)
				{
					if (TaskMaster.DebugForeground)
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

		/// <summary>
		/// Retrieve file path for the process.
		/// Slow due to use of WMI.
		/// </summary>
		/// <returns>The process path.</returns>
		/// <param name="processId">Process ID</param>
		string GetProcessPathViaWMI(int processId)
		{
			if (!TaskMaster.WMIQueries) return null;

			var wmitime = Stopwatch.StartNew();

			Statistics.WMIqueries++;

			string path = null;
			string wmiQueryString = "SELECT ExecutablePath FROM Win32_Process WHERE ProcessId = " + processId;
			try
			{
				using (var searcher = new System.Management.ManagementObjectSearcher(wmiQueryString))
				{
					foreach (ManagementObject item in searcher.Get())
					{
						object mpath = item["ExecutablePath"];
						if (mpath != null)
						{
							Log.Verbose(string.Format("WMI fetch (#{0}): {1}", processId, path));
							wmitime.Stop();
							Statistics.WMIquerytime += wmitime.Elapsed.TotalSeconds;
							return mpath.ToString();
						}
					}
				}
			}
			catch
			{
				// NOP, don't caree
			}

			wmitime.Stop();
			Statistics.WMIquerytime += wmitime.Elapsed.TotalSeconds;

			return path;
		}

		readonly object systemlock = new object();

		// TODO: ADD CACHE: pid -> process name, path, process

		bool GetPath(BasicProcessInfo info)
		{
			Debug.Assert(info.Path == null, "GetPath called even though path known.");

			try
			{
				info.Path = info.Process.MainModule?.FileName; // this will cause win32exception of various types, we don't Really care which error it is
			}
			catch
			{
				// NOP, don't care 
			}

			if (string.IsNullOrEmpty(info.Path))
			{
				info.Path = GetProcessPathViaC(info.Id);

				if (info.Path == null)
				{
					info.Path = GetProcessPathViaWMI(info.Id);
					if (string.IsNullOrEmpty(info.Path))
						return false;
				}
			}

			return true;
		}

		Cache<int, string, string> pathCache;
		public event EventHandler<CacheEventArgs> PathCacheUpdate;

		public static void PathCacheStats()
		{
			Log.Debug("Path cache state: {Count} items (Hits: {Hits}, Misses: {Misses}, Ratio: {Ratio})",
					  Statistics.PathCacheCurrent, Statistics.PathCacheHits, Statistics.PathCacheMisses,
					  string.Format("{0:N2}", Statistics.PathCacheMisses > 0 ? (Statistics.PathCacheHits / Statistics.PathCacheMisses) : 1));
		}

		ProcessState CheckPathWatch(BasicProcessInfo info)
		{
			Debug.Assert(info.Process != null);

			try
			{
				if (info.Process.HasExited) // can throw
				{
					if (TaskMaster.ShowInaction)
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

			bool cacheGet = false;
			if (pathCache != null)
			{
				string cpath;
				if (pathCache.Get(info.Id, out cpath, info.Name) != null)
				{
					if (!string.IsNullOrEmpty(cpath))
					{
						Statistics.PathCacheHits++;
						cacheGet = true;
						info.Path = cpath;
						//Log.Debug("PATH CACHE ITEM GET: {Path}", info.Path);
					}
					else
					{
						//Statistics.PathCacheMisses++; // will be done when adding the entry
						pathCache.Drop(info.Id);
						//Log.Debug("PATH CACHE ITEM BEGONE!");
					}

					Statistics.PathCacheCurrent = pathCache.Count;
				}
			}

			if (info.Path == null)
			{
				GetPath(info);

				if (info.Path == null)
					return ProcessState.AccessDenied;
			}

			if (pathCache != null && !cacheGet)
			{
				pathCache.Add(info.Id, info.Name, info.Path);
				Statistics.PathCacheMisses++; // adding new entry is as bad a miss


				Statistics.PathCacheCurrent = pathCache.Count;
				if (Statistics.PathCacheCurrent > Statistics.PathCachePeak)
					Statistics.PathCachePeak = Statistics.PathCacheCurrent;
				//Log.Debug("PATH CACHE ADD: {Path}", info.Path);
			}

			// TODO: This needs to be FASTER
			lock (watchlist_lock)
			{
				foreach (ProcessController pc in watchlist)
				{
					if (pc.Path == null) continue;

					//Log.Debug("with: "+ pc.Path);
					if (info.Path.StartsWith(pc.Path, StringComparison.InvariantCultureIgnoreCase)) // TODO: make this compatible with OSes that aren't case insensitive?
					{
						//if (cacheGet)
						//	Log.Debug("[{FriendlyName}] {Exec} (#{Pid}) – PATH CACHE GET!! :D", pc.FriendlyName, name, pid);
						if (TaskMaster.DebugPaths)
							Log.Verbose("[{PathFriendlyName}] matched at: {Path}", pc.FriendlyName, info.Path);

						ProcessState rv = ProcessState.Invalid;
						try
						{
							rv = pc.Touch(info, foreground: activeappmonitor?.isForeground(info.Id) ?? true);
						}
						catch (Exception ex)
						{
							Log.Fatal("[{FriendlyName}] '{Exec}' (#{Pid}) MASSIVE FAILURE!!!", pc.FriendlyName, info.Name, info.Id);
							Logging.Stacktrace(ex);
							rv = ProcessState.AccessDenied;
						}

						ForegroundWatch(info, pc);

						return rv;
					}
				}
			}

			PathCacheUpdate?.Invoke(this, null /* new CacheEventArgs() { Objects = pathCache.Count, Hits = pathCache.Hits, Misses = pathCache.Misses }*/);

			return ProcessState.Invalid;
		}

		public static string[] ProtectList { get; private set; } = { "consent", "winlogon", "wininit", "csrss", "dwm", "taskmgr" };
		public static string[] IgnoreList { get; private set; } = { "dllhost", "svchost", "taskeng", "consent", "taskhost", "rundll32", "conhost", "dwm", "wininit", "csrss", "winlogon", "services", "explorer", "taskmgr" };

		const int LowestInvalidPid = 4;
		bool IgnoreProcessID(int pid)
		{
			return (pid <= LowestInvalidPid);
		}

		public static bool IgnoreProcessName(string name)
		{
			if (TaskMaster.CaseSensitive)
				return IgnoreList.Contains(name);

			return IgnoreList.Contains(name, StringComparer.InvariantCultureIgnoreCase);
		}

		public static bool ProtectedProcessName(string name)
		{
			if (TaskMaster.CaseSensitive)
				return ProtectList.Contains(name);

			return ProtectList.Contains(name, StringComparer.InvariantCultureIgnoreCase);
		}

		/*
		void ChildController(BasicProcessInfo ci)
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

		void ForegroundWatch(BasicProcessInfo info, ProcessController prc)
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
				if (TaskMaster.DebugForeground)
					Log.Debug("[{FriendlyName}] {Exec} (#{Pid}) already in foreground watchlist.", prc.FriendlyName, info.Name, info.Id);
			}
			else
			{
				info.Flags |= (int)ProcessFlags.ActiveWait;
				WaitForExit(info);

				if (TaskMaster.DebugForeground)
					Log.Debug("[{FriendlyName}] {Exec} (#{Pid}) added to foreground watchlist.", prc.FriendlyName, info.Name, info.Id);
			}

			onActiveHandled?.Invoke(this, new ProcessEventArgs() { Control = prc, Info = info, State = ProcessEventArgs.ProcessState.Found });
		}

		ProcessState CheckProcess(BasicProcessInfo info, bool schedule_next = true)
		{
			Debug.Assert(!string.IsNullOrEmpty(info.Name), "CheckProcess received null process name.");
			Debug.Assert(info.Process != null, "CheckProcess received null process.");
			Debug.Assert(!IgnoreProcessID(info.Id), "CheckProcess received invalid process ID: " + info.Id);

			ProcessState state = ProcessState.Invalid;

			if (IgnoreProcessID(info.Id) || IgnoreProcessName(info.Name))
			{
				if (TaskMaster.Trace) Log.Verbose("Ignoring process: {ProcessName} (#{ProcessID})", info.Name, info.Id);
				return ProcessState.AccessDenied;
			}

			if (string.IsNullOrEmpty(info.Name))
			{
				Log.Warning("#{AppId} details unaccessible, ignored.", info.Id);
				return ProcessState.AccessDenied;
			}

			if (info.Id == Process.GetCurrentProcess().Id) return ProcessState.OK; // IGNORE SELF

			// TODO: check proc.processName for presence in images.
			ProcessController pc = null;
			Debug.Assert(execontrol != null);
			Debug.Assert(info != null);

			lock (execontrol_lock)
			{
				execontrol.TryGetValue(LowerCase(info.Name), out pc);
			}

			if (pc != null)
			{
				//await System.Threading.Tasks.Task.Delay(ProcessModifyDelay).ConfigureAwait(false);
				ForegroundWatch(info, pc);

				try
				{
					state = pc.Touch(info, schedule_next, foreground: activeappmonitor?.isForeground(info.Id) ?? true);
				}
				catch (Exception ex)
				{
					Log.Fatal("[{FriendlyName}] '{Exec}' (#{Pid}) MASSIVE FAILURE!!!", pc.FriendlyName, info.Name, info.Id);
					Logging.Stacktrace(ex);
					state = ProcessState.AccessDenied;
				}
				return state; // execontrol had this, we don't care about anything else for this.
			}

			//Log.Verbose("{AppName} not in executable control list.", info.Name);

			if (WatchlistWithPath > 0)
			{
				//Log.Verbose("Checking paths for '{ProcessName}' (#{ProcessID})", info.Name, info.Id);
				state = CheckPathWatch(info);
			}

			if (state != ProcessState.Invalid) return state; // we don't care to process more

			/*
			if (ControlChildren) // this slows things down a lot it seems
				ChildController(info);
			*/

			return ProcessState.Invalid;
		}

		readonly object BatchProcessingLock = new object();
		int processListLockRestart = 0;
		List<BasicProcessInfo> ProcessBatch = new List<BasicProcessInfo>();
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
					if (TaskMaster.DebugProcesses)
						Log.Debug("New instance timer stopped.");
				}
			}

			await Task.Yield();

			try
			{
				NewInstanceBatchProcessing();
			}
			catch (Exception ex)
			{
				Log.Fatal("<Process Manager> Error batch processing new instances.");
				Logging.Stacktrace(ex);
			}
		}

		void NewInstanceBatchProcessing()
		{
			List<BasicProcessInfo> list = new List<BasicProcessInfo>();
			lock (BatchProcessingLock)
			{
				StopBatchProcessingTimer();
				processListLockRestart = 0;
				Utility.Swap(ref list, ref ProcessBatch);
			}

			if (list.Count == 0) return;

			//Console.WriteLine("Processing {0} delayed processes.", list.Count);
			try
			{
				foreach (var info in list)
				{
					CheckProcess(info);
				}
			}
			catch (Exception ex)
			{
				Log.Fatal("Uncaught exception while processing new instances");
				Logging.Stacktrace(ex);
				return; // throw; // no point
			}
			finally
			{
				// DEBUG: if (TaskMaster.VeryVerbose) Console.WriteLine(string.Format("Handling: -{0} = {1} --- NewInstanceBatchProcessing", list.Count, Handling));
				onInstanceHandling?.Invoke(this, new InstanceEventArgs { Count = -list.Count });
			}

			//list.Clear(); // unnecessary?
		}

		public static int Handling { get; set; }

		public event EventHandler<InstanceEventArgs> onInstanceHandling;
		public event EventHandler<ProcessEventArgs> onActiveHandled;
		public event EventHandler<ProcessEventArgs> onWaitForExitEvent;

		// This needs to return faster
		async void NewInstanceTriage(object sender, System.Management.EventArrivedEventArgs e)
		{
			await Task.Yield(); // is there any reason to delay this?

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

			//Handle=pid
			// FIXME
			// DEBUG: if (TaskMaster.VeryVerbose) Console.WriteLine(string.Format("Handling: +{0} = {1} --- NewInstanceTriage", 1, Handling));

			Process process = null;
			try
			{
				process = Process.GetProcessById(pid);
			}
			catch
			{
				if (TaskMaster.ShowInaction)
					Log.Verbose("Caught #{Pid} but it vanished.", pid);
				return;
			}

			if (string.IsNullOrEmpty(name))
			{
				try
				{
					// This happens only when encountering a process with elevated privileges, e.g. admin
					// TODO: Mark as admin process
					name = process.ProcessName;
				}
				catch
				{
					Log.Error("Failed to retrieve name of process #{Pid}", pid);
					return;
				}
			}

			onInstanceHandling?.Invoke(this, new InstanceEventArgs { Count = 1 });

			if (TaskMaster.Trace) Log.Verbose("Caught: {ProcessName} (#{ProcessID}) at: {Path}", name, pid, path);

			DateTime start = DateTime.MinValue;
			try
			{
				start = process.StartTime;
			}
			catch { /* NOP */ }
			finally
			{
				if (start == DateTime.MinValue)
					start = DateTime.Now;
			}

			BasicProcessInfo info = new BasicProcessInfo
			{
				Name = name,
				Id = pid,
				Process = process,
				Path = path,
				Flags = 0
			};

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
				try
				{
					var rv = CheckProcess(info);
				}
				catch (Exception ex)
				{
					Log.Fatal("Uncaught exception while handling new instance");
					Logging.Stacktrace(ex);
					return; // would throw but this is event handler
				}
				finally
				{
					onInstanceHandling?.Invoke(this, new InstanceEventArgs { Count = -1 });
				}
			}
		}

		void UpdateHandling(object sender, InstanceEventArgs ev)
		{
			Handling += ev.Count;
			if (Handling < 0)
			{
				Log.Fatal("Handling counter underflow");
				Handling = 0;
			}
		}

		async void RescanOnTimer(object state)
		{
			//Log.Verbose("Rescanning...");

			await Task.Yield();

			int nextscan = 1;
			int tnext = 0;
			int rescanrequests = 0;
			string nextscanfor = null;

			lock (execontrol_lock)
			{
				var pcs = execontrol.Values;
				foreach (ProcessController pc in pcs)
				{
					if (pc.Rescan == 0) continue;

					rescanrequests++;

					tnext = pc.TryScan();

					if (tnext > nextscan)
					{
						nextscan = tnext;
						nextscanfor = pc.FriendlyName;
					}
				}
			}

			if (rescanrequests == 0)
			{
				if (TaskMaster.Trace) Log.Verbose("No apps have requests to rescan, stopping rescanning.");
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

		System.Management.ManagementEventWatcher watcher = null;
		void InitWMIEventWatcher()
		{
			if (!TaskMaster.WMIPolling) return;

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
					"SELECT * FROM __InstanceCreationEvent WITHIN " + TaskMaster.WMIPollDelay + " WHERE TargetInstance ISA 'Win32_Process'");
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
		const string statfile = "Watchlist.Statistics.ini";
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
				if (TaskMaster.Trace) Log.Verbose("Starting rescan timer.");

				if (RescanDelay > 0)
				{
					rescanTimer = new System.Threading.Timer(RescanOnTimer, null, 500, RescanDelay); // 5 minutes
				}
			}

			onInstanceHandling += UpdateHandling;

			if (RescanEverythingFrequency > 0)
			{
				RescanEverythingTimer = new System.Timers.Timer();
				RescanEverythingTimer.Interval = RescanEverythingFrequency;
				RescanEverythingTimer.Elapsed += ProcessEverythingRequest;
				RescanEverythingTimer.Start();
			}

			if (TaskMaster.PathCacheLimit > 0)
				pathCache = new Cache<int, string, string>(TaskMaster.PathCacheMaxAge, TaskMaster.PathCacheLimit, (TaskMaster.PathCacheLimit / 10).Constrain(5, 10));

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
			await Task.Yield();
			Stack<BasicProcessInfo> triggerList;
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

				triggerList = new Stack<BasicProcessInfo>();
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
				if (TaskMaster.Trace) Log.Verbose("Disposing process manager...");

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

					if (pathCache != null)
					{
						pathCache.Dispose();
						pathCache = null;
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
			Log.Verbose("Saving stats...");

			if (stats == null)
				stats = TaskMaster.loadConfig(statfile);

			lock (watchlist_lock)
			{
				foreach (ProcessController proc in watchlist)
				{
					// BROKEN
					string key = null;
					if (proc.Executable != null)
						key = proc.Executable;
					else if (proc.Path != null)
						key = proc.Path;
					else
						continue;

					if (proc.Adjusts > 0)
					{
						stats[key]["Adjusts"].IntValue = proc.Adjusts;
						TaskMaster.MarkDirtyINI(stats);
					}
					if (proc.LastSeen != DateTime.MinValue)
					{
						stats[key]["Last seen"].SetValue(proc.LastSeen.Unixstamp());
						TaskMaster.MarkDirtyINI(stats);
					}
				}
			}
		}

		// https://stackoverflow.com/a/34991822
		public static string GetProcessPathViaC(int pid)
		{
			var processHandle = OpenProcess(0x0400 | 0x0010, false, pid);

			if (processHandle == IntPtr.Zero)
			{
				return null;
			}

			const int lengthSb = 4000;

			var sb = new System.Text.StringBuilder(lengthSb);

			string result = null;

			if (GetModuleFileNameEx(processHandle, IntPtr.Zero, sb, lengthSb) > 0)
			{
				//result = Path.GetFileName(sb.ToString());
				result = sb.ToString();
			}

			CloseHandle(processHandle);

			return result;
		}

		[DllImport("kernel32.dll")]
		public static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

		[DllImport("psapi.dll")]
		static extern uint GetModuleFileNameEx(IntPtr hProcess, IntPtr hModule, [Out] System.Text.StringBuilder lpBaseName, [In] [MarshalAs(UnmanagedType.U4)] int nSize);

		[DllImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		static extern bool CloseHandle(IntPtr hObject);

		/// <summary>
		/// Empties the working set.
		/// </summary>
		/// <returns>Uhh?</returns>
		/// <param name="hwProc">Process handle.</param>
		[DllImport("psapi.dll")]
		static extern int EmptyWorkingSet(IntPtr hwProc);
	}

	public class ProcessorEventArgs : EventArgs
	{
		public float Current;
		public float Average;
		public float Low;
		public float High;

		public PowerManager.PowerMode Mode;

		public float Pressure;

		public bool Handled;
	}
}
