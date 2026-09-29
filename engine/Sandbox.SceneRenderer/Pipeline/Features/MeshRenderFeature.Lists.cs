namespace Sandbox.SceneRenderer.Features;

/// <summary>
/// Central layer membership, sorting and draw rules. New lists need a <see cref="MeshRuns"/> bit,
/// a <see cref="Lists"/> entry and classifier rules.
/// </summary>
internal sealed partial class MeshRenderFeature
{
	/// <summary>
	/// How a run list orders its objects.
	/// </summary>
	internal enum ListSort : byte
	{
		/// <summary>
		/// Group compatible materials and object state for instancing (<see cref="OpaqueKey"/>).
		/// </summary>
		Batched,

		/// <summary>
		/// Back to front on native's translucent sort key (<see cref="TranslucentSortDistance"/>), ties by world index.
		/// </summary>
		BackToFront,

		/// <summary>
		/// Nearest first, except fully translucent objects sort farthest first (<see cref="FullSortKey"/>).
		/// </summary>
		Full,

		/// <summary>
		/// Nearest first, whatever the object blends: decal geometry, which is never flagged translucent.
		/// </summary>
		NearestFirst,

		/// <summary>
		/// By <see cref="MeshObject.RenderOrder"/>, lowest first: a map's static overlays.
		/// </summary>
		RenderOrder,
	}

	/// <summary>
	/// Run-list sort, draw filter, fade combo and frame-copy policy.
	/// </summary>
	internal readonly record struct ListDefinition( MeshRuns Runs, ListSort Sort, DrawFilter Draws, bool Fade = false, bool CopiesFrameBuffer = false );

	/// <summary>
	/// Definitions indexed by <see cref="MeshRuns"/> bit, in draw order.
	/// </summary>
	internal static readonly ListDefinition[] Lists =
	[
		new( MeshRuns.Opaque, ListSort.Batched, DrawFilter.Opaque ),
		new( MeshRuns.OpaqueNoPrepass, ListSort.Batched, DrawFilter.Opaque ),
		new( MeshRuns.Faded, ListSort.Batched, DrawFilter.Opaque, Fade: true ),
		new( MeshRuns.Translucent, ListSort.BackToFront, DrawFilter.Translucent, CopiesFrameBuffer: true ),

		// Layer-matched overlays include all draws, including mixed blends.
		new( MeshRuns.StaticOverlay, ListSort.RenderOrder, DrawFilter.All ),

		new( MeshRuns.OverlayOpaque, ListSort.Batched, DrawFilter.Opaque ),
		new( MeshRuns.OverlayTranslucent, ListSort.BackToFront, DrawFilter.Translucent, CopiesFrameBuffer: true ),

		// Native's bloom layer isn't fully sorted
		new( MeshRuns.Bloom, ListSort.Batched, DrawFilter.Opaque ),

		new( MeshRuns.OverlayWithDepth, ListSort.Full, DrawFilter.Opaque ),
		new( MeshRuns.OverlayWithoutDepth, ListSort.Full, DrawFilter.Opaque ),
		new( MeshRuns.AfterUI, ListSort.Full, DrawFilter.Opaque, CopiesFrameBuffer: true ),

		// The refraction stencil draws only the translucent draws of what reads the copy
		new( MeshRuns.Refraction, ListSort.Batched, DrawFilter.Translucent ),

		new( MeshRuns.OverlayPrepass, ListSort.Batched, DrawFilter.Opaque ),
		new( MeshRuns.Decal, ListSort.NearestFirst, DrawFilter.Opaque ),
	];

	/// <summary>
	/// The lists a translucent layer copies the frame for, when something in them reads it (<see cref="ListDefinition.CopiesFrameBuffer"/>).
	/// </summary>
	internal static readonly MeshRuns FrameBufferCopyingLists = ListsWhere( static list => list.CopiesFrameBuffer );

	// Compute only the sort keys an object's lists need.
	static readonly MeshRuns BatchedLists = ListsWhere( static list => list.Sort == ListSort.Batched );
	static readonly MeshRuns DistanceSortedLists = ListsWhere( static list => list.Sort is ListSort.BackToFront or ListSort.Full or ListSort.NearestFirst );

	static MeshRuns ListsWhere( Func<ListDefinition, bool> predicate )
	{
		MeshRuns runs = 0;
		for ( int bit = 0; bit < Lists.Length; bit++ )
		{
			// Each list sits at its own bit, which is how layers name it
			if ( Lists[bit].Runs != (MeshRuns)(1 << bit) ) throw new InvalidOperationException( $"{Lists[bit].Runs} is listed at bit {bit}" );
			if ( predicate( Lists[bit] ) ) runs |= Lists[bit].Runs;
		}

		return runs;
	}

