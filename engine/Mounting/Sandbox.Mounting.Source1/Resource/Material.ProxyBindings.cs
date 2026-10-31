using System;
using System.IO;
using System.Runtime.InteropServices;
using static Sandbox.Mounting.Source1.MaterialProxyProgram;

namespace Sandbox.Mounting.Source1;

internal sealed partial class SourceMaterialBuilder
{
	private MaterialProxyProgram proxyProgram;
	private Texture proxyTexture;
	private byte[] proxyTextureData;

	private bool TryGetExtendedProxies( VmtFile vmt, string shader,
		out (TextureTransforms Transforms, MaterialAlpha Alpha, TextureAnimation? Animation) result )
	{
		result = default;
		var block = vmt.Root.GetBlock( "proxies" );
		if ( block is null || block.Children.All( IsLegacyProxy ) ) return false;
		if ( !SupportsAlpha( shader ) && shader != "unlittwotexture" ) return false;

		proxyProgram = new MaterialProxyProgram( VmtFile.SelectParameters( vmt ), FullPath );
		try
		{
			proxyProgram.Compile( block, proxy => IsEffectProxy( proxy, shader ), TextureFrames );
		}
		catch ( NotSupportedException e )
		{
			Log.Trace( $"{FullPath}: {e.Message} Using authored values for the proxy block." );
			proxyProgram = null;
			return false;
		}
		var matrices = new Dictionary<string, TextureTransform>( StringComparer.OrdinalIgnoreCase );
		foreach ( var (key, value) in proxyProgram.Outputs )
		{
			if ( !value.Matrix ) continue;
			if ( !SupportsTextureTransform( shader, key ) )
			{
				Log.Trace( $"{FullPath}: proxy texture-transform output '{key}' is not supported by '{shader}'." );
				continue;
			}
			if ( value.IsConstant )
			{
				var m = value.Components.Select( x => x.Constant ).ToArray();
				matrices[key] = new TextureTransform( new Matrix( m[0], m[3], 0, 0, m[1], m[4], 0, 0, 0, 0, 1, 0, m[2], m[5], 0, 1 ), Vector2.Zero );
			}
			else
				matrices[key] = new TextureTransform( Matrix.Identity, Vector2.Zero, new ProxyTransform( proxyProgram, value ) );
		}
		var alpha = new MaterialAlpha( Math.Clamp( parameters.Float( "$alpha", 1 ), 0, 1 ) );
		if ( proxyProgram.Outputs.TryGetValue( "$alpha", out var output ) )
			alpha = new MaterialAlpha( output.IsConstant ? Math.Clamp( output.Scalar.Constant, 0, 1 ) : 1, Dynamic: !output.IsConstant );
		var frameKey = shader is "water" or "refract" or "core" ? "$bumpframe" : "$frame";
		TextureAnimation? animation = null;
		if ( proxyProgram.Outputs.TryGetValue( frameKey, out var frameOutput ) )
			animation = frameOutput.Scalar.IsConstant
				? new TextureAnimation( 0, (int)Math.Clamp( frameOutput.Scalar.Constant, 0, ushort.MaxValue ) )
				: new TextureAnimation( 1 );
		result = (new TextureTransforms( parameters, matrices ), alpha, animation);
		return true;

		int TextureFrames( string key )
		{
			var expected = shader is "water" or "refract" or "core" ? "$normalmap" : "$basetexture";
			if ( !string.Equals( key, expected, StringComparison.OrdinalIgnoreCase ) || !SupportsAnimatedTexture( shader ) ) return 0;
			var texture = parameters.String( expected, shader == "water" ? "dev/water_normal" : shader == "refract" ? parameters.String( "$bumpmap" ) : null );
			if ( string.IsNullOrWhiteSpace( texture ) || texture.StartsWith( "_rt_", StringComparison.OrdinalIgnoreCase ) ) return 0;
			return Host.GetSourceTexture( VmtFile.NormalizePath( texture, ".vtf" ), contentRoot ).Header.Frames;
		}

		bool IsLegacyProxy( VmtNode proxy )
		{
			var p = new VmtParameters( proxy.Values, FullPath );
			if ( proxy.Name.Equals( "TextureScroll", StringComparison.OrdinalIgnoreCase ) )
				return !IsReference( p, "texturescrollrate" ) && !IsReference( p, "texturescrollangle" ) && !IsReference( p, "texturescale" );
			if ( proxy.Name.Equals( "Sine", StringComparison.OrdinalIgnoreCase ) )
				return (p.String( "resultvar" )?.Equals( "$alpha", StringComparison.OrdinalIgnoreCase ) == true
					&& !IsReference( p, "sineperiod" ) && !IsReference( p, "sinemin" )
					&& !IsReference( p, "sinemax" ) && !IsReference( p, "timeoffset" )) || IsEffectProxy( proxy, shader );
			return proxy.Name.Equals( "AnimatedTexture", StringComparison.OrdinalIgnoreCase ) || IsEffectProxy( proxy, shader );
		}
	}

