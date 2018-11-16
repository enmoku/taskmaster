//
// ProcessController.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2016-2018 M.A.
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
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Serilog;

namespace Taskmaster
{
	/// <summary>
	/// Process controller.
	/// </summary>
	sealed public class ProcessController : IDisposable
	{
		// EVENTS
		public event EventHandler<ProcessEventArgs> Modified;

		// Core information
		/// <summary>
		/// 
		/// </summary>
		public ProcessType Type = ProcessType.Generic;

		/// <summary>
		/// Whether or not this rule is enabled.
		/// </summary>
		public bool Enabled { get; set; } = false;

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

		public string Path { get; set; } = string.Empty;

		public float Volume { get; set; } = 0.5f;
		public AudioVolumeStrategy VolumeStrategy { get; set; } = AudioVolumeStrategy.Ignore;

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
		public System.Diagnostics.ProcessPriorityClass? Priority = null;

		public ProcessPriorityStrategy PriorityStrategy = ProcessPriorityStrategy.None;

		/// <summary>
		/// CPU core affinity.
		/// </summary>
		public IntPtr? Affinity = null;

		public ProcessAffinityStrategy AffinityStrategy = ProcessAffinityStrategy.None;
		int ScatterOffset = 0;
		int ScatterChunk = 1; // should default to Cores/4 or Range/2 at max, 1 at minimum.

		/// <summary>
		/// The power plan.
		/// </summary>
		public PowerInfo.PowerMode PowerPlan = PowerInfo.PowerMode.Undefined;

		int p_Recheck = 0;
		public int Recheck
		{
			get
			{
				return p_Recheck;
			}
			set
			{
				p_Recheck = value.Constrain(0, 300);
			}
		}

		public bool AllowPaging = false;

		public PathVisibilityOptions PathVisibility;

		string PathMask = string.Empty;

		/// <summary>
		/// Delay in milliseconds before we attempt to alter the process.
		/// </summary>
		public int ModifyDelay { get; set; } = 0;

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
		/// Same as <see cref="T:Taskmaster.ProcessControl.Executable"/> but with the extension missing.
		/// </summary>
		public string ExecutableFriendlyName { get; set; } = null;

		public ProcessController(string name, ProcessPriorityClass? priority = null, int affinity = -1)
		{
			FriendlyName = name;
			// Executable = executable;

			Priority = priority;
			if (affinity >= 0)
			{
				Affinity = new IntPtr(affinity);
				AffinityStrategy = ProcessAffinityStrategy.Limit;
			}
		}

		const string watchlistfile = "Watchlist.ini";

		public void DeleteConfig(ConfigWrapper cfg = null)
		{
			if (cfg == null)
				cfg = Taskmaster.Config.Load(watchlistfile);

			cfg.Config.Remove(FriendlyName); // remove the section, should remove items in the section
			cfg.MarkDirty();
		}

