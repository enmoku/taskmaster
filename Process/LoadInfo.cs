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

namespace Taskmaster.Process
{
	public class LoadValue
	{
		public bool Heavy { get; set; } = false;

		public uint HeavyCount { get; set; } = 0;

		public bool Light { get; set; } = false;

		public uint LightCount { get; set; } = 0;

		public float Min { get; private set; } = float.MaxValue;

		public float Current { get; private set; } = 0f;

		public float Max { get; private set; } = float.MinValue;

		public float Average { get; private set; } = 0f;

		readonly float[] Samples = { 0, 0, 0, 0, 0 };
		int SampleIndex = 0;

		public LoadType Type { get; set; }

		readonly float AverageThreshold, MaxThreshold, MinThreshold;

		public LoadValue(LoadType type, float average, float max, float min)
		{
			Type = type;

			AverageThreshold = average;
			MaxThreshold = max;
			MinThreshold = min;
		}

		public void Update(float value)
		{
			if (float.IsNaN(value)) return;

			Current = value;
			if (Min > Current) Min = Current;
			if (Max < Current) Max = Current;

			Samples[SampleIndex++ % 5] = Current; // BUG: Does ++ wrap around or just break?

			Average = (Samples[0] + Samples[1] + Samples[2] + Samples[3] + Samples[4]) / 5;
		}
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
