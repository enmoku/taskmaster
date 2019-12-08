//
// UI.TrayWndProc.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2016-2019 M.A.
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

using Serilog;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Taskmaster.UI
{
	using static Application;

	public class TrayWndProcProxy : Form
	{
		const int WM_QUERYENDSESSION = 0x0011;
		const int WM_ENDSESSION = 0x0016;

		const int ENDSESSION_CRITICAL = 0x40000000;
		const int ENDSESSION_LOGOFF = unchecked((int)0x80000000);
		const int ENDSESSION_CLOSEAPP = 0x1;

		readonly int hotkeymodifiers = (int)NativeMethods.KeyModifier.Control | (int)NativeMethods.KeyModifier.Shift | (int)NativeMethods.KeyModifier.Alt;

		bool HotkeysRegistered = false;

		public void RegisterGlobalHotkeys()
		{
			Debug.Assert(MKAh.Execution.IsMainThread, "RegisterGlobalHotkeys must be called from main thread");

			if (HotkeysRegistered) return;

			bool regM = false, regR = false;

			try
			{
				NativeMethods.RegisterHotKey(Handle, 0, hotkeymodifiers, Keys.M.GetHashCode());
				regM = true;

				NativeMethods.RegisterHotKey(Handle, 1, hotkeymodifiers, Keys.R.GetHashCode());
				regR = true;

				HotkeysRegistered = true;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}

			var sbs = new System.Text.StringBuilder(128);
			sbs.Append("<Global> Registered hotkeys: ");
			if (regM) sbs.Append("ctrl-alt-shift-m = free memory [foreground ignored]");
			if (regM && regR) sbs.Append(", ");
			if (regR) sbs.Append("ctrl-alt-shift-r = scan");
			Log.Information(sbs.ToString());
		}

		public void UnregisterGlobalHotkeys()
		{
			//Debug.Assert(Taskmaster.IsMainThread(), "UnregisterGlobalHotkeys must be called from main thread");

			if (HotkeysRegistered)
			{
				NativeMethods.UnregisterHotKey(Handle, 0);
				NativeMethods.UnregisterHotKey(Handle, 1);
			}
		}

		async Task FreeMemory(int ignorePid)
		{
			Log.Information("<Global> Hotkey detected; Freeing memory while ignoring foreground" +
				(ignorePid > 4 ? $" #{ignorePid}" : string.Empty) + " if possible.");
			await processmanager.FreeMemoryAsync(ignorePid).ConfigureAwait(false);
		}

		protected override void WndProc(ref Message m)
		{
			if (disposed) return;
			try
			{
				if (m.Msg == NativeMethods.WM_HOTKEY)
				{
					Keys key = (Keys)(((int)m.LParam >> 16) & 0xFFFF);
					//NativeMethods.KeyModifier modifiers = (NativeMethods.KeyModifier)((int)m.LParam & 0xFFFF);
					//int modifiers =(int)m.LParam & 0xFFFF;
					int hotkeyId = m.WParam.ToInt32();

					//if (modifiers != hotkeymodifiers)
					//Log.Debug($"<Global> Received unexpected modifier keys: {modifiers} instead of {hotkeymodifiers}");

					switch (hotkeyId)
					{
						case 0:
							if (Trace) Log.Verbose("<Global> Hotkey ctrl-alt-shift-m detected!!!");

							int ignorepid = activeappmonitor?.ForegroundId ?? -1;
							FreeMemory(ignorepid).ConfigureAwait(false);

							m.Result = IntPtr.Zero;
							break;
						case 1:
							if (Trace) Log.Verbose("<Global> Hotkey ctrl-alt-shift-r detected!!!");
							Log.Information("<Global> Hotkey detected; Hastening next scan.");
							processmanager?.HastenScan(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
							m.Result = IntPtr.Zero;
							break;
						default:
							Log.Debug("<Global> Received unexpected key event: " + key.ToString());
							break;
					}
				}
				else if (m.Msg == NativeMethods.WM_COMPACTING)
				{
					Log.Debug("<System> WM_COMPACTING received");
					// wParam = The ratio of central processing unit(CPU) time currently spent by the system compacting memory to CPU time currently spent by the system performing other operations.For example, 0x8000 represents 50 percent of CPU time spent compacting memory.
					// lParam = This parameter is not used.
					System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
					GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, false, true);
					m.Result = IntPtr.Zero;
				}
				else if (m.Msg == WM_QUERYENDSESSION)
				{
					string detail = "Unknown";

					int lparam = m.LParam.ToInt32();
					if (MKAh.Logic.Bit.IsSet(lparam, ENDSESSION_LOGOFF))
						detail = "User Logoff";
					if (MKAh.Logic.Bit.IsSet(lparam, ENDSESSION_CLOSEAPP))
						detail = "System Servicing";
					if (MKAh.Logic.Bit.IsSet(lparam, ENDSESSION_CRITICAL))
						detail = "System Critical";

					Log.Information("<OS> Session end signal received; Reason: " + detail);

					/*
					ShutdownBlockReasonCreate(Handle, "Cleaning up");
					Task.Run(() => {
						try
						{
							ExitCleanup();
						}
						finally
						{
							try
							{
								ShutdownBlockReasonDestroy(Handle);
							}
							finally
							{
								UnifiedExit();
							}
						}
					});

					// block exit
					m.Result = new IntPtr(0);
					return;
					*/
				}
				else if (m.Msg == WM_ENDSESSION)
				{
					if (m.WParam.ToInt32() == 1L) // == true; session is actually ending
					{
						string detail = "Unknown";

						int lparam = m.LParam.ToInt32();
						if (MKAh.Logic.Bit.IsSet(lparam, ENDSESSION_LOGOFF))
							detail = "User Logoff";
						if (MKAh.Logic.Bit.IsSet(lparam, ENDSESSION_CLOSEAPP))
							detail = "System Servicing";
						if (MKAh.Logic.Bit.IsSet(lparam, ENDSESSION_CRITICAL))
							detail = "System Critical";

						Log.Information("<OS> Session end signal confirmed; Reason: " + detail);

						UnifiedExit();
					}
					else
					{
						Log.Information("<OS> Session end cancellation received.");
					}
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}

			base.WndProc(ref m); // is this necessary?
		}

		#region IDisposable Support
		bool disposed = false;

		protected override void Dispose(bool disposing)
		{
			if (disposed) return;
			disposed = true;

			UnregisterGlobalHotkeys();

			base.Dispose(disposing);
		}
		#endregion
	}
}
