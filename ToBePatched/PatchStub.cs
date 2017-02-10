using System;
using System.Reflection;

namespace ToBePatched
{
	public class PatchStub
	{
		public static void Apply()
		{
			ToBePatched.Patch.Clear();

			ToBePatched.Patch.Add(
				"System.Void ToBePatched.MethodsToBePatched::PublicFunc()",
				delegate (MethodInfo method, object target, out object retVal, object[] args)
				{
					retVal = Patch.GetDefaultValue(method.ReturnType);
					return true;
				});


			ToBePatched.Patch.Add(
				"System.Void ToBePatched.MethodsToBePatched::PrivateFunc()",
				delegate (MethodInfo method, object target, out object retVal, object[] args)
				{
					retVal = Patch.GetDefaultValue(method.ReturnType);
					return true;
				});


			ToBePatched.Patch.Add(
				"System.Int32 ToBePatched.MethodsToBePatched::PriviateFuncWithRet()",
				delegate (MethodInfo method, object target, out object retVal, object[] args)
				{
					retVal = 10;
					return true;
				});


			ToBePatched.Patch.Add(
				"System.Int32 ToBePatched.MethodsToBePatched::PublicFuncWithRet()",
				delegate (MethodInfo method, object target, out object retVal, object[] args)
				{
					retVal = 20;
					return true;
				});


			ToBePatched.Patch.Add(
				"System.Int32 ToBePatched.MethodsToBePatched::PublicFuncWithOutAndRef(System.Int32&,System.Int32&)",
				delegate (MethodInfo method, object target, out object retVal, object[] args)
				{
					if ((int)args[0] != 10)
						throw new Exception("wrong argument");
					args[0] = 10;
					args[1] = 20;
					retVal = 30;
					return true;
				});

			ToBePatched.Patch.Add(
				"System.Int32 ToBePatched.MethodsToBePatched::StaticFuncWithParam(System.Int32)",
				delegate (MethodInfo method, object target, out object retVal, object[] args)
				{
					retVal = (int)args[0];
					return true;
				});
		}
	}
}
