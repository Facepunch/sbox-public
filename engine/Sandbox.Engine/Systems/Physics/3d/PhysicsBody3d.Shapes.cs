using NativeEngine;
using System.Runtime.InteropServices;
using Sandbox.Utility;
using static Sandbox.PhysicsGroupDescription.BodyPart;

namespace Sandbox;

partial class PhysicsBody3d
{
	public override int ShapeCount => native.GetShapeCount();

	/// <summary>
	/// All shapes that belong to this body.
	/// </summary>
	public override IEnumerable<PhysicsShape> Shapes
	{
		get
		{
			var shapeCount = native.GetShapeCount();

			for ( int i = 0; i < shapeCount; ++i )
			{
				yield return native.GetShape( i ).Owner;
			}
		}
	}

	/// <summary>
	/// Add a sphere shape to this body.
	/// </summary>
	public override PhysicsShape AddSphereShape( Vector3 center, float radius, bool rebuildMass = true )
	{
		var shape = native.AddSphereShape( center, radius );
		Dirty();
		return shape.Owner;
	}

	/// <summary>
	/// Add a sphere shape to this body.
	/// </summary>
	public override PhysicsShape AddSphereShape( in Sphere sphere, bool rebuildMass = true )
	{
		var shape = native.AddSphereShape( sphere.Center, sphere.Radius );
		Dirty();
		return shape.Owner;
	}

	/// <summary>
	/// Add a capsule shape to this body.
	/// </summary>
	public override PhysicsShape AddCapsuleShape( Vector3 center, Vector3 center2, float radius, bool rebuildMass = true )
	{
		var shape = native.AddCapsuleShape( center, center2, radius );
		Dirty();
		return shape.Owner;
	}

	/// <summary>
	/// Add a box shape to this body.
	/// </summary>
	public override PhysicsShape AddBoxShape( Vector3 position, Rotation rotation, Vector3 extent, bool rebuildMass = true )
	{
		var shape = native.AddBoxShape( position, rotation, extent.Abs() );
		Dirty();
		return shape.Owner;
	}

	/// <summary>
	/// Add a box shape to this body.
	/// </summary>
	public override PhysicsShape AddBoxShape( BBox box, Rotation rotation, bool rebuildMass = true )
	{
		var shape = native.AddBoxShape( box.Center, rotation, box.Size * 0.5f );
		Dirty();
		return shape.Owner;
	}

	/// <inheritdoc cref="AddHullShape(Vector3, Rotation, Span{Vector3}, bool)"/>
	public override PhysicsShape AddHullShape( Vector3 position, Rotation rotation, List<Vector3> points, bool rebuildMass = true )
	{
		return AddHullShape( position, rotation, CollectionsMarshal.AsSpan( points ), rebuildMass );
	}

	/// <summary>
	/// Add a convex hull shape to this body.
	/// </summary>
	public override unsafe PhysicsShape AddHullShape( Vector3 position, Rotation rotation, Span<Vector3> points, bool rebuildMass = true )
	{
		if ( points.Length == 0 )
			return null;

		PhysicsShape3d shape;

		fixed ( Vector3* points_ptr = points )
		{
			shape = native.AddHullShape( position, rotation, points.Length, (IntPtr)points_ptr );
		}

		if ( !shape.IsValid() || shape.ShapeType == PhysicsShapeType.SHAPE_SPHERE )
		{
			Log.Warning( "Unable to create hull shape" );
		}

		Dirty();

		return shape.Owner;
	}

	/// <inheritdoc cref="AddMeshShape(Span{Vector3}, Span{int})"/>
	public override PhysicsShape AddMeshShape( List<Vector3> vertices, List<int> indices )
	{
		return AddMeshShape( CollectionsMarshal.AsSpan( vertices ), CollectionsMarshal.AsSpan( indices ) );
	}

	/// <summary>
	/// Adds a mesh type shape to this physics body. Mesh shapes cannot be physically simulated!
	/// </summary>
	public override unsafe PhysicsShape AddMeshShape( Span<Vector3> vertices, Span<int> indices )
	{
		if ( vertices.Length == 0 )
			return null;

		if ( indices.Length == 0 )
			return null;

		var vertexCount = vertices.Length;

		foreach ( var i in indices )
		{
			if ( i < 0 || i >= vertexCount )
				throw new ArgumentOutOfRangeException( $"Index ({i}) out of range ({vertexCount - 1})" );
		}

		PhysicsShape3d shape;

		fixed ( Vector3* vertices_ptr = vertices )
		fixed ( int* indices_ptr = indices )
		{
			shape = native.AddMeshShape( vertexCount, (IntPtr)vertices_ptr, indices.Length, (IntPtr)indices_ptr, 0 );
		}

		if ( !shape.IsValid() || shape.ShapeType == PhysicsShapeType.SHAPE_SPHERE )
		{
			Log.Warning( "Unable to create mesh shape" );
		}

		Dirty();

		return shape.Owner;
	}

