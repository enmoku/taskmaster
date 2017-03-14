//
// NetMonitor.cs
//
// Author:
//       M.A. (enmoku) <>
//
// Copyright (c) 2016 M.A. (enmoku)
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

namespace TaskMaster
{
	using System;
	using System.Net;
	using System.Net.NetworkInformation;
	using System.Collections.Generic;
	using System.Linq;

	public class NetworkStatus : EventArgs
	{
		public bool Available;
		public DateTime Start;
		public TimeSpan Uptime;
	}

	public class InternetStatus : NetworkStatus
	{
	}

	public class NetMonitor : IDisposable
	{
		static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

		DateTime lastUptimeStart;

		public event EventHandler<InternetStatus> InternetStatusChange;
		public event EventHandler<NetworkStatus> NetworkStatusChange;

		string dnstestaddress = "www.google.com";

		public NetMonitor()
		{
			lastUptimeStart = DateTime.Now;

			var cfg = TaskMaster.loadConfig("Net.ini");
			SharpConfig.Section monsec = cfg["Monitor"];
			int oldsettings = monsec?.SettingCount ?? 0;
			dnstestaddress = monsec.GetSetDefault("DNS test", "www.google.com").StringValue;
			if (oldsettings != (monsec?.SettingCount ?? 0))
				TaskMaster.saveConfig("Net.ini", cfg);

			InterfaceInitialization();

			NetworkSetup();
		}

		TrayAccess tray;
		public TrayAccess Tray { private get { return tray; } set { tray = value; } }

		bool _netAvailable = false, _inetAvailable = false;
		public bool NetworkAvailable
		{
			get
			{
				return _netAvailable;
			}
			private set
			{
				_netAvailable = value;
			}
		}
		public bool InternetAvailable
		{
			get
			{
				return _inetAvailable;
			}
			private set
			{
				_inetAvailable = value;
			}
		}

		int uptimeSamples; // = 0;
		double uptimeTotal; // = 0;
		List<double> upTime = new List<double>();

		/// <summary>
		/// Current uptime in minutes.
		/// </summary>
		/// <value>The uptime.</value>
		public TimeSpan Uptime
		{
			get
			{
				if (TaskMaster.NetworkMonitorEnabled)
					return (DateTime.Now - lastUptimeStart);
				
				return TimeSpan.Zero;
			}
		}

		void ReportCurrentUptime()
		{
			Log.Info(string.Format("Current internet uptime: {0:1} minute(s)", Uptime.TotalMinutes));
		}

		void ReportUptime()
		{
			var ups = new System.Text.StringBuilder();

			ups.Append("Average uptime: ").Append(string.Format("{0:N1}", (uptimeTotal / uptimeSamples))).Append(" minutes");

			if (uptimeSamples > 3)
				ups.Append(" (").Append(string.Format("{0:N1}", upTime.GetRange(upTime.Count - 3, 3).Sum() / 3)).Append(" minutes for last 3 samples");

			ups.Append(".");

			Log.Info(ups.ToString());

			ReportCurrentUptime();
		}

		bool lastOnlineState; // = false;
		static int upstateTesting; // = 0;
		void RecordSample(bool online_state, bool address_changed)
		{
			if (online_state != lastOnlineState)
			{
				lastOnlineState = online_state;

				if (online_state)
				{
					lastUptimeStart = DateTime.Now;

					if (System.Threading.Interlocked.CompareExchange(ref upstateTesting, 1, 0) == 1)
					{
						System.Threading.Tasks.Task.Run(async () =>
						{
							//CLEANUP: Console.WriteLine("Debug: Queued internet uptime report");
							await System.Threading.Tasks.Task.Delay(new TimeSpan(0, 5, 0)); // wait 5 minutes

							ReportCurrentUptime();
							upstateTesting = 0;
						});
					}
				}
				else // went offline
				{
					double newUptime = (DateTime.Now - lastUptimeStart).TotalMinutes;
					upTime.Add(newUptime);
					uptimeTotal += newUptime;
					uptimeSamples += 1;
					if (uptimeSamples > 20)
					{
						uptimeTotal -= upTime[0];
						uptimeSamples -= 1;
						upTime.RemoveAt(0);
					}

					ReportUptime();
				}
			}
			else if (address_changed)
			{
				// same state but address change was detected
				Console.WriteLine("DEBUG: Address changed but internet connectivity unaffected.");
				ReportCurrentUptime();
			}
		}

