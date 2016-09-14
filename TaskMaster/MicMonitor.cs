//
// MicMonitor.cs
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

using System.Collections.Generic;
using NAudio.CoreAudioApi;

namespace TaskMaster
{
	using System;
	using devicePair = KeyValuePair<string, string>;

	public class VolumeChangedEventArgs : EventArgs
	{
		public double Old { get; set; }
		public double New { get; set; }
		public int Corrections { get; set; }
		public bool Corrected { get; set; }
	}

	public class MicMonitor
	{
		static NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

		public event EventHandler<VolumeChangedEventArgs> VolumeChanged;

		public double Target = Maximum;
		public const double Minimum = 0;
		public const double Maximum = 100;

		public int AdjustDelay = 5000;

		NAudio.Mixer.UnsignedMixerControl Control = null;
		NAudio.CoreAudioApi.MMDevice m_dev = null;

		public string DeviceName
		{
			get
			{
				return m_dev.DeviceFriendlyName;
			}
		}

		SharpConfig.Configuration stats;
		const string statfile = "MicMon.Statistics.ini";
		// ctor, constructor
		public MicMonitor()
		{
			Log.Trace("Starting...");
			stats = TaskMaster.loadConfig(statfile);
			// there should be easier way for this, right?
			corrections = (stats.Contains("Statistics") && stats["Statistics"].Contains("Corrections")) ? stats["Statistics"]["Corrections"].IntValue : 0;
			FindDefaultComms();
			Volume = Target;
		}

		~MicMonitor()
		{
			Log.Trace("Destructing");
		}

		void Close()
		{
			stats["Statistics"]["Corrections"].IntValue = corrections;
			TaskMaster.saveConfig(statfile, stats);
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

				// Free any other managed objects here.
				//
			}

			// Free any unmanaged objects here.
			//
			disposed = true;
		}

		async void OnVolumeChanged(VolumeChangedEventArgs e)
		{
			await System.Threading.Tasks.Task.Delay(100); // force async

			EventHandler<VolumeChangedEventArgs> handler = VolumeChanged;
			if (handler != null)
				handler(this, e);
		}

		public List<devicePair> enumerate()
		{
			Log.Trace("Enumerating devices...");
			List<devicePair> devices = new List<devicePair>();
			NAudio.CoreAudioApi.MMDeviceEnumerator mm_enum = new NAudio.CoreAudioApi.MMDeviceEnumerator();
			if (mm_enum != null)
			{
				NAudio.CoreAudioApi.MMDeviceCollection devs = mm_enum.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.DeviceState.Active);
				foreach (var dev in devs)
				{
					string[] parts = dev.ID.Split('}');
					Log.Trace(System.String.Format("{0} [guid: {1}]", dev.DeviceFriendlyName, parts[1].Substring(2)));
					devices.Add(new devicePair(parts[1].Substring(2), dev.DeviceFriendlyName));
				}
			}
			Log.Trace(String.Format("{0} microphone(s)", devices.Count));

			return devices;
		}

		public void stop()
		{
			Log.Trace("Stopping.");
			if (m_dev != null)
				m_dev.AudioEndpointVolume.OnVolumeNotification -= volumeChangeDetected;
		}

		bool setupControl()
		{
			Log.Trace("Setup.");
			int waveInDeviceNumber = 0;
			var mixerLine = new NAudio.Mixer.MixerLine((IntPtr)waveInDeviceNumber, 0, NAudio.Mixer.MixerFlags.WaveIn);

			foreach (var control in mixerLine.Controls)
			{
				if (control.ControlType == NAudio.Mixer.MixerControlType.Volume)
				{
					Control = control as NAudio.Mixer.UnsignedMixerControl;
					Volume = Control.Percent;
					break;
				}
			}
			if (Control == null)
				Log.Error("No volume control acquired!");

			return (Control != null);
		}

		NAudio.CoreAudioApi.MMDevice FindDefaultComms()
		{
			if (m_dev != null)
			{
				m_dev.AudioEndpointVolume.OnVolumeNotification -= volumeChangeDetected;
				m_dev = null;
			}

			setupControl();

			if (Control != null)
			{
				// get default communications device
				NAudio.CoreAudioApi.MMDeviceEnumerator mm_enum = new NAudio.CoreAudioApi.MMDeviceEnumerator();
				if (mm_enum != null)
				{
					m_dev = mm_enum.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.Role.Communications);
					if (m_dev != null)
						m_dev.AudioEndpointVolume.OnVolumeNotification += volumeChangeDetected;
				}
			}

			if (m_dev != null)
				Log.Info(String.Format("Default communications device: {0}", m_dev.FriendlyName));
			else
				Log.Error("No communications device found!");
			return m_dev;
		}

		// TODO: Add device enumeration
		// TODO: Add device selection
		// TODO: Add per device behaviour

		int corrections = 0;
		public int getCorrections()
		{
			return corrections;
		}

		static int correcting = 0;
		void volumeChangeDetected(NAudio.CoreAudioApi.AudioVolumeNotificationData data)
		{
			double oldVol = Volume;
			Volume = data.MasterVolume * 100;
			OnVolumeChanged(new VolumeChangedEventArgs { Old = oldVol, New = Volume, Corrections = corrections, Corrected=false });

			// This is a light HYSTERISIS limiter in case someone is sliding a volume bar around,
			// we act on it only once every [AdjustDelay] ms.
			// HOPEFULLY there are no edge cases with this triggering just before last adjustment
			// and the notification for the last adjustment coming slightly before. Seems super unlikely tho.
			// TODO: Delay this even more if volume is changed ~2 seconds before we try to do so.
			if (correcting==0 && Math.Abs(Volume-Target) > 0.05) // Volume != Target for double
			{
				Log.Info(String.Format("{0:N1}% -> {1:N1}% {2}", oldVol, Volume, Math.Abs(Volume - Target)));
				if (System.Threading.Interlocked.CompareExchange(ref correcting, 1, 0) == 0) // correcting==0, set it to 1, return original 0
				{
					//Log.Trace("Thread ID (dispatch): " + System.Threading.Thread.CurrentThread.ManagedThreadId);
					System.Threading.Tasks.Task.Run(async () =>
					{
						//Log.Trace("Thread ID (task): "+ System.Threading.Thread.CurrentThread.ManagedThreadId);
						await System.Threading.Tasks.Task.Delay(AdjustDelay);
						Volume = Target;
						corrections += 1;
						System.Threading.Interlocked.Exchange(ref correcting, 0);
						OnVolumeChanged(new VolumeChangedEventArgs { Old = oldVol, New = Volume, Corrections = corrections, Corrected=true});
					});
				}
			}
		}

		double _volume = 0;
		public double Volume
		{
			get
			{
				return _volume;
			}
			set
			{
				Log.Info(String.Format("Setting volume to {0:N1}%", value));
				if (Control != null)
				{
					if (Math.Abs(Control.Percent - value) > 0.05)
					{
						Control.Percent = value;
						_volume = value;
					}
					else
						Log.Warn("Volume already at target.");
				}
				else
					Log.Error("Volume control not set up."); // this should never happen, checking for it is a little superfluous as such
			}
		}
	}
}

