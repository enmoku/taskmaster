using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Taskmaster
{
	public static partial class TimeSpanExtensions
	{
		public static bool IsZero(this TimeSpan value) => value == TimeSpan.Zero;
	}
}
