//
// Power.Utility.cs
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

namespace Taskmaster.Power
{
	public static partial class Utility
	{
		public static Mode GetModeByName(string name)
		{
			if (string.IsNullOrEmpty(name)) return Mode.Undefined;

			switch (name.ToLowerInvariant())
			{
				case "low":
				case "powersaver":
				case "power saver":
					return Mode.PowerSaver;
				case "average":
				case "medium":
				case "balanced":
					return Mode.Balanced;
				case "high":
				case "highperformance":
				case "high performance":
					return Mode.HighPerformance;
				default:
					return Mode.Undefined;
			}
		}

		public static string GetModeName(Mode mode)
			=> mode switch
			{
				Mode.Balanced => "Balanced",
				Mode.HighPerformance => "High Performance",
				Mode.PowerSaver => "Power Saver",
				Mode.Custom => "Custom",
				_ => "Undefined",
			};

		public static string GetBehaviourName(PowerBehaviour behaviour)
			=> behaviour switch
			{
				PowerBehaviour.Auto => HumanReadable.Hardware.Power.AutoAdjust,
				PowerBehaviour.Manual => HumanReadable.Hardware.Power.Manual,
				PowerBehaviour.RuleBased => HumanReadable.Hardware.Power.RuleBased,
				_ => HumanReadable.Generic.Undefined,
			};
	}
}
