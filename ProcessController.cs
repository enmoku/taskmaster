//
// ProcessController.cs
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

using System;
using Serilog;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Linq;

namespace TaskMaster
{
	/// <summary>
	/// Process controller.
	/// </summary>
	public class ProcessController : IDisposable
	{
		/// <summary>
		/// Human-readable friendly name for the process.
		/// </summary>
		public string FriendlyName { get; set; } = null;

		string p_Executable = null;
		/// <summary>
		/// Executable filename related to this.
		/// </summary>
		public string Executable
		{
			get
			{
				return p_Executable;
			}
			set
			{
				ExecutableFriendlyName = System.IO.Path.GetFileNameWithoutExtension(p_Executable = value);
			}
		}

		public string Subpath { get; set; } = null;
		public string Path { get; set; } = null;

		public string[] IgnoreList { get; set; } = null;

		/*
		/// <summary>
		/// Determines if the process I/O is to be set background.
		/// </summary>
		/// <value><c>true</c> if background process; otherwise, <c>false</c>.</value>
		public bool BackgroundIO { get; set; } = false;
		*/

		/// <summary>
		/// Determines if the values are only maintained when the app is in foreground.
		/// </summary>
		/// <value><c>true</c> if foreground; otherwise, <c>false</c>.</value>
		public bool ForegroundOnly { get; set; } = false;

		/// <summary>
		/// Target priority class for the process.
		/// </summary>
		public System.Diagnostics.ProcessPriorityClass Priority = System.Diagnostics.ProcessPriorityClass.Normal;

		/// <summary>
		/// CPU core affinity.
		/// </summary>
		public IntPtr Affinity = new IntPtr(ProcessManager.allCPUsMask);

		/// <summary>
		/// The power plan.
		/// </summary>
		public PowerManager.PowerMode PowerPlan = PowerManager.PowerMode.Undefined;

		/// <summary>
		/// Allow priority decrease.
		/// </summary>
		public bool Decrease { get; set; } = true;
		/// <summary>
		/// Allow priority increase.
		/// </summary>
		public bool Increase { get; set; } = true;

		int p_Recheck = 0;
		public int Recheck
		{
			get
			{
				return p_Recheck;
			}
			set
			{
				p_Recheck = value.Min(0).Constrain(0, 300);
			}
		}

		public bool AllowPaging = false;

		int p_Rescan;
		/// <summary>
		/// Delay in minutes before we try to use Scan again.
		/// </summary>
		public int Rescan
		{
			get { return p_Rescan; }
			set { p_Rescan = value.Constrain(0, 300); }
		}

		/// <summary>
		/// Frienly executable name as required by various System.Process functions.
		/// Same as <see cref="T:TaskMaster.ProcessControl.Executable"/> but with the extension missing.
		/// </summary>
		public string ExecutableFriendlyName { get; set; } = null;

		public ProcessController(string name, ProcessPriorityClass priority, int affinity, string path = null)
		{
			FriendlyName = name;
			//Executable = executable;
			Priority = priority;
			if (affinity != ProcessManager.allCPUsMask)
				Affinity = new IntPtr(affinity);

			if (!string.IsNullOrEmpty(path))
			{
				Path = path;
				if (!System.IO.Directory.Exists(Path))
					Log.Warning("{FriendlyName} configured path {Path} does not exist!", FriendlyName, path);

				if (Path != null)
				{
					Log.Information("[{FriendlyName}] Watched in: {Path} [Priority: {Priority}, Mask: {Mask}]",
									FriendlyName, Path, Priority, Affinity.ToInt32());
				}
			}
		}

		HashSet<int> PausedIds = new HashSet<int>();

		public bool BackgroundPowerdown { get; set; } = true;
		public ProcessPriorityClass BackgroundPriority { get; set; } = ProcessPriorityClass.Normal;
		/// <summary>
		/// Pause the specified foreground process.
		/// </summary>
		public void Quell(BasicProcessInfo info)
		{
			if (PausedIds.Contains(info.Id)) return;
			// throw new InvalidOperationException(string.Format("{0} already paused", info.Name));

			if (TaskMaster.DebugForeground && TaskMaster.Trace)
				Log.Debug("[{Name}] Quelling {Exec} (#{Pid})", FriendlyName, info.Name, info.Id);

			//PausedState.Affinity = Affinity;
			//PausedState.Priority = Priority;
			//PausedState.PowerMode = PowerPlan;

			try
			{
				info.Process.PriorityClass = BackgroundPriority;
			}
			catch
			{
			}
			//info.Process.ProcessorAffinity = OriginalState.Affinity;

			if (TaskMaster.PowerManagerEnabled)
				if (PowerPlan != PowerManager.PowerMode.Undefined && BackgroundPowerdown)
					TaskMaster.powermanager.RestoreMode(info.Process);

			if (TaskMaster.DebugForeground)
				Log.Information("[{FriendlyName}] {Exec} (#{Pid}) priority reduced: {Current}→{Paused} [Background]",
					FriendlyName, info.Name, info.Id, Priority, BackgroundPriority);

			PausedIds.Add(info.Id);
		}

