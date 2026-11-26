using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Borealis.Source2;
using Borealis.Source2.Models;
using Borealis.Source2.Resources;
using Borealis.Source2.World;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using GpuDevice = Borealis.Gpu.Device;
using GpuTextureBarrier = Borealis.Gpu.TextureBarrier;
using NumericsMatrix = System.Numerics.Matrix4x4;
using NumericsVector3 = System.Numerics.Vector3;

namespace Sandbox.BorealisLab.Maps;

/// <summary>
/// A compiled map in a real <see cref="RenderWorld"/>, drawn by Borealis's <see cref="RenderSystem"/> through
/// <see cref="BackendDevice"/> on Borealis.Gpu, with s&amp;box's own compiled shaders and the map's materials, textures and
/// constants as their <c>.vmat_c</c> files have them. Scene objects are <see cref="MeshObject"/>s (LODs, mesh groups and
/// skins as compiled); aggregates are <see cref="AggregateObject"/>s whose fragments draw as one multi-draw indirect.
/// Baked lighting is the map's own: the world's lightmaps for lightmapped draws, and each light probe volume's textures
/// for the objects the compiler gave it (<see cref="WorldSceneObject.LightProbeVolume"/>). The entity lump gives the
/// lights the lightmaps index, the sun, fog, the 2D sky and the probe volumes' boxes (see <c>MapWorld.Entities.cs</c>), and
/// the map's <c>_sky</c> package its 3D skybox, loaded the same way into a world of its own.
/// </summary>
internal sealed partial class MapWorld : IDisposable
{
	readonly GpuDevice gpu;
	readonly RenderHost previousHost;
	readonly List<VpkFile> packages = new();

	public BackendDevice Backend { get; }
	public RenderWorld World { get; } = new();

	/// <summary>The 3D skybox's world, when the map has one.</summary>
	public RenderWorld SkyWorld { get; private set; }
	public RenderSystem System { get; }
	public ViewTarget Target { get; private set; }

	public int MeshObjects { get; private set; }
	public int Aggregates { get; private set; }
	public int Fragments { get; private set; }
	public int Models { get; private set; }
	public int LightmapTextures { get; private set; }
	public int LightmappedObjects { get; private set; }
	public int ProbeVolumes { get; private set; }
	public int ProbeObjects { get; private set; }
	public List<string> Problems { get; } = new();
	public List<(string Name, double Ms)> Phases { get; } = new();

	readonly List<MeshObject> meshObjects = new();
	readonly List<AggregateObject> aggregateObjects = new();

	/// <summary>The static overlays' render orders and how many objects have each, for the draw census.</summary>
	public IEnumerable<(int Order, int Objects)> OverlayOrders => meshObjects.Where( o => o.LayerMatch == LayerMatch.StaticOverlay ).GroupBy( o => o.RenderOrder ).Select( g => (g.Key, g.Count()) ).OrderBy( x => x.Key );

