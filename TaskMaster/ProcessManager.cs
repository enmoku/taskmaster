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
using System.Runtime.Remoting.Channels;
using System.Windows;

namespace TaskMaster
{
	using System;
	using System.Collections.Generic;
	using System.ComponentModel;
	using System.Diagnostics;
	using System.Linq;
	using Serilog;

	/// <summary>
	/// Process control.
	/// </summary>
	sealed public class ProcessController : AbstractProcessControl
	{
		/// <summary>
		/// Priority boost for foreground applications.
		/// </summary>
		public bool Boost = true;

		public bool Children = true;
		public ProcessPriorityClass ChildPriority = ProcessPriorityClass.Normal;
		public bool ChildPriorityReduction = false;

		public bool AllowPaging = false;

		int p_rescan;
		/// <summary>
		/// Delay in minutes before we try to use Scan again.
		/// </summary>
		public int Rescan
		{
			get { return p_rescan; }
			set { p_rescan = value >= 0 ? value : 0; }
		}

		// TODO EVENT(??)
		/// <summary>
		/// Touch the specified process and child.
		/// </summary>
		/// <param name="process">Process.</param>
		public ProcessState Touch(Process process, bool schedule_next = true)
		{
			Debug.Assert(process != null);
			int pid = process.Id;
			string name = process.ProcessName;

			try
			{
				if (process.HasExited)
				{
					if (TaskMaster.VerbosityThreshold > 3)
						Log.Verbose("{ProcessName} (#{ProcessID}) has already exited.", Executable, pid);
					return ProcessState.Invalid;
				}
			}
			catch (Win32Exception ex)
			{
				if (ex.NativeErrorCode != 5)
					Log.Warning("Access error: {ProcessName} (#{ProcessID})", Executable, pid);
				return ProcessState.AccessDenied; // we don't care what this error is exactly
			}

			if (TaskMaster.VerbosityThreshold > 3)
				Log.Verbose("Touching: {ProcessName} ({ExecutableName}, #{ProcessID})",
							FriendlyName, Executable, pid);

			ProcessState rv = ProcessState.Invalid;

			bool mAffinity, mPriority, mBoost, modified = false;
			lock (process)
			{
				mBoost = mPriority = mAffinity = false;
				IntPtr oldAffinity = process.ProcessorAffinity;
				ProcessPriorityClass oldPriority = process.PriorityClass;
				LastSeen = DateTime.Now;

				if (process.SetLimitedPriority(Priority, Increase, true))
					modified = mPriority = true;

				if (process.ProcessorAffinity.ToInt32() != Affinity.ToInt32())
				{
					//CLEANUP: System.Console.WriteLine("Current affinity: {0}", Convert.ToString(item.ProcessorAffinity.ToInt32(), 2));
					//CLEANUP: System.Console.WriteLine("Target affinity: {0}", Convert.ToString(proc.Affinity.ToInt32(), 2));
					try
					{
						process.ProcessorAffinity = Affinity;
						modified = mAffinity = true;
						Log.Verbose("Affinity for '{ExecutableName}' (#{ProcessID}) set: {OldAffinity} → {NewAffinity}.",
									Executable, pid, process.ProcessorAffinity.ToInt32(), Affinity.ToInt32());
					}
					catch (Win32Exception)
					{
						Log.Warning("Couldn't modify process ({ExecutableName}, #{ProcessID}) affinity [{OldAffinity} → {NewAffinity}].",
									Executable, pid, process.ProcessorAffinity.ToInt32(), Affinity.ToInt32());
					}
				}
				else
				{
					Log.Verbose("Affinity for '{ExecutableName}' (#{ProcessID}) is ALREADY set: {OldAffinity} → {NewAffinity}.",
									Executable, pid, process.ProcessorAffinity.ToInt32(), Affinity.ToInt32());
				}

				if (process.PriorityBoostEnabled != Boost)
				{
					process.PriorityBoostEnabled = Boost;
					modified = mBoost = true;
				}

				setPowerPlan(process);

				if (modified)
				{
					Adjusts += 1;

					LastTouch = DateTime.Now;
					rv = ProcessState.Modified;

					if (mPriority)
						Log.Information("[{PathName}] {ExecutableName} (#{ProcessID}); Priority: {OldPriority} → {NewPriority}",
										FriendlyName, ExecutableFriendlyName, pid, oldPriority.ToString(), Priority.ToString());
					if (mAffinity)
						Log.Information("[{PathName}] {ExecutableName} (#{ProcessID}); Affinity: {OldAffinity} → {NewAffinity}",
										FriendlyName, ExecutableFriendlyName, pid, oldAffinity, Affinity);
					if (mBoost)
						Log.Information("[{PathName}] {ExecutableName} (#{ProcessID}); Boost: {ProcessBoost}",
										FriendlyName, ExecutableFriendlyName, pid, Boost);

					onTouch?.Invoke(this, new ProcessEventArgs { Control = this, Process = process });
				}
				else
				{
					Log.Verbose("'{ProcessName}' (#{ProcessID}) seems to be OK already.",
								Executable, pid);

					rv = ProcessState.OK;
				}
			}

			if (schedule_next) RescanWithSchedule();

			return rv;
		}

