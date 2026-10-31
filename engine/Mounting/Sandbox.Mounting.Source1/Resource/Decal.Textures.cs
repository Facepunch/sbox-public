using System;
using System.IO;
using System.Runtime.InteropServices;
using Sandbox.Mounting;
using Sandbox.Rendering;

namespace Sandbox.Mounting.Source1;

internal sealed class SourceDecalTextureLoader( SourceDecalMaterial material, bool normal ) : ResourceLoader<BaseMount>
{
	protected override object Load()
	{
		var parameters = material.Parameters;
		var path = VmtFile.NormalizePath( parameters.String( normal ? "$bumpmap" : "$basetexture" ), ".vtf" );
		var root = BaseMount.GetContentRoot( material.Path, "materials" );
		var source = Host.GetSourceTexture( path, root );
		var frame = parameters.Integer( normal ? "$bumpframe" : "$frame" );
		var vtf = source.ReadFrame( frame );
		var rect = material.Subrect ?? new SourceDecalMaterial.Rectangle( 0, 0, vtf.Header.Width, vtf.Header.Height );
		if ( normal && material.Subrect.HasValue )
		{
			var color = Host.GetSourceTexture( parameters.String( "$basetexture" ), root );
			rect = new( rect.X * vtf.Header.Width / color.Header.Width, rect.Y * vtf.Header.Height / color.Header.Height,
				rect.Width * vtf.Header.Width / color.Header.Width, rect.Height * vtf.Header.Height / color.Header.Height );
		}
		var pixels = SourceDecalPixels.Convert( vtf, frame, material, rect, normal );
		var mips = new List<Vector4[]> { pixels };
		if ( (vtf.Header.Flags & VtfFlags.NOMIP) == 0 )
		{
			var width = rect.Width;
			var height = rect.Height;
			while ( width > 1 || height > 1 )
			{
				var nextWidth = Math.Max( 1, width / 2 );
				var nextHeight = Math.Max( 1, height / 2 );
				mips.Add( SourceDecalPixels.Mip( mips[^1], width, height, normal ) );
				width = nextWidth;
				height = nextHeight;
			}
		}
		var count = mips.Sum( mip => checked(mip.Length * 4) );
		var data = new Half[count];
		var offset = 0;
		for ( var mip = mips.Count - 1; mip >= 0; mip-- )
			foreach ( var pixel in mips[mip] )
			{
				data[offset++] = (Half)pixel.x;
				data[offset++] = (Half)pixel.y;
				data[offset++] = (Half)pixel.z;
				data[offset++] = (Half)pixel.w;
			}
		return new Texture2DBuilder().WithFormat( ImageFormat.RGBA16161616F ).WithSize( rect.Width, rect.Height )
			.WithMips( mips.Count ).WithData( MemoryMarshal.AsBytes( data.AsSpan() ).ToArray() ).WithStaticUsage().Finish();
	}
}

