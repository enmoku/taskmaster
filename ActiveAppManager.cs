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

using System.Diagnostics;

namespace TaskMaster
{
	using System;
	using System.Runtime.InteropServices;
	using Serilog;

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
			//Snatch();
			//D3DDevice();
			dele = new WinEventDelegate(WinEventProc);
			if (!SetupEventHook())
				throw new Exception("Failed to initialize active app manager.");

			// get current window, just in case it's something we're monitoring
			var hwnd = GetForegroundWindow();
			int pid;
			GetWindowThreadProcessId(hwnd, out pid);
			ForegroundId = pid;

			Log.Information("Foreground app manager active.");
		}

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

		public async void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
		{
			if (eventType == EVENT_SYSTEM_FOREGROUND)
			{
				await System.Threading.Tasks.Task.Yield();

				const int nChars = 256;
				IntPtr handle = IntPtr.Zero;
				var buff = new System.Text.StringBuilder(nChars);
				//handle = GetForegroundWindow();

				//http://www.pinvoke.net/default.aspx/user32.GetWindowPlacement

				if (GetWindowText(hwnd, buff, nChars) > 0) // get title? not really useful for most things
				{
					//System.Console.WriteLine("Active window: {0}", buff);
				}
				else
				{
					//System.Console.WriteLine("Couldn't get title of active window.");
				}

				// ?? why does it return here already sometimes? takes too long?

				/*
				if (System.Windows.Forms.Screen.FromHandle(hwnd).Bounds == System.Windows.Forms.Control.FromHandle(hwnd).Bounds)
				{
					//Console.WriteLine("Full screen.");
				}
				else
				{
					//Console.WriteLine("Not full screen.");
				}
				*/

				//SlimDX.Windows.DisplayMonitor.FromWindow(hwnd);
				/*
				Microsoft.DirectX.Direct3D.AdapterInformation inf = new Microsoft.DirectX.Direct3D.AdapterInformation
				Microsoft.DirectX.Direct3D.DisplayMode mode = inf.CurrentDisplayMode;
				Microsoft.DirectX.Direct3D.Format form = mode.Format;
				System.Console.WriteLine("Format: {0}", mode.Format);
				*/

				var activewindowev = new WindowChangedArgs();
				activewindowev.hWnd = hwnd;
				activewindowev.Title = buff.ToString();
				activewindowev.Fullscreen = Trinary.Nonce;
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
		}

		public bool D3DDevice()
		{
			return false;
		}

		[DllImport("user32.dll", SetLastError = true)]
		static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);
	}
}

