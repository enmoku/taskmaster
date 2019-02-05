//
// ProcessController.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2016–2019 M.A.
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MKAh;
using Serilog;

namespace Taskmaster
{
	/// <summary>
	/// Process controller.
	/// </summary>
	sealed public class ProcessController : IDisposable
	{
		// EVENTS
		public event EventHandler<ProcessModificationEventArgs> Modified;

		/// <summary>
		/// Process gone to background
		/// </summary>
		public event EventHandler<ProcessModificationEventArgs> Paused;
		/// <summary>
		/// Process back on foreground.
		/// </summary>
		public event EventHandler<ProcessModificationEventArgs> Resumed;
		/// <summary>
		/// Process is waiting for exit.
		/// </summary>
		public event EventHandler<ProcessModificationEventArgs> WaitingExit;

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

		internal string p_Executable = null;
		/// <summary>
		/// Executable filename related to this, with extension.
		/// </summary>
		public string Executable
		{
			get => p_Executable;
			set => ExecutableFriendlyName = System.IO.Path.GetFileNameWithoutExtension(p_Executable = value);
		}

		/// <summary>
		/// Frienly executable name as required by various System.Process functions.
		/// Same as <see cref="T:Taskmaster.ProcessControl.Executable"/> but with the extension missing.
		/// </summary>
		public string ExecutableFriendlyName { get; internal set; } = null;

		public bool ExclusiveMode { get; set; } = false;

		public string Path { get; set; } = string.Empty;
		int PathElements { get; set; }  = 0;

		/// <summary>
		/// User description for the rule.
		/// </summary>
		public string Description { get; set; } = string.Empty; // TODO: somehow unload this from memory

		public float Volume { get; set; } = 0.5f;
		public AudioVolumeStrategy VolumeStrategy { get; set; } = AudioVolumeStrategy.Ignore;

		public string[] IgnoreList { get; set; } = null;

		/// <summary>
		/// Processes are viable for analysis.
		/// </summary>
		public bool Analyze { get; set; } = false;

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
		public ForegroundMode Foreground { get; private set; } = ForegroundMode.Ignore;

		/// <summary>
		/// Target priority class for the process.
		/// </summary>
		public System.Diagnostics.ProcessPriorityClass? Priority { get; set; } = null;

		public ProcessPriorityStrategy PriorityStrategy { get; set; } = ProcessPriorityStrategy.None;

		/// <summary>
		/// CPU core affinity.
		/// </summary>
		public int AffinityMask { get; set; } = -1;

		public ProcessAffinityStrategy AffinityStrategy = ProcessAffinityStrategy.None;
		int ScatterOffset = 0;
		int ScatterChunk = 1; // should default to Cores/4 or Range/2 at max, 1 at minimum.

		public int AffinityIdeal = -1; // EXPERIMENTAL

		/// <summary>
		/// The power plan.
		/// </summary>
		public PowerInfo.PowerMode PowerPlan = PowerInfo.PowerMode.Undefined;

		public int Recheck { get; set; } = 0;

		public bool AllowPaging { get; set; } = false;

		public PathVisibilityOptions PathVisibility { get; set; } = PathVisibilityOptions.Process;

		string PathMask { get; set; } = string.Empty; // UNUSED

		/// <summary>
		/// Controls whether this particular controller allows itself to be logged.
		/// </summary>
		public bool LogAdjusts { get; set; } = true;
		
		/// <summary>
		/// Delay in milliseconds before we attempt to alter the process.
		/// For example, to allow a process to function at default settings for a while, or to work around undesirable settings
		/// the process sets for itself.
		/// </summary>
		public int ModifyDelay { get; set; } = 0;

		public ProcessController(string name, ProcessPriorityClass? priority = null, int affinity = -1)
		{
			FriendlyName = name;
			// Executable = executable;

			Priority = priority;
			if (affinity >= 0)
			{
				AffinityMask = affinity;
				AffinityStrategy = ProcessAffinityStrategy.Limit;
			}
		}

		const string watchlistfile = "Watchlist.ini";

		public void SetForegroundMode(ForegroundMode mode)
		{
			Foreground = mode;

			switch (mode)
			{
				case ForegroundMode.Ignore:
					Refresh();
					break;
				case ForegroundMode.Standard:
					// TODO: clear power
					ClearPower();
					break;
				case ForegroundMode.Full:
					break;
				case ForegroundMode.PowerOnly:
					ClearActive();
					break;
			}
		}

		void ClearActive()
		{
			foreach (var info in ActiveWait.Values)
			{
				info.Paused = false;
				info.ForegroundWait = false;
			}
		}

		void ClearPower()
		{
			foreach (var info in ActiveWait.Values)
			{
				if (info.PowerWait)
				{
					Taskmaster.powermanager?.Release(info.Id);
					info.PowerWait = false;
				}
			}
		}

		void Prepare()
		{
			if (PathElements == 0 && !string.IsNullOrEmpty(Path))
			{
				for (int i = 0; i < Path.Length; i++)
				{
					char c = Path[i];
					if (c == System.IO.Path.DirectorySeparatorChar || c == System.IO.Path.AltDirectorySeparatorChar) PathElements++;
				}

				if (!(Path.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString()) || Path.EndsWith(System.IO.Path.AltDirectorySeparatorChar.ToString())))
					PathElements++;
			}
		}

		public void Repair()
		{
			Prepare();

			switch (Foreground)
			{
				case ForegroundMode.Ignore:
					BackgroundAffinity = -1;
					BackgroundPriority = null;
					BackgroundPowerdown = false;
					break;
				case ForegroundMode.Standard:
					break;
				case ForegroundMode.Full:
					if (PowerPlan == PowerInfo.PowerMode.Undefined)
						Foreground = ForegroundMode.Standard;
					break;
				case ForegroundMode.PowerOnly:
					if (PowerPlan == PowerInfo.PowerMode.Undefined)
						Foreground = ForegroundMode.Ignore;
					break;
			}

			if (AffinityMask >= 0)
			{
				if (Bit.Count(BackgroundAffinity) > Bit.Count(AffinityMask))
				{
					// this be bad
				}
			}
			else
				BackgroundAffinity = -1;

			if (Priority.HasValue)
			{
				if (BackgroundPriority.HasValue && Priority.Value.ToInt32() < BackgroundPriority.Value.ToInt32())
				{
					// this be bad
				}
			}
			else
				BackgroundPriority = null;

			if (VolumeStrategy == AudioVolumeStrategy.Ignore)
				Volume = 0.5f;

			if (PathVisibility == PathVisibilityOptions.Invalid)
			{
				bool haveExe = !string.IsNullOrEmpty(Executable);
				bool havePath = !string.IsNullOrEmpty(Path);
				if (haveExe && havePath) PathVisibility = PathVisibilityOptions.Process;
				else if (havePath) PathVisibility = PathVisibilityOptions.Partial;
				else PathVisibility = PathVisibilityOptions.Full;
			}
		}

