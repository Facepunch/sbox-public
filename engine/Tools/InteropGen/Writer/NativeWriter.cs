using System.Collections.Generic;
using System.Linq;

namespace Facepunch.InteropGen;

/// <summary>
/// Emits the native (C++) source: the import function pointers and their implementations, the exported
/// thunks native exposes to managed, and the igen_* initializer that exchanges the function tables.
/// </summary>
internal partial class NativeWriter : BaseWriter
{
	public NativeWriter( Definition definitions, string targetName ) : base( definitions, targetName )
	{
	}

	public override void Generate()
	{
		string headerName = System.IO.Path.GetFileName( definitions.SaveFileCppH );

		if ( !string.IsNullOrWhiteSpace( definitions.PrecompiledHeader ) )
		{
			WriteLine( "// Precompiled Header (pch in def)" );
			WriteLine( $"#include \"{definitions.PrecompiledHeader}\"" );
			WriteLine();
		}

		WriteLine( $"#include \"{headerName}\"" );

		WriteLine( $"#include \"sbox/inetruntime.h\"" );
		WriteLine( $"#include \"tier0/managedhandle.h\"" );
		WriteLine( $"#include <string>" );
		WriteLine();

		WriteLine( "" );
		WriteLine( "#pragma warning(disable : 4714)" );
		WriteLine( "#ifdef _WIN32" );
		WriteLine( "#define CC __stdcall" );
		WriteLine( "#else" );
		WriteLine( "#define CC" );
		WriteLine( "#endif" );

		{
			WriteLine( "//" );
			WriteLine( "// For instances where we'd otherwise be returning a pointer to a local" );
			WriteLine( "//" );
			WriteLine( "static thread_local CUtlString _sfstr;" );
			StartBlock( "const char* SafeReturnString( const char* input )" );
			{
				WriteLine( "if ( input == nullptr ) return nullptr;" );
				WriteLine( "_sfstr.Set( input );" );
				WriteLine( "return _sfstr.Get();" );
			}
			EndBlock();

			WriteLine( "static thread_local std::wstring _wstr;" );
			StartBlock( "const wchar_t *SafeReturnWString( const wchar_t *input )" );
			{
				WriteLine( "if ( input == nullptr ) return nullptr;" );
				WriteLine( "_wstr.assign( input );" );
				WriteLine( "return _wstr.c_str();" );
			}
			EndBlock();
		}

		Imports();

		Exports();

		Initialize();

		WriteLine( "" );
	}


	/// <summary>
	/// The igen_* initializer that managed calls at startup: verifies the def hash, hands managed the
	/// native export table, checks struct sizes and receives the managed function pointers. Plus the
	/// IsReady/Shutdown state the rest of native uses to know if the binds are alive.
	/// </summary>
	private void Initialize()
	{
		IEnumerable<Function> imports = definitions.ManagedClasses.Where( x => !Skip.ShouldSkip( x ) ).SelectMany( x => x.Functions );
		int importCount = imports.Count();

		WriteLine( "//" );
		WriteLine( "// MANAGER" );
		WriteLine( "// " );
		WriteLine( "// Manager class to set everything up" );
		WriteLine( "//" );

		foreach ( string ns in definitions.ManagedNamespace.Split( "." ) )
		{
			StartBlock( $"namespace {ns}" );
		}

		{
			StartBlock( "void Debug_Error( const char* string )" );
			{
				WriteLine( $"Plat_FatalError( \"{definitions.Ident} Failed To Initialize - %s\", string );" );
				WriteLine( "exit( 575 );" );
			}
			EndBlock();

			WriteLine( "" );

			WriteLine( "bool s_isReady = false;" );
			WriteLine( "" );

			StartBlock( $"DLL_EXPORT void CC igen_{definitions.Ident}( int hash, void** managedFunctions, void** nativeFunctions, int* structSizes )" );
			{
				StartBlock( $"if ( hash != {definitions.Hash} )" );
				{
					WriteLine( $"Plat_FatalError( \"igen_{definitions.Ident}: interop hash mismatch - managed sent %d, native was built with {definitions.Hash}. The two sides are out of sync - rebuild both.\", hash );" );
				}
				EndBlock();

				WriteLine( "" );
				NativeFunctionTable();
				WriteLine( "" );

				StructSizeChecks();

				WriteLine();

				if ( importCount > 0 )
				{
					WriteLine( "" );
					WriteLine( $"// Not ready, failed, if any of the imports are null" );
					WriteLine( $"for ( int f =0; f<{importCount}; f++ ) if ( managedFunctions[f] == nullptr ) Plat_FatalError( \"igen_{definitions.Ident}: managed function pointer %d is null\", f );" );
				}


				WriteLine( "" );

				BindManagedFunctions( imports );

				WriteLine();
				WriteLine( "s_isReady = true;" );


			}
			EndBlock();

			WriteLine( "" );

			WriteLine();

			StartBlock( "bool IsReady()" );
			{
				WriteLine( "return s_isReady;" );
			}
			EndBlock();

			WriteLine( "" );

			StartBlock( "void Shutdown()" );
			{
				WriteLine( "s_isReady = false;" );
			}
			EndBlock();

		}
		foreach ( string ns in definitions.ManagedNamespace.Split( "." ) )
		{
			EndBlock();
		}
	}

