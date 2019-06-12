//
// HealthMonitor.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2018–2019 M.A.
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
using System.Threading;
using System.Threading.Tasks;
using MKAh;
using Serilog;
using Windows = MKAh.Wrapper.Windows;

namespace Taskmaster
{
	using System.Text;
	using static Taskmaster;

	public sealed class HealthReport
	{
		// Counters
		public float PageFaults = 0f;
		public float PageInputs = 0f;
		public float SplitIO = 0f;
		public float NVMTransfers = 0f;
		public float NVMQueue = 0f;
		public float NVMDelay = 0f;

		// Memory
		public float MemPressure = 0f;
		public float MemUsage = 0f;
	}

	/// <summary>
	/// Monitors for variety of problems and reports on them.
	/// </summary>
	[Component(RequireMainThread = false)]
	public class HealthMonitor : Component, IDisposal, IDisposable // Auto-Doc
	{
		bool DebugHealth = false;

		//Dictionary<int, Problem> activeProblems = new Dictionary<int, Problem>();
		readonly Settings.HealthMonitor Settings = new Settings.HealthMonitor();

		readonly CancellationTokenSource cancellationSource = new CancellationTokenSource();

		// Hard Page Faults
		//Windows.PerformanceCounter PageFaults = new Windows.PerformanceCounter("Memory", "Page Faults/sec", null);
		readonly Windows.PerformanceCounter PageInputs = null;

		const string LogicalDiskName = "LogicalDisk";
		const string AllInstancesName = "_Total";

		// NVM
		readonly Windows.PerformanceCounter SplitIO = new Windows.PerformanceCounter(LogicalDiskName, "Split IO/sec", AllInstancesName);
		readonly Windows.PerformanceCounter NVMTransfers = new Windows.PerformanceCounter(LogicalDiskName, "Disk Transfers/sec", AllInstancesName);
		readonly Windows.PerformanceCounter NVMQueue = new Windows.PerformanceCounter("PhysicalDisk", "Current Disk Queue Length", AllInstancesName);

		readonly Windows.PerformanceCounter NVMReadDelay = new Windows.PerformanceCounter(LogicalDiskName, "Avg. Disk Sec/Read", AllInstancesName);
		readonly Windows.PerformanceCounter NVMWriteDelay = new Windows.PerformanceCounter(LogicalDiskName, "Avg. Disk Sec/Write", AllInstancesName);

		/*
		Windows.PerformanceCounter NetRetransmit = new Windows.PerformanceCounter("TCP", "Segments Retransmitted/sec", "_Total");
		Windows.PerformanceCounter NetConnFails = new Windows.PerformanceCounter("TCP", "Connection Failures", "_Total");
		Windows.PerformanceCounter NetConnReset = new Windows.PerformanceCounter("TCP", "Connections Reset", "_Total");
		*/

		public HealthReport Poll
		{
			get
			{
				if (DisposedOrDisposing) throw new ObjectDisposedException(nameof(HealthMonitor), "Poll called after HealthManager is disposed.");

				return new HealthReport()
				{
					//PageFaults = PageFaults.Value,
					//PageInputs = PageInputs?.Value ?? float.NaN,
					SplitIO = SplitIO.Value,
					NVMTransfers = NVMTransfers.Value,
					NVMQueue = NVMQueue.Value,
					NVMDelay = Math.Max(NVMReadDelay.Value, NVMWriteDelay.Value),

					//MemPressure = 0f,
					//MemUsage = 0f,
				};
			}
		}

