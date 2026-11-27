using System.Runtime.InteropServices;

namespace Borealis.Features;

/// <summary>
/// <see cref="InstancedMeshObject"/>s a view's resident list doesn't draw (a device without resident meshes, a view with CPU
/// occlusion culling or a debug view, an object whose slot run isn't made yet): each placement is culled against the view
/// and takes the LOD its size picks, then every LOD's placements draw as one instanced run in the opaque list, as a mesh's
/// instances do. Nothing about a placement is lost to the path: the same frustum and size culls, the same LOD rule.
/// </summary>
internal sealed partial class MeshRenderFeature
{
	/// <summary>What the feature keeps of each instanced object a frame, by world index.</summary>
	struct InstanceState
	{
		public InstancedMeshObject Object;

		/// <summary>The frame (<see cref="TransformBuffer.Generation"/>) its entries were found for, and their first slot.</summary>
		public int Generation, First;

		/// <summary>Each placement's LOD at <see cref="LodGeneration"/> (255 for one not asked yet), for the main camera: every view takes the one the first chose.</summary>
		public byte[] Lods;
		public int LodGeneration;
		public InstancedMeshObject.Placement Placement;
	}

	InstanceState[] instanceStates = [];

	// One object's placements in one view: each one's LOD or NotDrawn, then how many take each LOD and the ids grouped by LOD
	const byte NotDrawn = 254, LodUnknown = 255;
	byte[] instanceDrawn = [];
	uint[] instanceIds = [];
	int[] lodCounts = [], lodStarts = [];
	float[] lodScale = [], lodFallback = [];

	/// <summary>Placements the CPU path culled since the feature was made - for tests.</summary>
	internal int InstancesCulled { get; private set; }