		public void TryScan()
		{
			RescanWithSchedule().Wait();
		}

		int ScheduledScan; // = 0;
		async System.Threading.Tasks.Task RescanWithSchedule()
		{
			double n = (DateTime.Now - LastScan).TotalMinutes;
			//Log.Trace(string.Format("[{0}] last scan {1:N1} minute(s) ago.", FriendlyName, n));
			if (Rescan > 0 && n >= Rescan)
			{
				if (System.Threading.Interlocked.CompareExchange(ref ScheduledScan, 1, 0) == 1)
					return;

				await Scan();

				ScheduledScan = 0;
			}
		}

		DateTime LastScan = DateTime.MinValue;
		public async System.Threading.Tasks.Task Scan()
		{
			await System.Threading.Tasks.Task.Delay(100).ConfigureAwait(false);

			Process[] procs;
			try
			{
				procs = Process.GetProcessesByName(ExecutableFriendlyName); // should be case insensitive by default
			}
			catch // name not found
			{
				Log.Verbose("{ProcessFriendlyName} is not running", ExecutableFriendlyName);
				return;
			}
			if (procs.Length == 0) return;

			//LastSeen = LastScan;
			LastScan = DateTime.Now;

			Log.Verbose("Scanning '{ProcessFriendlyName}' (found {ProcessInstances} instance(s))", FriendlyName, procs.Length);

			int tc = 0;
			foreach (Process process in procs)
			{
				if (Touch(process) != ProcessState.Invalid)
					tc++;
			}

			if (tc > 0) Log.Verbose("Scan for '{ProcessFriendlyName}' modified {ModifiedInstances} out of {ProcessInstances} instance(s)", FriendlyName, tc, procs.Length);
		}

		public static event EventHandler<ProcessEventArgs> onTouch;
	}

	sealed public class PathControl : AbstractProcessControl
	{
		public string Subpath;
		public string Path;

		public PathControl(string name, string executable, ProcessPriorityClass priority, int affinity, string subpath, string path = null)
		{
			FriendlyName = name;
			//Executable = executable;
			var friendlyName = System.IO.Path.GetFileNameWithoutExtension(executable);
			Priority = priority;
			if (affinity != ProcessManager.allCPUsMask)
				Affinity = new IntPtr(affinity);
			Subpath = subpath;
			Path = path;
			if (path != null)
				Log.Information("'{ProcessName}' watched in: {Path} [Priority: {Priority}, Mask: {Mask}]",
								FriendlyName, Path, Priority, Affinity.ToInt32());
			else
				Log.Information("'{ProcessName}' matching for '{Subpath}' [Priority: {Priority}, Mask: {Mask}]",
								executable, Subpath, Priority, Affinity.ToInt32());
		}

