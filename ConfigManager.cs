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
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Serilog;

namespace Taskmaster
{
	public class ConfigManager : IDisposable
	{
		public ConfigManager(string path)
		{
			datapath = path;
		}

		readonly string datapath = string.Empty;

		readonly object config_lock = new object();

		readonly Dictionary<string, SharpConfig.Configuration> Configs = new Dictionary<string, SharpConfig.Configuration>();
		readonly HashSet<SharpConfig.Configuration> Dirty = new HashSet<SharpConfig.Configuration>();
		readonly Dictionary<SharpConfig.Configuration, string> Paths = new Dictionary<SharpConfig.Configuration, string>();

		/// <exception cref="ArgumentException">When config parameter refers to something that never was loaded.</exception>
		public void Save(SharpConfig.Configuration config)
		{
			if (Paths.TryGetValue(config, out string filename))
				Save(filename, config);
			else
				throw new ArgumentException();
		}

		// TODO: Add error handling.
		public void Save(string configfile, SharpConfig.Configuration config)
		{
			try
			{
				System.IO.Directory.CreateDirectory(datapath);
			}
			catch
			{
				Log.Warning("Failed to create directory: {Path}", datapath);
				return;
			}

			string targetfile = System.IO.Path.Combine(datapath, configfile);

			try
			{
				// backup, copy in case following write fails
				System.IO.File.Copy(targetfile, targetfile + ".bak", overwrite: true);
			}
			catch (System.IO.FileNotFoundException) { } // NOP

			try
			{
				config.SaveToFile(targetfile);
			}
			catch
			{
				Log.Warning("Failed to write: {Target}", targetfile);
			}
			// TODO: Pre-allocate some space for the config file?
		}

		public void Unload(string configfile)
		{
			if (Configs.TryGetValue(configfile, out var retcfg))
			{
				Configs.Remove(configfile);
				Paths.Remove(retcfg);
			}
		}

		public SharpConfig.Configuration Load(string configfile)
		{
			SharpConfig.Configuration retcfg = null;
			if (Configs.TryGetValue(configfile, out retcfg)) return retcfg;

			var path = System.IO.Path.Combine(datapath, configfile);
			// Log.Trace("Opening: "+path);
			if (System.IO.File.Exists(path))
				retcfg = SharpConfig.Configuration.LoadFromFile(path);
			else
			{
				Log.Warning("Not found: {Path}", path);
				retcfg = new SharpConfig.Configuration();
				System.IO.Directory.CreateDirectory(datapath);
			}

			Configs.Add(configfile, retcfg);
			Paths.Add(retcfg, configfile);

			if (Taskmaster.Trace) Log.Verbose("{ConfigFile} added to known configurations files.", configfile);

			return retcfg;
		}

		public void MarkDirtyINI(SharpConfig.Configuration dirtiedcfg)
		{
			lock (config_lock)
			{
				try
				{
					if (Taskmaster.ImmediateSave)
						Save(dirtiedcfg);
					else
						Dirty.Add(dirtiedcfg);
				}
				catch { } // NOP, already in
			}
		}

		public bool NeedSave => Dirty.Count > 0;

		public void Save()
		{
			if (!NeedSave) return;

			lock (config_lock)
			{
				foreach (var config in Dirty)
					Save(config);
				Dirty.Clear();
			}
		}

		bool disposed; // = false;
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		void Dispose(bool disposing)
		{
			if (disposed) return;

			if (disposing)
			{
				if (Taskmaster.Trace) Log.Verbose("Disposing config manager...");

				Save();
			}

			disposed = true;
		}
	}
}