	private static readonly int[] PlaneIndices = [0, 1, 2, 2, 3, 0];

	public override PhysicsShape AddPlaneShape( Vector3 center, Rotation rotation, Vector2 size, bool rebuildMass = true )
	{
		var tangent = rotation.Right;
		var bitangent = rotation.Down;

		var halfX = size.x * 0.5f;
		var halfY = size.y * 0.5f;

		Span<Vector3> vertices =
		[
			center - tangent * halfX - bitangent * halfY,
			center + tangent * halfX - bitangent * halfY,
			center + tangent * halfX + bitangent * halfY,
			center - tangent * halfX + bitangent * halfY
		];

		return AddMeshShape( vertices, PlaneIndices );
	}

	internal override unsafe PhysicsShape AddHeightFieldShape( ushort[] heights, byte[] materials, int sizeX, int sizeY, float sizeScale, float heightScale, int materialCount )
	{
		if ( heights == null )
			throw new ArgumentException( "Height data is null" );

		var cellCount = sizeX * sizeY;
		if ( cellCount <= 0 )
			throw new ArgumentOutOfRangeException( "Size needs to be non zero" );

		if ( heights.Length != cellCount )
			throw new ArgumentOutOfRangeException( $"Height data length is {heights.Length}, should be {cellCount}" );

		if ( materials != null && materials.Length != cellCount )
			throw new ArgumentOutOfRangeException( $"Material data length is {materials.Length}, should be {cellCount}" );

		fixed ( ushort* pHeights = heights )
		fixed ( byte* pMaterials = materials )
		{
			var shape = native.AddHeightFieldShape(
				(IntPtr)pHeights,
				(IntPtr)pMaterials,
				sizeX, sizeY,
				sizeScale, heightScale,
				materialCount );

			Dirty();

			return shape.Owner;
		}
	}

	/// <summary>
	/// Remove all physics shapes, but not the physics body itself.
	/// </summary>
	public override void ClearShapes()
	{
		native.PurgeShapes();
	}

	internal override IDisposable TriggerScope()
	{
		native.SetTrigger( true );
		return new DisposeAction( () => native.SetTrigger( false ) );
	}

	/// <summary>
	/// Called from Shape.Remove()
	/// </summary>
	internal override void RemoveShape( PhysicsShape shape )
	{
		if ( !shape.IsValid() )
			return;

		if ( !this.IsValid() )
			return;

		if ( !World.IsValid() )
			return;

		native.RemoveShape( (PhysicsShape3d)shape._shape );
	}

	/// <summary>
	/// Meant to be only used on <b>dynamic</b> bodies, rebuilds mass from all shapes of this body based on their volume and physics properties.
	/// </summary>
	public override void RebuildMass() => native.BuildMass();

	public override PhysicsShape AddShape( HullPart part, Transform transform, bool rebuildMass = true )
	{
		var shape = native.AddHullShape( part.hull, transform );

		if ( !shape.IsValid() || shape.ShapeType == PhysicsShapeType.SHAPE_SPHERE )
		{
			Log.Warning( "Unable to create hull shape" );
		}

		Dirty();

		return shape.Owner;
	}

	public override PhysicsShape AddShape( MeshPart part, Transform transform, bool convertToHull, bool rebuildMass = true )
	{
		PhysicsShape3d shape;

		if ( convertToHull )
		{
			shape = native.AddHullShape( part.mesh, transform );
		}
		else
		{
			shape = native.AddMeshShape( part.mesh, transform, part.Surfaces is null ? 0 : part.Surfaces.Length );
		}

		if ( !shape.IsValid() || shape.ShapeType == PhysicsShapeType.SHAPE_SPHERE )
		{
			Log.Warning( $"Unable to create {(convertToHull ? "hull" : "mesh")} shape" );
		}

		Dirty();

		return shape.Owner;
	}
}
