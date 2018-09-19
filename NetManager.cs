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
		public bool Available = false;
		public DateTime Start = DateTime.MinValue;
		public TimeSpan Uptime = TimeSpan.MinValue;
	}

	sealed public class InternetStatus : NetworkStatus
	{
		public bool IPChanged = false;
	}

	sealed public class NetManager : IDisposable
	{
		DateTime lastUptimeStart;

		public event EventHandler<InternetStatus> InternetStatusChange;
		public event EventHandler IPChanged;
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
			var cfg = Taskmaster.Config.Load("Net.ini");

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
			PacketWarning.Peak = PacketStatTimerInterval;
			dirtyconf |= dirty;
			if (dirtyconf) Taskmaster.Config.Save(cfg);

			Log.Information("<Network> Traffic sample frequency: {Interval}s", PacketStatTimerInterval);
		}

		public NetManager()
		{
			Since = DateTime.Now;

			lastUptimeStart = DateTime.Now;

			LoadConfig();

			InterfaceInitialization();

			UpdateInterfaces(); // initialize

			// Log.Debug("{IFACELIST} – count: {c}", CurrentInterfaceList, CurrentInterfaceList.Count);

			deviceSampleTimer = new System.Threading.Timer((s) => { RecordDeviceState(InternetAvailable, false); }, null, 15000, DeviceTimerInterval * 60000);

			AnalyzeTrafficBehaviourTick(null); // initialize, not really needed
			packetStatTimer = new System.Threading.Timer(AnalyzeTrafficBehaviourTick, null, 500, PacketStatTimerInterval * 1000);

			Log.Information("<Network> Component loaded.");
		}

		LinearMeter PacketWarning = new LinearMeter(15);

		volatile List<NetDevice> CurrentInterfaceList = new List<NetDevice>(0);

		async void AnalyzeTrafficBehaviourTick(object state) => AnalyzeTrafficBehaviour();

		int analyzetrafficbehaviour_lock = 0;
		async void AnalyzeTrafficBehaviour()
		{
			Debug.Assert(CurrentInterfaceList != null);

			if (!Atomic.Lock(ref analyzetrafficbehaviour_lock)) return;

			await Task.Delay(0);

			try
			{
				PacketWarning.Leak();

				var oldifaces = CurrentInterfaceList;
				UpdateInterfaces(); // force refresh
				var ifaces = CurrentInterfaceList;

				if (ifaces == null) return; // no interfaces, just quit

				NetTraffic outgoing, incoming, oldoutgoing, oldincoming;

				for (int index = 0; index < ifaces.Count; index++)
				{
					outgoing = ifaces[index].Outgoing;
					incoming = ifaces[index].Incoming;
					oldoutgoing = oldifaces[index].Outgoing;
					oldincoming = oldifaces[index].Incoming;

					long totalerrors = outgoing.Errors + incoming.Errors;
					long totaldiscards = outgoing.Errors + incoming.Errors;
					long totalunicast = outgoing.Errors + incoming.Errors;
					long errors = (incoming.Errors - oldincoming.Errors) + (outgoing.Errors - oldoutgoing.Errors);
					long discards = (incoming.Discards - oldincoming.Discards) + (outgoing.Discards - oldoutgoing.Discards);
					long packets = (incoming.Unicast - oldincoming.Unicast) + (outgoing.Unicast - oldoutgoing.Unicast);

					// Console.WriteLine("{0} : Packets(+{1}), Errors(+{2}), Discarded(+{3})", ifaces[index].Name, packets, errors, discards);

					if (errors > 0)
					{
						if (PacketWarning.IsEmptyOrBrimming)
						{
							PacketWarning.Level = 1; // pump once or reset to 1, whichever
							Log.Warning("<Network> {Device} is suffering from traffic errors! (+{Rate} since last sample)", ifaces[index].Name, errors);
						}
						else
							PacketWarning.Pump();
					}

					onSampling?.Invoke(this,
						new NetDeviceTraffic
						{
							Index = index,
							Delta = new NetTraffic { Unicast = packets, Errors = errors, Discards = discards },
							Total = new NetTraffic { Unicast = totalunicast, Errors = totalerrors, Discards = totaldiscards },
						}
					);
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
				var currentUptime = DateTime.Now.TimeSince(lastUptimeStart).TotalMinutes;

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
						try
						{
							// CLEANUP: Console.WriteLine("Debug: Queued internet uptime report");
							await Task.Delay(new TimeSpan(0, 5, 0)); // wait 5 minutes

							ReportCurrentUpstate();
						}
						finally
						{
							Atomic.Unlock(ref upstateTesting);
						}
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

		bool Notified = false;

		int checking_inet; // = 0;
		bool CheckInet(bool address_changed = false)
		{
			// TODO: Figure out how to get Actual start time of internet connectivity.

			if (!Atomic.Lock(ref checking_inet)) return InternetAvailable;

			if (Taskmaster.Trace) Log.Verbose("<Network> Checking internet connectivity...");

			try
			{
				var oldInetAvailable = InternetAvailable;
				if (NetworkAvailable)
				{
					try
					{
						Dns.GetHostEntry(dnstestaddress); // FIXME: There should be some other method than DNS testing
						InternetAvailable = true;
						Notified = false;
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
								return InternetAvailable;
							case System.Net.Sockets.SocketError.SocketError:
							case System.Net.Sockets.SocketError.Interrupted:
							case System.Net.Sockets.SocketError.Fault:
								if (!Notified)
								{
									Log.Warning("<Network> Internet check interrupted. Potential hardware/driver issues.");
									Notified = true;
								}
								break;
							case System.Net.Sockets.SocketError.HostUnreachable:
							case System.Net.Sockets.SocketError.HostNotFound:
							case System.Net.Sockets.SocketError.HostDown:
								if (!Notified)
								{
									Log.Warning("<Network> DNS test failed, test host unreachable. Test host may be down.");
									Notified = true;
								}
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
			}
			finally
			{
				Atomic.Unlock(ref checking_inet);
			}

			InternetStatusChange?.Invoke(this, new InternetStatus { Available = InternetAvailable, Start = lastUptimeStart, Uptime = Uptime });

			return InternetAvailable;
		}

		List<IPAddress> AddressList = new List<IPAddress>(2);
		// List<NetworkInterface> PublicInterfaceList = new List<NetworkInterface>(2);
		IPAddress IPv4Address = IPAddress.None;
		NetworkInterface IPv4Interface;
		IPAddress IPv6Address = IPAddress.IPv6None;
		NetworkInterface IPv6Interface;

		void InterfaceInitialization()
		{
			bool ipv4 = false, ipv6 = false;
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
							ipv4 = true;
							// PublicInterfaceList.Add(n);
							break;
						case System.Net.Sockets.AddressFamily.InterNetworkV6:
							IPv6Address = ip;
							IPv6Interface = n;
							ipv6 = true;
							// PublicInterfaceList.Add(n);
							break;
					}
				}

				if (ipv4 && ipv6) break;
			}
		}

		object interfaces_lock = new object();
		int updateinterface_lock = 0;
		bool needUpdate = true;

		public void UpdateInterfaces()
		{
			if (!Atomic.Lock(ref updateinterface_lock)) return;

			needUpdate = false;

			try
			{
				// TODO: Rate limit
				if (Taskmaster.DebugNet) Log.Verbose("<Network> Enumerating network interfaces...");

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

					if (Taskmaster.DebugNet) Log.Verbose("<Network> Interface: {InterfaceName}", dev.Name);
				}

				lock (interfaces_lock) CurrentInterfaceList = ifacelistt;
			}
			finally
			{
				Atomic.Unlock(ref updateinterface_lock);
			}
		}

		public List<NetDevice> GetInterfaces()
		{
			lock (interfaces_lock)
			{
				if (needUpdate) UpdateInterfaces();
				return CurrentInterfaceList;
			}
		}

		async void NetAddrChanged(object sender, EventArgs e)
		{
			var tmpnow = DateTime.Now;

			bool AvailabilityChanged = InternetAvailable;

			await Task.Delay(0).ConfigureAwait(false); // asyncify

			CheckInet(address_changed: true);
			AvailabilityChanged = AvailabilityChanged != InternetAvailable;

			if (InternetAvailable)
			{
				// CLEANUP: Console.WriteLine("DEBUG: AddrChange: " + oldV4Address + " -> " + IPv4Address);
				// CLEANUP: Console.WriteLine("DEBUG: AddrChange: " + oldV6Address + " -> " + IPv6Address);

				IPAddress oldV6Address = IPv6Address;
				IPAddress oldV4Address = IPv4Address;

				InterfaceInitialization(); // Update IPv4Address & IPv6Address

				bool ipv4changed = false, ipv6changed = false;
				ipv4changed = !oldV4Address.Equals(IPv4Address);

				var sbs = new System.Text.StringBuilder();

				if (AvailabilityChanged)
				{
					Log.Information("<Network> Internet connection restored.");
					sbs.Append("Internet connection restored!").AppendLine();
				}

				if (ipv4changed)
				{
					var outstr4 = new System.Text.StringBuilder();
					outstr4.Append("IPv4 address changed: ").Append(oldV4Address).Append(" -> ").Append(IPv4Address);
					Log.Information(outstr4.ToString());
					sbs.Append(outstr4).AppendLine();
				}

				ipv6changed = !oldV6Address.Equals(IPv6Address);

				if (ipv6changed)
				{
					var outstr6 = new System.Text.StringBuilder();
					outstr6.Append("IPv6 address changed: ").Append(oldV6Address).Append(" -> ").Append(IPv6Address);
					Log.Information(outstr6.ToString());
					sbs.Append(outstr6).AppendLine();
				}

				if (sbs.Length > 0)
				{
					Tray.Tooltip(4000, sbs.ToString(), "Taskmaster",
						System.Windows.Forms.ToolTipIcon.Warning);

					// TODO: Make clicking on the tooltip copy new IP to clipboard?
				}

				if (ipv6changed || ipv6changed)
				{
					IPChanged?.Invoke(this, null);
				}
			}
			else
			{
				if (AvailabilityChanged)
				{
					Log.Warning("<Network> Unstable connectivity detected.");

					Tray.Tooltip(2000, "Unstable internet connection detected!", "Taskmaster",
						System.Windows.Forms.ToolTipIcon.Warning);
				}
			}

			//NetworkChanged(null,null);
		}

		public void SetupEventHooks()
		{
			NetworkChanged(null, null);
			NetworkAvailable = NetworkInterface.GetIsNetworkAvailable();

			NetworkChange.NetworkAvailabilityChanged += NetworkChanged;
			NetworkChange.NetworkAddressChanged += NetAddrChanged;

			// CheckInet().Wait(); // unnecessary?
		}

		bool netAntiFlicker = false;
		int delayednetworkavailable_lock = 0;
		async void DelayedNetworkConnectedUpdate(bool available, bool delayed=true)
		{
			// delay output, but output immediately if internet becomes available...

			if (!Atomic.Lock(ref delayednetworkavailable_lock)) return;

			await Task.Delay(0).ConfigureAwait(false); // asyncify

			try
			{
				if (available)
				{
					CheckInet();
					if (InternetAvailable)
					{
						needUpdate = true;
						return;
					}
				}

				if (netAntiFlicker && delayed)
				{
					const int delay = 15;
					int sleep = Convert.ToInt32(DateTime.Now.TimeTo(lastnetworkchange.AddSeconds(delay)).TotalSeconds * 1000) + 250;

					await Task.Delay(sleep.Constrain(1000, 16000));

					if (DateTime.Now.TimeSince(lastnetworkchange).TotalSeconds < delay)
					{
						if (Taskmaster.DebugNet) Log.Verbose("<Net> Delaying network status testing again: {Delay}<{Wait} is too soon",
							string.Format("{0:N0}s", DateTime.Now.TimeSince(lastnetworkchange).TotalSeconds), delay);
						DelayedNetworkConnectedUpdate(NetworkAvailable);
						return;
					}
					else
						netAntiFlicker = false;
				}

				Log.Information("<Network> Status changed: " + (available ? "Connected" : "Disconnected"));
				netAntiFlicker = true;

				if (NetworkAvailable)
					needUpdate = true;
			}
			catch { throw; } // for finally block
			finally
			{
				Atomic.Unlock(ref delayednetworkavailable_lock);
			}
		}

		DateTime lastnetworkchange = DateTime.MinValue;
		void NetworkChanged(object sender, EventArgs e)
		{
			var oldNetAvailable = NetworkAvailable;
			bool available = NetworkAvailable = NetworkInterface.GetIsNetworkAvailable();

			lastnetworkchange = DateTime.Now;

			// do stuff only if this is different from last time
			if (oldNetAvailable != available)
			{
				if (Taskmaster.DebugNet) Log.Verbose("<Net> Delaying network status testing");

				DelayedNetworkConnectedUpdate(available);

				NetworkStatusChange?.Invoke(this, new NetworkStatus { Available = available });
			}
			else
			{
				if (Taskmaster.DebugNet) Log.Debug("<Net> Network changed but still as available as before.");
			}
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

				Utility.Dispose(ref deviceSampleTimer);
				Utility.Dispose(ref packetStatTimer);
			}

			disposed = true;
		}
	}
}