		int checking_inet; // = 0;
		async System.Threading.Tasks.Task CheckInet(bool address_changed = false)
		{
			// TODO: Figure out how to get Actual start time of internet connectivity.

			if (System.Threading.Interlocked.CompareExchange(ref checking_inet, 1, 0) == 0)
				return;

			if (TaskMaster.Verbose)
				Log.Trace("Checking internet connectivity...");

			await System.Threading.Tasks.Task.Delay(100);

			bool oldInetAvailable = InternetAvailable;
			if (NetworkAvailable)
			{
				try
				{
					Dns.GetHostEntry(dnstestaddress); // FIXME: There should be some other method than DNS testing
					InternetAvailable = true;

					// Don't rely on DNS?
					Log.Info("DEBUG: IPv6 interface: " + (IPv6Interface.OperationalStatus == OperationalStatus.Up ? "Up" : "Down")
					         + "; IPv4 interface: " + (IPv4Interface.OperationalStatus == OperationalStatus.Up ? "Up" : "Down"));
				}
				catch (System.Net.Sockets.SocketException)
				{
					InternetAvailable = false;
				}
			}
			else
				InternetAvailable = false;

			RecordSample(InternetAvailable, address_changed);

			if (oldInetAvailable != InternetAvailable)
			{
				Log.Info("Network status: " + (NetworkAvailable ? "Up" : "Down") + ", Inet status: " + (InternetAvailable ? "Connected" : "Disconnected"));
			}

			checking_inet = 0;

			InternetStatusChange?.Invoke(this, new InternetStatus { Available = InternetAvailable, Start = lastUptimeStart, Uptime = Uptime });
		}

		List<IPAddress> AddressList = new List<IPAddress>(2);
		List<NetworkInterface> InterfaceList = new List<NetworkInterface>(2);
		IPAddress IPv4Address = IPAddress.None;
		NetworkInterface IPv4Interface;
		IPAddress IPv6Address = IPAddress.IPv6None;
		NetworkInterface IPv6Interface;