	private static bool IsEffectProxy( VmtNode proxy, string shader )
	{
		if ( shader is "core" or "vortwarp" && proxy.Name.Equals( "EntityOrigin", StringComparison.OrdinalIgnoreCase ) ) return true;
		if ( shader is "aftershock" or "volumeclouds" && proxy.Name.Equals( "CurrentTime", StringComparison.OrdinalIgnoreCase ) ) return true;
		if ( shader == "water" && proxy.Name.Equals( "WaterLOD", StringComparison.OrdinalIgnoreCase ) ) return true;
		return shader == "core" && proxy.Name.Equals( "Sine", StringComparison.OrdinalIgnoreCase )
			&& proxy.Values.TryGetValue( "resultvar", out var target ) && target.Equals( "$flowmaptexcoordoffset", StringComparison.OrdinalIgnoreCase );
	}

	private void BindProxyProgram( Material material )
	{
		if ( proxyProgram is null ) return;
		material.SetFeature( "F_SOURCE_PROXIES", 1 );
		var consumed = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var (key, value) in proxyProgram.Outputs )
		{
			if ( value.Matrix ) continue;
			var binding = key.ToLowerInvariant() switch
			{
				"$alpha" => ("g_flSourceAlpha", "g_flSourceAlphaProxy"),
				"$frame" when shaderName is not ("water" or "refract" or "core") => ("", "g_flSourceFrameProxy"),
				"$bumpframe" when shaderName is "water" or "refract" or "core" => ("", "g_flSourceFrameProxy"),
				"$detailblendfactor" when IsSurfaceShader( material ) => ("g_flSourceDetailBlendFactor", "g_flSourceDetailBlendProxy"),
				_ => default
			};
			if ( binding.Item2 is null ) continue;
			consumed.Add( key );
			if ( binding.Item2 == "g_flSourceFrameProxy" && animation is { StaticFrame: not null } ) continue;
			var expression = binding.Item2 == "g_flSourceFrameProxy"
				? TextureFrame( value.Scalar, Math.Max( 1, (int)material.GetVector4( "g_vSourceTextureAnimation" ).y ) )
				: value.Scalar;
			if ( value.IsConstant && binding.Item1.Length > 0 )
				material.Set( binding.Item1, key == "$alpha" ? Math.Clamp( value.Scalar.Constant, 0, 1 ) : value.Scalar.Constant );
			else
				material.Set( binding.Item2, (float)proxyProgram.Emit( expression ) );
		}

