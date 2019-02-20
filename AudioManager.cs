//
// AudioManager.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2018–2019 M.A.
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
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks;
using Serilog;
using Taskmaster.Events;

namespace Taskmaster
{
	/// <summary>
	/// Must be created on persistent thread, such as the main thread.
	/// </summary>
	public class AudioManager : IDisposable
	{
		readonly System.Threading.Thread Context = null;

		public event EventHandler<Events.AudioDeviceStateEventArgs> StateChanged;
		public event EventHandler<Events.AudioDefaultDeviceEventArgs> DefaultChanged;

		public event EventHandler<Events.AudioDeviceEventArgs> Added;
		public event EventHandler<Events.AudioDeviceEventArgs> Removed;

		public NAudio.CoreAudioApi.MMDeviceEnumerator Enumerator = null;

		public float OutVolume = 0.0f;
		public float InVolume = 0.0f;

		/// <summary>
		/// Games, voice communication, etc.
		/// </summary>
		public AudioDevice ConsoleDevice { get; private set; } = null;
		/// <summary>
		/// Multimedia, Movies, etc.
		/// </summary>
		public AudioDevice MultimediaDevice { get; private set; } = null;
		/// <summary>
		/// Voice capture.
		/// </summary>
		public AudioDevice RecordingDevice { get; private set; } = null;

		readonly AudioDeviceNotificationClient notificationClient = null;

		//public event EventHandler<ProcessEx> OnNewSession;

		const string configfile = "Audio.ini";

		/// <summary>
		/// Not thread safe
		/// </summary>
		/// <exception cref="InitFailure">If audio device can not be found.</exception>
		public AudioManager()
		{
			Debug.Assert(Taskmaster.IsMainThread(), "Requires main thread");
			Context = System.Threading.Thread.CurrentThread;

			Enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();

			GetDefaultDevice();

			notificationClient = new AudioDeviceNotificationClient();
			notificationClient.StateChanged += StateChangeProxy;
			notificationClient.DefaultDevice += DefaultDeviceProxy;
			notificationClient.Added += DeviceAddedProxy;
			notificationClient.Removed += DeviceRemovedProxy;

			Enumerator.RegisterEndpointNotificationCallback(notificationClient);

			/*
			var cfg = Taskmaster.Config.Load(configfile);

			foreach (var section in cfg)
			{

			}
			*/

			volumeTimer.Elapsed += VolumeTimer_Elapsed;

			Taskmaster.DisposalChute.Push(this);
		}

		private void VolumeTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
		{
			throw new NotImplementedException();

		}

		System.Timers.Timer volumeTimer = new System.Timers.Timer(100);
		public double VolumePollInterval => volumeTimer.Interval;

		public void StartVolumePolling() => volumeTimer.Start();
		public void StopVolumePolling() => volumeTimer.Stop();

		private void StateChangeProxy(object sender, Events.AudioDeviceStateEventArgs ea)
		{
			if (Taskmaster.DebugAudio)
			{
				string name = null;
				if (Devices.TryGetValue(ea.GUID, out var device))
					name = device.Name;

				Log.Debug($"<Audio> Device {name ?? ea.GUID} state changed to {ea.State.ToString()}");
			}

			StateChanged?.Invoke(this, ea);
		}

		private void DefaultDeviceProxy(object sender, AudioDefaultDeviceEventArgs ea)
		{
			DefaultChanged?.Invoke(sender, ea);
		}

		private void DeviceAddedProxy(object sender, AudioDeviceEventArgs ea)
		{
			var dev = Enumerator.GetDevice(ea.ID);
			if (dev != null)
			{
				var adev = new AudioDevice(dev);

				Devices.TryAdd(ea.GUID, new AudioDevice(dev));

				ea.Device = adev;

				Added?.Invoke(sender, ea);
			}
		}

		private void DeviceRemovedProxy(object sender, AudioDeviceEventArgs ea)
		{
			if (ea.GUID.Equals(MultimediaDevice.GUID, StringComparison.OrdinalIgnoreCase))
				MultimediaDevice = null;

			if (ea.GUID.Equals(ConsoleDevice.GUID, StringComparison.OrdinalIgnoreCase))
				ConsoleDevice = null;

			if (Devices.TryRemove(ea.GUID, out var dev))
				dev.Dispose();

			Removed?.Invoke(sender, ea);
		}

		ConcurrentDictionary<string, AudioDevice> Devices = new ConcurrentDictionary<string, AudioDevice>();

		public AudioDevice GetDevice(string guid)
		{
			if (Devices.TryGetValue(guid, out var dev))
				return dev;

			return null;
		}

		void GetDefaultDevice()
		{
			if (DisposingOrDisposed) return;

			try
			{
				var mmdevmultimedia = Enumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
				var mmdevconsole = Enumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Console);
				var mmdevinput = Enumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.Role.Communications);

				MultimediaDevice = new AudioDevice(mmdevmultimedia);
				ConsoleDevice = new AudioDevice(mmdevconsole);
				RecordingDevice = new AudioDevice(mmdevinput);

