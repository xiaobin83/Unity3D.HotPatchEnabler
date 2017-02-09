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
		}
	}
}
