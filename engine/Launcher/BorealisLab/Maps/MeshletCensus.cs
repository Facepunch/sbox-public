using Borealis;
using Borealis.Backend;
using Borealis.Culling;
using Borealis.Source2.Models;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GpuCullMode = Borealis.Gpu.CullMode;
using M4 = System.Numerics.Matrix4x4;
using V3 = System.Numerics.Vector3;
using V4 = System.Numerics.Vector4;
using VertexField = Borealis.Source2.Models.VertexField;

namespace Sandbox.BorealisLab.Maps;

/// <summary>
/// <c>-real -meshlet-census folder</c>: what culling meshlets would take out of a view's depth prepass. After the map has
/// drawn, every opaque draw the main view and its 3D skybox make is found again on the CPU (the same frustum, size and
/// fragment culls, at the LOD each object drew at), each triangle is sorted into off screen, back facing, covering no
/// sample, hidden (covers a sample, loses the depth test) and visible, from a CPU depth buffer at the frame's samples.
/// Then each draw's index range is cut into meshlets by meshoptimizer (<c>-meshopt-dll path</c>), and each meshlet is
/// tested as a GPU cull would: its box against the frustum, its normal cone against the camera, and its box's screen
/// bounds against the sample grid. Writes <c>meshlets.md</c>.
/// </summary>
internal static unsafe class MeshletCensus
{
	enum Layer { Opaque, OpaqueNoPrepass, Overlay, Translucent, Decal }

	// Triangle classes
	const byte Offscreen = 0, Back = 1, NoSample = 2, Hidden = 3, Visible = 4;
	static readonly string[] ClassNames = ["off screen", "back facing", "no sample", "hidden", "visible"];

	sealed class Draw
	{
		public bool Sky;
		public Layer Layer;
		public string Source;
		public bool AlphaTest, TwoSided;
		public MeshVertexBuffer Vertices;
		public VertexField Position;
		public MeshIndexBuffer Indices;
		public int VertexBase, IndexBase, IndexCount;
		public M4 ToWorld, ToClip;
		public ViewSetup View;
		public float Distance;
		public byte[] Classes;
		public int Triangles => IndexCount / 3;
		public (MeshVertexBuffer, int, MeshIndexBuffer, int, int, int) Key => (Vertices, Position.Offset, Indices, VertexBase, IndexBase, IndexCount);
	}

	sealed class ViewSetup
	{
		public RenderView View;
		public M4 WorldToClip;
		public V3 Camera;
		public float TanHalfFov;
	}

	/// <summary>One way of cutting meshes into meshlets.</summary>
	readonly record struct Config( int Vertices, int Triangles, float ConeWeight, bool Scan = false )
	{
		public override string ToString() => Scan ? FormattableString.Invariant( $"{Vertices}v/{Triangles}t scan" ) : FormattableString.Invariant( $"{Vertices}v/{Triangles}t cone {ConeWeight:0.##}" );
	}

	/// <summary>A meshlet: bounds in the mesh's space and its triangles as indices into the draw's triangle order.</summary>
	struct Meshlet
	{
		public V3 Min, Max;
		public V3 Apex, Axis;
		public float Cutoff;
		public int First, Count, VertexCount;
	}

	sealed class Meshlets
	{
		public Meshlet[] List;
		public int[] Triangles;
	}

	static float[] SampleX = [0.5f], SampleY = [0.5f];

	public static void Run( MapWorld map, MapView camera, int width, int height, int samples, string output, Action render )
	{
		Directory.CreateDirectory( output );
		var report = new StringBuilder();
		void Line( string s = "" )
		{
			report.AppendLine( s );
			Console.WriteLine( s );
		}

		SampleX = samples == 4 ? [0.375f, 0.875f, 0.125f, 0.625f] : [0.5f];
		SampleY = samples == 4 ? [0.125f, 0.375f, 0.625f, 0.875f] : [0.5f];
		if ( samples != 1 && samples != 4 ) Console.WriteLine( $"Meshlet census: {samples}x isn't modelled; counting at one sample" );
		samples = SampleX.Length;

		LoadMeshopt();
		var configs = (Args.String( "-meshlet-configs" ) ?? "64x124@0.25,64x124@scan,128x124@0.25,64x64@0.25,32x32@0.25").Split( ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries ).Select( ParseConfig ).ToArray();

		// Classic frames, so materials get the objects that say how they cull and meshes pick their LOD on the CPU
		var gpuDriven = map.Backend.GpuDriven;
		map.Backend.GpuDriven = false;
		for ( int i = 0; i < 3; i++ ) render();
		map.Backend.GpuDriven = gpuDriven;
		var cull = new Dictionary<GpuMaterial, GpuCullMode>();
		foreach ( var (material, layout, depthOnly) in map.Backend.MaterialCache.Describe() )
		{
			if ( material is null || depthOnly ) continue;
			cull[material] = layout.Variant.State.Cull;
		}

		var main = MakeView( camera, width, height );
		ViewSetup sky = null;
		if ( map.World.Skybox3D is { } skybox && map.System.Draw3DSkybox )
		{
			var view = new RenderView { Name = "3D Skybox" };
			RenderSystem.FillSkyboxView( view, main.View, skybox );
			view.Update();
			sky = Setup( view );
		}

		var draws = new List<Draw>();
		var unknownCull = new HashSet<GpuMaterial>();
		Collect( map.World, main, false, draws, cull, unknownCull, 0 );
		if ( sky is not null ) Collect( map.World.Skybox3D.World, sky, true, draws, cull, unknownCull, 0 );
		draws.RemoveAll( d => d.Layer is not (Layer.Opaque or Layer.OpaqueNoPrepass) );

		Line( $"# Meshlet census: {camera.Name}, {width}x{height}, {samples} sample{(samples > 1 ? "s" : "")} a pixel" );
		Line();
		Census( "main view and 3D skybox", draws, width, height, configs, Line );

		// -meshlet-census-sun size,...: sun views, orthographic along the sun at 2048x2048 one sample, centred where the
		// camera looks, each the given width in world units (an approximation of a cascade: every opaque draw as if it cast, texel-sized cull)
		foreach ( var size in (Args.String( "-meshlet-census-sun" ) ?? "").Split( ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries ).Select( x => float.Parse( x, CultureInfo.InvariantCulture ) ) )
		{
			const int SunResolution = 2048;
			var sunDirection = V3.Normalize( (V3)map.World.Lighting.SunDirection );
			var target = new V3( camera.LookAt.X, camera.LookAt.Y, camera.LookAt.Z );
			var sunView = new RenderView { Name = "Sun", Orthographic = true, OrthoSize = new Vector2( size, size ), ZNear = 10, ZFar = 200000, SizeCullThreshold = 0, Viewport = new Rect( 0, 0, SunResolution, SunResolution ) };
			sunView.Position = (Vector3)(target - sunDirection * 100000);
			sunView.Rotation = Rotation.LookAt( (Vector3)sunDirection );
			sunView.Update();
			var sun = Setup( sunView );
			var sunDraws = new List<Draw>();
			var (sampleX, sampleY) = (SampleX, SampleY);
			SampleX = [0.5f];
			SampleY = [0.5f];
			Collect( map.World, sun, false, sunDraws, cull, unknownCull, size / SunResolution * 0.5f );
			sunDraws.RemoveAll( d => d.Layer is not (Layer.Opaque or Layer.OpaqueNoPrepass) );
			Line();
			Line( FormattableString.Invariant( $"# Sun view {size:0} units wide, {SunResolution}x{SunResolution}, one sample (sun direction {sunDirection.X:0.00} {sunDirection.Y:0.00} {sunDirection.Z:0.00})" ) );
			Census( FormattableString.Invariant( $"sun {size:0}" ), sunDraws, SunResolution, SunResolution, configs, Line );
			(SampleX, SampleY) = (sampleX, sampleY);
		}

		Line();
		Line( $"{unknownCull.Count} materials had no object to say how they cull (taken as culling back faces)." );

		Line();
		Line( "## Load cost: every opaque draw range of the map (both worlds, every LOD), cut once" );
		Line();
		LoadCost( map, configs, Line );

		File.WriteAllText( Path.Combine( output, "meshlets.md" ), report.ToString() );
		Console.WriteLine( $"Meshlet census in {output}" );
	}

