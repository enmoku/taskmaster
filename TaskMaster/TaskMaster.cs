//
// TaskMaster.cs
//
// Author:
//       M.A. (enmoku)
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


/*
 * TODO: Fix process priorities, both CPU and IO
 * TODO: Detect full screen or GPU accelerated apps and adjust their priorities.
 * TODO: Detect if the above apps hang and lower their processing priorities.
 * TODO: Empty working set, detect if it does anything and report it.
 * 
 * MAYBE:
 *  - Monitor [MFT] fragmentation?
 *  - Detect which apps are making noise?
 *  - Detect high disk usage
 *  - Clean %TEMP% with same design goals as the OS builtin disk cleanup utility.
 *  - SMART stats? seems pointless...
 *  - Action logging
 * 
 * CONFIGURATION:
 * TODO: Ini file? Yaml?
 * TODO: Config in app directory
 * TODO: Config in %APPDATA% or %LOCALAPPDATA%
 * 
 * Other:
 *  - Multiple windows or tabbed window?
 */

namespace TaskMaster
{
	using System.Windows.Forms;

	public class MainWindow : Form
	{
		NotifyIcon nicon = null;

		void TrayShowConfig(object sender, System.EventArgs e)
		{
		}

		void Save()
		{
		}

		void ExitCleanup()
		{
			Save();

			Hide();
			if (nicon != null)
			{
				nicon.Visible = false;
				nicon.Icon = null;
				nicon = null;
			}
			Enabled = false;// mono.nexe hangs if disabled, so...
			Application.Exit();
		}

		void TrayExit(object sender, System.EventArgs e)
		{
			ExitCleanup();
		}

		void WindowClose(object sender, FormClosingEventArgs e)
		{
			switch (e.CloseReason)
			{
				case CloseReason.UserClosing:
					// X was pressed or similar, we're just hiding to tray.
					Hide();
					e.Cancel = true;
					break;
				case CloseReason.WindowsShutDown:
					System.Console.WriteLine("Windows shutdown detected, shutting down.");
					goto Cleanup;
				case CloseReason.TaskManagerClosing:
					System.Console.WriteLine("Task manager told us to close.");
					goto Cleanup;
				default:
					System.Console.WriteLine("Unidentified close reason.");
				Cleanup:
					ExitCleanup();
					break;
			}
		}

		void TrayRestoreWindow(object sender, System.EventArgs e)
		{
			CenterToScreen();
		}

		void TrayShowWindow(object sender, System.EventArgs e)
		{
			Show();
		}

		void MakeTrayIcon()
		{
			MenuItem toggleVisibility = new MenuItem("Open", new System.EventHandler(TrayShowWindow));
			MenuItem configMenuItem = new MenuItem("Configuration", new System.EventHandler(TrayShowConfig));
			MenuItem exitMenuItem = new MenuItem("Exit", new System.EventHandler(TrayExit));
			
			nicon = new NotifyIcon();
			nicon.Click += new System.EventHandler(TrayShowWindow);
			nicon.DoubleClick += new System.EventHandler(TrayRestoreWindow);
			nicon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location); // is this really the best way?
			    
			nicon.ContextMenu = new ContextMenu(new MenuItem[] { toggleVisibility, configMenuItem, exitMenuItem });
			nicon.BalloonTipText = "Taskmaster!";
			nicon.Visible = true;
		}

		#region Microphone control code
		//MicMonitor micMonitor = null;
		public void setMicMonitor(MicMonitor micmonitor)
		{
			var micMonitor = micmonitor;
			micName.Text = micMonitor.DeviceName;
			micMonitor.VolumeChanged += volumeChangeDetected;
			micVol.Value = System.Convert.ToInt32(micMonitor.Volume);
			micMonitor.minimize(); // we don't need the extra capabilities for now
			// TODO: Hook device changes
		}

		Label micName = null;
		NumericUpDown micVol = null;
		ListView micList = null;
		static decimal corrections = 0;
		Label corCountLabel = null;

		void UserMicVol(object sender, System.EventArgs e)
		{
			// TODO: Handle volume changes. Not really needed. Give presets?
			//micMonitor.setVolume(micVol.Value);
		}

		void volumeChangeDetected(object sender, VolumeChangedEventArgs e)
		{
			micVol.Value = System.Convert.ToInt32(e.Volume);
			corrections += 1;
			corCountLabel.Text = corrections.ToString();
			#if DEBUG
			nicon.ShowBalloonTip(2000, "TaskMaster!", "Volume change detected! Re-set to " + micVol.Value + "%", ToolTipIcon.Info);
			#endif
		}

		void MicEnum()
		{
			// TODO: Make this use MicMonitor
			int waveInDevices = NAudio.Wave.WaveIn.DeviceCount;
			for (int waveInDevice = 0; waveInDevice < waveInDevices; waveInDevice++)
			{
				NAudio.Wave.WaveInCapabilities deviceInfo = NAudio.Wave.WaveIn.GetCapabilities(waveInDevice);
				micList.Items.Add(new ListViewItem(new string[]{waveInDevice.ToString(),deviceInfo.ProductName,deviceInfo.ProductGuid.ToString() }));
			}
		}
		#endregion // Microphone control code

