using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Sandbox;
using Sandbox.Resources;
using CapsulePart = Sandbox.PhysicsGroupDescription.BodyPart.CapsulePart;
using HullPart = Sandbox.PhysicsGroupDescription.BodyPart.HullPart;
using MeshPart = Sandbox.PhysicsGroupDescription.BodyPart.MeshPart;
using SpherePart = Sandbox.PhysicsGroupDescription.BodyPart.SpherePart;

namespace Editor;

partial class SceneCompiler
{
	internal sealed record ModelCollisionPart( Transform Transform, CollisionChunk[] Chunks, CollisionShape[] Shapes );

	/// <summary>
	/// Merge everything we compiled into vphys resources, one per tag set. Objects inherit
	/// tags from their parents and compiled geometry leaves that hierarchy behind, so shapes can only
	/// share a resource with shapes tagged the same.
	/// </summary>
	static async Task<List<(string Tags, PhysicsGroupDescription Physics)>> BuildCollision( CollisionChunk[] chunks, CollisionShape[] shapes,
		SceneFolder folder, string outputFolder, Func<int, int, Task> step )
	{
		var groups = new Dictionary<string, CollisionGroup>();

		foreach ( var chunk in chunks )
		{
			groups.GetOrCreate( chunk.Tags ).Add( chunk );
		}

		foreach ( var shape in shapes )
		{
			groups.GetOrCreate( shape.Tags ).Add( shape );
		}

		var result = new List<(string, PhysicsGroupDescription)>( groups.Count );
		var built = 0;

		foreach ( var (tags, group) in groups )
		{
			if ( group.Build() is { } data )
			{
				var index = result.Count;
				var physics = PhysicsGroupDescription.Load( Write( folder, $"{outputFolder}/collision_{index}.vphys_c", data ) );
				if ( physics is null )
					throw new InvalidOperationException( $"Could not load compiled collision resource {index}." );

				result.Add( (tags, physics) );
			}

			await step( ++built, groups.Count );
		}

		return result;
	}

	/// <summary>
	/// All the collision sharing one tag set. Triangle geometry folds into a single welded mesh
	/// shape - render vertices are split wherever normals or texcoords differ, and every chunk
	/// starts its own set, so we index them by position. Triangles that share an edge have to share
	/// their vertices for box3d to find the concave edges ( RnMesh_t::m_TriangleFlags ), or
	/// everything snags on the seams. Convex shapes stay whole, one body each, because a body is
	/// what carries a surface.
	/// </summary>
	sealed class CollisionGroup
	{
		readonly List<PhysicsBodyBuilder> _bodies = [];
		readonly List<Surface> _surfaces = [];

		readonly List<Vector3> _vertices = [];
		readonly Dictionary<Vector3, int> _welded = [];
		readonly List<uint> _indices = [];
		readonly List<byte> _triangleSurfaces = [];

		public void Add( CollisionShape shape )
		{
			var body = new PhysicsBodyBuilder { BindPose = Transform.Zero, Surface = shape.Surface };

			shape.AddTo( body );

			_bodies.Add( body );
		}

		public void Add( CollisionChunk chunk )
		{
			var perTriangle = chunk.TriangleSurfaces;
			var remap = new int[chunk.Positions.Length];

			for ( int i = 0; i < remap.Length; i++ )
			{
				var position = chunk.Positions[i];

				if ( !_welded.TryGetValue( position, out var index ) )
				{
					index = _vertices.Count;
					_vertices.Add( position );
					_welded.Add( position, index );
				}

				remap[i] = index;
			}

			for ( int i = 0; i + 2 < chunk.Indices.Length; i += 3 )
			{
				var a = remap[chunk.Indices[i]];
				var b = remap[chunk.Indices[i + 1]];
				var c = remap[chunk.Indices[i + 2]];

				// Welding can collapse a triangle that was only ever a seam
				if ( a == b || b == c || c == a )
					continue;

				var triangle = i / 3;
				var surface = SurfaceIndex( _surfaces, perTriangle is not null && triangle < perTriangle.Length
					? perTriangle[triangle] ?? chunk.Surface
					: chunk.Surface );

				_indices.Add( (uint)a );
				_indices.Add( (uint)b );
				_indices.Add( (uint)c );
				_triangleSurfaces.Add( surface );
			}
		}

