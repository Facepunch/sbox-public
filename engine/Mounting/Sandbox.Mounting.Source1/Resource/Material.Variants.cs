using System;

namespace Sandbox.Mounting.Source1;

internal sealed class SourceMaterialVariant
{
	internal object Scope { get; }
	private readonly SourceMaterial source;
	private readonly string nativeShader;

	internal SourceMaterialVariant( SourceMaterial source )
	{
		this.source = source;
		Scope = source.Scope;
	}

	private SourceMaterialVariant( string shader, object scope )
	{
		nativeShader = shader;
		Scope = scope;
	}

	internal static SourceMaterialVariant ReadErrorMaterial( Material material, object scope )
	{
		var name = material.Name?.Replace( '\\', '/' ).ToLowerInvariant();
		if ( name != "materials/error.vmat" ) return null;
		var shader = material.ShaderName;
		return shader is "shaders/error.shader" or "shaders/error.shader_c"
			? new SourceMaterialVariant( shader, scope ) : null;
	}

	internal Material Create( string name )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( name );
		return source is not null ? source.CreateMaterial( name )
			: Material.Create( name, nativeShader, anonymous: false )
				?? throw new InvalidOperationException( $"Could not create named engine fallback '{name}' using '{nativeShader}'." );
	}
}