		void BuildUI()
		{
			Text = "Taskmaster";
			AutoSize = true;
			Padding = new Padding(12);
			Size = new System.Drawing.Size(680,400);
			//Padding = 12;
			//margin

			TableLayoutPanel lrows = new TableLayoutPanel();
			lrows.Parent = this;
			lrows.ColumnCount = 1;
			lrows.RowCount = 4;
			lrows.Dock = DockStyle.Fill;

			#region Main Window Row 1, microphone device
			Label micDevLbl = new Label();
			micDevLbl.Text = "Device";
			TableLayoutPanel micNameRow = new TableLayoutPanel();
			micNameRow.RowCount = 1;
			micNameRow.ColumnCount = 2;
			micNameRow.BackColor = System.Drawing.Color.BlanchedAlmond; // DEBUG
			micNameRow.Controls.Add(micDevLbl, 0, 0);
			micName = new Label();
			micName.AutoSize = true;
			micNameRow.Controls.Add(micName, 1, 0);
			micNameRow.Dock = DockStyle.Fill;
			micNameRow.AutoSize = true;
			lrows.Controls.Add(micNameRow, 0, 0);
			#endregion

			// uhh???
			#region Main Window Row 2, volume control
			TableLayoutPanel miccntrl = new TableLayoutPanel();
			miccntrl.ColumnCount = 5;
			miccntrl.RowCount = 1;
			miccntrl.BackColor = System.Drawing.Color.Azure; // DEBUG
			miccntrl.Dock = DockStyle.Fill;
			//miccntrl.Location = new System.Drawing.Point(0, 0);
			miccntrl.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			miccntrl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			lrows.Controls.Add(miccntrl, 0, 1);

			/*
			Panel micPanel = new Panel();
			micPanel.Parent = this;
			//micPanel.Height = 40;
			micPanel.AutoSize = true;
			micPanel.Dock = DockStyle.Bottom;
			*/

			Label micVolLabel = new Label();
			micVolLabel.Text = "Mic volume";
			micVolLabel.Anchor = AnchorStyles.Left;
			micVolLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

			Label micVolLabel2 = new Label();
			micVolLabel2.Text = "%";
			micVolLabel2.Anchor = AnchorStyles.Left;
			micVolLabel2.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			
			micVol = new NumericUpDown();
			micVol.Maximum = 100;
			micVol.Minimum = 0;
			micVol.Width = 60;
			micVol.ReadOnly = true;
			micVol.Enabled = false;
			micVol.ValueChanged += UserMicVol;
			micVol.Anchor = AnchorStyles.Left;

			miccntrl.Controls.Add(micVolLabel, 0, 0);
			miccntrl.Controls.Add(micVol, 1, 0);
			miccntrl.Controls.Add(micVolLabel2, 2, 0);

			Label corLbll = new Label();
			corLbll.Anchor = AnchorStyles.Left;
			corLbll.Text = "Correction count";
			corCountLabel = new Label();
			corCountLabel.Anchor = AnchorStyles.Left;
			corCountLabel.Text = 0.ToString();
			miccntrl.Controls.Add(corLbll, 3, 0);
			miccntrl.Controls.Add(corCountLabel, 4, 0);
			#endregion

			#region Main Window row 3, microphone device enumeration
			micList = new ListView();
			micList.Width = lrows.Width-3; // 3 for the bevel, but how to do this "right"?
 			micList.View = View.Details;
			micList.Columns.Add("#",20);
			micList.Columns.Add("Name",200);
			micList.Columns.Add("GUID", 200);

			lrows.Controls.Add(micList,0,2);
			#endregion

			//layout.Visible = true;

			/*
			Label micVolLabel = new Label();
			micVolLabel.Parent = micPanel;
			micVolLabel.AutoSize = true;
			//micVolLabel.Location = Location.
			*/

			/*
			TrackBar tb = new TrackBar();
			tb.Parent = micPanel;
			tb.TickStyle = TickStyle.Both;
			//tb.Size = new Size(150, 25);
			//tb.Height 
			tb.Dock = DockStyle.Fill; // fills parent
									  //tb.Location = new Point(0, 0); // insert point
									  //tb.Anchor = AnchorStyles.Right;
			*/
		}

		// constructor
		public MainWindow()
		{
			FormClosing += WindowClose;

			MakeTrayIcon();

			BuildUI();

			//micAdjustLock = new System.Threading.Semaphore(0, 1);
			MicEnum();

			// TODO: Detect mic device changes
			// TODO: Delay fixing by 5 seconds to prevent fix diarrhea

			// the form itself
			WindowState = FormWindowState.Normal;
			FormBorderStyle = FormBorderStyle.FixedDialog; // no min/max buttons as wanted
			MinimizeBox = false;
			MaximizeBox = false;
			Hide();
			//CenterToScreen();

			//nicon.ShowBalloonTip(3000, "TaskMaster!", "TaskMaster is running!" + Environment.NewLine + "Right click on the icon for more options.", ToolTipIcon.Info);
		}

		/*
		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
		}
		*/

		/*
		protected override void Dispose(bool disposing)
		{
			base.Dispose(disposing);
			//cleanup
		}
		*/
	}

	//[Guid("088f7210-51b2-4e06-9bd4-93c27a973874")]//there's no point to this, is there?
	public class TaskMaster
	{
		// entry point to the application
		//[STAThread] // supposedly needed to avoid shit happening with the WinForms GUI. Haven't noticed any of that shit.
		static public void Main()
		{
			MicMonitor mon = new MicMonitor();
			MainWindow tmw = new MainWindow();
			tmw.setMicMonitor(mon);

			#if DEBUG
			tmw.Show();
			#endif

			Application.Run();
		}
	}
}