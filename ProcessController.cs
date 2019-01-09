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
		public event EventHandler<ProcessEventArgs> Modified;

		/// <summary>
		/// Process gone to background
		/// </summary>
		public event EventHandler<ProcessEventArgs> Paused;
		/// <summary>
		/// Process back on foreground.
		/// </summary>
		public event EventHandler<ProcessEventArgs> Resumed;
		/// <summary>
		/// Process is waiting for exit.
		/// </summary>
		public event EventHandler<ProcessEventArgs> WaitingExit;

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
		int PathElements { get; set; }  = 0;

		/// <summary>
		/// User description for the rule.
		/// </summary>
		public string Description { get; set; } = string.Empty;

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
		public bool ForegroundOnly { get; private set; } = false;

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

		public PathVisibilityOptions PathVisibility = PathVisibilityOptions.File;

		string PathMask = string.Empty;

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
				AffinityMask = affinity;
				AffinityStrategy = ProcessAffinityStrategy.Limit;
			}
		}

		const string watchlistfile = "Watchlist.ini";

		public void SetForegroundOnly(bool fgonly)
		{
			if (ForegroundOnly && fgonly == false)
			{
				PausedIds.Clear();
				ForegroundWatch.Clear();
			}

			ForegroundOnly = fgonly;
		}

		void Prepare()
		{
			if (PathElements == 0 && !string.IsNullOrEmpty(Path))
			{
				foreach (char c in Path)
					if (c == System.IO.Path.DirectorySeparatorChar || c == System.IO.Path.AltDirectorySeparatorChar) PathElements++;
				PathElements++;
			}
		}

		public void SanityCheck()
		{
			Prepare();

			if (ForegroundOnly)
			{
				if (BackgroundPowerdown && PowerPlan == PowerInfo.PowerMode.Undefined)
					BackgroundPowerdown = false;
			}
			else
			{
				// sanity checking
				BackgroundAffinity = -1;
				BackgroundPriority = null;
				BackgroundPowerdown = false;
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
		}

		public void DeleteConfig(ConfigWrapper cfg = null)
		{
			if (cfg == null)
				cfg = Taskmaster.Config.Load(watchlistfile);

			cfg.Config.Remove(FriendlyName); // remove the section, should remove items in the section
			cfg.MarkDirty();
		}

		/// <summary>
		/// End various things for the given process
		/// </summary>
		public void End(ProcessEx info)
		{
			ForegroundWatch.TryRemove(info.Id, out _);
			PausedIds.TryRemove(info.Id, out _);

			if (info.PowerWait)
				Taskmaster.powermanager.Release(info.Id);

			PowerList.TryRemove(info.Id, out _);
		}

		/// <summary>
		/// Refresh the controller, freeing resources, locks, etc.
		/// </summary>
		public void Refresh()
		{
			//TODO: Update power
			if (PowerList != null)
			{
				foreach (int pid in PowerList.Keys.ToArray())
				{
					PowerList.TryRemove(pid, out _);
					Taskmaster.powermanager?.Release(pid);
				}
			}

			ForegroundWatch?.Clear();
			PausedIds?.Clear();
			PowerList?.Clear();

			RecentlyModified?.Clear();
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

			if (ForegroundOnly)
			{
				app["Foreground only"].BoolValue = ForegroundOnly;

				if (BackgroundPriority.HasValue)
					app["Background priority"].IntValue = ProcessHelpers.PriorityToInt(BackgroundPriority.Value);
				else
					app.Remove("Background priority");

				if (BackgroundAffinity >= 0)
					app["Background affinity"].IntValue = BackgroundAffinity;
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
				if (app.Contains("Rescan"))
				{
					app.Remove("Rescan"); // OBSOLETE
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

			if (LastSeen != DateTimeOffset.MinValue)
			{
				stats.Config[key]["Last seen"].SetValue(LastSeen.Unixstamp());
				stats.MarkDirty();
			}
		}

		// The following should be combined somehow?
		ConcurrentDictionary<int, int> PausedIds = new ConcurrentDictionary<int, int>(); // HACK: There's no ConcurrentHashSet
		ConcurrentDictionary<int, int> PowerList = new ConcurrentDictionary<int, int>(); // HACK
		ConcurrentDictionary<int, int> ForegroundWatch = new ConcurrentDictionary<int, int>(); // HACK
		ConcurrentDictionary<int, RecentlyModifiedInfo> RecentlyModified = new ConcurrentDictionary<int, RecentlyModifiedInfo>();

		public bool BackgroundPowerdown { get; set; } = true;
		public ProcessPriorityClass? BackgroundPriority { get; set; } = null;
		public int BackgroundAffinity { get; set; } = -1;

		/// <summary>
		/// Pause the specified foreground process.
		/// </summary>
		public void Pause(ProcessEx info, bool firsttime = false)
		{
			Debug.Assert(ForegroundOnly == true, "Pause called for non-foreground only rule");
			//Debug.Assert(!PausedIds.ContainsKey(info.Id));

			if (!PausedIds.TryAdd(info.Id, 0))
				return; // already paused.

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
						info.Process.ProcessorAffinity = new IntPtr(BackgroundAffinity);
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
				if (PowerPlan != PowerInfo.PowerMode.Undefined)
				{
					if (BackgroundPowerdown)
					{
						if (Taskmaster.DebugPower)
							Log.Debug("[" + FriendlyName + "] " + info.Name + " (#" + info.Id + ") background power down");

						UndoPower(info);
					}
					else
					{
						SetPower(info); // kinda hackish to call this here, but...
					}
				}
			}

			if (Taskmaster.ShowProcessAdjusts && firsttime)
			{
				var ev = new ProcessEventArgs()
				{
					PriorityNew = mPriority ? BackgroundPriority : null,
					PriorityOld = oldPriority,
					AffinityNew = mAffinity ? BackgroundAffinity : -1,
					AffinityOld = oldAffinity,
					Info = info,
					Control = this,
					State = ProcessRunningState.Reduced,
				};

				ev.User = new System.Text.StringBuilder();

				ev.User.Append(" – Background Mode");

				LogAdjust(ev);
			}

			info.State = ProcessModification.Paused;

			Paused?.Invoke(this, new ProcessEventArgs() { Control = this, Info = info, State = ProcessRunningState.Reduced });
		}

		void LogAdjust(ProcessEventArgs ev)
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

				if (Priority.HasValue && ev.State == ProcessRunningState.Reduced && Priority != ev.PriorityNew)
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

				if (AffinityMask >= 0 && ev.State == ProcessRunningState.Reduced && AffinityMask != ev.AffinityNew)
					sbs.Append($" [{AffinityMask}]");
			}
			else
				sbs.Append(HumanReadable.Generic.NotAvailable);

			if (ev.AffinityFail) sbs.Append(" [Failed]");

			if (Taskmaster.DebugProcesses) sbs.Append(" [").Append(AffinityStrategy.ToString()).Append("]");

			if (ev.User != null) sbs.Append(ev.User);

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
			Debug.Assert(ForegroundOnly == true);

			bool mAffinity = false, mPriority = false;
			ProcessPriorityClass oldPriority = ProcessPriorityClass.RealTime;
			int oldAffinity = -1, newAffinity = -1;

			if (!PausedIds.ContainsKey(info.Id))
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
					// TODO: Apply affinity strategy
					newAffinity = ProcessManagerUtility.ApplyAffinityStrategy(oldAffinity, AffinityMask, AffinityStrategy);
					if (newAffinity != oldAffinity)
					{
						info.Process.ProcessorAffinity = new IntPtr(newAffinity);
						mAffinity = true;
					}
				}
			}
			catch (InvalidOperationException) // ID not available, probably exited
			{
				PausedIds.TryRemove(info.Id, out _);
				return;
			}
			catch (Win32Exception) // access error
			{
				PausedIds.TryRemove(info.Id, out _);
				return;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				return;
			}

			if (Taskmaster.DebugForeground && Taskmaster.ShowProcessAdjusts)
			{
				var ev = new ProcessEventArgs()
				{
					PriorityNew = mPriority ? (ProcessPriorityClass?)Priority.Value : null,
					PriorityOld = oldPriority,
					AffinityNew = mAffinity ? newAffinity : -1,
					AffinityOld = oldAffinity,
					Info = info,
					Control = this,
					State = ProcessRunningState.Reduced,
				};

				ev.User = new System.Text.StringBuilder();

				ev.User.Append(" – Foreground Mode");

				LogAdjust(ev);
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

			info.State = ProcessModification.Resumed;

			PausedIds.TryRemove(info.Id, out _);

			Resumed?.Invoke(this, new ProcessEventArgs() { Control = this, Info = info, State = ProcessRunningState.Restored });
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
			Debug.Assert(Taskmaster.PowerManagerEnabled);
			Debug.Assert(PowerPlan != PowerInfo.PowerMode.Undefined);

			info.PowerWait = true;
			PowerList.TryAdd(info.Id, 0);
			var ea = new ProcessEventArgs() { Info = info, Control = this, State = ProcessRunningState.Undefined };

			bool rv = Taskmaster.powermanager.Force(PowerPlan, info.Id);
			WaitingExit?.Invoke(this, ea);
			return rv;
		}

		void UndoPower(ProcessEx info)
		{
			PowerList.TryRemove(info.Id, out _);
			Taskmaster.powermanager?.Release(info.Id);
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
			catch (Exception ex) { Logging.Stacktrace(ex); }

			return false;
		}

		string FormatPathName(ProcessEx info)
		{
			if (!string.IsNullOrEmpty(info.Path))
			{
				switch (PathVisibility)
				{
					case PathVisibilityOptions.Process:
						return info.Name;
					case PathVisibilityOptions.Partial:
						if (PathElements > 0)
						{
							List<string> parts = new List<string>(info.Path.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
							// replace Path
							parts.RemoveRange(0, PathElements);
							parts.Insert(0, HumanReadable.Generic.Ellipsis);
							return System.IO.Path.Combine(parts.ToArray());
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
								if (parts.Count > 3)
								{
									// c:\program files\brand\app\version\random\element\executable.exe
									// ...\brand\app\...\executable.exe
									parts.RemoveRange(2, parts.Count - 3); // remove all but two first and last
									parts.Insert(2, HumanReadable.Generic.Ellipsis);
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

							return System.IO.Path.Combine(parts.ToArray());
						}
					default:
					case PathVisibilityOptions.File:
						return System.IO.Path.GetFileName(info.Path);
					case PathVisibilityOptions.Folder:
						{
							var parts = info.Path.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
							var partpath = parts[parts.Length - 2] + System.IO.Path.DirectorySeparatorChar + parts[parts.Length - 1];
							return partpath;
						}
					case PathVisibilityOptions.Full:
						return info.Path;
				}
			}
			else
				return info.Name; // NAME
		}

		public void Modify(ProcessEx info)
		{
			Touch(info);
			if (Recheck > 0) TouchReapply(info);
		}

		public bool MatchPath(string path)
		{
			// TODO: make this compatible with OSes that aren't case insensitive?
			return path.StartsWith(Path, StringComparison.InvariantCultureIgnoreCase);
		}

		// TODO: Simplify this
		public async void Touch(ProcessEx info, bool refresh = false)
		{
			Debug.Assert(info.Process != null, "ProcessController.Touch given null process.");
			Debug.Assert(!ProcessManager.SystemProcessId(info.Id), "ProcessController.Touch given invalid process ID");
			Debug.Assert(!string.IsNullOrEmpty(info.Name), "ProcessController.Touch given empty process name.");

			bool foreground = true;

			if (ForegroundOnly)
			{
				if (PausedIds.ContainsKey(info.Id))
				{
					if (Taskmaster.Trace && Taskmaster.DebugForeground)
						Log.Debug("<Foreground> " + FormatPathName(info) + " (#" + info.Id + ") in background, ignoring.");
					return; // don't touch paused item
				}

				foreground = Taskmaster.activeappmonitor?.Foreground.Equals(info.Id) ?? true;
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
			ProcessPriorityClass? oldPriority = null;
			IntPtr? oldAffinity = null;

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
				oldAffinityMask = oldAffinity.Value.ToInt32();
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

			var now = DateTimeOffset.UtcNow;

			RecentlyModifiedInfo ormt = null;
			if (RecentlyModified.TryGetValue(info.Id, out ormt))
			{
				try
				{
					if (ormt.Info.Process.ProcessName.Equals(info.Process.ProcessName))
					{
						bool expected = false;
						if ((Priority.HasValue && info.Process.PriorityClass != Priority.Value) ||
							(AffinityMask >= 0 && info.Process.ProcessorAffinity.ToInt32() != AffinityMask))
							ormt.UnexpectedState += 1;
						else
						{
							ormt.ExpectedState += 1;
							expected = true;
						}

						if (ormt.LastIgnored.TimeTo(now).TotalSeconds < ProcessManager.ScanFrequency.TotalSeconds+5)
						{
							if (Taskmaster.DebugProcesses) Log.Debug("[" + FriendlyName + "] #" + info.Id + " ignored due to recent modification." +
								(expected ? $" Expected: {ormt.ExpectedState} :)" : $" Unexpected: {ormt.UnexpectedState} :("));

							if (ormt.UnexpectedState == 3) Log.Debug("[" + FriendlyName + "] #" + info.Id + " is resisting being modified.");

							ormt.LastIgnored = now;

							Statistics.TouchIgnore++;

							return;
						}
					}
				}
				catch { }
				//RecentlyModified.TryRemove(info.Id, out _);
			}

			if (Taskmaster.Trace) Log.Verbose("[" + FriendlyName + "] Touching: " + info.Name + " (#" + info.Id + ")");

			if (IgnoreList != null && IgnoreList.Any(item => item.Equals(info.Name, StringComparison.InvariantCultureIgnoreCase)))
			{
				if (Taskmaster.ShowInaction && Taskmaster.DebugProcesses)
					Log.Debug("[" + FriendlyName + "] " + info.Name + " (#" + info.Id + ") ignored due to user defined rule.");
				return; // return ProcessState.Ignored;
			}

			bool isProtectedFile = ProcessManager.ProtectedProcessName(info.Name);
			// TODO: IgnoreSystem32Path

			if (isProtectedFile && Taskmaster.ShowInaction && Taskmaster.DebugProcesses)
				Log.Debug("[" + FriendlyName + "] " + info.Name + " (#" + info.Id + ") in protected list, limiting tampering.");

			ProcessPriorityClass? newPriority = null;
			IntPtr? newAffinity = null;
			var newPower = PowerInfo.PowerMode.Undefined;

			bool mAffinity = false, mPriority = false, mPower = false, modified = false, fAffinity = false, fPriority = false;
			LastSeen = DateTimeOffset.UtcNow;

			newAffinity = null;
			newPriority = null;

			bool doModifyPriority = !isProtectedFile;
			bool doModifyAffinity = false;
			bool doModifyPower = false;

			bool firsttime = true;
			if (!isProtectedFile)
			{
				if (ForegroundOnly)
				{
					if (ForegroundWatch.TryAdd(info.Id, 0))
					{
						info.Process.EnableRaisingEvents = true;
						info.Process.Exited += (o, s) => End(info);
					}
					else 
						firsttime = false;

					if (!foreground)
					{
						if (Taskmaster.DebugForeground || Taskmaster.ShowInaction)
							Log.Debug("[" + FriendlyName + "] " + info.Name + " (#" + info.Id + ") not in foreground, not prioritizing.");

						Pause(info, firsttime);
						return;
					}
				}
			}
			else
			{
				if (Taskmaster.ShowInaction && Taskmaster.DebugProcesses)
					Log.Verbose("[" + FriendlyName + "] " + info.Name + " (#" + info.Id + ") PROTECTED");
			}

			int newAffinityMask = -1;

			if (AffinityMask >= 0)
			{
				newAffinityMask = AffinityMask;

				int modifiedAffinityMask = ProcessManagerUtility.ApplyAffinityStrategy(oldAffinityMask, newAffinityMask, AffinityStrategy);
				if (modifiedAffinityMask != newAffinityMask)
					newAffinityMask = modifiedAffinityMask;

				if (oldAffinityMask != newAffinityMask)
				{
					newAffinity = new IntPtr(newAffinityMask);
					doModifyAffinity = true;
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
				if (Taskmaster.DebugProcesses) Debug.WriteLine($"{info.Name} #{info.Id} --- affinity not touched");
			}

			// APPLY CHANGES HERE
			if (doModifyPriority)
			{
				try
				{
					if (info.Process.SetLimitedPriority(Priority.Value, PriorityStrategy))
					{
						modified = mPriority = true;
						newPriority = info.Process.PriorityClass;
					}
				}
				catch { fPriority = true; } // ignore errors, this is all we care of them
			}

			if (doModifyAffinity)
			{
				try
				{
					info.Process.ProcessorAffinity = newAffinity.Value;
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
					//oldPower = Taskmaster.powermanager.CurrentMode;
					mPower = SetPower(info);
					//mPower = (oldPP != Taskmaster.powermanager.CurrentMode);
				}
			}

			if (modified)
			{
				if (mPriority || mAffinity)
				{
					Statistics.TouchCount++;
					Adjusts += 1; // don't increment on power changes
				}

				LastTouch = now;

				ScanModifyCount++;
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

			if (modified) logevent = Taskmaster.ShowProcessAdjusts && !(ForegroundOnly && !Taskmaster.ShowForegroundTransitions);
			logevent |= (firsttime && ForegroundOnly);
			logevent |= (Taskmaster.ShowInaction && Taskmaster.DebugProcesses);

			var ev = new ProcessEventArgs()
			{
				PriorityNew = newPriority,
				PriorityOld = oldPriority,
				AffinityNew = newAffinityMask,
				AffinityOld = oldAffinityMask,
				Control = this,
				Info = info,
				State = ProcessRunningState.Found,
				PriorityFail = fPriority,
				AffinityFail = fAffinity,
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

			info.Handled = true;
			info.Modified = now;

			if (modified) Modified?.Invoke(this, ev);

			if (Recheck > 0)
			{
				info.NeedsRefresh = true;
				TouchReapply(info);
			}

			if (Taskmaster.WindowResizeEnabled && Resize.HasValue) TryResize(info);

			if (modified)
			{
				if (Taskmaster.IgnoreRecentlyModified)
				{
					var rmt = new RecentlyModifiedInfo()
					{
						Info = info,
						LastModified = now,
						ExpectedState = 1,
						UnexpectedState = 0,
					};

					RecentlyModified.AddOrUpdate(info.Id, rmt, (int key, RecentlyModifiedInfo urmt) =>
					{
						try
						{
							if (urmt.Info.Process.ProcessName.Equals(info.Process.ProcessName))
							{
								urmt.LastModified = now;
								urmt.UnexpectedState += 1;
								return urmt;
							}
						}
						catch { }

						urmt.Info = info;
						urmt.LastModified = now;
						urmt.UnexpectedState += 1;
						urmt.ExpectedState = 0;

						return urmt;
					});
				}

				InternalRefresh(now);
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
						if ((r.Value.LastIgnored.TimeTo(now).TotalSeconds > ProcessManager.ScanFrequency.TotalSeconds+5)
							|| (r.Value.LastModified.TimeTo(now).TotalMinutes > 10f))
							RecentlyModified.TryRemove(r.Key, out _);
					}
				}
			}
			finally
			{
				Atomic.Unlock(ref refresh_lock);
			}
		}

		NativeMethods.RECT rect = new NativeMethods.RECT();

		void TryResize(ProcessEx info)
		{
			Debug.Assert(Resize.HasValue);

			if (Taskmaster.DebugResize) Log.Debug("Attempting resize on " + info.Name + " (#" + info.Id + ")");

			try
			{
				if (ResizeWaitList.ContainsKey(info.Id)) return;

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

				ResizeWaitList.TryAdd(info.Id, 0);

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
				})).ConfigureAwait(false);

				info.Process.EnableRaisingEvents = true;
				info.Process.Exited += (s, ev) =>
				{
					try
					{
						re.Set();
						re.Reset();

						ResizeWaitList.TryRemove(info.Id, out _);

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
					catch (Exception ex)
					{
						Logging.Stacktrace(ex);
					}
					finally
					{
						ResizeWaitList.TryRemove(info.Id, out _);
					}
				};
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);

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

		public DateTimeOffset LastScan { get; private set; } = DateTimeOffset.MinValue;

		/// <summary>
		/// Atomic lock for RescanWithSchedule()
		/// </summary>
		int ScheduledScan = 0;

		int ScanModifyCount = 0;
		public void Scan()
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
			LastScan = DateTimeOffset.UtcNow;

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
					var info = ProcessUtility.GetInfo(pid, process, name, null, getPath: !string.IsNullOrEmpty(Path));
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

				// clear event handlers
				Modified = null;
				Paused = null;
				Resumed = null;

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

		public ProcessPriorityClass? PriorityNew = null;
		public ProcessPriorityClass? PriorityOld = null;
		public int AffinityNew = -1;
		public int AffinityOld = -1;

		public bool Protected = false;
		public bool AffinityFail = false;
		public bool PriorityFail = false;

		/// <summary>
		/// Text for end-users.
		/// </summary>
		public System.Text.StringBuilder User = null;
	}

	sealed public class RecentlyModifiedInfo
	{
		public ProcessEx Info { get; set; } = null;

		public uint UnexpectedState = 0;
		public uint ExpectedState = 0;

		public DateTimeOffset LastModified = DateTimeOffset.MinValue;
		public DateTimeOffset LastIgnored = DateTimeOffset.MinValue;
	}
}