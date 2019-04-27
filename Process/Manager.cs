//
// Process.Manager.cs
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
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MKAh;
using MKAh.Logic;
using Ini = MKAh.Ini;
using Serilog;

namespace Taskmaster.Process
{
	using static Taskmaster;

	sealed public class ProcessingCountEventArgs : EventArgs
	{
		/// <summary>
		/// Adjustment to previous total.
		/// </summary>
		public int Adjust { get; set; } = 0;
		/// <summary>
		/// Total items being processed.
		/// </summary>
		public int Total { get; set; } = 0;

		public ProcessingCountEventArgs(int count, int total)
		{
			Adjust = count;
			Total = total;
		}
	}

	sealed public class HandlingStateChangeEventArgs : EventArgs
	{
		public ProcessEx Info { get; set; } = null;

		public HandlingStateChangeEventArgs(ProcessEx info)
		{
			Debug.Assert(info != null, "ProcessEx is not assigned");
			Info = info;
		}
	}

	sealed public class Manager : IDisposal, IDisposable
	{
		Analyzer analyzer = null;

		public static bool DebugScan { get; set; } = false;
		public static bool DebugPaths { get; set; } = false;
		public static bool DebugAdjustDelay { get; set; } = false;
		public static bool DebugPaging { get; set; } = false;
		public static bool DebugProcesses { get; set; } = false;
		public static bool ShowUnmodifiedPortions { get; set; } = true;
		public static bool ShowOnlyFinalState { get; set; } = false;
		public static bool ShowForegroundTransitions { get; set; } = false;

		bool WindowResizeEnabled { get; set; } = false;

		/// <summary>
		/// Watch rules
		/// </summary>
		ConcurrentDictionary<Controller, int> Watchlist = new ConcurrentDictionary<Controller, int>();

		ConcurrentDictionary<int, DateTimeOffset> ScanBlockList = new ConcurrentDictionary<int, DateTimeOffset>();

		Lazy<List<Controller>> WatchlistCache = null;

		readonly object watchlist_lock = new object();

		public bool WMIPolling { get; private set; } = true;
		public int WMIPollDelay { get; private set; } = 5;

		public static TimeSpan? IgnoreRecentlyModified { get; set; } = TimeSpan.FromMinutes(30);

		// ctor, constructor
		public Manager()
		{
			RenewWatchlistCache();

			AllCPUsMask = Convert.ToInt32(Math.Pow(2, CPUCount) - 1 + double.Epsilon);

			if (RecordAnalysis.HasValue) analyzer = new Analyzer();

			//allCPUsMask = 1;
			//for (int i = 0; i < CPUCount - 1; i++)
			//	allCPUsMask = (allCPUsMask << 1) | 1;

			if (DebugProcesses) Log.Debug($"<CPU> Logical cores: {CPUCount}, full mask: {Convert.ToString(AllCPUsMask, 2)} ({AllCPUsMask} = OS control)");

			LoadConfig();

			LoadWatchlist();

			InitWMIEventWatcher();

			var InitialScanDelay = TimeSpan.FromSeconds(5);
			NextScan = DateTimeOffset.UtcNow.Add(InitialScanDelay);
			if (ScanFrequency.HasValue)
			{
				ScanTimer = new System.Timers.Timer(ScanFrequency.Value.TotalMilliseconds);
				ScanTimer.Elapsed += TimedScan;
			}

			MaintenanceTimer = new System.Timers.Timer(1_000 * 60 * 60 * 3); // every 3 hours
			MaintenanceTimer.Elapsed += CleanupTick;
			MaintenanceTimer.Start();

			Taskmaster.OnStart += OnStart;

			if (DebugProcesses) Log.Information("<Process> Component Loaded.");

			RegisterForExit(this);
			DisposalChute.Push(this);
		}

		async void OnStart(object sender, EventArgs ea)
			=> await Task.Run(() => Scan(), cts.Token).ContinueWith((_) => StartScanTimer(), cts.Token).ConfigureAwait(false);

		public Controller[] getWatchlist() => Watchlist.Keys.ToArray();

		// TODO: Need an ID mapping
		public bool GetControllerByName(string friendlyname, out Controller controller)
			=> (controller = (from prc
					in Watchlist.Keys
					where prc.FriendlyName.Equals(friendlyname, StringComparison.InvariantCultureIgnoreCase)
					select prc)
					.FirstOrDefault()) != null;

		/// <summary>
		/// Executable name to ProcessControl mapping.
		/// </summary>
		ConcurrentDictionary<string, Controller> ExeToController = new ConcurrentDictionary<string, Controller>();

		public bool GetController(ProcessEx info, out Controller prc)
		{
			if (info.Controller != null)
			{
				prc = info.Controller;
				return true;
			}

			if (ExeToController.TryGetValue(info.Name.ToLowerInvariant(), out prc))
				return true;

			if (!string.IsNullOrEmpty(info.Path) && GetPathController(info, out prc))
				return true;

			return false;
		}

		public static int CPUCount = Environment.ProcessorCount;
		public static int AllCPUsMask = Convert.ToInt32(Math.Pow(2, CPUCount) - 1 + double.Epsilon);

		public int DefaultBackgroundPriority = 1;
		public int DefaultBackgroundAffinity = 0;

		ForegroundManager activeappmonitor = null;
		public void Hook(ForegroundManager manager)
		{
			activeappmonitor = manager;
			activeappmonitor.ActiveChanged += ForegroundAppChangedEvent;
			activeappmonitor.OnDisposed += (_, _ea) => activeappmonitor = null;
		}

		Power.Manager powermanager = null;
		public void Hook(Power.Manager manager)
		{
			powermanager = manager;
			powermanager.onBehaviourChange += PowerBehaviourEvent;
			powermanager.OnDisposed += (_, _ea) => powermanager = null;
		}

		ConcurrentDictionary<int, int> ignorePids = new ConcurrentDictionary<int, int>();

		public void Ignore(int pid) => ignorePids.TryAdd(pid, 0);

		public void Unignore(int pid) => ignorePids.TryRemove(pid, out _);

		int freemem_lock = 0;
		public async Task FreeMemory(string executable = null, bool quiet = false, int ignorePid = -1)
		{
			if (!PagingEnabled) return;

			if (!Atomic.Lock(ref freemem_lock)) return;

			await Task.Delay(0).ConfigureAwait(false);

			try
			{
				if (string.IsNullOrEmpty(executable))
				{
					if (DebugPaging && !quiet) Log.Debug("<Process> Paging applications to free memory...");
				}
				else
				{
					var procs = System.Diagnostics.Process.GetProcessesByName(executable); // unnecessary maybe?
					if (procs.Length == 0)
					{
						Log.Error(executable + " not found, not freeing memory for it.");
						return;
					}

					foreach (var prc in procs)
					{
						if (executable.Equals(prc.ProcessName, StringComparison.InvariantCultureIgnoreCase))
						{
							ignorePid = prc.Id;
							break;
						}
					}

					if (DebugPaging && !quiet) Log.Debug("<Process> Paging applications to free memory for: " + executable);
				}

				//await Task.Delay(0).ConfigureAwait(false);

				FreeMemoryInternal(ignorePid, executable);
			}
			catch (Exception ex) when (ex is NullReferenceException || ex is OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				Atomic.Unlock(ref freemem_lock);
			}
		}

		void FreeMemoryInternal(int ignorePid = -1, string ignoreExe=null)
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException("FreeMemoryInterval called when ProcessManager was already disposed");

			Memory.Update();
			ulong b1 = Memory.FreeBytes;
			//var b1 = MemoryManager.Free;

			try
			{
				ScanTimer?.Stop();
				// TODO: Pause Scan until we're done

				Scan(ignorePid, ignoreExe, PageToDisk: true); // TODO: Call for this to happen otherwise
				if (cts.IsCancellationRequested) return;

				// TODO: Wait a little longer to allow OS to Actually page stuff. Might not matter?
				//var b2 = MemoryManager.Free;
				Memory.Update();
				ulong b2 = Memory.FreeBytes;

				Log.Information("<Memory> Paging complete, observed memory change: " +
					HumanInterface.ByteString((long)(b2 - b1), true, iec: true));
			}
			catch (Exception ex) when (ex is AggregateException || ex is OperationCanceledException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}

			StartScanTimer();
		}

		/// <summary>
		/// Spawn separate thread to run program scanning.
		/// </summary>
		async void TimedScan(object _, EventArgs _ea)
		{
			if (DisposedOrDisposing) return; // HACK: dumb timers be dumb

			try
			{
				if (Trace) Log.Verbose("Rescan requested.");

				await Task.Delay(0).ConfigureAwait(false); // asyncify
				if (cts.IsCancellationRequested) return;

				Scan();
				if (cts.IsCancellationRequested) return;
			}
			catch (Exception ex) when (ex is AggregateException || ex is OperationCanceledException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				Log.Error("Stopping periodic scans");
				ScanTimer?.Stop();
			}
		}

		//public event EventHandler ScanStartEvent;
		//public event EventHandler ScanEndEvent;

		public event EventHandler<ProcessModificationEventArgs> ProcessModified;

		public event EventHandler<ProcessingCountEventArgs> HandlingCounter;
		public event EventHandler<ProcessModificationEventArgs> ProcessStateChange;

		public event EventHandler<HandlingStateChangeEventArgs> HandlingStateChange;

