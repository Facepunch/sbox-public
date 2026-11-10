using Basalt;
using Basalt.Backend;
using Basalt.Gpu;
using Sandbox;
using System.Diagnostics;
using System.IO;

namespace BasaltGpuSample;

/// <summary>
/// <c>-materialbench</c>: every compiled model under the content's <c>models</c> folders (core and the citizen addon), laid
/// out on a grid and rendered by Basalt through Basalt.Gpu offscreen - depth prepass, sun shadow cascades, forward - first
/// binding by reflection every draw, then through material objects, in alternating runs. Prints the CPU time of a model
/// draw (bind and record) for each: per bind (a draw that changed material or mode) and per draw of the same material
/// again. See docs/managed/basalt-materials.md.
/// </summary>
internal static class MaterialBench
{
	public static int Run( Device gpu, int frames, int runs, int maxModels )
	{
		var backend = new BackendDevice( gpu );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		var system = new RenderSystem { ParallelRecording = false };
		ViewTarget target = null;

		try
		{
			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
			world.Lighting.SunColor = new Color( 1.25f, 1.15f, 1.0f );
			world.Lighting.AmbientColor = new Color( 0.12f, 0.15f, 0.2f, 1 );
			world.Lighting.SunShadowCascades = 3;

			// Every model the content has, in a square grid of cells sized to the largest
			var paths = new List<string>();
			foreach ( var root in backend.Content.Roots )
			{
				var models = Path.Combine( root, "models" );
				if ( !Directory.Exists( models ) ) continue;
				foreach ( var file in Directory.EnumerateFiles( models, "*.vmdl_c", SearchOption.AllDirectories ) )
					paths.Add( Path.GetRelativePath( root, file ).Replace( '\\', '/' )[..^2] );
			}

			paths.Sort( StringComparer.Ordinal );
			var meshes = new List<RenderMesh>();
			foreach ( var path in paths )
			{
				if ( meshes.Count >= maxModels ) break;
				try
				{
					if ( backend.LoadModel( path ) is { } mesh && mesh.Draws.Count > 0 && mesh.Bounds.Size.Length < 2000 ) meshes.Add( mesh );
				}
				catch ( Exception e )
				{
					Console.WriteLine( $"skipped {path}: {e.Message}" );
				}
			}

			var materials = new HashSet<GpuMaterial>();
			var meshDraws = 0;
			foreach ( var mesh in meshes )
			{
				foreach ( var draw in mesh.Draws ) materials.Add( draw.Material );
				meshDraws += mesh.Draws.Count;
			}

			var side = (int)MathF.Ceiling( MathF.Sqrt( meshes.Count ) );
			const float cell = 220;
			for ( int i = 0; i < meshes.Count; i++ )
			{
				var mesh = meshes[i];
				var position = new Vector3( (i % side - side / 2f) * cell, (i / side - side / 2f) * cell, 0 ) - mesh.Bounds.Center.WithZ( mesh.Bounds.Mins.z );
				world.Add( new MeshObject( mesh, new Transform( position ) ) );
			}

			world.Add( new MeshObject( BackendMesh.Create( backend, Ground( side * cell ), [0, 1, 2, 0, 2, 3], backend.CreateMaterial( "Ground", new Color( 0.5f, 0.5f, 0.5f ) ), "Ground" ), Transform.Zero ) );

			const int width = 1280, height = 720;
			var view = new RenderView { Name = "Material bench", FieldOfView = 60, ZNear = 10, ZFar = side * cell * 4, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, width, height ), SizeCullThreshold = 0 };
			view.Rotation = Rotation.From( 60, 0, 0 );
			view.Position = -view.Rotation.Forward * side * cell * 0.95f;
			target = ViewTarget.Create( new Vector2Int( width, height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Material bench" );

			Console.WriteLine( $"{gpu.Adapter}: {meshes.Count} models, {meshDraws} mesh draws, {materials.Count} materials" );

			// Warm both paths: pipelines, variants, material objects and their sets
			foreach ( var objects in (ReadOnlySpan<bool>)[false, true, false, true] ) Frames( gpu, backend, system, world, view, target, objects, 10 );

			Console.WriteLine( "path | frames | draws/frame | binds/frame | ns/bind | ns/repeat draw | ns/draw | draw ms/frame | Render() CPU ms/frame | sets written" );
			for ( int run = 0; run < runs; run++ )
			{
				foreach ( var objects in run % 2 == 0 ? (ReadOnlySpan<bool>)[false, true] : [true, false] )
				{
					var setsBefore = backend.MaterialObjectStats.SetsWritten;
					backend.TimeDraws = true;
					backend.DrawTimes.Reset();
					var cpu = Frames( gpu, backend, system, world, view, target, objects, frames );
					backend.TimeDraws = false;
					var t = backend.DrawTimes;
					var drawMs = (t.BindTicks + t.DrawTicks) * 1000.0 / Stopwatch.Frequency / frames;
					Console.WriteLine( $"{(objects ? "material objects" : "reflection")} | {frames} | {t.Draws / frames} | {t.Binds / frames} | {t.NanosecondsPerBind:F0} | {t.NanosecondsPerRepeat:F0} | {t.NanosecondsPerDraw:F0} | {drawMs:F3} | {cpu:F3} | {backend.MaterialObjectStats.SetsWritten - setsBefore}" );
				}
			}

			Console.WriteLine( $"material objects: {backend.MaterialObjectStats.Objects}; last frame: {system.Stats.Draws} forward, {system.Stats.DepthDraws} depth, {system.Stats.ShadowDraws} shadow draws; {gpu.ValidationErrors} validation errors" );
			return gpu.ValidationErrors > 0 ? 1 : 0;
		}
		finally
		{
			gpu.WaitIdle();
			target?.Dispose();
			system.Dispose();
			backend.Dispose();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>Render <paramref name="count"/> frames one path; returns the mean CPU milliseconds of a Render call.</summary>
	static double Frames( Device gpu, BackendDevice backend, RenderSystem system, RenderWorld world, RenderView view, ViewTarget target, bool materialObjects, int count )
	{
		backend.MaterialObjects = materialObjects;
		long ticks = 0;
		for ( int i = 0; i < count; i++ )
		{
			backend.BeginFrame();
			var start = Stopwatch.GetTimestamp();
			RenderHost.Current.NextFrame();
			system.Render( world, view, target );
			ticks += Stopwatch.GetTimestamp() - start;
		}

		gpu.WaitIdle();
		return ticks * 1000.0 / Stopwatch.Frequency / count;
	}

	static MeshVertex[] Ground( float size )
	{
		var h = size / 2;
		return
		[
			new( new Vector3( -h, -h, 0 ), Vector3.Up, new Vector2( 0, 0 ) ),
			new( new Vector3( h, -h, 0 ), Vector3.Up, new Vector2( 1, 0 ) ),
			new( new Vector3( h, h, 0 ), Vector3.Up, new Vector2( 1, 1 ) ),
			new( new Vector3( -h, h, 0 ), Vector3.Up, new Vector2( 0, 1 ) ),
		];
	}
}
