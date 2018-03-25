//
// MemoryLog.cs
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
using System.IO;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;

namespace Taskmaster
{
	sealed public class LogEventArgs : EventArgs
	{
		public readonly string Message;
		public readonly LogEventLevel Level;

		public LogEventArgs(string message, LogEventLevel level)
		{
			Message = message;
			Level = level;
		}
	}

	static class MemoryLog
	{
		public static event EventHandler<LogEventArgs> onNewEvent;

		public static int Max = 50;
		static readonly object LogLock = new object();
		public static System.Collections.Generic.List<LogEventArgs> Logs = new System.Collections.Generic.List<LogEventArgs>(Max);

		public static LoggingLevelSwitch LevelSwitch;

		public static void Clear()
		{
			lock (LogLock)
				Logs.Clear();

		}

		public static void ExcludeDebug() => LevelSwitch.MinimumLevel = LogEventLevel.Information;

		public static void ExcludeTrace() => LevelSwitch.MinimumLevel = LogEventLevel.Debug;

		public static void IncludeTrace() => LevelSwitch.MinimumLevel = LogEventLevel.Verbose;

		public static void Emit(object sender, LogEventArgs e)
		{
			lock (LogLock)
			{
				if (Logs.Count > Max) Logs.RemoveAt(0);
				Logs.Add(e);
			}

			onNewEvent?.Invoke(sender, e);
		}

		public static LogEventArgs[] ToArray()
		{
			LogEventArgs[] logcopy = null;
			lock (LogLock)
				logcopy = Logs.ToArray();


			return logcopy;
		}
	}

	namespace SerilogMemorySink
	{
		sealed class MemorySink : Serilog.Core.ILogEventSink, IDisposable
		{
			readonly TextWriter p_output;
			readonly object sinklock = new object();
			readonly IFormatProvider p_formatProvider;
			readonly ITextFormatter p_textFormatter;
			public LoggingLevelSwitch LevelSwitch;
			const string p_DefaultOutputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";

			public MemorySink(IFormatProvider formatProvider, string outputTemplate = p_DefaultOutputTemplate, LoggingLevelSwitch levelSwitch = null)
			{
				p_formatProvider = formatProvider;
				p_textFormatter = new Serilog.Formatting.Display.MessageTemplateTextFormatter(
					outputTemplate ?? p_DefaultOutputTemplate,
					p_formatProvider
				);
				p_output = new System.IO.StringWriter();
				LevelSwitch = levelSwitch;
			}

			public void Emit(LogEvent e)
			{
				if (e.Level < LevelSwitch.MinimumLevel) return;
				string formattedtext;

				lock (sinklock)
				{
					p_textFormatter.Format(e, p_output);
					formattedtext = p_output.ToString();

					((System.IO.StringWriter)p_output).GetStringBuilder().Clear(); // empty, weird results if not done.
				}

				MemoryLog.Emit(this, new LogEventArgs(formattedtext, e.Level));
			}

			public void Dispose()
			{
				Dispose(true);
				GC.SuppressFinalize(this);
			}

			bool disposed = false;
			void Dispose(bool disposing)
			{
				if (disposed) return;

				if (disposing)
				{
					if (Taskmaster.Trace)
						Log.Verbose("Disposing memory sink...");

					p_output?.Dispose();
				}

				disposed = true;
			}
		}

		public static class MemorySinkExtensions
		{
			public static Serilog.LoggerConfiguration MemorySink(
				this LoggerSinkConfiguration logConf,
				IFormatProvider formatProvider = null,
				string outputTemplate = null,
				LoggingLevelSwitch levelSwitch = null
			)
			{
				return logConf.Sink(new MemorySink(formatProvider, outputTemplate, levelSwitch));
			}
		}
	}
}