﻿//
// MicManager.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2016-2020 M.A.
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

using MKAh;
using MKAh.Synchronize;
using NAudio.CoreAudioApi;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace Taskmaster.Audio
{
	using static Application;

	[Context(RequireMainThread = true)]
	public class MicManager : IComponent
	{
		public event EventHandler<VolumeChangedEventArgs>? VolumeChanged;
		public event EventHandler<DefaultDeviceEventArgs>? DefaultChanged;
		public event EventHandler<DisposedEventArgs>? OnDisposed;

		bool DebugMic { get; }

		const string DeviceFilename = "Microphone.Devices.ini";

		public bool Control { get; private set; }

		public void SetControl(bool value) => Control = value;

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

		NAudio.Mixer.UnsignedMixerControl? VolumeControl;

		double _volume;

		/// <summary>
		/// Current volume.
		/// </summary>
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

				if (DebugMic) Log.Debug($"<Microphone> DEBUG Volume = {value:0.#} % (actual: {VolumeControl.Percent:0.#} %)");
			}
		}

		public Device? Device { get; private set; }

		/// <summary>
		/// Default device volume. From 0.0d to 100.0d
		/// </summary>
		double DefaultVolume
		{
			get => _defaultvolume;
			set => _defaultvolume = value.Constrain(0.0d, 100.0d);
		}

		double _defaultvolume = 100d;

		// ctor, constructor
		/// <exception cref="InitFailure">When initialization fails in a way that can not be continued from.</exception>
		public MicManager()
		{
			Debug.Assert(MKAh.Execution.IsMainThread, "Requires main thread");

			const string mvol = "Default recording volume";
			const string mcontrol = "Recording volume control";

			using var corecfg = Config.Load(CoreConfigFilename);
			var mediasec = corecfg.Config[Constants.Media];

			Control = mediasec.GetOrSet(mcontrol, false).Bool;
			DefaultVolume = mediasec.GetOrSet(mvol, 100.0d).Double;

			var dbgsec = corecfg.Config[HumanReadable.Generic.Debug];
			DebugMic = dbgsec.Get(HumanReadable.Hardware.Audio.Microphone)?.Bool ?? false;

			if (DebugMic) Log.Information("<Microphone> Component loaded.");
		}

		Manager? audiomanager;

		public void Hook(Manager manager)
		{
			Debug.Assert(manager != null, "AudioManager must not be null");

			try
			{
				audiomanager = manager;
				audiomanager.Added += DeviceAdded;
				audiomanager.Removed += DeviceRemoved;
				audiomanager.DefaultChanged += ChangeDefaultDevice;
				audiomanager.OnDisposed += (_, _2) => audiomanager = null;

				EnumerateDevices();

				RegisterDefaultDevice();
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		public void DeviceAdded(object sender, DeviceEventArgs ea)
		{
			if (disposed) return;

			try
			{
				if (ea.Device.Flow == DataFlow.Capture)
					KnownDevices.Add(ea.Device);
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		public void DeviceRemoved(object sender, DeviceEventArgs ea)
		{
			if (disposed) return;

			try
			{
				var guid = ea.GUID;
				var dev = KnownDevices.FirstOrDefault(x => x.GUID == guid);
				if (dev != null) KnownDevices.Remove(dev);
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		void ChangeDefaultDevice(object sender, DefaultDeviceEventArgs ea)
		{
			if (disposed) return;

			try
			{
				if (ea.Flow == DataFlow.Capture && ea.Role == Role.Communications)
				{
					UnregisterDefaultDevice();

					if (ea.GUID != Guid.Empty)
						RegisterDefaultDevice();
					else // no default
						Log.Warning("<Microphone> No communications device found!");

					DefaultChanged?.Invoke(this, ea);
				}
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		void UnregisterDefaultDevice()
		{
			if (disposed) throw new ObjectDisposedException(nameof(MicManager), "UnregisterDefaultDevice called after MicManager was disposed.");

			try
			{
				Device?.Dispose();
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				VolumeControl = null;
				Device = null;
			}
		}

		void RegisterDefaultDevice()
		{
			if (disposed) throw new ObjectDisposedException(nameof(MicManager), "RegisterDefaultDevice called after MicManager was disposed.");

			try
			{
				// FIXME: Deal with multiple recording devices.
				var waveInDeviceNumber = IntPtr.Zero; // 0 is default or first?

				// get default communications device
				try
				{
					Device = audiomanager.RecordingDevice;
					if (Device is null)
					{
						Log.Error("<Microphone> No communications device found!");
						return;
					}
				}
				catch (System.Runtime.InteropServices.COMException ex)
				{
					Logging.Stacktrace(ex);
					// NOP
				}

				NAudio.Mixer.MixerLine? mixerLine = null;
				try
				{
					mixerLine = new NAudio.Mixer.MixerLine(waveInDeviceNumber, 0, NAudio.Mixer.MixerFlags.WaveIn);
				}
				catch (NAudio.MmException)
				{
					Log.Error("<Microphone> Default device not found.");
					Device?.Dispose();
					Device = null;
					return;
				}

				VolumeControl = (NAudio.Mixer.UnsignedMixerControl)mixerLine.Controls.FirstOrDefault(
					(control) => control.ControlType == NAudio.Mixer.MixerControlType.Volume
				);

				_volume = (VolumeControl != null ? VolumeControl.Percent : double.NaN); // kinda hackish

				Device.MMDevice.AudioEndpointVolume.OnVolumeNotification += VolumeChangedHandler;

				//
				using var devcfg = Config.Load(DeviceFilename);
				var devsec = devcfg.Config[Device.GUID.ToString()];

				double devvol = devsec.GetOrSet(HumanReadable.Hardware.Audio.Volume, DefaultVolume).Double;
				bool devcontrol = devsec.GetOrSet(Constants.Control, false).Bool;
				devsec.GetOrSet(Application.Constants.Name, Device.Name);

				if (Control && !devcontrol) Control = false; // disable general control if device control is disabled

				Target = devvol.Constrain(0.0d, 100.0d);
				Log.Information($"<Microphone> Default device: {Device.ToShortString()} (volume: {Target:0.#} %) – Control: {(Control ? HumanReadable.Generic.Enabled : HumanReadable.Generic.Disabled)}");

				if (Control) Volume = Target;
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		public static void SaveDevice(Device device)
		{
			using var devcfg = Config.Load(DeviceFilename);
			var devsec = devcfg.Config[device.GUID.ToString()];
			devsec[Application.Constants.Name].String = device.MMDevice.DeviceFriendlyName;
			devsec[Constants.Control].Bool = device.VolumeControl;
			devsec[HumanReadable.Hardware.Audio.Volume].Float = device.Target;
		}

		void EnumerateDevices()
		{
			if (disposed) throw new ObjectDisposedException(nameof(MicManager), "EnumerateDevices called after MicManager was disposed.");

			if (Trace) Log.Verbose("<Microphone> Enumerating devices...");

			var devices = new List<Device>(4);

			try
			{
				using var devcfg = Config.Load(DeviceFilename);
				var devs = audiomanager.Enumerator?.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.DeviceState.Active) ?? null;
				if (devs is null) throw new InvalidOperationException("Enumerator not available, Audio Manager is dead");
				foreach (var dev in devs)
				{
					try
					{
						Guid guid = Utility.DeviceIdToGuid(dev.ID);
						var devsec = devcfg.Config[guid.ToString()];
						devsec.GetOrSet(Application.Constants.Name, dev.DeviceFriendlyName);
						bool control = devsec.GetOrSet(Constants.Control, false).Bool;
						float target = devsec.Get(HumanReadable.Hardware.Audio.Volume)?.Float ?? float.NaN;

						var mdev = new Device(dev)
						{
							VolumeControl = control,
							Target = target,
							Volume = dev.AudioSessionManager.SimpleAudioVolume.Volume,
						};

						devices.Add(mdev);

						if (Trace) Log.Verbose("<Microphone> Device: " + mdev.ToString());
					}
					catch (OutOfMemoryException) { throw; }
					catch (Exception ex)
					{
						Logging.Stacktrace(ex);
					}
				}

				if (Trace) Log.Verbose("<Microphone> " + KnownDevices.Count.ToString(CultureInfo.InvariantCulture) + " microphone(s)");
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}

			KnownDevices = devices;
		}

		/// <summary>
		/// Enumerate this instance.
		/// </summary>
		/// <returns>Enumeration of audio input devices as GUID/FriendlyName string pair.</returns>
		public ReadOnlyCollection<Device> Devices => new ReadOnlyCollection<Device>(KnownDevices);

		List<Device> KnownDevices = new List<Device>();

		// TODO: Add device enumeration
		// TODO: Add device selection
		// TODO: Add per device behaviour

		public int Corrections { get; private set; }

		int correcting_lock = Atomic.Unlocked;

		async void VolumeChangedHandler(NAudio.CoreAudioApi.AudioVolumeNotificationData data)
		{
			if (disposed) return;

			var oldVol = Volume;
			double newVol = data.MasterVolume * 100d;

			// BUG: This will allow tiny 
			if (Math.Abs(newVol - Target) <= SmallVolumeHysterisis)
			{
				if (ShowInaction && DebugMic)
					Log.Verbose($"<Microphone> Volume change too small ({Math.Abs(newVol - Target):0.#} %) to act on.");
				return;
			}

			if (Trace) Log.Verbose($"<Microphone> Volume changed from {oldVol:0.#} % to {newVol:0.#} %");

			// This is a light HYSTERISIS limiter in case someone is sliding a volume bar around,
			// we act on it only once every [AdjustDelay] ms.
			// HOPEFULLY there are no edge cases with this triggering just before last adjustment
			// and the notification for the last adjustment coming slightly before. Seems super unlikely tho.
			// TODO: Delay this even more if volume is changed ~2 seconds before we try to do so.

			if (Math.Abs(newVol - Target) >= VolumeHysterisis) // Volume != Target for double
			{
				if (Trace) Log.Verbose($"<Microphone> Volume off target by {Math.Abs(newVol - Target):0.#} %");

				if (Atomic.Lock(ref correcting_lock))
				{
					try
					{
						await System.Threading.Tasks.Task.Delay(AdjustDelay).ConfigureAwait(false); // actual hysterisis, this should be cancellable
						if (disposed) return; // recheck

						if (Control)
						{
							oldVol = VolumeControl.Percent;
							Log.Information($"<Microphone> Correcting volume from {oldVol:0.#} % to {Target:0.#} %");
							Volume = Target;
							Corrections++;
							VolumeChanged?.Invoke(this, new VolumeChangedEventArgs { Old = oldVol, New = Target, Corrections = Corrections });
						}
						else
						{
							if (DebugMic) Log.Debug($"<Microphone> Volume not corrected from {oldVol:0.#} %");
						}
					}
					finally
					{
						Atomic.Unlock(ref correcting_lock);
					}
				}
				else
				{
					if (Trace) Log.Verbose("<Microphone> DEBUG CorrectionAlreadyQueued");
				}
			}
			else
			{
				if (Trace) Log.Verbose("<Microphone> DEBUG NotCorrected");
			}
		}

		#region IDisposable Support
		bool disposed;

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposed) return;
			disposed = true;

			if (disposing)
			{
				if (Trace) Log.Verbose("Disposing microphone monitor...");

				VolumeChanged = null;

				Device?.Dispose();
				Device = null;

				OnDisposed?.Invoke(this, DisposedEventArgs.Empty);
				OnDisposed = null;

				//base.Dispose();
			}
		}

		public void ShutdownEvent(object sender, EventArgs ea)
		{
			hiddenwindow?.InvokeAsync(new Action(() =>
			{
				Device?.Dispose();
				Device = null;
			}));
		}
		#endregion
	}
}