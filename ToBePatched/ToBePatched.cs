using System;
using hotpatch;

namespace ToBePatched
{
	public class MethodsToBePatched
	{
		[HotPatch]
		public void PublicFunc()
		{
			throw new Exception("not patched");
		}

		[HotPatch]
		void PrivateFunc()
		{
			throw new Exception("not patched");
		}

		[HotPatch]
		int PriviateFuncWithRet()
		{
			throw new Exception("not patched");
		}

		[HotPatch]
		public int PublicFuncWithRet()
		{
			throw new Exception("not patched");
		}

		[HotPatch]
		public int PublicFuncWithOutAndRef(ref int a, out int b)
		{
			throw new Exception("not patched");
		}

		[HotPatch]
		public static int StaticFuncWithParam(int p)
		{
			throw new Exception("not patched");
		}
	}

	[HotPatch(PatchConstructors = true, Flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)]
	public class ClassToBePatched
	{
		public int IntField;
		public string StringField;

		int IntProp
		{
			get
			{
				throw new Exception("not patched");
			}
			set
			{
				throw new Exception("not patched");
			}
		}

		public ClassToBePatched(int value)
		{
			IntField = value;
		}

		public ClassToBePatched(string value)
		{
			StringField = value;
		}

		public int GetIntField()
		{
			return IntField;
		}
		public string GetStringField()
		{
			return StringField;
		}
		public int GetIntProp()
		{
			return IntProp;
		}

	}
}