		public ProcessState Touch(Process process)
		{
			Debug.Assert(process != null);

			try
			{
				if (process.HasExited)
				{
					Log.Verbose("{ProcessName} (#{ProcessID}) has already exited.", process.ProcessName, process.Id);
					return ProcessState.Invalid;
				}
			}
			catch (Win32Exception ex)
			{
				if (ex.NativeErrorCode != 5) // 5 was what?
					Log.Warning("Access error: {ProcessName} (#{ProcessID})", process.ProcessName, process.Id);
				return ProcessState.AccessDenied; // we don't care wwhat this error is
			}

			int pid = process.Id;
			string executable = process.ProcessName;
			bool modified = false;
			bool mPriority = false;
			bool mAffinity = false;
			bool mPower = false;
			int OldAffinity = 0;
			ProcessPriorityClass OldPriority = ProcessPriorityClass.RealTime;
			LastSeen = DateTime.Now;
			try
			{
				OldAffinity = process.ProcessorAffinity.ToInt32();
				OldPriority = process.PriorityClass;

				// TODO: ProcessControlAbstract.TouchApply()
				if (process.SetLimitedPriority(Priority, Increase, Decrease))
				{
					Adjusts += 1;
					mPriority = modified = true;
				}

				if (OldAffinity != Affinity.ToInt32())
				{
					try
					{
						process.ProcessorAffinity = Affinity;
						modified = mAffinity = true;
						mAffinity = modified = true;
					}
					catch (Win32Exception)
					{
						Log.Warning("Couldn't modify process ({ExecutableName}, #{ProcessID}) affinity [{OldAffinity} → {NewAffinity}].",
									executable, pid, OldAffinity, process.ProcessorAffinity.ToInt32());
					}
				}
				else
				{
					Log.Debug("Affinity for '{ExecutableName}' (#{ProcessID}) is ALREADY set: {OldAffinity} → {NewAffinity}.",
								executable, pid, OldAffinity, process.ProcessorAffinity.ToInt32());
				}

				setPowerPlan(process);
			}
			catch
			{
				Log.Warning("Failed to touch '{ProcessName}' (#{ProcessID})", process.ProcessName, process.Id);
				return ProcessState.AccessDenied;
			}

			System.Text.StringBuilder sbs = new System.Text.StringBuilder();
			sbs.Append("[").Append(FriendlyName).Append("] ").Append(process.ProcessName).Append(" (#").Append(process.Id).Append(")");
			if (!modified)
			{
				sbs.Append(" looks OK, not touched.");
			}
			else
			{
				onTouch?.Invoke(this, new PathControlEventArgs());
				if (mPriority)
					sbs.Append("; Priority: ").Append(OldPriority.ToString()).Append(" → ").Append(process.PriorityClass.ToString());
				if (mAffinity)
					sbs.Append("; Affinity: ").Append(OldAffinity).Append(" → ").Append(process.ProcessorAffinity.ToInt32());
			}
			Log.Information(sbs.ToString());
			sbs.Clear();

			return (modified ? ProcessState.Modified : ProcessState.OK);
		}

		public bool Locate()
		{
			if (Path != null && System.IO.Directory.Exists(Path))
				return true;

			Process process;
			try
			{
				process = Process.GetProcessesByName(ExecutableFriendlyName)[0]; // not great thing, but should be good enough.
			}
			catch
			{
				Log.Verbose("{ProcessFriendlyName} not running", ExecutableFriendlyName);
				return false;
			}

			Log.Verbose("Watched item '{PathName}' encountered.", FriendlyName);
			try
			{
				string corepath = System.IO.Path.GetDirectoryName(process.MainModule.FileName);
				string fullpath = System.IO.Path.Combine(corepath, Subpath);
				if (System.IO.Directory.Exists(fullpath))
				{
					Path = fullpath;
					Log.Debug(string.Format("'{0}' bound to: {1}", FriendlyName, Path));

					onLocate?.Invoke(this, new PathControlEventArgs());

					return true;
				}
			}
			catch (Exception ex)
			{
				Log.Warning("Access failure with '{PathName}'", FriendlyName);
				Console.Error.WriteLine(ex);
			}
			return false;
		}

		public static event EventHandler<PathControlEventArgs> onTouch;
		public static event EventHandler<PathControlEventArgs> onLocate;
	}

	public class PathControlEventArgs : EventArgs
	{
	}

