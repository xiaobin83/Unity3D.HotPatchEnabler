using System;
using System.Reflection;
using hotpatch;
using System.Collections.Generic;

namespace ToBePatched
{
	public class Patch
	{
		internal static object GetDefaultValue(Type type)
		{
			if (type.IsValueType  && type != typeof(void))
			{
				return Activator.CreateInstance(type);
			}
			return null;
		}

		public delegate bool PatchDelegate(MethodBase method, object target, out object retVal, object[] args);

		static Dictionary<string, PatchDelegate> patches = new Dictionary<string, PatchDelegate>();

		public static void Add(string signiture, PatchDelegate method)
		{
			patches[signiture] = method;
		}

		public static void Clear()
		{
			patches.Clear();
		}

		[HotPatchHub]
		public static bool HotPatchHubDelegate(string signature, MethodBase method, object target, out object retval, params object[] args)
		{
			PatchDelegate patchedMethod;
			if (patches.TryGetValue(signature, out patchedMethod))
			{
				return patchedMethod(method, target, out retval, args);
			}
			else
			{
				if (!method.IsConstructor)
					retval = GetDefaultValue(((MethodInfo)method).ReturnType);
				else
					retval = null;
				return false;
			}
		}

	}
}
