//
// Audio.Manager.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2018–2020 M.A.
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

using NAudio.Utils;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace Taskmaster.Audio
{
	using static Application;

	/// <summary>
	/// Must be created on persistent thread, such as the main thread.
	/// </summary>
	[Context(RequireMainThread = true)]
	public class Manager : IComponent
	{
		readonly System.Windows.Threading.Dispatcher dispatcher;

		public event EventHandler<DisposedEventArgs>? OnDisposed;

		public event EventHandler<DeviceStateEventArgs> StateChanged;
		public event EventHandler<DefaultDeviceEventArgs> DefaultChanged;

		public event EventHandler<DeviceEventArgs> Added;
		public event EventHandler<DeviceEventArgs> Removed;

		public NAudio.CoreAudioApi.MMDeviceEnumerator Enumerator { get; }

		public float OutVolume { get; set; }

		public float InVolume { get; set; }

		/// <summary>
		/// Games, voice communication, etc.
		/// </summary>
		public Device? ConsoleDevice { get; private set; }

		/// <summary>
		/// Multimedia, Movies, etc.
		/// </summary>
		public Device? MultimediaDevice { get; private set; }

		/// <summary>
		/// Voice capture.
		/// </summary>
		public Device? RecordingDevice { get; private set; }

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

			dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;

			Enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();

			notificationClient = new DeviceNotificationClient(this)
			{
				StateChanged = StateChangeProxy,
				DefaultDevice = DefaultDeviceProxy,
				Added = DeviceAddedProxy,
				Removed = DeviceRemovedProxy
			};

			EnumerateDevices();
			GetDefaultDevice();

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
			Logging.DebugMsg("<Audio> Enumerating devices.");
			foreach (var dev in Enumerator.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.All, NAudio.CoreAudioApi.DeviceState.All))
				DeviceAddedProxy(dev.ID);
			Logging.DebugMsg("<Audio> Device enumeration complete.");

			Logging.DebugMsg("<Audio> Ignored devices: " + IgnoredDevices.ToString(CultureInfo.InvariantCulture));
			if (IgnoredDevices > 0) // don't be silent about problems even if they're ignored
				Log.Warning("<Audio> Encountered " + IgnoredDevices.ToString(CultureInfo.InvariantCulture) + " problematic devices.");
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
					string name = Devices.TryGetValue(ea.GUID, out var device) ? device.ToShortString() : $"{{{ea.GUID}}}";

					Log.Debug($"<Audio> Device {name} state changed to {state}");
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

		int IgnoredDevices;

		void DeviceAddedProxy(string deviceId)
		{
			if (disposed) return;

			try
			{
				var dev = Enumerator.GetDevice(deviceId);
				if (dev != null)
				{
					var adev = new Device(dev);
					if (Trace && DebugAudio) Log.Debug("<Audio> [" + adev.Flow.ToString() + "] Device added: " + adev.ToShortString());

					Devices.TryAdd(adev.GUID, adev);

					Added?.Invoke(this, new DeviceEventArgs(deviceId, adev.GUID, adev));
				}
			}
			catch (System.Runtime.InteropServices.COMException ex)
			{
				IgnoredDevices++;

				if (DebugAudio) // Ignore when not debugging. It's a bad device we'd not use anyway.
				{
					Log.Error($"<Audio> COM exception (0x{ex.GetHResult():X}) handling new device: {Utility.DeviceIdToGuid(deviceId)}");
					// This should probably just be ignored.
					// It happens when a device doesn't have a name and those are probably always things that do nothing for us.
				}
			}
			catch (Exception ex)
			{
				Log.Error("<Audio> Error handling new device: {" + Utility.DeviceIdToGuid(deviceId).ToString() + "} – " + ex.Message);
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
					Log.Debug("<Audio> Device removed: " + dev?.ToShortString() ?? $"{{{Utility.DeviceIdToGuid(deviceId)}}}");
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

			hiddenwindow?.InvokeAsync(new Action(() =>
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

			Logging.DebugMsg("<Audio> Finding default devices.");

			try
			{
				var mmdevmultimedia = Enumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
				var mmdevconsole = Enumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Console);
				var mmdevinput = Enumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.Role.Communications);
				
				Devices.TryGetValue(Utility.DeviceIdToGuid(mmdevconsole.ID), out var multimediadev);
				MultimediaDevice = multimediadev;
				Devices.TryGetValue(Utility.DeviceIdToGuid(mmdevconsole.ID), out var consoledev);
				ConsoleDevice = consoledev;
				Devices.TryGetValue(Utility.DeviceIdToGuid(mmdevinput.ID), out var inputdev);
				RecordingDevice = inputdev;

				if (DebugAudio)
				{
					Log.Information("<Audio> Default movie/music device: " + MultimediaDevice.ToShortString());
					Log.Information("<Audio> Default game/voip device: " + ConsoleDevice.ToShortString());
					Log.Information("<Audio> Default communications device: " + RecordingDevice.ToShortString());
				}
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}

			Logging.DebugMsg("<Audio> Default device search complete.");
		}

		public void SetupEventHooks()
		{
			MultimediaDevice.MMDevice.AudioSessionManager.OnSessionCreated += OnSessionCreated;
			ConsoleDevice.MMDevice.AudioSessionManager.OnSessionCreated += OnSessionCreated;
		}

		Process.Manager? processmanager;

		public void Hook(Process.Manager procman)
		{
			processmanager = procman;
			processmanager.OnDisposed += (_, _2) => processmanager = null;
		}

		void OnSessionCreated(object _, NAudio.CoreAudioApi.Interfaces.IAudioSessionControl ea)
		{
			if (disposed) return;

			try
			{
				using var session = new NAudio.CoreAudioApi.AudioSessionControl(ea);
				int pid = (int)session.GetProcessID;
				string name = session.DisplayName;

				Process.ProcessEx? info;
				bool cached = false;
				if (!(cached = processmanager.GetCachedProcess(pid, out info) || Process.Utility.Construct(pid, out info, getPath: true, name: name)))
				{
					Log.Debug($"<Audio> Failed to get info for process #{pid}");
					return;
				}

				if (!cached) processmanager.CacheProcess(info); // just to be sure.

				// TODO: check for staleness

				//info.Path

				float volume = session.SimpleAudioVolume.Volume;

				//OnNewSession?.Invoke(this, info);
				var prc = info.Controller;
				if (prc == null)
				{
					if (!processmanager.GetController(info, out prc))
					{
						if (ShowInaction && DebugAudio)
							Log.Debug($"{info.ToFullFormattedString()}; Volume at {(volume * 100f):0.#} % – not watched: {info.Path}");
						return;
					}
				}

				if (!info.IsPathFormatted) info.Controller.FormatPathName(info);

				float oldvolume = session.SimpleAudioVolume.Volume;

				bool getNewVolume(float oldVolume, Process.Controller prc) => prc.VolumeStrategy switch
				{
					VolumeStrategy.Decrease => (oldVolume > prc.Volume),
					VolumeStrategy.Increase => (oldVolume < prc.Volume),
					VolumeStrategy.Force => (true),
					VolumeStrategy.DecreaseFromFull => (oldVolume > prc.Volume && oldVolume > 0.99f),
					VolumeStrategy.IncreaseFromMute => (oldVolume < prc.Volume && oldVolume < 0.01f),
					_ => false,
				};

				bool adjustVol = getNewVolume(oldvolume, prc);

				if (adjustVol)
				{
					session.SimpleAudioVolume.Volume = prc.Volume;

					if (prc.LogAdjusts)
						Log.Information($"{info.ToFullFormattedString()} Volume changed from {oldvolume * 100f:0.#} % to {prc.Volume * 100f:0.#} %");
				}
				else
				{
					if (ShowInaction && DebugAudio)
						Log.Debug($"{info.ToFullFormattedString()} Volume at {volume * 100f:0.#} % – Already correct (Plan: {prc.VolumeStrategy})");
				}
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		#region IDisposable Support
		bool disposed;

		protected virtual void Dispose(bool disposing)
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
