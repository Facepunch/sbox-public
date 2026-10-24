namespace Sandbox;

/// <summary>
/// Defines a collider from a compiled or procedural physics resource.
/// </summary>
[Hide]
[Expose]
[Title( "Physics Collider" )]
[Category( "Physics" )]
[Icon( "check_box_outline_blank" )]
public class PhysicsCollider : Collider, IHasPhysicsDescription
{
	private PhysicsGroupDescription _physics;

	[Property]
	public PhysicsGroupDescription Physics
	{
		get => _physics;
		set
		{
			_physics = value;

			UpdateShape();
		}
	}

	internal override void UpdateShape()
	{
		var body = PhysicsBody;
		using var mass = body.IsValid() ? body._body.BatchMassUpdates() : default;
		base.UpdateShape();

		// Because we rebuild the physics shapes, allow rigidbody to apply their properties.
		Rigidbody?.UpdateBody();
	}

	protected override void DrawGizmos()
	{
		if ( !Gizmo.IsSelected && !Gizmo.IsHovered )
			return;

		if ( !Physics.IsValid() ) return;

		Gizmo.Draw.Color = Gizmo.Colors.Green;

		foreach ( var part in Physics.Parts )
		{
			using ( Gizmo.Scope( $"part {part.GetHashCode()}", part.Transform ) )
			{
				foreach ( var sphere in part.Spheres )
				{
					Gizmo.Draw.LineSphere( sphere.Sphere );
				}

				foreach ( var capsule in part.Capsules )
				{
					Gizmo.Draw.LineCapsule( capsule.Capsule );
				}

				foreach ( var hull in part.Hulls )
				{
					Gizmo.Draw.Lines( hull.GetLines() );
				}

				foreach ( var mesh in part.Meshes )
				{
					Gizmo.Draw.LineTriangles( mesh.GetTriangles() );
				}
			}
		}
	}

	protected override IEnumerable<PhysicsShape> CreatePhysicsShapes( PhysicsBody targetBody, Transform local )
	{
		if ( !Physics.IsValid() )
			yield break;

		foreach ( var part in Physics.Parts )
		{
			Assert.NotNull( part, "Physics part was null" );

			var bx = local.ToWorld( part.Transform );

			foreach ( var sphere in part.Spheres )
			{
				PhysicsShape shape;
				using ( targetBody._body.UseShapeSurface( sphere.Surface ) )
				{
					shape = targetBody.AddSphereShape( bx.PointToWorld( sphere.Sphere.Center ), sphere.Sphere.Radius * bx.UniformScale );
				}
				yield return shape;
			}

			foreach ( var capsule in part.Capsules )
			{
				PhysicsShape shape;
				using ( targetBody._body.UseShapeSurface( capsule.Surface ) )
				{
					shape = targetBody.AddCapsuleShape( bx.PointToWorld( capsule.Capsule.CenterA ), bx.PointToWorld( capsule.Capsule.CenterB ), capsule.Capsule.Radius * bx.UniformScale );
				}
				yield return shape;
			}

			if ( !Scene.Is2D )
			{
				foreach ( var hull in part.Hulls )
				{
					PhysicsShape shape;
					using ( targetBody._body.UseShapeSurface( hull.Surface ) )
					{
						shape = targetBody.AddShape( hull, bx );
					}
					yield return shape;
				}

				foreach ( var mesh in part.Meshes )
				{
					PhysicsShape shape;
					using ( targetBody._body.UseShapeSurface( mesh.Surface, mesh.Surfaces ) )
					{
						shape = targetBody.AddShape( mesh, bx, false, true );
					}
					yield return shape;
				}
			}
			else if ( targetBody?._body is PhysicsBody2d body2d )
			{
				foreach ( var hull in part.Hulls )
				{
					foreach ( var shape in body2d.AddHullPartShapes( hull, bx ) )
					{
						yield return shape;
					}
				}

				foreach ( var mesh in part.Meshes )
				{
					foreach ( var shape in body2d.AddMeshPartShapes( mesh, bx ) )
					{
						yield return shape;
					}
				}
			}

			if ( part.Mass > 0 )
				targetBody.Mass = part.Mass;

			if ( part.OverrideMassCenter )
				targetBody.LocalMassCenter = part.MassCenterOverride;

			if ( part.LinearDamping > 0 )
				targetBody.LinearDamping = part.LinearDamping;

			if ( part.AngularDamping > 0 )
				targetBody.AngularDamping = part.AngularDamping;

			if ( part.GravityScale != 1.0f )
			{
				targetBody.DefaultGravityScale = part.GravityScale;
				targetBody.GravityScale = part.GravityScale;
			}
		}
	}

	void IHasPhysicsDescription.OnPhysicsReloaded()
	{
		Rebuild();
	}
}
