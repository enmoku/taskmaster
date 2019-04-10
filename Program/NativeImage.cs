//
// MKAh.Program.NativeImage.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2019 M.A.
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

using System.Diagnostics;
using System.Linq;

namespace MKAh.Program
{
	/// <summary>
	/// TODO: Detect when the native image needs to be regenerated.
	/// Failure to regenerate or remove the native image can result in the application becoming nonfunctional.
	/// </summary>
	public static partial class NativeImage
	{
		/// <summary>
		/// Wrapper.
		/// </summary>
		public static void Full()
		{
			var prc = Process.GetCurrentProcess();
			if (!Exists(prc))
				Create(prc).WaitForExit();
			else
				Update(prc).WaitForExit();
		}

		public static void UpdateOnly()
		{
			var prc = Process.GetCurrentProcess();
			if (Exists(prc))
				Update(prc);
		}

		public static bool Exists(Process process=null)
		{
			bool rv = false;

			if (process == null) process = Process.GetCurrentProcess();

			var modules = new ProcessModule[process.Modules.Count];
			process.Modules.CopyTo(modules, 0);

			var query = from mod in modules where mod.FileName.Contains($"\\{process.ProcessName}.ni") select mod.FileName;

			rv = query.Count() > 0;

			if (rv)
				Debug.WriteLine("Native image: " + query.ElementAt(0));
			else
				Debug.WriteLine("IL image: " + process.MainModule.FileName);

			return rv;
		}

		static readonly string ngenpath = System.IO.Path.Combine(
			System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows),
			"Microsoft.NET", "Framework", "v4.0.30319", "ngen.exe");

		/// <summary>
		/// Calls Ngen to generate native image. Returns Process for the
		/// </summary>
		/// <returns>Process for Ngen</returns>
		public static Process Create(Process process=null) => Ngen($"install \"{process.MainModule.FileName}\"", process);

		public static Process Remove(Process process=null) => Ngen($"uninstall \"{process.MainModule.FileName}\"", process);

		public static Process Update(Process process=null) => Ngen($"update \"{process.MainModule.FileName}\"", process);

		static Process Ngen(string argument, Process process=null)
		{
			if (process == null) process = Process.GetCurrentProcess();

			var startInfo = new ProcessStartInfo(ngenpath, argument)
			{
				CreateNoWindow = false,
				UseShellExecute = false,
			};

			return Process.Start(startInfo);
		}
	}
}
