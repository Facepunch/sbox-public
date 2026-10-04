using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Sandbox.Rendering;

namespace Sandbox.Clutter;

/// <summary>
/// Batched clutter, GPU frustum-culled per view and drawn indirect through each model's material.
/// </summary>
internal class ClutterBatchSceneObject : SceneCustomObject
{
	private static ComputeShader CullShader = new( "shaders/clutter_cull_cs.shader" );

	[ConVar( "clutter_cull_frustum_scale", ConVarFlags.Cheat )]
	internal static float CullFrustumScale { get; set; } = 1.0f;

	[ConVar( "r_clutter_shadows", ConVarFlags.Saved, Help = "Enable or disable shadows cast by clutter." )]
	internal static bool ShadowsEnabled { get; set; } = true;

	private const int MaxLods = 4; // dont think we need more than that
	private const uint EmptySphereBits = 0xBF800000; // -1.0f radius marks an unused slot.

	internal struct LodParams
	{
		public Vector3 CameraPos;
		public float TanHalfFov;
		public float ViewportWidth;
		public float OrthoWidth;
	}

	internal static LodParams Lod { get; set; } = new() { TanHalfFov = 1.0f, ViewportWidth = 1920.0f };

	private static readonly int ArgsStride = Marshal.SizeOf<GpuBuffer.IndirectDrawIndexedArguments>();
	private static readonly int ArgsInstanceCountOffset = Marshal.OffsetOf<GpuBuffer.IndirectDrawIndexedArguments>( nameof( GpuBuffer.IndirectDrawIndexedArguments.InstanceCount ) ).ToInt32();

	private readonly Model _model;
	private readonly int _lodCount;
	private readonly float _modelRadius;
	private readonly bool _wantsShadows;
	private readonly GpuBuffer<float> _lodDistances;

	private readonly int[] _drawCallCounts;

	// Culling fills the visible buffers once per view; the draws replay in every pass that draws the batch.
	private readonly CommandList _cullCommands = new( "ClutterCull" );
	private readonly CommandList _drawCommands = new( "ClutterBatch" );

	private GpuBuffer<GpuInstanceTransform>[] _visible;

	private GpuBuffer<GpuBuffer.IndirectDrawIndexedArguments>[] _args;

	private int _count;
	private int _capacity;

	private GpuBuffer<GpuInstanceTransform> _instances;

	private GpuBuffer<Vector4> _spheres;

	private readonly record struct TileSlot( int Offset, int Count )
	{
		public int End => Offset + Count;
	}

	private readonly Dictionary<PreparedInstances, TileSlot> _tileSlots = [];
	private readonly List<TileSlot> _freeSlots = [];
	private readonly HashSet<PreparedInstances> _incomingTiles = [];
	private readonly List<PreparedInstances> _removedTiles = [];
	private readonly List<PreparedInstances> _addedTiles = [];
	private readonly List<TileSlot> _slotsToClear = [];
	private int _highWater;

	/// <summary>
	/// Prepared tile data owned by the layer. Buffers return to the pool after the tile leaves its batch.
	/// </summary>
	internal sealed class PreparedInstances : IDisposable
	{
		public GpuInstanceTransform[] Transforms { get; }
		public Vector4[] Spheres { get; }
		public BBox Bounds { get; private set; }
		public int Count { get; }

		private int _next;
		private readonly BBox _modelBounds;
		private readonly float _modelRadius;

		public PreparedInstances( Model model, int count )
		{
			Count = count;
			Transforms = ArrayPool<GpuInstanceTransform>.Shared.Rent( count );
			Spheres = ArrayPool<Vector4>.Shared.Rent( count );
			_modelBounds = model.Bounds;
			_modelRadius = _modelBounds.Size.Length * 0.5f;
		}

		public void Add( Transform transform )
		{
			var center = transform.PointToWorld( _modelBounds.Center );
			var scale = transform.Scale;
			var radius = _modelRadius * MathF.Max( scale.x, MathF.Max( scale.y, scale.z ) );
			Transforms[_next] = GpuInstanceTransform.From( transform );
			Spheres[_next] = new Vector4( center.x, center.y, center.z, radius );
			var bounds = _modelBounds.Transform( transform );
			Bounds = _next == 0 ? bounds : Bounds.AddBBox( bounds );
			_next++;
		}

