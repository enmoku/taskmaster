//
// EmptyClass.cs
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using System.ComponentModel;
using NLog.Fluent;
using System.Windows;

namespace TaskMaster
{
	using System.Diagnostics;

	/// <summary>
	/// Process control.
	/// </summary>
	public class ProcessControl
	{
		static NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

		/// <summary>
		/// Human-readable friendly name for the process.
		/// </summary>
		public string FriendlyName;
		/// <summary>
		/// Executable filename related to this.
		/// </summary>
		public string Executable;
		/// <summary>
		/// Frienly executable name as required by various System.Process functions.
		/// Same as <see cref="T:TaskMaster.ProcessControl.Executable"/> but with the extension missing.
		/// </summary>
		public string ExecutableFriendlyName;
		/// <summary>
		/// Target priority class for the process.
		/// </summary>
		public ProcessPriorityClass Priority = ProcessPriorityClass.Normal;
		public bool Increase = false; // TODO: UNUSED
		/// <summary>
		/// CPU core affinity.
		/// </summary>
		public IntPtr Affinity = IntPtr.Zero;
		/// <summary>
		/// Priority boost for foreground applications.
		/// </summary>
		public bool Boost = true;

		public int Adjusts = 0;
		//public bool EmptyWorkingSet = true; // pointless?

		public DateTime lastSeen;
		public DateTime lastTouch;

		/// <summary>
		/// Initializes a new instance of the <see cref="T:TaskMaster.ProcessControl"/> class.
		/// </summary>
		/// <param name="friendlyname">Human-readable name for the process. For display purposes only.</param>
		/// <param name="executable">Executable filename.</param>
		/// <param name="priority">Target process priority.</param>
		/// <param name="increase">Increase.</param>
		/// <param name="affinity">CPU core affinity.</param>
		/// <param name="boost">Foreground process priority boost.</param>
		public ProcessControl(string friendlyname, string executable, ProcessPriorityClass priority=ProcessPriorityClass.Normal, bool increase=false, int affinity=0, bool boost=true)
		{
			Log.Debug(friendlyname + " (" + executable + "), " + priority + (affinity!=0?", Mask:"+affinity:""));
			//System.String.Format("{0} ({1}), {2}, Mask:{3}", friendlyname, executable, priority, affinity)

			FriendlyName = friendlyname;
			Executable = executable;
			ExecutableFriendlyName = System.IO.Path.GetFileNameWithoutExtension(executable);
			Priority = priority;
			Increase = increase;
			Affinity = new IntPtr(affinity);
			Boost = boost;

			lastSeen = System.DateTime.MinValue;
			lastTouch = System.DateTime.MinValue;

			Adjusts = 0;
		}
	}

	public class PathControl
	{
		static NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

		public string FriendlyName;
		public string Executable;
		public string ExecutableFriendlyName;
		public string Subpath;
		public string Path;
		public ProcessPriorityClass Priority = ProcessPriorityClass.Normal;

		System.DateTime LastSeen;

		public int Adjusts;

		public PathControl(string name, string executable, ProcessPriorityClass priority, string subpath, string path=null)
		{
			FriendlyName = name;
			Executable = executable;
			ExecutableFriendlyName = System.IO.Path.GetFileNameWithoutExtension(Executable);
			Priority = priority;
			Subpath = subpath;
			Path = path;
			if (path != null)
			{
				Log.Info(System.String.Format("'{0}' watched in: {1} [{2}]", FriendlyName, Path, Priority));
			}
			else
			{
				Log.Info(System.String.Format("'{0}' matching for '{1}' [{2}]", Executable, Subpath, Priority));
			}
			Adjusts = 0;
		}

