//
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

		public ConfigManager(string path)
		{
			datapath = path;
		}

		readonly object config_lock = new object();
		readonly HashSet<ConfigWrapper> Dirty = new HashSet<ConfigWrapper>();

		public ConfigWrapper Load(string filename)
		{
			lock (config_lock)
			{
				foreach (var oldcfg in Dirty)
				{
					if (oldcfg.File.Equals(filename))
						return oldcfg;
				}

				var config = new ConfigWrapper(null, filename, datapath);
				Dirty.Add(config);
				return config;
			}
		}

		public void Unload(ConfigWrapper config)
		{
			Dirty.Remove(config);
			config.Dispose();
		}

		public void Flush()
		{
			lock (config_lock)
			{
				foreach (var config in Dirty)
					config.Save();

				Dirty.Clear();
			}
		}

		bool disposed; // = false;
		public void Dispose()
		{
			Dispose(true);
		}

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

	public class ConfigWrapper : IDisposable
	{
		public SharpConfig.Configuration Config { get; private set; } = null;
		public string File { get; private set; } = null;
		string Path { get; set; } = null;

		public bool Dirty { get; private set; } = false;

		public ConfigWrapper(SharpConfig.Configuration config, string filename, string datapath)
		{
			Config = config;
			File = filename;
			Path = datapath;

			Load();
		}

		public void MarkDirty()
		{
			Dirty = true;
			if (Taskmaster.ImmediateSave) Save();
		}

		void Load()
		{
			var fullpath = System.IO.Path.Combine(Path, File);

			// Log.Trace("Opening: "+path);
			if (System.IO.File.Exists(fullpath))
				Config = SharpConfig.Configuration.LoadFromFile(fullpath);
			else
			{
				Log.Warning("Not found: {Path}", fullpath);
				Config = new SharpConfig.Configuration();
				System.IO.Directory.CreateDirectory(Path);
			}

			if (Taskmaster.Trace) Log.Verbose("{ConfigFile} added to known configurations files.", File);
		}

		public void Save()
		{
			if (!Dirty) return;

			try
			{
				System.IO.Directory.CreateDirectory(Path);
			}
			catch
			{
				Log.Warning("Failed to create directory: {Path}", Path);
				return;
			}

			string targetfile = System.IO.Path.Combine(Path, File);

			try
			{
				// backup, copy in case following write fails
				System.IO.File.Copy(targetfile, targetfile + ".bak", overwrite: true);
			}
			catch (System.IO.FileNotFoundException) { } // NOP

			try
			{
				Config.SaveToFile(targetfile);
			}
			catch
			{
				Log.Warning("Failed to write: {Target}", targetfile);
			}
			// TODO: Pre-allocate some space for the config file?

			Dirty = false;
		}

		public void Unload()
		{
			if (Dirty) Save();
			Config = null;
		}

		#region IDisposable Support
		private bool disposed = false;

		protected virtual void Dispose(bool disposing)
		{
			if (!disposed)
			{
				if (disposing)
				{
					if (Dirty) Save();
					Unload();
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
}
