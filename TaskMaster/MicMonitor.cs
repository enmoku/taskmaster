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

using System.Windows.Forms;

namespace TaskMaster
{
	public class VolumeChangedEventArgs : System.EventArgs
	{
		public double Volume { get; set; }
	}

	public class MicMonitor
	{
		public event System.EventHandler<VolumeChangedEventArgs> VolumeChanged;

		public double Volume = 0;
		public const double Minimum = 0;
		public const double Maximum = 100;

		public int AdjustDelay = 5000;

		NAudio.Mixer.UnsignedMixerControl Control = null;
		NAudio.CoreAudioApi.MMDevice m_dev = null;
		NAudio.CoreAudioApi.MMDeviceEnumerator m_enum = null;

		public MicMonitor()
		{
			setupDefaultComms();
		}

		protected virtual void OnVolumeChanged(VolumeChangedEventArgs e)
		{
			System.EventHandler<VolumeChangedEventArgs> handler = VolumeChanged;
			if (handler != null)
			{
				handler(this, e);
			}
		}

		public void stop()
		{
			if (m_dev != null)
			{
				m_dev.AudioEndpointVolume.OnVolumeNotification -= volumeChangeDetected;
			}
		}

		public void minimize()
		{
			m_enum = null;
			//m_dev = null; //needed for volume monitoring
		}

		bool setupControl()
		{
			int waveInDeviceNumber = 0;
			var mixerLine = new NAudio.Mixer.MixerLine((System.IntPtr)waveInDeviceNumber, 0, NAudio.Mixer.MixerFlags.WaveIn);

			foreach (var control in mixerLine.Controls)
			{
				if (control.ControlType == NAudio.Mixer.MixerControlType.Volume)
				{
					Control = control as NAudio.Mixer.UnsignedMixerControl;
					Volume = Control.Percent;
					break;
				}
			}
			return (Control != null);
		}

		NAudio.CoreAudioApi.MMDevice setupDefaultComms()
		{
			if (m_dev != null)
			{
				m_dev.AudioEndpointVolume.OnVolumeNotification -= volumeChangeDetected;
				m_dev = null;
			}

			setupControl();

			if (Control != null)
			{
				setVolume(Maximum);

				// get default communications device
				if (m_enum == null)
				{
					m_enum = new NAudio.CoreAudioApi.MMDeviceEnumerator();
				}
				m_dev = m_enum.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.Role.Communications);
				m_dev.AudioEndpointVolume.OnVolumeNotification += volumeChangeDetected;
			}
			return m_dev;
		}

		// TODO: Add device enumeration
		// TODO: Add device selection
		// TODO: Add per device behaviour

		static int correcting = 0;
		void volumeChangeDetected(NAudio.CoreAudioApi.AudioVolumeNotificationData data)
		{
			Volume = data.MasterVolume * 100;

			// This is a light hysterisis limiter in case someone is sliding a volume bar around,
			// we act on it only once every [AdjustDelay] ms.
			// HOPEFULLY there are no edge cases with this triggering just before last adjustment
			// and the notification for the last adjustment coming slightly before. Seems super unlikely tho.
			if (System.Threading.Interlocked.CompareExchange(ref correcting, 1, 0) == 0) // correcting==0, set it to 1, return original 0
			{
				System.Threading.Tasks.Task.Run(async () =>
				{
					await System.Threading.Tasks.Task.Delay(AdjustDelay);
					setVolume(Maximum);
					var e = new VolumeChangedEventArgs();
					e.Volume = Volume;
					OnVolumeChanged(e);
					System.Threading.Interlocked.Exchange(ref correcting, 0);
				});
			}
		}

		public void setVolume(double volume)
		{
			if (Control != null)
			{
				if (Control.Percent < Maximum)
				{
					Control.Percent = volume;
					Volume = volume;
				}
			}
		}

		public string DeviceName
		{
			get
			{
				return m_dev.DeviceFriendlyName;
			}
		}
	}
}

