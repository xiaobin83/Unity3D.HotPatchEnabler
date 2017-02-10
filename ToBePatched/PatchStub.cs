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
				delegate (MethodBase method_, object target, out object retVal, object[] args)
				{
					var method = (MethodInfo)method_;
					retVal = Patch.GetDefaultValue(method.ReturnType);
					return true;
				});


			ToBePatched.Patch.Add(
				"System.Void ToBePatched.MethodsToBePatched::PrivateFunc()",
				delegate (MethodBase method_, object target, out object retVal, object[] args)
				{
					var method = (MethodInfo)method_;
					retVal = Patch.GetDefaultValue(method.ReturnType);
					return true;
				});


			ToBePatched.Patch.Add(
				"System.Int32 ToBePatched.MethodsToBePatched::PriviateFuncWithRet()",
				delegate (MethodBase method, object target, out object retVal, object[] args)
				{
					retVal = 10;
					return true;
				});


			ToBePatched.Patch.Add(
				"System.Int32 ToBePatched.MethodsToBePatched::PublicFuncWithRet()",
				delegate (MethodBase method, object target, out object retVal, object[] args)
				{
					retVal = 20;
					return true;
				});


			ToBePatched.Patch.Add(
				"System.Int32 ToBePatched.MethodsToBePatched::PublicFuncWithOutAndRef(System.Int32&,System.Int32&)",
				delegate (MethodBase method, object target, out object retVal, object[] args)
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
				delegate (MethodBase method, object target, out object retVal, object[] args)
				{
					retVal = (int)args[0];
					return true;
				});

			// ==================

			ToBePatched.Patch.Add(
				"System.Void ToBePatched.ClassToBePatched::.ctor(System.Int32)",
				delegate (MethodBase method_, object target, out object retVal, object[] args)
				{
					retVal = null;
					args[0] = (int)args[0] + 10;
					return true;
				});

			ToBePatched.Patch.Add(
				"System.Void ToBePatched.ClassToBePatched::.ctor(System.String)",
				delegate (MethodBase method_, object target, out object retVal, object[] args)
				{
					retVal = null;
					args[0] = "world";
					return true;
				});

			ToBePatched.Patch.Add(
				"System.Int32 ToBePatched.ClassToBePatched::GetIntField()",
				delegate (MethodBase method_, object target, out object retVal, object[] args)
				{
					var t = (ClassToBePatched)target;
					retVal = t.IntField * 2;
					return true;
				});
		}
	}
}
