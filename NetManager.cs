//
// NetManager.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2016-2018 M.A.
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
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using Serilog;

namespace Taskmaster
{
	public class NetworkStatus : EventArgs
	{
		public bool Available;
		public DateTime Start;
		public TimeSpan Uptime;
	}

	sealed public class InternetStatus : NetworkStatus
	{
	}

	sealed public class NetManager : IDisposable
	{
		DateTime lastUptimeStart;

		public event EventHandler<InternetStatus> InternetStatusChange;
		public event EventHandler<NetworkStatus> NetworkStatusChange;

		string dnstestaddress = "www.google.com";

		readonly object uptime_lock = new object();

		int DeviceTimerInterval = 15 * 60;
		int PacketStatTimerInterval = 15; // second

		System.Threading.Timer deviceSampleTimer;
		System.Threading.Timer packetStatTimer;

		public event EventHandler<NetDeviceTraffic> onSampling;

		void LoadConfig()
		{
			var cfg = Taskmaster.LoadConfig("Net.ini");

			var dirty = false;
			var dirtyconf = false;

			var monsec = cfg["Monitor"];
			dnstestaddress = monsec.GetSetDefault("DNS test", "www.google.com", out dirty).StringValue;
			dirtyconf |= dirty;

			var devsec = cfg["Devices"];
			DeviceTimerInterval = devsec.GetSetDefault("Check frequency", 15, out dirty).IntValue.Constrain(1, 30) * 60;
			dirtyconf |= dirty;

			var pktsec = cfg["Traffic"];
			PacketStatTimerInterval = pktsec.GetSetDefault("Sample rate", 15, out dirty).IntValue.Constrain(1, 60);
			dirtyconf |= dirty;
			if (dirtyconf) Taskmaster.SaveConfig(cfg);

			Log.Information("<Network> Traffic sample frequency: {Interval}s", PacketStatTimerInterval);
		}

		public NetManager()
		{
			Since = DateTime.Now;

			lastUptimeStart = DateTime.Now;

			LoadConfig();

			InterfaceInitialization();

			LastChange.Enqueue(DateTime.MinValue);
			LastChange.Enqueue(DateTime.MinValue);
			LastChange.Enqueue(DateTime.MinValue);

			UpdateInterfaces();

			// Log.Debug("{IFACELIST} – count: {c}", CurrentInterfaceList, CurrentInterfaceList.Count);

			deviceSampleTimer = new System.Threading.Timer(async (s) => { await RecordDeviceState(InternetAvailable, false); }, null, 15000, DeviceTimerInterval * 60000);

			AnalyzeTrafficBehaviourTick(null); // initialize, not really needed
			packetStatTimer = new System.Threading.Timer(AnalyzeTrafficBehaviourTick, null, 500, PacketStatTimerInterval * 1000);

			Log.Information("<Network> Component loaded.");
		}

		int packetWarning = 0;
		List<NetDevice> PreviousInterfaceList = new List<NetDevice>();
		List<NetDevice> CurrentInterfaceList = new List<NetDevice>();

		async void AnalyzeTrafficBehaviourTick(object state) => AnalyzeTrafficBehaviour();

