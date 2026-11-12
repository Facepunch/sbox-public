namespace Sandbox;

public sealed partial class WaterBody
{
	readonly HashSet<Rigidbody> _touchingBodies = [];
	readonly List<WaterBody> _buoyancyWaters = [];
	readonly List<Collider> _buoyancyContacts = [];
	readonly List<Collider> _peerContacts = [];
	bool CanApplyBuoyancy => Active
		&& WorldScale.x != 0 && WorldScale.y != 0 && WorldScale.z != 0;

	/// <summary>
	/// Apply native buoyancy once per touching rigidbody, with one owning water body per impulse.
	/// </summary>
	void ApplyBuoyancy()
	{
		if ( Scene.IsEditor || !CanApplyBuoyancy || !_volume.IsValid() || !_volume.Active || Time.Delta <= 0 )
			return;

		_volume.GetTouching( _buoyancyContacts );
		foreach ( var collider in _buoyancyContacts )
			ApplyBuoyancy( collider );

		ClearBuoyancy();
	}

	/// <summary>
	/// Use native shape volumes, water density of 1000 kg/m³ and fixed drag at the sampled wave surface.
	/// </summary>
	void ApplyBuoyancy( Collider collider )
	{
		if ( !collider.IsValid() )
			return;

		var body = collider.Rigidbody;
		if ( !body.IsValid() || !_touchingBodies.Add( body ) || !body.PhysicsBody.IsValid() )
			return;

		if ( body.PhysicsBody.BodyType != PhysicsBodyType.Dynamic || body.IsProxy )
			return;

		var bounds = body.PhysicsBody.GetBounds();
		if ( !InFootprint( bounds.Center ) || !IntersectsFluid( bounds ) || BuoyancyOwner( body, collider, bounds ) != this )
			return;

		var local = ToSurface( bounds.Center );
		float height = SampleSurface( local, out var normal );
		body.ApplyBuoyancy( new Plane( FromSurface( local.WithZ( height ) ), normal ), 1000, 0.15f, 0.45f, Vector3.Zero, Time.Delta );
	}

	/// <summary>
	/// Test the bounds' vertical interval so floating bodies with centres above the surface remain buoyant.
	/// </summary>
	bool IntersectsFluid( BBox bounds )
	{
		var point = ToSurface( bounds.Center );
		float height = SampleHeight( point );
		float radius = Vector3.Dot( bounds.Extents, WorldRotation.Up.Abs() );
		return point.z - radius <= height && point.z + radius >= -WorldDepth;
	}

	/// <summary>
	/// Prefer eligible finite water over infinite water, then use scene order.
	/// A peer must currently touch the rigidbody so enabling or resizing it cannot suppress another body's impulse.
	/// </summary>
	WaterBody BuoyancyOwner( Rigidbody body, Collider contact, BBox bounds )
	{
		if ( _buoyancyWaters.Count == 0 )
			Scene.GetAll( _buoyancyWaters );

		WaterBody ocean = null;
		foreach ( var water in _buoyancyWaters )
		{
			if ( !water.CanApplyBuoyancy )
				continue;
			if ( water.Ocean && ocean is not null )
				continue;

			if ( !water.InFootprint( bounds.Center ) || !water.IntersectsFluid( bounds ) || !water.TouchesBody( body, contact ) )
				continue;

			if ( !water.Ocean )
				return water;
			ocean = water;
		}
		return ocean;
	}

	/// <summary>
	/// Require a native contact with any collider belonging to this rigidbody.
	/// </summary>
	bool TouchesBody( Rigidbody body, Collider contact )
	{
		if ( !_volume.IsValid() || !_volume.Active )
			return false;

		var touching = _volume.Touching;
		if ( touching.Contains( contact ) )
			return true;

		_volume.GetTouching( _peerContacts );
		bool found = false;
		foreach ( var collider in _peerContacts )
		{
			if ( collider.IsValid() && collider.Rigidbody == body )
			{
				found = true;
				break;
			}
		}
		_peerContacts.Clear();
		return found;
	}

	/// <summary>
	/// Release contact and overlap references after each simulation step or when the volume is destroyed.
	/// </summary>
	void ClearBuoyancy()
	{
		_touchingBodies.Clear();
		_buoyancyWaters.Clear();
		_buoyancyContacts.Clear();
	}
}