	static void Census( string title, List<Draw> draws, int width, int height, Config[] configs, Action<string> Line )
	{
		Line( "" );
		Line( $"{draws.Count} opaque draws ({draws.Count( d => !d.Sky )} main, {draws.Count( d => d.Sky )} 3D skybox), {draws.Sum( d => (long)d.Triangles ) / 1e6:0.000}M triangles; {draws.Count( d => d.TwoSided )} two-sided ({draws.Where( d => d.TwoSided ).Sum( d => (long)d.Triangles ) / 1e6:0.000}M triangles)" );

		var sw = Stopwatch.StartNew();
		Parallel.ForEach( draws, d => Classify( d, width, height ) );
		RasterizeVisibility( draws, width, height );
		Console.WriteLine( $"Classified in {sw.Elapsed.TotalSeconds:0.0} s" );

		Line( "" );
		Line( $"## Triangles, {title} (prepass: every opaque and alpha-tested draw; alpha test as geometric coverage)" );
		Line( "" );
		Line( "| View | Source | Draws | Triangles | Off screen | Back facing | No sample | Hidden | Visible |" );
		Line( "|---|---|---|---|---|---|---|---|---|" );
		foreach ( var isSky in new[] { false, true } )
		{
			var of = draws.Where( d => d.Sky == isSky ).ToList();
			if ( of.Count == 0 ) continue;
			foreach ( var g in of.GroupBy( d => d.Source ).OrderByDescending( g => g.Sum( d => (long)d.Triangles ) ) ) Line( TriangleRow( isSky ? "sky" : "main", g.Key, g.ToList() ) );
			Line( TriangleRow( isSky ? "sky" : "main", "**all**", of ) );
		}

		Line( TriangleRow( "both", "**all**", draws ) );

		// Meshlets: each unique draw range cut once per config
		var unique = draws.GroupBy( d => d.Key ).Select( g => g.First() ).ToList();
		foreach ( var config in configs )
		{
			sw.Restart();
			var built = new System.Collections.Concurrent.ConcurrentDictionary<(MeshVertexBuffer, int, MeshIndexBuffer, int, int, int), Meshlets>();
			Parallel.ForEach( unique, d => built[d.Key] = Build( d.Vertices, d.Position, d.Indices, d.VertexBase, d.IndexBase, d.IndexCount, config, mapTriangles: true ) );
			var buildSeconds = sw.Elapsed.TotalSeconds;
			var results = new Result[draws.Count];
			Parallel.For( 0, draws.Count, i => results[i] = Test( draws[i], built[draws[i].Key], draws[i].View, width, height ) );

			Line( "" );
			Line( $"## Meshlets {config}, {title} (built for {unique.Count} unique draw ranges in {buildSeconds:0.00} s on all cores)" );
			Line( "" );
			Line( "Culled by: frustum (meshlet box in clip space), cone (normal cone against the camera, apex form; not for two-sided draws), small (the box's screen bounds hold no sample). Ideal: meshlets whose every triangle is off screen, back facing or covers no sample." );
			Line( "" );
			Line( "| View | Source | Meshlets | Tris/meshlet | Verts/meshlet | Frustum | Cone | Small | Any | Ideal | Kept tris | Kept: off / back / no sample / hidden / visible | Kept meshlet verts | Cone culled a front triangle | Kept meshlets | Runs of kept meshlets (draws if each run were one) |" );
			Line( "|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|" );
			foreach ( var isSky in new[] { false, true } )
			{
				var of = Enumerable.Range( 0, draws.Count ).Where( i => draws[i].Sky == isSky ).ToList();
				if ( of.Count == 0 ) continue;
				foreach ( var g in of.GroupBy( i => draws[i].Source ).OrderByDescending( g => g.Sum( i => (long)draws[i].Triangles ) ) ) Line( MeshletRow( isSky ? "sky" : "main", g.Key, g.Select( i => results[i] ) ) );
				Line( MeshletRow( isSky ? "sky" : "main", "**all**", of.Select( i => results[i] ) ) );
			}

			Line( MeshletRow( "both", "**all**", results ) );
		}
	}

