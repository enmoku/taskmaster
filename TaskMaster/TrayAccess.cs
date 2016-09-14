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
			Tray = new NotifyIcon
			{
				Text = Application.ProductName + "!", // Tooltip so people know WTF I am.
				Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location) // is this really the best way?
			};
			Tray.BalloonTipText = Tray.Text;

			Log.Trace("Generating tray icon.");

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
			System.Diagnostics.Process.Start(TaskMaster.cfgpath);
			if (TaskMaster.tmw != null)
				TaskMaster.tmw.ShowConfigRequest(sender, e);
			else
				Log.Warn("Can't open configuration.");
		}

		void ExitRequest(object sender, EventArgs e)
		{
			Application.Exit();
			Console.WriteLine("END::Tray.ExitRequest()");
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
			if (TaskMaster.tmw.LowMemory)
			{
				Tray.MouseClick -= ShowWindow;
				Tray.MouseDoubleClick -= UnloseWindow;

				Tray.MouseClick += RestoreMainRequest;
			}
		}

		public void RegisterMain(ref MainWindow window)
		{
			Tray.MouseClick += ShowWindow;
			Tray.MouseDoubleClick += UnloseWindow;
			window.FormClosing += WindowClosed;
		}

		System.Diagnostics.Process Explorer;
		async void ExplorerCrashHandler(object sender, EventArgs e)
		{
			Log.Warn("Explorer crash detected!");
			await System.Threading.Tasks.Task.Delay(8000); // force async

			if (RegisterExplorerExit())
			{
				Tray.Visible = true; // TODO: Is this enough/necessary?
			}
			Log.Debug("Explorer crash handling done!");
		}

		bool RegisterExplorerExit()
		{
			Log.Trace("Registering Explorer crash monitor.");
			// this is for dealing with notify icon disappearing on explorer.exe crash/restart

			System.Diagnostics.Process[] procs = System.Diagnostics.Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension("explorer.exe"));
			if (procs.Count() > 0)
			{
				Explorer = procs[0];
				Explorer.Exited += ExplorerCrashHandler;
				Explorer.EnableRaisingEvents = true;
				Log.Info(System.String.Format("Explorer (#{0}) registered.", Explorer.Id));
				return true;
			}

			Log.Warn("Explorer not found.");
			return false;
		}

		/// <summary>
		/// Hides tray icon.
		/// Should be called before the app exits to make sure there's no lingering icon.
		/// </summary>
		public void Close()
		{
			if (Tray != null)
			{
				Tray.Visible = false;
				Tray.Dispose();
				Tray = null;
			}
		}

		~TrayAccess()
		{
			Log.Trace("Destructing");
			#if DEBUG
			if (!disposed)
				Log.Warn("Not disposed");
			#endif
		}

		bool disposed = false;
		public void Dispose()
		{
			Dispose(true);
			//GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposed)
				return;

			if (disposing)
			{
				Close();
				if (Tray != null)
				{
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

