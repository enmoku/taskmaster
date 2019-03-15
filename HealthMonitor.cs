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
using System.Diagnostics;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using MKAh;
using Serilog;

namespace Taskmaster
{
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
	sealed public class HealthMonitor : IDisposable // Auto-Doc
	{
		//Dictionary<int, Problem> activeProblems = new Dictionary<int, Problem>();
		readonly Settings.HealthMonitor Settings = new Settings.HealthMonitor();

		readonly CancellationTokenSource cancellationSource = new CancellationTokenSource();
		readonly CancellationToken ct;

		// Hard Page Faults
		//PerformanceCounterWrapper PageFaults = new PerformanceCounterWrapper("Memory", "Page Faults/sec", null);
		PerformanceCounterWrapper PageInputs = null;

		// NVM
		PerformanceCounterWrapper SplitIO = new PerformanceCounterWrapper("LogicalDisk", "Split IO/sec", "_Total");
		PerformanceCounterWrapper NVMTransfers = new PerformanceCounterWrapper("LogicalDisk", "Disk Transfers/sec", "_Total");
		PerformanceCounterWrapper NVMQueue = new PerformanceCounterWrapper("PhysicalDisk", "Current Disk Queue Length", "_Total");

		PerformanceCounterWrapper NVMReadDelay = new PerformanceCounterWrapper("LogicalDisk", "Avg. Disk Sec/Read", "_Total");
		PerformanceCounterWrapper NVMWriteDelay = new PerformanceCounterWrapper("LogicalDisk", "Avg. Disk Sec/Write", "_Total");

		/*
		PerformanceCounterWrapper NetRetransmit = new PerformanceCounterWrapper("TCP", "Segments Retransmitted/sec", "_Total");
		PerformanceCounterWrapper NetConnFails = new PerformanceCounterWrapper("TCP", "Connection Failures", "_Total");
		PerformanceCounterWrapper NetConnReset = new PerformanceCounterWrapper("TCP", "Connections Reset", "_Total");
		*/

		public HealthReport Poll
		{
			get
			{
				if (DisposedOrDisposing) throw new ObjectDisposedException("Poll called after HealthManager is disposed.");

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
			ct = cancellationSource.Token;

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
				//PageInputs = new PerformanceCounterWrapper("Memory", "Page Inputs/sec", null);
			}
			catch (InvalidOperationException) // counter not found... admin only?
			{
			}

			if (Settings.MemLevel > 0 && Taskmaster.PagingEnabled)
				Log.Information($"<Auto-Doc> Memory auto-paging level: {Settings.MemLevel.ToString()} MB");

			if (Settings.LowDriveSpaceThreshold > 0)
				Log.Information($"<Auto-Doc> Disk space warning level: {Settings.LowDriveSpaceThreshold.ToString()} MB");

			HealthTimer = new System.Timers.Timer(Settings.Frequency.TotalMilliseconds);
			HealthTimer.Elapsed += TimerCheck;
			HealthTimer.Start();

			EmergencyTimer = new System.Timers.Timer(500);
			EmergencyTimer.Elapsed += EmergencyTick;

			if (Taskmaster.DebugHealth) Log.Information("<Auto-Doc> Component loaded");

			Taskmaster.DisposalChute.Push(this);
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

				double pressure = MemoryManager.Pressure;
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

		void LoadConfig()
		{
			var cfg = Taskmaster.Config.Load("Health.ini");
			bool modified = false, configdirty = false;

			var gensec = cfg.Config["General"];
			var settingFreqSetting = gensec.GetSetDefault("Frequency", 5, out modified);
			Settings.Frequency = TimeSpan.FromMinutes(settingFreqSetting.IntValue.Constrain(1, 60 * 24));
			if (modified) settingFreqSetting.Comment = "How often we check for anything. In minutes.";
			configdirty |= modified;

			var freememsec = cfg.Config["Free Memory"];
			//freememsec.Comment = "Attempt to free memory when available memory goes below a threshold.";

			var memLevelSetting = freememsec.GetSetDefault("Threshold", 1000, out modified);
			Settings.MemLevel = (ulong)memLevelSetting.IntValue;
			// MemLevel = MemLevel > 0 ? MemLevel.Constrain(1, 2000) : 0;
			if (modified) memLevelSetting.Comment = "When memory goes down to this level, we act.";
			configdirty |= modified;
			if (Settings.MemLevel > 0)
			{
				var memIgnFocusSetting = freememsec.GetSetDefault("Ignore foreground", true, out modified);
				Settings.MemIgnoreFocus = memIgnFocusSetting.BoolValue;
				if (modified) memIgnFocusSetting.Comment = "Foreground app is not touched, regardless of anything.";
				configdirty |= modified;

				var ignListSetting = freememsec.GetSetDefault("Ignore list", new string[] { }, out modified);
				Settings.IgnoreList = ignListSetting.Array;
				if (modified) ignListSetting.Comment = "List of apps that we don't touch regardless of anything.";
				configdirty |= modified;

				var memCooldownSetting = freememsec.GetSetDefault("Cooldown", 60, out modified);
				Settings.MemCooldown = memCooldownSetting.IntValue.Constrain(1, 180);
				if (modified) memCooldownSetting.Comment = "Don't do this again for this many minutes.";
				configdirty |= modified;
			}

			// SELF-MONITORING
			var selfsec = cfg.Config["Self"];
			var fatalErrThSetting = selfsec.GetSetDefault("Fatal error threshold", 10, out modified);
			Settings.FatalErrorThreshold = fatalErrThSetting.IntValue.Constrain(1, 30);
			if (modified) fatalErrThSetting.Comment = "Auto-exit once number of fatal errors reaches this. 10 is very generous default.";
			configdirty |= modified;

			var fatalLogSetting = selfsec.GetSetDefault("Fatal log size threshold", 10, out modified);
			Settings.FatalLogSizeThreshold = fatalLogSetting.IntValue.Constrain(1, 500);
			if (modified) fatalLogSetting.Comment = "Auto-exit if total log file size exceeds this. In megabytes.";
			configdirty |= modified;

			// NVM
			var nvmsec = cfg.Config["Non-Volatile Memory"];
			var lowDriveSetting = nvmsec.GetSetDefault("Low space threshold", 150, out modified);
			Settings.LowDriveSpaceThreshold = lowDriveSetting.IntValue.Constrain(0, 60000);
			if (modified) lowDriveSetting.Comment = "Warn about free space going below this. In megabytes. From 0 to 60000.";
			configdirty |= modified;

			if (configdirty) cfg.MarkDirty();
		}

		int HealthCheck_lock = 0;
		//async void TimerCheck(object state)
		async void TimerCheck(object _, EventArgs _ea)
		{
			if (DisposedOrDisposing) return; // HACK: dumbness with timers

			// skip if already running...
			// happens sometimes when the timer keeps running but not the code here
			if (!Atomic.Lock(ref HealthCheck_lock)) return;

			try
			{
				try
				{
					Task.WaitAll(new[] {
						CheckSystem(),
						CheckErrors(),
						CheckLogs(),
						CheckMemory(),
						CheckNVM()
					}, ct);
				}
				catch (Exception ex) { Logging.Stacktrace(ex); }
			}
			catch { throw; }
			finally
			{
				Atomic.Unlock(ref HealthCheck_lock);
			}
		}

		uint LastTick = uint.MinValue;
		async Task CheckSystem()
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException("CheckSystem called after HealthMonitor was disposed.");

			await Task.Delay(0).ConfigureAwait(false);

			uint ntick = NativeMethods.GetTickCount();

			if (LastTick > ntick)
				Log.Warning("<Health> kernel32.dll/GetTickCount() has wrapped around.");

			LastTick = ntick;
		}