		public byte[] Build()
		{
			if ( _indices.Count >= 3 )
			{
				_bodies.Add( new PhysicsBodyBuilder { BindPose = Transform.Zero }
					.AddMesh( CollectionsMarshal.AsSpan( _vertices ), CollectionsMarshal.AsSpan( _indices ), CollectionsMarshal.AsSpan( _triangleSurfaces ) ) );
			}

			return _bodies.Count == 0 ? null : VPhysWriter.Write( _bodies, _surfaces );
		}
	}

	/// <summary>
	/// Weld a model's collision into the world, keeping the surface each shape was built with unless
	/// the component overrides it. Triangle meshes join the welded soup, everything else stays convex.
	/// </summary>
	static void AddModelCollision( List<CollisionChunk> chunks, List<CollisionShape> shapes, Dictionary<Model, ModelCollisionPart[]> cache, Model model, in Transform world, Surface surface, string tags )
	{
		if ( !model.IsValid() )
			return;

		if ( !cache.TryGetValue( model, out var local ) )
		{
			local = ReadCollision( model );
			cache[model] = local;
		}

		foreach ( var part in local )
		{
			var transform = world.ToWorld( part.Transform );
			var mirrored = transform.Scale.x * transform.Scale.y * transform.Scale.z < 0.0f;

			foreach ( var chunk in part.Chunks )
			{
				var indices = mirrored ? Flipped( chunk.Indices ) : chunk.Indices;

				// A component surface overrides the whole model, per triangle assignments included
				chunks.Add( new CollisionChunk( Transformed( chunk.Positions, transform ), indices, surface ?? chunk.Surface, tags, surface is null ? chunk.TriangleSurfaces : null ) );
			}

			foreach ( var shape in part.Shapes )
			{
				shapes.Add( shape.Instance( transform, surface ?? shape.Surface, tags ) );
			}
		}
	}

	/// <summary>
	/// Keep geometry in physics-part space so instances compose their scale and rotation with
	/// the part transform before transforming vertices, just like ModelCollider.
	/// </summary>
	static ModelCollisionPart[] ReadCollision( Model model )
	{
		if ( model.Physics is not { } physics )
			return [];

		var parts = new List<ModelCollisionPart>();

		foreach ( var part in physics.Parts )
		{
			var chunks = new List<CollisionChunk>();
			var shapes = new List<CollisionShape>();

			foreach ( var shape in part.Parts )
			{
				switch ( shape )
				{
					case HullPart hull:
						var points = hull.GetPoints().ToArray();

						if ( points.Length >= 4 )
						{
							shapes.Add( new HullShape( points, hull.Surface, null ) );
						}
						break;

					case MeshPart mesh:
						chunks.Add( new CollisionChunk( mesh.GetVertices(), mesh.GetIndices(), mesh.Surface, null, mesh.GetTriangleSurfaces() ) );
						break;

					case SpherePart sphere:
						shapes.Add( new SphereShape( sphere.Sphere, sphere.Surface, null ) );
						break;

					case CapsulePart capsule:
						shapes.Add( new CapsuleShape( capsule.Capsule, capsule.Surface, null ) );
						break;
				}
			}

			if ( chunks.Count > 0 || shapes.Count > 0 )
				parts.Add( new ModelCollisionPart( part.Transform, [.. chunks], [.. shapes] ) );
		}

		return [.. parts];
	}

	/// <summary>
	/// A mirroring transform turns every triangle inside out, so put the winding back.
	/// </summary>
	static int[] Flipped( int[] indices )
	{
		var flipped = new int[indices.Length];

		for ( int i = 0; i + 2 < indices.Length; i += 3 )
		{
			flipped[i] = indices[i];
			flipped[i + 1] = indices[i + 2];
			flipped[i + 2] = indices[i + 1];
		}

		return flipped;
	}

	static Vector3[] Transformed( Vector3[] points, in Transform transform )
	{
		var world = new Vector3[points.Length];

		for ( int i = 0; i < world.Length; i++ )
		{
			world[i] = transform.PointToWorld( points[i] );
		}

		return world;
	}

	/// <summary>
	/// Where a surface sits in the shape's surface list, adding it if it's new.
	/// Reject overflow rather than silently assigning a different collision surface.
	/// </summary>
	static byte SurfaceIndex( List<Surface> surfaces, Surface surface )
	{
		var index = surfaces.IndexOf( surface );

		if ( index >= 0 )
			return (byte)index;

		if ( surfaces.Count > byte.MaxValue )
			throw new InvalidOperationException( "Compiled collision supports at most 256 distinct surfaces per mesh. Reduce the surface count or split the collision geometry before compiling." );

		surfaces.Add( surface );
		return (byte)(surfaces.Count - 1);
	}
}
