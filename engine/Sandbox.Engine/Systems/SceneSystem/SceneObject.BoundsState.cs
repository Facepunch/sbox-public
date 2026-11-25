namespace Sandbox;

public partial class SceneObject
{
	/// <summary>Where an object's world bounds come from.</summary>
	enum BoundsKind : byte
	{
		/// <summary>Read from native each time: native moves them itself (animated objects, lights, decals, a map's).</summary>
		Native,

		/// <summary>
		/// Kept here, changed only by <see cref="Bounds"/>: custom and dynamic objects, whose bounds don't follow their transform,
		/// and scene models on an animation instance, whose pose sets them.
		/// </summary>
		Fixed,

		/// <summary>Kept here, from the model's mesh instances and the transform (a rigid model's, <c>OBJECT_BOUNDS_RIGID</c>).</summary>
		Rigid,
	}

	BoundsKind _boundsKind;
	BBox _bounds;

	// SetBoundsInfinite's
	static readonly BBox InfiniteBounds = new( new Vector3( -float.MaxValue ), new Vector3( float.MaxValue ) );

	/// <summary>
	/// The world bounds as native keeps them, infinite ones included (<see cref="Bounds"/> collapses those to a point).
	/// </summary>
	internal BBox WorldBounds => _boundsKind == BoundsKind.Native ? native.GetBounds() : _bounds;

	/// <summary>Keep bounds here from now on: a custom or dynamic object starting with infinite ones.</summary>
	internal void KeepFixedBounds( bool infinite )
	{
		_boundsKind = BoundsKind.Fixed;
		_bounds = infinite ? InfiniteBounds : BBox.FromPositionAndSize( Vector3.Zero );
	}

	/// <summary>
	/// Keep these bounds here from now on: an object whose type works its bounds out itself, as native's object
	/// does (a light from its range, a cubemap from its box), set as native's <c>SetBounds</c> keeps them.
	/// </summary>
	private protected void KeepBounds( in BBox bounds )
	{
		_boundsKind = BoundsKind.Fixed;
		SetKeptBounds( bounds );
	}

	/// <summary>Keep a rigid model's bounds here from now on, worked out from its mesh instances as native's transform does.</summary>
	void KeepRigidBounds()
	{
		// A scene model starts as a rigid one, as native's were, until its pose sets them; one native code made keeps native's
		if ( !ResolvesDraws || (this is SceneModel && HasNativeObject) )
		{
			_boundsKind = BoundsKind.Native;
			return;
		}

		_boundsKind = BoundsKind.Rigid;
		UpdateRigidBounds();
	}

	/// <summary>
	/// A scene model's bounds worked out from the pose its animation instance published: kept here from now on.
	/// </summary>
	private protected void SetPoseBounds( in BBox bounds )
	{
		_boundsKind = BoundsKind.Fixed;
		SetKeptBounds( bounds );
	}

	/// <summary>Bounds set from outside, quantised as native's <c>CSceneSystem::SetObjectBounds</c> keeps them; invalid ones are ignored.</summary>
	void SetKeptBounds( in BBox bounds )
	{
		if ( _boundsKind == BoundsKind.Native ) return;
		if ( !Finite( bounds.Mins ) || !Finite( bounds.Maxs ) ) return;
		_bounds = new BBox( new Vector3( MathF.Floor( bounds.Mins.x ), MathF.Floor( bounds.Mins.y ), MathF.Floor( bounds.Mins.z ) ),
			new Vector3( MathF.Ceiling( bounds.Maxs.x ), MathF.Ceiling( bounds.Maxs.y ), MathF.Ceiling( bounds.Maxs.z ) ) );

		static bool Finite( in Vector3 v ) => float.IsFinite( v.x ) && float.IsFinite( v.y ) && float.IsFinite( v.z );
	}

	/// <summary>
	/// <c>CSceneObject::UpdateBoundingBoxToMatchTransform</c> for a rigid model: the transformed bounds of every mesh instance
	/// whose mesh group is on, or a point at the origin with none.
	/// </summary>
	void UpdateRigidBounds()
	{
		if ( _boundsKind != BoundsKind.Rigid || Model is not { } model || model.native.IsNull ) return;

		var transform = Transform;
		var mask = MeshGroupMask;
		BBox? total = null;
		foreach ( var (instanceMask, local) in ModelDraws.For( model ).Instances )
		{
			if ( (instanceMask & mask) == 0 ) continue;
			var world = TransformBounds( transform, local );
			total = total is { } sum ? new BBox( Vector3.Min( sum.Mins, world.Mins ), Vector3.Max( sum.Maxs, world.Maxs ) ) : world;
		}

		SetKeptBounds( total ?? new BBox( transform.Position, transform.Position ) );
	}

	/// <summary><c>CMeshSystem::ChangeModel</c>'s bounds: every mesh instance's together, transformed.</summary>
	void ModelChangedBounds()
	{
		if ( _boundsKind != BoundsKind.Rigid || Model is not { } model || model.native.IsNull ) return;

		BBox? local = null;
		foreach ( var (_, bounds) in ModelDraws.For( model ).Instances )
			local = local is { } sum ? new BBox( Vector3.Min( sum.Mins, bounds.Mins ), Vector3.Max( sum.Maxs, bounds.Maxs ) ) : bounds;

		if ( local is { } box ) SetKeptBounds( TransformBounds( Transform, box ) );
	}

	/// <summary>Native's <c>TransformAABB</c>: the box's centre transformed, its extents through the absolute rotation and scale.</summary>
	internal static BBox TransformBounds( in Transform transform, in BBox local )
	{
		var center = (local.Mins + local.Maxs) * 0.5f;
		var extents = local.Maxs - center;
		var worldCenter = transform.PointToWorld( center );

		var rotation = transform.Rotation;
		var x = rotation.Forward * transform.Scale.x;
		var y = rotation.Left * transform.Scale.y;
		var z = rotation.Up * transform.Scale.z;
		var worldExtents = new Vector3(
			MathF.Abs( extents.x * x.x ) + MathF.Abs( extents.y * y.x ) + MathF.Abs( extents.z * z.x ),
			MathF.Abs( extents.x * x.y ) + MathF.Abs( extents.y * y.y ) + MathF.Abs( extents.z * z.y ),
			MathF.Abs( extents.x * x.z ) + MathF.Abs( extents.y * y.z ) + MathF.Abs( extents.z * z.z ) );

		return new BBox( worldCenter - worldExtents, worldCenter + worldExtents );
	}
}