		public HealthMonitor()
		{
			// TODO: Add different problems to monitor for

			// --------------------------------------------------------------------------------------------------------
			// What? : Fragmentation. Per drive.
			// How? : Keep reading disk access splits, warn when they exceed a threshold with sufficient samples or the split becomes excessive.
			// Use? : Recommend defrag.
			// -------------------------------------------------------------------------------------------------------

			// -------------------------------------------------------------------------------------------------------
			// What? : NVM performance. Per drive.
			// How? : Disk queue length. Disk delay.
			// Use? : Recommend reducing multitasking and/or moving the tasks to faster drive.
			// -------------------------------------------------------------------------------------------------------

			// -------------------------------------------------------------------------------------------------------
			// What? : CPU insufficiency.
			// How? : CPU instruction queue length.
			// Use? : Warn about CPU being insuffficiently scaled for the tasks being done.
			// -------------------------------------------------------------------------------------------------------

			// -------------------------------------------------------------------------------------------------------
			// What? : Free disk space
			// How? : Make sure drives have at least 4G free space. Possibly configurable.
			// Use? : Recommend disk cleanup and/or uninstalling unused apps.
			// Opt? : Auto-empty trash.
			// -------------------------------------------------------------------------------------------------------

			// -------------------------------------------------------------------------------------------------------
			// What? : Problematic apps
			// How? : Detect apps like TrustedInstaller, MakeCab, etc. running.
			// Use? : Inform user of high resource using background tasks that should be let to run.
			// .... Potentially inform how to temporarily mitigate the issue.
			// Opt? : Priority and affinity reduction.
			// --------------------------------------------------------------------------------------------------------

			// --------------------------------------------------------------------------------------------------------
			// What? : Driver crashes
			// How? : No idea.
			// Use? : Recommend driver up or downgrade.
			// Opt? : None. Analyze situation. This might've happened due to running out of memory.
			// --------------------------------------------------------------------------------------------------------

			// --------------------------------------------------------------------------------------------------------
			// What? : Underscaled network
			// How? : Network queue length
			// Use? : Recommend throttling network using apps.
			// Opt? : Check ECN state and recommend toggling it. Make sure CTCP is enabled.
			// --------------------------------------------------------------------------------------------------------

			// --------------------------------------------------------------------------------------------------------
			// What? : Background task taking too many resources.
			// How? : Monitor total CPU usage until it goes past certain threshold, check highest CPU usage app. ...
			// ... Check if the app is in foreground.
			// Use? : Warn about intense background tasks.
			// Opt? :
			// --------------------------------------------------------------------------------------------------------

			LoadConfig();

			try
			{
				//PageInputs = new Windows.PerformanceCounter("Memory", "Page Inputs/sec", null);
			}
			catch (InvalidOperationException) // counter not found... admin only?
			{
			}

			if (Settings.MemLevel > 0 && PagingEnabled)
				Log.Information($"<Auto-Doc> Memory auto-paging level: {Settings.MemLevel.ToString()} MB");

			if (Settings.LowDriveSpaceThreshold > 0)
				Log.Information($"<Auto-Doc> Disk space warning level: {Settings.LowDriveSpaceThreshold.ToString()} MB");

			HealthTimer = new System.Timers.Timer(Settings.Frequency.TotalMilliseconds);
			HealthTimer.Elapsed += TimerCheck;
			HealthTimer.Start();

			EmergencyTimer = new System.Timers.Timer(500);
			EmergencyTimer.Elapsed += EmergencyTick;

			if (DebugHealth) Log.Information("<Auto-Doc> Component loaded");

			RegisterForExit(this);
			DisposalChute.Push(this);
		}

		int EmergencyTick_lock = 0;
		int EmergencyPressure = -1;

		bool CriticalMemoryWarning = false;
		DateTimeOffset EmergencyOffset = DateTimeOffset.MinValue;
		private void EmergencyTick(object sender, System.Timers.ElapsedEventArgs e)
		{
			try
			{
				if (!Atomic.Lock(ref EmergencyTick_lock)) return;

				double pressure = Memory.Pressure;
				if (pressure > 1.10d) // 110%
				{
					if (EmergencyPressure < 0)
					{
						EmergencyPressure = 1;
						EmergencyOffset = DateTimeOffset.UtcNow;
					}
					else if (EmergencyOffset.TimeTo(DateTimeOffset.UtcNow).TotalSeconds >= 30)
					{
						// TODO: Take action

						if (!CriticalMemoryWarning)
						{
							Log.Warning("<Health> Free memory critically low. Please close applications to recover system responsiviness.");
							CriticalMemoryWarning = true;
						}
					}
				}
				else
				{
					// things look okay
					if (EmergencyPressure > 0)
					{
						EmergencyPressure = -1;
						EmergencyOffset = DateTimeOffset.UtcNow;
					}
					else if (EmergencyOffset.TimeTo(DateTimeOffset.UtcNow).TotalSeconds >= 15)
					{
						EmergencyTimer.Stop();
						CriticalMemoryWarning = false;
					}

					EmergencyPressure--;
				}
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				Atomic.Unlock(ref EmergencyTick_lock);
			}
		}

		readonly System.Timers.Timer HealthTimer = null;
		readonly System.Timers.Timer EmergencyTimer = null;

		DateTimeOffset MemFreeLast = DateTimeOffset.MinValue;

