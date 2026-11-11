using Basalt;
using Basalt.Backend;
using Basalt.Gpu;
using Basalt.Source2;
using Basalt.Source2.Models;
using Basalt.Source2.Resources;
using Basalt.Source2.World;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using GpuDevice = Basalt.Gpu.Device;
using GpuTextureBarrier = Basalt.Gpu.TextureBarrier;
using NumericsMatrix = System.Numerics.Matrix4x4;
using NumericsVector3 = System.Numerics.Vector3;

namespace Sandbox.BasaltLab.Maps;

/// <summary>
/// A compiled map in a real <see cref="RenderWorld"/>, drawn by Basalt's <see cref="RenderSystem"/> through
/// <see cref="BackendDevice"/> on Basalt.Gpu, with s&amp;box's own compiled shaders and the map's materials, textures and
/// constants as their <c>.vmat_c</c> files have them. Scene objects are <see cref="MeshObject"/>s (LODs, mesh groups and
/// skins as compiled); aggregates are <see cref="AggregateObject"/>s whose fragments draw as one multi-draw indirect.
/// Baked lighting is the map's own where its files can be read: the world's lightmaps for lightmapped draws, and each
/// light probe volume's textures for the objects the compiler gave it (<see cref="WorldSceneObject.LightProbeVolume"/>).
/// The volumes' boxes are in the entity lump, which isn't read, so each is the bounds of its objects. Cubemaps, the sky,
/// fog and entities aren't loaded: what the shaders read of them is the backend's placeholders.
/// </summary>
internal sealed class MapWorld : IDisposable
{
	readonly GpuDevice gpu;
	readonly RenderHost previousHost;
	readonly VpkFile package;

	public BackendDevice Backend { get; }
	public RenderWorld World { get; } = new();
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

	/// <param name="castShadows">Whether the map's static geometry casts sun shadows; Construct's sun is baked, so in the
	/// game its world geometry and static props don't.</param>
	/// <param name="bakedLighting">Light lightmapped draws from the map's lightmaps and the rest from its probe volumes.</param>
	public MapWorld( GpuDevice gpu, string folder, ContentFiles core, bool castShadows, bool aggregatesAsMeshes = false, bool bakedLighting = true )
	{
		this.gpu = gpu;
		var timer = Stopwatch.StartNew();
		void Phase( string name )
		{
			Phases.Add( (name, timer.Elapsed.TotalMilliseconds) );
			timer.Restart();
		}

		var vpk = Directory.GetFiles( Path.Combine( folder, "maps" ), "*.vpk" )
			.Where( x => !Path.GetFileNameWithoutExtension( x ).EndsWith( "_sky", StringComparison.OrdinalIgnoreCase ) )
			.OrderBy( x => x.Length )
			.First();

		package = VpkFile.Open( vpk );
		var content = new ContentFiles( folder );
		foreach ( var root in core.Roots ) content.Add( root );
		content.AddPackage( package );

		Backend = new BackendDevice( gpu, content );
		previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( Backend );
		System = new RenderSystem { ParallelRecording = false };

		World.Lighting.SunDirection = new Vector3( -0.55f, -0.45f, -0.5f ).Normal;
		World.Lighting.SunColor = new Color( 1.25f, 1.15f, 1.0f );
		World.Lighting.AmbientColor = new Color( 0.12f, 0.15f, 0.2f, 1 );
		World.Lighting.SunShadowCascades = 3;

		var world = WorldFile.Load( content, $"maps/{Path.GetFileNameWithoutExtension( vpk )}/world.vwrld" );
		Phase( "World" );

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

		const ulong UnsetMask = 0xD0D0D0D0D0D0D0D0;
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

				var mask = o.MeshGroupMask == UnsetMask ? file.DefaultMeshGroupMask : o.MeshGroupMask;
				var mesh = Mesh( o.Model, file, mask, skin );
				var obj = new MeshObject( mesh, ToTransform( o.Transform ) ) { CastShadows = castShadows };
				if ( o.Tint != global::System.Numerics.Vector4.One ) obj.Tint = new Color( o.Tint.X, o.Tint.Y, o.Tint.Z, o.Tint.W );
				World.Add( obj );
				meshObjects.Add( obj );
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
						if ( fragment.DrawCall < 0 || fragment.DrawCall >= calls.Count ) continue;
						var (fm, fc) = calls[fragment.DrawCall];
						var draw = whole.DrawsForLod( 0 ).ToArray().FirstOrDefault( d => d.Mesh == fm && d.DrawCall == fc );
						if ( draw.Material is null ) continue;
						var bounds = new BBox( new Vector3( fragment.Mins.X, fragment.Mins.Y, fragment.Mins.Z ), new Vector3( fragment.Maxs.X, fragment.Maxs.Y, fragment.Maxs.Z ) );
						var obj = new MeshObject( new RenderMesh( whole.Geometry, bounds, [[draw]], [] ), Transform.Zero ) { CastShadows = castShadows };
						World.Add( obj );
						meshObjects.Add( obj );
						Fragments++;
					}