	/// <summary>
	/// Classify mesh flags, layer match and LOD blend for main or shadow views
	/// (<c>CRenderingPipelineStandard::AddLayersToView</c>, <c>CSceneSystem::AddShadowView</c>).
	/// </summary>
	internal static MeshRuns ListsFor( MeshObject obj, MeshBlend blend, bool shadow )
	{
		// Layer matches are exclusive and never enter shadow views.
		switch ( obj.LayerMatch )
		{
			case LayerMatch.None: break;
			case LayerMatch.StaticOverlay: return !shadow && obj.GameLayers ? MeshRuns.StaticOverlay : 0;
			case LayerMatch.OverlayWithDepth: return shadow ? 0 : MeshRuns.OverlayWithDepth;
			case LayerMatch.OverlayWithoutDepth: return shadow ? 0 : MeshRuns.OverlayWithoutDepth;
			default: return 0;
		}

		// Tint alpha below one selects the fade layer.
		var faded = obj.Tint.a < 1;

		// Object flags gate draw types; bake-only light blockers enable neither.
		var opaque = blend != MeshBlend.Translucent && obj.DrawsOpaque;
		var translucent = blend != MeshBlend.Opaque && obj.DrawsTranslucent;

		// What skips the prepass goes after the rest of the opaque layer
		var opaqueList = faded ? MeshRuns.Faded : !obj.DepthPrepass ? MeshRuns.OpaqueNoPrepass : MeshRuns.Opaque;
		MeshRuns runs = 0;

		if ( shadow )
		{
			// Shadows ignore game-layer flags. DrawDepthTranslucent excludes mixed and faded objects.
			if ( opaque ) runs |= opaqueList;
			if ( translucent && blend != MeshBlend.Partial && !faded ) runs |= MeshRuns.Translucent;
			return runs;
		}

		// Overlays prepass separately (DepthNormalPrepassLayer) and redraw unfaded in front.
		var game = obj.GameLayers;
		var overlay = obj.Overlay;
		if ( opaque && game ) runs |= opaqueList == MeshRuns.Opaque && overlay ? MeshRuns.OverlayPrepass : opaqueList;
		if ( opaque && overlay && !faded ) runs |= MeshRuns.OverlayOpaque;
		if ( translucent && game ) runs |= MeshRuns.Translucent;
		if ( translucent && overlay && !faded ) runs |= MeshRuns.OverlayTranslucent;

		// Flag-only layers ignore GameLayers. Bloom excludes faded objects.
		if ( obj.Bloom && !faded ) runs |= MeshRuns.Bloom;
		if ( obj.Decal ) runs |= MeshRuns.Decal;
		if ( obj.AfterUI ) runs |= MeshRuns.AfterUI;

		// Refraction depth includes only unfaded translucent readers.
		if ( obj.WantsFrameBufferCopy && translucent && !faded ) runs |= MeshRuns.Refraction;

		return runs;
	}

	/// <summary>
	/// Classify visible custom objects by blend and layer flags, matching native <c>SceneCustomObject</c>.
	/// </summary>
	internal static MeshRuns ListsFor( CustomObject obj, bool shadow )
	{
		if ( !obj.Visible ) return 0;

		switch ( obj.LayerMatch )
		{
			case LayerMatch.None: break;
			case LayerMatch.OverlayWithDepth: return shadow ? 0 : MeshRuns.OverlayWithDepth;
			case LayerMatch.OverlayWithoutDepth: return shadow ? 0 : MeshRuns.OverlayWithoutDepth;
			default: return 0;
		}

		// Shadow views take every caster culling picked
		var opaque = obj.IsOpaque;
		MeshRuns runs = 0;
		if ( shadow || obj.GameLayers ) runs |= opaque ? MeshRuns.Opaque : MeshRuns.Translucent;
		if ( shadow ) return runs;

		if ( obj.Overlay ) runs |= opaque ? MeshRuns.OverlayOpaque : MeshRuns.OverlayTranslucent;
		if ( obj.Bloom ) runs |= MeshRuns.Bloom;
		if ( obj.AfterUI ) runs |= MeshRuns.AfterUI;
		return runs;
	}

	/// <summary>
	/// Only enabled casters without an exclusive layer match enter shadow maps.
	/// </summary>
	internal override bool CastsShadow( RenderObject obj ) => obj.LayerMatch == LayerMatch.None && obj is MeshObject { CastShadows: true } or CustomObject { CastShadows: true };
}
