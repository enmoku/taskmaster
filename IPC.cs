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
using System.Diagnostics;
using System.IO;

namespace Taskmaster
{
	public static partial class Taskmaster
	{
		const string PipeName = @"\\.\MKAh\Taskmaster\Pipe";
		const string PipeRestart = "TM...RESTART";
		const string PipeTerm = "TM...TERMINATE";
		const string PipeRefresh = "TM...REFRESH";
		static System.IO.Pipes.NamedPipeServerStream pipe = null;

		static void PipeCleaner(IAsyncResult result)
		{
			Debug.WriteLine("<IPC> Activity");

			if (pipe is null) return;

			try
			{
				var lp = pipe;
				//pipe = null;
				lp.EndWaitForConnection(result);

				if (!result.IsCompleted) return;
				if (!pipe.IsConnected) return;
				if (!pipe.IsMessageComplete) return;

				using (var sr = new StreamReader(lp))
				{
					var line = sr.ReadLine();
					if (line.StartsWith(PipeRestart))
					{
						Log.Warning("<IPC> Restart request received.");
						UnifiedExit(restart: true);
						return;
					}
					else if (line.StartsWith(PipeTerm))
					{
						Log.Warning("<IPC> Termination request received.");
						UnifiedExit(restart: false);
						return;
					}
					else if (line.StartsWith(PipeRefresh))
					{
						Log.Information("<IPC> Refresh.");
						Refresh();
						return;
					}
					else
					{
						Log.Error("<IPC> Unknown message: " + line);
					}
				}

				if (lp.CanRead) lp?.BeginWaitForConnection(PipeCleaner, null);
			}
			catch (ObjectDisposedException) { Statistics.DisposedAccesses++; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				if (ex is NullReferenceException) throw;
			}
		}

		static System.IO.Pipes.NamedPipeServerStream PipeDream()
		{
			try
			{
				var ps = new System.IO.Pipes.PipeSecurity();
				ps.AddAccessRule(new System.IO.Pipes.PipeAccessRule("Users", System.IO.Pipes.PipeAccessRights.Write, System.Security.AccessControl.AccessControlType.Allow));
				ps.AddAccessRule(new System.IO.Pipes.PipeAccessRule(System.Security.Principal.WindowsIdentity.GetCurrent().Name, System.IO.Pipes.PipeAccessRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));
				ps.AddAccessRule(new System.IO.Pipes.PipeAccessRule("SYSTEM", System.IO.Pipes.PipeAccessRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));

				pipe = new System.IO.Pipes.NamedPipeServerStream(PipeName, System.IO.Pipes.PipeDirection.In, 1, System.IO.Pipes.PipeTransmissionMode.Message, System.IO.Pipes.PipeOptions.Asynchronous, 16, 8);

				//DisposalChute.Push(pipe);

				pipe.BeginWaitForConnection(PipeCleaner, null);

				return pipe;
			}
			catch (IOException) // no pipes available?
			{
				Debug.WriteLine("Failed to set up pipe server.");
			}

			return null;
		}

		static void PipeExplorer(string message)
		{
			Debug.WriteLine("Attempting to communicate with running instance of TM.");

			try
			{
				using (var pe = new System.IO.Pipes.NamedPipeClientStream(".", PipeName, System.IO.Pipes.PipeAccessRights.Write, System.IO.Pipes.PipeOptions.WriteThrough, System.Security.Principal.TokenImpersonationLevel.Impersonation, HandleInheritability.None))
				using (var sw = new StreamWriter(pe))
				{
					if (!pe.IsConnected) pe.Connect(5_000);

					if (pe.IsConnected && pe.CanWrite)
					{
						sw.WriteLine(message);
						sw.Flush();
					}

					System.Threading.Thread.Sleep(100); // HACK: async pipes don't like things happening too fast.
				}
			}
			catch (IOException)
			{
				System.Windows.Forms.MessageBox.Show("Timeout communicating with existing Taskmaster instance.");
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex, crashsafe: true);
				if (ex is NullReferenceException) throw;
			}
		}
	}
}