					continue;
				}

				foreach ( var aggregate in MakeAggregates( Mesh( a.Model, file, file.DefaultMeshGroupMask, 0 ), file, a ) )
				{
					aggregate.CastShadows = castShadows;
					World.Add( aggregate );
					aggregateObjects.Add( aggregate );
					Aggregates++;
					Fragments += aggregate.Fragments.Length;
				}
			}
		}

		Phase( $"World objects ({MeshObjects} objects, {Aggregates} aggregates, {Fragments} fragments, {Models} meshes)" );

		if ( !bakedLighting ) return;
		LoadLightmaps( world );
		LoadProbeVolumes( Path.GetFileNameWithoutExtension( vpk ), probed );
		Phase( $"Baked lighting ({LightmapTextures} lightmaps for {LightmappedObjects} objects and aggregates, {ProbeVolumes} probe volumes for {ProbeObjects} objects)" );
	}

	/// <summary>
	/// The world's lightmaps, as native's <c>SetSceneObjectLightmapAttributes</c> binds them: one set of attributes for every
	/// aggregate and every object with lightmapped draws, with the textures by their base names, their bindless indices
	/// and the uv scale in <c>BakedLightingConstantBuffer_t</c>, and the lightmap combo.
	/// </summary>
	void LoadLightmaps( WorldFile world )
	{
		var textures = new List<(string Name, GpuTexture Texture)>();
		foreach ( var path in world.Lightmaps )
		{
			var name = Path.GetFileNameWithoutExtension( path );
			if ( name == "debug_chart_color" ) continue;
			if ( Backend.LoadTexture( path ) is BackendTexture { Dimension: TextureDimension.Texture2DArray } texture ) textures.Add( (name, texture) );
			else Problems.Add( $"lightmap {path} isn't readable" );
		}

		if ( textures.Count == 0 ) return;
		LightmapTextures = textures.Count;

		var scale = world.LightmapUvScale;
		var constants = new LightmapConstants { UvScale = new Vector4( scale.X > 0 ? scale.X : 1, scale.Y > 0 ? scale.Y : 1, 0, 0 ) };
		var attributes = Backend.CreateParameters();
		for ( int i = 0; i < textures.Count && i < 8; i++ )
		{
			attributes.Set( textures[i].Name, textures[i].Texture );
			constants.Indices[i] = textures[i].Texture.BindlessIndex;
		}

		attributes.SetData( "BakedLightingConstantBuffer_t", constants );
		attributes.Set( "UsesBakedLightmaps", true );
		attributes.Set( "UsesBakedLightingFromProbe", false );
		attributes.SetCombo( "D_BAKED_LIGHTING_FROM_PROBE", 0 );
		attributes.SetCombo( "D_BAKED_LIGHTING_FROM_LIGHTMAP", 1 );

		foreach ( var aggregate in aggregateObjects )
		{
			aggregate.Lightmapped = true;
			aggregate.LightmapAttributes = attributes;
			LightmappedObjects++;
		}

		foreach ( var obj in meshObjects )
		{
			if ( obj.Mesh is not { HasLightmappedDraws: true } ) continue;
			obj.LightmapAttributes = attributes;
			LightmappedObjects++;
		}
	}

	/// <summary>
	/// A light probe volume per volume the compiler chose for objects, with its textures (the probe, direct light indices
	/// and strengths) and its constants as <c>CSceneLightProbeVolumeObject::CreateConstants</c> makes them, over the bounds
	/// of its objects, which need a probe.
	/// </summary>
	void LoadProbeVolumes( string map, List<(MeshObject Object, int Volume, NumericsVector3 Mins, NumericsVector3 Maxs)> probed )
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
			var size = maxs - mins;
			var info = gpu.InfoOf( probe.Handle );
			var half = new NumericsVector3( 0.5f / info.Width, 0.5f / info.Height, 0.5f / info.Depth );
			const int Layers = 6;

			var constants = new ProbeConstants
			{
				Row0 = new Vector4( 1 / size.X, 0, 0, -mins.X / size.X ),
				Row1 = new Vector4( 0, 1 / size.Y, 0, -mins.Y / size.Y ),
				Row2 = new Vector4( 0, 0, 1 / size.Z, -mins.Z / size.Z ),
				TextureMin = new Vector4( half.X, half.Y, half.Z, 0 ),
				TextureMax = new Vector4( 1 - half.X, 1 - half.Y, 1f / Layers - half.Z, 0 ),
				Colour = new Vector4( 1, 1, 1, 0 ),
				Probe = probe.BindlessIndex,
				Indices = indices.BindlessIndex,
				Scalars = scalars.BindlessIndex,
			};

			var attributes = Backend.CreateParameters();
			attributes.SetData( "PerBatchLightProbeVolumeConstantBuffer_t", constants );
			attributes.Set( "UsesBakedLightmaps", false );
			attributes.Set( "UsesBakedLightingFromProbe", true );
			attributes.SetCombo( "D_BAKED_LIGHTING_FROM_PROBE", 1 );
			attributes.SetCombo( "D_BAKED_LIGHTING_FROM_LIGHTMAP", 0 );

			World.LightProbeVolumes.Add( new global::Basalt.LightProbeVolume
			{
				BoxMins = new Vector3( mins.X, mins.Y, mins.Z ),
				BoxMaxs = new Vector3( maxs.X, maxs.Y, maxs.Z ),
				LightGroups = [0],
				Attributes = attributes,
			} );
			ProbeVolumes++;

			foreach ( var (obj, _, _, _) in group )
			{
				obj.NeedsLightProbe = true;
				ProbeObjects++;
			}
		}
	}

	/// <summary>Eight lightmap bindless indices (six used), as two int4s.</summary>
	[System.Runtime.CompilerServices.InlineArray( 8 )]
	struct LightmapIndices
	{
		int element;
	}

	/// <summary><c>BakedLightingConstantBuffer_t</c>: the uv scale and the lightmaps' bindless indices.</summary>
	struct LightmapConstants
	{
		public Vector4 UvScale;
		public LightmapIndices Indices;
	}

	/// <summary><c>PerBatchLightProbeVolumeConstantBuffer_t</c>.</summary>
	struct ProbeConstants
	{
		public Vector4 Row0, Row1, Row2;
		public Vector4 TextureMin, TextureMax, Colour;
		public int Probe, Indices, Scalars;
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
	/// An aggregate's draws as <see cref="AggregateObject"/>s: one per run of draws sharing a vertex and an index buffer (in a
	/// compiled map that's all of them), each fragment one draw call over the buffers its first draw binds, in world space.
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
			if ( fragment.DrawCall < 0 || fragment.DrawCall >= calls.Count ) continue;
			var (m, c) = calls[fragment.DrawCall];
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
				Args = new Basalt.GpuBuffer.IndirectDrawIndexedArguments
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

	/// <summary>Size the target: colour and depth with <paramref name="samples"/> samples, as SceneLab's window is.</summary>
	public void SetTarget( int width, int height, int samples )
	{
		Target?.Dispose();
		Target = ViewTarget.Create( new Vector2Int( width, height ), samples, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Map" );
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

		RenderHost.Current.NextFrame();
		System.Render( World, view, Target );
	}

	/// <summary>The resolved colour and the layout Basalt left it in.</summary>
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
		System.Dispose();
		Backend.Dispose();
		RenderHost.Current = previousHost;
		package.Dispose();
	}
}
