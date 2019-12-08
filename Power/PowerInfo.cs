//
// PowerInfo.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2018 M.A.
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
	namespace Power
	{
		public enum Mode
		{
			PowerSaver = 0,
			Balanced = 1,
			HighPerformance = 2,
			Custom = 9,
			Undefined = 3
		};

		public class AutoAdjustSettings
		{
			/// <summary>
			/// Low mode control
			/// </summary>
			public AutoAdjustLevels Low;

			/// <summary>
			/// High mode control
			/// </summary>
			public AutoAdjustLevels High;

			/// <summary>
			/// Default mode when neither low or high is in effect
			/// </summary>
			public Mode DefaultMode { get; set; } = Mode.Balanced;

			/// <summary>
			/// CPU queue adjustment
			/// </summary>
			public QueueBarriers Queue;
		}

		public struct AutoAdjustLevels
		{
			public BackoffThresholds Backoff;
			public CommitThreshold Commit;
			public Mode Mode;
		}

		public struct BackoffThresholds
		{
			public int Level { get; set; }

			public float Low { get; set; }
			public float Mean { get; set; }
			public float High { get; set; }
		}

		public struct CommitThreshold
		{
			public int Level { get; set; }
			public float Threshold { get; set; }
		}

		public struct QueueBarriers
		{
			/// <summary>
			/// Queue equal or above this will prevent low mode.
			/// </summary>
			public int Low { get; set; }
			// Queue equal or above this will prevent non-high mode.
			public int High { get; set; }
		}
	}
}