		private void InterfaceInitialization()
		{
			NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
			foreach (NetworkInterface n in adapters)
			{
				if (n.NetworkInterfaceType == NetworkInterfaceType.Loopback || n.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
					continue;

				IPAddress[] ipa = n.GetAddresses();
				foreach (IPAddress ip in ipa)
				{
					switch (ip.AddressFamily)
					{
						case System.Net.Sockets.AddressFamily.InterNetwork:
							IPv4Address = ip;
							IPv4Interface = n;
							InterfaceList.Add(n);
							break;
						case System.Net.Sockets.AddressFamily.InterNetworkV6:
							IPv6Address = ip;
							IPv6Interface = n;
							InterfaceList.Add(n);
							break;
					}
				}
			}
		}

		int enumerating_inet; // = 0;
		public List<string[]> Interfaces()
		{
			if (System.Threading.Interlocked.CompareExchange(ref enumerating_inet, 1, 0) == 1)
				return null; // bail if we were already doing this

			if (TaskMaster.Verbose)
				Log.Trace("Enumerating network interfaces...");

			var ifacelist = new List<string[]>();

			NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
			foreach (NetworkInterface n in adapters)
			{
				if (n.NetworkInterfaceType == NetworkInterfaceType.Loopback || n.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
					continue;

				IPAddress _ipv4=IPAddress.None, _ipv6=IPAddress.None;
				foreach (UnicastIPAddressInformation ip in n.GetIPProperties().UnicastAddresses)
				{
					// TODO: Maybe figure out better way and early bailout from the foreach
					switch (ip.Address.AddressFamily)
					{
						case System.Net.Sockets.AddressFamily.InterNetwork:
							_ipv4 = ip.Address;
							break;
						case System.Net.Sockets.AddressFamily.InterNetworkV6:
							_ipv6 = ip.Address;
							break;
					}
				}

				ifacelist.Add(new string[] {
					n.Name,
					n.NetworkInterfaceType.ToString(),
					n.OperationalStatus.ToString(),
					Utility.ByterateString(n.Speed),
					_ipv4?.ToString() ?? "n/a",
					_ipv6?.ToString() ?? "n/a"
				});
			}

			enumerating_inet = 0;

			return ifacelist;
		}

		Queue<DateTime> LastChange = new Queue<DateTime>(3);
		void NetAddrChanged(object sender, EventArgs e)
		{
			IPAddress oldV6Address = IPv6Address;
			IPAddress oldV4Address = IPv4Address;

			LastChange.Enqueue(DateTime.Now);
			if (LastChange.Count > 3)
				LastChange.Dequeue();
			
			CheckInet(address_changed: true).Wait();

			if (InternetAvailable)
			{
				//CLEANUP: Console.WriteLine("DEBUG: AddrChange: " + oldV4Address + " -> " + IPv4Address);
				//CLEANUP: Console.WriteLine("DEBUG: AddrChange: " + oldV6Address + " -> " + IPv6Address);

				bool ipv4changed = false, ipv6changed = false;
				ipv4changed = !oldV4Address.Equals(IPv4Address);
#if DEBUG
				if (ipv4changed)
				{
					var outstr4 = new System.Text.StringBuilder();
					outstr4.Append("IPv4 address changed: ");
					outstr4.Append(oldV4Address).Append(" -> ").Append(IPv4Address);
					Log.Debug(outstr4.ToString());
					Tray.Tooltip(2000, outstr4.ToString(), "TaskMaster", System.Windows.Forms.ToolTipIcon.Info);
				}
#endif
				ipv6changed = !oldV6Address.Equals(IPv6Address);
#if DEBUG
				if (ipv6changed)
				{
					var outstr6 = new System.Text.StringBuilder();
					outstr6.Append("IPv6 address changed: ");
					outstr6.Append(oldV6Address).Append(" -> ").Append(IPv6Address);
					Log.Debug(outstr6.ToString());
				}
#endif

				if (!ipv4changed && !ipv6changed && (LastChange.Peek() - DateTime.Now).Minutes < 5)
					Log.Warn("Unstable internet connectivity detected.");
			}

			//NetworkChanged(null,null);
		}

		void NetworkSetup()
		{
			NetworkChanged(null, null);
			NetworkAvailable = NetworkInterface.GetIsNetworkAvailable();

			NetworkChange.NetworkAvailabilityChanged += NetworkChanged;
			NetworkChange.NetworkAddressChanged += NetAddrChanged;

			CheckInet().Wait();
		}

		async void NetworkChanged(object sender, EventArgs e)
		{
			bool oldNetAvailable = NetworkAvailable;
			NetworkAvailable = NetworkInterface.GetIsNetworkAvailable();

			// do stuff only if this is different from last time
			if (oldNetAvailable != NetworkAvailable)
			{
				Log.Debug("Network status changed: " + (NetworkAvailable ? "Connected" : "Disconnected"));

				NetworkStatusChange?.Invoke(this, new NetworkStatus { Available = NetworkAvailable });

				await System.Threading.Tasks.Task.Delay(200);

				await CheckInet();
			}
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		bool disposed; // = false;
		protected virtual void Dispose(bool disposing)
		{
			if (disposed) return;

			//base.Dispose(disposing);

			if (disposing)
			{
				ReportUptime();
			}

			disposed = true;
		}
	}
}
