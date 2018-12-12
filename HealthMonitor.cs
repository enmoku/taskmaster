//
// HealthMonitor.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2018 M.A.
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
using System.Management;
using System.Threading.Tasks;
using Serilog;

namespace Taskmaster
{
	[Serializable]
	sealed internal class HealthMonitorSettings
	{
		/// <summary>
		/// Scanning frequency.
		/// </summary>
		public int Frequency { get; set; } = 5 * 60;

		/// <summary>
		/// Free megabytes.
		/// </summary>
		public int MemLevel { get; set; } = 1000;

		/// <summary>
		/// Ignore foreground application.
		/// </summary>
		public bool MemIgnoreFocus { get; set; } = true;

		/// <summary>
		/// Ignore applications.
		/// </summary>
		public string[] IgnoreList { get; set; } = { };

		/// <summary>
		/// Cooldown in minutes before we attempt to do anything about low memory again.
		/// </summary>
		public int MemCooldown { get; set; } = 60;

		/// <summary>
		/// Fatal errors until we force exit.
		/// </summary>
		public int FatalErrorThreshold { get; set; } = 10;

		/// <summary>
		/// Log file total size at which we force exit.
		/// </summary>
		public int FatalLogSizeThreshold { get; set; } = 5;

		/// <summary>
		/// Low drive space threshold in megabytes.
		/// </summary>
		public long LowDriveSpaceThreshold { get; set; } = 150;
	}

	/// <summary>
	/// Monitors for variety of problems and reports on them.
	/// </summary>
	sealed public class HealthMonitor : IDisposable // Auto-Doc
	{
		//Dictionary<int, Problem> activeProblems = new Dictionary<int, Problem>();
		HealthMonitorSettings Settings = new HealthMonitorSettings();

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

			memfree = new PerformanceCounterWrapper("Memory", "Available MBytes", null);

			if (Settings.MemLevel > 0 && Taskmaster.PagingEnabled)
			{
				Log.Information("<Auto-Doc> Memory auto-paging level: {Level} MB", Settings.MemLevel);
			}

			if (Settings.LowDriveSpaceThreshold > 0)
			{
				Log.Information("<Auto-Doc> Disk space warning level: {Level} MB", Settings.LowDriveSpaceThreshold);
			}

			healthTimer = new System.Timers.Timer(Settings.Frequency * 60_000);
			healthTimer.Elapsed += TimerCheck;
			healthTimer.Start();

			if (Taskmaster.DebugHealth) Log.Information("<Auto-Doc> Component loaded");
		}

		readonly System.Timers.Timer healthTimer = null;

		DateTime MemFreeLast = DateTime.MinValue;

		void LoadConfig()
		{
			var cfg = Taskmaster.Config.Load("Health.ini");
			bool modified = false, configdirty = false;

			var gensec = cfg.Config["General"];
			Settings.Frequency = gensec.GetSetDefault("Frequency", 5, out modified).IntValue.Constrain(1, 60 * 24);
			gensec["Frequency"].Comment = "How often we check for anything. In minutes.";
			configdirty |= modified;

			var freememsec = cfg.Config["Free Memory"];
			freememsec.Comment = "Attempt to free memory when available memory goes below a threshold.";

			Settings.MemLevel = freememsec.GetSetDefault("Threshold", 1000, out modified).IntValue;
			// MemLevel = MemLevel > 0 ? MemLevel.Constrain(1, 2000) : 0;
			freememsec["Threshold"].Comment = "When memory goes down to this level, we act.";
			configdirty |= modified;
			if (Settings.MemLevel > 0)
			{
				Settings.MemIgnoreFocus = freememsec.GetSetDefault("Ignore foreground", true, out modified).BoolValue;
				freememsec["Ignore foreground"].Comment = "Foreground app is not touched, regardless of anything.";
				configdirty |= modified;

				Settings.IgnoreList = freememsec.GetSetDefault("Ignore list", new string[] { }, out modified).StringValueArray;
				freememsec["Ignore list"].Comment = "List of apps that we don't touch regardless of anything.";
				configdirty |= modified;

				Settings.MemCooldown = freememsec.GetSetDefault("Cooldown", 60, out modified).IntValue.Constrain(1, 180);
				freememsec["Cooldown"].Comment = "Don't do this again for this many minutes.";
			}

			// SELF-MONITORING
			var selfsec = cfg.Config["Self"];
			Settings.FatalErrorThreshold = selfsec.GetSetDefault("Fatal error threshold", 10, out modified).IntValue.Constrain(1, 30);
			selfsec["Fatal error threshold"].Comment = "Auto-exit once number of fatal errors reaches this. 10 is very generous default.";
			configdirty |= modified;

			Settings.FatalLogSizeThreshold = selfsec.GetSetDefault("Fatal log size threshold", 10, out modified).IntValue.Constrain(1, 500);
			selfsec["Fatal log size threshold"].Comment = "Auto-exit if total log file size exceeds this. In megabytes.";
			configdirty |= modified;

			// NVM
			var nvmsec = cfg.Config["Non-Volatile Memory"];
			Settings.LowDriveSpaceThreshold = nvmsec.GetSetDefault("Low space threshold", 150, out modified).IntValue.Constrain(0, 60000);
			nvmsec["Low space threshold"].Comment = "Warn about free space going below this. In megabytes. From 0 to 60000.";
			configdirty |= modified;

			if (configdirty) cfg.MarkDirty();
		}

