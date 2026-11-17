using Borealis.Gpu;
using Borealis.Source2.Shaders;

namespace Borealis.Backend;

/// <summary>
/// Renders environment map probes on Borealis.Gpu as the engine renders a realtime <c>EnvmapProbe</c>
/// (<c>CubemapRendering.Render</c> through <c>SceneBridge.RenderToCube</c>): mip 0 cleared to red, six 90-degree faces, each
/// a Borealis frame of its own (<see cref="RenderView.Capture"/>, no MSAA, an RGBA16F colour and a depth target the size of
/// the cube) copied into its slice as it finishes, so later faces see the earlier ones; then <c>envmap_filtering_cs</c>'s
/// downsample (each mip from mip 0) and its GGX filter, run twice, as <c>CubemapRendering.Filter</c> runs them. The faces
/// are this capture's own views of a world (<see cref="Render(RenderWorld, Vector3, Rotation, float, float, GpuTexture, RenderView)"/>)
/// or the caller's (<see cref="Render(GpuTexture, Action{int, ViewTarget}, int?)"/>: the engine's cube cameras).
/// </summary>
/// <remarks>
/// Native's GGX pass samples the cube it writes. Here it samples a copy of it taken before each pass, so each pass reads
/// what the one before left, without a hazard. Faces render with <see cref="RenderHost.Current"/>, which has to be a
/// <see cref="BackendRenderHost"/> over the same device.
/// </remarks>
public sealed class CubeCapture : IDisposable
{
	/// <summary>The mips <c>CubemapRendering.Filter</c> requires.</summary>
	public const int Mips = 7;

	/// <summary>
	/// The faces, as <c>SceneCamera.CubeRotations</c> turns the camera for each slice.
	/// </summary>
	static readonly Rotation[] FaceRotations =
	{
		Rotation.LookAt( Vector3.Backward, Vector3.Right ),
		Rotation.LookAt( Vector3.Forward, Vector3.Right ),
		Rotation.LookAt( Vector3.Right, Vector3.Up ),
		Rotation.LookAt( Vector3.Left, Vector3.Down ),
		Rotation.LookAt( Vector3.Down, Vector3.Right ),
		Rotation.LookAt( Vector3.Up, Vector3.Right ),
	};

	readonly BackendDevice device;
	readonly Device gpu;
	readonly int size;
	readonly RenderSystem[] faces = new RenderSystem[6];
	readonly ViewTarget target;
	readonly BufferHandle staging, red;
	readonly BackendTexture filterSource;
	readonly ShaderFile filter;
	readonly ShaderDrawer drawer;
	readonly ShaderAttributes attributes = new();
	readonly RenderView view = new() { Name = "Cube capture", FieldOfView = 90, Capture = true };

