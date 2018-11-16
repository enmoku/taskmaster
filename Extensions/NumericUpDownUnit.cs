using System;
using System.Collections.Generic;
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
		public NumericUpDownEx() : base()
		{
			// nothing here
		}

		public string Unit { get; set; } = null;

		protected override void UpdateEditText()
		{
			if (!string.IsNullOrEmpty(Unit)) Text = $"{Value} {Unit}";
			else base.UpdateEditText();
		}
	}
}