		const string HealthConfigFilename = "Health.ini";

		void LoadConfig()
		{
			using var cfg = Taskmaster.Config.Load(HealthConfigFilename);
			var gensec = cfg.Config["General"];
			var settingFreqSetting = gensec.GetOrSet("Frequency", 5)
				.InitComment("How often we check for anything. In minutes.")
				.Int.Constrain(1, 60 * 24);
			Settings.Frequency = TimeSpan.FromMinutes(settingFreqSetting);

			var freememsec = cfg.Config["Free Memory"];
			//freememsec.Comment = "Attempt to free memory when available memory goes below a threshold.";

			Settings.MemLevel = (ulong)freememsec.GetOrSet("Threshold", 1000)
				.InitComment("When memory goes down to this level, we act.")
				.Int;
			// MemLevel = MemLevel > 0 ? MemLevel.Constrain(1, 2000) : 0;

			if (Settings.MemLevel > 0)
			{
				Settings.MemIgnoreFocus = freememsec.GetOrSet("Ignore foreground", true)
					.InitComment("Foreground app is not touched, regardless of anything.")
					.Bool;

				Settings.IgnoreList = freememsec.GetOrSet("Ignore list", new string[] { })
					.InitComment("List of apps that we don't touch regardless of anything.")
					.Array;

				Settings.MemCooldown = freememsec.GetOrSet("Cooldown", 60)
					.InitComment("Don't do this again for this many minutes.")
					.Int.Constrain(1, 180);
			}

			// SELF-MONITORING
			var selfsec = cfg.Config["Self"];
			Settings.FatalErrorThreshold = selfsec.GetOrSet("Fatal error threshold", 10)
				.InitComment("Auto-exit once number of fatal errors reaches this. 10 is very generous default.")
				.Int.Constrain(1, 30);

			Settings.FatalLogSizeThreshold = selfsec.GetOrSet("Fatal log size threshold", 10)
				.InitComment("Auto-exit if total log file size exceeds this. In megabytes.")
				.Int.Constrain(1, 500);

			// NVM
			var nvmsec = cfg.Config["Non-Volatile Memory"];
			Settings.LowDriveSpaceThreshold = nvmsec.GetOrSet("Low space threshold", 150)
				.InitComment("Warn about free space going below this. In megabytes. From 0 to 60000.")
				.Int.Constrain(0, 60000);

			using var corecfg = Taskmaster.Config.Load(CoreConfigFilename);
			DebugHealth = corecfg.Config[HumanReadable.Generic.Debug].Get("Health")?.Bool ?? false;
		}

		int HealthCheck_lock = 0;
		//async void TimerCheck(object state)
		async void TimerCheck(object _, EventArgs _ea)
		{
			if (DisposedOrDisposing) return; // Dumbness with timers

			// skip if already running...
			// happens sometimes when the timer keeps running but not the code here
			if (!Atomic.Lock(ref HealthCheck_lock)) return;

			await Task.Delay(0).ConfigureAwait(false);

			try
			{
				if (cancellationSource.IsCancellationRequested) return;

				Task.WaitAll(new[] {
					CheckSystem(),
					CheckErrors(),
					CheckLogs(),
					CheckMemory(),
					CheckNVM()
				}, cancellationSource.Token);
			}
			catch (OperationCanceledException)
			{
				HealthTimer?.Stop();
			}
			catch (Exception ex) { Logging.Stacktrace(ex); }
			finally
			{
				Atomic.Unlock(ref HealthCheck_lock);
			}
		}

		uint LastTick = uint.MinValue;
		async Task CheckSystem()
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException(nameof(HealthMonitor), "CheckSystem called after HealthMonitor was disposed.");

			await Task.Delay(0).ConfigureAwait(false);

			uint ntick = MKAh.Native.GetTickCount();

			if (LastTick > ntick)
				Log.Warning("<Health> kernel32.dll/GetTickCount() has wrapped around.");

			LastTick = ntick;
		}

		async Task CheckErrors()
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException(nameof(HealthMonitor), "CheckErrors called after HealthMonitor was disposed.");

			await Task.Delay(0).ConfigureAwait(false);

