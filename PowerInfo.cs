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
	namespace PowerInfo
	{
		public enum PowerMode : int
		{
			PowerSaver = 0,
			Balanced = 1,
			HighPerformance = 2,
			Custom = 9,
			Undefined = 3
		};

		sealed public class AutoAdjustSettings
		{
			/// <summary>
			/// Low mode control
			/// </summary>
			public PowerLevels Low;
			/// <summary>
			/// High mode control
			/// </summary>
			public PowerLevels High;

			/// <summary>
			/// Default mode when neither low or high is in effect
			/// </summary>
			public PowerMode DefaultMode = PowerMode.Balanced;

			/// <summary>
			/// CPU queue adjustment
			/// </summary>
			public QueueBarriers Queue;
		}

		public struct PowerLevels
		{
			public BackoffThresholds Backoff;
			public CommitThreshold Commit;
			public PowerMode Mode;
		}

		public struct BackoffThresholds
		{
			public int Level;

			public float Low;
			public float Mean;
			public float High;
		}

		public struct CommitThreshold
		{
			public int Level;
			public float Threshold;
		}

		public struct QueueBarriers
		{
			/// <summary>
			/// Queue equal or above this will prevent low mode.
			/// </summary>
			public int Low;
			// Queue equal or above this will prevent non-high mode.
			public int High;
		}
	}
}