		PerformanceCounterWrapper memfree = null;

		int HealthCheck_lock = 0;
		//async void TimerCheck(object state)
		async void TimerCheck(object sender, EventArgs ev)
		{
			// skip if already running...
			// happens sometimes when the timer keeps running but not the code here
			if (!Atomic.Lock(ref HealthCheck_lock)) return;

			try
			{
				try
				{
					await CheckErrors().ConfigureAwait(false);
					await CheckLogs().ConfigureAwait(false);
					await CheckMemory().ConfigureAwait(false);
					await CheckNVM().ConfigureAwait(false);
				}
				catch (Exception ex) { Logging.Stacktrace(ex); }
			}
			catch { throw; }
			finally
			{
				Atomic.Unlock(ref HealthCheck_lock);
			}
		}

		double Memory = double.NaN;
		public double TotalMemory()
		{
			if (!double.IsNaN(Memory)) return Memory;

			using (ManagementClass mc = new ManagementClass("Win32_ComputerSystem"))
			{
				using (var res = mc.GetInstances())
				{
					foreach (var item in res)
					{
						return (Memory = Convert.ToDouble(Convert.ToUInt64(item.Properties["TotalPhysicalMemory"].Value)) / 1048576d);
					}
				}
			}

			return double.NaN;
		}

		DateTime FreeMemory_last = DateTime.MinValue;
		float FreeMemory_cached = 0f;
		/// <summary>
		/// Free memory, in megabytes.
		/// </summary>
		public float FreeMemory()
		{
			// this might be pointless
			var now = DateTime.Now;
			if (now.TimeSince(FreeMemory_last).TotalSeconds > 2)
			{
				FreeMemory_last = now;
				return FreeMemory_cached = memfree.Value;
			}

			return FreeMemory_cached;
		}

		public void InvalidateFreeMemory()
		{
			FreeMemory_last = DateTime.MinValue;
		}

		async Task CheckErrors()
		{
			await Task.Delay(0).ConfigureAwait(false);

			// TODO: Maybe make this errors within timeframe instead of total...?
			if (Statistics.FatalErrors >= Settings.FatalErrorThreshold)
			{
				Log.Fatal("<Auto-Doc> Fatal error count too high, exiting.");
				Taskmaster.UnifiedExit();
			}
		}

		string logpath = System.IO.Path.Combine(Taskmaster.datapath, "Logs");
		async Task CheckLogs()
		{
			await Task.Delay(0).ConfigureAwait(false);

			long size = 0;

			var files = System.IO.Directory.GetFiles(logpath, "*", System.IO.SearchOption.AllDirectories);
			foreach (var filename in files)
			{
				var fi = new System.IO.FileInfo(System.IO.Path.Combine(logpath, filename));
				size += fi.Length;
			}

			if (size >= Settings.FatalLogSizeThreshold * 1_000_000)
			{
				Log.Fatal("<Auto-Doc> Log files exceeding allowed size, exiting.");
				Taskmaster.UnifiedExit();
			}
		}

		List<string> warnedDrives = new List<string>();

		DateTime LastDriveWarning = DateTime.MinValue;

