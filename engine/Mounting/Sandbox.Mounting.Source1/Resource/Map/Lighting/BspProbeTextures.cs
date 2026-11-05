using System;
using System.IO;

namespace Sandbox.Mounting.Source1;

internal sealed class BspProbeTextures( string resourcePrefix )
{
	private readonly List<Texture> textures = [];
	private readonly Dictionary<(ImageFormat Format, ulong Value), Texture> constants = [];
	public int Count => textures.Count;
	public long ByteCount { get; private set; }

	public Texture GetOrCreate( Vector3Int counts, int depth, ImageFormat format, ReadOnlySpan<byte> data )
	{
		int stride = format switch
		{
			ImageFormat.RGBA8888 => 4,
			ImageFormat.RGBA16161616F => 8,
			_ => throw new NotSupportedException( $"Unsupported BSP probe texture format {format}." )
		};
		if ( counts.x is < 1 or > 32 || counts.y is < 1 or > 32 || counts.z is < 1 or > 32
			|| depth != counts.z && depth != counts.z * 6
			|| data.Length != checked(counts.x * counts.y * depth * stride) )
			throw new InvalidDataException( "BSP probe texture dimensions do not match its data." );

		var size = new Vector3Int( counts.x, counts.y, depth );
		bool constant = depth == counts.z && IsConstant( data, stride );
		ulong value = 0;
		if ( constant )
		{
			size = new Vector3Int( 1, 1, 1 );
			data = data[..stride];
			value = stride == 4 ? BitConverter.ToUInt32( data ) : BitConverter.ToUInt64( data );
			if ( constants.TryGetValue( (format, value), out var existing ) )
				return existing;
		}

		var name = $"{resourcePrefix}/probes/{textures.Count}";
		var texture = Texture.CreateVolume( size.x, size.y, size.z, format )
			.WithName( name ).WithStaticUsage().WithData( data.ToArray() ).Finish();
		if ( texture is null || !texture.IsValid || texture.IsError )
			throw new InvalidOperationException( $"{resourcePrefix}: could not create {size} {format} probe texture." );
		textures.Add( texture );
		if ( constant )
			constants.Add( (format, value), texture );
		ByteCount += data.Length;
		return texture;
	}

	private static bool IsConstant( ReadOnlySpan<byte> data, int stride )
	{
		var first = data[..stride];
		for ( int offset = stride; offset < data.Length; offset += stride )
			if ( !data.Slice( offset, stride ).SequenceEqual( first ) )
				return false;
		return true;
	}
}
