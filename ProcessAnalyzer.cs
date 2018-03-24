//
// ProcessAnalyzer.cs
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
using System.Diagnostics;

namespace Taskmaster
{
	public class ProcessAnalysis
	{
		public bool bla;
	}

	public class ProcessAnalyzer
	{
		public ProcessAnalyzer()
		{
		}

		public static ProcessAnalysis Analyze(System.Diagnostics.Process process)
		{
			ProcessAnalysis pa = new ProcessAnalysis();
			pa.bla = true;

			// this unfortunately returned only ntdll.dll, wow64.dll, wow64win.dll, and wow64cpu.dll for a game and seems unusuable for what it was desired
			foreach (ProcessModule module in process.Modules)
			{
				string file = null, name = null;
				try
				{
					file = module.FileName;
				}
				catch { }
				try
				{
					name = module.ModuleName;
				}
				catch { }
				Console.WriteLine(" -- " + name + " = " + file);
			}

			return pa;
		}
	}
}
