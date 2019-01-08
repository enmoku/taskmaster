﻿//
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
using System.Threading.Tasks;
using MKAh;
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
		public ulong MemLevel { get; set; } = 1000;

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

			if (Settings.MemLevel > 0 && Taskmaster.PagingEnabled)
			{
				Log.Information("<Auto-Doc> Memory auto-paging level: " + Settings.MemLevel + " MB");
			}

			if (Settings.LowDriveSpaceThreshold > 0)
			{
				Log.Information("<Auto-Doc> Disk space warning level: " + Settings.LowDriveSpaceThreshold + " MB");
			}

			healthTimer = new System.Threading.Timer(TimerCheck, null, 15_000, Settings.Frequency * 60_000);

			if (Taskmaster.DebugHealth) Log.Information("<Auto-Doc> Component loaded");

			Taskmaster.DisposalChute.Push(this);
		}

		readonly System.Threading.Timer healthTimer = null;

		DateTimeOffset MemFreeLast = DateTimeOffset.MinValue;

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

			Settings.MemLevel =(ulong)freememsec.GetSetDefault("Threshold", 1000, out modified).IntValue;
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

		int HealthCheck_lock = 0;
		//async void TimerCheck(object state)
		async void TimerCheck(object _)
		{
			// skip if already running...
			// happens sometimes when the timer keeps running but not the code here
			if (!Atomic.Lock(ref HealthCheck_lock)) return;
			if (disposed) return; // HACK: dumbness with timers

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

			// there's probably better way to do this?
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

		async Task CheckLogs()
		{
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
			await Task.Delay(0).ConfigureAwait(false);

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

					if (MemoryManager.Pressure > 1d)
					{
						if (!WarnedAboutMemoryPressure && now.TimeSince(LastPressureWarning).TotalSeconds > MemoryWarningCooldown)
						{
							Log.Warning("<Memory> Memory pressure is over 100%, please close applications to improve performance.");
							// TODO: Could list like ~5 apps that are using most memory here
							WarnedAboutMemoryPressure = true;
							LastPressureWarning = now;
						}
						else
						{
							// warned too recently, ignore for now
						}
					}
					else
						WarnedAboutMemoryPressure = false;
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		bool WarnedAboutMemoryPressure = false;
		DateTimeOffset LastPressureWarning = DateTimeOffset.MinValue;

		float MemoryWarningThreshold = 1.5f;
		bool WarnedAboutLowMemory = false;
		DateTimeOffset LastMemoryWarning = DateTimeOffset.MinValue;
		long MemoryWarningCooldown = 30;

		// VRAM  / GPU

		ulong vram_value { get; set; } = 0;
		/// <summary>
		/// Returns maximum available VRAM in MB.
		/// </summary>
		/// <returns></returns>
		public ulong VRAM()
		{
			if (!Taskmaster.WMIQueries) return 0;

			if (vram_value != 0) return vram_value;

			try
			{
				// apparently this maps to registry stuff:
				// https://docs.microsoft.com/en-us/windows-hardware/drivers/display/setting-hardware-information-in-the-registry
				// VideoPortGetRegistryParameters
				using (var searcher = new ManagementObjectSearcher(
					new ManagementScope(@"\\.\root\CIMV2"),
					new SelectQuery("SELECT AdapterRAM FROM Win32_VideoController"),
					new EnumerationOptions(null, new TimeSpan(0, 5, 0), 1, false, true, false, false, false, true, false)
					))
				{
					foreach (ManagementObject mo in searcher.Get())
					{
						var ram = mo["AdapterRAM"] as ulong?;

						if (ram.HasValue)
						{
							vram_value = ram.Value;
							break;
						}
					}
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}

			return vram_value;
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