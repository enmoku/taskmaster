//
// Audio.Device.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2017-2020 M.A.
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
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Taskmaster.Audio
{
	public class Device : IDisposable
	{
		public Device(NAudio.CoreAudioApi.MMDevice device)
			: this(Utility.DeviceIdToGuid(device.ID), string.Empty, device.DataFlow, device.State, device)
		{
			try
			{
				Name = device.FriendlyName;
			}
			catch { /* NOP */ }
		}

		public Device(Guid guid, string name, NAudio.CoreAudioApi.DataFlow flow, NAudio.CoreAudioApi.DeviceState state, NAudio.CoreAudioApi.MMDevice device)
		{
			GUID = guid;
			Name = name;
			State = state;
			Flow = flow;
			MMDevice = device;
		}

		public string Name { get; }
		public Guid GUID { get; }

		public bool VolumeControl { get; set; }
		public float Volume { get; set; } = float.NaN;
		public float Target { get; set; } = float.NaN;

		public NAudio.CoreAudioApi.DeviceState State { get; set; }
		public NAudio.CoreAudioApi.DataFlow Flow { get; set; }

		public NAudio.CoreAudioApi.MMDevice MMDevice { get; private set; }

		public override string ToString() => $"{Name ?? "n/a"} {{{GUID}}}";

		/// <summary>
		/// Prints either Name or GUID, not both.
		/// </summary>
		public string ToShortString() => !string.IsNullOrEmpty(Name) ? Name : $"{{{GUID}}}";

		#region IDisposable Support
		private bool disposed; // To detect redundant calls

		readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;

		public event EventHandler? OnDisposed;

		~Device() => Dispose(false);

		protected virtual void Dispose(bool disposing)
		{
			if (disposed) return;
			disposed = true;

			OnDisposed?.Invoke(this, EventArgs.Empty);
			OnDisposed = null;

			try
			{
				dispatcher.Invoke(() => MMDevice.Dispose(), DispatcherPriority.Normal, CancellationToken.None);
			}
			catch (TaskCanceledException)
			{
				// NOP
			}

			/*
			if (MKAh.Execution.IsMainThread)
			{
				MMDevice.Dispose();  // HACK: must happen in same thread as created
			}
			*/

			//base.Dispose();
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}
		#endregion
	}
}