		public void SaveConfig(ConfigWrapper cfg = null, SharpConfig.Section app = null)
		{
			// TODO: Check if anything actually was changed?

			if (cfg == null)
				cfg = Taskmaster.Config.Load(watchlistfile);
			
			if (app == null)
				app = cfg.Config[FriendlyName];

			if (!string.IsNullOrEmpty(Executable))
				app["Image"].StringValue = Executable;
			else
				app.Remove("Image");
			if (!string.IsNullOrEmpty(Path))
				app["Path"].StringValue = Path;
			else
				app.Remove("Path");

			app.Remove("Increase"); // DEPRECATED
			app.Remove("Decrease"); // DEPRECATED

			if (Priority.HasValue)
			{
				app["Priority"].IntValue = ProcessHelpers.PriorityToInt(Priority.Value);
				app["Priority strategy"].IntValue = (int)PriorityStrategy;
			}
			else
			{
				app.Remove("Priority");
				app.Remove("Priority strategy");
			}

			if (Affinity.HasValue && Affinity.Value.ToInt32() >= 0)
			{
				var affinity = Affinity.Value.ToInt32();
				//if (affinity == ProcessManager.allCPUsMask) affinity = 0; // convert back

				app["Affinity"].IntValue = affinity;
				app["Affinity strategy"].IntValue = (int)AffinityStrategy;
			}
			else
			{
				app.Remove("Affinity");
				app.Remove("Affinity strategy");
			}

			var pmode = PowerManager.GetModeName(PowerPlan);
			if (PowerPlan != PowerInfo.PowerMode.Undefined)
				app["Power mode"].StringValue = PowerManager.GetModeName(PowerPlan);
			else
				app.Remove("Power mode");

			if (ForegroundOnly)
			{
				app["Foreground only"].BoolValue = ForegroundOnly;

				if (BackgroundPriority.HasValue)
					app["Background priority"].IntValue = ProcessHelpers.PriorityToInt(BackgroundPriority.Value);
				else
					app.Remove("Background priority");

				if (BackgroundAffinity.HasValue)
					app["Background affinity"].IntValue = BackgroundAffinity.Value.ToInt32();
				else
					app.Remove("Background affinity");

				if (BackgroundPowerdown)
					app["Background powerdown"].BoolValue = BackgroundPowerdown;
				else
					app.Remove("Background powerdown");
			}
			else
			{
				app.Remove("Foreground only");
				app.Remove("Background priority");
				app.Remove("Background powerdown");
			}

			if (AllowPaging)
				app["Allow paging"].BoolValue = AllowPaging;
			else
				app.Remove("Allow paging");

			if (PathVisibility != PathVisibilityOptions.File)
				app["Path visibility"].IntValue = (int)PathVisibility;
			else
				app.Remove("Path visibility");

			if (!string.IsNullOrEmpty(Executable))
			{
				if (Rescan > 0) app["Rescan"].IntValue = Rescan;
				else app.Remove("Rescan");
				if (Recheck > 0) app["Recheck"].IntValue = Recheck;
				else app.Remove("Recheck");
			}

			if (!Enabled) app["Enabled"].BoolValue = Enabled;
			else app.Remove("Enabled");

			if (IgnoreList != null && IgnoreList.Length > 0)
				app["Ignore"].StringValueArray = IgnoreList;
			else
				app.Remove("Ignore");

			if (ModifyDelay > 0)
				app["Modify delay"].IntValue = ModifyDelay;
			else
				app.Remove("Modify delay");

			if (Resize.HasValue)
			{
				int[] res = app.TryGet("Resize")?.IntValueArray ?? null;
				if (res == null || res.Length != 4) res = new int[] { 0, 0, 0, 0 };

				if (Bit.IsSet((int)ResizeStrategy, (int)WindowResizeStrategy.Position))
				{
					res[0] = Resize.Value.Left;
					res[1] = Resize.Value.Top;
				}

				if (Bit.IsSet((int)ResizeStrategy, (int)WindowResizeStrategy.Size))
				{
					res[2] = Resize.Value.Width;
					res[3] = Resize.Value.Height;
				}

				app["Resize"].IntValueArray = res;

				app["Resize strategy"].IntValue = (int)ResizeStrategy;
			}
			else
			{
				app.Remove("Resize strategy");
				app.Remove("Resize");
			}

			if (VolumeStrategy != AudioVolumeStrategy.Ignore)
			{
				app["Volume"].FloatValue = Volume;
				app["Volume strategy"].IntValue = (int)VolumeStrategy;
			}
			else
			{
				app.Remove("Volume");
				app.Remove("Volume strategy");
			}

			// pass to config manager
			NeedsSaving = false;
			cfg.MarkDirty();
		}

		const string statfile = "Watchlist.Statistics.ini";

		public void LoadStats()
		{
			var stats = Taskmaster.Config.Load(statfile);

			string statkey = null;
			if (!string.IsNullOrEmpty(Executable)) statkey = Executable;
			else if (!string.IsNullOrEmpty(Path)) statkey = Path;

			if (!string.IsNullOrEmpty(statkey))
			{
				Adjusts = stats.Config[statkey].TryGet("Adjusts")?.IntValue ?? 0;

				var ls = stats.Config[statkey].TryGet("Last seen");
				if (null != ls && !ls.IsEmpty)
				{
					var stamp = long.MinValue;
					try
					{
						stamp = ls.GetValue<long>();
						LastSeen = stamp.Unixstamp();
					}
					catch { } // NOP
				}
			}
		}

		public void SaveStats()
		{
			var stats = Taskmaster.Config.Load(statfile);

			// BROKEN?
			string key = null;
			if (!string.IsNullOrEmpty(Executable)) key = Executable;
			else if (!string.IsNullOrEmpty(Path)) key = Path;
			else return;

			if (Adjusts > 0)
			{
				stats.Config[key]["Adjusts"].IntValue = Adjusts;
				stats.MarkDirty();
			}

			if (LastSeen != DateTime.MinValue)
			{
				stats.Config[key]["Last seen"].SetValue(LastSeen.Unixstamp());
				stats.MarkDirty();
			}
		}

		HashSet<int> PausedIds = new HashSet<int>();

		public bool BackgroundPowerdown { get; set; } = true;
		public ProcessPriorityClass? BackgroundPriority { get; set; } = null;
		public IntPtr? BackgroundAffinity { get; set; } = IntPtr.Zero;

