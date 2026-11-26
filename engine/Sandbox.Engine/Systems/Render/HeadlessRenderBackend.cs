using Borealis.Gpu;
using Borealis.Source2;
using Borealis.Source2.Materials;
using NativeEngine;
using Sandbox.Rendering;
using System.Runtime.CompilerServices;
using GpuFormat = Borealis.Gpu.Format;
using RenderViewport = NativeEngine.RenderViewport;

namespace Sandbox;

/// <summary>
/// The device of a process that draws on none: a server, a headless test, a content worker. Draws, dispatches, copies of
/// textures and barriers do nothing. Buffers keep their bytes in memory, so what's written reads back. A texture's pixels
/// read back on the CPU, from its compiled file or the pixels it was made with, so a compile can copy another texture.
/// </summary>
internal sealed class HeadlessRenderBackend : RenderBackend
{
	public static HeadlessRenderBackend Instance { get; } = new();

	readonly ConditionalWeakTable<GpuBuffer, byte[]> buffers = new();
	readonly System.Threading.Lock bufferLock = new();
	ContentFiles files;
	RenderViewport viewport;

	//
	// Blocks and resources
	//

	public override RenderAttributes BeginBlock() => RenderAttributes.CreateManaged();

	public override void EndBlock() { }

	public override int TextureIndex( Texture texture ) => 0;

	public override int SamplerIndex( SamplerState state ) => 0;

	public override IntPtr UploadContext => IntPtr.Zero;

	public override void Release( object resource )
	{
		if ( resource is GpuBuffer buffer )
		{
			lock ( bufferLock ) buffers.Remove( buffer );
		}
	}

	/// <summary>
	/// A rectangle of a mip and slice, decoded on the CPU: from the texture's compiled file when it was loaded from one,
	/// else from the pixels it was made with (mip 0). False for anything else (a render target's drawing).
	/// </summary>
	public override unsafe bool ReadPixels( Texture texture, NativeRect source, int slice, int mip, NativeRect destination, IntPtr data, ImageFormat format, int stride )
	{
		if ( source.w != destination.w || source.h != destination.h || source.w <= 0 || source.h <= 0 ) return false;

		var pixel = ImageLoader.GetMemRequired( 1, 1, 1, 1, format );
		if ( pixel <= 0 ) return false;
		stride = stride > 0 ? stride : destination.w * pixel;

		if ( CompiledFile( texture ) is { } file )
		{
			if ( mip < 0 || mip >= file.Mips || mip < file.TopMip || slice < 0 ) return false;
			if ( BorealisGpuRenderBackend.ImageFormatOf( file.Format ) is not { } stored ) return false;

			int width = Math.Max( file.Width >> mip, 1 ), height = Math.Max( file.Height >> mip, 1 );
			var depth = file.Dimension == Borealis.Gpu.TextureDimension.Texture3D ? Math.Max( file.Depth >> mip, 1 ) : 1;
			if ( slice >= file.Slices * depth ) return false;

			var sliceBytes = file.Format.RowPitch( width ) * file.Format.Rows( height );
			return CopyPixels( file.Mip( mip ).Slice( sliceBytes * slice, sliceBytes ), stored, file.Format.IsCompressed(), width, height, source, destination, data, format, pixel, stride );
		}

		if ( texture.InitialPixels is { } pixels && mip == 0 && slice == 0 && texture.Depth <= 1 )
		{
			var made = ImageLoader.GetMemRequired( texture.Width, texture.Height, 1, 1, texture.ImageFormat );
			if ( made <= 0 || pixels.Length < made ) return false;

			var compressed = texture.ImageFormat is ImageFormat.DXT1 or ImageFormat.DXT1_ONEBITALPHA or ImageFormat.DXT3 or ImageFormat.DXT5
				or ImageFormat.ATI1N or ImageFormat.ATI2N or ImageFormat.BC6H or ImageFormat.BC7;
			return CopyPixels( pixels.AsSpan( 0, made ), texture.ImageFormat, compressed, texture.Width, texture.Height, source, destination, data, format, pixel, stride );
		}

		return false;
	}

	/// <summary>The compiled file a texture was loaded from, read whole, or null.</summary>
	TextureFile CompiledFile( Texture texture )
	{
		var path = texture.ResourcePath;
		if ( string.IsNullOrEmpty( path ) || !path.EndsWith( ".vtex", StringComparison.OrdinalIgnoreCase ) ) return null;
		if ( Files.Find( path ) is not { } file ) return null;

		try
		{
			return TextureFile.Load( file );
		}
		catch ( Exception e ) when ( e is System.IO.IOException or System.IO.InvalidDataException or NotSupportedException )
		{
			return null;
		}
	}

	/// <summary>The engine's content folders, then wherever the process has mounted files (a content worker's roots).</summary>
	ContentFiles Files
	{
		get
		{
			if ( files is not null ) return files;
			var found = ContentFiles.FindGame();
			found.Resolve = MountedFile;
			return files = found;
		}
	}

	static string MountedFile( string path )
	{
		if ( BorealisGpuRenderBackend.MountedFile( path ) is { } found ) return found;
		if ( Sandbox.Engine.GlobalContext.Current?.FileMount is not { } mount ) return null;

		try
		{
			return mount.FileExists( path ) ? mount.GetFullPath( path ) : null;
		}
		catch ( System.IO.IOException )
		{
			return null;
		}
	}

	public override byte[] ReadCompiledPrefix( string path, long most )
	{
		try
		{
			return Files.TryReadPrefix( path, most );
		}
		catch ( System.IO.IOException )
		{
			return null;
		}
	}

	//
	// Targets and viewport
	//

