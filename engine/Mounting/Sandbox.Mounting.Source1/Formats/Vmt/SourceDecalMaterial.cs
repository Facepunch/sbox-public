using System;
using System.Collections.Generic;
using System.IO;
using Sandbox.Rendering;

namespace Sandbox.Mounting.Source1;

internal sealed record SourceDecalMaterial( string Path, string Shader, IReadOnlyDictionary<string, string> Values,
	SourceDecalMaterial.Rectangle? Subrect = null )
{
	internal readonly record struct Rectangle( int X, int Y, int Width, int Height );
	internal VmtParameters Parameters => new( Values, Path );
	internal DecalBlendMode BlendMode => Shader is "decalmodulate" or "modulate" ? DecalBlendMode.Multiply
		: Parameters.Boolean( "$additive" ) ? DecalBlendMode.Additive : DecalBlendMode.Alpha;
	internal float ModulationScale => Shader == "decalmodulate" || Parameters.Boolean( "$mod2x" ) ? 2 : 1;

	internal static SourceDecalMaterial Read( string path, Func<string, Stream> open, Action<string> report )
	{
		var active = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		return Read( VmtFile.NormalizePath( path, ".vmt" ) );

		SourceDecalMaterial Read( string current )
		{
			if ( active.Count >= 32 || !active.Add( current ) )
				throw new InvalidDataException( $"{path}: cyclic or excessively nested decal subrect materials." );
			try
			{
				var vmt = VmtFile.Load( current, open,
					symbol => symbol.ToLowerInvariant() switch { "srgb" => true, "gpu<2" or "lowfill" => false, _ => null },
					diagnostic => report( $"{diagnostic.Path}:{diagnostic.Line}: {diagnostic.Message}" ) );
				var shader = VmtFile.NormalizeShaderName( vmt.ShaderName );
				var values = VmtFile.SelectParameters( vmt );
				if ( shader != "subrect" ) return new( path, shader, values );
				var parameters = new VmtParameters( values, current );
				var parentPath = parameters.String( "$material" );
				if ( string.IsNullOrWhiteSpace( parentPath ) )
					throw new InvalidDataException( $"{current}: Subrect requires $material." );
				var parent = Read( VmtFile.NormalizePath( parentPath, ".vmt" ) );
				var position = parameters.Pair( "$pos", Vector2.Zero );
				var size = parameters.Pair( "$size", Vector2.Zero );
				if ( position.x < 0 || position.y < 0 || size.x <= 0 || size.y <= 0
					|| position.x != MathF.Truncate( position.x ) || position.y != MathF.Truncate( position.y )
					|| size.x != MathF.Truncate( size.x ) || size.y != MathF.Truncate( size.y )
					|| position.x + size.x > short.MaxValue || position.y + size.y > short.MaxValue )
					throw new InvalidDataException( $"{current}: Subrect requires positive integral dimensions and nonnegative integral offsets." );
				if ( parent.Subrect is { } rect && (position.x + size.x > rect.Width || position.y + size.y > rect.Height) )
					throw new InvalidDataException( $"{current}: Subrect extends outside its parent material." );
				var merged = new Dictionary<string, string>( parent.Values, StringComparer.OrdinalIgnoreCase );
				foreach ( var entry in values )
					if ( entry.Key.ToLowerInvariant() is not ("$material" or "$pos" or "$size") ) merged[entry.Key] = entry.Value;
				return parent with
				{
					Path = path,
					Values = merged,
					Subrect = new Rectangle( (int)position.x + (parent.Subrect?.X ?? 0),
						(int)position.y + (parent.Subrect?.Y ?? 0), (int)size.x, (int)size.y )
				};
			}
			finally
			{
				active.Remove( current );
			}
		}
	}
}
