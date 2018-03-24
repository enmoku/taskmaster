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
		public enum PowerMode
		{
			PowerSaver = 0,
			Balanced = 1,
			HighPerformance = 2,
			Custom = 9,
			Undefined = 3
		};

		public class AutoAdjustSettings
		{
			public PowerLevels Low = new PowerLevels();
			public PowerLevels High = new PowerLevels();

			public PowerMode DefaultMode = PowerMode.Balanced;
		}

		public class PowerLevels
		{
			public BackoffThresholds Backoff = new BackoffThresholds();
			public CommitThreshold Commit = new CommitThreshold();
			public PowerMode Mode = PowerMode.Balanced;
		}

		public class BackoffThresholds
		{
			public int Level;

			public float Low;
			public float Avg;
			public float High;
		}

		public class CommitThreshold
		{
			public int Level;
			public float Threshold;
		}
	}
}
