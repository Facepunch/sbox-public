using System;
using System.Collections.Generic;
using System.IO;

namespace Sandbox.Mounting.Source1;

internal sealed partial class SourceMaterialBuilder
{
	private readonly record struct TextureTransform( Matrix Transform, Vector2 Scroll, ProxyTransform Proxy = null )
	{
		public void Bind( Material material, string slot, float scale = 1 )
		{
			var matrix = Transform * Matrix.CreateScale( new Vector3( scale, scale, 1 ) );
			if ( !float.IsFinite( matrix.M11 ) || !float.IsFinite( matrix.M12 )
				|| !float.IsFinite( matrix.M21 ) || !float.IsFinite( matrix.M22 )
				|| !float.IsFinite( matrix.M41 ) || !float.IsFinite( matrix.M42 ) )
				throw new InvalidDataException( $"{material.Name}: {slot} texture transform overflows." );
			material.Set( $"g_vSource{slot}TransformU", new Vector3( matrix.M11, matrix.M21, matrix.M41 ) );
			material.Set( $"g_vSource{slot}TransformV", new Vector3( matrix.M12, matrix.M22, matrix.M42 ) );
			material.Set( $"g_vSource{slot}Scroll", new Vector3( Scroll.x, Scroll.y, scale ) );
			Proxy?.Bind( material, slot, scale );
		}
	}

	private readonly struct TextureTransforms( VmtParameters parameters, IReadOnlyDictionary<string, TextureTransform> scrolls )
	{
		public bool Contains( string key ) => parameters.Contains( key ) || scrolls?.ContainsKey( key ) == true;

		public TextureTransform Get( string key, TextureTransform? fallback = null )
		{
			if ( scrolls != null && scrolls.TryGetValue( key, out var scroll ) )
				return scroll;
			if ( parameters.Contains( key ) )
				return new TextureTransform( parameters.Transform( key ), Vector2.Zero );
			return fallback ?? new TextureTransform( Matrix.Identity, Vector2.Zero );
		}
	}

	private static bool SupportsTextureTransform( string shader, string key ) => shader switch
	{
		"water" or "refract" or "core" => key is "$bumptransform" or "$normalmaptransform" or "$bumptransform2",
		"unlittwotexture" => key is "$basetexturetransform" or "$texture2transform",
		"modulate" => key == "$basetexturetransform",
		"worldtwotextureblend" => key is "$basetexturetransform" or "$bumptransform",
		"vertexlitgeneric" or "lightmappedgeneric" or "unlitgeneric" or "worldvertextransition" or "vortwarp"
			=> key is "$basetexturetransform" or "$basetexturetransform2" or "$bumptransform" or "$bumptransform2"
				or "$detailtexturetransform" or "$envmapmasktransform" or "$blendmasktransform",
		_ => false
	};
}
