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
using Taskmaster.Events;

namespace Taskmaster
{
	sealed public class MicManager : IDisposable
	{
		public event EventHandler<VolumeChangedEventArgs> VolumeChanged;

		public event EventHandler<Events.AudioDeviceStateEventArgs> StateChanged;

		public event EventHandler<Events.AudioDefaultDeviceEventArgs> DefaultChanged;

		const string deviceFilename = "Microphone.Devices.ini";

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

		NAudio.CoreAudioApi.MMDevice m_dev = null;

		public string DeviceName => m_dev?.FriendlyName ?? string.Empty; // does not fail like m_dev.ID
		public string DeviceGuid { get; private set; } = string.Empty; // accessing m_dev.ID from here causes COMexception... thread local?

		readonly AudioDeviceNotificationClient notificationClient = null;
		NAudio.CoreAudioApi.MMDeviceEnumerator mm_enum = null;

		double DefaultVolume { get; set; } = 100d;

		// ctor, constructor
		/// <exception cref="InitFailure">When initialization fails in a way that can not be continued from.</exception>
		public MicManager()
		{
			Debug.Assert(Taskmaster.IsMainThread(), "Requires main thread");

			mm_enum = new NAudio.CoreAudioApi.MMDeviceEnumerator();

			if (mm_enum == null)
			{
				Log.Fatal("<Microphone> Failed to load multimedia device enumerator.");
				throw new InitFailure("Failed to load multimedia device enumerator");
			}

			RegisterDefaultDevice();

			notificationClient = new AudioDeviceNotificationClient();
			notificationClient.StateChanged += StateChangeProxy;
			notificationClient.DefaultDevice += ChangeDefaultDevice;

			mm_enum.RegisterEndpointNotificationCallback(notificationClient);

			var mvol = "Default recording volume";
			var mcontrol = "Recording volume control";

			var corecfg = Taskmaster.Config.Load(Taskmaster.coreconfig);

			var mediasec = corecfg.Config["Media"];

			bool dirty = false, modified = false;
			Control = mediasec.GetSetDefault(mcontrol, false, out modified).BoolValue;
			dirty |= modified;
			DefaultVolume = mediasec.GetSetDefault(mvol, 100.0d, out modified).DoubleValue.Constrain(0.0d, 100.0d);
			dirty |= modified;

			if (dirty) corecfg.MarkDirty();

			mediasec.Remove("Microphone volume"); // DEPRECATED

			if (Taskmaster.DebugMic) Log.Information("<Microphone> Component loaded.");

			Taskmaster.DisposalChute.Push(this);
		}

		void ChangeDefaultDevice(object sender, AudioDefaultDeviceEventArgs ea)
		{
			if (ea.Flow == DataFlow.Capture && ea.Role == Role.Communications)
			{
				UnregisterDefaultDevice();

				DeviceGuid = ea.GUID;

				if (!string.IsNullOrEmpty(ea.GUID))
					RegisterDefaultDevice();
				else // no default
					Log.Warning("<Microphone> No communications device found!");

				DefaultChanged?.Invoke(this, ea);
			}
		}

		void UnregisterDefaultDevice()
		{
			VolumeControl = null;
			if (m_dev != null) m_dev.AudioEndpointVolume.OnVolumeNotification -= VolumeChangedHandler; // unregister old
			m_dev = null;
		}

