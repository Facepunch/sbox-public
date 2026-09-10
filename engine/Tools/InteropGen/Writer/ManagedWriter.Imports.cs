using System.Collections.Generic;
using System.Linq;

namespace Facepunch.InteropGen;

internal partial class ManagedWriter
{
	/// <summary>
	/// The nested static class on each wrapper that holds the native function pointers.
	/// </summary>
	private const string InternalNative = "__N";

	private static string SelfNullCheck( string className, string memberName )
	{
		return $"if ( self == IntPtr.Zero ) Sandbox.Interop.ThrowNullSelf( \"{className}\", \"{memberName}\" );";
	}

	/// <summary>
	/// Emit a managed wrapper for every native class: a struct (or static class) holding the native
	/// pointer, typed methods and properties that call through the function pointer table, and the
	/// nested <see cref="InternalNative"/> table itself.
	/// </summary>
	private void Imports()
	{
		foreach ( Class c in definitions.NativeClasses )
		{
			if ( Skip.ShouldSkip( c ) )
			{
				continue;
			}

			if ( !string.IsNullOrEmpty( c.ManagedNamespace ) )
			{
				StartBlock( $"namespace {c.ManagedNamespace}" );
			}

			WriteWrapperType( c );

			if ( !string.IsNullOrEmpty( c.ManagedNamespace ) )
			{
				EndBlock();
			}

			WriteLine( "" );
		}
	}

	/// <summary>
	/// The whole wrapper type for one native class. Instance classes become a readonly struct around
	/// the native pointer; static/accessor classes become a static class; [SharedDataPointer] classes
	/// become a public class that disposes its native side from the finalizer.
	/// </summary>
	private void WriteWrapperType( Class c )
	{
		string st = (c.Accessor || c.Static) ? "static " : "";
		string t = (c.Accessor || c.Static) ? "class" : "struct";
		string read_only = "readonly ";
		string access = "internal";
		bool allowFromToPointer = true;
		bool destruct = false;

		if ( t == "class" )
		{
			read_only = "";
		}

		if ( c.HasAttribute( "SharedDataPointer" ) )
		{
			t = "class";
			access = "public";
			read_only = "";
			allowFromToPointer = false;
			destruct = true;
		}

		List<string> interfaces = [];

		if ( c.Functions.Any( x => x.Name == "Dispose" ) )
		{
			interfaces.Add( "System.IDisposable" );
		}

		// Struct wrappers compare by pointer - implement IEquatable so dictionary keys don't box
		if ( t == "struct" )
		{
			interfaces.Add( $"System.IEquatable<{c.ManagedName}>" );
		}

		string interfaceCode = interfaces.Count > 0 ? " : " + string.Join( ", ", interfaces ) : "";

		StartBlock( $"{access} unsafe {read_only}{st}partial {t} {c.ManagedName}{interfaceCode}" );
		{
			if ( !c.Accessor && !c.Static )
			{
				WritePointerMembers( c, t, read_only, allowFromToPointer, destruct );
			}

			WriteBaseClassConversions( c );

			foreach ( Function f in c.Functions )
			{
				WriteFunction( c, f, read_only );
			}

			foreach ( Variable v in c.Variables )
			{
				WriteVariable( c, v );
			}

			WriteFunctionPointerTable( c );
		}
		EndBlock();
	}

	/// <summary>
	/// The members every instance wrapper gets: the native pointer itself, IntPtr conversions,
	/// equality, construction and validity helpers.
	/// </summary>
	private void WritePointerMembers( Class c, string t, string read_only, bool allowFromToPointer, bool destruct )
	{
		WriteLine( $"internal {read_only}IntPtr self;" );
		WriteLine();

		if ( allowFromToPointer )
		{
			WriteLine( "// Allow blindly converting from an IntPtr" );
			WriteLine( $"static public implicit operator IntPtr( {c.ManagedName} value ) => value.self;" );
			WriteLine( $"static public implicit operator {c.ManagedName}( IntPtr value ) => new {c.ManagedName}( value );" );
			WriteLine( "" );
		}

		if ( t == "struct" )
		{
			WriteLine( "// Allow us to compare these pointers" );
			WriteLine( $"public static bool operator ==( {c.ManagedName} c1, {c.ManagedName} c2 ) => c1.self == c2.self;" );
			WriteLine( $"public static bool operator !=( {c.ManagedName} c1, {c.ManagedName} c2 ) => c1.self != c2.self;" );
			WriteLine( $"public readonly bool Equals( {c.ManagedName} other ) => self == other.self;" );
			WriteLine( $"public readonly override bool Equals( object obj ) => obj is {c.ManagedName} c && c == this;" );
			WriteLine( "" );
		}

		WriteLine( $"internal {c.ManagedName}( IntPtr ptr ) {{ self = ptr; }}" );

		if ( destruct )
		{
			WriteLine( $"~{c.ManagedName}() {{ if ( !IsNull ) Sandbox.MainThread.QueueDispose( (System.IDisposable)this ); }}" );
		}

		WriteLine( $"public override string ToString() => $\"{c.ManagedName} {{self:x}}\";" );

		WriteLine( "// Helpers to check validity" );
		WriteLine( "" );
		WriteLine( $"internal {read_only}bool IsNull{{ [MethodImpl( MethodImplOptions.AggressiveInlining )] get {{ return self == IntPtr.Zero; }} }}" );

		WriteLine( $"internal {read_only}bool IsValid => !IsNull;" );

		WriteLine( "[MethodImpl( MethodImplOptions.AggressiveInlining )]" );
		WriteLine( $"internal {read_only}IntPtr GetPointerAssertIfNull(){{ if ( self == IntPtr.Zero ) Sandbox.Interop.ThrowNull( \"{c.ManagedName}\" ); return self; }}" );

		WriteLine( "[MethodImpl( MethodImplOptions.AggressiveInlining )]" );

		WriteLine( $"public {read_only}override int GetHashCode() => self.GetHashCode();" );
		WriteLine();
	}

