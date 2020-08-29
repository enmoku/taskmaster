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

using MKAh;
using MKAh.Logic;
using MKAh.Synchronize;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Ini = MKAh.Ini;

namespace Taskmaster.Process
{
	using static Application;

	/// <summary>
	/// Process controller.
	/// </summary>
	public class Controller : IDisposable
	{
		internal bool Debug { get; set; } = false;

		/// <summary>
		/// <para>Don't allow user to tamper.</para>
		/// <para>Mostly for inbuilt rules that protect the system from bad configuration.</para>
		/// </summary>
		public bool Protected { get; set; } = false;

		// EVENTS

		public ModificationDelegate? Modified { get; set; }
		public InfoDelegate? Paused { get; set; }
		public InfoDelegate? Resumed { get; set; }
		public InfoDelegate? WaitingExit { get; set; }

		// Core information
		/// <summary>
		///
		/// </summary>
		public ProcessType Type { get; set; } = ProcessType.Generic;

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

		internal string[] pExecutables = Array.Empty<string>();

		/// <summary>
		/// Executable filename related to this, with extension.
		/// </summary>
		public string[] Executables
		{
			get => pExecutables;
			set
			{
				if (value is null) throw new ArgumentNullException(nameof(value));

				if (value.Length > 0)
				{
					var t_exe = new string[value.Length];
					var t_friendly = new string[value.Length];
					for (int i = 0; i < value.Length; i++)
					{
						t_exe[i] = value[i];
						t_friendly[i] = System.IO.Path.GetFileNameWithoutExtension(value[i]).ToLowerInvariant();
					}
					pExecutables = t_exe;
					ExecutableFriendlyName = t_friendly;
				}
				else
				{
					pExecutables = Array.Empty<string>();
					ExecutableFriendlyName = Array.Empty<string>();
				}
			}
		}

		internal void NullExecutalbes()
		{
			pExecutables = null;
			ExecutableFriendlyName = Array.Empty<string>();
		}

		/// <summary>
		/// Frienly executable name as required by various System.Process functions.
		/// Same as <see cref="ProcessControl.Executable"/> but with the extension missing.
		/// </summary>
		public string[] ExecutableFriendlyName { get; internal set; } = null;

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

		public int PathElements { get; private set; } = 0;

		/// <summary>
		/// User description for the rule.
		/// </summary>
		public string Description { get; set; } = string.Empty; // TODO: somehow unload this from memory

		/*
		// Load only as necessary
		public string Description
		{
			get
			{
				using var wcfg = Config.Load(Manager.WatchlistFile);
				return wcfg.Config.Get(FriendlyName)?.Get(HumanReadable.Generic.Description)?.String ?? string.Empty;
			}
			set
			{
				using var wcfg = Config.Load(Manager.WatchlistFile);
				wcfg.Config[FriendlyName][HumanReadable.Generic.Description].String = value;
			}
		}
		*/

		float _volume = 0.5f;

		/// <summary>
		/// Volume as 0.0 to 1.0 
		/// </summary>
		public float Volume { get => _volume; set => _volume = value.Constrain(0.0f, 1.0f); }
		public Audio.VolumeStrategy VolumeStrategy { get; set; } = Audio.VolumeStrategy.Ignore;

		public string[] IgnoreList { get; set; } = Array.Empty<string>();

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
		public System.Diagnostics.ProcessPriorityClass? Priority { get; set; }

		public IOPriority IOPriority { get; set; } = IOPriority.Ignore; // Win7 only?

		public PriorityStrategy PriorityStrategy { get; set; } = PriorityStrategy.Ignore;

		/// <summary>
		/// CPU core affinity.
		/// </summary>
		public int AffinityMask { get; set; } = -1;

		public AffinityStrategy AffinityStrategy;
		//int ScatterChunk = 1; // should default to Cores/4 or Range/2 at max, 1 at minimum.

		/// <summary>
		/// The power plan.
		/// </summary>
		public Power.Mode PowerPlan = Power.Mode.Undefined;

		public int Recheck { get; set; } = 0;

		public bool AllowPaging { get; set; } = false;

		public Process.PathVisibilityOptions PathVisibility { get; set; } = Process.PathVisibilityOptions.Process;

		//string PathMask { get; } = string.Empty; // UNUSED

		/// <summary>
		/// Controls whether this particular controller allows itself to be logged.
		/// </summary>
		public bool LogAdjusts { get; set; } = true;

		/// <summary>
		/// Log start and exit of the process.
		/// </summary>
		public bool LogStartAndExit { get; set; } = false;

