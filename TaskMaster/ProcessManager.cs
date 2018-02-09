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

namespace TaskMaster
{
	public class InstanceEventArgs : EventArgs
	{
		public int Count { get; set; } = 0;
		public int Total { get; set; } = 0;
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

			Log.Verbose("Scanning {ProcessCount} processes for changes.", procs.Length);

			// DEBUG: if (TaskMaster.VeryVerbose) Console.WriteLine(string.Format("Handling: +{0} = {1} --- ProcessEverything", procs.Length, Handling));
			onInstanceHandling?.Invoke(this, new InstanceEventArgs { Count = procs.Length });

			try
			{
				int i = 0;
				foreach (Process process in procs)
				{
					Log.Verbose("Checking [{ProcessIterator}/{ProcessCount}] '{ProcessName}'", ++i, procs.Length, process.ProcessName);
					await CheckProcess(process, schedule_next: false);
				}
			}
			catch (Exception e)
			{
				Console.WriteLine(e.StackTrace);
				Log.Warning("Uncaught exception while processing everything.");
				throw;
			}
			finally
			{
				// DEBUG: if (TaskMaster.VeryVerbose) Console.WriteLine(string.Format("Handling: -{0} = {1} --- ProcessEverything", procs.Length, Handling));
				onInstanceHandling?.Invoke(this, new InstanceEventArgs { Count = -procs.Length, Total = Handling });
			}

			if (TaskMaster.PathMonitorEnabled)
				UpdatePathWatch();

			Log.Verbose("Done processing everything.");
		}

		public static int PowerdownDelay { get; set; } = 7000;