	/// <summary>
	/// Add a run per LOD of every visible instanced object this view's resident list doesn't draw to <paramref name="runs"/>
	/// (the opaque list), writing instance ids after the list's own.
	/// </summary>
	void EmitInstances( RenderWorld world, ViewPass pass, TransformBuffer transforms, List<int> indices, List<Run> runs, Vector3 camera, float tanHalfFov, float screenWidth, in TextureSizes textures )
	{
		var objects = world.Objects;
		var shadow = pass.IsShadow;
		var frustum = pass.View.Frustum;
		var size = pass.KeepsSmallCasters ? default : SizeCull.ForPass( pass );
		var sphere = pass.LightSphere;
		var texels = pass.LodTexelsPerUnit;
		var splits = pass.SplitsLods;
		if ( instanceStates.Length < objects.Length ) Array.Resize( ref instanceStates, Math.Max( objects.Length, instanceStates.Length * 2 ) );

		foreach ( var index in CollectionsMarshal.AsSpan( indices ) )
		{
			var obj = (InstancedMeshObject)objects[index];
			if ( obj.Mesh is not { IsSkinned: false } model || obj.Count == 0 || obj.LayerMatch != LayerMatch.None || (!shadow && !obj.GameLayers) ) continue;

			var placement = obj.Placed;
			var count = placement.Centers.Length;
			var first = FirstSlot( obj, index, world, transforms );
			ref var state = ref instanceStates[index];
			var lodCount = model.LodCount;

			// The LODs chosen from the root camera, once a frame; a view of its own texels chooses its own
			byte[] lods = null;
			if ( texels <= 0 && lodCount > 1 )
			{
				if ( state.Lods is null || state.Lods.Length != count || !ReferenceEquals( state.Placement, placement ) || state.LodGeneration != transforms.Generation )
				{
					if ( state.Lods is null || state.Lods.Length != count ) state.Lods = new byte[count];
					Array.Fill( state.Lods, LodUnknown );
					state.Placement = placement;
					state.LodGeneration = transforms.Generation;
				}

				lods = state.Lods;
			}

			if ( instanceDrawn.Length < count ) instanceDrawn = new byte[Math.Max( count, instanceDrawn.Length * 2 )];
			if ( lodCounts.Length < lodCount )
			{
				lodCounts = new int[lodCount];
				lodStarts = new int[lodCount];
				lodScale = new float[lodCount];
				lodFallback = new float[lodCount];
			}

			Array.Clear( lodCounts, 0, lodCount );
			Array.Fill( lodScale, -1.0f, 0, lodCount );
			Array.Clear( lodFallback, 0, lodCount );
			var centers = placement.Centers;
			var extents = placement.Extents;
			var scales = placement.Scales;
			var drawn = 0;
			for ( int i = 0; i < count; i++ )
			{
				var center = centers[i];
				var extent = extents[i];
				if ( !frustum.Intersects( center, extent ) || size.Culls( center, extent ) || (sphere is { } s && center.Distance( s.Center ) - extent.Length > s.Radius) )
				{
					instanceDrawn[i] = NotDrawn;
					continue;
				}

				var lod = 0;
				if ( lodCount > 1 )
				{
					if ( lods is null ) lod = model.LodForScreenSize( texels, scales[i] );
					else if ( lods[i] != LodUnknown ) lod = lods[i];
					else lods[i] = (byte)(lod = LodOf( model, center, scales[i], camera, tanHalfFov, screenWidth ));
				}

				// The cache and what's drawn over it each take their LODs of an object that animates some (ViewPass.SplitsLods)
				if ( splits && !pass.DrawsLod( obj, lod ) )
				{
					instanceDrawn[i] = NotDrawn;
					continue;
				}

				instanceDrawn[i] = (byte)lod;
				lodCounts[lod]++;
				textures.AccumulateObject( center, extent, ref lodScale[lod], ref lodFallback[lod] );
				drawn++;
			}

			InstancesCulled += count - drawn;
			if ( drawn == 0 ) continue;

			// Ids grouped by LOD: each LOD's slice starts where the one before ends
			if ( instanceIds.Length < drawn ) instanceIds = new uint[Math.Max( drawn, instanceIds.Length * 2 )];
			var at = 0;
			for ( int lod = 0; lod < lodCount; lod++ )
			{
				lodStarts[lod] = at;
				at += lodCounts[lod];
			}

			var per = obj.EntriesPerInstance;
			for ( int i = 0; i < count; i++ )
			{
				if ( instanceDrawn[i] != NotDrawn ) instanceIds[lodStarts[instanceDrawn[i]]++] = (uint)(first + i * per);
			}

			at = 0;
			for ( int lod = 0; lod < lodCount; lod++ )
			{
				var length = lodCounts[lod];
				if ( length == 0 ) continue;

				var bin = BinFor( new BinKey( model, lod, null, false, DrawFilter.Opaque, null, null, null, false, false, false, null, false ) );
				bin.Used = mainPrepares;
				var ids = instanceIds.AsSpan( at, length );
				int start;
				if ( bin.Tinted )
				{
					// A block of ids per draw, each draw's own tint entry
					start = transforms.InstanceCount;
					var offset = model.TintOffsetForLod( lod );
					var lodDraws = model.DrawsForLod( lod ).Length;
					for ( int d = 0; d < lodDraws; d++ )
					{
						foreach ( var id in ids ) transforms.AddInstance( (int)id + offset + d );
					}
				}
				else
				{
					start = transforms.AddInstances( ids );
				}

				runs.Add( new Run( bin, start, length, -1, lodScale[lod], lodFallback[lod] ) );
				at += length;
			}
		}
	}

	/// <summary>
	/// An instanced object's first transform slot this frame: its kept run when it has one (written if its entries are out of date),
	/// else frame entries, which the first view to ask writes. Asked once a frame, however many views draw it.
	/// </summary>
	int FirstSlot( InstancedMeshObject obj, int index, RenderWorld world, TransformBuffer transforms )
	{
		ref var state = ref instanceStates[index];
		if ( ReferenceEquals( state.Object, obj ) && state.Generation == transforms.Generation ) return state.First;

		var first = transforms.KeptRunFor( index, obj, obj.RunLength, world.EntryVersions[index], out var write );
		if ( first >= 0 )
		{
			if ( write ) obj.WriteEntries( transforms, first, true );
		}
		else
		{
			first = transforms.FrameBase + transforms.Count;
			obj.WriteEntries( transforms, first, false );
		}

		state.Object = obj;
		state.Generation = transforms.Generation;
		state.First = first;
		return first;
	}

	/// <summary>
	/// The LOD of a placement with this scale at <paramref name="center"/>, as <see cref="ChooseLod"/> picks a mesh's.
	/// </summary>
	internal static int LodOf( RenderMesh model, Vector3 center, float scale, Vector3 camera, float tanHalfFov, float screenWidth )
	{
		var distance = center.Distance( camera );
		var screenSize = distance < 0.5f ? 1.0f : Math.Clamp( 0.5f / (distance * tanHalfFov), 0, 1 );
		return model.LodForScreenSize( screenSize * screenWidth, scale );
	}
}
