//
// ProcessManager.cs
//
// Author:
//       M.A. (enmoku) <>
//
// Copyright (c) 2016 M.A. (enmoku)
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
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using Serilog;
using Serilog.Sinks.File;
using System.Runtime.InteropServices;
using System.IO;
using System.Timers;
using System.Runtime.Remoting.Messaging;
using System.Windows.Forms;

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

	sealed public class ProcessManager : IDisposable
	{
		/// <summary>
		/// Actively watched process images.
		/// </summary>
		public List<ProcessController> watchlist = new List<ProcessController>();
		object watchlist_lock = new object();

		/// <summary>
		/// Number of watchlist items with path restrictions.
		/// </summary>
		int WatchlistWithPath = 0;

		/// <summary>
		/// Paths not yet properly initialized.
		/// </summary>
		public List<ProcessController> pathinit;
		object pathwatchlock = new object();
		/// <summary>
		/// Executable name to ProcessControl mapping.
		/// </summary>
		Dictionary<string, ProcessController> execontrol = new Dictionary<string, ProcessController>();

		public ProcessController getController(string executable)
		{
			ProcessController rv = null;
			execontrol.TryGetValue(LowerCase(executable), out rv);
			return rv;
		}

		public static int allCPUsMask = 1;
		public static int CPUCount = Environment.ProcessorCount;

		int ProcessModifyDelay = 4800;

		/// <summary>
		/// Empties the working set.
		/// </summary>
		/// <returns>Uhh?</returns>
		/// <param name="hwProc">Process handle.</param>
		[System.Runtime.InteropServices.DllImport("psapi.dll")]
		static extern int EmptyWorkingSet(IntPtr hwProc);

		/// <summary>
		/// Gets the control class instance of the executable if it exists.
		/// </summary>
		/// <returns>ProcessControl </returns>
		/// <param name="executable">Executable.</param>
		public ProcessController getControl(string executable)
		{
			foreach (ProcessController ctrl in watchlist)
			{
				if (ctrl.Executable == executable)
					return ctrl;
			}
			Log.Warning("{ExecutableName} was not found!", executable);
			return null;
		}

		/// <summary>
		/// Updates the path watch, trying to locate any watched directories.
		/// </summary>
		void UpdatePathWatch()
		{
			if (pathinit == null) return;

			Log.Verbose("Locating watched paths.");
			lock (pathwatchlock)
			{
				if (pathinit.Count > 0)
				{
					foreach (ProcessController pc in pathinit.ToArray())
					{
						if (pc.Locate())
						{
							watchlist.Add(pc);
							pathinit.Remove(pc);
							WatchlistWithPath += 1;
						}
					}
				}

				if (pathinit.Count == 0)
					pathinit = null;
			}
			Log.Verbose("Path location complete.");
		}

		public async void PageEverythingRequest(object sender, EventArgs e)
		{
			Log.Verbose("Paging requested.");
			if (!TaskMaster.PagingEnabled) return; // shouldn't happen, but here we have it anyway

			long saved = 0;
			var ws = Process.GetCurrentProcess().WorkingSet64;
			EmptyWorkingSet(Process.GetCurrentProcess().Handle);
			long nws = Process.GetCurrentProcess().WorkingSet64;
			saved += (ws - nws);
			Log.Verbose("Self-paged {PagedMBs:N1} MBs.", saved / 1000000);

			Process[] procs = Process.GetProcesses();

			Log.Verbose("Scanning {ProcessCount} processes for paging.", procs.Length);

			// DEBUG: if (TaskMaster.VeryVerbose) Console.WriteLine(string.Format("Handling: +{0} = {1} --- PageEverythingRequest", procs.Length, Handling));
			onInstanceHandling?.Invoke(this, new InstanceEventArgs { Count = procs.Length });

			try
			{
				foreach (Process process in procs)
				{
					Process prc = process;
					ProcessController control;
					if (execontrol.TryGetValue(LowerCase(prc.ProcessName), out control))
					{
						if (control.AllowPaging)
						{
							long ns = prc.WorkingSet64;
							EmptyWorkingSet(prc.Handle);
							prc.Refresh();
							long mns = (ns - prc.WorkingSet64);
							saved += mns;
							Log.Verbose("Paged: {ProcessName} (#{ProcessID}) – {PagedMBs:N1} MBs.", prc.ProcessName, prc.Id, mns / 1000000);
						}
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.StackTrace);
				Log.Warning("Uncaught exception while paging");
				throw;
			}
			finally
			{
				// DEBUG: if (TaskMaster.VeryVerbose) Console.WriteLine(string.Format("Handling: -{0} = {1} --- PageEverythingRequest", procs.Length, Handling));
				onInstanceHandling?.Invoke(this, new InstanceEventArgs { Count = -procs.Length });
			}

			Log.Information("Paged total of {PagedMBs:N1} MBs.", saved / 1000000);

			await Task.Yield();
			Log.Verbose("Paging complete.");
		}

		public void ProcessEverythingRequest(object sender, EventArgs e)
		{
			Log.Verbose("Rescan requested.");
			ProcessEverything();
		}

		System.Timers.Timer rescanTimer = new System.Timers.Timer(1000 * 5 * 60); // 5 minutes

		/// <summary>
		/// Processes everything. Pointlessly thorough, but there's no nicer way around for now.
		/// </summary>
		public async Task ProcessEverything()
		{
			Log.Verbose("Processing everything.");

			Process[] procs = Process.GetProcesses();

			Log.Debug("Scanning {ProcessCount} processes for changes.", procs.Length);

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

				Log.Verbose("Checking [{ProcessIterator}/{ProcessCount}] '{ProcessName}' (#{Pid})", ++i, procs.Length - 2, name, pid); // -2 for Idle&System

				await CheckProcess(new BasicProcessInfo { Process = process, Name = name, Id = pid, Path = null }, schedule_next: false);
			}

			// DEBUG: if (TaskMaster.VeryVerbose) Console.WriteLine(string.Format("Handling: -{0} = {1} --- ProcessEverything", procs.Length, Handling));
			onInstanceHandling?.Invoke(this, new InstanceEventArgs { Count = -(procs.Length - 2), Total = Handling });

			if (TaskMaster.PathMonitorEnabled)
				UpdatePathWatch();

			Log.Information("Scanned {ProcessCount} processes.", procs.Length);
			PathCacheStats();
		}


		public static int PowerdownDelay { get; set; } = 7000;

		static int BatchDelay = 2500;
		static int RescanDelay = 1000 * 60 * 5; // 5 minutes
		static int RescanEverythingFrequency = 0; //
		static bool BatchProcessing; // = false;
		static int BatchProcessingThreshold = 5;
		static bool ControlChildren = false; // = false;
		SharpConfig.Configuration stats;

		static System.Timers.Timer RescanEverythingTimer = null;

		public void loadWatchlist()
		{
			Log.Verbose("Loading general process configuration...");

			var coreperf = TaskMaster.cfg["Performance"];

			bool dirtyconfig = false, tdirty = false;
			ControlChildren = coreperf.GetSetDefault("Child processes", false, out tdirty).BoolValue;
			dirtyconfig |= tdirty;
			BatchProcessing = coreperf.GetSetDefault("Batch processing", false, out tdirty).BoolValue;
			coreperf["Batch processing"].Comment = "Process management works in delayed batches instead of immediately.";
			dirtyconfig |= tdirty;
			Log.Information("Batch processing: {BatchProcessing}", (BatchProcessing ? "Enabled" : "Disabled"));
			if (BatchProcessing)
			{
				BatchDelay = coreperf.GetSetDefault("Batch processing delay", 2500, out tdirty).IntValue;
				dirtyconfig |= tdirty;
				Log.Information("Batch processing delay: {BatchProcessingDelay:N1}s", BatchDelay / 1000);
				BatchProcessingThreshold = coreperf.GetSetDefault("Batch processing threshold", 5, out tdirty).IntValue;
				dirtyconfig |= tdirty;
				Log.Information("Batch processing threshold: {BatchProcessingThreshold}", BatchProcessingThreshold);
			}
			RescanDelay = coreperf.GetSetDefault("Rescan frequency", 5, out tdirty).IntValue * 1000 * 60;
			coreperf["Rescan frequency"].Comment = "How often to check for apps that want to be rescanned.";
			dirtyconfig |= tdirty;
			Log.Information("Rescan frequency: {RescanDelay:N1}m", RescanDelay / 1000 / 60);

			RescanEverythingFrequency = coreperf.GetSetDefault("Rescan everything frequency", 0, out tdirty).IntValue;
			if (RescanEverythingFrequency > 0)
			{
				if (RescanEverythingFrequency < 5) RescanEverythingFrequency = 5;
				RescanEverythingFrequency *= 1000 * 60; // to minutes
			}
			coreperf["Rescan everything frequency"].Comment = "Frequency (in minutes) at which we rescan everything. 0 disables.";
			dirtyconfig |= tdirty;
			if (RescanEverythingFrequency > 0)
				Log.Information("Rescan everything every {Frequency} minutes.", RescanEverythingFrequency / 1000 / 60);

			var powersec = TaskMaster.cfg["Power"];
			PowerdownDelay = powersec.GetSetDefault("Powerdown delay", 7, out tdirty).IntValue * 1000;
			powersec["Powerdown delay"].Comment = "Delay in seconds to restore old power mode after elevated power mode is no longer needed.\n0 disables the delay.";
			dirtyconfig |= tdirty;

			// --------------------------------------------------------------------------------------------------------

			//TaskMaster.cfg["Applications"]["Ignored"].StringValueArray = IgnoreList;
			var ignsetting = TaskMaster.cfg["Applications"];
			string[] newIgnoreList = ignsetting.GetSetDefault("Ignored", IgnoreList, out tdirty)?.StringValueArray;
			ignsetting.PreComment = "Special hardcoded protection applied to: consent, winlogon, wininit, and csrss.\nThese are vital system services and messing with them can cause severe system malfunctioning.\nMess with the ignore list at your own peril.";
			if (newIgnoreList != null)
			{
				IgnoreList = newIgnoreList;
				Log.Information("Custom application ignore list loaded.");
			}
			else
				TaskMaster.saveConfig(TaskMaster.cfg);
			dirtyconfig |= tdirty;

			if (dirtyconfig) TaskMaster.MarkDirtyINI(TaskMaster.cfg);

			Log.Information("Child process monitoring: {ChildControl}", (ControlChildren ? "Enabled" : "Disabled"));

			// --------------------------------------------------------------------------------------------------------

			Log.Verbose("Loading watchlist...");
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
					BackgroundIO = (section.TryGet("Background I/O")?.BoolValue ?? false), // Doesn't work
					ForegroundOnly = (section.TryGet("Foreground only")?.BoolValue ?? false),
					Recheck = (section.TryGet("Recheck")?.IntValue ?? 0),
					PowerPlan = pmode,
					IgnoreList = (section.TryGet("Ignore")?.StringValueArray ?? null),
					Children = (section.TryGet("Children")?.BoolValue ?? false),
					ChildPriority = ProcessHelpers.IntToPriority(section.TryGet("Child priority")?.IntValue ?? prio),
					ChildPriorityReduction = section.TryGet("Child priority reduction")?.BoolValue ?? false,
					AllowPaging = (section.TryGet("Allow paging")?.BoolValue ?? false),
				};

				if (cnt.Rescan > 0 && cnt.ExecutableFriendlyName == null)
				{
					Log.Warning("[{FriendlyName}] Configuration error, can not rescan without image name.");
					cnt.Rescan = 0;
				}

				dirtyconfig |= tdirty;

				cnt.Children &= ControlChildren;

				Log.Verbose("[{FriendlyName}] Match: {MatchName}, {TargetPriority}, Mask:{Affinity}, Rescan: {Rescan}m, Recheck: {Recheck}s",
							cnt.FriendlyName, (cnt.Executable == null ? cnt.Path : cnt.Executable), cnt.Priority, cnt.Affinity, cnt.Rescan, cnt.Recheck);

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
						catch { }
					}
				}

				if (cnt.Path != null || cnt.Subpath != null)
				{
					if (cnt.Locate())
					{
						lock (watchlist_lock)
							watchlist.Add(cnt);
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
						watchlist.Add(cnt);
					execontrol.Add(LowerCase(cnt.ExecutableFriendlyName), cnt);
					//Log.Verbose("[{ExecutableName}] Added to monitoring.", cnt.FriendlyName);
				}

				// SANITY CHECKING
				if (cnt.ExecutableFriendlyName != null)
					if (IgnoreProcessName(cnt.ExecutableFriendlyName))
						Log.Error("{Exec} in ignore list.");

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

		/// <summary>
		/// Retrieve file path for the process.
		/// Slow due to use of WMI.
		/// </summary>
		/// <returns>The process path.</returns>
		/// <param name="processId">Process ID</param>
		string GetProcessPathViaWMI(int processId)
		{
			if (!TaskMaster.WMIQueries) return null;

			Stopwatch n = Stopwatch.StartNew();

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
							n.Stop();
							Statistics.WMIquerytime += n.Elapsed.TotalSeconds;
							return mpath.ToString();
						}
					}
				}
			}
			catch
			{
			}

			n.Stop();
			Statistics.WMIquerytime += n.Elapsed.TotalSeconds;

			return path;
		}

		object systemlock = new object();

		// TODO: ADD CACHE: pid -> process name, path, process

		bool GetPath(ref BasicProcessInfo info)
		{
			Debug.Assert(info.Path == null, "GetPath called even though path known.");

			try
			{
				info.Path = info.Process.MainModule?.FileName; // this will cause win32exception of various types, we don't Really care which error it is
			}
			catch
			{
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

		System.Timers.Timer pathCacheTimer;
		Dictionary<int, BasicProcessInfo> pathCache = new Dictionary<int, BasicProcessInfo>(TaskMaster.PathCacheLimit + 2);
		Stack<int> pathCacheStack = new Stack<int>();

		public static void PathCacheStats()
		{
			Log.Debug("Path cache state: {Count} items (Hits: {Hits}, Misses: {Misses}, Ratio: {Ratio:N2})",
					  Statistics.PathCacheCurrent, Statistics.PathCacheHits, Statistics.PathCacheMisses,
					  Statistics.PathCacheMisses > 0 ? (Statistics.PathCacheHits / Statistics.PathCacheMisses) : 1);
		}

		ProcessState CheckPathWatch(BasicProcessInfo info)
		{
			Debug.Assert(info.Process != null);

			try
			{
				if (info.Process.HasExited) // can throw
				{
					Log.Verbose("{ProcessName} (#{ProcessID}) has already exited.", info.Name, info.Id);
					return ProcessState.Invalid;
				}
			}
			catch (InvalidOperationException ex)
			{
				Logging.Log("Invalid access to Process");
				Console.WriteLine(ex.StackTrace);
				throw;
			}
			catch (Win32Exception ex)
			{
				if (ex.NativeErrorCode != 5) // what was this?
					Log.Warning("Access error: {ProcessName} (#{ProcessID})", info.Name, info.Id);
				return ProcessState.AccessDenied; // we don't care wwhat this error is
			}

			bool cacheGet = false;
			bool cacheSet = false;
			BasicProcessInfo cacheInfo;
			if (pathCache.TryGetValue(info.Id, out cacheInfo))
			{
				if (info.Name == cacheInfo.Name)
				{
					/*
					try
					{
						info.Process.Refresh(); // we're only copying path, not the entire thing.
					}
					catch
					{
						Log.Warning("Failed to refresh '{Exe}' (#{Pid}).", info.Name, info.Id);
					}
					*/

					//path = info.Path;
					Statistics.PathCacheHits++;
					cacheGet = true;
					info.Path = cacheInfo.Path;
				}
				else
				{
					Statistics.PathCacheMisses++;
					pathCache.Remove(info.Id);
				}
				Statistics.PathCacheCurrent = pathCache.Count;
			}

			if (info.Path == null)
			{
				GetPath(ref info);

				if (info.Path == null)
					return ProcessState.AccessDenied;
			}

			if (pathCacheTimer == null)
			{
				pathCacheTimer = new System.Timers.Timer();
				pathCacheTimer.Interval = 1000 * 60 * 60;// 60 minutes
				pathCacheTimer.Elapsed += (sender, e) =>
				{
					Log.Debug("Pruning path cache: {Count} / {Max}", pathCache.Count, TaskMaster.PathCacheLimit);
					while (pathCache.Count > TaskMaster.PathCacheLimit)
					{
						pathCache.Remove(pathCacheStack.Pop());
						// TODO: Remove items with least hits instead of oldest
					}
				};
				pathCacheTimer.Start();
			}

			if (!cacheGet)
			{
				pathCache.Add(info.Id, info);
				pathCacheStack.Push(info.Id);
				cacheSet = true;

				Statistics.PathCacheCurrent = pathCache.Count;
				if (Statistics.PathCacheCurrent > Statistics.PathCachePeak)
					Statistics.PathCachePeak = Statistics.PathCacheCurrent;
			}

			// TODO: This needs to be FASTER
			lock (pathwatchlock)
			{
				foreach (ProcessController pc in watchlist)
				{
					if (pc.Path == null) continue;

					//Log.Debug("with: "+ pc.Path);
					if (info.Path.StartsWith(pc.Path, StringComparison.InvariantCultureIgnoreCase)) // TODO: make this compatible with OSes that aren't case insensitive?
					{
						//if (cacheGet)
						//	Log.Debug("[{FriendlyName}] {Exec} (#{Pid}) – PATH CACHE GET!! :D", pc.FriendlyName, name, pid);
						Log.Verbose("[{PathFriendlyName}] matched at: {Path}", // TODO: de-ugly
									pc.FriendlyName, info.Path);

						try
						{
							return pc.Touch(info);
						}
						catch
						{
							Log.Fatal("[{FriendlyName}] '{Exec}' (#{Pid}) MASSIVE FAILURE!!!", pc.FriendlyName, info.Name, info.Id);
							return ProcessState.AccessDenied;
						}
					}
				}
			}

			return ProcessState.Invalid;
		}

		public static string[] ProtectList { get; private set; } = { "consent", "winlogon", "wininit", "csrss", "dwm" };
		public static string[] IgnoreList { get; private set; } = { "dllhost", "svchost", "taskeng", "consent", "taskhost", "rundll32", "conhost", "dwm", "wininit", "csrss", "winlogon", "services", "explorer" };

		const int LowestInvalidPid = 4;

		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
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

		async Task CheckProcess(BasicProcessInfo info, bool schedule_next = true)
		{
			Debug.Assert(!string.IsNullOrEmpty(info.Name), "CheckProcess received null process name.");
			Debug.Assert(info.Process != null, "CheckProcess received null process.");
			Debug.Assert(!IgnoreProcessID(info.Id), "CheckProcess received invalid process ID: " + info.Id);

			await Task.Yield();

			if (IgnoreProcessID(info.Id) || IgnoreProcessName(info.Name))
			{
				Log.Verbose("Ignoring process: {ProcessName} (#{ProcessID})", info.Name, info.Id);
				return;
			}

			if (string.IsNullOrEmpty(info.Name))
			{
				Log.Warning("#{AppId} details unaccessible, ignored.", info.Id);
				return;
			}

			ProcessState state = ProcessState.Invalid;

			// TODO: check proc.processName for presence in images.
			ProcessController pc;
			if (execontrol.TryGetValue(LowerCase(info.Name), out pc))
			{
				//await System.Threading.Tasks.Task.Delay(ProcessModifyDelay).ConfigureAwait(false);
				try
				{
					state = pc.Touch(info, schedule_next);
				}
				catch
				{
					Log.Fatal("[{FriendlyName}] '{Exec}' (#{Pid}) MASSIVE FAILURE!!!", pc.FriendlyName, info.Name, info.Id);
				}
				return; // execontrol had this, we don't care about anything else for this.

			}
			//Log.Verbose("{AppName} not in executable control list.", info.Name);

			if (WatchlistWithPath > 0)
			{
				//Log.Verbose("Checking paths for '{ProcessName}' (#{ProcessID})", info.Name, info.Id);
				state = CheckPathWatch(info);
			}

			if (state != ProcessState.Invalid) return; // we don't care to process more

			if (ControlChildren) // this slows things down a lot it seems
				ChildController(info);
		}

		public struct BasicProcessInfo
		{
			public string Name;
			public string Path;
			public int Id;
			public Process Process;
			public DateTime Start;
		}

		void Swap<T>(ref T a, ref T b)
		{
			T temp = a;
			a = b;
			b = temp;
		}

		object processListLock = new object();
		int processListLockRestart = 0;
		List<BasicProcessInfo> processList = new List<BasicProcessInfo>();
		System.Timers.Timer processListTimer = new System.Timers.Timer(1000 * 5);
		void ProcessListTimerTick(object sender, EventArgs e)
		{
			lock (processListLock)
			{
				if (processList.Count == 0)
				{
					processListTimer.Stop();
#if DEBUG
					Log.Verbose("New instance timer stopped.");
#endif
				}
			}
			NewInstanceBatchProcessing();
		}

		async Task NewInstanceBatchProcessing()
		{
			List<BasicProcessInfo> list = new List<BasicProcessInfo>();
			lock (processListLock)
			{
				processListTimer.Stop();
				processListLockRestart = 0;
				Swap(ref list, ref processList);
			}

			if (list.Count == 0) return;

			//Console.WriteLine("Processing {0} delayed processes.", list.Count);
			try
			{
				foreach (var info in list)
				{
					//Console.WriteLine("Delayed.Processing = {0}, pid:{1}, process:{2}", info.Name, info.Id, (info.Process!=null));
					await CheckProcess(info);
				}
			}
			catch (Exception e)
			{
				Log.Warning("Uncaught exception while processing new instances");
				Log.Fatal(e.StackTrace);
				Console.WriteLine(e.StackTrace);
				throw;
			}
			finally
			{
				// DEBUG: if (TaskMaster.VeryVerbose) Console.WriteLine(string.Format("Handling: -{0} = {1} --- NewInstanceBatchProcessing", list.Count, Handling));
				onInstanceHandling?.Invoke(this, new InstanceEventArgs { Count = -list.Count });
			}

			//list.Clear(); // unnecessary?
		}

		public static int Handling { get; set; }

		public static event EventHandler<InstanceEventArgs> onInstanceHandling;

		async void NewInstanceTriage(object sender, System.Management.EventArrivedEventArgs e)
		{
			await Task.Yield();

			Stopwatch n = Stopwatch.StartNew();

			// TODO: Instance groups?
			int pid = 0;
			string name = string.Empty;
			string path = string.Empty;
			System.Management.ManagementBaseObject targetInstance;
			try
			{
				targetInstance = (System.Management.ManagementBaseObject)e.NewEvent.Properties["TargetInstance"].Value;
				pid = Convert.ToInt32(targetInstance.Properties["Handle"].Value);
				//targetInstance.Properties.Cast<System.Management.PropertyData>().ToList().ForEach(p => Console.WriteLine("{0}={1}", p.Name, p.Value));
				//ExecutablePath=fullpath
				path = (string)(targetInstance.Properties["ExecutablePath"].Value);
				name = System.IO.Path.GetFileNameWithoutExtension(path);
				if (name == string.Empty) // is this even possible?
					Log.Error("Pathless Pid({0}): {1} – is this even possible?", pid, (string)(targetInstance.Properties["ExecutablePath"].Value));
			}
			catch (Exception ex)
			{
				Log.Warning("{ExceptionSource} :: {ExceptionMessage}", ex.Source, ex.Message);
				Log.Warning(ex.StackTrace);
				Log.Warning("Failed to extract process ID from WMI event.");
				throw;
			}
			finally
			{
				n.Stop();
				Statistics.WMIquerytime += n.Elapsed.TotalSeconds;
				Statistics.WMIqueries += 1;
			}

			if (string.IsNullOrEmpty(name) && pid <= LowestInvalidPid)
			{
				Log.Warning("Failed to acquire neither process name nor process Id");
				return;
			}

			onInstanceHandling?.Invoke(this, new InstanceEventArgs { Count = 1 });

			//Handle=pid
			// FIXME
			// DEBUG: if (TaskMaster.VeryVerbose) Console.WriteLine(string.Format("Handling: +{0} = {1} --- NewInstanceTriage", 1, Handling));

			BasicProcessInfo info;
			Process process = null;
			try
			{
				process = Process.GetProcessById(pid);
				if (process == null)
				{
					onInstanceHandling?.Invoke(this, new InstanceEventArgs { Count = -1 });
					//throw new ProcessNotFoundException { Name = name, Id = pid };
					Log.Verbose("Caught #{Pid} but it vanished.", pid);
					return;
				}

				if (string.IsNullOrEmpty(name))
				{
					// This happens only when encountering a process with elevated privileges, e.g. admin
					// TODO: Mark as admin process
					name = process.ProcessName;
				}
			}
			catch
			{
				Log.Warning("Failed to retrieve information for '{Name}' (#{Pid})", name, pid);
				onInstanceHandling?.Invoke(this, new InstanceEventArgs { Count = -1 });
				return;
			}

			Log.Verbose("Caught: {ProcessName} (#{ProcessID}) at: {Path}", name, pid, path);

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

			info = new BasicProcessInfo { Name = name, Id = pid, Process = process, Path = path, Start = start };

			if (BatchProcessing)
			{
				lock (processListLock)
				{
					processList.Add(info);

					// Delay process timer a few times.
					if (processListTimer.Enabled)
					{
						processListLockRestart += 1;
						if (processListLockRestart < BatchProcessingThreshold)
							processListTimer.Stop();
					}
					processListTimer.Start();
					// DEBUG: Log.Debug("New instance timer [re]started.");
				}
			}
			else
			{
				try
				{
					await CheckProcess(info);
				}
				catch (Exception ex)
				{
					Log.Warning("Uncaught exception while handling new instance");
					Log.Fatal(ex.StackTrace);
					Console.WriteLine(ex.StackTrace);
					onInstanceHandling?.Invoke(this, new InstanceEventArgs { Count = -1 });
					throw;
				}
				finally
				{
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

		void RescanOnTimer(object sender, EventArgs e)
		{
			Log.Verbose("Rescanning...");

			int nextscan = int.MinValue;
			int tnext = 0;
			int rescanrequests = 0;

			foreach (ProcessController pc in execontrol.Values)
			{
				if (pc.Rescan > 0)
				{
					tnext = pc.TryScan();
					rescanrequests++;
					if (tnext > nextscan)
						nextscan = tnext;
				}
			}

			if (rescanrequests == 0)
			{
				Log.Verbose("No apps have requests to rescan, stopping rescanning.");
				rescanTimer.Stop();
			}
			else
			{
				rescanTimer.Interval = nextscan * (1000 * 60);
				Log.Debug("Rescan set to occur after {Scan} minutes.", nextscan);
			}
		}

		System.Management.ManagementEventWatcher watcher;

		const string watchfile = "Watchlist.ini";
		const string statfile = "Watchlist.Statistics.ini";
		// ctor, constructor
		public ProcessManager()
		{
			Log.Verbose("Starting...");

			Log.Information("Processor count: {ProcessorCount}", CPUCount);

			allCPUsMask = 1;
			for (int i = 0; i < CPUCount - 1; i++)
				allCPUsMask = (allCPUsMask << 1) | 1;

			Log.Information("Full CPU mask: {ProcessorBitMask} ({ProcessorMask}) (OS control)",
							Convert.ToString(allCPUsMask, 2), allCPUsMask);

			loadWatchlist();

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
					"SELECT * FROM __InstanceCreationEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_Process'");
				watcher = new System.Management.ManagementEventWatcher(scope, query);
			}
			catch (System.Runtime.InteropServices.COMException e)
			{
				Log.Error("Failed to initialize WMI event watcher [COM error]: " + e.Message);
				throw new InitFailure("WMI event watcher initialization failure");
			}
			catch (System.Management.ManagementException e)
			{
				Log.Error("Failed to initialize WMI event watcher [Unidentified error]: " + e.Message);
				throw new InitFailure("WMI event watcher initialization failure");
			}

			if (watcher != null)
			{
				watcher.EventArrived += NewInstanceTriage;

				if (BatchProcessing)
				{
					processListTimer.Interval += BatchDelay; // 2.5s delay
					processListTimer.Elapsed += ProcessListTimerTick;
				}

				/*
				// Only useful for debugging the watcher, but there doesn't seem to be any unwanted stops happening.
				watcher.Stopped += (object sender, System.Management.StoppedEventArgs e) =>
				{
					Log.Warn("New instance watcher stopped.");
				};
				*/
				try
				{
					watcher.Start();
					Log.Debug("New instance watcher initialized.");
				}
				catch
				{
					Log.Fatal("New instance watcher failed to initialize.");
					throw new InitFailure("New instance watched failed to initialize");
				}
			}
			else
			{
				Log.Error("Failed to initialize new instance watcher.");
				throw new InitFailure("New instance watcher not initialized");
			}

			rescanTimer.Elapsed += RescanOnTimer;

			if (execontrol.Count > 0)
			{
				Log.Verbose("Starting rescan timer.");
				rescanTimer.Interval = RescanDelay;
				rescanTimer.Start();
			}

			onInstanceHandling += UpdateHandling;

			if (RescanEverythingFrequency > 0)
			{
				RescanEverythingTimer = new System.Timers.Timer();
				RescanEverythingTimer.Interval = RescanEverythingFrequency;
				RescanEverythingTimer.Elapsed += ProcessEverythingRequest;
				RescanEverythingTimer.Start();
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
				Log.Verbose("Disposing process manager...");

				if (RescanEverythingTimer != null)
					RescanEverythingTimer.Stop();
				//watcher.EventArrived -= NewInstanceTriage;
				watcher.Stop(); // shouldn't be necessary
				watcher.Dispose();
				//watcher = null;
				rescanTimer.Stop();
				processListTimer.Stop();
				pathCacheTimer.Stop();

				saveStats();

				if (execontrol != null)
				{
					execontrol.Clear();
					execontrol = null;
				}
				if (processList != null)
				{
					processList.Clear();
					processList = null;
				}
				if (watchlist != null)
				{
					watchlist.Clear();
					watchlist = null;
				}
			}

			disposed = true;
		}

		void saveStats()
		{
			Log.Verbose("Saving stats...");
			if (stats == null)
				stats = TaskMaster.loadConfig(statfile);

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
	}
}
