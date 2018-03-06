//
// NetManager.cs
//
// Author:
//       M.A. (enmoku) <>
//
// Copyright (c) 2016-2018 M.A. (enmoku)
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
using System.Net;
using System.Net.NetworkInformation;
using System.Collections.Generic;
using System.Linq;
using Serilog;
using System.Threading.Tasks;

namespace TaskMaster
{
	public class NetworkStatus : EventArgs
	{
		public bool Available;
		public DateTime Start;
		public TimeSpan Uptime;
	}

	public class InternetStatus : NetworkStatus
	{
	}

	public class NetManager : IDisposable
	{
		DateTime lastUptimeStart;

		public event EventHandler<InternetStatus> InternetStatusChange;
		public event EventHandler<NetworkStatus> NetworkStatusChange;

		string dnstestaddress = "www.google.com";

		readonly object StateLock = new object();

		System.Timers.Timer deviceSampleTimer = new System.Timers.Timer();
		int DeviceTimerInterval = 15 * 60;
		System.Timers.Timer packletStatTimer = new System.Timers.Timer();
		int PacketStatTimerInterval = 15; // seconds
		public event EventHandler<NetDeviceTraffic> onSampling;

		void LoadConfig()
		{
			var cfg = TaskMaster.loadConfig("Net.ini");

			bool dirty = false;
			bool dirtyconf = false;

			var monsec = cfg["Monitor"];
			dnstestaddress = monsec.GetSetDefault("DNS test", "www.google.com", out dirty).StringValue;
			dirtyconf |= dirty;

			var devsec = cfg["Devices"];
			DeviceTimerInterval = devsec.GetSetDefault("Check frequency", 15, out dirty).IntValue.Constrain(1, 30) * 60;
			dirtyconf |= dirty;

			var pktsec = cfg["Traffic"];
			PacketStatTimerInterval = pktsec.GetSetDefault("Sample rate", 15, out dirty).IntValue.Constrain(1, 60);
			dirtyconf |= dirty;
			if (dirtyconf)
				TaskMaster.saveConfig(cfg);
		}

		public NetManager()
		{
			Since = DateTime.Now;

			lastUptimeStart = DateTime.Now;

			LoadConfig();

			InterfaceInitialization();

			NetworkSetup();

			LastChange.Enqueue(DateTime.MinValue);
			LastChange.Enqueue(DateTime.MinValue);
			LastChange.Enqueue(DateTime.MinValue);

			deviceSampleTimer.Interval = DeviceTimerInterval * 60000;
			deviceSampleTimer.Elapsed += SampleDeviceState;
			deviceSampleTimer.Start();
			CurrentInterfaceList = Interfaces();
			AnalyzePacketBehaviour(this, null); // initialize, not really needed
			packletStatTimer.Interval = PacketStatTimerInterval * 1000;
			packletStatTimer.Elapsed += AnalyzePacketBehaviour;
			packletStatTimer.Start();

			Log.Information("<Net Manager> Loaded.");
		}

