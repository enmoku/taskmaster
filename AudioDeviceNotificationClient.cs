//
// AudioDeviceNotificationClient.cs
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

namespace Taskmaster
{
	class AudioDeviceNotificationClient : NAudio.CoreAudioApi.Interfaces.IMMNotificationClient
	{
		/// <summary>
		/// Default device GUID, Role, and Flow.
		/// GUID is null if there's no default.
		/// </summary>
		public event EventHandler<Events.AudioDefaultDeviceEventArgs> DefaultDevice;
		public event EventHandler Changed;
		public event EventHandler<Events.AudioDeviceEventArgs> Added;
		public event EventHandler<Events.AudioDeviceEventArgs> Removed;
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

				DefaultDevice?.Invoke(this, new Events.AudioDefaultDeviceEventArgs(guid, defaultDeviceId, role, flow));
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
				string guid = AudioManager.AudioDeviceIdToGuid(pwstrDeviceId);

				Log.Debug("<Audio> Device added: " + guid);

				Added?.Invoke(this, new Events.AudioDeviceEventArgs(guid, pwstrDeviceId));
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
				string guid = AudioManager.AudioDeviceIdToGuid(deviceId);

				Log.Debug("<Audio> Device removed: " + guid);

				Removed?.Invoke(this, new Events.AudioDeviceEventArgs(guid, deviceId));
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

				var guid = AudioManager.AudioDeviceIdToGuid(deviceId);

				if (Taskmaster.DebugAudio) Log.Debug("<Audio> Device (" + guid + ") state: " + newState.ToString());

				StateChanged?.Invoke(this, new Events.AudioDeviceStateEventArgs(guid, deviceId, newState));
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
				string guid = AudioManager.AudioDeviceIdToGuid(pwstrDeviceId);

				Log.Debug("<Audio> Device (" + guid + ") property changed: " + key.ToString());

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