			// TODO: Maybe make this errors within timeframe instead of total...?
			if (Statistics.FatalErrors >= Settings.FatalErrorThreshold)
			{
				Log.Fatal("<Auto-Doc> Fatal error count too high, exiting.");
				UnifiedExit();
			}
		}

		async Task CheckLogs()
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException(nameof(HealthMonitor), "CheckErrors called after HealthMonitor was disposed.");

			await Task.Delay(0).ConfigureAwait(false);

			long size = 0;

			var files = System.IO.Directory.GetFiles(LogPath, "*", System.IO.SearchOption.AllDirectories);
			foreach (var filename in files)
			{
				size += (new System.IO.FileInfo(System.IO.Path.Combine(LogPath, filename))).Length;
			}

			if (size >= Settings.FatalLogSizeThreshold * 1_000_000)
			{
				Log.Fatal("<Auto-Doc> Log files exceeding allowed size, exiting.");
				UnifiedExit();
			}
		}

		readonly List<string> WarnedDrives = new List<string>();

		DateTimeOffset LastDriveWarning = DateTimeOffset.MinValue;

		async Task CheckNVM()
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException(nameof(HealthMonitor), "CheckErrors called after HealthMonitor was disposed.");

			await Task.Delay(0).ConfigureAwait(false);

			// TODO: Add defrag suggestion based on Windows.PerformanceCounter("LogicalDisk", "Split IO/sec", "_Total");

			var now = DateTimeOffset.UtcNow;
			if (now.TimeSince(LastDriveWarning).TotalHours >= 24)
				WarnedDrives.Clear();

			foreach (var drive in System.IO.DriveInfo.GetDrives())
			{
				if (drive.IsReady)
				{
					if ((drive.AvailableFreeSpace / 1_000_000) < Settings.LowDriveSpaceThreshold)
					{
						if (WarnedDrives.Contains(drive.Name)) continue;

						var sqrbi = new NativeMethods.SHQUERYRBINFO
						{
							cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.SHQUERYRBINFO))
						};

						uint hresult = NativeMethods.SHQueryRecycleBin(drive.Name, ref sqrbi);
						int error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
						long rbsize = sqrbi.i64Size;

						Log.Warning("<Auto-Doc> Low free space on " + drive.Name
							+ " (" + HumanInterface.ByteString(drive.AvailableFreeSpace) + "); recycle bin has: " + HumanInterface.ByteString(rbsize));

						WarnedDrives.Add(drive.Name);
						LastDriveWarning = now;
					}
					else
					{
						WarnedDrives.Remove(drive.Name);
					}
				}
			}

			// Empty Recycle Bin
			//uint flags = NativeMethods.SHERB_NOCONFIRMATION | NativeMethods.SHERB_NOPROGRESSUI | NativeMethods.SHERB_NOSOUND;
			//NativeMethods.SHEmptyRecycleBin(IntPtr.Zero, path, flags);
		}

		async Task CheckMemory()
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException(nameof(HealthMonitor), "CheckErrors called after HealthMonitor was disposed.");

			await Task.Delay(0).ConfigureAwait(false);

			Logging.DebugMsg("<<Auto-Doc>> Checking...");

			var now = DateTimeOffset.UtcNow;

			try
			{
				if (Settings.MemLevel > 0)
				{
					Memory.Update();
					var memfreemb = Memory.FreeBytes;

					if (memfreemb <= Settings.MemLevel)
					{
						var cooldown = now.TimeSince(MemFreeLast).TotalMinutes;
						MemFreeLast = now;

						if (cooldown >= Settings.MemCooldown && PagingEnabled)
						{
							// The following should just call something in ProcessManager
							int ignorepid = -1;

							if (Settings.MemIgnoreFocus && activeappmonitor != null && User.IdleTime().TotalMinutes <= 3d)
							{
								ignorepid = activeappmonitor.ForegroundId;
								Log.Verbose("<Auto-Doc> Protecting foreground app (#" + ignorepid + ")");
							}

							var sbs = new StringBuilder()
								.Append("<<Auto-Doc>> Free memory low [")
								.Append(HumanInterface.ByteString((long)memfreemb * 1_048_576, iec: true))
								.Append("], attempting to improve situation.");
							if (!Process.Utility.SystemProcessId(ignorepid))
								sbs.Append(" Ignoring foreground (#").Append(ignorepid).Append(").");

							Log.Warning(sbs.ToString());

							processmanager?.FreeMemory(null, quiet: true, ignorePid: ignorepid);
						}

						if (Memory.Pressure > 1.05d) // 105%
							EmergencyTimer.Enabled = true;
					}
					else if ((memfreemb * MemoryWarningThreshold) <= Settings.MemLevel)
					{
						if (!WarnedAboutLowMemory && now.TimeSince(LastMemoryWarning).TotalSeconds > MemoryWarningCooldown)
						{
							WarnedAboutLowMemory = true;
							LastMemoryWarning = now;

							Log.Warning("<Memory> Free memory fairly low: " + HumanInterface.ByteString((long)memfreemb * 1_048_576, iec: true));
						}
					}
					else
						WarnedAboutLowMemory = false;

					double pressure = Memory.Pressure;
					if (pressure > 1d)
					{
						LastPressureEvent = now;
						PressureAlleviatedBlurp = false;

						if (!WarnedAboutMemoryPressure && now.TimeSince(LastPressureWarning).TotalSeconds > MemoryWarningCooldown)
						{
							double actualgoal = ((Memory.Total * (pressure - 1d)) / 1_048_576);
							double freegoal = actualgoal + Math.Max(512d, Memory.Total * 0.02 / 1_048_576); // 512 MB or 2% extra to give space for disk cache
							Logging.DebugMsg(
								"Pressure:    " + $"{pressure * 100:N1} %" + Environment.NewLine +
								"Actual goal: " + $"{actualgoal:N2}" + Environment.NewLine +
								"Stated goal: " + $"{freegoal:N2}");
							Log.Warning($"<Memory> High pressure ({pressure * 100:N1} %), please close applications to improve performance (suggested minimum goal: {freegoal:N0}" + " MiB).");
							// TODO: Could list like ~5 apps that are using most memory here
							WarnedAboutMemoryPressure = true;
							LastPressureWarning = LastPressureEvent;
						}
						else
						{
							// warned too recently, ignore for now
						}
					}
					else
					{
						if (LastPressureEvent.TimeTo(now).TotalMinutes > 3d)
						{
							WarnedAboutMemoryPressure = false;

							if (!PressureAlleviatedBlurp && pressure <= 0.95d)
							{
								Log.Information($"<Memory> Pressure alleviated (remaining: {pressure * 100:N1} %) – {memfreemb} MB free.");
								PressureAlleviatedBlurp = true;
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		bool WarnedAboutMemoryPressure = false, PressureAlleviatedBlurp = true;
		DateTimeOffset LastPressureWarning = DateTimeOffset.MinValue;
		DateTimeOffset LastPressureEvent = DateTimeOffset.MinValue;

		float MemoryWarningThreshold = 1.5f;
		bool WarnedAboutLowMemory = false;
		DateTimeOffset LastMemoryWarning = DateTimeOffset.MinValue;
		long MemoryWarningCooldown = 30;

		#region IDisposable Support
		public event EventHandler<DisposedEventArgs> OnDisposed;

		bool DisposedOrDisposing = false;

		protected override void Dispose(bool disposing)
		{
			if (DisposedOrDisposing) return;

			if (disposing)
			{
				DisposedOrDisposing = true;

				if (Trace) Log.Verbose("Disposing health monitor...");

				cancellationSource.Cancel();
				cancellationSource?.Dispose();

				HealthTimer?.Dispose();
				EmergencyTimer?.Dispose();

				// kinda pointless
				NVMQueue?.Dispose();
				NVMReadDelay?.Dispose();
				NVMTransfers?.Dispose();
				NVMWriteDelay?.Dispose();
				SplitIO?.Dispose();

				//commitbytes?.Dispose();
				//commitbytes = null;
				//commitlimit?.Dispose();
				//commitlimit = null;
				//commitpercentile?.Dispose();
				//commitpercentile = null;
			}

			OnDisposed?.Invoke(this, DisposedEventArgs.Empty);
			OnDisposed = null;
		}

		public void ShutdownEvent(object sender, EventArgs ea)
		{
			cancellationSource.Cancel();
			HealthTimer?.Stop();
			EmergencyTimer?.Stop();
		}
		#endregion
	}

	/*
	enum ProblemState
	{
		New,
		Interacted,
		Invalid,
		Dismissed
	}

	sealed class Problem
	{
		int Id;
		string Description;

		//
		DateTime Occurrence;

		// don't re-state the problem in this time
		TimeSpan Cooldown;

		// user actions on this
		bool Acknowledged;
		bool Dismissed;

		// State
		ProblemState State;
	}

	interface AutoDoc
	{
		int Hooks();

	}

	sealed class MemoryAutoDoc : AutoDoc
	{
		public int Hooks() => 0;

		public MemoryAutoDoc()
		{
		}
	}
	*/
}