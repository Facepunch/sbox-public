namespace Sandbox;

public sealed partial class WaterBody
{
	/// <summary>
	/// A mesh on this object sets the water's extent: its top is the surface, its footprint the size and, unless it
	/// is flat, its height the depth. Works with an editable mesh, or a mesh tied to the object in Hammer. In game
	/// the mesh is switched off, so it neither draws nor collides.
	/// </summary>
	[Hide]
	public bool UsesMeshBounds => _meshBounds.HasValue;

	/// <summary>
	/// The mesh also sets the depth, which a flat mesh leaves to <see cref="Depth"/>.
	/// </summary>
	[Hide]
	public bool UsesMeshDepth => _meshBounds is { } bounds && bounds.Size.z >= 1;

	BBox? _meshBounds;
	object _meshBoundsSource;

	/// <summary>
	/// The mean surface's centre in local space: the origin, or the top centre of the object's mesh.
	/// </summary>
	internal Vector3 SurfaceOffset { get; private set; }

	/// <summary>
	/// The mean surface's centre in the world, which surface-relative coordinates are measured from.
	/// </summary>
	internal Vector3 SurfaceOrigin => SurfaceOffset == Vector3.Zero ? WorldPosition : WorldTransform.PointToWorld( SurfaceOffset );

	/// <summary>
	/// Take the extent from this object's mesh, if it has one. Returns whether it changed.
	/// </summary>
	bool UpdateMeshBounds()
	{
		var mesh = Components.Get<MeshComponent>( FindMode.EverythingInSelf );
		var hammerMesh = mesh is null ? Components.Get<HammerMesh>( FindMode.EverythingInSelf ) : null;

		// An editable mesh rebuilds its model on every edit, so the model identifies the geometry measured.
		object source = mesh is { Mesh: not null } ? mesh.Model ?? (object)mesh.Mesh
			: hammerMesh is not null ? (object)hammerMesh.EditorBounds ?? hammerMesh.Model : null;
		if ( source is null )
		{
			if ( _meshBounds is null && SurfaceOffset == Vector3.Zero )
				return false;
			_meshBounds = null;
			_meshBoundsSource = null;
			SurfaceOffset = Vector3.Zero;
			return true;
		}

		if ( Equals( source, _meshBoundsSource ) )
		{
			HideMeshInGame( mesh, hammerMesh );
			return false;
		}

		BBox? bounds = mesh is not null ? (mesh.Mesh.VertexHandles.Any() ? mesh.Mesh.CalculateBounds() : null)
			: hammerMesh.EditorBounds ?? hammerMesh.Model?.Bounds;
		_meshBoundsSource = source;
		if ( bounds is not { } box || !(box.Size.x > 0) || !(box.Size.y > 0) )
		{
			bool changed = _meshBounds is not null;
			_meshBounds = null;
			SurfaceOffset = Vector3.Zero;
			return changed;
		}

		_meshBounds = box;
		SurfaceOffset = box.Center.WithZ( box.Maxs.z );
		Size = new Vector2( box.Size.x, box.Size.y );
		if ( box.Size.z >= 1 )
			Depth = box.Size.z;
		HideMeshInGame( mesh, hammerMesh );
		return true;
	}

	/// <summary>
	/// The mesh only marks out the water. Outside the editor it would draw over the surface and block the volume.
	/// </summary>
	void HideMeshInGame( MeshComponent mesh, HammerMesh hammerMesh )
	{
		if ( Scene.IsEditor )
			return;
		if ( mesh is { Enabled: true } )
			mesh.Enabled = false;
		if ( hammerMesh is { Enabled: true } )
			hammerMesh.Enabled = false;
	}
}
