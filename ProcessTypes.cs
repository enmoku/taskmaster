//
// ProcessTypes.cs
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

namespace Taskmaster
{
	public enum ProcessType
	{
		/// <summary>
		/// Unspecified type of process.
		/// </summary>
		Generic,
		/// <summary>
		/// Video game.
		/// e.g. Undertale
		/// </summary>
		Game,
		/// <summary>
		/// Tiny utility process, not needing much resources.
		/// e.g. calc
		/// </summary>
		Utility,
		/// <summary>
		/// High priority work oriented process.
		/// e.g. Blender, Sai, Photoshop, etc.
		/// </summary>
		Productivity,
		/// <summary>
		/// Low priority service.
		/// e.g. Windows Search
		/// </summary>
		LoService,
		/// <summary>
		/// High priority service.
		/// e.g. audiodg
		/// </summary>
		HiService,
		/// <summary>
		/// System process. These should not be touched.
		/// e.g. dwm, csrss, winlogon, wininit
		/// </summary>
		System
	};
}
