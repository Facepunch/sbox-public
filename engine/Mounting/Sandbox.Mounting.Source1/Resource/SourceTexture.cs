using System;
using System.IO;

namespace Sandbox.Mounting.Source1;

internal sealed class SourceTexture : IDisposable
{
	private readonly VtfFile metadata;
	private readonly BaseMount.SourceFile file;
	private readonly string texturePath;
	private readonly object frameLock = new();
	private readonly Dictionary<int, VtfFile> decodedFrames = [];
	private bool disposed;

	internal SourceTexture( VtfFile metadata, BaseMount.SourceFile file, string texturePath )
	{
		this.metadata = metadata;
		this.file = file;
		this.texturePath = texturePath;
	}

	internal static SourceTexture Read( BaseMount.SourceFile file, string texturePath )
	{
		using var stream = file.Open();
		return new SourceTexture( new VtfFile( stream, onlyHeader: true ), file, texturePath );
	}

	public VtfHeader Header => metadata.Header;
	public int FrameCount => metadata.FrameCount;
	public int FaceCount => metadata.FaceCount;
	public int Depth => metadata.ZSliceCount;

	public Texture GetFrame( int frame )
	{
		ObjectDisposedException.ThrowIf( disposed, this );
		if ( (uint)frame >= FrameCount )
			throw new ArgumentOutOfRangeException( nameof( frame ), frame, $"VTF frame must be between 0 and {FrameCount - 1}." );
		return Texture.Load( frame == 0 ? texturePath : TextureLoader.FramePath( texturePath[..^5], frame ), false );
	}

	public Texture GetAnimationFrames()
	{
		ObjectDisposedException.ThrowIf( disposed, this );
		if ( FaceCount > 1 )
			throw new NotSupportedException( $"Animated cubemap VTF textures are not supported: {texturePath}" );
		return FrameCount == 1 ? GetFrame( 0 ) : Texture.Load( TextureLoader.FramePath( texturePath[..^5], null ), false );
	}

	public VtfFile ReadFrame( int? frame = null )
	{
		lock ( frameLock )
		{
			ObjectDisposedException.ThrowIf( disposed, this );
			if ( frame.HasValue && (uint)frame.Value >= FrameCount )
				throw new ArgumentOutOfRangeException( nameof( frame ), frame, $"VTF frame must be between 0 and {FrameCount - 1}." );
			var key = frame ?? -1;
			if ( decodedFrames.TryGetValue( key, out var decoded ) ) return decoded;
			using var stream = file.Open();
			decoded = new VtfFile( stream, metadata, frame );
			decodedFrames.Add( key, decoded );
			return decoded;
		}
	}

	internal VtfFile ReadTexture( int? frame )
	{
		lock ( frameLock )
		{
			ObjectDisposedException.ThrowIf( disposed, this );
			if ( frame.HasValue && (uint)frame.Value >= FrameCount )
				throw new ArgumentOutOfRangeException( nameof( frame ), frame, $"VTF frame must be between 0 and {FrameCount - 1}." );
			if ( decodedFrames.TryGetValue( frame ?? -1, out var decoded ) )
				return decoded;
			using var stream = file.Open();
			return new VtfFile( stream, metadata, frame, textureData: true );
		}
	}

	public void Dispose()
	{
		lock ( frameLock )
		{
			disposed = true;
			decodedFrames.Clear();
		}
	}
}
