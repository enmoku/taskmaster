//
// PowerPresets.cs
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

using Taskmaster.Power;

namespace Taskmaster
{
	static class PowerAutoadjustPresets
	{
		public static AutoAdjustSettings Default()
		{
			return new AutoAdjustSettings()
			{
				DefaultMode = Mode.Balanced,
				High = {
					Mode = Mode.HighPerformance,
					Backoff = {
						High = 60,
						Mean = 40,
						Low = 15,
						Level = 7,
					},
					Commit = {
						Level = 3,
						Threshold = 70
					}
				},
				Low = {
					Mode = Mode.PowerSaver,
					Backoff = {
						High = 50,
						Mean = 35,
						Low = 25,
						Level = 3,
					},
					Commit = {
						Level = 7,
						Threshold = 15
					}
				},
				Queue = {
					 High = 18,
					 Low = 5,
				},
			};
		}
	}
}
