//
// TrayAccess.cs
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
using System.Runtime.InteropServices;

namespace TaskMaster
{
	using System;
	using System.Linq;
	using System.Windows.Forms;

	sealed public class TrayAccess : IDisposable
	{
		static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

		NotifyIcon Tray;

		ContextMenuStrip ms;
		ToolStripMenuItem swr_menu;
		ToolStripMenuItem scr_menu;
		ToolStripMenuItem er_menu;

		public TrayAccess()
		{
			Tray = new NotifyIcon
			{
				Text = Application.ProductName + "!", // Tooltip so people know WTF I am.
				Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location) // is this really the best way?
			};
			Tray.BalloonTipText = Tray.Text;
			Tray.Disposed += (object sender, EventArgs e) => { Tray = null; };

			Log.Trace("Generating tray icon.");

			ms = new ContextMenuStrip();
			swr_menu = new ToolStripMenuItem("Open", null, ShowWindowRequest);
			scr_menu = new ToolStripMenuItem("Configuration", null, ShowConfigRequest);
			er_menu = new ToolStripMenuItem("Exit", null, ExitRequest);
			ms.Items.Add(swr_menu);
			ms.Items.Add(scr_menu);
			ms.Items.Add(er_menu);
			Tray.ContextMenuStrip = ms;
			Log.Trace("Tray menu ready");

			if (!RegisterExplorerExit())
				throw new InitFailure("Explorer registeriong failed; not running?");
			
			Tray.Visible = true;

			Log.Trace("Tray icon generated.");
		}

		void ShowWindowRequest(object sender, EventArgs e)
		{
			if (TaskMaster.tmw != null)
				TaskMaster.tmw.ShowWindowRequest(sender, null);
			else
				RestoreMain(sender, e);
		}

		void ShowConfigRequest(object sender, EventArgs e)
		{
			//CLEANUP: Console.WriteLine("Opening config folder.");
			System.Diagnostics.Process.Start(TaskMaster.cfgpath);

			TaskMaster.tmw?.ShowConfigRequest(sender, e);
			//CLEANUP: Console.WriteLine("Done opening config folder.");
		}

		public static event EventHandler onExit;

		void ExitRequest(object sender, EventArgs e)
		{
			//CLEANUP: Console.WriteLine("START:Tray.ExitRequest()");
			er_menu.Enabled = false;
			onExit?.Invoke(this, null);
			//CLEANUP: Console.WriteLine("END::Tray.ExitRequest()");
		}

		void RestoreMain(object sender, EventArgs e)
		{
			TaskMaster.BuildMain();
			TaskMaster.tmw.Show();
		}

		void RestoreMainRequest(object sender, MouseEventArgs e)
		{
			// this should really be system defined activation button in case the buttons are swapped, but C#/OS might handle that for us already?
			if (e.Button == MouseButtons.Left)
				RestoreMain(sender, null);
		}

		void ShowWindow(object sender, MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
				TaskMaster.tmw.ShowWindowRequest(sender, null);
		}

		void UnloseWindow(object sender, MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
				TaskMaster.tmw.RestoreWindowRequest(sender, null);
		}

		void CompactEvent(object sender, EventArgs e)
		{
			System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
			GC.Collect(5, GCCollectionMode.Forced, true, true);
		}

		void WindowClosed(object sender, FormClosingEventArgs e)
		{
			//CLEANUP: Console.WriteLine("START:TrayAccess.WindowClosed");

			switch (e.CloseReason)
			{
				case CloseReason.ApplicationExitCall:
					//CLEANUP: Console.WriteLine("BAIL:TrayAccess.WindowClosed");
					return;
			}

			if (TaskMaster.LowMemory)
			{
				//CLEANUP: Console.WriteLine("DEBUG:TrayAccess.WindowClosed.SaveMemory");

				Tray.MouseClick -= ShowWindow;
				Tray.MouseDoubleClick -= UnloseWindow;

				Tray.MouseClick += RestoreMainRequest;
			}
			//CLEANUP: Console.WriteLine("END:TrayAccess.WindowClosed");
		}

		public void RegisterMain(ref MainWindow window)
		{
			Tray.MouseClick += ShowWindow;
			Tray.MouseDoubleClick += UnloseWindow;
			window.FormClosing += WindowClosed;
			window.FormClosed += CompactEvent;
		}

		System.Diagnostics.Process[] Explorer;
		void ExplorerCrashHandler(object sender, EventArgs e)
		{
			Log.Warn("Explorer crash detected!");

			System.Threading.Tasks.Task.Run(async () =>
			{
				Log.Trace("Giving explorer some time to recover on its own...");

				await System.Threading.Tasks.Task.Delay(12000); // force async

				System.Diagnostics.Process[] procs = ExplorerInstances;
				if (procs.Count() > 0)
					return;

				int i = 0;
				while (!RegisterExplorerExit(procs))
				{
					procs = null;

					if (++i >= 10)
					{
						Log.Warn("Explorer registration failed. System unable to recover?");
						return;
					}

					await System.Threading.Tasks.Task.Delay(8000);
				}
				Tray.Visible = true; // TODO: Is this enough/necessary?
			});
		}

		System.Diagnostics.Process[] ExplorerInstances
		{
			get
			{
				return System.Diagnostics.Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension("explorer.exe"));
			}
		}

		bool RegisterExplorerExit(System.Diagnostics.Process[] procs=null)
		{
			Log.Trace("Registering Explorer crash monitor.");
			// this is for dealing with notify icon disappearing on explorer.exe crash/restart

			if (procs == null) procs = ExplorerInstances;

			if (procs.Count() > 0)
			{
				Explorer = procs;
				foreach (var proc in procs)
				{
					proc.Exited += ExplorerCrashHandler;
					proc.EnableRaisingEvents = true;
					Log.Info(string.Format("Explorer (#{0}) registered.", proc.Id));
				}
				return true;
			}

			Log.Warn("Explorer not found.");
			return false;
		}

		bool disposed = false;
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		void Dispose(bool disposing)
		{
			if (disposed)
				return;

			if (disposing)
			{
				if (Tray != null)
				{
					Tray.Visible = false;
					Tray.Dispose();
					Tray = null;
				}

				// Free any other managed objects here.
				//
			}

			// Free any unmanaged objects here.
			//
			disposed = true;
		}

		public void Tooltip(int timeout, string message, string title, ToolTipIcon icon)
		{
			Tray.ShowBalloonTip(timeout, title, message, icon);
		}

		public void Refresh()
		{
			Tray.Visible = true;
		}
	}
}

