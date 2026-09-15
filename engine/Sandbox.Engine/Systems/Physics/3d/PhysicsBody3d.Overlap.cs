using NativeEngine;

namespace Sandbox;

partial class PhysicsBody3d
{
	/// <summary>
	/// Checks if another body overlaps us, ignoring all collision rules
	/// </summary>
	public override bool CheckOverlap( PhysicsBody body )
	{
		if ( !body.IsValid() )
			return false;

		return CheckOverlap( body, body.Transform );
	}

	/// <summary>
	/// Checks if another body overlaps us at a given transform, ignoring all collision rules
	/// </summary>
	public override bool CheckOverlap( PhysicsBody body, Transform transform )
	{
		if ( !this.IsValid() || !body.IsValid() )
			return false;

		return native.CheckOverlap( (PhysicsBody3d)body._body, transform );
	}

	/// <summary>
	/// Checks if there's any contact points with another body
	/// </summary>
	internal override bool IsTouching( PhysicsBody body, bool triggersOnly )
	{
		if ( !body.IsValid() )
			return false;

		return native.IsTouching( (PhysicsBody3d)body._body, triggersOnly );
	}

	/// <summary>
	/// Checks if there's any contact points with another shape
	/// </summary>
	internal override bool IsTouching( PhysicsShape shape, bool triggersOnly )
	{
		if ( !shape.IsValid() )
			return false;

		return native.IsTouching( (PhysicsShape3d)shape._shape, triggersOnly );
	}

	/// <summary>
	/// Finds the smallest move needed to separate us from another body, ignoring all collision rules.
	/// </summary>
	public override bool ComputePenetration( PhysicsBody body, out Vector3 direction, out float distance )
	{
		direction = default;
		distance = default;

		if ( !body.IsValid() )
			return false;

		return ComputePenetration( body, body.Transform, out direction, out distance );
	}

	/// <summary>
	/// Finds the smallest move needed to separate us from another body at a given transform, ignoring all collision rules.
	/// </summary>
	public override bool ComputePenetration( PhysicsBody body, Transform transform, out Vector3 direction, out float distance )
	{
		direction = default;
		distance = default;

		if ( !this.IsValid() || !body.IsValid() )
			return false;

		return native.ComputePenetration( (PhysicsBody3d)body._body, transform, out direction, out distance );
	}
}
