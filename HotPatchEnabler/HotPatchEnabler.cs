/*
MIT License

Copyright (c) 2016 xiaobin83

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/
using System;
using System.IO;
using UnusedBytecodeStripper2.Chain;
using System.Collections.Generic;

namespace hotpatch
{
	[DllProcessor(Priority = 100)]
	public class HotPatchEnabler : IProcessDll
	{
		public void ProcessDll(string[] args)
		{
			Log("Start HotPatchEnabler ... ");
			HotPatchEditor.Log = Log;
			var dllFileNames = new List<string>();
			for (var i = 0; i < args.Length; i++)
			{
				switch (args[i])
				{
					case "-a":
						// eg: -a ./Client/Temp/StagingArea/Data/Managed/Assembly-CSharp.dll
						i += 1;
						dllFileNames.Add(args[i]);
						break;
				}
			}
			if (dllFileNames.Count > 0)
				HotPatchEditor.Active(dllFileNames.ToArray());
		}

		static void Log(string log)
		{
			File.AppendAllText("HotPatchEnabler.txt", log + "\n");
			Console.WriteLine(log);
		}
	}
}
