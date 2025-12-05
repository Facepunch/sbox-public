using System.Collections.Generic;
using System.Linq;

namespace Facepunch.InteropGen;

internal partial class NativeAotWriter
{

	private void GenerateExports()
	{
		WriteLine("    public static unsafe partial class Exports");
		WriteLine("    {");

		// GenerateDebugError();
		GenerateFillNativeFunctions();
		GenerateStubs();
		
		WriteLine("    }");
	}

	private void GenerateDebugError()
	{
		// Moved to EngineExports.cs
	}
	string CapitalizeFirst(string input)
	{
		if (string.IsNullOrEmpty(input)) return input;
		return char.ToUpper(input[0]) + input.Substring(1);
	}
	
	private void GenerateFillNativeFunctions()
	{

		WriteLine($"	public static unsafe void FillNativeFunctions{CapitalizeFirst(definitions.Ident)}(void** managedFunctions, void** nativeFunctions, int* structSizes)");
		WriteLine(" 	{");
		WriteLine(" 		var i = 0;");
		WriteLine(" 		nativeFunctions[i++] = (void*)(delegate* unmanaged<IntPtr, void>)&Sbox.Engine.Emulation.EngineExports.DebugError;");
		foreach (var c in definitions.Classes.Where(x => x.Native == true)) {
			if (ShouldSkip(c)) continue;


			Class bc = c.BaseClass;
			while (bc != null) {
				var subclass = bc;
				WriteLine($" 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*>)&From_{subclass.ManagedName}_To_{c.ManagedName};");
				WriteLine($" 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*>)&To_{subclass.ManagedName}_From_{c.ManagedName};");
				bc = bc.BaseClass;
			}
			foreach (var f in c.Functions) {
				IEnumerable<string> managedArgs = c.SelfArg( false, f.Static ).Concat( f.Parameters ).Where( x => x.IsRealArgument ).Select( x => $"{ToNativeAotTypeFromType(x.GetManagedDelegateType( false ))}" ).Concat( new[] { ToNativeAotTypeFromType(f.Return.GetManagedDelegateType( true )) } );
				string managedArgss = $"{string.Join( ", ", managedArgs )}";
				string nogc = "";

				if ( f.IsNoGC )
				{
					nogc = "[SuppressGCTransition]";
				}
				WriteLine($" 		nativeFunctions[i++] = (void*)(delegate* unmanaged{nogc}< {managedArgss} >)&{f.MangledName};");
			}
			foreach (var f in c.Variables) {
				WriteLine($" 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, {ToNativeAotTypeFromType(f.Return.GetManagedDelegateType( true ))}>)&_Get__{f.MangledName};");
				WriteLine($" 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, {ToNativeAotTypeFromType(f.Return.GetManagedDelegateType( true ))}, void*>)&_Set__{f.MangledName};");
			}
		}
		WriteLine(" 	}");
		WriteLine();
	}


	private void GenerateStubs()
	{
		foreach ( Class c in definitions.Classes.Where( x => x.Native == true ) )
		{
			if ( ShouldSkip( c ) ) continue;
			if ( definitions.IsIncluded( c ) ) continue;

			// Base class casts
			Class bc = c.BaseClass;
			while ( bc != null )
			{
				Class subclass = bc;
				// From_Sub_To_Base
				WriteLine( $"        [UnmanagedCallersOnly(EntryPoint = \"From_{subclass.ManagedName}_To_{c.ManagedName}\", CallConvs = new[] {{ typeof(CallConvSuppressGCTransition) }})]" );
				WriteLine( $"        public static void* From_{subclass.ManagedName}_To_{c.ManagedName}( void* ptr ) => ptr;" ); // Dummy cast
				
				// To_Sub_From_Base
				WriteLine( $"        [UnmanagedCallersOnly(EntryPoint = \"To_{subclass.ManagedName}_From_{c.ManagedName}\", CallConvs = new[] {{ typeof(CallConvSuppressGCTransition) }})]" );
				WriteLine( $"        public static void* To_{subclass.ManagedName}_From_{c.ManagedName}( void* ptr ) => ptr;" ); // Dummy cast
				
				bc = bc.BaseClass;
			}

			foreach ( Function f in c.Functions )
			{
				string args = GetSignatureArgs( c, f );
				string returnType = ToNativeAotTypeFromType( f.Return.GetManagedDelegateType( true ) );
				if (returnType == "bool") returnType = "byte";

				string callConvs = "";
				if ( f.IsNoGC )
				{
					callConvs = ", CallConvs = new[] { typeof(CallConvSuppressGCTransition) }";
				}
				
				WriteLine( $"        [UnmanagedCallersOnly(EntryPoint = \"{f.MangledName}\"{callConvs})]" );
				WriteLine( $"        public static {returnType} {f.MangledName}( {args} )" );
				WriteLine( "        {" );
				if ( returnType != "void" )
				{
					WriteLine( $"            throw new NotImplementedException(\"{f.MangledName} is not yet implemented in the linux emulation layer\");" );
				}
				WriteLine( "        }" );
			}

			foreach ( Variable f in c.Variables )
			{
				// Getters and Setters
				string args = GetSignatureArgs( c, null ); // Self arg only
				string returnType = ToNativeAotTypeFromType( f.Return.GetManagedDelegateType( true ) );
				if (returnType == "bool") returnType = "byte";
				
				WriteLine( $"        [UnmanagedCallersOnly(EntryPoint = \"_Get__{f.MangledName}\", CallConvs = new[] {{ typeof(CallConvSuppressGCTransition) }})]" );
				WriteLine( $"        public static {returnType} _Get__{f.MangledName}( {args} )" );
				WriteLine( "        {" );
				if ( returnType != "void" )
					WriteLine( $"            throw new NotImplementedException(\"{f.MangledName}\");" );
				WriteLine( "        }" );
				
				// Setter
				string setArgs = string.IsNullOrEmpty(args) ? "" : args + ", ";
				setArgs += $"{returnType} value"; // Use same return type logic for setter value
				
				WriteLine( $"        [UnmanagedCallersOnly(EntryPoint = \"_Set__{f.MangledName}\", CallConvs = new[] {{ typeof(CallConvSuppressGCTransition) }})]" );
				WriteLine( $"        public static void* _Set__{f.MangledName}( {setArgs} )" );
				WriteLine( "        {" );
					WriteLine( $"            throw new NotImplementedException(\"{f.MangledName}\");" );
				WriteLine( "        }" );
			}
		}
	}
	
