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

namespace TaskMaster
{
	using System;
	using System.Collections.Generic;

	public class VolumeChangedEventArgs : EventArgs
	{
		public double Old { get; set; }
		public double New { get; set; }
		public int Corrections { get; set; }
	}

	public class MicMonitor
	{
		static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

		public event EventHandler<VolumeChangedEventArgs> VolumeChanged;

		double _target = Maximum;
		public double Target
		{
			get
			{
				return _target;
			}
			set
			{
				if (Maximum >= value && value >= Minimum)
				{
					_target = value;

					Log.Info(string.Format("Microphone target volume set to: {0:N1}%", value));
				}
			}
		}
		public const double Minimum = 0;
		public const double Maximum = 100;

		public double VolumeHysterisis { get; } = 0.05;
		public double SmallVolumeHysterisis { get; } = 0.05/4;
		public int AdjustDelay { get; } = 5000;

		readonly NAudio.Mixer.UnsignedMixerControl Control;

		double _volume;
		public double Volume
		{
			/// <summary>
			/// Return cached volume. Use Control.Percent for actual current volume.
			/// </summary>
			/// <returns>The volume.</returns>
			get
			{
				// We need this to not directly refer to Control.Percent to avoid adding extra cache variables and managing them.
				// Much easier this way since you still have Control.Percent for up-to-date volume where necessary (shouldn't be, the _volume is updated decently).
				return _volume;
			}
			/// <summary>
			/// This will trigger VolumeChangedEvent so make sure you don't do infinite loops.
			/// </summary>
			/// <param name="value">New volume as 0 to 100 double</param>
			set
			{
				Control.Percent = _volume = value;

				Log.Trace(string.Format("Mic.Volume = {0:N1}% (actual: {1:N1}%)", value, Control.Percent));
			}
		}


		NAudio.CoreAudioApi.MMDevice m_dev;

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
			stats = TaskMaster.loadConfig(statfile);
			// there should be easier way for this, right?
			Corrections = (stats.Contains("Statistics") && stats["Statistics"].Contains("Corrections")) ? stats["Statistics"]["Corrections"].IntValue : 0;

			// find control interface
			// FIXME: Deal with multiple recording devices.
			IntPtr waveInDeviceNumber = IntPtr.Zero; // 0 is default or first?
			var mixerLine = new NAudio.Mixer.MixerLine(waveInDeviceNumber, 0, NAudio.Mixer.MixerFlags.WaveIn);

			foreach (var control in mixerLine.Controls)
			{
				if (control.ControlType == NAudio.Mixer.MixerControlType.Volume)
				{
					Control = control as NAudio.Mixer.UnsignedMixerControl;
					break;
				}
			}

			if (Control == null)
			{
				Log.Error("No volume control acquired!");
				throw new InitFailure("Mic monitor control not acquired.");
			}

			_volume = Control.Percent;

			// get default communications device
			var mm_enum = new NAudio.CoreAudioApi.MMDeviceEnumerator();
			m_dev = mm_enum?.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.Role.Communications);
			if (m_dev != null)
				m_dev.AudioEndpointVolume.OnVolumeNotification += VolumeChangedHandler;
			mm_enum = null; // unnecessary?

			if (m_dev == null)
			{
				Log.Error("No communications device found!");
				throw new InitFailure("No communications device found");
			}

			var mvol = "Microphone volume";
			bool save = false || !TaskMaster.cfg["Media"].Contains(mvol);
			double defaultvol = TaskMaster.cfg["Media"].GetSetDefault(mvol, 100d).DoubleValue;
			if (save) TaskMaster.saveConfig("Core.ini", TaskMaster.cfg);

			string fname = "MicMon.Devices.ini";
			string vname = "Volume";

			SharpConfig.Configuration devcfg = TaskMaster.loadConfig(fname);
			string guid = (m_dev.ID.Split('}'))[1].Substring(2);
			string devname = m_dev.DeviceFriendlyName;
			bool unset = !(devcfg[guid].Contains(vname));
			double devvol = devcfg[guid].GetSetDefault(vname, defaultvol).DoubleValue;
			devcfg[guid]["Name"].StringValue = devname;
			if (unset) TaskMaster.saveConfig(fname, devcfg);