	/// <summary>
	/// Fill the nativeFunctions array with a pointer to each exported symbol. Slot order comes from
	/// <see cref="NativeExportTable"/>, which the managed reader uses too.
	/// </summary>
	private void NativeFunctionTable()
	{
		int slotIndex = 0;
		foreach ( NativeSlot slot in NativeExportSlots() )
		{
			string symbol = slot.Kind switch
			{
				NativeSlotKind.Error => "Debug_Error",
				NativeSlotKind.CastFromTo => $"Exports::From_{slot.BaseClass.ManagedName}_To_{slot.Class.ManagedName}",
				NativeSlotKind.CastToFrom => $"Exports::To_{slot.BaseClass.ManagedName}_From_{slot.Class.ManagedName}",
				NativeSlotKind.Function => $"Exports::{slot.Function.MangledName}",
				NativeSlotKind.VariableGet => $"Exports::_Get__{slot.Variable.MangledName}",
				NativeSlotKind.VariableSet => $"Exports::_Set__{slot.Variable.MangledName}",
				_ => null
			};

			WriteLine( $"nativeFunctions[{slotIndex++}] = (void*)&{symbol};" );
		}
	}

	/// <summary>
	/// Verify both sides agree on every struct's size, and that no struct smaller than a pointer is
	/// missing its [small] attribute.
	/// </summary>
	private void StructSizeChecks()
	{
		int i = 0;
		foreach ( Struct s in definitions.Structs )
		{
			if ( Skip.ShouldSkip( s ) )
			{
				continue;
			}

			WriteLine( $"if ( sizeof( {s.NativeNameWithNamespace} ) != structSizes[{i}] ) Plat_FatalError( \"{s.NativeNameWithNamespace} is the wrong size\" );" );

			if ( !s.IsEnum && !s.HasAttribute( "small" ) )
			{
				WriteLine( $"static_assert ( sizeof( {s.NativeNameWithNamespace} ) >= 8, \"Please mark struct {s.NativeName} with a [small] - it's smaller than a pointer\" );" );
			}

			i++;
		}
	}

	/// <summary>
	/// Read each received managed function pointer out of the managedFunctions array into the matching
	/// Imports:: field.
	/// </summary>
	private void BindManagedFunctions( IEnumerable<Function> imports )
	{
		int i = 0;
		foreach ( Function f in imports )
		{
			Class c = f.Class;
			IEnumerable<string> nativeArgs = c.SelfArg( true, f.Static ).Concat( f.Parameters ).Select( x => $"{x.DelegateType( Side.Native, Dir.Outgoing )}" );
			string nativeArgS = string.Join( ",", nativeArgs );

			string functionType = $"{f.Return.DelegateType( Side.Native, Dir.Outgoing )} (CC *)( {nativeArgS.Trim( ',', ' ' )} )";

			WriteLine( $"Imports::{f.MangledName} = ({functionType}) managedFunctions[{i}];" );

			i++;
		}
	}
}
