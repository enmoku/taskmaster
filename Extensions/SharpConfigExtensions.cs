//
// SharpConfigExtensions.cs
//
// Author:
//       M.A. (enmoku) <>
//
// Copyright (c) 2016 M.A. (enmoku)
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

public static class SharpConfigExtensions
{
	/// <summary>
	/// Tries the get named section or returns null.
	/// </summary>
	/// <returns>The get.</returns>
	/// <param name="config">Config.</param>
	/// <param name="section">Section.</param>
	public static SharpConfig.Section TryGet(this SharpConfig.Configuration config, string section)
	{
		Debug.Assert(config != null);
		Debug.Assert(!string.IsNullOrEmpty(section));

		if (config.Contains(section))
			return config[section];
		return null;
	}

	/// <summary>
	/// Tries the get named setting or returns null.
	/// </summary>
	/// <returns>The get.</returns>
	/// <param name="section">Section.</param>
	/// <param name="setting">Setting.</param>
	public static SharpConfig.Setting TryGet(this SharpConfig.Section section, string setting)
	{
		Debug.Assert(section != null);
		Debug.Assert(!string.IsNullOrEmpty(setting));

		if (section.Contains(setting))
			return section[setting];
		return null;
	}

	public static SharpConfig.Setting GetSetDefault<T>(this SharpConfig.Section section, string setting, T fallback)
	{
		bool unused;
		return section.GetSetDefault(setting, fallback, out unused);
	}

	/// <summary>
	/// Get setting from section while setting a default value if it has none.
	/// </summary>
	/// <returns>The setting.</returns>
	/// <param name="section">Section.</param>
	/// <param name="setting">Setting name.</param>
	/// <param name="fallback">Fallback default value for the setting.</param>
	/// <typeparam name="T">Setting type.</typeparam>
	public static SharpConfig.Setting GetSetDefault<T>(this SharpConfig.Section section, string setting, T fallback, out bool defaulted)
	{
		Debug.Assert(section != null);
		Debug.Assert(!string.IsNullOrEmpty(setting));

		SharpConfig.Setting rv;
		rv = section[setting];
		if (!section[setting].IsArray && section[setting].GetValue<string>() == string.Empty)
		{
			section[setting].SetValue(fallback);
			defaulted = true;
		}
		else
			defaulted = false;
		// todo: what do do about arrays?

		return rv;
	}
}