	static Config ParseConfig( string s )
	{
		var at = s.Split( '@' );
		var vt = at[0].Split( 'x' );
		var scan = at.Length > 1 && at[1] == "scan";
		return new Config( int.Parse( vt[0], CultureInfo.InvariantCulture ), int.Parse( vt[1], CultureInfo.InvariantCulture ), at.Length > 1 && !scan ? float.Parse( at[1], CultureInfo.InvariantCulture ) : 0.25f, scan );
	}

	static string Pct( long part, long whole ) => whole == 0 ? "-" : FormattableString.Invariant( $"{100.0 * part / whole:0.0}%" );

	static string TriangleRow( string view, string source, List<Draw> g )
	{
		var counts = new long[5];
		foreach ( var d in g ) foreach ( var c in d.Classes.AsSpan( 0, d.Triangles ) ) counts[c]++;
		long total = counts.Sum();
		return FormattableString.Invariant( $"| {view} | {source} | {g.Count} | {total:N0} | {Pct( counts[0], total )} | {Pct( counts[1], total )} | {Pct( counts[2], total )} | {Pct( counts[3], total )} | {Pct( counts[4], total )} |" );
	}

	sealed class Result
	{
		public long Triangles, Meshlets, MeshletVertices;
		public long Frustum, Cone, Small, Any, Ideal, KeptVertices, ConeFront, KeptMeshlets, Runs;
		public readonly long[] Kept = new long[5];
	}

	static string MeshletRow( string view, string source, IEnumerable<Result> results )
	{
		var r = new Result();
		foreach ( var x in results )
		{
			r.Triangles += x.Triangles;
			r.Meshlets += x.Meshlets;
			r.MeshletVertices += x.MeshletVertices;
			r.Frustum += x.Frustum;
			r.Cone += x.Cone;
			r.Small += x.Small;
			r.Any += x.Any;
			r.Ideal += x.Ideal;
			r.KeptVertices += x.KeptVertices;
			r.ConeFront += x.ConeFront;
			r.KeptMeshlets += x.KeptMeshlets;
			r.Runs += x.Runs;
			for ( int i = 0; i < 5; i++ ) r.Kept[i] += x.Kept[i];
		}

		var kept = r.Triangles - r.Any;
		return FormattableString.Invariant( $"| {view} | {source} | {r.Meshlets:N0} | {r.Triangles / (double)Math.Max( r.Meshlets, 1 ):0.0} | {r.MeshletVertices / (double)Math.Max( r.Meshlets, 1 ):0.0} | {Pct( r.Frustum, r.Triangles )} | {Pct( r.Cone, r.Triangles )} | {Pct( r.Small, r.Triangles )} | {Pct( r.Any, r.Triangles )} | {Pct( r.Ideal, r.Triangles )} | {kept:N0} ({Pct( kept, r.Triangles )}) | {Pct( r.Kept[0], kept )} / {Pct( r.Kept[1], kept )} / {Pct( r.Kept[2], kept )} / {Pct( r.Kept[3], kept )} / {Pct( r.Kept[4], kept )} | {r.KeptVertices:N0} of {r.MeshletVertices:N0} | {r.ConeFront:N0} | {r.KeptMeshlets:N0} | {r.Runs:N0} |" );
	}

	static ViewSetup MakeView( MapView camera, int width, int height )
	{
		var view = new RenderView { Name = "Map", FieldOfView = camera.FieldOfView, ZNear = camera.ZNear, ZFar = camera.ZFar, Viewport = new Rect( 0, 0, width, height ) };
		view.Position = new Vector3( camera.Position.X, camera.Position.Y, camera.Position.Z );
		var forward = new Vector3( camera.LookAt.X, camera.LookAt.Y, camera.LookAt.Z ) - view.Position;
		view.Rotation = Rotation.LookAt( forward.Normal );
		view.Update();
		return Setup( view );
	}

	static ViewSetup Setup( RenderView view ) => new()
	{
		View = view,
		WorldToClip = view.WorldToProjection,
		Camera = (V3)view.Position,
		TanHalfFov = MathF.Tan( view.FieldOfView * MathF.PI / 360f ),
	};

