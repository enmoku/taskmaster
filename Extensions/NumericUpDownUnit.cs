using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Taskmaster.Extensions
{
	/// <summary>
	/// Extension on NumericUpDown to show some form of unit of measure.
	/// </summary>
	class NumericUpDownEx : NumericUpDown
	{
		NumberFormatInfo FormatInfo = new NumberFormatInfo();

		public NumericUpDownEx() : base()
		{
			// nothing here
		}

		public string Unit { get; set; } = null;

		protected override void UpdateEditText()
		{
			if (!string.IsNullOrEmpty(Unit))
			{
				FormatInfo.NumberDecimalDigits = DecimalPlaces;
				Text = string.Format(FormatInfo, "{0:F}", Value) + " " + Unit;
			}
			else
				base.UpdateEditText();
		}
	}
}