		void RegisterDefaultDevice()
		{
			try
			{
				// FIXME: Deal with multiple recording devices.
				var waveInDeviceNumber = IntPtr.Zero; // 0 is default or first?

				// get default communications device
				try
				{
					m_dev = mm_enum.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.Role.Communications);
				}
				catch (System.Runtime.InteropServices.COMException ex)
				{
					Logging.Stacktrace(ex);
					// NOP
				}

				if (m_dev == null)
				{
					Log.Error("<Microphone> No communications device found!");
					return;
				}

				NAudio.Mixer.MixerLine mixerLine = null;
				try
				{
					mixerLine = new NAudio.Mixer.MixerLine(waveInDeviceNumber, 0, NAudio.Mixer.MixerFlags.WaveIn);
				}
				catch (NAudio.MmException)
				{
					Log.Error("<Microphone> Default device not found.");
					m_dev = null;
					return;
				}

				VolumeControl = (NAudio.Mixer.UnsignedMixerControl)mixerLine.Controls.FirstOrDefault(
					(control) => control.ControlType == NAudio.Mixer.MixerControlType.Volume
				);

				_volume = (VolumeControl != null ? VolumeControl.Percent : double.NaN); // kinda hackish

				m_dev.AudioEndpointVolume.OnVolumeNotification += VolumeChangedHandler;

				//

				var vname = "Volume";
				var cname = "Control";

				var guid = AudioManager.AudioDeviceIdToGuid(m_dev.ID);

				var devcfg = Taskmaster.Config.Load(deviceFilename);
				var devsec = devcfg.Config[guid];

				bool dirty = false, modified = false;

				var devname = m_dev.FriendlyName;
				dirty |= !(devsec.Contains(vname));
				var devvol = devsec.GetSetDefault(vname, DefaultVolume, out modified).DoubleValue;
				dirty |= modified;
				bool devcontrol = devsec.GetSetDefault(cname, false, out modified).BoolValue;
				dirty |= modified;
				if (!devsec.Contains("Name"))
				{
					devsec["Name"].StringValue = devname;
					dirty = true;
				}

				if (dirty) devcfg.MarkDirty();

				if (Control && !devcontrol) Control = false; // disable general control if device control is disabled

				Target = devvol.Constrain(0.0d, 100.0d);
				Log.Information($"<Microphone> Default device: {devname} (volume: {Target:N1} %) – Control: {(Control ? "Enabled" : "Disabled")}");

				if (Control) Volume = Target;
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		private void StateChangeProxy(object sender, Events.AudioDeviceStateEventArgs ea)
		{
			StateChanged?.Invoke(this, ea);
		}

		/// <summary>
		/// Enumerate this instance.
		/// </summary>
		/// <returns>Enumeration of audio input devices as GUID/FriendlyName string pair.</returns>
		public List<MicDevice> DeviceList()
		{
			if (Taskmaster.Trace) Log.Verbose("<Microphone> Enumerating devices...");

			var devices = new List<MicDevice>();

			if (DisposedOrDisposing) return devices;

			try
			{
				bool modified = false;
				bool dirty = false;

				var devcfg = Taskmaster.Config.Load(deviceFilename);

					var devs = mm_enum.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.DeviceState.Active);
				foreach (var dev in devs)
				{
					try
					{
						string guid = AudioManager.AudioDeviceIdToGuid(dev.ID);
						var devsec = devcfg.Config[guid];
						if (!devsec.Contains("Name"))
						{
							devsec["Name"].StringValue = dev.DeviceFriendlyName;
							dirty = modified = true;
						}
						bool control = devsec.GetSetDefault("Control", false, out modified).BoolValue;
						dirty |= modified;
						float target = devsec.TryGet("Volume")?.FloatValue ?? float.NaN;

						var mdev = new MicDevice
						{
							Name = dev.DeviceFriendlyName,
							GUID = AudioManager.AudioDeviceIdToGuid(dev.ID),
							VolumeControl = control,
							Target = target,
							Volume = dev.AudioSessionManager.SimpleAudioVolume.Volume * 100d, // simpleaudiovolume is 0.0 to 1.0 instead of 0.0 to 100.0
							State = dev.State,
						};

						devices.Add(mdev);

						if (Taskmaster.Trace) Log.Verbose("<Microphone> Device: " + mdev.Name + " [GUID: " + mdev.GUID + "]");
					}
					catch (OutOfMemoryException) { throw; }
					catch (Exception ex)
					{
						Logging.Stacktrace(ex);
					}
				}

				if (dirty) devcfg.MarkDirty();

				if (Taskmaster.Trace) Log.Verbose("<Microphone> " + devices.Count + " microphone(s)");
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}

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

				mm_enum?.UnregisterEndpointNotificationCallback(notificationClient);  // unnecessary?

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

	class AudioDeviceNotificationClient : NAudio.CoreAudioApi.Interfaces.IMMNotificationClient
	{
		public event EventHandler<Events.AudioDefaultDeviceEventArgs> DefaultDevice;
		public event EventHandler Changed;
		public event EventHandler Added;
		public event EventHandler Removed;
		public event EventHandler<Events.AudioDeviceStateEventArgs> StateChanged;
		public event EventHandler PropertyChanged;

		public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
		{
			bool HaveDefaultDevice = !string.IsNullOrEmpty(defaultDeviceId);

			try
			{
				var guid = HaveDefaultDevice ? AudioManager.AudioDeviceIdToGuid(defaultDeviceId) : string.Empty;

				if (Taskmaster.DebugAudio && Taskmaster.Trace)
					Log.Verbose($"<Audio> Default device changed for {role.ToString()} ({flow.ToString()}): {(HaveDefaultDevice ? guid : HumanReadable.Generic.NotAvailable)}");

				switch (role)
				{
					case Role.Console:
					case Role.Multimedia:
						return;
					case Role.Communications:
						break;
				}

				switch (flow)
				{
					case DataFlow.All:
					case DataFlow.Render:
						return;
					case DataFlow.Capture:
						break;
				}

				DefaultDevice?.Invoke(this, new Events.AudioDefaultDeviceEventArgs(guid, role, flow));
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}

			//Log.Information("<Audio> Default device changed: " + defaultDeviceId);
		}

		public void OnDeviceAdded(string pwstrDeviceId)
		{
			Log.Debug("<Audio> Device added: " + pwstrDeviceId);

			Added?.Invoke(this, null);
		}

		public void OnDeviceRemoved(string deviceId)
		{
			Log.Debug("<Audio> Device removed: " + deviceId);

			Removed?.Invoke(this, null);
		}

		public void OnDeviceStateChanged(string deviceId, DeviceState newState)
		{
			try
			{
				switch (newState)
				{
					case DeviceState.Active:
						break;
					case DeviceState.Disabled:
						break;
					case DeviceState.NotPresent:
						break;
					case DeviceState.Unplugged:
						break;
					case DeviceState.All:
						break;
				}

				var guid = AudioManager.AudioDeviceIdToGuid(deviceId);
				Log.Debug("<Audio> Device (" + guid + ") state: " + newState.ToString());

				StateChanged?.Invoke(this, new Events.AudioDeviceStateEventArgs(guid, newState));
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
		{
			PropertyChanged?.Invoke(this, null);
		}
	}
}