		public void Dispose()
		{
			ArrayPool<GpuInstanceTransform>.Shared.Return( Transforms );
			ArrayPool<Vector4>.Shared.Return( Spheres );
		}
	}

	public ClutterBatchSceneObject( SceneWorld world, Model model, bool castShadows = true ) : base( world )
	{
		_model = model;
		_modelRadius = model.Bounds.Size.Length * 0.5f;

		var switches = model.GetLodSwitchDistances() ?? [];
		_lodCount = Math.Clamp( switches.Length, 1, MaxLods );

		var distances = new float[_lodCount];
		_drawCallCounts = new int[_lodCount];
		for ( int i = 0; i < _lodCount; i++ )
		{
			distances[i] = i < switches.Length ? switches[i] : 0f;
			_drawCallCounts[i] = Math.Max( 1, model.GetLodDrawCallCount( i ) );
		}

		_lodDistances = new GpuBuffer<float>( _lodCount, GpuBuffer.UsageFlags.Structured );
		_lodDistances.SetData( distances );

		_visible = new GpuBuffer<GpuInstanceTransform>[_lodCount];
		_args = new GpuBuffer<GpuBuffer.IndirectDrawIndexedArguments>[_lodCount];

		Flags.IsOpaque = true;
		Flags.IsTranslucent = false;
		_wantsShadows = castShadows;
		Flags.CastShadows = castShadows && ShadowsEnabled;
		Flags.WantsPrePass = true;
	}

	/// <summary>
	/// Re-applies <see cref="ShadowsEnabled"/> to this batch. Rebuilds the command list if the shadow state changed.
	/// </summary>
	public void UpdateShadows()
	{
		var castShadows = _wantsShadows && ShadowsEnabled;
		if ( Flags.CastShadows == castShadows )
			return;

		Flags.CastShadows = castShadows;
		BuildCommandList();
	}

	/// <summary>
	/// Uploads the instance set to the persistent GPU buffers. Only called when the set changes.
	/// </summary>
	public void SetInstances( List<PreparedInstances> tiles )
	{
		_incomingTiles.Clear();
		foreach ( var tile in tiles )
			_incomingTiles.Add( tile );

		_removedTiles.Clear();
		foreach ( var tile in _tileSlots.Keys )
		{
			if ( !_incomingTiles.Contains( tile ) )
				_removedTiles.Add( tile );
		}

		_addedTiles.Clear();
		foreach ( var tile in tiles )
		{
			if ( !_tileSlots.ContainsKey( tile ) )
				_addedTiles.Add( tile );
		}

		if ( _removedTiles.Count == 0 && _addedTiles.Count == 0 )
			return;

		_slotsToClear.Clear();
		foreach ( var tile in _removedTiles )
		{
			var slot = _tileSlots[tile];
			_slotsToClear.Add( slot );
			_tileSlots.Remove( tile );
			FreeSlot( slot );
		}

		foreach ( var tile in _addedTiles )
			_tileSlots[tile] = AllocateSlot( tile.Count );

		_count = _highWater;
		if ( _count == 0 )
		{
			BuildCommandList();
			return;
		}

		bool resized = EnsureCapacity( _count );
		if ( resized )
		{
			// New buffers have no tile data. Mark unused slots, then restore every active tile.
			_spheres.Clear( EmptySphereBits );
			foreach ( var (tile, slot) in _tileSlots )
				UploadTile( tile, slot );
		}
		else
		{
			foreach ( var slot in _slotsToClear )
			{
				using var empty = new PooledSpan<Vector4>( slot.Count );
				empty.Span.Fill( new Vector4( 0, 0, 0, -1 ) );
				_spheres.SetData( empty.Span, slot.Offset );
			}
			foreach ( var tile in _addedTiles )
				UploadTile( tile, _tileSlots[tile] );
		}

		var worldBounds = tiles[0].Bounds;
		foreach ( var tile in tiles )
			worldBounds = worldBounds.AddBBox( tile.Bounds );

		Bounds = worldBounds;
		BuildCommandList();
	}

