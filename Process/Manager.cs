﻿//
// Process.Manager.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2016–2020 M.A.
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
using MKAh.Synchronize;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ini = MKAh.Ini;

namespace Taskmaster.Process
{
	using static Application;

	[Context(RequireMainThread = false)]
	public class Manager : IComponent
	{
		readonly Analyzer? analyzer;

		MKAh.Cache.SimpleCache<int, ProcessEx> LoaderCache = new MKAh.Cache.SimpleCache<int, ProcessEx>(limit: 260, retention: 80);

		readonly ConcurrentDictionary<int, ProcessEx> WaitForExitList = new ConcurrentDictionary<int, ProcessEx>(Environment.ProcessorCount, 60);
		readonly ConcurrentDictionary<int, ProcessEx> Running = new ConcurrentDictionary<int, ProcessEx>(Environment.ProcessorCount, 60);

		readonly ConcurrentDictionary<string, InstanceGroupLoad> Loaders = new ConcurrentDictionary<string, InstanceGroupLoad>(Environment.ProcessorCount, 40);
		readonly object Loader_lock = new object();

		public event EventHandler<LoaderEvent> LoaderActivity;
		public event EventHandler<LoaderEndEvent> LoaderRemoval;

		public ProcessEx[] GetExitWaitList() => WaitForExitList.Values.ToArray(); // copy is good here

		readonly System.Threading.Timer LoadTimer;

		void StartLoadAnalysisTimer()
		{
			LoadTimer.Change(TimeSpan.FromMinutes(1d), TimeSpan.FromMinutes(1d));
		}

		void AddRunning(ProcessEx info)
		{
			if (isdisposed) return;

			if (Running.TryAdd(info.Id, info))
			{
				info.Process.Exited += ProcessExit;
				info.HookExit();

				info.Process.Refresh();
				if (info.Process.HasExited)
					ProcessExit(info.Process, EventArgs.Empty);

				if (LoaderTracking) StartLoadAnalysis(info).ConfigureAwait(false);
			}
		}

		async Task StartLoadAnalysis(ProcessEx info)
		{
			await Task.Delay(10).ConfigureAwait(false);

			bool added = false;

			InstanceGroupLoad group = null;

			try
			{
				// TODO: Do basic monitoring.
				lock (Loader_lock)
				{
					if (!Loaders.TryGetValue(info.Name, out group))
					{
						group = new InstanceGroupLoad(info.Name, LoadType.All);
						added = Loaders.TryAdd(info.Name, group); // this failing doesn't matter too much
					}

					group.TryAdd(info);
				}

				if (added) LoaderActivity?.Invoke(this, new LoaderEvent(new[] { group }));
			}
			catch (Exception ex) when (ex is InvalidOperationException || ex is NullReferenceException)
			{
				group?.Remove(info);
			}
			catch (Exception ex)
			{
				group?.Remove(info);
				Logging.Stacktrace(ex);
			}
		}

