using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Sandbox;

namespace Editor;

partial class SceneCompiler
{
	/// <summary>
	/// One piece of triangulated geometry with source-scene bounds, all drawn with a single material.
	/// </summary>
	internal readonly record struct Chunk( CompiledVertex[] Vertices, int[] Indices, BBox Bounds, CompiledStreams Streams )
	{
		public Transform Transform { get; init; } = Transform.Zero;
		public BBox LocalBounds { get; init; } = Bounds;
	}

	/// <summary>
	/// Which optional vertex streams a piece of geometry needs. A stream a source never had is
	/// left out of the compiled mesh, the same way it was missing from the source.
	/// </summary>
	[Flags]
	internal enum CompiledStreams
	{
		None = 0,

		/// <summary>
		/// A second texture coordinate and a vertex colour, as models carry them. S_UV2 materials
		/// read the first, foliage and vertex colour shaders the second.
		/// </summary>
		Model = 1,

		/// <summary>
		/// The blend and tint streams the mesh tool paints, which only the vertex paint shaders read.
		/// </summary>
		Paint = 2,
	}

	/// <summary>
	/// A vertex on its way into an aggregate, wide enough for anything either source carries. What
	/// reaches the GPU is narrowed down to the streams actually in use.
	/// </summary>
	internal struct CompiledVertex
	{
		public Vector3 Position;
		public Vector3 Normal;
		public Vector4 Tangent;
		public Vector2 Texcoord;
		public Vector2 Texcoord1;
		public Color32 Color;
		public Color32 Blend;
		public Color32 Paint;
	}

	/// <summary>
	/// One piece of triangulated world space geometry to collide with. <paramref name="Surface"/>
	/// covers the whole chunk unless <paramref name="TriangleSurfaces"/> names one per triangle,
	/// which is how a model built with mixed collision materials keeps them.
	/// </summary>
	internal readonly record struct CollisionChunk( Vector3[] Positions, int[] Indices, Surface Surface, string Tags, Surface[] TriangleSurfaces = null );

	/// <summary>
	/// A convex shape to collide with, kept whole. Each one becomes a body of its own because a
	/// body is what carries a surface.
	/// </summary>
	internal abstract record CollisionShape( Surface Surface, string Tags )
	{
		public abstract void AddTo( PhysicsBodyBuilder body );

		/// <summary>
		/// The same shape placed in the world. A sphere or a capsule carries one radius, so the
		/// magnitude of the x scale is used - neither is a shape physics can scale unevenly.
		/// </summary>
		public abstract CollisionShape Instance( in Transform world, Surface surface, string tags );
	}

	internal sealed record HullShape( Vector3[] Points, Surface Surface, string Tags ) : CollisionShape( Surface, Tags )
	{
		public override void AddTo( PhysicsBodyBuilder body ) => body.AddHull( Points );

		public override CollisionShape Instance( in Transform world, Surface surface, string tags )
			=> new HullShape( Transformed( Points, world ), surface, tags );
	}

	internal sealed record SphereShape( Sphere Sphere, Surface Surface, string Tags ) : CollisionShape( Surface, Tags )
	{
		public override void AddTo( PhysicsBodyBuilder body ) => body.AddSphere( Sphere );

		public override CollisionShape Instance( in Transform world, Surface surface, string tags )
			=> new SphereShape( new Sphere( world.PointToWorld( Sphere.Center ), Sphere.Radius * MathF.Abs( world.Scale.x ) ), surface, tags );
	}

	internal sealed record CapsuleShape( Capsule Capsule, Surface Surface, string Tags ) : CollisionShape( Surface, Tags )
	{
		public override void AddTo( PhysicsBodyBuilder body ) => body.AddCapsule( Capsule );

		public override CollisionShape Instance( in Transform world, Surface surface, string tags )
			=> new CapsuleShape( new Capsule( world.PointToWorld( Capsule.CenterA ), world.PointToWorld( Capsule.CenterB ), Capsule.Radius * MathF.Abs( world.Scale.x ) ), surface, tags );
	}