	/// <summary>
	/// Implicit/explicit conversion operators to and from every base class. These go through native
	/// dynamic_cast because with multiple inheritance the base pointer may differ.
	/// </summary>
	private void WriteBaseClassConversions( Class c )
	{
		Class bc = c.BaseClass;

		if ( bc == null )
		{
			return;
		}

		WriteLine( "// Converting to/from base classes (important if multiple inheritence, because they won't be the same pointer)" );
		while ( bc != null )
		{
			WriteLine( $"static public implicit operator {bc.ManagedNameWithNamespace}( {c.ManagedName} value ) => {InternalNative}.To_{bc.ManagedName}_From_{c.ManagedName}( value );" );
			WriteLine( $"static public explicit operator {c.ManagedName}( {bc.ManagedNameWithNamespace} value ) => {InternalNative}.From_{bc.ManagedName}_To_{c.ManagedName}( value );" );
			bc = bc.BaseClass;
		}
		WriteLine();
	}

	/// <summary>
	/// One method wrapper: null-check, marshal the arguments, call through the function pointer and
	/// unmarshal the return value.
	/// </summary>
	private void WriteFunction( Class c, Function f, string read_only )
	{
		string st = (c.Accessor || c.Static || f.Static) ? "static " : read_only;

		IEnumerable<string> managedArgs = f.Parameters.Where( x => x.IsRealArgument ).Select( x => $"{x.ManagedType} {x.Name}" );

		// String-marshalling wrappers have a fat body (stackalloc + try/finally) - forcing those
		// into every call site just bloats the code, so only force-inline the slim ones.
		if ( !f.Parameters.Any( x => x.WrapsManagedCall ) )
		{
			WriteLine( "[MethodImpl( MethodImplOptions.AggressiveInlining )]" );
		}

		if ( f.Name == "Dispose" )
		{
			Write( $"void System.IDisposable.Dispose( {string.Join( ", ", managedArgs )} ) {{ if ( IsNull ) return; ", true );
		}
		else
		{
			if ( f.Return.HasFlag( "asref" ) )
			{
				st += "ref ";
			}

			Write( $"internal {st}{f.Return.ManagedType} {f.GetManagedName()}( {string.Join( ", ", managedArgs )} ) {{ ", true );
		}

		{
			if ( !c.Accessor && !c.Static && !f.Static )
			{
				Write( SelfNullCheck( c.ManagedName, f.Name ) );
			}
			else
			{
				Write( $"if ( {InternalNative}.{f.MangledName} == null ) Sandbox.Interop.ThrowNullFunctionPointer();" );
			}

			IEnumerable<string> nativeArgs = c.SelfArg( false, f.Static ).Concat( f.Parameters ).Where( x => x.IsRealArgument ).Select( x => x.ToInterop( Side.Managed ) );
			string args = $"{string.Join( ", ", nativeArgs )}";

			string call = $"{InternalNative}.{f.MangledName}( {args} )";

			if ( f.HasReturn )
			{
				call = f.Return.FromInterop( Side.Managed, call );
				call = f.Return.ReturnWrapCall( call, Side.Managed );
			}
			else
			{
				call += ";";
			}

			foreach ( Arg param in f.Parameters )
			{
				call = param.WrapFunctionCall( call, Side.Managed );
			}

			// If we're a class and deleting our target, lets also nullify the pointer
			if ( f.Special.Contains( "delete" ) && read_only == "" )
			{
				call = $"try {{ {call} }} finally {{ self = default; }} ";
			}

			Write( call );
		}
		Write( " }\n" );
	}