		public bool isPaused(BasicProcessInfo info)
		{
			return PausedIds.Contains(info.Id);
		}

		public void Resume(BasicProcessInfo info)
		{
			if (!PausedIds.Contains(info.Id)) return;
			//throw new InvalidOperationException(string.Format("{0} not paused", info.Name));

			if (info.Process.PriorityClass.ToInt32() != Priority.ToInt32())
			{
				try
				{
					info.Process.PriorityClass = Priority;
					if (TaskMaster.DebugForeground)
						Log.Debug("[{FriendlyName}] {Exec} (#{Pid}) priority restored: {Paused}→{Restored} [Foreground]",
										FriendlyName, info.Name, info.Id, BackgroundPriority, Priority);
				}
				catch { }
			}
			//PausedState.Priority = Priority;
			//PausedState.PowerMode = PowerPlan;

			PausedIds.Remove(info.Id);
		}

		/// <summary>
		/// How many times we've touched associated processes.
		/// </summary>
		public int Adjusts { get; set; } = 0;
		/// <summary>
		/// Last seen any associated process.
		/// </summary>
		public DateTime LastSeen { get; set; } = DateTime.MinValue;
		/// <summary>
		/// Last modified any associated process.
		/// </summary>
		public DateTime LastTouch { get; set; } = DateTime.MinValue;

		/*
		public bool Children = false;
		public ProcessPriorityClass ChildPriority = ProcessPriorityClass.Normal;
		public bool ChildPriorityReduction = false;
		*/

		// -----------------------------------------------

		void ProcessEnd(object sender, EventArgs ev)
		{

		}

		// -----------------------------------------------

		protected bool setPowerPlan(BasicProcessInfo info)
		{
			if (!TaskMaster.PowerManagerEnabled) return false;

			if (PowerPlan != PowerManager.PowerMode.Undefined)
			{
				TaskMaster.powermanager.SaveMode();

				info.Flags |= (int)ProcessFlags.PowerWait;
				TaskMaster.processmanager.WaitForExit(info); // need nicer way to signal this

				return TaskMaster.powermanager.ForceMode(PowerPlan, info.Process);
			}
			return false;
		}