	/// <summary>
	/// One aggregate's worth of geometry, grouped and split but not yet turned into a model.
	/// </summary>
	internal sealed record AggregatePlan( Material Material, Color Tint, string Tags, Transform Transform, Chunk[] Chunks )
	{
		public bool Translucent { get; } = IsTranslucent( Material );
	}

	/// <summary>
	/// Everything the compile pulled out of the scene, ready to be turned into resources.
	/// </summary>
	internal sealed record CompilePlan( AggregatePlan[] Aggregates, CollisionChunk[] Collision, CollisionShape[] Shapes );

	/// <summary>
	/// What decides which aggregate a chunk lands in. Tint is part of the key because it is applied
	/// to the whole compiled object, the same way the map compiler refuses to merge meshes whose tints
	/// differ. Tags come along because the compiled object has to carry them.
	/// </summary>
	internal readonly record struct GroupKey( Material Material, Color Tint, string Tags, Transform Transform );

	/// <summary>
	/// A world transform split into what each vertex stream needs. Normals go through the inverse
	/// transpose so non uniform scale doesn't shear them off the surface, tangents through the
	/// plain linear part, and a mirroring scale flips the tangent sign.
	/// </summary>
	internal readonly struct GeometryTransform
	{
		readonly Transform _transform;
		readonly Vector3 _normalScale;

		public readonly bool Mirrored;

		public GeometryTransform( in Transform transform )
		{
			_transform = transform;
			var scale = transform.Scale;
			_normalScale = new Vector3( Reciprocal( scale.x ), Reciprocal( scale.y ), Reciprocal( scale.z ) );

			Mirrored = scale.x * scale.y * scale.z < 0.0f;

			static float Reciprocal( float value ) => value == 0.0f ? 0.0f : 1.0f / value;
		}

		public Vector3 Position( in Vector3 position ) => _transform.PointToWorld( position );

		public Vector3 Normal( in Vector3 normal ) => (_transform.Rotation * (normal * _normalScale)).Normal;

		public Vector4 Tangent( in Vector4 tangent )
		{
			var world = (_transform.Rotation * (new Vector3( tangent.x, tangent.y, tangent.z ) * _transform.Scale)).Normal;
			return new Vector4( world, Mirrored ? -tangent.w : tangent.w );
		}

		/// <summary>
		/// Move locally triangulated mesh tool vertices into world space, keeping the streams the
		/// mesh painted.
		/// </summary>
		public CompiledVertex[] Apply( PolygonMesh.MeshVertex[] vertices, out CompiledStreams streams )
		{
			var compiled = new CompiledVertex[vertices.Length];
			var painted = false;

			for ( int i = 0; i < vertices.Length; i++ )
			{
				ref readonly var vertex = ref vertices[i];

				compiled[i] = new CompiledVertex
				{
					Position = Position( vertex.Position ),
					Normal = Normal( vertex.Normal ),
					Tangent = Tangent( vertex.Tangent ),
					Texcoord = vertex.Texcoord,
					Color = Color32.White,
					Blend = vertex.Blend,
					Paint = vertex.Color,
				};

				painted |= vertex.Blend != default || vertex.Color != default;
			}

			streams = painted ? CompiledStreams.Paint : CompiledStreams.None;

			return compiled;
		}

		/// <summary>
		/// Just the positions, in world space. All a mesh that doesn't draw has to give us.
		/// </summary>
		public Vector3[] Positions( PolygonMesh.MeshVertex[] vertices )
		{
			var positions = new Vector3[vertices.Length];

			for ( int i = 0; i < positions.Length; i++ )
			{
				positions[i] = Position( vertices[i].Position );
			}

			return positions;
		}
	}

