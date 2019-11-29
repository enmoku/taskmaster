using System;

namespace Taskmaster
{
	public static partial class TimeSpanExtensions
	{
		public static bool IsZero(this TimeSpan value) => value == TimeSpan.Zero;
	}
}