	/// <summary>Every draw the view makes, as Borealis's culling finds them.</summary>
	static void Collect( RenderWorld world, ViewSetup v, bool isSky, List<Draw> draws, Dictionary<GpuMaterial, GpuCullMode> cull, HashSet<GpuMaterial> unknownCull, float minRadius )
	{
		var view = v.View;
		var frustum = view.Frustum;
		var size = new SizeCull( view );
		var objects = world.Objects;
		var centers = world.BoundsCenter;
		var extents = world.BoundsExtents;
		var toWorld = world.LocalToWorld;
		var camera = v.Camera;

		for ( int i = 0; i < objects.Length; i++ )
		{
			if ( !frustum.Intersects( centers[i], extents[i] ) ) continue;
			var obj = objects[i];
			var distance = V3.Distance( camera, centers[i] );

			if ( obj is MeshObject mo && mo.Mesh is { } mesh && mesh.Geometry is BackendModel model )
			{
				if ( size.Culls( centers[i], extents[i] ) || ((V3)extents[i]).Length() < minRadius ) continue;
				var lod = Math.Clamp( mo.DrawnLod, 0, mesh.LodCount - 1 );
				var local = (M4)toWorld[i];
				foreach ( ref readonly var draw in mesh.DrawsForLod( lod ) )
				{
					if ( draw.PrimitiveType != (int)PrimitiveKind.Triangles || draw.IndexCount == 0 ) continue;
					Layer layer;
					if ( mo.Decal ) layer = Layer.Decal;
					else if ( mo.LayerMatch == LayerMatch.StaticOverlay ) layer = Layer.Overlay;
					else if ( draw.Translucent ) layer = Layer.Translucent;
					else { if ( !mo.DrawsOpaque ) continue; layer = mo.DepthPrepass ? Layer.Opaque : Layer.OpaqueNoPrepass; }

					var d = Make( model, draw.Mesh, draw.DrawCall, draw.Material, cull, unknownCull );
					if ( d is null ) continue;
					d.Sky = isSky;
					d.Layer = layer;
					d.Source = SourceOf( isSky, "prop", draw.AlphaTest );
					d.AlphaTest = draw.AlphaTest;
					d.IndexBase += draw.StartIndex;
					d.IndexCount = draw.IndexCount;
					d.ToWorld = local;
					d.ToClip = local * v.WorldToClip;
					d.View = v;
					d.Distance = distance;
					draws.Add( d );
				}
			}
			else if ( obj is AggregateObject ag && ag.Mesh.Geometry is BackendModel agModel )
			{
				var first = ag.Mesh.DrawsForLod( 0 );
				if ( first.Length == 0 ) continue;
				var draw0 = first[0];
				var overlay = ag.LayerMatch == LayerMatch.StaticOverlay;
				var aggregateCulled = size.Culls( centers[i], extents[i] ) || ((V3)extents[i]).Length() < minRadius;
				var fragments = ag.Fragments;
				var fc = ag.Centers;
				var fe = ag.Extents;
				for ( int f = 0; f < fragments.Length; f++ )
				{
					if ( !frustum.Intersects( fc[f], fe[f] ) ) continue;
					if ( aggregateCulled || size.Culls( fc[f], fe[f] ) || ((V3)fe[f]).Length() < minRadius ) continue;

					var d = Make( agModel, draw0.Mesh, draw0.DrawCall, draw0.Material, cull, unknownCull );
					if ( d is null ) continue;
					ref readonly var fragment = ref fragments[f];
					var row = fragment.Transform;
					var local = new M4( (float)row.Row0.x, (float)row.Row1.x, (float)row.Row2.x, 0, (float)row.Row0.y, (float)row.Row1.y, (float)row.Row2.y, 0,
						(float)row.Row0.z, (float)row.Row1.z, (float)row.Row2.z, 0, (float)row.Row0.w, (float)row.Row1.w, (float)row.Row2.w, 1 );
					d.Sky = isSky;
					d.Layer = overlay ? Layer.Overlay : Layer.Opaque;
					d.Source = SourceOf( isSky, "aggregate", draw0.AlphaTest );
					d.AlphaTest = draw0.AlphaTest;

					// Indirect args are relative to what the aggregate's first draw binds
					d.VertexBase += fragment.Args.BaseVertex - agModel.File.Meshes[draw0.Mesh].DrawCalls[draw0.DrawCall].BaseVertex;
					d.IndexBase += (int)fragment.Args.FirstIndex;
					d.IndexCount = (int)fragment.Args.IndexCount;
					d.ToWorld = local;
					d.ToClip = local * v.WorldToClip;
					d.View = v;
					d.Distance = V3.Distance( camera, fc[f] );
					draws.Add( d );
				}
			}
		}
	}

	static string SourceOf( bool sky, string kind, bool alphaTest ) => alphaTest ? $"{kind}, alpha tested" : kind;

	static Draw Make( BackendModel model, int mesh, int call, GpuMaterial material, Dictionary<GpuMaterial, GpuCullMode> cull, HashSet<GpuMaterial> unknownCull )
	{
		var m = model.File.Meshes[mesh];
		var c = m.DrawCalls[call];
		if ( c.VertexBuffers.Count == 0 ) return null;
		var (vbIndex, vbOffset) = c.VertexBuffers[0];
		var vb = m.VertexBuffers[vbIndex];
		if ( vb.Find( "POSITION" ) is not { } position ) return null;
		var (ibIndex, ibOffset) = c.IndexBuffer;
		var ib = m.IndexBuffers[ibIndex];
		var known = cull.TryGetValue( material, out var mode );
		if ( !known && material is SurfaceMaterial surface )
		{
			mode = surface.RenderState.TwoSided ? GpuCullMode.None : GpuCullMode.Back;
			known = true;
		}

		if ( !known && material is BackendMaterial { Library: { } library, Shader: not null } backend )
		{
			lock ( cull )
			{
				known = cull.TryGetValue( material, out mode );
				if ( !known )
				{
					try
					{
						if ( backend.Variant( library, "Forward", "Forward", static _ => null, static _ => null )?.State is { } state )
						{
							mode = state.Cull;
							known = true;
						}
					}
					catch ( InvalidDataException )
					{
					}

					if ( known ) cull[material] = mode;
				}
			}
		}

		if ( !known && material is not null ) lock ( unknownCull ) unknownCull.Add( material );
		return new Draw
		{
			TwoSided = known && mode == GpuCullMode.None,
			Vertices = vb,
			Position = position,
			Indices = ib,
			VertexBase = vbOffset / vb.Stride + c.BaseVertex,
			IndexBase = ibOffset / ib.Size,
		};
	}

	[ThreadStatic] static V4[] clipScratch;

	static V4[] ClipVertices( Draw d, out int min )
	{
		min = int.MaxValue;
		var max = int.MinValue;
		for ( int k = 0; k < d.IndexCount; k++ )
		{
			var index = (int)d.Indices[d.IndexBase + k];
			if ( index < min ) min = index;
			if ( index > max ) max = index;
		}

		var count = max - min + 1;
		if ( clipScratch is null || clipScratch.Length < count ) clipScratch = new V4[Math.Max( count, 1024 )];
		for ( int k = 0; k < count; k++ )
		{
			var p = d.Vertices.Read( d.Position, d.VertexBase + min + k );
			clipScratch[k] = V4.Transform( new V4( p.X, p.Y, p.Z, 1 ), d.ToClip );
		}

		return clipScratch;
	}

	/// <summary>Off screen, back facing, no sample, or Hidden for a front-facing triangle covering a sample (made Visible by the raster).</summary>
	static void Classify( Draw d, int width, int height )
	{
		var clip = ClipVertices( d, out var min );
		d.Classes = new byte[d.Triangles];
		for ( int t = 0; t < d.Triangles; t++ )
		{
			var a = clip[(int)d.Indices[d.IndexBase + t * 3] - min];
			var b = clip[(int)d.Indices[d.IndexBase + t * 3 + 1] - min];
			var c = clip[(int)d.Indices[d.IndexBase + t * 3 + 2] - min];
			d.Classes[t] = ClassOf( a, b, c, d.TwoSided, width, height );
		}
	}

