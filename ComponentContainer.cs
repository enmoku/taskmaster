//
// ComponentContainer.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2018 M.A.
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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Taskmaster
{
	public class ComponentContainer : IDisposable
	{
		public MicManager micmonitor = null;
		public MainWindow mainwindow = null;
		public ProcessManager processmanager = null;
		public TrayAccess trayaccess = null;
		public NetManager netmonitor = null;
		public DiskManager diskmanager = null;
		public PowerManager powermanager = null;
		public ActiveAppManager activeappmonitor = null;
		public HealthMonitor healthmonitor = null;
		public SelfMaintenance selfmaintenance = null;

		public ComponentContainer()
		{

		}

		#region IDisposable Support
		private bool disposed = false;

		protected virtual void Dispose(bool disposing)
		{
			if (!disposed)
			{
				if (disposing)
				{
					//
					micmonitor?.Dispose();
					mainwindow?.Dispose();
					processmanager?.Dispose();
					trayaccess?.Dispose();
					netmonitor?.Dispose();
					diskmanager?.Dispose();
					powermanager?.Dispose();
					activeappmonitor?.Dispose();
					healthmonitor?.Dispose();
					selfmaintenance?.Dispose();
				}

				disposed = true;
			}
		}

		public void Dispose()
		{
			Dispose(true);
		}
		#endregion
	}
}
