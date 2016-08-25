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

using System;
using System.Linq;

namespace TaskMaster
{
	using System.Windows.Forms;

	public class TrayAccess : IDisposable
	{
		static NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

		NotifyIcon Tray;

		public TrayAccess()
		{
			Tray = new NotifyIcon();
			Tray.Text = Application.ProductName + "!"; // Tooltip so people know WTF I am.
			Tray.BalloonTipText = Tray.Text;
			Tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location); // is this really the best way?

			Log.Trace("Generating tray icon.");

			//MenuItem toggleVisibility = new MenuItem("Open", ShowWindowRequest);
			//MenuItem configMenuItem = new MenuItem("Configuration", ShowConfigRequest);
			//MenuItem exitMenuItem = new MenuItem("Exit", ExitRequest);
			//Tray.ContextMenu = new ContextMenu(new MenuItem[] { toggleVisibility, configMenuItem, exitMenuItem });
			ContextMenuStrip ms = new ContextMenuStrip();
			ms.Items.Add("Open", null, ShowWindowRequest);
			ms.Items.Add("Configuration", null, ShowConfigRequest);
			ms.Items.Add("Exit", null, ExitRequest);
			Tray.ContextMenuStrip = ms;

			RegisterExplorerExit();
			Tray.Visible = true;


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
			if (TaskMaster.tmw != null)
				TaskMaster.tmw.ShowConfigRequest(sender, e);
			else
				Log.Warn("Can't open configuration.");
		}

		void ExitRequest(object sender, EventArgs e)
		{
			if (TaskMaster.tmw != null)
				TaskMaster.tmw.ExitRequest(sender, e);
			else
				Application.Exit();
		}

		void RestoreMain(object sender, EventArgs e)
		{
			if (TaskMaster.tmw == null)
			{
				Log.Debug("Reconstructing main window.");
				TaskMaster.tmw = new MainWindow();
				TaskMaster.HookMainWindow();
				Tray.MouseClick -= RestoreMain;
			}

			TaskMaster.tmw.Show();
		}

		void RestoreMainRequest(object sender, MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				RestoreMain(sender, null);
			}
		}

		void ShowWindow(object sender, MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				TaskMaster.tmw.ShowWindowRequest(sender, null);
			}
		}

		void UnloseWindow(object sender, MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				TaskMaster.tmw.RestoreWindowRequest(sender, null);
			}
		}

		void WindowClosed(object sender, EventArgs e)
		{
			Tray.MouseClick -= ShowWindow;
			Tray.MouseDoubleClick -= UnloseWindow;

			Tray.MouseClick += RestoreMainRequest;
		}

		public void RegisterMain(MainWindow window)
		{
			Tray.MouseClick += ShowWindow;
			Tray.MouseDoubleClick += UnloseWindow;
			TaskMaster.tmw.FormClosing += WindowClosed;
		}

		System.Diagnostics.Process Explorer;
		async void ExplorerCrashHandler(object sender, EventArgs e)
		{
			await System.Threading.Tasks.Task.Delay(100); // force async

			Log.Warn("Explorer crash detected!");
			Tray.Visible = true; // TODO: Is this enough?
			try
			{
				Log.Debug("Unregistering");
				Explorer.Exited -= ExplorerCrashHandler;
				Log.Debug("Refreshing");
				Explorer.Refresh(); // do we need this?
			}
			catch (Exception)
			{
				Log.Error("Failed to unregister explorer crash handler.");
				throw;
			}

			// TODO: register it again
			//RegisterExplorerExit();
			Log.Debug("Explorer crash handling done!");
		}

		void RegisterExplorerExit()
		{
			Log.Trace("Registering Explorer crash monitor.");
			// this is for dealing with notify icon disappearing on explorer.exe crash/restart

			System.Diagnostics.Process[] procs = System.Diagnostics.Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension("explorer.exe"));
			if (procs.Count() > 0)
			{
				foreach (System.Diagnostics.Process proc in procs)
				{
					Explorer = proc;
					break;
				}
				Explorer.Exited += ExplorerCrashHandler;
				Explorer.EnableRaisingEvents = true;
				Log.Info(System.String.Format("Explorer (#{0}) registered.", Explorer.Id));
			}
			else
				Log.Warn("Explorer not found.");
		}

		~TrayAccess()
		{
			//Dispose();
		}

		public void Refresh()
		{
			Tray.Visible = true;
		}

		public void Dispose()
		{
			Dispose(true);
			System.GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposing)
			{
				if (Tray != null)
				{
					Tray.Visible = false;
					Tray.Dispose();
					Tray = null;
				}
			}
		}
	}
}

