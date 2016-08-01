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
using System.Security.Policy;
using System.Linq;
using System.Timers;
using System.ComponentModel;
using System.Configuration;

namespace TaskMaster
{
	using System.Diagnostics;

	/// <summary>
	/// Process control.
	/// </summary>
	public class ProcessControl
	{
		private static NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

		/// <summary>
		/// Human-readable friendly name for the process.
		/// </summary>
		public string FriendlyName = null;
		/// <summary>
		/// Executable filename related to this.
		/// </summary>
		public string Executable = null;
		/// <summary>
		/// Frienly executable name as required by various System.Process functions.
		/// Same as <see cref="T:TaskMaster.ProcessControl.Executable"/> but with the extension missing.
		/// </summary>
		public string ExecutableFriendlyName = null;
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
		//public int Delay = 60;
		//public int Recycle = 7;

		public DateTime lastSeen;
		public DateTime lastTouch;
		public int delay;
		public int effectiveDelay;
		public int delayIncrement;

		public ulong cycles;

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
			delay = 30;
			effectiveDelay = delay;
			delayIncrement = delay/2;
			cycles = 0;

			Adjusts = 0;
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
		private static NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

		public List<ProcessControl> images = new List<ProcessControl>();

		private System.Timers.Timer cycletimer = null;
		public event EventHandler onStart;
		public event EventHandler onEnd;
		public event EventHandler<ProcessEventArgs> onAdjust;

		int numCPUs = 1;
		int allCPUsMask = 1;

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

		private void onAdjustHandler(object sender, ProcessEventArgs e)
		{
			EventHandler<ProcessEventArgs> handler = onAdjust;
			if (handler != null)
				handler(this, e);
		}

		private void onStartHandler(object sender, EventArgs e)
		{
			EventHandler handler = onStart;
			if (handler != null)
				handler(this, e);
		}

		private void onEndHandler(object sender, EventArgs e)
		{
			EventHandler handler = onEnd;
			if (handler != null)
				handler(this, e);
		}

		public void Start()
		{
			cycletimer = new System.Timers.Timer();
			cycletimer.Elapsed += CycleEvent;
			cycletimer.Interval = 30000; // milliseconds
			cycletimer.Enabled = true;
		}

		public void Stop()
		{
			cycletimer.Elapsed -= CycleEvent;
			cycletimer.Enabled = false;
			cycletimer = null;
		}

		void CycleEvent(object sender, ElapsedEventArgs e)
		{
			Cycle();
		}

		public void Cycle()
		{
			Log.Trace("Cycling..."); // too spammy

			EventArgs e2 = new EventArgs();
			onStartHandler(this, e2);

			System.DateTime now = System.DateTime.Now;

			//System.Console.WriteLine("ProcMan: Cycle start");
			foreach (ProcessControl proc in images)
			{
				//System.Console.WriteLine("ProcMan: exe: {0}", proc.Executable);
				if ((now - proc.lastSeen).TotalSeconds < proc.effectiveDelay)
				{
					Log.Trace(System.String.Format("{0} being skipped, last seen too recently ({1}s ago; {2}s delay).",
							                       proc.Executable, (now - proc.lastSeen).TotalSeconds, proc.effectiveDelay));
					continue;
				}

				Process[] procs = Process.GetProcessesByName(proc.ExecutableFriendlyName);
				if (procs.Count() > 0)
					proc.lastSeen = now;

				foreach (Process item in procs)
				{
					bool Affinity = false;
					bool Priority = false;
					bool Boost = false;
					if ((item.PriorityClass > proc.Priority && proc.Increase) || (item.PriorityClass < proc.Priority))
					{
						item.PriorityClass = proc.Priority;
						Priority = true;
					}
					if (item.ProcessorAffinity != proc.Affinity) // FIXME: 0 and all cores selected should match
					{
						if (proc.Affinity == IntPtr.Zero && item.ProcessorAffinity.ToInt32() == allCPUsMask)
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
								item.ProcessorAffinity = proc.Affinity;
								Affinity = true;
							}
							catch (Win32Exception)
							{
								Log.Warn(System.String.Format("Couldn't modify process ({0}, #{1}) affinity.", proc.Executable, item.Id));
							}
						}
					}
					if (item.PriorityBoostEnabled != proc.Boost)
					{
						item.PriorityBoostEnabled = proc.Boost;
						Boost = true;
					}
					if (Priority || Affinity || Boost)
					{
						proc.Adjusts += 1;
						ProcessEventArgs e = new ProcessEventArgs();
						e.Affinity = Affinity;
						e.Priority = Priority;
						e.Boost = Boost;
						e.control = proc;
						e.process = item;

						proc.lastTouch = now;

						// TODO: Is StringBuilder fast enough for this to be good idea?
						System.Text.StringBuilder ls = new System.Text.StringBuilder();
						ls.Append("(").Append(proc.Executable).Append(") =");
						if (Priority)
							ls.Append(" Priority(").Append(proc.Priority).Append(")");
						if (Affinity)
							ls.Append(" Afffinity(").Append(proc.Affinity).Append(")");
						if (Boost)
							ls.Append(" Boost(").Append(Boost).Append(")");
						#if DEBUG
						ls.Append("; Start: ").Append(item.StartTime); // when the process was started
						#endif
						Log.Info(ls.ToString());
						//Log.Info(System.String.Format("{0} (#{1}) = Priority({2}), Mask({3}), Boost({4}) - Start: {5}",
						  //                            proc.Executable, item.Id, Priority, Affinity, Boost, item.StartTime));
						onAdjustHandler(this, e);
					}
				}

				if (proc.cycles < 2)
					proc.effectiveDelay = 2; // TODO: Implement fast recycle
				else if (proc.cycles == 2)
					proc.effectiveDelay = proc.delay;
				else if (proc.cycles < 6)
					proc.effectiveDelay = proc.effectiveDelay + proc.delayIncrement;
				proc.cycles += 1;
			}

			//System.Console.WriteLine("ProcMan: Cycle end.");
			onEndHandler(this, e2);
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
					return 2;
			}
		}

		SharpConfig.Configuration stats = null;
		public void loadConfig()
		{
			Log.Trace("Loading configuration.");
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
				cnt.delay = section.Contains("delay") ? section["delay"].IntValue : 30; // TODO: Add centralized default delay
				cnt.delayIncrement = section.Contains("delay increment") ? section["delay increment"].IntValue : 15; // TODO: Add centralized default increment
				if (stats.Contains(cnt.Executable))
				{
					cnt.Adjusts = stats[cnt.Executable].Contains("Adjusts") ? stats[cnt.Executable]["Adjusts"].IntValue : 0;
					cnt.lastSeen = stats[cnt.Executable].Contains("Last seen") ? stats[cnt.Executable]["Last seen"].DateTimeValue : System.DateTime.MinValue;
				}
				images.Add(cnt);
				Log.Trace(System.String.Format("'{0}' added to monitoring.", section.Name));
			}
		}

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

