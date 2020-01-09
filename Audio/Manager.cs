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

using Serilog;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Taskmaster.Audio
{
	using static Application;

	/// <summary>
	/// Must be created on persistent thread, such as the main thread.
	/// </summary>
	[Context(RequireMainThread = true)]
	public class Manager : IComponent
	{
		readonly System.Threading.Thread Context;

		public event EventHandler<DisposedEventArgs>? OnDisposed;

		public event EventHandler<DeviceStateEventArgs> StateChanged;
		public event EventHandler<DefaultDeviceEventArgs> DefaultChanged;

		public event EventHandler<DeviceEventArgs> Added;
		public event EventHandler<DeviceEventArgs> Removed;

		public NAudio.CoreAudioApi.MMDeviceEnumerator Enumerator { get; }

		public float OutVolume { get; set; } = 0.0f;

		public float InVolume { get; set; } = 0.0f;

		/// <summary>
		/// Games, voice communication, etc.
		/// </summary>
		public Device? ConsoleDevice { get; private set; } = null;

		/// <summary>
		/// Multimedia, Movies, etc.
		/// </summary>
		public Device? MultimediaDevice { get; private set; } = null;

		/// <summary>
		/// Voice capture.
		/// </summary>
		public Device? RecordingDevice { get; private set; } = null;

		readonly DeviceNotificationClient notificationClient;

		//public event EventHandler<ProcessEx> OnNewSession;

		//const string configfile = "Audio.ini";

		/// <summary>
		/// Not thread safe
		/// </summary>
		/// <exception cref="InitFailure">If audio device can not be found.</exception>
		public Manager()
		{
			Debug.Assert(MKAh.Execution.IsMainThread, "Requires main thread");
			Context = System.Threading.Thread.CurrentThread;

			Enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();

			notificationClient = new DeviceNotificationClient(this)
			{
				StateChanged = StateChangeProxy,
				DefaultDevice = DefaultDeviceProxy,
				Added = DeviceAddedProxy,
				Removed = DeviceRemovedProxy
			};

			GetDefaultDevice();
			EnumerateDevices();

			Enumerator.RegisterEndpointNotificationCallback(notificationClient);

			/*
			var cfg = Configuration.Load(configfile);

			foreach (var section in cfg)
			{

			}
			*/

			volumeTimer.Elapsed += VolumeTimer_Elapsed;
		}

		void EnumerateDevices()
		{
			foreach (var dev in Enumerator.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.All, NAudio.CoreAudioApi.DeviceState.All))
				DeviceAddedProxy(dev.ID);
		}

		void VolumeTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
		{
			throw new NotImplementedException();
		}

		readonly System.Timers.Timer volumeTimer = new System.Timers.Timer(100);
		public double VolumePollInterval => volumeTimer.Interval;

		public void StartVolumePolling() => volumeTimer.Start();
		public void StopVolumePolling() => volumeTimer.Stop();

		void StateChangeProxy(string deviceId, NAudio.CoreAudioApi.DeviceState state, Guid? guid = null)
		{
			if (disposed) return;

			try
			{
				var ea = new DeviceStateEventArgs(deviceId, state);

				if (DebugAudio)
				{
					string name = Devices.TryGetValue(ea.GUID, out var device) ? device.Name : ea.GUID.ToString();

					Log.Debug($"<Audio> Device {name} state changed to {state.ToString()}");
				}

				StateChanged?.Invoke(this, ea);
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		void DefaultDeviceProxy(Guid guid, string id, NAudio.CoreAudioApi.Role role, NAudio.CoreAudioApi.DataFlow flow)
		{
			if (disposed) return;

			try
			{
				if (string.IsNullOrEmpty(id))
				{
					Log.Warning("<Audio> Default device lost for " + role.ToString());
					switch (role)
					{
						case NAudio.CoreAudioApi.Role.Console: ConsoleDevice = null; break;
						case NAudio.CoreAudioApi.Role.Multimedia: MultimediaDevice = null; break;
						case NAudio.CoreAudioApi.Role.Communications: RecordingDevice = null; break;
					}
				}
				else
				{
					var dev = new Device(guid, id, flow, NAudio.CoreAudioApi.DeviceState.Active, Enumerator.GetDevice(id));

					switch (role)
					{
						case NAudio.CoreAudioApi.Role.Console:
							ConsoleDevice = dev;
							break;
						case NAudio.CoreAudioApi.Role.Multimedia:
							MultimediaDevice = dev;
							break;
						case NAudio.CoreAudioApi.Role.Communications:
							RecordingDevice = dev;
							break;
					}
				}

				DefaultChanged?.Invoke(this, new DefaultDeviceEventArgs(guid, id, role, flow));
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		void DeviceAddedProxy(string deviceId)
		{
			if (disposed) return;

			try
			{
				var dev = Enumerator.GetDevice(deviceId);
				if (dev != null)
				{
					var adev = new Device(dev);
					if (DebugAudio) Log.Debug("<Audio> Device added: " + (adev.Name ?? adev.GUID.ToString()));

					Devices.TryAdd(adev.GUID, adev);

					Added?.Invoke(this, new DeviceEventArgs(deviceId, adev.GUID, adev));
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		void DeviceRemovedProxy(string deviceId)
		{
			if (disposed) return;

			var guid = Utility.DeviceIdToGuid(deviceId);

			try
			{
				if (DebugAudio)
				{
					var dev = GetDevice(guid);
					Log.Debug("<Audio> Device removed: " + dev?.Name ?? deviceId);
				}

				if (MultimediaDevice != null && guid == MultimediaDevice.GUID)
				{
					UnregisterDevice(MultimediaDevice);
					MultimediaDevice = null;
				}
				else if (ConsoleDevice != null && guid == ConsoleDevice.GUID)
				{
					UnregisterDevice(ConsoleDevice);
					ConsoleDevice = null;
				}
				else if (RecordingDevice != null && guid == RecordingDevice.GUID)
				{
					UnregisterDevice(RecordingDevice);
					RecordingDevice = null;
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				if (Devices.TryRemove(guid, out var dev))
					dev.Dispose();

				Removed?.Invoke(this, new DeviceEventArgs(deviceId, guid, dev));
			}
		}

		void CloseNotificationClient()
		{
			Logging.DebugMsg("CloseNotificationClient");

			ExecuteOnMainThread(new Action(() =>
			{
				Enumerator?.UnregisterEndpointNotificationCallback(notificationClient);
			}));
		}

		void UnregisterDevice(Device device)
		{
			device.MMDevice.AudioSessionManager.OnSessionCreated -= OnSessionCreated;
		}

		readonly ConcurrentDictionary<Guid, Device> Devices = new ConcurrentDictionary<Guid, Device>();

		public Device GetDevice(Guid guid)
			=> Devices.TryGetValue(guid, out var dev) ? dev : null;

		void GetDefaultDevice()
		{
			if (disposed) return;

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

		public void SetupEventHooks()
		{
			MultimediaDevice.MMDevice.AudioSessionManager.OnSessionCreated += OnSessionCreated;
			ConsoleDevice.MMDevice.AudioSessionManager.OnSessionCreated += OnSessionCreated;
		}

		Process.Manager? processmanager = null;

		public void Hook(Process.Manager procman)
		{
			processmanager = procman;
			processmanager.OnDisposed += (_, _ea) => processmanager = null;
		}

		void OnSessionCreated(object _, NAudio.CoreAudioApi.Interfaces.IAudioSessionControl ea)
		{
			if (disposed) return;

			Debug.Assert(System.Threading.Thread.CurrentThread != Context, "Must be called in same thread.");

			try
			{
				using var session = new NAudio.CoreAudioApi.AudioSessionControl(ea);
				int pid = (int)session.GetProcessID;
				string name = session.DisplayName;

				// TODO: Fetch cached copy from manager?
				Process.ProcessEx? info;
				if (processmanager.GetCachedProcess(pid, out info) || Process.Utility.Construct(pid, out info, getPath: true, name: name))
				{
					// TODO: check for staleness

					//info.Path

					float volume = session.SimpleAudioVolume.Volume;

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
							Log.Information($"<Audio> {info} volume changed from {oldvolume * 100f:N1} % to {prc.Volume * 100f:N1} %");
						}
						else
						{
							if (ShowInaction && DebugAudio)
								Log.Debug($"<Audio> {info}; Volume: {volume * 100f:N1} % – Already correct (Plan: {prc.VolumeStrategy.ToString()})");
						}
					}
					else
					{
						if (ShowInaction && DebugAudio)
							Log.Debug($"<Audio> {info}; Volume: {(volume * 100f):N1} % – not watched: {info.Path}");
					}
				}
				else
				{
					Log.Debug($"<Audio> Failed to get info for session #{pid}");
				}
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		#region IDisposable Support
		bool disposed = false;

		protected void Dispose(bool disposing)
		{
			if (disposed) return;
			disposed = true;

			if (disposing)
			{
				volumeTimer.Dispose();

				CloseNotificationClient(); // unnecessary? definitely hangs if any mmdevice has been disposed
				Enumerator.Dispose();

				if (MultimediaDevice != null)
					MultimediaDevice.MMDevice.AudioSessionManager.OnSessionCreated -= OnSessionCreated;
				if (ConsoleDevice != null)
					ConsoleDevice.MMDevice.AudioSessionManager.OnSessionCreated -= OnSessionCreated;

				MultimediaDevice?.Dispose();
				ConsoleDevice?.Dispose();
				RecordingDevice?.Dispose();

				foreach (var dev in Devices.Values)
					dev.Dispose();

				OnDisposed?.Invoke(this, DisposedEventArgs.Empty);
				OnDisposed = null;
			}

			//base.Dispose();
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		public void ShutdownEvent(object sender, EventArgs ea) => volumeTimer?.Stop();
		#endregion
	}

	public enum VolumeStrategy
	{
		Ignore = 0,
		Decrease = 1,
		Increase = 2,
		Force = 3,
		DecreaseFromFull = 4,
		IncreaseFromMute = 5
	}
}
