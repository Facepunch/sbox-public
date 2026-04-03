
namespace Sandbox;

partial class MapInstance
{
	private PhysicsGroupDescription Physics;
	private List<PhysicsBody> Bodies { get; set; } = new();
	private List<CollisionEventSystem> CollisionEvents { get; set; } = new();
	private MapCollider Collider { get; set; }

	void OnEnableCollisionChanged()
	{
		AddCollision();

		foreach ( var body in Bodies )
		{
			if ( !body.IsValid() )
				continue;

			body.Enabled = EnableCollision;
		}
	}

	protected override void OnTagsChanged()
	{
		UpdateTags();
	}

	private void UpdateTags()
	{
		if ( !_mapPhysics.IsValid() )
			return;

		foreach ( var body in Bodies )
		{
			if ( !body.IsValid() )
				continue;

			var shapes = body.Shapes;

			foreach ( var shape in shapes )
			{
				shape.Tags.SetFrom( _mapPhysics.Tags );
			}
		}
	}

	private void RemoveCollision()
	{
		foreach ( var e in CollisionEvents )
			e.Dispose();

		CollisionEvents.Clear();

		foreach ( var body in Bodies )
		{
			if ( !body.IsValid() )
				continue;

			body.Remove();
		}

		Bodies.Clear();
	}

	private void AddCollision()
	{
		if ( Bodies.Any() )
			return;

		if ( !EnableCollision )
			return;

		if ( Physics is null )
			return;

		if ( Physics.Parts.Count == 0 )
			return;

		var world = NoOrigin ? new Transform( 0 ) : WorldTransform.WithScale( 1.0f );

		foreach ( var part in Physics.Parts )
		{
			Assert.NotNull( part, "Physics part was null" );

			var body = new PhysicsBody( Scene.PhysicsWorld );
			body.Component = Collider;
			body.Transform = world;
			Bodies.Add( body );
			CollisionEvents.Add( new CollisionEventSystem( body ) );

			var local = part.Transform;

			foreach ( var sphere in part.Spheres )
			{
				var shape = body.AddSphereShape( local.PointToWorld( sphere.Sphere.Center ), sphere.Sphere.Radius * local.UniformScale );
				Assert.NotNull( shape, "Sphere shape was null" );
				shape.Surface = sphere.Surface;
			}

			foreach ( var capsule in part.Capsules )
			{
				var shape = body.AddCapsuleShape( local.PointToWorld( capsule.Capsule.CenterA ), local.PointToWorld( capsule.Capsule.CenterB ), capsule.Capsule.Radius * local.UniformScale );
				Assert.NotNull( shape, "Capsule shape was null" );
				shape.Surface = capsule.Surface;
			}

			foreach ( var hull in part.Hulls )
			{
				var shape = body.AddShape( hull, local );
				Assert.NotNull( shape, "Hull shape was null" );
				shape.Surface = hull.Surface;
			}

			foreach ( var mesh in part.Meshes )
			{
				var shape = body.AddShape( mesh, local, false );
				Assert.NotNull( shape, "Mesh shape was null" );

				shape.Surface = mesh.Surface;
				shape.Surfaces = mesh.Surfaces;
			}

			SetCollisionAttributes( body, part );
		}

		UpdateTags();
	}

	private void SetCollisionAttributes( PhysicsBody body, PhysicsGroupDescription.BodyPart part )
	{
		if ( !body.IsValid() )
			return;

		var shapeCount = body.ShapeCount;
		var indicesCount = part.native.GetCollisionAttributeCount();
		var attributeCount = Physics.native.GetCollisionAttributeCount();

		if ( indicesCount > 0 )
		{
			if ( indicesCount != shapeCount )
			{
				Log.Warning( $"Inconsistent collision attribute index array of size {indicesCount}, expected {shapeCount} (number of shapes)" );
			}

			for ( int i = 0; i < shapeCount && i < indicesCount; ++i )
			{
				var index = part.native.GetCollisionAttributeIndex( i );
				if ( index < attributeCount )
				{
					var shape = body.native.GetShape( i );
					if ( !shape.IsValid() )
						continue;

					var tags = Physics.GetTags( index );
					if ( tags is null )
						continue;

					foreach ( var tag in tags )
					{
						shape.native.AddTag( tag.Value );
					}
				}
				else
				{
					Log.Warning( $"Invalid collision attribute palette entry {index}, expected below {attributeCount}" );
				}
			}
		}
		else if ( part.native.m_nCollisionAttributeIndex < attributeCount )
		{
			var tags = Physics.GetTags( part.native.m_nCollisionAttributeIndex );
			foreach ( var shape in body.Shapes )
			{
				foreach ( var tag in tags )
				{
					shape.native.AddTag( tag.Value );
				}
			}
		}
	}
}