		int packetWarning = 0;
		List<NetDevice> CurrentInterfaceList;
		async void AnalyzePacketBehaviour(object sender, EventArgs ev)
		{
			await Task.Yield();

			if (packetWarning > 0)
				packetWarning--;

			var ifaces = Interfaces();
			if (ifaces == null) return; // being called too often, shouldn't happen but eh.
			if (ifaces.Count == CurrentInterfaceList.Count) // Crude, but whatever. Prone to false statistics.
			{
				for (int index = 0; index < ifaces.Count; index++)
				{
					long errors = (ifaces[index].Incoming.Errors - CurrentInterfaceList[index].Incoming.Errors)
						+ (ifaces[index].Outgoing.Errors - CurrentInterfaceList[index].Outgoing.Errors);
					long discards = (ifaces[index].Incoming.Discarded - CurrentInterfaceList[index].Incoming.Discarded)
						+ (ifaces[index].Outgoing.Discarded - CurrentInterfaceList[index].Outgoing.Discarded);
					long packets = (ifaces[index].Incoming.Unicast - CurrentInterfaceList[index].Incoming.Unicast)
						+ (ifaces[index].Outgoing.Unicast - CurrentInterfaceList[index].Outgoing.Unicast);

					//Console.WriteLine("{0} : Packets(+{1}), Errors(+{2}), Discarded(+{3})", ifaces[index].Name, packets, errors, discards);

					if (errors > 0)
					{
						if (packetWarning == 0 || (packetWarning % PacketStatTimerInterval == 0))
						{
							packetWarning = 2;
							Log.Warning("<Net Monitor> {Device} is suffering from traffic errors! (+{Rate} since last sample)", ifaces[index].Name, errors);
						}
						else
							packetWarning++;
					}
					onSampling?.Invoke(this, new NetDeviceTraffic { Index = index, Traffic = new NetTraffic { Unicast = packets, Errors = errors, Discarded = discards } });
				}
			}
			else
				Console.WriteLine("Interface list changed.");
			CurrentInterfaceList = ifaces;
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
		DateTime Since;

		/// <summary>
		/// Current uptime in minutes.
		/// </summary>
		/// <value>The uptime.</value>
		public TimeSpan Uptime
		{
			get
			{
				if (TaskMaster.NetworkMonitorEnabled && InternetAvailable)
					return (DateTime.Now - lastUptimeStart);

				return TimeSpan.Zero;
			}
		}

		bool InternetAvailableLast = false;

		void ReportCurrentUpstate()
		{
			if (InternetAvailable)
			{
				if (InternetAvailable != InternetAvailableLast) // prevent spamming available message
				{
					Log.Information("<Network> Internet available.");
					//Log.Verbose("Current internet uptime: {UpTime:N1} minute(s)", Uptime.TotalMinutes);
				}
				else
				{
					if (InternetAvailable != InternetAvailableLast) // prevent spamming unavailable message
						Log.Warning("<Network> Internet access unavailable.");
				}
				InternetAvailableLast = InternetAvailable;
			}
		}

		void ReportUptime()
		{
			var ups = new System.Text.StringBuilder();

			ups.Append("<Network> Average uptime: ");
			lock (StateLock)
			{
				double currentUptime = (DateTime.Now - lastUptimeStart).TotalMinutes;

				ups.Append(string.Format("{0:N1}", ((uptimeTotal + currentUptime) / (uptimeSamples + 1)))).Append(" minutes");

				if (uptimeSamples > 3)
					ups.Append(" (").Append(string.Format("{0:N1}", upTime.GetRange(upTime.Count - 3, 3).Sum() / 3)).Append(" minutes for last 3 samples");
			}

			ups.Append(" since: ").Append(Since)
			   .Append(" (").Append(string.Format("{0:N2}", (DateTime.Now - Since).TotalHours)).Append("h ago)")
			   .Append(".");

			Log.Information(ups.ToString());

			ReportCurrentUpstate();
		}

		public void SampleDeviceState(object sender, EventArgs e)
		{
			RecordDeviceState(InternetAvailable, false);
		}

		bool lastOnlineState; // = false;
		static int upstateTesting; // = 0;
		void RecordDeviceState(bool online_state, bool address_changed)
		{
			if (online_state != lastOnlineState)
			{
				lock (StateLock)
					lastOnlineState = online_state;

				if (online_state)
				{
					lock (StateLock)
						lastUptimeStart = DateTime.Now;

					// this part is kinda pointless
					if (System.Threading.Interlocked.CompareExchange(ref upstateTesting, 1, 0) == 0)
					{
						System.Threading.Tasks.Task.Run(async () =>
						{
							//CLEANUP: Console.WriteLine("Debug: Queued internet uptime report");
							await System.Threading.Tasks.Task.Delay(new TimeSpan(0, 5, 0)); // wait 5 minutes

							ReportCurrentUpstate();
							upstateTesting = 0;
						});
					}
				}
				else // went offline
				{
					lock (StateLock)
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
					}
					ReportUptime();
				}
				return;
			}
			else if (address_changed)
			{
				// same state but address change was detected
				Console.WriteLine("DEBUG: Address changed but internet connectivity unaffected.");
			}
		}

		int checking_inet; // = 0;
		async Task CheckInet(bool address_changed = false)
		{
			// TODO: Figure out how to get Actual start time of internet connectivity.

			if (System.Threading.Interlocked.CompareExchange(ref checking_inet, 1, 0) == 1)
				return;

			if (TaskMaster.Trace)
				Log.Verbose("Checking internet connectivity...");

			await Task.Yield();

			bool oldInetAvailable = InternetAvailable;
			if (NetworkAvailable)
			{
				try
				{
					Dns.GetHostEntry(dnstestaddress); // FIXME: There should be some other method than DNS testing
					InternetAvailable = true;

					// Don't rely on DNS?
					/*
					Log.Debug("DEBUG: IPv6 interface: " + IPv6Interface.OperationalStatus.ToString()
					   + "; IPv4 interface: " + IPv4Interface.OperationalStatus.ToString());
					*/
				}
				catch (System.Net.Sockets.SocketException ex)
				{
					switch (ex.SocketErrorCode)
					{
						case System.Net.Sockets.SocketError.TimedOut:
							Log.Warning("Internet availability test timed-out: assuming we're online.");
							/*
							Task.Run(async delegate
							{
								await Task.Yield();
								CheckInet(false);
							});
							*/
							InternetAvailable = true; // timeout can only occur if we actually have internet.. sort of. We have no tri-state tho.
							return;
						case System.Net.Sockets.SocketError.NetworkDown:
						case System.Net.Sockets.SocketError.NetworkUnreachable:
						case System.Net.Sockets.SocketError.HostUnreachable:
						default:
							InternetAvailable = false;
							break;
					}
				}
			}
			else
				InternetAvailable = false;

			RecordDeviceState(InternetAvailable, address_changed);

			if (oldInetAvailable != InternetAvailable)
				Log.Information("Network status: {NetworkAvailable}, Internet status: {InternetAvailable}", (NetworkAvailable ? "Up" : "Down"), (InternetAvailable ? "Connected" : "Disconnected"));
			else
			{
				if (TaskMaster.Trace)
					Log.Verbose("Internet status unchanged");
			}

			checking_inet = 0;

			InternetStatusChange?.Invoke(this, new InternetStatus { Available = InternetAvailable, Start = lastUptimeStart, Uptime = Uptime });
		}

