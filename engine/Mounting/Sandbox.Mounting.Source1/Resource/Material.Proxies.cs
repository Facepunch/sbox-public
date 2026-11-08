using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Sandbox.Mounting.Source1;

internal sealed partial class SourceMaterialBuilder
{
	private readonly record struct TextureAnimation( float FrameRate, int? StaticFrame = null )
	{
		public void Bind( Material material, SurfaceTexture texture )
		{
			var enabled = FrameRate > 0 && texture?.Header.Frames > 1;
			material.SetFeature( "F_SOURCE_TEXTURE_ANIMATION", enabled ? 1 : 0 );
			if ( enabled )
				material.Set( "g_vSourceTextureAnimation", new Vector4( FrameRate, texture.Header.Frames, 0, 0 ) );
		}
	}

	private readonly record struct MaterialAlpha( float Value, Vector4? Sine = null, bool Dynamic = false )
	{
		public bool CanBeTranslucent => Dynamic || (Sine is { } wave ? Math.Min( wave.z, wave.w ) < 1 : Value < 1);

		public void Bind( Material material )
		{
			material.Set( "g_flSourceAlpha", Value );
			material.SetFeature( "F_SOURCE_ALPHA_SINE", Sine.HasValue ? 1 : 0 );
			if ( Sine is { } wave )
				material.Set( "g_vSourceAlphaSine", wave );
		}
	}

	private (TextureTransforms Transforms, MaterialAlpha Alpha, TextureAnimation? Animation) GetMaterialProxies( VmtFile vmt, string shader, VmtParameters parameters )
	{
		if ( TryGetExtendedProxies( vmt, shader, out var extended ) )
			return extended;
		var alpha = new MaterialAlpha( SupportsAlpha( shader ) ? Math.Clamp( parameters.Float( "$alpha", 1 ), 0, 1 ) : 1 );
		TextureAnimation? animation = null;
		var unchanged = (new TextureTransforms( parameters, null ), alpha, animation);
		var proxies = vmt.Root.GetBlock( "proxies" );
		if ( proxies == null || proxies.Children.Count == 0 )
			return unchanged;

		foreach ( var proxy in proxies.Children )
		{
			if ( !proxy.IsBlock || !(proxy.Name.Equals( "TextureScroll", StringComparison.OrdinalIgnoreCase )
				|| proxy.Name.Equals( "Sine", StringComparison.OrdinalIgnoreCase )
				|| proxy.Name.Equals( "AnimatedTexture", StringComparison.OrdinalIgnoreCase )
				|| (shader is "core" or "vortwarp" && proxy.Name.Equals( "EntityOrigin", StringComparison.OrdinalIgnoreCase ))
				|| (shader is "aftershock" or "volumeclouds" && proxy.Name.Equals( "CurrentTime", StringComparison.OrdinalIgnoreCase ))
				|| (shader == "water" && proxy.Name.Equals( "WaterLOD", StringComparison.OrdinalIgnoreCase ))) )
			{
				Log.Trace( $"{FullPath}: proxy '{proxy.Name}' is not implemented; using static parameters for the entire proxy block to avoid partial proxy dependencies." );
				return unchanged;
			}
		}

		Dictionary<string, TextureTransform> scrolls = null;
		foreach ( var proxy in proxies.Children )
		{
			var inputs = new VmtParameters( proxy.Values, FullPath );
			if ( (shader == "vortwarp" && proxy.Name.Equals( "EntityOrigin", StringComparison.OrdinalIgnoreCase ))
				|| (shader is "aftershock" or "volumeclouds" && proxy.Name.Equals( "CurrentTime", StringComparison.OrdinalIgnoreCase ))
				|| (shader == "core" && (proxy.Name.Equals( "EntityOrigin", StringComparison.OrdinalIgnoreCase )
				|| (proxy.Name.Equals( "Sine", StringComparison.OrdinalIgnoreCase )
					&& inputs.String( "resultvar" )?.Equals( "$flowmaptexcoordoffset", StringComparison.OrdinalIgnoreCase ) == true))) )
			{
				continue;
			}
			else if ( proxy.Name.Equals( "WaterLOD", StringComparison.OrdinalIgnoreCase ) )
			{
				Log.Trace( $"{FullPath}: WaterLOD's global distance controls are not imported; using the material's authored water mode without freezing independent wave proxies." );
			}
			else if ( proxy.Name.Equals( "TextureScroll", StringComparison.OrdinalIgnoreCase ) )
			{
				var target = inputs.String( "texturescrollvar" )?.ToLowerInvariant();
				if ( !SupportsTextureTransform( shader, target ) )
				{
					Log.Trace( $"{FullPath}: TextureScroll target '{target}' is not supported by '{shader}'; using static parameters for the entire proxy block." );
					return unchanged;
				}
				if ( IsReference( inputs, "texturescrollrate" ) || IsReference( inputs, "texturescrollangle" ) || IsReference( inputs, "texturescale" ) )
				{
					Log.Trace( $"{FullPath}: TextureScroll material-variable inputs are not implemented; using static parameters for the entire proxy block." );
					return unchanged;
				}

				var rate = inputs.Float( "texturescrollrate", 1 );
				var angle = inputs.Float( "texturescrollangle" ) * (Math.PI / 180);
				var scale = inputs.Float( "texturescale", 1 );
				var velocity = new Vector2( (float)(Math.Cos( angle ) * rate), (float)(Math.Sin( angle ) * rate) );
				scrolls ??= new Dictionary<string, TextureTransform>( StringComparer.OrdinalIgnoreCase );
				scrolls[target] = new TextureTransform( Matrix.CreateScale( new Vector3( scale, scale, 1 ) ), velocity );
			}
			else if ( proxy.Name.Equals( "Sine", StringComparison.OrdinalIgnoreCase ) )
			{
				var target = inputs.String( "resultvar" );
				if ( !SupportsAlpha( shader ) || !string.Equals( target, "$alpha", StringComparison.OrdinalIgnoreCase ) )
				{
					Log.Trace( $"{FullPath}: Sine target '{target}' is not supported by '{shader}'; using static parameters for the entire proxy block." );
					return unchanged;
				}
				if ( IsReference( inputs, "sineperiod" ) || IsReference( inputs, "sinemin" )
					|| IsReference( inputs, "sinemax" ) || IsReference( inputs, "timeoffset" ) )
				{
					Log.Trace( $"{FullPath}: Sine material-variable inputs are not implemented; using static parameters for the entire proxy block." );
					return unchanged;
				}
				alpha = GetSineAlpha( inputs );
			}
			else
			{
				var effect = shader is "water" or "refract" or "core";
				var textureKey = effect ? "$normalmap" : "$basetexture";
				var frameKey = effect ? "$bumpframe" : "$frame";
				var texture = parameters.String( textureKey, shader == "water" ? "dev/water_normal"
					: shader == "refract" ? parameters.String( "$bumpmap" ) : null );
				if ( !SupportsAnimatedTexture( shader )
					|| !string.Equals( inputs.String( "animatedtexturevar" ), textureKey, StringComparison.OrdinalIgnoreCase )
					|| !string.Equals( inputs.String( "animatedtextureframenumvar" ), frameKey, StringComparison.OrdinalIgnoreCase )
					|| string.IsNullOrWhiteSpace( texture ) )
				{
					Log.Trace( $"{FullPath}: AnimatedTexture requires a supported {textureKey}/{frameKey} target; using static parameters for the entire proxy block." );
					return unchanged;
				}
				if ( IsReference( inputs, "animatedtextureframerate" ) || IsReference( inputs, "animationnowrap" ) )
				{
					Log.Trace( $"{FullPath}: AnimatedTexture material-variable inputs are not implemented; using static parameters for the entire proxy block." );
					return unchanged;
				}
				var rate = inputs.Float( "animatedtextureframerate", 15 );
				if ( rate < 0 || inputs.Boolean( "animationnowrap" ) )
				{
					Log.Trace( $"{FullPath}: reverse and non-looping AnimatedTexture playback are not implemented; using static parameters for the entire proxy block." );
					return unchanged;
				}
				animation = new TextureAnimation( rate );
			}
		}
		return (new TextureTransforms( parameters, scrolls ), alpha, animation);
	}

