//
// MicManager.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2016-2018 M.A.
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
using System.Collections.Generic;
using System.Linq;
using Serilog;

namespace Taskmaster
{
	sealed public class VolumeChangedEventArgs : EventArgs
	{
		public double Old { get; set; }
		public double New { get; set; }
		public int Corrections { get; set; }
	}

	sealed public class MicManager : IDisposable
	{
		public event EventHandler<VolumeChangedEventArgs> VolumeChanged;

		double _target;
		public double Target
		{
			get
			{
				return _target;
			}
			set
			{
				// Bounding
				if (Maximum < value)
					value = Maximum;
				else if (Minimum > value) value = Minimum;

				_target = value;

				Log.Information("<Microphone> Target volume set to: {Volume:N1}%", value);
			}
		}

		public double Minimum { get; } = 0;
		public double Maximum { get; } = 100;

		public double VolumeHysterisis { get; } = 0.05;
		public double SmallVolumeHysterisis { get; } = 0.05 / 4;
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

				if (Taskmaster.DebugMic)
					Log.Debug("<Microphone> DEBUG Volume = {Volume:N1}% (actual: {ActualVolume:N1}%)", value, Control.Percent);
			}
		}

		NAudio.CoreAudioApi.MMDevice m_dev;

		public string DeviceName => m_dev.DeviceFriendlyName;

		SharpConfig.Configuration stats;
		const string statfile = "Microphone.Statistics.ini";
		
		// ctor, constructor
		public MicManager()
		{
			System.Diagnostics.Debug.Assert(Taskmaster.IsMainThread(), "Requires main thread");

			// Target = Maximum; // superfluous; CLEANUP

			stats = Taskmaster.Config.Load(statfile);
			// there should be easier way for this, right?
			Corrections = (stats.Contains("Statistics") && stats["Statistics"].Contains("Corrections")) ? stats["Statistics"]["Corrections"].IntValue : 0;

			// DEVICES

			// find control interface
			// FIXME: Deal with multiple recording devices.
			var waveInDeviceNumber = IntPtr.Zero; // 0 is default or first?
			var mixerLine = new NAudio.Mixer.MixerLine(waveInDeviceNumber, 0, NAudio.Mixer.MixerFlags.WaveIn);

			Control = (NAudio.Mixer.UnsignedMixerControl)mixerLine.Controls.FirstOrDefault(
				(control) => control.ControlType == NAudio.Mixer.MixerControlType.Volume
			);

			if (Control == null)
			{
				Log.Error("<Microphone> No volume control acquired!");
				throw new InitFailure("Mic monitor control not acquired.");
			}

			_volume = Control.Percent;

			// get default communications device
			var mm_enum = new NAudio.CoreAudioApi.MMDeviceEnumerator();
			m_dev = mm_enum?.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.Role.Communications);
			if (m_dev != null) m_dev.AudioEndpointVolume.OnVolumeNotification += VolumeChangedHandler;
			mm_enum = null; // unnecessary?

			if (m_dev == null)
			{
				Log.Error("<Microphone> No communications device found!");
				throw new InitFailure("No communications device found");
			}

			var mvol = "Microphone volume";
			var save = false || !Taskmaster.cfg["Media"].Contains(mvol);
			var defaultvol = Taskmaster.cfg["Media"].GetSetDefault(mvol, 100d).DoubleValue;
			if (save) Taskmaster.Config.Save(Taskmaster.cfg);

			var fname = "Microphone.Devices.ini";
			var vname = "Volume";

			var devcfg = Taskmaster.Config.Load(fname);
			var guid = (m_dev.ID.Split('}'))[1].Substring(2);
			var devname = m_dev.DeviceFriendlyName;
			var unset = !(devcfg[guid].Contains(vname));
			var devvol = devcfg[guid].GetSetDefault(vname, defaultvol).DoubleValue;
			devcfg[guid]["Name"].StringValue = devname;
			if (unset) Taskmaster.Config.Save(devcfg);

			Target = devvol;
			Log.Information("<Microphone> Default device: {Device} (volume: {TargetVolume:N1}%)", m_dev.FriendlyName, Target);
			Volume = Target;

			Log.Information("<Microphone> Component loaded.");
		}

		bool disposed; // false
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this); // why?
		}

		void Dispose(bool disposing)
		{
			if (disposed) return;

			if (disposing)
			{
				if (Taskmaster.Trace) Log.Verbose("Disposing microphone monitor...");

				if (m_dev != null)
				{
					m_dev.AudioEndpointVolume.OnVolumeNotification -= VolumeChangedHandler;
					m_dev = null;
				}

				if (micstatsdirty)
				{
					stats["Statistics"]["Corrections"].IntValue = Corrections;
					Taskmaster.Config.Save(stats);
				}
			}

			disposed = true;
		}

		/// <summary>
		/// Enumerate this instance.
		/// </summary>
		/// <returns>Enumeration of audio input devices as GUID/FriendlyName string pair.</returns>
		public List<MicDevice> enumerate()
		{
			if (Taskmaster.Trace) Log.Verbose("<Microphone> Enumerating devices...");

			var devices = new List<MicDevice>();
			var mm_enum = new NAudio.CoreAudioApi.MMDeviceEnumerator();
			if (mm_enum != null)
			{
				var devs = mm_enum.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.DeviceState.Active);
				foreach (var dev in devs)
				{
					var mdev = new MicDevice { Name = dev.DeviceFriendlyName, GUID = dev.ID.Split('}')[1].Substring(2) };
					devices.Add(mdev);
					if (Taskmaster.Trace) Log.Verbose("<Microphone> Device: {Microphone} [GUID: {GUID}]", mdev.Name, mdev.GUID);
				}
			}

			if (Taskmaster.Trace) Log.Verbose("<Microphone> {DeviceCount} microphone(s)", devices.Count);

			return devices;
		}

		// TODO: Add device enumeration
		// TODO: Add device selection
		// TODO: Add per device behaviour

		public int Corrections { get; set; }
		bool micstatsdirty; // false

		static int correcting; // = 0;
		async void VolumeChangedHandler(NAudio.CoreAudioApi.AudioVolumeNotificationData data)
		{
			var oldVol = Volume;
			double newVol = data.MasterVolume * 100;

			if (Math.Abs(newVol - Target) <= SmallVolumeHysterisis)
			{
				if (Taskmaster.ShowInaction)
					Log.Verbose("<Microphone> Volume change too small ({VolumeChange:N1}%) to act on.", Math.Abs(newVol - Target));
				return;
			}

			if (Taskmaster.Trace) Log.Verbose("<Microphone> Volume changed from {OldVolume:N1}% to {NewVolume:N1}%", oldVol, newVol);

			// This is a light HYSTERISIS limiter in case someone is sliding a volume bar around,
			// we act on it only once every [AdjustDelay] ms.
			// HOPEFULLY there are no edge cases with this triggering just before last adjustment
			// and the notification for the last adjustment coming slightly before. Seems super unlikely tho.
			// TODO: Delay this even more if volume is changed ~2 seconds before we try to do so.
			if (Math.Abs(newVol - Target) >= VolumeHysterisis) // Volume != Target for double
			{
				if (Taskmaster.Trace) Log.Verbose("<Microphone> DEBUG: Volume changed = [{OldVolume:N1} -> {NewVolume:N1}], Off.Target: {VolumeOffset:N1}",
												  oldVol, newVol, Math.Abs(newVol - Target));

				if (Atomic.Lock(ref correcting))
				{
					await System.Threading.Tasks.Task.Delay(AdjustDelay); // actual hysterisis, this should be cancellable

					oldVol = Control.Percent;
					Log.Information("<Microphone> Correcting volume from {OldVolume:N1} to {NewVolume:N1}", oldVol, Target);
					Volume = Target;
					Corrections += 1;
					micstatsdirty = true;
					correcting = 0;

					VolumeChanged?.Invoke(this, new VolumeChangedEventArgs { Old = oldVol, New = Target, Corrections = Corrections });
				}
				else
				{
					if (Taskmaster.Trace) Log.Verbose("<Microphone> DEBUG CorrectionAlreadyQueued");
				}
			}
			else
			{
				if (Taskmaster.Trace) Log.Verbose("<Microphone> DEBUG NotCorrected");
			}
		}
	}
}