			Log.Info(string.Format("Communications device: {0} (volume: {1:N1}%)", m_dev.FriendlyName, devvol));

			Volume = Target = devvol;
		}

		bool disposed; // false
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposed)
				return;

			if (disposing)
			{
				if (m_dev != null)
				{
					m_dev.AudioEndpointVolume.OnVolumeNotification -= VolumeChangedHandler;
					m_dev = null;
				}

				if (micstatsdirty)
				{
					stats["Statistics"]["Corrections"].IntValue = Corrections;
					TaskMaster.saveConfig(statfile, stats);
				}
			}

			disposed = true;
		}

		public List<KeyValuePair<string, string>> enumerate()
		{
			Log.Trace("Enumerating devices...");

			var devices = new List<KeyValuePair<string, string>>();
			var mm_enum = new NAudio.CoreAudioApi.MMDeviceEnumerator();
			if (mm_enum != null)
			{
				NAudio.CoreAudioApi.MMDeviceCollection devs = mm_enum.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.DeviceState.Active);
				foreach (var dev in devs)
				{
					string[] parts = dev.ID.Split('}');
					Log.Trace(string.Format("{0} [guid: {1}]", dev.DeviceFriendlyName, parts[1].Substring(2)));
					devices.Add(new KeyValuePair<string, string>(parts[1].Substring(2), dev.DeviceFriendlyName));
				}
			}
			Log.Trace(string.Format("{0} microphone(s)", devices.Count));

			return devices;
		}

		// TODO: Add device enumeration
		// TODO: Add device selection
		// TODO: Add per device behaviour

		public int Corrections { get; set; }
		bool micstatsdirty; // false

		static int correcting = 0;

		void VolumeChangedHandler(NAudio.CoreAudioApi.AudioVolumeNotificationData data)
		{
			double oldVol = _volume;
			double newVol = data.MasterVolume * 100;

			if (Math.Abs(newVol - Target) <= SmallVolumeHysterisis)
				return;

			if (TaskMaster.VeryVerbose)
				Log.Trace(string.Format("Mic volume changed from {0:N1}% to {1:N1}%", oldVol, newVol));

			// This is a light HYSTERISIS limiter in case someone is sliding a volume bar around,
			// we act on it only once every [AdjustDelay] ms.
			// HOPEFULLY there are no edge cases with this triggering just before last adjustment
			// and the notification for the last adjustment coming slightly before. Seems super unlikely tho.
			// TODO: Delay this even more if volume is changed ~2 seconds before we try to do so.
			if (Math.Abs(newVol - Target) >= VolumeHysterisis) // Volume != Target for double
			{
				Log.Trace(string.Format("Mic.Volume.Changed = [{0:N1} -> {1:N1}], Off.Target: {2:N1}", oldVol, newVol, Math.Abs(newVol - Target)));

				if (System.Threading.Interlocked.CompareExchange(ref correcting, 1, 0) == 0)
				{
					//Log.Trace("Thread ID (dispatch): " + System.Threading.Thread.CurrentThread.ManagedThreadId);
					System.Threading.Tasks.Task.Run(async () =>
					{
						//Log.Trace("Thread ID (task): "+ System.Threading.Thread.CurrentThread.ManagedThreadId);
						await System.Threading.Tasks.Task.Delay(AdjustDelay); // actual hysterisis, this should be cancellable
						oldVol = Control.Percent;
						Log.Info(string.Format("Correcting microphone volume from {0:N1} to {1:N1}", oldVol, Target));
						Volume = Target;
						Corrections += 1;
						micstatsdirty = true;
						correcting = 0;

						VolumeChanged?.Invoke(this, new VolumeChangedEventArgs { Old = oldVol, New = Target, Corrections = Corrections });
					});
				}
				else
				{
					if (TaskMaster.VeryVerbose)
						Log.Trace("Mic.Volume.CorrectionAlreadyQueued");
				}
			}
			else
			{
				if (TaskMaster.VeryVerbose)
					Log.Trace("Mic.Volume.NotCorrected");
			}
		}
	}
}

