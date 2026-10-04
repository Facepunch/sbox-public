using System.Runtime.InteropServices;
using Sandbox.Rendering;

namespace Sandbox.Clutter;

/// <summary>
/// Draws every clutter batch in a scene. All instances live in shared buffers, so each view culls every batch in one
/// set of compute passes, then each batch draws its LODs indirect through its model's materials.
/// </summary>
internal sealed class ClutterRenderer : SceneCustomObject
{
	private static ComputeShader CullShader = new( "shaders/clutter_cull_cs.shader" );

	[ConVar( "clutter_cull_frustum_scale", ConVarFlags.Cheat )]
	internal static float CullFrustumScale { get; set; } = 1.0f;

	[ConVar( "r_clutter_shadows", ConVarFlags.Saved, Help = "Enable or disable shadows cast by clutter." )]
	internal static bool ShadowsEnabled { get; set; } = true;

	[ConVar( "r_clutter_shadow_distance", ConVarFlags.Saved, Min = 0, Help = "Clutter farther than this from the camera casts no shadows. 0 = r.shadows.csm.distance." )]
	internal static float ShadowDistance { get; set; } = 0.0f;

	/// <summary>
	/// Shadows end at the cascade distance anyway, so the clutter limit never goes past it.
	/// </summary>
	private static float EffectiveShadowDistance => ShadowDistance > 0.0f
		? MathF.Min( ShadowDistance, ShadowMapper.CascadeDistance )
		: ShadowMapper.CascadeDistance;

	internal struct LodParams
	{
		public Vector3 CameraPos;
		public float TanHalfFov;
		public float ViewportWidth;
		public float OrthoWidth;
	}

	internal static LodParams Lod { get; set; } = new() { TanHalfFov = 1.0f, ViewportWidth = 1920.0f };

	private const uint EmptySphereBits = 0xBF800000; // -1.0f radius marks an unused slot.
	private const int CountPass = 0;
	private const int ArgsPass = 1;
	private const int ScatterPass = 2;

	private static readonly int ArgsStride = Marshal.SizeOf<GpuBuffer.IndirectDrawIndexedArguments>();

	// Rows in the GPU profiler overlay and markers in graphics debuggers.
	private static readonly ProfilingSampler CullSampler = new( "ClutterCull" );
	private static readonly ProfilingSampler DrawSampler = new( "ClutterBatch" );

	/// <summary>
	/// Matches ClutterBatch_t in clutter_cull_cs.shader.
	/// </summary>
	[StructLayout( LayoutKind.Sequential )]
	private struct GpuBatch
	{
		public Vector4 LodSwitchDistances;
		public uint ArgsBase0, ArgsBase1, ArgsBase2, ArgsBase3;
		public uint DrawCount0, DrawCount1, DrawCount2, DrawCount3;
		public float ModelRadius;
		public uint LodCount;
		public uint CastShadows;
		public uint Padding;
	}

	internal readonly record struct Slot( int Offset, int Count )
	{
		public int End => Offset + Count;
	}

	private readonly List<ClutterBatch> _batches = [];

	// Batch table slots; a removed batch's slot is reused by the next one.
	private readonly List<ClutterBatch> _batchesById = [];
	private readonly Stack<int> _freeIds = [];

	// Every instance, by tile slot. Visibility and LOD are per view, so the culled buffers hold no state between views.
	private GpuBuffer<GpuInstanceTransform> _instances;
	private GpuBuffer<Vector4> _spheres;
	private GpuBuffer<uint> _instanceBatches;
	private GpuBuffer<uint> _instanceLods;
	private GpuBuffer<GpuInstanceTransform> _visible;
	private int _capacity;

	private readonly List<Slot> _freeSlots = [];
	private int _highWater;

	private GpuBuffer<GpuBatch> _batchTable;
	private GpuBuffer<uint> _counts;
	private GpuBuffer<GpuBuffer.IndirectDrawIndexedArguments> _args;
	private int _argsCount;

