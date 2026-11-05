using System;
using System.IO;
using static Sandbox.Mounting.Source1.BspGeometry;

namespace Sandbox.Mounting.Source1;

partial class MapLoader
{
	private const int LightmapAtlasSize = 2048;
	private readonly List<LightmapAtlas> _lightmapPages = [];
	FaceSurface[] BuildLightmaps( BspFile bsp, bool skybox )
	{
		_lightmapPages.Clear();
		var patterns = BuildLightstylePatterns( bsp );
		var surfaces = new FaceSurface[bsp.Faces.Length];
		var world = bsp.Models[0];
		for ( int i = 0; i < bsp.Faces.Length; i++ )
		{
			ref var face = ref bsp.Faces[i];
			if ( i >= world.FirstFace && i < world.FirstFace + world.FaceCount
				&& (skybox ? !bsp.IsActiveSkyboxFace( i ) : bsp.IsSkyboxFace( i )) )
				continue;
			if ( !IsRenderable( bsp, face ) || face.LightOffset == -1 || face.Style0 == 255 )
				continue;
			int width = checked(face.LightmapSizeS + 1);
			int height = checked(face.LightmapSizeT + 1);
			if ( width <= 0 || height <= 0 || width > LightmapAtlasSize || height > LightmapAtlasSize )
				throw new InvalidDataException( $"Face {i} has invalid lightmap dimensions {width}x{height}." );
			bool bumped = (bsp.TexInfos[face.TexInfo].Flags & BspSurfaceFlags.BumpLight) != 0;
			int planes = bumped ? 4 : 1;
			int planeBytes = checked(width * height * 4);
			int count = face.Style1 == 255 ? 1 : face.Style2 == 255 ? 2 : face.Style3 == 255 ? 3 : 4;
			int sourcePlanes = planes + (bsp.LightstylesWithCsm ? 1 : 0);
			BspFile.ValidateRange( face.LightOffset, (long)planeBytes * sourcePlanes * count, bsp.LightingData.Length, $"Face {i} lightmap styles" );
			LightmapNode node = null;
			int pageIndex = 0;
			for ( ; pageIndex < _lightmapPages.Count; pageIndex++ )
			{
				if ( _lightmapPages[pageIndex].Bumped != bumped )
					continue;
				node = _lightmapPages[pageIndex].Allocate( width, height );
				if ( node is not null )
					break;
			}

			if ( node is null )
			{
				var atlas = new LightmapAtlas( LightmapAtlasSize, bumped );
				_lightmapPages.Add( atlas );
				node = atlas.Allocate( width, height ) ?? throw new InvalidDataException( $"Face {i} does not fit an empty lightmap atlas." );
			}

			surfaces[i] = new FaceSurface
			{
				OffsetX = node.X,
				OffsetY = node.Y,
				Width = width,
				Height = height,
				Page = pageIndex,
				StyleCount = count,
				LightmapMinsS = face.LightmapMinsS,
				LightmapMinsT = face.LightmapMinsT
			};
		}

		int stride = bsp.Hdr ? 8 : 4;
		foreach ( var atlas in _lightmapPages )
			atlas.FinalizePacking( bsp.Hdr );

		Span<byte> styles = stackalloc byte[4];
		Span<float> weights = stackalloc float[4];
		byte[] styleBuffer = [];
		byte[] encodedBuffer = [];
		for ( int i = 0; i < surfaces.Length; i++ )
		{
			ref var surface = ref surfaces[i];
			if ( surface.Width == 0 )
				continue;
			ref var face = ref bsp.Faces[i];
			var atlas = _lightmapPages[surface.Page];
			surface.InverseAtlasSize = new Vector2( 1.0f / atlas.Width, 1.0f / atlas.Height );
			int width = surface.Width, height = surface.Height;
			int planes = atlas.PlaneCount;
			int planeBytes = checked(width * height * 4);
			styles[0] = face.Style0;
			styles[1] = face.Style1;
			styles[2] = face.Style2;
			styles[3] = face.Style3;
			int count = surface.StyleCount;
			for ( int style = 0; style < count; style++ )
				weights[style] = styles[style] < LightmapCodec.StyleCount ? LightmapCodec.StyleValue( patterns[styles[style]], 0 ) : 1.0f;
			int sourcePlanes = planes + (bsp.LightstylesWithCsm ? 1 : 0);
			ReadOnlySpan<byte> samples = bsp.LightingData.AsSpan( face.LightOffset, planeBytes * planes * count );
			if ( bsp.LightstylesWithCsm )
			{
				int sampleBytes = checked(planeBytes * planes * count);
				if ( styleBuffer.Length < sampleBytes )
					styleBuffer = new byte[sampleBytes];
				var rgbSamples = styleBuffer.AsSpan( 0, sampleBytes );
				for ( int style = 0; style < count; style++ )
					bsp.LightingData.AsSpan( face.LightOffset + style * sourcePlanes * planeBytes, planes * planeBytes ).CopyTo( rgbSamples[(style * planes * planeBytes)..] );
				samples = rgbSamples;
			}

			int encodedBytes = checked(width * height * planes * stride);
			if ( encodedBuffer.Length < encodedBytes )
				encodedBuffer = new byte[encodedBytes];
			var encoded = encodedBuffer.AsSpan( 0, encodedBytes );
			LightmapCodec.Encode( samples, width * height, planes, weights[..count], bsp.Hdr, encoded );
			for ( int plane = 0; plane < planes; plane++ )
				for ( int y = 0; y < height; y++ )
				{
					int from = (plane * height + y) * width * stride;
					int to = ((plane * atlas.Height + surface.OffsetY + y) * atlas.Width + surface.OffsetX) * stride;
					encoded.Slice( from, width * stride ).CopyTo( atlas.Data.AsSpan( to, width * stride ) );
				}
		}

		for ( int page = 0; page < _lightmapPages.Count; page++ )
			_lightmapPages[page].Upload( $"{ResourcePrefix}/lightmaps/{page}", bsp.Hdr );

		return surfaces;
	}