		/// <summary>
		/// Warn about this rule matching.
		/// </summary>
		public bool Warn { get; set; } = false;

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
				AffinityStrategy = AffinityStrategy.Limit;
			}
		}

		public void SetForegroundMode(ForegroundMode mode)
		{
			Foreground = mode;

			BackgroundPowerdown = (Foreground == ForegroundMode.PowerOnly || Foreground == ForegroundMode.Full);

			switch (mode)
			{
				case ForegroundMode.Ignore:
					DisableTracking();
					break;
				case ForegroundMode.Standard:
					foreach (var info in ActiveWait.Values)
						if (info.PowerWait)
							globalmodules.powermanager?.Release(info);
					break;
				case ForegroundMode.Full:
					break;
				case ForegroundMode.PowerOnly:
					foreach (var info in ActiveWait.Values)
						info.ForegroundWait = info.InBackground = false;
					break;
			}
		}

		// TODO: This needs to be re-run every time path is altered.
		void Prepare()
		{
			if (PathElements == 0 && !string.IsNullOrEmpty(Path))
			{
				for (int i = 0; i < Path.Length; i++)
				{
					char c = Path[i];
					if (c == System.IO.Path.DirectorySeparatorChar || c == System.IO.Path.AltDirectorySeparatorChar) PathElements++;
				}

				if (!(Path.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.InvariantCulture)
					|| Path.EndsWith(System.IO.Path.AltDirectorySeparatorChar.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.InvariantCulture)))
					PathElements++;
			}
		}

		public void Repair()
		{
			PathElements = 0;
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
				bool haveExe = Executables.Length > 0;
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

				var sbs = new StringBuilder(256);

				sbs.Append('[').Append(FriendlyName).Append(']').Append(" Malconfigured. Following re-adjusted: ");

				var fixedList = new List<string>(8);

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

		public void Rename(string newName)
		{
			using var cfg = Config.Load(Manager.WatchlistFile);
			if (cfg.Config.TryGet(FriendlyName, out var section))
				section.Name = newName;
			//else
			//	Log.Warning("[" + FriendlyName + "] Does not exist in configuration."); // probably only happens when you rename a rule before it has been saved to disk?

			FriendlyName = newName;
		}

		void ProcessExitEvent(object sender, EventArgs _2)
		{
			if (sender is System.Diagnostics.Process process)
				RecentlyModified.TryRemove(process.Id, out _);
		}

		/// <summary>
		/// End various things for the given process
		/// </summary>
		public void End(object sender, EventArgs _2)
		{
			var process = sender as System.Diagnostics.Process;

			if (ActiveWait.TryRemove(process.Id, out var info))
			{
				info.State = HandlingState.Exited;
				//info.InBackground = false; // IRRELEVANT
				//info.ForegroundWait = false; // IRRELEVANT

				if (info.PowerWait && PowerPlan != Power.Mode.Undefined) UndoPower(info);
				info.PowerWait = false;
			}
		}

		/// <summary>
		/// Disable tracking.
		/// </summary>
		void DisableTracking()
		{
			if (DebugPower || Debug) Log.Debug($"[{FriendlyName}] Disabling tracking");

			foreach (var info in ActiveWait.Values)
			{
				info.Process.Refresh();
				if (!info.Process.HasExited)
				{
					info.InBackground = false;
					info.ForegroundWait = false;
					info.PowerWait = false;

					// Re-apply the controller?
				}
				else
					info.State = HandlingState.Exited;
			}

			RecentlyModified.Clear();
		}

		/// <summary>
		/// Refresh the controller, freeing resources, locks, etc. Does not affect valid still active instances.
		/// </summary>
		public void ResetInvalid()
		{
			if (DebugPower || Debug) Log.Debug($"[{FriendlyName}] Refresh");

			var cleanupList = new List<ProcessEx>(2);

			foreach (var info in ActiveWait.Values)
			{
				info.Process.Refresh();
				if (info.Process.HasExited)
				{
					info.State = HandlingState.Exited;
					cleanupList.Add(info);
				}
			}

			RecentlyModified.Clear();

			foreach (var info in cleanupList)
				End(info.Process, EventArgs.Empty);
		}

		public void SaveConfig()
		{
			using var cfg = Config.Load(Manager.WatchlistFile);
			Logging.DebugMsg("Saving: " + FriendlyName);
			SaveConfig(cfg.File);
		}

		public void SaveConfig(Configuration.File cfg, Ini.Section app = null)
		{
			System.Diagnostics.Debug.Assert(cfg != null);

			if (app is null) app = cfg.Config[FriendlyName];

			if (Executables.Length > 0)
			{
				var exarr = app["Executables"];
				if (!(exarr.StringArray?.SequenceEqual(Executables) ?? false)) exarr.StringArray = Executables;
			}
			else
				app.TryRemove("Executables");

			if (!string.IsNullOrEmpty(Path))
			{
				var path = app[HumanReadable.System.Process.Path];
				if (!(path.String?.Equals(Path, StringComparison.InvariantCulture) ?? false)) path.String = Path;
			}
			else
				app.TryRemove(HumanReadable.System.Process.Path);

			if (!string.IsNullOrEmpty(Description))
			{
				var desc = app[HumanReadable.Generic.Description];
				if (!(desc.String?.Equals(Description, StringComparison.InvariantCulture) ?? false)) desc.String = Description;
			}
			else
				app.TryRemove(HumanReadable.Generic.Description);

			if (Priority.HasValue)
			{
				var prioset = app[HumanReadable.System.Process.Priority];
				var priosetnew = Utility.PriorityToInt(Priority.Value);
				if (prioset.TryInt != priosetnew) prioset.Int = priosetnew;

				var priostrat = app[HumanReadable.System.Process.PriorityStrategy];
				if (priostrat.TryInt != (int)PriorityStrategy) priostrat.Int = (int)PriorityStrategy;
			}
			else
			{
				app.TryRemove(HumanReadable.System.Process.Priority);
				app.TryRemove(HumanReadable.System.Process.PriorityStrategy);
			}

			if (AffinityMask >= 0)
			{
				//if (affinity == ProcessManager.allCPUsMask) affinity = 0; // convert back

				var affset = app[HumanReadable.System.Process.Affinity];
				if (affset.TryInt != AffinityMask) affset.Int = AffinityMask;

				var affstrat = app[HumanReadable.System.Process.AffinityStrategy];
				if (affstrat.TryInt != (int)AffinityStrategy) affstrat.Int = (int)AffinityStrategy;
			}
			else
			{
				app.TryRemove(HumanReadable.System.Process.Affinity);
				app.TryRemove(HumanReadable.System.Process.AffinityStrategy);
			}

			if (IOPriority != IOPriority.Ignore)
			{
				var ioprio = app["IO priority"];
				if (ioprio.TryInt != (int)IOPriority) ioprio.Int = (int)IOPriority;
			}
			else
				app.TryRemove("IO priority");

			if (PowerPlan != Power.Mode.Undefined)
			{
				var powmode = app[HumanReadable.Hardware.Power.Mode];
				var powmodenew = Power.Utility.GetModeName(PowerPlan);
				if (!(powmode.String?.Equals(powmodenew, StringComparison.InvariantCultureIgnoreCase) ?? false)) powmode.String = powmodenew;
			}
			else
				app.TryRemove(HumanReadable.Hardware.Power.Mode);

			switch (Foreground)
			{
				case ForegroundMode.PowerOnly:
				case ForegroundMode.Ignore:
					if (Foreground == ForegroundMode.Ignore) app.TryRemove("Background powerdown");
					app.TryRemove("Foreground only");
					app.TryRemove("Foreground mode");
					app.TryRemove("Background priority");
					app.TryRemove("Background affinity");
					break;
				case ForegroundMode.Full:
				case ForegroundMode.Standard:
					if (Foreground == ForegroundMode.Standard) app.TryRemove("Background powerdown");

					var fgmode = app["Foreground mode"];
					if (fgmode.TryInt != (int)Foreground) fgmode.Int = (int)Foreground;

					if (BackgroundPriority.HasValue)
					{
						var bgprio = app["Background priority"];
						var bgprionew = Utility.PriorityToInt(BackgroundPriority.Value);
						if (bgprio.TryInt != bgprionew) bgprio.Int = bgprionew;
					}
					else
						app.TryRemove("Background priority");

					if (BackgroundAffinity >= 0)
					{
						var bgaff = app["Background affinity"];
						if (bgaff.TryInt != BackgroundAffinity) bgaff.Int = BackgroundAffinity;
					}
					else
						app.TryRemove("Background affinity");
					break;
			}

			if (AllowPaging)
			{
				var paging = app["Allow paging"];
				if (paging.TryBool != AllowPaging) paging.Bool = AllowPaging;
			}
			else
				app.TryRemove("Allow paging");

			if (PathVisibility != PathVisibilityOptions.Invalid)
			{
				var pathvis = app["Path visibility"];
				if (pathvis.TryInt != (int)PathVisibility) pathvis.Int = (int)PathVisibility;
			}
			else
				app.TryRemove("Path visibility");

			if (Executables.Length > 0 && Recheck > 0)
			{
				var recheck = app["Recheck"];
				if (recheck.TryInt != Recheck) recheck.Int = Recheck;
			}
			else
				app.TryRemove("Recheck");

			if (!Enabled)
			{
				var enabled = app[HumanReadable.Generic.Enabled];
				if (enabled.TryBool != Enabled) enabled.Bool = Enabled;
			}
			else
				app.TryRemove(HumanReadable.Generic.Enabled);

			var preforder = app["Preference"];
			if (preforder.TryInt != OrderPreference) preforder.Int = OrderPreference;

			if (IgnoreList.Length > 0)
			{
				var ignlist = app[HumanReadable.Generic.Ignore];
				if (!(ignlist.StringArray?.SequenceEqual(IgnoreList) ?? false)) ignlist.StringArray = IgnoreList;
			}
			else
				app.TryRemove(HumanReadable.Generic.Ignore);

			if (ModifyDelay > 0)
			{
				var modset = app[Constants.ModifyDelay];
				if (modset.TryInt != ModifyDelay) modset.Int = ModifyDelay;
			}
			else
				app.TryRemove(Constants.ModifyDelay);

			// DEPRECATED / OBSOLETE
			app.TryRemove("Resize strategy");
			app.TryRemove("Resize");

			if (VolumeStrategy != Audio.VolumeStrategy.Ignore)
			{
				var volset = app[HumanReadable.Hardware.Audio.Volume];
				if (volset.TryFloat != Volume) volset.Float = Volume;

				var volstrat = app[Constants.VolumeStrategy];
				if (volstrat.TryInt != (int)VolumeStrategy) volstrat.Int = (int)VolumeStrategy;
			}
			else
			{
				app.TryRemove(HumanReadable.Hardware.Audio.Volume);
				app.TryRemove(Constants.VolumeStrategy);
			}

			var logset = app[HumanReadable.Generic.Logging];
			if (logset.TryBool != LogAdjusts) logset.Bool = LogAdjusts;

			if (LogStartAndExit)
			{
				var logstartandexit = app["Log start and exit"];
				if (logstartandexit.TryBool != LogStartAndExit) logstartandexit.Bool = LogStartAndExit;
			}
			else
				app.TryRemove("Log start and exit");

			if (Warn)
			{
				var warn = app["Warn"];
				if (warn.TryBool != Warn) warn.Bool = Warn;
			}
			else
				app.TryRemove("Warn");

			if (LogDescription)
			{
				var logdesc = app["Log description"];
				if (logdesc.TryBool != LogDescription) logdesc.Bool = LogDescription;
			}
			else
				app.TryRemove("Log description");

			app.TryRemove(Constants.Exclusive); // DEPRECATED / OBSOLETE

			if (DeclareParent)
			{
				var decpar = app["Declare parent"];
				if (decpar.TryBool != DeclareParent) decpar.Bool = DeclareParent;
			}
			else
				app.TryRemove("Declare parent");

			var legacy = app.Get("Legacy workaround");
			if (legacy is null)
			{
				if (LegacyWorkaround) app["Legacy workaround"].Bool = LegacyWorkaround;
			}
			else if (legacy.TryBool != LegacyWorkaround)
				legacy.Bool = LegacyWorkaround;

			Logging.DebugMsg(cfg.Filename + " has gained " + cfg.Config.Changes.ToString(CultureInfo.InvariantCulture) + " total changes.");

			// pass to config manager
			NeedsSaving = false;
		}

		// The following should be combined somehow?
		/// <summary>
		/// List of processes actively waited on, such as things that need cleanup on exit.
		/// </summary>
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
			System.Diagnostics.Debug.Assert(Foreground != ForegroundMode.Ignore, "Pause called for non-foreground only rule");
			System.Diagnostics.Debug.Assert(info.Controller != null, "No controller attached");

			//Debug.Assert(!PausedIds.ContainsKey(info.Id));

			if (info.InBackground) return; // already paused

			if (DebugForeground && Trace) Log.Debug(info.ToFullFormattedString() + " Quelling.");

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
			catch { /* NOP */ }
			// info.Process.ProcessorAffinity = OriginalState.Affinity;

			if (PowerManagerEnabled && PowerPlan != Power.Mode.Undefined)
			{
				if (BackgroundPowerdown)
				{
					if (DebugPower) Log.Debug(info.ToFullFormattedString() + " Power down.");

					UndoPower(info);
				}
				else
					SetPower(info); // kinda hackish to call this here, but...
			}

			info.State = HandlingState.Paused;

			if (ShowProcessAdjusts && firsttime)
			{
				var ev = new ModificationInfo(info)
				{
					PriorityNew = mPriority ? BackgroundPriority : null,
					PriorityOld = oldPriority,
					AffinityNew = mAffinity ? BackgroundAffinity : -1,
					AffinityOld = oldAffinity,
				};

				ev.User = new StringBuilder(" – Background Mode", 128);

				OnAdjust?.Invoke(ev);
			}

			Paused?.Invoke(info);
		}

		public ModificationDelegate? OnAdjust;

		public void SetForeground(ProcessEx info)
		{
			System.Diagnostics.Debug.Assert(Foreground != ForegroundMode.Ignore, "Resume called for non-foreground rule");
			System.Diagnostics.Debug.Assert(info.Controller != null, "No controller attached");

			if (info.Restricted)
			{
				if (Debug) Logging.DebugMsg("<Process> " + info + " RESTRICTED - cancelling SetForeground");
				return;
			}

			bool mAffinity = false, mPriority = false;
			ProcessPriorityClass oldPriority;
			int oldAffinity, newAffinity;

			IOPriority nIO = IOPriority.Ignore;

			if (!info.InBackground)
			{
				if (DebugForeground) Log.Debug($"<Foreground> {FormatPathName(info)} #{info.Id} not paused; not resuming.");
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
					nIO = (IOPriority)SetIO(info, DefaultForegroundIOPriority); // force these to always have normal I/O priority
			}
			catch (InvalidOperationException) // ID not available, probably exited
			{
				info.InBackground = false;
				return;
			}
			catch (Win32Exception) // access error
			{
				info.InBackground = false;
				info.Restricted = true;
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

			info.State = HandlingState.Resumed;

			if (DebugForeground && ShowProcessAdjusts)
			{
				var ev = new ModificationInfo(info)
				{
					PriorityNew = mPriority ? (ProcessPriorityClass?)Priority.Value : null,
					PriorityOld = oldPriority,
					AffinityNew = mAffinity ? newAffinity : -1,
					AffinityOld = oldAffinity,
					NewIO = nIO,
				};

				ev.User = new StringBuilder(" – Foreground Mode", 128);

				OnAdjust?.Invoke(ev);
			}

			Resumed?.Invoke(info);
		}

		/// <summary>
		/// How many times we've touched associated processes.
		/// </summary>
		public int Adjusts { get; set; } = 0;

		/// <summary>
		/// Last seen any associated process.
		/// </summary>
		public DateTimeOffset LastSeen { get; private set; } = DateTimeOffset.MinValue;

		/// <summary>
		/// Last modified any associated process.
		/// </summary>
		public DateTimeOffset LastTouch { get; private set; } = DateTimeOffset.MinValue;

		/*
		public bool Children = false;
		public ProcessPriorityClass ChildPriority = ProcessPriorityClass.Normal;
		public bool ChildPriorityReduction = false;
		*/

		// -----------------------------------------------

		void ProcessEnd(object _, EventArgs _2)
		{

		}

		// -----------------------------------------------

		bool SetPower(ProcessEx info)
		{
			System.Diagnostics.Debug.Assert(PowerManagerEnabled, "SetPower called despite power manager being disabled");
			System.Diagnostics.Debug.Assert(PowerPlan != Power.Mode.Undefined, "Powerplan is undefined");
			System.Diagnostics.Debug.Assert(info.Controller != null, "No controller attached");

			if (DebugPower || DebugForeground)
				Log.Debug(info.ToFullFormattedString() + " foreground power on");

			try
			{
				info.PowerWait = true;

				bool rv = globalmodules.powermanager.Force(PowerPlan, info.Id);

				WaitForExit(info);

				WaitingExit?.Invoke(info);

				if (DebugPower) Log.Debug($"[{FriendlyName}] {FormatPathName(info)} #{info.Id.ToString()} power exit wait set");

				return rv;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				throw;
			}
		}

		static void UndoPower(ProcessEx info)
		{
			if (info.PowerWait) globalmodules.powermanager?.Release(info);
		}

		static readonly string[] UnwantedPathBits = new string[] { "x64", "x86", "bin", "debug", "release", "win32", "win64", "common", "binaries" };
		static readonly string[] SpecialCasePathBits = new string[] { "steamapps" };

		public string FormatPathName(ProcessEx info)
		{
			if (!string.IsNullOrEmpty(info.FormattedPath)) return info.FormattedPath;

			if (string.IsNullOrEmpty(info.Path)) return info.Name;

			switch (PathVisibility)
			{
				default:
					//case PathVisibilityOptions.Process:
					return info.Name;
				case PathVisibilityOptions.Partial:
					if (PathElements > 0)
					{
						var pparts = new List<string>(info.Path.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
						pparts.RemoveRange(0, PathElements);
						pparts.Insert(0, HumanReadable.Generic.Ellipsis);
						return info.FormattedPath = System.IO.Path.Combine(pparts.ToArray());
					}
					else
						return info.Path;
				case PathVisibilityOptions.Smart:
					// TODO: Cut off bin, x86, x64, win64, win32 or similar generic folder parts
					// TODO: Don't replace lone folders shorter than 4 characters.
					var sparts = new List<string>(info.Path.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
					if (PathElements > 0)
					{
						// following notes assume matching c:\program files
						sparts.RemoveRange(0, PathElements); // remove Path component

						// TODO: remove long path elements
						// TODO: remove repetitive path elements
						// replace
						if (sparts.Count > 4)
						{
							// TODO: Special cases for %APPDATA% and similar?

							// c:\program files\brand\app\version\random\element\executable.exe
							// ...\brand\app\...\executable.exe
							//parts.RemoveRange(3, parts.Count - 4); // remove all but two first and last
							//parts.Insert(3, HumanReadable.Generic.Ellipsis);

							bool previousWasCut = false;
							// remove unwanted bits
							for (int i = 1; i < sparts.Count - 1; i++) // always conserve first
							{
								string cur = sparts[i].ToLowerInvariant();
								if (SpecialCasePathBits.Any(x => x.Equals(cur, StringComparison.InvariantCulture))) // e.g. steamapps
								{
									sparts[i] = HumanReadable.Generic.Ellipsis;
									sparts.RemoveAt(++i); // common, i at app name, rolled over with loop
									previousWasCut = false;
								}
								else if ((i > 2 && i < sparts.Count - 3) // remove midpoint
									|| UnwantedPathBits.Any((x) => x.Equals(cur, StringComparison.InvariantCulture)) // flat out unwanted
									|| cur.Equals(info.Name, StringComparison.InvariantCultureIgnoreCase) // folder is same as exe name
									|| (info.Name.Length > 5 && cur.StartsWith(info.Name, StringComparison.InvariantCultureIgnoreCase))) // folder name starts with exe name
								{
									if (previousWasCut)
										sparts.RemoveAt(i--); // remove current and roll back loop
									else
									{
										sparts[i] = HumanReadable.Generic.Ellipsis;
										//sparts.Insert(i, System.IO.Path.DirectorySeparatorChar.ToString());
									}

									previousWasCut = true;
								}
								else
									previousWasCut = false;
							}
						}

						sparts.Insert(0, HumanReadable.Generic.Ellipsis); // add starting ellipsis

						// ...\brand\app\app.exe
					}
					else if (sparts.Count <= 5) // should consider total length, too
					{
						return info.Path; // as is
					}
					else
					{
						// Minimal structure
						// drive A B C file
						// 1 2 3 4 5
						// c:\programs\brand\app\app.exe as is
						// c:\programs\brand\app\v2.5\x256\bin\app.exe -> c:\programs\brand\app\...\app.exe

						sparts[0] += System.IO.Path.DirectorySeparatorChar; // Path.Combine doesn't handle drive letters in expected way

						sparts.RemoveRange(4, sparts.Count - 5);
						sparts.Insert(sparts.Count - 1, HumanReadable.Generic.Ellipsis);
					}

					//Console.WriteLine("Parts: " + string.Join(" || ", sparts.ToArray()));

					info.FormattedPath = System.IO.Path.Combine(sparts.ToArray());

					return info.FormattedPath;
				case PathVisibilityOptions.Full:
					return info.Path;
			}
		}

		public async Task Modify(ProcessEx info)
		{
			await Touch(info).ConfigureAwait(false);
			if (Recheck > 0) TouchReapply(info).ConfigureAwait(false); // this can go do its thing
		}

		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		bool IsForeground(int pid) => Foreground == ForegroundMode.Ignore || (globalmodules.activeappmonitor?.ForegroundId.Equals(pid) ?? true);

		bool WaitForExit(ProcessEx info)
		{
			ActiveWait.TryAdd(info.Id, info);

			try
			{
				WaitingExit?.Invoke(info);
				info.Process.Exited += End;
				info.HookExit();

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
			string name = info.Name;
			if (IgnoreList.Any(item => item.Equals(name, StringComparison.InvariantCultureIgnoreCase)))
			{
				info.State = HandlingState.Abandoned;
				return true; // return ProcessState.Ignored;
			}

			return false;
		}

		// TODO: SIMPLIFY THIS
		async Task Touch(ProcessEx info, bool refresh = false)
		{
			System.Diagnostics.Debug.Assert(info.Process != null, "ProcessController.Touch given null process.");
			System.Diagnostics.Debug.Assert(!Utility.SystemProcessId(info.Id), "ProcessController.Touch given invalid process ID");
			System.Diagnostics.Debug.Assert(!string.IsNullOrEmpty(info.Name), "ProcessController.Touch given empty process name.");
			System.Diagnostics.Debug.Assert(info.Controller != null, "No controller attached");

			if (info.Restricted)
			{
				if (Debug && ShowInaction) Logging.DebugMsg("<Process> " + info + " RESTRICTED - cancelling Touch");
				info.State = HandlingState.Invalid;
				return;
			}

			try
			{
				if (Foreground != ForegroundMode.Ignore && info.InBackground)
				{
					if (Trace && DebugForeground && ShowInaction)
						Log.Debug("<Foreground> " + FormatPathName(info) + " #" + info.Id.ToString(CultureInfo.InvariantCulture) + " in background, ignoring.");
					info.State = HandlingState.Paused;
					return; // don't touch paused item
				}

				info.PowerWait = (PowerPlan != Power.Mode.Undefined);
				info.ForegroundWait = Foreground != ForegroundMode.Ignore;

				ProcessPriorityClass? oldPriority = null;
				IntPtr? oldAffinity = null;

				int oldAffinityMask = 0;

				await Task.Delay(refresh ? 5 : ModifyDelay).ConfigureAwait(false);

				// EXTRACT INFORMATION

				try
				{
					if (ModifyDelay > 0) info.Process.Refresh();

					if (info.Process.HasExited)
					{
						if (Debug && ShowInaction) Log.Debug(info.ToFullFormattedString() + " has already exited.");
						info.State = HandlingState.Exited;
						return; // return ProcessState.Invalid;
					}

					oldAffinity = info.Process.ProcessorAffinity;
					oldPriority = info.Process.PriorityClass;
					oldAffinityMask = oldAffinity.Value.ToInt32().Replace(0, Utility.FullCPUMask);
				}
				catch (InvalidOperationException) // Already exited
				{
					info.State = HandlingState.Exited;
					return;
				}
				catch (Win32Exception) // denied
				{
					// failure to retrieve exit code, this probably means we don't have sufficient rights. assume it is gone.
					info.State = HandlingState.AccessDenied;
					info.Restricted = true;
					return;
				}
				catch (OutOfMemoryException) { throw; }
				catch (Exception ex) // invalidoperation or notsupported, neither should happen
				{
					Logging.Stacktrace(ex);
					info.State = HandlingState.Invalid;
					return;
				}

				var now = DateTimeOffset.UtcNow;

				int lAffinityMask = AffinityMask; // local copy

				if (LegacyWorkaround && !string.IsNullOrEmpty(info.Path))
				{
					// TODO: randomize core
					// TODO: spread core
					// TODO: smartly select least used core

					if (info.Legacy == LegacyLevel.Undefined)
						LegacyTest(info);

					if (info.Legacy == LegacyLevel.Win95 || info.IsUniprocessorOnly)
						lAffinityMask = Bit.Fill(0, lAffinityMask, 1); // single core
				}
				else
					info.Legacy = LegacyLevel.None;

				// TEST FOR RECENTLY MODIFIED
				if (RecentlyModified.TryGetValue(info.Id, out RecentlyModifiedInfo ormt))
				{
					bool CheckForRecentlyModified(ProcessEx info, RecentlyModifiedInfo ormt)
					{
						LastSeen = now;

						if (ormt.Info == info) // to make sure this isn't different process
						{
							if (ormt.FreeWill)
							{
								if (ShowInaction && Debug)
									Log.Debug($"[{FriendlyName}] {FormatPathName(info)} #{info.Id.ToString()} has been granted agency, ignoring.");
								info.State = HandlingState.Unmodified;
								return true;
							}

							// Ignore well behaving apps.
							if (ormt.Submitted && ormt.ExpectedState % 20 != 0)
							{
								ormt.ExpectedState++;
								if (Debug) Logging.DebugMsg($"[{FriendlyName}] {FormatPathName(info)} #{info.Id.ToString()} Is behaving well ({ormt.ExpectedState.ToString()}), skipping a check.");
								info.State = HandlingState.Unmodified;
								return true;
							}

							bool expected = true;
							// Branch-hint: False
							if ((Priority.HasValue && info.Process.PriorityClass != Priority.Value)
								|| (lAffinityMask >= 0 && info.Process.ProcessorAffinity.ToInt32() != lAffinityMask))
							{
								expected = false;
								ormt.ExpectedState--;
								ormt.Submitted = false;
								if (Trace) Logging.DebugMsg($"[{FriendlyName}] {FormatPathName(info)} #{info.Id.ToString()} Recently Modified ({ormt.ExpectedState}); Unexpected state.");
							}
							else
							{
								ormt.ExpectedState++;
								// MAYBE: Allow modification in case this happens too much?
								if (Trace) Logging.DebugMsg($"[{FriendlyName}] {FormatPathName(info)} #{info.Id.ToString()} Recently Modified ({ormt.ExpectedState}); Expected state.");
								if (ormt.ExpectedState > 20) ormt.Submitted = true;
							}

							if (ormt.LastIgnored.To(now) < Manager.IgnoreRecentlyModified
								|| ormt.LastModified.To(now) < Manager.IgnoreRecentlyModified)
							{
								if (Debug && ShowInaction) Log.Debug(info.ToFullFormattedString() + " Ignored due to recent modification. State " + (expected ? "un" : "") + "changed ×" + ormt.ExpectedState.ToString(CultureInfo.InvariantCulture));

								if (ormt.ExpectedState == -2) // 2-3 seems good number
								{
									ormt.FreeWill = true;

									Logging.DebugMsg($"[{FriendlyName}] {FormatPathName(info)} #{info.Id.ToString()} agency granted");

									if (ShowAgency)
										Log.Debug($"[{FriendlyName}] {FormatPathName(info)} #{info.Id.ToString()} is resisting being modified: Agency granted.");

									ormt.Info.Process.Exited += ProcessExitEvent;

									// Agency granted, restore I/O priority to normal
									if (IOPriority != IOPriority.Ignore && (int)IOPriority < (int)DefaultForegroundIOPriority)
										SetIO(info, DefaultForegroundIOPriority); // restore normal I/O for these in case we messed around with it
								}

								ormt.LastIgnored = now;

								Statistics.TouchIgnore++;

								info.State = HandlingState.Unmodified;
								return true;
							}
							else if (expected)
							{
								// this potentially ignores power modification
								info.State = HandlingState.Unmodified;
								return true;
							}

							Logging.DebugMsg($"[{FriendlyName}] {FormatPathName(info)} #{info.Id.ToString()} pass through");
						}
						else
						{
							if (Debug) Log.Debug($"[{FriendlyName}] #{info.Id.ToString()} passed because it does not match old #{ormt.Info.Id}");

							RecentlyModified.TryRemove(info.Id, out _); // id does not match name
						}
						//RecentlyModified.TryRemove(info.Id, out _);

						return false;
					}

					if (CheckForRecentlyModified(info, ormt)) return;
				}

				if (Trace) Log.Verbose(info.ToFullFormattedString() + " Touching...");

				info.Valid = true;

				// TODO: IgnoreSystem32Path

				if (Debug && info.PriorityProtected && ShowInaction)
					Log.Debug(info.ToFullFormattedString() + " Protected; Limiting tampering.");

				ProcessPriorityClass? newPriority = null;
				IntPtr? newAffinity;
				int newAffinityMask = -1;

				bool mAffinity = false, mPriority = false, mPower = false, modified = false, failSetAffinity = false, failSetPriority = false;

				IOPriority nIO = IOPriority.Ignore;

				bool FirstTimeSeenForForeground = true;
				bool foreground = false;

				if (info.FullyProtected)
				{
					Logging.DebugMsg(info.ToFullFormattedString() + " fully protected, ignoring.");
					goto LogModification;
				}

				foreground = IsForeground(info.Id);

				if (!info.PriorityProtected && Priority.HasValue)
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
								Log.Debug(info.ToFullFormattedString() + " Not in foreground, not prioritizing.");

							SetBackground(info, FirstTimeSeenForForeground);
							// info.State = ProcessHandlingState.Paused; // Pause() sets this
							return;
						}
					}

					// APPLY CHANGES
					try
					{
						if (info.Process.SetLimitedPriority(Priority.Value, PriorityStrategy))
						{
							modified = mPriority = true;
							newPriority = info.Process.PriorityClass;
						}
					}
					catch (OutOfMemoryException) { throw; }
					catch { failSetPriority = true; } // ignore errors, this is all we care of them

					if (IOPriorityEnabled && IOPriority != IOPriority.Ignore)
						nIO = (IOPriority)SetIO(info);
				}
				else
				{
					if (ShowInaction && Debug && info.PriorityProtected) Log.Verbose(info.ToFullFormattedString() + " PROTECTED");
				}

				if (lAffinityMask >= 0 && !info.AffinityProtected)
				{
					if (EstablishNewAffinity(oldAffinityMask, out newAffinityMask))
					{
						newAffinity = new IntPtr(newAffinityMask.Replace(0, Utility.FullCPUMask));
						try
						{
							info.Process.ProcessorAffinity = newAffinity.Value;
							modified = mAffinity = true;
						}
						catch (OutOfMemoryException) { throw; }
						catch { failSetAffinity = true; } // ignore errors, this is all we care of them
					}
					else
						newAffinityMask = -1;
				}
				else
				{
					if (Debug && Trace && ShowInaction) Logging.DebugMsg($"{FormatPathName(info)} #{info.Id.ToString()} --- affinity not touched");
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
				if (PowerManagerEnabled && PowerPlan != Power.Mode.Undefined)
				{
					if (!foreground && BackgroundPowerdown)
					{
						if (DebugForeground)
							Log.Debug(info + " not in foreground, not powering up.");
					}
					else
					{
						mPower = SetPower(info);
					}
				}

				info.Timer.Stop();

				if (ShowInaction && Debug && (failSetPriority || failSetAffinity))
				{
					var sbs = new StringBuilder(info.ToFullFormattedString()).Append(" Failed to set process ");

					if (failSetPriority) sbs.Append("priority");
					if (failSetAffinity)
					{
						if (failSetPriority) sbs.Append(", ");
						sbs.Append("affinity");
					}
					sbs.Append(".");
					Log.Warning(sbs.ToString());
				}

				LogModification:
				// such a mess
				bool logevent = modified && (ShowProcessAdjusts && !(Foreground != ForegroundMode.Ignore && !Manager.ShowForegroundTransitions));
				logevent |= (FirstTimeSeenForForeground && Foreground != ForegroundMode.Ignore);
				logevent |= (ShowInaction && Debug);

				if (logevent || modified)
				{
					var ev = new ModificationInfo(info)
					{
						PriorityNew = newPriority,
						PriorityOld = oldPriority,
						AffinityNew = newAffinityMask,
						AffinityOld = oldAffinityMask,
						PriorityFail = Priority.HasValue && failSetPriority,
						AffinityFail = AffinityMask >= 0 && failSetAffinity,
						NewIO = nIO,
					};

					if (logevent)
					{
						var sbs = new StringBuilder(256);

						if (mPower) sbs.Append(" [Power Mode: ").Append(Power.Utility.GetModeName(PowerPlan)).Append(']');

						if (!modified && ShowInaction && Debug)
						{
							if (failSetAffinity || failSetPriority) sbs.Append(" – failed to modify.");
							else sbs.Append(" – looks OK, not touched.");
						}

						ev.User = sbs;

						OnAdjust?.Invoke(ev);
					}

					if (modified)
					{
						if (mPriority || mAffinity)
						{
							Statistics.TouchCount++;
							Adjusts++; // don't increment on power changes
						}

						LastTouch = now;

						info.State = HandlingState.Modified;
						Modified?.Invoke(ev);

						if (Manager.IgnoreRecentlyModified.HasValue)
						{
							RecentlyModifiedInfo updateValueFactory(int key, RecentlyModifiedInfo nrmt)
							{
								// TODO: Match full path if available
								if (!nrmt.Info.Name.Equals(info.Name, StringComparison.InvariantCultureIgnoreCase))
								{
									// REPLACE. THIS SEEMS WRONG
									nrmt.Info = info;
									nrmt.FreeWill = false;
									nrmt.ExpectedState = 0;
									nrmt.LastModified = now;
									nrmt.LastIgnored = DateTimeOffset.MinValue;
								}

								return nrmt;
							}

							RecentlyModified.AddOrUpdate(
								info.Id,
								new RecentlyModifiedInfo(info, now),
								updateValueFactory
							);

							WaitForExit(info);
						}
					}
					else
						info.State = HandlingState.Unmodified;

					await InternalRefresh(now).ConfigureAwait(false);
				}
				else
					info.State = HandlingState.Finished;
			}
			catch (InvalidOperationException)
			{
				info.State = HandlingState.Exited;
			}
			catch (OutOfMemoryException) { info.State = HandlingState.Abandoned; throw; }
			catch (Exception ex)
			{
				info.State = HandlingState.Invalid;
				Logging.Stacktrace(ex);
			}
		}

		static void LegacyTest(ProcessEx info)
		{
			try
			{
				var pereader = new External.PeHeaderReader(info.Path);

				info.Is32BitExecutable = pereader.Is32BitHeader;
				info.IsUniprocessorOnly = pereader.IsUniprocessorOnly;
				info.IsLargeAddressAware = pereader.IsLargeAddressAware;

				if (pereader.Is32BitHeader)
				{
					//bool definiteLegacy = pereader.TimeStamp.Year < 1999; // pre w2k
					//bool likelyLegacy = pereader.TimeStamp.Year < 2002; // late arrival
					//var h32 = pereader.OptionalHeader32;
					int OsMajorVer = pereader.OptionalHeader32.MajorOperatingSystemVersion;
					//int OsMinorVer = h32.MinorOperatingSystemVersion;

					if (OsMajorVer < 5) // pre w2k
						info.Legacy = LegacyLevel.Win95;
					/*
					else if (osver < 6)
						info.Legacy = LegacyLevel.Win2k;
					else if (osver < 10)
						info.Legacy = LegacyLevel.Win7;
					else
						info.Legacy = LegacyLevel.Win10
					*/

					//Log.Debug($"[{FriendlyName}] {info.Name} #{info.Id} – LEGACY – OS Version: {osver}.{osvers} – Timestamp: {pereader.TimeStamp:g}");

				}
				else // 64 bit ones are guaranteed to be new enough
				{
					info.Legacy = LegacyLevel.None;
					/*
					var h64 = pereader.OptionalHeader64;
					int osver = h64.MajorOperatingSystemVersion;
					int osvers = h64.MinorOperatingSystemVersion;
					Log.Debug($"[{FriendlyName}] {info.Name} #{info.Id} – not-LEGACY (64bit) – OS Version: {osver}.{osvers} – Timestamp: {pereader.TimeStamp:g}");
					*/
				}
			}
			catch (System.IO.IOException)
			{
				Logging.DebugMsg(info.ToFullFormattedString() + " Legacy test failed due to access error.");

				// TODO: Retry after a bit?
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		const IOPriority DefaultForegroundIOPriority = IOPriority.Normal;

		int SetIO(ProcessEx info, IOPriority overridePriority = IOPriority.Ignore)
		{
			int target = overridePriority == IOPriority.Ignore ? (int)IOPriority : (int)overridePriority;

			int nIO = -1;

			try
			{
				int original = Utility.SetIO(info.Process, target, out nIO);

				if (original < 0)
				{
					if (Debug && Trace) Log.Debug(info.ToFullFormattedString() + " I/O priority access error");
				}
				else if (original == target)
				{
					if (Trace && Debug && ShowInaction)
						Log.Debug(info.ToFullFormattedString() + " I/O priority ALREADY set to " + original.ToString(CultureInfo.InvariantCulture) + ", target: " + target.ToString(CultureInfo.InvariantCulture));
					nIO = -1;
				}
				else
				{
					if (Debug && Trace)
					{
						if (nIO >= 0 && nIO != original)
							Log.Debug(info.ToFullFormattedString() + " I/O priority set from " + original.ToString() + " to " + nIO.ToString() + ", target: " + target.ToString());
						else if (ShowInaction)
							Log.Debug(info.ToFullFormattedString() + " I/O priority NOT set from " + original.ToString() + " to " + target.ToString());
					}
				}
			}
			catch (OutOfMemoryException) { throw; }
			catch (ArgumentException)
			{
				if (Debug && ShowInaction && Trace) Log.Debug(info.ToFullFormattedString() + " I/O priority not set, failed to open process.");
			}
			catch (InvalidOperationException)
			{
				if (Debug && Trace)
					Log.Debug(info.ToFullFormattedString() + " I/O priority access error");
			}

			return nIO;
		}

		/// <summary>
		/// Sets new affinity mask. Returns true if the new mask differs from old.
		/// </summary>
		// TODO: Apply affinity strategy
		bool EstablishNewAffinity(int oldmask, out int newmask)
			=> (newmask = Utility.ApplyAffinityStrategy(oldmask, AffinityMask, AffinityStrategy)) != oldmask;

		GenericLock refresh_lock = new GenericLock();

		async Task InternalRefresh(DateTimeOffset now)
		{
			if (!refresh_lock.TryLock()) return;
			using var scoped_unlock = refresh_lock.ScopedUnlock();

			await Task.Delay(0).ConfigureAwait(false);

			if (RecentlyModified.Count > 5)
			{
				var ignrecent = Manager.IgnoreRecentlyModified;

				foreach (var r in RecentlyModified.Where(x => x.Value.LastIgnored.To(now) > ignrecent || x.Value.LastModified.To(now) > ignrecent))
					RecentlyModified.TryRemove(r.Key, out _);
			}
		}

		public bool NeedsSaving { get; set; } = false;

		public bool ColorReset { get; set; } = false;

		public bool LegacyWorkaround { get; set; } = false;

		async Task TouchReapply(ProcessEx info)
		{
			await Task.Delay(Math.Max(Recheck, 5) * 1_000).ConfigureAwait(false);

			if (Debug) Log.Debug(info.ToFullFormattedString() + " Rechecking.");

			try
			{
				info.Process.Refresh();
				if (info.Process.HasExited)
				{
					info.State = HandlingState.Exited;
					if (Trace) Log.Verbose(info.ToFullFormattedString() + " Process has disappeared.");
					return;
				}

				await Touch(info, refresh: true).ConfigureAwait(false);
			}
			catch (OutOfMemoryException) { throw; }
			catch (Win32Exception)
			{
				info.Restricted = true;
			}
			catch (InvalidOperationException) // exited
			{
				info.State = HandlingState.Exited;
			}
			catch (Exception ex)
			{
				Log.Warning(info.ToFullFormattedString() + " Something bad happened while re-applying changes.");
				Logging.Stacktrace(ex);
				info.State = HandlingState.Abandoned;
				//throw; // would throw but this is async function
			}
		}

		public string ToDetailedString()
		{
			var sbs = new StringBuilder(1024 * 2);

			sbs.Append("[ ").Append(FriendlyName).AppendLine(" ]");
			if (Description?.Length > 0)
				sbs.Append("Description: ").AppendLine(Description).AppendLine();

			sbs.Append("Order preference: ").Append(OrderPreference.ToString()).Append(" – actual: ").AppendLine(ActualOrder.ToString());

			if (Executables.Length > 0)
				sbs.Append("Executable").Append(Executables.Length == 1 ? string.Empty : "s").Append(": ").AppendLine(string.Join(", ", Executables));

			if (Path?.Length > 0)
				sbs.Append("Path: ").AppendLine(Path);

			sbs.AppendLine();

			if (ModifyDelay > 0)
				sbs.Append(Constants.ModifyDelay).Append(": ").Append(ModifyDelay).AppendLine(" ms");
			if (Recheck > 0)
				sbs.Append("Recheck: ").Append(Recheck).AppendLine(" ms");

			if (Priority.HasValue)
			{
				sbs.Append("Priority: ").Append(MKAh.Readable.ProcessPriority(Priority.Value))
					.Append(" – strategy: ").AppendLine(PriorityStrategy.ToString());
			}

			if (AffinityMask >= 0)
			{
				sbs.Append("Affinity: ")
					.Append(Process.Utility.FormatBitMask(AffinityMask, Hardware.Utility.ProcessorCount, LogBitmask));
				sbs.Append(" – strategy: ").AppendLine(AffinityStrategy.ToString());
			}

			if (PowerPlan != Power.Mode.Undefined)
				sbs.Append("Power plan: ").AppendLine(Power.Utility.GetModeName(PowerPlan));

			if (IOPriority != IOPriority.Ignore)
				sbs.Append("I/O priority: ").AppendLine(IOPriority.ToString());

			if (IgnoreList.Length > 0)
				sbs.Append("Ignore: ").AppendLine(string.Join(", ", IgnoreList));

			sbs.AppendLine();
			if (VolumeStrategy != Audio.VolumeStrategy.Ignore)
				sbs.Append("Mixer volume: ").AppendFormat(CultureInfo.InvariantCulture, "{0:N0}", Volume * 100f).Append(" %")
					.Append(" – strategy: ").AppendLine(VolumeStrategy.ToString());

			sbs.Append("Log adjusts: ").Append(LogAdjusts ? "Enabled" : "Disabled")
				.Append(" – start & exit").AppendLine(LogStartAndExit ? "Enabled" : "Disabled");
			sbs.Append("Path visibility: ").Append(PathVisibility.ToString());

			if (DeclareParent)
				sbs.AppendLine("[DeclareParent]");

			if (AllowPaging)
				sbs.AppendLine("[AllowPaging]");

			return sbs.ToString();
		}

		#region IDisposable Support
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		private bool disposed; // = false;

		protected virtual void Dispose(bool disposing)
		{
			if (disposed) return;
			disposed = true;

			if (disposing)
			{
				if (Trace) Log.Verbose("Disposing process controller [" + FriendlyName + "]");

				// clear event handlers
				Modified = null;
				Paused = null;
				Resumed = null;

				try
				{
					if (NeedsSaving) SaveConfig();
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
					throw;
				}

				//base.Dispose();
			}
		}
		#endregion
	}

	public class RecentlyModifiedInfo
	{
		public RecentlyModifiedInfo(ProcessEx info, DateTimeOffset lastModified)
		{
			Info = info;
			LastModified = lastModified;
		}

		public ProcessEx Info { get; set; } = null;

		public bool FreeWill { get; set; } = false;

		public int ExpectedState { get; set; } = 0;

		public bool Submitted { get; set; } = false;

		public DateTimeOffset LastModified { get; set; }
		public DateTimeOffset LastIgnored { get; set; } = DateTimeOffset.MinValue;
	}
}