	static byte ClassOf( V4 a, V4 b, V4 c, bool twoSided, int width, int height )
	{
		if ( Outside( a, b, c ) ) return Offscreen;

		// Crossing the near plane: the rasterizer clips it; count it as covering a sample
		if ( a.W <= 1e-4f || b.W <= 1e-4f || c.W <= 1e-4f || a.Z > a.W || b.Z > b.W || c.Z > c.W ) return Hidden;
		var (ax, ay) = Screen( a, width, height );
		var (bx, by) = Screen( b, width, height );
		var (cx, cy) = Screen( c, width, height );
		var area = Area( ax, ay, bx, by, cx, cy );
		if ( area == 0 ) return NoSample;
		if ( area < 0 && !twoSided ) return Back;
		return CoversSample( ax, ay, bx, by, cx, cy, width, height ) ? Hidden : NoSample;
	}

	/// <summary>Every vertex on the outside of one clip plane (reverse Z: depth runs from w at the near plane to 0 at the far).</summary>
	static bool Outside( V4 a, V4 b, V4 c )
	{
		if ( a.X < -a.W && b.X < -b.W && c.X < -c.W ) return true;
		if ( a.X > a.W && b.X > b.W && c.X > c.W ) return true;
		if ( a.Y < -a.W && b.Y < -b.W && c.Y < -c.W ) return true;
		if ( a.Y > a.W && b.Y > b.W && c.Y > c.W ) return true;
		if ( a.Z < 0 && b.Z < 0 && c.Z < 0 ) return true;
		if ( a.Z > a.W && b.Z > b.W && c.Z > c.W ) return true;
		return false;
	}

	/// <summary>
	/// Framebuffer position. s&amp;box's vertex shaders negate clip-space y (<c>sbox/vertex.slangh</c>) and the viewport's height
	/// is positive, so clip y up is framebuffer y down; winding is decided after that flip.
	/// </summary>
	static (double X, double Y) Screen( V4 v, int width, int height ) => ((v.X / v.W * 0.5 + 0.5) * width, (0.5 - v.Y / v.W * 0.5) * height);

	/// <summary>Vulkan's signed area: positive is counter-clockwise, which is front facing for s&amp;box's shaders.</summary>
	static double Area( double ax, double ay, double bx, double by, double cx, double cy ) => -0.5 * ((ax * by - bx * ay) + (bx * cy - cx * by) + (cx * ay - ax * cy));

	static bool CoversSample( double ax, double ay, double bx, double by, double cx, double cy, int width, int height )
	{
		var x0 = Math.Max( (int)Math.Floor( Math.Min( ax, Math.Min( bx, cx ) ) ), 0 );
		var x1 = Math.Min( (int)Math.Floor( Math.Max( ax, Math.Max( bx, cx ) ) ), width - 1 );
		var y0 = Math.Max( (int)Math.Floor( Math.Min( ay, Math.Min( by, cy ) ) ), 0 );
		var y1 = Math.Min( (int)Math.Floor( Math.Max( ay, Math.Max( by, cy ) ) ), height - 1 );
		if ( x0 > x1 || y0 > y1 ) return false;
		if ( (long)(x1 - x0 + 1) * (y1 - y0 + 1) > 64 ) return true;
		var e = new Edges( ax, ay, bx, by, cx, cy );
		for ( int y = y0; y <= y1; y++ )
			for ( int x = x0; x <= x1; x++ )
				for ( int s = 0; s < SampleX.Length; s++ )
					if ( e.Inside( x + SampleX[s], y + SampleY[s], out _, out _, out _ ) ) return true;
		return false;
	}

	readonly struct Edges
	{
		readonly double ax, ay, bx, by, cx, cy;
		readonly bool t0, t1, t2;
		public readonly double Area2;
		public readonly bool Swapped;

		public Edges( double ax, double ay, double bx, double by, double cx, double cy )
		{
			var area2 = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
			Swapped = area2 < 0;
			if ( Swapped )
			{
				(bx, cx) = (cx, bx);
				(by, cy) = (cy, by);
				area2 = -area2;
			}

			(this.ax, this.ay, this.bx, this.by, this.cx, this.cy) = (ax, ay, bx, by, cx, cy);
			Area2 = area2;
			t0 = TopLeft( bx, by, cx, cy );
			t1 = TopLeft( cx, cy, ax, ay );
			t2 = TopLeft( ax, ay, bx, by );
		}

		static bool TopLeft( double x0, double y0, double x1, double y1 ) => y1 < y0 || (y1 == y0 && x1 > x0);

		static double E( double x0, double y0, double x1, double y1, double px, double py ) => (x1 - x0) * (py - y0) - (y1 - y0) * (px - x0);

		public bool Inside( double px, double py, out double w0, out double w1, out double w2 )
		{
			w0 = E( bx, by, cx, cy, px, py );
			w1 = E( cx, cy, ax, ay, px, py );
			w2 = E( ax, ay, bx, by, px, py );
			return (w0 > 0 || (w0 == 0 && t0)) && (w1 > 0 || (w1 == 0 && t1)) && (w2 > 0 || (w2 == 0 && t2));
		}
	}

	/// <summary>
	/// The prepass into a CPU depth buffer at the frame's samples (main world, then the 3D skybox into its own), then each
	/// covering triangle that has a sample equal to the finished depth is Visible (the skybox's only where the main world
	/// left the pixel empty).
	/// </summary>
	static void RasterizeVisibility( List<Draw> draws, int width, int height )
	{
		var samples = SampleX.Length;
		var mainDepth = new float[width * height * samples];
		var skyDepth = new float[width * height * samples];
		var main = draws.Where( d => !d.Sky ).ToList();
		var sky = draws.Where( d => d.Sky ).ToList();
		foreach ( var d in main.Where( d => !d.AlphaTest ).Concat( main.Where( d => d.AlphaTest ) ) ) Rasterize( d, width, height, mainDepth, false, null );
		foreach ( var d in sky.Where( d => !d.AlphaTest ).Concat( sky.Where( d => d.AlphaTest ) ) ) Rasterize( d, width, height, skyDepth, false, null );
		var mainEmpty = new bool[width * height];
		for ( int p = 0; p < width * height; p++ )
		{
			var empty = true;
			for ( int s = 0; s < samples; s++ ) empty &= mainDepth[p * samples + s] == 0;
			mainEmpty[p] = empty;
		}

		foreach ( var d in main ) Rasterize( d, width, height, mainDepth, true, null );
		foreach ( var d in sky ) Rasterize( d, width, height, skyDepth, true, mainEmpty );
	}