	private void UploadTile( PreparedInstances tile, TileSlot slot )
	{
		_instances.SetData( tile.Transforms.AsSpan( 0, tile.Count ), slot.Offset );
		_spheres.SetData( tile.Spheres.AsSpan( 0, tile.Count ), slot.Offset );
	}

	private TileSlot AllocateSlot( int count )
	{
		int bestSlotIndex = -1;
		for ( int i = 0; i < _freeSlots.Count; i++ )
		{
			if ( _freeSlots[i].Count >= count && (bestSlotIndex < 0 || _freeSlots[i].Count < _freeSlots[bestSlotIndex].Count) )
				bestSlotIndex = i;
		}

		if ( bestSlotIndex >= 0 )
		{
			var free = _freeSlots[bestSlotIndex];
			if ( free.Count == count )
				_freeSlots.RemoveAt( bestSlotIndex );
			else
				_freeSlots[bestSlotIndex] = new TileSlot( free.Offset + count, free.Count - count );

			return new TileSlot( free.Offset, count );
		}

		var slot = new TileSlot( _highWater, count );
		_highWater += count;
		return slot;
	}

	private void FreeSlot( TileSlot slot )
	{
		_freeSlots.Add( slot );
		_freeSlots.Sort( static ( a, b ) => a.Offset.CompareTo( b.Offset ) );

		// Merge adjacent ranges so future tiles can reuse the space without growing the buffers.
		for ( int i = 1; i < _freeSlots.Count; )
		{
			var previous = _freeSlots[i - 1];
			var next = _freeSlots[i];
			if ( previous.End == next.Offset )
			{
				_freeSlots[i - 1] = new TileSlot( previous.Offset, previous.Count + next.Count );
				_freeSlots.RemoveAt( i );
			}
			else
			{
				i++;
			}
		}

		while ( _freeSlots.Count > 0 && _freeSlots[^1].End == _highWater )
		{
			_highWater = _freeSlots[^1].Offset;
			_freeSlots.RemoveAt( _freeSlots.Count - 1 );
		}
	}

	private bool EnsureCapacity( int count )
	{
		if ( _instances != null && count <= _capacity )
			return false;

		var capacity = Math.Max( count, _capacity + Math.Max( _capacity / 2, 1 ) );
		DisposeBuffers();
		_capacity = capacity;

		_instances = new GpuBuffer<GpuInstanceTransform>( capacity, GpuBuffer.UsageFlags.Structured );
		_spheres = new GpuBuffer<Vector4>( capacity, GpuBuffer.UsageFlags.Structured );

		for ( int lod = 0; lod < _lodCount; lod++ )
		{
			_visible[lod] = new GpuBuffer<GpuInstanceTransform>( capacity, GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.Append );

			var drawCallCount = _drawCallCounts[lod];
			var args = new GpuBuffer.IndirectDrawIndexedArguments[drawCallCount];
			for ( int d = 0; d < drawCallCount; d++ )
			{
				_model.GetLodDrawCallRange( lod, d, out int startIndex, out int indexCount, out int baseVertex );
				args[d] = new GpuBuffer.IndirectDrawIndexedArguments
				{
					IndexCount = (uint)indexCount,
					FirstIndex = (uint)startIndex,
					BaseVertex = baseVertex
				};
			}

			_args[lod] = new GpuBuffer<GpuBuffer.IndirectDrawIndexedArguments>( drawCallCount, GpuBuffer.UsageFlags.IndirectDrawArguments );
			_args[lod].SetData( args );
		}

		return true;
	}