	/// <summary>
	/// One variable wrapper: a property whose getter and setter call the native get/set pair.
	/// </summary>
	private void WriteVariable( Class c, Variable f )
	{
		string st = (c.Accessor || c.Static || f.Static) ? "static " : "";
		IEnumerable<string> nativeArgs = c.SelfArg( false, f.Static ).Select( x => x.ToInterop( Side.Managed ) );
		string args = $"{string.Join( ", ", nativeArgs )}";

		StartBlock( $"internal {st}{f.Return.ManagedType} {f.GetManagedName()}" );
		{
			Write( "get { ", true );
			{
				if ( !c.Accessor && !c.Static && !f.Static )
				{
					Write( SelfNullCheck( c.ManagedName, f.Name ) );
				}

				string call = $"{InternalNative}.Get__{f.MangledName}( {args} )";

				call = f.Return.FromInterop( Side.Managed, call );
				call = f.Return.ReturnWrapCall( call, Side.Managed );

				Write( call );

			}
			Write( " }\n" );

			if ( !string.IsNullOrEmpty( args ) )
			{
				args += ", ";
			}

			Write( "set { ", true );
			{
				if ( !c.Accessor && !c.Static && !f.Static )
				{
					Write( SelfNullCheck( c.ManagedName, f.Name ) );
				}

				string call = $"{InternalNative}.Set__{f.MangledName}( {args}{f.Return.ToInterop( Side.Managed )} );";
				call = f.Return.WrapFunctionCall( call, Side.Managed ).Replace( "returnvalue", "value" );
				Write( call );

			}
			Write( " }\n" );

		}
		EndBlock();
		WriteLine( "" );
	}

	/// <summary>
	/// The nested <see cref="InternalNative"/> class: one unmanaged function pointer field per cast,
	/// function and variable get/set. Filled in by NativeInterop.Initialize at startup.
	/// </summary>
	private void WriteFunctionPointerTable( Class c )
	{
		StartBlock( $"internal static class {InternalNative}" );
		{

			Class bc = c.BaseClass;

			while ( bc != null )
			{
				WriteLine( $"internal static delegate* unmanaged[SuppressGCTransition]< IntPtr, IntPtr > From_{bc.ManagedName}_To_{c.ManagedName};" );
				WriteLine( $"internal static delegate* unmanaged[SuppressGCTransition]< IntPtr, IntPtr > To_{bc.ManagedName}_From_{c.ManagedName};" );

				bc = bc.BaseClass;
			}

			foreach ( Function f in c.Functions )
			{
				IEnumerable<string> managedArgs = c.SelfArg( false, f.Static ).Concat( f.Parameters ).Where( x => x.IsRealArgument ).Select( x => $"{x.DelegateType( Side.Managed, Dir.Outgoing )}" ).Concat( new[] { f.Return.DelegateType( Side.Managed, Dir.Incoming ) } );
				string managedArgss = $"{string.Join( ", ", managedArgs )}";

				string nogc = "";
				if ( f.IsNoGC )
				{
					nogc = "[SuppressGCTransition]";
				}

				WriteLine( $"internal static delegate* unmanaged{nogc}< {managedArgss} > {f.MangledName};" );
			}

			foreach ( Variable f in c.Variables )
			{
				List<string> managedArgs = c.SelfArg( false, f.Static ).Select( x => $"{x.DelegateType( Side.Managed, Dir.Incoming )}" ).ToList();
				managedArgs.Add( f.Return.DelegateType( Side.Managed, Dir.Incoming ) );
				string managedArgss = $"{string.Join( ", ", managedArgs )}";

				managedArgs[managedArgs.Count - 1] = f.Return.DelegateType( Side.Managed, Dir.Outgoing );
				string setterArgss = $"{string.Join( ", ", managedArgs )}";

				WriteLine( $"internal static delegate* unmanaged[SuppressGCTransition]<{managedArgss}> Get__{f.MangledName};\n" );
				WriteLine( $"internal static delegate* unmanaged[SuppressGCTransition]<{setterArgss}, void> Set__{f.MangledName};\n" );
			}
		}
		EndBlock();
	}

	/// <summary>
	/// Native DECLARE_POINTER_HANDLE types: emitted as a readonly struct that just carries the pointer.
	/// </summary>
	private void PointerStructs()
	{
		foreach ( Struct c in definitions.Structs.Where( x => x.IsPointer == true ) )
		{
			if ( Skip.ShouldSkip( c ) )
			{
				continue;
			}

			if ( !string.IsNullOrEmpty( c.ManagedNamespace ) )
			{
				StartBlock( $"namespace {c.ManagedNamespace}" );
			}

			WriteLine( "/// <summary>" );
			WriteLine( "/// This is a pointer but native pretends like it's a handle/struct using DECLARE_POINTER_HANDLE. We just treat it like a pointer." );
			WriteLine( "/// </summary>" );
			StartBlock( $"internal unsafe readonly struct {c.ManagedName}" );
			{
				WriteLine( "internal readonly IntPtr self;" );
				WriteLine();

				WriteLine( "// Allow blindly converting from an IntPtr" );
				WriteLine( $"static public implicit operator IntPtr( {c.ManagedName} value ) => value.self;" );
				WriteLine( $"static public implicit operator {c.ManagedName}( IntPtr value ) => new {c.ManagedName}( value );" );
				WriteLine( $"public {c.ManagedName}( IntPtr value ) {{ self = value; }}" );
				WriteLine( "" );
			}
			EndBlock();

			if ( !string.IsNullOrEmpty( c.ManagedNamespace ) )
			{
				EndBlock();
			}

			WriteLine( "" );
		}
	}
}
