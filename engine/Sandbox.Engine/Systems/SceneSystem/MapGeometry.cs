using Borealis;
using Borealis.Backend;
using Borealis.Source2.Models;
using Borealis.Source2.World;
using BorealisMesh = Borealis.RenderMesh;
using NumericsMatrix = System.Numerics.Matrix4x4;

namespace Sandbox;

/// <summary>
/// A map's world geometry as Borealis draws it, read from its compiled world (<c>world.vwrld</c> and its nodes) by
/// Borealis.Source2: no native scene objects. Each world node's scene objects become <see cref="MeshObject"/>s and its
/// aggregates <see cref="AggregateObject"/>s, set up as native's world renderer sets up its objects (<c>CWorldNode</c>):
/// flags from the object type, mesh groups and material group, tint, LOD override, light group, lighting origin, light
/// probe handshake, overlay render order, the world, map and solid tags, and the map's lightmaps. Every render world a
/// scene world with the map is drawn in gets its own objects (<see cref="Instance"/>), sharing the models.
/// </summary>
internal sealed class MapGeometry
{
	/// <summary>The compiled world.</summary>
	public WorldFile World { get; }

	/// <summary>The map's folder, <c>maps/construct_25</c>: where its lightmaps are.</summary>
	public string Folder { get; }

	/// <summary>Where the map is placed: added to every object's position.</summary>
	public Vector3 Origin
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			Version++;
			trace = null;
		}
	}

	/// <summary>Changes whenever the objects need making again (<see cref="Origin"/>).</summary>
	public int Version { get; private set; }

	public MapGeometry( WorldFile world, string folder, Vector3 origin )
	{
		World = world;
		Folder = folder;
		Origin = origin;
		Version = 0;
	}

	/// <summary>Bounds of every node, placed.</summary>
	public BBox Bounds
	{
		get
		{
			var (mins, maxs) = World.Bounds;
			return new BBox( new Vector3( mins.X, mins.Y, mins.Z ) + Origin, new Vector3( maxs.X, maxs.Y, maxs.Z ) + Origin );
		}
	}

	MapTrace trace;

	/// <summary>The map's render meshes for traces, built the first time something traces them.</summary>
	public MapTrace Trace( BackendDevice gpu ) => trace ??= new MapTrace( this, gpu );

	// The tags native's world node gives every object (CWorldNode::ApplyTags)
	static uint[] worldTags;
	internal static uint[] WorldTags => worldTags ??= [StringToken.FindOrCreate( "world" ), StringToken.FindOrCreate( "map" ), StringToken.FindOrCreate( "solid" )];

	/// <summary>
	/// An object's placement: its compiled object-to-world matrix (row vectors, translation in the last row), moved to the
	/// map's origin.
	/// </summary>
	internal static Transform ToTransform( NumericsMatrix m, Vector3 origin )
	{
		var x = new Vector3( m.M11, m.M12, m.M13 );
		var y = new Vector3( m.M21, m.M22, m.M23 );
		var z = new Vector3( m.M31, m.M32, m.M33 );
		var scale = new Vector3( x.Length, y.Length, z.Length );
		if ( Vector3.Dot( Vector3.Cross( x, y ), z ) < 0 ) scale = scale.WithX( -scale.x );
		var rotation = Rotation.LookAt( x / scale.x, z / scale.z );
		return new Transform( new Vector3( m.M41, m.M42, m.M43 ) + origin, rotation, scale );
	}

	/// <summary>The map's objects in one render world.</summary>
	public Instance Instantiate( BackendDevice gpu, RenderWorld world, int subWorld ) => new( this, gpu, world, subWorld );

	/// <summary>
	/// A map's objects in one render world. Objects whose models are still loading are made once they have loaded
	/// (<see cref="Update"/>, once a frame); <see cref="Dispose"/> takes them out and lets go of the models, materials and
	/// lightmaps they held.
	/// </summary>
	internal sealed class Instance : IDisposable
	{
		readonly MapGeometry map;
		readonly BackendDevice gpu;
		readonly RenderWorld world;
		readonly int subWorld;
		int version;

		// What's still to make, by model: (node, object) for scene objects, (node, -1 - aggregate) for aggregates
		readonly Dictionary<string, List<(int Node, int Item)>> waiting = new( StringComparer.OrdinalIgnoreCase );

		readonly List<RenderObject> made = new();

		// Each model's mesh (every mesh group), held while this instance lives, and its variants (mesh groups, material group)
		readonly Dictionary<string, BorealisMesh> meshes = new( StringComparer.OrdinalIgnoreCase );
		readonly Dictionary<(string Path, ulong Groups, int Skin), BorealisMesh> variants = new();
		readonly List<string> heldMaterials = new();
		bool lightmapsHeld;

		/// <summary>Objects made so far.</summary>
		public int Objects => made.Count;

		/// <summary>Objects still waiting for their models.</summary>
		public int Waiting => waiting.Sum( x => x.Value.Count );

		public Instance( MapGeometry map, BackendDevice gpu, RenderWorld world, int subWorld )
		{
			this.map = map;
			this.gpu = gpu;
			this.world = world;
			this.subWorld = subWorld;
			Queue();
		}

		void Queue()
		{
			version = map.Version;
			var nodes = map.World.Nodes;
			for ( int n = 0; n < nodes.Count; n++ )
			{
				var node = nodes[n];
				for ( int i = 0; i < node.SceneObjects.Count; i++ ) Wait( node.SceneObjects[i].Model, n, i );
				for ( int i = 0; i < node.Aggregates.Count; i++ ) Wait( node.Aggregates[i].Model, n, -1 - i );
			}
		}

		void Wait( string model, int node, int item )
		{
			if ( string.IsNullOrEmpty( model ) ) return;
			if ( !waiting.TryGetValue( model, out var list ) ) waiting[model] = list = new();
			list.Add( (node, item) );
		}

		/// <summary>Make what's waiting whose model has loaded; make everything again when the map has moved.</summary>
		public void Update()
		{
			if ( version != map.Version )
			{
				Clear();
				Queue();
			}

			if ( waiting.Count == 0 ) return;

			List<string> done = null;
			foreach ( var (path, items) in waiting )
			{
				var mesh = MeshFor( path, out var pending );
				if ( pending ) continue;

				// Read on a worker, as the mesh was (already, unless models load at once)
				var file = mesh is null ? null : gpu.ReadModel( path );
				if ( file is { IsCompleted: false } ) continue;

				(done ??= new()).Add( path );
				if ( file is not { IsCompletedSuccessfully: true, Result: { } model } ) continue;

				foreach ( var (node, item) in items )
				{
					var source = map.World.Nodes[node];
					if ( item >= 0 ) Add( MakeObject( source.SceneObjects[item], path, mesh, model ) );
					else MakeAggregates( source.Aggregates[-1 - item], mesh, model );
				}
			}

			if ( done is null ) return;
			foreach ( var path in done ) waiting.Remove( path );
		}

		void Add( RenderObject obj )
		{
			if ( obj is null ) return;

			obj.SubWorld = subWorld;
			obj.Tags = WorldTags;
			world.Add( obj );
			made.Add( obj );
		}

		BorealisMesh MeshFor( string path, out bool pending )
		{
			pending = false;
			if ( meshes.TryGetValue( path, out var mesh ) ) return mesh;

			mesh = gpu.AsyncModels ? gpu.LoadModelAsync( path, ulong.MaxValue, out pending, pin: false ) : gpu.LoadModel( path, ulong.MaxValue );
			if ( pending ) return null;

			meshes[path] = mesh;
			gpu.HoldModel( mesh );
			return mesh;
		}

		/// <summary>
		/// A scene object as <c>CWorldNode::CreateSceneObjects</c> makes it. One that isn't a model (the map's own
		/// geometry) takes no tint, material group or mesh groups of its own (<c>CreateSceneObjects</c>, not
		/// <c>SetSingleObjectParams</c>).
		/// </summary>
		MeshObject MakeObject( WorldSceneObject source, string path, BorealisMesh mesh, ModelFile file )
		{
			var flags = source.Flags;
			var isModel = (flags & ObjectTypeFlags.Model) != 0;

			var skin = 0;
			if ( isModel && !string.IsNullOrEmpty( source.Skin ) )
			{
				for ( int g = 0; g < file.MaterialGroups.Count; g++ )
				{
					if ( string.Equals( file.MaterialGroups[g].Name, source.Skin, StringComparison.OrdinalIgnoreCase ) ) skin = g;
				}
			}

			var groups = isModel ? source.MeshGroups( file.DefaultMeshGroupMask ) : file.DefaultMeshGroupMask;
			var drawn = Variant( path, mesh, file, groups, skin );
			var obj = new MeshObject( drawn, ToTransform( source.Transform, map.Origin ) );

			// CWorldNode's flags: static unless it renders with dynamic objects; a light blocker only casts shadows (it's for
			// baking); a decal draws only in the decal layer
			obj.IsStatic = (flags & ObjectTypeFlags.RenderWithDynamic) == 0;
			obj.CastShadows = (flags & ObjectTypeFlags.NoShadows) == 0;

			// Opaque, and translucent too where a draw blends (the mesh system's translucency flags)
			obj.DrawsTranslucent = HasTranslucentDraws( drawn );
			if ( (flags & ObjectTypeFlags.BlockLight) != 0 )
			{
				obj.IsStatic = true;
				obj.CastShadows = true;
				obj.DrawsOpaque = false;
				obj.DrawsTranslucent = false;
			}
			else if ( (flags & ObjectTypeFlags.Decal) != 0 )
			{
				obj.IsStatic = false;
				obj.CastShadows = false;
				obj.DrawsOpaque = false;
				obj.DrawsTranslucent = false;
				obj.Decal = true;
			}

			obj.IsWorld = obj.IsStatic || !isModel;

			if ( isModel && source.Tint != System.Numerics.Vector4.One ) obj.Tint = new Color( source.Tint.X, source.Tint.Y, source.Tint.Z, source.Tint.W );
			// Clamped to the model's last LOD; a model without LODs takes none (CModel::ComputeMaxLODLevelUsedByModel's -1)
			if ( source.LodOverride >= 0 ) obj.LodOverride = drawn.LodCount <= 1 ? -1 : Math.Min( source.LodOverride, drawn.LodCount - 1 );
			if ( source.LightingOrigin is { } origin ) obj.LightingOrigin = new Vector3( origin.X, origin.Y, origin.Z ) + map.Origin;
			obj.LightGroup = source.LightGroup;
			obj.LightProbeHandshake = source.LightProbeVolume;

			ApplyMaterialFlags( obj, drawn, file, source.OverlayRenderOrder );

			if ( drawn.HasLightmappedDraws ) obj.LightmapAttributes = Lightmaps();
			return obj;
		}

		/// <summary>
		/// The flags native's scene system takes from an object's materials (<c>DefaultMaterialBasedSceneObjectUpdater</c>):
		/// an <c>overlay</c> material puts it in the static overlay layer in its render order, <c>NoZPrepass</c> keeps it out
		/// of the prepass, materials that all have <c>DoNotCastShadows</c> cast none, <c>bWantsFBCopyTexture</c> asks for the
		/// frame buffer copy, and a draw without baked lighting whose material wants a probe needs one.
		/// </summary>
		static void ApplyMaterialFlags( MeshObject obj, BorealisMesh mesh, ModelFile file, int renderOrder )
		{
			var any = false;
			var allNoShadows = true;
			for ( int lod = 0; lod < mesh.LodCount; lod++ )
			{
				foreach ( var draw in mesh.DrawsForLod( lod ) )
				{
					if ( draw.Material is not { } material ) continue;
					any = true;

					if ( Attribute( material, "overlay" ) && obj.LayerMatch != LayerMatch.StaticOverlay )
					{
						obj.LayerMatch = LayerMatch.StaticOverlay;
						obj.RenderOrder = renderOrder;
					}

					if ( Attribute( material, "NoZPrepass" ) ) obj.DepthPrepass = false;
					if ( Attribute( material, "bWantsFBCopyTexture" ) ) obj.WantsFrameBufferCopy = true;
					allNoShadows &= Attribute( material, "DoNotCastShadows" );

					const MeshDrawFlags Baked = MeshDrawFlags.BakedLightingFromLightmap | MeshDrawFlags.BakedLightingFromVertexStream;
					var call = draw.Mesh < file.Meshes.Count && draw.DrawCall < file.Meshes[draw.Mesh].DrawCalls.Count ? file.Meshes[draw.Mesh].DrawCalls[draw.DrawCall] : null;
					if ( call is not null && (call.Flags & Baked) == 0 && Attribute( material, "NeedsLightProbe" ) && Attribute( material, "UsesBakedLightingFromProbe", true ) )
						obj.NeedsLightProbe = true;
				}
			}

			if ( any && allNoShadows ) obj.CastShadows = false;
		}

		static bool HasTranslucentDraws( BorealisMesh mesh )
		{
			for ( int lod = 0; lod < mesh.LodCount; lod++ )
			{
				foreach ( var draw in mesh.DrawsForLod( lod ) )
				{
					if ( draw.Translucent ) return true;
				}
			}

			return false;
		}

		/// <summary>A material's attribute as a bool (a number other than zero): a surface answers as the material it stands in for.</summary>
		static bool Attribute( GpuMaterial material, string name, bool fallback = false ) => material switch
		{
			BackendMaterial m => m.Attribute( name, fallback ),
			SurfaceMaterial s => s.Attribute( name, fallback ),
			_ => fallback,
		};

		/// <summary>
		/// An aggregate as <c>CWorldNode::CreateAggregateSceneObjects</c> makes it, its fragments as native's aggregate draws
		/// them (<c>RenderTools::GetAggregateFragments</c>): each one draw of the model's mesh, in world space, its draw's
		/// tint and uv density, its index the base instance. A fragment draws the draw call its draw index names by mesh id
		/// (<see cref="ModelFile.AggregateDrawCall"/>). Draws share one object per vertex and index buffer they bind: a
		/// compiled map's draws with culling data get buffers of their own, which the others' can't draw them from.
		/// </summary>
		void MakeAggregates( WorldAggregate source, BorealisMesh mesh, ModelFile file )
		{
			// Draw descriptors numbered across meshes in order (a world aggregate's model has one)
			var calls = new List<(int Mesh, MeshDrawCall Call)>();
			foreach ( var m in file.Meshes )
			{
				foreach ( var c in m.DrawCalls ) calls.Add( (m.Index, c) );
			}

			if ( calls.Count == 0 || source.Meshes.Count == 0 ) return;

			var origin = map.Origin;
			var transform = Matrix3x4.Identity;
			transform.Row0.w = origin.x;
			transform.Row1.w = origin.y;
			transform.Row2.w = origin.z;

			// Fragments by the buffers their draws bind, the first such draw's index (what the group's mesh draws) beside them
			var draws = mesh.DrawsForLod( 0 );
			var groups = new Dictionary<(int Mesh, int Vertices, int Indices), (int First, List<AggregateObject.FragmentDraw> Fragments)>();
			for ( int i = 0; i < source.Meshes.Count; i++ )
			{
				var fragment = source.Meshes[i];
				var index = file.AggregateDrawCall( fragment.DrawCall );
				if ( index < 0 ) continue;

				var (meshIndex, call) = calls[index];
				var drawIndex = -1;
				for ( int d = 0; d < draws.Length && drawIndex < 0; d++ )
				{
					if ( draws[d].Mesh == meshIndex && draws[d].DrawCall == index - FirstCall( file, meshIndex ) ) drawIndex = d;
				}

				if ( drawIndex < 0 ) continue;

				var vertices = call.VertexBuffers.Count > 0 ? call.VertexBuffers[0].Buffer : 0;
				var key = (meshIndex, vertices, call.IndexBuffer.Buffer);
				if ( !groups.TryGetValue( key, out var group ) ) groups[key] = group = (drawIndex, new());

				// Relative to what the group's first draw binds: its vertex buffer offset and its index buffer offset
				var first = calls[FirstCall( file, meshIndex ) + draws[group.First].DrawCall].Call;
				var buffers = file.Meshes[meshIndex];
				var stride = vertices < buffers.VertexBuffers.Count ? Math.Max( buffers.VertexBuffers[vertices].Stride, 1 ) : 1;
				var indexSize = call.IndexBuffer.Buffer < buffers.IndexBuffers.Count ? Math.Max( buffers.IndexBuffers[call.IndexBuffer.Buffer].Size, 1 ) : 2;
				var vertexShift = ((call.VertexBuffers.Count > 0 ? call.VertexBuffers[0].Offset : 0) - (first.VertexBuffers.Count > 0 ? first.VertexBuffers[0].Offset : 0)) / stride;
				var indexShift = (call.IndexBuffer.Offset - first.IndexBuffer.Offset) / indexSize;

				var objectFlags = (ObjectTypeFlags)fragment.Flags;
				var fragmentFlags = FragmentFlags.None;
				if ( (objectFlags & ObjectTypeFlags.NoShadows) != 0 ) fragmentFlags |= FragmentFlags.DoNotCastShadows;
				if ( (objectFlags & ObjectTypeFlags.DisabledInLowQuality) != 0 ) fragmentFlags |= FragmentFlags.DisabledInLowQuality;
				if ( (objectFlags & ObjectTypeFlags.RenderToCubemaps) != 0 ) fragmentFlags |= FragmentFlags.RenderToCubemaps;

				group.Fragments.Add( new AggregateObject.FragmentDraw
				{
					Transform = transform,
					BoundsMin = new Vector3( fragment.Mins.X, fragment.Mins.Y, fragment.Mins.Z ) + origin,
					BoundsMax = new Vector3( fragment.Maxs.X, fragment.Maxs.Y, fragment.Maxs.Z ) + origin,
					Flags = fragmentFlags,
					DrawTint = new Vector3( call.Tint.X, call.Tint.Y, call.Tint.Z ),
					UvDensity = call.UvDensity,
					Args = new Borealis.GpuBuffer.IndirectDrawIndexedArguments
					{
						IndexCount = (uint)call.IndexCount,
						InstanceCount = 1,
						FirstIndex = (uint)(call.StartIndex + indexShift),
						BaseVertex = call.BaseVertex + vertexShift,
						FirstInstance = (uint)group.Fragments.Count,
					},
				} );
			}

			foreach ( var (first, fragments) in groups.Values )
			{
				var groupMesh = groups.Count == 1 && first == 0 ? mesh : new BorealisMesh( mesh.Geometry, mesh.Bounds, [[draws[first]]], [] );
				var lightmapped = (calls[FirstCall( file, draws[first].Mesh ) + draws[first].DrawCall].Call.Flags & MeshDrawFlags.BakedLightingFromLightmap) != 0;
				var aggregate = new AggregateObject( groupMesh, fragments.ToArray() )
				{
					Lightmapped = lightmapped,
					CastShadows = ((ObjectTypeFlags)source.Flags & ObjectTypeFlags.NoShadows) == 0,
					IsStatic = true,
					IsWorld = true,
				};

				if ( lightmapped ) aggregate.LightmapAttributes = Lightmaps();
				Add( aggregate );
			}
		}

		/// <summary>The number of a mesh's first draw call among the model's, numbered across meshes in order.</summary>
		static int FirstCall( ModelFile file, int mesh )
		{
			var first = 0;
			for ( int m = 0; m < mesh; m++ ) first += file.Meshes[m].DrawCalls.Count;
			return first;
		}

		/// <summary>
		/// The mesh an object draws: the model's with every mesh group, its draws hidden outside <paramref name="groups"/>
		/// and given <paramref name="skin"/>'s materials, shared by every object drawing the same.
		/// </summary>
		BorealisMesh Variant( string path, BorealisMesh mesh, ModelFile file, ulong groups, int skin )
		{
			if ( variants.TryGetValue( (path, groups, skin), out var made ) ) return made;

			// Each draw's material as the model has it
			var drawn = new Dictionary<(int, int), GpuMaterial>();
			for ( int lod = 0; lod < mesh.LodCount; lod++ )
			{
				foreach ( var draw in mesh.DrawsForLod( lod ) ) drawn.TryAdd( (draw.Mesh, draw.DrawCall), draw.Material );
			}

			var states = new List<BorealisMesh.DrawState>();
			for ( int m = 0; m < file.Meshes.Count; m++ )
			{
				var visible = (file.Meshes[m].MeshGroupMask & groups) != 0;
				for ( int d = 0; d < file.Meshes[m].DrawCalls.Count; d++ )
				{
					if ( !drawn.TryGetValue( (m, d), out var material ) ) continue;
					var call = file.Meshes[m].DrawCalls[d];
					if ( skin != 0 )
					{
						var materialPath = file.MaterialFor( call.Material, skin );
						if ( !string.Equals( materialPath, file.MaterialFor( call.Material, 0 ), StringComparison.OrdinalIgnoreCase ) )
						{
							material = gpu.ModelMaterial( materialPath );
							heldMaterials.Add( materialPath );
						}
					}

					var translucent = BorealisMesh.IsTranslucent( material );
					var fastPath = material is { ShadowFastPath: true } && !translucent && !BorealisMesh.IsAlphaTest( material );
					var lightmapped = (call.Flags & MeshDrawFlags.BakedLightingFromLightmap) != 0;
					states.Add( new BorealisMesh.DrawState( m, d, material, visible, translucent, fastPath, lightmapped ) );
				}
			}

			made = states.Count == 0 ? mesh : mesh.WithDraws( states.ToArray() );
			variants[(path, groups, skin)] = made;
			return made;
		}

		ShaderParameters Lightmaps()
		{
			if ( map.World.Lightmaps.Count == 0 ) return null;
			if ( !lightmapsHeld )
			{
				gpu.HoldMapLightmaps( map.Folder );
				lightmapsHeld = true;
			}

			return gpu.MapLightmapParameters( map.Folder );
		}

		void Clear()
		{
			foreach ( var obj in made ) world.Remove( obj );
			made.Clear();
			waiting.Clear();
		}

		public void Dispose()
		{
			Clear();
			foreach ( var (_, mesh) in meshes ) gpu.ReleaseModel( mesh );
			meshes.Clear();
			variants.Clear();
			foreach ( var path in heldMaterials ) gpu.ReleaseModelMaterial( path );
			heldMaterials.Clear();
			if ( lightmapsHeld ) gpu.ReleaseMapLightmaps( map.Folder );
			lightmapsHeld = false;
		}
	}
}
