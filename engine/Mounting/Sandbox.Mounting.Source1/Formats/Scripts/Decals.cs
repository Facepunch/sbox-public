using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Sandbox.Mounting.Source1;

internal sealed class SourceDecalLibrary
{
	internal sealed record Entry( string Material, float Weight );
	internal Dictionary<string, IReadOnlyList<Entry>> Groups { get; } = new( StringComparer.OrdinalIgnoreCase );
	internal Dictionary<char, string> Translations { get; } = [];

	internal string ImpactGroup( char material )
	{
		if ( material == '-' ) return null;
		if ( material != 'C' && Translations.TryGetValue( material, out var translated ) && translated.Length > 0 )
			return translated;
		return Groups.ContainsKey( "Impact.Concrete" ) ? "Impact.Concrete" : null;
	}

	internal static SourceDecalLibrary Parse( string text, string path, Action<string> report )
	{
		var result = new SourceDecalLibrary();
		foreach ( var block in ScriptManifest.Parse( text, path, report ) )
		{
			if ( !block.IsBlock )
				throw new InvalidDataException( $"{path}: decal definitions must be named blocks." );
			if ( block.Name.Equals( "TranslationData", StringComparison.OrdinalIgnoreCase ) )
			{
				foreach ( var entry in block.Children )
				{
					if ( entry.IsBlock || entry.Name.Length != 1 )
						throw new InvalidDataException( $"{path}: decal translations require single-character game materials." );
					result.Translations[char.ToUpperInvariant( entry.Name[0] )] = entry.Value;
				}
				continue;
			}
			var entries = new List<Entry>();
			double total = 0;
			foreach ( var entry in block.Children )
			{
				if ( entry.IsBlock || !float.TryParse( entry.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var weight )
					|| !float.IsFinite( weight ) || weight < 0 )
					throw new InvalidDataException( $"{path}: decal group '{block.Name}' has an invalid weight for '{entry.Name}'." );
				entries.Add( new Entry( VmtFile.NormalizePath( entry.Name, ".vmt" ), weight ) );
				total += weight;
			}
			if ( total <= 0 )
				throw new InvalidDataException( $"{path}: decal group '{block.Name}' has no positive selection weights." );
			result.Groups[block.Name] = entries;
		}
		foreach ( var translation in result.Translations )
		{
			if ( translation.Value.Length > 0 && !result.Groups.ContainsKey( translation.Value ) )
			{
				report( $"{path}: decal translation '{translation.Key}' references missing group '{translation.Value}'." );
				result.Translations[translation.Key] = "";
			}
		}
		return result;
	}
}