				Log.Information("<Audio> Default movie/music device: " + MultimediaDevice.Name);
				Log.Information("<Audio> Default game/voip device: " + ConsoleDevice.Name);
				Log.Information("<Audio> Default communications device: " + RecordingDevice.Name);

				MultimediaDevice.MMDevice.AudioSessionManager.OnSessionCreated += OnSessionCreated;
				ConsoleDevice.MMDevice.AudioSessionManager.OnSessionCreated += OnSessionCreated;
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		ProcessManager processmanager = null;
		public void Hook(ProcessManager procman)
		{
			processmanager = procman;
		}

		private async void OnSessionCreated(object _, NAudio.CoreAudioApi.Interfaces.IAudioSessionControl ea)
		{
			Debug.Assert(System.Threading.Thread.CurrentThread != Context, "Must be called in same thread.");

			if (DisposingOrDisposed) return;
			if (processmanager == null) return;

			await Task.Delay(0).ConfigureAwait(false);

			try
			{
				var session = new NAudio.CoreAudioApi.AudioSessionControl(ea);

				int pid = (int)session.GetProcessID;
				string name = session.DisplayName;

				float volume = session.SimpleAudioVolume.Volume;

				if (ProcessUtility.GetInfo(pid, out var info, getPath: true, name: name))
				{
					//OnNewSession?.Invoke(this, info);
					if (processmanager.GetController(info, out var prc))
					{
						bool volAdjusted = false;
						float oldvolume = session.SimpleAudioVolume.Volume;
						switch (prc.VolumeStrategy)
						{
							default:
							case AudioVolumeStrategy.Ignore:
								break;
							case AudioVolumeStrategy.Force:
								session.SimpleAudioVolume.Volume = prc.Volume;
								volAdjusted = true;
								break;
							case AudioVolumeStrategy.Decrease:
								if (oldvolume > prc.Volume)
								{
									session.SimpleAudioVolume.Volume = prc.Volume;
									volAdjusted = true;
								}
								break;
							case AudioVolumeStrategy.Increase:
								if (oldvolume < prc.Volume)
								{
									session.SimpleAudioVolume.Volume = prc.Volume;
									volAdjusted = true;
								}
								break;
							case AudioVolumeStrategy.DecreaseFromFull:
								if (oldvolume > prc.Volume && oldvolume >= 0.99f)
								{
									session.SimpleAudioVolume.Volume = prc.Volume;
									volAdjusted = true;
								}
								break;
							case AudioVolumeStrategy.IncreaseFromMute:
								if (oldvolume < prc.Volume && oldvolume <= 0.01f)
								{
									session.SimpleAudioVolume.Volume = prc.Volume;
									volAdjusted = true;
								}
								break;
						}

						if (volAdjusted)
						{
							Log.Information($"<Audio> {info.Name} (#{info.Id}) volume changed from {oldvolume * 100f:N1} % to {prc.Volume * 100f:N1} %");
						}
						else
						{
							if (Taskmaster.ShowInaction && Taskmaster.DebugAudio)
								Log.Debug($"<Audio> {info.Name} (#{pid}) Volume: {volume * 100f:N1} % – Already correct (Plan: {prc.VolumeStrategy.ToString()})");
						}
					}
					else
					{
						if (Taskmaster.ShowInaction && Taskmaster.DebugAudio)
							Log.Debug($"<Audio> {info.Name} (#{pid}) Volume: {(volume * 100f):N1} % – not watched: {info.Path}");
					}
				}
				else
				{
					Log.Debug($"<Audio> Failed to get info for session (#{pid})");
				}
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		#region IDisposable Support
		private bool DisposingOrDisposed = false;

		protected virtual void Dispose(bool disposing)
		{
			if (!DisposingOrDisposed)
			{
				DisposingOrDisposed = true;

				if (disposing)
				{
					Enumerator?.UnregisterEndpointNotificationCallback(notificationClient);  // unnecessary? definitely hangs if any mmdevice has been disposed
					Enumerator?.Dispose();
					Enumerator = null;

					MultimediaDevice.MMDevice.AudioSessionManager.OnSessionCreated -= OnSessionCreated;
					ConsoleDevice.MMDevice.AudioSessionManager.OnSessionCreated -= OnSessionCreated;

					MultimediaDevice?.Dispose();
					ConsoleDevice?.Dispose();

					foreach (var dev in Devices.Values)
						dev.Dispose();
				}
			}
		}

		public void Dispose() => Dispose(true);
		#endregion

		/// <summary>
		/// Takes Device ID in form of {a.b.c.dddddddd}.{aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee} and retuurns GUID part only.
		/// </summary>
		public static string AudioDeviceIdToGuid(string deviceId)
		{
			return (deviceId.Split('}'))[1].Substring(2);
		}
	}

	public enum AudioVolumeStrategy
	{
		Ignore = 0,
		Decrease = 1,
		Increase = 2,
		Force = 3,
		DecreaseFromFull=4,
		IncreaseFromMute=5
	}
}