		int hastenscan = 0;
		public async void HastenScan(int delay = 15, bool sort = false)
		{
			// delay is unused but should be used somehow

			if (!Atomic.Lock(ref hastenscan)) return;

			try
			{
				await Task.Delay(0).ConfigureAwait(false); // asyncify

				double nextscan = Math.Max(0, DateTimeOffset.UtcNow.TimeTo(NextScan).TotalSeconds);
				if (nextscan > 5) // skip if the next scan is to happen real soon
				{
					NextScan = DateTimeOffset.UtcNow;
					ScanTimer?.Stop();
					if (sort) lock (watchlist_lock) SortWatchlist();
					if (cts.IsCancellationRequested) return;

					await Task.Run(() => Scan(), cts.Token).ContinueWith((_) => StartScanTimer(), cts.Token).ConfigureAwait(false);
				}
			}
			catch (Exception ex) when (ex is NullReferenceException || ex is OutOfMemoryException) { throw; }
			catch (TaskCanceledException) { } // NOP
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				Atomic.Unlock(ref hastenscan);
			}
		}

		void StartScanTimer()
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException("StartScanTimer called when ProcessManager was already disposed");

			if (!ScanFrequency.HasValue) return; // dumb hack

			try
			{
				// restart just in case
				ScanTimer?.Stop();
				ScanTimer?.Start();
				NextScan = DateTimeOffset.UtcNow.AddMilliseconds(ScanTimer.Interval);
			}
			catch (ObjectDisposedException) { Statistics.DisposedAccesses++; }
		}

		readonly int SelfPID = System.Diagnostics.Process.GetCurrentProcess().Id;

		int ScanFoundProcs = 0;

		int scan_lock = 0;
		bool Scan(int ignorePid = -1, string ignoreExe=null, bool PageToDisk = false)
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException("Scan called when ProcessManager was already disposed");
			if (cts.IsCancellationRequested) return false;

			if (!Atomic.Lock(ref scan_lock)) return false;

			try
			{
				NextScan = (LastScan = DateTimeOffset.UtcNow).Add(ScanFrequency.Value);

				if (DebugScan) Log.Debug("<Process> Full Scan: Start");

				//ScanStartEvent?.Invoke(this, EventArgs.Empty);

				if (!SystemProcessId(ignorePid)) Ignore(ignorePid);

				var procs = System.Diagnostics.Process.GetProcesses();
				ScanFoundProcs = procs.Length - 2; // -2 for Idle&System
				Debug.Assert(ScanFoundProcs > 0, "System has no running processes"); // should be impossible to fail
				SignalProcessHandled(ScanFoundProcs); // scan start

				//var loaderOffload = new List<ProcessEx>();

				List<ProcessEx> pageList = PageToDisk ? new List<ProcessEx>() : null;

				foreach (var process in procs)
					ScanTriage(process, PageToDisk);

				//SystemLoaderAnalysis(loaderOffload);

				if ((pageList?.Count ?? 0) > 0)
				{
					Task.Run(() =>
					{
						foreach (var info in pageList)
						{
							try
							{
								info.Process.Refresh();
								if (info.Process.HasExited) continue;
								NativeMethods.EmptyWorkingSet(info.Process.Handle); // process.Handle may throw which we don't care about
							}
							catch { }
						}
						pageList.Clear();

					}, cts.Token).ConfigureAwait(false);
				}

				if (DebugScan) Log.Debug("<Process> Full Scan: Complete");

				SignalProcessHandled(-ScanFoundProcs); // scan done

				//ScanEndEvent?.Invoke(this, EventArgs.Empty);

				if (!SystemProcessId(ignorePid)) Unignore(ignorePid);
			}
			catch (Exception ex) when (ex is NullReferenceException || ex is OutOfMemoryException || ex is OperationCanceledException) { throw; }
			catch (AggregateException ex)
			{
				System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException ?? ex).Throw();
				throw;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				Atomic.Unlock(ref scan_lock);
			}

			return true;
		}

		private void ScanTriage(System.Diagnostics.Process process, bool PageToDisk=false)
		{
			cts.Token.ThrowIfCancellationRequested();

			string name = string.Empty;
			int pid = -1;

			try
			{
				name = process.ProcessName;
				pid = process.Id;

				if (ScanBlockList.TryGetValue(pid, out var found))
				{
					if (found.TimeTo(DateTimeOffset.UtcNow).TotalSeconds < 5d) return;
					ScanBlockList.TryRemove(pid, out _);
				}

				if (IgnoreProcessID(pid) || IgnoreProcessName(name) || pid == SelfPID)
				{
					if (ShowInaction && DebugScan) Log.Debug($"<Process/Scan> Ignoring {name} (#{pid})");
					return;
				}

				if (DebugScan) Log.Verbose($"<Process> Checking: {name} (#{pid})");

				ProcessEx info = null;

				if (WaitForExitList.TryGetValue(pid, out info))
				{
					if (Trace && DebugProcesses) Debug.WriteLine($"Re-using old ProcessEx: {info.Name} (#{info.Id})");
					info.Process.Refresh();
					if (info.Process.HasExited) // Stale, for some reason still kept
					{
						if (Trace && DebugProcesses) Debug.WriteLine("Re-using old ProcessEx - except not, stale");
						WaitForExitList.TryRemove(pid, out _);
						info = null;
					}
					else
						info.State = ProcessHandlingState.Triage; // still valid
				}

				if (info != null || Utility.GetInfo(pid, out info, process, null, name, null, getPath: true))
				{
					info.Timer = Stopwatch.StartNew();

					ProcessTriage(info, old:true).ConfigureAwait(false);

					if (PageToDisk)
					{
						try
						{
							if (DebugPaging) Log.Debug($"<Process> Paging: {info.Name} (#{info.Id})");

							NativeMethods.EmptyWorkingSet(info.Process.Handle); // process.Handle may throw which we don't care about
						}
						catch { } // don't care
					}

					HandlingStateChange?.Invoke(this, new HandlingStateChangeEventArgs(info));

					//if (!info.Exited) loaderOffload.Add(info);
				}
			}
			catch (Exception ex) when (ex is NullReferenceException || ex is OutOfMemoryException) { throw; }
			catch (OperationCanceledException) { throw; }
			catch (InvalidOperationException)
			{
				if (ShowInaction && DebugScan)
					Log.Debug($"<Process/Scan> Failed to retrieve info for process: {name} (#{pid})");
			}
			catch (AggregateException ex)
			{
				System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException ?? ex).Throw();
				throw;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		int SystemLoader_lock = 0;
		DateTimeOffset LastLoaderAnalysis = DateTimeOffset.MinValue;

		async Task SystemLoaderAnalysis(List<ProcessEx> procs)
		{
			var now = DateTimeOffset.UtcNow;

			if (!Atomic.Lock(ref SystemLoader_lock)) return;

			// TODO: TEST IF ANY RESOURCE IS BEING DRAINED

			try
			{
				if (LastLoaderAnalysis.TimeTo(now).TotalMinutes < 1d) return;

				await Task.Delay(5_000).ConfigureAwait(false);
				if (cts.IsCancellationRequested) return;

				long PeakWorkingSet = 0;
				ProcessEx PeakWorkingSetInfo = null;
				long HighestPrivate = 0;
				ProcessEx HighestPrivateInfo = null;

				// TODO: Combine results of multiprocess apps
				foreach (var info in procs)
				{
					try
					{
						info.Process.Refresh();
						if (info.Process.HasExited) continue;

						long ws = info.Process.PeakWorkingSet64, pm = info.Process.PrivateMemorySize64;
						if (ws > PeakWorkingSet)
						{
							PeakWorkingSet = ws;
							PeakWorkingSetInfo = info;
						}
						if (pm > HighestPrivate)
						{
							HighestPrivate = pm;
							HighestPrivateInfo = info;
						}
					}
					catch (Exception ex) when (ex is NullReferenceException || ex is OutOfMemoryException) { throw; }
					catch (Exception ex)
					{
						Logging.Stacktrace(ex);
					}
				}
			}
			catch (Exception ex) when (ex is NullReferenceException || ex is OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				LastLoaderAnalysis = DateTimeOffset.UtcNow;
				Atomic.Unlock(ref SystemLoader_lock);
			}
		}

		/// <summary>
		/// In seconds.
		/// </summary>
		public TimeSpan? ScanFrequency { get; private set; } = TimeSpan.FromSeconds(180);
		DateTimeOffset LastScan { get; set; } = DateTimeOffset.MinValue; // UNUSED
		public DateTimeOffset NextScan { get; set; } = DateTimeOffset.MinValue;

		// static bool ControlChildren = false; // = false;

		readonly System.Timers.Timer ScanTimer = null;
		readonly System.Timers.Timer MaintenanceTimer = null;

		// move all this to prc.Validate() or prc.SanityCheck();
		bool ValidateController(Controller prc)
		{
			var rv = true;

			if (prc.Priority.HasValue && prc.BackgroundPriority.HasValue && prc.BackgroundPriority.Value.ToInt32() >= prc.Priority.Value.ToInt32())
			{
				prc.SetForegroundMode(ForegroundMode.Ignore);
				Log.Warning("[" + prc.FriendlyName + "] Background priority equal or higher than foreground priority, ignoring.");
			}

			if (string.IsNullOrEmpty(prc.Executable) && string.IsNullOrEmpty(prc.Path))
			{
				Log.Warning("[" + prc.FriendlyName + "] Executable and Path missing; ignoring.");
				rv = false;
			}

			// SANITY CHECKING
			if (!string.IsNullOrEmpty(prc.ExecutableFriendlyName))
			{
				if (IgnoreProcessName(prc.ExecutableFriendlyName))
				{
					if (ShowInaction && DebugProcesses)
						Log.Warning(prc.Executable ?? prc.ExecutableFriendlyName + " in ignore list; all changes denied.");

					// rv = false; // We'll leave the config in.
				}
				else if (ProtectedProcessName(prc.ExecutableFriendlyName))
				{
					if (ShowInaction && DebugProcesses)
						Log.Warning(prc.Executable ?? prc.ExecutableFriendlyName + " in protected list; priority changing denied.");
				}
			}

			return rv;
		}

		void SaveController(Controller prc)
		{
			lock (watchlist_lock)
			{
				if (!string.IsNullOrEmpty(prc.Executable))
					ExeToController.TryAdd(prc.ExecutableFriendlyName.ToLowerInvariant(), prc);

				if (!string.IsNullOrEmpty(prc.Path))
					WatchlistWithPath++;

				Watchlist.TryAdd(prc, 0);
				RenewWatchlistCache();
			}

			if (Trace) Log.Verbose("[" + prc.FriendlyName + "] Match: " + (prc.Executable ?? prc.Path) + ", " +
				(prc.Priority.HasValue ? Readable.ProcessPriority(prc.Priority.Value) : HumanReadable.Generic.NotAvailable) +
				", Mask:" + (prc.AffinityMask >= 0 ? prc.AffinityMask.ToString() : HumanReadable.Generic.NotAvailable) +
				", Recheck: " + prc.Recheck + "s, Foreground: " + prc.Foreground.ToString());
		}

		void LoadConfig()
		{
			if (DebugProcesses) Log.Information("<Process> Loading configuration...");

			using (var corecfg = Config.Load(CoreConfigFilename).BlockUnload())
			{
				var perfsec = corecfg.Config["Performance"];

				// ControlChildren = coreperf.GetSetDefault("Child processes", false, out tdirty).Bool;
				// dirtyconfig |= tdirty;

				int ignRecentlyModified = perfsec.GetOrSet("Ignore recently modified", 30)
					.InitComment("Performance optimization. More notably this enables granting self-determination to apps that actually think they know better.")
					.Int.Constrain(0, 24 * 60);
				IgnoreRecentlyModified = ignRecentlyModified > 0 ? (TimeSpan?)TimeSpan.FromMinutes(ignRecentlyModified) : null;

				var tscan = perfsec.GetOrSet("Scan frequency", 15)
					.InitComment("Frequency (in seconds) at which we scan for processes. 0 disables.")
					.Int.Constrain(0, 360);
				ScanFrequency = (tscan > 0) ? (TimeSpan?)TimeSpan.FromSeconds(tscan.Constrain(5, 360)) : null;

				// --------------------------------------------------------------------------------------------------------

				WMIPolling = perfsec.GetOrSet("WMI event watcher", false)
					.InitComment("Use WMI to be notified of new processes starting. If disabled, only rescanning everything will cause processes to be noticed.")
					.Bool;

				WMIPollDelay = perfsec.GetOrSet("WMI poll delay", 5)
					.InitComment("WMI process watcher delay (in seconds).  Smaller gives better results but can inrease CPU usage. Accepted values: 1 to 30.")
					.Int.Constrain(1, 30);

				// --------------------------------------------------------------------------------------------------------

				var fgpausesec = corecfg.Config["Foreground Focus Lost"];
				// RestoreOriginal = fgpausesec.GetSetDefault("Restore original", false, out modified).Bool;
				// dirtyconfig |= modified;
				DefaultBackgroundPriority = fgpausesec.GetOrSet("Default priority", 2)
					.InitComment("Default is normal to avoid excessive loading times while user is alt-tabbed.")
					.Int.Constrain(0, 4);

				// OffFocusAffinity = fgpausesec.GetSetDefault("Affinity", 0, out modified).Int;
				// dirtyconfig |= modified;
				// OffFocusPowerCancel = fgpausesec.GetSetDefault("Power mode cancel", true, out modified).Bool;
				// dirtyconfig |= modified;

				DefaultBackgroundAffinity = fgpausesec.GetOrSet("Default affinity", 14).Int.Constrain(0, AllCPUsMask);

				// --------------------------------------------------------------------------------------------------------

				// Taskmaster.cfg["Applications"]["Ignored"].StringArray = IgnoreList;
				var ignsetting = corecfg.Config["Applications"];
				string[] newIgnoreList = ignsetting.GetOrSet(HumanReadable.Generic.Ignore, IgnoreList)
					.InitComment("Special hardcoded protection applied to: consent, winlogon, wininit, and csrss. These are vital system services and messing with them can cause severe system malfunctioning. Mess with the ignore list at your own peril.")
					.Array;

				if ((newIgnoreList?.Length ?? 0) > 0)
				{
					var ignoreOmitted = IgnoreList.Except(newIgnoreList);
					var qlist = ignoreOmitted.ToList();

					if (qlist.Count > 0)
						Log.Warning("<Process> Custom ignore list loaded; omissions from default: " + string.Join(", ", qlist));
					else
						Log.Information("<Process> Custom ignore list loaded.");

					IgnoreList = newIgnoreList;
				}
				if (DebugProcesses) Log.Debug("<Process> Ignore list: " + string.Join(", ", IgnoreList));

				IgnoreSystem32Path = ignsetting.GetOrSet("Ignore System32", true)
					.InitComment("Ignore programs in %SYSTEMROOT%/System32 folder.")
					.Bool;

				var dbgsec = corecfg.Config[HumanReadable.Generic.Debug];
				DebugWMI = dbgsec.Get("WMI")?.Bool ?? false;
				DebugScan = dbgsec.Get("Full scan")?.Bool ?? false;
				DebugPaths = dbgsec.Get("Paths")?.Bool ?? false;
				DebugAdjustDelay = dbgsec.Get("Adjust Delay")?.Bool ?? false;
				DebugProcesses = dbgsec.Get("Processes")?.Bool ?? false;
				DebugPaging = dbgsec.Get("Paging")?.Bool ?? false;

				var logsec = corecfg.Config["Logging"];
				if (logsec.TryGet("Show unmodified portions", out var dumodport))
				{
					ShowUnmodifiedPortions = dumodport.Bool;
					logsec.Remove(dumodport); // DEPRECATED
				}
				ShowUnmodifiedPortions = logsec.GetOrSet("Unmodified portions", ShowUnmodifiedPortions).Bool;
				if (logsec.TryGet("Show only final state", out var donfinal))
				{
					ShowOnlyFinalState = donfinal.Bool;
					logsec.Remove(donfinal); // DEPRECATED
				}
				ShowOnlyFinalState = logsec.GetOrSet("Final state only", ShowOnlyFinalState).Bool;
				ShowForegroundTransitions = logsec.GetOrSet("Foreground transitions", ShowForegroundTransitions).Bool;

				if (!IgnoreSystem32Path) Log.Warning($"<Process> System32 ignore disabled.");

				var exsec = corecfg.Config["Experimental"];
				WindowResizeEnabled = exsec.Get("Window Resize")?.Bool ?? false;
			}


			var sbs = new StringBuilder();
			sbs.Append("<Process> ");

			if (ScanFrequency.HasValue)
				sbs.Append($"Scan frequency: {ScanFrequency.Value.TotalSeconds:N0}s");

			sbs.Append("; ");

			if (WMIPolling)
				sbs.Append("New instance watcher poll delay " + WMIPollDelay + "s");
			else
				sbs.Append("New instance watcher disabled");

			sbs.Append("; ");

			if (IgnoreRecentlyModified.HasValue)
				sbs.Append($"Recently modified ignored for {IgnoreRecentlyModified.Value.TotalMinutes:N1} mins");
			else
				sbs.Append("Recently modified not ignored");

			Log.Information(sbs.ToString());
		}

		void LoadWatchlist()
		{
			Log.Information("<Process> Loading watchlist...");

			var appcfg = Config.Load(WatchlistFile);

			int WatchlistWithHybrid = 0;

			if (appcfg.Config.ItemCount == 0)
			{
				Config.Unload(appcfg);

				Log.Warning("<Process> Watchlist empty; writing example list.");

				// DEFAULT CONFIGURATION
				var tpath = System.IO.Path.Combine(DataPath, WatchlistFile);
				try
				{
					System.IO.File.WriteAllText(tpath, Properties.Resources.Watchlist);
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
					throw;
				}

				appcfg = Config.Load(WatchlistFile);
			}

			using (var sappcfg = appcfg.BlockUnload())
			{
				foreach (Ini.Section section in sappcfg.Config)
				{
					if (string.IsNullOrEmpty(section.Name))
					{
						Log.Warning($"<Watchlist:{section.Line}> Nameless section; Skipping.");
						continue;
					}

					var ruleExec = section.Get("Image");
					var rulePath = section.Get(HumanReadable.System.Process.Path);

					if (ruleExec is null && rulePath is null)
					{
						// TODO: Deal with incorrect configuration lacking image
						Log.Warning($"<Watchlist:{section.Line}> [" + section.Name + "] No image nor path; Skipping.");
						continue;
					}

					ProcessPriorityClass? prioR = null;

					int prio;
					int aff;
					Power.Mode pmode = Power.Mode.Undefined;

					var rulePrio = section.Get(HumanReadable.System.Process.Priority);
					var ruleAff = section.Get(HumanReadable.System.Process.Affinity);
					var rulePow = section.Get(HumanReadable.Hardware.Power.Mode);

					if (rulePrio is null && ruleAff is null && rulePow is null)
					{
						// TODO: Deal with incorrect configuration lacking these things
						Log.Warning($"<Watchlist:{section.Line}> [{section.Name}] No priority, affinity, nor power plan; Skipping.");
						continue;
					}

					prio = rulePrio?.Int ?? -1;
					aff = (ruleAff?.Int ?? -1);
					var pmode_t = rulePow?.String;

					if (aff > AllCPUsMask || aff < -1)
					{
						Log.Warning($"<Watchlist:{ruleAff.Line}> [{section.Name}] Affinity({aff}) is malconfigured. Skipping.");
						//aff = Bit.And(aff, allCPUsMask); // at worst case results in 1 core used
						// TODO: Count bits, make 2^# assumption about intended cores but scale it to current core count.
						//		Shift bits to allowed range. Assume at least one core must be assigned, and in case of holes at least one core must be unassigned.
						aff = -1; // ignore
					}

					pmode = Power.Manager.GetModeByName(pmode_t);
					if (pmode == Power.Mode.Custom)
					{
						Log.Warning($"<Watchlist:{rulePow.Line}> [{section.Name}] Unrecognized power plan: {pmode_t}");
						pmode = Power.Mode.Undefined;
					}

					prioR = ProcessHelpers.IntToNullablePriority(prio);
					ProcessPriorityStrategy priostrat = ProcessPriorityStrategy.None;
					if (prioR.HasValue)
					{
						priostrat = (ProcessPriorityStrategy)(section.Get(HumanReadable.System.Process.PriorityStrategy)?.Int.Constrain(0, 3) ?? 0);
						if (priostrat == ProcessPriorityStrategy.None) prioR = null; // invalid data
					}

					ProcessAffinityStrategy affStrat = (aff >= 0)
						? (ProcessAffinityStrategy)(section.Get(HumanReadable.System.Process.AffinityStrategy)?.Int.Constrain(0, 3) ?? 2)
						: ProcessAffinityStrategy.None;

					int baff = section.Get("Background affinity")?.Int ?? -1;
					int bpriot = section.Get("Background priority")?.Int ?? -1;
					ProcessPriorityClass? bprio = (bpriot >= 0) ? (ProcessPriorityClass?)ProcessHelpers.IntToPriority(bpriot) : null;

					var pvis = (PathVisibilityOptions)(section.Get("Path visibility")?.Int.Constrain(-1, 3) ?? -1);

					string[] tignorelist = (section.Get(HumanReadable.Generic.Ignore)?.Array ?? null);
					if (tignorelist != null && tignorelist.Length > 0)
					{
						for (int i = 0; i < tignorelist.Length; i++)
							tignorelist[i] = tignorelist[i].ToLowerInvariant();
					}
					else
						tignorelist = null;

					var prc = new Controller(section.Name, prioR, aff)
					{
						Enabled = (section.Get(HumanReadable.Generic.Enabled)?.Bool ?? true),
						Executable = (ruleExec?.Value ?? null),
						Description = (section.Get(HumanReadable.Generic.Description)?.Value ?? null),
						// friendly name is filled automatically
						PriorityStrategy = priostrat,
						AffinityStrategy = affStrat,
						Path = (rulePath?.Value ?? null),
						ModifyDelay = (section.Get("Modify delay")?.Int ?? 0),
						//BackgroundIO = (section.TryGet("Background I/O")?.Bool ?? false), // Doesn't work
						Recheck = (section.Get("Recheck")?.Int ?? 0).Constrain(0, 300),
						PowerPlan = pmode,
						PathVisibility = pvis,
						BackgroundPriority = bprio,
						BackgroundAffinity = baff,
						IgnoreList = tignorelist,
						AllowPaging = (section.Get("Allow paging")?.Bool ?? false),
						Analyze = (section.Get("Analyze")?.Bool ?? false),
						ExclusiveMode = (section.Get("Exclusive")?.Bool ?? false),
						OrderPreference = (section.Get("Preference")?.Int.Constrain(0, 100) ?? 10),
						IOPriority = (section.Get("IO priority")?.Int.Constrain(0, 2) ?? -1), // 0-1 background, 2 = normal, anything else seems to have no effect
						LogAdjusts = (section.Get("Logging")?.Bool ?? true),
						LogStartAndExit = (section.Get("Log start and exit")?.Bool ?? false),
						Volume = (section.Get(HumanReadable.Hardware.Audio.Volume)?.Float.Constrain(0.0f, 1.0f) ?? 0.5f),
						VolumeStrategy = (Audio.VolumeStrategy)(section.Get("Volume strategy")?.Int.Constrain(0, 5) ?? 0),
					};

					//prc.MMPriority = section.TryGet("MEM priority")?.Int ?? int.MinValue; // unused

					int? foregroundMode = section.Get("Foreground mode")?.Int;
					if (foregroundMode.HasValue)
						prc.SetForegroundMode((ForegroundMode)foregroundMode.Value.Constrain(-1, 2));

					//prc.SetForegroundMode((ForegroundMode)(section.TryGet("Foreground mode")?.Int.Constrain(-1, 2) ?? -1)); // NEW

					var ruleIdeal = section.Get("Affinity ideal");
					prc.AffinityIdeal = ruleIdeal?.Int.Constrain(-1, CPUCount - 1) ?? -1;
					if (prc.AffinityIdeal >= 0 && !Bit.IsSet(prc.AffinityMask, prc.AffinityIdeal))
					{
						Log.Debug($"<Watchlist:{ruleIdeal.Line}> [{prc.FriendlyName}] Affinity ideal to mask mismatch: {HumanInterface.BitMask(prc.AffinityMask, CPUCount)}, ideal core: {prc.AffinityIdeal}");
						prc.AffinityIdeal = -1;
					}

					// TODO: Blurp about following configuration errors
					if (prc.AffinityMask < 0) prc.AffinityStrategy = ProcessAffinityStrategy.None;
					else if (prc.AffinityStrategy == ProcessAffinityStrategy.None) prc.AffinityMask = -1;

					if (!prc.Priority.HasValue) prc.PriorityStrategy = ProcessPriorityStrategy.None;
					else if (prc.PriorityStrategy == ProcessPriorityStrategy.None) prc.Priority = null;

					int[] resize = section.Get("Resize")?.IntArray ?? null; // width,height
					if (resize?.Length == 4)
					{
						int resstrat = section.Get("Resize strategy")?.Int.Constrain(0, 3) ?? -1;
						if (resstrat < 0) resstrat = 0;

						prc.ResizeStrategy = (WindowResizeStrategy)resstrat;

						prc.Resize = new System.Drawing.Rectangle(resize[0], resize[1], resize[2], resize[3]);
					}

					prc.Repair();

					AddController(prc);

					if (!string.IsNullOrEmpty(prc.Executable) && !string.IsNullOrEmpty(prc.Path))
						WatchlistWithHybrid += 1;

					// cnt.Children &= ControlChildren;

					// cnt.delay = section.Contains("delay") ? section["delay"].Int : 30; // TODO: Add centralized default delay
					// cnt.delayIncrement = section.Contains("delay increment") ? section["delay increment"].Int : 15; // TODO: Add centralized default increment
				}
			}

			lock (watchlist_lock)
			{
				RenewWatchlistCache();

				SortWatchlist();
			}

			// --------------------------------------------------------------------------------------------------------

			Log.Information($"<Process> Watchlist items – Name-based: {(Watchlist.Count - WatchlistWithPath)}; Path-based: {WatchlistWithPath - WatchlistWithHybrid}; Hybrid: {WatchlistWithHybrid} – Total: {Watchlist.Count}");
		}

		public static readonly string[] IONames = new[] { "Background", "Low", "Normal" };

		void OnControllerAdjust(object sender, ProcessModificationEventArgs ea)
		{
			if (sender is Controller prc)
			{
				if (!prc.LogAdjusts) return;

				bool onlyFinal = ShowOnlyFinalState;

				var sbs = new StringBuilder()
					.Append("[").Append(prc.FriendlyName).Append("] ").Append(prc.FormatPathName(ea.Info))
					.Append(" (#").Append(ea.Info.Id).Append(")");

				if (ShowUnmodifiedPortions || ea.PriorityNew.HasValue)
				{
					sbs.Append("; Priority: ");
					if (ea.PriorityOld.HasValue)
					{
						if (!onlyFinal || !ea.PriorityNew.HasValue) sbs.Append(Readable.ProcessPriority(ea.PriorityOld.Value));

						if (ea.PriorityNew.HasValue)
						{
							if (!onlyFinal) sbs.Append(" → ");
							sbs.Append(Readable.ProcessPriority(ea.PriorityNew.Value));
						}

						if (prc.Priority.HasValue && ea.Info.State == ProcessHandlingState.Paused && prc.Priority != ea.PriorityNew)
							sbs.Append($" [{ProcessHelpers.PriorityToInt(prc.Priority.Value)}]");
					}
					else
						sbs.Append(HumanReadable.Generic.NotAvailable);

					if (ea.PriorityFail) sbs.Append(" [Failed]");
					if (ea.Protected) sbs.Append(" [Protected]");
				}

				if (ShowUnmodifiedPortions || ea.AffinityNew >= 0)
				{
					sbs.Append("; Affinity: ");
					if (ea.AffinityOld >= 0)
					{
						if (!onlyFinal || ea.AffinityNew < 0) sbs.Append(ea.AffinityOld);

						if (ea.AffinityNew >= 0)
						{
							if (!onlyFinal) sbs.Append(" → ");
							sbs.Append(ea.AffinityNew);
						}

						if (prc.AffinityMask >= 0 && ea.Info.State == ProcessHandlingState.Paused && prc.AffinityMask != ea.AffinityNew)
							sbs.Append($" [{prc.AffinityMask}]");
					}
					else
						sbs.Append(HumanReadable.Generic.NotAvailable);

					if (ea.AffinityFail) sbs.Append(" [Failed]");
				}

				if (DebugProcesses) sbs.Append(" [").Append(prc.AffinityStrategy.ToString()).Append("]");

				if (ea.NewIO >= 0) sbs.Append(" – I/O: ").Append(IONames[ea.NewIO]);

				if (ea.User != null) sbs.Append(ea.User);

				if (DebugAdjustDelay)
				{
					sbs.Append(" – ").Append($"{ea.Info.Timer.ElapsedMilliseconds:N0} ms");
					if (ea.Info.WMIDelay > 0d) sbs.Append(" + ").Append($"{ea.Info.WMIDelay:N0}").Append(" ms watcher delay");
				}

				// TODO: Add option to logging to file but still show in UI
				if (!(ShowInaction && DebugProcesses)) Log.Information(sbs.ToString());
				else Log.Debug(sbs.ToString());

				ea.User?.Clear();
				ea.User = null;
			}
		}

		void RenewWatchlistCache()
		{
			WatchlistCache = new Lazy<List<Controller>>(LazyRecacheWatchlist);
			ResetWatchlistCancellation();
		}

		List<Controller> LazyRecacheWatchlist() => Watchlist.Keys.ToList();
		CancellationTokenSource watchlist_cts = new CancellationTokenSource();

		void ResetWatchlistCancellation()
		{
			watchlist_cts.Cancel();
			watchlist_cts = new CancellationTokenSource();
		}

		public void SortWatchlist()
		{
			try
			{
				lock (watchlist_lock)
				{
					var token = watchlist_cts.Token;

					if (Trace) Debug.WriteLine("SORTING PROCESS MANAGER WATCHLIST");
					WatchlistCache.Value.Sort(WatchlistSorter);

					if (token.IsCancellationRequested) return; // redo?

					int order = 0;
					foreach (var prc in WatchlistCache.Value)
						prc.ActualOrder = order++;

					if (token.IsCancellationRequested) return; // redo?
				}

				// TODO: Signal UI the actual order may have changed
				WatchlistSorted?.Invoke(this, EventArgs.Empty);
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		int WatchlistSorter(Controller x, Controller y)
		{
			if (x.Enabled && !y.Enabled) return -1;
			else if (!x.Enabled && y.Enabled) return 1;
			else if (x.OrderPreference < y.OrderPreference) return -1;
			else if (x.OrderPreference > y.OrderPreference) return 1;
			else if (x.Adjusts > y.Adjusts) return -1;
			else if (x.Adjusts < y.Adjusts) return 1;
			else if (string.IsNullOrEmpty(x.Path) && !string.IsNullOrEmpty(y.Path)) return -1;
			else if (!string.IsNullOrEmpty(x.Path) && string.IsNullOrEmpty(y.Path)) return 1;
			return 0;
		}

		public event EventHandler WatchlistSorted;

		public void AddController(Controller prc)
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException("AddController called when ProcessManager was already disposed");

			if (ValidateController(prc))
			{
				prc.Modified += ProcessModifiedProxy;
				//prc.Paused += ProcessPausedProxy;
				//prc.Resumed += ProcessResumedProxy;
				prc.Paused += ProcessWaitingExitProxy;
				prc.WaitingExit += ProcessWaitingExitProxy;

				SaveController(prc);

				prc.OnAdjust += OnControllerAdjust;
			}
		}

		void ProcessModifiedProxy(object sender, ProcessModificationEventArgs ea) => ProcessModified?.Invoke(sender, ea);

		void ProcessWaitingExitProxy(object _, ProcessModificationEventArgs ea)
		{
			try
			{
				WaitForExit(ea.Info);
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				Log.Error("Unregistering '" + ea.Info.Controller.FriendlyName + "' exit wait proxy");
				ea.Info.Controller.Paused -= ProcessWaitingExitProxy;
				ea.Info.Controller.WaitingExit -= ProcessWaitingExitProxy;
			}
		}

		void ProcessResumedProxy(object _, ProcessModificationEventArgs _ea)
		{
			throw new NotImplementedException();
		}

		void ProcessPausedProxy(object _, ProcessModificationEventArgs _ea)
		{
			throw new NotImplementedException();
		}

		public void RemoveController(Controller prc)
		{
			if (!string.IsNullOrEmpty(prc.ExecutableFriendlyName))
				ExeToController.TryRemove(prc.ExecutableFriendlyName.ToLowerInvariant(), out _);

			lock (watchlist_lock) RenewWatchlistCache();

			prc.Modified -= ProcessModified;
			prc.Paused -= ProcessPausedProxy;
			prc.Resumed -= ProcessResumedProxy;
		}

		ConcurrentDictionary<int, ProcessEx> WaitForExitList = new ConcurrentDictionary<int, ProcessEx>();

		void WaitForExitTriggered(ProcessEx info)
		{
			Debug.Assert(info.Controller != null, "ProcessController not defined");
			Debug.Assert(!SystemProcessId(info.Id), "WaitForExitTriggered for system process");

			info.State = ProcessHandlingState.Exited;

			try
			{
				if (DebugForeground || DebugPower)
					Log.Debug($"[{info.Controller.FriendlyName}] {info.Name} (#{info.Id}) exited [Power: {info.PowerWait}, Active: {info.ForegroundWait}]");

				info.ForegroundWait = false;

				WaitForExitList.TryRemove(info.Id, out _);

				info.Controller?.End(info.Process, EventArgs.Empty);

				ProcessStateChange?.Invoke(this, new ProcessModificationEventArgs(info));
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		void PowerBehaviourEvent(object _, Power.Manager.PowerBehaviourEventArgs ea)
		{
			if (DisposedOrDisposing) return;

			try
			{
				if (ea.Behaviour == Power.Manager.PowerBehaviour.Manual)
					CancelPowerWait();
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				Log.Error("Unregistering power behaviour event");
				powermanager.onBehaviourChange -= PowerBehaviourEvent;
			}
		}

		public void CancelPowerWait()
		{
			var cancelled = 0;

			Stack<ProcessEx> clearList = null;

			if (WaitForExitList.Count == 0) return;

			clearList = new Stack<ProcessEx>();
			foreach (var info in WaitForExitList.Values)
			{
				if (info.PowerWait)
				{
					// don't clear if we're still waiting for foreground
					if (!info.ForegroundWait)
					{
						try
						{
							info.Process.EnableRaisingEvents = false;
						}
						catch { } // nope, this throwing just verifies we're doing the right thing

						clearList.Push(info);
						cancelled++;
					}
				}
			}

			while (clearList.Count > 0)
				WaitForExitTriggered(clearList.Pop());

			if (cancelled > 0)
				Log.Information("Cancelled power mode wait on " + cancelled + " process(es).");
		}

		bool WaitForExit(ProcessEx info)
		{
			Debug.Assert(info.Controller != null, "No controller attached");

			bool exithooked = false;

			if (DisposedOrDisposing) throw new ObjectDisposedException("WaitForExit called when ProcessManager was already disposed");

			if (WaitForExitList.TryAdd(info.Id, info))
			{
				try
				{
					info.Process.EnableRaisingEvents = true;
					info.Process.Exited += (_, _ea) => WaitForExitTriggered(info);

					// TODO: Just in case check if it exited while we were doing this.
					exithooked = true;

					info.Process.Refresh();
					if (info.Process.HasExited)
					{
						info.State = ProcessHandlingState.Exited;
						throw new InvalidOperationException("Exited after we registered for it?");
					}
				}
				catch (InvalidOperationException) // already exited
				{
					WaitForExitTriggered(info);
				}
				catch (Exception ex) // unknown error
				{
					Logging.Stacktrace(ex);
					WaitForExitTriggered(info);
				}

				if (exithooked) ProcessStateChange?.Invoke(this, new ProcessModificationEventArgs(info));
			}

			return exithooked;
		}

		public ProcessEx[] getExitWaitList() => WaitForExitList.Values.ToArray(); // copy is good here

		Controller PreviousForegroundController = null;
		ProcessEx PreviousForegroundInfo;

		void ForegroundAppChangedEvent(object _sender, WindowChangedArgs ev)
		{
			if (DisposedOrDisposing) return;

			System.Diagnostics.Process process = ev.Process;
			try
			{
				if (DebugForeground) Log.Verbose("<Process> Foreground Received: #" + ev.Id);

				if (PreviousForegroundInfo != null)
				{
					if (PreviousForegroundInfo.Id != ev.Id) // testing previous to current might be superfluous
					{
						if (PreviousForegroundController != null)
						{
							//Log.Debug("PUTTING PREVIOUS FOREGROUND APP to BACKGROUND");
							if (PreviousForegroundController.Foreground != ForegroundMode.Ignore)
								PreviousForegroundController.Pause(PreviousForegroundInfo);

							ProcessStateChange?.Invoke(this, new ProcessModificationEventArgs(PreviousForegroundInfo));
						}
					}
					else
					{
						if (ShowInaction && DebugForeground)
							Log.Debug("<Foreground> Changed but the app is still the same. Curious, don't you think?");
					}
				}

				if (WaitForExitList.TryGetValue(ev.Id, out ProcessEx info))
				{
					process = info.Process;

					if (info.ForegroundWait)
					{
						var prc = info.Controller;
						if (Trace && DebugForeground) Log.Debug($"[{prc.FriendlyName}] {info.Name} (#{info.Id}) on foreground!");

						if (prc.Foreground != ForegroundMode.Ignore) prc.Resume(info);

						ProcessStateChange?.Invoke(this, new ProcessModificationEventArgs(info));

						PreviousForegroundInfo = info;
						PreviousForegroundController = prc;

						return;
					}
				}

				if (DebugForeground && Trace) Log.Debug("<Process> NULLING PREVIOUS FOREGRDOUND");

				PreviousForegroundInfo = null;
				PreviousForegroundController = null;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				Log.Error("Unregistering foreground changed event");
				activeappmonitor.ActiveChanged -= ForegroundAppChangedEvent;
			}
			finally
			{
				if (IOPriorityEnabled)
				{
					try
					{
						if (process is null) process = System.Diagnostics.Process.GetProcessById(ev.Id);
						Utility.SetIO(process, 2, out _, decrease: false); // set foreground app I/O to highest possible
					}
					catch (Exception ex) when (ex is NullReferenceException || ex is OutOfMemoryException) { throw; }
					catch (ArgumentException) { }
					catch (InvalidOperationException) { }
				}
			}
		}

		// TODO: ADD CACHE: pid -> process name, path, process

		/// <summary>
		/// Number of watchlist items with path restrictions.
		/// </summary>
		int WatchlistWithPath = 0;

		bool GetPathController(ProcessEx info, out Controller prc)
		{
			prc = null;

			if (WatchlistWithPath <= 0) return false;

			try
			{
				info.Process.Refresh();
				if (info.Process.HasExited) // can throw
				{
					info.State = ProcessHandlingState.Exited;
					if (ShowInaction && DebugProcesses) Log.Verbose(info.Name + " (#" + info.Id + ") has already exited.");
					return false; // return ProcessState.Invalid;
				}
			}
			catch (InvalidOperationException ex)
			{
				Log.Fatal("INVALID ACCESS to Process");
				Logging.Stacktrace(ex);
				return false; // return ProcessState.AccessDenied; //throw; // no point throwing
			}
			catch (System.ComponentModel.Win32Exception ex)
			{
				if (ex.NativeErrorCode != 5) // what was this?
					Log.Warning("Access error: " + info.Name + " (#" + info.Id + ")");
				return false; // return ProcessState.AccessDenied; // we don't care wwhat this error is
			}

			if (string.IsNullOrEmpty(info.Path) && !Utility.FindPath(info))
				return false; // return ProcessState.Error;

			if (IgnoreSystem32Path && info.Path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.System)))
			{
				if (ShowInaction && DebugProcesses) Log.Debug("<Process/Path> " + info.Name + " (#" + info.Id + ") in System32, ignoring");
				return false;
			}

			lock (watchlist_lock)
			{
				RenewWatchlistCache();

				// TODO: This needs to be FASTER
				// Can't parallelize...
				foreach (var lprc in WatchlistCache.Value)
				{
					if (!lprc.Enabled) continue;

					if (string.IsNullOrEmpty(lprc.Path)) continue;
					if (!string.IsNullOrEmpty(lprc.Executable)) continue;

					/*
					if (!string.IsNullOrEmpty(lprc.Executable))
					{
						if (lprc.ExecutableFriendlyName.Equals(info.Name, StringComparison.InvariantCultureIgnoreCase))
						{
							if (DebugPaths)
								Log.Debug("[" + lprc.FriendlyName + "] Path+Exe matched.");
						}
						else
							continue;
					}
					*/

					if (info.Path.StartsWith(lprc.Path, StringComparison.InvariantCultureIgnoreCase))
					{
						if (DebugPaths) Log.Verbose($"[{lprc.FriendlyName}] (CheckPathWatch) Matched at: {info.Path}");

						prc = lprc;
						break;
					}
				}
			}

			info.Controller = prc;

			return prc != null;
		}

		static string[] ProtectList { get; set; } = {
			"consent", // UAC, user account control prompt
			"winlogon", // core system
			"wininit", // core system
			"csrss", // client server runtime, subsystems
			"dwm", // desktop window manager
			"taskmgr", // task manager
			"LogonUI", // session lock
			"services", // service control manager
		};

		static string[] IgnoreList { get; set; } = {
			"svchost", // service host
			"taskeng", // task scheduler
			"consent", // UAC, user account control prompt
			"taskhost", // task scheduler process host
			"rundll32", //
			"dllhost", //
			//"conhost", // console host, hosts command prompts (cmd.exe)
			"dwm", // desktop window manager
			"wininit", // core system
			"csrss", // client server runtime, subsystems
			"winlogon", // core system
			"services", // service control manager
			"explorer", // file manager
			"taskmgr", // task manager
			"audiodg" // audio device isolation
		};

		// %SYSTEMROOT%\System32 (Environment.SpecialFolder.System)
		public bool IgnoreSystem32Path { get; private set; } = true;

		bool DebugWMI = false;

		/// <summary>
		/// Tests if the process ID is core system process (0[idle] or 4[system]) that can never be valid program.
		/// </summary>
		/// <returns>true if the pid should not be used</returns>
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		public static bool SystemProcessId(int pid) => pid <= 4;

		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		public bool IgnoreProcessID(int pid) => SystemProcessId(pid) || ignorePids.ContainsKey(pid);

		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		public static bool IgnoreProcessName(string name) => IgnoreList.Any(item => item.Equals(name, StringComparison.InvariantCultureIgnoreCase));

		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		public static bool ProtectedProcessName(string name) => ProtectList.Any(item => item.Equals(name, StringComparison.InvariantCultureIgnoreCase));
		// %SYSTEMROOT%

		/*
		void ChildController(ProcessEx ci)
		{
			//await System.Threading.Tasks.Task.Yield();
			// TODO: Cache known children so we don't look up the parent? Reliable mostly with very unique/long executable names.

			Debug.Assert(ci.Process != null, "ChildController was given a null process.");

			// TODO: Cache known children so we don't look up the parent? Reliable mostly with very unique/long executable names.
			Stopwatch n = Stopwatch.StartNew();
			int ppid = -1;
			try
			{
				// TODO: Deal with intermediary processes (double parent)
				if (ci.Process == null) ci.Process = Process.GetProcessById(ci.Id);
				ppid = ci.Process.ParentProcessId();
			}
			catch // PID not found
			{
				Log.Warning("Couldn't get parent process for {ChildProcessName} (#{ChildProcessID})", ci.Name, ci.Id);
				return;
			}

			if (!IgnoreProcessID(ppid)) // 0 and 4 are system processes, we don't care about their children
			{
				Process parentproc;
				try
				{
					parentproc = Process.GetProcessById(ppid);
				}
				catch // PID not found
				{
					Log.Verbose("Parent PID(#{ProcessID}) not found", ppid);
					return;
				}

				if (IgnoreProcessName(parentproc.ProcessName)) return;
				bool denyChange = ProtectedProcessName(parentproc.ProcessName);

				ProcessController parent = null;
				if (!denyChange)
				{
					if (execontrol.TryGetValue(ci.Process.ProcessName.ToLower()), out parent))
					{
						try
						{
							if (!parent.ChildPriorityReduction && (ProcessHelpers.PriorityToInt(ci.Process.PriorityClass) > ProcessHelpers.PriorityToInt(parent.ChildPriority)))
							{
								Log.Verbose(ci.Name + " (#" + ci.Id + ") has parent " + parent.FriendlyName + " (#" + parentproc.Id + ") has non-reductable higher than target priority.");
							}
							else if (parent.Children
									 && ProcessHelpers.PriorityToInt(ci.Process.PriorityClass) != ProcessHelpers.PriorityToInt(parent.ChildPriority))
							{
								ProcessPriorityClass oldprio = ci.Process.PriorityClass;
								try
								{
									ci.Process.SetLimitedPriority(parent.ChildPriority, true, true);
								}
								catch (Exception e)
								{
									Console.WriteLine(e.StackTrace);
									Log.Warning("Uncaught exception; Failed to modify priority for '{ProcessName}'", ci.Process.ProcessName);
								}
								Log.Information("{ChildProcessName} (#{ChildProcessID}) child of {ParentFriendlyName} (#{ParentProcessID}) Priority({OldChildPriority} -> {NewChildPriority})",
												ci.Name, ci.Id, parent.FriendlyName, ppid, oldprio, ci.Process.PriorityClass);
							}
							else
							{
								Log.Verbose(ci.Name + " (#" + ci.Id + ") has parent " + parent.FriendlyName + " (#" + parentproc.Id + ")");
							}
						}
						catch
						{
							Log.Warning("[{FriendlyName}] {Exe} (#{Pid}) access failure.", parent.FriendlyName, ci.Name, ci.Id);
						}
					}
				}
			}
			n.Stop();
			Statistics.Parentseektime += n.Elapsed.TotalSeconds;
			Statistics.ParentSeeks += 1;
		}
		*/

		/// <summary>
		/// Add to foreground watch list if necessary.
		/// </summary>
		async Task ForegroundWatch(ProcessEx info)
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException("ForegroundWatch called when ProcessManager was already disposed"); ;

			var prc = info.Controller;

			await Task.Delay(0).ConfigureAwait(false);

			Debug.Assert(prc.Foreground != ForegroundMode.Ignore);

			info.ForegroundWait = true;

			bool keyadded = WaitForExit(info);

			if (Trace && DebugForeground)
				Log.Debug($"[{prc.FriendlyName}] {info.Name} (#{info.Id}) {(!keyadded ? "already in" : "added to")} foreground watchlist.");

			ProcessStateChange?.Invoke(this, new ProcessModificationEventArgs(info));
		}

		// TODO: This should probably be pushed into ProcessController somehow.
		async Task ProcessTriage(ProcessEx info, bool old=false)
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException("ProcessTriage called when ProcessManager was already disposed");

			await Task.Delay(0, cts.Token).ConfigureAwait(false); // asyncify
			if (cts.IsCancellationRequested) return;

			try
			{
				info.State = ProcessHandlingState.Triage;

				HandlingStateChange?.Invoke(this, new HandlingStateChangeEventArgs(info));

				if (string.IsNullOrEmpty(info.Name))
				{
					Log.Warning($"#{info.Id.ToString()} details unaccessible, ignored.");
					info.State = ProcessHandlingState.AccessDenied;
					return; // ProcessState.AccessDenied;
				}

				Controller prc = null;
				if (ExeToController.TryGetValue(info.Name.ToLowerInvariant(), out prc))
					info.Controller = prc; // fill

				if (prc != null || GetPathController(info, out prc))
				{
					if (!info.Controller.Enabled)
					{
						if (DebugProcesses) Log.Debug("[" + info.Controller.FriendlyName + "] Matched, but rule disabled; ignoring.");
						info.State = ProcessHandlingState.Abandoned;
						return;
					}

					info.State = ProcessHandlingState.Processing;

					await Task.Delay(0, cts.Token).ConfigureAwait(false); // asyncify again
					if (cts.IsCancellationRequested) return;

					if (!old && prc.LogStartAndExit)
					{
						Log.Information($"<Process> {info.Name} #{info.Id} started.");
						info.Process.Exited += (_, _ea) => Log.Information($"<Process> {info.Name} #{info.Id} exited.");
						info.Process.EnableRaisingEvents = true;
						// TOOD: What if the process exited just before we enabled raising for the events?
					}

					try
					{
						if (Trace && DebugProcesses) Debug.WriteLine($"Trying to modify: {info.Name} (#{info.Id})");

						await info.Controller.Modify(info);

						if (prc.Foreground != ForegroundMode.Ignore) ForegroundWatch(info).ConfigureAwait(false);

						if (prc.ExclusiveMode) ExclusiveMode(info).ConfigureAwait(false);

						if (RecordAnalysis.HasValue && info.Controller.Analyze && info.Valid && info.State != ProcessHandlingState.Abandoned)
							analyzer.Analyze(info).ConfigureAwait(false);

						if (WindowResizeEnabled && prc.Resize.HasValue)
							prc.TryResize(info).ConfigureAwait(false);

						if (info.State == ProcessHandlingState.Processing)
						{
							Debug.WriteLine($"[{info.Controller.FriendlyName}] {info.Name} (#{info.Id}) correcting state to Finished");
							info.State = ProcessHandlingState.Finished;
						}
					}
					catch (Exception ex) when (ex is NullReferenceException || ex is OutOfMemoryException) { throw; }
					catch (Exception ex)
					{
						Logging.Stacktrace(ex);
					}
					finally
					{
						HandlingStateChange?.Invoke(this, new HandlingStateChangeEventArgs(info));
					}
				}
				else
				{
					info.State = ProcessHandlingState.Abandoned;
					if (Trace && DebugProcesses) Debug.WriteLine($"ProcessTriage no matching rule for: {info.Name} (#{info.Id})");
				}

				/*
				if (ControlChildren) // this slows things down a lot it seems
					ChildController(info);
				*/
			}
			catch (Exception ex) when (ex is NullReferenceException || ex is OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				HandlingStateChange?.Invoke(this, new HandlingStateChangeEventArgs(info));
			}
		}

		object Exclusive_lock = new object();
		ConcurrentDictionary<int, ProcessEx> ExclusiveList = new ConcurrentDictionary<int, ProcessEx>();

		async Task ExclusiveMode(ProcessEx info)
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException("ExclusiveMode called when ProcessManager was already disposed"); ;
			if (!MKAh.Execution.IsAdministrator) return; // sadly stopping services requires admin rights

			if (DebugProcesses) Log.Debug($"[{info.Controller.FriendlyName}] {info.Name} (#{info.Id}) Exclusive mode initiating.");

			await Task.Delay(0).ConfigureAwait(false);

			try
			{
				bool ensureExit = false;
				try
				{
					lock (Exclusive_lock)
					{
						if (ExclusiveList.TryAdd(info.Id, info))
						{
							if (DebugProcesses) Log.Debug($"<Exclusive> [{info.Controller.FriendlyName}] {info.Name} (#{info.Id.ToString()}) starting");
							info.Process.EnableRaisingEvents = true;
							info.Process.Exited += EndExclusiveMode;

							ExclusiveEnabled = true;

							foreach (var service in Services)
								service.Stop(service.FullDisable);

							ensureExit = true;
						}
					}
				}
				catch (InvalidOperationException)
				{
					ensureExit = true; // already exited
				}

				if (ensureExit)
				{
					info.Process.Refresh();
					if (info.Process.HasExited)
					{
						info.State = ProcessHandlingState.Exited;
						EndExclusiveMode(info.Process, EventArgs.Empty);
					}
				}
			}
			catch (Exception ex) when (ex is NullReferenceException || ex is OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		void EndExclusiveMode(object sender, EventArgs ea)
		{
			if (DisposedOrDisposing) return;

			try
			{
				lock (Exclusive_lock)
				{
					if (sender is System.Diagnostics.Process process && ExclusiveList.TryRemove(process.Id, out var info))
					{
						if (DebugProcesses) Log.Debug($"<Exclusive> [{info.Controller.FriendlyName}] {info.Name} (#{info.Id.ToString()}) ending");
						if (ExclusiveList.Count == 0)
						{
							if (DebugProcesses) Log.Debug("<Exclusive> Ended for all, restarting services.");
							try
							{
								ExclusiveEnd();
							}
							catch (InvalidOperationException)
							{
								Log.Warning($"<Exclusive> Failure to restart WUA after {info.Name} (#{info.Id}) exited.");
							}
						}
						else
						{
							if (DebugProcesses) Log.Debug("<Exclusive> Still used by: #" + string.Join(", #", ExclusiveList.Keys));
						}
					}
				}
			}
			catch (Exception ex) when (ex is NullReferenceException || ex is OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		readonly List<ServiceWrapper> Services = new List<ServiceWrapper>(new ServiceWrapper[] {
			new ServiceWrapper("wuaserv") { FullDisable = true }, // Windows Update
			new ServiceWrapper("wsearch") { FullDisable = true }, // Windows Search
		});

		bool ExclusiveEnabled = false;

		void ExclusiveEnd()
		{
			if (!ExclusiveEnabled) return;

			foreach (var service in Services)
			{
				try
				{
					service.Start(service.FullDisable);
				}
				catch (Exception ex) when (!(ex is InvalidOperationException))
				{
					Logging.Stacktrace(ex);
				}
			}
		}

		int Handling { get; set; } = 0; // this isn't used for much...

		void SignalProcessHandled(int adjust)
		{
			Handling += adjust;
			HandlingCounter?.Invoke(this, new ProcessingCountEventArgs(adjust, Handling));
		}

		/// <summary>
		/// Triage process exit events.
		/// </summary>
		async void ProcessEndTriage(object sender, EventArrivedEventArgs ea)
		{
			int pid = -1;
			try
			{
				var targetInstance = ea.NewEvent.Properties["TargetInstance"].Value as ManagementBaseObject;
				//var tpid = targetInstance.Properties["Handle"].Value as int?; // doesn't work for some reason

				pid = Convert.ToInt32(targetInstance.Properties["Handle"].Value as string);

				if (pid > 4) ScanBlockList.TryRemove(pid, out _);
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				return;
			}
		}

		private void StartTraceTriage(object sender, EventArrivedEventArgs e)
		{
			var now = DateTimeOffset.UtcNow;
			var timer = Stopwatch.StartNew();

			var targetInstance = e.NewEvent;
			int pid = 0;
			int ppid = 0;
			string name = string.Empty;

			ProcessHandlingState state = ProcessHandlingState.Invalid;

			try
			{
				pid = Convert.ToInt32(targetInstance.Properties["ProcessID"].Value as string);
				ppid = Convert.ToInt32(targetInstance.Properties["ParentProcessID"].Value as string);
				name = targetInstance.Properties["ProcessName"].Value as string;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				state = ProcessHandlingState.Invalid;
				return;
			}
			finally
			{
				Statistics.WMIPollTime += timer.Elapsed.TotalSeconds;
				Statistics.WMIPolling += 1;
			}

			Log.Debug($"<Process> {name} (#{pid}; parent #{ppid})");
		}

		// This needs to return faster
		async void NewInstanceTriage(object _, EventArrivedEventArgs ea)
		{
			var now = DateTimeOffset.UtcNow;
			var timer = Stopwatch.StartNew();

			int pid = -1;
			string name = string.Empty, path = string.Empty;
			ProcessEx info = null;
			DateTime creation = DateTime.MinValue;
			TimeSpan wmidelay = TimeSpan.Zero;

			ProcessHandlingState state = ProcessHandlingState.Invalid;

			await Task.Delay(0).ConfigureAwait(false);
			if (cts.IsCancellationRequested) return;

			try
			{
				SignalProcessHandled(1); // wmi new instance

				var wmiquerytime = Stopwatch.StartNew();
				// TODO: Instance groups?
				try
				{
					string iname=string.Empty, cmdl=string.Empty;
					using (var targetInstance = ea.NewEvent.Properties["TargetInstance"].Value as ManagementBaseObject)
					{
						//var tpid = targetInstance.Properties["Handle"].Value as int?; // doesn't work for some reason
						pid = Convert.ToInt32(targetInstance.Properties["Handle"].Value as string);

						iname = targetInstance.Properties["Name"].Value as string;
						path = targetInstance.Properties["ExecutablePath"].Value as string;
						if (DebugAdjustDelay) creation = ManagementDateTimeConverter.ToDateTime(targetInstance.Properties["CreationDate"].Value as string);
						if (string.IsNullOrEmpty(path))
							cmdl = targetInstance.Properties["CommandLine"].Value as string; // CommandLine sometimes has the path when executablepath does not
					}

					ScanBlockList.TryAdd(pid, DateTimeOffset.UtcNow);

					name = System.IO.Path.GetFileNameWithoutExtension(iname);
					if (!string.IsNullOrEmpty(cmdl))
					{
						int off = 0;
						string npath = "";
						if (cmdl[0] == '"')
						{
							off = cmdl.IndexOf('"', 1);
							npath = cmdl.Substring(1, off - 1);
						}
						else
						{
							off = cmdl.IndexOf(' ', 1);
							// off < 1 = no arguments
							npath = off <= 1 ? cmdl : cmdl.Substring(0, off);
						}

						if (npath.IndexOf('"', 0) >= 0) Log.Fatal("WMI.TargetInstance.CommandLine still had invalid characters: " + npath);
						path = npath;
					}
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
					state = ProcessHandlingState.Invalid;
					return;
				}
				finally
				{
					Statistics.WMIPollTime += timer.Elapsed.TotalSeconds;
					Statistics.WMIPolling += 1;
				}

				if (DebugAdjustDelay)
				{
					wmidelay = new DateTimeOffset(creation.ToUniversalTime()).TimeTo(now);
					if (Trace) Debug.WriteLine($"WMI delay (#{pid}): {wmidelay.TotalMilliseconds:N0} ms");
				}

				if (IgnoreProcessID(pid)) return; // We just don't care

				if (string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(path))
					name = System.IO.Path.GetFileNameWithoutExtension(path);

				if (string.IsNullOrEmpty(name) && pid < 0)
				{
					// likely process exited too fast
					if (DebugProcesses && ShowInaction) Log.Debug("<<WMI>> Failed to acquire neither process name nor process Id");
					state = ProcessHandlingState.AccessDenied;
					return;
				}

				if (Trace) Debug.WriteLine($"NewInstanceTriage: {name} (#{pid})");

				if (IgnoreProcessName(name))
				{
					if (ShowInaction && DebugProcesses)
						Log.Debug($"<Process> {name} (#{pid}) ignored due to its name.");
					return;
				}

				if (Utility.GetInfo(pid, out info, path: path, getPath: true, name: name))
				{
					info.Timer = timer;
					info.WMIDelay = wmidelay.TotalMilliseconds;
					NewInstanceTriagePhaseTwo(info, out state);
				}
				else
				{
					if (ShowInaction && DebugProcesses)
						Log.Debug($"<Process> {name} (#{pid}) could not be mined for info.");
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				Log.Error("Unregistering new instance triage");
				if (NewProcessWatcher != null) NewProcessWatcher.EventArrived -= NewInstanceTriage;
				NewProcessWatcher?.Dispose();
				NewProcessWatcher = null;
				timer?.Stop();
			}
			finally
			{
				if (info is null) info = new ProcessEx { Id = pid, Timer = timer, State = state, WMIDelay = wmidelay.TotalMilliseconds };
				HandlingStateChange?.Invoke(this, new HandlingStateChangeEventArgs(info));

				SignalProcessHandled(-1); // done with it
			}
		}

		void NewInstanceTriagePhaseTwo(ProcessEx info, out ProcessHandlingState state)
		{
			//await Task.Delay(0).ConfigureAwait(false);
			info.State = ProcessHandlingState.Invalid;

			if (cts.IsCancellationRequested) throw new ObjectDisposedException("NewInstanceTriagePhaseTwo called when ProcessManager was already disposed");

			try
			{
				try
				{
					info.Process = System.Diagnostics.Process.GetProcessById(info.Id);
				}
				catch (ArgumentException)
				{
					state = info.State = ProcessHandlingState.Exited;
					if (ShowInaction && DebugProcesses)
						Log.Verbose("Caught #" + info.Id + " but it vanished.");
					return;
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
					state = info.State = ProcessHandlingState.Invalid;
					return;
				}

				if (string.IsNullOrEmpty(info.Name))
				{
					try
					{
						// This happens only when encountering a process with elevated privileges, e.g. admin
						// TODO: Mark as admin process?
						info.Name = info.Process.ProcessName;
					}
					catch (Exception ex) when (ex is NullReferenceException || ex is OutOfMemoryException) { throw; }
					catch
					{
						Log.Error("Failed to retrieve name of process #" + info.Id);
						state = info.State = ProcessHandlingState.Invalid;
						return;
					}
				}

				if (Trace) Log.Verbose($"Caught: {info.Name} (#{info.Id}) at: {info.Path}");

				// info.Process.StartTime; // Only present if we started it

				state = info.State = ProcessHandlingState.Triage;

				ProcessTriage(info).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				state = info.State = ProcessHandlingState.Invalid;
			}
			finally
			{
				HandlingStateChange?.Invoke(this, new HandlingStateChangeEventArgs(info));
			}
		}

		/*
		Lazy<System.Collections.Specialized.StringCollection> startTraceItems = new Lazy<System.Collections.Specialized.StringCollection>(MakeTraceItemList);
		System.Collections.Specialized.StringCollection MakeTraceItemList()
			=> new System.Collections.Specialized.StringCollection { "TargetInstance.SourceName", "ProcessID", "ParentProcessID", "ProcessName" };
		*/

		ManagementEventWatcher NewProcessWatcher = null;
		ManagementEventWatcher ProcessEndWatcher = null;

		public const string WMIEventNamespace = @"\\.\root\CIMV2";

		void InitWMIEventWatcher()
		{
			if (!WMIPolling) return;

			// FIXME: doesn't seem to work when lots of new processes start at the same time.
			try
			{
				// Transition to permanent event listener?
				// https://msdn.microsoft.com/en-us/library/windows/desktop/aa393014(v=vs.85).aspx

				/*
				// Win32_ProcessStartTrace  works poorly for some reason
				try
				{
					// Win32_ProcessStartTrace requires Admin rights
					if (MKAh.Execution.IsAdministrator)
					{
						var query = new EventQuery("SELECT ProcessID,ParentProcessID,ProcessName FROM Win32_ProcessStartTrace WITHIN " + WMIPollDelay);
						//query.Condition = "TargetInstance ISA 'Win32_Process'";
						//query.WithinInterval = TimeSpan.FromSeconds(WMIPollDelay);
						NewProcessWatcher = new ManagementEventWatcher(new ManagementScope(WMIEventNamespace), query);
						NewProcessWatcher.EventArrived += StartTraceTriage;
					}
				}
				catch (Exception ex)
				{
					NewProcessWatcher = null;
					Logging.Stacktrace(ex);
				}
				*/

				if (NewProcessWatcher is null)
				{
					// 'TargetInstance.Handle','TargetInstance.Name','TargetInstance.ExecutablePath','TargetInstance.CommandLine'
					var query = new EventQuery("SELECT TargetInstance FROM __InstanceCreationEvent WITHIN " + WMIPollDelay + " WHERE TargetInstance ISA 'Win32_Process'");

					// Avast cybersecurity causes this to throw an exception
					NewProcessWatcher = new ManagementEventWatcher(new ManagementScope(WMIEventNamespace), query);

					/*
					ProcessEndWatcher = new ManagementEventWatcher(
						new ManagementScope(@"\\.\root\CIMV2"),
						new EventQuery("SELECT * FROM __InstanceDeletionEvent WITHIN 10 WHERE TargetInstance ISA 'Win32_Process'")
						);
					*/

					NewProcessWatcher.EventArrived += NewInstanceTriage;
					//ProcessEndWatcher.EventArrived += ProcessEndTriage;
				}

				NewProcessWatcher.Start();
				//ProcessEndWatcher.Start();

				if (DebugWMI) Log.Debug("<<WMI>> New instance watcher initialized.");
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				throw new InitFailure("<<WMI>> Event watcher initialization failure", ex);
			}
		}

		void StopWMIEventWatcher()
		{
			NewProcessWatcher?.Dispose();
			NewProcessWatcher = null;
			ProcessEndWatcher?.Dispose();
			ProcessEndWatcher = null;
		}

		public const string WatchlistFile = "Watchlist.ini";

		async void CleanupTick(object _sender, EventArgs _ea)
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException("CleanupTick called when ProcessManager was already disposed");

			await Task.Delay(0).ConfigureAwait(false);

			Cleanup();

			lock (watchlist_lock) SortWatchlist();

			var now = DateTimeOffset.UtcNow;
			foreach (var item in ScanBlockList)
			{
				if (item.Value.TimeTo(now).TotalSeconds > 5d)
					ScanBlockList.TryRemove(item.Key, out _);
			}
		}

		int cleanup_lock = 0;
		/// <summary>
		/// Cleanup.
		/// </summary>
		/// <remarks>
		/// Locks: waitforexit_lock
		/// </remarks>
		public async void Cleanup()
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException("Cleanup called when ProcessManager was already disposed"); ;

			if (!Atomic.Lock(ref cleanup_lock)) return; // already in progress

			if (DebugPower || DebugProcesses) Log.Debug("<Process> Periodic cleanup");

			// TODO: Verify that this is actually useful?

			Stack<ProcessEx> triggerList = null;
			try
			{
				var items = WaitForExitList.Values;
				foreach (var info in items)
				{
					try
					{
						info.Process.Refresh();
						info.Process.WaitForExit(20);
					}
					catch { } // ignore
				}

				await Task.Delay(1_000).ConfigureAwait(false);
				if (cts.IsCancellationRequested) return;

				triggerList = new Stack<ProcessEx>();
				foreach (var info in items)
				{
					try
					{
						info.Process.Refresh();
						info.Process.WaitForExit(20);
						if (info.Process.HasExited)
						{
							info.State = ProcessHandlingState.Exited;
							triggerList.Push(info);
						}
					}
					catch (Exception ex) when (ex is NullReferenceException || ex is OutOfMemoryException) { throw; }
					catch
					{
						//Logging.Stacktrace(ex);
						triggerList.Push(info);// potentially unwanted behaviour, but it's better this way
					}
				}

				if (triggerList != null)
				{
					while (triggerList.Count > 0)
						WaitForExitTriggered(triggerList.Pop()); // causes removal so can't be done in above loop
				}

				if (User.IdleTime().TotalHours > 2d)
				{
					foreach (var prc in Watchlist.Keys)
						prc.Refresh();
				}

				lock (Exclusive_lock)
				{
					foreach (var info in ExclusiveList.Values)
					{
						try
						{
							info.Process.Refresh();
							if (info.Process.HasExited)
							{
								info.State = ProcessHandlingState.Exited;
								EndExclusiveMode(info.Process, EventArgs.Empty);
							}
						}
						catch { }
					}
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				Atomic.Unlock(ref cleanup_lock);
				triggerList?.Clear();
			}
		}

		#region IDisposable Support
		public event EventHandler<DisposedEventArgs> OnDisposed;

		public void Dispose() => Dispose(true);

		readonly CancellationTokenSource cts = new CancellationTokenSource();

		bool DisposedOrDisposing = false;
		void Dispose(bool disposing)
		{
			if (DisposedOrDisposing) return;

			if (disposing)
			{
				DisposedOrDisposing = true;

				if (Trace) Log.Verbose("Disposing process manager...");

				cts.Cancel(true);

				//ScanStartEvent = null;
				//ScanEndEvent = null;
				ProcessModified = null;
				HandlingCounter = null;
				ProcessStateChange = null;
				HandlingStateChange = null;

				if (powermanager != null && powermanager.IsDisposed)
				{
					powermanager.onBehaviourChange -= PowerBehaviourEvent;
					powermanager = null;
				}

				try
				{
					//watcher.EventArrived -= NewInstanceTriage;
					NewProcessWatcher?.Dispose();
					NewProcessWatcher = null;

					if (activeappmonitor != null)
					{
						activeappmonitor.ActiveChanged -= ForegroundAppChangedEvent;
						activeappmonitor = null;
					}

					ScanTimer?.Dispose();
					MaintenanceTimer?.Dispose();
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
					// throw; // would throw but this is dispose
				}

				try
				{
					ExeToController?.Clear();
					ExeToController = null;

					using (var wcfg = Config.Load(WatchlistFile).BlockUnload())
					{
						foreach (var prc in Watchlist.Keys)
						{
							if (prc.NeedsSaving) prc.SaveConfig(wcfg.File);
							prc.Dispose();
						}
					}

					lock (watchlist_lock)
					{
						Watchlist?.Clear();
						Watchlist = null;

						if (WatchlistCache.IsValueCreated) WatchlistCache.Value.Clear();
						WatchlistCache = null;
					}
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
					// throw; // would throw but this is dispose
				}

				CancelPowerWait();
				WaitForExitList.Clear();

				foreach (var service in Services)
					service.Dispose();
				Services.Clear();

				ExclusiveList?.Clear();
				lock (Exclusive_lock) MKAh.Utility.DiscardExceptions(() => ExclusiveEnd());
			}

			OnDisposed?.Invoke(this, DisposedEventArgs.Empty);
			OnDisposed = null;
		}

		public void ShutdownEvent(object sender, EventArgs ea)
		{
			NewProcessWatcher?.Stop();
			ScanTimer?.Stop();
			MaintenanceTimer?.Stop();
		}
		#endregion
	}
}