	/// <summary>
	/// Triangulate every mesh and static prop into world space and work out which aggregate each
	/// piece belongs in. Awaits <paramref name="onProgress"/> as it goes so the editor stays alive.
	/// </summary>
	static async Task<CompilePlan> Plan( MeshComponent[] meshes, ModelRenderer[] props, HashSet<Guid> processed, SceneCompilerSettings settings, Func<int, int, Task> onProgress, CancellationToken cancel )
	{
		var groups = new Dictionary<GroupKey, List<Chunk>>();
		var collision = new List<CollisionChunk>();
		var shapes = new List<CollisionShape>();
		var physics = new Dictionary<Model, ModelCollisionPart[]>();
		var total = meshes.Length + props.Length;
		var done = 0;

		foreach ( var source in meshes )
		{
			cancel.ThrowIfCancellationRequested();

			source.Mesh.SetSmoothingAngle( source.SmoothingAngle );
			var submeshes = source.Mesh.Triangulate();
			if ( submeshes.Count == 0 )
			{
				await onProgress( ++done, total );
				continue;
			}

			var transform = new GeometryTransform( source.WorldTransform );
			var tags = TagKey( source.GameObject );
			var visible = !source.HideInGame;

			if ( source.Collision == MeshComponent.CollisionType.Hull )
			{
				// The live model omits editor-hidden faces. Use the same complete snapshot as
				// rendering. Material surfaces belong to triangles, not the mesh's single hull.
				var points = new List<Vector3>();
				foreach ( var submesh in submeshes )
					points.AddRange( transform.Positions( submesh.Vertices ) );

				shapes.Add( new HullShape( [.. points], source.Surface, tags ) );
			}

			var collides = source.Collision == MeshComponent.CollisionType.Mesh;

			foreach ( var submesh in submeshes )
			{
				// A mesh that doesn't draw never builds render vertices - it's compiled for its
				// collision alone, which is how you'd build something like a player clip.
				if ( visible )
				{
					var preserveOrigin = PropOrigin( submesh.Material );
					var vertexTransform = preserveOrigin ? new GeometryTransform( Transform.Zero ) : transform;
					var vertices = vertexTransform.Apply( submesh.Vertices, out var streams );

					Add( groups, submesh.Material, source.Color, tags, vertices, submesh.Indices, streams,
						preserveOrigin ? source.WorldTransform : Transform.Zero,
						preserveOrigin ? source.Mesh.CalculateBounds() : Bounds( vertices ) );
				}

				if ( collides )
				{
					var indices = transform.Mirrored ? Flipped( submesh.Indices ) : submesh.Indices;
					collision.Add( new CollisionChunk( transform.Positions( submesh.Vertices ), indices, source.Surface ?? submesh.Material?.Surface, tags ) );
				}
			}

			processed.Add( source.Id );

			await onProgress( ++done, total );
		}

		var cache = new Dictionary<(Model Model, ulong BodyGroups, int Lod), List<PropMesh>>();

		foreach ( var renderer in props )
		{
			cancel.ThrowIfCancellationRequested();

			if ( TryCompileProp( renderer, cache, groups ) )
			{
				var go = renderer.GameObject;

				processed.Add( renderer.Id );

				// The prop would only build itself another renderer, so it goes too.
				if ( go.Components.Get<Prop>( FindMode.EverythingInSelf ) is { } owner )
				{
					processed.Add( owner.Id );
				}

				if ( Collider( go ) is { } collider )
				{
					AddModelCollision( collision, shapes, physics, collider.Model, collider.WorldTransform, collider.Surface, TagKey( go ) );
					processed.Add( collider.Id );
				}
			}

			await onProgress( ++done, total );
		}

		var plans = new List<AggregatePlan>( groups.Count );
		total += groups.Count;

		async Task StepGeometry()
		{
			cancel.ThrowIfCancellationRequested();
			await onProgress( done, total );
			cancel.ThrowIfCancellationRequested();
		}

		foreach ( var (key, sources) in groups )
		{
			var chunks = new List<Chunk>();

			for ( int i = 0; i < sources.Count; i++ )
			{
				if ( PropOrigin( key.Material ) )
				{
					await StepGeometry();
					chunks.Add( sources[i] );
				}
				else
				{
					await Split( chunks, sources[i], settings.MaxChunkSize, StepGeometry );
				}
				sources[i] = default;
			}

			foreach ( var cluster in await Cluster( [.. chunks], MaxFragments( key.Material ), settings.AggregateCost, StepGeometry ) )
			{
				plans.Add( new AggregatePlan( key.Material, key.Tint, key.Tags, key.Transform, cluster ) );
			}

			await onProgress( ++done, total );
		}

		return new CompilePlan( [.. plans], [.. collision], [.. shapes] );
	}

