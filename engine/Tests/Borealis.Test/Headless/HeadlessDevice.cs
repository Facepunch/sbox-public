using Borealis;
using Borealis.Gpu;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace BorealisTests.Headless;

/// <summary>
/// A device with no GPU behind it. Resources are plain objects, and contexts record what they're asked to do; what's
/// submitted on a thread goes to that thread's <see cref="Capture"/>. It checks what a GPU wouldn't forgive: a semaphore
/// waited on twice, or before whatever signals it was submitted.
/// </summary>
internal sealed class HeadlessDevice : GpuDevice
{
	/// <summary>
	/// Whether it claims an async compute queue. Off by default; a test that turns it on is <c>[DoNotParallelize]</c>, as
	/// every render system reads it. See <see cref="WithAsyncCompute"/>.
	/// </summary>
	public bool AsyncCompute { get; set; }

	/// <summary>
	/// Whether it claims indirect draw counts.
	/// </summary>
	public bool DrawIndirectCount { get; set; } = true;

	/// <summary>
	/// Turn <see cref="AsyncCompute"/> on or off until disposed.
	/// </summary>
	public IDisposable WithAsyncCompute( bool supported )
	{
		var previous = AsyncCompute;
		AsyncCompute = supported;
		return new Restore( () => AsyncCompute = previous );
	}

	sealed class Restore( Action restore ) : IDisposable
	{
		public void Dispose() => restore();
	}

	[ThreadStatic] static FrameCapture capturing;

	/// <summary>
	/// Collect what's submitted on this thread until disposed.
	/// </summary>
	public FrameCapture Capture() => capturing = new FrameCapture( capturing );

	internal static void EndCapture( FrameCapture capture ) => capturing = capture.Previous;

	readonly ConcurrentDictionary<string, GpuTexture> loadedTextures = new( StringComparer.OrdinalIgnoreCase );
	readonly ConcurrentDictionary<string, GpuMaterial> loadedMaterials = new( StringComparer.OrdinalIgnoreCase );
	readonly ConcurrentDictionary<IntPtr, GpuTexture> swapChains = new();
	readonly ConcurrentDictionary<IntPtr, Semaphore> semaphores = new();
	long nextHandle;
	int vertexCache;

	sealed class Semaphore
	{
		public HeadlessContext Signaller;
		public bool Submitted;
		public bool Waited;
	}

	static GpuTexture Placeholder( string name, TextureFormat format = TextureFormat.RGBA8888, bool cube = false )
		=> new HeadlessTexture( new TextureDesc { Width = 1, Height = 1, Depth = 1, Format = format, Samples = 1, Mips = 1, Cube = cube }, name );

	public override GpuTexture CreateTexture( in TextureDesc desc, string name ) => new HeadlessTexture( desc, name );

	public override GpuTexture LoadTexture( string path ) => loadedTextures.GetOrAdd( path, static p => Placeholder( p, cube: p.Contains( "cube", StringComparison.OrdinalIgnoreCase ) ) );

	public override GpuTexture Black { get; } = Placeholder( "black" );

	public override int MissingTextureViewIndex( bool srgb ) => 0;

	public override void Clear( GpuTexture texture, Color color ) { }

	public override IDisposable CreateBuffer( int elementCount, int elementSize, GpuBuffer.UsageFlags usage, string name ) => new HeadlessBuffer( elementCount, elementSize, usage, name );

	public override void SetData( GpuBuffer buffer, ReadOnlySpan<byte> data ) { }

	public override ShaderParameters CreateParameters() => new HeadlessParameters();

	public override GpuTexture SheetMetadata { get; } = Placeholder( "sheet metadata" );

	public override GpuMaterial LoadMaterial( string path ) => loadedMaterials.GetOrAdd( path, static p => new HeadlessMaterial( p, p ) );

	public override GpuMaterial MaterialFromShader( string shaderPath ) => loadedMaterials.GetOrAdd( shaderPath, static p => new HeadlessMaterial( p, p ) );

	public override GpuMaterial CreateMaterial( string name, string shaderPath ) => new HeadlessMaterial( name, shaderPath );

