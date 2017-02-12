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
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Reflection;


namespace hotpatch
{
	public class HotPatchEditor
	{
		public static System.Action<string> Log = (msg) => Console.WriteLine(msg);
		public static System.Action<string> LogError = Log;


		// http://stackoverflow.com/a/9469697/84998
		static MemberInfo MemberInfoCore(Expression body, ParameterExpression param)
		{
			if (body.NodeType == ExpressionType.MemberAccess)
			{
				var bodyMemberAccess = (MemberExpression)body;
				return bodyMemberAccess.Member;
			}
			else if (body.NodeType == ExpressionType.Call)
			{
				var bodyMemberAccess = (MethodCallExpression)body;
				return bodyMemberAccess.Method;
			}
			else throw new NotSupportedException();
		}

		static MemberInfo MemberInfo<T1>(Expression<Func<T1>> memberSelectionExpression)
		{
			if (memberSelectionExpression == null) throw new ArgumentNullException("memberSelectionExpression");
			return MemberInfoCore(memberSelectionExpression.Body, null/*param*/);
		}

		// patch class
		// after patched, all HotPatchAttribute will be removed
		public static HashSet<AssemblyDefinition> PatchClasses(IEnumerable<TypeDefinition> classesToPatch, MethodReference hubMethod)
		{
			var patchedAssembly = new HashSet<AssemblyDefinition>();
			foreach (var c in classesToPatch)
			{
				Log("Patching class " + c.FullName);
				if (PatchClass(c, hubMethod))
				{
					patchedAssembly.Add(c.Module.Assembly);
				}
			}
			return patchedAssembly;
		}

		static MethodInfo getMethodFromHandleMethod_;
		static MethodInfo getMethodFromHandleMethod
		{
			get
			{
				if (getMethodFromHandleMethod_ == null)
				{
					getMethodFromHandleMethod_ = (MethodInfo)MemberInfo(() => MethodInfo.GetMethodFromHandle(new RuntimeMethodHandle()));
				}
				return getMethodFromHandleMethod_;
			}
		}

		static string hotPatchAttrName
		{
			get
			{
				return typeof(HotPatchAttribute).FullName;
			}
		}