	static void Add( Dictionary<GroupKey, List<Chunk>> groups, Material material, Color tint, string tags, CompiledVertex[] vertices, int[] indices, CompiledStreams streams, Transform transform, BBox bounds )
	{
		var key = new GroupKey( material, tint, tags, IsTranslucent( material ) ? transform : Transform.Zero );

		groups.GetOrCreate( key ).Add( new Chunk( vertices, indices, bounds.Transform( transform ), streams ) { Transform = transform, LocalBounds = bounds } );
	}

	/// <summary>
	/// Split whole triangles into cullable chunks, bounded by the renderer's fragment capacity.
	/// </summary>
	static async Task Split( List<Chunk> chunks, Chunk source, float maxChunkSize, Func<Task> step )
	{
		await step();
		var (vertices, indices, whole, streams) = source;

		if ( Longest( whole ) <= maxChunkSize )
		{
			chunks.Add( source );
			return;
		}

		var count = indices.Length / 3;
		var triangles = new int[count];
		var centers = new Vector3[count];
		var keys = new float[count];

		for ( int i = 0; i < count; i++ )
		{
			triangles[i] = i;
			centers[i] = Center( vertices, indices, i );
		}

		// Vertices are shared between pieces, so each one gets its own compacted copy - this maps
		// an index of the whole mesh to the piece being compacted, and is cleared again after.
		int[] map = null;

		var budget = SceneAggregateObject.MaxFragments;
		var pending = new Stack<(Range Range, int Budget)>();
		pending.Push( (Range.All, budget) );
		var oversized = 0;

		while ( pending.TryPop( out var node ) )
		{
			await step();
			var range = node.Range;
			var (offset, length) = range.GetOffsetAndLength( count );
			var span = triangles.AsSpan( range );
			var bounds = Bounds( vertices, indices, span );

			if ( length < 2 || node.Budget == 1 || Longest( bounds ) <= maxChunkSize )
			{
				AddLeaf();
				continue;
			}

			var axis = LongestAxis( bounds );

			for ( int i = 0; i < length; i++ )
			{
				keys[i] = Axis( centers[span[i]], axis );
			}

			keys.AsSpan( 0, length ).Sort( span );

			var at = length / 2;
			var left = Bounds( vertices, indices, span[..at] );
			var right = Bounds( vertices, indices, span[at..] );

			// Only split when the smaller bounds reduce expected triangle work.
			if ( Area( left ) * at + Area( right ) * (length - at) >= Area( bounds ) * length )
			{
				AddLeaf();
				continue;
			}

			var leftBudget = Math.Clamp( (int)((long)node.Budget * at / length), 1, node.Budget - 1 );
			pending.Push( ((offset + at)..(offset + length), node.Budget - leftBudget) );
			pending.Push( (offset..(offset + at), leftBudget) );

			void AddLeaf()
			{
				if ( Longest( bounds ) > maxChunkSize )
					oversized++;

				if ( length == count )
				{
					chunks.Add( source );
					return;
				}

				if ( map is null )
				{
					map = new int[vertices.Length];
					Array.Fill( map, -1 );
				}

				chunks.Add( Compact( vertices, indices, triangles.AsSpan( range ), map, bounds, streams ) );
			}
		}

		if ( oversized > 0 )
			Log.Warning( $"Compile Scene: kept {oversized} chunks larger than MaxChunkSize {maxChunkSize} at {whole} to avoid excessive splitting." );
	}

	/// <summary>
	/// Pull one piece of a split mesh out into a chunk of its own, keeping only the vertices its
	/// triangles use. Leaves <paramref name="map"/> cleared for the next piece.
	/// </summary>
	static Chunk Compact( CompiledVertex[] vertices, int[] indices, ReadOnlySpan<int> triangles, int[] map, BBox bounds, CompiledStreams streams )
	{
		var local = new int[triangles.Length * 3];
		var used = new CompiledVertex[Math.Min( vertices.Length, local.Length )];
		var count = 0;

		for ( int i = 0; i < triangles.Length; i++ )
		{
			for ( int j = 0; j < 3; j++ )
			{
				var index = indices[triangles[i] * 3 + j];

				if ( map[index] < 0 )
				{
					map[index] = count;
					used[count++] = vertices[index];
				}

				local[i * 3 + j] = map[index];
			}
		}

		for ( int i = 0; i < triangles.Length; i++ )
		{
			for ( int j = 0; j < 3; j++ )
			{
				map[indices[triangles[i] * 3 + j]] = -1;
			}
		}

		return new Chunk( count == used.Length ? used : used[..count], local, bounds, streams );
	}