		public void DeleteConfig(ConfigWrapper cfg = null)
		{
			if (cfg == null)
				cfg = Taskmaster.Config.Load(watchlistfile);

			cfg.Config.Remove(FriendlyName); // remove the section, should remove items in the section
			cfg.MarkDirty();
		}

		void ProcessExitEvent(object sender, EventArgs _ea)
		{
			var process = (Process)sender;
			RecentlyModified.TryRemove(process.Id, out _);
		}

		/// <summary>
		/// End various things for the given process
		/// </summary>
		public void End(object sender, EventArgs _ea)
		{
			var process = (Process)sender;

			if (ActiveWait.TryGetValue(process.Id, out var info))
			{
				info.State = ProcessHandlingState.Exited;
				info.Paused = false; // IRRELEVANT
				info.ForegroundWait = false; // IRRELEVANT

				if (info.PowerWait && PowerPlan != PowerInfo.PowerMode.Undefined) UndoPower(info);
				info.PowerWait = false;
			}
		}

		/// <summary>
		/// Refresh the controller, freeing resources, locks, etc.
		/// </summary>
		public void Refresh()
		{
			if (Taskmaster.DebugPower || Taskmaster.DebugProcesses)
				Log.Debug($"[{FriendlyName}] Refresh");

			ClearActive();
			ClearPower();
			foreach (var info in ActiveWait.Values)
			{
				info.Process.Refresh();
				if (!info.Process.HasExited)
				{
					info.Paused = false;
					info.ForegroundWait = false;
					info.PowerWait = false;
					if (!Resize.HasValue)
						info.Process.EnableRaisingEvents = false;

					// Re-apply the controller?
				}
				else
					info.State = ProcessHandlingState.Exited;
			}

			if (!Resize.HasValue) ActiveWait.Clear();

			RecentlyModified.Clear();
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
				app[HumanReadable.System.Process.Path].StringValue = Path;
			else
				app.Remove(HumanReadable.System.Process.Path);

			if (!string.IsNullOrEmpty(Description))
				app[HumanReadable.Generic.Description].StringValue = Description;
			else
				app.Remove(HumanReadable.Generic.Description);

			if (Priority.HasValue)
			{
				app[HumanReadable.System.Process.Priority].IntValue = ProcessHelpers.PriorityToInt(Priority.Value);
				app[HumanReadable.System.Process.PriorityStrategy].IntValue = (int)PriorityStrategy;
			}
			else
			{
				app.Remove(HumanReadable.System.Process.Priority);
				app.Remove(HumanReadable.System.Process.PriorityStrategy);
			}

			if (AffinityMask >= 0)
			{
				//if (affinity == ProcessManager.allCPUsMask) affinity = 0; // convert back

				app[HumanReadable.System.Process.Affinity].IntValue = AffinityMask;
				app[HumanReadable.System.Process.AffinityStrategy].IntValue = (int)AffinityStrategy;
			}
			else
			{
				app.Remove(HumanReadable.System.Process.Affinity);
				app.Remove(HumanReadable.System.Process.AffinityStrategy);
			}

			var pmode = PowerManager.GetModeName(PowerPlan);
			if (PowerPlan != PowerInfo.PowerMode.Undefined)
				app[HumanReadable.Hardware.Power.Mode].StringValue = PowerManager.GetModeName(PowerPlan);
			else
				app.Remove(HumanReadable.Hardware.Power.Mode);

			switch (Foreground)
			{
				case ForegroundMode.Ignore:
					app.Remove("Background powerdown");
					clearNonPower:
					app.Remove("Foreground only");
					app.Remove("Foreground mode");
					app.Remove("Background priority");
					app.Remove("Background affinity");
					break;
				case ForegroundMode.Standard:
					app.Remove("Background powerdown");
					saveFgMode:
					app["Foreground mode"].IntValue = (int)Foreground;
					if (BackgroundPriority.HasValue)
						app["Background priority"].IntValue = ProcessHelpers.PriorityToInt(BackgroundPriority.Value);
					else
						app.Remove("Background priority");
					if (BackgroundAffinity >= 0)
						app["Background affinity"].IntValue = BackgroundAffinity;
					else
						app.Remove("Background affinity");
					break;
				case ForegroundMode.Full:
					goto saveFgMode;
				case ForegroundMode.PowerOnly:
					goto clearNonPower;
			}

			if (AllowPaging)
				app["Allow paging"].BoolValue = AllowPaging;
			else
				app.Remove("Allow paging");

			if (PathVisibility != PathVisibilityOptions.Invalid)
				app["Path visibility"].IntValue = (int)PathVisibility;
			else
				app.Remove("Path visibility");

			if (!string.IsNullOrEmpty(Executable))
			{
				if (app.Contains(HumanReadable.System.Process.Rescan))
				{
					app.Remove(HumanReadable.System.Process.Rescan); // OBSOLETE
					Log.Debug("<Process> Obsoleted INI cleanup: Rescan frequency");
				}
				if (Recheck > 0) app["Recheck"].IntValue = Recheck;
				else app.Remove("Recheck");
			}

			if (!Enabled) app[HumanReadable.Generic.Enabled].BoolValue = Enabled;
			else app.Remove(HumanReadable.Generic.Enabled);

			if (IgnoreList != null && IgnoreList.Length > 0)
				app[HumanReadable.Generic.Ignore].StringValueArray = IgnoreList;
			else
				app.Remove(HumanReadable.Generic.Ignore);

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

		// The following should be combined somehow?
		ConcurrentDictionary<int, ProcessEx> ActiveWait = new ConcurrentDictionary<int, ProcessEx>();

		ConcurrentDictionary<int, RecentlyModifiedInfo> RecentlyModified = new ConcurrentDictionary<int, RecentlyModifiedInfo>();

		public bool BackgroundPowerdown { get; set; } = true;
		public ProcessPriorityClass? BackgroundPriority { get; set; } = null;
		public int BackgroundAffinity { get; set; } = -1;

		/// <summary>
		/// Pause the specified foreground process.
		/// </summary>
		public void Pause(ProcessEx info, bool firsttime = false)
		{
			Debug.Assert(Foreground != ForegroundMode.Ignore, "Pause called for non-foreground only rule");
			Debug.Assert(info.Controller != null, "No controller attached");

			//Debug.Assert(!PausedIds.ContainsKey(info.Id));

			if (info.Paused) return; // already paused

			if (Taskmaster.DebugForeground && Taskmaster.Trace)
				Log.Debug("[" + FriendlyName + "] Quelling " + info.Name + " (#" + info.Id + ")");

			// PausedState.Affinity = Affinity;
			// PausedState.Priority = Priority;
			// PausedState.PowerMode = PowerPlan;

			bool mAffinity = false, mPriority = false;
			ProcessPriorityClass oldPriority = ProcessPriorityClass.RealTime;
			int oldAffinity = -1;

			try
			{
				oldPriority = info.Process.PriorityClass;
				if (BackgroundPriority.HasValue)
				{
					if (oldPriority != BackgroundPriority.Value)
					{
						info.Process.PriorityClass = BackgroundPriority.Value;
						mPriority = true;
					}
				}

				oldAffinity = info.Process.ProcessorAffinity.ToInt32();
				if (BackgroundAffinity >= 0)
				{
					if (oldAffinity != BackgroundAffinity)
					{
						info.Process.ProcessorAffinity = new IntPtr(BackgroundAffinity.Replace(0, ProcessManager.AllCPUsMask));
						mAffinity = true;
					}
				}
				else if (AffinityMask >= 0 && EstablishNewAffinity(oldAffinity, out int newAffinityMask)) // set foreground affinity otherwise
				{
					info.Process.ProcessorAffinity = new IntPtr(BackgroundAffinity.Replace(0, ProcessManager.AllCPUsMask));
					mAffinity = true;
				}
			}
			catch (OutOfMemoryException) { throw; }
			catch { }
			// info.Process.ProcessorAffinity = OriginalState.Affinity;

			if (Taskmaster.PowerManagerEnabled)
			{
				if (PowerPlan != PowerInfo.PowerMode.Undefined)
				{
					if (BackgroundPowerdown)
					{
						if (Taskmaster.DebugPower)
							Log.Debug("[" + FriendlyName + "] " + info.Name + " (#" + info.Id + ") power down");

						UndoPower(info);
					}
					else
					{
						SetPower(info); // kinda hackish to call this here, but...
					}
				}
			}

			info.State = ProcessHandlingState.Paused;

			if (Taskmaster.ShowProcessAdjusts && firsttime)
			{
				var ev = new ProcessModificationEventArgs(info)
				{
					PriorityNew = mPriority ? BackgroundPriority : null,
					PriorityOld = oldPriority,
					AffinityNew = mAffinity ? BackgroundAffinity : -1,
					AffinityOld = oldAffinity,
				};

				ev.User = new System.Text.StringBuilder();

				ev.User.Append(" – Background Mode");

				LogAdjust(ev);
			}

			Paused?.Invoke(this, new ProcessModificationEventArgs(info));
		}

		void LogAdjust(ProcessModificationEventArgs ev)
		{
			if (!LogAdjusts) return;

			var sbs = new System.Text.StringBuilder();
			sbs.Append("[").Append(FriendlyName).Append("] ").Append(FormatPathName(ev.Info))
				.Append(" (#").Append(ev.Info.Id).Append(")");

			sbs.Append("; Priority: ");
			if (ev.PriorityOld.HasValue)
			{
				sbs.Append(Readable.ProcessPriority(ev.PriorityOld.Value));
				if (ev.PriorityNew.HasValue)
					sbs.Append(" → ").Append(Readable.ProcessPriority(ev.PriorityNew.Value));

				if (Priority.HasValue && ev.Info.State == ProcessHandlingState.Paused && Priority != ev.PriorityNew)
					sbs.Append($" [{ProcessHelpers.PriorityToInt(Priority.Value)}]");

				if (ev.PriorityFail) sbs.Append(" [Failed]");
				if (ev.Protected) sbs.Append(" [Protected]");
			}
			else
				sbs.Append(HumanReadable.Generic.NotAvailable);

			sbs.Append("; Affinity: ");
			if (ev.AffinityOld >= 0)
			{
				sbs.Append(ev.AffinityOld);
				if (ev.AffinityNew >= 0)
					sbs.Append(" → ").Append(ev.AffinityNew);

				if (AffinityMask >= 0 && ev.Info.State == ProcessHandlingState.Paused && AffinityMask != ev.AffinityNew)
					sbs.Append($" [{AffinityMask}]");
			}
			else
				sbs.Append(HumanReadable.Generic.NotAvailable);

			if (ev.AffinityFail) sbs.Append(" [Failed]");

			if (Taskmaster.DebugProcesses) sbs.Append(" [").Append(AffinityStrategy.ToString()).Append("]");

			if (ev.User != null) sbs.Append(ev.User);

            if (Taskmaster.DebugAdjustDelay)
            {
                sbs.Append(" – ").Append($"{ev.Info.Timer.ElapsedMilliseconds:N0} ms");
                if (ev.Info.WMIDelay > 0) sbs.Append(" + ").Append(ev.Info.WMIDelay).Append(" ms watcher delay");
            }

			// TODO: Add option to logging to file but still show in UI
			if (!(Taskmaster.ShowInaction && Taskmaster.DebugProcesses)) Log.Information(sbs.ToString());
			else Log.Debug(sbs.ToString());

			ev.User?.Clear();
			ev.User = null;
			sbs.Clear();
			sbs = null;
		}

		public void Resume(ProcessEx info)
		{
			Debug.Assert(Foreground != ForegroundMode.Ignore, "Resume called for non-foreground rule");
			Debug.Assert(info.Controller != null, "No controller attached");

			bool mAffinity = false, mPriority = false;
			ProcessPriorityClass oldPriority = ProcessPriorityClass.RealTime;
			int oldAffinity = -1, newAffinity = -1;

			if (!info.Paused)
			{
				if (Taskmaster.DebugForeground)
					Log.Debug("<Foreground> " + FormatPathName(info) + " (#" + info.Id + ") not paused; not resuming.");
				return; // can't resume unpaused item
			}

			try
			{
				oldPriority = info.Process.PriorityClass;
				if (Priority.HasValue && info.Process.PriorityClass.ToInt32() != Priority.Value.ToInt32())
				{
					info.Process.SetLimitedPriority(Priority.Value, PriorityStrategy);
					mPriority = true;
				}

				oldAffinity = info.Process.ProcessorAffinity.ToInt32();
				if (AffinityMask >= 0)
				{
					if (EstablishNewAffinity(oldAffinity, out newAffinity))
					{
						info.Process.ProcessorAffinity = new IntPtr(newAffinity.Replace(0, ProcessManager.AllCPUsMask));
						mAffinity = true;
					}
				}

				if (AffinityIdeal >= 0) ApplyAffinityIdeal(info);
			}
			catch (InvalidOperationException) // ID not available, probably exited
			{
				info.Paused = false;
				return;
			}
			catch (Win32Exception) // access error
			{
				info.Paused = false;
				return;
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				info.Paused = false;
				return;
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

			info.Paused = false;

			info.State = ProcessHandlingState.Resumed;

			if (Taskmaster.DebugForeground && Taskmaster.ShowProcessAdjusts)
			{
				var ev = new ProcessModificationEventArgs(info)
				{
					PriorityNew = mPriority ? (ProcessPriorityClass?)Priority.Value : null,
					PriorityOld = oldPriority,
					AffinityNew = mAffinity ? newAffinity : -1,
					AffinityOld = oldAffinity,
				};

				ev.User = new System.Text.StringBuilder();

				ev.User.Append(" – Foreground Mode");

				LogAdjust(ev);
			}

			Resumed?.Invoke(this, new ProcessModificationEventArgs(info));
		}

		/// <summary>
		/// How many times we've touched associated processes.
		/// </summary>
		public int Adjusts { get; set; } = 0;
		/// <summary>
		/// Last seen any associated process.
		/// </summary>
		public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.MinValue;
		/// <summary>
		/// Last modified any associated process.
		/// </summary>
		public DateTimeOffset LastTouch { get; set; } = DateTimeOffset.MinValue;

		/*
		public bool Children = false;
		public ProcessPriorityClass ChildPriority = ProcessPriorityClass.Normal;
		public bool ChildPriorityReduction = false;
		*/

		// -----------------------------------------------

		void ProcessEnd(object _, EventArgs _ea)
		{
		}

		// -----------------------------------------------

		bool SetPower(ProcessEx info)
		{
			Debug.Assert(Taskmaster.PowerManagerEnabled, "SetPower called despite power manager being disabled");
			Debug.Assert(PowerPlan != PowerInfo.PowerMode.Undefined, "Powerplan is undefined");
			Debug.Assert(info.Controller != null, "No controller attached");

			bool rv = false;

			try
			{
				info.PowerWait = true;
				rv = Taskmaster.powermanager.Force(PowerPlan, info.Id);

				WaitForExit(info);

				WaitingExit?.Invoke(this, new ProcessModificationEventArgs(info));

				if (Taskmaster.DebugPower) Log.Debug($"[{FriendlyName}] {FormatPathName(info)} (#{info.Id.ToString()}) power exit wait set");
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				throw;
			}

			return rv;
		}

		void UndoPower(ProcessEx info)
		{
			if (info.PowerWait) Taskmaster.powermanager?.Release(info.Id);
		}

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
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex) { Logging.Stacktrace(ex); }

			return false;
		}

