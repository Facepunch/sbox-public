namespace Borealis.Features;

/// <summary>
/// Which meshes may be resident (AggregateRenderFeature.Resident.cs): those whose every draw, at every LOD and in every
/// pass, the GPU-driven path takes the way <see cref="TakeGpuDriven"/> takes a run, in the opaque list only. Frame-buffer
/// readers too: colour layers take the frame copy before a list that holds one.
/// </summary>
internal sealed partial class MeshRenderFeature
{
	/// <summary>LODs a resident mesh's model may have: the GPU-driven batch keeps its LOD in four bits.</summary>
	const int MaxResidentLods = 16;

	/// <summary>
	/// Whether <paramref name="obj"/> may be resident: rigid, nothing per object but its lighting, opaque at every LOD and in
	/// the opaque list alone, and every material it draws with one the device takes resident. Everything this reads gives
	/// the object a new draw version when it changes. Whether it moves is the resident lists' business.
	/// </summary>
	internal bool ResidentCandidate( MeshObject obj, GpuDevice device )
	{
		if ( obj.Mesh is not { } model || obj.IsSkinned || obj.MorphSource is not null ) return false;
		if ( obj.Attributes is not null || obj.AttributesFrom is not null || obj.Callbacks is not null || obj.LayerMatch != LayerMatch.None ) return false;
		if ( obj.Tint.a < 1 || !obj.DepthPrepass || !obj.DrawsOpaque || obj.Overlay || obj.Bloom || obj.Decal || obj.AfterUI || (obj.WantsFrameBufferCopy && NoGpuDrivenReaders) ) return false;

		// What needs a stencil is prepared, so its view's depth gets one (MeshRenderFeature.WantsStencil)
		if ( obj.WantsStencil ) return false;
		if ( !UseMeshMaterials || model.LodCount > MaxResidentLods || model.SwitchDistances.Length > Gpu.GpuContext.ResidentMeshes.MaxSwitchDistances ) return false;
		if ( depthOnlyMaterial is not null && !device.TakesResident( depthOnlyMaterial ) ) return false;

		var materialOverride = obj.MaterialOverride;
		if ( materialOverride is not null && !device.TakesResident( materialOverride ) ) return false;
		for ( int lod = 0; lod < model.LodCount; lod++ )
		{
			if ( obj.BlendForLod( lod ) != MeshBlend.Opaque ) return false;
			foreach ( ref readonly var draw in model.DrawsForLod( lod ) )
			{
				if ( draw.Translucent ) return false;
				if ( materialOverride is null && (draw.Material is not { } material || !device.TakesResident( material )) ) return false;
			}
		}

		return true;
	}

	/// <summary>
	/// Whether a static overlay (<see cref="LayerMatch.StaticOverlay"/>) may be resident, in its render order's list: rigid,
	/// one LOD (its sort key holds its bin's), nothing per object but its lighting, no frame copy or stencil, and every material
	/// it draws with one the device takes resident. Blend and prepass don't matter in its layer.
	/// </summary>
	internal bool ResidentOverlayCandidate( MeshObject obj, GpuDevice device )
	{
		if ( obj.LayerMatch != LayerMatch.StaticOverlay || obj.Mesh is not { } model || obj.IsSkinned || obj.MorphSource is not null ) return false;
		if ( obj.Attributes is not null || obj.AttributesFrom is not null || obj.Callbacks is not null || obj.WantsFrameBufferCopy || obj.WantsStencil ) return false;
		if ( !UseMeshMaterials || model.LodCount != 1 ) return false;

		if ( obj.MaterialOverride is { } materialOverride ) return device.TakesOverlay( materialOverride );
		foreach ( ref readonly var draw in model.DrawsForLod( 0 ) )
		{
			if ( draw.Material is not { } material || !device.TakesOverlay( material ) ) return false;
		}

		return true;
	}

	/// <summary>
	/// Whether a static overlay that isn't resident draws the classic way after its render order's GPU-driven call, every
	/// draw at every LOD (<see cref="OverlayBatch"/> takes none of them, or the device takes none of its materials): then a
	/// list of the rest of its render order keeps the order, and the view draws it after the list.
	/// </summary>
	internal bool OverlayDrawnAfter( MeshObject obj, GpuDevice device )
	{
		if ( obj.LayerMatch != LayerMatch.StaticOverlay || obj.Mesh is not { } model || obj.AttributesFrom is not null || obj.Callbacks is not null ) return false;
		if ( obj.Attributes is not null || obj.IsSkinned || obj.MorphSource is not null || obj.WantsFrameBufferCopy ) return true;

		if ( obj.MaterialOverride is { } materialOverride ) return !device.TakesOverlay( materialOverride );
		for ( int lod = 0; lod < model.LodCount; lod++ )
		{
			foreach ( ref readonly var draw in model.DrawsForLod( lod ) )
			{
				if ( draw.Material is not { } material || device.TakesOverlay( material ) ) return false;
			}
		}

		return true;
	}

	/// <summary>
	/// A resident overlay's place in the static overlay list as <see cref="AddToLists"/> sorts it: render order, then its
	/// bin's material bits (<see cref="OpaqueKey"/>), then its world index.
	/// </summary>
	internal static ulong ResidentOverlayKey( MeshObject obj, ShaderParameters probe, int index )
	{
		var batchKey = OpaqueKey( obj.Mesh, 0, obj.MaterialOverride, obj.Attributes, obj.LightmapAttributes, probe );
		return (ulong)(uint)(obj.RenderOrder ^ int.MinValue) << 32 | (batchKey >> 24 & 0xFFFF) << 16 | (uint)index & 0xFFFF;
	}

	/// <summary>A resident mesh's light probe attributes in main views, as <see cref="Classify"/> picks them; null for none.</summary>
	internal static ShaderParameters ResidentProbe( MeshObject obj, List<LightProbeVolume> volumes, long volumesKey, Vector3 center, Vector3 extents )
	{
		if ( !obj.NeedsLightProbe || volumes.Count == 0 ) return null;
		var probe = ProbeFor( obj, volumes, volumesKey, center, extents, 0 );
		return probe >= 0 ? volumes[probe].Attributes : null;
	}
}
