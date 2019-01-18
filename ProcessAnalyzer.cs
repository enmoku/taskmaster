//
// ProcessAnalyzer.cs
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
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Serilog;
using System.Collections.Concurrent;

namespace Taskmaster
{
	sealed public class ProcessAnalyzer
	{
		public ProcessAnalyzer()
		{

		}

		ConcurrentDictionary<byte[], int> cache = new ConcurrentDictionary<byte[], int>(new StructuralEqualityComparer<byte[]>());

		public async void Analyze(ProcessEx info)
		{
			if (string.IsNullOrEmpty(info.Path)) return;

			var crypt = new System.Security.Cryptography.SHA512Cng();
			byte[] hash = crypt.ComputeHash(Encoding.UTF8.GetBytes(info.Path.ToLowerInvariant()));

			// TODO: Prevent bloating somehow.
			if (!cache.TryAdd(hash, 0)) return; // already there

			bool record = Taskmaster.RecordAnalysis > 0;

			int delay = record ? Taskmaster.RecordAnalysis.Constrain(10, 180) : 30;
			Log.Debug($"<Analysis> {info.Name} (#{info.Id}) scheduled");

			var linkedModules = new List<ModuleInfo>();
			var identifiedModules = new ConcurrentDictionary<string, ModuleInfo>();
			long privMem = 0;
			long threadCount = 0;
			long workingSet = 0;
			long virtualMem = 0;

			bool x64 = true;

			string modFile = string.Empty;
			FileVersionInfo version = null;
			long modMemory = 0;

			try
			{
				await Task.Delay(TimeSpan.FromSeconds(delay));

				//var pa = new ProcessAnalysis();
				//pa.bla = true;

				if (info.Process.HasExited)
				{
					Log.Debug($"<Analysis> {info.Name} (#{info.Id}) cancelled; already gone");
					cache.TryRemove(hash, out _);
					return;
				}

				if (Taskmaster.Trace) Debug.WriteLine("Analyzing:" + $"{info.Name} (#{info.Id})");

				modFile = info.Process.MainModule.FileName;
				version = info.Process.MainModule.FileVersionInfo;
				modMemory = info.Process.MainModule.ModuleMemorySize;

				// Process.Modules unfortunately returned only ntdll.dll, wow64.dll, wow64win.dll, and wow64cpu.dll for a game and seems unusuable for what it was desired
				foreach (ProcessModule mod in info.Process.Modules)
				{
					if (mod.ModuleName.StartsWith("wow64.dll", StringComparison.InvariantCultureIgnoreCase))
					{
						x64 = false;
						break;
					}
				}

				IntPtr[] modulePtrs = new IntPtr[0];
				int bytesNeeded = 0;

				// Determine number of modules
				if (!EnumProcessModulesEx(info.Process.Handle, modulePtrs, 0, out bytesNeeded, (uint)ModuleFilter.ListModulesAll))
					return;

				int totalModules = bytesNeeded / IntPtr.Size;
				modulePtrs = new IntPtr[totalModules];
				var handle = info.Process.Handle;
				if (EnumProcessModulesEx(handle, modulePtrs, bytesNeeded, out bytesNeeded, (uint)ModuleFilter.ListModulesAll))
				{
					for (int index = 0; index < totalModules; index++)
					{
						StringBuilder modulePath = new StringBuilder(1024);
						GetModuleFileNameEx(handle, modulePtrs[index], modulePath, (uint)(modulePath.Capacity));

						string moduleName = Path.GetFileName(modulePath.ToString());
						//ModuleInformation moduleInformation = new ModuleInformation();
						//GetModuleInformation(handle, modulePtrs[index], out moduleInformation, (uint)(IntPtr.Size * (modulePtrs.Length)));

						//linkedModules.Add(moduleName.ToLowerInvariant());
						//Debug.WriteLine(" - " + moduleName);

						var mi = IdentifyModule(moduleName.Trim());
						if (mi.Primary) identifiedModules.TryAdd(mi.Identity, mi);

						if (record) linkedModules.Add(mi);
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
				cache.TryRemove(hash, out _);
				Log.Debug($"[{info.Controller.FriendlyName}] {info.Name} (#{info.Id}) exited before analysis could begin.");
			}
			catch (Win32Exception)
			{
				// access denied
				cache.TryRemove(hash, out _);
				Log.Debug($"[{info.Controller.FriendlyName}] {info.Name} (#{info.Id}) was denied access for analysis.");
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				cache.TryRemove(hash, out _);
				Logging.Stacktrace(ex);
			}

			try
			{
				// LOG analysis
				bool memLow = privMem < 8; // less than 8MB private memory
				bool memModerate = privMem > 400; // more than 400 MB
				bool memHigh = privMem > 1200; // more than 1200 MB
				bool memExtreme = privMem > 2600; // more than 2600 MB

				var sbs = new StringBuilder();
				sbs.Append("<Analysis> ").Append(info.Name).Append($" (#{info.Id})").Append(" facts: ");
				List<string> components = new List<string>();

				if (x64) components.Add("64-bit");
				else components.Add("32-bit");

				if (memLow) components.Add("Memory(Low)");
				else if (memExtreme) components.Add("Memory(Extreme)");
				else if (memHigh) components.Add("Memory(High)");
				else if (memModerate) components.Add("Memory(Moderate)");

				long latestDX = 0, latestDXX = 0;
				foreach (var modname in identifiedModules.Keys)
				{
					components.Add(modname);

					// recommendations
					if (modname.StartsWith("DirectX", StringComparison.InvariantCultureIgnoreCase))
					{
						if (identifiedModules.TryGetValue(modname, out var mod))
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

				var recommendations = new List<string>();

				if (latestDXX > latestDX)
					recommendations.Add($"force DX {latestDX / 10} rendering");

				if (identifiedModules.ContainsKey("PhysX"))
					recommendations.Add("disable PhysX");

				if (recommendations.Count > 0)
				{
					sbs.Clear();
					sbs.Append("<Analysis> ").Append(info.Name).Append($" (#{info.Id})").Append(" recommendations: ");
					sbs.Append(string.Join(", ", recommendations));
					Log.Information(sbs.ToString());
				}

				// RECORD analysis

				if (record)
				{
					var file = $"{DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")}-{info.Name}.analysis.yml";
					var path = Path.Combine(Taskmaster.datapath, "Analysis");
					var endpath = Path.Combine(path, file);
					var di = Directory.CreateDirectory(path);

					var contents = new StringBuilder();
					contents.Append("Analysis:").AppendLine()
						.Append("\t").Append("Process: ").Append(info.Name).AppendLine()
						.Append("\t").Append("Version: ").Append(version.FileVersion?.ToString() ?? string.Empty).AppendLine()
						.Append("\t").Append("Product: ").Append(version.ProductName?.ToString() ?? string.Empty).AppendLine()
						.Append("\t").Append("Company: ").Append(version.CompanyName?.ToString() ?? string.Empty).AppendLine()
						.Append("\t").Append("64-bit : ").Append(x64 ? "Yes" : "No").AppendLine()
						.Append("\t").Append("Path   : ").Append(info.Path).AppendLine()
						.Append("\t").Append("Threads: ").Append(threadCount).AppendLine()
						.Append("\t").Append("Memory : ").AppendLine()
						.Append("\t\t- ").Append("Private : ").Append(privMem).AppendLine()
						.Append("\t\t- ").Append("Working : ").Append(workingSet).AppendLine()
						.Append("\t\t- ").Append("Virtual : ").Append(virtualMem).AppendLine()
						.Append("\t").Append("Modules: ").AppendLine();

					foreach (var mod in linkedModules)
					{
						bool notUnknown = mod.Type != ModuleType.Unknown;
						bool hasIdentity = !string.IsNullOrEmpty(mod.Identity);
						bool subItems = notUnknown || hasIdentity;
						contents.Append("\t\t- ").Append(mod.Name).Append(subItems ? ":" : "").AppendLine();
						if (notUnknown)
							contents.Append("\t\t  Type     : ").Append(mod.Type.ToString()).AppendLine();
						if (hasIdentity)
							contents.Append("\t\t  Identity : ").Append(mod.Identity).AppendLine();
					}

					File.WriteAllText(endpath, contents.ToString());
				}
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		// TODO: build external library of components that's loaded as a dictionary of sorts
		public ModuleInfo IdentifyModule(string moduleName)
		{
			var mi = new ModuleInfo(moduleName);

			if (moduleName.StartsWith("wxmsw", StringComparison.InvariantCultureIgnoreCase))
			{
				mi.Type = ModuleType.Interface;
				mi.Identity = "WxWidgets";
				mi.Open = true;
			}
			else if (moduleName.StartsWith("dsound.dll", StringComparison.InvariantCultureIgnoreCase))
			{
				mi.Type = ModuleType.Audio;
				mi.Identity = "DirectSound";
				mi.Primary = true;
				mi.Upgrade = "XAudio";
			}
			else if (moduleName.StartsWith("xaudio", StringComparison.InvariantCultureIgnoreCase))
			{
				mi.Type = ModuleType.Audio;
				mi.Identity = "XAudio";
				mi.Primary = true;
			}
			else if (moduleName.StartsWith("physx", StringComparison.InvariantCultureIgnoreCase))
			{
				mi.Type = ModuleType.Physics;
				mi.Identity = "PhysX";
				mi.Proprietary = true;
				mi.Primary = true;
				// If possible, using something else for physics would be good.
				// This is likely loaded regardless of such choice.
				mi.Recommendation = ModuleRecommendation.Change;
			}
			else if (moduleName.StartsWith("xinput", StringComparison.InvariantCultureIgnoreCase))
			{
				mi.Type = ModuleType.Controller;
				mi.Identity = "XInput";
				mi.Primary = true;
			}
			else if (moduleName.StartsWith("dinput", StringComparison.InvariantCultureIgnoreCase))
			{
				mi.Type = ModuleType.Controller;
				mi.Identity = "DirectInput";
				mi.Primary = true;
				// Unless non-gamepads are involved, directinput is bad...
				// This will likely be loaded regardless of users chosen controller, however.
				mi.Recommendation = ModuleRecommendation.Change;
				mi.Upgrade = "XInput";
			}
			else if (moduleName.StartsWith("d3d8thk.dll", StringComparison.InvariantCultureIgnoreCase))
			{
				mi.Type = ModuleType.Generic;
				mi.Identity = "DirectX 8 Thunk API";
				mi.Value = 8;
				mi.Recommendation = ModuleRecommendation.Change; // upgrade to DX11 if possible
			}
			else if (moduleName.StartsWith("gameux.dll", StringComparison.InvariantCultureIgnoreCase))
			{
				mi.Type = ModuleType.Generic;
				mi.Identity = "GameUX";
			}
			else if (moduleName.StartsWith("openal32.dll", StringComparison.InvariantCultureIgnoreCase)) // wrap_oal.dll too
			{
				mi.Type = ModuleType.Audio;
				mi.Identity = "OpenAL";
				mi.Open = true;
				mi.Primary = true;
				mi.Upgrade = "OpenAL Soft"; // Potential, not necessarily any better (https://kcat.strangesoft.net/openal.html)
			}
			else if (moduleName.StartsWith("wow64.dll", StringComparison.InvariantCultureIgnoreCase))
			{
				mi.Type = ModuleType.Generic;
				mi.Identity = "Windows-on-Windows";
				mi.Recommendation = ModuleRecommendation.Change; // Should run 64-bit version if available
			}
			else if (moduleName.StartsWith("d3d9.dll", StringComparison.InvariantCultureIgnoreCase))
			{
				mi.Type = ModuleType.Multimedia;
				mi.Identity = "DirectX 9";
				mi.Value = 90;
				mi.Primary = true;
			}
			else if (moduleName.StartsWith("d3dx9_", StringComparison.InvariantCultureIgnoreCase))
			{
				mi.Type = ModuleType.Multimedia;
				mi.Identity = "DirectX 9 Extensions";
				mi.Value = 90;
				mi.Extension = true;
			}
			else if (moduleName.StartsWith("d3d10.dll", StringComparison.InvariantCultureIgnoreCase))
			{
				mi.Type = ModuleType.Multimedia;
				mi.Identity = "DirectX 10";
				mi.Value = 100;
				mi.Primary = true;
				// Anything else is better. This is likely loaded regardless of user choice.
				mi.Recommendation = ModuleRecommendation.Change;
			}
			else if (moduleName.StartsWith("d3d10_1.dll", StringComparison.InvariantCultureIgnoreCase))
			{
				mi.Type = ModuleType.Multimedia;
				mi.Identity = "DirectX 10.1";
				mi.Value = 101;
				mi.Extension = true;
			}
			else if (moduleName.StartsWith("d3dx10_", StringComparison.InvariantCultureIgnoreCase))
			{
				mi.Type = ModuleType.Multimedia;
				mi.Identity = "DirectX 10 Extensions";
				mi.Value = 100;
				mi.Extension = true;
			}
			else if (moduleName.StartsWith("d3d11.dll", StringComparison.InvariantCultureIgnoreCase))
			{
				mi.Type = ModuleType.Multimedia;
				mi.Identity = "DirectX 11";
				mi.Value = 110;
				mi.Primary = true;
			}
			else if (moduleName.StartsWith("d3dx11_", StringComparison.InvariantCultureIgnoreCase))
			{
				mi.Type = ModuleType.Multimedia;
				mi.Identity = "DirectX 11 Extensions";
				mi.Value = 110;
				mi.Extension = true;
			}
			else if (moduleName.StartsWith("d3d12.dll", StringComparison.InvariantCultureIgnoreCase))
			{
				mi.Type = ModuleType.Multimedia;
				mi.Identity = "DirectX 12";
				mi.Value = 120;
				mi.Primary = true;
			}
			else if (moduleName.StartsWith("d3dx12_", StringComparison.InvariantCultureIgnoreCase))
			{
				mi.Type = ModuleType.Multimedia;
				mi.Identity = "DirectX 12 Extensions";
				mi.Value = 120;
				mi.Extension = true;
			}
			else if (moduleName.StartsWith("binkw32.dll", StringComparison.InvariantCultureIgnoreCase))
			{
				mi.Type = ModuleType.Multimedia;
				mi.Identity = "Bink";
				mi.Proprietary = true;
			}
			else if (moduleName.StartsWith("wsock32.dll", StringComparison.InvariantCultureIgnoreCase))
			{
				mi.Type = ModuleType.Network;
				mi.Identity = "WinSock";
				mi.Primary = true;
			}
			else if (moduleName.StartsWith("bugsplat.dll", StringComparison.InvariantCultureIgnoreCase))
			{
				mi.Type = ModuleType.Generic;
				mi.Identity = "BugSplat";
			}
			else if (moduleName.StartsWith("gdi32.dll", StringComparison.InvariantCultureIgnoreCase))
			{
				mi.Type = ModuleType.Graphics;
				mi.Identity = "GDI"; // deprecated graphics API, may still be loaded as part of the newer ones?

				//mi.Primary = true;
				//mi.Deprecated = true;

				mi.Recommendation = ModuleRecommendation.Change; // GDI+
			}
			else if (moduleName.StartsWith("gdiplus.dll", StringComparison.InvariantCultureIgnoreCase))
			{
				mi.Type = ModuleType.Graphics;
				mi.Identity = "GDI+";
				//mi.Primary = true;
			}
			else if (moduleName.StartsWith("steamapi.dll", StringComparison.InvariantCultureIgnoreCase))
			{
				mi.Type = ModuleType.Generic;
				mi.Identity = "Steam API"; // distributed via Steam, not a guarantee of anything
			}
			else if (moduleName.StartsWith("fmodex.dll", StringComparison.InvariantCultureIgnoreCase))
			{
				mi.Type = ModuleType.Audio;
				mi.Identity = "FMOD EX";
				mi.Primary = true;
			}
			else if (moduleName.StartsWith("stlport.dll", StringComparison.InvariantCultureIgnoreCase))
			{
				mi.Type = ModuleType.Generic;
				mi.Identity = "STLPort"; // Alternate C++ STL http://www.stlport.org/
			}
			else if (moduleName.StartsWith("api-ms-win-downlevel-", StringComparison.InvariantCultureIgnoreCase))
			{
				mi.Type = ModuleType.Unknown;
				mi.Identity = "Downlevel API"; // ??? is this just older versions of dlls being loaded?
			}
			else if (moduleName.StartsWith("unityplayer.dll", StringComparison.InvariantCultureIgnoreCase))
			{
				mi.Type = ModuleType.Framework;
				mi.Identity = "Unity engine";
				mi.Primary = true;
				// Unity has some performance problems, but most of them seem to be caused by just bad programming by devs,
				// probably not doing some optimizations that they expect the engine to do on its own.
			}
			else if (moduleName.StartsWith("opengl32.dll", StringComparison.InvariantCultureIgnoreCase))
			{
				mi.Type = ModuleType.Graphics;
				mi.Identity = "OpenGL";
				mi.Primary = true;
			}

			return mi;
		}

		[StructLayout(LayoutKind.Sequential)]
		public struct ModuleInformation
		{
			public IntPtr lpBaseOfDll;
			public uint SizeOfImage;
			public IntPtr EntryPoint;
		}

		internal enum ModuleFilter
		{
			ListModulesDefault = 0x0,
			ListModules32Bit = 0x01,
			ListModules64Bit = 0x02,
			ListModulesAll = 0x03,
		}

		[DllImport("psapi.dll")]
		public static extern bool EnumProcessModulesEx(IntPtr hProcess, [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U4)] [In][Out] IntPtr[] lphModule, int cb, [MarshalAs(UnmanagedType.U4)] out int lpcbNeeded, uint dwFilterFlag);

		[DllImport("psapi.dll")]
		public static extern uint GetModuleFileNameEx(IntPtr hProcess, IntPtr hModule, [Out] StringBuilder lpBaseName, [In] [MarshalAs(UnmanagedType.U4)] uint nSize);

		[DllImport("psapi.dll", SetLastError = true)]
		public static extern bool GetModuleInformation(IntPtr hProcess, IntPtr hModule, out ModuleInformation lpmodinfo, uint cb);
	}

	sealed public class ModuleInfo
	{
		public ModuleInfo(string name) { Name = name; }

		public string Name = string.Empty;
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
		public bool Primary = false;
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
}