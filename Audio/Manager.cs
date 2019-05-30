//
// Audio.Manager.cs
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

namespace Taskmaster.Audio
{
	using static Taskmaster;

	/// <summary>
	/// Must be created on persistent thread, such as the main thread.
	/// </summary>
	public class Manager : IDisposal, IDisposable
	{
		readonly System.Threading.Thread Context = null;

		public event EventHandler<DeviceStateEventArgs> StateChanged;
		public event EventHandler<DefaultDeviceEventArgs> DefaultChanged;

		public event EventHandler<DeviceEventArgs> Added;
		public event EventHandler<DeviceEventArgs> Removed;

		public NAudio.CoreAudioApi.MMDeviceEnumerator Enumerator = null;

		public float OutVolume = 0.0f;
		public float InVolume = 0.0f;

		/// <summary>
		/// Games, voice communication, etc.
		/// </summary>
		public Device ConsoleDevice { get; private set; } = null;
		/// <summary>
		/// Multimedia, Movies, etc.
		/// </summary>
		public Device MultimediaDevice { get; private set; } = null;
		/// <summary>
		/// Voice capture.
		/// </summary>
		public Device RecordingDevice { get; private set; } = null;

		DeviceNotificationClient notificationClient = null;

		//public event EventHandler<ProcessEx> OnNewSession;

		const string configfile = "Audio.ini";

		/// <summary>
		/// Not thread safe
		/// </summary>
		/// <exception cref="InitFailure">If audio device can not be found.</exception>
		public Manager()
		{
			Debug.Assert(MKAh.Execution.IsMainThread, "Requires main thread");
			Context = System.Threading.Thread.CurrentThread;

			Enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();

			GetDefaultDevice();

			notificationClient = new DeviceNotificationClient();
			notificationClient.StateChanged += StateChangeProxy;
			notificationClient.DefaultDevice += DefaultDeviceProxy;
			notificationClient.Added += DeviceAddedProxy;
			notificationClient.Removed += DeviceRemovedProxy;

			Enumerator.RegisterEndpointNotificationCallback(notificationClient);

			/*
			var cfg = Configuration.Load(configfile);

			foreach (var section in cfg)
			{

			}
			*/

			volumeTimer.Elapsed += VolumeTimer_Elapsed;

			RegisterForExit(this);
			DisposalChute.Push(this);
		}

		void VolumeTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
		{
			throw new NotImplementedException();
		}

		System.Timers.Timer volumeTimer = new System.Timers.Timer(100);
		public double VolumePollInterval => volumeTimer.Interval;

		public void StartVolumePolling() => volumeTimer.Start();
		public void StopVolumePolling() => volumeTimer.Stop();

		void StateChangeProxy(object sender, DeviceStateEventArgs ea)
		{
			if (DisposingOrDisposed) return;

			try
			{
				if (DebugAudio)
				{
					string name = Devices.TryGetValue(ea.GUID, out var device) ? device.Name : null;

					Log.Debug($"<Audio> Device {name ?? ea.GUID} state changed to {ea.State.ToString()}");
				}

				StateChanged?.Invoke(this, ea);
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				CloseNotificationClient();
			}
		}

		void DefaultDeviceProxy(object sender, DefaultDeviceEventArgs ea)
		{
			if (DisposingOrDisposed) return;

			try
			{
				DefaultChanged?.Invoke(sender, ea);
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				CloseNotificationClient();
			}
		}