	static void Rasterize( Draw d, int w, int h, float[] depth, bool equal, bool[] allowed )
	{
		var samples = SampleX.Length;
		var clip = ClipVertices( d, out var min );
		for ( int t = 0; t < d.Triangles; t++ )
		{
			if ( d.Classes[t] < Hidden ) continue;
			var a = clip[(int)d.Indices[d.IndexBase + t * 3] - min];
			var b = clip[(int)d.Indices[d.IndexBase + t * 3 + 1] - min];
			var c = clip[(int)d.Indices[d.IndexBase + t * 3 + 2] - min];
			if ( a.W <= 1e-4f || b.W <= 1e-4f || c.W <= 1e-4f || a.Z > a.W || b.Z > b.W || c.Z > c.W ) continue;
			var (ax, ay) = Screen( a, w, h );
			var (bx, by) = Screen( b, w, h );
			var (cx, cy) = Screen( c, w, h );
			double az = a.Z / a.W, bz = b.Z / b.W, cz = c.Z / c.W;
			var x0 = Math.Max( (int)Math.Floor( Math.Min( ax, Math.Min( bx, cx ) ) ), 0 );
			var x1 = Math.Min( (int)Math.Floor( Math.Max( ax, Math.Max( bx, cx ) ) ), w - 1 );
			var y0 = Math.Max( (int)Math.Floor( Math.Min( ay, Math.Min( by, cy ) ) ), 0 );
			var y1 = Math.Min( (int)Math.Floor( Math.Max( ay, Math.Max( by, cy ) ) ), h - 1 );
			if ( x0 > x1 || y0 > y1 ) continue;
			var e = new Edges( ax, ay, bx, by, cx, cy );
			var visible = false;
			for ( int y = y0; y <= y1 && !(equal && visible); y++ )
			{
				for ( int x = x0; x <= x1 && !(equal && visible); x++ )
				{
					var pixel = y * w + x;
					if ( allowed is not null && !allowed[pixel] ) continue;
					for ( int s = 0; s < samples; s++ )
					{
						if ( !e.Inside( x + SampleX[s], y + SampleY[s], out var w0, out var w1, out var w2 ) ) continue;
						if ( e.Swapped ) (w1, w2) = (w2, w1);
						var z = (float)((w0 * az + w1 * bz + w2 * cz) / e.Area2);
						ref var stored = ref depth[pixel * samples + s];
						if ( equal )
						{
							if ( z == stored ) { visible = true; break; }
						}
						else if ( z > stored ) stored = z;
					}
				}
			}

			if ( equal && visible ) d.Classes[t] = Visible;
		}
	}

	/// <summary>Cut a draw's index range into meshlets with meshoptimizer.</summary>
	static Meshlets Build( MeshVertexBuffer vertices, VertexField position, MeshIndexBuffer indices, int vertexBase, int indexBase, int indexCount, Config config, bool mapTriangles )
	{
		var min = int.MaxValue;
		var max = int.MinValue;
		for ( int k = 0; k < indexCount; k++ )
		{
			var index = (int)indices[indexBase + k];
			if ( index < min ) min = index;
			if ( index > max ) max = index;
		}

		var vertexCount = max - min + 1;
		var positions = new float[vertexCount * 3];
		for ( int k = 0; k < vertexCount; k++ )
		{
			var p = vertices.Read( position, vertexBase + min + k );
			positions[k * 3] = p.X;
			positions[k * 3 + 1] = p.Y;
			positions[k * 3 + 2] = p.Z;
		}

		var local = new uint[indexCount];
		for ( int k = 0; k < indexCount; k++ ) local[k] = (uint)((int)indices[indexBase + k] - min);
		return Build( local, positions, vertexCount, config, mapTriangles );
	}

	[StructLayout( LayoutKind.Sequential )]
	struct NativeMeshlet
	{
		public uint VertexOffset, TriangleOffset, VertexCount, TriangleCount;
	}

	[StructLayout( LayoutKind.Sequential )]
	struct NativeBounds
	{
		public V3 Center;
		public float Radius;
		public V3 ConeApex;
		public V3 ConeAxis;
		public float ConeCutoff;
		public int ConeS8;
	}

	static delegate* unmanaged< nuint, nuint, nuint, nuint > buildMeshletsBound;
	static delegate* unmanaged< NativeMeshlet*, uint*, byte*, uint*, nuint, float*, nuint, nuint, nuint, nuint, float, nuint > buildMeshlets;
	static delegate* unmanaged< NativeMeshlet*, uint*, byte*, uint*, nuint, nuint, nuint, nuint, nuint > buildMeshletsScan;

	// Ticks spent in meshoptimizer's builder and bounds, for the load cost
	static long buildTicks, boundsTicks;
	static delegate* unmanaged< uint*, byte*, nuint, float*, nuint, nuint, NativeBounds > computeMeshletBounds;

	static void LoadMeshopt()
	{
		if ( buildMeshlets is not null ) return;
		var path = Args.String( "-meshopt-dll" ) ?? Path.Combine( AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "meshoptimizer.dll" : "libmeshoptimizer.so" );
		var lib = NativeLibrary.Load( path );
		buildMeshletsBound = (delegate* unmanaged< nuint, nuint, nuint, nuint >)NativeLibrary.GetExport( lib, "meshopt_buildMeshletsBound" );
		buildMeshlets = (delegate* unmanaged< NativeMeshlet*, uint*, byte*, uint*, nuint, float*, nuint, nuint, nuint, nuint, float, nuint >)NativeLibrary.GetExport( lib, "meshopt_buildMeshlets" );
		buildMeshletsScan = (delegate* unmanaged< NativeMeshlet*, uint*, byte*, uint*, nuint, nuint, nuint, nuint, nuint >)NativeLibrary.GetExport( lib, "meshopt_buildMeshletsScan" );
		computeMeshletBounds = (delegate* unmanaged< uint*, byte*, nuint, float*, nuint, nuint, NativeBounds >)NativeLibrary.GetExport( lib, "meshopt_computeMeshletBounds" );
	}

