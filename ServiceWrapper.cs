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
using System.Management;
using System.ServiceProcess;
using Serilog;

namespace Taskmaster
{
	public class ServiceWrapper : IDisposable
	{
		readonly string ServiceName;

		/// <summary>
		/// If true, stopping causes the service to be also disabled.
		/// </summary>
		public bool FullDisable = false;

		readonly Lazy<ServiceController> Service;
		readonly Lazy<ManagementObject> WMI;

		public ServiceWrapper(string service, string scope = @"\\.\root\CIMV2")
		{
			ServiceName = service;

			Service = new Lazy<ServiceController>(() => new ServiceController(ServiceName), false);
			WMI = new Lazy<ManagementObject>(() => new ManagementObject(scope, $"Win32_Service.Name='{ServiceName}'", null), false);
		}

		bool Running => Service.Value.Status == ServiceControllerStatus.Running;

		public bool NeedsRestart { get; private set; } = false;

		bool NeedsEnable = false;

		public void Disable()
		{
			try
			{
				var mode = WMI.Value.GetPropertyValue("StarMode") as string;

				if (string.IsNullOrEmpty(mode) || mode.Equals("disabled", StringComparison.InvariantCultureIgnoreCase))
					return;

				Logging.DebugMsg($"SERVICE [{ServiceName}] DISABLE");

				WMI.Value.SetPropertyValue("StartMode", "Disabled");

				NeedsEnable = true;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		public void Enable()
		{
			if (disposed || !NeedsEnable || !WMI.IsValueCreated) return;

			try
			{
				Logging.DebugMsg($"SERVICE [{ServiceName}] ENABLE");

				WMI.Value.SetPropertyValue("StartMode", "Automatic");
				WMI.Value.SetPropertyValue("DelayedAutoStart", true);

				NeedsEnable = false;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		/// <summary>Start or unpause service.</summary>
		/// <exception cref="System.ComponentModel.Win32Exception"></exception>
		/// <exception cref="InvalidOperationException"></exception>
		public void Start(bool enable = false)
		{
			if (disposed || !NeedsRestart || !Service.IsValueCreated) return;

			try
			{
				if (enable) Enable();

				Service.Value.Refresh();

				// TODO:
				var status = Service.Value.Status;
				switch (status)
				{
					case ServiceControllerStatus.Running:
					case ServiceControllerStatus.ContinuePending:
					case ServiceControllerStatus.StartPending:
						return;
					case ServiceControllerStatus.PausePending:
					case ServiceControllerStatus.StopPending:
						// TODO: Schedule restart
						break;
					case ServiceControllerStatus.Stopped:
					case ServiceControllerStatus.Paused:
						if (status == ServiceControllerStatus.Paused && Service.Value.CanPauseAndContinue)
							Service.Value.Continue();
						else
							Service.Value.Start();
						break;
				}

				NeedsRestart = false;
			}
			catch (Exception ex) when (ex is NullReferenceException || ex is OutOfMemoryException) { throw; }
		}

		public void Stop(bool disable = false)
		{
			if (disposed) return;

			if (!NeedsRestart) return;

			try
			{
				if (disable) Disable();

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

		#region IDisposable Support
		~ServiceWrapper() => Dispose(false);

		bool disposed = false; // To detect redundant calls

		void Dispose(bool disposing)
		{
			if (disposed) return;

			if (disposing)
			{
				// this is desired even on destructor
				if (Service.IsValueCreated)
				{
					if (NeedsRestart) Start();
					Service.Value.Dispose();
				}

				//base.Dispose();
			}

			disposed = true;
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}
		#endregion

	}
}
