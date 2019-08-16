//
// IPC.cs
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

using Serilog;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Taskmaster
{
	using static Application;

	internal static class IPC
	{
		internal const string PipeName = @"\\.\MKAh\Taskmaster\Pipe";
		internal const string RestartMessage = "TM...RESTART";
		internal const string TerminationMessage = "TM...TERMINATE";
		internal const string RefreshMessage = "TM...REFRESH";

		static System.IO.Pipes.NamedPipeServerStream pipe = null;

		internal static async void Receive(IAsyncResult result)
		{
			if (pipe is null) return;

			try
			{
				var lp = pipe;
				//pipe = null;
				lp.EndWaitForConnection(result);

				Logging.DebugMsg("<IPC> Activity");

				if (!result.IsCompleted) return;
				if (!pipe.IsConnected) return;
				if (!pipe.IsMessageComplete) return;

				using var sr = new StreamReader(lp);
				var line = await sr.ReadLineAsync().ConfigureAwait(true);
				if (line.StartsWith(RestartMessage))
				{
					Log.Information("<IPC> Restart request received.");
					UnifiedExit(restart: true);
					return;
				}
				else if (line.StartsWith(TerminationMessage))
				{
					Log.Information("<IPC> Termination request received.");
					UnifiedExit(restart: false);
					return;
				}
				else if (line.StartsWith(RefreshMessage))
				{
					Log.Information("<IPC> Refresh.");
					Refresh();
					return;
				}
				else
				{
					Log.Error("<IPC> Unknown message: " + line);
				}

				if (lp.CanRead) lp?.BeginWaitForConnection(Receive, null);
			}
			catch (ObjectDisposedException) { Statistics.DisposedAccesses++; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				if (ex is NullReferenceException) throw;
			}
		}

		internal static System.IO.Pipes.NamedPipeServerStream? Listen()
		{
			try
			{
				var ps = new System.IO.Pipes.PipeSecurity();
				ps.AddAccessRule(new System.IO.Pipes.PipeAccessRule("Users", System.IO.Pipes.PipeAccessRights.Write, System.Security.AccessControl.AccessControlType.Allow));
				using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
				ps.AddAccessRule(new System.IO.Pipes.PipeAccessRule(id.Name, System.IO.Pipes.PipeAccessRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));
				ps.AddAccessRule(new System.IO.Pipes.PipeAccessRule("SYSTEM", System.IO.Pipes.PipeAccessRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));

				pipe = new System.IO.Pipes.NamedPipeServerStream(PipeName, System.IO.Pipes.PipeDirection.In, 1, System.IO.Pipes.PipeTransmissionMode.Message, System.IO.Pipes.PipeOptions.Asynchronous, 16, 8);

				//DisposalChute.Push(pipe);

				pipe.BeginWaitForConnection(Receive, null);

				return pipe;
			}
			catch (IOException) // no pipes available?
			{
				Logging.DebugMsg("Failed to set up pipe server.");
			}

			return null;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <exception cref="IOException">Communication timeout</exception>
		/// <exception cref="UnauthorizedAccessException">Running process has elevated privileges compared to our own.</exception>
		internal static async Task Transmit(string message)
		{
			Logging.DebugMsg("Attempting to communicate with running instance of TM.");

			System.IO.Pipes.NamedPipeClientStream pe = null;

			try
			{
				pe = new System.IO.Pipes.NamedPipeClientStream(".", PipeName, System.IO.Pipes.PipeAccessRights.Write, System.IO.Pipes.PipeOptions.WriteThrough, System.Security.Principal.TokenImpersonationLevel.Impersonation, HandleInheritability.None);
				using var sw = new StreamWriter(pe);
				if (!pe.IsConnected) await pe.ConnectAsync(5_000).ConfigureAwait(true);

				if (pe.IsConnected && pe.CanWrite)
				{
					await sw.WriteLineAsync(message).ConfigureAwait(true);
					await sw.FlushAsync().ConfigureAwait(true);
				}

				await Task.Delay(100).ConfigureAwait(true); // HACK: async pipes don't like things happening too fast.
			}
			catch (UnauthorizedAccessException)
			{
				bool admin = MKAh.Execution.IsAdministrator;
				MessageBox.ShowModal(Name, "Unauthorized access.\n\n" + (admin ? "No recommendations." : "Existing process may be running at higher privilege level.\nPlease retry with admin rights."), MessageBox.Buttons.OK);
				throw;
			}
			catch (IOException)
			{
				System.Windows.Forms.MessageBox.Show("Timeout communicating with existing instance.");
				throw;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex, crashsafe: true);
				if (ex is NullReferenceException) throw;
			}
			finally
			{
				try { pe?.Dispose(); } catch { } // this can throw useless things if the connection never happened
			}
		}

		internal static void Close()
		{
			try { pipe?.Dispose(); } catch { }
			pipe = null;
		}

		// Static Finalizer
		internal static readonly Finalizer finalizer = new Finalizer();

		internal sealed class Finalizer
		{
			~Finalizer() => Close();
		}
	}
}