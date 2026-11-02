using System;
using System.IO;
using Sandbox.Mounting;
using Sandbox.Rendering;

namespace Sandbox.Mounting.Source1;

internal sealed class SourceDecalLoader( SourceDecalMaterial material ) : ResourceLoader<BaseMount>
{
	protected override object Load()
	{
		if ( material.Shader is not ("decalmodulate" or "modulate" or "decal" or "lightmappedgeneric_decal"
			or "lightmappedgeneric" or "unlitgeneric" or "vertexlitgeneric") )
			throw new NotSupportedException( $"{material.Path}: unsupported Source decal shader '{material.Shader}'." );
		var parameters = material.Parameters;
		var color = LoadTexture( "color" );
		var source = Host.GetSourceTexture( parameters.String( "$basetexture" ), BaseMount.GetContentRoot( material.Path, "materials" ) );
		var normal = !string.IsNullOrWhiteSpace( parameters.String( "$bumpmap" ) ) ? LoadTexture( "normal" ) : null;
		var scale = parameters.Float( "$decalscale", 1 );
		if ( scale <= 0 || !float.IsFinite( color.Width * scale ) || !float.IsFinite( color.Height * scale ) )
			throw new InvalidDataException( $"{material.Path}: $decalscale must produce finite positive decal dimensions." );
		var tint = parameters.Vector( "$color", Vector3.One ) * parameters.Vector( "$color2", Vector3.One );
		var linearTint = new Color( SourceDecalPixels.Linear( tint.x ), SourceDecalPixels.Linear( tint.y ), SourceDecalPixels.Linear( tint.z ),
			Math.Clamp( parameters.Float( "$alpha", 1 ), 0, 1 ) );
		foreach ( var key in new[] { "$envmap", "$detail", "$lightwarptexture", "$phongwarptexture" } )
			if ( !string.IsNullOrWhiteSpace( parameters.String( key ) ) )
				Log.Trace( $"{material.Path}: {key} is not represented by native projected decals." );
		return new DecalDefinition
		{
			ColorTexture = color,
			NormalTexture = normal,
			BlendMode = material.BlendMode,
			Width = color.Width * scale,
			Height = color.Height * scale,
			Tint = linearTint,
			FilterMode = (source.Header.Flags & VtfFlags.POINTSAMPLE) != 0 ? FilterMode.Point
				: (source.Header.Flags & VtfFlags.TRILINEAR) != 0 ? FilterMode.Trilinear : FilterMode.Anisotropic
		};

		Texture LoadTexture( string kind )
		{
			var resource = $"mount://{Host.Ident}/{material.Path}.decal_{kind}.vtex";
			var texture = Texture.Load( resource, false );
			if ( texture is null || !texture.IsValid || texture.IsError )
				throw new InvalidDataException( $"{material.Path}: decal {kind} texture could not be loaded." );
			return texture;
		}
	}
}

internal sealed class SourceDecalPrefabLoader( string group, IReadOnlyList<SourceDecalLibrary.Entry> entries ) : ResourceLoader<BaseMount>
{
	private PrefabFile prefab;

	protected override object Load()
	{
		var definitions = new List<DecalDefinition>();
		foreach ( var entry in entries )
		{
			if ( entry.Weight <= 0 || !Host.HasDecalMaterial( entry.Material ) ) continue;
			definitions.Add( Host.GetDecal( entry.Material ) );
		}
		if ( entries.Count > 0 && definitions.Count == 0 )
			throw new InvalidDataException( $"{Path}: decal group has no usable material variants." );
		var builder = new PrefabBuilder().WithName( Path );
		using ( builder.Scope() )
		{
			Game.ActiveScene.Enabled = false;
			var root = new GameObject( true, group );
			root.AddComponent<TemporaryEffect>();
			if ( definitions.Count > 0 )
			{
				var decal = root.AddComponent<Decal>( false );
				decal.Decals = definitions;
				decal.Transient = true;
				decal.SortLayer = 1;
				decal.Enabled = true;
			}
		}
		return prefab = builder.Create()
			?? throw new InvalidOperationException( $"{Path}: could not create the decal group prefab." );
	}

	protected override void Shutdown()
	{
		PrefabBuilder.Destroy( prefab );
		prefab = null;
		base.Shutdown();
	}
}
