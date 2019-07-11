//
// AutoAdjustReaction.cs
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

namespace Taskmaster.Power
{
	public class AutoAdjustReactionEventArgs : ProcessorLoadEventArgs
	{
		public Mode Mode = Mode.Undefined;
		public Reaction Reaction = Reaction.Average;
		public bool Steady = false;

		/// <summary>
		/// Pressure to change, from 0.0f to 1.0f.
		/// </summary>
		public float Pressure = 0F;

		/// <summary>
		/// Has this power event been put to use or if this is speculative.
		/// </summary>
		public bool Enacted = false;

		public static AutoAdjustReactionEventArgs From(ProcessorLoadEventArgs ea)
		{
			return new AutoAdjustReactionEventArgs
			{
				Load = new ProcessorLoad
				{
					Current = ea.Load.Current,
					Mean = ea.Load.Mean,
					High = ea.Load.High,
					Low = ea.Load.Low,
					Period = ea.Load.Period,
					Queue = ea.Load.Queue,
				}
			};
		}
	}
}
