//
// Audio.DeviceNotificationClient.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2019 M.A.
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
using NAudio.CoreAudioApi;
using Serilog;

namespace Taskmaster.Audio
{
	using static Taskmaster;

	class DeviceNotificationClient : NAudio.CoreAudioApi.Interfaces.IMMNotificationClient
	{
		/// <summary>
		/// Default device GUID, Role, and Flow.
		/// GUID is null if there's no default.
		/// </summary>
		public event EventHandler<DefaultDeviceEventArgs> DefaultDevice;
		public event EventHandler Changed;
		public event EventHandler<DeviceEventArgs> Added;
		public event EventHandler<DeviceEventArgs> Removed;
		public event EventHandler<DeviceStateEventArgs> StateChanged;
		public event EventHandler PropertyChanged;

		public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
		{
			bool HaveDefaultDevice = !string.IsNullOrEmpty(defaultDeviceId);

			try
			{
				var guid = HaveDefaultDevice ? Utility.DeviceIdToGuid(defaultDeviceId) : Guid.Empty;

				if (DebugAudio && Trace)
					Log.Verbose($"<Audio> Default device changed for {role.ToString()} ({flow.ToString()}): {(HaveDefaultDevice ? guid.ToString() : HumanReadable.Generic.NotAvailable)}");

				DefaultDevice?.Invoke(this, new DefaultDeviceEventArgs(guid, defaultDeviceId, role, flow));
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
			try
			{
				var ea = new DeviceEventArgs(pwstrDeviceId);
				if (!DebugAudio) Logging.DebugMsg("Audio.DeviceNotificationClient.OnDeviceAdded: " + ea.ID);
				Added?.Invoke(this, ea);
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		public void OnDeviceRemoved(string deviceId)
		{
			try
			{
				var ea = new DeviceEventArgs(deviceId);
				if (!DebugAudio) Logging.DebugMsg("Audio.DeviceNotificationClient.OnDeviceRemoved: " + ea.ID);
				Removed?.Invoke(this, ea);
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
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

				var ea = new DeviceStateEventArgs(deviceId, newState);
				StateChanged?.Invoke(this, ea);
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
		{
			try
			{
				if (DebugAudio)
				{
					var guid = Utility.DeviceIdToGuid(pwstrDeviceId);

					var device = audiomanager?.GetDevice(guid);

					Log.Debug("<Audio> Device " + (device?.Name ?? guid.ToString()) + " property changed: " + key.formatId.ToString() + " [" + key.propertyId + "]");
				}

				//PropertyChanged?.Invoke(this, null);
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}

		}
	}
}