		async Task CheckErrors()
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException("CheckErrors called after HealthMonitor was disposed.");

			await Task.Delay(0).ConfigureAwait(false);

			// TODO: Maybe make this errors within timeframe instead of total...?
			if (Statistics.FatalErrors >= Settings.FatalErrorThreshold)
			{
				Log.Fatal("<Auto-Doc> Fatal error count too high, exiting.");
				Taskmaster.UnifiedExit();
			}
		}

		async Task CheckLogs()
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException("CheckErrors called after HealthMonitor was disposed.");

			await Task.Delay(0).ConfigureAwait(false);

			long size = 0;

			var files = System.IO.Directory.GetFiles(Taskmaster.logpath, "*", System.IO.SearchOption.AllDirectories);
			foreach (var filename in files)
			{
				var fi = new System.IO.FileInfo(System.IO.Path.Combine(Taskmaster.logpath, filename));
				size += fi.Length;
			}

			if (size >= Settings.FatalLogSizeThreshold * 1_000_000)
			{
				Log.Fatal("<Auto-Doc> Log files exceeding allowed size, exiting.");
				Taskmaster.UnifiedExit();
			}
		}

		List<string> warnedDrives = new List<string>();

		DateTimeOffset LastDriveWarning = DateTimeOffset.MinValue;

		async Task CheckNVM()
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException("CheckErrors called after HealthMonitor was disposed.");

			await Task.Delay(0).ConfigureAwait(false);

			// TODO: Add defrag suggestion based on PerformanceCounterWrapper("LogicalDisk", "Split IO/sec", "_Total");

			var now = DateTimeOffset.UtcNow;
			if (now.TimeSince(LastDriveWarning).TotalHours >= 24)
				warnedDrives.Clear();

			foreach (var drive in System.IO.DriveInfo.GetDrives())
			{
				if (drive.IsReady)
				{
					if ((drive.AvailableFreeSpace / 1_000_000) < Settings.LowDriveSpaceThreshold)
					{
						if (warnedDrives.Contains(drive.Name)) continue;

						var sqrbi = new NativeMethods.SHQUERYRBINFO
						{
							cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.SHQUERYRBINFO))
						};

						uint hresult = NativeMethods.SHQueryRecycleBin(drive.Name, ref sqrbi);
						int error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
						long rbsize = sqrbi.i64Size;

						Log.Warning("<Auto-Doc> Low free space on " + drive.Name
							+ " (" + HumanInterface.ByteString(drive.AvailableFreeSpace) + "); recycle bin has: " + HumanInterface.ByteString(rbsize));

						warnedDrives.Add(drive.Name);
						LastDriveWarning = now;
					}
					else
					{
						warnedDrives.Remove(drive.Name);
					}
				}
			}

			// Empty Recycle Bin
			//uint flags = NativeMethods.SHERB_NOCONFIRMATION | NativeMethods.SHERB_NOPROGRESSUI | NativeMethods.SHERB_NOSOUND;
			//NativeMethods.SHEmptyRecycleBin(IntPtr.Zero, path, flags);
		}

		async Task CheckMemory()
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException("CheckErrors called after HealthMonitor was disposed.");

			await Task.Delay(0).ConfigureAwait(false);

			Debug.WriteLine("<<Auto-Doc>> Checking...");

			var now = DateTimeOffset.UtcNow;

			try
			{
				if (Settings.MemLevel > 0)
				{
					MemoryManager.Update();
					var memfreemb = MemoryManager.FreeBytes;

					if (memfreemb <= Settings.MemLevel)
					{
						var cooldown = now.TimeSince(MemFreeLast).TotalMinutes;
						MemFreeLast = now;

						if (cooldown >= Settings.MemCooldown && Taskmaster.PagingEnabled)
						{
							// The following should just call something in ProcessManager
							int ignorepid = -1;

							if (Settings.MemIgnoreFocus && Taskmaster.activeappmonitor != null)
							{
								var idle = User.IdleTime();
								if (idle.TotalMinutes <= 3d)
								{
									ignorepid = Taskmaster.activeappmonitor.Foreground;
									Log.Verbose("<Auto-Doc> Protecting foreground app (#" + ignorepid + ")");
								}
							}

							var sbs = new System.Text.StringBuilder();
							sbs.Append("<<Auto-Doc>> Free memory low [")
								.Append(HumanInterface.ByteString((long)memfreemb * 1_048_576, iec:true))
								.Append("], attempting to improve situation.");
							if (!ProcessManager.SystemProcessId(ignorepid))
								sbs.Append(" Ignoring foreground (#").Append(ignorepid).Append(").");

							Log.Warning(sbs.ToString());

							Taskmaster.processmanager?.FreeMemory(null, quiet: true, ignorePid: ignorepid);
						}

						if (MemoryManager.Pressure > 1.05d) // 105%
							EmergencyTimer.Enabled = true;
					}
					else if ((memfreemb * MemoryWarningThreshold) <= Settings.MemLevel)
					{
						if (!WarnedAboutLowMemory && now.TimeSince(LastMemoryWarning).TotalSeconds > MemoryWarningCooldown)
						{
							WarnedAboutLowMemory = true;
							LastMemoryWarning = now;

							Log.Warning("<Memory> Free memory fairly low: " + HumanInterface.ByteString((long)memfreemb * 1_048_576, iec:true));
						}
					}
					else
						WarnedAboutLowMemory = false;

					double pressure = MemoryManager.Pressure;
					if (pressure > 1d)
					{
						LastPressureEvent = now;
						PressureAlleviatedBlurp = false;

						if (!WarnedAboutMemoryPressure && now.TimeSince(LastPressureWarning).TotalSeconds > MemoryWarningCooldown)
						{
							double actualgoal = ((MemoryManager.Total * (pressure - 1d)) / 1_048_576);
							double freegoal = actualgoal + Math.Max(512d, MemoryManager.Total * 0.02 / 1_048_576); // 512 MB or 2% extra to give space for disk cache
							Debug.WriteLine("Pressure:    " + $"{pressure * 100:N1} %");
							Debug.WriteLine("Actual goal: " + $"{actualgoal:N2}");
							Debug.WriteLine("Stated goal: " + $"{freegoal:N2}");
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
								Log.Information("<Memory> Pressure alleviated (" + $"{pressure * 100:N1} %" + ").");
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

		bool DisposedOrDisposing; // = false;
		public void Dispose() => Dispose(true);

		void Dispose(bool disposing)
		{
			if (DisposedOrDisposing) return;

			if (disposing)
			{
				DisposedOrDisposing = true;

				if (Taskmaster.Trace) Log.Verbose("Disposing health monitor...");

				cancellationSource.Cancel();

				HealthTimer?.Dispose();
				EmergencyTimer?.Dispose();

				//commitbytes?.Dispose();
				//commitbytes = null;
				//commitlimit?.Dispose();
				//commitlimit = null;
				//commitpercentile?.Dispose();
				//commitpercentile = null;
			}
		}
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