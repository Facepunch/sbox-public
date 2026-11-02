using NativeEngine;
using System.Runtime.InteropServices;
using System.Buffers;
using static Sandbox.PhysicsGroupDescription.BodyPart;

namespace Sandbox;

partial class PhysicsBody3d
{
	unsafe PhysicsShapeSettings GetNativeShapeSettings( bool rebuildMass = true )
	{
		var settings = GetShapeSettings( rebuildMass );
		if ( CreationCollider is null )
			return settings;

		int index = 0;
		var tokens = CreationCollider.GameObject.Tags.GetTokens();
		if ( tokens is HashSet<uint> tags )
		{
			foreach ( var tag in tags )
			{
				if ( index == 16 ) break;
				settings.Tags[index++] = tag;
			}
			return settings;
		}

		foreach ( var tag in tokens )
		{
			if ( index == 16 ) break;
			settings.Tags[index++] = tag;
		}
		return settings;
	}

	PhysicsShape CompleteShapeCreation( PhysicsShape3d shape )
	{
		shape.InitializeCreationSettings( this );
		Dirty();
		return shape.Owner;
	}

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
		var shape = native.AddSphereShape( center, radius, GetNativeShapeSettings( rebuildMass ) );
		return CompleteShapeCreation( shape );
	}

	/// <summary>
	/// Add a sphere shape to this body.
	/// </summary>
	public override PhysicsShape AddSphereShape( in Sphere sphere, bool rebuildMass = true )
	{
		var shape = native.AddSphereShape( sphere.Center, sphere.Radius, GetNativeShapeSettings( rebuildMass ) );
		return CompleteShapeCreation( shape );
	}

	/// <summary>
	/// Add a capsule shape to this body.
	/// </summary>
	public override PhysicsShape AddCapsuleShape( Vector3 center, Vector3 center2, float radius, bool rebuildMass = true )
	{
		var shape = native.AddCapsuleShape( center, center2, radius, GetNativeShapeSettings( rebuildMass ) );
		return CompleteShapeCreation( shape );
	}

	/// <summary>
	/// Add a box shape to this body.
	/// </summary>
	public override PhysicsShape AddBoxShape( Vector3 position, Rotation rotation, Vector3 extent, bool rebuildMass = true )
	{
		var shape = native.AddBoxShape( position, rotation, extent.Abs(), GetNativeShapeSettings( rebuildMass ) );
		return CompleteShapeCreation( shape );
	}

	/// <summary>
	/// Add a box shape to this body.
	/// </summary>
	public override PhysicsShape AddBoxShape( BBox box, Rotation rotation, bool rebuildMass = true )
	{
		var shape = native.AddBoxShape( box.Center, rotation, box.Size * 0.5f, GetNativeShapeSettings( rebuildMass ) );
		return CompleteShapeCreation( shape );
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
			shape = native.AddHullShape( position, rotation, points.Length, (IntPtr)points_ptr, GetNativeShapeSettings( rebuildMass ) );
		}

		if ( !shape.IsValid() || shape.ShapeType == PhysicsShapeType.SHAPE_SPHERE )
		{
			Log.Warning( "Unable to create hull shape" );
		}

		return CompleteShapeCreation( shape );
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
		=> AddMeshShape( vertices, indices, true );

	unsafe PhysicsShape AddMeshShape( Span<Vector3> vertices, Span<int> indices, bool rebuildMass )
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
			shape = native.AddMeshShape( vertexCount, (IntPtr)vertices_ptr, indices.Length, (IntPtr)indices_ptr, 0, GetNativeShapeSettings( rebuildMass ) );
		}

		if ( !shape.IsValid() || shape.ShapeType == PhysicsShapeType.SHAPE_SPHERE )
		{
			Log.Warning( "Unable to create mesh shape" );
		}

		return CompleteShapeCreation( shape );
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

		return AddMeshShape( vertices, PlaneIndices, rebuildMass );
	}

	internal override unsafe PhysicsShape AddHeightFieldShape( ushort[] heights, byte[] materials, int sizeX, int sizeY, float sizeScale, float heightScale, int materialCount, ReadOnlySpan<int> surfaceIndices = default )
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
		fixed ( int* pSurfaces = surfaceIndices )
		{
			var settings = GetNativeShapeSettings();
			settings.SurfaceIndices = (IntPtr)pSurfaces;
			settings.SurfaceCount = surfaceIndices.Length;
			var shape = native.AddHeightFieldShape(
				(IntPtr)pHeights,
				(IntPtr)pMaterials,
				sizeX, sizeY,
				sizeScale, heightScale,
				materialCount, settings );

			return CompleteShapeCreation( shape );
		}
	}

	/// <summary>
	/// Remove all physics shapes, but not the physics body itself.
	/// </summary>
	public override void ClearShapes()
	{
		native.PurgeShapes();
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
	internal override bool SetMassUpdatesDeferred( bool defer ) => native.SetMassUpdatesDeferred( defer );

	public override PhysicsShape AddShape( HullPart part, Transform transform, bool rebuildMass = true )
	{
		var shape = native.AddHullShape( part.hull, transform, GetNativeShapeSettings( rebuildMass ) );

		if ( !shape.IsValid() || shape.ShapeType == PhysicsShapeType.SHAPE_SPHERE )
		{
			Log.Warning( "Unable to create hull shape" );
		}

		return CompleteShapeCreation( shape );
	}

	public override unsafe PhysicsShape AddShape( MeshPart part, Transform transform, bool convertToHull, bool rebuildMass = true )
	{
		PhysicsShape3d shape;
		var settings = GetNativeShapeSettings( rebuildMass );

		if ( convertToHull )
		{
			shape = native.AddHullShape( part.mesh, transform, settings );
		}
		else
		{
			int count = CreationSurfaces?.Length ?? 0;
			int[] rented = null;
			Span<int> surfaceIndices = count <= 64 ? stackalloc int[count] : (rented = ArrayPool<int>.Shared.Rent( count )).AsSpan( 0, count );
			try
			{
				for ( int i = 0; i < count; i++ )
					surfaceIndices[i] = CreationSurfaces[i]?.Index ?? -1;

				fixed ( int* indices = surfaceIndices )
				{
					settings.SurfaceIndices = (IntPtr)indices;
					settings.SurfaceCount = count;
					shape = native.AddMeshShape( part.mesh, transform, part.Surfaces?.Length ?? 0, settings );
				}
			}
			finally
			{
				if ( rented is not null )
					ArrayPool<int>.Shared.Return( rented );
			}
		}

		if ( !shape.IsValid() || shape.ShapeType == PhysicsShapeType.SHAPE_SPHERE )
		{
			Log.Warning( $"Unable to create {(convertToHull ? "hull" : "mesh")} shape" );
		}

		return CompleteShapeCreation( shape );
	}
}
