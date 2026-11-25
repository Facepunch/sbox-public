using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Borealis.Source2;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using Capture = BorealisGpuSample.Capture;
using GpuDevice = Borealis.Gpu.Device;
using V3 = System.Numerics.Vector3;

namespace Sandbox.BorealisLab.Maps;

/// <summary>
/// <c>-error-census ident[,ident...] [-out folder] [-validation]</c>: each installed map loaded on a Borealis.Gpu device of
/// its own with no window (GPU-driven, as the game draws it), then what of it doesn't draw as its content says: every
/// material that falls back (not built: the error material; a shader that won't load: the error shader; a file that can't
/// be read: nothing) with the objects and aggregates drawing it, shaders that don't load or compile, and pass errors; then
/// one counted frame per view (<see cref="DrawCensus"/>: draws with the error shader, draws that recorded nothing, aggregate
/// batches left classic) with its picture. Writes <c>census.md</c> and each view's draws and picture into the folder.
/// </summary>
internal static class ErrorCensus
{
	public static bool Requested => Args.String( "-error-census" ) is not null;

	public static int Run()
	{
		var idents = Args.String( "-error-census" ).Split( ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries );
		var output = Path.GetFullPath( Args.String( "-out" ) ?? Path.Combine( "screenshots", "borealislab", "errorcensus" ) );
		Directory.CreateDirectory( output );

		var errors = 0;
		var messages = new ConcurrentDictionary<string, int>();
		using var gpu = GpuDevice.Create( new DeviceOptions
		{
			ApplicationName = "Sbox",
			EngineName = "Source2",
			Validation = Args.Has( "-validation" ),
			Message = m =>
			{
				if ( m.Severity < MessageSeverity.Warning ) return;
				if ( m.Severity == MessageSeverity.Error ) System.Threading.Interlocked.Increment( ref errors );
				if ( messages.AddOrUpdate( $"[{m.Severity}] {m.Id}: {m.Text}", 1, ( _, n ) => n + 1 ) == 1 && errors < 20 ) Console.WriteLine( $"[{m.Severity}] {m.Id}: {m.Text}" );
			},
		} );

		var core = ContentFiles.FindGame();
		if ( core.Roots.Count == 0 ) core = ContentFiles.FindGame( Environment.CurrentDirectory );

		var md = new StringBuilder( $"# Error census, {DateTime.Now:yyyy-MM-dd HH:mm}, {gpu.Adapter.Name}, validation {(gpu.ValidationEnabled ? "on" : "off")}\n\n" );
		foreach ( var ident in idents )
		{
			// A package ident, or a workshop item's id (a game package with its maps)
			var folder = ulong.TryParse( ident, out var id ) ? WorkshopContent.Find( id ) : MapRun.FindPackage( ident );
			if ( folder is null )
			{
				md.Append( $"## {ident}\n\nNot installed.\n\n" );
				continue;
			}

			Console.WriteLine( $"{ident}: {folder}" );
			md.Append( Map( gpu, ident, folder, core, output ) );
			File.WriteAllText( Path.Combine( output, "census.md" ), md.ToString() );
		}

		md.Append( $"## Device messages\n\n{(messages.IsEmpty ? "None.\n" : string.Concat( messages.OrderByDescending( m => m.Value ).Select( m => $"- {m.Value} {m.Key}\n" ) ))}" );
		File.WriteAllText( Path.Combine( output, "census.md" ), md.ToString() );
		Console.WriteLine( $"Written {Path.Combine( output, "census.md" )}" );
		return errors > 0 ? 1 : 0;
	}