	/// <summary>
	/// Bakes the cull dispatch into <see cref="_cullCommands"/> and the indirect draws into <see cref="_drawCommands"/>
	/// for the current instance set. Per-view cull inputs are pushed through Graphics.Attributes in RenderSceneObject.
	/// </summary>
	private void BuildCommandList()
	{
		_cullCommands.Reset();
		_drawCommands.Reset();

		if ( _instances == null || _count == 0 )
			return;

		_cullCommands.Attributes.Set( "AllInstances", _instances );
		_cullCommands.Attributes.Set( "AllInstanceSpheres", _spheres );
		_cullCommands.Attributes.Set( "InstanceCount", _count );
		_cullCommands.Attributes.Set( "ClutterModelRadius", _modelRadius );
		_cullCommands.Attributes.Set( "ClutterLodCount", _lodCount );
		_cullCommands.Attributes.Set( "ClutterLodSwitchDistances", _lodDistances );

		for ( int slot = 0; slot < MaxLods; slot++ )
			_cullCommands.Attributes.Set( $"VisibleLod{slot}", _visible[slot < _lodCount ? slot : 0] );

		for ( int lod = 0; lod < _lodCount; lod++ )
		{
			_cullCommands.ResourceBarrierTransition( _visible[lod], ResourceState.UnorderedAccess );
			_cullCommands.SetCounterValue( _visible[lod], 0 );
		}

		_cullCommands.DispatchCompute( CullShader, _count, 1, 1 );

		// Appends must complete before reading each bucket's count.
		for ( int lod = 0; lod < _lodCount; lod++ )
			_cullCommands.UavBarrier( _visible[lod] );

		for ( int lod = 0; lod < _lodCount; lod++ )
		{
			_cullCommands.ResourceBarrierTransition( _args[lod], ResourceState.CopyDestination );

			// Every draw call at this LOD draws the same visible-instance set, just a different
			// material's index range, so the same counter is replicated into each entry's InstanceCount.
			for ( int d = 0; d < _drawCallCounts[lod]; d++ )
				_cullCommands.CopyStructureCount( _visible[lod], _args[lod], d * ArgsStride + ArgsInstanceCountOffset );
		}

		for ( int lod = 0; lod < _lodCount; lod++ )
		{
			_cullCommands.ResourceBarrierTransition( _visible[lod], ResourceState.GenericRead );
			_cullCommands.ResourceBarrierTransition( _args[lod], ResourceState.IndirectArgument );
		}

		_drawCommands.Attributes.Set( "DisableScreenSpaceShadows", Flags.CastShadows ? 0 : 1 );

		for ( int lod = 0; lod < _lodCount; lod++ )
			_drawCommands.DrawModelInstancedIndirect( _model, _visible[lod], _args[lod], 0, lod );
	}

	/// <summary>
	/// Whether this pass must cull before drawing. The depth normal prepass culls for its view, and the view's opaque
	/// passes run after it on the GPU, so they draw what it culled. Shadow views and views without the prepass cull themselves.
	/// Native records a view's passes on job threads at once, so this is decided from the view, not from recording order.
	/// </summary>
	private static bool PassNeedsCull()
	{
		if ( Graphics.LayerType != SceneLayerType.Opaque )
			return true;

		var view = Graphics.SceneView;
		return !view.IsValid || !view.GetRenderAttributesPtr().GetBoolValue( RenderPipeline.DepthNormalPrepassAttribute, false );
	}

	public override void RenderSceneObject()
	{
		if ( _instances == null || _count == 0 )
			return;

		if ( PassNeedsCull() )
		{
			// Per-view inputs, read by the cull dispatch during replay.
			Graphics.Attributes.Set( "ClutterFrustumScale", CullFrustumScale );
			Graphics.Attributes.Set( "ClutterLodCameraPos", Lod.CameraPos );
			Graphics.Attributes.Set( "ClutterLodTanHalfFov", Lod.TanHalfFov );
			Graphics.Attributes.Set( "ClutterLodViewportWidth", Lod.ViewportWidth );
			Graphics.Attributes.Set( "ClutterLodOrthoWidth", Lod.OrthoWidth );
			Graphics.Attributes.Set( "ClutterWorldToProjection", Graphics.ViewFrustum.GetReverseZViewProjTranspose() );

			_cullCommands.ExecuteOnRenderThread();
		}

		_drawCommands.ExecuteOnRenderThread();
	}

	private void DisposeBuffers()
	{
		_instances?.Dispose();
		_instances = null;

		_spheres?.Dispose();
		_spheres = null;

		for ( int lod = 0; lod < _lodCount; lod++ )
		{
			_visible[lod]?.Dispose();
			_visible[lod] = null;
			_args[lod]?.Dispose();
			_args[lod] = null;
		}

		_capacity = 0;
	}

	internal override void OnNativeDestroy()
	{
		DisposeBuffers();
		_lodDistances?.Dispose();
		base.OnNativeDestroy();
	}
}
