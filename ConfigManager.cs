﻿//
// ConfigManager.cs
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

namespace Taskmaster
{
	public class ConfigManager : IDisposable
	{
		readonly string datapath = string.Empty;

		public ConfigManager(string path) => datapath = path;

		readonly object config_lock = new object();
		readonly HashSet<ConfigWrapper> Loaded = new HashSet<ConfigWrapper>();

		public ConfigWrapper Load(string filename)
		{
			try
			{
				lock (config_lock)
				{
					foreach (var oldcfg in Loaded)
					{
						if (oldcfg.File.Equals(filename))
							return oldcfg;
					}
					SharpConfig.Configuration scfg = null;

					var fullpath = System.IO.Path.Combine(datapath, filename);
					if (System.IO.File.Exists(fullpath))
						scfg = SharpConfig.Configuration.LoadFromFile(fullpath);
					else
					{
						Log.Warning("Not found: " + fullpath);
						scfg = new SharpConfig.Configuration();
						System.IO.Directory.CreateDirectory(datapath);
					}

					var config = new ConfigWrapper(scfg, filename);
					Loaded.Add(config);

					config.onUnload += (cfg, ea) => Loaded.Remove((ConfigWrapper)cfg);
					config.onSave += (cfg, ea) => Save((ConfigWrapper)cfg);

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

		void Save(ConfigWrapper cfg)
		{
			try
			{
				lock (config_lock)
				{
					var fullpath = System.IO.Path.Combine(datapath, cfg.File);
					cfg.Config.SaveToFile(fullpath);
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		public void Unload(ConfigWrapper config)
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
