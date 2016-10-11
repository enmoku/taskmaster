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

namespace TaskMaster
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Diagnostics;
	using System.ComponentModel;

	public enum ProcessState
	{
		OK,
		Modified,
		AccessDenied,
		Invalid
	}

	/// <summary>
	/// Process control.
	/// </summary>
	sealed public class ProcessController : AbstractProcessControl
	{
		public bool Increase = false;
		/// <summary>
		/// CPU core affinity.
		/// </summary>
		public IntPtr Affinity = IntPtr.Zero;

		/// <summary>
		/// Priority boost for foreground applications.
		/// </summary>
		public bool Boost = true;

		public bool Children = true;
		public ProcessPriorityClass ChildPriority = System.Diagnostics.ProcessPriorityClass.Normal;

		int p_rescan;
		/// <summary>
		/// Delay before we try to use Scan again.
		/// </summary>
		public int Rescan
		{
			get { return p_rescan; }
			set { p_rescan = value >= 0 ? value : 0; }
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="T:TaskMaster.ProcessControl"/> class.
		/// </summary>
		/// <param name="friendlyname">Human-readable name for the process. For display purposes only.</param>
		/// <param name="executable">Executable filename.</param>
		/// <param name="priority">Target process priority.</param>
		/// <param name="increase">Increase.</param>
		/// <param name="affinity">CPU core affinity.</param>
		/// <param name="boost">Foreground process priority boost.</param>
		/*
		public ProcessController(string friendlyname, string executable, ProcessPriorityClass priority=ProcessPriorityClass.Normal, bool increase=false, int affinity=0, bool boost=true, int rescan=0)
		{
			FriendlyName = friendlyname;
			Executable = executable;
			ExecutableFriendlyName = System.IO.Path.GetFileNameWithoutExtension(executable);
			Priority = priority;
			Increase = increase;
			Affinity = new IntPtr(affinity != 0 ? affinity : ProcessManager.allCPUsMask);
			Boost = boost;

			Rescan = rescan;

			ChildPriority = priority;

			Log.Trace(FriendlyName + " (" + Executable + "), " + Priority + (Affinity != IntPtr.Zero ? ", Mask:" + Affinity : "") + (Rescan>0 ? ", Rescan: " + Rescan + " minutes":""));
		}
		*/

		// TODO EVENT(??)
		/// <summary>
		/// Touch the specified process and child.
		/// </summary>
		/// <param name="process">Process.</param>
		/// <param name="child">If set to <c>true</c> child.</param>
		public bool Touch(Process process, out ProcessState state)
		{
			state = ProcessState.Invalid;
			Debug.Assert(process != null);
			try
			{
				if (process.HasExited)
				{
					if (TaskMaster.VeryVerbose)
						Log.Warn(string.Format("{0} (pid:{1}) has already exited.", Executable, process.Id));
					return false;
				}
			}
			catch (Win32Exception ex)
			{
				if (ex.NativeErrorCode != 5)
					Log.Warn("Access error: " + process.ProcessName + " (pid:" + process.Id + ")");
				state = ProcessState.AccessDenied;
				return false; // we don't care what this error is
			}

			if (TaskMaster.VeryVerbose)
				Log.Debug(string.Format("{0} ({1}, pid:{2})", FriendlyName, Executable, process.Id));

			bool mAffinity, mPriority, mBoost, modified = false;
			lock (process)
			{
				mBoost = mPriority = mAffinity = false;
				IntPtr oldAffinity = process.ProcessorAffinity;
				ProcessPriorityClass oldPriority = process.PriorityClass;
				LastSeen = DateTime.Now;

				if (process.SetLimitedPriority(Priority, Increase, true))
					modified = mPriority = true;

				if (process.ProcessorAffinity != Affinity)
				{
					//CLEANUP: System.Console.WriteLine("Current affinity: {0}", Convert.ToString(item.ProcessorAffinity.ToInt32(), 2));
					//CLEANUP: System.Console.WriteLine("Target affinity: {0}", Convert.ToString(proc.Affinity.ToInt32(), 2));
					try
					{
						process.ProcessorAffinity = Affinity;
						modified = mAffinity = true;
					}
					catch (System.ComponentModel.Win32Exception)
					{
						Log.Warn(string.Format("Couldn't modify process ({0}, #{1}) affinity [{2} -> {3}].", Executable, process.Id, process.ProcessorAffinity.ToInt32(), Affinity.ToInt32()));
					}
				}

				if (process.PriorityBoostEnabled != Boost)
				{
					process.PriorityBoostEnabled = Boost;
					modified = mBoost = true;
				}

				if (modified)
				{
					Adjusts += 1;

					LastTouch = DateTime.Now;

					// TODO: Is StringBuilder fast enough for this to be good idea?
					var ls = new System.Text.StringBuilder();
					ls.Append(Executable).Append(" (pid:").Append(process.Id).Append(") - ");
					//ls.Append("(").Append(control.Executable).Append(") =");
					if (mPriority)
						ls.Append(" Priority(").Append(oldPriority).Append(" -> ").Append(Priority).Append(")");
					if (mAffinity)
						ls.Append(" Affinity(").Append(oldAffinity).Append(" -> ").Append(Affinity).Append(")");
					if (mBoost)
						ls.Append(" Boost(").Append(Boost).Append(")");
					//ls.Append("; Start: ").Append(process.StartTime); // when the process was started // DEBUG
					Log.Info(ls.ToString());

					state = ProcessState.Modified;

					onTouch?.Invoke(this, new ProcessEventArgs { Control = this, Process = process });
				}
				else
				{
					if (TaskMaster.VeryVerbose)
						Log.Trace(string.Format("'{0}' (pid:{1}) seems to be OK already.", Executable, process.Id));
					
					state = ProcessState.OK;
				}
			}

			ScanScheduler();


			return modified;
		}

		int ScanScheduled = 0;
		async System.Threading.Tasks.Task ScanScheduler()
		{
			double n = (DateTime.Now - LastScan).TotalMinutes;
			//Log.Trace(string.Format("[{0}] last scan {1:N1} minute(s) ago.", FriendlyName, n));
			if (Rescan > 0 && n >= Rescan)
			{
				if (System.Threading.Interlocked.CompareExchange(ref ScanScheduled, 1, 0) == 1)
					return;

				if (TaskMaster.Verbose)
					Log.Trace(string.Format("'{0}' detected, rescanning.", FriendlyName));
				
				await Scan();

				ScanScheduled = 0;
			}
		}

		DateTime LastScan = DateTime.MinValue;
		public async System.Threading.Tasks.Task Scan()
		{
			await System.Threading.Tasks.Task.Delay(100);

			Process[] procs = Process.GetProcessesByName(ExecutableFriendlyName); // should be case insensitive by default
			if (procs.Length == 0) return;

			//LastSeen = LastScan;
			LastScan = DateTime.Now;

			if (TaskMaster.VeryVerbose)
				Log.Trace("Scanning '" + FriendlyName + "' (found " + procs.Length + " instances)");

			int tc = 0;
			ProcessState state;
			foreach (Process process in procs)
			{
				if (Touch(process, out state))
					tc++;
			}

			if (TaskMaster.Verbose)
				Log.Trace("Scan for '" + FriendlyName + "' modified " + tc + " instance(s)");
		}

		public static event EventHandler<ProcessEventArgs> onTouch;
	}

	sealed public class PathControl : AbstractProcessControl
	{
		public string Subpath;
		public string Path;

		public PathControl(string name, string executable, ProcessPriorityClass priority, string subpath, string path=null)
		{
			FriendlyName = name;
			Executable = executable;
			ExecutableFriendlyName = System.IO.Path.GetFileNameWithoutExtension(Executable);
			Priority = priority;
			Subpath = subpath;
			Path = path;
			if (path != null)
				Log.Info(string.Format("'{0}' watched in: {1} [{2}]", FriendlyName, Path, Priority));
			else
				Log.Info(string.Format("'{0}' matching for '{1}' [{2}]", Executable, Subpath, Priority));
		}

		public bool Touch(Process process, string path, out ProcessState state)
		{
			Debug.Assert(process != null);
			Debug.Assert(!string.IsNullOrEmpty(path));

			state = ProcessState.Invalid;

			try
			{
				if (process.HasExited)
				{
					if (TaskMaster.Verbose)
						Log.Warn(string.Format("{0} (pid:{1}) has already exited.", process.ProcessName, process.Id));
					return false;
				}
			}
			catch (System.ComponentModel.Win32Exception ex)
			{
				if (ex.NativeErrorCode != 5)
					Log.Warn("Access error: " + process.ProcessName + " (pid:" + process.Id + ")");
				state = ProcessState.AccessDenied;
				return false; // we don't care wwhat this error is
			}

			string name = System.IO.Path.GetFileName(path);
			try
			{
				ProcessPriorityClass oldPriority = process.PriorityClass;
				if (process.SetLimitedPriority(Priority, true, true))
				//if (ProcessManager.PriorityToInt(process.PriorityClass) < ProcessManager.PriorityToInt(Priority)) // TODO: possibly allow decreasing priority, but for this 
				{
					//process.PriorityClass = Priority;
					LastSeen = DateTime.Now;
					Adjusts += 1;

					onTouch?.Invoke(this, new PathControlEventArgs());

					Log.Info(string.Format("{0} (pid:{1}); Priority({2} -> {3})", name, process.Id, oldPriority, Priority));

					state = ProcessState.Modified;
					return true;
				}

				state = ProcessState.OK;

				Log.Debug(string.Format("{0} (pid:{1}); looks OK, not touched.", name, process.Id));
			}
			catch
			{
				state = ProcessState.AccessDenied;

				Log.Info(string.Format("Failed to touch '{0}' (pid:{1})", name, process.Id));
			}

			return false;
		}

		public bool Locate()
		{
			if (Path != null && System.IO.Directory.Exists(Path))
				return true;
			
			Process process = Process.GetProcessesByName(ExecutableFriendlyName)[0]; // not great thing, but should be good enough.
			if (process == null)
				return false;
			
			Log.Trace("Watched item '" + FriendlyName + "' encountered.");
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
				Log.Warn(string.Format("Access failure with '{0}'", FriendlyName));
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

	sealed public class ProcessManager : IDisposable
	{
		static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

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
		/// <summary>
		/// Executable name to ProcessControl mapping.
		/// </summary>
		Dictionary<string, ProcessController> execontrol = new Dictionary<string, ProcessController>();

		int numCPUs = 1;

		public static int allCPUsMask = 1;

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
			Log.Warn(executable + " was not found!");
			return null;
		}

		void UpdatePathWatch()
		{
			if (pathinit != null)
			{
				Log.Trace("Locating watched paths.");
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

				if (pathinit.Count == 0) pathinit = null;
				Log.Trace("Path location complete.");
			}
		}

		/// <summary>
		/// Processes everything. Pointlessly thorough, but there's no nicer way around for now.
		/// </summary>
		public void ProcessEverything()
		{
			Log.Trace("Processing everything.");

			Process[] procs = Process.GetProcesses();

			Log.Trace(string.Format("Scanning {0} processes.", procs.Length));
			foreach (Process process in procs)
				CheckProcess(process);

			/*
			// Does the same as the above
			Log.Trace("Going through process control list.");
			foreach (ProcessController control in images)
				control.Scan();
			*/

			UpdatePathWatch();
			Log.Trace("Done processing everything.");
		}

		static bool ControlChildren = false;
		SharpConfig.Configuration stats;
		bool stats_dirty = false;
		public void loadConfig()
		{
			Log.Trace("Loading watchlist");
			SharpConfig.Configuration appcfg = TaskMaster.loadConfig(appfile);
			if (stats == null)
				stats = TaskMaster.loadConfig(statfile);

			bool disableChildControl = !TaskMaster.cfg["Performance"].GetSetDefault("Child processes", false).BoolValue;

			foreach (SharpConfig.Section section in appcfg)
			{
				if (TaskMaster.VeryVerbose)
					Log.Debug("Section: " + section.Name);

				if (!section.Contains("image"))
				{
					// TODO: Deal with incorrect configuration lacking image
					Log.Warn(string.Format("'{0}' has no image.", section.Name));
					continue;
				}
				if (!section.Contains("priority") && !section.Contains("affinity"))
				{
					// TODO: Deal with incorrect configuration lacking image
					Log.Warn(string.Format("'{0}' has no priority or affinity.", section.Name));
					continue;
				}

				int aff = section.TryGet("Affinity")?.IntValue ?? 0;
				int prio = section.TryGet("Priority")?.IntValue ?? 2;
				var cnt = new ProcessController
				{
					FriendlyName = section.Name,
					Executable = section["Image"].StringValue,
					// friendly name is filled automatically
					Priority = ProcessHelpers.IntToPriority(prio),
					Increase = (section.TryGet("Increase")?.BoolValue ?? false),
					Affinity = new IntPtr(aff != 0 ? aff : allCPUsMask),
					Boost = (section.TryGet("Boost")?.BoolValue ?? true),
					Rescan = (section.TryGet("Rescan")?.IntValue ?? 0),
					Children = section.GetSetDefault("Children", false).BoolValue,
					ChildPriority = ProcessHelpers.IntToPriority(section.TryGet("Child priority")?.IntValue ?? prio)
				};

				if (disableChildControl)
					cnt.Children = false;
				else
					ControlChildren |= cnt.Children;

				Log.Trace(cnt.FriendlyName + " (" + cnt.Executable + "), " + cnt.Priority + (cnt.Affinity != IntPtr.Zero ? ", Mask:" + cnt.Affinity : "") + (cnt.Rescan > 0 ? ", Rescan: " + cnt.Rescan + " minutes" : "") + (cnt.Children ? ", Children: " + cnt.ChildPriority : ""));

				//cnt.delay = section.Contains("delay") ? section["delay"].IntValue : 30; // TODO: Add centralized default delay
				//cnt.delayIncrement = section.Contains("delay increment") ? section["delay increment"].IntValue : 15; // TODO: Add centralized default increment
				if (stats.Contains(cnt.Executable))
				{
					cnt.Adjusts = stats[cnt.Executable].TryGet("Adjusts")?.IntValue ?? 0;
					cnt.LastSeen = stats[cnt.Executable].TryGet("Last seen")?.DateTimeValue ?? DateTime.MinValue;
					stats_dirty = true;
				}

				images.Add(cnt);
				execontrol.Add(cnt.ExecutableFriendlyName, cnt);
				if (TaskMaster.VeryVerbose)
					Log.Trace(string.Format("'{0}' added to monitoring.", section.Name));
			}

			//TaskMaster.cfg["Applications"]["Ignored"].StringValueArray = IgnoreList;
			string[] newIgnoreList = TaskMaster.cfg["Applications"].GetSetDefault("Ignored", IgnoreList)?.StringValueArray;
			if (newIgnoreList != null)
			{
				IgnoreList = newIgnoreList;
				Log.Info("Custom application ignore list loaded.");
			}
			else
				TaskMaster.saveConfig("Core.ini", TaskMaster.cfg);

			ControlChildren &= !disableChildControl;

			Log.Info("Child process monitoring: " + (ControlChildren ? "Enabled" : "Disabled"));
		}

		/// <summary>
		/// Retrieve file path for the process.
		/// Slow due to use of WMI.
		/// </summary>
		/// <returns>The process path.</returns>
		/// <param name="processId">Process ID</param>
		string GetProcessPath(int processId)
		{
			if (!TaskMaster.WMIqueries) return null;

			Stopwatch n = Stopwatch.StartNew();

			string path = null;
			string wmiQueryString = "SELECT ProcessId, ExecutablePath FROM Win32_Process WHERE ProcessId = " + processId;
			using (var searcher = new System.Management.ManagementObjectSearcher(wmiQueryString))
			{
				using (var results = searcher.Get())
				{
					System.Management.ManagementObject mo = results.Cast<System.Management.ManagementObject>().FirstOrDefault();
					if (mo != null)
					{
						path = (string)mo["ExecutablePath"];
						if (path != null && TaskMaster.VeryVerbose)
							Log.Debug("WMI fetch (#" + processId + "): " + path);

						return path;
					}
				}
			}

			n.Stop();
			Statistics.WMIquerytime += n.Elapsed.TotalSeconds;

			return path;
		}

		bool CheckPathWatch(Process process, out ProcessState state){
			Debug.Assert(process != null);

			state = ProcessState.Invalid;

			try
			{
				if (process.HasExited)
				{
					if (TaskMaster.Verbose)
						Log.Warn(string.Format("{0} (pid:{1}) has already exited.", process.ProcessName, process.Id));
					return false;
				}
			}
			catch (System.ComponentModel.Win32Exception ex)
			{
				if (ex.NativeErrorCode != 5)
					Log.Warn("Access error: " + process.ProcessName + " (pid:" + process.Id + ")");
				return false; // we don't care wwhat this error is
			}

			bool slow = false;
			string path;
			try
			{
				path = process.MainModule?.FileName; // this will cause win32exception of various types, we don't Really care which error it is
			}
			catch (NotSupportedException)
			{
				Log.Warn("[Unexpected] Not supported operation: " + process.ProcessName + " (pid:" + process.Id + ")");
				state = ProcessState.AccessDenied;
				return false;
			}
			catch (System.ComponentModel.Win32Exception ex)
			{
				if ((path = GetProcessPath(process.Id)) == null)
				{
					#if DEBUG
					switch (ex.NativeErrorCode)
					{
						case 5:
							Log.Trace(string.Format("Access denied to '{0}' (pid:{1})", process.ProcessName, process.Id));
							break;
						case 299: // 32/64 bit taskmaster accessing opposite
							Log.Debug(string.Format("Can not fully access '{0}' (pid:{1})", process.ProcessName, process.Id));
							break;
						default:
							Log.Debug(string.Format("Unknown failure with '{0}' (pid:{1}), error: {2}", process.ProcessName, process.Id, ex.NativeErrorCode));
							Log.Debug(ex);
							break;
					}
					#endif
					// we can not touch this so we shouldn't even bother trying
					Log.Trace("Failed to access '{0}' (pid:{1})", process.ProcessName, process.Id);
					state = ProcessState.AccessDenied;
					return false;
				}
				slow = true;
			}

			// TODO: This needs to be FASTER
			foreach (PathControl pc in pathwatch)
			{
				//Log.Debug("with: "+ pc.Path);
				if (path.StartsWith(pc.Path, StringComparison.InvariantCultureIgnoreCase)) // TODO: make this compatible with OSes that aren't case insensitive?
				{
					Log.Info("[" + pc.FriendlyName + "] matched " + (slow ? "~slowly~ " : "") + "at: " + path);
					bool touched = pc.Touch(process, path, out state);
					return true;
				}
			}

			if (TaskMaster.VeryVerbose)
				Log.Trace("Not for us: " + path);
			
			return false;
		}

		static string[] IgnoreList = { "dllhost", "svchost", "taskeng", "consent", "taskhost", "rundll32", "conhost", "dwm", "wininit", "csrss", "winlogon", "services", "explorer" };

		bool IgnoreProcessID(int pid)
		{
			return (pid <= 4);
		}

		bool IgnoreProcessName(string name)
		{
			if (TaskMaster.CaseSensitive)
				return IgnoreList.Contains(name);
			else
				return IgnoreList.Contains(name, StringComparer.InvariantCultureIgnoreCase);
		}

		async System.Threading.Tasks.Task CheckProcess(Process process)
		{
			Debug.Assert(process != null);

			//if (TaskMaster.VeryVerbose) Log.Debug("Processing: " + process.ProcessName);
			
			if (IgnoreProcessID(process.Id) || IgnoreProcessName(process.ProcessName))
			{
				if (TaskMaster.VeryVerbose) Log.Trace("Ignoring process: " + process.ProcessName + " (#" + process.Id + ")");
				return;
			}

			ProcessState state = ProcessState.Invalid;

			// TODO: check proc.processName for presence in images.
			ProcessController control;
			if (execontrol.TryGetValue(process.ProcessName, out control))
			{
				await System.Threading.Tasks.Task.Delay(100);

				if (control.Touch(process, out state))
					if (TaskMaster.VeryVerbose)
						Log.Debug("Control group: " + control.FriendlyName + ", process: " + process.ProcessName + " (#" + process.Id + ")");
				return; // execontrol had this, we don't care about anything else for this.
			}

			if (pathwatch.Count > 0)
			{
				if (TaskMaster.VeryVerbose) Log.Debug(string.Format("Checking paths for '{0}' (pid:{1})", process.ProcessName, process.Id));
				if (CheckPathWatch(process, out state)) return; // we don't care to process more
			}

			switch (state)
			{
				case ProcessState.Invalid: break;
				default: return;
			}

			if (ControlChildren) // this slows things down a lot it seems
			{
				await System.Threading.Tasks.Task.Delay(100);

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
					Log.Warn("Couldn't get parent process for " + process.ProcessName + " (#" + process.Id + ")");
				}

				if (!IgnoreProcessID(ppid)) // 0 and 4 are system processes, we don't care about their children
				{
					Process parent = Process.GetProcessById(ppid);
					if (IgnoreList.Contains(parent.ProcessName))
					{
						// nothing, ignoring
					}
					else if (execontrol.TryGetValue(parent.ProcessName, out control) && control.Children
						&& ProcessHelpers.PriorityToInt(process.PriorityClass) != ProcessHelpers.PriorityToInt(control.ChildPriority))
					{
						ProcessPriorityClass oldprio = process.PriorityClass;
						process.SetLimitedPriority(control.ChildPriority, true, false);
						Log.Info(process.ProcessName + " (pid:" + process.Id + ") child of " + control.FriendlyName + " (pid:" + ppid + ") Priority(" + oldprio + " -> " + process.PriorityClass + ")");
					}
					else
						if (TaskMaster.Verbose) Log.Debug(process.ProcessName + " (#" + process.Id + ") has parent " + parent.ProcessName + " (#" + parent.Id + ")");
				}
				n.Stop();
				Statistics.Parentseektime += n.Elapsed.TotalSeconds;
				Statistics.ParentSeeks += 1;
			}
		}

		void NewInstanceHandler(object sender, System.Management.EventArrivedEventArgs e)
		{
			Stopwatch n = Stopwatch.StartNew();
			System.Management.ManagementBaseObject targetInstance;
			int pid;
			try
			{
				targetInstance = (System.Management.ManagementBaseObject)e.NewEvent.Properties["TargetInstance"].Value;
				pid = Convert.ToInt32(targetInstance.Properties["Handle"].Value);
			}
			catch (Exception)
			{
				Log.Warn("Failed to extract process ID from WMI event.");
				n.Stop();
				Statistics.WMIquerytime += n.Elapsed.TotalSeconds;
				Statistics.WMIqueries += 1;
				throw;
			}

			Process process = null;
			try
			{
				// since targetinstance actually has fuckall information, we need to extract it...
				process = Process.GetProcessById(pid);
			}
			catch (Exception)
			{
				Log.Trace("Process exited before we had time to identify it."); // technically an error [Warn], but not that interesting for anyone
				n.Stop();
				Statistics.WMIquerytime += n.Elapsed.TotalSeconds;
				Statistics.WMIqueries += 1;
				return;
			}

			if (TaskMaster.VeryVerbose)
				Log.Trace("Caught: " + process.ProcessName + " (pid:"+process.Id+")");
			
			CheckProcess(process);

			n.Stop();
			Statistics.WMIquerytime += n.Elapsed.TotalSeconds;
			Statistics.WMIqueries += 1;
		}

		void LoadPathList()
		{
			Log.Trace("Loading user defined paths...");
			SharpConfig.Configuration pathcfg = TaskMaster.loadConfig(pathfile);
			bool pathfile_dirty = false;
			foreach (SharpConfig.Section section in pathcfg)
			{
				string name = section.Name;
				string executable = section.TryGet("image")?.StringValue;
				string path = section.TryGet("path")?.StringValue;
				string subpath = section.TryGet("subpath")?.StringValue;
				bool increase = section.TryGet("increase")?.BoolValue ?? true;
				ProcessPriorityClass priority = ProcessHelpers.IntToPriority(section.TryGet("priority")?.IntValue ?? 2);

				// TODO: technically subpath should be enough...
				if (path == null)
				{
					if (subpath == null)
					{
						Log.Warn(name + " does not have 'path' nor 'subpath'.");
						continue;
					}
					if (executable == string.Empty)
					{
						Log.Warn(name + " has no 'path' nor 'image'.");
						continue;
					}
				}

				if (!System.IO.Directory.Exists(path))
				{
					Log.Warn(path + "(" + name + ") does not exist.");
					if (subpath == null && executable != null)
						continue; // we can't use this info to figure out new path
					path = null; // should be enough to construct new path
				}

				if (path != null && subpath != null && !path.Contains(subpath))
					Log.Warn(name + " is misconfigured: " + subpath + " not in " + path); // we don't really care

				var pc = new PathControl(name, executable, priority, subpath, path);
				if (pc.Locate())
				{
					pathwatch.Add(pc);
					if (pathinit != null) pathinit.Remove(pc);
					if (section.TryGet("path")?.StringValue != pc.Path)
					{
						section["path"].StringValue = pc.Path;
						pathfile_dirty = true;
					}
					Log.Trace(name + " (" + pc.Path + ") added to active watch list.");
				}
				else
				{
					if (pathinit == null) pathinit = new List<PathControl>();
					pathinit.Add(pc);
					Log.Trace(name + " ("+subpath+") added to init list.");
				}
				if (pathinit != null) Log.Debug("Path init list has " + pathinit.Count + " item(s), should be 0.");
				else Log.Debug("Path init list is not present. Huzzah!");
			}

			if (pathinit != null && pathinit.Count == 0) pathinit = null;

			if (pathfile_dirty)
				TaskMaster.saveConfig(pathfile, pathcfg);
			Log.Trace("Path loading complete.");
		}

		public List<PathControl> ActivePaths()
		{
			return pathwatch;
		}

		System.Management.ManagementEventWatcher watcher;

		const string appfile = "Apps.ini";
		const string pathfile = "Paths.ini";
		const string statfile = "Apps.Statistics.ini";
		// ctor, constructor
		public ProcessManager()
		{
			Log.Trace("Starting...");

			numCPUs = Environment.ProcessorCount;
			Log.Info(string.Format("Processor count: {0}", numCPUs));

			allCPUsMask = 1;
			for (int i = 0; i < numCPUs - 1; i++)
				allCPUsMask = (allCPUsMask << 1) | 1;
			
			Log.Info(string.Format("Full processor mask: {0} ({1})", Convert.ToString(allCPUsMask, 2), allCPUsMask));

			loadConfig();
			LoadPathList();

			// FIXME: doesn't seem to work when lots of new processes start at the same time.
			try
			{
				var query = new System.Management.EventQuery("SELECT TargetInstance FROM __InstanceCreationEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_Process'");
				var scope = new System.Management.ManagementScope(new System.Management.ManagementPath(@"\\.\root\CIMV2")); // @"\\.\root\CIMV2"
				watcher = new System.Management.ManagementEventWatcher(scope, query);
			}
			catch (System.Management.ManagementException e)
			{
				Log.Error("Failed to initialize WMI event watcher: " + e.Message);
			}

			if (watcher != null)
			{
				watcher.EventArrived += NewInstanceHandler;
				/*
				// Only useful for debugging the watcher, but there doesn't seem to be any unwanted stops happening.
				watcher.Stopped += (object sender, System.Management.StoppedEventArgs e) =>
				{
					Log.Warn("New instance watcher stopped.");
				};
				*/
				watcher.Start();
				Log.Debug("New instance watcher initialized.");
			}
			else
			{
				Log.Error("Failed to initialize new instance watcher.");
				throw new InitFailure("New instance watcher not initialized");
			}
		}

		bool disposed = false;
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

				if (stats_dirty)
					saveStats();
			}

			disposed = true;
		}

		void saveStats()
		{
			Log.Trace("Saving stats...");
			if (stats == null)
				stats = TaskMaster.loadConfig(statfile);

			foreach (ProcessController proc in images)
			{
				if (proc.Adjusts > 0)
				{
					stats[proc.Executable]["Adjusts"].IntValue = proc.Adjusts;
					stats_dirty = true;
				}
				if (proc.LastSeen != DateTime.MinValue)
				{
					stats[proc.Executable]["Last seen"].DateTimeValue = proc.LastSeen;
					stats_dirty = true;
				}
			}

			if (stats_dirty)
				TaskMaster.saveConfig(statfile, stats);
		}
	}
}
