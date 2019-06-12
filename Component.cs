﻿//
// Component.cs
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
using System.Runtime.InteropServices;

namespace Taskmaster
{
	public abstract class Component : IDisposable
	{
		public Component()
		{
			if (this.GetType().GetCustomAttributes(typeof(ComponentAttribute), false).Length == 0)
				throw new NotImplementedException(this.GetType().ToString());
		}

		public int ComponentId;

		public void Dispose() => Dispose(true);

		protected abstract void Dispose(bool disposing);
	}

	[System.AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
	public class ComponentAttribute : Attribute
	{
		public bool RequireMainThread { get; set; }
	}

	public class DependencyAttribute : Attribute
	{
		public Type Dependency { get; private set; }

		public DependencyAttribute(Type type) => Dependency = type;
	}
}
