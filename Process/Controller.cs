//
// Process.Controller.cs
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
using MKAh.Logic;
using Serilog;
using Ini = MKAh.Ini;

namespace Taskmaster.Process
{
	using static Taskmaster;

	/// <summary>
	/// Process controller.
	/// </summary>
	sealed public class Controller : IDisposable
	{
		/// <summary>
		/// <para>Don't allow user to tamper.</para>
		/// <para>Mostly for inbuilt rules that protect the system from bad configuration.</para>
		/// </summary>
		public bool Protected { get; set; } = false;

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
		/// Order of preference
		/// </summary>
		public int OrderPreference { get; set; } = 10;
		public int ActualOrder { get; set; } = 10;

		/// <summary>
		/// Whether or not this rule is enabled.
		/// </summary>
		public bool Enabled { get; set; } = false;

		/// <summary>
		/// Human-readable friendly name for the process.
		/// </summary>
		public string FriendlyName { get; private set; } = null;

		internal string[] p_Executable = null;

		/// <summary>
		/// Executable filename related to this, with extension.
		/// </summary>
		public string[] Executables
		{
			get => p_Executable;
			set
			{
				if (value is null)
				{
					p_Executable = null;
					ExecutableFriendlyName = null;
				}
				else
				{
					var t_exe = new string[value.Length];
					var t_friendly = new string[value.Length];
					for (int i = 0; i < value.Length; i++)
						t_friendly[i] = System.IO.Path.GetFileNameWithoutExtension(t_exe[i] = value[i]).ToLowerInvariant();
					p_Executable = t_exe;
					ExecutableFriendlyName = t_friendly;
				}
			}
		}

		/// <summary>
		/// Frienly executable name as required by various System.Process functions.
		/// Same as <see cref="T:Taskmaster.ProcessControl.Executable"/> but with the extension missing.
		/// </summary>
		public string[] ExecutableFriendlyName { get; internal set; } = null;

		public bool ExclusiveMode { get; set; } = false;

		public bool DeclareParent { get; set; } = false;

		public string Path { get; set; } = string.Empty;

		/*
		Lazy<System.Text.RegularExpressions.Regex> FastMatchExp = null;
		void ResetFastMatch() => FastMatchExp = new Lazy<System.Text.RegularExpressions.Regex>(GenerateRegex);
		public bool FastMatch(string match) => FastMatchExp.Value.IsMatch(match);

		public bool CanFastMatch => FastMatchExp.IsValueCreated;

		const string FilePathPadding = @"(.*)?\\";
		const string FilePathPaddingForSlash = @"[(.*)((.*)?\\)?]";

		System.Text.RegularExpressions.Regex GenerateRegex()
		{
			string path = string.Empty;
			string exe = string.Empty;
			bool hPath = false, hFile = false, endsWithSlash=false;
			if (!string.IsNullOrEmpty(Path))
			{
				path = "^" + System.Text.RegularExpressions.Regex.Escape(Path);
				endsWithSlash = !(Path[-1] is '\\');
				hPath = true;
			}
			if (!string.IsNullOrEmpty(Executable))
			{
				exe = System.Text.RegularExpressions.Regex.Escape(Executable) + "$";
				hFile = true;
			}

			bool both = hFile && hPath;

			//
			//
			// c:\ (.*)?\\ exe = c:\a\exe
			// c: .*\\ exe = c:\exe
			// c:\ [(.*)((.*)?\\)?] exe = both of the above
			// \\ exe
			// c:\

			var res = path + (both ? (endsWithSlash ? FilePathPaddingForSlash : FilePathPadding) : (hFile ? @"\\" : string.Empty)) + exe;
			Logging.DebugMsg("Process.Controller.GenerateRegex = " + res);

			var regex = new System.Text.RegularExpressions.Regex(res,
			System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.ExplicitCapture);
			return regex;
		}
		*/

		int PathElements { get; set; } = 0;

		/// <summary>
		/// User description for the rule.
		/// </summary>
		public string Description { get; set; } = string.Empty; // TODO: somehow unload this from memory

		float _volume = 0.5f;

		/// <summary>
		/// Volume as 0.0 to 1.0 
		/// </summary>
		public float Volume { get => _volume; set => _volume = value.Constrain(0.0f, 1.0f); }
		public Audio.VolumeStrategy VolumeStrategy { get; set; } = Audio.VolumeStrategy.Ignore;

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

		public IOPriority IOPriority = IOPriority.Ignore; // Win7 only?

		public ProcessPriorityStrategy PriorityStrategy { get; set; } = ProcessPriorityStrategy.None;

		/// <summary>
		/// CPU core affinity.
		/// </summary>
		public int AffinityMask { get; set; } = -1;

		public ProcessAffinityStrategy AffinityStrategy = ProcessAffinityStrategy.None;
		int ScatterOffset = 0;
		int ScatterChunk = 1; // should default to Cores/4 or Range/2 at max, 1 at minimum.

		int _affinityIdeal = -1;

		public int AffinityIdeal // EXPERIMENTAL
		{
			get => _affinityIdeal;
			set => _affinityIdeal = value.Constrain(-1, Utility.CPUCount);
		}

		/// <summary>
		/// The power plan.
		/// </summary>
		public Power.Mode PowerPlan = Power.Mode.Undefined;

		public int Recheck { get; set; } = 0;

		public bool AllowPaging { get; set; } = false;

		public Process.PathVisibilityOptions PathVisibility { get; set; } = Process.PathVisibilityOptions.Process;

		string PathMask { get; } = string.Empty; // UNUSED

		/// <summary>
		/// Controls whether this particular controller allows itself to be logged.
		/// </summary>
		public bool LogAdjusts { get; set; } = true;

		/// <summary>
		/// Log start and exit of the process.
		/// </summary>
		public bool LogStartAndExit { get; set; } = false;

		/// <summary>
		/// Log process description as seen on task manager description column.
		/// </summary>
		public bool LogDescription { get; set; } = false;

