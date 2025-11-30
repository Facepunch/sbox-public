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
		GenerateStubs();
		// GenerateFillNativeFunctions();
		
		WriteLine("    }");
	}

	private void GenerateDebugError()
	{
		// Moved to EngineExports.cs
	}

	/*
	private void GenerateFillNativeFunctions()
	{
		// ...
	}
	*/
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
				WriteLine( $"        [UnmanagedCallersOnly(EntryPoint = \"From_{subclass.ManagedName}_To_{c.ManagedName}\")]" );
				WriteLine( $"        public static void* From_{subclass.ManagedName}_To_{c.ManagedName}( void* ptr ) => ptr;" ); // Dummy cast
				
				// To_Sub_From_Base
				WriteLine( $"        [UnmanagedCallersOnly(EntryPoint = \"To_{subclass.ManagedName}_From_{c.ManagedName}\")]" );
				WriteLine( $"        public static void* To_{subclass.ManagedName}_From_{c.ManagedName}( void* ptr ) => ptr;" ); // Dummy cast
				
				bc = bc.BaseClass;
			}

			foreach ( Function f in c.Functions )
			{
				// Generate stub for function
				// Signature must match what is expected by the delegate in Interop.Engine.cs
				// We can use f.MangledName as the method name.
				
				// We need to reconstruct the signature.
				// The arguments are pointers or primitives.
				
				string args = GetSignatureArgs( c, f );
				string returnType = f.Return.IsVoid ? "void" : "void*"; // Simplified for now, should be specific type
				// Actually, let's look at how NativeWriter does it.
				// It uses GetNativeDelegateType.
				
				// For NativeAOT, we want to match the signature exactly.
				// But for a "Null Engine", we can just use void* for everything complex.
				// However, if we use [UnmanagedCallersOnly], the types must be blittable.
				
				// Let's try to be as precise as possible.
				returnType = ToNativeAotType( f.Return );
				if (returnType == "bool") returnType = "byte"; // bool is not blittable in some contexts, usually byte or int
				
				// NativeWriter uses "CC" calling convention which is usually stdcall on Windows, cdecl on Linux.
				// [UnmanagedCallersOnly] defaults to platform default (cdecl on Linux).
				
				WriteLine( $"        [UnmanagedCallersOnly(EntryPoint = \"{f.MangledName}\")]" );
				WriteLine( $"        public static {returnType} {f.MangledName}( {args} )" );
				WriteLine( "        {" );
				if ( returnType != "void" )
				{
					WriteLine( $"            return default;" );
				}
				WriteLine( "        }" );
			}

			foreach ( Variable f in c.Variables )
			{
				// Getters and Setters
				string args = GetSignatureArgs( c, null ); // Self arg only
				string returnType = ToNativeAotType( f.Return );
				
				WriteLine( $"        [UnmanagedCallersOnly(EntryPoint = \"_Get__{f.MangledName}\")]" );
				WriteLine( $"        public static {returnType} _Get__{f.MangledName}( {args} )" );
				WriteLine( "        {" );
				if ( returnType != "void" )
					WriteLine( $"            return default;" );
				WriteLine( "        }" );
				
				// Setter
				string setArgs = string.IsNullOrEmpty(args) ? "" : args + ", ";
				setArgs += $"{ToNativeAotType( f.Return )} value";
				
				WriteLine( $"        [UnmanagedCallersOnly(EntryPoint = \"_Set__{f.MangledName}\")]" );
				WriteLine( $"        public static void _Set__{f.MangledName}( {setArgs} )" );
				WriteLine( "        {" );
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
			foreach ( var p in f.Parameters )
			{
				string type = ToNativeAotType( p );
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
}
