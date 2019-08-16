//
// Process.Analyzer.cs
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

using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Taskmaster.Process
{
	using static Application;

	public class Analyzer
	{
		public Analyzer()
		{
			var modulepath = Path.Combine(DataPath, ModuleFile);
			if (!File.Exists(modulepath) || new FileInfo(modulepath).LastWriteTimeUtc < BuildDate())
				File.WriteAllText(modulepath, Properties.Resources.KnownModules, Encoding.UTF8);
			Load(ModuleFile);

			var usermodulepath = Path.Combine(DataPath, UserModuleFile);
			if (File.Exists(usermodulepath))
				Load(UserModuleFile);
		}

		readonly ConcurrentDictionary<byte[], int> cache = new ConcurrentDictionary<byte[], int>(new StructuralEqualityComparer<byte[]>());

		void RemoveCached(byte[] hash) => cache.TryRemove(hash, out _);

		public async Task Analyze(ProcessEx info)
		{
			if (string.IsNullOrEmpty(info.Path)) return;

			if (info.Restricted)
			{
				if (Manager.DebugProcesses) Logging.DebugMsg($"<Process> {info} RESTRICTED - cancelling Analyze");
				return;
			}

			using var crypt = new System.Security.Cryptography.SHA512Cng();
			byte[] hash = crypt.ComputeHash(Encoding.UTF8.GetBytes(info.Path.ToLowerInvariant()));

			// TODO: Prevent bloating somehow.
			if (!cache.TryAdd(hash, 0)) return; // already there

			Log.Debug($"<Analysis> {info} scheduled");

			var AllLinkedModules = new ConcurrentDictionary<string, ModuleInfo>();
			var ImportantModules = new ConcurrentDictionary<string, ModuleInfo>();

			long privMem = 0;
			long threadCount = 0;
			long workingSet = 0;
			long virtualMem = 0;

			bool x64 = true;

			string modFile;
			FileVersionInfo? version = default;
			long modMemory;

			try
			{
				await Task.Delay(RecordAnalysis.Value).ConfigureAwait(false);

				info.Process.Refresh();
				if (info.Process.HasExited)
				{
					info.State = HandlingState.Exited;
					Log.Debug($"<Analysis> {info} cancelled; already gone");
					RemoveCached(hash);
					return;
				}

				if (Trace) Logging.DebugMsg("Analyzing:" + info.ToString());

				var module = info.Process.MainModule;
				modFile = module.FileName;
				version = module.FileVersionInfo;
				modMemory = module.ModuleMemorySize;

				var modules = info.Process.Modules;
				// Process.Modules unfortunately returned only ntdll.dll, wow64.dll, wow64win.dll, and wow64cpu.dll for a game and seems unusuable for what it was desired
				foreach (ProcessModule mod in modules)
				{
					if (mod.ModuleName.StartsWith("wow64.dll", StringComparison.InvariantCultureIgnoreCase))
					{
						x64 = false;
						break;
					}
				}

				IntPtr[] modulePtrs = Array.Empty<IntPtr>();

				// Determine number of modules
				if (!NativeMethods.EnumProcessModulesEx(info.Process.Handle, modulePtrs, 0, out int bytesNeeded, (uint)NativeMethods.ModuleFilter.ListModulesAll))
					return;

				int totalModules = bytesNeeded / IntPtr.Size;
				modulePtrs = new IntPtr[totalModules];
				var handle = info.Process.Handle;
				if (NativeMethods.EnumProcessModulesEx(handle, modulePtrs, bytesNeeded, out bytesNeeded, (uint)NativeMethods.ModuleFilter.ListModulesAll))
				{
					for (int index = 0; index < totalModules; index++)
					{
						var modulePath = new StringBuilder(1024);
						NativeMethods.GetModuleFileNameEx(handle, modulePtrs[index], modulePath, (uint)(modulePath.Capacity));

						string moduleName = Path.GetFileName(modulePath.ToString());
						//ModuleInformation moduleInformation = new ModuleInformation();
						//GetModuleInformation(handle, modulePtrs[index], out moduleInformation, (uint)(IntPtr.Size * (modulePtrs.Length)));

						//linkedModules.Add(moduleName.ToLowerInvariant());
						//Logging.DebugMsg(" - " + moduleName);

						var file = moduleName.Trim();
						var identity = IdentifyModule(file);

						if (AllLinkedModules.TryGetValue(identity, out ModuleInfo mi))
						{ /* NOP */ }
						else if (KnownModules.TryGetValue(identity, out var lmi))
							mi = lmi.Clone();
						else
							mi = new ModuleInfo() { Identity = "Unknown" };

						mi.Detected.Add(file);

						AllLinkedModules.TryAdd(identity, mi);

						if (mi.Listed) ImportantModules.TryAdd(identity, mi);
					}
				}

				try
				{
					privMem = info.Process.PrivateMemorySize64 / 1_048_576;
					workingSet = info.Process.PeakWorkingSet64 / 1_048_576;
					virtualMem = info.Process.PeakVirtualMemorySize64 / 1_048_576;
					threadCount = info.Process.Threads.Count;
				}
				catch { }
			}
			catch (InvalidOperationException)
			{
				// already exited
				RemoveCached(hash);
				Log.Debug($"{info.ToFullString()} exited before analysis could begin.");
			}
			catch (Win32Exception)
			{
				info.Restricted = true;
				// access denied
				RemoveCached(hash);
				Log.Debug($"{info.ToFullString()} was denied access for analysis.");
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				RemoveCached(hash);
				Logging.Stacktrace(ex);
			}

			try
			{
				// LOG analysis
				var sbs = new StringBuilder("<Analysis> ", 512).Append(info.Name)
					.Append(" #").Append(info.Id).Append(" facts: ");

				var components = new List<string>(16);

				if (x64) components.Add("64-bit");
				else components.Add("32-bit");

				if (privMem < 8) components.Add("Memory(Low)"); // less than 8MB private memory
				else if (privMem > 2600) components.Add("Memory(Extreme)");// more than 2600 MB
				else if (privMem > 1200) components.Add("Memory(High)"); // more than 1200 MB
				else if (privMem > 400) components.Add("Memory(Moderate)"); // more than 400 MB

				long latestDX = 0, latestDXX = 0;
				foreach (var modname in ImportantModules.Keys)
				{
					components.Add(modname);

					// recommendations
					if (modname.StartsWith("DirectX", StringComparison.InvariantCultureIgnoreCase))
					{
						if (ImportantModules.TryGetValue(modname, out var mod))
						{
							if (modname.IndexOf("extension", StringComparison.InvariantCultureIgnoreCase) >= 0)
								latestDXX = Math.Max(mod.Value, latestDXX);
							else
								latestDX = Math.Max(mod.Value, latestDX);
						}
					}
				}

				if (components.Count > 0) sbs.Append(string.Join(", ", components));
				else sbs.Append("None");

				Log.Information(sbs.ToString());

				// DUMP RECOMMENDATIONS

				var recommendations = new List<string>(8);

				if (latestDXX > latestDX)
					recommendations.Add($"force DX {(latestDX / 10).ToString()} rendering");

				if (ImportantModules.ContainsKey("PhysX"))
					recommendations.Add("disable PhysX");

				if (recommendations.Count > 0)
				{
					sbs.Clear();
					sbs.Append("<Analysis> ").Append(info.Name).Append(" #").Append(info.Id).Append(" recommendations: ");
					sbs.Append(string.Join(", ", recommendations));
					Log.Information(sbs.ToString());
				}

				// RECORD analysis

				var file = $"{DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")}-{info.Name}.analysis.yml";
				var path = Path.Combine(DataPath, "Analysis");
				var endpath = Path.Combine(path, file);
				Directory.CreateDirectory(path);

				const string ymlIndent = "  ";

				var contents = new StringBuilder(1024 * 4)
					.AppendLine("Analysis:")
					.Append(ymlIndent).Append("Process: ").AppendLine(info.Name)
					.Append(ymlIndent).Append("Version: ").AppendLine(version.FileVersion)
					.Append(ymlIndent).Append("Product: ").AppendLine(version.ProductName)
					.Append(ymlIndent).Append("Company: ").AppendLine(version.CompanyName)
					.Append(ymlIndent).Append("64-bit : ").AppendLine(x64 ? "Yes" : "No")
					.Append(ymlIndent).Append("Path   : ").AppendLine(info.Path)
					.Append(ymlIndent).Append("Threads: ").AppendLine(threadCount.ToString())
					.Append(ymlIndent).AppendLine("Memory : ")
					.Append(ymlIndent).Append(ymlIndent).Append("Private : ").AppendLine(privMem.ToString())
					.Append(ymlIndent).Append(ymlIndent).Append("Working : ").AppendLine(workingSet.ToString())
					.Append(ymlIndent).Append(ymlIndent).Append("Virtual : ").AppendLine(virtualMem.ToString())
					.Append(ymlIndent).AppendLine("Modules: ");

				foreach (var mod in AllLinkedModules.Values)
				{
					contents.Append(ymlIndent).Append(ymlIndent).Append(mod.Identity).AppendLine(":");
					if (mod.Type != ModuleType.Unknown)
						contents.Append(ymlIndent).Append(ymlIndent).Append(ymlIndent)
							.Append("Type: ").AppendLine(mod.Type.ToString());
					contents.Append(ymlIndent).Append(ymlIndent).Append(ymlIndent)
						.Append("Files: [ ").Append(string.Join(", ", mod.Detected)).AppendLine(" ]");
				}

				File.WriteAllText(endpath, contents.ToString());
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		// TODO: build external library of components that's loaded as a dictionary of sorts
		public string IdentifyModule(string moduleName)
		{
			foreach (var modfile in KnownFiles)
			{
				if (moduleName.StartsWith(modfile.Key, StringComparison.InvariantCultureIgnoreCase))
					return modfile.Value.Identity;
			}

			return "Unknown";
		}

		static readonly string[] yesvalues = { "yes", "true" };

		const string ModuleFile = "Modules.Known.ini";
		const string UserModuleFile = "Modules.User.ini";

		/// <summary>
		/// Identity to Module
		/// </summary>
		readonly ConcurrentDictionary<string, ModuleInfo> KnownModules = new ConcurrentDictionary<string, ModuleInfo>();

		/// <summary>
		/// File matching to Module
		/// </summary>
		readonly ConcurrentDictionary<string, ModuleInfo> KnownFiles = new ConcurrentDictionary<string, ModuleInfo>();

		readonly Dictionary<string, ModuleRecommendation> RecMap = new Dictionary<string, ModuleRecommendation>()
		{
			{ "enable", ModuleRecommendation.Enable },
			{ "disable", ModuleRecommendation.Disable },
			{ "change", ModuleRecommendation.Change },
		};

		readonly Dictionary<string, ModuleType> TypeMap = new Dictionary<string, ModuleType>()
		{
			{ "audio", ModuleType.Audio },
			{ "controller", ModuleType.Controller },
			{ "framework", ModuleType.Framework },
			{ "generic", ModuleType.Generic },
			{ "graphics", ModuleType.Graphics },
			{ "interface", ModuleType.Interface },
			{ "multimedia", ModuleType.Multimedia },
			{ "network", ModuleType.Network },
			{ "physics", ModuleType.Physics },
			{ "processing", ModuleType.Processing },
			{ "system", ModuleType.System },
		};

		public void Load(string moduleFilename)
		{
			try
			{
				var modulepath = Path.Combine(DataPath, moduleFilename);

				using var cfg = Config.Load(modulepath);
				foreach (var section in cfg.Config)
				{
					try
					{
						string name = section.Name;
						if (KnownModules.ContainsKey(name)) continue;

						string[] files = section.Get("files")?.Array ?? System.Array.Empty<string>();

						if (files.Length == 0) continue;

						string listeds = section.Get("listed")?.Value.ToLowerInvariant() ?? "no";
						bool listed = yesvalues.Any((x) => x.Equals(listeds));
						//string upgrade = section.TryGet("upgrade")?.Value ?? null;
						//bool open = yesvalues.Contains(section.TryGet("open")?.Value.ToLowerInvariant() ?? "no");
						//bool prop = yesvalues.Contains(section.TryGet("proprietary")?.Value.ToLowerInvariant() ?? "no");
						string exts = section.Get("extension")?.Value.ToLowerInvariant() ?? "no";
						bool ext = yesvalues.Any((x) => x.Equals(exts));
						string ttype = section.Get("type")?.Value.ToLowerInvariant() ?? "unknown"; // TODO

						//string trec = section.TryGet("recommendation")?.Value.ToLowerInvariant() ?? null;
						//string notes = section.TryGet("notes")?.Value ?? null;

						long value = section.Get("value")?.Int ?? 0;
						/*
						ModuleRecommendation rec = ModuleRecommendation.Undefined;
						if (!RecMap.TryGetValue(trec, out rec))
							rec = ModuleRecommendation.Undefined;
						*/

						if (!TypeMap.TryGetValue(ttype, out var type))
							type = ModuleType.Unknown;

						string identity = section.Name;

						var mi = new ModuleInfo
						{
							Identity = identity,
							Type = type,
							Files = files,
							Listed = listed,
							//Upgrade = upgrade,
							//Open = open,
							//Extension = ext,
							//Proprietary = prop,
							//Recommendation = rec,
							Value = value,
						};

						if (KnownModules.TryAdd(identity, mi))
						{
							foreach (var kfile in files)
								KnownFiles.TryAdd(kfile, mi);
						}
					}
					catch (Exception ex)
					{
						Logging.Stacktrace(ex);
					}
				}

				Log.Information($"<Analysis> Modules known: {KnownModules.Count.ToString()}");
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}
	}

	public class ModuleInfo
	{
		public string[] Files = System.Array.Empty<string>();
		public List<string> Detected = new List<string>();
		public ModuleType Type = ModuleType.Unknown;
		public string Identity = string.Empty;
		public long Value = 0;

		public string Upgrade = string.Empty;

		//public List<string> Incompatible = new List<string>();

		/// <summary>
		/// Recommended action to be taken when possible.
		/// </summary>
		public ModuleRecommendation Recommendation = ModuleRecommendation.Undefined;

		//public string Explanation = string.Empty;

		/// <summary>
		/// Primary component.
		/// </summary>
		public bool Listed = false;

		/// <summary>
		/// Extension to some other component, not too interesting on its own.
		/// </summary>
		public bool Extension = false;

		/// <summary>
		/// Relates to proprietary hardware or software that requires special access to use.
		/// </summary>
		public bool Proprietary = false;

		/// <summary>
		/// Open standard.
		/// </summary>
		public bool Open = false;

		public ModuleInfo Clone()
		{
			string[] farr = System.Array.Empty<string>();
			if (Files.Length > 0)
			{
				farr = new string[Files.Length];
				Files.CopyTo(farr, 0);
			}

			var lmi = new ModuleInfo()
			{
				Files = farr,
				Type = Type,
				Identity = Identity,
				Value = Value,
				Recommendation = Recommendation,
				Listed = Listed,
				Extension = Extension,
				Proprietary = Proprietary,
				Open = Open,
			};
			lmi.Detected.AddRange(Detected);
			return lmi;
		}
	}

	public enum ModuleType
	{
		Audio, // xaudio, openal
		Graphics, // dx11, opengl
		Controller, // xinput
		Network, // WinSock
		Multimedia, // Bink
		Physics, // PhysX/Havok
		Interface, // wxwidgets
		Processing, // OpenCL
		Unknown,
		Generic, // API
		System, // kernel
		Framework, // engine
	}

	public enum ModuleRecommendation
	{
		Undefined,
		Enable,
		Disable,
		Change,
	}

	public static partial class NativeMethods
	{
		[StructLayout(LayoutKind.Sequential)]
		internal struct ModuleInformation
		{
			internal IntPtr lpBaseOfDll;
			internal uint SizeOfImage;
			internal IntPtr EntryPoint;
		}

		internal enum ModuleFilter
		{
			ListModulesDefault = 0x0,
			ListModules32Bit = 0x01,
			ListModules64Bit = 0x02,
			ListModulesAll = 0x03,
		}

		[DllImport("psapi.dll")]
		internal static extern bool EnumProcessModulesEx(IntPtr hProcess, [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U4)] [In][Out] IntPtr[] lphModule, int cb, [MarshalAs(UnmanagedType.U4)] out int lpcbNeeded, uint dwFilterFlag);

		[DllImport("psapi.dll", SetLastError = true)]
		internal static extern bool GetModuleInformation(IntPtr hProcess, IntPtr hModule, out ModuleInformation lpmodinfo, uint cb);
	}
}