	/// <param name="castShadows">Whether the map's static geometry casts sun shadows; Construct's sun is baked, so in the
	/// game its world geometry and static props don't.</param>
	/// <param name="bakedLighting">Light lightmapped draws from the map's lightmaps and the rest from its probe volumes.</param>
	/// <param name="entities">Read the entity lump: lights, the sun, fog, the sky, probe volume boxes and the 3D skybox.</param>
	/// <param name="gpuDriven">Keep static geometry in GPU-driven pages too, so aggregates can draw GPU-driven (<see cref="BackendDevice.GpuDriven"/>).</param>
	/// <param name="made">Called with the backend as soon as it's made, before anything loads (to hear its errors from the start).</param>
	public MapWorld( GpuDevice gpu, string folder, ContentFiles core, bool castShadows, bool aggregatesAsMeshes = false, bool bakedLighting = true, bool entities = true, bool gpuDriven = false, Action<BackendDevice> made = null )
	{
		this.gpu = gpu;
		timer = Stopwatch.StartNew();

		// The map is the shortest-named package under maps/ (a map in a folder of its own, maps/city/name.vpk, is city/name);
		// its 3D skybox is the one its skybox_reference names, or else name_sky
		var maps = Path.Combine( folder, "maps" );
		var vpks = Directory.GetFiles( maps, "*.vpk", SearchOption.AllDirectories );
		string MapName( string path ) => Path.ChangeExtension( Path.GetRelativePath( maps, path ), null ).Replace( '\\', '/' );
		var vpk = vpks.Where( x => !Path.GetFileNameWithoutExtension( x ).EndsWith( "_sky", StringComparison.OrdinalIgnoreCase ) ).OrderBy( x => x.Length ).First();
		var map = MapName( vpk );

		var content = new ContentFiles( folder );
		foreach ( var root in core.Roots ) content.Add( root );
		var mapPackage = VpkFile.Open( vpk );
		packages.Add( mapPackage );
		content.AddPackage( mapPackage );

		Backend = new BackendDevice( gpu, content ) { GpuDrivenGeometry = gpuDriven };
		made?.Invoke( Backend );

		// -surface-convert blendable,...: those .shaders' materials drawn with the surfaces beside them (blendable.slang)
		foreach ( var shader in (Args.String( "-surface-convert" ) ?? "").Split( ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries ) )
		{
			if ( Backend.Surfaces.Folders.Count == 0 ) Backend.Surfaces.AddFolder( Path.Combine( core.Roots[0], "shaders" ) );
			Backend.Surfaces.Convert( $"shaders/{shader}.shader", $"{shader}.slang" );
		}

		// -surface-convert-match a,b: only materials whose path contains one of these convert (for finding a difference)
		if ( Args.String( "-surface-convert-match" ) is { } match )
		{
			var parts = match.Split( ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries );
			Backend.Surfaces.ConvertMaterial = path => parts.Any( p => path.Contains( p, StringComparison.OrdinalIgnoreCase ) );
		}

		// A package's own Slang shaders, compiled as it mounts
		var packageShaders = Path.Combine( folder, "shaders" );
		if ( Directory.Exists( packageShaders ) && Directory.GetFiles( packageShaders, "*.slang" ).Length > 0 ) _ = Backend.Surfaces.Mount( packageShaders );

		previousHost = RenderHost.Current;
		RenderHost.Current = new LabBackendRenderHost( Backend );
		System = new RenderSystem { ParallelRecording = false };

		World.Lighting.SunDirection = new Vector3( -0.55f, -0.45f, -0.5f ).Normal;
		World.Lighting.SunColor = new Color( 1.25f, 1.15f, 1.0f );
		World.Lighting.AmbientColor = new Color( 0.12f, 0.15f, 0.2f, 1 );
		World.Lighting.SunShadowCascades = 3;

		var level = LoadLevel( World, content, map, castShadows, aggregatesAsMeshes, bakedLighting, entities );
		var reference = entities ? level.Entities.FirstOrDefault( e => e.ClassName == "skybox_reference" ) : null;
		var skyName = reference?.String( "targetMapName" ) is { Length: > 0 } target ? Path.ChangeExtension( target.Replace( '\\', '/' ), null ) : null;
		if ( skyName is not null && skyName.StartsWith( "maps/", StringComparison.OrdinalIgnoreCase ) ) skyName = skyName["maps/".Length..];
		var skyVpk = vpks.FirstOrDefault( x => string.Equals( MapName( x ), skyName, StringComparison.OrdinalIgnoreCase ) )
			?? vpks.FirstOrDefault( x => string.Equals( MapName( x ), map + "_sky", StringComparison.OrdinalIgnoreCase ) );
		if ( reference is not null && skyVpk is not null )
		{
			var skyPackage = VpkFile.Open( skyVpk );
			packages.Add( skyPackage );
			content.AddPackage( skyPackage );
			SkyWorld = new RenderWorld();
			var sky = LoadLevel( SkyWorld, content, MapName( skyVpk ), false, aggregatesAsMeshes, bakedLighting, entities );
			LoadSkybox( reference, sky );
		}

		if ( entities && !Args.Has( "-noenvprobes" ) ) AddEnvMapProbes( level );
		// -nowater leaves the scene's water bodies out
		if ( entities && !Args.Has( "-nowater" ) ) LoadWater( content, map );
	}

	readonly Stopwatch timer;

	void Phase( string name )
	{
		Phases.Add( (name, timer.Elapsed.TotalMilliseconds) );
		timer.Restart();
	}

	/// <summary>One compiled world loaded into a render world, and what of it later steps need.</summary>
	sealed class Level
	{
		public string Map;
		public IReadOnlyList<MapEntity> Entities = [];
		public readonly List<MeshObject> Meshes = new();
		public readonly List<AggregateObject> Aggregates = new();
	}

