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
using System.Threading.Tasks;
using Serilog;

namespace Taskmaster
{
	/// <summary>
	/// Monitors for variety of problems and reports on them.
	/// </summary>
	sealed public class HealthMonitor : IDisposable // Auto-Doc
	{
		//Dictionary<int, Problem> activeProblems = new Dictionary<int, Problem>();

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

			if (MemLevel > 0)
			{
				memfree = new PerformanceCounterWrapper("Memory", "Available MBytes", null);
				commitbytes = new PerformanceCounterWrapper("Memory", "Committed Bytes", null);
				commitlimit = new PerformanceCounterWrapper("Memory", "Commit Limit", null);
				commitpercentile = new PerformanceCounterWrapper("Memory", "% Committed Bytes in Use", null);

				Log.Information("<Auto-Doc> Memory auto-paging level: {Level} MB", MemLevel);
			}

			healthTimer = new System.Threading.Timer(TimerCheck, null, 5000, Frequency * 60 * 1000);

			Log.Information("<Auto-Doc> Loaded");
		}

		System.Threading.Timer healthTimer = null;

		int MemLevel = 1000;
		bool MemIgnoreFocus = true;
		string[] IgnoreList = { };
		int MemCooldown = 60;
		DateTime MemFreeLast = DateTime.MinValue;

		int Frequency = 5 * 60;

		SharpConfig.Configuration cfg = null;
		void LoadConfig()
		{
			cfg = Taskmaster.LoadConfig("Health.ini");
			bool modified = false, configdirty = false;

			var gensec = cfg["General"];
			Frequency = gensec.GetSetDefault("Frequency", 5, out modified).IntValue.Constrain(1, 60 * 24);
			gensec["Frequency"].Comment = "How often we check for anything. In minutes.";
			configdirty |= modified;

			var freememsec = cfg["Free Memory"];
			freememsec.Comment = "Attempt to free memory when available memory goes below a threshold.";

			MemLevel = freememsec.GetSetDefault("Threshold", 1000, out modified).IntValue;
			// MemLevel = MemLevel > 0 ? MemLevel.Constrain(1, 2000) : 0;
			freememsec["Threshold"].Comment = "When memory goes down to this level, we act.";
			configdirty |= modified;
			if (MemLevel > 0)
			{
				MemIgnoreFocus = freememsec.GetSetDefault("Ignore foreground", true, out modified).BoolValue;
				freememsec["Ignore foreground"].Comment = "Foreground app is not touched, regardless of anything.";
				configdirty |= modified;

				IgnoreList = freememsec.GetSetDefault("Ignore list", new string[] { }, out modified).StringValueArray;
				freememsec["Ignore list"].Comment = "List of apps that we don't touch regardless of anything.";
				configdirty |= modified;

				MemCooldown = freememsec.GetSetDefault("Cooldown", 60, out modified).IntValue.Constrain(1, 180);
				freememsec["Cooldown"].Comment = "Don't do this again for this many minutes.";
			}

			if (configdirty) Taskmaster.MarkDirtyINI(cfg);
		}

		PerformanceCounterWrapper memfree = null;
		PerformanceCounterWrapper commitbytes = null;
		PerformanceCounterWrapper commitlimit = null;
		PerformanceCounterWrapper commitpercentile = null;

		int HealthCheck_lock = 0;
		async void TimerCheck(object state)
		{
			// skip if already running...
			// happens sometimes when the timer keeps running but not the code here
			if (!Atomic.Lock(ref HealthCheck_lock)) return;

			try
			{
				await Check();
			}
			catch (Exception ex) { Logging.Stacktrace(ex); }
			finally
			{
				Atomic.Unlock(ref HealthCheck_lock);
			}
		}

		async Task Check()
		{
			await Task.Delay(0);

			// Console.WriteLine("<<Auto-Doc>> Checking...");

			if (MemLevel > 0)
			{
				var memfreemb = memfree?.Value ?? 0; // MB
				var commitb = commitbytes?.Value ?? 0;
				var commitlimitb = commitlimit?.Value ?? 0;
				var commitp = commitpercentile?.Value ?? 0;

				// Console.WriteLine("Memory free: " + string.Format("{0:N1}", memfreet) + " / " + MemLevel);
				if (memfreemb <= MemLevel)
				{
					// Console.WriteLine("<<Auto-Doc>> Memlevel below threshold.");

					var now = DateTime.Now;
					var cooldown = (now - MemFreeLast).TotalMinutes; // passed time since MemFreeLast
					MemFreeLast = now;

					// Console.WriteLine(string.Format("Cooldown: {0:N2} minutes [{1}]", cooldown, MemCooldown));

					if (cooldown >= MemCooldown)
					{
						// The following should just call something in ProcessManager

						var ignorepid = -1;
						try
						{
							if (MemIgnoreFocus)
							{
								ignorepid = Taskmaster.activeappmonitor.Foreground;
								Taskmaster.processmanager.Ignore(ignorepid);
							}

							Log.Information("<<Auto-Doc>> Free memory low [{Memory}], attempting to improve situation.", HumanInterface.ByteString((long)memfreemb * 1000000));

							await Taskmaster.processmanager.FreeMemory(null);
						}
						finally
						{
							if (MemIgnoreFocus)
								Taskmaster.processmanager.Unignore(ignorepid);
						}

						// sampled too soon, OS has had no significant time to swap out data

						var memfreemb2 = memfree?.Value ?? 0; // MB
						var commitp2 = commitpercentile?.Value ?? 0;
						var commitb2 = commitbytes?.Value ?? 0;
						var actualbytes = commitb * (commitp / 100);
						var actualbytes2 = commitb2 * (commitp2 / 100);

						Log.Information("<<Auto-Doc>> Free memory: {Memory} ({Change} change observed)",
							HumanInterface.ByteString((long)(memfreemb2 * 1000)),
							//HumanInterface.ByteString((long)commitb2), HumanInterface.ByteString((long)commitlimitb),
							HumanInterface.ByteString((long)(actualbytes - actualbytes2)));
					}
				}
				else if (memfreemb * 1.5 <= MemLevel)
				{
					Console.WriteLine("DEBUG: Free memory fairly low: " + HumanInterface.ByteString((long)(memfreemb * 1000)));
				}
			}
		}

		bool disposed; // = false;
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		void Dispose(bool disposing)
		{
			if (disposed) return;

			if (disposing)
			{
				if (Taskmaster.Trace) Log.Verbose("Disposing health monitor...");

				healthTimer?.Dispose();
				healthTimer = null;

				commitbytes?.Dispose();
				commitbytes = null;
				commitlimit?.Dispose();
				commitlimit = null;
				commitpercentile?.Dispose();
				commitpercentile = null;
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