	/// <summary>
	/// Captures into cubes of <paramref name="size"/> (a power of two), the size <c>EnvmapProbe.Resolution</c> gives.
	/// </summary>
	public CubeCapture( BackendDevice device, int size )
	{
		this.device = device;
		gpu = device.Gpu;
		this.size = size;
		// Depth without a stencil, which a face whose draws need one gets before it records (RenderSystem.DepthFormat)
		target = ViewTarget.Create( new Vector2Int( size, size ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32, "Cube capture face" );
		staging = gpu.CreateBuffer( new BufferInfo { Size = 6L * size * size * 8, Usage = BufferUsage.CopySource, Name = "Cube capture faces" } );
		red = gpu.CreateBuffer( new BufferInfo { Size = (long)size * size * 8, Usage = BufferUsage.CopySource, Name = "Cube capture red" } );
		gpu.WriteBuffer( red, RedFace( size ) );
		filterSource = (BackendTexture)CreateCube( device, size, "Cube capture filter source" );
		filter = device.Shaders.Load( "shaders/envmap_filtering_cs.shader" );
		drawer = new ShaderDrawer( device ) { MaterialObjects = false };
		view.Viewport = new Rect( 0, 0, size, size );
	}

	/// <summary>Whether the filter shader is built; without it captures have no rough mips.</summary>
	public bool CanFilter => filter is not null;

	/// <summary>The cubes this capture makes, in pixels.</summary>
	public int Size => size;

	/// <summary>
	/// A cube to capture into, as <c>EnvmapProbe</c> makes one: RGBA16F, <see cref="Mips"/> mips, writable by compute,
	/// cleared to black and left readable.
	/// </summary>
	public static GpuTexture CreateCube( BackendDevice device, int size, string name )
	{
		var cube = (BackendTexture)device.CreateTexture( new TextureDesc
		{
			Width = size,
			Height = size,
			Depth = 1,
			Format = TextureFormat.RGBA16161616F,
			Samples = 1,
			Mips = Mips,
			Cube = true,
			RenderTarget = true,
			Storage = true,
		}, name );

		device.Clear( cube, Color.Transparent );
		return cube;
	}

	/// <summary>
	/// Capture <paramref name="world"/> from <paramref name="position"/> turned by <paramref name="rotation"/> into
	/// <paramref name="cube"/> (made by <see cref="CreateCube"/> at this capture's size) and filter it, as
	/// <c>EnvmapProbe.RenderCubemap</c> does. <paramref name="setup"/> gives the faces what a camera's view would: ambient
	/// light, fog, time. Each face is recorded and submitted; call once a frame, after <see cref="BackendDevice.BeginFrame"/>.
	/// </summary>
	public void Render( RenderWorld world, Vector3 position, Rotation rotation, float zNear, float zFar, GpuTexture cube, RenderView setup = null )
	{
		view.Position = position;
		view.ZNear = zNear;
		view.ZFar = zFar;
		view.AmbientLight = setup?.AmbientLight;
		view.Fog = setup?.Fog;
		view.GradientFog = setup?.GradientFog;
		view.Time = setup?.Time;
		Render( cube, ( i, target ) =>
		{
			view.Rotation = rotation * FaceRotations[i];
			(faces[i] ??= new RenderSystem { ParallelRecording = false }).Render( world, view, target );
		}, 0 );
	}

	/// <summary>
	/// Capture into <paramref name="cube"/> (made by <see cref="CreateCube"/> at this capture's size) with faces the caller
	/// renders: <paramref name="renderFace"/> draws face <c>i</c> (turned as <c>SceneCamera.CubeRotations[i]</c> turns the
	/// camera) into the target it's given, and submits it. Then the filter at <paramref name="filterQuality"/>
	/// (<c>CubemapRendering.GGXFilterType</c>: 0 fast, 1 quality), or none when it's null.
	/// </summary>
	public void Render( GpuTexture cube, Action<int, ViewTarget> renderFace, int? filterQuality )
	{
		var destination = (BackendTexture)cube;
		if ( destination.Width != size ) throw new ArgumentException( $"The cube is {destination.Width} wide; this capture makes {size}", nameof( cube ) );

		using var zone = Zones.CubeCapture.Start();
		ClearMip0( destination );
		for ( int i = 0; i < 6; i++ )
		{
			renderFace( i, target );
			CopyFace( i, destination );
		}

		if ( filterQuality is { } quality ) Filter( destination, quality );
	}

	/// <summary>
	/// Mip 0 of every face red, as <c>Texture.Clear( Color.Red )</c> clears a float cube (native clears one mip), from a face
	/// of red kept in a buffer.
	/// </summary>
	void ClearMip0( BackendTexture cube )
	{
		var list = gpu.BeginCommandList( QueueType.Graphics, "Cube capture clear" );
		list.Barrier( Mip( cube.Handle, 0, 1, ImageLayout.ShaderRead, ImageLayout.CopyDestination, PipelineStage.AllCommands, MemoryAccess.ShaderRead, PipelineStage.Transfer, MemoryAccess.TransferWrite ) );
		for ( int face = 0; face < 6; face++ ) list.CopyBufferToTexture( red, 0, cube.Handle, 0, face, size, size );
		list.Barrier( Mip( cube.Handle, 0, 1, ImageLayout.CopyDestination, ImageLayout.ShaderRead, PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.AllCommands, MemoryAccess.ShaderRead ) );
		gpu.Submit( list );
	}

	/// <summary>A face of RGBA16F red, (1, 0, 0, 1).</summary>
	static byte[] RedFace( int size )
	{
		var texel = new byte[8];
		System.Buffers.Binary.BinaryPrimitives.WriteHalfLittleEndian( texel.AsSpan( 0 ), (Half)1 );
		System.Buffers.Binary.BinaryPrimitives.WriteHalfLittleEndian( texel.AsSpan( 6 ), (Half)1 );
		var face = new byte[size * size * 8];
		for ( int i = 0; i < face.Length; i += 8 ) texel.CopyTo( face, i );
		return face;
	}

	/// <summary>The finished face into mip 0 of its slice, through the staging buffer.</summary>
	void CopyFace( int face, BackendTexture cube )
	{
		var (color, layout) = BackendDevice.Unwrap( target.Color );
		var offset = (long)face * size * size * 8;
		var list = gpu.BeginCommandList( QueueType.Graphics, "Cube capture face copy" );
		list.Barrier( TextureBarrier.Transition( color, layout, ImageLayout.CopySource, PipelineStage.AllCommands, MemoryAccess.MemoryWrite, PipelineStage.Transfer, MemoryAccess.TransferRead ) );
		list.Barrier( PipelineStage.Transfer, MemoryAccess.TransferRead | MemoryAccess.TransferWrite, PipelineStage.Transfer, MemoryAccess.TransferWrite );
		list.CopyTextureToBuffer( color, 0, 0, staging, offset );
		list.Barrier( new BufferBarrier { Buffer = staging, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.Transfer, DestinationAccess = MemoryAccess.TransferRead } );
		list.Barrier( Mip( cube.Handle, 0, 1, ImageLayout.ShaderRead, ImageLayout.CopyDestination, PipelineStage.AllCommands, MemoryAccess.ShaderRead, PipelineStage.Transfer, MemoryAccess.TransferWrite, face ) );
		list.CopyBufferToTexture( staging, offset, cube.Handle, 0, face, size, size );
		list.Barrier( Mip( cube.Handle, 0, 1, ImageLayout.CopyDestination, ImageLayout.ShaderRead, PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.AllCommands, MemoryAccess.ShaderRead, face ) );
		gpu.Submit( list );
		BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );
	}

