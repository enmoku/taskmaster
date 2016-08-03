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
				Log.Info(System.String.Format("'{0}' watched in: {1}", FriendlyName, Path));
			}
			else
			{
				Log.Info(System.String.Format("'{0}' matching for '{1}'", Executable, subpath));
			}
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

		public List<ProcessControl> images = new List<ProcessControl>();
		public List<PathControl> pathwatch = new List<PathControl>();
		public List<PathControl> pathinit = new List<PathControl>();
		IDictionary<string, ProcessControl> execontrol = new Dictionary<string,ProcessControl>();

		System.Timers.Timer slowwatchtimer;
		public event EventHandler<ProcessEventArgs> onAdjust;

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

		void onAdjustHandler(object sender, ProcessEventArgs e)
		{
			EventHandler<ProcessEventArgs> handler = onAdjust;
			if (handler != null)
				handler(this, e);
		}

		public void Start()
		{
			slowwatchtimer = new System.Timers.Timer();
			slowwatchtimer.Elapsed += SlowWatchEvent;
			slowwatchtimer.Interval = 1000 * 60 * 10; // milliseconds, 10 minutes
			slowwatchtimer.Enabled = true;
		}

		public void Stop()
		{
			slowwatchtimer.Elapsed -= SlowWatchEvent;
			slowwatchtimer.Enabled = false;
			slowwatchtimer = null;
		}

		void SlowWatchEvent(object sender, ElapsedEventArgs e)
		{
			SlowWatch();
		}

		public async void ControlPath(PathControl control, Process process,  string path)
		{
			string name = System.IO.Path.GetFileName(path);
			ProcessPriorityClass oldPriority = process.PriorityClass;
			if (process.PriorityClass < control.Priority) // TODO: possibly allow decreasing priority, but for this 
			{
				process.PriorityClass = control.Priority;
				Log.Info(System.String.Format("{4} // '{0}' (pid:{1}); priority: {2} -> {3}", name, process.Id, oldPriority, control.Priority, control.FriendlyName));
			}
			else
			{
				Log.Debug(System.String.Format("'{0}' (pid:{1}); looks OK, not touched.", name, process.Id));
			}
		}

		public async void Control(ProcessControl control, Process process)
		{
			if (process.HasExited)
			{
				Log.Debug(System.String.Format("'{0}' (pid:{1}) has already exited.", control.Executable, process.Id));
				return;
			}
			process.Refresh();
			Log.Trace(System.String.Format("{0} ({1}, pid:{2})", control.FriendlyName, control.Executable, process.Id));
			bool Affinity = false;
			IntPtr oldAffinity = process.ProcessorAffinity;
			bool Priority = false;
			ProcessPriorityClass oldPriority = process.PriorityClass;
			bool Boost = false;
			if ((process.PriorityClass < control.Priority && control.Increase) || (process.PriorityClass > control.Priority))
			{
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
				ls.Append(control.Executable).Append(" (pid:").Append(process.Id).Append(") =");
				//ls.Append("(").Append(control.Executable).Append(") =");
				if (Priority)
					ls.Append(" Priority(").Append(oldPriority).Append(" -> ").Append(control.Priority).Append(")");
				if (Affinity)
					ls.Append(" Afffinity(").Append(oldAffinity).Append(" -> ").Append(control.Affinity).Append(")");
				if (Boost)
					ls.Append(" Boost(").Append(Boost).Append(")");
#if DEBUG
				//ls.Append("; Start: ").Append(process.StartTime); // when the process was started
#endif
				Log.Info(ls.ToString());
				//Log.Info(System.String.Format("{0} (#{1}) = Priority({2}), Mask({3}), Boost({4}) - Start: {5}",
				//                            proc.Executable, item.Id, Priority, Affinity, Boost, item.StartTime));
				onAdjustHandler(this, e);
			}
			else
			{
				Log.Trace(System.String.Format("'{0}' (pid:{1}) seems to be OK already.", control.Executable, process.Id));
			}
		}

		public void SlowWatch()
		{
			foreach (ProcessControl proc in images)
			{
				Process[] procs = Process.GetProcessesByName(proc.ExecutableFriendlyName);
				if (procs.Count() > 0)
				{
					proc.lastSeen = System.DateTime.Now;
					Log.Trace(System.String.Format("Cycling '{0}' - found: {1}", proc.Executable, procs.Count()));
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

			if (pathinit.Count > 0)
			{
				foreach (PathControl path in pathinit.ToArray())
				{
					Process proc = Process.GetProcessesByName(path.ExecutableFriendlyName).First();
					if (proc == null)
						continue;
					Log.Trace("Watched item '"+path.FriendlyName+"' encountered.");
					try
					{
						string corepath = System.IO.Path.GetDirectoryName(proc.MainModule.FileName);
						string fullpath = System.IO.Path.Combine(corepath, path.Subpath);
						if (System.IO.Directory.Exists(fullpath))
						{
							path.Path = fullpath;
							Log.Debug(System.String.Format("'{0}' bound to: {1}", path.FriendlyName, path.Path));
							pathwatch.Add(path);
							pathinit.Remove(path);
						}
						/*
						else
						{
							Log.Warn("Not found! " + fullpath);
						}
						*/
					}
					catch (Exception ex)
					{
						Log.Warn(System.String.Format("Failed to init path for '{0}'", path.FriendlyName));
						Console.Error.WriteLine(ex);
					}
				}
			}
		}

		// wish this wasn't necessary
		ProcessPriorityClass IntToPriority(int priority)
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
		int PriorityToInt(ProcessPriorityClass priority)
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
			Log.Info("Loading watchlist");
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

		public void NewInstanceHandler(object sender, System.Management.EventArrivedEventArgs e)
		{
			System.Management.ManagementBaseObject targetInstance = (System.Management.ManagementBaseObject)e.NewEvent.Properties["TargetInstance"].Value;
			int pid = System.Convert.ToInt32(targetInstance.Properties["Handle"].Value);
			// since targetinstance actually has fuckall information, we need to extract it...
			System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(pid);

			string path;
			try
			{
				path = process.MainModule.FileName; // this will cause win32exception of various types, we don't Really care which error it is
			}
			catch (System.ComponentModel.Win32Exception ex)
			{
				#if DEBUG
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
				#endif
				// we can not touch this so we shouldn't even bother trying
				Log.Trace("Failed to access '{0}' (pid:{1})", process.ProcessName, pid);
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
						if (path.ToLowerInvariant().StartsWith(pc.Path.ToLowerInvariant())) // TODO: make this compatible with OSes that aren't case insensitive?
						{
							Log.Trace(pc.FriendlyName + " matched " + path);
							await System.Threading.Tasks.Task.Delay(3800); // wait a little more.
							ControlPath(pc, process, path);
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

		System.Management.ManagementEventWatcher watcher;
		SharpConfig.Configuration cfg;
		private const string configfile = "Apps.ini";
		private const string statfile = "Apps.Statistics.ini";
		// ctor, constructor
		public ProcessManager()
		{
			Log.Trace("Starting...");
			loadConfig();

			numCPUs = Environment.ProcessorCount;
			Log.Info(System.String.Format("Number of CPUs: {0}", numCPUs));

			// is there really no easier way?
			System.Collections.BitArray bits = new System.Collections.BitArray(numCPUs);
			for (int i = 0; i < numCPUs; i++)
				bits.Set(i, true);
			int[] bint = new int[1];
			bits.CopyTo(bint, 0);
			allCPUsMask = bint[0];
			Log.Info(System.String.Format("All CPUs mask: {0}", Convert.ToString(allCPUsMask, 2)));

			#if DEBUG
			{
				PathControl pc = new PathControl(
					"Steam CDN",
					"Steam.exe",
					ProcessPriorityClass.Normal,
					"steamapps"
				);
				pathinit.Add(pc);
			}
			#endif

			watcher = new System.Management.ManagementEventWatcher(@"\\.\root\CIMV2",
				"SELECT TargetInstance FROM __InstanceCreationEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_Process'");
			if (watcher != null)
			{
				watcher.EventArrived += NewInstanceHandler;
				watcher.Start();
				Log.Info("Instance creation watcher started.");
			}
			else
			{
				Log.Error("Failed to register instance creation watcher.");
			}
		}

		~ProcessManager()
		{
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

