//
// Network.Device.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2017-2018 M.A.
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

namespace Taskmaster.Network
{
	public class DeviceTraffic
	{
		public int Index { get; set; } = 0;
		public TrafficData Delta { get; set; } = new TrafficData();
		public TrafficData Total { get; set; } = new TrafficData();

		public DeviceTraffic() { }

		public DeviceTraffic(DeviceTraffic old)
		{
			Index = old.Index;
			Delta = new TrafficData(old.Delta);
			Total = new TrafficData(old.Total);
		}
	}

	public sealed class DeviceTrafficEventArgs : EventArgs
	{
		public DeviceTraffic Traffic = new DeviceTraffic();
	}

	public class TrafficData
	{
		/// <summary>
		/// Unicast packets
		/// </summary>
		public long Unicast { get; set; } = 0;

		/// <summary>
		/// Broadcast and Multicast packets
		/// </summary>
		public long NonUnicast { get; set; } = 0;

		public long Discards { get; set; } = 0;

		public long Errors { get; set; } = 0;

		public long Bytes { get; set; } = 0;

		/// <summary>
		/// Unknown packets, only for incoming data.
		/// </summary>
		public long Unknown { get; set; } = 0;

		public void From(IPInterfaceStatistics stats, bool incoming = true)
		{
			if (incoming)
			{
				Unicast = stats.UnicastPacketsReceived;
				NonUnicast = stats.NonUnicastPacketsReceived;
				Discards = stats.IncomingPacketsDiscarded;
				Errors = stats.IncomingPacketsWithErrors;
				Unknown = stats.IncomingUnknownProtocolPackets;
				Bytes = stats.BytesReceived;
			}
			else
			{
				Unicast = stats.UnicastPacketsSent;
				NonUnicast = stats.NonUnicastPacketsSent;
				Discards = stats.OutgoingPacketsDiscarded;
				Errors = stats.OutgoingPacketsWithErrors;
				Bytes = stats.BytesSent;
			}
		}

		public TrafficData() { }

		public TrafficData(TrafficData old)
		{
			Unicast = old.Unicast;
			NonUnicast = old.NonUnicast;
			Discards = old.Discards;
			Errors = old.Errors;
			Bytes = old.Bytes;
			Unknown = old.Unknown;
		}
	}

	sealed public class Device
	{
		public int Index { get; set; }
		public Guid Id { get; set; }

		public string Name { get; set; } = string.Empty;
		public NetworkInterfaceType Type { get; set; } = NetworkInterfaceType.Unknown;
		public OperationalStatus Status { get; set; } = OperationalStatus.NotPresent;
		public long Speed { get; set; } = 0;
		public IPAddress IPv4Address { get; set; } = null;
		public IPAddress IPv6Address { get; set; } = null;

		// Stats
		public TrafficData Outgoing = new TrafficData();
		public TrafficData Incoming = new TrafficData();
	}
}