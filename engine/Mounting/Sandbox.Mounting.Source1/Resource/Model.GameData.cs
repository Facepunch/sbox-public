using System;
using System.IO;
using Sandbox.Mounting;

namespace Sandbox.Mounting.Source1;

internal sealed partial class SourceModelBuilder
{
	private void AddGameData( ModelBuilder builder, MdlFile mdl, PhyFile physics )
	{
		AddPropSettings( builder, mdl, physics );
		if ( physics is not null )
			AddBreakPieces( builder, physics );
	}

	private void AddPropSettings( ModelBuilder builder, MdlFile mdl, PhyFile physics )
	{
		if ( mdl.KeyValues.Count == 0 )
			return;

		try
		{
			var hasAuthoredPieces = physics?.Text.Any( block => block.IsBlock
				&& block.Name.Equals( "break", StringComparison.OrdinalIgnoreCase ) ) == true;
			var data = Host.GetPropData().Read( mdl.KeyValues, FullPath, message => Log.Trace( message ), hasAuthoredPieces );
			if ( data is not null )
				builder.WithData( data );
		}
		catch ( Exception e ) when ( e is IOException or InvalidDataException or NotSupportedException )
		{
			Log.Trace( $"{FullPath}: prop settings were not imported: {e.Message}" );
		}
	}

	private void AddBreakPieces( ModelBuilder builder, PhyFile physics )
	{
		try
		{
			var pieces = SourceBreakPieces.Read( physics.Text, FullPath, ResolveBreakModel, message => Log.Trace( message ) );
			if ( pieces.Length > 0 )
				builder.WithData( pieces );
		}
		catch ( Exception e ) when ( e is IOException or InvalidDataException or NotSupportedException )
		{
			Log.Trace( $"{FullPath}: break pieces were not imported: {e.Message}" );
		}
	}

	private string ResolveBreakModel( string path )
	{
		return Host.GetBreakModelPath( path );
	}
}