	/// <summary>
	/// Load <paramref name="map"/>'s world into <paramref name="target"/>: its models, scene objects and aggregates, its
	/// entities and its baked lighting.
	/// </summary>
	Level LoadLevel( RenderWorld target, ContentFiles content, string map, bool castShadows, bool aggregatesAsMeshes, bool bakedLighting, bool entities )
	{
		var level = new Level { Map = map };
		var world = WorldFile.Load( content, $"maps/{map}/world.vwrld" );
		Phase( $"World {map}" );

		// Models read in parallel; their buffers, materials and textures are made on this thread
		var paths = world.Nodes.SelectMany( n => n.SceneObjects.Select( o => o.Model ).Concat( n.Aggregates.Select( a => a.Model ) ) )
			.Where( x => !string.IsNullOrEmpty( x ) ).Distinct( StringComparer.OrdinalIgnoreCase ).ToArray();
		var files = new ConcurrentDictionary<string, ModelFile>( StringComparer.OrdinalIgnoreCase );
		Parallel.ForEach( paths, path =>
		{
			try
			{
				files[path] = ModelFile.Load( content, path );
			}
			catch ( Exception e )
			{
				lock ( Problems ) Problems.Add( $"model {path}: {e.Message}" );
			}
		} );
		Phase( $"Models read ({files.Count})" );

		// Every material's textures read and uploaded once, before the models ask for them
		var materials = files.Values.SelectMany( f => f.Meshes.SelectMany( m => m.DrawCalls.Select( d => d.Material ) ).Concat( f.MaterialGroups.SelectMany( g => g.Materials ) ) )
			.Where( x => !string.IsNullOrEmpty( x ) ).Distinct( StringComparer.OrdinalIgnoreCase ).ToArray();
		foreach ( var material in materials ) Backend.LoadMaterial( material );
		Phase( $"Materials and textures ({materials.Length})" );

		var meshes = new Dictionary<(string Path, ulong Mask, int Skin), RenderMesh>();
		RenderMesh Mesh( string path, ModelFile file, ulong mask, int skin )
		{
			if ( meshes.TryGetValue( (path, mask, skin), out var mesh ) ) return mesh;
			Models++;
			return meshes[(path, mask, skin)] = BackendModel.Create( Backend, file, mask, skin );
		}

		var probed = new List<(MeshObject Object, int Volume, NumericsVector3 Mins, NumericsVector3 Maxs)>();
		foreach ( var node in world.Nodes )
		{
			foreach ( var o in node.SceneObjects )
			{
				if ( !files.TryGetValue( o.Model, out var file ) ) continue;
				var skin = 0;
				for ( int g = 0; g < file.MaterialGroups.Count; g++ )
				{
					if ( !string.IsNullOrEmpty( o.Skin ) && string.Equals( file.MaterialGroups[g].Name, o.Skin, StringComparison.OrdinalIgnoreCase ) ) skin = g;
				}

				var mesh = Mesh( o.Model, file, o.MeshGroups( file.DefaultMeshGroupMask ), skin );
				var obj = new MeshObject( mesh, ToTransform( o.Transform ) ) { CastShadows = castShadows };
				ApplyTypeFlags( obj, ObjectTypeFlags( o.TypeFlags ), castShadows );
				ApplyMaterialFlags( obj, o.OverlayRenderOrder );
				if ( o.LodOverride >= 0 ) obj.LodOverride = o.LodOverride;
				if ( o.Tint != global::System.Numerics.Vector4.One ) obj.Tint = new Color( o.Tint.X, o.Tint.Y, o.Tint.Z, o.Tint.W );
				target.Add( obj );
				meshObjects.Add( obj );
				level.Meshes.Add( obj );
				MeshObjects++;
				if ( o.LightProbeVolume != 0 )
				{
					var (mins, maxs) = WorldBounds( file, o.Transform );
					probed.Add( (obj, o.LightProbeVolume, mins, maxs) );
				}
			}

			foreach ( var a in node.Aggregates )
			{
				if ( !files.TryGetValue( a.Model, out var file ) ) continue;
				if ( aggregatesAsMeshes )
				{
					// Each draw of the aggregate as an object of its own, drawn directly (a comparison for the multi-draws)
					var whole = Mesh( a.Model, file, file.DefaultMeshGroupMask, 0 );
					var calls = new List<(int, int)>();
					for ( int m = 0; m < file.Meshes.Count; m++ ) for ( int c = 0; c < file.Meshes[m].DrawCalls.Count; c++ ) calls.Add( (m, c) );
					foreach ( var fragment in a.Meshes )
					{
						var index = file.AggregateDrawCall( fragment.DrawCall );
						if ( index < 0 ) continue;
						var (fm, fc) = calls[index];
						var draw = whole.DrawsForLod( 0 ).ToArray().FirstOrDefault( d => d.Mesh == fm && d.DrawCall == fc );
						if ( draw.Material is null ) continue;
						var bounds = new BBox( new Vector3( fragment.Mins.X, fragment.Mins.Y, fragment.Mins.Z ), new Vector3( fragment.Maxs.X, fragment.Maxs.Y, fragment.Maxs.Z ) );
						var obj = new MeshObject( new RenderMesh( whole.Geometry, bounds, [[draw]], [] ), Transform.Zero ) { CastShadows = castShadows };
						target.Add( obj );
						meshObjects.Add( obj );
						level.Meshes.Add( obj );
						Fragments++;
					}

					continue;
				}

				foreach ( var aggregate in MakeAggregates( Mesh( a.Model, file, file.DefaultMeshGroupMask, 0 ), file, a ) )
				{
					aggregate.CastShadows = castShadows;
					if ( Materials( aggregate.Mesh ).Any( m => Attribute( m, "overlay", false ) ) ) aggregate.LayerMatch = LayerMatch.StaticOverlay;
					target.Add( aggregate );
					aggregateObjects.Add( aggregate );
					level.Aggregates.Add( aggregate );
					Aggregates++;
					Fragments += aggregate.Fragments.Length;
				}
			}
		}

		Phase( $"World objects ({MeshObjects} objects, {Aggregates} aggregates, {Fragments} fragments, {Models} meshes)" );

		if ( entities )
		{
			level.Entities = LoadEntities( content, world );
			ApplyEntities( target, level );
			Phase( $"Entities ({level.Entities.Count})" );
		}

		if ( !bakedLighting ) return level;
		LoadLightmaps( world, level );

		// The volumes' own boxes from the entity lump, else boxes around the objects that use them
		if ( !entities || !LoadProbeVolumes( target, level, probed ) ) EstimateProbeVolumes( target, map, probed );
		Phase( $"Baked lighting ({LightmapTextures} lightmaps for {LightmappedObjects} objects and aggregates, {ProbeVolumes} probe volumes for {ProbeObjects} objects)" );
		return level;
	}

