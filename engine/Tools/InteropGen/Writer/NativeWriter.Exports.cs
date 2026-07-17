using System.Collections.Generic;
using System.Linq;

namespace Facepunch.InteropGen;

internal partial class NativeWriter
{
	/// <summary>
	/// How a member is reached on the native side: instance pointer, static scope, global scope or accessor.
	/// </summary>
	private static string AccessPrefix( Class c, bool memberStatic )
	{
		if ( c.Accessor )
		{
			return $"{c.NativeNameWithNamespace}->";
		}

		if ( c.Static && c.NativeName.StartsWith( "global" ) )
		{
			return "::";
		}

		if ( c.Static || memberStatic )
		{
			return $"{c.NativeNameWithNamespace}::";
		}

		return $"(({c.NativeNameWithNamespace}*)self)->";
	}

	private void Exports()
	{
		WriteLine( "//" );
		WriteLine( "// EXPORTS" );
		WriteLine( "// " );
		WriteLine( "// Functions that we're exposing to managed" );
		WriteLine( "//" );
		StartBlock( "namespace Exports" );
		{
			foreach ( Class c in definitions.NativeClasses )
			{
				if ( Skip.ShouldSkip( c ) )
				{
					continue;
				}

				WriteCasts( c );

				foreach ( Function f in c.Functions )
				{
					WriteExportFunction( c, f );
				}

				foreach ( Variable v in c.Variables )
				{
					WriteExportVariable( c, v );
				}
			}
		}
		EndBlock();

		WriteLine();
	}

	/// <summary>
	/// dynamic_cast helpers to and from every base class, used by the managed conversion operators.
	/// </summary>
	private void WriteCasts( Class c )
	{
		if ( c.BaseClass == null )
		{
			return;
		}

		Class bc = c.BaseClass;

		while ( bc != null )
		{
			WriteCast( bc, c );
			bc = bc.BaseClass;
		}

		WriteLine();
	}

	/// <summary>
	/// One exported thunk: unmarshal the arguments, call the native member (or its special/inline/stub
	/// replacement) and marshal the return value back.
	/// </summary>
	private void WriteExportFunction( Class c, Function f )
	{
		// We prepend __ to argument names for inline functions.
		// That way the body can use the right named vars after converting to the proper type.
		string varNamePrepend = f.Body != null ? "__" : "";

		IEnumerable<string> nativeArgs = c.SelfArg( true, f.Static ).Concat( f.Parameters ).Where( x => x.IsRealArgument ).Select( x => $"{x.DelegateType( Side.Native, Dir.Incoming )} {varNamePrepend}{x.Name}" );
		string nativeArgS = string.Join( ", ", nativeArgs );

		StartBlock( $"{f.Return.DelegateType( Side.Native, Dir.Outgoing )} {f.MangledName}( {nativeArgS} )" );
		{
			WriteLine( $"CALL_FROM_MANAGED_START();" );

			// Generate stub implementation for platform-specific functions
			if ( Skip.ShouldStubFunction( c ) )
			{
				WriteLine( $"// Stubbed implementation for {c.ManagedName}::{f.Name}" );
				if ( !f.Return.IsVoid )
				{
					WriteLine( $"return {f.Return.DefaultValue};" );
				}
			}
			else if ( !HandleSpecial( c, f ) )
			{
				if ( c.Accessor )
				{
					WriteLine( $"// Make sure this isn't null" );
					WriteLine( $"Assert( {c.NativeNameWithNamespace} );" );
					WriteLine();
				}

				IEnumerable<string> args = f.Parameters.Select( x => x.FromInterop( Side.Native ) ).Where( x => x != null );
				string argsS = string.Join( ", ", args ).Replace( "__selftype__", $"{c.NativeNameWithNamespace}*" );

				string pre = AccessPrefix( c, f.Static );

				if ( c.IsResourceHandle )
				{
					string strongHandle = $"{c.ResourceHandleName}Strong";
					WriteLine( $"{strongHandle}* __handle = ({strongHandle}*)self;" );
					WriteLine( $"if ( __handle == nullptr || !__handle->HasData() ) return {f.Return.DefaultValue};" );
					WriteLine( $"{c.NativeNameWithNamespace}* __self = const_cast<{c.NativeNameWithNamespace}*>( __handle->GetData() );" );
					WriteLine( $"if ( __self == nullptr ) return {f.Return.DefaultValue};" );

					pre = $"__self->";
				}

				string functionCall = $"{pre}{f.Name}( {argsS} )";

				if ( !f.Return.IsVoid )
				{
					functionCall = f.Return.ToInterop( Side.Native, functionCall );
					functionCall = f.Return.ReturnWrapCall( functionCall, Side.Native );
					WriteLine( $"{functionCall}" );
				}
				else
				{
					WriteLine( $"{functionCall};" );
				}
			}

			WriteLine( $"CALL_FROM_MANAGED_END();" );
		}
		EndBlock();
	}

