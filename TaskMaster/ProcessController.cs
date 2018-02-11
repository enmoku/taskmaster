//
// ProcessController.cs
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
using Serilog.Sinks.File;
using System.Threading;

namespace TaskMaster
{
	using System;
	using Serilog;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.Runtime.InteropServices;
	using System.ComponentModel;
	using System.Threading.Tasks;
	using System.Linq;

	/// <summary>
	/// Process controller.
	/// </summary>
	public class ProcessController
	{
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
					Log.Information("'{ProcessName}' watched in: {Path} [Priority: {Priority}, Mask: {Mask}]",
									FriendlyName, Path, Priority, Affinity.ToInt32());
				}
			}
		}

		string p_executable = null;
		/// <summary>
		/// Executable filename related to this.
		/// </summary>
		public string Executable
		{
			get
			{
				return p_executable;
			}
			set
			{
				p_executable = value;
				ExecutableFriendlyName = System.IO.Path.GetFileNameWithoutExtension(value);
			}
		}

		/// <summary>
		/// Frienly executable name as required by various System.Process functions.
		/// Same as <see cref="T:TaskMaster.ProcessControl.Executable"/> but with the extension missing.
		/// </summary>
		public string ExecutableFriendlyName { get; set; } = null;

		/// <summary>
		/// Human-readable friendly name for the process.
		/// </summary>
		public string FriendlyName { get; set; } = null;

		public string Subpath { get; set; } = null;
		public string Path { get; set; } = null;

		public string[] IgnoreList { get; set; } = null;

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

		/// <summary>
		/// Determines if the process I/O is to be set background.
		/// </summary>
		/// <value><c>true</c> if background process; otherwise, <c>false</c>.</value>
		public bool BackgroundIO { get; set; } = false;

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

		int p_recheck = 0;
		public int Recheck
		{
			get
			{
				return p_recheck;
			}
			set
			{
				p_recheck = value >= 0 ? value : 0;
			}
		}

		public bool Children = false;
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

		// -----------------------------------------------

		static object waitingExitLock = new object();
		static List<Process> waitingExit = new List<Process>(3);
		static HashSet<int> waitingExitPids = new HashSet<int>();

		protected bool setPowerPlan(Process process)
		{
			if (!TaskMaster.PowerManagerEnabled) return false;

			if (PowerPlan != PowerManager.PowerMode.Undefined)
			{
				bool wait = true;

				lock (waitingExitLock)
				{
					if (waitingExit.Count == 0)
						PowerManager.SaveMode();

					if (waitingExitPids.Add(process.Id))
						waitingExit.Add(process);
					else
						wait = false;

					Log.Verbose("POWER MODE: {0} processes desiring higher power mode.", waitingExit.Count);

					if (wait)
					{
						string name = process.ProcessName;
						process.EnableRaisingEvents = true;
						process.Exited += async (sender, ev) => // ASYNC POWER RESTORE ON EXIT
						{
							await Task.Delay(ProcessManager.PowerdownDelay);

							waitingExit.Remove(process);
							waitingExitPids.Remove(process.Id);

							if (waitingExit.Count == 0)
							{
								Log.Debug("POWER MODE: '{ProcessName}' exited, restoring normal functionality.", name);
								PowerManager.RestoreMode();
							}
							else
							{
								Log.Debug("POWER MODE: {0} processes still wanting higher power mode.", waitingExit.Count);
								List<string> names = new List<string>();
								foreach (var b in waitingExit)
									names.Add(b.ProcessName);
								Log.Debug("POWER MODE WAIT LIST: " + string.Join(", ", names));
							}
						};

						if (PowerManager.Current != PowerPlan)
						{
							Log.Verbose("Power mode upgrading to: {PowerPlan}", PowerPlan);
							PowerManager.upgradeMode(PowerPlan);
						}
					}
				}

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

		void SetBackground(Process process)
		{
			SetIOPriority(process, PriorityTypes.PROCESS_MODE_BACKGROUND_BEGIN);
		}

		public static void SetIOPriority(Process process, PriorityTypes priority)
		{
			try { SetPriorityClass(process.Handle, (uint)priority); }
			catch { }
		}

		public ProcessState Touch(ProcessManager.BasicProcessInfo info, bool schedule_next = false, bool recheck = false)
		{
			Debug.Assert(info.Process != null, "ProcessController.Touch given null process.");
			Debug.Assert(info.Id > 4, "ProcessController.Touch given invalid process ID");
			Debug.Assert(!string.IsNullOrEmpty(info.Name), "ProcessController.Touch given empty process name.");

			if (info.Process == null || info.Id <= 4 || string.IsNullOrEmpty(info.Name))
			{
				throw new ArgumentNullException();
				return ProcessState.Invalid;
			}

			if (recheck)
			{
				try
				{
					info.Process.Refresh();
				}
				catch { /* NOP */ }
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
					Log.Verbose("[{FriendlyName}] {ProcessName} (#{ProcessID}) has already exited.", FriendlyName, info.Name, info.Id);
					return ProcessState.Invalid;
				}
			}
			catch (Win32Exception ex)
			{
				if (ex.NativeErrorCode != 5)
					Log.Warning("[{FriendlyName}] '{ProcessName}' (#{ProcessID}) access failure determining if it's still running.", FriendlyName, info.Name, info.Id);
				return ProcessState.AccessDenied; // we don't care what this error is exactly
			}

			Log.Verbose("[{FriendlyName}] Touching: {ExecutableName} (#{ProcessID})",
						FriendlyName, info.Name, info.Id);

			ProcessState rv = ProcessState.Invalid;

			if (IgnoreList != null && IgnoreList.Contains(info.Name, StringComparer.InvariantCultureIgnoreCase))
			{
				Log.Debug("[{FriendlyName}] {Exec} (#{ProcessID}) ignored due to user defined rule.", FriendlyName, info.Name, info.Id);
				return ProcessState.AccessDenied;
			}

			bool denyChange = ProcessManager.ProtectedProcessName(info.Name);
			if (denyChange)
				Log.Debug("[{FriendlyName}] {ProcessName} (#{ProcessID}) in protected list, limiting tampering.", FriendlyName, info.Name, info.Id);

			bool mAffinity = false, mPriority = false, mPower = false, modified = false;
			LastSeen = DateTime.Now;

			IntPtr oldAffinity = IntPtr.Zero;
			ProcessPriorityClass oldPriority = ProcessPriorityClass.RealTime;
			try
			{
				oldAffinity = info.Process.ProcessorAffinity;
				oldPriority = info.Process.PriorityClass;
			}
			catch { }

			lock (info.Process) // unnecessary
			{
				if (!denyChange)
				{
					try
					{
						if (info.Process.SetLimitedPriority(Priority, Increase, Decrease))
							modified = mPriority = true;
					}
					catch
					{
						// NOP
					}
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
						//			execname, pid, process.ProcessorAffinity.ToInt32(), Affinity.ToInt32());
					}
				}
				catch
				{
					Log.Warning("Couldn't access process ({ExecutableName}, #{ProcessID}) affinity.",
								info.Name, info.Id);
				}

				PowerManager.PowerMode oldPP = PowerManager.Current;
				setPowerPlan(info.Process);
				mPower = (oldPP != PowerManager.Current);
			}

			var sbs = new System.Text.StringBuilder();
			sbs.Append("[").Append(FriendlyName).Append("] ").Append(info.Name).Append(" (#").Append(info.Id).Append(")");
			if (modified)
			{
				if (mPriority || mAffinity)
					Adjusts += 1; // don't increment on power changes

				try
				{
					info.Process.Refresh();
					ProcessPriorityClass newPriority = info.Process.PriorityClass;
					if (info.Process.PriorityClass != Priority)
					{
						/*
string Namespace = @"root\cimv2";
string className = "Win32_Process";
Microsoft.Management.Infrastructure.CimInstance diskDrive = new Microsoft.Management.Infrastructure.CimInstance(className, Namespace);
*/
					}
				}
				catch
				{
				}

				LastTouch = DateTime.Now;
				rv = ProcessState.Modified;

				if (mPriority)
					sbs.Append("; Priority: ").Append(oldPriority.ToString()).Append(" → ").Append(Priority.ToString());
				if (mAffinity)
					sbs.Append("; Affinity: ").Append(oldAffinity.ToInt32()).Append(" → ").Append(Affinity.ToInt32());
				if (mPower)
					sbs.Append(string.Format(" [Power Mode: {0}]", PowerPlan.ToString()));

				Log.Information(sbs.ToString());

				rv = ProcessState.Modified;

				onTouch?.Invoke(this, new ProcessEventArgs { Control = this, Process = info.Process });
			}
			else
			{
				//if (DateTime.Now - LastSeen
				sbs.Append(" looks OK, not touched.");
				Log.Verbose(sbs.ToString());
				rv = ProcessState.OK;
			}

			if (schedule_next) RescanWithSchedule();

			if (Recheck > 0 && recheck == false)
			{
				Task.Run(new Func<Task>(async () =>
				{
					await Task.Delay(Math.Max(Recheck, 5) * 1000);
					Log.Debug("Rechecking '{Process}' (#{PID})", info.Name, info.Id);
					if (!info.Process.HasExited)
						Touch(info, schedule_next: false, recheck: true);
					else
						Log.Debug("'{Process}' (#{PID}) is gone yo.", info.Name, info.Id);
				}));
			}

			return rv;		}

		/// <summary>
		/// Synchronous call to RescanWithSchedule()
		/// </summary>
		public int TryScan()
		{
			RescanWithSchedule().Wait();

			int n2 = Convert.ToInt32((DateTime.Now - LastScan.AddMinutes(Rescan)).TotalMinutes);
			return n2;
		}

		DateTime LastScan = DateTime.MinValue;

		/// <summary>
		/// Atomic lock for RescanWithSchedule()
		/// </summary>
		int ScheduledScan; // = 0;

		async Task RescanWithSchedule()
		{
			double n = (DateTime.Now - LastScan).TotalMinutes;
			//Log.Trace(string.Format("[{0}] last scan {1:N1} minute(s) ago.", FriendlyName, n));
			if (Rescan > 0 && n >= Rescan)
			{
				if (System.Threading.Interlocked.CompareExchange(ref ScheduledScan, 1, 0) == 1)
					return;

				Log.Debug("'{FriendlyName}' rescan initiating.", FriendlyName);

				await Scan();

				ScheduledScan = 0;
			}
			else
				Log.Verbose("'{FriendlyName}' scan too recent, ignoring.", FriendlyName);
		}

		public async Task Scan()
		{
			await Task.Yield();

			if (ExecutableFriendlyName == null) return;

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

			//LastSeen = LastScan;
			LastScan = DateTime.Now;

			if (procs.Length == 0) return;


			Log.Verbose("Scanning '{ProcessFriendlyName}' (found {ProcessInstances} instance(s))", FriendlyName, procs.Length);

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
				if (Touch(new ProcessManager.BasicProcessInfo { Name = name, Id = pid, Process = process, Path = null }) == ProcessState.Modified)
					tc++;
			}

			if (tc > 0)
				Log.Verbose("Scan for '{ProcessFriendlyName}' modified {ModifiedInstances} out of {ProcessInstances} instance(s)",
							FriendlyName, tc, procs.Length);
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

		public static event EventHandler<ProcessEventArgs> onTouch;
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
}