		void DeviceAddedProxy(object sender, DeviceEventArgs ea)
		{
			if (DisposingOrDisposed) return;

			try
			{
				var dev = Enumerator.GetDevice(ea.ID);
				if (dev != null)
				{
					var adev = new Device(dev);

					Devices.TryAdd(ea.GUID, adev);

					ea.Device = adev;

					Added?.Invoke(sender, ea);
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				CloseNotificationClient();
			}
		}

		void DeviceRemovedProxy(object sender, DeviceEventArgs ea)
		{
			if (DisposingOrDisposed) return;

			try
			{
				if (MultimediaDevice != null && ea.GUID.Equals(MultimediaDevice.GUID, StringComparison.OrdinalIgnoreCase))
				{
					UnregisterDevice(MultimediaDevice);
					MultimediaDevice = null;
				}

				if (ConsoleDevice != null && ea.GUID.Equals(ConsoleDevice.GUID, StringComparison.OrdinalIgnoreCase))
				{
					UnregisterDevice(ConsoleDevice);
					ConsoleDevice = null;
				}

			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				CloseNotificationClient();
			}
			finally
			{
				if (Devices.TryRemove(ea.GUID, out var dev))
					dev.Dispose();

				Removed?.Invoke(sender, ea);
			}
		}

		void CloseNotificationClient()
		{
			Logging.DebugMsg("CloseNotificationClient");

			if (notificationClient is null) return;

			ExecuteOnMainThread(new Action(() =>
			{
				Enumerator?.UnregisterEndpointNotificationCallback(notificationClient);
				notificationClient = null;
			}));
		}

		void UnregisterDevice(Device device)
		{
			device.MMDevice.AudioSessionManager.OnSessionCreated -= OnSessionCreated;
		}

		readonly ConcurrentDictionary<string, Device> Devices = new ConcurrentDictionary<string, Device>();

		public Device GetDevice(string guid)
			=> Devices.TryGetValue(guid, out var dev) ? dev : null;

		void GetDefaultDevice()
		{
			if (DisposingOrDisposed) return;

			try
			{
				var mmdevmultimedia = Enumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
				var mmdevconsole = Enumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Console);
				var mmdevinput = Enumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.Role.Communications);

				MultimediaDevice = new Device(mmdevmultimedia);
				ConsoleDevice = new Device(mmdevconsole);
				RecordingDevice = new Device(mmdevinput);

				Log.Information("<Audio> Default movie/music device: " + MultimediaDevice.Name);
				Log.Information("<Audio> Default game/voip device: " + ConsoleDevice.Name);
				Log.Information("<Audio> Default communications device: " + RecordingDevice.Name);
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		public void EventHooks()
		{
			MultimediaDevice.MMDevice.AudioSessionManager.OnSessionCreated += OnSessionCreated;
			ConsoleDevice.MMDevice.AudioSessionManager.OnSessionCreated += OnSessionCreated;
		}

		Process.Manager processmanager = null;
		public void Hook(Process.Manager procman)
		{
			processmanager = procman;
			processmanager.OnDisposed += (_, _ea) => processmanager = null;
		}

		async void OnSessionCreated(object _, NAudio.CoreAudioApi.Interfaces.IAudioSessionControl ea)
		{
			Debug.Assert(System.Threading.Thread.CurrentThread != Context, "Must be called in same thread.");

			if (DisposingOrDisposed) return;

			await Task.Delay(0).ConfigureAwait(false);

			try
			{
				using (var session = new NAudio.CoreAudioApi.AudioSessionControl(ea))
				{
					int pid = (int)session.GetProcessID;
					string name = session.DisplayName;

					float volume = session.SimpleAudioVolume.Volume;

					if (Process.Utility.GetInfo(pid, out var info, getPath: true, name: name))
					{
						//OnNewSession?.Invoke(this, info);
						if (processmanager.GetController(info, out var prc))
						{
							bool volAdjusted = false;
							float oldvolume = session.SimpleAudioVolume.Volume;
							switch (prc.VolumeStrategy)
							{
								default:
								case VolumeStrategy.Ignore:
									break;
								case VolumeStrategy.Force:
									session.SimpleAudioVolume.Volume = prc.Volume;
									volAdjusted = true;
									break;
								case VolumeStrategy.Decrease:
									if (oldvolume > prc.Volume)
									{
										session.SimpleAudioVolume.Volume = prc.Volume;
										volAdjusted = true;
									}
									break;
								case VolumeStrategy.Increase:
									if (oldvolume < prc.Volume)
									{
										session.SimpleAudioVolume.Volume = prc.Volume;
										volAdjusted = true;
									}
									break;
								case VolumeStrategy.DecreaseFromFull:
									if (oldvolume > prc.Volume && oldvolume > 0.99f)
									{
										session.SimpleAudioVolume.Volume = prc.Volume;
										volAdjusted = true;
									}
									break;
								case VolumeStrategy.IncreaseFromMute:
									if (oldvolume < prc.Volume && oldvolume < 0.01f)
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
								if (ShowInaction && DebugAudio)
									Log.Debug($"<Audio> {info.Name} (#{pid}) Volume: {volume * 100f:N1} % – Already correct (Plan: {prc.VolumeStrategy.ToString()})");
							}
						}
						else
						{
							if (ShowInaction && DebugAudio)
								Log.Debug($"<Audio> {info.Name} (#{pid}) Volume: {(volume * 100f):N1} % – not watched: {info.Path}");
						}
					}
					else
					{
						Log.Debug($"<Audio> Failed to get info for session (#{pid})");
					}
				}
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		#region IDisposable Support
		public event EventHandler<DisposedEventArgs> OnDisposed;

		bool DisposingOrDisposed = false;

		protected virtual void Dispose(bool disposing)
		{
			if (!DisposingOrDisposed)
			{
				DisposingOrDisposed = true;

				if (disposing)
				{
					volumeTimer?.Dispose();
					volumeTimer = null;

					CloseNotificationClient(); // unnecessary? definitely hangs if any mmdevice has been disposed
					Enumerator?.Dispose();
					Enumerator = null;

					if (MultimediaDevice != null)
						MultimediaDevice.MMDevice.AudioSessionManager.OnSessionCreated -= OnSessionCreated;
					if (ConsoleDevice != null)
						ConsoleDevice.MMDevice.AudioSessionManager.OnSessionCreated -= OnSessionCreated;

					MultimediaDevice?.Dispose();
					ConsoleDevice?.Dispose();
					RecordingDevice?.Dispose();

					foreach (var dev in Devices.Values)
						dev.Dispose();
				}
			}

			OnDisposed?.Invoke(this, DisposedEventArgs.Empty);
			OnDisposed = null;
		}

		public void Dispose() => Dispose(true);

		public void ShutdownEvent(object sender, EventArgs ea)
		{
			volumeTimer?.Stop();
		}
		#endregion
	}

	public enum VolumeStrategy
	{
		Ignore = 0,
		Decrease = 1,
		Increase = 2,
		Force = 3,
		DecreaseFromFull=4,
		IncreaseFromMute=5
	}
}
