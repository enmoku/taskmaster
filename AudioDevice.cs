//
// AudioDevice.cs
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

namespace Taskmaster
{
	sealed public class AudioDevice : System.IDisposable
	{
		public AudioDevice(NAudio.CoreAudioApi.MMDevice device)
		{
			GUID = AudioManager.AudioDeviceIdToGuid(device.ID);
			Name = device.FriendlyName;
			State = device.State;
			Flow = device.DataFlow;
			MMDevice = device;
		}

		public AudioDevice(string guid, string name, NAudio.CoreAudioApi.DataFlow flow, NAudio.CoreAudioApi.DeviceState state, NAudio.CoreAudioApi.MMDevice device)
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
		public double Volume { get; set; }
		public double Target { get; set; }

		public NAudio.CoreAudioApi.DeviceState State { get; set; }
		public NAudio.CoreAudioApi.DataFlow Flow { get; set; }

		public NAudio.CoreAudioApi.MMDevice MMDevice { get; private set; }

		public override string ToString() => $"{Name ?? "n/a"} {{{GUID ?? "n/a"}}}";

		#region IDisposable Support
		private bool DisposingOrDisposed = false; // To detect redundant calls

		void Dispose(bool disposing)
		{
			if (!DisposingOrDisposed)
			{
				DisposingOrDisposed = true;

				if (disposing)
				{
					//MMDevice?.Dispose(); // hangs
				}
			}
		}

		// TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
		// ~AudioDevice() {
		//   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
		//   Dispose(false);
		// }

		// This code added to correctly implement the disposable pattern.
		public void Dispose()
		{
			// Do not change this code. Put cleanup code in Dispose(bool disposing) above.
			Dispose(true);
			// TODO: uncomment the following line if the finalizer is overridden above.
			// GC.SuppressFinalize(this);
		}
		#endregion
	}
}