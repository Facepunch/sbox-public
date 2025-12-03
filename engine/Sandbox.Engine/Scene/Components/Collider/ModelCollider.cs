namespace Sandbox;

/// <summary>
/// Defines a collider based on a model.
/// </summary>
[Expose]
[Title( "Model Collider" )]
[Category( "Physics" )]
[Icon( "check_box_outline_blank" )]
public class ModelCollider : Collider, IHasModel
{
	private Model _model;

	[Property]
	public Model Model
	{
		get => _model;
		set
		{
			_model = value;

			Rebuild();
		}
	}

	protected override void OnAwake()
	{
		base.OnAwake();

		if ( _model is null )
		{
			var renderer = GetComponent<ModelRenderer>();
			if ( renderer.IsValid() )
			{
				_model = renderer.Model;
			}
		}
	}

	protected override void DrawGizmos()
	{
		if ( !Gizmo.IsSelected && !Gizmo.IsHovered )
			return;

		if ( Model is null ) return;

		if ( Model.Physics is null ) return;

		Gizmo.Draw.Color = Gizmo.Colors.Green;

		foreach ( var part in Model.Physics.Parts )
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
		if ( Model is null || Model.Physics is null )
			yield break;

		if ( Model.Physics.Parts.Count == 0 )
			yield break;

		foreach ( var part in Model.Physics.Parts )
		{
			Assert.NotNull( part, "Physics part was null" );

			var boneIndex = Model.Bones.GetBone( part.BoneName )?.Index ?? -1;

			// Bone transform
			var bx = local.ToWorld( part.Transform );

			foreach ( var sphere in part.Spheres )
			{
				var shape = targetBody.AddSphereShape( bx.PointToWorld( sphere.Sphere.Center ), sphere.Sphere.Radius * bx.UniformScale );
				Assert.NotNull( shape, "Sphere shape was null" );
				shape.Surface = sphere.Surface;
				shape.BoneIndex = boneIndex;
				yield return shape;
			}

			foreach ( var capsule in part.Capsules )
			{
				var shape = targetBody.AddCapsuleShape( bx.PointToWorld( capsule.Capsule.CenterA ), bx.PointToWorld( capsule.Capsule.CenterB ), capsule.Capsule.Radius * bx.UniformScale );
				Assert.NotNull( shape, "Capsule shape was null" );
				shape.Surface = capsule.Surface;
				shape.BoneIndex = boneIndex;
				yield return shape;
			}

			foreach ( var hull in part.Hulls )
			{
				var shape = targetBody.AddShape( hull, bx );
				Assert.NotNull( shape, "Hull shape was null" );
				shape.Surface = hull.Surface;
				shape.BoneIndex = boneIndex;
				yield return shape;
			}

			foreach ( var mesh in part.Meshes )
			{
				var shape = targetBody.AddShape( mesh, bx, false, true );
				Assert.NotNull( shape, "Mesh shape was null" );

				shape.Surface = mesh.Surface;
				shape.Surfaces = mesh.Surfaces;
				shape.BoneIndex = boneIndex;

				yield return shape;
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

	internal void OnModelReloaded()
	{
		Rebuild();
	}

	void IHasModel.OnModelReloaded()
	{
		OnModelReloaded();
	}
}
