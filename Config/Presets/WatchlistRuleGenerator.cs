//
// WatchlistRuleGenerator.cs
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

using Microsoft.Win32;
using System.Collections.Generic;

namespace Taskmaster
{
	internal class WatchlistRuleGenerator
	{
		const string MuiCachePath = @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";
		// HKEY_CURRENT_USER\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache
		const string SteamIdentifier = "/steamapps/common/";

		public WatchlistRuleGenerator()
		{
			// Locate Steam install folders
			// There can be multiple.

			var paths = new HashSet<string>();
			using var key = Registry.CurrentUser.OpenSubKey(MuiCachePath, writable: true);
			var values = key.GetValueNames();
			foreach (var path in values)
			{
				if (path.Contains("/steamapps/common/") || path.Contains(SteamIdentifier.Replace('/', '\\')))
				{
					Logging.DebugMsg("Steam games install path found");
					var off = path.IndexOf("steamapps") + SteamIdentifier.Length;
					paths.Add(path.Substring(0, off));
				}
			}

			// Known App Database?????
		}
	}
}