	static string Map( GpuDevice gpu, string ident, string folder, ContentFiles core, string output )
	{
		var said = new ConcurrentDictionary<string, int>();
		void Say( string kind, string message ) => said.AddOrUpdate( $"{kind}: {message}", 1, ( _, n ) => n + 1 );

		// -census-classic: GPU-driven drawing off, every draw the classic way, to compare
		var gpuDriven = !Args.Has( "-census-classic" );
		using var map = new MapWorld( gpu, folder, core, castShadows: false, gpuDriven: gpuDriven, made: backend =>
		{
			backend.Shaders.Unloaded = m => Say( "shader", m );
			backend.Surfaces.Error = m => Say( "surface", m );
			backend.Passes.Error = m => Say( "pass", m );
			backend.Warn = m => Say( "warning", m );
		} );
		map.SetTarget( 1280, 720, 4 );
		map.Backend.GpuDriven = gpuDriven;
		map.Backend.MaterialObjects = true;

		var md = new StringBuilder( $"## {ident}\n\n{map.MeshObjects} mesh objects, {map.Aggregates} aggregates ({map.Fragments} fragments), {map.Models} models; {map.Problems.Count} load problems\n\n" );
		foreach ( var problem in map.Problems.Take( 8 ) ) md.Append( $"- load: {problem}\n" );
		if ( map.Problems.Count > 8 ) md.Append( $"- load: {map.Problems.Count - 8} more\n" );

		// Every object's materials, as it draws them at its first level: which don't draw as their content says
		var objects = new Dictionary<(string Material, string Why), (int Meshes, int Aggregates, string Worlds, Vector3 At)>();
		void Walk( RenderWorld world, string worldName )
		{
			if ( world is null ) return;
			foreach ( var obj in world.Objects )
			{
				var mesh = obj switch { MeshObject m => m.Mesh, AggregateObject a => a.Mesh, _ => null };
				if ( mesh is null ) continue;
				foreach ( var material in mesh.Draws.Select( d => d.Material ).Distinct() )
				{
					if ( Why( map.Backend, material ) is not { } why ) continue;
					var key = (material?.Name ?? "(none)", why);
					var (meshes, aggregates, worlds, at) = objects.GetValueOrDefault( key );
					if ( meshes + aggregates == 0 ) at = obj.Transform.PointToWorld( obj.LocalBounds.Center );
					if ( obj is AggregateObject ) aggregates++;
					else meshes++;
					if ( worlds is null || !worlds.Contains( worldName ) ) worlds = worlds is null ? worldName : $"{worlds}, {worldName}";
					objects[key] = (meshes, aggregates, worlds, at);
				}
			}
		}

		Walk( map.World, "world" );
		Walk( map.SkyWorld, "3D skybox" );

		// -census-only-fallbacks: every other mesh and aggregate taken out, so the pictures show whether those draw at all
		if ( Args.Has( "-census-only-fallbacks" ) )
		{
			foreach ( var world in new[] { map.World, map.SkyWorld } )
			{
				if ( world is null ) continue;
				foreach ( var obj in world.Objects.ToArray() )
				{
					var mesh = obj switch { MeshObject m => m.Mesh, AggregateObject a => a.Mesh, _ => null };
					if ( mesh is not null && !mesh.Draws.Any( d => Why( map.Backend, d.Material ) is not null ) ) world.Remove( obj );
				}
			}
		}

		// Views: Construct's two, or two from over the middle of the map's objects
		var views = ident.Equals( "facepunch.construct", StringComparison.OrdinalIgnoreCase ) ? MapRun.ConstructViews : Overviews( map.World );

		// -census-look x,y,z;...: and a view of each of these points, from above and to the side
		var looks = (Args.String( "-census-look" ) ?? "").Split( ';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries )
			.Select( ( p, i ) => p.Split( ',' ).Select( v => float.Parse( v, System.Globalization.CultureInfo.InvariantCulture ) ).ToArray() is [var x, var y, var z]
				? new MapView( $"look{i}", new V3( x + 260, y + 200, z + 160 ), new V3( x, y, z ) ) : default ).Where( v => v.Name is not null );
		views = [.. views, .. looks];
		var census = new StringBuilder();
		foreach ( var view in views )
		{
			// Until what builds on workers (meshlets, LODs, package shaders, pipelines) has been quiet two seconds, or a minute
			var building = System.Diagnostics.Stopwatch.StartNew();
			var quiet = System.Diagnostics.Stopwatch.StartNew();
			var asked = map.Backend.BuildsRequested;
			while ( quiet.Elapsed.TotalSeconds < 2 && building.Elapsed.TotalSeconds < 60 )
			{
				Frame( map, view );
				if ( map.Backend.BuildsPending > 0 || map.Backend.BuildsRequested != asked || gpu.PipelineLibraryCreation.Pending > 0 || gpu.DeferredPipelines.Pending > 0 ) quiet.Restart();
				asked = map.Backend.BuildsRequested;
			}

			var name = $"{ident.Split( '.' ).Last()}-{view.Name}";
			gpu.WaitIdle();
			map.Backend.DrawTimes.Reset();
			map.Backend.GpuDrivenLeftClassic.Clear();
			map.Backend.TimeDraws = true;
			var drawn = new DrawCensus();
			map.Backend.Census = drawn;
			try
			{
				Frame( map, view );
			}
			finally
			{
				map.Backend.Census = null;
				map.Backend.TimeDraws = false;
			}

			gpu.WaitIdle();
			drawn.Resolve( gpu );
			File.WriteAllText( Path.Combine( output, $"{name}-draws.csv" ), drawn.Csv() );
			var (texture, layout) = map.Output;
			Capture.Save( gpu, texture, layout, Path.Combine( output, $"{name}.png" ) );

			census.Append( $"### {view.Name} ({view.Position} looking at {view.LookAt})\n\n" );
			// Bucket draws name no material: a bucket holds many
			var bad = drawn.Draws.Where( d => d.Shader is "error" || (d.Path == DrawCensus.Path.Classic && d.Material is null) || (d.Material is not null && map.Backend.MaterialFallbacks.ContainsKey( d.Material )) )
				.GroupBy( d => (d.Material, d.Shader, d.Kind) ).OrderByDescending( g => g.Sum( d => d.Calls ) ).ToArray();
			census.Append( bad.Length == 0 ? "No draw with the error shader or a material that fell back.\n" : "| Material | Shader | What | Draw calls | Passes |\n|---|---|---|--:|---|\n" );
			foreach ( var g in bad ) census.Append( $"| {g.Key.Material ?? "(none)"} | {g.Key.Shader} | {g.Key.Kind} | {g.Sum( d => d.Calls )} | {string.Join( "; ", g.Select( d => d.Pass ).Distinct() )} |\n" );
			lock ( map.Backend.DrawTimes.Failed )
			{
				foreach ( var ((material, mode), count) in map.Backend.DrawTimes.FailedModes ) census.Append( $"- recorded nothing: {count} draws of {material} ({BackendContext.ModeNameOf( mode )})\n" );
			}

			foreach ( var (why, count) in map.Backend.GpuDrivenLeftClassic ) census.Append( $"- aggregate batches left classic: {count} {why}\n" );
			foreach ( var (why, count) in drawn.LeftClassic ) census.Append( $"- left classic (census): {count} {why}\n" );
			census.Append( '\n' );
		}

		md.Append( "\n### Materials that don't draw as their content says\n\n" );
		md.Append( objects.Count == 0 ? "None.\n" : "| Material | Why | Mesh objects | Aggregates | In | First at |\n|---|---|--:|--:|---|---|\n" );
		foreach ( var ((material, why), (meshes, aggregates, worlds, at)) in objects.OrderBy( x => x.Key.Material, StringComparer.OrdinalIgnoreCase ) )
		{
			md.Append( FormattableString.Invariant( $"| {material} | {why} | {meshes} | {aggregates} | {worlds} | {at.x:0},{at.y:0},{at.z:0} |\n" ) );
		}

		var unused = map.Backend.MaterialFallbacks.Where( f => !objects.Keys.Any( k => k.Material.Equals( f.Key, StringComparison.OrdinalIgnoreCase ) ) ).ToArray();
		foreach ( var (path, why) in unused ) md.Append( $"- also fell back (no map object draws it at its first level): {path}: {why}\n" );
		md.Append( "\n### Shaders, surfaces and passes\n\n" );
		md.Append( said.IsEmpty ? "Nothing said.\n" : string.Concat( said.OrderBy( s => s.Key, StringComparer.Ordinal ).Select( s => $"- {s.Key}{(s.Value > 1 ? $" (x{s.Value})" : "")}\n" ) ) );
		md.Append( '\n' ).Append( census );
		return md.ToString();
	}

	/// <summary>Why a material doesn't draw as its content says, or null when it does.</summary>
	static string Why( BackendDevice backend, GpuMaterial material )
	{
		if ( material is null ) return "no material: draws nothing";
		var fellBack = backend.MaterialFallbacks.TryGetValue( material.Name ?? "", out var why ) ? why : null;
		return material switch
		{
			BackendMaterial { Shader: null } => fellBack ?? "no shader: draws nothing",
			BackendMaterial { Shader.IsErrorShader: true } => fellBack ?? "the error shader",
			_ => fellBack,
		};
	}

	static void Frame( MapWorld map, MapView view )
	{
		map.Backend.BeginFrame();
		map.Render( view );
	}

	/// <summary>Two views over the middle of the world's objects (the middle 90% of their positions), from either side.</summary>
	static MapView[] Overviews( RenderWorld world )
	{
		var positions = world.Objects.ToArray().Select( o => o.Transform.Position ).ToArray();
		if ( positions.Length == 0 ) return [new MapView( "origin", new V3( 300, 300, 200 ), V3.Zero )];
		float Pick( Func<Vector3, float> axis, float at )
		{
			var sorted = positions.Select( axis ).OrderBy( v => v ).ToArray();
			return sorted[(int)Math.Clamp( at * (sorted.Length - 1), 0, sorted.Length - 1 )];
		}

		var min = new V3( Pick( p => p.x, 0.05f ), Pick( p => p.y, 0.05f ), Pick( p => p.z, 0.05f ) );
		var max = new V3( Pick( p => p.x, 0.95f ), Pick( p => p.y, 0.95f ), Pick( p => p.z, 0.95f ) );
		var centre = (min + max) / 2;
		var size = Math.Max( (max - min).Length(), 500 );
		return
		[
			new MapView( "overview-a", centre + new V3( 0.45f, 0.35f, 0.3f ) * size, centre ),
			new MapView( "overview-b", centre + new V3( -0.45f, -0.35f, 0.3f ) * size, centre ),
		];
	}
}