	static Meshlets Build( uint[] indices, float[] positions, int vertexCount, Config config, bool mapTriangles )
	{
		var bound = (int)buildMeshletsBound( (nuint)indices.Length, (nuint)config.Vertices, (nuint)config.Triangles );
		var meshlets = new NativeMeshlet[bound];
		var meshletVertices = new uint[indices.Length];
		var meshletTriangles = new byte[indices.Length];
		int count;
		fixed ( NativeMeshlet* m = meshlets )
		fixed ( uint* mv = meshletVertices )
		fixed ( byte* mt = meshletTriangles )
		fixed ( uint* ix = indices )
		fixed ( float* p = positions )
		{
			var started = Stopwatch.GetTimestamp();
			count = config.Scan
				? (int)buildMeshletsScan( m, mv, mt, ix, (nuint)indices.Length, (nuint)vertexCount, (nuint)config.Vertices, (nuint)config.Triangles )
				: (int)buildMeshlets( m, mv, mt, ix, (nuint)indices.Length, p, (nuint)vertexCount, 12, (nuint)config.Vertices, (nuint)config.Triangles, config.ConeWeight );
			Interlocked.Add( ref buildTicks, Stopwatch.GetTimestamp() - started );
		}

		// Each meshlet triangle back to the draw's triangle with the same corners (cyclic order kept)
		Dictionary<(uint, uint, uint), int> original = null;
		if ( mapTriangles )
		{
			original = new Dictionary<(uint, uint, uint), int>( indices.Length / 3 );
			for ( int t = 0; t < indices.Length / 3; t++ ) original.TryAdd( Canonical( indices[t * 3], indices[t * 3 + 1], indices[t * 3 + 2] ), t );
		}

		var result = new Meshlets { List = new Meshlet[count], Triangles = mapTriangles ? new int[indices.Length / 3] : null };
		var next = 0;
		fixed ( uint* mv = meshletVertices )
		fixed ( byte* mt = meshletTriangles )
		fixed ( float* p = positions )
		{
			for ( int i = 0; i < count; i++ )
			{
				ref readonly var m = ref meshlets[i];
				var boundsStarted = Stopwatch.GetTimestamp();
				var b = computeMeshletBounds( mv + m.VertexOffset, mt + m.TriangleOffset, m.TriangleCount, p, (nuint)vertexCount, 12 );
				Interlocked.Add( ref boundsTicks, Stopwatch.GetTimestamp() - boundsStarted );
				var lo = new V3( float.MaxValue );
				var hi = new V3( float.MinValue );
				for ( int k = 0; k < m.VertexCount; k++ )
				{
					var v = (int)meshletVertices[m.VertexOffset + k];
					var q = new V3( positions[v * 3], positions[v * 3 + 1], positions[v * 3 + 2] );
					lo = V3.Min( lo, q );
					hi = V3.Max( hi, q );
				}

				result.List[i] = new Meshlet { Min = lo, Max = hi, Apex = b.ConeApex, Axis = b.ConeAxis, Cutoff = b.ConeCutoff, First = next, Count = (int)m.TriangleCount, VertexCount = (int)m.VertexCount };
				if ( mapTriangles )
				{
					for ( int t = 0; t < m.TriangleCount; t++ )
					{
						var o = m.TriangleOffset + t * 3;
						var key = Canonical( meshletVertices[m.VertexOffset + meshletTriangles[o]], meshletVertices[m.VertexOffset + meshletTriangles[o + 1]], meshletVertices[m.VertexOffset + meshletTriangles[o + 2]] );
						result.Triangles[next + t] = original.TryGetValue( key, out var tri ) ? tri : -1;
					}
				}

				next += (int)m.TriangleCount;
			}
		}

		return result;
	}

	static (uint, uint, uint) Canonical( uint a, uint b, uint c )
	{
		if ( a <= b && a <= c ) return (a, b, c);
		if ( b <= a && b <= c ) return (b, c, a);
		return (c, a, b);
	}

	/// <summary>Each meshlet of a draw tested as a GPU cull would test it.</summary>
	static Result Test( Draw d, Meshlets meshlets, ViewSetup v, int width, int height )
	{
		var r = new Result { Triangles = d.Triangles, Meshlets = meshlets.List.Length };

		// Normals go through the inverse transpose; a mirroring transform flips winding, and non-uniform scale breaks the
		// cone's angle, so those draws get no cone test
		var m = d.ToWorld;
		var sx = new V3( m.M11, m.M12, m.M13 ).Length();
		var sy = new V3( m.M21, m.M22, m.M23 ).Length();
		var sz = new V3( m.M31, m.M32, m.M33 ).Length();
		var uniform = MathF.Max( sx, MathF.Max( sy, sz ) ) <= MathF.Min( sx, MathF.Min( sy, sz ) ) * 1.01f;
		var mirror = m.GetDeterminant() < 0;
		var coneAllowed = !d.TwoSided && uniform;

		Span<V4> corners = stackalloc V4[8];
		var previousKept = false;
		foreach ( ref readonly var meshlet in meshlets.List.AsSpan() )
		{
			r.MeshletVertices += meshlet.VertexCount;

			// Box corners in clip space
			for ( int k = 0; k < 8; k++ )
			{
				var p = new V3( (k & 1) != 0 ? meshlet.Max.X : meshlet.Min.X, (k & 2) != 0 ? meshlet.Max.Y : meshlet.Min.Y, (k & 4) != 0 ? meshlet.Max.Z : meshlet.Min.Z );
				corners[k] = V4.Transform( new V4( p, 1 ), d.ToClip );
			}

			var frustum = OutsideAll( corners );
			var cone = false;
			if ( coneAllowed && meshlet.Cutoff < 1 )
			{
				var apex = V3.Transform( meshlet.Apex, m );
				var axis = V3.Normalize( V3.TransformNormal( meshlet.Axis, m ) ) * (mirror ? -1 : 1);
				cone = V3.Dot( V3.Normalize( apex - v.Camera ), axis ) >= meshlet.Cutoff;
			}

			var small = !frustum && NoSampleInBounds( corners, width, height );
			var any = frustum || cone || small;

			var ideal = true;
			for ( int t = 0; t < meshlet.Count; t++ )
			{
				var tri = meshlets.Triangles[meshlet.First + t];
				var c = tri < 0 ? Hidden : d.Classes[tri];
				if ( c > NoSample ) ideal = false;
				if ( cone && c >= NoSample ) r.ConeFront++;
				if ( !any ) r.Kept[c]++;
			}

			if ( frustum ) r.Frustum += meshlet.Count;
			if ( cone ) r.Cone += meshlet.Count;
			if ( small ) r.Small += meshlet.Count;
			if ( any ) r.Any += meshlet.Count;
			else
			{
				r.KeptVertices += meshlet.VertexCount;
				r.KeptMeshlets++;
				if ( !previousKept ) r.Runs++;
			}

			previousKept = !any;
			if ( ideal ) r.Ideal += meshlet.Count;
		}

		return r;
	}