		static string[] UnwantedPathBits = new string[] { "x64", "x86", "bin", "debug", "release", "win32", "win64", "common", "binaries" };
		static string[] SpecialCasePathBits = new string[] { "steamapps" };

		string FormatPathName(ProcessEx info)
		{
			if (!string.IsNullOrEmpty(info.FormattedPath)) return info.FormattedPath;

			if (!string.IsNullOrEmpty(info.Path))
			{
				switch (PathVisibility)
				{
					default:
					case PathVisibilityOptions.Process:
						return info.Name;
					case PathVisibilityOptions.Partial:
						if (PathElements > 0)
						{
							List<string> parts = new List<string>(info.Path.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
							// replace Path
							parts.RemoveRange(0, PathElements);
							parts.Insert(0, HumanReadable.Generic.Ellipsis);
							return info.FormattedPath = System.IO.Path.Combine(parts.ToArray());
						}
						else
							return info.Path;
					case PathVisibilityOptions.Smart:
						{
							// TODO: Cut off bin, x86, x64, win64, win32 or similar generic folder parts
							List<string> parts = new List<string>(info.Path.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
							if (PathElements > 0)
							{
								// cut Path from the output
								// following notes assume matching c:\program files

								parts.RemoveRange(0, PathElements); // remove Path component

								// replace 
								if (parts.Count > 4)
								{
									// TODO: Special cases for %APPDATA% and similar?

									// c:\program files\brand\app\version\random\element\executable.exe
									// ...\brand\app\...\executable.exe
									//parts.RemoveRange(3, parts.Count - 4); // remove all but two first and last
									//parts.Insert(3, HumanReadable.Generic.Ellipsis);

									bool replaced = false;
									// remove unwanted bits
									for (int i = 0; i < parts.Count; i++)
									{
										string cur = parts[i].ToLowerInvariant();
										if (SpecialCasePathBits.Contains(cur)) // steamapps
										{
											parts[i] = HumanReadable.Generic.Ellipsis;
											parts.RemoveAt(++i); // common, i at app name, rolled over with loop
											replaced = false;
										}
										else if ((i > 2 && i < parts.Count - 3 ) || UnwantedPathBits.Contains(cur))
										{
											if (replaced)
												parts.RemoveAt(i--); // remove current and roll back loop
											else
												parts[i] = HumanReadable.Generic.Ellipsis;

											replaced = true;
										}
										else
											replaced = false;
									}
								}

								parts.Insert(0, HumanReadable.Generic.Ellipsis); // add starting ellipsis

								// ...\brand\app\app.exe
							}
							else if (parts.Count <= 5) return info.Path; // as is
							else
							{
								// Minimal structure
								// drive A B C file
								// 1 2 3 4 5
								// c:\programs\brand\app\app.exe as is
								// c:\programs\brand\app\v2.5\x256\bin\app.exe -> c:\programs\brand\app\...\app.exe

								parts.RemoveRange(4, parts.Count - 5);
								parts[0] = parts[0] + System.IO.Path.DirectorySeparatorChar; // Path.Combine handles drive letter weird
								parts.Insert(parts.Count - 1, HumanReadable.Generic.Ellipsis);
							}

							return info.FormattedPath = System.IO.Path.Combine(parts.ToArray());
						}
					case PathVisibilityOptions.Full:
						return info.Path;
				}
			}
			else
				return info.Name; // NAME
		}

		public async void Modify(ProcessEx info)
		{
			await Touch(info);
			if (Recheck > 0) TouchReapply(info); // this can go do its thing
		}

		public bool MatchPath(string path)
		{
			// TODO: make this compatible with OSes that aren't case insensitive?
			return path.StartsWith(Path, StringComparison.InvariantCultureIgnoreCase);
		}

		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		bool InForeground(int pid) => Foreground != ForegroundMode.Ignore ? Taskmaster.activeappmonitor?.Foreground.Equals(pid) ?? true : true;

		bool WaitForExit(ProcessEx info)
		{
			ActiveWait.TryAdd(info.Id, info);

			try
			{
				WaitingExit?.Invoke(this, new ProcessModificationEventArgs(info));
				info.Process.EnableRaisingEvents = true;
				info.Process.Exited += End;
				info.Process.Refresh();
				if (info.Process.HasExited)
				{
					End(info.Process, null);
					return false;
				}
			}
			catch
			{
				End(info.Process, null);
				throw;
				return false;
			}

			return true;
		}

		// TODO: Simplify this
		async Task Touch(ProcessEx info, bool refresh = false)
		{
			Debug.Assert(info.Process != null, "ProcessController.Touch given null process.");
			Debug.Assert(!ProcessManager.SystemProcessId(info.Id), "ProcessController.Touch given invalid process ID");
			Debug.Assert(!string.IsNullOrEmpty(info.Name), "ProcessController.Touch given empty process name.");
			Debug.Assert(info.Controller != null, "No controller attached");

			try
			{
				if (Foreground != ForegroundMode.Ignore && info.Paused)
				{
					if (Taskmaster.Trace && Taskmaster.DebugForeground)
						Log.Debug("<Foreground> " + FormatPathName(info) + " (#" + info.Id + ") in background, ignoring.");
					info.State = ProcessHandlingState.Paused;
					return; // don't touch paused item
				}

				info.PowerWait = (PowerPlan != PowerInfo.PowerMode.Undefined);
				info.ForegroundWait = Foreground != ForegroundMode.Ignore;

				bool responding = true;
				ProcessPriorityClass? oldPriority = null;
				IntPtr? oldAffinity = null;

				int oldAffinityMask = 0;
				var oldPower = PowerInfo.PowerMode.Undefined;


                await Task.Delay(refresh ? 0 : ModifyDelay).ConfigureAwait(false);

				// EXTRACT INFORMATION

				try
				{
					if (ModifyDelay > 0) info.Process.Refresh();

					responding = info.Process.Responding;

					if (info.Process.HasExited)
					{
						if (Taskmaster.DebugProcesses)
							Log.Debug("[" + FriendlyName + "] " + info.Name + " (#" + info.Id + ") has already exited.");
						info.State = ProcessHandlingState.Exited;
						return; // return ProcessState.Invalid;
					}

					oldAffinity = info.Process.ProcessorAffinity;
					oldPriority = info.Process.PriorityClass;
					oldAffinityMask = oldAffinity.Value.ToInt32().Replace(0, ProcessManager.AllCPUsMask);
				}
				catch (InvalidOperationException) // Already exited
				{
					info.State = ProcessHandlingState.Exited;
					return;
				}
				catch (Win32Exception) // denied
				{
					// failure to retrieve exit code, this probably means we don't have sufficient rights. assume it is gone.
					info.State = ProcessHandlingState.AccessDenied;
					return;
				}
				catch (OutOfMemoryException) { throw; }
				catch (Exception ex) // invalidoperation or notsupported, neither should happen
				{
					Logging.Stacktrace(ex);
					info.State = ProcessHandlingState.Invalid;
					return;
				}

				var now = DateTimeOffset.UtcNow;

				// TEST FOR RECENTLY MODIFIED
				if (RecentlyModified.TryGetValue(info.Id, out RecentlyModifiedInfo ormt))
				{
					LastSeen = DateTimeOffset.UtcNow;

						if (ormt.Info.Name.Equals(info.Name))
						{
							if (ormt.FreeWill)
							{
								if (Taskmaster.ShowInaction && Taskmaster.DebugProcesses)
									Log.Debug($"[{FriendlyName}] {FormatPathName(info)} (#{info.Id.ToString()}) has been granted agency, ignoring.");
								info.State = ProcessHandlingState.Abandoned;
								return;
							}

							bool expected = false;
							if ((Priority.HasValue && info.Process.PriorityClass != Priority.Value) ||
								(AffinityMask >= 0 && info.Process.ProcessorAffinity.ToInt32() != AffinityMask))
							{
								ormt.ExpectedState--;
								Debug.WriteLine($"[{FriendlyName}] {FormatPathName(info)} (#{info.Id.ToString()}) Recently Modified ({ormt.ExpectedState}) ---");
							}
							else
							{
								ormt.ExpectedState++;
								Debug.WriteLine($"[{FriendlyName}] {FormatPathName(info)} (#{info.Id.ToString()}) Recently Modified ({ormt.ExpectedState}) +++");
								expected = true;
							}

							if (ormt.LastIgnored.TimeTo(now) < ProcessManager.IgnoreRecentlyModified ||
								ormt.LastModified.TimeTo(now) < ProcessManager.IgnoreRecentlyModified)
							{
								if (Taskmaster.DebugProcesses) Log.Debug("[" + FriendlyName + "] #" + info.Id + " ignored due to recent modification." +
									(expected ? $" Expected: {ormt.ExpectedState} :)" : $" Unexpected: {ormt.ExpectedState} :("));

								if (ormt.ExpectedState == -2) // 2-3 seems good number
								{
									ormt.FreeWill = true;
									if (Taskmaster.ShowAgency)
										Log.Debug($"[{FriendlyName}] {FormatPathName(info)} (#{info.Id.ToString()}) is resisting being modified: Agency granted.");
									// TODO: Let it be.
									ormt.Info.Process.Exited += ProcessExitEvent;
								}

								ormt.LastIgnored = now;

								Statistics.TouchIgnore++;

								info.State = ProcessHandlingState.Unmodified;
								return;
							}
							else if (expected)
							{
								// this potentially ignores power modification
								info.State = ProcessHandlingState.Unmodified;
								return;
							}
						}
						else
						{
							if (Taskmaster.DebugProcesses) Log.Debug("[" + FriendlyName + "] #" + info.Id.ToString() + " passed because name does not match; new: " +
								info.Name + ", old: " + ormt.Info.Name);

							RecentlyModified.TryRemove(info.Id, out _); // id does not match name
						}
					//RecentlyModified.TryRemove(info.Id, out _);
				}

				if (Taskmaster.Trace) Log.Verbose("[" + FriendlyName + "] Touching: " + info.Name + " (#" + info.Id.ToString() + ")");

				if (IgnoreList != null && IgnoreList.Any(item => item.Equals(info.Name, StringComparison.InvariantCultureIgnoreCase)))
				{
					if (Taskmaster.ShowInaction && Taskmaster.DebugProcesses)
						Log.Debug("[" + FriendlyName + "] " + info.Name + " (#" + info.Id.ToString() + ") ignored due to user defined rule.");
					info.State = ProcessHandlingState.Abandoned;
					return; // return ProcessState.Ignored;
				}

				info.Valid = true;

				bool isProtectedFile = ProcessManager.ProtectedProcessName(info.Name);
				// TODO: IgnoreSystem32Path

				if (isProtectedFile && Taskmaster.ShowInaction && Taskmaster.DebugProcesses)
					Log.Debug("[" + FriendlyName + "] " + info.Name + " (#" + info.Id.ToString() + ") in protected list, limiting tampering.");

				ProcessPriorityClass? newPriority = null;
				IntPtr? newAffinity = null;

				bool mAffinity = false, mPriority = false, mPower = false, modified = false, fAffinity = false, fPriority = false;

				bool foreground = InForeground(info.Id);

				bool FirstTimeSeenForForeground = true;
				if (!isProtectedFile)
				{
					if (Foreground != ForegroundMode.Ignore)
					{
						if (!info.ForegroundWait)
						{
							info.ForegroundWait = true;
							WaitForExit(info);
						}
						else
							FirstTimeSeenForForeground = false;

						if (!foreground)
						{
							if (Taskmaster.DebugForeground || Taskmaster.ShowInaction)
								Log.Debug("[" + FriendlyName + "] " + info.Name + " (#" + info.Id + ") not in foreground, not prioritizing.");

							Pause(info, FirstTimeSeenForForeground);
							// info.State = ProcessHandlingState.Paused; // Pause() sets this
							return;
						}
					}
				}
				else
				{
					if (Taskmaster.ShowInaction && Taskmaster.DebugProcesses)
						Log.Verbose("[" + FriendlyName + "] " + info.Name + " (#" + info.Id + ") PROTECTED");
				}

				// APPLY CHANGES HERE
				if (!isProtectedFile)
				{
					try
					{
						if (info.Process.SetLimitedPriority(Priority.Value, PriorityStrategy))
						{
							modified = mPriority = true;
							newPriority = info.Process.PriorityClass;
						}
					}
					catch (OutOfMemoryException) { throw; }
					catch { fPriority = true; } // ignore errors, this is all we care of them
				}

				int newAffinityMask = -1;

				if (AffinityMask >= 0)
				{
					newAffinityMask = AffinityMask.Replace(0, ProcessManager.AllCPUsMask);

					if (EstablishNewAffinity(oldAffinityMask, out newAffinityMask))
					{
						newAffinity = new IntPtr(newAffinityMask.Replace(0, ProcessManager.AllCPUsMask));
						try
						{
							info.Process.ProcessorAffinity = newAffinity.Value;
							modified = mAffinity = true;
						}
						catch (OutOfMemoryException) { throw; }
						catch { fAffinity = true; } // ignore errors, this is all we care of them
					}
					else
						newAffinityMask = -1;
					
					/*
					if (oldAffinityMask != newAffinityMask)
					{
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
						// int bitsnew = Bit.Count(newAffinityMask);
						// TODO: Somehow shift bits old to new if there's free spots
					}
					*/
				}
				else
				{
					if (Taskmaster.DebugProcesses) Debug.WriteLine($"{FormatPathName(info)} #{info.Id.ToString()} --- affinity not touched");
				}

				if (AffinityIdeal >= 0)
					ApplyAffinityIdeal(info);

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
				}
				*/

				//var oldPP = PowerInfo.PowerMode.Undefined;
				if (Taskmaster.PowerManagerEnabled && PowerPlan != PowerInfo.PowerMode.Undefined)
				{
					if (!foreground && BackgroundPowerdown)
					{
						if (Taskmaster.DebugForeground)
							Log.Debug(info.Name + " (#" + info.Id + ") not in foreground, not powering up.");
					}
					else
					{
						mPower = SetPower(info);
					}
				}

                info.Timer.Stop();

				if (modified)
				{
					if (mPriority || mAffinity)
					{
						Statistics.TouchCount++;
						Adjusts += 1; // don't increment on power changes
					}

					LastTouch = now;
				}

				if (Priority.HasValue)
				{
					if (fPriority && Taskmaster.ShowInaction && Taskmaster.DebugProcesses)
						Log.Warning("[" + FriendlyName + "] " + info.Name + " (#" + info.Id + ") failed to set process priority.");
				}
				if (AffinityMask >= 0)
				{
					if (fAffinity && Taskmaster.ShowInaction && Taskmaster.DebugProcesses)
						Log.Warning("[" + FriendlyName + "] " + info.Name + " (#" + info.Id + ") failed to set process affinity.");
				}

				bool logevent = false;

				if (modified) logevent = Taskmaster.ShowProcessAdjusts && !(Foreground != ForegroundMode.Ignore && !Taskmaster.ShowForegroundTransitions);
				logevent |= (FirstTimeSeenForForeground && Foreground != ForegroundMode.Ignore);
				logevent |= (Taskmaster.ShowInaction && Taskmaster.DebugProcesses);

				var ev = new ProcessModificationEventArgs(info)
				{
					PriorityNew = newPriority,
					PriorityOld = oldPriority,
					AffinityNew = newAffinityMask,
					AffinityOld = oldAffinityMask,
					PriorityFail = Priority.HasValue && fPriority,
					AffinityFail = AffinityMask >= 0 && fAffinity,
					Protected = isProtectedFile,
				};

				if (logevent)
				{
					var sbs = new System.Text.StringBuilder();

					if (mPower) sbs.Append(" [Power Mode: ").Append(PowerManager.GetModeName(PowerPlan)).Append("]");

					if (!modified && (Taskmaster.ShowInaction && Taskmaster.DebugProcesses)) sbs.Append(" – looks OK, not touched.");

					ev.User = sbs;

					LogAdjust(ev);
				}

				if (modified)
				{
					info.State = ProcessHandlingState.Modified;
					Modified?.Invoke(this, ev);

					if (ProcessManager.IgnoreRecentlyModified.HasValue)
					{
						var rmt = new RecentlyModifiedInfo()
						{
							Info = info,
							LastModified = now,
							LastIgnored = DateTimeOffset.MinValue,
							FreeWill = false,
							ExpectedState = 0,
						};

						RecentlyModified.AddOrUpdate(info.Id, rmt, (int key, RecentlyModifiedInfo nrmt)=> {
							if (!nrmt.Info.Name.Equals(info.Name))
							{
								// REPLACE. THIS SEEMS WRONG
								nrmt.Info = info;
								nrmt.FreeWill = false;
								nrmt.ExpectedState = 0;
								nrmt.LastModified = now;
								nrmt.LastIgnored = DateTimeOffset.MinValue;
							}
							return nrmt;
						});
					}
					InternalRefresh(now);
				}
				else
					info.State = ProcessHandlingState.Finished;
			}
			catch (OutOfMemoryException) { info.State = ProcessHandlingState.Abandoned; throw; }
			catch (Exception ex)
			{
				info.State = ProcessHandlingState.Invalid;
				Logging.Stacktrace(ex);
			}
		}

		/// <summary>
		/// Sets new affinity mask. Returns true if the new mask differs from old.
		/// </summary>
		bool EstablishNewAffinity(int oldmask, out int newmask)
		{
			// TODO: Apply affinity strategy
			newmask = ProcessManagerUtility.ApplyAffinityStrategy(oldmask, AffinityMask, AffinityStrategy);
			return (newmask != oldmask);
		}

		void ApplyAffinity(ProcessEx info)
		{

		}

		void ApplyAffinityIdeal(ProcessEx info)
		{
			try
			{
				var threads = info.Process.Threads;
				threads[0].IdealProcessor = AffinityIdeal;
				// Is there benefit for changing only the first/primary thread?
				// Optimistically the main thread constains the app main loop and other core functions.
				// This is not guaranteed however.
			}
			catch (OutOfMemoryException) { throw; }
			catch (Win32Exception) { } // NOP; Access denied or such. Possibly malconfigured ideal
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		int refresh_lock = 0;
		async Task InternalRefresh(DateTimeOffset now)
		{
			if (!Atomic.Lock(ref refresh_lock)) return;

			await Task.Delay(0).ConfigureAwait(false);

			try
			{
				if (RecentlyModified.Count > 5)
				{
					foreach (var r in RecentlyModified)
					{
						if ((r.Value.LastIgnored.TimeTo(now) > ProcessManager.IgnoreRecentlyModified)
							|| (r.Value.LastModified.TimeTo(now) > ProcessManager.IgnoreRecentlyModified))
							RecentlyModified.TryRemove(r.Key, out _);
					}
				}
			}
			finally
			{
				Atomic.Unlock(ref refresh_lock);
			}
		}

		public async Task TryResize(ProcessEx info)
		{
			Debug.Assert(Resize.HasValue, "Trying to resize when resize is not defined");

			await Task.Delay(0).ConfigureAwait(false); // asyncify

			bool gotCurrentSize = false, sizeChanging = false;

			try
			{
				if (ActiveWait.TryGetValue(info.Id, out var oinfo) && oinfo.Resize)
				{
					if (Taskmaster.DebugResize) Log.Debug($"<Resize> Already monitoring {info.Name} (#{info.Id.ToString()})");
					return;
				}

				NativeMethods.RECT rect = new NativeMethods.RECT();

				IntPtr hwnd = info.Process.MainWindowHandle;
				if (NativeMethods.GetWindowRect(hwnd, ref rect))
					gotCurrentSize = true;

				var oldrect = new System.Drawing.Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

				var newsize = new System.Drawing.Rectangle(
					Resize.Value.Left != 0 ? Resize.Value.Left : oldrect.Left,
					Resize.Value.Top != 0 ? Resize.Value.Top : oldrect.Top,
					Resize.Value.Width != 0 ? Resize.Value.Width : oldrect.Width,
					Resize.Value.Height != 0 ? Resize.Value.Height : oldrect.Height
					);

				if (!newsize.Equals(oldrect))
				{
					sizeChanging = true;

					// TODO: Add option to monitor the app and save the new size so relaunching the app keeps the size.

					NativeMethods.MoveWindow(hwnd, newsize.Left, newsize.Top, newsize.Width, newsize.Height, true);

					if (ResizeStrategy != WindowResizeStrategy.None)
					{
						Resize = newsize;
						NeedsSaving = true;
					}
				}

				StringBuilder sbs = null;
				if (Taskmaster.DebugResize)
				{
					sbs = new StringBuilder();
					sbs.Append("<Resize> ").Append(info.Name).Append(" (#").Append(info.Id).Append(")");
					if (!gotCurrentSize)
					{
						sbs.Append(" Failed to get current size");
						if (sizeChanging) sbs.Append(";");
					}
					if (sizeChanging)
					{
						sbs.Append(" Changing");
						if (gotCurrentSize) sbs.Append(" from ").Append(oldrect.Width).Append("×").Append(oldrect.Height);
						sbs.Append(" to ").Append(newsize.Width).Append("×").Append(newsize.Height);
					}
				}

				if (ResizeStrategy == WindowResizeStrategy.None)
				{
					if (Taskmaster.DebugResize)
					{
						sbs.Append("; remembering size or pos not enabled.");
						Log.Debug(sbs.ToString());
					}
					return;
				}
				else
				{
					if (Taskmaster.DebugResize) Log.Debug(sbs.ToString());
				}

				info.Resize = true;
				ActiveWait.TryAdd(info.Id, info);

				System.Threading.ManualResetEvent re = new System.Threading.ManualResetEvent(false);
				Task.Run(() =>
				{
					if (Taskmaster.DebugResize) Log.Debug($"<Resize> Starting monitoring {info.Name} (#{info.Id.ToString()})");
					try
					{
						while (!re.WaitOne(60_000))
						{
							if (Taskmaster.DebugResize) Log.Debug($"<Resize> Recording size and position for {info.Name} (#{info.Id.ToString()})");

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
					catch (OutOfMemoryException) { throw; }
					catch (Exception ex)
					{
						Logging.Stacktrace(ex);
					}
					if (Taskmaster.DebugResize) Log.Debug($"<Resize> Stopping monitoring {info.Name} (#{info.Id.ToString()})");
				}).ConfigureAwait(false);

				if (WaitForExit(info))
				{
					info.Process.EnableRaisingEvents = true;
					info.Process.Exited += (_, _ea) => ProcessEndResize(info, oldrect, re);
					// TODO: 
					info.Process.Refresh();
					if (info.Process.HasExited && info.Resize)
					{
						info.State = ProcessHandlingState.Exited;
						ProcessEndResize(info, oldrect, re);
					}
				}
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				if (Taskmaster.DebugResize) Log.Debug($"<Resize> Attempt failed for {info.Name} (#{info.Id.ToString()})");

				Logging.Stacktrace(ex);
				info.Resize = false;
			}
		}

		void ProcessEndResize(ProcessEx info, System.Drawing.Rectangle oldrect, System.Threading.ManualResetEvent re)
		{
			try
			{
				info.Resize = false;

				re.Set();
				re.Reset();

				if (!ResizeWaitList.TryRemove(info.Id, out _))
				{
					//Log.Debug("Process Exit");
				}

				if ((Bit.IsSet(((int)ResizeStrategy), (int)WindowResizeStrategy.Size)
					&& (oldrect.Width != Resize.Value.Width || oldrect.Height != Resize.Value.Height))
					|| (Bit.IsSet(((int)ResizeStrategy), (int)WindowResizeStrategy.Position)
					&& (oldrect.Left != Resize.Value.Left || oldrect.Top != Resize.Value.Top)))
				{
					if (Taskmaster.DebugResize)
						Log.Debug("Saving " + info.Name + " (#" + info.Id + ") size to " +
							Resize.Value.Width + "×" + Resize.Value.Height);

					NeedsSaving = true;
				}
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				ResizeWaitList.TryRemove(info.Id, out _);
			}
		}

		public bool NeedsSaving = false;

		public WindowResizeStrategy ResizeStrategy = WindowResizeStrategy.None;

		public bool RememberSize = false;
		public bool RememberPos = false;
		//public int[] Resize = null;
		public System.Drawing.Rectangle? Resize = null;
		ConcurrentDictionary<int, int> ResizeWaitList = new ConcurrentDictionary<int, int>();

		async Task TouchReapply(ProcessEx info)
		{
			await Task.Delay(Math.Max(Recheck, 5) * 1_000).ConfigureAwait(false);

			if (Taskmaster.DebugProcesses)
				Log.Debug("[" + FriendlyName + "] " + info.Name + " (#" + info.Id + ") rechecking");

			try
			{
				info.Process.Refresh();
				if (info.Process.HasExited)
				{
					info.State = ProcessHandlingState.Exited;
					if (Taskmaster.Trace) Log.Verbose("[" + FriendlyName + "] " + info.Name + " (#" + info.Id + ") is gone yo.");
					return;
				}

				await Touch(info, refresh: true).ConfigureAwait(false);
			}
			catch (Win32Exception) // access denied
			{
				return;
			}
			catch (InvalidOperationException) // exited
			{
				return;
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Log.Warning("[" + FriendlyName + "] " + info.Name + " (#" + info.Id + ") – something bad happened.");
				Logging.Stacktrace(ex);
                info.State = ProcessHandlingState.Abandoned;
				return; //throw; // would throw but this is async function
			}
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

				// clear event handlers
				Modified = null;
				Paused = null;
				Resumed = null;

				if (NeedsSaving) SaveConfig();
			}

			disposed = true;
		}
	}

	sealed public class RecentlyModifiedInfo
	{
		public ProcessEx Info { get; set; } = null;

		public bool FreeWill = false;

		public int ExpectedState = 0;

		public DateTimeOffset LastModified = DateTimeOffset.MinValue;
		public DateTimeOffset LastIgnored = DateTimeOffset.MinValue;
	}
}