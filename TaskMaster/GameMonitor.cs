//
// GameMonitor.cs
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
	using System.Runtime.InteropServices;

	public class WindowChangedArgs : EventArgs
	{
		public IntPtr hwnd { get; set; }
		public string title { get; set; }
	}

	public class GameMonitor : IDisposable
	{
		static NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

		WinEventDelegate dele;
		IntPtr m_hhook = IntPtr.Zero;

		public void SetupEventHook()
		{
			m_hhook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, dele, 0, 0, WINEVENT_OUTOFCONTEXT);
			// FIXME: Seems to stop functioning really easily? Possibly from other events being caught.
			if (m_hhook == IntPtr.Zero)
			{
				//System.Console.WriteLine("GameMon: Failed to set foreground app monitor.");
				Log.Error("Foreground window event hook not attached.");
			}
		}

		public void SetupEventHookEvent(object sender, ProcessEventArgs e)
		{
			//SetupEventHook();
		}

		public GameMonitor()
		{
			Log.Trace("Starting...");
			//Snatch();
			//D3DDevice();
			dele = new WinEventDelegate(WinEventProc);
			SetupEventHook();
		}

		public void Dispose()
		{
			Log.Trace("Disposing...");
			//UnhookWinEvent(m_hhook); // Automatic
		}

		delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

		[DllImport("user32.dll")]
		static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
		//[DllImport("user32.dll")]
		//static extern bool UnhookWinEvent(IntPtr hWinEventHook); // automatic

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

		public event EventHandler<WindowChangedArgs> ActiveChanged;

		protected virtual void OnActiveChanged(WindowChangedArgs e)
		{
			EventHandler<WindowChangedArgs> handler = ActiveChanged;
			if (handler != null)
				handler(this, e);
		}

		public void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
		{
			if (eventType == EVENT_SYSTEM_FOREGROUND)
			{
				const int nChars = 256;
				IntPtr handle = IntPtr.Zero;
				var buff = new System.Text.StringBuilder(nChars);
				//handle = GetForegroundWindow();

				if (GetWindowText(hwnd, buff, nChars) > 0)
				{
					//System.Console.WriteLine("Active window: {0}", buff);
				}
				else
				{
					//System.Console.WriteLine("Couldn't get title of active window.");
				}

				// ?? why does it return here already sometimes? takes too long?

				//Console.WriteLine("Noob!");
				if (System.Windows.Forms.Screen.FromHandle(hwnd).Bounds == System.Windows.Forms.Control.FromHandle(hwnd).Bounds)
				{
					//Console.WriteLine("Full screen.");

				}
				else
				{
					//Console.WriteLine("Not full screen.");
				}

				//SlimDX.Windows.DisplayMonitor.FromWindow(hwnd);
				/*
				Microsoft.DirectX.Direct3D.AdapterInformation inf = new Microsoft.DirectX.Direct3D.AdapterInformation
				Microsoft.DirectX.Direct3D.DisplayMode mode = inf.CurrentDisplayMode;
				Microsoft.DirectX.Direct3D.Format form = mode.Format;
				System.Console.WriteLine("Format: {0}", mode.Format);
				*/
				var e = new WindowChangedArgs();
				e.hwnd = hwnd;
				e.title = buff.ToString();
				OnActiveChanged(e);
			}
		}

		public bool D3DDevice()
		{
			return false;
		}

		public void Snatch()
		{
			/*
			Process procs[] = Process.GetProcessesByName("chrome.exe");
			Process proc;
			proc.PriorityBoostEnabled = true; // boost focused apps
			proc.PriorityClass = ProcessPriorityClass.BelowNormal;
			if (!proc.Responding) // not responding
			{
				// kill it?
			}
			*/
			//proc.StartTime//when process was started

		}
	}
}

