//
// NumericUpDownEx.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2016 M.A.
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
using System.Globalization;
using System.Windows.Forms;

namespace Taskmaster.UI.Extensions
{
	/// <summary>
	/// Extension on NumericUpDown to show some form of unit of measure.
	/// </summary>
	public class NumericUpDownEx : NumericUpDown
	{
		readonly NumberFormatInfo FormatInfo = new NumberFormatInfo();

		public NumericUpDownEx() { }

		public NumericUpDownEx(string unit) => Unit = unit;

		public string Unit { get; set; } = string.Empty; // BUG: this does not trigger UpdateEditText

		// turn text to value
		new protected void ParseEditText()
		{
			if (decimal.TryParse((string.IsNullOrEmpty(Unit) ? Text : Text.Replace(Unit, "")).Trim(), out decimal tval))
				Value = Math.Min(Math.Max(tval, Minimum), Maximum);
			else
			{
				UpdateEditText(); // reset text
				//throw new NotImplementedException();
				// HACK: Just ignore... fuck if there's reasonable default for garbled user data
			}
		}

		// Turn value to text. Called by Value=# setter
		protected override void UpdateEditText()
		{
			if (UserEdit)
			{
				UserEdit = false; // HACK: to prevent infinite loop
				ParseEditText(); // HACK: For some reason ValidateEditText() is not called and instead this happens
				return; // HACK: UpdateEditText is called by ParseEditText indirectly by Value=??
			}

			FormatInfo.NumberDecimalDigits = DecimalPlaces;

			Text = string.Format(FormatInfo, "{0:F}", Value) + (!string.IsNullOrEmpty(Unit) ? $" {Unit}" : "");
		}
	}
}