	/// <summary>
	/// The world's lightmaps, as native's <c>SetSceneObjectLightmapAttributes</c> binds them: one set of attributes for every
	/// aggregate and every object with lightmapped draws, with the textures by their base names, their bindless indices
	/// and the uv scale in <c>BakedLightingConstantBuffer_t</c>, and the lightmap combo.
	/// </summary>
	void LoadLightmaps( WorldFile world, Level level )
	{
		var problems = Problems.Count;
		var attributes = Backend.LightmapParameters( world.Lightmaps, world.LightmapUvScale, Problems );
		if ( attributes is null ) return;
		LightmapTextures += world.Lightmaps.Count( x => Path.GetFileNameWithoutExtension( x ) != "debug_chart_color" ) - (Problems.Count - problems);

		foreach ( var aggregate in level.Aggregates )
		{
			aggregate.Lightmapped = true;
			aggregate.LightmapAttributes = attributes;
			LightmappedObjects++;
		}

		foreach ( var obj in level.Meshes )
		{
			if ( obj.Mesh is not { HasLightmappedDraws: true } ) continue;
			obj.LightmapAttributes = attributes;
			LightmappedObjects++;
		}
	}

	/// <summary>
	/// Without the entity lump: a light probe volume per volume the compiler chose for objects, with its textures (the
	/// probe, direct light indices and strengths) and its constants as <c>CSceneLightProbeVolumeObject::CreateConstants</c>
	/// makes them, over the bounds of its objects, which need a probe.
	/// </summary>
	void EstimateProbeVolumes( RenderWorld target, string map, List<(MeshObject Object, int Volume, NumericsVector3 Mins, NumericsVector3 Maxs)> probed )
	{
		foreach ( var group in probed.GroupBy( p => p.Volume ).OrderBy( g => g.Key ) )
		{
			var prefix = $"maps/{map}/lightmaps/env_light_probe_volume_{group.Key}";
			if ( Backend.LoadTexture( prefix + ".vtex" ) is not BackendTexture { Dimension: TextureDimension.Texture3D } probe )
			{
				Problems.Add( $"probe volume {group.Key} isn't readable" );
				continue;
			}

			// Each the shape the shader samples (a missing one would be a 2D stand-in): the probe's own otherwise
			var indices = Backend.LoadTexture( prefix + "_dli.vtex" ) is BackendTexture { Dimension: TextureDimension.Texture3D } dli ? dli : probe;
			var scalars = Backend.LoadTexture( prefix + "_dls.vtex" ) is BackendTexture { Dimension: TextureDimension.Texture3D } dls ? dls : probe;
			var mins = group.Select( p => p.Mins ).Aggregate( NumericsVector3.Min ) - new NumericsVector3( 16 );
			var maxs = group.Select( p => p.Maxs ).Aggregate( NumericsVector3.Max ) + new NumericsVector3( 16 );
			var attributes = Backend.LightProbeVolumeParameters( probe, indices, scalars, Transform.Zero, new Vector3( mins.X, mins.Y, mins.Z ), new Vector3( maxs.X, maxs.Y, maxs.Z ) );

			target.LightProbeVolumes.Add( new global::Borealis.LightProbeVolume
			{
				BoxMins = new Vector3( mins.X, mins.Y, mins.Z ),
				BoxMaxs = new Vector3( maxs.X, maxs.Y, maxs.Z ),
				LightGroups = [0],
				Attributes = attributes,
				Handshake = group.Key,
			} );
			ProbeVolumes++;

			foreach ( var (obj, _, _, _) in group )
			{
				obj.NeedsLightProbe = true;
				obj.LightProbeHandshake = group.Key;
				ProbeObjects++;
			}
		}
	}

