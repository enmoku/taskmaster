//
// Process.LoadInfo.cs
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

using System;
using System.Linq;

namespace Taskmaster.Process
{
	public class LoadValue
	{
		public bool Heavy { get; set; }

		public uint HeavyCount { get; set; }

		public bool Light { get; set; }

		public uint LightCount { get; set; }

		public float Min => Samples.Min();

		public float Current => Samples[(SampleIndex-1) % 5];

		public float Max => Samples.Max();

		public float Average => (Samples[0] + Samples[1] + Samples[2] + Samples[3] + Samples[4]) / 5;

		readonly float[] Samples = { 0, 0, 0, 0, 0 };
		int SampleIndex = 1;

		public LoadType Type { get; set; }

		readonly float AverageThreshold, MaxThreshold, MinThreshold;

		public LoadValue(LoadType type, float average, float max, float min)
		{
			Type = type;

			AverageThreshold = average;
			MaxThreshold = max;
			MinThreshold = min;
		}

		public void Update(float value) => Samples[SampleIndex++ % 5] = value; // BUG: Does ++ wrap around or just break?
	}

	[Flags]
	public enum LoadType
	{
		None = 0,
		CPU = 1,
		RAM = 2,
		IO = 4,
		Storage = RAM | IO,
		Volatile = CPU | RAM,
		All = CPU | RAM | IO,
	}
}