	/// <summary>
	/// <c>CubemapRendering.FilterDownsample</c> then <c>FilterGGX</c> (twice) at <paramref name="quality"/>, leaving the cube
	/// readable.
	/// </summary>
	void Filter( BackendTexture cube, int quality )
	{
		if ( filter is null ) return;

		var list = gpu.BeginCommandList( QueueType.Graphics, "Cube capture filter" );
		var mip0 = gpu.CreateView( cube.Handle, new TextureViewInfo { Dimension = TextureDimension.TextureCube, BaseMip = 0, MipCount = 1, LayerCount = 6 } );
		var mips = new TextureViewHandle[Mips];
		for ( int i = 1; i < Mips; i++ ) mips[i] = gpu.CreateView( cube.Handle, new TextureViewInfo { Dimension = TextureDimension.Texture2DArray, BaseMip = i, MipCount = 1, LayerCount = 6 } );

		// Each mip from mip 0, four bilinear taps a texel
		list.Barrier( Mip( cube.Handle, 1, Mips - 1, ImageLayout.ShaderRead, ImageLayout.General, PipelineStage.AllCommands, MemoryAccess.ShaderRead, PipelineStage.ComputeShader, MemoryAccess.ShaderWrite ) );
		attributes.Clear();
		attributes.Set( "DownsampleInput", mip0 );
		attributes.SetCombo( "D_PASS", 0 );
		attributes.SetCombo( "D_QUALITY", 0 );
		for ( int i = 1; i < Mips; i++ )
		{
			var mipSize = size >> i;
			attributes.Set( "DownsampleOutput", mips[i] );
			attributes.Set( "Size", mipSize );
			drawer.Dispatch( list, filter, attributes, mipSize, mipSize, 6 );
			list.Barrier( PipelineStage.ComputeShader, MemoryAccess.ShaderWrite, PipelineStage.ComputeShader, MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite );
		}

		// GGX, twice, each pass reading what the last left
		attributes.Clear();
		attributes.Set( "Source", filterSource.Handle );
		for ( int i = 1; i < Mips; i++ ) attributes.Set( $"Destination{i}", mips[i] );
		attributes.SetCombo( "D_QUALITY", quality );
		attributes.SetCombo( "D_PASS", 1 );
		attributes.Set( "BaseResolution", size );
		for ( int pass = 0; pass < 2; pass++ )
		{
			list.Barrier( Mip( cube.Handle, 0, 1, ImageLayout.ShaderRead, ImageLayout.CopySource, PipelineStage.AllCommands, MemoryAccess.ShaderRead, PipelineStage.Transfer, MemoryAccess.TransferRead ) );
			list.Barrier( Mip( cube.Handle, 1, Mips - 1, ImageLayout.General, ImageLayout.CopySource, PipelineStage.ComputeShader, MemoryAccess.ShaderWrite, PipelineStage.Transfer, MemoryAccess.TransferRead ) );
			list.Barrier( TextureBarrier.Transition( filterSource.Handle, filterSource.Layout, ImageLayout.CopyDestination, PipelineStage.AllCommands, MemoryAccess.ShaderRead, PipelineStage.Transfer, MemoryAccess.TransferWrite ) );
			for ( int m = 0; m < Mips; m++ ) list.CopyTexture( cube.Handle, filterSource.Handle, m, 6 );
			list.Barrier( TextureBarrier.Transition( filterSource.Handle, ImageLayout.CopyDestination, ImageLayout.ShaderRead, PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.ComputeShader, MemoryAccess.ShaderRead ) );
			filterSource.Layout = ImageLayout.ShaderRead;
			list.Barrier( Mip( cube.Handle, 0, 1, ImageLayout.CopySource, ImageLayout.ShaderRead, PipelineStage.Transfer, MemoryAccess.TransferRead, PipelineStage.AllCommands, MemoryAccess.ShaderRead ) );
			list.Barrier( Mip( cube.Handle, 1, Mips - 1, ImageLayout.CopySource, ImageLayout.General, PipelineStage.Transfer, MemoryAccess.TransferRead, PipelineStage.ComputeShader, MemoryAccess.ShaderWrite ) );

			drawer.Dispatch( list, filter, attributes, SumOfSquaresOfPowersOfTwo( size / 2 ), 6, 1 );
		}

		list.Barrier( Mip( cube.Handle, 1, Mips - 1, ImageLayout.General, ImageLayout.ShaderRead, PipelineStage.ComputeShader, MemoryAccess.ShaderWrite, PipelineStage.AllCommands, MemoryAccess.ShaderRead ) );
		gpu.Submit( list );
		cube.Layout = ImageLayout.ShaderRead;

		gpu.Destroy( mip0 );
		for ( int i = 1; i < Mips; i++ ) gpu.Destroy( mips[i] );
	}

