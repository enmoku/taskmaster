//
// ProcessControlAbstract.cs
//
// Author:
//       M.A. (enmoku) <>
//
// Copyright (c) 2016 M.A. (enmoku)
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

namespace TaskMaster
{
	using System;
	using Serilog;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.Runtime.InteropServices;

	public abstract class AbstractProcessControl
	{
		string p_executable;
		/// <summary>
		/// Executable filename related to this.
		/// </summary>
		public string Executable
		{
			get
			{
				return p_executable;
			}
			set
			{
				p_executable = value;
				ExecutableFriendlyName = System.IO.Path.GetFileNameWithoutExtension(value);
			}
		}

		/// <summary>
		/// Frienly executable name as required by various System.Process functions.
		/// Same as <see cref="T:TaskMaster.ProcessControl.Executable"/> but with the extension missing.
		/// </summary>
		public string ExecutableFriendlyName { get; set; }

		/// <summary>
		/// Human-readable friendly name for the process.
		/// </summary>
		public string FriendlyName { get; set; }

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

		/// <summary>
		/// Determines if the process I/O is to be set background.
		/// </summary>
		/// <value><c>true</c> if background process; otherwise, <c>false</c>.</value>
		public bool BackgroundIO { get; set; } = false;

		/// <summary>
		/// Determines if the values are only maintained when the app is in foreground.
		/// </summary>
		/// <value><c>true</c> if foreground; otherwise, <c>false</c>.</value>
		public bool ForegroundOnly { get; set; } = false;

		/// <summary>
		/// Target priority class for the process.
		/// </summary>
		public System.Diagnostics.ProcessPriorityClass Priority = System.Diagnostics.ProcessPriorityClass.Normal;

		/// <summary>
		/// CPU core affinity.
		/// </summary>
		public IntPtr Affinity = new IntPtr(ProcessManager.allCPUsMask);

		/// <summary>
		/// The power plan.
		/// </summary>
		public PowerManager.PowerMode PowerPlan = PowerManager.PowerMode.Undefined;

		/// <summary>
		/// Allow priority decrease.
		/// </summary>
		public bool Decrease = true;
		public bool Increase = true;

		static object waitingExitLock = new object();
		static List<Process> waitingExit = new List<Process>(1);

		protected void setPowerPlan(Process process)
		{
			if (!TaskMaster.PowerManagerEnabled) return;

			if (PowerPlan != PowerManager.PowerMode.Undefined)
			{
				lock (waitingExitLock)
				{
					if (waitingExit.Count == 0)
						PowerManager.SaveMode();
					waitingExit.Add(process);
					Log.Verbose("POWER MODE: {0} processes desiring higher power mode.", waitingExit.Count);
				}

				process.EnableRaisingEvents = true;
				process.Exited += async (sender, ev) =>
				{
					lock (waitingExitLock)
					{
						waitingExit.Remove(process);
						Log.Verbose("POWER MODE: process exited, {0} still waiting to exit.", waitingExit.Count);
					}
					await System.Threading.Tasks.Task.Delay(ProcessManager.PowerdownDelay);
					lock (waitingExitLock)
					{
						if (waitingExit.Count == 0)
							PowerManager.RestoreMode();
						else
							Log.Verbose("POWER MODE: {0} processes still wanting higher power mode.", waitingExit.Count);
					}
				};

				if (PowerManager.Current != PowerPlan)
				{
					Log.Verbose("Power mode upgrading to: {PowerPlan}", PowerPlan);
					PowerManager.upgradeMode(PowerPlan);
				}
			}
		}

		[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		public static extern bool SetPriorityClass(IntPtr handle, uint priorityClass);

		public enum PriorityTypes
		{
			ABOVE_NORMAL_PRIORITY_CLASS = 0x00008000,
			BELOW_NORMAL_PRIORITY_CLASS = 0x00004000,
			HIGH_PRIORITY_CLASS = 0x00000080,
			IDLE_PRIORITY_CLASS = 0x00000040,
			NORMAL_PRIORITY_CLASS = 0x00000020,
			PROCESS_MODE_BACKGROUND_BEGIN = 0x00100000,
			PROCESS_MODE_BACKGROUND_END = 0x00200000,
			REALTIME_PRIORITY_CLASS = 0x00000100
		}

		void SetBackground(Process process)
		{
			SetIOPriority(process, PriorityTypes.PROCESS_MODE_BACKGROUND_BEGIN);
		}

		public static void SetIOPriority(Process process, PriorityTypes priority)
		{
			try { SetPriorityClass(process.Handle, (uint)priority); }
			catch { }
		}

		/*
		protected bool TouchApply(Process process)
		{
			ProcessPriorityClass oldPriority = process.PriorityClass;

			bool rv = process.SetLimitedPriority(Priority, Increase, Decrease);
			LastSeen = DateTime.Now;
			Adjusts += 1;

			Log.Info(string.Format("{0} (#{1}); Priority: {2} → {3}", process.ProcessName, process.Id, oldPriority, Priority));

			return rv;		}
		*/
	}
}