		int analyzetrafficbehaviour_lock = 0;
		async void AnalyzeTrafficBehaviour()
		{
			Debug.Assert(CurrentInterfaceList != null);

			if (!Atomic.Lock(ref analyzetrafficbehaviour_lock)) return;

			await Task.Delay(0);

			try
			{
				if (packetWarning > 0) packetWarning--;

				var oldifaces = CurrentInterfaceList;
				UpdateInterfaces();
				var ifaces = CurrentInterfaceList;

				if (ifaces == null) return; // no interfaces, just quit

				for (int index = 0; index < ifaces.Count; index++)
				{
					var errors = (ifaces[index].Incoming.Errors - oldifaces[index].Incoming.Errors)
						+ (ifaces[index].Outgoing.Errors - oldifaces[index].Outgoing.Errors);
					var discards = (ifaces[index].Incoming.Discarded - oldifaces[index].Incoming.Discarded)
						+ (ifaces[index].Outgoing.Discarded - oldifaces[index].Outgoing.Discarded);
					var packets = (ifaces[index].Incoming.Unicast - oldifaces[index].Incoming.Unicast)
						+ (ifaces[index].Outgoing.Unicast - oldifaces[index].Outgoing.Unicast);

					// Console.WriteLine("{0} : Packets(+{1}), Errors(+{2}), Discarded(+{3})", ifaces[index].Name, packets, errors, discards);

					if (errors > 0)
					{
						if (packetWarning == 0 || (packetWarning % PacketStatTimerInterval == 0))
						{
							packetWarning = 2;
							Log.Warning("<Network> {Device} is suffering from traffic errors! (+{Rate} since last sample)", ifaces[index].Name, errors);
						}
						else
							packetWarning++;
					}

					onSampling?.Invoke(this, new NetDeviceTraffic { Index = index, Traffic = new NetTraffic { Unicast = packets, Errors = errors, Discarded = discards } });
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				Atomic.Unlock(ref analyzetrafficbehaviour_lock);
			}
		}

		public TrayAccess Tray { get; set; } // bad design

		public bool NetworkAvailable { get; private set; }
		public bool InternetAvailable { get; private set; }

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
				if (Taskmaster.NetworkMonitorEnabled && InternetAvailable)
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
					// Log.Verbose("Current internet uptime: {UpTime:N1} minute(s)", Uptime.TotalMinutes);
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
			lock (uptime_lock)
			{
				var currentUptime = (DateTime.Now - lastUptimeStart).TotalMinutes;

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

		public async void SampleDeviceState(object state)
		{
			await RecordDeviceState(InternetAvailable, false);
		}

		bool lastOnlineState; // = false;
		static int upstateTesting; // = 0;
		async Task RecordDeviceState(bool online_state, bool address_changed)
		{
			if (online_state != lastOnlineState)
			{
				lastOnlineState = online_state;

				if (online_state)
				{
					lastUptimeStart = DateTime.Now;

					// this part is kinda pointless
					if (Atomic.Lock(ref upstateTesting))
					{
						// CLEANUP: Console.WriteLine("Debug: Queued internet uptime report");
						await Task.Delay(new TimeSpan(0, 5, 0)); // wait 5 minutes

						ReportCurrentUpstate();
						upstateTesting = 0;
					}
				}
				else // went offline
				{
					lock (uptime_lock)
					{
						var newUptime = (DateTime.Now - lastUptimeStart).TotalMinutes;
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
				Console.WriteLine("<Network> DEBUG: Address changed but internet connectivity unaffected.");
			}
		}

		int checking_inet; // = 0;
		async Task CheckInet(bool address_changed = false)
		{
			// TODO: Figure out how to get Actual start time of internet connectivity.

			if (!Atomic.Lock(ref checking_inet)) return;

			if (Taskmaster.Trace) Log.Verbose("<Network> Checking internet connectivity...");

			var oldInetAvailable = InternetAvailable;
			if (NetworkAvailable)
			{
				try
				{
					Dns.GetHostEntry(dnstestaddress); // FIXME: There should be some other method than DNS testing
					InternetAvailable = true;
					// TODO: Don't rely on DNS?
				}
				catch (System.Net.Sockets.SocketException ex)
				{
					InternetAvailable = false;
					switch (ex.SocketErrorCode)
					{
						case System.Net.Sockets.SocketError.AccessDenied:
						case System.Net.Sockets.SocketError.SystemNotReady:
							break;
						case System.Net.Sockets.SocketError.TryAgain:
						case System.Net.Sockets.SocketError.TimedOut:
						default:
							Log.Information("<Network> Internet availability test inconclusive, assuming connected.");
							InternetAvailable = true;
							Atomic.Unlock(ref checking_inet);
							return;
						case System.Net.Sockets.SocketError.SocketError:
						case System.Net.Sockets.SocketError.Interrupted:
						case System.Net.Sockets.SocketError.Fault:
							Log.Warning("<Network> Internet check interrupted. Potential hardware/driver issues.");
							break;
						case System.Net.Sockets.SocketError.HostUnreachable:
						case System.Net.Sockets.SocketError.HostNotFound:
						case System.Net.Sockets.SocketError.HostDown:
							Log.Warning("<Network> DNS test failed, test host unreachable. Test host may be down.");
							break;
						case System.Net.Sockets.SocketError.NetworkDown:
						case System.Net.Sockets.SocketError.NetworkReset:
						case System.Net.Sockets.SocketError.NetworkUnreachable:
							break;
					}
				}
			}
			else
				InternetAvailable = false;

			RecordDeviceState(InternetAvailable, address_changed);

			if (oldInetAvailable != InternetAvailable)
			{
				string status = "All OK";
				if (NetworkAvailable && !InternetAvailable) status = "ISP/route problems";
				else if (!NetworkAvailable) status = "Cable unplugged or router/modem down";

				Log.Information("<Network> Status: {NetworkAvailable}, Internet: {InternetAvailable} – {Status}",
					(NetworkAvailable ? "Up" : "Down"),
					(InternetAvailable ? "Connected" : "Disconnected"),
					status);
			}
			else
			{
				if (Taskmaster.Trace) Log.Verbose("<Network> Connectivity unchanged.");
			}

			Atomic.Unlock(ref checking_inet);

			InternetStatusChange?.Invoke(this, new InternetStatus { Available = InternetAvailable, Start = lastUptimeStart, Uptime = Uptime });
		}

		List<IPAddress> AddressList = new List<IPAddress>(2);
		// List<NetworkInterface> PublicInterfaceList = new List<NetworkInterface>(2);
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
							// PublicInterfaceList.Add(n);
							break;
						case System.Net.Sockets.AddressFamily.InterNetworkV6:
							IPv6Address = ip;
							IPv6Interface = n;
							// PublicInterfaceList.Add(n);
							break;
					}
				}
			}
		}