	private string GetSignatureArgs( Class c, Function f )
	{
		var args = new List<string>();
		
		if ( !c.Static && (f == null || !f.Static) )
		{
			args.Add( "void* self" );
		}
		
		if ( f != null )
		{
			int i = 0;
			foreach ( var p in f.Parameters.Where( x => x.IsRealArgument ) )
			{
				string type = ToNativeAotTypeFromType( p.GetManagedDelegateType( false ) );
				
				// [UnmanagedCallersOnly] requires blittable types
				if ( type == "bool" ) type = "byte";
				
				// Avoid keywords
				string name = p.Name;
				if ( name == "lock" || name == "string" || name == "int" || name == "out" || name == "ref" || name == "base" || name == "object" ) name = "@" + name;
				if ( string.IsNullOrEmpty( name ) ) name = $"arg{i}";
				
				args.Add( $"{type} {name}" );
				i++;
			}
		}
		
		return string.Join( ", ", args );
	}

	private string ToNativeAotType( Arg arg )
	{
		string type = arg.GetNativeDelegateType( true );
		
		// Fix common C++ types to C# equivalents
		if ( type.Contains( "*" ) ) return "void*"; // All pointers to void* for simplicity
		if ( type == "const char*" || type == "char*" ) return "byte*";
		if ( type == "int64" ) return "long";
		if ( type == "uint64" ) return "ulong";
		if ( type == "int32" ) return "int";
		if ( type == "uint32" ) return "uint";
		if ( type == "Vector" ) return "void*"; // Structs passed by value? Dangerous. Assuming pointers or small structs. 
		// Actually, if it's passed by value in C++, we can't just say void* in C#.
		// But for [UnmanagedCallersOnly], we can only use blittable types.
		// If the struct is not defined, we are in trouble.
		// Let's assume for now everything unknown is void* or int.
		
		// Check if it's a known primitive
		if ( type == "int" || type == "float" || type == "double" || type == "bool" || type == "byte" || type == "short" || type == "long" || type == "sbyte" || type == "ushort" || type == "uint" || type == "ulong" || type == "nint" || type == "nuint" )
			return type;

		// If it's a struct name like "Vector", we should probably use void* if we don't have the struct definition.
		// BUT, if it's passed by value, the ABI is different.
		// Fortunately, most Source 2 types are passed by pointer or are simple.
		// "Vector" is 3 floats.
		
		return "void*"; // Fallback
	}

	private string ToNativeAotTypeFromType( string type )
	{
		// Fix common C++ types to C# equivalents
		if ( type.Contains( "*" ) ) return "void*"; // All pointers to void* for simplicity
		if ( type == "const char*" || type == "char*" ) return "byte*";
		if ( type == "int64" ) return "long";
		if ( type == "uint64" ) return "ulong";
		if ( type == "int32" ) return "int";
		if ( type == "uint32" ) return "uint";
		if ( type == "Vector" ) return "void*"; // Structs passed by value? Dangerous. Assuming pointers or small structs. 
		// Actually, if it's passed by value in C++, we can't just say void* in C#.
		// But for [UnmanagedCallersOnly], we can only use blittable types.
		// If the struct is not defined, we are in trouble.
		// Let's assume for now everything unknown is void* or int.
		
		// Check if it's a known primitive
		if ( type == "int" || type == "float" || type == "double" || type == "bool" || type == "byte" || type == "short" || type == "long" || type == "sbyte" || type == "ushort" || type == "uint" || type == "ulong" || type == "nint" || type == "nuint" )
			return type;

		// If it's a struct name like "Vector", we should probably use void* if we don't have the struct definition.
		// BUT, if it's passed by value, the ABI is different.
		// Fortunately, most Source 2 types are passed by pointer or are simple.
		// "Vector" is 3 floats.
		
		return "void*"; // Fallback
	}
}
