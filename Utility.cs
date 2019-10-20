//
// Utility.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2016-2019 M.A.
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
using System.Runtime.CompilerServices;
using System.Text;

namespace Taskmaster
{
	public static class Logging
	{
		static void AppendStacktace(Exception ex, ref StringBuilder output)
		{
			output.AppendLine()
				.Append("Exception:    ").AppendLine(ex.GetType().Name)
				.Append("Message:      ").AppendLine(ex.Message).AppendLine();

			var projectdir = Properties.Resources.ProjectDirectory.Trim();

			if (string.IsNullOrEmpty(ex.StackTrace))
				output.AppendLine("!!! Stacktrace missing !!!").AppendLine();
			else
			{
				var trace = ex.StackTrace.Replace(projectdir, HumanReadable.Generic.Ellipsis + System.IO.Path.DirectorySeparatorChar);
				output.AppendLine("----- Stacktrace -----")
					.AppendLine(trace);
			}
		}

		[Conditional("DEBUG")]
		public static void DebugMsg(string message)
			=> System.Diagnostics.Debug.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + message);

		public static void Stacktrace(Exception ex, bool crashsafe = false, [CallerMemberName] string method = "", [CallerLineNumber] int lineNo = -1, [CallerFilePath] string file = "")
		{
			var projectdir = Properties.Resources.ProjectDirectory.Trim();
			if (!crashsafe)
			{
				string trace = ex.StackTrace.Replace(projectdir, HumanReadable.Generic.Ellipsis + System.IO.Path.DirectorySeparatorChar);
				var msg = $"Exception [{method}:{lineNo}]: {ex.GetType().Name} : {ex.Message}\n{trace}";

				DebugMsg(msg);

				Serilog.Log.Fatal(msg);
				if (ex is InitFailure iex && (iex.InnerExceptions?.Length ?? 0) > 1)
				{
					for (int i = 1; i < iex.InnerExceptions.Length; i++)
					{
						trace = iex.InnerExceptions[i].StackTrace.Replace(projectdir, HumanReadable.Generic.Ellipsis + System.IO.Path.DirectorySeparatorChar);
						Serilog.Log.Fatal($"Exception: {iex.InnerExceptions[i].GetType().Name} : {iex.InnerExceptions[i].Message}\n{trace}");
					}
				}
			}
			else
			{
				if (Application.NoLogging) return;

				try
				{
					if (!System.IO.Directory.Exists(Application.LogPath)) System.IO.Directory.CreateDirectory(Application.LogPath);

					string logfilename = Application.UniqueCrashLogs ? $"crash-{DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")}.log" : "crash.log";
					var logfile = System.IO.Path.Combine(Application.LogPath, logfilename);

					var now = DateTime.Now;

					file = file.Replace(projectdir, HumanReadable.Generic.Ellipsis + System.IO.Path.DirectorySeparatorChar);

					var sbs = new StringBuilder(1024);
					sbs.Append("Datetime:     ").Append(now.ToLongDateString()).Append(' ').AppendLine(now.ToLongTimeString())
						.Append("Caught at: ").Append(method).Append(':').Append(lineNo).Append(" [").Append(file).AppendLine("]")
						.Append("Site: ").AppendLine(ex.TargetSite?.ToString() ?? string.Empty)
						.AppendLine()
						.Append("Command line: ").AppendLine(Environment.CommandLine)
						.AppendLine()
						.Append("Message: ").AppendLine(ex.Message);

#if DEBUG
					var exceptionsbs = new StringBuilder(512);
#endif

					AppendStacktace(ex, ref sbs);
#if DEBUG
					AppendStacktace(ex, ref exceptionsbs);
#endif
					if (ex.InnerException != null)
					{
						sbs.AppendLine().AppendLine("--- Inner Exception ---");
						AppendStacktace(ex.InnerException, ref sbs);
#if DEBUG
						AppendStacktace(ex.InnerException, ref exceptionsbs);
#endif
					}

#if DEBUG
					if (ex is InitFailure iex && (iex.InnerExceptions?.Length ?? 0) > 1)
					{
						for (int i = 1; i < iex.InnerExceptions.Length; i++)
						{
							if (iex.InnerExceptions[i] != null) // this is weird
								AppendStacktace(iex.InnerExceptions[i], ref exceptionsbs);
						}
					}
#endif

					System.IO.File.WriteAllText(logfile, sbs.ToString(), Encoding.Unicode);
					DebugMsg("Crash log written to " + logfile);
#if DEBUG
					Debug.WriteLine(exceptionsbs.ToString());
#endif

				}
				catch (OutOfMemoryException) { throw; }
				catch
				{
					throw; // nothing to be done, we're already crashing and burning by this point
				}
			}
		}
	}
}