		public void Touch(Process process, string path)
		{
			string name = System.IO.Path.GetFileName(path);
			ProcessPriorityClass oldPriority = process.PriorityClass;
			if (ProcessManager.PriorityToInt(process.PriorityClass) < ProcessManager.PriorityToInt(Priority)) // TODO: possibly allow decreasing priority, but for this 
			{
				process.PriorityClass = Priority;
				LastSeen = System.DateTime.Now;
				Adjusts += 1;
				Log.Info(System.String.Format("{0} (pid:{1}); Priority({2} -> {3})", name, process.Id, oldPriority, Priority));
			}
			else
			{
				Log.Debug(System.String.Format("{0} (pid:{1}); looks OK, not touched.", name, process.Id));
			}

		}

		public bool Locate()
		{
			if (Path != null && System.IO.Directory.Exists(Path))
				return true;
			
			Process process = Process.GetProcessesByName(ExecutableFriendlyName).First();
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
					Log.Debug(System.String.Format("'{0}' bound to: {1}", FriendlyName, Path));

					return true;
				}
				/*
				else
				{
					Log.Trace("Not found! " + fullpath);
				}
				*/
			}
			catch (Exception ex)
			{
				Log.Warn(System.String.Format("Access failure with '{0}'", FriendlyName));
				Console.Error.WriteLine(ex);
			}
			return false;
		}
	}

	public class PathControlEventArgs : EventArgs
	{
		public PathControl Control;
		public PathControlEventArgs(PathControl control)
		{
			Control = control;
		}
	}

	public class ProcessEventArgs : EventArgs
	{
		public ProcessControl control { get; set; }
		public Process process { get; set; }
		public bool Priority { get; set; }
		public bool Affinity { get; set; }
		public bool Boost { get; set; }
	}

	public class ProcessManager : IDisposable
	{
		static NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

		/// <summary>
		/// Actively watched process images.
		/// </summary>
		public List<ProcessControl> images = new List<ProcessControl>();
		/// <summary>
		/// Actively watched paths.
		/// </summary>
		public List<PathControl> pathwatch = new List<PathControl>();
		/// <summary>
		/// Paths not yet properly initialized.
		/// </summary>
		public List<PathControl> pathinit = new List<PathControl>();
		/// <summary>
		/// Executable name to ProcessControl mapping.
		/// </summary>
		IDictionary<string, ProcessControl> execontrol = new Dictionary<string,ProcessControl>();

		public event EventHandler<ProcessEventArgs> onProcAdjust;
		public event EventHandler<PathControlEventArgs> onPathAdjust;
		public event EventHandler<PathControlEventArgs> onPathLocated;

		int numCPUs = 1;
		int allCPUsMask = 1;

		/// <summary>
		/// Gets the control class instance of the executable if it exists.
		/// </summary>
		/// <returns>ProcessControl </returns>
		/// <param name="executable">Executable.</param>
		public ProcessControl getControl(string executable)
		{
			foreach (ProcessControl ctrl in images)
			{
				if (ctrl.Executable == executable)
					   return ctrl;
			}
			Log.Warn(executable + " was not found!");
			return null;
		}

		void onProcAdjustHandler(object sender, ProcessEventArgs e)
		{
			EventHandler<ProcessEventArgs> handler = onProcAdjust;
			if (handler != null)
				handler(this, e);
		}

		public bool Control(ProcessControl control, Process process)
		{
			// TODO: move this to ProcessControl if possible
			if (process.HasExited)
			{
				Log.Trace(System.String.Format("{0} (pid:{1}) has already exited.", control.Executable, process.Id));
				return false;
			}

			//process.Refresh(); // is this necessary?
			Log.Trace(System.String.Format("{0} ({1}, pid:{2})", control.FriendlyName, control.Executable, process.Id));
			bool Affinity = false;
			IntPtr oldAffinity = process.ProcessorAffinity;
			bool Priority = false;
			ProcessPriorityClass oldPriority = process.PriorityClass;
			bool Boost = false;
			control.lastSeen = System.DateTime.Now;
			if (((PriorityToInt(process.PriorityClass) < PriorityToInt(control.Priority)) && control.Increase) || (PriorityToInt(process.PriorityClass) > PriorityToInt(control.Priority)))
			{
				/*
				Console.WriteLine(System.String.Format(
					"increase {0} < {1} = {5}*; decrease {2} > {3} = {6}; increase:{4}",
					PriorityToInt(process.PriorityClass),
					PriorityToInt(control.Priority),
					PriorityToInt(process.PriorityClass),
					PriorityToInt(control.Priority),
					control.Increase,
					(PriorityToInt(process.PriorityClass) < PriorityToInt(control.Priority) && control.Increase),
					(PriorityToInt(process.PriorityClass) > PriorityToInt(control.Priority))
				));
				*/
				process.PriorityClass = control.Priority;
				Priority = true;
			}
			if (process.ProcessorAffinity != control.Affinity) // FIXME: 0 and all cores selected should match
			{
				if (control.Affinity == IntPtr.Zero && process.ProcessorAffinity.ToInt32() == allCPUsMask)
				{
					//System.Console.WriteLine("Current and target affinity set to OS control. No action needed.");
					// No action needed.
				}
				else
				{
					//System.Console.WriteLine("Current affinity: {0}", Convert.ToString(item.ProcessorAffinity.ToInt32(), 2));
					//System.Console.WriteLine("Target affinity: {0}", Convert.ToString(proc.Affinity.ToInt32(), 2));
					try
					{
						process.ProcessorAffinity = control.Affinity;
						Affinity = true;
					}
					catch (Win32Exception)
					{
						Log.Warn(System.String.Format("Couldn't modify process ({0}, #{1}) affinity.", control.Executable, process.Id));
					}
				}
			}
			if (process.PriorityBoostEnabled != control.Boost)
			{
				process.PriorityBoostEnabled = control.Boost;
				Boost = true;
			}
			if (Priority || Affinity || Boost)
			{
				control.Adjusts += 1;
				ProcessEventArgs e = new ProcessEventArgs();
				e.Affinity = Affinity;
				e.Priority = Priority;
				e.Boost = Boost;
				e.control = control;
				e.process = process;

				control.lastTouch = System.DateTime.Now;

				// TODO: Is StringBuilder fast enough for this to be good idea?
				System.Text.StringBuilder ls = new System.Text.StringBuilder();
				ls.Append(control.Executable).Append(" (pid:").Append(process.Id).Append("); ");
				//ls.Append("(").Append(control.Executable).Append(") =");
				if (Priority)
					ls.Append(" Priority(").Append(oldPriority).Append(" -> ").Append(control.Priority).Append(")");
				if (Affinity)
					ls.Append(" Afffinity(").Append(oldAffinity).Append(" -> ").Append(control.Affinity).Append(")");
				if (Boost)
					ls.Append(" Boost(").Append(Boost).Append(")");
				//ls.Append("; Start: ").Append(process.StartTime); // when the process was started // DEBUG
				Log.Info(ls.ToString());
				//Log.Info(System.String.Format("{0} (#{1}) = Priority({2}), Mask({3}), Boost({4}) - Start: {5}",
				//                            proc.Executable, item.Id, Priority, Affinity, Boost, item.StartTime));
				onProcAdjustHandler(this, e);
			}
			else
			{
				Log.Trace(System.String.Format("'{0}' (pid:{1}) seems to be OK already.", control.Executable, process.Id));
			}

			return (Priority || Affinity || Boost);
		}

		async void onPathLocatedHandler(PathControlEventArgs e)
		{
			// TODO: Event
			EventHandler<PathControlEventArgs> handler = onPathLocated;
			if (handler != null)
				handler(e.Control, e);
		}

		async void onPathAdjustHandler(PathControlEventArgs e)
		{
			// TODO: Event
			EventHandler<PathControlEventArgs> handler = onPathAdjust;
			if (handler != null)
				handler(e.Control, e);
		}

		void UpdatePathWatch()
		{
			if (pathinit.Count > 0)
			{
				foreach (PathControl path in pathinit.ToArray())
				{
					if (path.Locate())
					{
						pathwatch.Add(path);
						pathinit.Remove(path);
						onPathLocatedHandler(new PathControlEventArgs(path));
					}
				}
			}
		}

		public void ProcessEverything()
		{
			foreach (ProcessControl proc in images)
			{
				Process[] procs = Process.GetProcessesByName(proc.ExecutableFriendlyName);
				if (procs.Count() > 0)
				{
					proc.lastSeen = System.DateTime.Now;
				}

				foreach (Process item in procs)
				{
					try
					{
						Control(proc, item);
					}
					catch (Exception ex)
					{
						Log.Warn(System.String.Format("Failed to control '{0}' (pid:{1})", proc.Executable, item.Id));
						Console.Error.WriteLine(ex);
					}
				}
			}

			UpdatePathWatch();
		}

		// wish this wasn't necessary
		public static ProcessPriorityClass IntToPriority(int priority)
		{
			switch (priority)
			{
				case 0:
					return ProcessPriorityClass.Idle;
				case 1:
					return ProcessPriorityClass.BelowNormal;
				default:
					return ProcessPriorityClass.Normal;
				case 3:
					return ProcessPriorityClass.AboveNormal;
				case 4:
					return ProcessPriorityClass.High;
			}
		}

		// wish this wasn't necessary
		public static int PriorityToInt(ProcessPriorityClass priority)
		{
			switch (priority)
			{
				case ProcessPriorityClass.Idle:
					return 0;
				case ProcessPriorityClass.BelowNormal:
					return 1;
				case ProcessPriorityClass.Normal:
					return 2;
				case ProcessPriorityClass.AboveNormal:
					return 3;
				case ProcessPriorityClass.High:
					return 4;
				default:
					return 2; // normal
			}
		}

		SharpConfig.Configuration stats;
		public void loadConfig()
		{
			Log.Trace("Loading watchlist");
			cfg = TaskMaster.loadConfig(configfile);
			if (stats == null)
				stats = TaskMaster.loadConfig(statfile);

			foreach (SharpConfig.Section section in cfg.AsEnumerable())
			{
				Log.Trace("Section: "+section.Name);
				if (!section.Contains("image"))
				{
					// TODO: Deal with incorrect configuration lacking image
					Log.Warn(System.String.Format("'{0}' has no image.", section.Name));
					continue;
				}
				if (!(section.Contains("priority") || section.Contains("affinity")))
				{
					// TODO: Deal with incorrect configuration lacking image
					Log.Warn(System.String.Format("'{0}' has no priority or affinity.", section.Name));
					continue;
				}

				ProcessControl cnt = new ProcessControl(
					section.Name,
					section["image"].StringValue,
					section.Contains("priority") ? IntToPriority(section["priority"].IntValue) : ProcessPriorityClass.Normal,
					section.Contains("increase") ? section["increase"].BoolValue : true,
					section.Contains("affinity") ? section["affinity"].IntValue : 0,
					section.Contains("boost") ? section["boost"].BoolValue : true
				);
				//cnt.delay = section.Contains("delay") ? section["delay"].IntValue : 30; // TODO: Add centralized default delay
				//cnt.delayIncrement = section.Contains("delay increment") ? section["delay increment"].IntValue : 15; // TODO: Add centralized default increment
				if (stats.Contains(cnt.Executable))
				{
					cnt.Adjusts = stats[cnt.Executable].Contains("Adjusts") ? stats[cnt.Executable]["Adjusts"].IntValue : 0;
					cnt.lastSeen = stats[cnt.Executable].Contains("Last seen") ? stats[cnt.Executable]["Last seen"].DateTimeValue : System.DateTime.MinValue;
				}
				images.Add(cnt);
				execontrol.Add(new KeyValuePair<string,ProcessControl>(cnt.ExecutableFriendlyName, cnt));
				Log.Trace(System.String.Format("'{0}' added to monitoring.", section.Name));
			}
		}

		[Conditional("DEBUG")]
		void NewInstanceHandler_NativeError(Process process, int pid, System.ComponentModel.Win32Exception ex)
		{
			switch (ex.NativeErrorCode)
			{
				case 5:
					Log.Trace(System.String.Format("Access denied to '{0}' (pid:{1})", process.ProcessName, pid));
					break;
				case 299:
					Log.Trace(System.String.Format("Can not access 64 bit app '{0}' (pid:{1})", process.ProcessName, pid));
					break;
				default:
					Log.Trace(System.String.Format("Unknown failure with '{0}' (pid:{1}), error: {2}", process.ProcessName, pid, ex.NativeErrorCode));
					Log.Trace(ex);
					break;
			}
		}

		public void NewInstanceHandler(object sender, System.Management.EventArrivedEventArgs e)
		{
			System.Management.ManagementBaseObject targetInstance = (System.Management.ManagementBaseObject)e.NewEvent.Properties["TargetInstance"].Value;
			Process process;
			try
			{
				process = Process.GetProcessById(Convert.ToInt32(targetInstance.Properties["Handle"].Value));
			}
			catch (Exception)
			{
				Log.Error("Process exited before we had time to act on it.");
				return;
			}

			// since targetinstance actually has fuckall information, we need to extract it...

			string path;
			try
			{
				if (process.HasExited)
					return;
				path = process.MainModule.FileName; // this will cause win32exception of various types, we don't Really care which error it is
			}
			catch (System.ComponentModel.Win32Exception ex)
			{
				NewInstanceHandler_NativeError(process, process.Id, ex);
				// we can not touch this so we shouldn't even bother trying
				Log.Trace("Failed to access '{0}' (pid:{1})", process.ProcessName, process.Id);
				return;
			}

			// TODO: check proc.processName for presence in images.
			ProcessControl control;
			if (execontrol.TryGetValue(process.ProcessName, out control))
			{
				Log.Trace(System.String.Format("Delaying touching of '{0}' (pid:{1})", control.Executable, process.Id));
				System.Threading.Tasks.Task.Run(async () =>
				{
					await System.Threading.Tasks.Task.Delay(1200); // wait before we touch this, to let them do their own stuff in case they want to be smart
					Log.Trace(System.String.Format("Controlling '{0}' (pid:{1})", control.Executable, process.Id));
					Control(control, process);
				});
			}
			else if (pathwatch.Count > 0)
			{
				Log.Trace(pathwatch.Count + " paths to be tested against " + path);
				System.Threading.Tasks.Task.Run(async () =>
				{
					await System.Threading.Tasks.Task.Delay(1200); // waith 5 seconds before we do anything about it

					// TODO: This needs to be FASTER
					//Log.Debug("test: "+path);
					foreach (PathControl pc in pathwatch)
					{
						//Log.Debug("with: "+ pc.Path);
						if (path.StartsWith(pc.Path, StringComparison.InvariantCultureIgnoreCase)) // TODO: make this compatible with OSes that aren't case insensitive?
						{
							Log.Trace(pc.FriendlyName + " matched " + path);
							await System.Threading.Tasks.Task.Delay(3800); // wait a little more.
							pc.Touch(process, path);
							onPathAdjustHandler(new PathControlEventArgs(pc));
							break;
						}
						else
						{
							Log.Trace("Not matched: " + path);
						}
					}
				});
			}
			/* detected 
			else
			{
				Log.Trace("IGNORED: " + process.ProcessName + " = " + path);
			}
			*/
		}

		void LoadPathList()
		{
			pathcfg = TaskMaster.loadConfig(pathfile);
			foreach (SharpConfig.Section section in pathcfg.AsEnumerable())
			{
				string name = section.Name;
				string executable = section.Contains("image") ? section["image"].StringValue : null;
				string path = section.Contains("path") ? section["path"].StringValue : null;
				string subpath = section.Contains("subpath") ? section["subpath"].StringValue : null;
				bool increase = section.Contains("increase") ? section["increase"].BoolValue : true;
				ProcessPriorityClass priority = section.Contains("priority") ? IntToPriority(section["priority"].IntValue) : ProcessPriorityClass.Normal;

				// TODO: technically subpath should be enough...
				if (path == null && subpath == null)
				{
					Log.Warn(name + " does not have 'path' nor 'subpath'.");
					continue;
				}
				else if (path == null && executable == string.Empty)
				{
					Log.Warn(name + " has no 'path' nor 'image'.");
					continue;
				}
				else if (path != null && !System.IO.Directory.Exists(path))
				{
					Log.Warn(path + "(" + name + ") does not exist.");
					if (subpath == null && executable != null)
						continue; // we can't use this info to figure out new path
					path = null; // should be enough to construct new path
				}

				if (path != null && subpath != null && !path.Contains(subpath))
					Log.Warn(name + " is misconfigured: " + subpath + " not in " + path); // we don't really care

				PathControl pc = new PathControl(name, executable, priority, subpath, path);
				if (pc.Locate())
				{
					pathwatch.Add(pc);
					onPathLocatedHandler(new PathControlEventArgs(pc));
					Log.Info(name + " ("+pc.Path+") added to active watch list.");
					section["path"].StringValue = pc.Path;
					pathfilemodified = true;
				}
				else
				{
					pathinit.Add(pc);
					Log.Info(name + " ("+subpath+") added to init list.");
				}
			}
		}

		void InitProcessWatcher()
		{
			watcher = new System.Management.ManagementEventWatcher(@"\\.\root\CIMV2",
				"SELECT TargetInstance FROM __InstanceCreationEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_Process'");
			if (watcher != null)
			{
				watcher.EventArrived += NewInstanceHandler;
				watcher.Start();
				Log.Trace("New process watcher initialized.");
			}
			else
			{
				Log.Error("Failed to initialize new process watcher.");
			}
		}

		public List<PathControl> ActivePaths()
		{
			return pathwatch;
		}

		void ConfigureProcessors()
		{
			numCPUs = Environment.ProcessorCount;
			Log.Info(System.String.Format("Processor count: {0}", numCPUs));

			// is there really no easier way?
			System.Collections.BitArray bits = new System.Collections.BitArray(numCPUs);
			for (int i = 0; i < numCPUs; i++)
				bits.Set(i, true);
			int[] bint = new int[1];
			bits.CopyTo(bint, 0);
			allCPUsMask = bint[0];
			Log.Info(System.String.Format("Full mask: {0} ({1})", Convert.ToString(allCPUsMask, 2),allCPUsMask));
		}

		System.Management.ManagementEventWatcher watcher;
		SharpConfig.Configuration cfg;
		SharpConfig.Configuration pathcfg;
		const string configfile = "Apps.ini";
		const string pathfile = "Paths.ini";
		bool pathfilemodified = false;
		const string statfile = "Apps.Statistics.ini";
		// ctor, constructor
		public ProcessManager()
		{
			Log.Trace("Starting...");
			loadConfig();
			LoadPathList();
			InitProcessWatcher();
			ConfigureProcessors();
		}

		~ProcessManager()
		{
			if (pathfilemodified)
				TaskMaster.saveConfig(pathfile, pathcfg);
			
			watcher.Stop();
		}

		void saveStats()
		{
			Log.Trace("Saving stats...");
			if (stats == null)
				stats = TaskMaster.loadConfig(statfile);

			foreach (ProcessControl proc in images)
			{
				if (proc.Adjusts > 0)
					stats[proc.Executable]["Adjusts"].IntValue = proc.Adjusts;
				if (proc.lastSeen != System.DateTime.MinValue)
					stats[proc.Executable]["Last seen"].DateTimeValue = proc.lastSeen;
			}

			TaskMaster.saveConfig(statfile, stats);
		}

		public void Dispose()
		{
			Log.Trace("Disposing...");
			//TaskMaster.saveConfig(configfile, cfg); // we aren't modifyin it yet
			saveStats();
		}
	}
}

