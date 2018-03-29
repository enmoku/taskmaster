//
// Aspects.cs
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

using System;
using System.Threading.Tasks;
using PostSharp.Aspects;
using PostSharp.Serialization;

namespace Taskmaster.Aspects
{
	[PSerializable]
	sealed public class LoggingAspect : OnMethodBoundaryAspect
	{
		public override void OnEntry(MethodExecutionArgs args)
		{
			Console.WriteLine("The {0} method has been entered.", args.Method.Name);
		}

		public override void OnSuccess(MethodExecutionArgs args)
		{
			Console.WriteLine("The {0} method executed successfully.", args.Method.Name);
		}

		public override void OnExit(MethodExecutionArgs args)
		{
			Console.WriteLine("The {0} method has exited.", args.Method.Name);
		}

		public override void OnException(MethodExecutionArgs args)
		{
			Console.WriteLine("An exception was thrown in {0}.", args.Method.Name);
		}
	}

	/// <summary>
	/// Simplifies need for BeginInvoke.
	/// </summary>
	[LinesOfCodeAvoided(5)]
	[PSerializable]
	sealed public class UIThreadAspect : MethodInterceptionAspect
	{
		public override void OnInvoke(MethodInterceptionArgs args)
		{
			var ctrl = (System.Windows.Forms.Control)args.Instance;

			if (ctrl.InvokeRequired)
				ctrl.BeginInvoke(new Action(args.Proceed));
			else
				args.Proceed();
		}
	}
}