		static bool PatchMethod(MethodDefinition m, MethodReference hubMethod, bool patchConstructor)
		{
			RemoveAttributes(hotPatchAttrName, m.CustomAttributes);

			if (m.IsConstructor && !patchConstructor)
				return false;

			var signature = m.FullName;
			Log(string.Format("Adding patching stub to \"{0}\"", signature));

			// import required stuff
			var getMethodFromHandleMethodRef = m.Module.Import(getMethodFromHandleMethod);
			var objectTypeRef = m.Module.Import(typeof(object));
			var objectArrayTypeRef = m.Module.Import(typeof(object[]));
			var voidTypeRef = m.Module.Import(typeof(void));

			var ilProcessor = m.Body.GetILProcessor();
			// https://msdn.microsoft.com/en-us/library/system.reflection.emit.opcodes(v=vs.110).aspx
			var hubMethodRef = m.Module.Import(hubMethod);
			var isStatic = m.IsStatic;

			var continueCurrentMethod = ilProcessor.Create(OpCodes.Nop);
			var anchorToArguments = ilProcessor.Create(OpCodes.Ldnull);
			var anchorToRefOrOutArguments = ilProcessor.Create(OpCodes.Nop);
			var anchorToReturn = ilProcessor.Create(OpCodes.Ret);

			var anchorToReturnPart = ilProcessor.Create(OpCodes.Nop);

			Instruction[] retPartInstructions;

			if (m.IsConstructor)
			{
				retPartInstructions = new []
				{
					ilProcessor.Create(OpCodes.Brfalse, continueCurrentMethod),
					anchorToRefOrOutArguments,
					continueCurrentMethod,
				};
			}
			else
			{
				retPartInstructions = new[]
				{
					ilProcessor.Create(OpCodes.Brfalse, continueCurrentMethod),
					// ref/out params
					anchorToRefOrOutArguments,
					anchorToReturn,
					continueCurrentMethod
				};

			}



			if (m.HasParameters)
			{
				// local var, argument array
				m.Body.Variables.Add(new VariableDefinition(objectArrayTypeRef));
			}
			// local val, ret val (last one)
			m.Body.Variables.Add(new VariableDefinition(objectTypeRef));

			var firstInstruction = ilProcessor.Create(OpCodes.Nop);
			ilProcessor.InsertBefore(m.Body.Instructions.First(), firstInstruction); // place holder

			var anchorToMethodOf = ilProcessor.Create(OpCodes.Nop);
			Instruction[] methodOfInstructions;
			methodOfInstructions = new [] 
			{
				ilProcessor.Create(OpCodes.Ldtoken, m),
				ilProcessor.Create(OpCodes.Call, getMethodFromHandleMethodRef),
			};
			var instructions = new[]
			{
				ilProcessor.Create(OpCodes.Ldstr, signature),
				// http://evain.net/blog/articles/2010/05/05/parameterof-propertyof-methodof/
				anchorToMethodOf,
				// push	null or this
				isStatic ? ilProcessor.Create(OpCodes.Ldnull) : ilProcessor.Create(OpCodes.Ldarg_0),
				// ret value
				ilProcessor.Create(OpCodes.Ldloca_S, (byte)(m.Body.Variables.Count - 1)),
				// copy arguments to params object[]
				anchorToArguments,
				// call
				ilProcessor.Create(OpCodes.Call, hubMethodRef),

				anchorToReturnPart,
			};
			ReplaceInstruction(ilProcessor, firstInstruction, instructions);
			ReplaceInstruction(ilProcessor, anchorToMethodOf, methodOfInstructions);
			ReplaceInstruction(ilProcessor, anchorToReturnPart, retPartInstructions);

			var paramStart = 0;
			if (!isStatic)
			{
				paramStart = 1;
			}

			// process arguments
			bool hasRefOrOutParameter = false;
			if (m.HasParameters)
			{
				var paramsInstructions = new List<Instruction>()
				{
					ilProcessor.Create(OpCodes.Ldc_I4, m.Parameters.Count),
					ilProcessor.Create(OpCodes.Newarr, objectTypeRef),
					ilProcessor.Create(OpCodes.Dup),
					ilProcessor.Create(OpCodes.Stloc, m.Body.Variables.Count - 2)
				};

				for (int i = 0; i < m.Parameters.Count; ++i)
				{
					var param = m.Parameters[i];
					if (param.IsOut)
					{
						// placeholder for outs
						hasRefOrOutParameter = true;
					}
					else
					{
						paramsInstructions.Add(ilProcessor.Create(OpCodes.Dup));
						paramsInstructions.Add(ilProcessor.Create(OpCodes.Ldc_I4, i));
						paramsInstructions.Add(ilProcessor.Create(OpCodes.Ldarg, i + paramStart));
						if (param.ParameterType.IsByReference)
						{
							hasRefOrOutParameter = true;

							var elemType = param.ParameterType.GetElementType();

							if (elemType.IsValueType)
							{
								paramsInstructions.Add(ilProcessor.Create(OpCodes.Ldobj, elemType));
							}
							else
							{
								paramsInstructions.Add(ilProcessor.Create(OpCodes.Ldind_Ref));
							}

							if (elemType.IsValueType)
							{
								paramsInstructions.Add(ilProcessor.Create(OpCodes.Box, elemType));
							}

							paramsInstructions.Add(ilProcessor.Create(OpCodes.Stelem_Ref));
						}
						else
						{
							if (param.ParameterType.IsPrimitive)
							{
								paramsInstructions.Add(ilProcessor.Create(OpCodes.Box, param.ParameterType));
							}
							paramsInstructions.Add(ilProcessor.Create(OpCodes.Stelem_Ref));
						}
					}
				}
				ReplaceInstruction(ilProcessor, anchorToArguments, paramsInstructions);
			}

			if (hasRefOrOutParameter || m.IsConstructor)
			{
				var refOutInstructions = new List<Instruction>();
				for (int i = 0; i < m.Parameters.Count; ++i)
				{
					var param = m.Parameters[i];
					if (param.IsOut || param.ParameterType.IsByReference || m.IsConstructor)
					{
						// ith_refOutArg = arg[i]


						// ith_refOutArg
						if (param.IsOut || param.ParameterType.IsByReference)
							refOutInstructions.Add(ilProcessor.Create(OpCodes.Ldarg, i + paramStart));

						// arg
						refOutInstructions.Add(ilProcessor.Create(OpCodes.Ldloc, m.Body.Variables.Count - 2));

						// arg[i]
						refOutInstructions.Add(ilProcessor.Create(OpCodes.Ldc_I4, i));

						// (type)arg[i]
						refOutInstructions.Add(ilProcessor.Create(OpCodes.Ldelem_Ref));

						// ith_refOutArg = (type)arg[i]
						TypeReference elemType;
						elemType = param.ParameterType.GetElementType();
						if (elemType.IsValueType)
						{
							refOutInstructions.Add(ilProcessor.Create(OpCodes.Unbox_Any, elemType));
							if (param.IsOut || param.ParameterType.IsByReference)
								refOutInstructions.Add(ilProcessor.Create(OpCodes.Stobj, elemType));
							else
								refOutInstructions.Add(ilProcessor.Create(OpCodes.Starg, i + paramStart));
						}
						else
						{
							refOutInstructions.Add(ilProcessor.Create(OpCodes.Castclass, elemType));
							if (param.IsOut || param.ParameterType.IsByReference)
								refOutInstructions.Add(ilProcessor.Create(OpCodes.Stind_Ref));
							else
								refOutInstructions.Add(ilProcessor.Create(OpCodes.Starg, i + paramStart));
						}
					}
				}
				ReplaceInstruction(ilProcessor, anchorToRefOrOutArguments, refOutInstructions);
			}


			// process return
			if (m.ReturnType.FullName != voidTypeRef.FullName)
			{
				var retInstructions = new List<Instruction>();
				retInstructions.Add(ilProcessor.Create(OpCodes.Ldloc, m.Body.Variables.Count - 1));
				if (m.ReturnType.IsPrimitive)
				{
					retInstructions.Add(ilProcessor.Create(OpCodes.Unbox_Any, m.ReturnType));
				}
				else
				{
					retInstructions.Add(ilProcessor.Create(OpCodes.Castclass, m.ReturnType));
				}
				retInstructions.Add(ilProcessor.Create(OpCodes.Ret));
				ReplaceInstruction(ilProcessor, anchorToReturn, retInstructions);
			}

			return true;
		}


