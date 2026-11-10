using System;

namespace Sandbox.Mounting.Source1;

internal sealed class SourceMaterialVariant
{
	internal SourceMaterial Source { get; }
	internal object Scope { get; }
	internal IReadOnlyList<SourceMaterialOverrides> Overrides { get; }

	private readonly string nativeShader;
	private readonly SourceMaterialOverrides nativeBindings;
	internal SourceMaterialVariant( SourceMaterial source, IReadOnlyList<SourceMaterialOverrides> overrides = null )
	{
		Source = source;
		Scope = source.Scope;
		Overrides = overrides ?? Array.Empty<SourceMaterialOverrides>();
	}

	private SourceMaterialVariant( string shader, SourceMaterialOverrides bindings, object scope, IReadOnlyList<SourceMaterialOverrides> overrides = null )
	{
		nativeShader = shader;
		nativeBindings = bindings;
		Scope = scope;
		Overrides = overrides ?? Array.Empty<SourceMaterialOverrides>();
	}

	internal static SourceMaterialVariant ReadEngineFallback( Material material, object scope )
	{
		var name = material.Name?.Replace( '\\', '/' ).ToLowerInvariant();
		if ( name is not ("materials/error.vmat" or "materials/dev/reflectivity_30.vmat") )
			return null;
		var bindings = new SourceMaterialOverrides();
		var shader = material.ShaderName;
		if ( name == "materials/error.vmat" )
		{
			if ( shader is not ("shaders/error.shader" or "shaders/error.shader_c") )
				return null;
		}
		else
		{
			if ( shader is not ("shaders/complex.shader" or "shaders/complex.shader_c") )
				return null;
			bindings.Features["F_SPECULAR"] = material.GetFeature( "F_SPECULAR" );
			foreach ( var parameter in new[]
			{
				"g_flAmbientOcclusionDirectDiffuse",
				"g_flAmbientOcclusionDirectSpecular",
				"g_flModelTintAmount",
				"g_vColorTint",
				"g_flFadeExponent",
				"g_bFogEnabled",
				"g_flMetalness",
				"g_flRoughnessScaleFactor",
				"g_nScaleTexCoordUByModelScaleAxis",
				"g_nScaleTexCoordVByModelScaleAxis",
				"g_vTexCoordOffset",
				"g_vTexCoordScale",
				"g_vTexCoordScrollSpeed"
			}

			)
				bindings.Values[parameter] = material.GetVector4( parameter );
			foreach ( var input in new[]
			{
				"g_tColor",
				"g_tNormal",
				"g_tAmbientOcclusion"
			}

			)
			{
				var texture = material.GetTexture( input );
				if ( texture is null || !texture.IsValid || texture.IsError )
					throw new InvalidOperationException( $"Engine fallback '{name}' texture '{input}' could not be loaded." );
				bindings.Textures[input] = texture;
			}
		}

		return new SourceMaterialVariant( shader, bindings, scope );
	}

	internal static SourceMaterialVariant ReadErrorMaterial( Material material, object scope )
		=> material.Name?.Replace( '\\', '/' ).Equals( "materials/error.vmat", StringComparison.OrdinalIgnoreCase ) == true
			? ReadEngineFallback( material, scope ) : null;

	internal SourceMaterialVariant WithOverrides( SourceMaterialOverrides overrides )
	{
		var bindings = Overrides.Append( overrides.Snapshot() ).ToArray();
		return Source is not null ? new( Source, bindings ) : new( nativeShader, nativeBindings, Scope, bindings );
	}

	internal Material Create( string name )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( name );
		var material = Source is not null ? Source.CreateMaterial( name, Overrides.Any( binding => binding.MapSurface ) ) : Material.Create( name, nativeShader, anonymous: false ) ?? throw new InvalidOperationException( $"Could not create named engine fallback '{name}' using '{nativeShader}'." );
		nativeBindings?.Bind( material );
		foreach ( var binding in Overrides )
			binding.Bind( material );
		if ( Source is not null )
			SourceMaterialBuilder.UpdateProbeUsage( material );
		return material;
	}
}

internal sealed class SourceMaterialOverrides
{
	internal bool MapSurface { get; init; }
	internal Dictionary<string, int> Features { get; } = [];
	internal Dictionary<string, int> Integers { get; } = [];
	internal Dictionary<string, float> Scalars { get; } = [];
	internal Dictionary<string, bool> Flags { get; } = [];
	internal Dictionary<string, Vector3> Vectors { get; } = [];
	internal Dictionary<string, Vector4> Values { get; } = [];
	internal Dictionary<string, Texture> Textures { get; } = [];

	internal SourceMaterialOverrides Snapshot()
	{
		var result = new SourceMaterialOverrides { MapSurface = MapSurface };
		foreach ( var (name, value) in Features )
			result.Features.Add( name, value );
		foreach ( var (name, value) in Integers )
			result.Integers.Add( name, value );
		foreach ( var (name, value) in Scalars )
			result.Scalars.Add( name, value );
		foreach ( var (name, value) in Flags )
			result.Flags.Add( name, value );
		foreach ( var (name, value) in Vectors )
			result.Vectors.Add( name, value );
		foreach ( var (name, value) in Values )
			result.Values.Add( name, value );
		foreach ( var (name, value) in Textures )
			result.Textures.Add( name, value );
		return result;
	}

	internal void Bind( Material material )
	{
		foreach ( var (name, value) in Features )
			material.SetFeature( name, value );
		foreach ( var (name, value) in Integers )
			material.Set( name, value );
		foreach ( var (name, value) in Scalars )
			material.Set( name, value );
		foreach ( var (name, value) in Flags )
			material.Set( name, value );
		foreach ( var (name, value) in Vectors )
			material.Set( name, value );
		foreach ( var (name, value) in Values )
			material.Set( name, value );
		foreach ( var (name, value) in Textures )
			material.Set( name, value );
	}
}