	/// <summary>1² + 2² + 4² + ... up to <paramref name="n"/>: the GGX pass's texels across its mips (<c>CubemapRendering.SumOfSquaresTwo</c>).</summary>
	internal static int SumOfSquaresOfPowersOfTwo( int n )
	{
		var sum = 0;
		for ( int i = 1; i <= n; i *= 2 ) sum += i * i;
		return sum;
	}

	static TextureBarrier Mip( TextureHandle texture, int baseMip, int mipCount, ImageLayout from, ImageLayout to, PipelineStage sourceStage, MemoryAccess sourceAccess, PipelineStage destinationStage, MemoryAccess destinationAccess, int layer = -1 ) => new()
	{
		Texture = texture,
		OldLayout = from,
		NewLayout = to,
		SourceStage = sourceStage,
		SourceAccess = sourceAccess,
		DestinationStage = destinationStage,
		DestinationAccess = destinationAccess,
		BaseMip = baseMip,
		MipCount = mipCount,
		BaseLayer = layer < 0 ? 0 : layer,
		LayerCount = layer < 0 ? 0 : 1,
	};

	/// <summary>Free the faces' render systems and targets, the staging buffers and the filter's copy, once the GPU is idle.</summary>
	public void Dispose()
	{
		gpu.WaitIdle();
		foreach ( var face in faces ) face?.Dispose();
		target.Dispose();
		filterSource.Dispose();
		gpu.Destroy( staging );
		gpu.Destroy( red );
		drawer.Dispose();
	}
}
