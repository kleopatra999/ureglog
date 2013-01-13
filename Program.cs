using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace UnityVS
{
	class Program
	{
		private static void Main(string[] args)
		{
			try
			{
				if (args.Length == 0)
					Usage();

				var assets = args[0];
				if (!Directory.Exists(assets))
					Usage();

				RegisterAssemblyResolveHook();

				Run(Directory.EnumerateFiles(assets, "*.dll", SearchOption.AllDirectories).ToArray());
			}
			catch (Exception e)
			{
				Console.WriteLine("Fatal exception: {0}", e);
			}
		}

		private static void Usage()
		{
			Console.WriteLine("ureglog Assets");
			Environment.Exit(1);
		}

		private static void Run(string[] assemblies)
		{
			var resolver = AssemblyResolverFor(assemblies);

			foreach (var assembly in assemblies.Where(a => !Path.GetFileName(a).Contains("SyntaxTree.VisualStudio.Unity")))
				TryPatchAssembly(assembly, resolver);
		}

		private static IAssemblyResolver AssemblyResolverFor(IEnumerable<string> assemblies)
		{
			var resolver = new DefaultAssemblyResolver();

			foreach (var directory in assemblies.Select(a => Path.GetFullPath(Path.GetDirectoryName(a))).ToHashSet())
				resolver.AddSearchDirectory(directory);

			return resolver;
		}

		private static void TryPatchAssembly(string assembly, IAssemblyResolver resolver)
		{
			var bridge = SafeResolveBridge(resolver);
			if (bridge == null)
				throw new InvalidOperationException("The UnityVS package can not be found in the Assets folder.");

			var module = SafeReadModule(assembly, resolver);
			if (module == null)
				return;

			var calls = (from t in module.GetTypes()
						from m in t.Methods
						where m.HasBody
						from i in m.Body.Instructions
						where IsRegisterLogCallbackCall(i)
						select new { Method = m, Instruction = i }).ToArray();

			if (calls.Length == 0)
				return;

			foreach (var call in calls.ToArray())
				PatchRegisterLogCallbackCall(call.Method, call.Instruction, bridge);

			module.Write(assembly);

			Console.WriteLine("Successfully patched {0}", assembly);
		}

		private static AssemblyDefinition SafeResolveBridge(IAssemblyResolver resolver)
		{
			try
			{
				return resolver.Resolve("SyntaxTree.VisualStudio.Unity.Bridge", new ReaderParameters { AssemblyResolver = resolver });
			}
			catch (AssemblyResolutionException)
			{
				return null;
			}
		}

		private static void PatchRegisterLogCallbackCall(MethodDefinition method, Instruction instruction, AssemblyDefinition bridge)
		{
			//  replace
			//    Application.RegisterLogCallback(callback)
			//  by
			//    VisualStudioIntegration.LogCallback = (Application.LogCallback) Delegate.Combine(callback, VisualStudioIntegration.LogCallback);

			var il = method.Body.GetILProcessor();
			var module = method.Module;

			var vsi = bridge.MainModule.GetType("SyntaxTree.VisualStudio.Unity.Bridge.VisualStudioIntegration");
			var lcb = vsi.Fields.Single(f => f.Name == "LogCallback");

			var corlib = module.AssemblyResolver.Resolve((AssemblyNameReference) module.TypeSystem.Corlib);

			var dlg = corlib.MainModule.GetType("System.Delegate");
			var combine = dlg.Methods.Single(m => m.Name == "Combine" && m.Parameters.Count == 2);

			il.InsertBefore(instruction, il.Create(OpCodes.Ldsfld, module.Import(lcb)));

			foreach (var i in new[]
			{
				il.Create(OpCodes.Call, module.Import(combine)),
				il.Create(OpCodes.Castclass, module.Import(lcb.FieldType)),
				il.Create(OpCodes.Stsfld, module.Import(lcb)),
			})
			{
				il.InsertBefore(instruction, i);
			}

			il.Remove(instruction);
		}

		private static bool IsRegisterLogCallbackCall(Instruction instruction)
		{
			if (instruction.OpCode != OpCodes.Call)
				return false;

			var method = instruction.Operand as MethodReference;
			if (method == null)
				return false;

			if (method.Name != "RegisterLogCallback")
				return false;

			if (method.DeclaringType.FullName != "UnityEngine.Application")
				return false;

			return true;
		}

		private static ModuleDefinition SafeReadModule(string assembly, IAssemblyResolver resolver)
		{
			try
			{
				return ModuleDefinition.ReadModule(assembly, new ReaderParameters { AssemblyResolver = resolver });
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static void RegisterAssemblyResolveHook()
		{
			AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
			{
				var name = new AssemblyName(args.Name);
				var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name.Name);
				if (stream == null)
					return null;

				var memory = new MemoryStream((int) stream.Length);
				stream.WriteTo(memory);
				return Assembly.Load(memory.ToArray());
			};
		}
	}
}
