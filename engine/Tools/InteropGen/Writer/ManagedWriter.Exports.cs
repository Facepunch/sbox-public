using System.Collections.Generic;
using System.Linq;

namespace Facepunch.InteropGen;

internal partial class ManagedWriter
{

	private void Exports()
	{
		StartBlock( $"internal static unsafe class Exports" );
		{
			foreach ( Class c in definitions.ManagedClasses )
			{
				if ( Skip.ShouldSkip( c ) )
				{
					continue;
				}

				foreach ( Function f in c.Functions )
				{
					ExportFunction( c, f );
				}

				if ( c.Variables.Count > 0 )
				{
					throw new System.NotImplementedException();
				}
			}
		}
		EndBlock();
		WriteLine( "" );
	}

	private void ExportFunction( Class c, Function f )
	{
		IEnumerable<string> nativeArgs = c.SelfArg( false, f.Static ).Concat( f.Parameters ).Select( x => $"{x.DelegateType( Side.Managed, Dir.Incoming )} {x.Name}" );
		string nativeArgS = string.Join( ", ", nativeArgs );

		IEnumerable<string> managedArgs = f.Parameters.Select( x => x.FromInterop( Side.Managed ) );
		string managedArgsS = string.Join( ", ", managedArgs );

		string namespc = $"{c.ManagedNamespace}.{c.ManagedName}";

		WriteLine( "/// <summary>" );
		WriteLine( $"/// {namespc}.{f.Name}( ... )" );
		WriteLine( "/// </summary>" );
		WriteLine( "[UnmanagedCallersOnly]" );
		StartBlock( $"internal static {f.Return.DelegateType( Side.Managed, Dir.Incoming )} {f.MangledName}( {nativeArgS.Trim( ',', ' ' )} )" );
		{
			StartBlock( "try" );
			{
				string func = $"{c.ManagedNameWithNamespace}.{f.Name}( {managedArgsS} )";

				if ( !c.Native && !c.Static && !f.Static )
				{
					WriteLine( $"if ( !Sandbox.InteropSystem.TryGetObject<{c.ManagedNameWithNamespace}>( self, out var instance ) )" );
					WriteLine( $"	return{(f.HasReturn ? " default" : "")};" );
					WriteLine();

					func = $"instance.{f.Name}( {managedArgsS} )";
				}

				if ( f.HasReturn )
				{
					func = f.Return.ToInterop( Side.Managed, func );
					func = f.Return.ReturnWrapCall( func, Side.Managed );
				}
				else
				{
					func += ";";
				}

				WriteLines( func );
			}
			EndBlock();
			StartBlock( "catch ( System.Exception ___e )" );
			{
				WriteLine( $"{definitions.ExceptionHandlerName}( \"{c.ManagedNamespace}.{c.ManagedName}\", \"{f.Name}\", ___e );" );

				if ( f.HasReturn )
				{
					WriteLine( $"return default;" );
				}
			}
			EndBlock();


		}
		EndBlock();
		WriteLine();
	}
}