		static bool PatchMethods(IEnumerable<MethodDefinition> methods, MethodReference hubMethod, HashSet<AssemblyDefinition> pendingAssembly, bool patchConstructors)
		{
			var patched = false;
			foreach (var m in methods)
			{
				if (PatchMethod(m, hubMethod, patchConstructors))
				{
					if (pendingAssembly != null)
						pendingAssembly.Add(m.Module.Assembly);
				}
			}
			return patched;
		}

		static bool PatchClass(TypeDefinition c, MethodReference hubMethod)
		{
			var attr = c.CustomAttributes.First(a => a.AttributeType.FullName == hotPatchAttrName);
			BindingFlags searchFlags = HotPatchAttribute.DefaultFlags;
			var patchConstructors = HotPatchAttribute.DefaultPatchConstructors;
			if (attr.HasFields)
			{
				try
				{
					var flagsField = attr.Fields.First(f => f.Name == "Flags");
					searchFlags = (BindingFlags)flagsField.Argument.Value;
				}
				catch { }

				try
				{
					var patchConstructorsField = attr.Fields.First(f => f.Name == "PatchConstructors");
					patchConstructors = (bool)patchConstructorsField.Argument.Value;
				}
				catch { }
			}

			var patched = false;
			if (c.HasMethods)
			{
				PatchMethods(c.Methods, hubMethod, null, patchConstructors);
				patched	= true;
			}

			RemoveAttributes(hotPatchAttrName, c.CustomAttributes);
			return patched;
		}

		static MethodInfo GetMethodInfoOfDelegate(Type d)
		{
			return d.GetMethod("Invoke");
		}

		public static MethodReference SearchHubMethod(IEnumerable<TypeDefinition> allTypes, HashSet<AssemblyDefinition> pendingAssemblies)
		{
			// find	hub	methods
			var hubs = allTypes
				.Where(t => t.HasMethods)
				.SelectMany(t => t.Methods)
				.Where(m =>	m.HasCustomAttributes && m.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == typeof(HotPatchHubAttribute).FullName) != null)
				.ToArray();
			MethodDefinition hubMethod = null;
			if (hubs.Length > 0)
			{
				hubMethod = hubs[0];
			}
			if (hubMethod == null)
			{
				throw new Exception("cannot find hub method. check HotPatchHubAttribute.");
			}
			Log("using " + hubMethod.FullName + " as HotPatchHub.");

			var hotPatchHubAttrName = typeof(HotPatchHubAttribute).FullName;
			foreach (var h in hubs)
			{
				if (RemoveAttributes(hotPatchHubAttrName, h.CustomAttributes))
				{
					pendingAssemblies.Add(h.Module.Assembly);
				}
			}

