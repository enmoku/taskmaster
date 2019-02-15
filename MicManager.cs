//
// MicManager.cs
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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MKAh;
using NAudio.CoreAudioApi;
using Serilog;

namespace Taskmaster
{
	sealed public class MicManager : IDisposable
	{
		public event EventHandler<VolumeChangedEventArgs> VolumeChanged;

		public bool Control { get; private set; } = false;

		double _target = 50d;
		public double Target
		{
			get => _target;
			set => _target = value.Constrain(Minimum, Maximum);
		}

		public const double Minimum = 0d;
		public const double Maximum = 100d;

		public const double VolumeHysterisis = 0.05d;
		public const double SmallVolumeHysterisis = VolumeHysterisis / 4d;
		TimeSpan AdjustDelay { get; } = TimeSpan.FromSeconds(5);

		NAudio.Mixer.UnsignedMixerControl VolumeControl = null;

		double _volume;
		public double Volume
		{
			/// <summary>
			/// Return cached volume. Use Control.Percent for actual current volume.
			/// </summary>
			/// <returns>The volume.</returns>
			// We need this to not directly refer to Control.Percent to avoid adding extra cache variables and managing them.
			// Much easier this way since you still have Control.Percent for up-to-date volume where necessary (shouldn't be, the _volume is updated decently).
			get => _volume;
			/// <summary>
			/// This will trigger VolumeChangedEvent so make sure you don't do infinite loops.
			/// </summary>
			/// <param name="value">New volume as 0 to 100 double</param>
			set
			{
				VolumeControl.Percent = _volume = value;

				if (Taskmaster.DebugMic)
					Log.Debug($"<Microphone> DEBUG Volume = {value:N1} % (actual: {VolumeControl.Percent:N1} %)");
			}
		}

		NAudio.CoreAudioApi.MMDevice m_dev;

		public string DeviceName => m_dev.DeviceFriendlyName;

		NAudio.CoreAudioApi.MMDeviceEnumerator mm_enum = null;

		// ctor, constructor
		/// <exception cref="InitFailure">When initialization fails in a way that can not be continued from.</exception>
		public MicManager()
		{
			Debug.Assert(Taskmaster.IsMainThread(), "Requires main thread");

			// Target = Maximum; // superfluous; CLEANUP

			// DEVICES

			// find control interface
			// FIXME: Deal with multiple recording devices.
			var waveInDeviceNumber = IntPtr.Zero; // 0 is default or first?

			NAudio.Mixer.MixerLine mixerLine = null;
			try
			{
				mixerLine = new NAudio.Mixer.MixerLine(waveInDeviceNumber, 0, NAudio.Mixer.MixerFlags.WaveIn);
			}
			catch (NAudio.MmException ex)
			{
				Log.Fatal("<Microphone> Default device not found.");
				throw new InitFailure("Failed to get default microphone device.", ex);
			}

			VolumeControl = (NAudio.Mixer.UnsignedMixerControl)mixerLine.Controls.FirstOrDefault(
				(control) => control.ControlType == NAudio.Mixer.MixerControlType.Volume
			);

			if (VolumeControl == null)
			{
				Log.Error("<Microphone> No volume control acquired!");
				throw new InitFailure("Mic monitor control not acquired.");
			}

			_volume = VolumeControl.Percent;

			// get default communications device
			mm_enum = new NAudio.CoreAudioApi.MMDeviceEnumerator();
			m_dev = mm_enum?.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.Role.Communications);
			if (m_dev != null)
			{
				m_dev.AudioEndpointVolume.OnVolumeNotification += VolumeChangedHandler;
			}

			if (m_dev == null)
			{
				Log.Error("<Microphone> No communications device found!");
				throw new InitFailure("No communications device found");
			}

			var mvol = "Default recording volume";
			var mcontrol = "Recording volume control";

			var corecfg = Taskmaster.Config.Load(Taskmaster.coreconfig);

			var mediasec = corecfg.Config["Media"];

			bool dirty = !mediasec.Contains(mcontrol) || !mediasec.Contains(mvol);
			Control = mediasec.GetSetDefault(mcontrol, false).BoolValue;
			var defaultvol = mediasec.GetSetDefault(mvol, 100d).DoubleValue;

			mediasec.Remove("Microphone volume"); // DEPRECATED

			var fname = "Microphone.Devices.ini";
			var vname = "Volume";

			var devcfg = Taskmaster.Config.Load(fname);
			var guid = AudioManager.AudioDeviceIdToGuid(m_dev.ID);
			var devname = m_dev.DeviceFriendlyName;
			dirty |= !(devcfg.Config[guid].Contains(vname));
			var devvol = devcfg.Config[guid].GetSetDefault(vname, defaultvol).DoubleValue;
			devcfg.Config[guid]["Name"].StringValue = devname;
			if (dirty)
			{
				devcfg.MarkDirty();
				devcfg.Save(force: true);
			}
			