	void BindLightmap( SourceMaterialOverrides bindings, int page )
	{
		var textures = _lightmapPages[page].Textures;
		bindings.Textures["g_tSourceLightmap"] = textures[0];
		bindings.Scalars["g_flSourceHasBumpLightmap"] = textures.Length == 4 ? 1f : 0f;
		for ( int plane = 1; plane < textures.Length; plane++ )
			bindings.Textures[$"g_tSourceLightmap{plane}"] = textures[plane];
	}

	struct FaceSurface
	{
		public int OffsetX, OffsetY, Width, Height, Page, StyleCount;
		public int LightmapMinsS, LightmapMinsT;
		public Vector2 InverseAtlasSize;
	}

	class LightmapAtlas( int size, bool bumped )
	{
		readonly LightmapNode root = new( 0, 0, size, size );
		public readonly bool Bumped = bumped;
		private int occupiedWidth, occupiedHeight;
		public int Width { get; private set; }
		public int Height { get; private set; }
		public int PlaneCount => Bumped ? 4 : 1;
		public byte[] Data { get; private set; }
		public Texture[] Textures { get; private set; }
		public LightmapNode Allocate( int width, int height )
		{
			if ( Width != 0 )
				throw new InvalidOperationException( "Lightmap packing has already been finalized." );
			var node = AllocateRect( root, width, height );
			if ( node is not null )
			{
				occupiedWidth = Math.Max( occupiedWidth, node.X + width );
				occupiedHeight = Math.Max( occupiedHeight, node.Y + height );
			}
			return node;
		}

		public void FinalizePacking( bool hdr )
		{
			if ( Width != 0 || occupiedWidth == 0 || occupiedHeight == 0 )
				throw new InvalidOperationException( "Lightmap packing must be nonempty and finalized only once." );
			Width = (int)System.Numerics.BitOperations.RoundUpToPowerOf2( (uint)occupiedWidth );
			Height = (int)System.Numerics.BitOperations.RoundUpToPowerOf2( (uint)occupiedHeight );
			Data = new byte[checked(Width * Height * PlaneCount * (hdr ? 8 : 4))];
		}

		public void Upload( string name, bool hdr )
		{
			if ( Data is null )
				throw new InvalidOperationException( "Lightmap data must be prepared before uploading." );
			int planeBytes = Width * Height * (hdr ? 8 : 4);
			Textures = new Texture[PlaneCount];
			for ( int plane = 0; plane < PlaneCount; plane++ )
			{
				var data = PlaneCount == 1 ? Data : Data.AsSpan( plane * planeBytes, planeBytes ).ToArray();
				Textures[plane] = Texture.Create( Width, Height, hdr ? ImageFormat.RGBA16161616 : ImageFormat.RGBA8888 )
					.WithName( $"{name}_{plane}" ).WithData( data ).Finish();
			}
			Data = null;
		}

		static LightmapNode AllocateRect( LightmapNode node, int w, int h )
		{
			if ( node.FreeWidth < w || node.FreeHeight < h )
				return null;
			if ( node.First is null )
			{
				if ( node.Width == w && node.Height == h )
				{
					node.FreeWidth = node.FreeHeight = 0;
					return node;
				}

				if ( (node.Width - w) > (node.Height - h) )
				{
					node.First = new LightmapNode( node.X, node.Y, w, node.Height );
					node.Second = new LightmapNode( node.X + w, node.Y, node.Width - w, node.Height );
				}
				else
				{
					node.First = new LightmapNode( node.X, node.Y, node.Width, h );
					node.Second = new LightmapNode( node.X, node.Y + h, node.Width, node.Height - h );
				}
			}

			var result = AllocateRect( node.First, w, h ) ?? AllocateRect( node.Second, w, h );
			node.FreeWidth = Math.Max( node.First.FreeWidth, node.Second.FreeWidth );
			node.FreeHeight = Math.Max( node.First.FreeHeight, node.Second.FreeHeight );
			return result;
		}
	}

	class LightmapNode( int x, int y, int width, int height )
	{
		public readonly int X = x, Y = y, Width = width, Height = height;
		public int FreeWidth = width, FreeHeight = height;
		public LightmapNode First, Second;
	}
}