		object interfaces_lock = new object();
		int updateinterface_lock = 0;

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
		public void UpdateInterfaces()
		{
			if (!Atomic.Lock(ref updateinterface_lock)) return;

			try
			{
				// TODO: Rate limit
				if (Taskmaster.DebugNet) Log.Debug("<Network> Enumerating network interfaces...");

				var ifacelistt = new List<NetDevice>();
				// var ifacelist = new List<string[]>();

				var index = 0;
				NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
				foreach (NetworkInterface dev in adapters)
				{
					var ti = index++;
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
					// devi.PrintStats();
					ifacelistt.Add(devi);

					if (Taskmaster.DebugNet)
						Log.Debug("<Network> Interface: {InterfaceName}", dev.Name);
				}

				lock (interfaces_lock) CurrentInterfaceList = ifacelistt;
			}
			finally
			{
				Atomic.Unlock(ref updateinterface_lock);
			}
		}

		public List<NetDevice> GetInterfaces() => CurrentInterfaceList;

		Queue<DateTime> LastChange = new Queue<DateTime>(3);
		async void NetAddrChanged(object sender, EventArgs e)
		{
			var tmpnow = DateTime.Now;
			var oldV6Address = IPv6Address;
			var oldV4Address = IPv4Address;

			LastChange.Dequeue();
			LastChange.Enqueue(tmpnow);

			await CheckInet(address_changed: true).ConfigureAwait(false);

			if (InternetAvailable)
			{
				// CLEANUP: Console.WriteLine("DEBUG: AddrChange: " + oldV4Address + " -> " + IPv4Address);
				// CLEANUP: Console.WriteLine("DEBUG: AddrChange: " + oldV6Address + " -> " + IPv6Address);

				bool ipv4changed = false, ipv6changed = false;
				ipv4changed = !oldV4Address.Equals(IPv4Address);

				if (ipv4changed)
				{
					var outstr4 = new System.Text.StringBuilder();
					outstr4.Append("IPv4 address changed: ");
					outstr4.Append(oldV4Address).Append(" -> ").Append(IPv4Address);
					Log.Debug(outstr4.ToString());

					Tray.Tooltip(2000, outstr4.ToString(), "Taskmaster", System.Windows.Forms.ToolTipIcon.Info);
					// TODO: Make clicking on the tooltip copy new IP to clipboard?
				}

				ipv6changed = !oldV6Address.Equals(IPv6Address);

				if (ipv6changed)
				{
					var outstr6 = new System.Text.StringBuilder();
					outstr6.Append("IPv6 address changed: ");
					outstr6.Append(oldV6Address).Append(" -> ").Append(IPv6Address);
					Log.Debug(outstr6.ToString());

					Tray.Tooltip(2000, outstr6.ToString(), "Taskmaster", System.Windows.Forms.ToolTipIcon.Info);
				}

				if (!ipv4changed && !ipv6changed && (LastChange.Peek() - DateTime.Now).Minutes < 5)
				{
					Log.Warning("<Network> Unstable connectivity detected.");

					Tray.Tooltip(2000, "Unstable internet connection detected!", "Taskmaster", System.Windows.Forms.ToolTipIcon.Warning);
				}
			}

			// NetworkChanged(null,null);
		}

		public void SetupEventHooks()
		{
			NetworkChanged(null, null);
			NetworkAvailable = NetworkInterface.GetIsNetworkAvailable();

			NetworkChange.NetworkAvailabilityChanged += NetworkChanged;
			NetworkChange.NetworkAddressChanged += NetAddrChanged;

			// CheckInet().Wait(); // unnecessary?
		}

		async void NetworkChanged(object sender, EventArgs e)
		{
			var oldNetAvailable = NetworkAvailable;
			NetworkAvailable = NetworkInterface.GetIsNetworkAvailable();

			// do stuff only if this is different from last time
			if (oldNetAvailable != NetworkAvailable)
			{
				Log.Information("<Network> Status changed: " + (NetworkAvailable ? "Connected" : "Disconnected"));

				NetworkStatusChange?.Invoke(this, new NetworkStatus { Available = NetworkAvailable });

				await CheckInet().ConfigureAwait(false);
			}

			UpdateInterfaces();
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		bool disposed; // = false;
		void Dispose(bool disposing)
		{
			if (disposed) return;

			// base.Dispose(disposing);

			if (disposing)
			{
				if (Taskmaster.Trace) Log.Verbose("Disposing network monitor...");
				ReportUptime();

				deviceSampleTimer?.Dispose();
				deviceSampleTimer = null;
				packetStatTimer?.Dispose();
				packetStatTimer = null;
			}

			disposed = true;
		}
	}
}