	static Vector3 Center( CompiledVertex[] vertices, int[] indices, int triangle )
	{
		var at = triangle * 3;

		return (vertices[indices[at]].Position + vertices[indices[at + 1]].Position + vertices[indices[at + 2]].Position) / 3.0f;
	}

	static bool IsTranslucent( Material material ) => material.IsValid() && material.Flags.IsTranslucent;

	/// <summary>
	/// How many fragments an aggregate of this material may hold. Aggregates are an opaque path, so
	/// translucent geometry becomes a model instead - those sort as one object, keep them small.
	/// </summary>
	static int MaxFragments( Material material )
	{
		return IsTranslucent( material ) ? 8 : SceneAggregateObject.MaxFragments;
	}

	/// <summary>
	/// Split chunks into the aggregates that draw them, subdividing while the culling that buys is
	/// worth more than the extra draw, and always far enough to fit the fragment limit.
	/// </summary>
	static async Task<List<Chunk[]>> Cluster( Chunk[] chunks, int maxFragments, float aggregateCost, Func<Task> step )
	{
		var aggregates = new List<Chunk[]>();
		var pending = new Stack<(Range Range, int Depth)>();

		// Sorting by a key span beats a comparison delegate, and the same buffer then carries the
		// split's running areas - neither outlives the node being worked on.
		var keys = new float[chunks.Length];
		var suffix = new float[chunks.Length];

		// Fall back to balanced splits if the tree becomes too deep.
		var depthLimit = 2 * System.Numerics.BitOperations.Log2( (uint)chunks.Length );
		pending.Push( (Range.All, 0) );

		while ( pending.TryPop( out var node ) )
		{
			await step();
			var range = node.Range;
			var (offset, length) = range.GetOffsetAndLength( chunks.Length );
			var span = chunks.AsSpan( range );
			var bounds = Bounds( span );
			var axis = LongestAxis( bounds );

			for ( int i = 0; i < length; i++ )
			{
				keys[i] = Axis( span[i].Bounds.Center, axis );
			}

			keys.AsSpan( 0, length ).Sort( span );

			var at = FindSplit( span, bounds, maxFragments, aggregateCost, suffix.AsSpan( 0, length ) );

			if ( at == 0 )
			{
				aggregates.Add( span.ToArray() );
				continue;
			}

			if ( node.Depth >= depthLimit )
				at = length / 2;

			pending.Push( ((offset + at)..(offset + length), node.Depth + 1) );
			pending.Push( (offset..(offset + at), node.Depth + 1) );
		}

		return aggregates;
	}

	/// <summary>
	/// Find where along the sorted chunks to split, or 0 to leave them as one aggregate. Cost is
	/// the surface area heuristic: how likely a group is to be onscreen, times what it costs when
	/// it is. Equal-cost splits prefer balanced children; no-split still wins ties when legal.
	/// Falls back to halving if nothing wins but we're still over the fragment limit.
	/// </summary>
	static int FindSplit( ReadOnlySpan<Chunk> chunks, BBox bounds, int maxFragments, float aggregateCost, Span<float> suffix )
	{
		if ( chunks.Length < 2 )
			return 0;

		var box = chunks[^1].Bounds;

		for ( int i = chunks.Length - 1; i > 0; i-- )
		{
			box = box.AddBBox( chunks[i].Bounds );
			suffix[i] = Area( box ) * (chunks.Length - i);
		}

		var area = Area( bounds );
		var best = 0;
		var bestCost = chunks.Length <= maxFragments ? area * chunks.Length : float.MaxValue;
		var parentCost = area * aggregateCost;

		box = chunks[0].Bounds;

		for ( int i = 1; i < chunks.Length; i++ )
		{
			var cost = parentCost + Area( box ) * i + suffix[i];

			if ( cost < bestCost || (best != 0 && cost == bestCost && Math.Abs( chunks.Length - 2L * i ) < Math.Abs( chunks.Length - 2L * best )) )
			{
				bestCost = cost;
				best = i;
			}

			box = box.AddBBox( chunks[i].Bounds );
		}

		return best == 0 && chunks.Length > maxFragments ? chunks.Length / 2 : best;
	}

