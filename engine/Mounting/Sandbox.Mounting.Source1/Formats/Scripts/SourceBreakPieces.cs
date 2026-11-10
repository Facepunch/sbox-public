using System;
using System.Collections.Generic;
using System.IO;
using Sandbox.ModelEditor.Nodes;

namespace Sandbox.Mounting.Source1;

internal static class SourceBreakPieces
{
	internal static ModelBreakPiece[] Read( IReadOnlyList<VmtNode> blocks, string path,
		Func<string, string> resolve, Action<string> report )
	{
		var pieces = new List<ModelBreakPiece>();
		var unsupported = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var block in blocks )
		{
			if ( !block.IsBlock || !block.Name.Equals( "break", StringComparison.OrdinalIgnoreCase ) )
				continue;

			var values = new VmtParameters( block.Values, path );
			var name = values.String( "model" ) ?? values.String( "ragdoll" );
			if ( string.IsNullOrWhiteSpace( name ) )
				throw new InvalidDataException( $"{path}: break block has no model." );

			name = name.Replace( '\\', '/' ).Trim();
			if ( !name.StartsWith( "models/", StringComparison.OrdinalIgnoreCase ) )
				name = $"models/{name}";
			if ( !name.EndsWith( ".mdl", StringComparison.OrdinalIgnoreCase ) )
				name += ".mdl";
			name = ScriptManifest.ContentPath( name );

			var model = resolve( name );
			if ( model is null )
			{
				report( $"{path}: break model '{name}' is missing; that piece was omitted." );
				continue;
			}

			var fade = values.Float( "fadetime", 20 );
			if ( fade < 0 )
				throw new InvalidDataException( $"{path}: negative break piece fadetime." );
			var offset = values.Vector( "offset", default );
			pieces.Add( new ModelBreakPiece
			{
				PieceName = $"piece_{pieces.Count}",
				Model = model,
				Offset = offset,
				FadeTime = fade,
				CollisionTags = values.Boolean( "debris", values.Contains( "fadetime" ) ) ? "debris" : "",
				IsClientOnly = values.String( "multiplayer_break" )?.Equals( "client", StringComparison.OrdinalIgnoreCase ) == true,
				PlacementBone = values.String( "placementbone" ),
				PlacementAttachment = values.String( "placementattachment" )
			} );

			foreach ( var key in block.Values.Keys )
			{
				if ( key.ToLowerInvariant() is not ("model" or "ragdoll" or "offset" or "fadetime" or "debris" or "multiplayer_break") )
					unsupported.Add( key );
			}
		}

		if ( unsupported.Count > 0 )
			report( $"{path}: break piece fields not implemented by engine Prop: {string.Join( ", ", unsupported.Order( StringComparer.OrdinalIgnoreCase ) )}." );
		return pieces.ToArray();
	}
}