internal static class SourceDecalPixels
{
	internal static Vector4[] Convert( VtfFile vtf, int frame, SourceDecalMaterial material,
		SourceDecalMaterial.Rectangle rect, bool normal )
	{
		if ( rect.X < 0 || rect.Y < 0 || rect.Width <= 0 || rect.Height <= 0
			|| (long)rect.X + rect.Width > vtf.Header.Width || (long)rect.Y + rect.Height > vtf.Header.Height )
			throw new InvalidDataException( $"{material.Path}: decal subrect extends outside its texture." );
		var source = vtf.ReadRgbaPixels( frame, (rect.X, rect.Y, rect.Width, rect.Height) );
		var output = new Vector4[checked(rect.Width * rect.Height)];
		var parameters = material.Parameters;
		var transform = parameters.Transform( normal ? "$bumptransform" : "$basetexturetransform" );
		var scale = !normal && material.BlendMode == DecalBlendMode.Multiply ? material.ModulationScale : 1;
		var hdr = vtf.Header.HiResFormat is VtfFormat.RGBA16161616F or VtfFormat.RGB323232F or VtfFormat.RGBA32323232F;
		var alphaTest = !normal && parameters.Boolean( "$alphatest" );
		var alphaReference = alphaTest ? parameters.Float( "$alphatestreference", 0.5f ) : 0;
		for ( var y = 0; y < rect.Height; y++ )
			for ( var x = 0; x < rect.Width; x++ )
			{
				var uv = transform.Transform( new Vector2( (x + 0.5f) / rect.Width, (y + 0.5f) / rect.Height ) );
				if ( !float.IsFinite( uv.x ) || !float.IsFinite( uv.y ) )
					throw new InvalidDataException( $"{material.Path}: decal texture coordinates overflow." );
				var sx = Math.Clamp( (int)MathF.Floor( Math.Clamp( uv.x, 0, 1 ) * rect.Width ), 0, rect.Width - 1 );
				var sy = Math.Clamp( (int)MathF.Floor( Math.Clamp( uv.y, 0, 1 ) * rect.Height ), 0, rect.Height - 1 );
				var pixel = source[sy * rect.Width + sx];
				if ( normal )
				{
					if ( vtf.Header.HiResFormat == VtfFormat.UV88 )
					{
						var nx = Math.Max( -1, (sbyte)(byte)MathF.Round( pixel.x * 255 ) / 127f );
						var ny = Math.Max( -1, (sbyte)(byte)MathF.Round( pixel.y * 255 ) / 127f );
						pixel = new( nx * 0.5f + 0.5f, 0.5f - ny * 0.5f, MathF.Sqrt( Math.Max( 0, 1 - nx * nx - ny * ny ) ) * 0.5f + 0.5f, 1 );
					}
					else if ( (vtf.Header.Flags & VtfFlags.SSBUMP) != 0 )
					{
						var n = new Vector3( 0.81649658f * pixel.x - 0.40824829f * (pixel.y + pixel.z),
							0.70710678f * (pixel.y - pixel.z), 0.57735027f * (pixel.x + pixel.y + pixel.z) );
						n = n.LengthSquared > 1e-8f ? n.Normal : Vector3.Up;
						pixel = new( n.x * 0.5f + 0.5f, 0.5f - n.y * 0.5f, n.z * 0.5f + 0.5f, pixel.w );
					}
					else
						pixel.y = 1 - pixel.y;
				}
				else
				{
					pixel.x = hdr ? pixel.x * scale : Linear( pixel.x * scale );
					pixel.y = hdr ? pixel.y * scale : Linear( pixel.y * scale );
					pixel.z = hdr ? pixel.z * scale : Linear( pixel.z * scale );
					if ( alphaTest )
						pixel.w = pixel.w >= alphaReference ? 1 : 0;
					else if ( material.Shader == "decalmodulate" )
						pixel.w = pixel.w > 0 ? 1 : 0;
				}
				if ( !float.IsFinite( pixel.x ) || !float.IsFinite( pixel.y ) || !float.IsFinite( pixel.z ) || !float.IsFinite( pixel.w )
					|| Math.Abs( pixel.x ) > 65504 || Math.Abs( pixel.y ) > 65504 || Math.Abs( pixel.z ) > 65504 )
					throw new InvalidDataException( $"{material.Path}: decal pixels exceed the finite half-float range." );
				pixel.w = Math.Clamp( pixel.w, 0, 1 );
				output[y * rect.Width + x] = pixel;
			}
		return output;
	}

	internal static Vector4[] Mip( Vector4[] pixels, int width, int height, bool normal )
	{
		var nextWidth = Math.Max( 1, width / 2 );
		var nextHeight = Math.Max( 1, height / 2 );
		var output = new Vector4[checked(nextWidth * nextHeight)];
		for ( var y = 0; y < nextHeight; y++ )
			for ( var x = 0; x < nextWidth; x++ )
			{
				var sum = Vector4.Zero;
				var count = 0;
				for ( var sy = y * height / nextHeight; sy < (y + 1) * height / nextHeight; sy++ )
					for ( var sx = x * width / nextWidth; sx < (x + 1) * width / nextWidth; sx++ )
					{
						var p = pixels[sy * width + sx];
						sum += new Vector4( p.x * p.w, p.y * p.w, p.z * p.w, p.w );
						count++;
					}
				var color = sum.w > 0 ? sum / sum.w : new Vector4( 0.5f, 0.5f, 1, 0 );
				if ( normal )
				{
					var n = new Vector3( color.x * 2 - 1, color.y * 2 - 1, color.z * 2 - 1 );
					n = n.LengthSquared > 1e-8f ? n.Normal : Vector3.Up;
					color = new( n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1 );
				}
				color.w = sum.w / count;
				output[y * nextWidth + x] = color;
			}
		return output;
	}

	internal static float Linear( float value ) => value <= 0.04045f ? value / 12.92f : MathF.Pow( (value + 0.055f) / 1.055f, 2.4f );
}