	static BBox Bounds( ReadOnlySpan<Chunk> chunks )
	{
		var bounds = chunks[0].Bounds;

		for ( int i = 1; i < chunks.Length; i++ )
		{
			bounds = bounds.AddBBox( chunks[i].Bounds );
		}

		return bounds;
	}

	static BBox Bounds( ReadOnlySpan<CompiledVertex> vertices )
	{
		var min = vertices[0].Position;
		var max = min;

		for ( int i = 1; i < vertices.Length; i++ )
		{
			min = Vector3.Min( min, vertices[i].Position );
			max = Vector3.Max( max, vertices[i].Position );
		}

		return new BBox( min, max );
	}

	static BBox Bounds( CompiledVertex[] vertices, int[] indices, ReadOnlySpan<int> triangles )
	{
		var min = vertices[indices[triangles[0] * 3]].Position;
		var max = min;

		for ( int i = 0; i < triangles.Length; i++ )
		{
			for ( int j = 0; j < 3; j++ )
			{
				var position = vertices[indices[triangles[i] * 3 + j]].Position;

				min = Vector3.Min( min, position );
				max = Vector3.Max( max, position );
			}
		}

		return new BBox( min, max );
	}

	static float Area( BBox bounds )
	{
		var size = bounds.Size;
		return size.x * size.y + size.y * size.z + size.z * size.x;
	}

	static int LongestAxis( BBox bounds )
	{
		var size = bounds.Size;
		if ( size.x >= size.y && size.x >= size.z ) return 0;
		return size.y >= size.z ? 1 : 2;
	}

	static float Longest( BBox bounds )
	{
		var size = bounds.Size;
		return MathF.Max( size.x, MathF.Max( size.y, size.z ) );
	}

	static float Axis( Vector3 point, int axis ) => axis switch { 0 => point.x, 1 => point.y, _ => point.z };

