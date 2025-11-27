using System.Collections.Generic;
using System.Linq;

namespace Facepunch.InteropGen;

internal partial class ManagerWriter
{
	private const string InternalNative = "__N";
	private static bool addRecording = false;

	private void Imports()
	{
		foreach ( Class c in definitions.Classes.Where( x => x.Native == true ) )
		{
			if ( ShouldSkip( c, "managed-definition" ) )
			{
				continue;
			}

			if ( !string.IsNullOrEmpty( c.ManagedNamespace ) )
			{
				StartBlock( $"namespace {c.ManagedNamespace}" );
			}

			string st = (c.Accessor || c.Static) ? "static " : "";
			string t = (c.Accessor || c.Static) ? "class" : "struct";
			string read_only = "readonly ";
			string access = "internal";
			string interfaceCode = "";
			bool allowFromToPointer = true;
			bool destruct = false;
			if ( c.Functions.Any( x => x.Name == "Dispose" ) )
			{
				interfaceCode = " : System.IDisposable";
			}

			if ( c.HasAttribute( "SharedDataPointer" ) )
			{
				t = "class";
				access = "public";
				read_only = "";
				allowFromToPointer = false;
				destruct = true;
			}

			StartBlock( $"{access} unsafe {st}partial {t} {c.ManagedName}{interfaceCode}" );
			{

				if ( !c.Accessor && !c.Static )
				{
					WriteLine( "internal IntPtr self;" );
					WriteLine();

					if ( allowFromToPointer )
					{
						WriteLine( "// Allow blindly converting from an IntPtr" );
						WriteLine( $"static public implicit operator IntPtr( {c.ManagedName} value ) => value.self;" );
						WriteLine( $"static public implicit operator {c.ManagedName}( IntPtr value ) => new {c.ManagedName} {{ self = value }};" );
						WriteLine( "" );
					}

					if ( t == "struct" )
					{
						WriteLine( "// Allow us to compare these pointers" );
						WriteLine( $"public static bool operator ==( {c.ManagedName} c1, {c.ManagedName} c2 ) => c1.self == c2.self;" );
						WriteLine( $"public static bool operator !=( {c.ManagedName} c1, {c.ManagedName} c2 ) => c1.self != c2.self;" );
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
					WriteLine( $"internal {read_only}IntPtr GetPointerAssertIfNull(){{ NullCheck(); return self; }}" );

					WriteLine( "[MethodImpl( MethodImplOptions.AggressiveInlining )]" );
					WriteLine( $"internal {read_only}void NullCheck( [CallerMemberName] string n = \"\" ) {{ if ( IsNull ) throw new System.NullReferenceException( $\"{c.ManagedName} was null when calling {{n}}\" ); }}" );

					WriteLine( $"public {read_only}override int GetHashCode() => self.GetHashCode();" );
					WriteLine();
				}

				Class bc = c.BaseClass;

				if ( bc != null )
				{
					WriteLine( "// Converting to/from base classes (important if multiple inheritence, because they won't be the same pointer)" );
					while ( bc != null )
					{
						Class subclass = bc;
						WriteLine( $"static public implicit operator {subclass.ManagedNameWithNamespace}( {c.ManagedName} value ) => {InternalNative}.To_{subclass.ManagedName}_From_{c.ManagedName}( value );" );
						WriteLine( $"static public explicit operator {c.ManagedName}( {subclass.ManagedNameWithNamespace} value ) => {InternalNative}.From_{subclass.ManagedName}_To_{c.ManagedName}( value );" );
						bc = bc.BaseClass;
					}
					WriteLine();
				}


				foreach ( Function f in c.Functions )
				{
					st = (c.Accessor || c.Static || f.Static) ? "static " : read_only;

					IEnumerable<string> managedArgs = f.Parameters.Where( x => x.IsRealArgument ).Select( x => $"{x.ManagedType} {x.Name}" );

					WriteLine( "[MethodImpl( MethodImplOptions.AggressiveInlining )]" );

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

					if ( addRecording )
					{
						Write( $"Sandbox.InteropSystem.Record( \"{c.ManagedName}.{f.Name}\", \"{string.Join( ",", c.Attributes )},{string.Join( ",", f.attr )}\" );" );
					}

					{
						if ( !c.Accessor && !c.Static && !f.Static )
						{
							Write( $"NullCheck(); " );
						}
						else
						{
							Write( $"if ( {InternalNative}.{f.MangledName} == null ) throw new System.Exception( \"Function Pointer Is Null\" );" );
						}

						IEnumerable<string> nativeArgs = c.SelfArg( false, f.Static ).Concat( f.Parameters ).Where( x => x.IsRealArgument ).Select( x => x.ToInterop( false ) );
						string args = $"{string.Join( ", ", nativeArgs )}";

						string call = $"{InternalNative}.{f.MangledName}( {args} )";

						if ( f.HasReturn )
						{
							call = f.Return.FromInterop( false, call );
							call = f.Return.ReturnWrapCall( call, false );
						}
						else
						{
							call += ";";
						}

						foreach ( Arg param in f.Parameters )
						{
							call = param.WrapFunctionCall( call, false );
						}

						if ( f.ManagedCallReplacement != null )
						{
							call = f.ManagedCallReplacement.Invoke();
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


				foreach ( Variable f in c.Variables )
				{
					st = (c.Accessor || c.Static || f.Static) ? "static " : "";
					IEnumerable<string> nativeArgs = c.SelfArg( false, f.Static ).Select( x => x.ToInterop( false ) );
					string args = $"{string.Join( ", ", nativeArgs )}";

					StartBlock( $"internal {st}{f.Return.ManagedType} {f.GetManagedName()}" );
					{
						Write( "get { ", true );
						{
							if ( !c.Accessor && !c.Static && !f.Static )
							{
								Write( $"NullCheck(); " );
							}

							string call = $"{InternalNative}.Get__{f.MangledName}( {args} )";

							call = f.Return.FromInterop( false, call );
							call = f.Return.ReturnWrapCall( call, false );

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
								Write( $"NullCheck(); " );
							}

							string call = $"{InternalNative}.Set__{f.MangledName}( {args}{f.Return.ToInterop( false, "value" )} );";
							call = f.Return.WrapFunctionCall( call, false ).Replace( "returnvalue", "value" );
							Write( call );

						}
						Write( " }\n" );

					}
					EndBlock();
					WriteLine( "" );
				}

				StartBlock( $"internal static class {InternalNative}" );
				{

					bc = c.BaseClass;

					while ( bc != null )
					{
						Class subclass = bc;
						WriteLine( $"internal static delegate* unmanaged[SuppressGCTransition]< IntPtr, IntPtr > From_{subclass.ManagedName}_To_{c.ManagedName};" );
						WriteLine( $"internal static delegate* unmanaged[SuppressGCTransition]< IntPtr, IntPtr > To_{subclass.ManagedName}_From_{c.ManagedName};" );

						bc = bc.BaseClass;
					}

					foreach ( Function f in c.Functions )
					{
						IEnumerable<string> managedArgs = c.SelfArg( false, f.Static ).Concat( f.Parameters ).Where( x => x.IsRealArgument ).Select( x => $"{x.GetManagedDelegateType( false )}" ).Concat( new[] { f.Return.GetManagedDelegateType( true ) } );
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
						List<string> managedArgs = c.SelfArg( false, f.Static ).Select( x => $"{x.GetManagedDelegateType( true )}" ).ToList();
						managedArgs.Add( f.Return.GetManagedDelegateType( true ) );
						string managedArgss = $"{string.Join( ", ", managedArgs )}";

						WriteLine( $"internal static delegate* unmanaged[SuppressGCTransition]<{managedArgss}> Get__{f.MangledName};\n" );
						WriteLine( $"internal static delegate* unmanaged[SuppressGCTransition]<{managedArgss}, void> Set__{f.MangledName};\n" );
					}
				}
				EndBlock();
			}
			EndBlock();

			if ( !string.IsNullOrEmpty( c.ManagedNamespace ) )
			{
				EndBlock();
			}

			WriteLine( "" );

		}
	}


	private void PointerStructs()
	{
		foreach ( Struct c in definitions.Structs.Where( x => x.IsPointer == true ) )
		{
			if ( ShouldSkip( c, "managed-definition" ) )
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
			StartBlock( $"internal unsafe struct {c.ManagedName}" );
			{
				WriteLine( "internal IntPtr self;" );
				WriteLine();

				WriteLine( "// Allow blindly converting from an IntPtr" );
				WriteLine( $"static public implicit operator IntPtr( {c.ManagedName} value ) => value.self;" );
				WriteLine( $"static public implicit operator {c.ManagedName}( IntPtr value ) => new {c.ManagedName} {{ self = value }};" );
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




