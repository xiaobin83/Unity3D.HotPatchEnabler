using NUnit.Framework;
using System;
using System.IO;
using System.Reflection;

namespace hotpatch
{
	[TestFixture]
	class TestHotPatch
	{
		Type MethodsToBePatchedType;
		Type ClassToBePatchedType;

		[OneTimeSetUp]
		public void TestActivePatch()
		{
			bool activated = HotPatchEditor.Active(new[] { "ToBePatched.dll" }, (name) => name + ".mod.dll", processSymbols: true);
			if (!activated)
			{
				throw new Exception("patch not activated");
			}
			var patched = Assembly.LoadFile(
				Path.Combine(
					Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
					"ToBePatched.dll.mod.dll"));
			MethodsToBePatchedType = patched.GetType("ToBePatched.MethodsToBePatched");
			ClassToBePatchedType = patched.GetType("ToBePatched.ClassToBePatched");

			var stub = patched.GetType("ToBePatched.PatchStub");
			stub.InvokeMember("Apply", BindingFlags.InvokeMethod | BindingFlags.Static | BindingFlags.Public, null, null, new object[] { });
		}

		[Test]
		public void TestMethodsToBePatchedType_PublicFunc()
		{
			var inst = Activator.CreateInstance(MethodsToBePatchedType);
			MethodsToBePatchedType.InvokeMember("PublicFunc", BindingFlags.InvokeMethod | BindingFlags.Instance | BindingFlags.Public, null, inst, new object[] { });
		}


		[Test]
		public void TestMethodsToBePatchedType_PrivateFunc()
		{
			var inst = Activator.CreateInstance(MethodsToBePatchedType);
			MethodsToBePatchedType.InvokeMember("PrivateFunc", BindingFlags.InvokeMethod | BindingFlags.Instance | BindingFlags.NonPublic, null, inst, new object[] { });
		}



	}
}
