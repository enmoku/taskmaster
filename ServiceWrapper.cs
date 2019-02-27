//
// ServiceWrapper.cs
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
using System.ServiceProcess;
using Serilog;

namespace Taskmaster
{
	public sealed class ServiceWrapper : IDisposable
	{
		readonly string ServiceName;

		public ServiceWrapper(string service)
		{
			ServiceName = service;

			Service = new Lazy<ServiceController>(() => new ServiceController(ServiceName));
		}

		bool Running => Service.Value.Status == ServiceControllerStatus.Running;

		public bool NeedsRestart { get; private set; } = false;

		public void Start()
		{
			if (DisposedOrDisposing || !NeedsRestart || !Service.IsValueCreated) return;

			try
			{
				Service.Value.Refresh();

				// TODO:
				switch (Service.Value.Status)
				{
					case ServiceControllerStatus.Running:
					case ServiceControllerStatus.ContinuePending:
					case ServiceControllerStatus.StartPending:
						return;
					case ServiceControllerStatus.PausePending:
					case ServiceControllerStatus.StopPending:
						// TODO: Schedule restart
						break;
					case ServiceControllerStatus.Paused:
						if (Service.Value.CanPauseAndContinue)
							Service.Value.Continue();
						else
							goto Restart;
						break;
					case ServiceControllerStatus.Stopped:
					Restart:
						Service.Value.Start();
						break;
				}

				NeedsRestart = false;
			}
			catch (Exception ex) when (ex is NullReferenceException || ex is OutOfMemoryException) { throw; }
		}

		public void Stop()
		{
			if (DisposedOrDisposing) return;

			if (!NeedsRestart) return;

			bool cRunning = false;

			try
			{
				Service.Value.Refresh();

				switch (Service.Value.Status)
				{
					case ServiceControllerStatus.Running:
						if (Service.Value.CanPauseAndContinue)
							Service.Value.Pause();
						else
							Service.Value.Stop();
						NeedsRestart = true;
						break;
					case ServiceControllerStatus.ContinuePending:
					case ServiceControllerStatus.StartPending:
						// TODO: Schedule later stop/pause
						break;
					case ServiceControllerStatus.Paused:
					case ServiceControllerStatus.PausePending:
						break;
					case ServiceControllerStatus.Stopped:
					case ServiceControllerStatus.StopPending:
						break;
				}
			}
			catch (InvalidOperationException ex) // not running
			{
				Log.Error(ex, "<Exclusive> Failure to stop WUA");
			}
			catch (Exception ex) when (ex is NullReferenceException || ex is OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		readonly Lazy<ServiceController> Service;

		#region IDisposable Support
		~ServiceWrapper() => Dispose(false);

		private bool DisposedOrDisposing = false; // To detect redundant calls

		void Dispose(bool disposing)
		{
			if (DisposedOrDisposing) return;

			// this is desired even on destructor
			if (Service.IsValueCreated)
			{
				if (NeedsRestart) Start();
				Service.Value.Dispose();
			}

			DisposedOrDisposing = true;
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}
		#endregion

	}
}
