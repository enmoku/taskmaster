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

namespace Taskmaster
{
	sealed public class ProcessAnalysis
	{
		public bool bla;
	}

	sealed public class ProcessAnalyzer
	{
		public ProcessAnalyzer()
		{
		}

		public async void Analyze(ProcessEx info)
		{
			try
			{
				await Task.Delay(TimeSpan.FromSeconds(15));

				var pa = new ProcessAnalysis();
				pa.bla = true;

				Debug.WriteLine("Analyzing:" + $"{info.Name} (#{info.Id})");

				if (info.Process.HasExited) return;

				// Process.Modules unfortunately returned only ntdll.dll, wow64.dll, wow64win.dll, and wow64cpu.dll for a game and seems unusuable for what it was desired

				IntPtr[] modulePtrs = new IntPtr[0];
				int bytesNeeded = 0;

				// Determine number of modules
				if (!EnumProcessModulesEx(info.Process.Handle, modulePtrs, 0, out bytesNeeded, (uint)ModuleFilter.ListModulesAll))
				{
					return;
				}

				bool wxwidgets = false, dsound = false, xaudio=false, physx = false, gamecontroller = false, gameux = false, openal = false, x86 = false, dx9 = false, dx10=false, dx11=false, dx12=false, bink = false, net = true;

				int totalModules = bytesNeeded / IntPtr.Size;
				modulePtrs = new IntPtr[totalModules];
				List<string> linkedModules = new List<string>();
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

						if (moduleName.StartsWith("wxmsw", StringComparison.InvariantCultureIgnoreCase))
							wxwidgets = true;
						else if (moduleName.StartsWith("dsound.dll", StringComparison.InvariantCultureIgnoreCase))
							dsound = true;
						else if (moduleName.StartsWith("xaudio", StringComparison.InvariantCultureIgnoreCase))
							xaudio = true;
						else if (moduleName.StartsWith("physx", StringComparison.InvariantCultureIgnoreCase))
							physx = true;
						else if (moduleName.StartsWith("xinput", StringComparison.InvariantCultureIgnoreCase))
							gamecontroller = true;
						else if (moduleName.StartsWith("dinput", StringComparison.InvariantCultureIgnoreCase))
							gamecontroller = true;
						else if (moduleName.StartsWith("gameux.dll", StringComparison.InvariantCultureIgnoreCase))
							gameux = true;
						else if (moduleName.StartsWith("openal32.dll", StringComparison.InvariantCultureIgnoreCase)) // wrap_oal.dll too
							openal = true;
						else if (moduleName.StartsWith("wow64.dll", StringComparison.InvariantCultureIgnoreCase))
							x86 = true;
						else if (moduleName.StartsWith("d3d9.dll", StringComparison.InvariantCultureIgnoreCase))
							dx9 = true;
						else if (moduleName.StartsWith("d3dx9_", StringComparison.InvariantCultureIgnoreCase))
							dx9 = true;
						else if (moduleName.StartsWith("d3dx10_", StringComparison.InvariantCultureIgnoreCase))
							dx10 = true;
						else if (moduleName.StartsWith("d3dx11_", StringComparison.InvariantCultureIgnoreCase))
							dx11 = true;
						else if (moduleName.StartsWith("d3dx12_", StringComparison.InvariantCultureIgnoreCase))
							dx12 = true;
						else if (moduleName.StartsWith("binkw32.dll", StringComparison.InvariantCultureIgnoreCase))
							bink = true;
						else if (moduleName.StartsWith("wsock32.dll", StringComparison.InvariantCultureIgnoreCase))
							net = true;
					}
				}

				var sbs = new StringBuilder();
				sbs.Append("<Process> Analysis of ").Append(info.Name).Append($" (#{info.Id})").Append(" complete, components identified: ");
				List<string> components = new List<string>();
				if (dsound || openal || xaudio) components.Add("Sound");
				if (physx) components.Add("Physics");
				if (gamecontroller) components.Add("Controller");
				if (gameux) components.Add("GameUX");
				if (dx9) components.Add("DX9");
				if (dx10) components.Add("DX10");
				if (dx11) components.Add("DX11");
				if (dx12) components.Add("DX12");
				if (x86) components.Add("32-bit");
				else components.Add("64-bit");
				if (bink) components.Add("Bink");
				if (net) components.Add("Net");
				sbs.Append(string.Join(", ", components));

				Log.Information(sbs.ToString());
			}
			catch (InvalidOperationException)
			{
				// already exited
			}
			catch (Win32Exception)
			{
				// access denied
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
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
}