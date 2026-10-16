using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace Sandbox.Mounting.Source1;

internal sealed record SourceSurfaceDefinition( string Name, string SourcePath, IReadOnlyDictionary<string, string> Values )
{
	internal VmtParameters Parameters => new( Values, $"{SourcePath}: {Name}" );

	internal char GameMaterial
	{
		get
		{
			var text = Parameters.String( "gamematerial", "0" );
			if ( text.Length == 1 && !char.IsDigit( text[0] ) ) return char.ToUpperInvariant( text[0] );
			var value = Parameters.Integer( "gamematerial" );
			if ( value < 0 || value > char.MaxValue )
				throw new InvalidDataException( $"{SourcePath}: surface '{Name}' has invalid game material." );
			return char.ToUpperInvariant( (char)value );
		}
	}

	internal static void Parse( string text, string path, Dictionary<string, SourceSurfaceDefinition> surfaces, Action<string> report )
	{
		foreach ( var block in ScriptManifest.Parse( text, path, report ) )
		{
			if ( !block.IsBlock || string.IsNullOrWhiteSpace( block.Name ) )
				throw new InvalidDataException( $"{path}: surface definitions must be named blocks." );

			if ( !surfaces.TryGetValue( block.Name, out var previous ) )
				surfaces.TryGetValue( "default", out previous );
			var values = previous is null ? new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase )
				: new Dictionary<string, string>( previous.Values, StringComparer.OrdinalIgnoreCase );

			foreach ( var entry in block.Children )
			{
				if ( entry.IsBlock )
					throw new InvalidDataException( $"{path}: surface '{block.Name}' has a nested '{entry.Name}' block." );
				if ( entry.Name.Equals( "base", StringComparison.OrdinalIgnoreCase ) )
				{
					if ( !surfaces.TryGetValue( entry.Value, out var parent ) )
					{
						report( $"{path}: surface '{block.Name}' references unavailable base '{entry.Value}'; retaining preceding values." );
						continue;
					}
					values = new Dictionary<string, string>( parent.Values, StringComparer.OrdinalIgnoreCase );
				}
				else
					values[entry.Name] = entry.Value;
			}

			var definition = new SourceSurfaceDefinition( block.Name, path, new ReadOnlyDictionary<string, string>( values ) );
			_ = definition.GameMaterial;
			foreach ( var key in new[] { "friction", "elasticity", "density" } )
			{
				if ( definition.Parameters.Float( key ) < 0 )
					throw new InvalidDataException( $"{path}: surface '{block.Name}' has negative '{key}'." );
			}
			surfaces[block.Name] = definition;
		}
	}
}
