//
// ModuleManager.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2019–2020 M.A.
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
	public class ModuleManager
	{
		public Audio.MicManager? micmonitor;
		public UI.MainWindow? mainwindow;
		public UI.VolumeMeter? volumemeter;
		public UI.LoaderDisplay? loaderdisplay;
		public Process.Manager? processmanager;
		public UI.TrayAccess trayaccess;
		public Network.Manager? netmonitor;
		public StorageManager? storagemanager;
		public Power.Manager? powermanager;
		public Process.ForegroundManager? activeappmonitor;
		public HealthMonitor? healthmonitor;
		public SelfMaintenance selfmaintenance;
		public Audio.Manager? audiomanager;
		public Hardware.CPUMonitor? cpumonitor;
		public Hardware.Monitor? hardware;
		//public AlertManager alerts;

		// TODO: Disposal?
	}

	public static partial class Application
	{
		public static bool ProcessMonitorEnabled { get; private set; } = true;
		public static bool MicrophoneManagerEnabled { get; private set; } = false;
		// public static bool MediaMonitorEnabled { get; private set; } = true;
		public static bool NetworkMonitorEnabled { get; private set; } = false;
		public static bool PagingEnabled { get; private set; } = false;
		public static bool ActiveAppMonitorEnabled { get; private set; } = false;
		public static bool PowerManagerEnabled { get; private set; } = false;
		public static bool MaintenanceMonitorEnabled { get; private set; } = false;
		public static bool StorageMonitorEnabled { get; private set; } = false;
		public static bool HealthMonitorEnabled { get; private set; } = true;
		public static bool AudioManagerEnabled { get; private set; } = false;
		public static bool HardwareMonitorEnabled { get; private set; } = false;
		public static bool AlertManagerEnabled { get; private set; } = false;
	}
}