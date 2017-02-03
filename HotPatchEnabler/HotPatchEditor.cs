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
		public static System.Action<string> Log = delegate { };


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

		public static void Active(string[] pathOfAssemblies, Func<string, string> writeAssemblyAs = null, bool processSymbols = false)
		{
			var readerParameters = new ReaderParameters { ReadSymbols = true };
			var writerParameters = new WriterParameters { WriteSymbols = true };
			IEnumerable<TypeDefinition> allTypes = null;
			foreach (var p in pathOfAssemblies)
			{
				Log("searching " + p);
				if (allTypes == null)
				{
					if (processSymbols)
						allTypes = AssemblyDefinition.ReadAssembly(p, readerParameters).Modules.SelectMany(m => m.GetTypes());
					else
						allTypes = AssemblyDefinition.ReadAssembly(p).Modules.SelectMany(m => m.GetTypes());
				}
				else
				{
					allTypes = allTypes.Union(AssemblyDefinition.ReadAssembly(p).Modules.SelectMany(m => m.GetTypes()));
				}
			}

			var allMethods = allTypes
				.Where(t => t.HasMethods)
				.SelectMany(t => t.Methods)
				.Where(m => m.HasCustomAttributes)
				.ToArray();

			// find	hub	method
			var hubs = allMethods
				.Where(m => m.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == typeof(HotPatchHubAttribute).FullName) != null).ToArray();

			MethodReference hubMethod = null;
			if (hubs.Length > 0)
			{
				hubMethod = hubs[0];
			}
			if (hubMethod == null)
			{
				Log("LuaHotPatchHub not found. It should be a public static method.");
				return;
			}

			var injectingTargets = allMethods
				.Where(m => m.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == typeof(HotPatchAttribute).ToString()) != null)
				.ToArray();

			var getMethodFromHandleMethod = (MethodInfo)MemberInfo(() => MethodInfo.GetMethodFromHandle(new RuntimeMethodHandle()));

			var pendingAssembly = new HashSet<AssemblyDefinition>();

			var hotPatchAttrName = typeof(HotPatchAttribute).ToString();

			foreach (var m in injectingTargets)
			{
				RemoveAttributes(hotPatchAttrName, m.CustomAttributes);

				var signature = m.FullName;
				Log(string.Format("Adding patching stub to \"{0}\"", signature));
				var getMethodFromHandleMethodRef = m.Module.ImportReference(getMethodFromHandleMethod);
				var objectTypeRef = m.Module.ImportReference(typeof(object));
				var objectArrayTypeRef = m.Module.ImportReference(typeof(object[]));
				var voidTypeRef = m.Module.ImportReference(typeof(void));

				var ilProcessor = m.Body.GetILProcessor();
				// https://msdn.microsoft.com/en-us/library/system.reflection.emit.opcodes(v=vs.110).aspx
				var hubMethodRef = m.Module.ImportReference(hubMethod);
				var isStatic = m.IsStatic;

				var continueCurrentMethod = ilProcessor.Create(OpCodes.Nop);
				var anchorToArguments = ilProcessor.Create(OpCodes.Ldnull);
				var anchorToRefOrOutArguments = ilProcessor.Create(OpCodes.Nop);
				var anchorToReturn = ilProcessor.Create(OpCodes.Ret);

				if (m.HasParameters)
				{
					// local var, argument array
					m.Body.Variables.Add(new VariableDefinition(objectArrayTypeRef));
				}
				// local val, ret val (last one)
				m.Body.Variables.Add(new VariableDefinition(objectTypeRef));

				var firstInstruction = ilProcessor.Create(OpCodes.Nop);
				ilProcessor.InsertBefore(m.Body.Instructions.First(), firstInstruction); // place holder

				var instructions = new[]
				{
					ilProcessor.Create(OpCodes.Ldstr, signature),
					// http://evain.net/blog/articles/2010/05/05/parameterof-propertyof-methodof/
					ilProcessor.Create(OpCodes.Ldtoken, m),
					ilProcessor.Create(OpCodes.Call, getMethodFromHandleMethodRef),
					// push	null or this
					isStatic ? ilProcessor.Create(OpCodes.Ldnull) : ilProcessor.Create(OpCodes.Ldarg_0),
					// ret value
					ilProcessor.Create(OpCodes.Ldloca_S, (byte)(m.Body.Variables.Count - 1)),
					// copy arguments to params object[]
					anchorToArguments,
					// call
					ilProcessor.Create(OpCodes.Call, hubMethodRef),
					ilProcessor.Create(OpCodes.Brfalse, continueCurrentMethod),
					// ref/out params
					anchorToRefOrOutArguments,
					// return part
					anchorToReturn,
					continueCurrentMethod
				};

				ReplaceInstruction(ilProcessor, firstInstruction, instructions);

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

				if (hasRefOrOutParameter)
				{
					var refOutInstructions = new List<Instruction>();
					for (int i = 0; i < m.Parameters.Count; ++i)
					{
						var param = m.Parameters[i];
						if (param.IsOut || param.ParameterType.IsByReference)
						{
							// ith_refOutArg = arg[i]


							// ith_refOutArg
							refOutInstructions.Add(ilProcessor.Create(OpCodes.Ldarg, i + paramStart));

							// arg
							refOutInstructions.Add(ilProcessor.Create(OpCodes.Ldloc, m.Body.Variables.Count - 2));

							// arg[i]
							refOutInstructions.Add(ilProcessor.Create(OpCodes.Ldc_I4, i));

							// (type)arg[i]
							refOutInstructions.Add(ilProcessor.Create(OpCodes.Ldelem_Ref));
							TypeReference elemType = param.ParameterType.GetElementType();
							if (elemType.IsValueType)
							{
								refOutInstructions.Add(ilProcessor.Create(OpCodes.Unbox_Any, elemType));
							}
							if (elemType.IsValueType)
							{
								refOutInstructions.Add(ilProcessor.Create(OpCodes.Stobj, elemType));
							}
							else
							{
								refOutInstructions.Add(ilProcessor.Create(OpCodes.Castclass, elemType));
								// ith_refOutArg = (type)arg[i]
								refOutInstructions.Add(ilProcessor.Create(OpCodes.Stind_Ref));
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

				pendingAssembly.Add(m.Module.Assembly);
			}

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

		static void RemoveAttributes(string attrName, Mono.Collections.Generic.Collection<CustomAttribute> customAttributes)
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
			}
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