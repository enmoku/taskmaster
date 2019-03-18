//
// Config.Manager.cs
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
using System.Collections.Generic;
using Serilog;

namespace Taskmaster.Config
{
	public class Manager : IDisposable
	{
		readonly string datapath = string.Empty;

		public Manager(string path) => datapath = path;

		readonly object config_lock = new object();
		readonly HashSet<File> Loaded = new HashSet<File>();

		public File Load(string filename)
		{
			try
			{
				lock (config_lock)
				{
					// TODO: Hash filenames
					foreach (var oldcfg in Loaded)
					{
						if (oldcfg.Filename.Equals(filename, StringComparison.InvariantCultureIgnoreCase))
							return oldcfg;
					}

					MKAh.Ini.Config mcfg = null;

					var fullpath = System.IO.Path.Combine(datapath, filename);
					if (System.IO.File.Exists(fullpath))
					{
						mcfg = MKAh.Ini.Config.FromFile(fullpath, System.Text.Encoding.UTF8);
					}
					else
					{
						Log.Warning("Not found: " + fullpath);
						mcfg = new MKAh.Ini.Config();
						System.IO.Directory.CreateDirectory(datapath);
					}

					var config = new File(mcfg, filename);
					Loaded.Add(config);

					config.onUnload += (cfg, ea) => Loaded.Remove((File)cfg);
					config.onSave += (cfg, ea) => Save((File)cfg);

					return config;
				}
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				throw;
			}
		}

		void Save(File cfg)
		{
			try
			{
				lock (config_lock)
				{
					var fullpath = System.IO.Path.Combine(datapath, cfg.Filename);
					cfg.Config.SaveToFile(fullpath, new System.Text.UTF8Encoding(false));
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		public void Unload(File config)
		{
			lock (config_lock)
			{
				Loaded.Remove(config);
				config.Dispose();
			}
		}

		public void Flush()
		{
			lock (config_lock)
			{
				foreach (var config in Loaded)
					config.Save();

				Loaded.Clear();
			}
		}

		bool disposed; // = false;
		public void Dispose() => Dispose(true);

		void Dispose(bool disposing)
		{
			if (disposed) return;

			if (disposing)
			{
				if (Taskmaster.Trace) Log.Verbose("Disposing config manager...");

				Flush();
			}

			disposed = true;
		}
	}
}
