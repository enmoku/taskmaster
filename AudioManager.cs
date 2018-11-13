//
// AudioManager.cs
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
using System.Diagnostics;

namespace Taskmaster
{
	/// <summary>
	/// Must be created on persistent thread, such as the main thread.
	/// </summary>
	public class AudioManager : IDisposable
	{
		readonly System.Threading.Thread Context = null;

		NAudio.CoreAudioApi.MMDevice mmdev_media = null;

		//public event EventHandler<ProcessEx> OnNewSession;

		const string configfile = "Audio.ini";

		/// <summary>
		/// Not thread safe
		/// </summary>
		/// <exception cref="InitFailure">If audio device can not be found.</exception>
		public AudioManager()
		{
			Context = System.Threading.Thread.CurrentThread;

			var mm_enum = new NAudio.CoreAudioApi.MMDeviceEnumerator();
			mmdev_media = mm_enum?.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
			if (mmdev_media == null)
			{
				throw new InitFailure("Failed to capture default audio output device.");
			}

			Serilog.Log.Information("<Audio> Defalt device: {Device}", mmdev_media.DeviceFriendlyName);

			mmdev_media.AudioSessionManager.OnSessionCreated += OnSessionCreated;

			/*
			var cfg = Taskmaster.Config.Load(configfile);

			foreach (var section in cfg)
			{

			}
			*/
		}

		private void OnSessionCreated(object sender, NAudio.CoreAudioApi.Interfaces.IAudioSessionControl asession)
		{
			Debug.Assert(System.Threading.Thread.CurrentThread != Context, "Must be called in same thread.");

			try
			{
				var session = new NAudio.CoreAudioApi.AudioSessionControl(asession);

				int pid = (int)session.GetProcessID;
				string name = session.DisplayName;

				float volume = session.SimpleAudioVolume.Volume;

				var info = ProcessManagerUtility.GetInfo(pid, getPath:true);
				if (info != null)
				{
					//OnNewSession?.Invoke(this, info);
					var prc = Taskmaster.Components.processmanager.getController(info.Name);
					if (prc == null && !string.IsNullOrEmpty(info.Path)) prc = Taskmaster.Components.processmanager.getWatchedPath(info);
					if (prc != null)
					{
						bool volAdjusted = false;
						float oldvolume = session.SimpleAudioVolume.Volume;
						switch (prc.VolumeStrategy)
						{
							default:
							case AudioVolumeStrategy.Ignore:
								break;
							case AudioVolumeStrategy.Force:
								session.SimpleAudioVolume.Volume = prc.Volume;
								volAdjusted = true;
								break;
							case AudioVolumeStrategy.Decrease:
								if (oldvolume > prc.Volume)
								{
									session.SimpleAudioVolume.Volume = prc.Volume;
									volAdjusted = true;
								}
								break;
							case AudioVolumeStrategy.Increase:
								if (oldvolume < prc.Volume)
								{
									session.SimpleAudioVolume.Volume = prc.Volume;
									volAdjusted = true;
								}
								break;
							case AudioVolumeStrategy.DecreaseFromFull:
								if (oldvolume > prc.Volume && oldvolume >= 0.99f)
								{
									session.SimpleAudioVolume.Volume = prc.Volume;
									volAdjusted = true;
								}
								break;
							case AudioVolumeStrategy.IncreaseFromMute:
								if (oldvolume < prc.Volume && oldvolume <= 0.01f)
								{
									session.SimpleAudioVolume.Volume = prc.Volume;
									volAdjusted = true;
								}
								break;
						}

						if (volAdjusted)
						{
							Serilog.Log.Information("<Audio> " + info.Name + " (#" + info.Id + ") " +
								"volume changed from " + $"{(oldvolume * 100):N1}%" + " to " + $"{(prc.Volume * 100):N1}%");
						}
						else
						{
							if (Taskmaster.ShowInaction && Taskmaster.DebugAudio)
								Serilog.Log.Debug("<Audio> " + info.Name + " (#" + pid + ") Volume: " + $"{(volume * 100):N1}%" +
									" – Already correct (Plan: " + prc.VolumeStrategy.ToString() + ")");
						}
					}
					else
					{
						if (Taskmaster.ShowInaction && Taskmaster.DebugAudio)
							Serilog.Log.Debug("<Audio> " + info.Name + " (#" + pid + ") Volume: " + $"{(volume * 100):N1}%" +
								" – not watched: " + info.Path);
					}
				}
				else
				{
					Serilog.Log.Debug("<Audio> Failed to get info for session (#{Pid})", pid);
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		#region IDisposable Support
		private bool disposed = false;

		protected virtual void Dispose(bool disposing)
		{
			if (!disposed)
			{
				if (disposing)
				{
					mmdev_media?.Dispose();
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

	class AudioSession : NAudio.CoreAudioApi.Interfaces.IAudioSessionEventsHandler
	{
		readonly NAudio.CoreAudioApi.AudioSessionControl session = null;

		public AudioSession(NAudio.CoreAudioApi.AudioSessionControl audiosession)
		{
			session = audiosession;
		}

		public void OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channelIndex)
		{
			// Yeah
		}

		public void OnDisplayNameChanged(string displayName)
		{
			Serilog.Log.Debug("<Audio> Display name changed: {Name}", displayName);
		}

		public void OnGroupingParamChanged(ref Guid groupingId)
		{
			// Ehhh?
		}

		public void OnIconPathChanged(string iconPath)
		{
			Serilog.Log.Debug("<Audio> Icon path changed: {Name}", iconPath);
		}

		public void OnSessionDisconnected(NAudio.CoreAudioApi.Interfaces.AudioSessionDisconnectReason disconnectReason)
		{
			uint pid = session.GetProcessID;
			//string process = session.GetSessionIdentifier;
			string instance = session.GetSessionInstanceIdentifier;
			string name = session.DisplayName;

			// Don't care really
			Serilog.Log.Debug("<Audio> {Name} (#{ID}) Disconnected: {Disconnected}",
				name, pid, disconnectReason.ToString());

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
			string instance = session.GetSessionInstanceIdentifier;
			string name = session.DisplayName;

			Serilog.Log.Debug("<Audio> {Name} (#{ID}) State changed: {State}",
				name, pid, state.ToString());

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
			Serilog.Log.Debug("<Audio> Volume: {Vol}, Muted: {Muted}", volume, isMuted);
		}
	}

	public enum AudioVolumeStrategy
	{
		Ignore = 0,
		Decrease = 1,
		Increase = 2,
		Force = 3,
		DecreaseFromFull=4,
		IncreaseFromMute=5
	}
}
