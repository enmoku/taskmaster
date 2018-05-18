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
	public class ConfigManager
	{
		public ConfigManager(string path)
		{
			datapath = path;
		}

		readonly string datapath = string.Empty;

		readonly Dictionary<string, SharpConfig.Configuration> Configs = new Dictionary<string, SharpConfig.Configuration>();
		readonly Dictionary<SharpConfig.Configuration, bool> ConfigDirty = new Dictionary<SharpConfig.Configuration, bool>();
		readonly Dictionary<SharpConfig.Configuration, string> ConfigPaths = new Dictionary<SharpConfig.Configuration, string>();

		public void SaveConfig(SharpConfig.Configuration config)
		{
			if (ConfigPaths.TryGetValue(config, out string filename))
				SaveConfig(filename, config);
			else
				throw new ArgumentException();
		}

		// TODO: Add error handling.
		public void SaveConfig(string configfile, SharpConfig.Configuration config)
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
				config.SaveToFile(targetfile);
			}
			catch
			{
				Log.Warning("Failed to write: {Target}", targetfile);
			}
			// TODO: Pre-allocate some space for the config file?
		}

		public void UnloadConfig(string configfile)
		{
			if (Configs.TryGetValue(configfile, out var retcfg))
			{
				Configs.Remove(configfile);
				ConfigPaths.Remove(retcfg);
			}
		}

		public SharpConfig.Configuration LoadConfig(string configfile)
		{
			SharpConfig.Configuration retcfg;
			if (Configs.TryGetValue(configfile, out retcfg))
			{
				return retcfg;
			}

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
			ConfigPaths.Add(retcfg, configfile);

			if (Taskmaster.Trace) Log.Verbose("{ConfigFile} added to known configurations files.", configfile);

			return retcfg;
		}

		public void MarkDirtyINI(SharpConfig.Configuration dirtiedcfg)
		{
			try
			{
				if (Taskmaster.ImmediateSave)
					SaveConfig(dirtiedcfg);
				else
					ConfigDirty.Add(dirtiedcfg, true);
			}
			catch { } // NOP, already in
		}


	}
}