	public class ProcessEventArgs : EventArgs
	{
		public ProcessController Control { get; set; }
		public Process Process { get; set; }
	}

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
		public List<ProcessController> images = new List<ProcessController>();
		/// <summary>
		/// Actively watched paths.
		/// </summary>
		public List<PathControl> pathwatch = new List<PathControl>();
		/// <summary>
		/// Paths not yet properly initialized.
		/// </summary>
		public List<PathControl> pathinit;
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
		public static int CPUCount = 1;

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
			foreach (ProcessController ctrl in images)
			{
				if (ctrl.Executable == executable)
					return ctrl;
			}
			Log.Warning("{ExecutableName} was not found!", executable);
			return null;
		}

		void UpdatePathWatch()
		{
			if (pathinit == null) return;

			Log.Verbose("Locating watched paths.");
			lock (pathwatchlock)
			{
				if (pathinit.Count > 0)
				{
					foreach (PathControl path in pathinit.ToArray())
					{
						if (!pathwatch.Contains(path) && path.Locate())
						{
							pathwatch.Add(path);
							pathinit.Remove(path);
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

			Handling += procs.Length;
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
				Handling -= procs.Length;
				// DEBUG: if (TaskMaster.VeryVerbose) Console.WriteLine(string.Format("Handling: -{0} = {1} --- PageEverythingRequest", procs.Length, Handling));
				onInstanceHandling?.Invoke(this, new InstanceEventArgs { Count = -procs.Length });
			}

			Log.Information("Paged total of {PagedMBs:N1} MBs.", saved / 1000000);

			await Task.Delay(10).ConfigureAwait(false);
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

			Handling += procs.Length;
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
				Handling -= procs.Length;
				// DEBUG: if (TaskMaster.VeryVerbose) Console.WriteLine(string.Format("Handling: -{0} = {1} --- ProcessEverything", procs.Length, Handling));
				onInstanceHandling?.Invoke(this, new InstanceEventArgs { Count = procs.Length, Total = Handling });
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
		public void loadConfig()
		{
			Log.Verbose("Loading watchlist");
			SharpConfig.Configuration appcfg = TaskMaster.loadConfig(appfile);
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
					var t4 = exsec.GetSetDefault("Children", true).BoolValue;
					exsec["Children"].Comment = "Allow modifying processes started by this.";
					var t5 = exsec.GetSetDefault("Allow paging", false).BoolValue;
					exsec["Allow paging"].Comment = "Allows this process to be pushed to paging/swap file.";

					var stsec = appcfg["Steam"];
					var s1 = stsec.GetSetDefault("Image", "steam.exe").StringValue;
					var s2 = stsec.GetSetDefault("Priority", 1).IntValue;
					stsec["Priority"].PreComment = "Boost=True # Enabled by default. Windows normally boosts foreground apps, this would disable that behaviour.";
					var s4 = stsec.GetSetDefault("Children", true).BoolValue;
					stsec["Children"].PreComment = "Background=False # Disabled by default. This can lead to severe performance loss.";
					var s5 = stsec.GetSetDefault("Child priority", 3).IntValue;
					stsec["Child priority"].Comment = "Child process priority if it differs from the main app priority.";
					var s6 = stsec.GetSetDefault("Child priority reduction", false).BoolValue;
					stsec["Child priority reduction"].Comment = "Allow or deny reducing child process priority.";
					var s7 = stsec.GetSetDefault("Allow paging", true).BoolValue;

					var sthsec = appcfg["Steam WebHelper"];
					var sh1 = sthsec.GetSetDefault("Image", "steamwebhelper.exe").StringValue;
					var sh2 = sthsec.GetSetDefault("Priority", 1).IntValue;
					var sh4 = sthsec.GetSetDefault("Children", false).BoolValue;
					var sh7 = sthsec.GetSetDefault("Allow paging", true).BoolValue;

					TaskMaster.saveConfig(appcfg);
				}
			}

			var coreperf = TaskMaster.cfg["Performance"];

			bool dirtyconfig = false, tdirty = false, tdirty2 = false;
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
				if (!section.Contains("image"))
				{
					// TODO: Deal with incorrect configuration lacking image
					Log.Warning("'{SectionName}' has no image.", section.Name);
					continue;
				}
				if (!section.Contains("priority") && !section.Contains("affinity"))
				{
					// TODO: Deal with incorrect configuration lacking image
					Log.Warning("'{SectionName}' has no priority or affinity.", section.Name);
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
				var cnt = new ProcessController
				{
					FriendlyName = section.Name,
					Executable = section["Image"].StringValue,
					// friendly name is filled automatically
					Priority = ProcessHelpers.IntToPriority(prio),
					Increase = (section.TryGet("Increase")?.BoolValue ?? false),
					Decrease = (section.TryGet("Decrease")?.BoolValue ?? true),
					Affinity = new IntPtr(aff != 0 ? aff : allCPUsMask),
					Boost = (section.TryGet("Boost")?.BoolValue ?? true),
					Rescan = (section.TryGet("Rescan")?.IntValue ?? 0),
					BackgroundIO = (section.TryGet("Background I/O")?.BoolValue ?? false),
					ForegroundOnly = (section.TryGet("Foreground only")?.BoolValue ?? false),
					PowerPlan = pmode,
					Children = (section.TryGet("Children")?.BoolValue ?? false),
					ChildPriority = ProcessHelpers.IntToPriority(section.TryGet("Child priority")?.IntValue ?? prio),
					ChildPriorityReduction = section.TryGet("Child priority reduction")?.BoolValue ?? false,
					AllowPaging = (section.TryGet("Allow paging")?.BoolValue ?? false),
				};
				dirtyconfig |= tdirty2;
				dirtyconfig |= tdirty;

				if (disableChildControl)
					cnt.Children = false;
				else
					ControlChildren |= cnt.Children;

				Log.Verbose("{FriendlyName} ({ExecutableName}), {TargetPriority}, Mask:{Affinity}, Rescan: {RescanDelay} minutes, Children: {ChildControl} = {ChildPriority}",
					   cnt.FriendlyName, cnt.Executable, cnt.Priority, cnt.Affinity, cnt.Rescan, cnt.Children, cnt.ChildPriority);

				//cnt.delay = section.Contains("delay") ? section["delay"].IntValue : 30; // TODO: Add centralized default delay
				//cnt.delayIncrement = section.Contains("delay increment") ? section["delay increment"].IntValue : 15; // TODO: Add centralized default increment
				if (stats.Contains(cnt.Executable))
				{
					cnt.Adjusts = stats[cnt.Executable].TryGet("Adjusts")?.IntValue ?? 0;

					var ls = stats[cnt.Executable].TryGet("Last seen");
					if (null != ls && !ls.IsEmpty)
					{
						long stamp = ls.GetValue<long>();
						cnt.LastSeen = stamp.Unixstamp();
					}
				}

				images.Add(cnt);
				execontrol.Add(LowerCase(cnt.ExecutableFriendlyName), cnt);
				Log.Verbose("'{ExecutableName}' added to monitoring.", cnt.ExecutableFriendlyName);
			}

			//TaskMaster.cfg["Applications"]["Ignored"].StringValueArray = IgnoreList;
			string[] newIgnoreList = TaskMaster.cfg["Applications"].GetSetDefault("Ignored", IgnoreList, out tdirty)?.StringValueArray;
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

			try
			{
				if (process.HasExited) // can throw
				{
					Log.Verbose("{ProcessName} (#{ProcessID}) has already exited.", process.ProcessName, process.Id);
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
					Log.Warning("Access error: {ProcessName} (#{ProcessID})", process.ProcessName, process.Id);
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
				Log.Fatal("[Unexpected] Not supported operation: {ProcessName} (#{ProcessID})", process.ProcessName, process.Id);
				return ProcessState.AccessDenied;
			}
			catch (Win32Exception)
			{
			}

			if (string.IsNullOrEmpty(path))
			{
				slow = true;
				path = GetProcessPath(process.Id);
				if (string.IsNullOrEmpty(path))
				{
					Log.Debug("Failed to access path of '{Process}' (#{ProcessID})", process.ProcessName, process.Id);
					return ProcessState.AccessDenied;
				}
			}

			// TODO: This needs to be FASTER
			lock (pathwatchlock)
			{
				foreach (PathControl pc in pathwatch)
				{
					//Log.Debug("with: "+ pc.Path);
					if (path.StartsWith(pc.Path, StringComparison.InvariantCultureIgnoreCase)) // TODO: make this compatible with OSes that aren't case insensitive?
					{
						if (TaskMaster.VerbosityThreshold > 3)
							Log.Verbose("[{PathFriendlyName}] matched {Speed}at: {Path}", // TODO: de-ugly
										pc.FriendlyName, (slow ? "~slowly~ " : ""), path);

						return pc.Touch(process);
					}
				}
			}

			return ProcessState.Invalid;
		}

		static string[] IgnoreList = { "dllhost", "svchost", "taskeng", "consent", "taskhost", "rundll32", "conhost", "dwm", "wininit", "csrss", "winlogon", "services", "explorer" };

		const int LowestInvalidPid = 4;
		bool IgnoreProcessID(int pid)
		{
			return (pid <= LowestInvalidPid);
		}

		bool IgnoreProcessName(string name)
		{
			if (TaskMaster.CaseSensitive)
				return IgnoreList.Contains(name);

			return IgnoreList.Contains(name, StringComparer.InvariantCultureIgnoreCase);
		}

		async System.Threading.Tasks.Task CheckProcessByName(BasicProcessInfo info)
		{
			Debug.Assert(!string.IsNullOrEmpty(info.Name) && info.Id > -1, "CheckProcessByName process name and ID are null");

			if ((info.Id > -1 && IgnoreProcessID(info.Id)) || (!string.IsNullOrEmpty(info.Name) && IgnoreProcessName(info.Name)))
				return;

			await System.Threading.Tasks.Task.Delay(100).ConfigureAwait(true);

			Process process = null;
			ProcessState state = ProcessState.Invalid;

			//await System.Threading.Tasks.Task.Delay(ProcessModifyDelay).ConfigureAwait(false);
			try
			{
				process = Process.GetProcessById(info.Id);
				if (string.IsNullOrEmpty(info.Name))
				{
					Log.Debug("CheckProcessByName :: name filled from retrieved process");
					info.Name = process.ProcessName;
				}
			}
			catch // PID not found
			{
#if DEBUG
				Log.Verbose("Process ID #{ProcessID} was not found", info.Id);
#endif
				return;
			}

			if (string.IsNullOrEmpty(info.Name))
			{
				Log.Warning("CheckProcessByName :: received no name to check");
				return;
			}

			// TODO: check proc.processName for presence in images.
			ProcessController control = null;
			if (execontrol.TryGetValue(LowerCase(info.Name), out control))
			{
				try
				{
					ProcessState rv = control.Touch(process);
					if (rv != ProcessState.Invalid)
						Log.Verbose("Control group: {ProcessName}, process: {ExecutableName} (#{ProcessID})",
									control.FriendlyName, process.ProcessName, process.Id);
				}
				catch (Exception e)
				{
					Console.WriteLine(e.StackTrace);
					Log.Warning("Uncaught exception in control.Touch: {ProcessFriendlyName}", info.Name);
					throw;
				}
				return; // execontrol had this, we don't care about anything else for this.
			}
			else
				Log.Verbose("'{Executable}' not in our control list.", info.Name);

			if (pathwatch.Count > 0)
			{
				if (TaskMaster.VerbosityThreshold > 3) Log.Verbose("Checking paths for '{ProcessName}' (#{ProcessID})", process.ProcessName, process.Id);

				try
				{
					if (process == null) process = Process.GetProcessById(info.Id);
					if ((state = CheckPathWatch(process)) != ProcessState.Invalid) return; // we don't care to process more
				}
				catch // PID not found
				{
					Log.Warning("PID not found");
					// nop
				}
			}

			if (ControlChildren && state == ProcessState.Invalid) // this slows things down a lot it seems
			{
				if (info.Process == null) info.Process = process;
				ChildController(info);
			}
		}

		void ChildController(BasicProcessInfo childinfo)
		{
			//await System.Threading.Tasks.Task.Delay(100).ConfigureAwait(false);

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
#if DEBUG
					Log.Verbose("Parent PID(#{ProcessID}) not found", ppid);
#endif
					return;
				}

				if (IgnoreList.Contains(parentproc.ProcessName)) return;

				ProcessController parent = null;
				if (execontrol.TryGetValue(LowerCase(childinfo.Process.ProcessName), out parent))
				{
					if (!parent.ChildPriorityReduction && (ProcessHelpers.PriorityToInt(childinfo.Process.PriorityClass) > ProcessHelpers.PriorityToInt(parent.ChildPriority)))
					{
#if DEBUG
						Log.Debug(childinfo.Name + " (#" + childinfo.Id + ") has parent " + parent.FriendlyName + " (#" + parentproc.Id + ") has non-reductable higher than target priority.");
#endif
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
#if DEBUG
						Log.Debug(childinfo.Name + " (#" + childinfo.Id + ") has parent " + parent.FriendlyName + " (#" + parentproc.Id + ")");
#endif
					}
				}
			}
			n.Stop();
			Statistics.Parentseektime += n.Elapsed.TotalSeconds;
			Statistics.ParentSeeks += 1;
		}

		async Task CheckProcess(BasicProcessInfo info)
		{
			if (info.Process != null)
			{
				await CheckProcess(info.Process);
			}
			else if (info.Id > LowestInvalidPid)
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
				await CheckProcess(info.Process);
			}
			else if (!string.IsNullOrEmpty(info.Name))
			{
				await CheckProcessByName(info);
			}
			else
			{
				Log.Error("Received incomplete process information."); // this should never happen
			}
		}

		async System.Threading.Tasks.Task CheckProcess(Process process, bool schedule_next = true)
		{
			Debug.Assert(process != null);

			//if (TaskMaster.VeryVerbose) Log.Debug("Processing: " + process.ProcessName);

			if (IgnoreProcessID(process.Id) || IgnoreProcessName(process.ProcessName))
			{
				Log.Verbose("Ignoring process: {ProcessName} (#{ProcessID})", process.ProcessName, process.Id);
				return;
			}

			if (string.IsNullOrEmpty(process.ProcessName))
			{
				Log.Warning("#{AppId} details unaccessible, ignored.", process.Id);
				return;
			}

			ProcessState state = ProcessState.Invalid;

			// TODO: check proc.processName for presence in images.
			ProcessController control;
			if (execontrol.TryGetValue(LowerCase(process.ProcessName), out control))
			{
				//await System.Threading.Tasks.Task.Delay(ProcessModifyDelay).ConfigureAwait(false);

				state = control.Touch(process, schedule_next);
				if (state != ProcessState.Invalid)
				{
					Log.Verbose("Control group: {ProcessFriendlyName}, process: {ProcessName} (#{ProcessID})",
									control.FriendlyName, process.ProcessName, process.Id);
				}
				return; // execontrol had this, we don't care about anything else for this.
			}
			else
				Log.Verbose("{AppName} not in control list.", process.ProcessName);

			if (pathwatch.Count > 0)
			{
				Log.Verbose("Checking paths for '{ProcessName}' (#{ProcessID})", process.ProcessName, process.Id);
				state = CheckPathWatch(process);
			}

			if (state != ProcessState.Invalid) return; // we don't care to process more

			if (ControlChildren) // this slows things down a lot it seems
			{
				//await System.Threading.Tasks.Task.Delay(100).ConfigureAwait(false);

				// TODO: Cache known children so we don't look up the parent? Reliable mostly with very unique/long executable names.
				Stopwatch n = Stopwatch.StartNew();
				int ppid = -1;
				try
				{
					// TODO: Deal with intermediary processes (double parent)
					ppid = process.ParentProcessId();
				}
				catch (Win32Exception)
				{
					Log.Warning("Couldn't get parent process for {ProcessName} (#{ProcessID})",
								process.ProcessName, process.Id);
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

					if (IgnoreList.Contains(parent.ProcessName))
					{
						// nothing, ignoring
					}
					else
					{
						ProcessController parentcontrol = null;
						if (execontrol.TryGetValue(LowerCase(parent.ProcessName), out parentcontrol))
						{

							if (!parentcontrol.ChildPriorityReduction && (ProcessHelpers.PriorityToInt(process.PriorityClass) > ProcessHelpers.PriorityToInt(parentcontrol.ChildPriority)))
							{
								Log.Verbose(process.ProcessName + " (#" + process.Id + ") has parent " + parent.ProcessName + " (#" + parent.Id + ") has non-reductable higher than target priority.");
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
																   process.ProcessName, process.Id, parent.ProcessName, parent.Id);
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
				Console.WriteLine(e.StackTrace);
				Log.Warning("Uncaught exception while processing new instances");
				throw;
			}
			finally
			{
				Handling -= list.Count;
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

#if DEBUG
			Log.Verbose("Caught: {ProcessName} (#{ProcessID})", info.Name, info.Id);
#endif

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
			foreach (PropertyData pd in ev.NewEvent.Properties)
			{
				Console.WriteLine("\n============================= =========");
				Console.WriteLine("{0},{1},{2}", pd.Name, pd.Type, pd.Value);
			}
		}

		async void NewInstanceTriage(object sender, System.Management.EventArrivedEventArgs e)
		{
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
					Console.WriteLine("Pathless Pid({0}): {1}", pid, (string)(targetInstance.Properties["ExecutablePath"].Value));
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

			await Task.Delay(100);

			if (string.IsNullOrEmpty(exename) && pid == -1)
			{
				Log.Warning("Failed to acquire neither process name nor process Id");
				return;
			}

			//Handle=pid
			// FIXME
			Handling += 1;
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
					Console.WriteLine(ex.StackTrace);
					Log.Warning("Uncaught exception while handling new instance");
					throw;
				}
				finally
				{
					Handling -= 1;
					// DEBUG: if (TaskMaster.VeryVerbose) Console.WriteLine(string.Format("Handling: -{0} = {1} --- NewInstanceTriage", 1, Handling));
					onInstanceHandling?.Invoke(this, new InstanceEventArgs { Count = -1 });
				}
			}
		}

		void LoadPathList()
		{
			Log.Verbose("Loading user defined paths...");
			SharpConfig.Configuration pathcfg = TaskMaster.loadConfig(pathfile);

			if (pathcfg.Count() == 0)
			{
				// Generate initial Paths.ini
				var stsec = pathcfg["Steam"];
				stsec.Comment = "Without path set, TaskMaster locates the proper path if it's running on launch based on image. Subpath is appended to the located path if provided.";
				var st1 = stsec.GetSetDefault("Image", "steam.exe").StringValue;
				stsec["Image"].Comment = "Process filename";
				var st2 = stsec.GetSetDefault("Subpath", "steamapps").StringValue;
				var st3 = stsec.GetSetDefault("Decrease", false).BoolValue;
				var st4 = stsec.GetSetDefault("Priority", 3).IntValue;

				var gsec = pathcfg["Games"];
				var g1 = gsec.GetSetDefault("Path", "C:\\Games").StringValue;
				var g3 = gsec.GetSetDefault("Decrease", false).BoolValue;
				var g4 = gsec.GetSetDefault("Priority", 3).IntValue;

				TaskMaster.saveConfig(pathcfg);
			}

			bool pathfile_dirty = false;
			foreach (SharpConfig.Section section in pathcfg)
			{
				string name = section.Name;
				string executable = section.TryGet("Image")?.StringValue;
				string path = section.TryGet("Path")?.StringValue;
				string subpath = section.TryGet("Subpath")?.StringValue;
				bool increase = section.TryGet("Increase")?.BoolValue ?? true;
				bool decrease = section.TryGet("Decrease")?.BoolValue ?? true;
				ProcessPriorityClass priority = ProcessHelpers.IntToPriority(section.TryGet("Priority")?.IntValue ?? 2);
				int affinity = section.TryGet("Affinity")?.IntValue ?? allCPUsMask;

				// TODO: POWERMODE
				string pmodes = section.TryGet("Power mode")?.StringValue ?? null;
				PowerManager.PowerMode pmode = PowerManager.GetModeByName(pmodes);
				if (pmode == PowerManager.PowerMode.Custom)
				{
					Log.Warning("'{SectionName}' has unrecognized power plan: {PowerPlan}", section.Name, pmodes);
					pmode = PowerManager.PowerMode.Undefined;
				}

				// TODO: technically subpath should be enough...
				if (path == null)
				{
					if (subpath == null)
					{
						Log.Warning("{SectionName} does not have 'path' nor 'subpath'.", name);
						continue;
					}
					if (executable == string.Empty)
					{
						Log.Warning("{SectionName} has no 'path' nor 'image'.", name);
						continue;
					}
				}

				if (!System.IO.Directory.Exists(path))
				{
					Log.Warning("{Path} ({SectionName}) does not exist.", path, name);
					if (subpath == null && executable != null)
						continue; // we can't use this info to figure out new path
					path = null; // should be enough to construct new path
				}

				if (path != null && subpath != null && !path.Contains(subpath))
					Log.Warning("{SectioName} is misconfigured: {SubPath} not in {Path}",
							   name, subpath, path); // we don't really care

				var pc = new PathControl(name, executable, priority, affinity, subpath, path);
				pc.Decrease = decrease;
				pc.Increase = increase;
				pc.PowerPlan = pmode;
				if (pc.Locate())
				{
					lock (pathwatchlock)
					{
						pathwatch.Add(pc);
						if (pathinit != null) pathinit.Remove(pc);
						if (section.TryGet("path")?.StringValue != pc.Path)
						{
							section["path"].StringValue = pc.Path;
							pathfile_dirty = true;
						}
					}

					if (stats.Contains(pc.Path))
					{
						pc.Adjusts = stats[pc.Path].TryGet("Adjusts")?.IntValue ?? 0;
					}

					Log.Verbose("{SectionName} ({Path}) added to active watch list.",
								name, pc.Path);
				}
				else
				{
					lock (pathwatchlock)
					{
						if (pathinit == null) pathinit = new List<PathControl>();
						pathinit.Add(pc);
					}
					Log.Verbose("{SectionName} ({SubPath}) added to init list.",
								name, subpath);
				}
			}

			if (pathinit != null) Log.Debug("Path init list has " + pathinit.Count + " item(s), should be 0.");
			else Log.Debug("Path init list is not present. Huzzah!");

			lock (pathwatchlock)
			{
				if (pathinit != null && pathinit.Count == 0) pathinit = null;
			}

			if (pathfile_dirty) TaskMaster.saveConfig(pathcfg);

			Log.Verbose("Path loading complete.");
		}

		public List<PathControl> ActivePaths()
		{
			return pathwatch;
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
			if (rescanrequests == 0)
			{
				Log.Verbose("No apps have requests to rescan, stopping rescanning.");
				rescanTimer.Stop();
			}
		}

		System.Management.ManagementEventWatcher watcher;

		const string appfile = "Apps.ini";
		const string pathfile = "Paths.ini";
		const string statfile = "Apps.Statistics.ini";
		// ctor, constructor
		public ProcessManager()
		{
			Log.Verbose("Starting...");

			CPUCount = Environment.ProcessorCount;
			Log.Information("Processor count: {ProcessorCount}", CPUCount);

			allCPUsMask = 1;
			for (int i = 0; i < CPUCount - 1; i++)
				allCPUsMask = (allCPUsMask << 1) | 1;

			Log.Information("Full CPU mask: {ProcessorBitMask} ({ProcessorMask}) (OS control)",
							Convert.ToString(allCPUsMask, 2), allCPUsMask);

			loadConfig();

			if (TaskMaster.PathMonitorEnabled)
				LoadPathList();

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

			foreach (ProcessController proc in images)
			{
				if (proc.Adjusts > 0)
				{
					stats[proc.Executable]["Adjusts"].IntValue = proc.Adjusts;
					TaskMaster.MarkDirtyINI(stats);
				}
				if (proc.LastSeen != DateTime.MinValue)
				{
					stats[proc.Executable]["Last seen"].SetValue(proc.LastSeen.Unixstamp());
					TaskMaster.MarkDirtyINI(stats);
				}
			}

			foreach (PathControl path in pathwatch)
			{
				if (path.Adjusts > 0 || path.LastSeen != DateTime.MinValue)
				{
					stats[path.Path]["Adjusts"].IntValue = path.Adjusts;
					TaskMaster.MarkDirtyINI(stats);
				}
			}
		}
	}
}
