using System;
using System.Collections.Generic;
using System.Linq;

namespace Facepunch.InteropGen;

internal partial class NativeAotWriter
{
	private void GenerateImports()
	{
		WriteLine("    public static unsafe partial class Imports");
		WriteLine("    {");

		foreach (var c in definitions.Classes.Where(x => x.Native == false))
		{
			if (ShouldSkip(c)) continue;
			if (definitions.IsIncluded(c)) continue;

			foreach (var f in c.Functions)
			{
				// Types natifs du côté DELEGATE (false = incoming = from C++)
				IEnumerable<string> argTypes = c.SelfArg(true, f.Static)
					.Concat(f.Parameters)
					.Where(x => x.IsRealArgument)
					.Select(x => ToNativeAotDelegateType(x));

				string returnType = ToNativeAotDelegateType(f.Return);

				string signature = string.Join(", ", argTypes);

				WriteLine($"        public static delegate* unmanaged<{signature}{(signature.Length > 0 ? ", " : "")}{returnType}> _ptr_{f.MangledName};");
			}
		}
		GenerateStoreImports();
		GenerateImportImplementations();
		WriteLine("    }");
		WriteLine();
	}
	private void GenerateStoreImports()
	{
		foreach (var c in definitions.Classes.Where(x => x.Native == false))
		{
			if (ShouldSkip(c)) continue;
			if (definitions.IsIncluded(c)) continue;

			foreach (var f in c.Functions)
			{
				IEnumerable<string> argTypes = c.SelfArg(true, f.Static)
					.Concat(f.Parameters)
					.Where(x => x.IsRealArgument)
					.Select(x => ToNativeAotDelegateType(x));

				string returnType = ToNativeAotDelegateType(f.Return);
				string signature = string.Join(", ", argTypes);
				if (!string.IsNullOrWhiteSpace(signature)) signature += ", ";

				WriteLine($"        public static void StoreImport_{f.MangledName}(void* ptr)");
				WriteLine($"        {{");
				WriteLine($"            _ptr_{f.MangledName} = (delegate* unmanaged<{signature}{returnType}>)ptr;");
				WriteLine($"        }}");
			}
		}
		WriteLine();
	}

	private void GenerateImportImplementations()
	{
		foreach (var c in definitions.Classes.Where(x => x.Native == false)
											.OrderByDescending(x => x.ClassDepth))
		{
			if (ShouldSkip(c)) continue;
			if (definitions.IsIncluded(c)) continue;

			WriteLine($"// {c.ManagedNameWithNamespace}");

			foreach (var f in c.Functions)
			{
				string args = GetSignatureArgsForDelegate(c, f);
				string returnType = ToNativeAotType(f.Return);

				WriteLine($"[UnmanagedCallersOnly(EntryPoint = \"{f.MangledName}\")]");
				WriteLine($"public static {returnType} {f.MangledName}( {args} )");
				WriteLine("{");

				// construction des args pour passer à Imports::<fn>
				var callArgs = new List<string>();
				if (!c.Static && !f.Static)
					callArgs.Add("self");

				foreach (var p in f.Parameters)
				{
					// Just pass the parameter name directly - signatures already match
					callArgs.Add(p.Name);
				}

				string argCall = string.Join(", ", callArgs);

				if (returnType != "void")
				{
					// Only apply bool conversion if delegate returns byte but managed expects bool
					string delegateReturnType = ToNativeAotDelegateType(f.Return);
					if (f.Return.ManagedType == "bool" && delegateReturnType == "byte")
					{
						WriteLine($"    return Imports._ptr_{f.MangledName}( {argCall} ) != 0;");
					}
					else
					{
						WriteLine($"    return ({returnType})Imports._ptr_{f.MangledName}( {argCall} );");
					}
				}
				else
				{
					WriteLine($"    Imports._ptr_{f.MangledName}( {argCall} );");
				}

				WriteLine("}");
				WriteLine();
			}
		}
	}

	private string ToNativeAotDelegateType(Arg arg)
	{
		// Le type natif brut
		string type = arg.GetNativeDelegateType(false);

		//
		// POINTERS
		//
		if (type.Contains("*"))
			return "void*";

		//
		// CSTRING
		//
		if (type is "char*" or "const char*")
			return "byte*";

		//
		// PRIMITIFS
		//
		return type switch
		{
			"bool"   => "byte",   // important: bool non-blittable → byte
			"int8"   => "sbyte",
			"uint8"  => "byte",
			"int16"  => "short",
			"uint16" => "ushort",
			"int32"  => "int",
			"uint32" => "uint",
			"int64"  => "long",
			"uint64" => "ulong",
			"float"  => "float",
			"double" => "double",

			// Structures ou types inconnus → pointeur
			_ => "void*"
		};
	}

	private string CastFromNative(string type, string value)
	{
		if (type == "byte" /* bool */) return $"({value} != 0)";
		return $"({type}){value}";
	}

	private string GetSignatureArgsForDelegate(Class c, Function f)
	{
		var args = new List<string>();
		
		if (!c.Static && !f.Static)
		{
			args.Add("void* self");
		}
		
		int i = 0;
		foreach (var p in f.Parameters)
		{
			string type = ToNativeAotDelegateType(p); // Use delegate type instead of managed type
			string name = p.Name;
			if (name == "lock" || name == "string" || name == "int" || name == "out" || name == "ref" || name == "base" || name == "object") name = "@" + name;
			if (string.IsNullOrEmpty(name)) name = $"arg{i}";
			
			args.Add($"{type} {name}");
			i++;
		}
		
		return string.Join(", ", args);
	}
}