		/// <summary>
		/// Pause the specified foreground process.
		/// </summary>
		public void Pause(ProcessEx info)
		{
			Debug.Assert(ForegroundOnly == true);
			Debug.Assert(!isPaused(info));

			PausedIds.Add(info.Id);

			if (Taskmaster.DebugForeground && Taskmaster.Trace)
				Log.Debug("[" + FriendlyName + "] Quelling " + info.Name + " (#" + info.Id + ")");

			// PausedState.Affinity = Affinity;
			// PausedState.Priority = Priority;
			// PausedState.PowerMode = PowerPlan;

			bool mAffinity = false, mPriority = false;
			ProcessPriorityClass oldPriority = ProcessPriorityClass.RealTime;
			IntPtr oldAffinity = IntPtr.Zero;

			try
			{
				if (BackgroundPriority.HasValue)
				{
					oldPriority = info.Process.PriorityClass;
					if (oldPriority != BackgroundPriority.Value)
					{
						info.Process.PriorityClass = BackgroundPriority.Value;
						mPriority = true;
					}
				}
				if (BackgroundAffinity.HasValue)
				{
					oldAffinity = info.Process.ProcessorAffinity;
					if (oldAffinity.ToInt32() != BackgroundAffinity.Value.ToInt32())
					{
						info.Process.ProcessorAffinity = BackgroundAffinity.Value;
						mAffinity = true;
					}
				}
			}
			catch
			{
			}
			// info.Process.ProcessorAffinity = OriginalState.Affinity;

			if (Taskmaster.PowerManagerEnabled)
			{
				if (PowerPlan != PowerInfo.PowerMode.Undefined && BackgroundPowerdown)
				{
					if (Taskmaster.DebugPower)
						Log.Debug("[" + FriendlyName + "] " + info.Name + " (#" + info.Id + ") background power down");

					UndoPower(info);
				}
			}

			if (Taskmaster.DebugForeground)
			{
				var sbs = new System.Text.StringBuilder();
				sbs.Append("[").Append(FriendlyName).Append("] ").Append(FormatPathName(info))
					.Append(" (#").Append(info.Id).Append(")");
				if (mPriority)
					sbs.Append("; Priority: ").Append(oldPriority.ToString()).Append("→").Append(BackgroundPriority.Value.ToString());
				if (mAffinity)
					sbs.Append("; Affinity: ").Append(oldAffinity.ToInt32()).Append("→").Append(BackgroundAffinity.Value.ToInt32());
				if (!mAffinity && !mPriority)
					sbs.Append("; Already at target values");
				sbs.Append(" [Background]");

				Log.Debug(sbs.ToString());
				sbs.Clear();
			}

			ForegroundMonitor(info);
		}

		bool isPaused(ProcessEx info) => PausedIds.Contains(info.Id);

		object foreground_lock = new object();