		static int BatchDelay = 2500;
		static int RescanDelay = 1000 * 60 * 5; // 5 minutes
		static bool BatchProcessing; // = false;
		static int BatchProcessingThreshold = 5;
		static bool ControlChildren; // = false;
		SharpConfig.Configuration stats;
		public void loadWatchlist()
		{
			Log.Verbose("Loading watchlist");
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

			var coreperf = TaskMaster.cfg["Performance"];

			bool dirtyconfig = false, tdirty = false;
			bool disableChildControl = !coreperf.GetSetDefault("Child processes", false, out tdirty).BoolValue;
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

			var powersec = TaskMaster.cfg["Power"];
			PowerdownDelay = powersec.GetSetDefault("Powerdown delay", 7, out tdirty).IntValue * 1000;
			powersec["Powerdown delay"].Comment = "Delay in seconds to restore old power mode after elevated power mode is no longer needed.";
			dirtyconfig |= tdirty;

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
					BackgroundIO = (section.TryGet("Background I/O")?.BoolValue ?? false),
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

				if (disableChildControl)
					cnt.Children = false;
				else
					ControlChildren |= cnt.Children;

				Log.Verbose("{FriendlyName} ({MatchName}), {TargetPriority}, Mask:{Affinity}, Rescan: {RescanDelay} minutes",
							cnt.FriendlyName, (cnt.Executable == null ? cnt.Path : cnt.Executable), cnt.Priority, cnt.Affinity, cnt.Rescan, cnt.Children, cnt.ChildPriority);

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
							Log.Verbose("'{FriendlyName}' ({Subpath}) waiting to be located.", cnt.FriendlyName, cnt.Subpath);
						}
						else
						{
							Log.Warning("'{FriendlyName}' malconfigured. Insufficient or wrong information.", cnt.FriendlyName);
						}
					}
				}
				else
				{
					lock (watchlist_lock)
						watchlist.Add(cnt);
					execontrol.Add(LowerCase(cnt.ExecutableFriendlyName), cnt);
					Log.Verbose("'{ExecutableName}' added to monitoring.", cnt.FriendlyName);
				}
			}

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

			ControlChildren &= !disableChildControl;

			Log.Information("Child process monitoring: {ChildControl}", (ControlChildren ? "Enabled" : "Disabled"));
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
		string GetProcessPath(int processId)
		{
			if (!TaskMaster.WMIQueries) return null;

			Stopwatch n = Stopwatch.StartNew();

			string path = null;
			string wmiQueryString = "SELECT ExecutablePath FROM Win32_Process WHERE ProcessId = " + processId;
			using (var searcher = new System.Management.ManagementObjectSearcher(wmiQueryString))
			{
				using (var results = searcher.Get())
				{
					var mo = results.Cast<System.Management.ManagementObject>().FirstOrDefault();
					if (mo != null)
					{
						path = (string)mo["ExecutablePath"];

						if (string.IsNullOrEmpty(path)) Log.Verbose(string.Format("WMI fetch (#{0}): {1}", processId, path));

						return path;
					}
				}
			}

			n.Stop();
			Statistics.WMIquerytime += n.Elapsed.TotalSeconds;

			return path;
		}

		ProcessState CheckPathWatch(Process process)
		{
			Debug.Assert(process != null);

			string name = process.ProcessName;
			int pid = process.Id;

			try
			{
				if (process.HasExited) // can throw
				{
					Log.Verbose("{ProcessName} (#{ProcessID}) has already exited.", name, pid);
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
				if (ex.NativeErrorCode != 5)
					Log.Warning("Access error: {ProcessName} (#{ProcessID})", name, pid);
				return ProcessState.AccessDenied; // we don't care wwhat this error is
			}

			bool slow = false;
			string path = null;
			try
			{
				path = process.MainModule?.FileName; // this will cause win32exception of various types, we don't Really care which error it is
			}
			catch (NotSupportedException)
			{
				Log.Fatal("[Unexpected] Not supported operation: {ProcessName} (#{ProcessID})", name, pid);
				return ProcessState.AccessDenied;
			}
			catch (Win32Exception)
			{
			}

			if (string.IsNullOrEmpty(path))
			{
				slow = true;
				path = GetProcessPath(pid);
				if (string.IsNullOrEmpty(path))
				{
					Log.Debug("Failed to access path of '{Process}' (#{ProcessID})", name, pid);
					return ProcessState.AccessDenied;
				}
			}

			// TODO: This needs to be FASTER
			lock (pathwatchlock)
			{
				foreach (ProcessController pc in watchlist)
				{
					if (pc.Path == null) continue;

					//Log.Debug("with: "+ pc.Path);
					if (path.StartsWith(pc.Path, StringComparison.InvariantCultureIgnoreCase)) // TODO: make this compatible with OSes that aren't case insensitive?
					{
						Log.Verbose("[{PathFriendlyName}] matched {Speed}at: {Path}", // TODO: de-ugly
									pc.FriendlyName, (slow ? "~slowly~ " : ""), path);

						return pc.Touch(process);
					}
				}
			}

			return ProcessState.Invalid;
		}

		public static string[] ProtectList { get; private set; } = { "consent", "winlogon", "wininit", "csrss", "dwm" };
		public static string[] IgnoreList { get; private set; } = { "dllhost", "svchost", "taskeng", "consent", "taskhost", "rundll32", "conhost", "dwm", "wininit", "csrss", "winlogon", "services", "explorer" };

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

		void ChildController(BasicProcessInfo childinfo)
		{
			//await System.Threading.Tasks.Task.Yield();

			// TODO: Cache known children so we don't look up the parent? Reliable mostly with very unique/long executable names.
			Stopwatch n = Stopwatch.StartNew();
			int ppid = -1;
			try
			{
				// TODO: Deal with intermediary processes (double parent)
				if (childinfo.Process == null) childinfo.Process = Process.GetProcessById(childinfo.Id);
				ppid = childinfo.Process.ParentProcessId();
			}
			catch // PID not found
			{
				Log.Warning("Couldn't get parent process for {ChildProcessName} (#{ChildProcessID})", childinfo.Name, childinfo.Id);
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
					if (execontrol.TryGetValue(LowerCase(childinfo.Process.ProcessName), out parent))
					{
						if (!parent.ChildPriorityReduction && (ProcessHelpers.PriorityToInt(childinfo.Process.PriorityClass) > ProcessHelpers.PriorityToInt(parent.ChildPriority)))
						{
							Log.Verbose(childinfo.Name + " (#" + childinfo.Id + ") has parent " + parent.FriendlyName + " (#" + parentproc.Id + ") has non-reductable higher than target priority.");
						}
						else if (parent.Children
								 && ProcessHelpers.PriorityToInt(childinfo.Process.PriorityClass) != ProcessHelpers.PriorityToInt(parent.ChildPriority))
						{
							ProcessPriorityClass oldprio = childinfo.Process.PriorityClass;
							try
							{
								childinfo.Process.SetLimitedPriority(parent.ChildPriority, true, false);
							}
							catch (Exception e)
							{
								Console.WriteLine(e.StackTrace);
								Log.Warning("Uncaught exception; Failed to modify priority for '{ProcessName}'", childinfo.Process.ProcessName);
							}
							Log.Information("{ChildProcessName} (#{ChildProcessID}) child of {ParentFriendlyName} (#{ParentProcessID}) Priority({OldChildPriority} -> {NewChildPriority})",
											childinfo.Name, childinfo.Id, parent.FriendlyName, ppid, oldprio, childinfo.Process.PriorityClass);
						}
						else
						{
							Log.Verbose(childinfo.Name + " (#" + childinfo.Id + ") has parent " + parent.FriendlyName + " (#" + parentproc.Id + ")");
						}
					}
				}
			}
			n.Stop();
			Statistics.Parentseektime += n.Elapsed.TotalSeconds;
			Statistics.ParentSeeks += 1;
		}

		async Task CheckProcess(BasicProcessInfo info)
		{
			if (info.Process == null)
			{
				if (info.Id > LowestInvalidPid)
				{
					try
					{
						info.Process = Process.GetProcessById(info.Id);
					}
					catch
					{
						// Ignore
						return;
					}
				}
				else if (!string.IsNullOrEmpty(info.Name))
				{
					try
					{
						Process[] procs = Process.GetProcessesByName(info.Name);
						if (procs.Length > 0)
						{
							info.Process = procs[0];
						}
						else
						{
							return;
						}
					}
					catch
					{
						// NOP
						return;
					}
				}
				else
				{
					Log.Error("Received incomplete process information."); // this should never happen
				}
			}

			await CheckProcess(info.Process);
		}

		async System.Threading.Tasks.Task CheckProcess(Process process, bool schedule_next = true)
		{
			Debug.Assert(process != null);

			await Task.Yield();

			//if (TaskMaster.VeryVerbose) Log.Debug("Processing: " + process.ProcessName);

			string name;
			int pid;

			try
			{
				name = process.ProcessName;
				pid = process.Id;

				if (IgnoreProcessID(pid) || IgnoreProcessName(name))
				{
					Log.Verbose("Ignoring process: {ProcessName} (#{ProcessID})", name, pid);
					return;
				}

				if (string.IsNullOrEmpty(name))
				{
					Log.Warning("#{AppId} details unaccessible, ignored.", pid);
					return;
				}
			}
			catch
			{
				// NOP
				return;
			}

			ProcessState state = ProcessState.Invalid;

			// TODO: check proc.processName for presence in images.
			ProcessController control;
			if (execontrol.TryGetValue(LowerCase(name), out control))
			{
				//await System.Threading.Tasks.Task.Delay(ProcessModifyDelay).ConfigureAwait(false);

				state = control.Touch(process, schedule_next);
				if (state != ProcessState.Invalid)
				{
					Log.Verbose("Control group: {ProcessFriendlyName}, process: {ProcessName} (#{ProcessID})",
									control.FriendlyName, name, pid);
				}
				return; // execontrol had this, we don't care about anything else for this.
			}
			else
				Log.Verbose("{AppName} not in control list.", name);

			if (WatchlistWithPath > 0)
			{
				Log.Verbose("Checking paths for '{ProcessName}' (#{ProcessID})", name, pid);
				state = CheckPathWatch(process);
			}

			if (state != ProcessState.Invalid) return; // we don't care to process more

			if (ControlChildren) // this slows things down a lot it seems
			{
				//await System.Threading.Tasks.Task.Yield();

				// TODO: Cache known children so we don't look up the parent? Reliable mostly with very unique/long executable names.
				Stopwatch n = Stopwatch.StartNew();
				int ppid = -1;
				try
				{
					// TODO: Deal with intermediary processes (double parent)
					ppid = process.ParentProcessId();
				}
				catch
				{
					Log.Warning("Couldn't get parent process for {ProcessName} (#{ProcessID})",
								name, pid);
					return;
				}

				if (!IgnoreProcessID(ppid)) // 0 and 4 are system processes, we don't care about their children
				{
					Process parent = null;
					try
					{
						parent = Process.GetProcessById(ppid);
					}
					catch
					{
						// PId not found and other problems.
						Log.Verbose("Parent PID(#{ProcessID}) not found", ppid);
						return;
					}

					if (IgnoreProcessName(parent.ProcessName))
					{
						// nothing, ignoring
					}
					else
					{
						bool denyChange = ProtectedProcessName(parent.ProcessName);

						ProcessController parentcontrol = null;
						if (!denyChange && execontrol.TryGetValue(LowerCase(parent.ProcessName), out parentcontrol))
						{

							if (!parentcontrol.ChildPriorityReduction && (ProcessHelpers.PriorityToInt(process.PriorityClass) > ProcessHelpers.PriorityToInt(parentcontrol.ChildPriority)))
							{
								Log.Verbose(name + " (#" + pid + ") has parent " + name + " (#" + pid + ") has non-reductable higher than target priority.");
							}
							else if (parentcontrol.Children
								&& ProcessHelpers.PriorityToInt(process.PriorityClass) != ProcessHelpers.PriorityToInt(parentcontrol.ChildPriority))
							{
								ProcessPriorityClass oldprio = process.PriorityClass;
								process.SetLimitedPriority(parentcontrol.ChildPriority, true, false);
								// TODO: Is this ever reached?
								Log.Information("{ProcessName} (#{ProcessID}) child of {ParentFriendlyName} (#{ParentProcessID}) Priority({OldPriority} → {NewPriority})",
												process.ProcessName, process.Id, parentcontrol.FriendlyName, ppid, oldprio, process.PriorityClass);
								parentcontrol.Touch(process);
							}
						}
						else
						{
							Log.Verbose("{ProcessName} (#{ProcessID}) has parent {ParentName} (#{ParentProcessID})",
																   name, pid, parent.ProcessName, parent.Id);
						}
					}
				}
				n.Stop();
				Statistics.Parentseektime += n.Elapsed.TotalSeconds;
				Statistics.ParentSeeks += 1;
			}
		}

		struct BasicProcessInfo
		{
			public string Name;
			public int Id;
			public Process Process;
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
					await NewInstanceHandler(info);
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

		async Task NewInstanceHandler(BasicProcessInfo info)
		{
			Debug.Assert(!string.IsNullOrEmpty(info.Name) || info.Id > -1, "Empty info for NewInstanceHandler (#" + info.Id + ")");

			Stopwatch n = Stopwatch.StartNew();
			if (string.IsNullOrEmpty(info.Name))
			{
				try
				{
					// since targetinstance actually has fuckall information, we need to extract it...
					info.Process = Process.GetProcessById(info.Id); // FIXME: Has abnormally long execution times since 4.7 update.
					info.Name = info.Process.ProcessName;
				}
				catch // PID not found
				{
#if DEBUG
					Log.Verbose("Process exited before we had time to identify it."); // technically an error [Warn], but not that interesting for anyone
#endif
					return;
				}
				finally
				{
					n.Stop();
					//TODO: Track GetProcessById queries
					//Statistics.WMIquerytime += n.Elapsed.TotalSeconds;
					//Statistics.WMIqueries += 1;
				}
			}

			Log.Verbose("Caught: {ProcessName} (#{ProcessID})", info.Name, info.Id);

			await CheckProcess(info);

			n.Stop();
			// TODO: Track new instance handling
			//Statistics.WMIquerytime += n.Elapsed.TotalSeconds;
			//Statistics.WMIqueries += 1;
		}

		public static int Handling { get; set; }

		public static event EventHandler<InstanceEventArgs> onInstanceHandling;

		async void NewInstanceTriage2(object sender, EventArrivedEventArgs ev)
		{
			await Task.Yield();

			foreach (PropertyData pd in ev.NewEvent.Properties)
			{
				Console.WriteLine("\n============================= =========");
				Console.WriteLine("{0},{1},{2}", pd.Name, pd.Type, pd.Value);
			}
		}

		async void NewInstanceTriage(object sender, System.Management.EventArrivedEventArgs e)
		{
			await Task.Yield();

			Stopwatch n = Stopwatch.StartNew();

			// TODO: Instance groups?
			int pid = -1;
			string exename = string.Empty;
			System.Management.ManagementBaseObject targetInstance;
			try
			{
				targetInstance = (System.Management.ManagementBaseObject)e.NewEvent.Properties["TargetInstance"].Value;
				pid = Convert.ToInt32(targetInstance.Properties["Handle"].Value);
				//targetInstance.Properties.Cast<System.Management.PropertyData>().ToList().ForEach(p => Console.WriteLine("{0}={1}", p.Name, p.Value));
				//ExecutablePath=fullpath
				exename = System.IO.Path.GetFileNameWithoutExtension((string)(targetInstance.Properties["ExecutablePath"].Value));
				if (exename == string.Empty) // is this even possible?
					Log.Fatal("Pathless Pid({0}): {1} – is this even possible?", pid, (string)(targetInstance.Properties["ExecutablePath"].Value));
			}
			catch (Exception ex)
			{
				Log.Warning("{ExceptionSource} :: {ExceptionMessage}", ex.Source, ex.Message);
				Log.Warning("{Exception}");
				Log.Warning("Failed to extract process ID from WMI event.");
				throw;
			}
			finally
			{
				n.Stop();
				Statistics.WMIquerytime += n.Elapsed.TotalSeconds;
				Statistics.WMIqueries += 1;
			}

			if (string.IsNullOrEmpty(exename) && pid == -1)
			{
				Log.Warning("Failed to acquire neither process name nor process Id");
				return;
			}

			//Handle=pid
			// FIXME
			// DEBUG: if (TaskMaster.VeryVerbose) Console.WriteLine(string.Format("Handling: +{0} = {1} --- NewInstanceTriage", 1, Handling));
			onInstanceHandling?.Invoke(this, new InstanceEventArgs { Count = 1 });

			BasicProcessInfo info = new BasicProcessInfo { Name = exename, Id = pid, Process = null };

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
					await NewInstanceHandler(info);
				}
				catch (Exception ex)
				{
					Log.Warning("Uncaught exception while handling new instance");
					Log.Fatal(ex.StackTrace);
					Console.WriteLine(ex.StackTrace);
					throw;
				}
				finally
				{
					// DEBUG: if (TaskMaster.VeryVerbose) Console.WriteLine(string.Format("Handling: -{0} = {1} --- NewInstanceTriage", 1, Handling));
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

		void RescanOnTimer(object sender, EventArgs e)
		{
			Log.Verbose("Rescanning...");

			int rescanrequests = 0;
			foreach (ProcessController pc in execontrol.Values)
			{
				if (pc.Rescan > 0)
				{
					pc.TryScan();
					rescanrequests++;
				}
			}

			// TODO: Add Path rescanning

			if (rescanrequests == 0)
			{
				Log.Verbose("No apps have requests to rescan, stopping rescanning.");
				rescanTimer.Stop();
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

				var scope = new System.Management.ManagementScope(new System.Management.ManagementPath(@"\\.\root\CIMV2")); // @"\\.\root\CIMV2"

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
				var query = new System.Management.EventQuery("SELECT * FROM __InstanceCreationEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_Process'");
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
				watcher.Stop(); // shouldn't be necessary
				watcher.Dispose();
				rescanTimer.Stop();
				processListTimer.Stop();

				saveStats();
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
	}
}