		void RemoveLoader(string name, InstanceGroupLoad group = null)
		{
			try
			{
				if (group != null || Loaders.TryGetValue(name, out group))
				{
					Loaders.TryRemove(name, out _);
					LoaderRemoval?.Invoke(this, new LoaderEndEvent(group));
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		async Task EndLoadAnalysis(ProcessEx info)
		{
			InstanceGroupLoad load;
			lock (Loader_lock)
			{
				if (!Loaders.TryGetValue(info.Name, out load))
					return;

				load.Remove(info);
			}

			await Task.Delay(30_000).ConfigureAwait(false);

			try
			{
				lock (Loader_lock)
				{
					if (load.InstanceCount <= 0)
						RemoveLoader(info.Name, load);
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		public void GenerateLoadTrackers()
		{
			InstanceGroupLoad[] loadlist;
			lock (Loader_lock) loadlist = Loaders.Values.ToArray();

			var now = DateTimeOffset.UtcNow;

			// Initialize samples
			foreach (var loader in loadlist)
				if (now.Since(loader.Last).TotalSeconds > 30d) loader.Update();

			LoaderActivity?.Invoke(this, new LoaderEvent(loadlist));
		}

		bool TrackEverything, InstantLoaderSignaling;

		void InspectLoaders(object _)
		{
			if (LoaderActivity is null)
			{
				if (Trace) Logging.DebugMsg("<Process:Loaders> None subscribed.");
				return; // don't process while no-one is subscribed
			}

			Logging.DebugMsg("<Process:Loaders> Inspecting.");

			var now = DateTimeOffset.UtcNow;

			var heavyLoaders = new List<InstanceGroupLoad>(4);
			var removeList = new List<InstanceGroupLoad>(2);

			int skipped = 0;

			InstanceGroupLoad[] loadlist;
			lock (Loader_lock) loadlist = Loaders.Values.ToArray();

			var heaviest = loadlist[0];

			foreach (var loader in loadlist)
			{
				if (loader.InstanceCount > 0)
				{
					if (loader.Last.To(now).TotalSeconds < 5d)
					{
						loader.Update();

						if (!TrackEverything && loader.Load < 0.5f)
						{
							skipped++;
							continue; // skip lightweights
						}

						if (heaviest.Load < loader.Load)
							heaviest = loader;
					}

					if (loader.Samples > 0)
					{
						heavyLoaders.Add(loader);

						if (loader.LastHeavy && loader.Heavy > 5)
							Logging.DebugMsg($"LOADER [{loader.Load:0.#}]: {loader.Instance} [×{loader.InstanceCount}] - CPU: {loader.CPULoad.Average:0.#}, RAM: {loader.RAMLoad.Current / MKAh.Units.Binary.Giga:0.###} GiB, IO: {loader.IOLoad.Average:0.#}/s");
					}
					else
						skipped++;
				}
				else
				{
					skipped++;

					if (now.Since(loader.Last).TotalMinutes > 3f)
						removeList.Add(loader);

					// TODO: Remove
				}
			}

			Logging.DebugMsg($"HEAVYWEIGHT [{heaviest.Load:0.#}]: {heaviest.Instance} [×{heaviest.InstanceCount}] - CPU: {heaviest.CPULoad.Average:0.#}, RAM: {heaviest.RAMLoad.Current / MKAh.Units.Binary.Giga:0.###} GiB, IO: {heaviest.IOLoad.Average:0.#}/s");

			/*
			heavyLoaders.Sort(LoadInfoComparer);

			int i = 0;
			foreach (var loader in heavyLoaders)
			{
				loader.Order = i;
				Logging.DebugMsg($"LOADER [{loader.Load:N1}]: {loader.Instance} [×{loader.InstanceCount}] - CPU: {loader.CPULoad.Average:N1} %, RAM: {loader.RAMLoad.Current / MKAh.Units.Binary.Giga:N3} GiB, IO: {loader.IOLoad.Average:N1}/s");
			}
			*/

			LoaderActivity?.Invoke(this, new LoaderEvent(heavyLoaders.ToArray()));

			foreach (var group in removeList)
				RemoveLoader(group.Instance);
		}

		int LoadInfoComparer(InstanceGroupLoad x, InstanceGroupLoad y)
		{
			const int XDown = 1, YDown = -1;

			if (x.LastHeavy && !y.LastHeavy)
				return YDown;
			else if (!x.LastHeavy && y.LastHeavy)
				return XDown;

			if (x.Load - .2f > y.Load)
				return YDown;
			if (x.Load < y.Load - .2f)
				return XDown;

			if (x.Heavy > y.Heavy)
				return YDown;
			else if (x.Heavy < y.Heavy)
				return XDown;

			return 0;

			/*
			int weight = 0;

			if (x.RAMLoad.HeavyCount > y.RAMLoad.HeavyCount)
				weight++;
			else if (x.RAMLoad.HeavyCount < y.RAMLoad.HeavyCount)
				weight--;

			if (x.CPULoad.HeavyCount > y.CPULoad.HeavyCount)
				weight++;
			else if (x.CPULoad.HeavyCount < y.CPULoad.HeavyCount)
				weight--;

			if (x.IOLoad.HeavyCount > y.IOLoad.HeavyCount)
				weight++;
			else if (x.IOLoad.HeavyCount < y.IOLoad.HeavyCount)
				weight--;

			return weight;
			*/
		}

		void ProcessExit(object sender, EventArgs ea)
		{
			if (isdisposed) return;

			if (sender is System.Diagnostics.Process proc && RemoveRunning(proc.Id, out var info))
			{
				info.State = HandlingState.Exited;
				info.ExitWait = false;
			}
		}

		bool RemoveRunning(int pid, out ProcessEx removed)
		{
			if (isdisposed)
			{
				removed = null;
				return false;
			}

			if (Running.TryRemove(pid, out removed))
			{
				WaitForExitList.TryRemove(pid, out _);

				if (LoaderTracking) EndLoadAnalysis(removed).ConfigureAwait(false);

				return true;
			}

			return false;
		}

		public int RunningCount => Running.Count;

		public void CacheProcess(ProcessEx info) => AddRunning(info);

		public bool GetCachedProcess(int pid, out ProcessEx? info)
		{
			if (Running.TryGetValue(pid, out info))
				return true;

			info = null;
			return false;
		}

		public bool LoaderTracking { get; set; }

		public static bool DebugLoaders { get; set; }

		public static bool DebugScan { get; set; }

		public static bool DebugPaths { get; set; }

		public static bool DebugAdjustDelay { get; set; }

		public static bool DebugPaging { get; set; }

		public static bool DebugProcesses { get; private set; }

		public static bool ShowUnmodifiedPortions { get; set; } = true;

		public static bool ShowOnlyFinalState { get; set; }

		public static bool ShowForegroundTransitions { get; set; }

		bool ColorResetEnabled { get; set; }

		/// <summary>
		/// Watch rules
		/// </summary>
		readonly ConcurrentDictionary<Controller, int> Watchlist = new ConcurrentDictionary<Controller, int>();

		readonly ConcurrentDictionary<int, DateTimeOffset> ScanBlockList = new ConcurrentDictionary<int, DateTimeOffset>();

		Lazy<List<Controller>> WatchlistCache;
		bool NeedSort = true;

		readonly object watchlist_lock = new object();

		public bool WMIPolling { get; private set; } = true;
		public int WMIPollDelay { get; private set; } = 5;

		public static TimeSpan? IgnoreRecentlyModified { get; set; } = TimeSpan.FromMinutes(30);

		// ctor, constructor
		public Manager()
		{
			RenewWatchlistCache();

			// testing
			// LoadTimer = new Timer(InspectLoaders, null, System.Threading.Timeout.InfiniteTimeSpan, System.Threading.Timeout.InfiniteTimeSpan);

			ScanTimer = new System.Timers.Timer(ScanFrequency.Value.TotalMilliseconds);
			MaintenanceTimer = new System.Timers.Timer(1_000 * 60 * 60 * 3); // every 3 hours

			if (RecordAnalysis.HasValue) analyzer = new Analyzer();

			if (DebugProcesses) Log.Debug($"<CPU> Logical cores: {Hardware.Utility.ProcessorCount}, full mask: {Process.Utility.FormatBitMask(Utility.FullCPUMask, Hardware.Utility.ProcessorCount, BitmaskStyle.Bits)} ({Utility.FullCPUMask} = OS control)");

			LoadConfig();

			LoadWatchlist();

			if (WMIPolling)
			{
				try
				{
					// 'TargetInstance.Handle','TargetInstance.Name','TargetInstance.ExecutablePath','TargetInstance.CommandLine'
					var query = new EventQuery("SELECT TargetInstance,TIME_CREATED FROM __InstanceCreationEvent WITHIN " + WMIPollDelay.ToString(CultureInfo.InvariantCulture) + " WHERE TargetInstance ISA 'Win32_Process'");

					// Avast cybersecurity causes this to throw an exception
					NewProcessWatcher = new ManagementEventWatcher(new ManagementScope(WMIEventNamespace), query);


					NewProcessWatcher.EventArrived += NewInstanceTriage;
					//ProcessEndWatcher.EventArrived += ProcessEndTriage;

					NewProcessWatcher.Start();
					if (DebugWMI) Log.Debug("<<WMI>> New instance watcher initialized.");
				}
				catch (UnauthorizedAccessException) { throw; }
				catch (System.Runtime.InteropServices.COMException ex)
				{
					Log.Fatal("<<WMI>> Exception: " + ex.Message);
					throw new InitFailure("<<WMI>> Event watcher initialization failure", ex);
				}
			}

			var InitialScanDelay = TimeSpan.FromSeconds(5);
			NextScan = DateTimeOffset.UtcNow.Add(InitialScanDelay);

			ScanTimer.Elapsed += TimedScan;

			MaintenanceTimer.Elapsed += CleanupTick;
			MaintenanceTimer.Start();

			Application.OnStart += OnStart;

			if (DebugProcesses) Log.Information("<Process> Component Loaded.");
		}

		async Task ScanAsync()
		{
			await Task.Delay(10).ConfigureAwait(false);

			Scan();
			StartScanTimer();
		}

		async void OnStart(object sender, EventArgs ea)
		{
			try
			{
				await Task.Factory.StartNew(ScanAsync, cts.Token, TaskCreationOptions.LongRunning | TaskCreationOptions.HideScheduler, TaskScheduler.Default).ConfigureAwait(false);
			}
			catch (TaskCanceledException)
			{
				// NOP
			}
		}

		public Controller[] GetWatchlist() => Watchlist.Keys.ToArray();

		// TODO: Need an ID mapping
		public bool GetControllerByName(string friendlyname, out Controller controller)
			=> (controller = Watchlist.Keys.FirstOrDefault(prc => prc.FriendlyName.Equals(friendlyname, StringComparison.InvariantCultureIgnoreCase))) != null;

		/// <summary>
		/// Executable name to ProcessControl mapping.
		/// </summary>
		readonly ConcurrentDictionary<string, Controller[]> ExeToController = new ConcurrentDictionary<string, Controller[]>();

		public int DefaultBackgroundPriority = 1, DefaultBackgroundAffinity;

		ForegroundManager? activeappmonitor;

		public void Hook(ForegroundManager manager)
		{
			activeappmonitor = manager;
			activeappmonitor.ActiveChanged += ForegroundAppChangedEvent;
			activeappmonitor.OnDisposed += (_, _2) => activeappmonitor = null;
		}

		Power.Manager? powermanager;

		public void Hook(Power.Manager manager)
		{
			powermanager = manager;
			powermanager.BehaviourChange += PowerBehaviourEvent;
			powermanager.OnDisposed += (_, _2) => powermanager = null;
		}

		readonly ConcurrentDictionary<int, int> IgnorePids = new ConcurrentDictionary<int, int>();

		public void Ignore(int pid) => IgnorePids.TryAdd(pid, 0);

		public void Unignore(int pid) => IgnorePids.TryRemove(pid, out _);

		readonly MKAh.Synchronize.Atomic FreeMemLock = new MKAh.Synchronize.Atomic();

		public async Task FreeMemoryAsync(int ignorePid = -1)
		{
			if (!PagingEnabled) return;

			try
			{
				if (DebugPaging) Log.Debug("<Process> Requesting OS to page applications to swap file...");

				await FreeMemoryInternal(ignorePid).ConfigureAwait(false);
			}
			catch (Exception ex) when (ex is NullReferenceException || ex is OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		internal async Task FreeMemoryInternal(int ignorePid = -1)
		{
			if (isdisposed) throw new ObjectDisposedException(nameof(Manager), "FreeMemoryInterval called when ProcessManager was already disposed");

			if (!FreeMemLock.TryLock()) return;
			using var fmlock = FreeMemLock.ScopedUnlock();

			await Task.Delay(5).ConfigureAwait(false);

			Memory.Update();
			long memorybefore = Memory.Free;
			//var b1 = MemoryManager.Free;

			try
			{
				ScanTimer.Stop(); // Pause Scan until we're done

				while (ScanLock.IsLocked) await Task.Delay(200).ConfigureAwait(false);

				Scan(ignorePid, PageToDisk: true); // TODO: Call for this to happen otherwise
				if (cts.IsCancellationRequested) return;

				// TODO: Wait a little longer to allow OS to Actually page stuff. Might not matter?
				//var b2 = MemoryManager.Free;
				Memory.Update();
				long memoryafter = Memory.Free;

				Log.Information("<Memory> Paging complete, observed change: " +
					HumanInterface.ByteString(memorybefore - memoryafter, true, iec: true) + " – this may not be due to actions performed.");
			}
			catch (Exception ex) when (ex is AggregateException || ex is OperationCanceledException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				StartScanTimer();
			}
		}

		/// <summary>
		/// Spawn separate thread to run program scanning.
		/// </summary>
		async void TimedScan(object _sender, System.Timers.ElapsedEventArgs _)
		{
			if (isdisposed) return; // HACK: dumb timers be dumb

			try
			{
				if (Trace) Log.Verbose("Rescan requested.");

				await Task.Delay(10).ConfigureAwait(false); // asyncify
				if (cts.IsCancellationRequested) return;

				Scan();
				if (cts.IsCancellationRequested) return;
			}
			catch (OperationCanceledException) { /* NOP */ }
			catch (AggregateException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				Log.Error("Stopping periodic scans");
				ScanTimer.Stop();
			}
		}

		public ModificationDelegate ProcessModified;
		public InfoDelegate ProcessStateChange;

		public event EventHandler<HandlingStateChangeEventArgs> HandlingStateChange;

		readonly MKAh.Synchronize.Atomic hastenScanLock = new MKAh.Synchronize.Atomic();

		public async Task HastenScan(TimeSpan delay, bool forceSort = false)
		{
			// delay is unused but should be used somehow

			if (!hastenScanLock.TryLock()) return;
			using var hslock = hastenScanLock.ScopedUnlock();

			try
			{
				await Task.Delay(delay).ConfigureAwait(false); // asyncify

				double nextscan = Math.Max(0, DateTimeOffset.UtcNow.To(NextScan).TotalSeconds);
				if (nextscan > 5) // skip if the next scan is to happen real soon
				{
					NextScan = DateTimeOffset.UtcNow;
					ScanTimer.Stop();
					if (forceSort) SortWatchlist();
					if (cts.IsCancellationRequested) return;

					await Task.Factory.StartNew(ScanAsync, cts.Token, TaskCreationOptions.LongRunning | TaskCreationOptions.HideScheduler, TaskScheduler.Default).ConfigureAwait(false);
				}
			}
			catch (Exception ex) when (ex is NullReferenceException || ex is OutOfMemoryException) { throw; }
			catch (TaskCanceledException) { /* NOP */ }
			catch (OperationCanceledException) { /* NOP */ }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		void StartScanTimer()
		{
			if (isdisposed) throw new ObjectDisposedException(nameof(Manager), "StartScanTimer called when ProcessManager was already disposed");

			if (!ScanFrequency.HasValue) return; // dumb hack

			try
			{
				// restart just in case
				ScanTimer.Stop();
				ScanTimer.Start();
				NextScan = DateTimeOffset.UtcNow.AddMilliseconds(ScanTimer.Interval);
			}
			catch (ObjectDisposedException) { Statistics.DisposedAccesses++; }
		}

		readonly int SelfPID = System.Diagnostics.Process.GetCurrentProcess().Id;

		readonly MKAh.Synchronize.Atomic ScanLock = new MKAh.Synchronize.Atomic();

		public event EventHandler ScanStart;
		public event EventHandler<ScanEndEventArgs> ScanEnd;

		void Scan(int ignorePid = -1, bool PageToDisk = false)
		{
			if (isdisposed) throw new ObjectDisposedException(nameof(Manager), "Scan called when ProcessManager was already disposed");
			if (cts.IsCancellationRequested) return;

			if (!ScanLock.TryLock()) return;
			using var scscoped = ScanLock.ScopedUnlock();

			var timer = Stopwatch.StartNew();

			int found = 0;

			try
			{
				NextScan = (LastScan = DateTimeOffset.UtcNow).Add(ScanFrequency.Value);

				if (Trace && DebugScan) Log.Debug("<Process> Full Scan: Start");

				ScanStart?.Invoke(this, EventArgs.Empty);

				if (!Utility.SystemProcessId(ignorePid)) Ignore(ignorePid);

				var procs = System.Diagnostics.Process.GetProcesses();
				found = procs.Length;

				System.Threading.Interlocked.Add(ref Handling, found);

				//var loaderOffload = new List<ProcessEx>();

				int modified = 0, ignored = 0, unmodified = 0;
				foreach (var process in procs)
				{
					var info = ScanTriage(process, PageToDisk);

					if (info != null)
					{
						if (info.State == HandlingState.Modified)
							modified++;
						else if (info.State == HandlingState.Abandoned)
							ignored++;
						else
							unmodified++;
					}
					else
						ignored++;

					System.Threading.Interlocked.Decrement(ref Handling);
				}

				//SystemLoaderAnalysis(loaderOffload);

				if (Trace && DebugScan) Log.Debug("<Process> Full Scan: Complete");

				int missed = found - (modified + ignored + unmodified);
				if (missed > 0)
				{
					System.Threading.Interlocked.Add(ref Handling, -missed);

					Log.Error("<Process> Missed " + missed.ToString(CultureInfo.InvariantCulture) + " items while scanning.");
				}

				ScanEnd?.Invoke(this, new ScanEndEventArgs() { Found = found, Ignored = ignored, Modified = modified });

				if (!Utility.SystemProcessId(ignorePid)) Unignore(ignorePid);
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
		}

		ProcessEx ScanTriage(System.Diagnostics.Process process, bool doPaging = false)
		{
			cts.Token.ThrowIfCancellationRequested();

			string name = string.Empty;
			int pid = -1;

			ProcessEx info = null;

			try
			{
				name = process.ProcessName;
				pid = process.Id;

				if (ScanBlockList.TryGetValue(pid, out var found))
				{
					if (found.To(DateTimeOffset.UtcNow).TotalSeconds < 5d) return null;
					ScanBlockList.TryRemove(pid, out _);
				}

				if (IgnoreProcessID(pid) || IgnoreProcessName(name) || pid == SelfPID)
				{
					if (ShowInaction && DebugScan) Log.Debug($"<Process/Scan> Ignoring {name} #{pid}");
					return null;
				}

				if (Trace && DebugScan) Log.Verbose($"<Process> Checking: {name} #{pid}");

				if (GetCachedProcess(pid, out info))
				{
					bool stale = false;

					try
					{
						if (Trace && DebugProcesses) Logging.DebugMsg("<Process:Scan> Re-using old ProcessEx: " + info);

						//info.Process.Refresh();
						//if (info.Process.HasExited) // Stale, for some reason still kept
						if (info.Exited)
						{
							if (Trace && DebugProcesses) Logging.DebugMsg("<Process:Scan> Re-using old ProcessEx - except not, stale");
							stale = true;
						}
						else
							info.State = HandlingState.Triage; // still valid
					}
					catch
					{
						stale = true;
					}

					if (stale)
					{
						RemoveRunning(pid, out _);
						info = null;
					}
				}

				if (info != null || Utility.Construct(pid, out info, process, null, name, null, getPath: true))
				{
					info.Timer.Restart();

					// Protected files, expensive but necessary.
					info.PriorityProtected = ProtectedProcess(info.Name, info.Path);
					info.AffinityProtected = (info.PriorityProtected && ProtectionLevel == 2);
					info.FullyProtected = (info.PriorityProtected && ProtectionLevel == 2);

					ProcessTriage(info, old: true).ConfigureAwait(false);

					if (doPaging) PageToDisk(info).ConfigureAwait(false); // don't wait on this

					if (info.State == HandlingState.Triage)
					{
						//Logging.DebugMsg("Process still in Triage state, setting to Finished");
						info.State = HandlingState.Finished; // HACK
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
					Log.Debug($"<Process/Scan> Failed to retrieve info for process: {name} #{pid}");
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

			return info;
		}

		static async Task PageToDisk(ProcessEx info)
		{
			await Task.Delay(1_500).ConfigureAwait(false);

			try
			{
				if (DebugPaging)
				{
					var sbs = new StringBuilder(info.ToFullFormattedString()).Append(" Paging.");
					Log.Debug(sbs.ToString());
				}

				NativeMethods.EmptyWorkingSet(info.Process.Handle); // process.Handle may throw which we don't care about
			}
			catch { } // don't care
		}

		int SystemLoader_lock = Atomic.Unlocked;
		DateTimeOffset LastLoaderAnalysis = DateTimeOffset.MinValue;

		async Task SystemLoaderAnalysis(List<ProcessEx> procs)
		{
			var now = DateTimeOffset.UtcNow;

			if (!MKAh.Synchronize.Atomic.Lock(ref SystemLoader_lock)) return;

			// TODO: TEST IF ANY RESOURCE IS BEING DRAINED

			try
			{
				if (LastLoaderAnalysis.To(now).TotalMinutes < 1d) return;

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
				MKAh.Synchronize.Atomic.Unlock(ref SystemLoader_lock);
			}
		}

		internal void SetDebug(bool value)
		{
			DebugProcesses = value;
			Utility.Debug = value;

			foreach (Controller prc in Watchlist.Keys) prc.Debug = value;
		}

		/// <summary>
		/// In seconds.
		/// </summary>
		public TimeSpan? ScanFrequency { get; private set; } = TimeSpan.FromSeconds(180);

		DateTimeOffset LastScan { get; set; } = DateTimeOffset.MinValue; // UNUSED

		public DateTimeOffset NextScan { get; set; }

		// static bool ControlChildren = false; // = false;

		readonly System.Timers.Timer ScanTimer;
		readonly System.Timers.Timer MaintenanceTimer;

		// move all this to prc.Validate() or prc.SanityCheck();
		bool ValidateController(Controller prc)
		{
			var rv = true;

			if (prc.Priority.HasValue && prc.BackgroundPriority.HasValue && prc.BackgroundPriority.Value.ToInt32() >= prc.Priority.Value.ToInt32())
			{
				prc.SetForegroundMode(ForegroundMode.Ignore);
				Log.Warning("[" + prc.FriendlyName + "] Background priority equal or higher than foreground priority, ignoring.");
			}

			if (prc.Executables.Length <= 0 && string.IsNullOrEmpty(prc.Path))
			{
				Log.Warning("[" + prc.FriendlyName + "] Executable and Path missing; ignoring.");
				rv = false;
			}

			// SANITY CHECKING
			if (prc.ExecutableFriendlyName.Length > 0)
			{
				// TODO: MULTIEXE

				foreach (var exe in prc.ExecutableFriendlyName)
				{
					if (IgnoreProcessName(exe))
					{
						if (ShowInaction && DebugProcesses)
							Log.Warning(exe + " in ignore list; all changes denied.");

						// rv = false; // We'll leave the config in.
					}
					else if (ProtectedProcessName(exe))
					{
						if (ShowInaction && DebugProcesses)
							Log.Warning(exe + " in protected list; priority changing denied.");
					}
				}
			}

			return rv;
		}

		void SaveController(Controller prc)
		{
			lock (watchlist_lock)
			{
				Watchlist.TryAdd(prc, 0);
				RenewWatchlistCache();
			}

			// TODO: MULTIEXE
			/*
			if (Trace) Log.Verbose("[" + prc.FriendlyName + "] Match: " + (prc.Executables ?? prc.Path) + ", " +
				(prc.Priority.HasValue ? Readable.ProcessPriority(prc.Priority.Value) : HumanReadable.Generic.NotAvailable) +
				", Mask:" + (prc.AffinityMask >= 0 ? prc.AffinityMask.ToString() : HumanReadable.Generic.NotAvailable) +
				", Recheck: " + prc.Recheck + "s, Foreground: " + prc.Foreground.ToString());
			*/
		}

		void LoadConfig()
		{
			if (DebugProcesses) Log.Information("<Process> Loading configuration...");

			using var corecfg = Config.Load(CoreConfigFilename);
			var perfsec = corecfg.Config["Performance"];

			// ControlChildren = coreperf.GetSetDefault("Child processes", false, out tdirty).Bool;
			// dirtyconfig |= tdirty;

			int ignRecentlyModified = perfsec.GetOrSet("Ignore recently modified", 30)
				.InitComment("Performance optimization. More notably this enables granting self-determination to apps that actually think they know better.")
				.Int.Constrain(0, 24 * 60);
			IgnoreRecentlyModified = ignRecentlyModified > 0 ? (TimeSpan?)TimeSpan.FromMinutes(ignRecentlyModified) : null;

			var tscan = perfsec.GetOrSet(Constants.ScanFrequency, 15)
				.InitComment("Frequency (in seconds) at which we scan for processes. 0 disables.")
				.Int.Constrain(0, 360);
			ScanFrequency = (tscan > 0) ? (TimeSpan?)TimeSpan.FromSeconds(tscan.Constrain(5, 360)) : null;

			// --------------------------------------------------------------------------------------------------------

			WMIPolling = perfsec.GetOrSet(Constants.WMIEventWatcher, false)
				.InitComment("Use WMI to be notified of new processes starting. If disabled, only rescanning everything will cause processes to be noticed.")
				.Bool;

			WMIPollDelay = perfsec.GetOrSet(Constants.WMIPollDelay, 5)
				.InitComment("WMI process watcher delay (in seconds).  Smaller gives better results but can inrease CPU usage. Accepted values: 1 to 5.")
				.Int.Constrain(1, 5);

			// --------------------------------------------------------------------------------------------------------

			var fgpausesec = corecfg.Config["Foreground Focus Lost"];
			// RestoreOriginal = fgpausesec.GetSetDefault("Restore original", false, out modified).Bool;
			// dirtyconfig |= modified;
			DefaultBackgroundPriority = fgpausesec.GetOrSet(Constants.DefaultPriority, 2)
				.InitComment("Default is normal to avoid excessive loading times while user is alt-tabbed.")
				.Int.Constrain(0, 4);

			// OffFocusAffinity = fgpausesec.GetSetDefault("Affinity", 0, out modified).Int;
			// dirtyconfig |= modified;
			// OffFocusPowerCancel = fgpausesec.GetSetDefault("Power mode cancel", true, out modified).Bool;
			// dirtyconfig |= modified;

			DefaultBackgroundAffinity = fgpausesec.GetOrSet(Constants.DefaultAffinity, 14).Int.Constrain(0, Utility.FullCPUMask);

			// --------------------------------------------------------------------------------------------------------

			// Taskmaster.cfg["Applications"]["Ignored"].StringArray = IgnoreList;
			var ignsetting = corecfg.Config["Applications"];
			string[] newIgnoreList = ignsetting.GetOrSet(HumanReadable.Generic.Ignore, IgnoreList)
				.InitComment("Special hardcoded protection applied to: consent, winlogon, wininit, and csrss. These are vital system services and messing with them can cause severe system malfunctioning. Mess with the ignore list at your own peril.")
				.Array;

			if (newIgnoreList?.Length > 0)
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

			ProtectionLevel = ignsetting.GetOrSet("Protection level", 2)
				.InitComment("Amount of core system shielding to do. 1 = Affinity tuning allowed. 2 = Full protection.")
				.Int;
			if (ProtectionLevel < 1 || ProtectionLevel > 2) ProtectionLevel = 2;

			var dbgsec = corecfg.Config[HumanReadable.Generic.Debug];
			// .Get to not add these in case they don't exist
			DebugWMI = dbgsec.Get("WMI")?.Bool ?? false;
			DebugScan = dbgsec.Get("Full scan")?.Bool ?? false;
			DebugPaths = dbgsec.Get("Paths")?.Bool ?? false;
			DebugAdjustDelay = dbgsec.Get("Adjust Delay")?.Bool ?? false;
			DebugProcesses = dbgsec.Get("Processes")?.Bool ?? false;
			DebugPaging = dbgsec.Get(Application.Constants.Paging)?.Bool ?? false;
			DebugLoaders = dbgsec.Get("Loaders")?.Bool ?? false;

			var logsec = corecfg.Config[Application.Constants.Logging];

			ShowUnmodifiedPortions = logsec.GetOrSet("Unmodified portions", ShowUnmodifiedPortions).Bool;
			ShowOnlyFinalState = logsec.GetOrSet("Final state only", ShowOnlyFinalState).Bool;
			ShowForegroundTransitions = logsec.GetOrSet("Foreground transitions", ShowForegroundTransitions).Bool;

			EnableParentFinding = logsec.GetOrSet("Enable parent finding", EnableParentFinding).Bool;

			if (!IgnoreSystem32Path) Log.Warning($"<Process> System32 ignore disabled.");

			var exsec = corecfg.Config[Application.Constants.Experimental];

			exsec.TryRemove("Window resize"); // DEPRECATED / OBSOLETE

			ColorResetEnabled = exsec.Get(Application.Constants.ColorReset)?.Bool ?? false;
			LoaderTracking = exsec.Get(Application.Constants.LoaderTracking)?.Bool ?? false;

			var sbs = new StringBuilder("<Process> Scan ", 128);

			if (ScanFrequency.HasValue)
				sbs.Append("frequency: ").AppendFormat(CultureInfo.InvariantCulture, "{0:N0}", ScanFrequency.Value.TotalSeconds).Append('s');
			else
				sbs.Append("disabled");

			sbs.Append("; ");

			if (WMIPolling)
				sbs.Append("New instance watcher poll delay ").Append(WMIPollDelay).Append('s');
			else
				sbs.Append("New instance watcher disabled");

			sbs.Append("; ");

			if (IgnoreRecentlyModified.HasValue)
				sbs.Append("Recently modified ignored for ").AppendFormat(CultureInfo.InvariantCulture, "{0:0.#}", IgnoreRecentlyModified.Value.TotalMinutes).Append(" mins");
			else
				sbs.Append("Recently modified not ignored");

			Log.Information(sbs.ToString());
		}

		void LoadWatchlist()
		{
			Log.Information("<Process> Loading watchlist...");

			int withPath = 0, hybrids = 0;

			using var appcfg = Config.Load(WatchlistFile);

			if (appcfg.Config.ItemCount == 0)
			{
				Log.Warning("<Process> Watchlist empty; loading example list.");

				// DEFAULT CONFIGURATION
				appcfg.File.Replace(Ini.Config.FromData(Properties.Resources.Watchlist.Split(new string[] { appcfg.Config.LineEnd }, StringSplitOptions.None)));

				// this won't be saved unless modified, which is probably fine
			}

			foreach (Ini.Section section in appcfg.Config)
			{
				try
				{
					if (string.IsNullOrEmpty(section.Name))
					{
						Log.Warning($"<Watchlist:{section.Line}> Nameless section; Skipping.");
						continue;
					}

					var rulePath = section.Get(HumanReadable.System.Process.Path);

					Ini.Setting ruleExec = section.Get(Constants.Executables);

					if (ruleExec is null && rulePath is null)
					{
						// TODO: Deal with incorrect configuration lacking image
						Log.Warning($"<Watchlist:{section.Line}> [" + section.Name + "] No image nor path; Skipping.");
						continue;
					}

					var rulePrio = section.Get(HumanReadable.System.Process.Priority);
					var ruleAff = section.Get(HumanReadable.System.Process.Affinity);
					var rulePow = section.Get(HumanReadable.Hardware.Power.Mode);

					int prio = rulePrio?.Int ?? -1;
					int aff = (ruleAff?.Int ?? -1);
					var pmode_t = rulePow?.String;

					if (aff > Utility.FullCPUMask || aff < -1)
					{
						Log.Warning($"<Watchlist:{ruleAff.Line}> [{section.Name}] Affinity({aff}) is malconfigured. Skipping.");
						//aff = Bit.And(aff, allCPUsMask); // at worst case results in 1 core used
						// TODO: Count bits, make 2^# assumption about intended cores but scale it to current core count.
						//		Shift bits to allowed range. Assume at least one core must be assigned, and in case of holes at least one core must be unassigned.
						aff = -1; // ignore
					}

					Power.Mode pmode = Power.Utility.GetModeByName(pmode_t);
					if (pmode == Power.Mode.Custom)
					{
						Log.Warning($"<Watchlist:{rulePow.Line}> [{section.Name}] Unrecognized power plan: {pmode_t}");
						pmode = Power.Mode.Undefined;
					}

					ProcessPriorityClass? prioR = Utility.IntToNullablePriority(prio);
					PriorityStrategy priostrat = PriorityStrategy.Ignore;
					if (prioR.HasValue)
					{
						priostrat = (PriorityStrategy)(section.Get(HumanReadable.System.Process.PriorityStrategy)?.Int.Constrain(0, 3) ?? 0);
						if (priostrat == PriorityStrategy.Ignore) prioR = null; // invalid data
					}

					AffinityStrategy affStrat = (aff >= 0)
						? (AffinityStrategy)(section.Get(HumanReadable.System.Process.AffinityStrategy)?.Int.Constrain(0, 3) ?? 2)
						: AffinityStrategy.Ignore;

					int baff = section.Get(Constants.BackgroundAffinity)?.Int ?? -1;
					int bpriot = section.Get(Constants.BackgroundPriority)?.Int ?? -1;
					ProcessPriorityClass? bprio = (bpriot >= 0) ? (ProcessPriorityClass?)Utility.IntToPriority(bpriot) : null;

					var pvis = (PathVisibilityOptions)(section.Get(Constants.PathVisibility)?.Int.Constrain(-1, 3) ?? -1);

					string[] tignorelist = (section.Get(HumanReadable.Generic.Ignore)?.Array ?? null);
					if (tignorelist?.Length > 0)
					{
						for (int i = 0; i < tignorelist.Length; i++)
						{
							ref var tig = ref tignorelist[i];
							tig = tig.ToLowerInvariant(); // does this work correctly?
														  //tignorelist[i] = tignorelist[i].ToLowerInvariant();
						}
					}
					else
						tignorelist = Array.Empty<string>();

					if (section.TryGet(Constants.Exclusive, out var exmode)) // DEPRECATED / OBSOLETE
						section.Remove(exmode);

					var prc = new Controller(section.Name, prioR, aff)
					{
						Enabled = (section.Get(HumanReadable.Generic.Enabled)?.Bool ?? true),
						Executables = (ruleExec?.StringArray ?? Array.Empty<string>()),
						Description = (section.Get(HumanReadable.Generic.Description)?.String ?? null),
						// friendly name is filled automatically
						PriorityStrategy = priostrat,
						AffinityStrategy = affStrat,
						Path = (rulePath?.String ?? null),
						ModifyDelay = (section.Get(Constants.ModifyDelay)?.Int ?? 0),
						//BackgroundIO = (section.TryGet("Background I/O")?.Bool ?? false), // Doesn't work
						Recheck = (section.Get(Constants.Recheck)?.Int ?? 0).Constrain(0, 300),
						PowerPlan = pmode,
						PathVisibility = pvis,
						BackgroundPriority = bprio,
						BackgroundAffinity = baff,
						IgnoreList = tignorelist,
						AllowPaging = (section.Get(Constants.AllowPaging)?.Bool ?? false),
						Analyze = (section.Get(Constants.Analyze)?.Bool ?? false),
						DeclareParent = (section.Get(Constants.DeclareParent)?.Bool ?? false),
						OrderPreference = (section.Get(Constants.Preference)?.Int.Constrain(0, 100) ?? 10),
						LogAdjusts = (section.Get(Application.Constants.Logging)?.Bool ?? true),
						LogStartAndExit = (section.Get(Constants.LogStartAndExit)?.Bool ?? false),
						Warn = (section.Get("Warn")?.Bool ?? false),
						Volume = (section.Get(HumanReadable.Hardware.Audio.Volume)?.Float ?? 0.5f),
						VolumeStrategy = (Audio.VolumeStrategy)(section.Get(Constants.VolumeStrategy)?.Int.Constrain(0, 5) ?? 0),
					};

					//prc.MMPriority = section.TryGet("MEM priority")?.Int ?? int.MinValue; // unused

					// special case
					var legacyworkaround = section.Get("Legacy workaround");

					try
					{
						prc.LegacyWorkaround = legacyworkaround?.Bool ?? false;
					}
					catch
					{
						Log.Error("[" + prc.FriendlyName + "] Malformed setting: " + legacyworkaround.Name + " = " + legacyworkaround.Value);
						section.Remove(legacyworkaround);
					}

					int? foregroundMode = section.Get(Constants.ForegroundMode)?.Int;
					if (foregroundMode.HasValue)
						prc.SetForegroundMode((ForegroundMode)foregroundMode.Value.Constrain(-1, 2));

					//prc.SetForegroundMode((ForegroundMode)(section.TryGet("Foreground mode")?.Int.Constrain(-1, 2) ?? -1)); // NEW

					if (section.TryGet("Affinity ideal", out var ideal)) // DEPRECATED / OBSOLETE
						section.Remove(ideal);

					// TODO: Blurp about following configuration errors
					if (prc.AffinityMask < 0) prc.AffinityStrategy = AffinityStrategy.Ignore;
					else if (prc.AffinityStrategy == AffinityStrategy.Ignore) prc.AffinityMask = -1;

					if (!prc.Priority.HasValue) prc.PriorityStrategy = PriorityStrategy.Ignore;
					else if (prc.PriorityStrategy == PriorityStrategy.Ignore) prc.Priority = null;

					// DEPRECATED / OBSOLETE
					section.TryRemove("Resize");
					section.TryRemove("Resize strategy");

					if (ColorResetEnabled)
						prc.ColorReset = section.Get(Constants.ColorReset)?.Bool ?? false;

					prc.Repair();

					if (AddController(prc))
					{
						if (!string.IsNullOrEmpty(prc.Path))
						{
							withPath++;

							if (prc.Executables.Length > 0) hybrids++;
						}

						// TODO: Check if this rule has problems.
					}
					else
						prc.Dispose();

					// cnt.Children &= ControlChildren;

					// cnt.delay = section.Contains("delay") ? section["delay"].Int : 30; // TODO: Add centralized default delay
					// cnt.delayIncrement = section.Contains("delay increment") ? section["delay increment"].Int : 15; // TODO: Add centralized default increment
				}
				catch (Exception ex)
				{
					Log.Error("<Watchlist> Error reading rule: " + section.Name);
					Log.Error(ex, "<Exception> ");
					// TODO: Should throw?
				}
			}

			lock (watchlist_lock) RenewWatchlistCache();
			SortWatchlist();

			// --------------------------------------------------------------------------------------------------------

			Log.Information($"<Process> Watchlist items – Name-based: {(Watchlist.Count - withPath)}; Path-based: {withPath - hybrids}; Hybrid: {hybrids} – Total: {Watchlist.Count}");
		}

		async void OnControllerAdjust(ModificationInfo ea)
		{
			try
			{
				var prc = ea.Info.Controller;

				if (!prc.LogAdjusts && !DebugProcesses) return;

				bool action = (ea.AffinityOld != ea.AffinityNew) || (ea.PriorityOld != ea.PriorityNew);

				if (!ShowInaction && !action && !DebugProcesses) return;

				await Task.Delay(10).ConfigureAwait(false); // probably not necessary

				bool onlyFinal = ShowOnlyFinalState;

				var sbs = new StringBuilder(256)
					.Append('[').Append(prc.FriendlyName).Append("] ").Append(prc.FormatPathName(ea.Info))
					.Append(" #").Append(ea.Info.Id);

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

						if (prc.Priority.HasValue && ea.Info.State == HandlingState.Paused && prc.Priority != ea.PriorityNew)
							sbs.Append(" [").Append(Utility.PriorityToInt(prc.Priority.Value)).Append(']');
					}
					else
						sbs.Append(HumanReadable.Generic.NotAvailable);

					if (ea.PriorityFail) sbs.Append(" [Failed]");
					if (ea.Info.PriorityProtected) sbs.Append(" [Protected]");
				}

				if (ShowUnmodifiedPortions || ea.AffinityNew >= 0)
				{
					sbs.Append("; Affinity: ");
					if (ea.AffinityOld >= 0)
					{
						if (!onlyFinal || ea.AffinityNew < 0)
							sbs.Append(Process.Utility.FormatBitMask(ea.AffinityOld, Hardware.Utility.ProcessorCount, LogBitmask));

						if (ea.AffinityNew >= 0)
						{
							if (!onlyFinal) sbs.Append(" → ");
							sbs.Append(Process.Utility.FormatBitMask(ea.AffinityNew, Hardware.Utility.ProcessorCount, LogBitmask));
						}

						if (prc.AffinityMask >= 0 && ea.Info.State == HandlingState.Paused && prc.AffinityMask != ea.AffinityNew)
							sbs.Append(" [mask: ").Append(prc.AffinityMask).Append(']'); // what was this supposed to show?
					}
					else
						sbs.Append(HumanReadable.Generic.NotAvailable);

					if (ea.AffinityFail) sbs.Append(" [Failed]");
					if (ea.Info.AffinityProtected) sbs.Append(" [Protected]");
				}

				if (DebugProcesses) sbs.Append(" [").Append(prc.AffinityStrategy.ToString()).Append(']');

				if (ea.NewIO >= 0) sbs.Append(" – I/O: ").Append(ea.NewIO.ToString());

				if (ea.Info.Is32BitExecutable)
				{
					sbs.Append(" [32-bit]");
					if (!ea.Info.IsLargeAddressAware) sbs.Append(" [no LAA]");
				}
				if (ea.Info.Legacy == LegacyLevel.Win95) sbs.Append(" [Legacy]");
				if (ea.Info.IsUniprocessorOnly) sbs.Append(" [Uniprocessor]");

				if (ea.User != null) sbs.Append(ea.User);

				if (DebugAdjustDelay)
				{
					sbs.Append(" – ").AppendFormat(CultureInfo.InvariantCulture, "{0:N0}", ea.Info.Timer.ElapsedMilliseconds).Append(" ms");
					if (ea.Info.WMIDelay.TotalMilliseconds > 0d) sbs.Append(" + ").Append(ea.Info.WMIDelay.TotalMilliseconds.ToString("N0", CultureInfo.InvariantCulture)).Append(" ms watcher delay");
				}

				if (EnableParentFinding && prc.DeclareParent)
				{
					sbs.Append(" – Parent: ");
					if (GetParent(ea.Info, out var parent))
						sbs.Append(parent.Name).Append(" #").Append(parent.Id);
					else
						sbs.Append("n/a");
				}

				// TODO: Add option to logging to file but still show in UI

				if (prc.LogAdjusts && action) Log.Information(sbs.ToString());
				else Log.Debug(sbs.ToString());

				ea.User?.Clear();
				ea.User = null;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		public bool GetParent(ProcessEx info, out ProcessEx? parent)
		{
			try
			{
				int parentId = info.Process.ParentProcessId();

				if (GetCachedProcess(parentId, out parent))
					return true;

				return Utility.Construct(parentId, out parent);
			}
			catch { /* don't care */ }

			parent = null;
			return false;
		}

		void RenewWatchlistCache() => WatchlistCache = new Lazy<List<Controller>>(LazyRecacheWatchlist, false);

		List<Controller> LazyRecacheWatchlist()
		{
			NeedSort = true;
			ResetWatchlistCancellation();
			return Watchlist.Keys.ToList();
		}

		CancellationTokenSource watchlist_cts = new CancellationTokenSource();

		void ResetWatchlistCancellation()
		{
			watchlist_cts.Cancel();
			watchlist_cts = new CancellationTokenSource();
		}

		/// <summary>
		/// Locks watchlist_lock
		/// </summary>
		public void SortWatchlist()
		{
			try
			{
				var token = watchlist_cts.Token;

				if (Trace) Logging.DebugMsg("SORTING PROCESS MANAGER WATCHLIST");
				lock (watchlist_lock)
				{
					WatchlistCache.Value.Sort(WatchlistSorter);

					if (token.IsCancellationRequested) return; // redo?

					int order = 0;
					foreach (var prc in WatchlistCache.Value)
						prc.ActualOrder = order++;

					NeedSort = false;
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

		public bool AddController(Controller prc)
		{
			if (isdisposed) throw new ObjectDisposedException(nameof(Manager), "AddController called when ProcessManager was already disposed");

			if (ValidateController(prc))
			{
				prc.Modified += ProcessModifiedProxy;
				//prc.Paused += ProcessPausedProxy;
				//prc.Resumed += ProcessResumedProxy;
				prc.Paused += ProcessWaitingExitProxy;
				prc.WaitingExit += ProcessWaitingExitProxy;

				SaveController(prc);

				prc.OnAdjust = OnControllerAdjust;

				return true;
			}

			return false;
		}

		void ProcessModifiedProxy(ModificationInfo mi) => ProcessModified?.Invoke(mi);

		void ProcessWaitingExitProxy(ProcessEx info)
		{
			try
			{
				WaitForExit(info);
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				var prc = info.Controller;
				Log.Error("Unregistering '" + prc.FriendlyName + "' exit wait proxy");
				prc.Paused -= ProcessWaitingExitProxy;
				prc.WaitingExit -= ProcessWaitingExitProxy;
			}
		}

		void ProcessResumedProxy(ProcessEx info)
		{
			throw new NotImplementedException();
		}

		void ProcessPausedProxy(ProcessEx info)
		{
			throw new NotImplementedException();
		}

		public void RemoveController(Controller prc)
		{
			if (prc.Executables.Length > 0)
			{
				// TODO: MULTIEXE ; What to do when multiple rules have same exe name?
				foreach (var exe in prc.ExecutableFriendlyName)
				{
					if (ExeToController.TryRemove(exe, out var list) && list.Length - 1 > 0)
					{
						var nlist = new List<Controller>(list);
						nlist.Remove(prc);
						ExeToController.TryAdd(exe, nlist.ToArray());
					}
				}
			}

			lock (watchlist_lock) RenewWatchlistCache();

			prc.Modified -= ProcessModified;
			prc.Paused -= ProcessPausedProxy;
			prc.Resumed -= ProcessResumedProxy;
		}

		public static void DeleteConfig(Controller prc)
		{
			using var cfg = Config.Load(WatchlistFile);
			cfg.Config.TryRemove(prc.FriendlyName); // remove the section, removes the items in the section
		}

		void WaitForExitTriggered(ProcessEx info)
		{
			Debug.Assert(info.Controller != null, "ProcessController not defined");
			Debug.Assert(!Utility.SystemProcessId(info.Id), "WaitForExitTriggered for system process");

			info.State = HandlingState.Exited;

			try
			{
				if (DebugForeground || DebugPower)
					Log.Debug(info.ToFullFormattedString() + " Process exited [Power: " + info.PowerWait + ", Active: " + info.ForegroundWait + "]");

				info.ForegroundWait = false;

				RemoveRunning(info.Id, out _);

				info.Controller?.End(info.Process, EventArgs.Empty);

				ProcessStateChange?.Invoke(info);
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		void PowerBehaviourEvent(object _, Power.PowerBehaviourEventArgs ea)
		{
			if (isdisposed) return;

			try
			{
				if (ea.Behaviour == Power.PowerBehaviour.Manual)
					CancelPowerWait();
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				Log.Error("Unregistering power behaviour event");
				powermanager.BehaviourChange -= PowerBehaviourEvent;
			}
		}

		public void CancelPowerWait()
		{
			var cancelled = 0;

			if (WaitForExitList.IsEmpty) return;

			var clearList = new Stack<ProcessEx>();
			foreach (var info in WaitForExitList.Values)
			{
				if (info.PowerWait)
				{
					info.PowerWait = false;

					// don't clear if we're still waiting for foreground
					if (!info.ForegroundWait)
					{
						try
						{
							info.Process.EnableRaisingEvents = false;
						}
						catch { /* nope, this throwing just verifies we're doing the right thing */ }

						clearList.Push(info);
						cancelled++;
					}
				}
			}

			while (clearList.Count > 0)
				WaitForExitTriggered(clearList.Pop());

			if (cancelled > 0)
				Log.Information("Cancelled power mode wait on " + cancelled.ToString(CultureInfo.InvariantCulture) + " process(es).");
		}

		bool WaitForExit(ProcessEx info)
		{
			Debug.Assert(info.Controller != null, "No controller attached");

			bool exithooked = false;

			if (isdisposed) throw new ObjectDisposedException(nameof(Manager), "WaitForExit called when ProcessManager was already disposed");

			if (WaitForExitList.TryAdd(info.Id, info))
			{
				AddRunning(info);

				try
				{
					info.Process.Exited += (_, _2) => WaitForExitTriggered(info);
					info.HookExit();

					// TODO: Just in case check if it exited while we were doing this.
					exithooked = true;

					info.Process.Refresh();
					if (info.Process.HasExited)
					{
						info.State = HandlingState.Exited;
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

				if (exithooked) ProcessStateChange?.Invoke(info);
			}

			return exithooked;
		}

		Controller? PreviousForegroundController;
		ProcessEx? PreviousForegroundInfo;

		// BUG: This is a mess.
		void ForegroundAppChangedEvent(object _sender, WindowChangedArgs ev)
		{
			if (isdisposed) return;

			System.Diagnostics.Process process = ev.Process;
			try
			{
				if (DebugForeground) Log.Verbose("<Process> Foreground Received: #" + ev.Id.ToString(CultureInfo.InvariantCulture));

				if (PreviousForegroundInfo != null)
				{
					if (PreviousForegroundInfo.Id != ev.Id) // testing previous to current might be superfluous
					{
						if (PreviousForegroundController != null)
						{
							//Log.Debug("PUTTING PREVIOUS FOREGROUND APP to BACKGROUND");
							if (PreviousForegroundController.Foreground != ForegroundMode.Ignore)
								PreviousForegroundController.SetBackground(PreviousForegroundInfo);

							ProcessStateChange?.Invoke(PreviousForegroundInfo);
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
						if (Trace && DebugForeground) Log.Debug(info.ToFullFormattedString() + " Process on foreground!");

						if (prc.Foreground != ForegroundMode.Ignore) prc.SetForeground(info);

						ProcessStateChange?.Invoke(info);

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
		}

		// TODO: ADD CACHE: pid -> process name, path, process

		public bool GetController(ProcessEx info, out Controller prc)
		{
			if (isdisposed) return (prc = null) != null; // silly shorthand

			if (info.Controller != null)
			{
				prc = info.Controller;
				return true;
			}

			try
			{
				info.Process.Refresh();
				if (info.Process.HasExited) // can throw
				{
					info.State = HandlingState.Exited;
					if (ShowInaction && DebugProcesses) Log.Verbose(info.ToFullFormattedString() + " has already exited.");
					prc = null;
					return false; // return ProcessState.Invalid;
				}
			}
			catch (InvalidOperationException ex)
			{
				Log.Fatal("INVALID ACCESS to Process");
				Logging.Stacktrace(ex);
				prc = null;
				return false; // return ProcessState.AccessDenied; //throw; // no point throwing
			}
			catch (System.ComponentModel.Win32Exception ex)
			{
				info.Restricted = true;
				if (ex.NativeErrorCode != 5) // what was this?
					Log.Warning("Access error: " + info);
				prc = null;
				return false; // return ProcessState.AccessDenied; // we don't care wwhat this error is
			}

			bool hasPath = !string.IsNullOrEmpty(info.Path);

			if (!hasPath) hasPath = Utility.FindPath(info);

			if (hasPath && IgnoreSystem32Path && info.Path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.System), StringComparison.InvariantCultureIgnoreCase))
			{
				if (ShowInaction && DebugProcesses) Log.Debug("<Process/Path> " + info + " in System32, ignoring");
				prc = null;
				return false;
			}

			List<Controller> lcache;

			lock (watchlist_lock) lcache = WatchlistCache.Value;

			//RenewWatchlistCache(); // this doesn't need to be done every time
			//lcache = WatchlistCache.Value;
			if (NeedSort) SortWatchlist();

			// TODO: This needs to be FASTER
			// Can't parallelize...
			foreach (var lprc in lcache)
			{
				if (!lprc.Enabled) continue;

				if (lprc.ExecutableFriendlyName.Length > 0)
				{
					string name = info.Name;
					if (!lprc.ExecutableFriendlyName.Any((x) => x.Equals(name, StringComparison.InvariantCultureIgnoreCase)))
						continue;
				}

				if (lprc.Path?.Length > 0)
				{
					if (!hasPath || !info.Path.StartsWith(lprc.Path, StringComparison.InvariantCultureIgnoreCase))
						continue;
				}

				info.Controller = prc = lprc;
				return true;
			}

			prc = null;
			return false;
		}

		public bool EnableParentFinding { get; set; }

		public string[] ProtectList { get; } = {
			"conhost",
			"runtimebroker", // win10 only
			"applicationframehost", // win10 only
			"lsass",
			"ctfmon",
			"audiodg",
			"winlogon",
			"wininit",
			"svchost", // service host
			"csrss",
			"consent",
			"services",
			"ntoskrnl",
			"taskeng", // task scheduler
			"consent", // UAC, user account control prompt
			"conhost",
			"ctfmon",
			"csrss", // client server runtime, subsystems
			"lsass",
			"taskmgr", // task manager
			"taskhost", // task scheduler process host
			"rundll32", //
			"dllhost", //
			//"conhost", // console host, hosts command prompts (cmd.exe)
			"dwm", // desktop window manager
			"wininit", // core system
			"winlogon", // core system
			"services", // service control manager
			"LogonUI", // session lock
		};

		/// <summary>
		/// User controlled user list.
		/// </summary>
		public string[] IgnoreList { get; private set; } = {
			"explorer", // file manager
			"audiodg" // audio device isolation
		};

		// %SYSTEMROOT%\System32 (Environment.SpecialFolder.System)
		public bool IgnoreSystem32Path { get; private set; } = true;

		/// <summary>
		/// Amount of protection to do.
		/// 1 = Priority denied.
		/// 2 = Affinity & Priority denied.
		/// </summary>
		public int ProtectionLevel { get; private set; } = 2;

		bool DebugWMI;

		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		public bool IgnoreProcessID(int pid) => Utility.SystemProcessId(pid) || IgnorePids.ContainsKey(pid);

		// BUG: Flat process name matching is not great.
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		public bool IgnoreProcessName(string name) => IgnoreList.Any(item => item.Equals(name, StringComparison.InvariantCultureIgnoreCase));

		// BUG: Flat process name matching is not great.
		public bool ProtectedProcessName(string name) => ProtectList.Any(item => item.Equals(name, StringComparison.InvariantCultureIgnoreCase));
		// %SYSTEMROOT%

		public bool ProtectedProcess(string name, string path)
		{
			// Assume lack of path means it belongs in systemroot. Not great assumption, but we probably can't touch it anyway.
			if (path?.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.InvariantCultureIgnoreCase) ?? true)
				return ProtectedProcessName(name);
			return false;
		}

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
			if (isdisposed) throw new ObjectDisposedException(nameof(Manager), "ForegroundWatch called when ProcessManager was already disposed");

			var prc = info.Controller;

			await Task.Delay(10).ConfigureAwait(false);

			Debug.Assert(prc.Foreground != ForegroundMode.Ignore);

			info.ForegroundWait = true;

			bool keyadded = WaitForExit(info);

			if (Trace && DebugForeground)
				Log.Debug(info.ToFullFormattedString() + " " + (!keyadded ? "Already in" : "Added to") + " foreground watchlist.");

			ProcessStateChange?.Invoke(info);
		}

		/// <summary>
		/// Seconds.
		/// </summary>
		double MinRunningTimeForTracking = 30d;

		// TODO: This should probably be pushed into ProcessController somehow.
		async Task ProcessTriage(ProcessEx info, bool old = false)
		{
			if (isdisposed) throw new ObjectDisposedException(nameof(Manager), "ProcessTriage called when ProcessManager was already disposed");

			await Task.Delay(10, cts.Token).ConfigureAwait(false); // asyncify
			if (cts.IsCancellationRequested)
			{
				info.State = HandlingState.Abandoned;
				return;
			}

			try
			{
				var time = info.Process.StartTime.ToUniversalTime();
				var ago = time.To(DateTime.UtcNow);
				if (Trace) Logging.DebugMsg($"<Process:Triage> {info} – Started: {info.Process.StartTime:g} ({ago:g} ago)");

				// Add to tracking if it's not already there, but only if it has been running for X minutes
				if (!info.ExitWait && ago.TotalSeconds >= MinRunningTimeForTracking) AddRunning(info);
			}
			catch // no access to startime
			{
				info.Restricted = true;
				if (DebugProcesses) Logging.DebugMsg("<Process:Triage>" + info.ToString() + " – NO ACCESS");
			}

			try
			{
				info.State = HandlingState.Triage;

				HandlingStateChange?.Invoke(this, new HandlingStateChangeEventArgs(info));

				if (string.IsNullOrEmpty(info.Name))
				{
					Log.Warning($"<Process:Triage> {info} – Details innaccessible; Ignored.");
					info.State = HandlingState.AccessDenied;
					return; // ProcessState.AccessDenied;
				}

				if (GetController(info, out var prc))
				{
					if (!prc.Enabled)
					{
						if (DebugProcesses) Log.Debug("[" + prc.FriendlyName + "] Matched, but rule disabled; ignoring.");
						info.State = HandlingState.Abandoned;
						return;
					}

					await Task.Delay(10, cts.Token).ConfigureAwait(false); // asyncify again

					if (cts.IsCancellationRequested)
					{
						info.State = HandlingState.Abandoned;
						return;
					}

					info.State = HandlingState.Processing;

					if (prc.LogDescription)
					{
						try
						{
							info.Description = info.Process.MainModule.FileVersionInfo.FileDescription;
						}
						catch { } // ignore
					}

					if (!old)
					{
						if (prc.LogStartAndExit)
						{
							var str = info.ToFullFormattedString() + " started.";

							if (prc.Warn)
								Log.Warning(str);
							else
								Log.Information(str);

							info.Process.Exited += (_, _2) =>
							{
								var str = info.ToFullFormattedString() + " Exited (run time: " + info.Start.To(DateTimeOffset.UtcNow).ToString("g", CultureInfo.InvariantCulture) + ").";
								if (prc.Warn)
									Log.Warning(str);
								else
									Log.Information(str);
							};
							info.HookExit();
							// TOOD: What if the process exited just before we enabled raising for the events?
						}
						else if (prc.Warn)
						{
							Log.Warning(info.ToFullFormattedString() + " Started.");
						}
					}

					try
					{
						if (prc.Ignored(info))
						{
							if (ShowInaction && Manager.DebugProcesses)
								Log.Debug(info.ToFullFormattedString() + " Ignored due to user defined rule.");
							info.State = HandlingState.Invalid;
							return;
						}

						if (info.Restricted)
						{
							if (DebugProcesses) Logging.DebugMsg(info.ToFullFormattedString() + " Triage: RESTRICTED; Cancelling");
							info.State = HandlingState.Invalid;
							return;
						}

						if (Trace && DebugProcesses) Logging.DebugMsg(info.ToFullFormattedString() + " Triage: Trying to modify.");

						await prc.Modify(info).ConfigureAwait(false);

						if (prc.Foreground != ForegroundMode.Ignore) await ForegroundWatch(info).ConfigureAwait(false);

						if (RecordAnalysis.HasValue && info.Controller.Analyze && info.Valid && info.State != HandlingState.Abandoned)
							analyzer?.Analyze(info).ConfigureAwait(false);

						//if (ColorResetEnabled && prc.ColorReset)
						//	await RegisterColorReset(info).ConfigureAwait(false);

						if (info.State == HandlingState.Processing)
						{
							Logging.DebugMsg(info.ToFullFormattedString() + " Correcting state to Finished. This should not happen.");
							info.State = HandlingState.Finished;
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

					if (info.State == HandlingState.Triage) info.State = HandlingState.Finished;
				}
				else
				{
					info.State = HandlingState.Abandoned;
					if (Trace && DebugProcesses) Logging.DebugMsg("<Process:Triage> " + info + " has no matching rule.");
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

		public async Task RegisterColorReset(ProcessEx info)
		{
			Debug.Assert(ColorResetEnabled, "Trying to do color reset when it's disabled.");

			await Task.Delay(10).ConfigureAwait(false); // asyncify

			if (!WaitForExit(info))
			{
				info.Process.Exited += (_, _2) => AttemptColorReset(info);
				info.HookExit();

				info.Process.Refresh();
				if (info.Process.HasExited && info.ColorReset)
					AttemptColorReset(info);
			}
		}

		async void AttemptColorReset(ProcessEx info)
		{
			await Task.Delay(10).ConfigureAwait(false);

			Log.Information(info.ToFullFormattedString() + " Exited; Resetting color (NOT REALLY, SORRY!).");
			return;

			/*
			var buffer = new StringBuilder(4096);

			IntPtr hdc = IntPtr.Zero; // hardware device context

			bool got = Taskmaster.NativeMethods.GetICMProfile(hdc, Convert.ToUInt64(buffer.Capacity) + 1UL, buffer);
			*/
		}

		public int HandlingCount => Handling;

		private int Handling; // this isn't used for much...

		/*
		/// <summary>
		/// Triage process exit events.
		/// </summary>
		void ProcessEndTriage(object sender, EventArrivedEventArgs ea)
		{
			try
			{
				var targetInstance = ea.NewEvent.Properties["TargetInstance"].Value as ManagementBaseObject;
				//var tpid = targetInstance.Properties["Handle"].Value as int?; // doesn't work for some reason

				int pid = Convert.ToInt32(targetInstance.Properties["Handle"].Value as string);

				if (!Utility.SystemProcessId(pid)) ScanBlockList.TryRemove(pid, out _);
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				return;
			}
		}
		*/

		/*
		void StartTraceTriage(object sender, EventArrivedEventArgs e)
		{
			//var now = DateTimeOffset.UtcNow;
			var timer = Stopwatch.StartNew();

			var targetInstance = e.NewEvent;
			int pid = 0;
			int ppid = 0;
			string name = string.Empty;

			try
			{
				pid = Convert.ToInt32(targetInstance.Properties["ProcessID"].Value as string);
				ppid = Convert.ToInt32(targetInstance.Properties["ParentProcessID"].Value as string);
				name = targetInstance.Properties["ProcessName"].Value as string;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				return;
			}
			finally
			{
				Statistics.WMIPollTime += timer.Elapsed.TotalSeconds;
				Statistics.WMIPolling++;
			}

			if (DebugProcesses) Log.Debug($"<Process> {name} #{pid}; parent #{ppid}");
		}
		*/

		// This needs to return faster
		async void NewInstanceTriage(object _, EventArrivedEventArgs ea)
		{
			var now = DateTimeOffset.UtcNow;
			var timer = Stopwatch.StartNew();

			System.Threading.Interlocked.Increment(ref Handling);

			int pid = -1;
			string name = string.Empty, path = string.Empty;
			ProcessEx info = null;
			DateTime creation = DateTime.MinValue;
			TimeSpan wmidelay = TimeSpan.Zero;

			HandlingState state = HandlingState.Invalid;

			await Task.Delay(10).ConfigureAwait(false);

			try
			{
				//var wmiquerytime = Stopwatch.StartNew(); // unused
				// TODO: Instance groups?
				try
				{
					using var targetInstance = ea.NewEvent.Properties["TargetInstance"].Value as ManagementBaseObject;
					//var tpid = targetInstance.Properties["Handle"].Value as int?; // doesn't work for some reason
					pid = Convert.ToInt32(targetInstance.Properties["Handle"].Value as string, CultureInfo.InvariantCulture);

					string iname = targetInstance.Properties[Application.Constants.Name].Value as string;
					path = targetInstance.Properties["ExecutablePath"].Value as string;
					if (DebugAdjustDelay && targetInstance.Properties["CreationDate"].Value is string cdate && !string.IsNullOrEmpty(cdate))
						creation = ManagementDateTimeConverter.ToDateTime(cdate);

					string cmdl = string.Empty;
					if (string.IsNullOrEmpty(path))
						cmdl = targetInstance.Properties["CommandLine"].Value as string; // CommandLine sometimes has the path when executablepath does not

					ScanBlockList.TryAdd(pid, DateTimeOffset.UtcNow);

					name = System.IO.Path.GetFileNameWithoutExtension(iname);
					if (!string.IsNullOrEmpty(cmdl))
					{
						int off = 0;
						string npath;
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
					Logging.DebugMsg("<Process> NewInstanceTriage failed for #" + pid + " - Exception: " + ex.Message);
					Logging.Stacktrace(ex);
					state = HandlingState.Invalid;
					return;
				}
				finally
				{
					Statistics.WMIPollTime += timer.Elapsed.TotalSeconds;
					Statistics.WMIPolling++;
				}

				if (DebugAdjustDelay && creation != DateTime.MinValue)
				{
					wmidelay = new DateTimeOffset(creation.ToUniversalTime()).To(now);
					if (Trace) Logging.DebugMsg($"WMI delay #{pid}: {wmidelay.TotalMilliseconds:N0} ms");
				}

				if (IgnoreProcessID(pid)) return; // We just don't care

				System.Diagnostics.Process proc = null;
				if (string.IsNullOrEmpty(name))
				{
					if (!string.IsNullOrEmpty(path))
						name = System.IO.Path.GetFileNameWithoutExtension(path);
					else
					{
						try
						{
							proc = System.Diagnostics.Process.GetProcessById(pid);
							// This happens only when encountering a process with elevated privileges, e.g. admin
							// TODO: Mark as admin process?
							name = proc.ProcessName;
						}
						catch (OutOfMemoryException) { throw; }
						catch
						{
							// already exited?
							if (DebugProcesses) Log.Error("<Process> Failed to retrieve name of process #" + pid.ToString(CultureInfo.InvariantCulture) + "; Process likely already gone.");
							state = HandlingState.Invalid;
							proc?.Dispose();
							return;
						}
					}
				}

				if (Trace) Logging.DebugMsg($"NewInstanceTriage: {name} #{pid}");

				if (IgnoreProcessName(name))
				{
					if (ShowInaction && DebugProcesses)
						Log.Debug($"<Process> {name} #{pid} ignored due to its name.");
					return;
				}

				if (Utility.Construct(pid, out info, process: proc, path: path, getPath: true, name: name))
				{
					RemoveRunning(pid, out var _);

					info.Timer = timer;
					info.PriorityProtected = ProtectedProcess(info.Name, info.Path);
					info.AffinityProtected = (info.PriorityProtected && ProtectionLevel == 2);
					info.FullyProtected = (info.PriorityProtected && ProtectionLevel == 2);

					info.WMIDelay = wmidelay;

					if (cts.IsCancellationRequested) throw new ObjectDisposedException(nameof(Manager));

					if (Trace) Log.Verbose("Caught: " + info + " at: " + info.Path);

					state = info.State = HandlingState.Triage;

					await ProcessTriage(info).ConfigureAwait(false);
				}
				else
				{
					if (ShowInaction && DebugProcesses)
						Log.Debug($"<Process> {name} #{pid} could not be mined for info.");
				}
			}
			catch (ArgumentException)
			{
				state = HandlingState.Exited;
				if (info != null) info.State = state;
				if (ShowInaction && DebugProcesses) Log.Verbose("Caught #" + pid.ToString(CultureInfo.InvariantCulture) + " but it vanished.");
			}
			catch (Exception ex)
			{
				state = HandlingState.Invalid;
				if (info != null) info.State = state;
				Logging.Stacktrace(ex);
				Log.Error("Unregistering new instance triage");
				StopWMIEventWatcher();
				timer?.Stop();
			}
			finally
			{
				System.Threading.Interlocked.Decrement(ref Handling);

				if (info is null) info = new ProcessEx(pid, now) { Timer = timer, State = state, WMIDelay = wmidelay };
				HandlingStateChange?.Invoke(this, new HandlingStateChangeEventArgs(info));
			}
		}

		/*
		Lazy<System.Collections.Specialized.StringCollection> startTraceItems = new Lazy<System.Collections.Specialized.StringCollection>(MakeTraceItemList);
		System.Collections.Specialized.StringCollection MakeTraceItemList()
			=> new System.Collections.Specialized.StringCollection { "TargetInstance.SourceName", "ProcessID", "ParentProcessID", "ProcessName" };
		*/

		readonly ManagementEventWatcher NewProcessWatcher;
		//ManagementEventWatcher ProcessEndWatcher = null;

		const string WMIEventNamespace = @"\\.\root\CIMV2";

		void StopWMIEventWatcher()
		{
			try
			{
				NewProcessWatcher.EventArrived -= NewInstanceTriage;
				NewProcessWatcher.Stop();
				NewProcessWatcher.Dispose(); // throws if WMI service is acting up
			}
			catch
			{
				Log.Error("<<WMI>> Error stopping event watcher, WMI service misbehaving?");
			}
		}

		public const string WatchlistFile = "Watchlist.ini";

		async void CleanupTick(object _sender, System.Timers.ElapsedEventArgs _2)
		{
			if (isdisposed) throw new ObjectDisposedException(nameof(Manager), "CleanupTick called when ProcessManager was already disposed");

			await Task.Delay(10).ConfigureAwait(false);

			Cleanup().ConfigureAwait(false);

			SortWatchlist();

			var now = DateTimeOffset.UtcNow;
			foreach (var item in from item in ScanBlockList
								 where item.Value.To(now).TotalSeconds > 5d
								 select item)
				ScanBlockList.TryRemove(item.Key, out _);
		}

		int cleanup_lock = Atomic.Unlocked;

		/// <summary>
		/// Cleanup.
		/// </summary>
		/// <remarks>
		/// Locks: waitforexit_lock
		/// </remarks>
		public async Task Cleanup()
		{
			if (isdisposed) throw new ObjectDisposedException(nameof(Manager), "Cleanup called when ProcessManager was already disposed");

			if (!MKAh.Synchronize.Atomic.Lock(ref cleanup_lock)) return; // already in progress

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
							info.State = HandlingState.Exited;
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
						prc.ResetInvalid();
				}

				Parallel.ForEach(Running.Values, info =>
				{
					try
					{
						//info.Process.Refresh();
						//if (info.Process.HasExited)
						if (info.Exited)
						{
							//info.State = HandlingState.Exited;
						}
					}
					catch { /* don't care */ }
				});
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				MKAh.Synchronize.Atomic.Unlock(ref cleanup_lock);
				triggerList?.Clear();
			}
		}

		#region IDisposable Support
		public event EventHandler<DisposedEventArgs>? OnDisposed;

		readonly CancellationTokenSource cts = new CancellationTokenSource();

		bool isdisposed;

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (isdisposed) return;
			isdisposed = true;

			if (disposing)
			{
				if (Trace) Log.Verbose("Disposing process manager...");

				cts.Cancel(true);

				ScanStart = null;
				ScanEnd = null;

				ProcessModified = null;
				ProcessStateChange = null;
				HandlingStateChange = null;

				if (powermanager != null)
				{
					powermanager.BehaviourChange -= PowerBehaviourEvent;
					powermanager = null;
				}

				try
				{
					StopWMIEventWatcher();

					if (activeappmonitor != null)
					{
						activeappmonitor.ActiveChanged -= ForegroundAppChangedEvent;
						activeappmonitor = null;
					}

					ScanTimer.Dispose();
					MaintenanceTimer.Dispose();
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
					// throw; // would throw but this is dispose
				}

				try
				{
					ExeToController.Clear();

					using var wcfg = Config.Load(WatchlistFile);
					foreach (var prc in Watchlist.Keys)
					{
						try
						{
							if (prc.NeedsSaving) prc.SaveConfig(wcfg.File);
							prc.Dispose();
						}
						catch (Exception ex)
						{
							Logging.Stacktrace(ex);
						}
					}

					lock (watchlist_lock)
					{
						Watchlist.Clear();

						if (WatchlistCache.IsValueCreated) WatchlistCache.Value.Clear();
					}
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
					// throw; // would throw but this is dispose
				}

				CancelPowerWait();
				WaitForExitList.Clear();

				//base.Dispose();

				OnDisposed?.Invoke(this, DisposedEventArgs.Empty);
				OnDisposed = null;
			}
		}

		public void ShutdownEvent(object sender, EventArgs ea)
		{
			StopWMIEventWatcher();
			ScanTimer.Stop();
			MaintenanceTimer.Stop();
		}
		#endregion
	}
}