		List<IPAddress> AddressList = new List<IPAddress>(2);
		//List<NetworkInterface> PublicInterfaceList = new List<NetworkInterface>(2);
		IPAddress IPv4Address = IPAddress.None;
		NetworkInterface IPv4Interface;
		IPAddress IPv6Address = IPAddress.IPv6None;
		NetworkInterface IPv6Interface;

		void InterfaceInitialization()
		{
			NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
			foreach (NetworkInterface n in adapters)
			{
				if (n.NetworkInterfaceType == NetworkInterfaceType.Loopback || n.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
					continue;

				// TODO: Implement early exit and smarter looping

				IPAddress[] ipa = n.GetAddresses();
				foreach (IPAddress ip in ipa)
				{
					switch (ip.AddressFamily)
					{
						case System.Net.Sockets.AddressFamily.InterNetwork:
							IPv4Address = ip;
							IPv4Interface = n;
							//PublicInterfaceList.Add(n);
							break;
						case System.Net.Sockets.AddressFamily.InterNetworkV6:
							IPv6Address = ip;
							IPv6Interface = n;
							//PublicInterfaceList.Add(n);
							break;
					}
				}
			}
		}

		int enumerating_inet; // = 0;

		// TODO: This is unnecessarily heavy
		/// <summary>
		/// Returns list of interfaces.
		/// * Device Name
		/// * Type
		/// * Status
		/// * Link Speed
		/// * IPv4 Address
		/// * IPv6 Address
		/// </summary>
		/// <returns>string[] { Device Name, Type, Status, Link Speed, IPv4 Address, IPv6 Address } or null</returns>
		public List<NetDevice> Interfaces()
		{
			if (System.Threading.Interlocked.CompareExchange(ref enumerating_inet, 1, 0) == 1)
				return null; // bail if we were already doing this

			if (TaskMaster.DebugNetMonitor)
				Log.Debug("Enumerating network interfaces...");

			var ifacelist = new List<NetDevice>();
			//var ifacelist = new List<string[]>();

			int index = 0;
			NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
			foreach (NetworkInterface dev in adapters)
			{
				int ti = index++;
				if (dev.NetworkInterfaceType == NetworkInterfaceType.Loopback || dev.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
					continue;

				var stats = dev.GetIPStatistics();

				IPAddress _ipv4 = IPAddress.None, _ipv6 = IPAddress.None;
				foreach (UnicastIPAddressInformation ip in dev.GetIPProperties().UnicastAddresses)
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

				var devi = new NetDevice
				{
					Index = ti,
					Name = dev.Name,
					Type = dev.NetworkInterfaceType,
					Status = dev.OperationalStatus,
					Speed = dev.Speed,
					IPv4Address = _ipv4,
					IPv6Address = _ipv6,
				};

				devi.Incoming.From(stats, true);
				devi.Outgoing.From(stats, false);
				//devi.PrintStats();
				ifacelist.Add(devi);

				if (TaskMaster.DebugNetMonitor)
					Log.Debug("Interface: {InterfaceName}", dev.Name);
			}

			enumerating_inet = 0;

			return ifacelist;
		}

		Queue<DateTime> LastChange = new Queue<DateTime>(3);
		void NetAddrChanged(object sender, EventArgs e)
		{
			var tmpnow = DateTime.Now;
			IPAddress oldV6Address = IPv6Address;
			IPAddress oldV4Address = IPv4Address;

			LastChange.Dequeue();
			LastChange.Enqueue(tmpnow);

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
					Log.Warning("Unstable internet connectivity detected.");
			}

			//NetworkChanged(null,null);
		}

		void NetworkSetup()
		{
			NetworkChanged(null, null);
			NetworkAvailable = NetworkInterface.GetIsNetworkAvailable();

			NetworkChange.NetworkAvailabilityChanged += NetworkChanged;
			NetworkChange.NetworkAddressChanged += NetAddrChanged;

			//CheckInet().Wait(); // unnecessary?
		}

		async void NetworkChanged(object sender, EventArgs e)
		{
			bool oldNetAvailable = NetworkAvailable;
			NetworkAvailable = NetworkInterface.GetIsNetworkAvailable();

			// do stuff only if this is different from last time
			if (oldNetAvailable != NetworkAvailable)
			{
				Log.Information("Network status changed: " + (NetworkAvailable ? "Connected" : "Disconnected"));

				NetworkStatusChange?.Invoke(this, new NetworkStatus { Available = NetworkAvailable });

				await CheckInet();
			}

			CurrentInterfaceList = Interfaces();
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
				if (TaskMaster.Trace)
					Log.Verbose("Disposing network monitor...");
				ReportUptime();
			}

			disposed = true;
		}
	}
}