	/// <summary>A model's bounds placed by a row-vector object-to-world matrix, in world space.</summary>
	static (NumericsVector3 Mins, NumericsVector3 Maxs) WorldBounds( ModelFile file, NumericsMatrix transform )
	{
		var mins = new NumericsVector3( float.MaxValue );
		var maxs = new NumericsVector3( float.MinValue );
		for ( int i = 0; i < 8; i++ )
		{
			var corner = new NumericsVector3( (i & 1) != 0 ? file.Maxs.X : file.Mins.X, (i & 2) != 0 ? file.Maxs.Y : file.Mins.Y, (i & 4) != 0 ? file.Maxs.Z : file.Mins.Z );
			var world = NumericsVector3.Transform( corner, transform );
			mins = NumericsVector3.Min( mins, world );
			maxs = NumericsVector3.Max( maxs, world );
		}

		return (mins, maxs);
	}

	/// <summary>
	/// An aggregate's draws as <see cref="AggregateObject"/>s: one per run of draws sharing a vertex and an index buffer (a
	/// compiled map's draws with culling data get buffers of their own), each fragment one draw call over the buffers its
	/// first draw binds, in world space. A fragment draws the draw call its draw index names by mesh id
	/// (<see cref="ModelFile.AggregateDrawCall"/>).
	/// </summary>
	IEnumerable<AggregateObject> MakeAggregates( RenderMesh mesh, ModelFile file, WorldAggregate aggregate )
	{
		// Draw calls numbered across meshes, as the world numbers them
		var calls = new List<(int Mesh, int Call)>();
		for ( int m = 0; m < file.Meshes.Count; m++ )
		{
			for ( int c = 0; c < file.Meshes[m].DrawCalls.Count; c++ ) calls.Add( (m, c) );
		}

		var draws = mesh.DrawsForLod( 0 ).ToArray();
		var groups = new Dictionary<(int Mesh, int Vertices, int Indices), (RenderMesh.Draw First, MeshDrawCall FirstCall, List<AggregateObject.FragmentDraw> Fragments)>();
		var transform = 0;
		foreach ( var fragment in aggregate.Meshes )
		{
			var index = file.AggregateDrawCall( fragment.DrawCall );
			if ( index < 0 ) continue;
			var (m, c) = calls[index];
			var call = file.Meshes[m].DrawCalls[c];
			if ( call.Primitive != PrimitiveKind.Triangles || call.IndexCount == 0 ) continue;

			var drawIndex = Array.FindIndex( draws, d => d.Mesh == m && d.DrawCall == c );
			if ( drawIndex < 0 ) continue;

			var vertices = call.VertexBuffers.Count > 0 ? call.VertexBuffers[0].Buffer : 0;
			var key = (m, vertices, call.IndexBuffer.Buffer);
			if ( !groups.TryGetValue( key, out var group ) ) groups[key] = group = (draws[drawIndex], call, new());

			// Relative to what the group's first draw binds: its vertex buffer offset and its index buffer offset
			var mesh0 = file.Meshes[m];
			var stride = mesh0.VertexBuffers.Count > vertices ? Math.Max( mesh0.VertexBuffers[vertices].Stride, 1 ) : 1;
			var indexSize = mesh0.IndexBuffers.Count > call.IndexBuffer.Buffer ? Math.Max( mesh0.IndexBuffers[call.IndexBuffer.Buffer].Size, 1 ) : 2;
			var vertexShift = ((call.VertexBuffers.Count > 0 ? call.VertexBuffers[0].Offset : 0) - (group.FirstCall.VertexBuffers.Count > 0 ? group.FirstCall.VertexBuffers[0].Offset : 0)) / stride;
			var indexShift = (call.IndexBuffer.Offset - group.FirstCall.IndexBuffer.Offset) / indexSize;

			var world = fragment.HasTransform && transform < aggregate.FragmentTransforms.Count ? aggregate.FragmentTransforms[transform++] : NumericsMatrix.Identity;
			group.Fragments.Add( new AggregateObject.FragmentDraw
			{
				Transform = ToMatrix3x4( world ),
				BoundsMin = new Vector3( fragment.Mins.X, fragment.Mins.Y, fragment.Mins.Z ),
				BoundsMax = new Vector3( fragment.Maxs.X, fragment.Maxs.Y, fragment.Maxs.Z ),
				Flags = (fragment.Flags & 0x400) != 0 ? FragmentFlags.RenderToCubemaps : FragmentFlags.None,
				DrawTint = new Vector3( fragment.Tint.X * call.Tint.X, fragment.Tint.Y * call.Tint.Y, fragment.Tint.Z * call.Tint.Z ),
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

		foreach ( var (first, _, fragments) in groups.Values )
		{
			if ( fragments.Count == 0 ) continue;
			var bounds = new BBox( fragments[0].BoundsMin, fragments[0].BoundsMax );
			foreach ( var f in fragments ) bounds = bounds.AddPoint( f.BoundsMin ).AddPoint( f.BoundsMax );
			var groupMesh = new RenderMesh( mesh.Geometry, bounds, [[first]], [] );
			yield return new AggregateObject( groupMesh, fragments.ToArray() );
		}
	}

	const uint ObjectTypeDecal = 0x4, ObjectTypeBlockLight = 0x10, ObjectTypeNoShadows = 0x20;

	/// <summary><c>m_nObjectTypeFlags</c> (<c>ObjectTypeFlags_t</c>) from its number or its flag names.</summary>
	static uint ObjectTypeFlags( string flags )
	{
		if ( string.IsNullOrEmpty( flags ) ) return 0;
		if ( uint.TryParse( flags, out var number ) ) return number;

		uint value = 0;
		foreach ( var name in flags.Split( ['|', ' ', ','], StringSplitOptions.RemoveEmptyEntries ) )
		{
			value |= name switch
			{
				"OBJECT_TYPE_IMAGE_LOD" => 0x1u,
				"OBJECT_TYPE_GEOMETRY_LOD" => 0x2u,
				"OBJECT_TYPE_DECAL" => ObjectTypeDecal,
				"OBJECT_TYPE_MODEL" => 0x8u,
				"OBJECT_TYPE_BLOCK_LIGHT" => ObjectTypeBlockLight,
				"OBJECT_TYPE_NO_SHADOWS" => ObjectTypeNoShadows,
				"OBJECT_TYPE_WORLDSPACE_TEXURE_BLEND" => 0x40u,
				"OBJECT_TYPE_DISABLED_IN_LOW_QUALITY" => 0x80u,
				"OBJECT_TYPE_NO_SUN_SHADOWS" => 0x100u,
				"OBJECT_TYPE_RENDER_WITH_DYNAMIC" => 0x200u,
				"OBJECT_TYPE_RENDER_TO_CUBEMAPS" => 0x400u,
				_ => 0u,
			};
		}

		return value;
	}

	/// <summary>
	/// A scene object's flags as the world renderer makes them (<c>CWorldNode</c>'s scene objects): a light blocker is
	/// neither opaque nor translucent, so it draws nowhere (it's for baking); a decal draws only in the decal layer and casts
	/// no shadow; a no-shadow object casts none.
	/// </summary>
	static void ApplyTypeFlags( MeshObject obj, uint flags, bool castShadows )
	{
		if ( (flags & ObjectTypeNoShadows) != 0 ) obj.CastShadows = false;

		if ( (flags & ObjectTypeBlockLight) != 0 )
		{
			obj.DrawsOpaque = false;
			obj.DrawsTranslucent = false;
			obj.CastShadows = castShadows;
		}
		else if ( (flags & ObjectTypeDecal) != 0 )
		{
			obj.DrawsOpaque = false;
			obj.DrawsTranslucent = false;
			obj.Decal = true;
			obj.CastShadows = false;
		}
	}

	/// <summary>The compiled materials a mesh draws with, at every LOD: .shader materials and surfaces.</summary>
	static IEnumerable<GpuMaterial> Materials( RenderMesh mesh )
	{
		for ( int lod = 0; lod < mesh.LodCount; lod++ )
		{
			foreach ( var draw in mesh.DrawsForLod( lod ).ToArray() )
			{
				if ( draw.Material is BackendMaterial or SurfaceMaterial ) yield return draw.Material;
			}
		}
	}

	/// <summary>A material's bool attribute: a surface answers as the .shader material it stands in for.</summary>
	static bool Attribute( GpuMaterial material, string name, bool fallback ) => material switch
	{
		BackendMaterial m => m.Attribute( name, fallback ),
		SurfaceMaterial s => s.Attribute( name, fallback ),
		_ => fallback,
	};

	/// <summary>
	/// The flags native's scene system takes from an object's materials (<c>DefaultMaterialBasedSceneObjectUpdater</c>): an
	/// <c>overlay</c> material (static_overlay's decals) puts it in the static overlay layer, in its render order and out of
	/// the prepass and the opaque layers; <c>NoZPrepass</c> keeps it out of the prepass; materials that all have
	/// <c>DoNotCastShadows</c> cast none; <c>bWantsFBCopyTexture</c> asks for the frame buffer copy.
	/// </summary>
	static void ApplyMaterialFlags( MeshObject obj, int renderOrder )
	{
		var materials = Materials( obj.Mesh ).Distinct().ToArray();
		if ( materials.Length == 0 ) return;

		if ( materials.Any( m => Attribute( m, "overlay", false ) ) )
		{
			obj.LayerMatch = LayerMatch.StaticOverlay;
			obj.RenderOrder = renderOrder;
		}

		if ( materials.Any( m => Attribute( m, "NoZPrepass", false ) ) ) obj.DepthPrepass = false;
		if ( materials.All( m => Attribute( m, "DoNotCastShadows", false ) ) ) obj.CastShadows = false;
		if ( materials.Any( m => Attribute( m, "bWantsFBCopyTexture", false ) ) ) obj.WantsFrameBufferCopy = true;
	}

	static Matrix3x4 ToMatrix3x4( NumericsMatrix m ) => new()
	{
		Row0 = new Vector4( m.M11, m.M21, m.M31, m.M41 ),
		Row1 = new Vector4( m.M12, m.M22, m.M32, m.M42 ),
		Row2 = new Vector4( m.M13, m.M23, m.M33, m.M43 ),
	};

	/// <summary>A row-vector object-to-world matrix as a position, rotation and per-axis scale.</summary>
	static Transform ToTransform( NumericsMatrix m )
	{
		var x = new Vector3( m.M11, m.M12, m.M13 );
		var y = new Vector3( m.M21, m.M22, m.M23 );
		var z = new Vector3( m.M31, m.M32, m.M33 );
		var scale = new Vector3( x.Length, y.Length, z.Length );
		if ( Vector3.Dot( Vector3.Cross( x, y ), z ) < 0 ) scale = scale.WithX( -scale.x );
		var rotation = Rotation.LookAt( x / scale.x, z / scale.z );
		return new Transform( new Vector3( m.M41, m.M42, m.M43 ), rotation, scale );
	}

	/// <summary>Whether the map's static geometry casts sun shadows.</summary>
	public void SetCastShadows( bool cast )
	{
		foreach ( var o in meshObjects ) o.CastShadows = cast;
		foreach ( var a in aggregateObjects ) a.CastShadows = cast;
	}

	readonly RenderView view = new() { Name = "Map", FieldOfView = 80, ZNear = 5, ZFar = 30000, ClearColor = new Color( 0.45f, 0.55f, 0.7f ) };

	/// <summary>The shaders' debug view (<c>ToolsVis.hlsl</c>'s modes: 10 albedo, 2 diffuse lighting...); 0 for none.</summary>
	public int ToolsVisMode { get; set; }

	/// <summary>Shader time in seconds for every frame (<see cref="RenderView.Time"/>), so captures repeat; null for the frame's.</summary>
	public float? Time
	{
		get => view.Time;
		set => view.Time = value;
	}

	/// <summary>Size the target: colour and depth with <paramref name="samples"/> samples, as SceneLab's window is.</summary>
	public void SetTarget( int width, int height, int samples )
	{
		Target?.Dispose();
		Target = ViewTarget.Create( new Vector2Int( width, height ), samples, TextureFormat.RGBA16161616F, System.DepthFormat, "Map" );
		view.Viewport = new Rect( 0, 0, width, height );
	}

	/// <summary>Render the world from <paramref name="camera"/> into <see cref="Target"/> (recorded and submitted).</summary>
	public void Render( MapView camera )
	{
		view.Position = new Vector3( camera.Position.X, camera.Position.Y, camera.Position.Z );
		var forward = new Vector3( camera.LookAt.X, camera.LookAt.Y, camera.LookAt.Z ) - view.Position;
		view.Rotation = Rotation.LookAt( forward.Normal );
		view.FieldOfView = camera.FieldOfView;
		view.ZNear = camera.ZNear;
		view.ZFar = camera.ZFar;
		view.ToolsVisMode = ToolsVisMode;

		RenderHost.Current.NextFrame();
		UpdateWater( view );
		System.Render( World, view, Target, Args.Has( "-ssr-normals" ) ? ReflectingCameraStages : CameraStages );
	}

	/// <summary>
	/// A game camera's stages with no commands in them, as SceneLab's camera has: the frame is a camera's, so the sun's
	/// contact shadows draw as the game draws them. With <c>-ssr-normals</c> the camera has screen-space reflections, so the
	/// opaque pass writes their normals and roughness as the game's does, though the lab traces none (no host).
	/// </summary>
	sealed class EmptyCameraStages( bool reflections ) : IRenderStages
	{
		public void Render( RenderStage stage, in StageTarget target ) { }
		public bool RendersInsideFrame( RenderStage stage ) => false;
		public bool HasScreenSpaceReflections => reflections;
	}

	static readonly EmptyCameraStages CameraStages = new( false ), ReflectingCameraStages = new( true );

	/// <summary>The resolved colour and the layout Borealis left it in.</summary>
	public (TextureHandle Texture, ImageLayout Layout) Output => BackendDevice.Unwrap( Target.Samples > 1 ? Target.Resolved : Target.Color );

	/// <summary>Copy the resolved colour into a swap chain image (left ready to present).</summary>
	public void CopyTo( CommandList list, TextureHandle image )
	{
		var (texture, layout) = Output;
		list.Barrier( GpuTextureBarrier.Transition( texture, layout, ImageLayout.CopySource, PipelineStage.AllCommands, MemoryAccess.MemoryWrite, PipelineStage.Transfer, MemoryAccess.TransferRead ) );
		list.Barrier( GpuTextureBarrier.Transition( image, ImageLayout.Undefined, ImageLayout.CopyDestination, PipelineStage.Transfer, MemoryAccess.None, PipelineStage.Transfer, MemoryAccess.TransferWrite ) );
		var info = gpu.InfoOf( texture );
		var target = gpu.InfoOf( image );
		list.Blit( texture, (0, 0, info.Width, info.Height), image, (0, 0, target.Width, target.Height), Filter.Linear );
		list.Barrier( GpuTextureBarrier.Transition( image, ImageLayout.CopyDestination, ImageLayout.Present, PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.BottomOfPipe, MemoryAccess.None ) );
		BackendDevice.SetLayout( Target.Samples > 1 ? Target.Resolved : Target.Color, ImageLayout.CopySource );
	}

	public void Dispose()
	{
		gpu.WaitIdle();
		Target?.Dispose();
		DisposeWater();
		System.Dispose();
		Backend.Dispose();
		RenderHost.Current = previousHost;
		foreach ( var package in packages ) package.Dispose();
	}
}