			Target = devvol.Constrain(0, 100);
			Log.Information($"<Microphone> Default device: {m_dev.FriendlyName} (volume: {Target:N1} %) – Control: {(Control?"Enabled":"Disabled")}");

			if (Control) Volume = Target;

			if (Taskmaster.DebugMic) Log.Information("<Microphone> Component loaded.");

			Taskmaster.DisposalChute.Push(this);
		}

		/// <summary>
		/// Enumerate this instance.
		/// </summary>
		/// <returns>Enumeration of audio input devices as GUID/FriendlyName string pair.</returns>
		public List<MicDevice> enumerate()
		{
			if (Taskmaster.Trace) Log.Verbose("<Microphone> Enumerating devices...");

			var devices = new List<MicDevice>();

			if (DisposedOrDisposing) return devices;

			var mm_enum = new NAudio.CoreAudioApi.MMDeviceEnumerator();
			if (mm_enum != null)
			{
				var devs = mm_enum.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.DeviceState.Active);
				foreach (var dev in devs)
				{
					var mdev = new MicDevice { Name = dev.DeviceFriendlyName, GUID = AudioManager.AudioDeviceIdToGuid(dev.ID) };
					devices.Add(mdev);
					if (Taskmaster.Trace) Log.Verbose("<Microphone> Device: " + mdev.Name + " [GUID: " + mdev.GUID + "]");
				}
			}

			if (Taskmaster.Trace) Log.Verbose("<Microphone> " + devices.Count + " microphone(s)");

			return devices;
		}

		// TODO: Add device enumeration
		// TODO: Add device selection
		// TODO: Add per device behaviour

		public int Corrections { get; private set; } = 0;

		int correcting_lock; // = 0;
		async void VolumeChangedHandler(NAudio.CoreAudioApi.AudioVolumeNotificationData data)
		{
			if (DisposedOrDisposing) return;

			var oldVol = Volume;
			double newVol = data.MasterVolume * 100;

			if (Math.Abs(newVol - Target) <= SmallVolumeHysterisis)
			{
				if (Taskmaster.ShowInaction && Taskmaster.DebugMic)
					Log.Verbose($"<Microphone> Volume change too small ({Math.Abs(newVol - Target):N1} %) to act on.");
				return;
			}

			if (Taskmaster.Trace) Log.Verbose($"<Microphone> Volume changed from {oldVol:N1} % to {newVol:N1} %");

			// This is a light HYSTERISIS limiter in case someone is sliding a volume bar around,
			// we act on it only once every [AdjustDelay] ms.
			// HOPEFULLY there are no edge cases with this triggering just before last adjustment
			// and the notification for the last adjustment coming slightly before. Seems super unlikely tho.
			// TODO: Delay this even more if volume is changed ~2 seconds before we try to do so.
			if (Math.Abs(newVol - Target) >= VolumeHysterisis) // Volume != Target for double
			{
				if (Taskmaster.Trace)
					Log.Verbose($"<Microphone> DEBUG: Volume changed = [{oldVol:N1} → {newVol:N1}], Off.Target: {Math.Abs(newVol - Target):N1}");

				if (Atomic.Lock(ref correcting_lock))
				{
					try
					{
						await System.Threading.Tasks.Task.Delay(AdjustDelay); // actual hysterisis, this should be cancellable
						if (Control)
						{
							oldVol = VolumeControl.Percent;
							Log.Information($"<Microphone> Correcting volume from {oldVol:N1} to {Target:N1}");
							Volume = Target;
							Corrections += 1;
							VolumeChanged?.Invoke(this, new VolumeChangedEventArgs { Old = oldVol, New = Target, Corrections = Corrections });
						}
						else
						{
							Log.Debug($"<Microphone> Volume not corrected from {oldVol:N1}");
						}
					}
					finally
					{
						Atomic.Unlock(ref correcting_lock);
					}
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

		bool disposed = false; // false
		bool DisposedOrDisposing = false;
		public void Dispose()
		{
			Dispose(true);
		}

		void Dispose(bool disposing)
		{
			DisposedOrDisposing = true;

			if (disposed) return;

			if (disposing)
			{
				if (Taskmaster.Trace) Log.Verbose("Disposing microphone monitor...");

				mm_enum?.Dispose();
				mm_enum = null;

				VolumeChanged = null;

				if (m_dev != null)
				{
					m_dev.AudioEndpointVolume.OnVolumeNotification -= VolumeChangedHandler;
					m_dev = null;
				}
			}

			disposed = true;
		}
	}
}