	internal override GpuContext CreateContext( GpuContext frame = null ) => new HeadlessContext( this, (HeadlessContext)frame );

	public override bool SupportsAsyncCompute => AsyncCompute;

	public override bool SupportsDrawIndirectCount => DrawIndirectCount;

	/// <summary>
	/// Whether it claims to draw aggregates GPU-driven, culled on the GPU and resident (it records nothing of them): what
	/// views prepare for such a device. Off by default; a test that turns it on is <c>[DoNotParallelize]</c>.
	/// </summary>
	public bool ResidentAggregates { get; set; }

	internal override bool DrawsAggregatesGpuDriven => ResidentAggregates;

	internal override bool CullsAggregatesGpuDriven => ResidentAggregates;

	internal override bool DrawsAggregatesResident => ResidentAggregates;

	public override int AllocateVertexCache( int vertices ) => Interlocked.Add( ref vertexCache, vertices ) - vertices;

	public override Vector4 MorphSubrect( int handle ) => default;

	public override void BeginSubmitting( GpuSwapChain swapChain ) { }

	public override void SetSubmitTag( ulong tag ) { }

	public override float RenderTime => 0;

	public override IntPtr CreateConstantBuffer( int size ) => (IntPtr)Interlocked.Increment( ref nextHandle );

	public override void DestroyConstantBuffer( IntPtr buffer ) { }

	public override int SwapChainSamples( GpuSwapChain swapChain ) => 1;

	/// <summary>
	/// A swap chain's colour buffer.
	/// </summary>
	public GpuTexture SwapChainColor( GpuSwapChain swapChain ) => swapChains.GetOrAdd( swapChain.Handle, static h => Placeholder( $"swap chain {h}", TextureFormat.BGRA8888 ) );

	internal GpuSemaphore Signal( HeadlessContext context )
	{
		var handle = (IntPtr)Interlocked.Increment( ref nextHandle );
		semaphores[handle] = new Semaphore { Signaller = context };
		return new GpuSemaphore( handle );
	}

	internal void Wait( GpuSemaphore semaphore )
	{
		if ( !semaphores.TryGetValue( semaphore.Handle, out var state ) ) throw new InvalidOperationException( $"Waiting on semaphore {semaphore.Handle}, which nothing signals" );
		if ( state.Waited ) throw new InvalidOperationException( $"Semaphore {semaphore.Handle} already has a waiter: binary semaphores have one" );
		state.Waited = true;
	}

	/// <summary>
	/// A context's recording reaching its queue.
	/// </summary>
	internal void Submit( HeadlessContext context, GpuSemaphore wait, GpuSemaphore signal, List<GpuCommand> commands )
	{
		if ( wait.Handle != IntPtr.Zero )
		{
			if ( !semaphores.TryRemove( wait.Handle, out var waited ) || !waited.Submitted )
				throw new InvalidOperationException( $"Submitted waiting on semaphore {wait.Handle} before its signaller was: the GPU would never start it" );
		}

		if ( signal.Handle != IntPtr.Zero ) semaphores[signal.Handle].Submitted = true;

		var submission = new GpuSubmission( context, context.AsyncCompute, wait, signal, commands.ToArray() );
		capturing?.Add( submission );
	}
}

/// <summary>
/// What's submitted on a thread while it's open (<see cref="HeadlessDevice.Capture"/>): each context's recording, in
/// submission order.
/// </summary>
internal sealed class FrameCapture( FrameCapture previous ) : IDisposable
{
	internal FrameCapture Previous { get; } = previous;

	readonly List<GpuSubmission> submissions = new();

	/// <summary>
	/// Every submission, in order.
	/// </summary>
	public IReadOnlyList<GpuSubmission> Submissions => submissions;

	/// <summary>
	/// Every command, in submission order.
	/// </summary>
	public IEnumerable<GpuCommand> Commands
	{
		get
		{
			foreach ( var submission in submissions )
				foreach ( var command in submission.Commands )
					yield return command;
		}
	}

	internal void Add( GpuSubmission submission )
	{
		submissions.Add( submission );
		Previous?.Add( submission );
	}

	public void Dispose() => HeadlessDevice.EndCapture( this );
}