	public override RenderViewport GetViewport() => viewport;
	public override void SetViewport( in RenderViewport viewport ) => this.viewport = viewport;
	public override void SetViewport( Rect rect ) => viewport = new RenderViewport( rect ) { MinZ = viewport.MinZ, MaxZ = viewport.MaxZ };
	public override void BindRenderTarget( RenderTarget target ) { }
	public override void Clear( Color color, bool clearColor, bool clearDepth, bool clearStencil ) { }
	public override void ClearTexture( Texture texture, Color color ) { }

	//
	// Draws, copies and compute: nothing to draw on
	//

	public override void DrawVertices( Type vertexType, int vertexSize, IntPtr vertices, int vertexCount, IntPtr indices, int indexCount, Material material, RenderAttributes attributes, Graphics.PrimitiveType primitiveType ) { }
	public override bool SetRenderState( Material material, RenderAttributes attributes, Type vertexType ) => false;
	public override void Draw( GpuBuffer vertexBuffer, Graphics.PrimitiveType primitiveType, int startVertex, int vertexCount ) { }
	public override void DrawIndexed( GpuBuffer vertexBuffer, GpuBuffer indexBuffer, Graphics.PrimitiveType primitiveType, int startIndex, int indexCount ) { }
	public override void DrawIndexedInstanced( GpuBuffer indexBuffer, Graphics.PrimitiveType primitiveType, int instanceCount ) { }
	public override void DrawInstancedIndirect( GpuBuffer vertexBuffer, Graphics.PrimitiveType primitiveType, GpuBuffer indirectBuffer, uint bufferOffset, uint drawCount, uint stride ) { }
	public override void DrawIndexedInstancedIndirect( GpuBuffer vertexBuffer, GpuBuffer indexBuffer, Graphics.PrimitiveType primitiveType, GpuBuffer indirectBuffer, uint bufferOffset, uint drawCount, uint stride ) { }
	public override void DrawModel( Model model, ReadOnlySpan<Transform> transforms, int? lodLevel, RenderAttributes attributes ) { }
	public override void DrawModel( Model model, int count, RenderAttributes attributes ) { }
	public override void DrawModelIndirect( Model model, GpuBuffer arguments, int argumentsOffset, RenderAttributes attributes ) { }
	public override void DrawModelIndirect( Model model, GpuBuffer transforms, GpuBuffer arguments, int argumentsOffset, int lodLevel, RenderAttributes attributes ) { }
	public override void CopyTexture( Texture source, Texture destination, int sourceMip, int sourceSlice, int destinationMip, int destinationSlice ) { }
	public override void ResolveColor( Texture destination, Rect viewport ) { }
	public override void ResolveDepth( Texture destination, Rect viewport ) { }
	public override void GenerateMipMaps( Texture texture, Graphics.DownsampleMethod method, int initialMip ) { }
	public override void Dispatch( Material computeMaterial, RenderAttributes attributes, int threadsX, int threadsY, int threadsZ ) { }
	public override void DispatchIndirect( Material computeMaterial, RenderAttributes attributes, GpuBuffer indirectBuffer, uint bufferOffset ) { }
	public override void ResourceBarrierTransition( Texture texture, ResourceState state, int mip ) { }
	public override void ResourceBarrierTransition( GpuBuffer buffer, ResourceState state ) { }
	public override void ResourceBarrierTransition( GpuBuffer buffer, ResourceState before, ResourceState after ) { }
	public override void UavBarrier( Texture texture ) { }
	public override void UavBarrier( GpuBuffer buffer ) { }
	public override void CopyStructureCount( GpuBuffer source, GpuBuffer destination, uint destinationOffset ) { }
	public override void SetStructureCount( GpuBuffer buffer, uint value ) { }

	//
	// Buffers: bytes in memory
	//

	byte[] Bytes( GpuBuffer buffer )
	{
		lock ( bufferLock )
		{
			return buffers.GetValue( buffer, b => new byte[Math.Max( (long)b.ElementCount * b.ElementSize, 0 )] );
		}
	}

	public override unsafe void SetBufferData( GpuBuffer buffer, IntPtr data, int size, int offset )
	{
		var bytes = Bytes( buffer );
		if ( offset < 0 || size <= 0 || offset >= bytes.Length ) return;
		new ReadOnlySpan<byte>( (void*)data, Math.Min( size, bytes.Length - offset ) ).CopyTo( bytes.AsSpan( offset ) );
	}

	public override void ReadBuffer( GpuBuffer buffer, Span<byte> data, int offset )
	{
		var bytes = Bytes( buffer );
		data.Clear();
		if ( offset < 0 || offset >= bytes.Length ) return;
		bytes.AsSpan( offset, Math.Min( data.Length, bytes.Length - offset ) ).CopyTo( data );
	}

	public override void ReadBufferAsync( GpuBuffer buffer, AsyncGPUReadback.BufferReadDelegate callback, int offset, int size )
	{
		var bytes = new byte[Math.Max( size, 0 )];
		ReadBuffer( buffer, bytes, offset );
		callback( bytes );
	}

	public override void CopyBuffer( GpuBuffer source, GpuBuffer destination, uint sourceOffset, uint destinationOffset, uint size )
	{
		var from = Bytes( source );
		var to = Bytes( destination );
		if ( sourceOffset >= from.Length || destinationOffset >= to.Length ) return;
		var count = (int)Math.Min( size, Math.Min( from.Length - sourceOffset, to.Length - destinationOffset ) );
		from.AsSpan( (int)sourceOffset, count ).CopyTo( to.AsSpan( (int)destinationOffset ) );
	}

	public override void FillBuffer( GpuBuffer buffer, uint value )
	{
		var bytes = Bytes( buffer );
		System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>( bytes.AsSpan( 0, bytes.Length & ~3 ) ).Fill( value );
	}
}
