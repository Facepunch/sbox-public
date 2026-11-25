using NativeEngine;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sandbox;

public class SceneDynamicObject : SceneObject
{
	/// <summary>The vertices, which Borealis draws: on the render thread, its frame's (<see cref="Drawn"/>).</summary>
	internal List<Vertex> ManagedVertices => ForDrawing().Vertices;

	/// <summary>What the vertices are, as last begun.</summary>
	internal Graphics.PrimitiveType ManagedPrimitiveType => ForDrawing().PrimitiveType;

	/// <summary>The material set last, for a backend other than Source 2's.</summary>
	internal Material ManagedMaterial => ForDrawing().Material;

	/// <summary>
	/// What a frame draws of the object. While a frame handed over may be drawing on the render thread
	/// (<see cref="Rendering.RenderSync.Overlapping"/>), the main thread's first change since that hand-over keeps this for
	/// that frame and goes on in a copy, as a command list keeps its recording: the frame draws the vertices it was handed
	/// over with, however the game rebuilds them meanwhile.
	/// </summary>
	internal sealed class Drawn
	{
		public readonly List<Vertex> Vertices = new();
		public Graphics.PrimitiveType PrimitiveType;
		public Material Material;
	}

	Drawn _drawn = new(), _drawnHeld;
	long _splitEpoch = -1, _splitPublished = -1;

	/// <summary>What the main thread writes: split from what a frame handed over draws at its first write since the hand-over.</summary>
	Drawn ForWriting( bool clearing = false )
	{
		// Checked cheapest first: AddVertex comes here for every vertex
		if ( !Rendering.RenderSync.Overlapping ) return _drawn;
		var epoch = Rendering.RenderSync.Epoch;
		if ( _splitEpoch == epoch || Rendering.RenderSync.OnRenderThread ) return _drawn;
		_splitEpoch = epoch;

		// The other set isn't drawn in this frame. Written in this order for ForDrawing, which reads them in the other order
		var held = _drawn;
		var fresh = _drawnHeld ?? new Drawn();
		fresh.Vertices.Clear();
		if ( !clearing ) fresh.Vertices.AddRange( held.Vertices );
		fresh.PrimitiveType = held.PrimitiveType;
		fresh.Material = held.Material;
		System.Threading.Volatile.Write( ref _drawnHeld, held );
		System.Threading.Volatile.Write( ref _splitPublished, epoch );
		System.Threading.Volatile.Write( ref _drawn, fresh );
		return fresh;
	}

	/// <summary>What a draw on this thread reads: on the render thread, its frame's as it was handed over.</summary>
	Drawn ForDrawing()
	{
		var current = System.Threading.Volatile.Read( ref _drawn );
		if ( !Rendering.RenderSync.OnRenderThread ) return current;
		return System.Threading.Volatile.Read( ref _splitPublished ) == Rendering.RenderSync.Epoch ? System.Threading.Volatile.Read( ref _drawnHeld ) : current;
	}

	public SceneDynamicObject( SceneWorld sceneWorld )
	{
		Assert.IsValid( sceneWorld );

		MakeManaged( sceneWorld, null, SceneCustomObject.ManagedFlags, batchable: false ); // as native's CDynamicSceneObject::Create made it

		// Start off with infinite bounds so it will actually get rendered
		// if the user forgets/doesn't need bounds.
		KeepFixedBounds( infinite: true );
	}

	public Material Material
	{
		set => ForWriting().Material = value;
	}

	public void Clear()
	{
		WritesNativeObject( "Reset" );
		ForWriting( clearing: true ).Vertices.Clear();
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void AddVertex( in Vertex v )
	{
		WritesNativeObject( "AddVertex" );
		ForWriting().Vertices.Add( v );
	}

	public unsafe void AddVertex( in Span<Vertex> v )
	{
		fixed ( Vertex* ptr = &MemoryMarshal.GetReference( v ) )
		{
			AddVertex( ptr, v.Length );
		}
	}

	internal unsafe void AddVertex( in Vertex* ptr, int count )
	{
		WritesNativeObject( "AddVertexRange" );
		ForWriting().Vertices.AddRange( new ReadOnlySpan<Vertex>( ptr, count ) );
	}

	public IDisposable Write( Graphics.PrimitiveType type, int vertices, int indices )
	{
		Begin( type, vertices );
		return null;
	}

	public void Init( Graphics.PrimitiveType type ) => Begin( type, 0 );

	// Native's Begin drops the vertices and sets the primitive type
	void Begin( Graphics.PrimitiveType type, int vertices )
	{
		ThrowIfUndefined( type );
		WritesNativeObject( "Begin" );
		var drawn = ForWriting( clearing: true );
		drawn.Vertices.Clear();
		drawn.PrimitiveType = type;
	}

	internal override bool OwnsRenderObject => true;

	internal override Borealis.RenderObject MakeRenderObject( Rendering.ISceneWorldRender render ) => new Rendering.BridgeDynamicObject( this );

	internal override void WriteRender( Borealis.RenderObject obj, Rendering.SceneObjectChange change, Rendering.ISceneWorldRender render )
	{
		base.WriteRender( obj, change, render );
		if ( obj is not Borealis.CustomObject custom ) return;

		// Its material is known before it draws (gizmos, the debug overlay): whether it needs a stencil, so the view's depth has
		// one before the frame records. Other custom objects pick their materials as they draw
		if ( RenderBackend.Default is BorealisGpuRenderBackend gpu ) custom.NeedsStencil = gpu.NeedsStencil( ManagedMaterial );
		WriteCustom( custom, change );
	}

	internal static void ThrowIfUndefined( Graphics.PrimitiveType type )
	{
		if ( !Enum.IsDefined( type ) )
			throw new ArgumentOutOfRangeException( nameof( type ), $"Unknown primitive type {(int)type}" );
	}
}