		/// <summary>
		/// Delay in milliseconds before we attempt to alter the process.
		/// For example, to allow a process to function at default settings for a while, or to work around undesirable settings
		/// the process sets for itself.
		/// </summary>
		public int ModifyDelay { get; set; } = 0;

		public Controller(string name, ProcessPriorityClass? priority = null, int affinity = -1)
		{
			//ResetFastMatch(); // RegExp

			FriendlyName = name;
			// Executable = executable;

			Priority = priority;
			if (affinity >= 0)
			{
				AffinityMask = affinity;
				AffinityStrategy = ProcessAffinityStrategy.Limit;
			}
		}

		public void SetForegroundMode(ForegroundMode mode)
		{
			Foreground = mode;

			BackgroundPowerdown = (Foreground == ForegroundMode.PowerOnly || Foreground == ForegroundMode.Full);

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
				info.ForegroundWait = info.InBackground = false;
		}

		void ClearPower()
		{
			foreach (var info in ActiveWait.Values)
			{
				if (info.PowerWait)
				{
					powermanager?.Release(info);
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

			bool FixedSomething = false, ForegroundFixed = false, BackgroundAffinityFixed = false, BackgroundPriorityFixed = false, PathVisibilityFixed = false;
			bool AffinityMismatchFixed = false, PriorityMismatchFixed = false;

			switch (Foreground)
			{
				case ForegroundMode.Ignore:
					if (BackgroundAffinity >= 0 || BackgroundPriority.HasValue || BackgroundPowerdown)
						FixedSomething = ForegroundFixed = true;
					if (BackgroundAffinity >= 0)
					{
						Log.Warning($"[{FriendlyName}] Background affinity: {BackgroundAffinity} → -1");
						BackgroundAffinity = -1;
						BackgroundAffinityFixed = true;
					}
					if (BackgroundPriority.HasValue)
					{
						Log.Warning($"[{FriendlyName}] Background priority: {BackgroundPriority.Value.ToInt32()} → null");
						BackgroundPriority = null;
						BackgroundPriorityFixed = true;
					}

					if (BackgroundPowerdown) Log.Warning($"[{FriendlyName}] Background powerdown:  true → false");

					BackgroundPowerdown = false;
					break;
				case ForegroundMode.Standard:
					break;
				case ForegroundMode.Full:
					if (PowerPlan == Power.Mode.Undefined)
					{
						Log.Warning($"[{FriendlyName}] Powerplan undefined: Foreground mode from Full to Standard");
						Foreground = ForegroundMode.Standard;
						FixedSomething = ForegroundFixed = true;
					}
					break;
				case ForegroundMode.PowerOnly:
					if (PowerPlan == Power.Mode.Undefined)
					{
						Log.Warning($"[{FriendlyName}] Powerplan undefined: Foreground mode from Power Only to Ignore");
						Foreground = ForegroundMode.Ignore;
						FixedSomething = ForegroundFixed = true;
					}
					break;
			}

			if (BackgroundAffinity >= 0)
			{
				if (AffinityMask >= 0)
				{
					if (Bit.Count(BackgroundAffinity) > Bit.Count(AffinityMask))
					{
						Log.Warning($"[{FriendlyName}] Background affinity too great: {BackgroundAffinity} > {AffinityMask}");
						// this be bad
						BackgroundAffinity = -1;
						FixedSomething = BackgroundAffinityFixed = true;
						AffinityMismatchFixed = true;
					}
				}
				else
				{
					Log.Warning($"[{FriendlyName}] Background affinity without foreground: {BackgroundAffinity} → -1");

					BackgroundAffinity = -1;
					FixedSomething = BackgroundAffinityFixed = true;
					AffinityMismatchFixed = true;
				}
			}

			if (BackgroundPriority.HasValue)
			{
				if (Priority.HasValue)
				{
					if (BackgroundPriority.HasValue && Priority.Value.ToInt32() < BackgroundPriority.Value.ToInt32())
					{
						Log.Warning($"[{FriendlyName}] Background priority too great: {BackgroundPriority.Value.ToInt32()} > {Priority.Value.ToInt32()}");

						// this be bad
						BackgroundPriority = null;
						FixedSomething = BackgroundPriorityFixed = true;
						PriorityMismatchFixed = true;
					}
				}
				else
				{
					Log.Warning($"[{FriendlyName}] Background priority without foreground: {BackgroundPriority.Value.ToInt32()} → null");

					BackgroundPriority = null;
					FixedSomething = BackgroundPriorityFixed = true;
					PriorityMismatchFixed = true;
				}
			}

			if (VolumeStrategy == Audio.VolumeStrategy.Ignore)
				Volume = 0.5f;

			if (PathVisibility == PathVisibilityOptions.Invalid)
			{
				bool haveExe = Executables?.Length > 0;
				bool havePath = !string.IsNullOrEmpty(Path);
				if (haveExe && havePath) PathVisibility = PathVisibilityOptions.Process;
				else if (havePath) PathVisibility = PathVisibilityOptions.Partial;
				else PathVisibility = PathVisibilityOptions.Full;
				FixedSomething = PathVisibilityFixed = true;

				Log.Warning($"[{FriendlyName}] Path Visibility from Invalid to {PathVisibility.ToString()}");
			}

			if (FixedSomething)
			{
				NeedsSaving = true;

				var sbs = new StringBuilder();

				sbs.Append("[").Append(FriendlyName).Append("]").Append(" Malconfigured. Following re-adjusted: ");

				var fixedList = new List<string>();

				if (PriorityMismatchFixed) fixedList.Add("priority mismatch");
				if (AffinityMismatchFixed) fixedList.Add("affinity mismatch");
				if (BackgroundAffinityFixed) fixedList.Add("background affinity");
				if (BackgroundPriorityFixed) sbs.Append("background priority");
				if (ForegroundFixed) fixedList.Add("foreground options");
				if (PathVisibilityFixed) fixedList.Add("path visibility");

				sbs.Append(string.Join(", ", fixedList));

				Log.Error(sbs.ToString());
			}
		}

		public void SetName(string newName)
		{
			using var cfg = Config.Load(Manager.WatchlistFile);
			if (cfg.Config.TryGet(FriendlyName, out var section))
				section.Name = newName;
			FriendlyName = newName;
		}

		public void DeleteConfig()
		{
			using var cfg = Config.Load(Manager.WatchlistFile);
			cfg.Config.TryRemove(FriendlyName); // remove the section, removes the items in the section
		}

		void ProcessExitEvent(object sender, EventArgs _ea)
		{
			if (sender is System.Diagnostics.Process process)
				RecentlyModified.TryRemove(process.Id, out _);
		}

		/// <summary>
		/// End various things for the given process
		/// </summary>
		public void End(object sender, EventArgs _ea)
		{
			var process = sender as System.Diagnostics.Process;

			if (ActiveWait.TryGetValue(process.Id, out var info))
			{
				info.State = ProcessHandlingState.Exited;
				info.InBackground = false; // IRRELEVANT
				info.ForegroundWait = false; // IRRELEVANT

				if (info.PowerWait && PowerPlan != Power.Mode.Undefined) UndoPower(info);
				info.PowerWait = false;
			}
		}

		/// <summary>
		/// Refresh the controller, freeing resources, locks, etc.
		/// </summary>
		public void Refresh()
		{
			if (DebugPower || Manager.DebugProcesses) Log.Debug($"[{FriendlyName}] Refresh");

			ClearActive();
			ClearPower();
			foreach (var info in ActiveWait.Values)
			{
				info.Process.Refresh();
				if (!info.Process.HasExited)
				{
					info.InBackground = false;
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

		public void SaveConfig()
		{
			using var cfg = Config.Load(Manager.WatchlistFile);
			Logging.DebugMsg("Saving: " + FriendlyName);
			SaveConfig(cfg.File);
		}

		public void SaveConfig(Configuration.File cfg, Ini.Section app = null)
		{
			// TODO: Check if anything actually was changed?

			Debug.Assert(cfg != null);

			if (app is null)
				app = cfg.Config[FriendlyName];

			if (Executables?.Length > 0)
				app["Executables"].StringArray = Executables;
			else
				app.TryRemove("Executables");

			if (!string.IsNullOrEmpty(Path))
				app[HumanReadable.System.Process.Path].Value = Path;
			else
				app.TryRemove(HumanReadable.System.Process.Path);

			if (!string.IsNullOrEmpty(Description))
				app[HumanReadable.Generic.Description].Value = Description;
			else
				app.TryRemove(HumanReadable.Generic.Description);

			if (Priority.HasValue)
			{
				app[HumanReadable.System.Process.Priority].Int = ProcessHelpers.PriorityToInt(Priority.Value);
				app[HumanReadable.System.Process.PriorityStrategy].Int = (int)PriorityStrategy;
			}
			else
			{
				app.TryRemove(HumanReadable.System.Process.Priority);
				app.TryRemove(HumanReadable.System.Process.PriorityStrategy);
			}

			if (AffinityMask >= 0)
			{
				//if (affinity == ProcessManager.allCPUsMask) affinity = 0; // convert back

				app[HumanReadable.System.Process.Affinity].Int = AffinityMask;
				app[HumanReadable.System.Process.AffinityStrategy].Int = (int)AffinityStrategy;
			}
			else
			{
				app.TryRemove(HumanReadable.System.Process.Affinity);
				app.TryRemove(HumanReadable.System.Process.AffinityStrategy);
			}

			if (AffinityIdeal >= 0)
				app["Affinity ideal"].Int = AffinityIdeal;
			else
				app.TryRemove("Affinity ideal");

			if (IOPriority != IOPriority.Ignore)
				app["IO priority"].Int = (int)IOPriority;
			else
				app.TryRemove("IO priority");

			if (PowerPlan != Power.Mode.Undefined)
				app[HumanReadable.Hardware.Power.Mode].Value = Power.Utility.GetModeName(PowerPlan);
			else
				app.TryRemove(HumanReadable.Hardware.Power.Mode);

			switch (Foreground)
			{
				case ForegroundMode.Ignore:
					app.TryRemove("Background powerdown");
				clearNonPower:
					app.TryRemove("Foreground only");
					app.TryRemove("Foreground mode");
					app.TryRemove("Background priority");
					app.TryRemove("Background affinity");
					break;
				case ForegroundMode.Standard:
					app.TryRemove("Background powerdown");
				saveFgMode:
					app["Foreground mode"].Int = (int)Foreground;
					if (BackgroundPriority.HasValue)
						app["Background priority"].Int = ProcessHelpers.PriorityToInt(BackgroundPriority.Value);
					else
						app.TryRemove("Background priority");
					if (BackgroundAffinity >= 0)
						app["Background affinity"].Int = BackgroundAffinity;
					else
						app.TryRemove("Background affinity");
					break;
				case ForegroundMode.Full:
					goto saveFgMode;
				case ForegroundMode.PowerOnly:
					goto clearNonPower;
			}

			if (AllowPaging)
				app["Allow paging"].Bool = AllowPaging;
			else
				app.TryRemove("Allow paging");

			if (PathVisibility != PathVisibilityOptions.Invalid)
				app["Path visibility"].Int = (int)PathVisibility;
			else
				app.TryRemove("Path visibility");

			if (Executables?.Length > 0 && Recheck > 0)
				app["Recheck"].Int = Recheck;
			else
				app.TryRemove("Recheck");

			if (!Enabled) app[HumanReadable.Generic.Enabled].Bool = Enabled;
			else app.TryRemove(HumanReadable.Generic.Enabled);

			app["Preference"].Int = OrderPreference;

			if (IgnoreList?.Length > 0)
				app[HumanReadable.Generic.Ignore].Array = IgnoreList;
			else
				app.TryRemove(HumanReadable.Generic.Ignore);

			if (ModifyDelay > 0)
				app[Constants.ModifyDelay].Int = ModifyDelay;
			else
				app.TryRemove(Constants.ModifyDelay);

			if (Resize.HasValue)
			{
				int[] res = app.Get(Constants.Resize)?.IntArray ?? null;
				if ((res?.Length ?? 0) != 4) res = new int[] { 0, 0, 0, 0 };

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

				app[Constants.Resize].IntArray = res;

				app[Constants.ResizeStrategy].Int = (int)ResizeStrategy;
			}
			else
			{
				app.TryRemove(Constants.ResizeStrategy);
				app.TryRemove(Constants.Resize);
			}

			if (VolumeStrategy != Audio.VolumeStrategy.Ignore)
			{
				app[HumanReadable.Hardware.Audio.Volume].Float = Volume;
				app[Constants.VolumeStrategy].Int = (int)VolumeStrategy;
			}
			else
			{
				app.TryRemove(HumanReadable.Hardware.Audio.Volume);
				app.TryRemove(Constants.VolumeStrategy);
			}

			app[HumanReadable.Generic.Logging].Bool = LogAdjusts;

			if (LogStartAndExit)
				app["Log start and exit"].Bool = true;
			else
				app.TryRemove("Log start and exit");

			if (LogDescription)
				app["Log description"].Bool = true;
			else
				app.TryRemove("Log description");

			if (ExclusiveMode)
				app[Constants.Exclusive].Bool = true;
			else
				app.TryRemove(Constants.Exclusive);

			if (DeclareParent)
				app["Declare parent"].Bool = true;
			else
				app.TryRemove("Declare parent");

			Logging.DebugMsg(cfg.Filename + " has gained " + cfg.Config.Changes + " total changes.");

			// pass to config manager
			NeedsSaving = false;
		}

		// The following should be combined somehow?
		readonly ConcurrentDictionary<int, ProcessEx> ActiveWait = new ConcurrentDictionary<int, ProcessEx>();

		readonly ConcurrentDictionary<int, RecentlyModifiedInfo> RecentlyModified = new ConcurrentDictionary<int, RecentlyModifiedInfo>();

		/// <summary>
		/// Caching from Foreground
		/// </summary>
		bool BackgroundPowerdown { get; set; } = false;
		public ProcessPriorityClass? BackgroundPriority { get; set; } = null;
		public int BackgroundAffinity { get; set; } = -1;

		/// <summary>
		/// Pause the specified foreground process.
		/// </summary>
		public void SetBackground(ProcessEx info, bool firsttime = false)
		{
			Debug.Assert(Foreground != ForegroundMode.Ignore, "Pause called for non-foreground only rule");
			Debug.Assert(info.Controller != null, "No controller attached");

			//Debug.Assert(!PausedIds.ContainsKey(info.Id));

			if (info.InBackground) return; // already paused

			if (DebugForeground && Trace) Log.Debug($"[{FriendlyName}] Quelling {info.Name} (#{info.Id})");

			// PausedState.Affinity = Affinity;
			// PausedState.Priority = Priority;
			// PausedState.PowerMode = PowerPlan;

			bool mAffinity = false, mPriority = false;
			ProcessPriorityClass oldPriority = ProcessPriorityClass.RealTime;
			int oldAffinity = -1;

			try
			{
				oldPriority = info.Process.PriorityClass;
				if (BackgroundPriority.HasValue && oldPriority != BackgroundPriority.Value)
				{
					info.Process.PriorityClass = BackgroundPriority.Value;
					mPriority = true;
				}

				oldAffinity = info.Process.ProcessorAffinity.ToInt32();
				if (BackgroundAffinity >= 0)
				{
					if (oldAffinity != BackgroundAffinity)
					{
						info.Process.ProcessorAffinity = new IntPtr(BackgroundAffinity.Replace(0, Utility.FullCPUMask));
						mAffinity = true;
					}
				}
				else if (AffinityMask >= 0 && EstablishNewAffinity(oldAffinity, out int newAffinityMask)) // set foreground affinity otherwise
				{
					info.Process.ProcessorAffinity = new IntPtr(BackgroundAffinity.Replace(0, Utility.FullCPUMask));
					mAffinity = true;
				}
			}
			catch (OutOfMemoryException) { throw; }
			catch { }
			// info.Process.ProcessorAffinity = OriginalState.Affinity;

			if (PowerManagerEnabled && PowerPlan != Power.Mode.Undefined)
			{
				if (BackgroundPowerdown)
				{
					if (DebugPower) Log.Debug($"[{FriendlyName}] {info.Name} (#{info.Id}) power down");

					UndoPower(info);
				}
				else
					SetPower(info); // kinda hackish to call this here, but...
			}

			info.State = ProcessHandlingState.Paused;

			if (ShowProcessAdjusts && firsttime)
			{
				var ev = new ProcessModificationEventArgs(info)
				{
					PriorityNew = mPriority ? BackgroundPriority : null,
					PriorityOld = oldPriority,
					AffinityNew = mAffinity ? BackgroundAffinity : -1,
					AffinityOld = oldAffinity,
				};

				ev.User = new StringBuilder();

				ev.User.Append(" – Background Mode");

				OnAdjust?.Invoke(this, ev);
			}

			Paused?.Invoke(this, new ProcessModificationEventArgs(info));
		}

		public event EventHandler<ProcessModificationEventArgs> OnAdjust;

		public void SetForeground(ProcessEx info)
		{
			Debug.Assert(Foreground != ForegroundMode.Ignore, "Resume called for non-foreground rule");
			Debug.Assert(info.Controller != null, "No controller attached");

			bool mAffinity = false, mPriority = false;
			ProcessPriorityClass oldPriority;
			int oldAffinity, newAffinity;

			IOPriority nIO = IOPriority.Ignore;

			if (!info.InBackground)
			{
				if (DebugForeground) Log.Debug($"<Foreground> {FormatPathName(info)} (#{info.Id}) not paused; not resuming.");
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
				if (AffinityMask >= 0 && EstablishNewAffinity(oldAffinity, out newAffinity))
				{
					info.Process.ProcessorAffinity = new IntPtr(newAffinity.Replace(0, Utility.FullCPUMask));
					mAffinity = true;
				}
				else
					newAffinity = -1;

				if (IOPriorityEnabled)
					nIO = (IOPriority)SetIO(info, (IOPriority)DefaultForegroundIOPriority); // force these to always have normal I/O priority

				if (AffinityIdeal >= 0) ApplyAffinityIdeal(info);
			}
			catch (InvalidOperationException) // ID not available, probably exited
			{
				info.InBackground = false;
				return;
			}
			catch (Win32Exception) // access error
			{
				info.InBackground = false;
				return;
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				info.InBackground = false;
				return;
			}

			// PausedState.Priority = Priority;
			// PausedState.PowerMode = PowerPlan;

			if (PowerManagerEnabled && PowerPlan != Power.Mode.Undefined && BackgroundPowerdown)
				SetPower(info);

			info.InBackground = false;

			info.State = ProcessHandlingState.Resumed;

			if (DebugForeground && ShowProcessAdjusts)
			{
				var ev = new ProcessModificationEventArgs(info)
				{
					PriorityNew = mPriority ? (ProcessPriorityClass?)Priority.Value : null,
					PriorityOld = oldPriority,
					AffinityNew = mAffinity ? newAffinity : -1,
					AffinityOld = oldAffinity,
					NewIO = nIO,
				};

				ev.User = new StringBuilder();

				ev.User.Append(" – Foreground Mode");

				OnAdjust?.Invoke(this, ev);
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
			Debug.Assert(PowerManagerEnabled, "SetPower called despite power manager being disabled");
			Debug.Assert(PowerPlan != Power.Mode.Undefined, "Powerplan is undefined");
			Debug.Assert(info.Controller != null, "No controller attached");

			if (DebugPower || DebugForeground)
				Log.Debug("[" + FriendlyName + "] " + info.Name + " (#" + info.Id + ") foreground power on");

			bool rv = false;

			try
			{
				info.PowerWait = true;

				rv = powermanager.Force(PowerPlan, info.Id);

				WaitForExit(info);

				WaitingExit?.Invoke(this, new ProcessModificationEventArgs(info));

				if (DebugPower) Log.Debug($"[{FriendlyName}] {FormatPathName(info)} (#{info.Id.ToString()}) power exit wait set");
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
			if (info.PowerWait) powermanager?.Release(info);
		}

		static string[] UnwantedPathBits = new string[] { "x64", "x86", "bin", "debug", "release", "win32", "win64", "common", "binaries" };
		static string[] SpecialCasePathBits = new string[] { "steamapps" };

		public string FormatPathName(ProcessEx info)
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
							var parts = new List<string>(info.Path.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
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
							var parts = new List<string>(info.Path.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
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
									for (int i = 0; i < parts.Count - 1; i++)
									{
										string cur = parts[i].ToLowerInvariant();
										if (SpecialCasePathBits.Any((x) => x.Equals(cur))) // steamapps
										{
											parts[i] = HumanReadable.Generic.Ellipsis;
											parts.RemoveAt(++i); // common, i at app name, rolled over with loop
											replaced = false;
										}
										else if ((i > 2 && i < parts.Count - 3) // remove midpoint
											|| UnwantedPathBits.Any((x) => x.Equals(cur)) // flat out unwanted
											|| (info.Name.Length > 5 && cur.Any((x) => x.Equals(info.Name.ToLowerInvariant())))) // folder contains exe name
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
								parts[0] += System.IO.Path.DirectorySeparatorChar; // Path.Combine handles drive letter weird
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

		public async Task Modify(ProcessEx info)
		{
			await Touch(info).ConfigureAwait(false);
			if (Recheck > 0) await TouchReapply(info).ConfigureAwait(false); // this can go do its thing
		}

		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		bool IsForeground(int pid) => Foreground != ForegroundMode.Ignore ? activeappmonitor?.ForegroundId.Equals(pid) ?? true : true;

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
					End(info.Process, EventArgs.Empty);
					return true;
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				End(info.Process, EventArgs.Empty);
				return true;
			}

			return false;
		}

		public bool Ignored(ProcessEx info)
		{
			if (IgnoreList?.Any(item => item.Equals(info.Name, StringComparison.InvariantCultureIgnoreCase)) == true)
			{
				info.State = ProcessHandlingState.Abandoned;
				return true; // return ProcessState.Ignored;
			}

			return false;
		}

	// TODO: Simplify this
	async Task Touch(ProcessEx info, bool refresh = false)
		{
			Debug.Assert(info.Process != null, "ProcessController.Touch given null process.");
			Debug.Assert(!Utility.SystemProcessId(info.Id), "ProcessController.Touch given invalid process ID");
			Debug.Assert(!string.IsNullOrEmpty(info.Name), "ProcessController.Touch given empty process name.");
			Debug.Assert(info.Controller != null, "No controller attached");

			try
			{
				if (Foreground != ForegroundMode.Ignore && info.InBackground)
				{
					if (Trace && DebugForeground)
						Log.Debug("<Foreground> " + FormatPathName(info) + " (#" + info.Id + ") in background, ignoring.");
					info.State = ProcessHandlingState.Paused;
					return; // don't touch paused item
				}

				info.PowerWait = (PowerPlan != Power.Mode.Undefined);
				info.ForegroundWait = Foreground != ForegroundMode.Ignore;

				bool responding = true;
				ProcessPriorityClass? oldPriority = null;
				IntPtr? oldAffinity = null;

				int oldAffinityMask = 0;

				await Task.Delay(refresh ? 0 : ModifyDelay).ConfigureAwait(false);

				// EXTRACT INFORMATION

				try
				{
					if (ModifyDelay > 0) info.Process.Refresh();

					responding = info.Process.Responding;

					if (info.Process.HasExited)
					{
						if (Manager.DebugProcesses)
							Log.Debug("[" + FriendlyName + "] " + info.Name + " (#" + info.Id + ") has already exited.");
						info.State = ProcessHandlingState.Exited;
						return; // return ProcessState.Invalid;
					}

					oldAffinity = info.Process.ProcessorAffinity;
					oldPriority = info.Process.PriorityClass;
					oldAffinityMask = oldAffinity.Value.ToInt32().Replace(0, Utility.FullCPUMask);
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
							if (ShowInaction && Manager.DebugProcesses)
								Log.Debug($"[{FriendlyName}] {FormatPathName(info)} (#{info.Id.ToString()}) has been granted agency, ignoring.");
							info.State = ProcessHandlingState.Abandoned;
							return;
						}

						bool expected = false;
						if ((Priority.HasValue && info.Process.PriorityClass != Priority.Value)
							|| (AffinityMask >= 0 && info.Process.ProcessorAffinity.ToInt32() != AffinityMask))
						{
							ormt.ExpectedState--;
							Logging.DebugMsg($"[{FriendlyName}] {FormatPathName(info)} (#{info.Id.ToString()}) Recently Modified ({ormt.ExpectedState}) ---");
						}
						else
						{
							ormt.ExpectedState++;
							// TODO: allow modification in case this happens too much?
							Logging.DebugMsg($"[{FriendlyName}] {FormatPathName(info)} (#{info.Id.ToString()}) Recently Modified ({ormt.ExpectedState}) +++");
							expected = true;
						}

						if (ormt.LastIgnored.TimeTo(now) < Manager.IgnoreRecentlyModified
							|| ormt.LastModified.TimeTo(now) < Manager.IgnoreRecentlyModified)
						{
							if (Manager.DebugProcesses && Taskmaster.ShowInaction) Log.Debug($"[{FriendlyName}] {info.Name} (#{info.Id}) ignored due to recent modification. {(expected ? $"State unchanged ×{ormt.ExpectedState}" : $"State changed ×{ormt.ExpectedState}")}");

							if (ormt.ExpectedState == -2) // 2-3 seems good number
							{
								ormt.FreeWill = true;

								Logging.DebugMsg($"[{FriendlyName}] {FormatPathName(info)} (#{info.Id.ToString()}) agency granted");

								if (ShowAgency)
									Log.Debug($"[{FriendlyName}] {FormatPathName(info)} (#{info.Id.ToString()}) is resisting being modified: Agency granted.");

								ormt.Info.Process.Exited += ProcessExitEvent;

								// Agency granted, restore I/O priority to normal
								if (IOPriority != IOPriority.Ignore && (int)IOPriority < DefaultForegroundIOPriority) SetIO(info, (IOPriority)DefaultForegroundIOPriority); // restore normal I/O for these in case we messed around with it
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

						Logging.DebugMsg($"[{FriendlyName}] {FormatPathName(info)} (#{info.Id.ToString()}) pass through");
					}
					else
					{
						if (Manager.DebugProcesses) Log.Debug($"[{FriendlyName}] #{info.Id.ToString()} passed because name does not match; new: {info.Name}, old: {ormt.Info.Name}");

						RecentlyModified.TryRemove(info.Id, out _); // id does not match name
					}
					//RecentlyModified.TryRemove(info.Id, out _);
				}

				if (Trace) Log.Verbose($"[{FriendlyName}] Touching: {info.Name} (#{info.Id.ToString()})");

				info.Valid = true;

				// TODO: IgnoreSystem32Path

				if (Manager.DebugProcesses && info.PriorityProtected && ShowInaction)
					Log.Debug($"[{FriendlyName}] {info.Name} (#{info.Id.ToString()}) in protected list, limiting tampering.");

				ProcessPriorityClass? newPriority = null;
				IntPtr? newAffinity = null;

				bool mAffinity = false, mPriority = false, mPower = false, modified = false, fAffinity = false, fPriority = false;

				IOPriority nIO = IOPriority.Ignore;

				bool foreground = IsForeground(info.Id);

				bool FirstTimeSeenForForeground = true;
				if (!info.PriorityProtected)
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
							if (DebugForeground || ShowInaction)
								Log.Debug($"[{FriendlyName}] {info.Name} (#{info.Id}) not in foreground, not prioritizing.");

							SetBackground(info, FirstTimeSeenForForeground);
							// info.State = ProcessHandlingState.Paused; // Pause() sets this
							return;
						}
					}
				}
				else
				{
					if (ShowInaction && Manager.DebugProcesses)
						Log.Verbose($"[{FriendlyName}] {info.Name} (#{info.Id}) PROTECTED");
				}

				// APPLY CHANGES HERE
				if (!info.PriorityProtected)
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

					if (IOPriorityEnabled && IOPriority != IOPriority.Ignore)
						nIO = (IOPriority)SetIO(info);
				}

				int newAffinityMask = -1;

				if (AffinityMask >= 0 && !info.AffinityProtected)
				{
					newAffinityMask = AffinityMask.Replace(0, Utility.FullCPUMask);

					if (EstablishNewAffinity(oldAffinityMask, out newAffinityMask))
					{
						newAffinity = new IntPtr(newAffinityMask.Replace(0, Utility.FullCPUMask));
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
					if (Manager.DebugProcesses) Logging.DebugMsg($"{FormatPathName(info)} #{info.Id.ToString()} --- affinity not touched");
				}

				// TODO: Make sure the ideal matches set mask
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
				if (PowerManagerEnabled && PowerPlan != Power.Mode.Undefined)
				{
					if (!foreground && BackgroundPowerdown)
					{
						if (DebugForeground)
							Log.Debug($"{info.Name} (#{info.Id}) not in foreground, not powering up.");
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
						Adjusts++; // don't increment on power changes
					}

					LastTouch = now;
				}

				if (Priority.HasValue)
				{
					if (fPriority && ShowInaction && Manager.DebugProcesses)
						Log.Warning($"[{FriendlyName}] {info.Name} (#{info.Id}) failed to set process priority.");
				}
				if (AffinityMask >= 0)
				{
					if (fAffinity && ShowInaction && Manager.DebugProcesses)
						Log.Warning($"[{FriendlyName}] {info.Name} (#{info.Id}) failed to set process affinity.");
				}

				bool logevent = false;

				if (modified) logevent = ShowProcessAdjusts && !(Foreground != ForegroundMode.Ignore && !Manager.ShowForegroundTransitions);
				logevent |= (FirstTimeSeenForForeground && Foreground != ForegroundMode.Ignore);
				logevent |= (ShowInaction && Manager.DebugProcesses);

				var ev = new ProcessModificationEventArgs(info)
				{
					PriorityNew = newPriority,
					PriorityOld = oldPriority,
					AffinityNew = newAffinityMask,
					AffinityOld = oldAffinityMask,
					PriorityFail = Priority.HasValue && fPriority,
					AffinityFail = AffinityMask >= 0 && fAffinity,
					NewIO = nIO,
				};

				if (logevent)
				{
					var sbs = new StringBuilder();

					if (mPower) sbs.Append(" [Power Mode: ").Append(Power.Utility.GetModeName(PowerPlan)).Append("]");

					if (!modified && (ShowInaction && Manager.DebugProcesses)) sbs.Append(" – looks OK, not touched.");

					ev.User = sbs;

					OnAdjust?.Invoke(this, ev);
				}

				if (modified)
				{
					info.State = ProcessHandlingState.Modified;
					Modified?.Invoke(this, ev);

					if (Manager.IgnoreRecentlyModified.HasValue)
					{
						var rmt = new RecentlyModifiedInfo()
						{
							Info = info,
							LastModified = now,
							LastIgnored = DateTimeOffset.MinValue,
							FreeWill = false,
							ExpectedState = 0,
						};

						RecentlyModified.AddOrUpdate(info.Id, rmt, (int key, RecentlyModifiedInfo nrmt) =>
						{
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

					await InternalRefresh(now).ConfigureAwait(false);
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

		int DefaultForegroundIOPriority = 2;

		int SetIO(ProcessEx info, IOPriority overridePriority = IOPriority.Ignore)
		{
			int target = overridePriority == IOPriority.Ignore ? (int)IOPriority : (int)overridePriority;

			int nIO = -1;

			try
			{
				int original = Utility.SetIO(info.Process, target, out nIO);

				if (original < 0)
				{
					if (Manager.DebugProcesses && Trace)
						Log.Debug($"[{FriendlyName}] {info.Name} (#{info.Id}) – I/O priority access error");
				}
				else if (original == target)
				{
					if (Trace && Manager.DebugProcesses && ShowInaction)
						Log.Debug($"[{FriendlyName}] {info.Name} (#{info.Id}) – I/O priority ALREADY set to {original}, target: {target}");
					nIO = -1;
				}
				else
				{
					if (Manager.DebugProcesses && Trace)
					{
						if (nIO >= 0 && nIO != original)
							Log.Debug($"[{FriendlyName}] {info.Name} (#{info.Id}) – I/O priority set from {original} to {nIO}, target: {target}");
						else if (ShowInaction)
							Log.Debug($"[{FriendlyName}] {info.Name} (#{info.Id}) – I/O priority NOT set from {original} to {target}");
					}
				}
			}
			catch (OutOfMemoryException) { throw; }
			catch (ArgumentException)
			{
				if (Manager.DebugProcesses && ShowInaction && Trace)
					Log.Debug($"[{FriendlyName}] {info.Name} (#{info.Id}) – I/O priority not set, failed to open process.");
			}
			catch (InvalidOperationException)
			{
				if (Manager.DebugProcesses && Trace)
					Log.Debug($"[{FriendlyName}] {info.Name} (#{info.Id}) – I/O priority access error");
			}

			return nIO;
		}

		/// <summary>
		/// Sets new affinity mask. Returns true if the new mask differs from old.
		/// </summary>
		// TODO: Apply affinity strategy
		bool EstablishNewAffinity(int oldmask, out int newmask)
			=> (newmask = Utility.ApplyAffinityStrategy(oldmask, AffinityMask, AffinityStrategy)) != oldmask;

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
						if ((r.Value.LastIgnored.TimeTo(now) > Manager.IgnoreRecentlyModified)
							|| (r.Value.LastModified.TimeTo(now) > Manager.IgnoreRecentlyModified))
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
					if (DebugResize) Log.Debug($"<Resize> Already monitoring {info.Name} (#{info.Id.ToString()})");
					return;
				}

				var rect = new NativeMethods.RECT();

				if (info.Handle.Equals(IntPtr.Zero)) info.Handle = info.Process.MainWindowHandle;

				if (NativeMethods.GetWindowRect(info.Handle, ref rect))
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

					NativeMethods.MoveWindow(info.Handle, newsize.Left, newsize.Top, newsize.Width, newsize.Height, true);

					if (ResizeStrategy != WindowResizeStrategy.None)
					{
						Resize = newsize;
						NeedsSaving = true;
					}
				}

				StringBuilder sbs = null;
				if (DebugResize)
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

					if (ResizeStrategy == WindowResizeStrategy.None)
						sbs.Append("; remembering size or pos not enabled.");
					Log.Debug(sbs.ToString());
				}

				if (ResizeStrategy == WindowResizeStrategy.None) return;

				info.Resize = true;
				ActiveWait.TryAdd(info.Id, info);

				var re = new System.Threading.ManualResetEvent(false);
				MonitorWindowResize(info, rect, oldrect, re).ConfigureAwait(false);

				if (!WaitForExit(info))
				{
					info.Process.EnableRaisingEvents = true;
					info.Process.Exited += (_, _ea) => ProcessEndResize(info, oldrect, re);
					// TODO: ??? forgot to fill this in
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
				if (DebugResize) Log.Debug($"<Resize> Attempt failed for {info.Name} (#{info.Id.ToString()})");

				Logging.Stacktrace(ex);
				info.Resize = false;
			}
		}

		async Task MonitorWindowResize(ProcessEx info, NativeMethods.RECT rect, System.Drawing.Rectangle oldrect, System.Threading.ManualResetEvent re)
		{
			if (DebugResize) Log.Debug($"<Resize> Starting monitoring {info.Name} (#{info.Id.ToString()})");
			try
			{
				while (!re.WaitOne(60_000))
				{
					if (DebugResize) Log.Debug($"<Resize> Recording size and position for {info.Name} (#{info.Id.ToString()})");

					NativeMethods.GetWindowRect(info.Handle, ref rect);

					bool rpos = Bit.IsSet((int)ResizeStrategy, (int)WindowResizeStrategy.Position);
					bool rsiz = Bit.IsSet((int)ResizeStrategy, (int)WindowResizeStrategy.Size);
					Resize = new System.Drawing.Rectangle(
						rpos ? rect.Left : Resize.Value.Left, rpos ? rect.Top : Resize.Value.Top,
						rsiz ? rect.Right - rect.Left : Resize.Value.Left, rsiz ? rect.Bottom - rect.Top : Resize.Value.Top
						);

					NeedsSaving = true;
				}
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}

			if (DebugResize) Log.Debug($"<Resize> Stopping monitoring {info.Name} (#{info.Id.ToString()})");
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

				if ((Bit.IsSet((int)ResizeStrategy, (int)WindowResizeStrategy.Size)
					&& (oldrect.Width != Resize.Value.Width || oldrect.Height != Resize.Value.Height))
					|| (Bit.IsSet((int)ResizeStrategy, (int)WindowResizeStrategy.Position)
					&& (oldrect.Left != Resize.Value.Left || oldrect.Top != Resize.Value.Top)))
				{
					if (DebugResize) Log.Debug($"Saving {info.Name} (#{info.Id}) size to {Resize.Value.Width}×{Resize.Value.Height}");

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
				re?.Dispose();
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

			if (Manager.DebugProcesses) Log.Debug($"[{FriendlyName}] {info.Name} (#{info.Id}) rechecking");

			try
			{
				info.Process.Refresh();
				if (info.Process.HasExited)
				{
					info.State = ProcessHandlingState.Exited;
					if (Trace) Log.Verbose($"[{FriendlyName}] {info.Name} (#{info.Id}) is gone yo.");
					return;
				}

				await Touch(info, refresh: true).ConfigureAwait(false);
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException) // access denied or exited
			{
				return;
			}
			catch (Exception ex)
			{
				Log.Warning($"[{FriendlyName}] {info.Name} (#{info.Id}) – something bad happened.");
				Logging.Stacktrace(ex);
				info.State = ProcessHandlingState.Abandoned;
				return; //throw; // would throw but this is async function
			}
		}

		public string ToDetailedString()
		{
			var sbs = new StringBuilder();

			sbs.Append("[ ").Append(FriendlyName).AppendLine(" ]");
			if (Description?.Length > 0)
				sbs.Append("Description: ").AppendLine(Description).AppendLine();

			sbs.Append("Order preference: ").Append(OrderPreference.ToString()).Append(" – actual: ").AppendLine(ActualOrder.ToString());

			if (Executables?.Length > 0)
				sbs.Append("Executable").Append(Executables.Length == 1 ? string.Empty : "s").Append(": ").AppendLine(string.Join(", ", Executables));

			if (Path?.Length > 0)
				sbs.Append("Path: ").AppendLine(Path);

			sbs.AppendLine();

			if (ModifyDelay > 0)
				sbs.Append("Modify delay: ").Append(ModifyDelay).AppendLine(" ms");
			if (Recheck > 0)
				sbs.Append("Recheck: ").Append(Recheck).AppendLine(" ms");

			if (Priority.HasValue)
			{
				sbs.Append("Priority: ").Append(MKAh.Readable.ProcessPriority(Priority.Value))
					.Append(" – strategy: ").AppendLine(PriorityStrategy.ToString());
			}

			if (AffinityMask >= 0)
			{
				sbs.Append("Affinity: ").Append(HumanInterface.BitMask(AffinityMask, Utility.CPUCount)).Append(" [").Append(AffinityMask.ToString()).Append("]");
				if (AffinityIdeal >= 0)
					sbs.Append(" – ideal core: ").Append(AffinityIdeal);
				sbs.Append(" – strategy: ").AppendLine(AffinityStrategy.ToString());
			}

			if (PowerPlan != Power.Mode.Undefined)
				sbs.Append("Power plan: ").AppendLine(Power.Utility.GetModeName(PowerPlan));

			if (IOPriority != IOPriority.Ignore)
				sbs.Append("I/O priority: ").AppendLine(IOPriority.ToString());

			if (IgnoreList?.Length > 0)
				sbs.Append("Ignore: ").AppendLine(string.Join(", ", IgnoreList));

			sbs.AppendLine();
			if (VolumeStrategy != Audio.VolumeStrategy.Ignore)
				sbs.Append("Mixer volume: ").AppendFormat("{0:N0}", Volume * 100f).Append(" %")
					.Append(" – strategy: ").AppendLine(VolumeStrategy.ToString());

			sbs.Append("Log adjusts: ").Append(LogAdjusts ? "Enabled" : "Disabled")
				.Append(" – start & exit").AppendLine(LogStartAndExit ? "Enabled" : "Disabled");
			sbs.Append("Path visibility: ").Append(PathVisibility.ToString());

			if (DeclareParent)
				sbs.AppendLine("[DeclareParent]");

			if (ExclusiveMode)
				sbs.AppendLine("[Exclusive]");

			if (AllowPaging)
				sbs.AppendLine("[AllowPaging]");

			return sbs.ToString();
		}

		#region IDisposable Support
		public void Dispose() => Dispose(true);

		bool DisposedOrDisposing; // = false;

		void Dispose(bool disposing)
		{
			if (DisposedOrDisposing) return;

			if (disposing)
			{
				if (Trace) Log.Verbose("Disposing process controller [" + FriendlyName + "]");

				// clear event handlers
				Modified = null;
				Paused = null;
				Resumed = null;

				if (NeedsSaving) SaveConfig();
			}

			DisposedOrDisposing = true;
		}
		#endregion
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