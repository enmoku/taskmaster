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
		public event EventHandler<ProcessEventArgs> Modified;

		/// <summary>
		/// Public identifier.
		/// </summary>
		public int Id { get; set; } = -1;

		/// <summary>
		/// Whether or not this rule is enabled.
		/// </summary>
		public bool Enabled { get; set; } = false;

		/// <summary>
		/// Whether this rule is valid.
		/// </summary>
		public bool Valid { get; set; } = false;

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
				if (BackgroundPriority != ProcessPriorityClass.RealTime)
					app["Background priority"].IntValue = ProcessHelpers.PriorityToInt(BackgroundPriority);
				else
					app.Remove("Background priority");
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

			NeedsSaving = false;

			cfg.MarkDirty();

			Log.Information("[{Name}] Modified.", FriendlyName);
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
		public ProcessPriorityClass BackgroundPriority { get; set; } = ProcessPriorityClass.Normal;

		/// <summary>
		/// Pause the specified foreground process.
		/// </summary>
		public void Pause(ProcessEx info)
		{
			Debug.Assert(ForegroundOnly == true);

			if (PausedIds.Contains(info.Id)) return; // already paused
			// throw new InvalidOperationException(string.Format("{0} already paused", info.Name));

			if (Taskmaster.DebugForeground && Taskmaster.Trace)
				Log.Debug("[{Name}] Quelling {Exec} (#{Pid})", FriendlyName, info.Name, info.Id);

			// PausedState.Affinity = Affinity;
			// PausedState.Priority = Priority;
			// PausedState.PowerMode = PowerPlan;

			try
			{
				info.Process.PriorityClass = BackgroundPriority;
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
						Log.Debug("<Process> [{Name}] {Exec} (#{Pid}) background power down",
							FriendlyName, info.Name, info.Id);

					UndoPower(info);
				}
			}

			if (Taskmaster.DebugForeground)
				Log.Debug("[{FriendlyName}] {Exec} (#{Pid}) priority reduced: {Current}→{Paused} [Background]",
					FriendlyName, info.Name, info.Id, Priority, BackgroundPriority);

			ForegroundMonitor(info);
		}

		public bool isPaused(ProcessEx info) => PausedIds.Contains(info.Id);

		public void Resume(ProcessEx info)
		{
			Debug.Assert(ForegroundOnly == true);

			if (!PausedIds.Contains(info.Id)) return; // can't resume unpaused item

			// throw new InvalidOperationException(string.Format("{0} not paused", info.Name));

			if (Priority.HasValue && info.Process.PriorityClass.ToInt32() != Priority.Value.ToInt32())
			{
				try
				{
					info.Process.PriorityClass = Priority.Value;
					if (Taskmaster.DebugForeground)
						Log.Debug("[{FriendlyName}] {Exec} (#{Pid}) priority restored: {Paused}→{Restored} [Foreground]",
										FriendlyName, info.Name, info.Id, BackgroundPriority, Priority);
				}
				catch
				{
					// should only happen if the process is already gone
				}
			}
			// PausedState.Priority = Priority;
			// PausedState.PowerMode = PowerPlan;

			if (Taskmaster.PowerManagerEnabled)
			{
				if (PowerPlan != PowerInfo.PowerMode.Undefined && BackgroundPowerdown)
				{
					if (Taskmaster.DebugPower || Taskmaster.DebugForeground)
						Log.Debug("<Process> [{Name}] {Exec} (#{Pid}) foreground power on",
							FriendlyName, info.Name, info.Id);

					SetPower(info);
				}
			}

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

		bool SetPower(ProcessEx info)
		{
			if (!Taskmaster.PowerManagerEnabled) return false;
			if (PowerPlan == PowerInfo.PowerMode.Undefined) return false;
			Taskmaster.powermanager.SaveMode();

			Taskmaster.processmanager.WaitForExit(info); // need nicer way to signal this

			return Taskmaster.powermanager.Force(PowerPlan, info.Id);
		}

		void UndoPower(ProcessEx info) => Taskmaster.powermanager?.Release(info.Id);

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

		HashSet<int> ForegroundWatch = null;

		void ForegroundMonitor(ProcessEx info)
		{
			if (ForegroundWatch == null) ForegroundWatch = new HashSet<int>();

			if (!ForegroundWatch.Contains(info.Id))
			{
				ForegroundWatch.Add(info.Id);

				PausedIds.Add(info.Id);

				info.Process.Exited += (o, s) =>
				{
					ForegroundWatch.Remove(info.Id);
					PausedIds.Remove(info.Id);
				};
			}
		}

		// TODO: Deal with combo path+exec
		public async void Touch(ProcessEx info, bool schedule_next = false, bool recheck = false)
		{
			Debug.Assert(info.Process != null, "ProcessController.Touch given null process.");
			Debug.Assert(info.Id > 4, "ProcessController.Touch given invalid process ID");
			Debug.Assert(!string.IsNullOrEmpty(info.Name), "ProcessController.Touch given empty process name.");

			if (PausedIds.Contains(info.Id)) return; // don't touch paused item

			/*
			try
			{
				if (!info.Process.Responding)
					return; // ignore non-responding apps
			}
			catch { }
			*/

			bool foreground = Taskmaster.activeappmonitor?.Foreground.Equals(info.Id) ?? true;

			info.PowerWait = (PowerPlan != PowerInfo.PowerMode.Undefined);
			info.ActiveWait = ForegroundOnly;

			if (!recheck || ModifyDelay > 0)
				await Task.Delay(recheck ? 0 : ModifyDelay).ConfigureAwait(false);

			if (recheck || ModifyDelay > 0)
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

			try
			{
				if (info.Process.HasExited)
				{
					if (Taskmaster.DebugProcesses)
						Log.Debug("[{FriendlyName}] {ProcessName} (#{ProcessID}) has already exited.", FriendlyName, info.Name, info.Id);
					return; // return ProcessState.Invalid;
				}
			}
			catch (Win32Exception ex)
			{
				if (ex.NativeErrorCode != 5)
					Log.Warning("[{FriendlyName}] {ProcessName} (#{ProcessID}) access failure determining if it's still running.", FriendlyName, info.Name, info.Id);
				return; // return ProcessState.Error; // we don't care what this error is exactly
			}
			catch (Exception ex) // invalidoperation or notsupported
			{
				Logging.Stacktrace(ex);
				return;
			}

			if (Taskmaster.Trace) Log.Verbose("[{FriendlyName}] Touching: {ExecutableName} (#{ProcessID})", FriendlyName, info.Name, info.Id);

			if (IgnoreList != null && IgnoreList.Contains(info.Name, StringComparer.InvariantCultureIgnoreCase))
			{
				if (Taskmaster.ShowInaction && Taskmaster.DebugProcesses)
					Log.Debug("[{FriendlyName}] {Exec} (#{ProcessID}) ignored due to user defined rule.", FriendlyName, info.Name, info.Id);
				return; // return ProcessState.Ignored;
			}

			bool denyChange = ProcessManager.ProtectedProcessName(info.Name);
			// TODO: IgnoreSystem32Path

			if (denyChange && Taskmaster.ShowInaction && Taskmaster.DebugProcesses)
				Log.Debug("[{FriendlyName}] {ProcessName} (#{ProcessID}) in protected list, limiting tampering.", FriendlyName, info.Name, info.Id);

			// TODO: Validate path.
			if (!string.IsNullOrEmpty(Path))
			{
				if (string.IsNullOrEmpty(info.Path))
				{
					if (ProcessManagerUtility.FindPath(info))
					{
						// Yay
					}
					else
						return; // return ProcessState.Error;
				}

				if (info.PathMatched || info.Path.StartsWith(Path, StringComparison.InvariantCultureIgnoreCase)) // FIXME: this is done twice
				{
					// OK
					if (Taskmaster.DebugPaths && !info.PathMatched)
						Log.Verbose("[{PathFriendlyName}] (Touch) Matched at: {Path}", FriendlyName, info.Path);

					info.PathMatched = true;
				}
				else
				{
					if (Taskmaster.DebugPaths)
						Log.Verbose("[{PathFriendlyName}] {ExePath} NOT IN {Path} – IGNORING", FriendlyName, info.Path, Path);
					return; // return ProcessState.Ignored;
				}
			}

			bool mAffinity = false, mPriority = false, mPower = false, modified = false, fAffinity = false, fPriority = false;
			LastSeen = DateTime.Now;

			var oldAffinity = IntPtr.Zero;
			var oldPriority = ProcessPriorityClass.RealTime;
			try
			{
				oldAffinity = info.Process.ProcessorAffinity;
				oldPriority = info.Process.PriorityClass;
			}
			catch (InvalidOperationException)
			{
				// Already exited
				info.State = ProcessModification.Exited;
				return;
			}
			catch (ArgumentException)
			{
				// Already exited
				info.State = ProcessModification.Exited;
				return;
			}
			catch (Exception ex) { Logging.Stacktrace(ex); }

			IntPtr newAffinity = Affinity.GetValueOrDefault();
			
			var newPriority = oldPriority;

			if (!denyChange)
			{
				if (!foreground && ForegroundOnly)
				{
					if (Taskmaster.DebugForeground || Taskmaster.ShowInaction)
						Log.Debug("{Exec} (#{Pid}) not in foreground, not prioritizing.", info.Name, info.Id);

					ForegroundMonitor(info);

					// NOP
				}
				else if (Priority.HasValue)
				{
					try
					{
						if (info.Process.SetLimitedPriority(Priority.Value, PriorityStrategy))
						{
							modified = mPriority = true;
							newPriority = Priority.Value;
						}
					}
					catch
					{
						fPriority = true;
						if (Taskmaster.ShowInaction)
							Log.Warning("[{FriendlyName}] {Exec} (#{Pid}) failed to set process priority.", FriendlyName, info.Name, info.Id);
						// NOP
					}
				}
				else
				{
					// no priority changing
				}
			}
			else
			{
				if (Taskmaster.ShowInaction)
					Log.Verbose("{Exec} (#{Pid}) protected.", info.Name, info.Id);
			}

			if (Affinity.HasValue)
			{
				try
				{
					var oldAffinityMask = info.Process.ProcessorAffinity.ToInt32();
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
						{
							info.Process.ProcessorAffinity = newAffinity;

							modified = mAffinity = true;
						}
						// Log.Verbose("Affinity for '{ExecutableName}' (#{ProcessID}) set: {OldAffinity} → {NewAffinity}.",
						// execname, pid, process.ProcessorAffinity.ToInt32(), Affinity.ToInt32());
					}
					else
					{
						// Log.Verbose("Affinity for '{ExecutableName}' (#{ProcessID}) is ALREADY set: {OldAffinity} → {NewAffinity}.",
						// 			info.Name, info.Id, info.Process.ProcessorAffinity.ToInt32(), Affinity.ToInt32());
					}
				}
				catch
				{
					fAffinity = true;
					if (Taskmaster.ShowInaction)
						Log.Warning("[{FriendlyName}] {Exec} (#{Pid}) failed to set process affinity.", FriendlyName, info.Name, info.Id);
				}
			}
			else
			{
				// no affinity changing
			}

			/*
			if (BackgroundIO)
			{
				try
				{
					//Process.EnterDebugMode(); // doesn't help

					//mBGIO = SetBackground(info.Process); // doesn't work, can only be done to current process

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

			//var oldPP = PowerInfo.PowerMode.Undefined;
			if (Taskmaster.PowerManagerEnabled && PowerPlan != PowerInfo.PowerMode.Undefined)
			{
				if (!foreground && BackgroundPowerdown)
				{
					if (Taskmaster.DebugForeground)
						Log.Debug("{Exec} (#{Pid}) not in foreground, not powering up.", info.Name, info.Id);

					ForegroundMonitor(info);
				}
				else
				{
					//oldPP = Taskmaster.powermanager.CurrentMode;
					mPower = SetPower(info);
					//mPower = (oldPP != Taskmaster.powermanager.CurrentMode);
				}
			}

			var sbs = new System.Text.StringBuilder();
			sbs.Append("[").Append(FriendlyName).Append("] ").Append(info.Name).Append(" (#").Append(info.Id).Append(")");

			if (mPriority || mAffinity)
			{
				Statistics.TouchCount++;
				Adjusts += 1; // don't increment on power changes
			}

			if (modified)
			{
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
					catch (ArgumentException)
					{
						// Already exited
						info.State = ProcessModification.Exited;
						return;
					}
					catch (Exception ex) { Logging.Stacktrace(ex); }
				}

				LastTouch = DateTime.Now;

				ScanModifyCount++;
			}

			if (Priority.HasValue)
			{
				sbs.Append("; Priority: ");
				if (mPriority)
					sbs.Append(oldPriority.ToString()).Append(" → ");
				sbs.Append(newPriority.ToString());
				if (denyChange) sbs.Append(" [Protected]");
				if (fPriority) sbs.Append(" [Failed]");
			}
			if (Affinity.HasValue)
			{
				// TODO: respect display configuration for 
				sbs.Append("; Affinity: ");
				if (mAffinity)
					sbs.Append(oldAffinity.ToInt32()).Append(" → ");
				sbs.Append(newAffinity);

				if (fAffinity) sbs.Append(" [Failed]");

				if (Taskmaster.DebugProcesses) sbs.Append(string.Format(" [{0}]", AffinityStrategy.ToString()));
			}
			if (mPower)
				sbs.Append(string.Format(" [Power Mode: {0}]", PowerPlan.ToString()));

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
				// else
				// 	Log.Verbose(sbs.ToString());
			}

			TryResize(info);

			if (modified)
			{
				Modified?.Invoke(this, new ProcessEventArgs { Control = this, Info = info });
			}

			if (schedule_next) TryScan();

			// schedule re-application of the rule
			if (Recheck > 0 && recheck == false)
			{
				Task.Run(new Action(() => { TouchReapply(info); }));
			}

			return; // return rv;
		}

		NativeMethods.RECT rect = new NativeMethods.RECT();

		void TryResize(ProcessEx info)
		{
			if (!Resize.HasValue) return;

			if (Taskmaster.DebugResize) Log.Debug("Attempting resize on {Name} (#{Pid})", info.Name, info.Id);

			try
			{
				lock (ResizeWaitList_lock)
				{
					if (ResizeWaitList.Contains(info.Id)) return;

					IntPtr hwnd = info.Process.MainWindowHandle;
					if (!NativeMethods.GetWindowRect(hwnd, ref rect))
					{
						if (Taskmaster.DebugResize) Log.Debug("Failed to retrieve current size of {Name} (#{Pid})", info.Name, info.Id);
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
							Log.Debug("Resizing {Name} (#{Pid}) from {OldWidth}×{OldHeight} to {NewWidth}×{NewHeight}",
								info.Name, info.Id, oldrect.Width, oldrect.Height, newsize.Width, newsize.Height);

						// TODO: Add option to monitor the app and save the new size so relaunching the app keeps the size.

						NativeMethods.MoveWindow(hwnd, newsize.Left, newsize.Top, newsize.Width, newsize.Height, true);

						if (ResizeStrategy != WindowResizeStrategy.None)
						{
							lock (Taskmaster.watchlist_lock)
							{
								Resize = newsize;
								NeedsSaving = true;
							}
						}
					}

					if (ResizeStrategy == WindowResizeStrategy.None)
					{
						if (Taskmaster.DebugResize) Log.Debug("Remembering size or pos not enabled for {Name} (#{Pid})", info.Name, info.Id);
						return;
					}

					ResizeWaitList.Add(info.Id);

					System.Threading.ManualResetEvent re = new System.Threading.ManualResetEvent(false);
					Task.Run(new Action(() =>
					{
						if (Taskmaster.DebugResize) Log.Debug("<Resize> Starting monitoring {Exe} (#{Pid})", info.Name, info.Id);
						try
						{
							while (!re.WaitOne(60_000))
							{
								if (Taskmaster.DebugResize) Log.Debug("<Resize> Recording size and position for {Exe} (#{Pid})", info.Name, info.Id);

								NativeMethods.GetWindowRect(hwnd, ref rect);

								bool rpos = Bit.IsSet(((int)ResizeStrategy), (int)WindowResizeStrategy.Position);
								bool rsiz = Bit.IsSet(((int)ResizeStrategy), (int)WindowResizeStrategy.Size);
								newsize = new System.Drawing.Rectangle(
									rpos ? rect.Left : Resize.Value.Left, rpos ? rect.Top : Resize.Value.Top,
									rsiz ? rect.Right - rect.Left : Resize.Value.Left, rsiz ? rect.Bottom - rect.Top : Resize.Value.Top
									);

								lock (Taskmaster.watchlist_lock)
								{
									Resize = newsize;
									NeedsSaving = true;
								}
							}
						}
						catch (Exception ex)
						{
							Logging.Stacktrace(ex);
						}
						if (Taskmaster.DebugResize) Log.Debug("<Resize> Stopping monitoring {Exe} (#{Pid})", info.Name, info.Id);
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
									Log.Debug("Saving {Name} (#{Pid}) size to {NewWidth}×{NewHeight}",
										info.Name, info.Id, Resize.Value.Width, Resize.Value.Height);

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
				Log.Debug("[{FriendlyName}] {Process} (#{PID}) rechecking", FriendlyName, info.Name, info.Id);

			try
			{
				if (!info.Process.HasExited)
				{
					Touch(info, schedule_next: false, recheck: true);
				}
				else
				{
					if (Taskmaster.Trace) Log.Verbose("[{FriendlyName}] {Process} (#{PID}) is gone yo.", FriendlyName, info.Name, info.Id);
				}
			}
			catch (Exception ex)
			{
				Log.Warning("[{FriendlyName}] {Process} (#{PID}) – something bad happened.", FriendlyName, info.Name, info.Id);
				Logging.Stacktrace(ex);
				return; //throw; // would throw but this is async function
			}
		}

		/// <summary>
		/// Synchronous call to RescanWithSchedule()
		/// </summary>
		public int TryScan()
		{
			Task.WaitAll(RescanWithSchedule());

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
				// Log.Trace(string.Format("[{0}] last scan {1:N1} minute(s) ago.", FriendlyName, n));
				if (Rescan > 0 && n >= Rescan)
				{
					if (Atomic.Lock(ref ScheduledScan))
					{
						try
						{
							if (Taskmaster.DebugProcesses)
								Log.Debug("[{FriendlyName}] Rescan initiating.", FriendlyName);

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
				if (Taskmaster.Trace) Log.Verbose("{FriendlyName} is not running", ExecutableFriendlyName);
				return;
			}

			// LastSeen = LastScan;
			LastScan = DateTime.Now;

			if (procs.Length == 0) return;

			if (Taskmaster.DebugProcesses)
				Log.Debug("[{FriendlyName}] Scanning found {ProcessInstances} instance(s)", FriendlyName, procs.Length);

			ScanModifyCount = 0;
			foreach (Process process in procs)
			{
				string name;
				int pid;
				try
				{
					name = process.ProcessName;
					pid = process.Id;

					var info = ProcessManagerUtility.GetInfo(pid, process, name, null, getPath: !string.IsNullOrEmpty(Path));

					Touch(info);
				}
				catch // access failure or similar, we don't care
				{
					continue; // shouldn't happen, but if it does, we don't care
				}
			}

			if (ScanModifyCount > 0)
			{
				if (Taskmaster.DebugProcesses)
					Log.Verbose("[{ProcessFriendlyName}] Scan modified {ModifiedInstances} out of {ProcessInstances} instance(s)",
								FriendlyName, ScanModifyCount, procs.Length);
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
				if (Taskmaster.Trace) Log.Verbose("Disposing process controller [{FriendlyName}]", FriendlyName);

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