	private readonly HashSet<ClutterBatch.PreparedInstances> _incomingTiles = [];
	private readonly List<ClutterBatch.PreparedInstances> _removedTiles = [];
	private readonly List<ClutterBatch.PreparedInstances> _addedTiles = [];

	public ClutterRenderer( SceneWorld world ) : base( world )
	{
		Flags.IsOpaque = true;
		Flags.IsTranslucent = false;
		Flags.CastShadows = false;
		Flags.WantsPrePass = true;
	}

	public ClutterBatch CreateBatch( Model model, bool wantsShadows )
	{
		var batch = new ClutterBatch( model, wantsShadows );

		if ( _freeIds.TryPop( out var id ) )
		{
			_batchesById[id] = batch;
		}
		else
		{
			id = _batchesById.Count;
			_batchesById.Add( batch );
		}

		batch.Id = id;
		_batches.Add( batch );
		return batch;
	}

	public void RemoveBatch( ClutterBatch batch )
	{
		if ( batch.Id < 0 )
			return;

		foreach ( var slot in batch.Tiles.Values )
			ReleaseSlot( slot );

		batch.Tiles.Clear();
		batch.InstanceCount = 0;

		_batches.Remove( batch );
		_batchesById[batch.Id] = null;
		_freeIds.Push( batch.Id );
		batch.Id = -1;

		RebuildLayout();
	}

	/// <summary>
	/// Uploads a batch's instance set to the shared GPU buffers. Only called when the set changes.
	/// </summary>
	public void SetInstances( ClutterBatch batch, List<ClutterBatch.PreparedInstances> tiles )
	{
		_incomingTiles.Clear();
		foreach ( var tile in tiles )
			_incomingTiles.Add( tile );

		_removedTiles.Clear();
		foreach ( var tile in batch.Tiles.Keys )
		{
			if ( !_incomingTiles.Contains( tile ) )
				_removedTiles.Add( tile );
		}

		_addedTiles.Clear();
		foreach ( var tile in tiles )
		{
			if ( !batch.Tiles.ContainsKey( tile ) )
				_addedTiles.Add( tile );
		}

		if ( _removedTiles.Count == 0 && _addedTiles.Count == 0 )
			return;

		foreach ( var tile in _removedTiles )
		{
			batch.InstanceCount -= tile.Count;
			ReleaseSlot( batch.Tiles[tile] );
			batch.Tiles.Remove( tile );
		}

		foreach ( var tile in _addedTiles )
		{
			batch.InstanceCount += tile.Count;
			batch.Tiles[tile] = AllocateSlot( tile.Count );
		}

		if ( EnsureCapacity( _highWater ) )
		{
			// New buffers have no tile data. Mark unused slots, then restore every batch's tiles.
			_spheres.Clear( EmptySphereBits );
			foreach ( var other in _batches )
			{
				foreach ( var (tile, slot) in other.Tiles )
					UploadTile( other, tile, slot );
			}
		}
		else
		{
			foreach ( var tile in _addedTiles )
				UploadTile( batch, tile, batch.Tiles[tile] );
		}

		var bounds = tiles.Count > 0 ? tiles[0].Bounds : default;
		foreach ( var tile in tiles )
			bounds = bounds.AddBBox( tile.Bounds );

		batch.Bounds = bounds;
		RebuildLayout();
	}

	/// <summary>
	/// Re-applies <see cref="ShadowsEnabled"/>.
	/// </summary>
	public void UpdateShadows()
	{
		bool castShadows = false;
		if ( ShadowsEnabled )
		{
			foreach ( var batch in _batches )
				castShadows |= batch.WantsShadows && batch.InstanceCount > 0;
		}

		Flags.CastShadows = castShadows;
	}

	private void UploadTile( ClutterBatch batch, ClutterBatch.PreparedInstances tile, Slot slot )
	{
		_instances.SetData( tile.Transforms.AsSpan( 0, tile.Count ), slot.Offset );
		_spheres.SetData( tile.Spheres.AsSpan( 0, tile.Count ), slot.Offset );

		using var ids = new PooledSpan<uint>( tile.Count );
		ids.Span.Fill( (uint)batch.Id );
		_instanceBatches.SetData( ids.Span, slot.Offset );
	}