	/// <summary>
	/// The exported get/set pair for one native variable.
	/// </summary>
	private void WriteExportVariable( Class c, Variable f )
	{
		IEnumerable<string> nativeArgs = c.SelfArg( true, f.Static ).Select( x => $"{x.NativeType} {x.Name}" );
		string nativeArgS = string.Join( ", ", nativeArgs );

		StartBlock( $"{f.Return.DelegateType( Side.Native, Dir.Outgoing )} _Get__{f.MangledName}( {nativeArgS} )" );
		{
			string functionCall = $"{AccessPrefix( c, f.Static )}{f.Name}";

			functionCall = f.Return.ToInterop( Side.Native, functionCall );
			functionCall = f.Return.ReturnWrapCall( functionCall, Side.Native );
			WriteLine( $"{functionCall}" );
		}
		EndBlock();

		if ( !string.IsNullOrEmpty( nativeArgS ) )
		{
			nativeArgS += ", ";
		}

		StartBlock( $"void _Set__{f.MangledName}( {nativeArgS}{f.Return.DelegateType( Side.Native, Dir.Incoming )} value )" );
		{
			string functionCall = $"{AccessPrefix( c, f.Static )}{f.Name} = {f.Return.FromInterop( Side.Native, "value" )}";

			WriteLine( $"{functionCall};" );
		}
		EndBlock();
	}

	private void WriteCast( Class subclass, Class c )
	{
		Write( $"void* From_{subclass.ManagedName}_To_{c.ManagedName}( {subclass.NativeNameWithNamespace}* ptr ) {{ ", true );
		{
			Write( $"return dynamic_cast<{c.NativeNameWithNamespace}*>( ptr );" );
		}
		Write( " }\n" );
		Write( $"void* To_{subclass.ManagedName}_From_{c.ManagedName}( {c.NativeNameWithNamespace}* ptr ) {{ ", true );
		{
			Write( $"return dynamic_cast<{subclass.NativeNameWithNamespace}*>( ptr );" );
		}
		Write( " }\n" );
	}

	private bool HandleSpecial( Class c, Function f )
	{
		if ( f.NativeCallReplacement != null )
		{
			WriteLines( f.NativeCallReplacement );
			return true;
		}

		//
		// If we have a body, we're an inline function, so use that instead of generating the body
		//
		if ( f.Body != null )
		{
			//
			// For functions with a body the args are passed in with __ prepended to the front
			// this lets us convert them to the proper types for use in the body
			//
			IEnumerable<Arg> args = c.SelfArg( true, f.Static ).Concat( f.Parameters );

			if ( c.IsResourceHandle && !f.Static )
			{
				string strongHandle = $"{c.ResourceHandleName}Strong";
				StartBlock( "// Handle Conversion - make __self point to the actual class instead of the handle" );
				WriteLine( $"{strongHandle}* __handle = ({strongHandle}*)__self;" );
				WriteLine( $"if ( __handle == nullptr || !__handle->HasData() ) return {f.Return.DefaultValue};" );
				WriteLine( $"__self = const_cast<{c.NativeNameWithNamespace}*>( __handle->GetData() );" );
				WriteLine( $"if ( __self == nullptr ) return {f.Return.DefaultValue};" );
				EndBlock();
				WriteLine();
			}

			WriteLine( $"// Convert parameters" );

			foreach ( Arg arg in args )
			{
				if ( arg.IsSelf )
				{
					WriteLine( $"{c.NativeNameWithNamespace}* {arg.Name} = ({c.NativeNameWithNamespace}*) __{arg.Name};" );
					continue;
				}

				WriteLine( $"auto {arg.Name} = {arg.FromInterop( Side.Native, $"__{arg.Name}" )};" );
			}

			string body = f.Body.ToString();
			body = body.Replace( "this->", "self->" );

			//
			// If the inline shit is returning shit, we want to try to wrap it with ToInterop and ReturnWrapCall
			// this code below is janky and probably isn't the real solution, but it worked for what I was doing
			//
			if ( !f.Return.IsVoid )
			{
				string[] lines = body.Split( "\n" );

				for ( int i = 0; i < lines.Length; i++ )
				{
					if ( !lines[i].Trim().StartsWith( "return " ) )
					{
						continue;
					}

					lines[i] = lines[i].Trim().Replace( "return ", "" ).TrimEnd( ';', '\r', '\n' );

					lines[i] = f.Return.ToInterop( Side.Native, lines[i] );
					lines[i] = f.Return.ReturnWrapCall( lines[i], Side.Native );
				}

				body = string.Join( "\n", lines );
			}

			//
			// Print the body with this-> changed to self->
			//
			WriteLine( $"" );
			WriteLine( $"// Body" );
			WriteLines( body );
			return true;
		}

		if ( f.Special.Contains( "delete" ) )
		{
			WriteLine( $"delete (({c.NativeNameWithNamespace}*)self);" );
			return true;
		}

		if ( f.Special.Contains( "new" ) )
		{
			IEnumerable<string> args = f.Parameters.Select( x => x.FromInterop( Side.Native ) );
			string argsS = string.Join( ", ", args );

			WriteLine( $"return new {c.NativeNameWithNamespace}( {argsS} );" );
			return true;
		}

		return false;
	}
}