	static bool OutsideAll( ReadOnlySpan<V4> c )
	{
		int nx = 0, px = 0, ny = 0, py = 0, far = 0, near = 0;
		foreach ( var v in c )
		{
			if ( v.X < -v.W ) nx++;
			if ( v.X > v.W ) px++;
			if ( v.Y < -v.W ) ny++;
			if ( v.Y > v.W ) py++;
			if ( v.Z < 0 ) far++;
			if ( v.Z > v.W ) near++;
		}

		var n = c.Length;
		return nx == n || px == n || ny == n || py == n || far == n || near == n;
	}

	/// <summary>Whether the screen bounds of the box (wholly in front of the near plane) hold none of the frame's samples.</summary>
	static bool NoSampleInBounds( ReadOnlySpan<V4> corners, int width, int height )
	{
		double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
		foreach ( var c in corners )
		{
			if ( c.W <= 1e-4f || c.Z > c.W ) return false;
			var (x, y) = Screen( c, width, height );
			x0 = Math.Min( x0, x );
			y0 = Math.Min( y0, y );
			x1 = Math.Max( x1, x );
			y1 = Math.Max( y1, y );
		}

		for ( int s = 0; s < SampleX.Length; s++ )
		{
			var hasX = Math.Ceiling( x0 - SampleX[s] ) <= Math.Floor( x1 - SampleX[s] );
			var hasY = Math.Ceiling( y0 - SampleY[s] ) <= Math.Floor( y1 - SampleY[s] );
			if ( hasX && hasY ) return false;
		}

		return true;
	}

	/// <summary>Every opaque draw range of both worlds at every LOD, cut once per config: time on one core and on all, and memory.</summary>
	static void LoadCost( MapWorld map, Config[] configs, Action<string> line )
	{
		var ranges = new Dictionary<(MeshVertexBuffer, int, MeshIndexBuffer, int, int, int), (MeshVertexBuffer V, VertexField P, MeshIndexBuffer I, int VB, int IB, int IC)>();
		var none = new Dictionary<GpuMaterial, GpuCullMode>();
		var ignored = new HashSet<GpuMaterial>();
		void Add( Draw d )
		{
			if ( d is not null && d.IndexCount >= 3 ) ranges.TryAdd( d.Key, (d.Vertices, d.Position, d.Indices, d.VertexBase, d.IndexBase, d.IndexCount) );
		}

		foreach ( var world in new[] { map.World, map.World.Skybox3D?.World }.Where( w => w is not null ).Distinct() )
		{
			foreach ( var obj in world.Objects )
			{
				if ( obj is MeshObject { Mesh: { } mesh } && mesh.Geometry is BackendModel model )
				{
					for ( int lod = 0; lod < mesh.LodCount; lod++ )
					{
						foreach ( ref readonly var draw in mesh.DrawsForLod( lod ) )
						{
							if ( draw.PrimitiveType != (int)PrimitiveKind.Triangles || draw.IndexCount == 0 || draw.Translucent ) continue;
							var d = Make( model, draw.Mesh, draw.DrawCall, draw.Material, none, ignored );
							if ( d is null ) continue;
							d.IndexBase += draw.StartIndex;
							d.IndexCount = draw.IndexCount;
							Add( d );
						}
					}
				}
				else if ( obj is AggregateObject ag && ag.Mesh.Geometry is BackendModel agModel )
				{
					var first = ag.Mesh.DrawsForLod( 0 );
					if ( first.Length == 0 ) continue;
					var draw0 = first[0];
					foreach ( ref readonly var fragment in ag.Fragments )
					{
						var d = Make( agModel, draw0.Mesh, draw0.DrawCall, draw0.Material, none, ignored );
						if ( d is null ) continue;
						d.VertexBase += fragment.Args.BaseVertex - agModel.File.Meshes[draw0.Mesh].DrawCalls[draw0.DrawCall].BaseVertex;
						d.IndexBase += (int)fragment.Args.FirstIndex;
						d.IndexCount = (int)fragment.Args.IndexCount;
						Add( d );
					}
				}
			}
		}

		var list = ranges.Values.ToList();
		long triangles = list.Sum( r => (long)r.IC / 3 );
		line( $"{list.Count:N0} draw ranges, {triangles / 1e6:0.000}M triangles." );
		line( "" );
		line( "| Config | Meshlets | Tris/meshlet | One core s | All cores s | ns a triangle (one core) | of which meshopt build s | of which meshopt bounds s | Records MB (32 B each) |" );
		line( "|---|---|---|---|---|---|---|---|---|" );
		foreach ( var config in configs )
		{
			var sw = Stopwatch.StartNew();
			long meshlets = 0;
			(buildTicks, boundsTicks) = (0, 0);
			foreach ( var r in list ) meshlets += Build( r.V, r.P, r.I, r.VB, r.IB, r.IC, config, mapTriangles: false ).List.Length;
			var one = sw.Elapsed.TotalSeconds;
			var (buildSeconds, boundsSeconds) = (buildTicks / (double)Stopwatch.Frequency, boundsTicks / (double)Stopwatch.Frequency);
			sw.Restart();
			Parallel.ForEach( list, r => Build( r.V, r.P, r.I, r.VB, r.IB, r.IC, config, mapTriangles: false ) );
			var all = sw.Elapsed.TotalSeconds;
			line( FormattableString.Invariant( $"| {config} | {meshlets:N0} | {triangles / (double)Math.Max( meshlets, 1 ):0.0} | {one:0.00} | {all:0.00} | {one * 1e9 / Math.Max( triangles, 1 ):0} | {buildSeconds:0.00} | {boundsSeconds:0.00} | {meshlets * 32 / 1048576.0:0.0} |" ) );
		}
	}
}