		[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		public static extern bool SetPriorityClass(IntPtr handle, uint priorityClass);

		public enum PriorityTypes
		{
			ABOVE_NORMAL_PRIORITY_CLASS = 0x00008000,
			BELOW_NORMAL_PRIORITY_CLASS = 0x00004000,
			HIGH_PRIORITY_CLASS = 0x00000080,
			IDLE_PRIORITY_CLASS = 0x00000040,
			NORMAL_PRIORITY_CLASS = 0x00000020,
			PROCESS_MODE_BACKGROUND_BEGIN = 0x00100000,
			PROCESS_MODE_BACKGROUND_END = 0x00200000,
			REALTIME_PRIORITY_CLASS = 0x00000100
		}

		/*
		// Windows doesn't allow setting this for other processes
		bool SetBackground(Process process)
		{
			return SetIOPriority(process, PriorityTypes.PROCESS_MODE_BACKGROUND_BEGIN);
		}
		*/

		public static bool SetIOPriority(Process process, PriorityTypes priority)
		{
			try
			{
				bool rv = SetPriorityClass(process.Handle, (uint)priority);
				return rv;
			}
			catch { /* NOP, don't care */ }
			return false;
		}

		// TODO: Deal with combo path+exec
		public ProcessState Touch(BasicProcessInfo info, bool schedule_next = false, bool recheck = false, bool foreground = true)
		{
			Debug.Assert(info.Process != null, "ProcessController.Touch given null process.");
			Debug.Assert(info.Id > 4, "ProcessController.Touch given invalid process ID");
			Debug.Assert(!string.IsNullOrEmpty(info.Name), "ProcessController.Touch given empty process name.");

			if (info.Process == null || info.Id <= 4 || string.IsNullOrEmpty(info.Name))
			{
				Log.Fatal("ProcessController.Touch({Name},#{Pid}) received invalid arguments.", info.Name, info.Id);
				throw new ArgumentNullException();
				//return ProcessState.Invalid;
			}

			if (recheck)
			{
				try
				{
					info.Process.Refresh();
				}
				catch
				{
					Log.Warning("[{FriendlyName}] {Exec} (#{Pid}) failed to refresh.", FriendlyName, info.Name, info.Id);
				}
			}

			/*
			ProcessPriorityClass oldPriority = process.PriorityClass;

			bool rv = process.SetLimitedPriority(Priority, Increase, Decrease);
			LastSeen = DateTime.Now;
			Adjusts += 1;
			*/

			try
			{
				if (info.Process.HasExited)
				{
					if (TaskMaster.DebugProcesses)
						Log.Debug("[{FriendlyName}] {ProcessName} (#{ProcessID}) has already exited.", FriendlyName, info.Name, info.Id);
					return ProcessState.Invalid;
				}
			}
			catch (Win32Exception ex)
			{
				if (ex.NativeErrorCode != 5)
					Log.Warning("[{FriendlyName}] {ProcessName} (#{ProcessID}) access failure determining if it's still running.", FriendlyName, info.Name, info.Id);
				return ProcessState.AccessDenied; // we don't care what this error is exactly
			}

			if (TaskMaster.Trace) Log.Verbose("[{FriendlyName}] Touching: {ExecutableName} (#{ProcessID})", FriendlyName, info.Name, info.Id);

			ProcessState rv = ProcessState.Invalid;

			if (IgnoreList != null && IgnoreList.Contains(info.Name, StringComparer.InvariantCultureIgnoreCase))
			{
				if (TaskMaster.ShowInaction && TaskMaster.DebugProcesses)
					Log.Debug("[{FriendlyName}] {Exec} (#{ProcessID}) ignored due to user defined rule.", FriendlyName, info.Name, info.Id);
				return ProcessState.AccessDenied;
			}

			bool denyChange = ProcessManager.ProtectedProcessName(info.Name);
			if (denyChange)
				if (TaskMaster.ShowInaction && TaskMaster.DebugProcesses)
					Log.Debug("[{FriendlyName}] {ProcessName} (#{ProcessID}) in protected list, limiting tampering.", FriendlyName, info.Name, info.Id);

			bool mAffinity = false, mPriority = false, mPower = false, mBGIO = false, modified = false;
			LastSeen = DateTime.Now;

			IntPtr oldAffinity = IntPtr.Zero;
			ProcessPriorityClass oldPriority = ProcessPriorityClass.RealTime;
			try
			{
				oldAffinity = info.Process.ProcessorAffinity;
				oldPriority = info.Process.PriorityClass;
			}
			catch { /* NOP, don't care */ }

			if (!denyChange)
			{
				if (!foreground && ForegroundOnly)
				{
					if (TaskMaster.DebugForeground)
						Log.Debug("{Exec} (#{Pid}) not in foreground, not prioritizing.", info.Name, info.Id);

					if (!PausedIds.Contains(info.Id))
						PausedIds.Add(info.Id);
					// NOP
				}
				else
				{
					try
					{
						if (info.Process.SetLimitedPriority(Priority, Increase, Decrease))
							modified = mPriority = true;
					}
					catch
					{
						Log.Warning("[{FriendlyName}] {Exec} (#{Pid}) failed to set process priority.", FriendlyName, info.Name, info.Id);
						// NOP
					}
				}
			}
			else
			{
				if (TaskMaster.ShowInaction)
					Log.Verbose("{Exec} (#{Pid}) protected.", info.Name, info.Id);
			}

			try
			{
				if (info.Process.ProcessorAffinity.ToInt32() != Affinity.ToInt32())
				{
					info.Process.ProcessorAffinity = Affinity;
					modified = mAffinity = true;
					//Log.Verbose("Affinity for '{ExecutableName}' (#{ProcessID}) set: {OldAffinity} → {NewAffinity}.",
					//execname, pid, process.ProcessorAffinity.ToInt32(), Affinity.ToInt32());
				}
				else
				{
					//Log.Verbose("Affinity for '{ExecutableName}' (#{ProcessID}) is ALREADY set: {OldAffinity} → {NewAffinity}.",
					//			info.Name, info.Id, info.Process.ProcessorAffinity.ToInt32(), Affinity.ToInt32());
				}
			}
			catch
			{
				Log.Warning("[{FriendlyName}] {Exec} (#{Pid}) failed to set process affinity.", FriendlyName, info.Name, info.Id);
			}

			/*
			if (BackgroundIO)
			{
				try
				{
					//Process.EnterDebugMode(); // doesn't help

					mBGIO = SetBackground(info.Process); // doesn't work, can only be done to current process

					//Process.LeaveDebugMode();
				}
				catch
				{
					// NOP, don't caree
				}
				if (mBGIO == false)
					Log.Warning("[{FriendlyName}] {Exec} (#{Pid}) Failed to set background I/O mode.", FriendlyName, info.Name, info.Id);
			}
			*/

			PowerManager.PowerMode oldPP = PowerManager.PowerMode.Undefined;
			if (TaskMaster.PowerManagerEnabled)
			{
				oldPP = TaskMaster.powermanager.CurrentMode;
				setPowerPlan(info);
				mPower = (oldPP != TaskMaster.powermanager.CurrentMode);
			}

			var sbs = new System.Text.StringBuilder();
			sbs.Append("[").Append(FriendlyName).Append("] ").Append(info.Name).Append(" (#").Append(info.Id).Append(")");

			if (mPriority || mAffinity)
				Adjusts += 1; // don't increment on power changes

			if (modified)
			{
				if (mPriority)
				{
					try
					{
						info.Process.Refresh();
						ProcessPriorityClass newPriority = info.Process.PriorityClass;
						if (newPriority.ToInt32() != Priority.ToInt32())
						{
							Log.Warning("[{FriendlyName}] {Exe} (#{Pid}) Post-mortem of modification: FAILURE (Expected: {TgPrio}, Detected: {CurPrio}).",
										FriendlyName, info.Name, info.Id, Priority.ToString(), newPriority.ToString());
						}
					}
					catch
					{
						// NOP, don't caree
					}
				}

				LastTouch = DateTime.Now;

				rv = ProcessState.Modified;
			}

			sbs.Append("; Priority: ");
			if (mPriority)
				sbs.Append(oldPriority.ToString()).Append(" → ");
			sbs.Append(Priority.ToString()).Append("; Affinity: ");
			if (mAffinity)
				sbs.Append(oldAffinity.ToInt32()).Append(" → ");
			sbs.Append(Affinity.ToInt32());
			if (mPower)
				sbs.Append(string.Format(" [Power Mode: {0}]", PowerPlan.ToString()));
			if (mBGIO)
				sbs.Append(" [BgIO!]");

			if (modified)
			{
				Log.Information(sbs.ToString());
			}
			else
			{
				//if (DateTime.Now - LastSeen
				sbs.Append(" – looks OK, not touched.");
				if (TaskMaster.ShowInaction && TaskMaster.DebugProcesses)
					Log.Debug(sbs.ToString());
				//else
				//	Log.Verbose(sbs.ToString());
				rv = ProcessState.OK;
			}

			if (modified)
				onTouch?.Invoke(this, new ProcessEventArgs { Control = this, Info = info });

			if (schedule_next)
				TryScan();

			if (Recheck > 0 && recheck == false)
			{
				Task.Run(new Func<Task>(async () =>
				{
					await Task.Delay(Math.Max(Recheck, 5) * 1000);

					if (TaskMaster.DebugProcesses)
						Log.Debug("[{FriendlyName}] {Process} (#{PID}) rechecking", FriendlyName, info.Name, info.Id);

					try
					{
						if (!info.Process.HasExited)
							Touch(info, schedule_next: false, recheck: true);
						else
						{
							if (TaskMaster.Trace) Log.Verbose("[{FriendlyName}] {Process} (#{PID}) is gone yo.", FriendlyName, info.Name, info.Id);
						}
					}
					catch (Exception ex)
					{
						Log.Warning("[{FriendlyName}] {Process} (#{PID}) – something bad happened.", FriendlyName, info.Name, info.Id);
						Logging.Stacktrace(ex);
						return; //throw; // would throw but this is async function
					}
				}));
			}

			return rv;
		}

		/// <summary>
		/// Synchronous call to RescanWithSchedule()
		/// </summary>
		public int TryScan()
		{
			Task.Run(new Func<Task>(async () =>
			{
				await Task.Yield();
				try
				{
					RescanWithSchedule();
				}
				catch (Exception ex)
				{
					Log.Warning("Error in scheduled rescan.");
					Logging.Stacktrace(ex);
					return; //throw; // would throw, but this is async function
				}
			}));

			return Convert.ToInt32((LastScan.AddMinutes(Rescan) - DateTime.Now).TotalMinutes);
		}

		DateTime LastScan = DateTime.MinValue;

		/// <summary>
		/// Atomic lock for RescanWithSchedule()
		/// </summary>
		int ScheduledScan = 0;

		void RescanWithSchedule()
		{
			double n = (DateTime.Now - LastScan).TotalMinutes;
			//Log.Trace(string.Format("[{0}] last scan {1:N1} minute(s) ago.", FriendlyName, n));
			if (Rescan > 0 && n >= Rescan)
			{
				if (System.Threading.Interlocked.CompareExchange(ref ScheduledScan, 1, 0) == 1)
					return;

				if (TaskMaster.DebugProcesses)
					Log.Debug("[{FriendlyName}] Rescan initiating.", FriendlyName);

				Scan();

				ScheduledScan = 0;
			}
			//else
			//	Log.Verbose("[{FriendlyName}] Scan too recent, ignoring.", FriendlyName); // this is too spammy.
		}

		public void Scan()
		{
			if (ExecutableFriendlyName == null) return;

			Process[] procs;
			try
			{
				procs = Process.GetProcessesByName(ExecutableFriendlyName); // should be case insensitive by default
			}
			catch // name not found
			{
				if (TaskMaster.Trace) Log.Verbose("{FriendlyName} is not running", ExecutableFriendlyName);
				return;
			}

			//LastSeen = LastScan;
			LastScan = DateTime.Now;

			if (procs.Length == 0) return;

			if (TaskMaster.DebugProcesses)
				Log.Debug("[{FriendlyName}] Scanning found {ProcessInstances} instance(s)", FriendlyName, procs.Length);

			int tc = 0;
			foreach (Process process in procs)
			{
				string name;
				int pid;
				try
				{
					name = process.ProcessName;
					pid = process.Id;
				}
				catch
				{
					continue; // shouldn't happen
				}
				if (Touch(new BasicProcessInfo { Name = name, Id = pid, Process = process, Path = null }) == ProcessState.Modified)
					tc++;
			}

			if (tc > 0)
			{
				if (TaskMaster.DebugProcesses)
					Log.Verbose("[{ProcessFriendlyName}] Scan modified {ModifiedInstances} out of {ProcessInstances} instance(s)",
								FriendlyName, tc, procs.Length);
			}
		}

		public bool Locate()
		{
			if (Path != null)
			{
				if (System.IO.Directory.Exists(Path))
					return true;
				return false;
			}

			if (Subpath == null) return false;

			Process process;
			try
			{
				process = Process.GetProcessesByName(ExecutableFriendlyName)[0]; // not great thing, but should be good enough.
			}
			catch
			{
				if (TaskMaster.Trace) Log.Verbose("{FriendlyName} not running", ExecutableFriendlyName);
				return false;
			}

			if (TaskMaster.Trace) Log.Verbose("[{FriendlyName}] Watched item '{Item}' encountered.", FriendlyName, ExecutableFriendlyName);

			try
			{
				string corepath = System.IO.Path.GetDirectoryName(process.MainModule.FileName);
				string fullpath = System.IO.Path.Combine(corepath, Subpath);
				if (System.IO.Directory.Exists(fullpath))
				{
					Path = fullpath;
					Log.Debug("[{FriendlyName}] Bound to: {Path}", FriendlyName, Path);

					onLocate?.Invoke(this, new PathControlEventArgs());

					return true;
				}
			}
			catch (Exception ex)
			{
				Log.Warning("[{FriendlyName}] Access failure while determining path.");
				Logging.Stacktrace(ex);
				//throw; // no point
			}

			return false;
		}

		public static event EventHandler<ProcessEventArgs> onTouch;
		public static event EventHandler<PathControlEventArgs> onLocate;

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		bool disposed; // = false;
		protected virtual void Dispose(bool disposing)
		{
			if (disposed) return;

			if (disposing)
			{
				if (TaskMaster.Trace) Log.Verbose("Disposing process controller [{FriendlyName}]", FriendlyName);
			}

			disposed = true;
		}
	}

	public class PathControlEventArgs : EventArgs
	{
	}

	public class ProcessEventArgs : EventArgs
	{
		public ProcessController Control { get; set; }
		public BasicProcessInfo Info;
		public enum ProcessState
		{
			Starting,
			Found,
			Reduced,
			Restored,
			Cancel,
			Exiting,
			Undefined
		}
		public ProcessState State;
	}
}
