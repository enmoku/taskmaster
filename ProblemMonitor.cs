//
// ProblemMonitor.cs
//
// Author:
//       M.A. (enmoku) <>
//
// Copyright (c) 2018 M.A. (enmoku)
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
using System.Collections.Generic;

namespace Taskmaster
{
	/// <summary>
	/// Monitors for variety of problems and reports on them.
	/// </summary>
	public class ProblemMonitor
	{
		Dictionary<int, Problem> activeProblems = new Dictionary<int, Problem>();

		public ProblemMonitor()
		{
			// TODO: Add different problems to monitor for

			// --------------------------------------------------------------------------------------------------------
			// What? : Fragmentation. Per drive.
			// How? : Keep reading disk access splits, warn when they exceed a threshold with sufficient samples or the split becomes excessive.
			// Use? : Recommend defrag.
			// -------------------------------------------------------------------------------------------------------

			// -------------------------------------------------------------------------------------------------------
			// What? : NVM performance. Per drive.
			// How? : Disk queue length. Disk delay.
			// Use? : Recommend reducing multitasking and/or moving the tasks to faster drive.
			// -------------------------------------------------------------------------------------------------------

			// -------------------------------------------------------------------------------------------------------
			// What? : CPU insufficiency.
			// How? : CPU instruction queue length.
			// Use? : Warn about CPU being insuffficiently scaled for the tasks being done.
			// -------------------------------------------------------------------------------------------------------

			// -------------------------------------------------------------------------------------------------------
			// What? : Free disk space
			// How? : Make sure drives have at least 4G free space. Possibly configurable.
			// Use? : Recommend disk cleanup and/or uninstalling unused apps.
			// Opt? : Auto-empty trash.
			// -------------------------------------------------------------------------------------------------------

			// -------------------------------------------------------------------------------------------------------
			// What? : Problematic apps
			// How? : Detect apps like TrustedInstaller, MakeCab, etc. running.
			// Use? : Inform user of high resource using background tasks that should be let to run.
			// .... Potentially inform how to temporarily mitigate the issue.
			// Opt? : Priority and affinity reduction.
			// --------------------------------------------------------------------------------------------------------

			// --------------------------------------------------------------------------------------------------------
			// What? : Driver crashes
			// How? : No idea.
			// Use? : Recommend driver up or downgrade.
			// Opt? : None. Analyze situation. This might've happened due to running out of memory.
			// --------------------------------------------------------------------------------------------------------

			// --------------------------------------------------------------------------------------------------------
			// What? : Underscaled network
			// How? : Network queue length
			// Use? : Recommend throttling network using apps.
			// Opt? : Check ECN state and recommend toggling it. Make sure CTCP is enabled.
			// --------------------------------------------------------------------------------------------------------

			// --------------------------------------------------------------------------------------------------------
			// What? : Background task taking too many resources.
			// How? : Monitor total CPU usage until it goes past certain threshold, check highest CPU usage app. ...
			// ... Check if the app is in foreground.
			// Use? : Warn about intense background tasks.
			// Opt? : 
			// --------------------------------------------------------------------------------------------------------
		}
	}

	enum ProblemState
	{
		New,
		Interacted,
		Invalid,
		Dismissed
	}

	struct Problem
	{
		int Id;
		string Description;

		// 
		DateTime Occurrence;

		// don't re-state the problem in this time
		TimeSpan Cooldown;

		// user actions on this
		bool Acknowledged;
		bool Dismissed;

		// State
		ProblemState State;
	}
}
