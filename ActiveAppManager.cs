//
// ActiveAppManager.cs
//
// Author:
//       M.A. (enmoku) <>
//
// Copyright (c) 2016-2018 M.A. (enmoku)
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
using System.Diagnostics;
using System.Runtime.InteropServices;
using Serilog;
using Microsoft.Win32.SafeHandles;

namespace TaskMaster
{

	public class WindowChangedArgs : EventArgs
	{
		public IntPtr hWnd { get; set; }
		public int Id { get; set; }
		public Process Process { get; set; }
		public string Title { get; set; }
		public Trinary Fullscreen { get; set; }
		public string Executable { get; set; }
	}

	public class ActiveAppManager : IDisposable
	{
		public event EventHandler<WindowChangedArgs> ActiveChanged;

		public ActiveAppManager()
		{
			dele = new WinEventDelegate(WinEventProc);
			if (!SetupEventHook())
				throw new Exception("Failed to initialize active app manager.");

			// get current window, just in case it's something we're monitoring
			var hwnd = GetForegroundWindow();
			int pid;
			GetWindowThreadProcessId(hwnd, out pid);
			ForegroundId = pid;

			var perfsec = TaskMaster.cfg["Performance"];
			bool modified = false;
			Hysterisis = perfsec.GetSetDefault("Foreground hysterisis", 1500, out modified).IntValue.Constrain(0, 30000);
			perfsec["Foreground hysterisis"].Comment = "In milliseconds, from 0 to 30000. Delay before we inspect foreground app, in case user rapidly swaps apps.";
			if (modified) TaskMaster.MarkDirtyINI(TaskMaster.cfg);

			Log.Information("<Foreground Manager> Loaded.");
		}

		int Hysterisis = 500;

		WinEventDelegate dele;
		IntPtr windowseventhook = IntPtr.Zero;

		public int ForegroundId { get; private set; } = -1;

		public bool isForeground(int ProcessId)
		{
			return ProcessId == ForegroundId;
		}

		public bool SetupEventHook()
		{
			windowseventhook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, dele, 0, 0, WINEVENT_OUTOFCONTEXT);
			// FIXME: Seems to stop functioning really easily? Possibly from other events being caught.
			if (windowseventhook == IntPtr.Zero)
			{
				Log.Error("Foreground window event hook not attached.");
				return false;
			}
			return true;
		}

		public void SetupEventHookEvent(object sender, ProcessEventArgs e)
		{
			//SetupEventHook();
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		bool disposed = false;
		void Dispose(bool disposing)
		{
			if (disposed)
				return;

			if (disposing)
			{
				if (TaskMaster.Trace)
					Log.Verbose("Disposing FG monitor...");

				UnhookWinEvent(windowseventhook); // Automaticc
			}

			disposed = true;
		}

		/*
		public static string GetActiveWindowTitle()
		{
			const int nChars = 256;
			IntPtr handle = IntPtr.Zero;
			System.Text.StringBuilder Buff = new System.Text.StringBuilder(nChars);
			handle = GetForegroundWindow();

			if (GetWindowText(handle, Buff, nChars) > 0)
				return Buff.ToString();
			
			return null;
		}
		*/

		int foreground = 0;

		//[UIPermissionAttribute(SecurityAction.Demand)] // fails
		//[SecurityPermissionAttribute(SecurityAction.Demand, UnmanagedCode = true)]
		public async void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
		{
			if (eventType != EVENT_SYSTEM_FOREGROUND) return;

			foreground++;

			using (var m = SelfAwareness.Mind("Hystersis Delay hung", DateTime.Now.AddSeconds((Hysterisis / 1000) + 5)))
			{
				await System.Threading.Tasks.Task.Delay(Hysterisis); // minded
			}

			int old = -1;
			if ((old = foreground) > 1) // if we've swapped in this time, we won't bother checking anything about it.
			{
				foreground--;
				//Log.Verbose("<Foreground> {0} apps in foreground, we're late to the party.", old);
				return;
			}

			//IntPtr handle = IntPtr.Zero; // hwnd arg already has this
			//handle = GetForegroundWindow();

			//http://www.pinvoke.net/default.aspx/user32.GetWindowPlacement

			const int nChars = 256;
			System.Text.StringBuilder buff = null;
			if (TaskMaster.DebugForeground)
			{
				buff = new System.Text.StringBuilder(nChars);

				// Window title, we don't care tbh.
				if (GetWindowText(hwnd, buff, nChars) > 0) // get title? not really useful for most things
				{
					//System.Console.WriteLine("Active window: {0}", buff);
				}
			}

			// BUG: ?? why does it return here already sometimes? takes too long?
			// ----------------------
			Trinary fullScreen = Trinary.Nonce;
			try
			{
				/*
				// WORKS, JUST KINDA USELESS FOR NOW
				// PLAN: Use this for process analysis.
				System.Windows.Forms.Screen screen = System.Windows.Forms.Screen.FromHandle(hwnd); // passes

				RECT rect = new RECT();
				GetWindowRect(hwnd, ref rect);

				var r = new System.Drawing.Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
				bool full = r.Equals(screen.Bounds);
				fullScreen = full ? Trinary.True : Trinary.False;
				*/
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}

			try
			{
				var activewindowev = new WindowChangedArgs();
				activewindowev.hWnd = hwnd;
				activewindowev.Title = (buff != null ? buff.ToString() : string.Empty);
				activewindowev.Fullscreen = fullScreen;
				int pid = 0;
				GetWindowThreadProcessId(hwnd, out pid);
				ForegroundId = activewindowev.Id = pid;
				try
				{
					Process proc = Process.GetProcessById(activewindowev.Id);
					activewindowev.Process = proc;
					activewindowev.Executable = proc.ProcessName;
				}
				catch { /* NOP */ }

				if (TaskMaster.DebugForeground && TaskMaster.ShowInaction)
					Log.Debug("Active Window (#{Pid}): {Title}", activewindowev.Id, activewindowev.Title);

				ActiveChanged?.Invoke(this, activewindowev);
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}

			foreground--;
		}

		[DllImport("user32.dll", SetLastError = true)]
		static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);


		delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

		[DllImport("user32.dll")]
		static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

		[DllImport("user32.dll")]
		static extern bool UnhookWinEvent(IntPtr hWinEventHook); // automatic

		const uint WINEVENT_OUTOFCONTEXT = 0;
		const uint EVENT_SYSTEM_FOREGROUND = 3;

		[DllImport("user32.dll")]
		static extern IntPtr GetForegroundWindow();

		[DllImport("user32.dll")]
		static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

		[StructLayout(LayoutKind.Sequential)]
		private struct RECT
		{
			public int Left;
			public int Top;
			public int Right;
			public int Bottom;
		}

		[DllImport("user32.dll")]
		private static extern bool GetWindowRect(IntPtr hWnd, [In, Out] ref RECT rect);
	}
}