	/// <summary>
	/// Frees a slot and marks its instances empty, so culling skips them until the slot is reused.
	/// </summary>
	private void ReleaseSlot( Slot slot )
	{
		if ( _spheres is not null && slot.End <= _capacity )
		{
			using var empty = new PooledSpan<Vector4>( slot.Count );
			empty.Span.Fill( new Vector4( 0, 0, 0, -1 ) );
			_spheres.SetData( empty.Span, slot.Offset );
		}

		FreeSlot( slot );
	}

	private Slot AllocateSlot( int count )
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
				_freeSlots[bestSlotIndex] = new Slot( free.Offset + count, free.Count - count );

			return new Slot( free.Offset, count );
		}

		var slot = new Slot( _highWater, count );
		_highWater += count;
		return slot;
	}

	private void FreeSlot( Slot slot )
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
				_freeSlots[i - 1] = new Slot( previous.Offset, previous.Count + next.Count );
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

		var capacity = Math.Max( Math.Max( count, 1 ), _capacity + Math.Max( _capacity / 2, 1 ) );
		DisposeInstanceBuffers();
		_capacity = capacity;

		_instances = new GpuBuffer<GpuInstanceTransform>( capacity, GpuBuffer.UsageFlags.Structured, "ClutterInstances" );
		_spheres = new GpuBuffer<Vector4>( capacity, GpuBuffer.UsageFlags.Structured, "ClutterSpheres" );
		_instanceBatches = new GpuBuffer<uint>( capacity, GpuBuffer.UsageFlags.Structured, "ClutterInstanceBatches" );
		_instanceLods = new GpuBuffer<uint>( capacity, GpuBuffer.UsageFlags.Structured, "ClutterInstanceLods" );
		_visible = new GpuBuffer<GpuInstanceTransform>( capacity, GpuBuffer.UsageFlags.Structured, "ClutterVisible" );

		return true;
	}

	/// <summary>
	/// Lays every batch LOD's indirect draw entries out back to back and uploads the batch table the cull reads.
	/// </summary>
	private void RebuildLayout()
	{
		int entries = 0;
		foreach ( var batch in _batches )
		{
			if ( batch.InstanceCount == 0 )
				continue;

			for ( int lod = 0; lod < batch.LodCount; lod++ )
			{
				batch.ArgsBase[lod] = entries;
				entries += batch.DrawCallCounts[lod];
			}
		}

		_argsCount = entries;

		using var table = new PooledSpan<GpuBatch>( Math.Max( _batchesById.Count, 1 ) );
		table.Span.Clear();

		using var args = new PooledSpan<GpuBuffer.IndirectDrawIndexedArguments>( Math.Max( entries, 1 ) );

		BBox bounds = default;
		bool hasBounds = false;

		foreach ( var batch in _batches )
		{
			if ( batch.InstanceCount == 0 )
				continue;

			bounds = hasBounds ? bounds.AddBBox( batch.Bounds ) : batch.Bounds;
			hasBounds = true;

			ref var entry = ref table.Span[batch.Id];
			entry.LodSwitchDistances = new Vector4(
				batch.LodSwitchDistances[0],
				batch.LodCount > 1 ? batch.LodSwitchDistances[1] : 0,
				batch.LodCount > 2 ? batch.LodSwitchDistances[2] : 0,
				batch.LodCount > 3 ? batch.LodSwitchDistances[3] : 0 );
			entry.ModelRadius = batch.ModelRadius;
			entry.LodCount = (uint)batch.LodCount;
			entry.CastShadows = batch.WantsShadows ? 1u : 0u;

			for ( int lod = 0; lod < batch.LodCount; lod++ )
			{
				SetLodEntry( ref entry, lod, (uint)batch.ArgsBase[lod], (uint)batch.DrawCallCounts[lod] );

				for ( int d = 0; d < batch.DrawCallCounts[lod]; d++ )
				{
					batch.Model.GetLodDrawCallRange( lod, d, out int startIndex, out int indexCount, out int baseVertex );
					args.Span[batch.ArgsBase[lod] + d] = new GpuBuffer.IndirectDrawIndexedArguments
					{
						IndexCount = (uint)indexCount,
						FirstIndex = (uint)startIndex,
						BaseVertex = baseVertex
					};
				}
			}
		}

		if ( hasBounds )
			Bounds = bounds;

		UpdateShadows();

		if ( entries == 0 )
			return;

		_batchTable = Grow( _batchTable, _batchesById.Count, GpuBuffer.UsageFlags.Structured, "ClutterBatches" );
		_counts = Grow( _counts, _batchesById.Count * ClutterBatch.MaxLods, GpuBuffer.UsageFlags.Structured, "ClutterCounts" );
		_args = Grow( _args, entries, GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.IndirectDrawArguments, "ClutterArgs" );

		_batchTable.SetData( table.Span[.._batchesById.Count] );
		_args.SetData( args.Span[..entries] );
	}

	private static void SetLodEntry( ref GpuBatch entry, int lod, uint argsBase, uint drawCount )
	{
		switch ( lod )
		{
			case 0: entry.ArgsBase0 = argsBase; entry.DrawCount0 = drawCount; break;
			case 1: entry.ArgsBase1 = argsBase; entry.DrawCount1 = drawCount; break;
			case 2: entry.ArgsBase2 = argsBase; entry.DrawCount2 = drawCount; break;
			default: entry.ArgsBase3 = argsBase; entry.DrawCount3 = drawCount; break;
		}
	}

	private static GpuBuffer<T> Grow<T>( GpuBuffer<T> buffer, int count, GpuBuffer.UsageFlags flags, string name ) where T : unmanaged
	{
		if ( buffer is not null && buffer.ElementCount >= count )
			return buffer;

		var capacity = Math.Max( count, buffer is null ? 0 : buffer.ElementCount + buffer.ElementCount / 2 );
		buffer?.Dispose();
		return new GpuBuffer<T>( capacity, flags, name );
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
		if ( _argsCount == 0 || _highWater == 0 )
			return;

		bool shadow = Graphics.LayerType == SceneLayerType.Shadow;

		if ( PassNeedsCull() )
		{
			using ( CommandList.ProfileImmediate( CullSampler ) )
				Cull( shadow );
		}

		using ( CommandList.ProfileImmediate( DrawSampler ) )
			Draw( shadow );
	}

	private void Draw( bool shadow )
	{
		var view = new ViewBounds( shadow );

		foreach ( var batch in _batches )
		{
			if ( batch.InstanceCount == 0 )
				continue;

			bool castShadows = batch.WantsShadows && ShadowsEnabled;
			if ( shadow && !castShadows )
				continue;

			// Each LOD draw costs full material setup on the CPU even when the GPU culled it to zero instances,
			// so skip the LODs no tile in this view can reach.
			int lods = view.ReachableLods( batch );
			if ( lods == 0 )
				continue;

			Graphics.Attributes.Set( "DisableScreenSpaceShadows", castShadows ? 0 : 1 );

			for ( int lod = 0; lod < batch.LodCount; lod++ )
			{
				if ( (lods & (1 << lod)) != 0 )
					Graphics.DrawModelInstancedIndirect( batch.Model, _visible, _args, batch.ArgsBase[lod] * ArgsStride, lod );
			}
		}
	}

	/// <summary>
	/// The current view as the cull shader sees it, reduced to what can be tested per tile on the CPU. Every test is at
	/// least as loose as the shader's, so a skipped LOD is one the GPU culled to zero instances anyway.
	/// </summary>
	private readonly ref struct ViewBounds
	{
		// Distance and scale are widened by this much, so float differences against the GPU can't skip a live LOD.
		private const float Slack = 0.01f;

		private readonly Plane _left, _right, _top, _bottom;
		private readonly bool _testPlanes;
		private readonly float _maxDistance;
		private readonly LodParams _lod;

		public ViewBounds( bool shadow )
		{
			// Only the side planes: the shader skips near and far for orthographic views, and side planes alone are
			// looser everywhere else. A frustum scale below 1 widens the shader's planes past the view's.
			var frustum = Graphics.Frustum;
			_left = frustum.LeftPlane;
			_right = frustum.RightPlane;
			_top = frustum.TopPlane;
			_bottom = frustum.BottomPlane;
			_testPlanes = CullFrustumScale >= 1.0f;

			_maxDistance = shadow ? EffectiveShadowDistance : 0.0f;
			_lod = Lod;
		}

		/// <summary>
		/// Bit mask of the LODs any of the batch's instances in this view can pick.
		/// </summary>
		public int ReachableLods( ClutterBatch batch )
		{
			int mask = 0;
			int all = (1 << batch.LodCount) - 1;

			foreach ( var tile in batch.Tiles.Keys )
			{
				float nearest = DistanceTo( tile.CenterBounds, _lod.CameraPos );
				if ( _maxDistance > 0.0f && nearest - tile.RadiusMax > _maxDistance * (1.0f + Slack) )
					continue;

				float farthest = FarthestDistance( tile.CenterBounds, _lod.CameraPos );

				// Every sphere in the tile lies inside its center bounds grown by the largest radius. The extra margin
				// grows with distance, covering sub-pixel jitter the shader's projection may carry and this test doesn't.
				var spheres = tile.CenterBounds.Grow( tile.RadiusMax * (1.0f + Slack) + farthest * Slack );
				if ( _testPlanes && !(_left.IsInFront( spheres, true ) && _right.IsInFront( spheres, true ) && _top.IsInFront( spheres, true ) && _bottom.IsInFront( spheres, true )) )
					continue;

				float scaleMin = Scale( batch, tile.RadiusMin );
				float scaleMax = Scale( batch, tile.RadiusMax );

				// Farther and smaller instances pick coarser LODs, so the nearest, largest and the farthest, smallest
				// instance possible bound every LOD in the tile.
				int finest = ComputeLod( batch, nearest * (1.0f - Slack), scaleMax * (1.0f + Slack) );
				int coarsest = ComputeLod( batch, farthest * (1.0f + Slack), scaleMin * (1.0f - Slack) );

				mask |= ((1 << (coarsest + 1)) - 1) & ~((1 << finest) - 1);
				if ( mask == all )
					break;
			}

			return mask;
		}

		private static float Scale( ClutterBatch batch, float radius ) => batch.ModelRadius > 1e-6f ? radius / batch.ModelRadius : 1.0f;

		/// <summary>
		/// Matches ComputeLod in clutter_cull_cs.shader.
		/// </summary>
		private int ComputeLod( ClutterBatch batch, float distance, float scale )
		{
			float tanHalf = MathF.Max( _lod.TanHalfFov, 1e-5f );
			float screen = _lod.OrthoWidth > 0.0f
				? Math.Clamp( 1.0f / _lod.OrthoWidth, 0.0f, 1.0f )
				: Math.Clamp( 0.5f / MathF.Max( distance * tanHalf, 1e-5f ), 0.0f, 1.0f );
			float pixels = screen * _lod.ViewportWidth;
			float metric = pixels > 0.0f ? 50.0f / pixels : 0.0f;

			int lod = Math.Max( batch.LodCount - 1, 0 );
			while ( lod > 0 )
			{
				float d = batch.LodSwitchDistances[lod] * scale;
				if ( d > 0.0f && d < metric )
					break;

				lod--;
			}

			return lod;
		}

		private static float DistanceTo( in BBox box, Vector3 point )
		{
			var closest = Vector3.Max( box.Mins, Vector3.Min( point, box.Maxs ) );
			return point.Distance( closest );
		}

		private static float FarthestDistance( in BBox box, Vector3 point )
		{
			var far = new Vector3(
				MathF.Abs( point.x - box.Mins.x ) > MathF.Abs( point.x - box.Maxs.x ) ? box.Mins.x : box.Maxs.x,
				MathF.Abs( point.y - box.Mins.y ) > MathF.Abs( point.y - box.Maxs.y ) ? box.Mins.y : box.Maxs.y,
				MathF.Abs( point.z - box.Mins.z ) > MathF.Abs( point.z - box.Maxs.z ) ? box.Mins.z : box.Maxs.z );
			return point.Distance( far );
		}
	}

	/// <summary>
	/// Culls every batch for the current view: count survivors per batch LOD, lay the LODs out in the visible buffer
	/// and write their draw arguments, then scatter the survivors into place.
	/// </summary>
	private void Cull( bool shadow )
	{
		var attributes = RenderAttributes.Pool.Get();

		attributes.Set( "AllInstances", _instances );
		attributes.Set( "AllInstanceSpheres", _spheres );
		attributes.Set( "AllInstanceBatches", _instanceBatches );
		attributes.Set( "ClutterBatches", _batchTable );
		attributes.Set( "InstanceCount", _highWater );
		attributes.Set( "BatchCount", _batchesById.Count );
		attributes.Set( "ClutterCounts", _counts );
		attributes.Set( "ClutterInstanceLods", _instanceLods );
		attributes.Set( "ClutterArgs", _args );
		attributes.Set( "ClutterVisible", _visible );

		attributes.Set( "ClutterFrustumScale", CullFrustumScale );
		attributes.Set( "ClutterLodCameraPos", Lod.CameraPos );
		attributes.Set( "ClutterLodTanHalfFov", Lod.TanHalfFov );
		attributes.Set( "ClutterLodViewportWidth", Lod.ViewportWidth );
		attributes.Set( "ClutterLodOrthoWidth", Lod.OrthoWidth );
		attributes.Set( "ClutterWorldToProjection", Graphics.ViewFrustum.GetReverseZViewProjTranspose() );
		attributes.Set( "ClutterMaxDistance", shadow ? EffectiveShadowDistance : 0.0f );
		attributes.Set( "CullShadowCasters", shadow ? 1 : 0 );

		Graphics.ResourceBarrierTransition( _counts, ResourceState.CopyDestination );
		_counts.Clear();
		Graphics.ResourceBarrierTransition( _counts, ResourceState.CopyDestination, ResourceState.UnorderedAccess );
		Graphics.ResourceBarrierTransition( _instanceLods, ResourceState.UnorderedAccess );

		attributes.SetCombo( "D_PASS", CountPass );
		CullShader.DispatchWithAttributes( attributes, _highWater, 1, 1 );

		Graphics.UavBarrier( _counts );
		Graphics.UavBarrier( _instanceLods );
		Graphics.ResourceBarrierTransition( _args, ResourceState.UnorderedAccess );

		attributes.SetCombo( "D_PASS", ArgsPass );
		CullShader.DispatchWithAttributes( attributes, 1, 1, 1 );

		Graphics.UavBarrier( _counts );
		Graphics.ResourceBarrierTransition( _visible, ResourceState.UnorderedAccess );

		attributes.SetCombo( "D_PASS", ScatterPass );
		CullShader.DispatchWithAttributes( attributes, _highWater, 1, 1 );

		Graphics.ResourceBarrierTransition( _visible, ResourceState.GenericRead );
		Graphics.ResourceBarrierTransition( _args, ResourceState.IndirectArgument );

		RenderAttributes.Pool.Return( attributes );
	}

	private void DisposeInstanceBuffers()
	{
		_instances?.Dispose();
		_instances = null;
		_spheres?.Dispose();
		_spheres = null;
		_instanceBatches?.Dispose();
		_instanceBatches = null;
		_instanceLods?.Dispose();
		_instanceLods = null;
		_visible?.Dispose();
		_visible = null;
		_capacity = 0;
	}

	internal override void OnNativeDestroy()
	{
		DisposeInstanceBuffers();

		_batchTable?.Dispose();
		_batchTable = null;
		_counts?.Dispose();
		_counts = null;
		_args?.Dispose();
		_args = null;

		base.OnNativeDestroy();
	}
}