		public void Resume(ProcessEx info)
		{
			Debug.Assert(ForegroundOnly == true);

			bool mAffinity = false, mPriority = false;
			ProcessPriorityClass oldPriority = ProcessPriorityClass.RealTime;
			IntPtr oldAffinity = IntPtr.Zero;

			lock (foreground_lock)
			{
				if (!isPaused(info))
				{
					if (Taskmaster.DebugForeground)
						Log.Debug("<Foreground> " + FormatPathName(info) + " (#" + info.Id + ") not paused; not resuming.");
					return; // can't resume unpaused item
				}
				try
				{
					if (Priority.HasValue && info.Process.PriorityClass.ToInt32() != Priority.Value.ToInt32())
					{
						info.Process.PriorityClass = Priority.Value;
						mPriority = true;
					}

					if (Affinity.HasValue)
					{
						info.Process.ProcessorAffinity = Affinity.Value;
						mAffinity = true;
					}
				}
				catch (InvalidOperationException) // ID not available, probably exited
				{
					PausedIds.Remove(info.Id);
					return;
				}
				catch (Win32Exception) // access error
				{
					PausedIds.Remove(info.Id);
					return;
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
					return;
				}

				if (Taskmaster.DebugForeground)
				{
					var sbs = new System.Text.StringBuilder();
					sbs.Append("[").Append(FriendlyName).Append("] ").Append(FormatPathName(info))
						.Append(" (#").Append(info.Id).Append(")");
					if (mPriority)
						sbs.Append("; Priority: ").Append(oldPriority.ToString()).Append("→").Append(Priority.ToString());
					if (mAffinity)
						sbs.Append("; Affinity: ").Append(oldAffinity.ToInt32()).Append("→").Append(Affinity.Value.ToInt32());
					if (!mAffinity && !mPriority)
						sbs.Append("; Already at target values");
					sbs.Append(" [Foreground]");

					Log.Debug(sbs.ToString());
					sbs.Clear();
				}

				// PausedState.Priority = Priority;
				// PausedState.PowerMode = PowerPlan;

				if (Taskmaster.PowerManagerEnabled)
				{
					if (PowerPlan != PowerInfo.PowerMode.Undefined && BackgroundPowerdown)
					{
						if (Taskmaster.DebugPower || Taskmaster.DebugForeground)
							Log.Debug("[" + FriendlyName + "] " + info.Name + " (#" + info.Id + ") foreground power on");

						SetPower(info);
					}
				}

				PausedIds.Remove(info.Id);
			}
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

		bool SetPower(ProcessEx info)
		{
			Debug.Assert(Taskmaster.PowerManagerEnabled);
			Debug.Assert(PowerPlan != PowerInfo.PowerMode.Undefined);

			info.PowerWait = true;
			Taskmaster.Components.processmanager.WaitForExit(info); // TODO: need nicer way to signal this

			return Taskmaster.Components.powermanager.Force(PowerPlan, info.Id);
		}

		void UndoPower(ProcessEx info) => Taskmaster.Components.powermanager?.Release(info.Id);

		/*
		// Windows doesn't allow setting this for other processes
		bool SetBackground(Process process)
		{
			return SetIOPriority(process, PriorityTypes.PROCESS_MODE_BACKGROUND_BEGIN);
		}
		*/

		/// <summary>
		/// Set disk I/O priority. Works only for setting own process priority.
		/// Would require invasive injecting to other process to affect them.
		/// </summary>
		/// <exception>None</exception>
		public static bool SetIOPriority(Process process, NativeMethods.PriorityTypes priority)
		{
			try
			{
				var rv = NativeMethods.SetPriorityClass(process.Handle, (uint)priority);
				return rv;
			}
			catch (InvalidOperationException) { } // Already exited
			catch (ArgumentException) { } // already exited?
			catch (Exception ex) { Logging.Stacktrace(ex); }

			return false;
		}

		HashSet<int> ForegroundWatch = new HashSet<int>();

		string FormatPathName(ProcessEx info)
		{
			if (!string.IsNullOrEmpty(info.Path))
			{
				switch (PathVisibility)
				{
					default:
					case PathVisibilityOptions.File:
						return System.IO.Path.GetFileName(info.Path);
					case PathVisibilityOptions.Folder:
						var parts = info.Path.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
						var partpath = parts[parts.Length - 2] + System.IO.Path.DirectorySeparatorChar + parts[parts.Length - 1];
						return partpath;
					case PathVisibilityOptions.Full:
						return info.Path;
				}
			}
			else
				return info.Name; // NAME
		}

		void ForegroundMonitor(ProcessEx info)
		{
			lock (foreground_lock)
			{
				if (ForegroundWatch.Contains(info.Id)) return;

				ForegroundWatch.Add(info.Id);

				info.Process.Exited += (o, s) =>
				{
					lock (foreground_lock)
					{
						ForegroundWatch.Remove(info.Id);
						PausedIds.Remove(info.Id);
					}
				};

				var sbs = new System.Text.StringBuilder();
				sbs.Append("[").Append(FriendlyName).Append("] ")
					.Append(FormatPathName(info))
					.Append(" (#").Append(info.Id).Append(")");
				if (Priority.HasValue) sbs.Append("; Priority:").Append(Priority.Value.ToString());
				if (Affinity.HasValue) sbs.Append("; Affinity:").Append(Affinity.Value.ToInt32());
				if (PowerPlan != PowerInfo.PowerMode.Undefined) sbs.Append("; Power:").Append(PowerPlan.GetShortName());
				sbs.Append(" – Foreground Only");
				Log.Information(sbs.ToString());
			}
		}

		public async Task Modify(ProcessEx info)
		{
			Touch(info);
			if (Recheck > 0) TouchReapply(info);
		}

		public bool MatchPath(string path)
		{
			return path.StartsWith(Path, StringComparison.InvariantCultureIgnoreCase);
		}

		// TODO: Simplify this
		public async void Touch(ProcessEx info, bool refresh = false)
		{
			Debug.Assert(info.Process != null, "ProcessController.Touch given null process.");
			Debug.Assert(info.Id > 4, "ProcessController.Touch given invalid process ID");
			Debug.Assert(!string.IsNullOrEmpty(info.Name), "ProcessController.Touch given empty process name.");

			bool foreground = true;

			if (ForegroundOnly)
			{
				if (isPaused(info))
				{
					if (Taskmaster.Trace && Taskmaster.DebugForeground)
						Log.Debug("<Foreground> " + FormatPathName(info) + " (#" + info.Id + ") in background, ignoring.");
					return; // don't touch paused item
				}

				foreground = Taskmaster.Components.activeappmonitor?.Foreground.Equals(info.Id) ?? true;
			}

			info.PowerWait = (PowerPlan != PowerInfo.PowerMode.Undefined);
			info.ActiveWait = ForegroundOnly;

			await Task.Delay(refresh ? 0 : ModifyDelay).ConfigureAwait(false);

			if (info.NeedsRefresh || ModifyDelay > 0)
			{
				info.Process.Refresh();
				info.NeedsRefresh = false;
			}

			bool responding = true;
			var oldPriority = ProcessPriorityClass.RealTime;
			var oldAffinity = IntPtr.Zero;
			int oldAffinityMask = 0;
			var oldPower = PowerInfo.PowerMode.Undefined;

			// EXTRACT INFORMATION

			try
			{
				responding = info.Process.Responding;

				if (info.Process.HasExited)
				{
					if (Taskmaster.DebugProcesses)
						Log.Debug("[" + FriendlyName + "] " + info.Name + " (#" + info.Id + ") has already exited.");
					return; // return ProcessState.Invalid;
				}

				oldAffinity = info.Process.ProcessorAffinity;
				oldPriority = info.Process.PriorityClass;
				oldAffinityMask = oldAffinity.ToInt32();
			}
			catch (InvalidOperationException) // Already exited
			{
				info.State = ProcessModification.Exited;
				return;
			}
			catch (Win32Exception) // denied
			{
				// failure to retrieve exit code, this probably means we don't have sufficient rights. assume it is gone.
				info.State = ProcessModification.AccessDenied;
				return;
			}
			catch (Exception ex) // invalidoperation or notsupported, neither should happen
			{
				Logging.Stacktrace(ex);
				info.State = ProcessModification.Invalid;
				return;
			}

			if (Taskmaster.Trace) Log.Verbose("[" + FriendlyName + "] Touching: " + info.Name + " (#" + info.Id + ")");

			if (IgnoreList != null && IgnoreList.Contains(info.Name, StringComparer.InvariantCultureIgnoreCase))
			{
				if (Taskmaster.ShowInaction && Taskmaster.DebugProcesses)
					Log.Debug("[" + FriendlyName + "] " + info.Name + " (#" + info.Id + ") ignored due to user defined rule.");
				return; // return ProcessState.Ignored;
			}

			bool protectedfile = ProcessManager.ProtectedProcessName(info.Name);
			// TODO: IgnoreSystem32Path

			if (protectedfile && Taskmaster.ShowInaction && Taskmaster.DebugProcesses)
				Log.Debug("[" + FriendlyName + "] " + info.Name + " (#" + info.Id + ") in protected list, limiting tampering.");

			// TODO: Validate path.
			if (!string.IsNullOrEmpty(Path))
			{
				if (string.IsNullOrEmpty(info.Path) && !ProcessManagerUtility.FindPath(info))
					return; // return ProcessState.Error;

				if (info.PathMatched || MatchPath(info.Path)) // FIXME: this is done twice
				{
					// OK
					if (Taskmaster.DebugPaths && !info.PathMatched)
						Log.Verbose("[" + FriendlyName + "] (Touch) Matched at: " + info.Path);

					info.PathMatched = true;
				}
				else
				{
					if (Taskmaster.DebugPaths)
						Log.Verbose("[" + FriendlyName + "] " + info.Path + " NOT IN " + Path + " – IGNORING");
					return; // return ProcessState.Ignored;
				}
			}

			var newPriority = ProcessPriorityClass.RealTime;
			var newAffinity = IntPtr.Zero;
			var newPower = PowerInfo.PowerMode.Undefined;

			bool mAffinity = false, mPriority = false, mPower = false, modified = false, fAffinity = false, fPriority = false;
			LastSeen = DateTime.Now;

			newAffinity = Affinity.GetValueOrDefault();
			newPriority = Priority.GetValueOrDefault();

			if (ForegroundOnly) ForegroundMonitor(info);

			bool doModifyPriority = false;
			bool doModifyAffinity = false;
			bool doModifyPower = false;

			if (!protectedfile)
			{
				if (Priority.HasValue)
				{
					if (!foreground && ForegroundOnly)
					{
						if (Taskmaster.DebugForeground || Taskmaster.ShowInaction)
							Log.Debug("[" + FriendlyName + "] " + info.Name + " (#" + info.Id + ") not in foreground, not prioritizing.");

						Pause(info);
					}
					else
						doModifyPriority = true;
				}
			}
			else
			{
				if (Taskmaster.ShowInaction && Taskmaster.DebugProcesses)
					Log.Verbose("[" + FriendlyName + "] " + info.Name + " (#" + info.Id + ") protected.");
			}

			if (Affinity.HasValue)
			{
				var newAffinityMask = Affinity.Value.ToInt32();
				if (oldAffinityMask != newAffinityMask)
				{
					/*
					var taff = Affinity;
					if (AllowedCores || !Increase)
					{
						var minaff = Bit.Or(newAffinityMask, oldAffinityMask);
						var mincount = Bit.Count(minaff);
						var bitsold = Bit.Count(oldAffinityMask);
						var bitsnew = Bit.Count(newAffinityMask);
						var minaff1 = minaff;
						minaff = Bit.Fill(minaff, bitsnew, Math.Min(bitsold, bitsnew));
						if (minaff1 != minaff)
						{
							Console.WriteLine("--- Affinity | Core Shift ---");
							Console.WriteLine(Convert.ToString(minaff1, 2).PadLeft(ProcessManager.CPUCount));
							Console.WriteLine(Convert.ToString(minaff, 2).PadLeft(ProcessManager.CPUCount));
						}
						else
						{
							Console.WriteLine("--- Affinity | Meh ---");
							Console.WriteLine(Convert.ToString(Affinity.ToInt32(), 2).PadLeft(ProcessManager.CPUCount));
							Console.WriteLine(Convert.ToString(minaff, 2).PadLeft(ProcessManager.CPUCount));
						}

						// shuffle cores from old to new
						taff = new IntPtr(minaff);
					}
					*/
					// int bitsnew = Bit.Count(newAffinityMask);
					// TODO: Somehow shift bits old to new if there's free spots

					int modifiedAffinity = ProcessManagerUtility.ApplyAffinityStrategy(oldAffinityMask, newAffinityMask, AffinityStrategy);
					if (modifiedAffinity != newAffinityMask)
					{
						newAffinityMask = modifiedAffinity;
						newAffinity = new IntPtr(newAffinityMask);
					}

					if (oldAffinityMask != newAffinityMask)
						doModifyAffinity = true;
				}
			}

			// APPLY CHANGES HERE

			if (doModifyPriority)
			{
				try
				{
					if (info.Process.SetLimitedPriority(Priority.Value, PriorityStrategy))
					{
						modified = mPriority = true;
						newPriority = Priority.Value;
					}
				}
				catch { fPriority = true; } // ignore errors, this is all we care of them
			}

			if (doModifyAffinity)
			{
				try
				{
					info.Process.ProcessorAffinity = newAffinity;
					modified = mAffinity = true;
				}
				catch { fAffinity = true; } // ignore errors, this is all we care of them
			}

			/*
			if (BackgroundIO)
			{
				try
				{
					//mBGIO = SetBackground(info.Process); // doesn't work, can only be done to current process
				}
				catch
				{
					// NOP, don't caree
				}
				if (mBGIO == false)
					Log.Warning("[{FriendlyName}] {Exec} (#{Pid}) Failed to set background I/O mode.", FriendlyName, info.Name, info.Id);
			}
			*/

			//var oldPP = PowerInfo.PowerMode.Undefined;
			if (Taskmaster.PowerManagerEnabled && PowerPlan != PowerInfo.PowerMode.Undefined)
			{
				if (!foreground && BackgroundPowerdown)
				{
					if (Taskmaster.DebugForeground)
						Log.Debug(info.Name + " (#" + info.Id + ") not in foreground, not powering up.");

					ForegroundMonitor(info);
				}
				else
				{
					//oldPower = Taskmaster.powermanager.CurrentMode;
					mPower = SetPower(info);
					//mPower = (oldPP != Taskmaster.powermanager.CurrentMode);
				}
			}

			// OUTPUT LOGS

			var sbs = new System.Text.StringBuilder();

			sbs.Append("[").Append(FriendlyName).Append("] ")
				.Append(FormatPathName(info))
				.Append(" (#").Append(info.Id).Append(")"); // PID

			if (modified)
			{
				if (mPriority || mAffinity)
				{
					Statistics.TouchCount++;
					Adjusts += 1; // don't increment on power changes
				}

				/*
				// Check if the change took effect?
				if (mPriority)
				{
					try
					{
						info.Process.Refresh();
						newPriority = info.Process.PriorityClass;
						if (newPriority.ToInt32() != Priority.Value.ToInt32())
						{
							Log.Warning("[{FriendlyName}] {Exe} (#{Pid}) Post-mortem of modification: FAILURE (Expected: {TgPrio}, Detected: {CurPrio}).",
										FriendlyName, info.Name, info.Id, Priority.ToString(), newPriority.ToString());
						}
					}
					catch (InvalidOperationException)
					{
						// Already exited
						info.State = ProcessModification.Exited;
						return;
					}
					catch (Win32Exception)
					{
						info.State = ProcessModification.AccessDenied;
						return;
					}
					catch (Exception ex)
					{
						Logging.Stacktrace(ex);
						return;
					}
				}
				*/

				LastTouch = DateTime.Now;

				ScanModifyCount++;
			}

			if (Priority.HasValue)
			{
				sbs.Append("; Priority: ");
				if (mPriority)
					sbs.Append(oldPriority.ToString()).Append(" → ");
				sbs.Append(newPriority.ToString());
				if (protectedfile) sbs.Append(" [Protected]");
				if (fPriority)
				{
					sbs.Append(" [Failed]");
					if (Taskmaster.ShowInaction)
						Log.Warning("[" + FriendlyName + "] " + info.Name + " (#" + info.Id + ") failed to set process priority.");
				}
			}
			if (Affinity.HasValue)
			{
				// TODO: respect display configuration for 
				sbs.Append("; Affinity: ");
				if (mAffinity)
					sbs.Append(oldAffinity.ToInt32()).Append(" → ");
				sbs.Append(newAffinity);

				if (fAffinity)
				{
					sbs.Append(" [Failed]");
					if (Taskmaster.ShowInaction)
						Log.Warning("[" + FriendlyName + "] " + info.Name + " (#" + info.Id + ") failed to set process affinity.");
				}

				if (Taskmaster.DebugProcesses) sbs.Append(" [").Append(AffinityStrategy.ToString()).Append("]");
			}

			if (mPower) sbs.Append(" [Power Mode: ").Append(PowerPlan.ToString()).Append("]");

			if (modified)
			{
				if (Taskmaster.DebugProcesses || Taskmaster.ShowProcessAdjusts)
				{
					if (ForegroundOnly && !Taskmaster.ShowForegroundTransitions) { } // do nothing
					else
						Log.Information(sbs.ToString());
				}
			}
			else
			{
				// if (DateTime.Now - LastSeen
				if (Taskmaster.ShowInaction && Taskmaster.DebugProcesses)
				{
					sbs.Append(" – looks OK, not touched.");
					Log.Debug(sbs.ToString());
				}
			}

			TryResize(info);

			info.Handled = true;
			info.Modified = DateTime.Now;

			if (modified) Modified?.Invoke(this, new ProcessEventArgs { Control = this, Info = info });

			sbs.Clear();

			if (Recheck > 0)
			{
				info.NeedsRefresh = true;
				TouchReapply(info);
			}
		}

		NativeMethods.RECT rect = new NativeMethods.RECT();

		void TryResize(ProcessEx info)
		{
			if (!Resize.HasValue) return;

			if (Taskmaster.DebugResize) Log.Debug("Attempting resize on " + info.Name + " (#" + info.Id + ")");

			try
			{
				lock (ResizeWaitList_lock)
				{
					if (ResizeWaitList.Contains(info.Id)) return;

					IntPtr hwnd = info.Process.MainWindowHandle;
					if (!NativeMethods.GetWindowRect(hwnd, ref rect))
					{
						if (Taskmaster.DebugResize) Log.Debug("Failed to retrieve current size of " + info.Name + " (#" + info.Id + ")");
					}

					var oldrect = new System.Drawing.Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

					var newsize = new System.Drawing.Rectangle(
						Resize.Value.Left != 0 ? Resize.Value.Left : oldrect.Left,
						Resize.Value.Top != 0 ? Resize.Value.Top : oldrect.Top,
						Resize.Value.Width != 0 ? Resize.Value.Width : oldrect.Width,
						Resize.Value.Height != 0 ? Resize.Value.Height : oldrect.Height
						);

					if (!newsize.Equals(oldrect))
					{
						if (Taskmaster.DebugResize)
							Log.Debug("Resizing " + info.Name + " (#" + info.Id + ") from " +
								oldrect.Width + "×" + oldrect.Height + " to " +
								newsize.Width + "×" + newsize.Height);

						// TODO: Add option to monitor the app and save the new size so relaunching the app keeps the size.

						NativeMethods.MoveWindow(hwnd, newsize.Left, newsize.Top, newsize.Width, newsize.Height, true);

						if (ResizeStrategy != WindowResizeStrategy.None)
						{
							Resize = newsize;
							NeedsSaving = true;
						}
					}

					if (ResizeStrategy == WindowResizeStrategy.None)
					{
						if (Taskmaster.DebugResize) Log.Debug("Remembering size or pos not enabled for " + info.Name + " (#" + info.Id + ")");
						return;
					}

					ResizeWaitList.Add(info.Id);

					System.Threading.ManualResetEvent re = new System.Threading.ManualResetEvent(false);
					Task.Run(new Action(() =>
					{
						if (Taskmaster.DebugResize) Log.Debug("<Resize> Starting monitoring " + info.Name + " (#" + info.Id + ")");
						try
						{
							while (!re.WaitOne(60_000))
							{
								if (Taskmaster.DebugResize) Log.Debug("<Resize> Recording size and position for " + info.Name + " (#" + info.Id + ")");

								NativeMethods.GetWindowRect(hwnd, ref rect);

								bool rpos = Bit.IsSet(((int)ResizeStrategy), (int)WindowResizeStrategy.Position);
								bool rsiz = Bit.IsSet(((int)ResizeStrategy), (int)WindowResizeStrategy.Size);
								newsize = new System.Drawing.Rectangle(
									rpos ? rect.Left : Resize.Value.Left, rpos ? rect.Top : Resize.Value.Top,
									rsiz ? rect.Right - rect.Left : Resize.Value.Left, rsiz ? rect.Bottom - rect.Top : Resize.Value.Top
									);

								Resize = newsize;
								NeedsSaving = true;
							}
						}
						catch (Exception ex)
						{
							Logging.Stacktrace(ex);
						}
						if (Taskmaster.DebugResize) Log.Debug("<Resize> Stopping monitoring " + info.Name + " (#" + info.Id + ")");
					}));

					info.Process.EnableRaisingEvents = true;
					info.Process.Exited += (s, ev) =>
					{
						try
						{
							re.Set();
							re.Reset();

							lock (ResizeWaitList_lock)
							{
								ResizeWaitList.Remove(info.Id);
							}

							if ((Bit.IsSet(((int)ResizeStrategy), (int)WindowResizeStrategy.Size)
								&& (oldrect.Width != Resize.Value.Width || oldrect.Height != Resize.Value.Height))
								|| (Bit.IsSet(((int)ResizeStrategy), (int)WindowResizeStrategy.Position)
								&& (oldrect.Left != Resize.Value.Left || oldrect.Top != Resize.Value.Top)))
							{
								if (Taskmaster.DebugResize)
									Log.Debug("Saving " + info.Name + " (#" + info.Id + ") size to " +
										Resize.Value.Width + "×" + Resize.Value.Height);

								var cfg = Taskmaster.Config.Load(watchlistfile);
								var app = cfg.Config[FriendlyName];

								SaveConfig(cfg, app);
							}
						}
						catch (Exception ex)
						{
							Logging.Stacktrace(ex);
						}
						finally
						{
							ResizeWaitList.Remove(info.Id);
						}
					};
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);

				lock (ResizeWaitList_lock)
				{
					ResizeWaitList.Remove(info.Id);
				}
			}
		}

		public bool NeedsSaving = false;

		public WindowResizeStrategy ResizeStrategy = WindowResizeStrategy.None;

		public bool RememberSize = false;
		public bool RememberPos = false;
		//public int[] Resize = null;
		public System.Drawing.Rectangle? Resize = null;
		List<int> ResizeWaitList = new List<int>();
		object ResizeWaitList_lock = new object();

		async Task TouchReapply(ProcessEx info)
		{
			await Task.Delay(Math.Max(Recheck, 5) * 1000).ConfigureAwait(false);

			if (Taskmaster.DebugProcesses)
				Log.Debug("[" + FriendlyName + "] " + info.Name + " (#" + info.Id + ") rechecking");

			try
			{
				if (info.Process.HasExited)
				{
					if (Taskmaster.Trace) Log.Verbose("[" + FriendlyName + "] " + info.Name + " (#" + info.Id + ") is gone yo.");
					return;
				}

				Touch(info, refresh: true);
			}
			catch (Win32Exception) // access denied
			{
				return;
			}
			catch (InvalidOperationException) // exited
			{
				return;
			}
			catch (Exception ex)
			{
				Log.Warning("[" + FriendlyName + "] " + info.Name + " (#" + info.Id + ") – something bad happened.");
				Logging.Stacktrace(ex);
				return; //throw; // would throw but this is async function
			}
		}

		/// <summary>
		/// Synchronous call to RescanWithSchedule()
		/// </summary>
		public int TryScan()
		{
			RescanWithSchedule();

			return Convert.ToInt32((LastScan.AddMinutes(Rescan) - DateTime.Now).TotalMinutes); // this will produce wrong numbers
		}

		DateTime LastScan = DateTime.MinValue;

		/// <summary>
		/// Atomic lock for RescanWithSchedule()
		/// </summary>
		int ScheduledScan = 0;

		/// <exception>None</exception>
		async Task RescanWithSchedule()
		{
			try
			{
				var n = (DateTime.Now - LastScan).TotalMinutes;
				if (Rescan > 0 && n >= Rescan)
				{
					if (Atomic.Lock(ref ScheduledScan))
					{
						try
						{
							if (Taskmaster.DebugProcesses)
								Log.Debug("[" + FriendlyName + "] Rescan initiating.");

							await Scan().ConfigureAwait(false);
						}
						catch { throw; } // for finally block
						finally
						{
							Atomic.Unlock(ref ScheduledScan);
						}
					}
				}
				// else
				// 	Log.Verbose("[{FriendlyName}] Scan too recent, ignoring.", FriendlyName); // this is too spammy.
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		int ScanModifyCount = 0;
		public async Task Scan()
		{
			if (string.IsNullOrEmpty(ExecutableFriendlyName)) return;

			//await Task.Delay(0);

			Process[] procs;
			try
			{
				procs = Process.GetProcessesByName(ExecutableFriendlyName); // should be case insensitive by default
			}
			catch // name not found
			{
				if (Taskmaster.Trace) Log.Verbose(Executable ?? ExecutableFriendlyName + " is not running");
				return;
			}

			// LastSeen = LastScan;
			LastScan = DateTime.Now;

			if (procs.Length == 0) return;

			if (Taskmaster.DebugProcesses)
				Log.Debug("[" + FriendlyName + "] Scanning found " + procs.Length + " instance(s)");

			ScanModifyCount = 0;
			foreach (Process process in procs)
			{
				string name;
				int pid;
				try
				{
					name = process.ProcessName;
					pid = process.Id;
				}
				catch { continue; } // access failure or similar, we don't care
				try
				{
					var info = ProcessManagerUtility.GetInfo(pid, process, name, null, getPath: !string.IsNullOrEmpty(Path));
					Modify(info);
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
					continue;
				}
			}

			if (ScanModifyCount > 0)
			{
				if (Taskmaster.DebugProcesses)
					Log.Verbose("[" + FriendlyName + "] Scan modified " + ScanModifyCount + " out of " + procs.Length + " instance(s)");
			}
		}

		public bool Locate()
		{
			if (!string.IsNullOrEmpty(Path))
			{
				if (System.IO.Directory.Exists(Path)) return true;
				return false;
			}
			return false;
		}

		public void Dispose()
		{
			Dispose(true);
		}

		bool disposed; // = false;
		void Dispose(bool disposing)
		{
			if (disposed) return;

			if (disposing)
			{
				if (Taskmaster.Trace) Log.Verbose("Disposing process controller [" + FriendlyName + "]");

				Modified = null; // clear events

				if (NeedsSaving) SaveConfig();
			}

			disposed = true;
		}
	}

	sealed public class PathControlEventArgs : EventArgs
	{
	}

	sealed public class ProcessEventArgs : EventArgs
	{
		public ProcessController Control { get; set; } = null;
		public ProcessEx Info = null;
		public ProcessRunningState State = ProcessRunningState.Undefined;
	}
}