//
// Audio.Device.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2017-2019 M.A.
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

namespace Taskmaster.Audio
{
	sealed public class Device : IDisposable
	{
		public Device(NAudio.CoreAudioApi.MMDevice device)
			: this(Manager.AudioDeviceIdToGuid(device.ID), device.FriendlyName, device.DataFlow, device.State, device)
		{
			// nop
		}

		public Device(string guid, string name, NAudio.CoreAudioApi.DataFlow flow, NAudio.CoreAudioApi.DeviceState state, NAudio.CoreAudioApi.MMDevice device)
		{
			GUID = guid;
			Name = name;
			State = state;
			Flow = flow;
			MMDevice = device;
		}

		public string Name { get; private set; }
		public string GUID { get; private set; }

		public bool VolumeControl { get; set; }
		public float Volume { get; set; }
		public float Target { get; set; }

		public NAudio.CoreAudioApi.DeviceState State { get; set; }
		public NAudio.CoreAudioApi.DataFlow Flow { get; set; }

		public NAudio.CoreAudioApi.MMDevice MMDevice { get; private set; } = null;

		public override string ToString() => $"{Name ?? "n/a"} {{{GUID ?? "n/a"}}}";

		#region IDisposable Support
		bool DisposingOrDisposed = false; // To detect redundant calls

		~Device() => Dispose(false);

		void Dispose(bool disposing)
		{
			if (DisposingOrDisposed) return;
			DisposingOrDisposed = true;

			if (MKAh.Execution.IsMainThread)
			{
				MMDevice?.Dispose();  // HACK: must happen in same thread as created
				MMDevice = null;
			}
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}
		#endregion
	}
}