		if ( IsSurfaceShader( material ) || shaderName is "modulate" or "unlittwotexture" )
		{
			if ( proxyProgram.Outputs.ContainsKey( "$color" ) || proxyProgram.Outputs.ContainsKey( "$color2" ) )
			{
				var a = Tint( "$color" );
				var b = Tint( "$color2" );
				var hdr = shaderName is "vertexlitgeneric" or "unlitgeneric" or "vortwarp" or "modulate" or "unlittwotexture";
				var nodes = Enumerable.Range( 0, 3 ).Select( i => Linear( Make( Operation.Multiply, ComponentAt( a, i ), ComponentAt( b, i ) ), hdr ) ).ToArray();
				BindVector( "g_vSourceColorTint", "g_vSourceColorTintProxy", nodes );
				consumed.Add( "$color" );
				consumed.Add( "$color2" );
			}
			foreach ( var key in new[] { "$envmaptint", "$selfillumtint" } )
			{
				if ( !proxyProgram.Outputs.TryGetValue( key, out var value ) || !IsSurfaceShader( material ) ) continue;
				var linear = key == "$envmaptint" ? !(shaderName == "vertexlitgeneric" && parameters.Boolean( "$phong" )) : shaderName == "worldtwotextureblend";
				var nodes = Enumerable.Range( 0, 3 ).Select( i => linear ? Linear( ComponentAt( value, i ), false ) : ComponentAt( value, i ) ).ToArray();
				var slot = key == "$envmaptint" ? "EnvTint" : "SelfIllumTint";
				BindVector( $"g_vSource{slot}", $"g_vSource{slot}Proxy", nodes );
				consumed.Add( key );
			}
		}
		foreach ( var key in proxyProgram.Outputs.Keys )
		{
			if ( !consumed.Contains( key ) && !proxyProgram.Outputs[key].Matrix && !proxyProgram.Inputs.Contains( key ) )
				Log.Trace( $"{FullPath}: proxy output '{key}' is not bound by '{shaderName}'; retaining its authored rendering parameter." );
		}
		if ( !proxyProgram.HasInstructions )
		{
			material.SetFeature( "F_SOURCE_PROXIES", 0 );
			return;
		}
		var data = proxyProgram.Instructions.ToArray();
		var bytes = MemoryMarshal.AsBytes( data.AsSpan() ).ToArray();
		if ( proxyTexture is not { IsValid: true, IsError: false } || proxyTextureData is null || !bytes.AsSpan().SequenceEqual( proxyTextureData ) )
		{
			proxyTexture = Texture.Create( 1, data.Length, ImageFormat.RGBA32323232F )
				.WithData( bytes ).WithStaticUsage().Finish();
			if ( proxyTexture is not { IsValid: true, IsError: false } )
				throw new InvalidDataException( $"{FullPath}: could not create the material proxy program." );
			proxyTextureData = bytes;
		}
		material.Set( "g_tSourceProxyProgram", proxyTexture );
		material.Set( "g_nSourceProxyCount", (float)data.Length );

		Value Tint( string key )
		{
			if ( proxyProgram.Outputs.TryGetValue( key, out var value ) ) return value;
			var authored = parameters.Vector( key, Vector3.One );
			return new Value( [Constant( authored.x ), Constant( authored.y ), Constant( authored.z )] );
		}

		void BindVector( string uniform, string binding, Expression[] nodes )
		{
			if ( nodes.All( x => x.IsConstant ) )
				material.Set( uniform, new Vector3( nodes[0].Constant, nodes[1].Constant, nodes[2].Constant ) );
			else
				material.Set( binding, new Vector3( proxyProgram.Emit( nodes[0] ), proxyProgram.Emit( nodes[1] ), proxyProgram.Emit( nodes[2] ) ) );
		}
	}

	private static Expression Linear( Expression value, bool hdr )
	{
		var linear = Make( Operation.Pow, Make( Operation.Max, value, Constant( 0 ) ), Constant( 2.2f ) );
		return hdr ? Make( Operation.Select, Make( Operation.LessOrEqual, value, Constant( 1 ) ), linear, value ) : linear;
	}

	private sealed record ProxyTransform( MaterialProxyProgram Program, MaterialProxyProgram.Value Value )
	{
		public void Bind( Material material, string slot, float scale )
		{
			var indices = Value.Components.Select( x => Program.Emit( Make( Operation.Multiply, x, Constant( scale ) ) ) ).ToArray();
			material.Set( $"g_vSource{slot}ProxyU", new Vector3( indices[0], indices[1], indices[2] ) );
			material.Set( $"g_vSource{slot}ProxyV", new Vector3( indices[3], indices[4], indices[5] ) );
		}
	}
}
