//
// Audio.Session.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2018–2019 M.A.
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
using System.Text;
using Serilog;

namespace Taskmaster
{
	class AudioSession : NAudio.CoreAudioApi.Interfaces.IAudioSessionEventsHandler
	{
		readonly NAudio.CoreAudioApi.AudioSessionControl session = null;

		public AudioSession(NAudio.CoreAudioApi.AudioSessionControl audiosession) => session = audiosession;

		public void OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channelIndex)
		{
			// Yeah
		}

		public void OnDisplayNameChanged(string displayName) => Log.Debug("<Audio> Display name changed: " + displayName);

		public void OnGroupingParamChanged(ref Guid groupingId)
		{
			// Ehhh?
		}

		public void OnIconPathChanged(string iconPath) => Log.Debug("<Audio> Icon path changed: " + iconPath);

		public void OnSessionDisconnected(NAudio.CoreAudioApi.Interfaces.AudioSessionDisconnectReason disconnectReason)
		{
			uint pid = session.GetProcessID;
			//string process = session.GetSessionIdentifier;
			//string instance = session.GetSessionInstanceIdentifier;
			string name = session.DisplayName;

			// Don't care really
			var sbs = new StringBuilder();
			sbs.Append("<Audio> ").Append(name).Append(" (#").Append(pid).Append(") Disconnected: ").Append(disconnectReason.ToString());
			Log.Debug(sbs.ToString());

			switch (disconnectReason)
			{
				case NAudio.CoreAudioApi.Interfaces.AudioSessionDisconnectReason.DisconnectReasonDeviceRemoval:
					break;
				case NAudio.CoreAudioApi.Interfaces.AudioSessionDisconnectReason.DisconnectReasonExclusiveModeOverride:
					break;
				case NAudio.CoreAudioApi.Interfaces.AudioSessionDisconnectReason.DisconnectReasonFormatChanged:
					break;
				case NAudio.CoreAudioApi.Interfaces.AudioSessionDisconnectReason.DisconnectReasonServerShutdown:
					break;
				case NAudio.CoreAudioApi.Interfaces.AudioSessionDisconnectReason.DisconnectReasonSessionDisconnected:
					break;
				case NAudio.CoreAudioApi.Interfaces.AudioSessionDisconnectReason.DisconnectReasonSessionLogoff:
					break;
			}

			session.UnRegisterEventClient(this); // unnecessary?
		}

		public void OnStateChanged(NAudio.CoreAudioApi.Interfaces.AudioSessionState state)
		{
			uint pid = session.GetProcessID;
			//string instance = session.GetSessionInstanceIdentifier;
			string name = session.DisplayName;

			var sbs = new StringBuilder();
			sbs.Append("<Audio> ").Append(name).Append(" (#").Append(pid).Append(") State changed: ").Append(state.ToString());
			Log.Debug(sbs.ToString());

			switch (state)
			{
				case NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive:

					break;
				case NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateExpired:
				case NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateInactive: // e.g. pause
					//session.UnRegisterEventClient(this); // unnecessary?
					break;
			}
		}

		public void OnVolumeChanged(float volume, bool isMuted)
		{
			var sbs = new StringBuilder();
			sbs.Append("<Audio> Volume: ").AppendFormat("{0:N2}", volume).Append(", Muted: ").Append(isMuted ? "True" : "False");
			Log.Debug(sbs.ToString());
		}
	}
}