		async Task CheckNVM()
		{
			await Task.Delay(0).ConfigureAwait(false);

			var now = DateTime.Now;
			if (now.TimeSince(LastDriveWarning).TotalHours >= 24)
				warnedDrives.Clear();

			foreach (var drive in System.IO.DriveInfo.GetDrives())
			{
				if (drive.IsReady)
				{
					//Log.Debug("<Auto-Doc> {Drive} free space: {Space}", drive.Name, HumanInterface.ByteString(drive.AvailableFreeSpace));

					if ((drive.AvailableFreeSpace / 1_000_000) < Settings.LowDriveSpaceThreshold)
					{
						if (warnedDrives.Contains(drive.Name)) continue;

						var sqrbi = new NativeMethods.SHQUERYRBINFO
						{
							cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.SHQUERYRBINFO))
						};

						//Console.WriteLine(drive.Name);
						uint hresult = NativeMethods.SHQueryRecycleBin(drive.Name, ref sqrbi);
						int error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
						//Console.WriteLine(hresult.ToString("X") + " --- error: " + error);
						//Console.WriteLine(sqrbi.i64NumItems);
						//Console.WriteLine(sqrbi.i64Size);
						long rbsize = sqrbi.i64Size;

						Log.Warning("<Auto-Doc> Low free space on {Drive} ({Space}); recycle bin has: {Recycle}",
							drive.Name, HumanInterface.ByteString(drive.AvailableFreeSpace), HumanInterface.ByteString(rbsize));

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
			await Task.Delay(0).ConfigureAwait(false);

			// Console.WriteLine("<<Auto-Doc>> Checking...");

			try
			{
				if (Settings.MemLevel > 0)
				{
					var memfreemb = FreeMemory();

					var now = DateTime.Now;

					if (memfreemb <= Settings.MemLevel)
					{
						var cooldown = now.TimeSince(MemFreeLast).TotalMinutes;
						MemFreeLast = now;

						if (cooldown >= Settings.MemCooldown && Taskmaster.PagingEnabled)
						{
							// The following should just call something in ProcessManager
							int ignorepid = -1;

							if (Settings.MemIgnoreFocus && Taskmaster.Components.activeappmonitor != null)
							{
								uint lastact = User.LastActive();
								if (lastact != uint.MinValue && User.IdleFor(lastact) <= (60 * 60 * 3))
								{
									ignorepid = Taskmaster.Components.activeappmonitor.Foreground;
									Log.Verbose("<Auto-Doc> Protecting foreground app (#{Id})", ignorepid);
								}
							}

							var sbs = new System.Text.StringBuilder();
							sbs.Append("<<Auto-Doc>> Free memory low [")
								.Append(HumanInterface.ByteString((long)(memfreemb * 1_000_000)))
								.Append("], attempting to improve situation.");
							if (!ProcessManager.SystemProcessId(ignorepid))
								sbs.Append(" Ignoring foreground (#").Append(ignorepid).Append(").");

							Log.Warning(sbs.ToString());

							Taskmaster.Components.processmanager?.FreeMemory(null, quiet: true, ignorePid: ignorepid);

							// sampled too soon, OS has had no significant time to swap out data

							// FreeMemory returns before the process is complete, so the following is done too soon.
							/*
							var memfreemb2 = memfree.Value; // MB

							Log.Information("<<Auto-Doc>> Free memory: {Memory} ({Change} change observed)",
								HumanInterface.ByteString((long)(memfreemb2 * 1_000_000)),
								//HumanInterface.ByteString((long)commitb2), HumanInterface.ByteString((long)commitlimitb),
								HumanInterface.ByteString((long)((memfreemb2 - memfreemb) * 1_000_000), positivesign: true));
							*/
						}
					}
					else if ((memfreemb * MemoryWarningThreshold) <= Settings.MemLevel)
					{
						if (!WarnedAboutLowMemory && now.TimeSince(LastMemoryWarning).TotalSeconds > MemoryWarningCooldown)
						{
							WarnedAboutLowMemory = true;
							LastMemoryWarning = now;

							Log.Warning("<Memory> Free memory fairly low: {Memory}",
								HumanInterface.ByteString((long)(memfreemb * 1_000_000)));
						}
					}
					else
					{
						WarnedAboutLowMemory = false;
					}
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		float MemoryWarningThreshold = 1.5f;
		bool WarnedAboutLowMemory = false;
		DateTime LastMemoryWarning = DateTime.MinValue;
		long MemoryWarningCooldown = 30;

		// VRAM  / GPU

		/// <summary>
		/// Returns maximum available VRAM in MB.
		/// </summary>
		/// <returns></returns>
		public int VRAM()
		{
			if (!Taskmaster.WMIQueries) return 0;

			int vram = 0;

			try
			{
				using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT AdapterRAM FROM Win32_VideoController"))
				{

					foreach (ManagementObject mo in searcher.Get())
					{
						var ram = mo["AdapterRAM"] as uint?;

						if (ram.HasValue)
						{
							vram = (int)(ram.Value / 1048576);
							break;
						}
					}
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}

			return vram;
		}

		bool disposed; // = false;
		public void Dispose()
		{
			Dispose(true);
		}

		void Dispose(bool disposing)
		{
			if (disposed) return;

			if (disposing)
			{
				if (Taskmaster.Trace) Log.Verbose("Disposing health monitor...");

				healthTimer?.Dispose();

				//commitbytes?.Dispose();
				//commitbytes = null;
				//commitlimit?.Dispose();
				//commitlimit = null;
				//commitpercentile?.Dispose();
				//commitpercentile = null;
				memfree?.Dispose();
				memfree = null;

				PerformanceCounterWrapper.Sensors.Clear();
			}

			disposed = true;
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