	/// <summary>
	/// Concatenate a plan's chunks into the single mesh an aggregate draws from, giving each chunk
	/// its own draw call so it can be culled and drawn as a fragment.
	/// </summary>
	static (Model Model, List<AggregateFragmentInfo> Fragments) Build( AggregatePlan plan, string resourcePath, SceneCompileStatistics statistics )
	{
		var material = plan.Material;
		var chunks = plan.Chunks;

		var vertexTotal = 0;
		var indexTotal = 0;
		var streams = CompiledStreams.None;

		foreach ( var chunk in chunks )
		{
			vertexTotal += chunk.Vertices.Length;
			indexTotal += chunk.Indices.Length;
			streams |= chunk.Streams;
			statistics.VertexCount += chunk.Vertices.Length;
			statistics.TriangleCount += chunk.Indices.Length / 3;
		}

		var indices = new int[indexTotal];
		var fragments = new List<AggregateFragmentInfo>( chunks.Length );
		var bounds = chunks[0].Bounds;
		var localBounds = chunks[0].LocalBounds;

		var indexCount = 0;

		for ( int i = 0; i < chunks.Length; i++ )
		{
			chunks[i].Indices.CopyTo( indices, indexCount );
			indexCount += chunks[i].Indices.Length;

			fragments.Add( new AggregateFragmentInfo( chunks[i].LocalBounds ) { LocalTransform = chunks[i].Transform } );
			bounds = bounds.AddBBox( chunks[i].Bounds );
			localBounds = localBounds.AddBBox( chunks[i].LocalBounds );
		}

		var mesh = new Mesh( material );

		// A stream nothing needs is left out - the renderer feeds a missing semantic the same
		// default the source had, and the vertex drops from 68 bytes to as few as 48.
		switch ( streams )
		{
			case CompiledStreams.None:
				mesh.CreateVertexBuffer( vertexTotal, Pack( chunks, vertexTotal, static ( in CompiledVertex v ) => new PlainVertex
				{
					Position = v.Position,
					Normal = v.Normal,
					Tangent = v.Tangent,
					Texcoord = v.Texcoord,
				} ) );
				break;

			case CompiledStreams.Model:
				mesh.CreateVertexBuffer( vertexTotal, Pack( chunks, vertexTotal, static ( in CompiledVertex v ) => new ModelVertex
				{
					Position = v.Position,
					Normal = v.Normal,
					Tangent = v.Tangent,
					Texcoord = v.Texcoord,
					Texcoord1 = v.Texcoord1,
					Color = v.Color,
				} ) );
				break;

			case CompiledStreams.Paint:
				mesh.CreateVertexBuffer( vertexTotal, Pack( chunks, vertexTotal, static ( in CompiledVertex v ) => new PaintedVertex
				{
					Position = v.Position,
					Normal = v.Normal,
					Tangent = v.Tangent,
					Texcoord = v.Texcoord,
					Blend = v.Blend,
					Paint = v.Paint,
				} ) );
				break;

			default:
				mesh.CreateVertexBuffer( vertexTotal, Pack( chunks, vertexTotal, static ( in CompiledVertex v ) => new FullVertex
				{
					Position = v.Position,
					Normal = v.Normal,
					Tangent = v.Tangent,
					Texcoord = v.Texcoord,
					Texcoord1 = v.Texcoord1,
					Color = v.Color,
					Blend = v.Blend,
					Paint = v.Paint,
				} ) );
				break;
		}

		mesh.CreateIndexBuffer( indices.Length, indices );
		mesh.Bounds = plan.Translucent ? localBounds : bounds;

		// A mesh comes with one draw call on it, which the first chunk claims. Chunk indices are
		// relative to the chunk's own start vertex, which the draw adds back on as the base vertex.
		mesh.SetVertexRange( 0, chunks[0].Vertices.Length );
		mesh.SetIndexRange( 0, chunks[0].Indices.Length );

		var vertexCount = chunks[0].Vertices.Length;
		indexCount = chunks[0].Indices.Length;

		for ( int i = 1; i < chunks.Length; i++ )
		{
			mesh.AddSubMesh( material, indexCount, chunks[i].Indices.Length, vertexCount, chunks[i].Vertices.Length );

			vertexCount += chunks[i].Vertices.Length;
			indexCount += chunks[i].Indices.Length;
		}

		// After the sub meshes, because this writes every draw call the mesh has.
		mesh.UvDensity = UvDensity( chunks, indexTotal );

		if ( !plan.Translucent )
			statistics.FragmentCount += fragments.Count;

		return (Model.Builder.WithName( resourcePath ).AddMesh( mesh ).Create(), fragments);
	}

	/// <summary>
	/// Texture streaming sizes its requests from the draw call's UV density, so a mesh that leaves it
	/// at zero asks for nothing and sits on the lowest mip until something else asks for the texture.
	/// Same 20th percentile of world-to-UV scale the model compiler uses.
	/// </summary>
	static float UvDensity( Chunk[] chunks, int indexTotal )
	{
		var densities = new List<float>( indexTotal / 3 );

		foreach ( var chunk in chunks )
		{
			var vertices = chunk.Vertices;
			var indices = chunk.Indices;

			for ( int i = 0; i + 2 < indices.Length; i += 3 )
			{
				ref readonly var a = ref vertices[indices[i]];
				ref readonly var b = ref vertices[indices[i + 1]];
				ref readonly var c = ref vertices[indices[i + 2]];

				AddDensity( densities, chunk.Transform.PointToWorld( a.Position ), chunk.Transform.PointToWorld( b.Position ),
					chunk.Transform.PointToWorld( c.Position ), a.Texcoord, b.Texcoord, c.Texcoord );
			}
		}

		return UvDensity( densities );
	}

	/// <inheritdoc cref="UvDensity(Chunk[], int)"/>
	static float UvDensity( ReadOnlySpan<PolygonMesh.MeshVertex> vertices, ReadOnlySpan<int> indices )
	{
		var densities = new List<float>( indices.Length / 3 );

		for ( int i = 0; i + 2 < indices.Length; i += 3 )
		{
			ref readonly var a = ref vertices[indices[i]];
			ref readonly var b = ref vertices[indices[i + 1]];
			ref readonly var c = ref vertices[indices[i + 2]];

			AddDensity( densities, a.Position, b.Position, c.Position, a.Texcoord, b.Texcoord, c.Texcoord );
		}

		return UvDensity( densities );
	}

