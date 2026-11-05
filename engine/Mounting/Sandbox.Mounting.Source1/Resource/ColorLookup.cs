using System;
using System.IO;
using System.Runtime.InteropServices;
using Sandbox.Mounting;

namespace Sandbox.Mounting.Source1;

internal sealed class SourceColorLookup
{
	internal const int SourceSize = 32;
	internal const int NativeSize = 16;
	internal const int DataLength = SourceSize * SourceSize * SourceSize * 3;
	private readonly byte[] data;

	internal SourceColorLookup( Stream stream )
	{
		data = new byte[DataLength];
		stream.ReadExactly( data );
		if ( stream.ReadByte() != -1 )
			throw new InvalidDataException( "Source color-correction lookup must contain exactly 32 x 32 x 32 RGB texels." );
	}

	internal Vector3 Sample( Vector3 color )
	{
		var position = color.Clamp( 0, 1 ) * (SourceSize - 1);
		var x = (int)position.x;
		var y = (int)position.y;
		var z = (int)position.z;
		var dx = position.x - x;
		var dy = position.y - y;
		var dz = position.z - z;
		var lower = Vector3.Lerp( Vector3.Lerp( Pixel( x, y, z ), Pixel( x + 1, y, z ), dx ),
			Vector3.Lerp( Pixel( x, y + 1, z ), Pixel( x + 1, y + 1, z ), dx ), dy );
		var upper = Vector3.Lerp( Vector3.Lerp( Pixel( x, y, z + 1 ), Pixel( x + 1, y, z + 1 ), dx ),
			Vector3.Lerp( Pixel( x, y + 1, z + 1 ), Pixel( x + 1, y + 1, z + 1 ), dx ), dy );
		return Vector3.Lerp( lower, upper, dz );
	}

	private Vector3 Pixel( int x, int y, int z )
	{
		var offset = (Math.Min( x, SourceSize - 1 ) + Math.Min( y, SourceSize - 1 ) * SourceSize
			+ Math.Min( z, SourceSize - 1 ) * SourceSize * SourceSize) * 3;
		return new Vector3( data[offset], data[offset + 1], data[offset + 2] ) / 255;
	}

	internal static float[] CreateNativeData( Func<Vector3, Vector3> sample )
	{
		var result = new float[NativeSize * NativeSize * NativeSize * 4];
		for ( var b = 0; b < NativeSize; b++ )
			for ( var g = 0; g < NativeSize; g++ )
				for ( var r = 0; r < NativeSize; r++ )
				{
					var input = new Vector3( r, g, b ) / (NativeSize - 1);
					var output = sample( new Vector3( Gamma( input.x ), Gamma( input.y ), Gamma( input.z ) ) );
					var offset = (b * NativeSize + r + g * NativeSize * NativeSize) * 4;
					result[offset] = SourceDecalPixels.Linear( output.x );
					result[offset + 1] = SourceDecalPixels.Linear( output.y );
					result[offset + 2] = SourceDecalPixels.Linear( output.z );
					result[offset + 3] = 1;
				}
		return result;
	}

	private static float Gamma( float value ) => value <= 0.0031308f ? value * 12.92f : 1.055f * MathF.Pow( value, 1 / 2.4f ) - 0.055f;

	internal static Texture CreateTexture( string name, float[] pixels ) => new Texture2DBuilder()
		.WithName( name )
		.WithAnonymous( false )
		.WithSize( NativeSize * NativeSize, NativeSize )
		.WithFormat( ImageFormat.RGBA32323232F )
		.WithData( MemoryMarshal.AsBytes( pixels.AsSpan() ).ToArray() )
		.WithStaticUsage()
		.Finish();
}

internal sealed class SourceColorLookupLoader( string filename ) : ResourceLoader<BaseMount>
{
	protected override object Load()
	{
		using var stream = Host.OpenContentFile( filename );
		var lookup = new SourceColorLookup( stream );
		return SourceColorLookup.CreateTexture( Path, SourceColorLookup.CreateNativeData( lookup.Sample ) );
	}
}

internal sealed class MapColorLookupLoader( string filename ) : ResourceLoader<BaseMount>
{
	protected override object Load()
	{
		using var map = Host.OpenContentFile( filename );
		var header = new BspHeader( map );
		var entities = BspFile.ReadEntities( BspFile.ReadRaw( map, header.Start, header.Lumps[0] ) );
		var state = SourcePostProcessing.Read( entities );
		var corrections = state.Corrections.Values.Where( correction => correction.IsGlobal && correction.Enabled && correction.Weight > 0 ).ToArray();
		var exclusiveWeight = corrections.LastOrDefault( correction => correction.Entity.Integer( "exclusive" ) != 0 )?.Weight ?? 0;
		var sources = new List<(SourceColorLookup Lookup, float Weight)>();
		var lookups = new Dictionary<string, SourceColorLookup>( StringComparer.OrdinalIgnoreCase );
		foreach ( var correction in corrections )
		{
			var weight = correction.Weight * (correction.Entity.Integer( "exclusive" ) != 0 ? 1 : 1 - exclusiveWeight);
			if ( weight <= 0 )
				continue;
			var lookupFile = correction.Entity.Get( "filename", "" ).Replace( '\\', '/' );
			var root = BaseMount.GetMapPakRoot( filename );
			var path = Host.GetContentPath( lookupFile, root )
				?? throw new FileNotFoundException( $"Color-correction lookup '{correction.Entity.Get( "filename" )}' was not found in '{filename}' content." );
			if ( !lookups.TryGetValue( path, out var lookup ) )
			{
				var stream = Host.OpenContentFile( lookupFile, root );
				try
				{
					lookups.Add( path, lookup = new SourceColorLookup( stream ) );
				}
				finally
				{
					stream.Dispose();
				}
			}
			sources.Add( (lookup, weight) );
		}
		return SourceColorLookup.CreateTexture( Path, CreateNativeData( sources ) );
	}

	internal static float[] CreateNativeData( IEnumerable<(SourceColorLookup Lookup, float Weight)> sources )
	{
		var active = sources.OrderByDescending( source => source.Weight ).Take( 4 ).ToArray();
		var total = active.Sum( source => source.Weight );
		var identity = total <= 1 - 1e-3f ? 1 - total : 0;
		var divisor = identity > 0 ? 1 : Math.Max( total, 1e-6f );
		return SourceColorLookup.CreateNativeData( input =>
		{
			var output = input * identity;
			foreach ( var (lookup, weight) in active )
				output += lookup.Sample( input ) * (weight / divisor);
			return output;
		} );
	}
}
