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

	[HotPatch(Flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)]
	public class ClassToBePatched
	{
		int IntField = 20;

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

		void PriviateFunc()
		{
			throw new Exception("not patched");
		}

		int PriviateFuncWithRet()
		{
			throw new Exception("not patched");
		}

		public int PublicFuncWithRet()
		{
			throw new Exception("not patched");
		}

		public int PublicFuncWithOutAndRef(ref int a, out int b)
		{
			throw new Exception("not patched");
		}

		public static int StaticFuncWithParam(int p)
		{
			throw new Exception("not patched");
		}
	}
}