	static void AddDensity( List<float> densities, in Vector3 pa, in Vector3 pb, in Vector3 pc, in Vector2 ta, in Vector2 tb, in Vector2 tc )
	{
		var area = Vector3.Cross( pb - pa, pc - pa ).Length * 0.5f;
		if ( area <= 0.0f ) return;

		var uv = 0.5f * MathF.Abs( (tb.x - ta.x) * (tc.y - ta.y) - (tc.x - ta.x) * (tb.y - ta.y) );
		if ( uv <= 0.0f ) return;

		densities.Add( MathF.Sqrt( area / uv ) );
	}

	static float UvDensity( List<float> densities )
	{
		if ( densities.Count == 0 ) return 0.0f;

		densities.Sort();
		return densities[2 * (densities.Count - 1) / 10];
	}

	/// <summary>
	/// The compiled vertex when nothing needs more than the standard shaders read.
	/// </summary>
	[StructLayout( LayoutKind.Sequential )]
	struct PlainVertex
	{
		[VertexLayout.Position] public Vector3 Position;
		[VertexLayout.Normal] public Vector3 Normal;
		[VertexLayout.Tangent] public Vector4 Tangent;
		[VertexLayout.TexCoord] public Vector2 Texcoord;
	}

	/// <summary>
	/// A compiled vertex carrying what a model brings with it - a second texture coordinate for
	/// S_UV2 materials, and a colour for the foliage and vertex colour shaders.
	/// </summary>
	[StructLayout( LayoutKind.Sequential )]
	struct ModelVertex
	{
		[VertexLayout.Position] public Vector3 Position;
		[VertexLayout.Normal] public Vector3 Normal;
		[VertexLayout.Tangent] public Vector4 Tangent;
		[VertexLayout.TexCoord] public Vector2 Texcoord;
		[VertexLayout.TexCoord( 1 )] public Vector2 Texcoord1;
		[VertexLayout.Color] public Color32 Color;
	}

	/// <summary>
	/// A compiled vertex carrying what the mesh tool paints.
	/// </summary>
	[StructLayout( LayoutKind.Sequential )]
	struct PaintedVertex
	{
		[VertexLayout.Position] public Vector3 Position;
		[VertexLayout.Normal] public Vector3 Normal;
		[VertexLayout.Tangent] public Vector4 Tangent;
		[VertexLayout.TexCoord] public Vector2 Texcoord;
		[VertexLayout.TexCoord( 4 )] public Color32 Blend;
		[VertexLayout.TexCoord( 5 )] public Color32 Paint;
	}

	/// <summary>
	/// A compiled vertex for an aggregate that painted meshes and models both landed in.
	/// </summary>
	[StructLayout( LayoutKind.Sequential )]
	struct FullVertex
	{
		[VertexLayout.Position] public Vector3 Position;
		[VertexLayout.Normal] public Vector3 Normal;
		[VertexLayout.Tangent] public Vector4 Tangent;
		[VertexLayout.TexCoord] public Vector2 Texcoord;
		[VertexLayout.TexCoord( 1 )] public Vector2 Texcoord1;
		[VertexLayout.Color] public Color32 Color;
		[VertexLayout.TexCoord( 4 )] public Color32 Blend;
		[VertexLayout.TexCoord( 5 )] public Color32 Paint;
	}

	/// <summary>
	/// Lay a plan's chunks end to end as the vertices the GPU will read.
	/// </summary>
	static T[] Pack<T>( Chunk[] chunks, int total, PackVertex<T> pack ) where T : unmanaged
	{
		var vertices = new T[total];
		var at = 0;

		foreach ( var chunk in chunks )
		{
			foreach ( ref readonly var vertex in chunk.Vertices.AsSpan() )
			{
				vertices[at++] = pack( vertex );
			}
		}

		return vertices;
	}

	delegate T PackVertex<out T>( in CompiledVertex vertex );
}