			if (hubMethod.HasThis
				|| hubMethod.IsPrivate)
			{
				throw new Exception("hub method should be a public static method. while " + hubMethod.FullName + " is not.");
			}

			var method = GetMethodInfoOfDelegate(typeof(HotPatchHubDelegate));

			// check return	type
			if (method.ReturnType.FullName != hubMethod.ReturnType.FullName)
			{
				return null;
			}

			// check parameters
			var parameters = method.GetParameters();
			var hubParameters = hubMethod.Parameters.ToArray();
			if (parameters.Length != hubParameters.Length)
			{
				throw new Exception("hub method have different signature than " + method.ToString());
			}
			for (int i = 0; i < parameters.Length; ++i)
			{
				var pa = parameters[i];
				var hubPa = hubParameters[i];
				if (pa.ParameterType.FullName != hubPa.ParameterType.FullName)
				{
					throw new Exception("hub method has different parameter type at index " + i);
				}
			}
			return hubMethod;
		}

		static IEnumerable<TypeDefinition> LoadAllTypes(string[] pathOfAssemblies, ReaderParameters readerParameters)
		{
			IEnumerable<TypeDefinition> allTypes = null;
			foreach (var p in pathOfAssemblies)
			{
				if (allTypes == null)
				{
					if (readerParameters != null)
						allTypes = AssemblyDefinition.ReadAssembly(p, readerParameters).Modules.SelectMany(m => m.GetTypes()).ToArray();
					else
						allTypes = AssemblyDefinition.ReadAssembly(p).Modules.SelectMany(m => m.GetTypes()).ToArray();
				}
				else
				{
					allTypes = allTypes.Union(AssemblyDefinition.ReadAssembly(p).Modules.SelectMany(m => m.GetTypes())).ToArray();
				}
			}
			return allTypes;
		}

		public static void Active(string[] pathOfAssemblies, Func<string, string> writeAssemblyAs = null, bool processSymbols = false)
		{
			var readerParameters = new ReaderParameters { ReadSymbols = true };
			var writerParameters = new WriterParameters { WriteSymbols = true };
			IEnumerable<TypeDefinition> allTypes = LoadAllTypes(pathOfAssemblies, processSymbols ? readerParameters : null);

			var pendingAssembly = new HashSet<AssemblyDefinition>();

			// find	hub	methods
			MethodReference hubMethod = SearchHubMethod(allTypes, pendingAssembly);

			// patch class
			var classesToPatch = allTypes.Where(t => t.HasCustomAttributes && t.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == typeof(HotPatchAttribute).FullName) != null);
			var patched = PatchClasses(classesToPatch, hubMethod);
			pendingAssembly.UnionWith(patched);
			classesToPatch = null;

			// patch methods
			var injectingTargets = allTypes
				.Where(t =>	t.HasMethods)
				.SelectMany(t => t.Methods)
				.Where(m => m.HasCustomAttributes && m.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == typeof(HotPatchAttribute).ToString()) != null);

			PatchMethods(injectingTargets, hubMethod, pendingAssembly, patchConstructors: false);

			foreach (var a in pendingAssembly)
			{
				var path = pathOfAssemblies.First(s => s.Contains(a.MainModule.Name));
				if (writeAssemblyAs != null) path = writeAssemblyAs(path);
				if (processSymbols)
					a.Write(path, writerParameters);
				else
					a.Write(path);
			}

			pathOfAssemblies = null;
		}

		static bool RemoveAttributes(string attrName, Mono.Collections.Generic.Collection<CustomAttribute> customAttributes)
		{
			int index = -1;
			for (var i = 0; i < customAttributes.Count; i++)
			{
				var attr = customAttributes[i];
				if (attr.Constructor != null && attr.Constructor.DeclaringType.FullName == attrName)
				{
					index = i;
					break;
				}
			}
			if (index != -1)
			{
				customAttributes.RemoveAt(index);
				return true;
			}
			return false;
		}

		static void ReplaceInstruction(ILProcessor ilProcessor, Instruction anchorInstruction, IEnumerable<Instruction> instructions)
		{
			bool firstOne = true;
			foreach (var ins in instructions)
			{
				if (firstOne)
				{
					ilProcessor.Replace(anchorInstruction, ins);
					firstOne = false;
				}
				else
				{
					ilProcessor.InsertAfter(anchorInstruction, ins);
				}
				anchorInstruction = ins;
			}
		}



	}
}