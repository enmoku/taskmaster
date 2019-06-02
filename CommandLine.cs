//
// CommandLine.cs
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
using System.Diagnostics;
using Serilog;

namespace Taskmaster
{
	using static Taskmaster;

	public static partial class CommandLine
	{
		internal static int AdminCounter { get; set; } = 0;

		public const string AdminArg = "--admin";
		public const string RestartArg = "--restart";

		internal static void ParseArguments(string[] args)
		{
			for (int i = 0; i < args.Length; i++)
			{
				if (!args[i].StartsWith("--"))
				{
					Log.Error("<Start> Unrecognized command-line parameter: " + args[i]);
					continue;
				}

				switch (args[i])
				{
					case RestartArg:
						if (args.Length > i + 1 && !args[i + 1].StartsWith("--"))
							RestartCounter = Convert.ToInt32(args[++i]);
						break;
					case AdminArg:
						// AdminCounter protects from restart loop from attempting to gain admin rights and constantly failing
						if (args.Length > i + 1 && !args[i + 1].StartsWith("--"))
						{
							try
							{
								AdminCounter = Convert.ToInt32(args[++i]);
							}
							catch (Exception ex) when (ex is NullReferenceException || ex is OutOfMemoryException) { throw; }
							catch (Exception ex)
							{
								Logging.Stacktrace(ex, crashsafe: true);
							}
						}

						if (AdminCounter <= 1)
						{
							if (!MKAh.Execution.IsAdministrator)
							{
								Log.Information("Restarting with elevated privileges.");
								try
								{
									var info = System.Diagnostics.Process.GetCurrentProcess().StartInfo;
									info.FileName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
									info.Arguments = $"{AdminArg} {(++AdminCounter).ToString()}";
									info.Verb = "runas"; // elevate privileges
									Log.CloseAndFlush();
									var proc = System.Diagnostics.Process.Start(info);
								}
								catch (Exception ex) when (ex is NullReferenceException || ex is OutOfMemoryException) { throw; }
								catch { } // without finally block might not execute
								finally
								{
									UnifiedExit(restart: true);
									throw new RunstateException("Quick exit to restart", Runstate.Restart);
								}
							}
						}
						else
						{
							MessageBox.ShowModal(Name + " launch error", "Failure to elevate privileges, resuming as normal.", MessageBox.Buttons.OK);
						}

						break;
					default:
						break;
				}
			}
		}

		internal static void NewProcessInfo(out ProcessStartInfo info, bool admin = false)
		{
			var ti = System.Diagnostics.Process.GetCurrentProcess().StartInfo;
			//info.FileName = Process.GetCurrentProcess().ProcessName;
			var cwd = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
			ti.WorkingDirectory = System.IO.Path.GetDirectoryName(cwd);
			ti.FileName = System.IO.Path.GetFileName(cwd);

			var nargs = new System.Collections.Generic.List<string> { RestartArg, (++RestartCounter).ToString() };
			if (admin)
			{
				nargs.Add(AdminArg);
				nargs.Add((++AdminCounter).ToString());
				ti.Verb = "runas"; // elevate privileges
			}

			ti.Arguments = string.Join(" ", nargs);

			info = ti;
		}
	}
}
