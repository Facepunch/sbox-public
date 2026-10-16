using System;
using System.Collections.Generic;
using System.IO;
using Sandbox.Mounting;

namespace Sandbox.Mounting.Source1;

internal sealed partial class TextureLoader( string filename, int? frame = 0 ) : ResourceLoader<BaseMount>
{
	internal static string FramePath( string filename, int? frame ) => frame switch
	{
		0 => $"{filename}.vtex",
		null => $"{filename}.animation.vtex",
		_ => $"{filename}.frame_{frame}.vtex"
	};

	protected override object Load()
	{
		var texture = CreateTexture( Host.GetSourceTexture( filename ).ReadTexture( frame ), Path, frame );
		if ( texture is null || !texture.IsValid || texture.IsError )
			throw new InvalidDataException( $"Texture '{filename}' frame {frame} could not be loaded." );
		return texture;
	}

	internal static Texture CreateTexture( VtfFile vtf, string filename, int? frame )
	{
		var width = vtf.Header.Width;
		var height = vtf.Header.Height;
		var srcFormat = vtf.Header.HiResFormat;
		if ( width > short.MaxValue || height > short.MaxValue )
			throw new InvalidDataException( $"VTF dimensions exceed the texture builder limits: {filename}" );
		if ( vtf.ZSliceCount != 1 )
		{
			Log.Trace( $"Volume VTF textures are not supported: {filename}" );
			return default;
		}
		var isCube = vtf.FaceCount > 1;
		var frameCount = frame.HasValue ? 1 : vtf.FrameCount;
		if ( !frame.HasValue && isCube )
			throw new NotSupportedException( $"Animated cubemap VTF textures are not supported: {filename}" );
		if ( frameCount > short.MaxValue )
			throw new InvalidDataException( $"VTF frame count exceeds the texture builder limits: {filename}" );
		if ( isCube && width != height )
			throw new InvalidDataException( $"VTF cubemap faces must be square: {filename}" );

		var (format, data, mipCount) = ReadTextureData( vtf, frame );
		if ( format == ImageFormat.None )
		{
			Log.Trace( $"Unsupported texture format {srcFormat} for {filename}" );
			return default;
		}

		if ( frameCount > 1 )
		{
			return new TextureArrayBuilder()
				.WithFormat( format )
				.WithSize( width, height )
				.WithCount( frameCount )
				.WithData( data )
				.WithMips( mipCount )
				.WithStaticUsage()
				.Finish();
		}

		if ( isCube )
		{
			return new TextureCubeBuilder()
				.WithFormat( format )
				.WithSize( width, height )
				.WithData( data )
				.WithMips( mipCount )
				.WithStaticUsage()
				.Finish();
		}

		return new Texture2DBuilder()
			.WithFormat( format )
			.WithSize( width, height )
			.WithData( data )
			.WithMips( mipCount )
			.WithStaticUsage()
			.Finish();
	}
}