	private MaterialAlpha GetSineAlpha( VmtParameters inputs )
	{
		var period = inputs.Float( "sineperiod", 1 );
		var min = inputs.Float( "sinemin" );
		var max = inputs.Float( "sinemax", 1 );
		var offset = inputs.Float( "timeoffset" );
		var clampedMin = Math.Clamp( min, 0, 1 );
		if ( clampedMin == Math.Clamp( max, 0, 1 ) )
			return new MaterialAlpha( clampedMin );

		if ( period == 0 )
			period = 1;
		if ( !float.IsFinite( (float)(Math.Tau / period) ) || !float.IsFinite( max - min ) )
			throw new InvalidDataException( $"{FullPath}: Sine parameters overflow shader evaluation." );
		return new MaterialAlpha( 1, new Vector4( period, offset, min, max ) );
	}

	private static bool IsReference( VmtParameters inputs, string key ) => inputs.String( key ) is { } text
		&& !float.TryParse( text, NumberStyles.Float, CultureInfo.InvariantCulture, out _ );

	private static bool SupportsAlpha( string shader ) => shader is "vertexlitgeneric" or "lightmappedgeneric"
		or "unlitgeneric" or "worldvertextransition" or "modulate" or "water" or "refract" or "vortwarp" or "worldtwotextureblend";

	private static bool SupportsAnimatedTexture( string shader ) => shader is "vertexlitgeneric" or "lightmappedgeneric"
		or "unlitgeneric" or "worldvertextransition" or "modulate" or "unlittwotexture" or "water" or "refract" or "core" or "vortwarp" or "worldtwotextureblend";
}
