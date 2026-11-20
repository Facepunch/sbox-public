using Borealis.Gpu;
using Borealis.Source2.Shaders;
using Sandbox.ShaderCompiler;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SurfaceManifestRequest = Borealis.Backend.SurfacePipelineManifest.Request;

namespace Borealis.Backend;

/// <summary>
/// New-tier shaders on a <see cref="BackendDevice"/>: compiled from Slang by file name (searched in
/// <see cref="Folders"/>) the first time a <see cref="SurfaceMaterial"/> of theirs draws, with their modules, binding
/// layouts and pipelines, and the material records they read. Only Borealis.Gpu compiles them; old shaders are untouched.
/// </summary>
public sealed class SurfaceShaders : IDisposable
{
	readonly BackendDevice device;
	readonly Dictionary<string, SurfaceProgram> programs = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<string, Task<SurfaceProgram>> compiling = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<string, CompiledShader> layouts = new( StringComparer.OrdinalIgnoreCase );
	readonly List<string> folders = new();
	SlangCompiler compiler;
	readonly Dictionary<string, List<string>> changedWhileCompiling = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<string, CompilePriority> priorities = new( StringComparer.OrdinalIgnoreCase );
	bool disposed;

	internal SurfaceShaders( BackendDevice device )
	{
		this.device = device;
		Records = new SurfaceRecords( device.Gpu );
		if ( !BackendDevice.HasSwitch( "-no-pipeline-manifest" ) ) Manifest = new SurfacePipelineManifest( SurfacePipelineManifest.PathBeside( device.Gpu.PipelineCachePath ) );
	}

	/// <summary>
	/// The surface pipelines draws made, kept beside the device's pipeline cache and made again on workers as each program
	/// compiles (<see cref="SurfacePipelineManifest"/>); null without a pipeline cache file or with <c>-no-pipeline-manifest</c>.
	/// </summary>
	internal SurfacePipelineManifest Manifest { get; }

	/// <summary>
	/// Make each program's pipeline parts ahead as it compiles (<see cref="SurfaceProgram.PrepareParts"/>), where the device
	/// links pipelines from parts: the passes its materials draw with (<see cref="NoteMaterial"/>), with the specialization
	/// values draws look up (<see cref="SpecializationValues"/>; nothing is guessed before the first lookup).
	/// <c>-no-prepare-parts</c> turns it off.
	/// </summary>
	public bool PrepareParts { get; set; } = !BackendDevice.HasSwitch( "-no-prepare-parts" );

	/// <summary>
	/// Specialization constants' values by attribute, as draws last looked them up: global settings (shadow filter quality,
	/// DDGI and the like), the same for every program. What parts made ahead take for them.
	/// </summary>
	internal IReadOnlyDictionary<string, int> SpecializationValues => specializationValues;

	readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> specializationValues = new( StringComparer.Ordinal );

	/// <summary>
	/// A draw looked a specialization constant up (<see cref="SpecializationValues"/>): the first has every program compiled
	/// so far guess its parts, on a worker.
	/// </summary>
	internal void SpecializationLookedUp( string attribute, int value )
	{
		if ( specializationValues.TryGetValue( attribute, out var known ) && known == value ) return;
		specializationValues[attribute] = value;

		SurfaceProgram[] all;
		lock ( programs )
		{
			if ( valuesLookedUp || disposed ) return;
			valuesLookedUp = true;
			all = [.. programs.Values.Where( p => p is not null )];
		}

		if ( PrepareParts ) _ = Task.Run( () =>
		{
			foreach ( var program in all ) program.PrepareParts();
		} );
	}

	/// <summary>Whether a draw has looked the specialization values up yet: guesses wait for the first (<see cref="SpecializationLookedUp"/>).</summary>
	internal bool ValuesLookedUp
	{
		get
		{
			lock ( programs ) return valuesLookedUp;
		}
	}

	bool valuesLookedUp;

	// The passes each shader's materials draw with (SurfacePassNotes), by program key: noted as materials load (under the programs lock)
	readonly Dictionary<string, uint> notes = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// A material of <paramref name="file"/> loaded with render state <paramref name="state"/>, to be drawn as
	/// <paramref name="kinds"/> (a model's draw call, or its first draw): its program guesses the parts of those passes
	/// (<see cref="SurfacePassNotes"/>) as well, once it's compiled, on a worker. Once per material and draw call, never per
	/// draw.
	/// </summary>
	internal void NoteMaterial( string file, SurfaceRenderState state, SurfaceDrawKinds kinds )
	{
		if ( file is null ) return;
		var noted = SurfacePassNotes.Of( state, kinds );
		file = Key( file );
		SurfaceProgram program;
		lock ( programs )
		{
			var known = notes.GetValueOrDefault( file );
			if ( (known | noted) == known ) return;
			notes[file] = known | noted;

			// One still compiling reads the notes once it's kept (Start)
			if ( !programs.TryGetValue( file, out program ) || program is null ) return;
		}

		if ( PrepareParts ) _ = Task.Run( program.PrepareParts );
	}

	/// <summary>
	/// A pipeline of <paramref name="program"/> was made for a draw with <paramref name="state"/> as <paramref name="kind"/>
	/// (once per pipeline, never per draw): noted as a material's load is, its guesses made on a worker (the caller holds
	/// the program's lock).
	/// </summary>
	internal void NoteDrawn( SurfaceProgram program, SurfaceRenderState state, SurfaceDrawKinds kind )
	{
		if ( program.SourceFile is not { } file ) return;
		var noted = SurfacePassNotes.Of( state, kind );
		file = Key( file );
		lock ( programs )
		{
			var known = notes.GetValueOrDefault( file );
			if ( disposed || (known | noted) == known ) return;
			notes[file] = known | noted;
		}

		_ = Task.Run( program.PrepareParts );
	}

	/// <summary>The passes the materials of a program's file have noted so far (<see cref="NoteMaterial"/>); every pass for a program of no file.</summary>
	internal uint Notes( string file )
	{
		if ( file is null ) return SurfacePassNotes.All;
		lock ( programs ) return notes.GetValueOrDefault( Key( file ) );
	}

	/// <summary>Folders searched for shaders by file name, in order.</summary>
	public IReadOnlyList<string> Folders => folders;

	/// <summary>Search <paramref name="folder"/> for shaders too.</summary>
	public void AddFolder( string folder )
	{
		folder = Path.GetFullPath( folder );
		lock ( programs ) folders.Add( folder );
		Watch( folder );
	}

	/// <summary>
	/// Draw <c>.vmat</c> materials of the <c>.shader</c> <paramref name="shader"/> (<c>shaders/blendable.shader</c>) with the
	/// surface in <paramref name="file"/> (<c>blendable.slang</c>) instead, from materials loaded after this. Each field
	/// takes the material's value of its <c>[Source]</c>, or of its name with a vfx prefix (<c>g_tColorA</c> for
	/// <c>ColorA</c>); the shader's defaults where the material sets none.
	/// </summary>
	public void Convert( string shader, string file )
	{
		lock ( programs ) conversions[ShaderKey( shader )] = file;
	}

	/// <summary>The surface standing in for <paramref name="shader"/>, or null.</summary>
	internal string ConversionFor( string shader )
	{
		if ( shader is null ) return null;
		var key = ShaderKey( shader );
		lock ( programs )
		{
			if ( conversions.TryGetValue( key, out var file ) ) return file;
		}

		if ( ConvertShaders && !NotConvertedByDefault.Contains( key ) && DefaultSurfaces.TryGetValue( key, out var surface ) ) return DefaultSurface( key, surface );
		return ConvertPackages ? PackageSurface( shader ) : null;
	}

	/// <summary>
	/// Whether package shaders (a <c>.shader_c</c> an older engine compiled, with its source embedded) draw as the surface
	/// <see cref="ShaderConverter"/> makes of that source, where it makes one that compiles; their other materials draw as
	/// before. Off by default (<c>-package-surfaces</c> turns it on). Read by each device as it's made.
	/// </summary>
	public static bool ConvertPackagesByDefault { get; set; } = BackendDevice.HasSwitch( "-package-surfaces" );

	/// <summary>Whether this device draws package shaders as surfaces; from <see cref="ConvertPackagesByDefault"/>.</summary>
	public bool ConvertPackages { get; set; } = ConvertPackagesByDefault;

	readonly Dictionary<string, string> packageSurfaces = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>The surface converted from a package shader's embedded source, made once per shader path; null when there's none.</summary>
	string PackageSurface( string shader )
	{
		lock ( packageSurfaces )
		{
			if ( packageSurfaces.TryGetValue( shader, out var known ) ) return known;
		}

		string path = null;
		try
		{
			var compiled = shader.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) ? shader : shader + "_c";
			if ( device.Content.TryRead( compiled ) is { } data && ShaderFile.ReadOutdated( data, compiled ) is { Outdated: not null } file )
				path = PackageSurfaceFile( shader, file, Path.Combine( Compiler().CacheFolder, "packages" ) );
		}
		catch ( Exception e ) when ( e is IOException or InvalidDataException or UnauthorizedAccessException )
		{
			Error?.Invoke( $"{shader}: not converted ({e.Message})" );
		}

		lock ( packageSurfaces ) return packageSurfaces[shader] = path;
	}

	/// <summary>
	/// What <see cref="ShaderConverter"/> makes of a package shader's embedded source (<see cref="ShaderConverter.FromEmbedded"/>),
	/// written to <paramref name="folder"/> by name and contents; null when it isn't a material the converter takes.
	/// </summary>
	internal static string PackageSurfaceFile( string shader, ShaderFile file, string folder )
	{
		var features = (file.Features?.StaticCombos ?? []).Select( c => (c.Name, c.Min, c.Max) );
		if ( ShaderConverter.FromEmbedded( file.EmbeddedSource, features ) is not { } text ) return null;

		// A module name: the file name as an identifier
		var stem = System.Text.RegularExpressions.Regex.Replace( Path.GetFileNameWithoutExtension( shader.Replace( '\\', '/' ) ), "[^a-zA-Z0-9_]", "_" );
		if ( stem.Length == 0 || !char.IsLetter( stem[0] ) ) stem = "s" + stem;
		var result = ShaderConverter.Convert( text, stem );
		if ( result.Source is null ) return null;

		var hash = System.Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( System.Text.Encoding.UTF8.GetBytes( result.Source ) ), 0, 6 ).ToLowerInvariant();
		Directory.CreateDirectory( folder );
		var path = Path.Combine( folder, $"{stem}_{hash}.slang" );
		if ( !File.Exists( path ) ) File.WriteAllText( path, result.Source );
		return path;
	}

	/// <summary>
	/// The shaders drawn as surfaces by default (<see cref="ConvertShaders"/>): by the surface written beside them
	/// (<c>standard.slang</c>; in the core shaders, else in a content folder's, as the citizen's <c>citizen_eye.shader</c>),
	/// or, for null, by what <see cref="ShaderConverter"/> makes of the core <c>.shader</c>.
	/// </summary>
	static readonly Dictionary<string, string> DefaultSurfaces = new( StringComparer.OrdinalIgnoreCase )
	{
		["complex"] = "standard.slang",
		["blendable"] = "blendable.slang",
		["generic"] = "standard.slang",
		["vertex_color"] = "vertex_color.slang",
		["static_overlay"] = "static_overlay.slang",
		["glass"] = "glass.slang",
		["foliage"] = "foliage.slang",
		["fur"] = "fur.slang",
		["skin"] = "skin.slang",
		["eyeball"] = "eyeball_surface.slang",
		["citizen_eye"] = "citizen_eye.slang",
		["simple"] = "standard.slang",
		["bark"] = null,
		["terrain_preview"] = "terrain_preview.slang",
		["water_simple"] = "water_simple.slang",
		["terrain"] = "terrain_surface.slang",
		["water"] = "water_surface.slang",
		["surface_test_rock"] = null,
	};

	/// <summary>
	/// Core shaders (by name, <c>eyeball</c>) whose materials load as their <c>.shader</c> even with <see cref="ConvertShaders"/>
	/// on, for comparing one surface against its shader while the others stay surfaces. Read as materials load. Starts with
	/// the names in <c>BOREALIS_SHADER_ROUTE</c> (<c>terrain,water</c>), so a game run can compare them too.
	/// </summary>
	public static HashSet<string> NotConvertedByDefault { get; } = new( (Environment.GetEnvironmentVariable( "BOREALIS_SHADER_ROUTE" ) ?? "").Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ), StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Specialise pipelines for each material's features (<see cref="Sandbox.ShaderCompiler.MaterialLayout.Features"/>), built in the background
	/// while the unspecialised pipeline draws: the default. Read by each device as it's made (<see cref="SpecializeFeatures"/>).
	/// </summary>
	public static bool SpecializeByDefault { get; set; } = true;

	/// <summary>Whether this device specialises surface pipelines for their materials' features; from <see cref="SpecializeByDefault"/>.</summary>
	public bool SpecializeFeatures { get; set; } = SpecializeByDefault;

	/// <summary>
	/// Make specialised pipelines on a worker while the unspecialised one draws (the default), or at once where they're first
	/// asked for: for tests and pictures that must not depend on when a worker finishes.
	/// </summary>
	public bool SpecializeInBackground { get; set; } = true;

	/// <summary>
	/// The single switch between the surface tier and the <c>.shader</c> route for the core shaders that have a surface:
	/// on (the default), their materials load as surfaces; off, as their <c>.shader</c>, for comparisons. Read by each
	/// device as it's made (<see cref="ConvertShaders"/>).
	/// </summary>
	public static bool ConvertByDefault { get; set; } = true;

	/// <summary>
	/// Whether this device loads the core shaders' materials as the surfaces standing in for them; from
	/// <see cref="ConvertByDefault"/>. Shaders given with <see cref="Convert"/> convert either way.
	/// </summary>
	public bool ConvertShaders { get; set; } = ConvertByDefault;

	readonly Dictionary<string, string> defaultPaths = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>A default surface's path: beside its .shader in the core shaders, or the converter's output in the cache.</summary>
	string DefaultSurface( string key, string surface )
	{
		lock ( defaultPaths )
		{
			if ( defaultPaths.TryGetValue( key, out var known ) ) return known;
			string path = null;
			try
			{
				path = DefaultSurfaceFile( key, surface, CoreShaders(), device.Content.Roots, Path.Combine( Compiler().CacheFolder, "converted" ), Error );
				if ( surface is not null && path is not null ) Watch( Path.GetDirectoryName( path ) );
			}
			catch ( InvalidOperationException e )
			{
				Error?.Invoke( $"{key}.shader: {e.Message}" );
			}

			return defaultPaths[key] = path;
		}
	}

	/// <summary>
	/// The file of the surface <paramref name="surface"/> drawing the core shader <paramref name="key"/> by default: beside it in
	/// <paramref name="shaders"/>, else in a content root's shaders; for a null surface, the core <c>.shader</c> converted
	/// (<see cref="ShaderConverter"/>) and written to <paramref name="converted"/> when it changed. Null when there's none.
	/// </summary>
	internal static string DefaultSurfaceFile( string key, string surface, string shaders, IEnumerable<string> roots, string converted, Action<string> error )
	{
		if ( surface is not null ) return new[] { shaders }.Concat( roots.Select( r => Path.Combine( r, "shaders" ) ) ).Select( f => Path.Combine( f, surface ) ).FirstOrDefault( File.Exists );
		if ( !File.Exists( Path.Combine( shaders, key + ".shader" ) ) ) return null;

		var result = ShaderConverter.Convert( File.ReadAllText( Path.Combine( shaders, key + ".shader" ) ), key, include => File.Exists( Path.Combine( shaders, include ) ) ? File.ReadAllText( Path.Combine( shaders, include ) ) : null );
		if ( result.Source is null )
		{
			error?.Invoke( $"{key}.shader: not converted ({string.Join( "; ", result.Notes )})" );
			return null;
		}

		// Written only when it changed, so its compile is cached too
		Directory.CreateDirectory( converted );
		var path = Path.Combine( converted, key + ".slang" );
		var text = result.Source.Replace( "\n", "\r\n" );
		if ( !File.Exists( path ) || File.ReadAllText( path ) != text ) File.WriteAllText( path, text );
		return path;
	}

	/// <summary>
	/// Every surface the core shaders' materials are drawn with by default, the converted ones written to
	/// <paramref name="converted"/>: what the build compiles offline (<see cref="OfflineShaders"/>).
	/// </summary>
	internal static List<string> DefaultSurfaceFiles( string shaders, IEnumerable<string> roots, string converted, Action<string> error ) =>
		DefaultSurfaces.Select( s => DefaultSurfaceFile( s.Key, s.Value, shaders, roots, converted, error ) ).Where( p => p is not null ).Distinct( StringComparer.OrdinalIgnoreCase ).ToList();

	/// <summary>
	/// A material of the surface in <paramref name="file"/> filled the way a converted <c>.vmat</c> is (see
	/// <see cref="Convert"/>): <paramref name="textures"/> (compiled texture paths) and <paramref name="values"/> by the
	/// <c>.shader</c>'s variable names. Null when the surface doesn't compile.
	/// </summary>
	public GpuMaterial CreateMaterial( string file, IReadOnlyDictionary<string, string> textures, IReadOnlyDictionary<string, float[]> values, string name = null )
	{
		if ( Compile( file ) is not { } compiled ) return null;
		var material = new Borealis.Source2.Materials.MaterialFile
		{
			Name = name ?? file,
			Shader = file,
			Ints = new Dictionary<string, int>(),
			Floats = new Dictionary<string, float>(),
			Vectors = values?.ToDictionary( v => v.Key, v => v.Value ) ?? new Dictionary<string, float[]>(),
			Textures = textures?.ToDictionary( t => t.Key, t => t.Value ) ?? new Dictionary<string, string>(),
			IntAttributes = new Dictionary<string, int>(),
			ShaderVersion = 1,
		};

		return ConvertedMaterial.Create( compiled, material, name ?? file, texture => device.LoadTexture( texture ) );
	}

	/// <summary>Whether any shader has a surface standing in for it.</summary>
	internal bool HasConversions
	{
		get
		{
			if ( ConvertShaders ) return true;
			lock ( programs ) return conversions.Count > 0;
		}
	}

	/// <summary>Materials loaded as the surface standing in for their shader.</summary>
	public int ConvertedMaterials => convertedMaterials;

	int convertedMaterials;
	readonly List<string> convertedPaths = new();

	/// <summary>Which materials of a converted shader load as the surface, by path; null for all. For comparisons.</summary>
	public Func<string, bool> ConvertMaterial { get; set; }

	/// <summary>The paths of the materials loaded as surfaces, in load order, for reports.</summary>
	public IReadOnlyList<string> ConvertedPaths
	{
		get
		{
			lock ( convertedPaths ) return convertedPaths.ToArray();
		}
	}

	internal void CountConverted( string path )
	{
		System.Threading.Interlocked.Increment( ref convertedMaterials );
		lock ( convertedPaths ) convertedPaths.Add( path );
	}

	/// <summary>Surface batches the GPU-driven path has taken, added up over every frame, for reports.</summary>
	public long GpuDrivenBatches => System.Threading.Interlocked.Read( ref gpuDrivenBatches );

	long gpuDrivenBatches;

	internal void CountGpuDriven() => System.Threading.Interlocked.Increment( ref gpuDrivenBatches );

	static string ShaderKey( string shader ) => Path.GetFileNameWithoutExtension( shader.Replace( '\\', '/' ) ).ToLowerInvariant();

	readonly Dictionary<string, string> conversions = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>Called with a shader's compile errors; the material then draws nothing.</summary>
	public Action<string> Error { get; set; } = message => Console.Error.WriteLine( message );

	/// <summary>
	/// Compile surfaces on the compile threads while their materials draw as the <c>.shader</c> they stand in for (the
	/// game's, unless <c>-wait-for-shaders</c>), or where first drawn, waiting (off, the default): for tests and pictures
	/// that must not depend on when a compile finishes. Read by each device as it's made (<see cref="CompileInBackground"/>).
	/// </summary>
	public static bool CompileInBackgroundByDefault { get; set; }

	/// <summary>Whether this device's draws wait for a surface to compile; from <see cref="CompileInBackgroundByDefault"/>.</summary>
	public bool CompileInBackground { get; set; } = CompileInBackgroundByDefault;

	/// <summary>Raised each time a program compiled in the background is ready, so what passed over its materials looks again.</summary>
	public int ReadyVersion => System.Threading.Volatile.Read( ref readyVersion );

	int readyVersion;

	/// <summary>Surfaces compiling now, for reports and tests.</summary>
	public int Compiling
	{
		get
		{
			lock ( programs ) return compiling.Count;
		}
	}

	/// <summary>
	/// Wait until no surface is compiling (one that starts meanwhile too), for tests and captures that need every material
	/// drawn as its surface; false if <paramref name="timeout"/> passed first.
	/// </summary>
	public bool WaitForCompiles( TimeSpan timeout )
	{
		var clock = System.Diagnostics.Stopwatch.StartNew();
		while ( true )
		{
			Task[] running;
			lock ( programs ) running = [.. compiling.Values];
			if ( running.Length == 0 ) return true;
			var left = timeout - clock.Elapsed;
			if ( left <= TimeSpan.Zero || !Task.WaitAll( running, left ) ) return false;
		}
	}

	/// <summary>
	/// Start compiling, in the background, every surface the core shaders' materials are drawn with by default
	/// (<see cref="ConvertShaders"/>), so a map's first draws find them ready or nearly so. Does nothing when the surfaces are
	/// compiled where first drawn instead.
	/// </summary>
	public void Warm()
	{
		if ( !ConvertShaders || !CompileInBackground ) return;
		foreach ( var (key, surface) in DefaultSurfaces )
		{
			if ( NotConvertedByDefault.Contains( key ) || DefaultSurface( key, surface ) is not { } found ) continue;
			var path = Key( found );
			lock ( programs )
			{
				if ( !programs.ContainsKey( path ) && !compiling.ContainsKey( path ) ) compiling[path] = Start( path, background: true );
			}
		}
	}

	/// <summary>
	/// Whether the pixel stage reads a material's record at the offset the vertex stage passes on (on, the default: what
	/// one GPU-driven bucket per pipeline needs) or at the draw's own (off: uniform per draw, for buckets split by
	/// material). A specialization constant: both forms are compiled into every surface.
	/// </summary>
	public bool MaterialOffsetPerPixel { get; set; } = true;

	/// <summary>The records every surface material's fields are in.</summary>
	internal SurfaceRecords Records { get; }

	/// <summary>The engine's Slang and core shaders, beside the content's <c>core</c> folder.</summary>
	SlangCompiler Compiler()
	{
		lock ( folders )
		{
			if ( compiler is not null ) return compiler;
			var shaders = CoreShaders();
			var game = Path.GetDirectoryName( Path.GetDirectoryName( shaders ) );
			Watch( shaders );
			CompileZones.Install();
			return compiler = new SlangCompiler( EngineFolders.NativeIn( game ), [shaders] )
			{
				CacheFolder = CacheFolder ?? Path.Combine( game, ".source2", "borealissurfaces" ),
				UseOffline = UseOffline,
				Compiling = what => Info?.Invoke( $"Borealis.Gpu: compiling {what} with Slang at run time (no offline compile)" ),
			};
		}
	}

	/// <summary>The engine's core shaders: in the content root with the surface library (a map's own package comes first in its content).</summary>
	string CoreShaders()
	{
		var core = device.Content.Roots.FirstOrDefault( r => File.Exists( Path.Combine( r, "shaders", "sbox", "surface.slang" ) ) ) ?? device.Content.Roots.FirstOrDefault()
			?? throw new InvalidOperationException( "No content folder to find the engine's shaders in" );
		return Path.Combine( core, "shaders" );
	}

	/// <summary>
	/// Where compiled surfaces are kept between runs; <c>game/.source2/borealissurfaces</c> beside the engine's own caches
	/// unless set before the first compile.
	/// </summary>
	public string CacheFolder { get; set; }

	/// <summary>
	/// Whether compiles read the build's offline compile of each source (<see cref="SlangOutput"/>) first; set before the
	/// first compile. Off, for tests of compiling itself, and with <c>-no-offline-shaders</c> to compare against run-time
	/// compiles.
	/// </summary>
	public bool UseOffline { get; set; } = !Environment.GetCommandLineArgs().Any( a => a.Equals( "-no-offline-shaders", StringComparison.OrdinalIgnoreCase ) );

	/// <summary>
	/// Told of each Slang compile here at run time, which the build's offline compile didn't cover: hotload, game code's
	/// shaders, package shaders. The console's output unless set.
	/// </summary>
	public Action<string> Info { get; set; } = message => Console.WriteLine( message );

	readonly List<FileSystemWatcher> watchers = new();
	readonly List<SurfaceProgram> retired = new();

	/// <summary>
	/// Hotload: a change to any file a program was compiled from (its module, a module it imports, a core include) drops
	/// the program, so the next draw of its materials compiles it again (from the cache when nothing it read changed).
	/// The dropped program stays alive until the device is disposed: frames in flight may still draw with it.
	/// </summary>
	void Watch( string folder )
	{
		if ( !Directory.Exists( folder ) ) return;
		lock ( watchers )
		{
			if ( watchers.Exists( w => string.Equals( w.Path, folder, StringComparison.OrdinalIgnoreCase ) ) ) return;
			var watcher = new FileSystemWatcher( folder ) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
			watcher.Changed += ( _, e ) => Changed( e.FullPath );
			watcher.Created += ( _, e ) => Changed( e.FullPath );
			watcher.Renamed += ( _, e ) => Changed( e.FullPath );
			watcher.EnableRaisingEvents = true;
			watchers.Add( watcher );
		}
	}

	/// <summary>Programs compiled again after a source change, for reports and tests.</summary>
	public int Hotloads => hotloads;

	int hotloads;

	void Changed( string path )
	{
		path = Path.GetFullPath( path );
		SlangCompiler.SourceChanged();
		lock ( programs )
		{
			// A compile in flight may have read what was there before: it looks when it's done (Start)
			foreach ( var changes in changedWhileCompiling.Values ) changes.Add( path );
			foreach ( var (file, layout) in layouts.ToArray() )
			{
				if ( layout is null || layout.Dependencies.Contains( path, StringComparer.OrdinalIgnoreCase ) ) layouts.Remove( file );
			}

			foreach ( var (file, program) in programs.ToArray() )
			{
				if ( program is null )
				{
					// One that failed: try again after any change
					programs.Remove( file );
					continue;
				}

				if ( !program.Compiled.Dependencies.Contains( path, StringComparer.OrdinalIgnoreCase ) ) continue;
				programs.Remove( file );
				program.Stale = true;
				retired.Add( program );
				hotloads++;
			}
		}
	}

	/// <summary>
	/// A package's shader folder: searched like <see cref="AddFolder"/>, watched for changes, and every surface and raw
	/// module in it compiled now on a worker, so its materials' first draws find them ready. Errors go to <see cref="Error"/>.
	/// </summary>
	public System.Threading.Tasks.Task Mount( string folder )
	{
		folder = Path.GetFullPath( folder );
		AddFolder( folder );
		Watch( folder );
		var files = Directory.Exists( folder ) ? Directory.GetFiles( folder, "*.slang" ) : [];
		Task<SurfaceProgram>[] started;
		lock ( programs )
		{
			started = files.Select( f => Path.GetFileName( f ) ).Where( f => !programs.ContainsKey( f ) ).Select( f => compiling.TryGetValue( f, out var running ) ? running : compiling[f] = Start( f, background: true ) ).ToArray();
		}

		return Task.WhenAll( started );
	}

	/// <summary>Compile a shader by file name (or path) and keep it, waiting for it; null, after reporting, when it doesn't compile.</summary>
	public CompiledShader Compile( string file ) => Program( file )?.Compiled;

	/// <summary>
	/// A shader's name and material layout (no passes), for loading its materials: its program's when it's compiled, else
	/// reflected alone (from the cache when it's there) while the program compiles in the background. Null, after
	/// reporting, when it doesn't reflect. Waits for the program instead when surfaces compile where first drawn.
	/// </summary>
	internal CompiledShader Layout( string file )
	{
		if ( !CompileInBackground ) return Compile( file );
		file = Key( file );
		lock ( programs )
		{
			if ( programs.TryGetValue( file, out var known ) ) return known?.Compiled;
			if ( layouts.TryGetValue( file, out var layout ) ) return layout;
		}

		var path = Find( file );
		if ( path is null ) return Compile( file );

		CompiledShader reflected = null;
		try
		{
			using ( Zones.SurfaceCompile.Start() ) reflected = Compiler().ReflectAsync( path ).GetAwaiter().GetResult();
		}
		catch ( ShaderCompileException e )
		{
			Error?.Invoke( $"{file}: {e.Errors}" );
		}

		lock ( programs )
		{
			layouts[file] = reflected;
			if ( reflected is not null && !programs.ContainsKey( file ) && !compiling.ContainsKey( file ) ) compiling[file] = Start( file, background: false );
			Hurry( file );
		}

		return reflected;
	}

	/// <summary>
	/// A material's program, compiled on first use; null when it can't draw, and while it compiles in the background (the
	/// material then draws as the <c>.shader</c> it stands in for, or not at all).
	/// </summary>
	internal SurfaceProgram For( SurfaceMaterial material )
	{
		// Kept on the material once found: draws ask for it every frame
		var known = material.Program;
		if ( known is { Stale: false } && ReferenceEquals( known.Device, device ) ) return known;

		// A material no model noted (one a game draws itself) notes its passes at its first draw, as static or classic: once,
		// not on every draw while its program compiles
		if ( known is null && !material.PassesNoted )
		{
			material.PassesNoted = true;
			NoteMaterial( material.Shader, material.RenderState, SurfaceDrawKinds.Static | SurfaceDrawKinds.Dynamic );
		}

		var program = CompileInBackground ? Ready( material.Shader ) : Program( material.Shader );

		// One a hotload dropped draws on while its source compiles again: it stays alive until the device is disposed
		if ( program is null ) return known is not null && ReferenceEquals( known.Device, device ) && IsCompiling( material.Shader ) ? known : null;
		if ( program.LayoutHash != material.LayoutHash || program.Compiled.Material.Size != material.RecordSize )
		{
			if ( program.Mismatched.Add( material.GetType() ) ) Error?.Invoke( $"{material.Shader}: {material.GetType().Name} was generated for another layout of {program.Compiled.Material.Type}; regenerate it" );
			return null;
		}

		material.Program = program;
		return program;
	}

	/// <summary>A shader's file: <paramref name="file"/> itself when it's a path, else the first of <see cref="Folders"/> that has it.</summary>
	string Find( string file )
	{
		if ( File.Exists( file ) ) return file;
		lock ( programs ) return folders.Select( f => Path.Combine( f, file ) ).FirstOrDefault( File.Exists );
	}

	/// <summary>Programs are kept by full path, or by file name for one found in <see cref="Folders"/>.</summary>
	static string Key( string file ) => Path.IsPathRooted( file ) ? Path.GetFullPath( file ) : file;

	bool IsCompiling( string file )
	{
		lock ( programs ) return compiling.ContainsKey( Key( file ) );
	}

	/// <summary>A shader's program, compiled now if it isn't, or waited for if it's compiling: null when it doesn't compile.</summary>
	SurfaceProgram Program( string file )
	{
		file = Key( file );
		Task<SurfaceProgram> task;
		lock ( programs )
		{
			if ( programs.TryGetValue( file, out var known ) ) return known;
			if ( !compiling.TryGetValue( file, out task ) ) compiling[file] = task = Start( file, background: false );
			Hurry( file );
		}

		using ( Zones.SurfaceCompile.Start() ) return task.GetAwaiter().GetResult();
	}

	/// <summary>A shader's program if it's ready (null when it doesn't compile); null while it compiles, which this starts.</summary>
	SurfaceProgram Ready( string file )
	{
		file = Key( file );
		lock ( programs )
		{
			if ( programs.TryGetValue( file, out var known ) ) return known;
			if ( !compiling.ContainsKey( file ) ) compiling[file] = Start( file, background: false );
			Hurry( file );
			return null;
		}
	}

	/// <summary>A compile warming in the background that a draw now waits for goes ahead of the other background work. Under the programs lock.</summary>
	void Hurry( string file )
	{
		if ( priorities.TryGetValue( file, out var priority ) ) priority.Urgent = true;
	}

	/// <summary>
	/// Compile a shader on the compile threads and make its program; kept in <see cref="programs"/> when done, unless a
	/// file it read changed meanwhile (<see cref="Changed"/>): then it's dropped, and the next draw starts again. Called
	/// under the programs lock.
	/// </summary>
	Task<SurfaceProgram> Start( string file, bool background )
	{
		changedWhileCompiling[file] = new List<string>();
		var priority = priorities[file] = background ? CompilePriority.Background() : new CompilePriority { Urgent = true };
		var compiler = Compiler();
		return Task.Run( async () =>
		{
			SurfaceProgram program = null;
			var path = Find( file );
			if ( path is null )
			{
				Error?.Invoke( $"{file}: no such shader in {string.Join( ", ", folders )}" );
			}
			else
			{
				try
				{
					var raw = SlangCompiler.IsRaw( File.ReadAllText( path ) );
					var compiled = await (raw ? compiler.CompileRawAsync( path, priority ) : compiler.CompileSurfaceAsync( path, priority )).ConfigureAwait( false );
					program = new SurfaceProgram( device, compiled ) { SourceFile = file };
					if ( Manifest?.Take( file, program ) is { Count: > 0 } requests ) program.Prewarm( requests );
					if ( compiled.GpuDrivenError is { } gpuDrivenError ) Error?.Invoke( $"{file}: no GPU-driven passes, its draws go the classic way: {gpuDrivenError}" );
				}
				catch ( ShaderCompileException e )
				{
					Error?.Invoke( $"{file}: {e.Errors}" );
				}
				catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException or InvalidOperationException or GpuException )
				{
					// Draws go on without it, as for a compile error
					Error?.Invoke( $"{file}: {e.Message}" );
				}
			}

			lock ( programs )
			{
				compiling.Remove( file );
				priorities.Remove( file );
				var changes = changedWhileCompiling.Remove( file, out var changed ) ? changed : [];

				// One that failed tries again after any change, as Changed has it
				var stale = program is null ? changes.Count > 0 : changes.Exists( c => program.Compiled.Dependencies.Contains( c, StringComparer.OrdinalIgnoreCase ) );
				if ( disposed || stale )
				{
					if ( program is not null ) retired.Add( program );
					program = null;
				}
				else
				{
					programs[file] = program;
				}
			}

			// Its guesses once it's kept: a material noted from now on finds it there (NoteMaterial), one before is in the notes.
			// Its own render state (the [Material] struct's defaults) on static geometry is noted first: what most of its
			// materials draw as, guessed now rather than when they load, which in a streaming map is a frame before they draw
			if ( program is not null && PrepareParts )
			{
				if ( program.Compiled.Tier == ShaderTier.Surface ) NoteMaterial( file, program.Compiled.Material.States().Resolve( new byte[program.Compiled.Material.Size] ), SurfaceDrawKinds.Static | SurfaceDrawKinds.StaticLightmapped );
				program.PrepareParts();
			}
			System.Threading.Interlocked.Increment( ref readyVersion );
			return program;
		} );
	}

	/// <summary>Destroy what was made, once the compiles in flight are done.</summary>
	public void Dispose()
	{
		lock ( watchers )
		{
			foreach ( var watcher in watchers ) watcher.Dispose();
			watchers.Clear();
		}

		Task[] running;
		lock ( programs )
		{
			disposed = true;
			running = [.. compiling.Values];
		}

		try
		{
			Task.WaitAll( running );
		}
		catch ( AggregateException )
		{
			// Each one's failure was reported
		}

		lock ( programs )
		{
			foreach ( var program in programs.Values ) program?.Dispose();
			foreach ( var program in retired ) program.Dispose();
			programs.Clear();
			retired.Clear();
		}

		Manifest?.Save();
		Records.Dispose();
	}
}

/// <summary>A compiled new-tier shader on a device: per pass, its modules and binding layouts, and pipelines made once.</summary>
internal sealed class SurfaceProgram : IDisposable
{
	readonly BackendDevice device;
	readonly Dictionary<string, PassObjects> passes = new();
	readonly Dictionary<(PassObjects Pass, VertexLayout Layout, Format Color, Format Depth, int Samples, ulong Constants, bool Ids, bool OpaqueFade, SurfaceRenderState State, bool DepthEqual, ulong Features, bool Normals), PipelineHandle> pipelines = new();
	readonly HashSet<(PassObjects Pass, VertexLayout Layout, Format Color, Format Depth, int Samples, ulong Constants, bool Ids, bool OpaqueFade, SurfaceRenderState State, bool DepthEqual, ulong Features, bool Normals)> building = new();
	readonly Dictionary<ulong, SurfaceFeatures> features = new();
	readonly List<System.Threading.Tasks.Task> builds = new();
	bool disposed;

	/// <summary>Material classes already reported as generated for another layout.</summary>
	public readonly HashSet<Type> Mismatched = new();

	public CompiledShader Compiled { get; }
	public uint LayoutHash { get; }

	/// <summary>The file it was compiled from, which the pipeline manifest names it by; null for one made otherwise.</summary>
	public string SourceFile { get; init; }

	/// <summary>Pipelines being made ahead from the manifest (<see cref="Prewarm"/>), by key, and whether a worker has started each.</summary>
	readonly Dictionary<(PassObjects Pass, VertexLayout Layout, Format Color, Format Depth, int Samples, ulong Constants, bool Ids, bool OpaqueFade, SurfaceRenderState State, bool DepthEqual, ulong Features, bool Normals), Prewarming> prewarming = new();

	sealed class Prewarming
	{
		public volatile bool Started;
		public System.Threading.Tasks.Task Task;
	}

	/// <summary>Pipelines made ahead from the manifest, for reports and the tests.</summary>
	public int Prewarmed;

	public sealed class PassObjects
	{
		public CompiledPass Pass;
		public ShaderHandle Vertex, Pixel;
		public BindingLayoutHandle[] Sets;
	}

	/// <summary>The device it's made on.</summary>
	public BackendDevice Device => device;

	/// <summary>Dropped by a hotload: its materials look their program up again.</summary>
	public bool Stale
	{
		get => System.Threading.Volatile.Read( ref stale );
		set => System.Threading.Volatile.Write( ref stale, value );
	}

	bool stale;

	public SurfaceProgram( BackendDevice device, CompiledShader compiled )
	{
		this.device = device;
		Compiled = compiled;
		LayoutHash = MaterialClassWriter.Hash( compiled.Material );
		Displaces = compiled.Generated is { } generated && (generated.Contains( "SurfaceVertexDisplaced", StringComparison.Ordinal ) || generated.Contains( "SurfaceOutputDisplacedWith", StringComparison.Ordinal ) || generated.Contains( "SurfaceStageVertex", StringComparison.Ordinal ));

		var modules = new Dictionary<CompiledStage, ShaderHandle>();
		ShaderHandle Module( CompiledStage stage )
		{
			if ( stage is null ) return default;
			if ( !modules.TryGetValue( stage, out var module ) ) modules[stage] = module = device.Gpu.CreateShader( stage.Spirv, $"{compiled.Name} {stage.Entry}" );
			return module;
		}

		foreach ( var (name, pass) in compiled.Passes )
		{
			var sets = new BindingLayoutHandle[ShaderAbi.BindlessSet];
			foreach ( var (stage, flags) in new[] { (pass.Vertex, ShaderStages.Vertex), (pass.Pixel, ShaderStages.Fragment) } )
			{
				if ( stage is null ) continue;
				var bySet = stage.Bindings.Where( b => b.Set < ShaderAbi.BindlessSet ).GroupBy( b => b.Set );
				foreach ( var group in bySet )
				{
					var slots = group.Select( b => new BindingSlot( b.Binding, Type( b ), flags, Math.Max( b.Count, 1 ) ) ).ToArray();
					sets[group.Key] = device.Gpu.CreateBindingLayout( slots );
				}
			}

			passes[name] = new PassObjects { Pass = pass, Vertex = Module( pass.Vertex ), Pixel = Module( pass.Pixel ), Sets = sets };
		}
	}

	static BindingType Type( ShaderBinding binding ) => binding.Kind switch
	{
		BindingKind.ConstantBuffer or BindingKind.Globals => BindingType.UniformBuffer,
		BindingKind.Buffer or BindingKind.MaterialRecords => BindingType.StorageBuffer,
		BindingKind.Sampler => BindingType.Sampler,
		BindingKind.StorageTexture => BindingType.StorageTexture,
		_ => BindingType.SampledTexture,
	};

	/// <summary>A pass by name, or null.</summary>
	public PassObjects Pass( string name ) => passes.TryGetValue( name, out var pass ) ? pass : null;

	/// <summary>
	/// What pipelines for <paramref name="material"/> are specialised for: its feature values, the same object for every
	/// material with the same values. Null when the shader has no features or the device doesn't specialise.
	/// </summary>
	public SurfaceFeatures Features( SurfaceMaterial material )
	{
		if ( !device.Surfaces.SpecializeFeatures ) return null;
		var fields = Compiled.Material.Features;
		if ( fields.Count == 0 ) return null;

		// Kept on the material until a field changes: buckets ask for every batch
		var version = material.Version;
		if ( material.Specialization is { } cached && cached.Version == version && ReferenceEquals( cached.Program, this ) ) return cached.Features;

		var (key, values) = material.Features( fields );
		SurfaceFeatures found;
		lock ( features )
		{
			if ( !features.TryGetValue( key, out found ) )
			{
				var constants = new SpecializationConstant[values.Length + 1];
				constants[0] = new SpecializationConstant( Sandbox.ShaderCompiler.MaterialLayout.SpecializedId, 1 );
				for ( int i = 0; i < values.Length; i++ ) constants[i + 1] = new SpecializationConstant( (uint)(Sandbox.ShaderCompiler.MaterialLayout.FirstFeatureId + i), (uint)values[i] );
				features[key] = found = new SurfaceFeatures( key, constants );
			}
		}

		material.Specialization = new SurfaceMaterial.SpecializedFor( version, this, found );
		return found;
	}

	/// <summary>Specialised pipelines made so far, and being made, for reports.</summary>
	public (int Made, int Building) Specialized
	{
		get
		{
			lock ( pipelines ) return (pipelines.Keys.Count( k => k.Features != 0 ), building.Count);
		}
	}

	readonly PassObjects[] gpuDrivenPasses = new PassObjects[32];
	readonly bool[] gpuDrivenLooked = new bool[32];

	/// <summary>
	/// A GPU-driven pass (<c>[Depth|Forward][AlphaTest|Translucent]GpuDriven[Lightmapped|Skinned]</c>), found once; null when
	/// there's none. <paramref name="translucent"/> is the colour pass of blended materials (S_TRANSLUCENT);
	/// <paramref name="vertexCache"/> draws skinned vertices from the vertex cache, and has no fallback.
	/// </summary>
	public PassObjects GpuDrivenPass( bool noColor, bool alphaTest, bool lightmapped, bool translucent = false, bool vertexCache = false )
	{
		translucent &= !noColor;
		var index = (vertexCache ? 16 : 0) | (translucent ? 8 : 0) | (noColor ? 4 : 0) | (alphaTest && !translucent ? 2 : 0) | (lightmapped && !vertexCache ? 1 : 0);
		if ( System.Threading.Volatile.Read( ref gpuDrivenLooked[index] ) ) return gpuDrivenPasses[index];

		var name = (noColor ? (alphaTest ? ShaderPasses.DepthAlphaTest : ShaderPasses.Depth) : translucent ? ShaderPasses.Forward + "Translucent" : (alphaTest ? ShaderPasses.Forward + "AlphaTest" : ShaderPasses.Forward)) + ShaderPasses.GpuDriven;
		var found = vertexCache ? Pass( name + ShaderPasses.Skinned ) : (lightmapped ? Pass( name + ShaderPasses.Lightmapped ) : null) ?? Pass( name );
		gpuDrivenPasses[index] = found;
		System.Threading.Volatile.Write( ref gpuDrivenLooked[index], true );
		return found;
	}

	/// <summary>
	/// Whether its vertex stage moves vertices (<c>IVertexSurface</c>, <c>IInterpolatedVertexSurface</c>) or is its own
	/// (<c>IVertexStageSurface</c>): when not, its positions alone place them.
	/// </summary>
	public bool Displaces { get; }

	/// <summary>
	/// A pipeline for a pass drawing vertices laid out as <paramref name="layout"/> into these formats, with a material's
	/// resolved render state <paramref name="state"/> in colour passes (its blending, depth test and write, culling);
	/// depth is tested greater-or-equal (reverse Z) and alpha-tested passes use alpha to coverage with multisampling, as
	/// today's shaders do. Depth-only passes cull by the state's two-sidedness and test and write depth.
	/// Mesh vertices are stream 0, instance ids stream 1, the default stream 2, as for classic draws. Specialization
	/// constants (<c>SpecializedConstant()</c> globals) are per pipeline, <paramref name="constantsKey"/> their values.
	/// With <paramref name="ids"/>, the visibility-driven forward pass's id prepass: target 0 an R8 scratch (its alpha drives
	/// alpha to coverage), target 1 the ids, as BackendDevice.Pipeline makes them. With <paramref name="depthEqual"/>, a
	/// colour pass whose depth a prepass already wrote with the same vertex stage: tested equal, not written.
	/// With <paramref name="normals"/>, the opaque pass's normals target follows colour: an opaque forward pass of a surface
	/// writes its normal and roughness there (<see cref="WritesNormals"/>), any other leaves it as it is.
	/// With <paramref name="specialized"/>, the pipeline specialised for those feature values once a worker has made it; the
	/// unspecialised one until then, so nothing waits for it.
	/// In a tools pass (<paramref name="tools"/>) a colour pass draws with the engine's pixel stage instead
	/// (<see cref="ToolsViewStage"/>), unspecialised and not kept for the next run.
	/// </summary>
	public PipelineHandle Pipeline( PassObjects pass, VertexLayout layout, Format color, Format depth, int samples, SpecializationConstant[] vertexConstants, SpecializationConstant[] pixelConstants, ulong constantsKey, SurfaceRenderState state, bool ids = false, bool opaqueFade = false, bool depthEqual = false, SurfaceFeatures specialized = null, bool normals = false, ToolsPass tools = ToolsPass.None )
	{
		// Only a pixel stage reads the features
		if ( !pass.Pixel.IsValid ) specialized = null;
		normals &= color != Format.Unknown && !ids;
		if ( tools != ToolsPass.None && color != Format.Unknown && !ids ) return ToolsPipeline( pass, layout, color, depth, samples, vertexConstants, constantsKey, state, opaqueFade, depthEqual, normals, tools );
		var key = (pass, layout, color, depth, samples, constantsKey, ids, opaqueFade, state, depthEqual, specialized?.Key ?? 0, normals);

		// Never waited for: one a worker is making from the manifest is asked for again here, its draws skipped until made
		// (GraphicsPipelineInfo.SkipUntilReady); the worker finds it made and drops its own
		lock ( pipelines )
		{
			if ( pipelines.TryGetValue( key, out var pipeline ) ) return pipeline;
			return MakePipeline( key, pass, layout, color, depth, samples, vertexConstants, pixelConstants, constantsKey, state, ids, opaqueFade, depthEqual, specialized, normals );
		}
	}

	readonly Dictionary<(PassObjects Pass, VertexLayout Layout, Format Color, Format Depth, int Samples, ulong Constants, bool OpaqueFade, SurfaceRenderState State, bool DepthEqual, bool Normals, ToolsPass Tools), PipelineHandle> toolsPipelines = new();

	PipelineHandle ToolsPipeline( PassObjects pass, VertexLayout layout, Format color, Format depth, int samples, SpecializationConstant[] vertexConstants, ulong constantsKey, SurfaceRenderState state, bool opaqueFade, bool depthEqual, bool normals, ToolsPass tools )
	{
		var key = (pass, layout, color, depth, samples, constantsKey, opaqueFade, state, depthEqual, normals, tools);
		lock ( pipelines )
		{
			if ( toolsPipelines.TryGetValue( key, out var made ) ) return made;
			var info = Info( pass, layout, color, depth, samples, vertexConstants, null, state, false, opaqueFade, depthEqual, normals );
			ToolsViewStage.Apply( device.Shaders, ref info, tools, ToolsViews.ShaderColor( Compiled.Name ) );
			using ( Zones.PipelineCreate.Start() ) made = device.Gpu.CreateGraphicsPipeline( info );
			return toolsPipelines[key] = made;
		}
	}

	/// <summary>
	/// Queue the shader parts of the passes its materials draw with (<see cref="SurfaceShaders.NoteMaterial"/>) for the
	/// device's builder threads (pre-rasterisation from its vertex stage, fragment from its pixel stage), with the
	/// specialization constants its draws most likely have (once a draw has looked them up), where the device links
	/// pipelines from parts (<see cref="DeviceCapabilities.PipelineLibraries"/>): a first draw's pipeline is then a fast
	/// link. A guess, so after what draws wait for and the part manifest's parts; a program the manifest has parts of is
	/// left to it (a pass it has none of wasn't drawn last time). Called again as materials note more passes: only the new
	/// ones are queued. A draw that needs a part a worker is making waits for it rather than make it again.
	/// </summary>
	public void PrepareParts()
	{
		if ( !device.Gpu.Capabilities.PipelineLibraries ) return;
		var wanted = device.Surfaces.Notes( SourceFile );
		if ( wanted == 0 || !device.Surfaces.ValuesLookedUp ) return;
		lock ( pipelines )
		{
			var fresh = wanted & ~guessedNotes;
			if ( disposed || fresh == 0 || passes.Values.Any( p => device.Gpu.PartsRecorded( p.Vertex ) || device.Gpu.PartsRecorded( p.Pixel ) ) ) return;
			guessedNotes |= fresh;
			var chosen = SurfacePassNotes.Resolve( fresh, this, device.GpuDriven );
			if ( chosen.Count == 0 ) return;
			builds.RemoveAll( b => b.IsCompleted );
			builds.Add( System.Threading.Tasks.Task.Run( () =>
			{
				foreach ( var (pass, _) in chosen )
				{
					if ( System.Threading.Volatile.Read( ref disposed ) ) return;
					if ( pass.Pass.Vertex is null || !pass.Vertex.IsValid ) continue;
					var perPixel = pass.Pass.Name.Contains( ShaderPasses.GpuDriven, StringComparison.Ordinal ) || device.Surfaces.MaterialOffsetPerPixel;
					var pixel = pass.Pixel.IsValid;
					var parts = (device.Gpu.PartsRecorded( pass.Vertex ) ? PipelineParts.None : PipelineParts.PreRasterization)
						| (pixel && !device.Gpu.PartsRecorded( pass.Pixel ) ? PipelineParts.FragmentShader : PipelineParts.None);
					if ( parts == PipelineParts.None ) continue;
					var info = new GraphicsPipelineInfo
					{
						VertexShader = pass.Vertex,
						VertexEntry = pass.Pass.Vertex.Entry,
						VertexConstants = BackendContext.LikelySurfaceConstants( pass.Pass.Vertex, perPixel, device.Surfaces.SpecializationValues ),
						FragmentShader = pixel ? pass.Pixel : default,
						FragmentEntry = pixel ? pass.Pass.Pixel.Entry : null,
						FragmentConstants = pixel ? BackendContext.LikelySurfaceConstants( pass.Pass.Pixel, perPixel, device.Surfaces.SpecializationValues ) : null,
						Sets = pass.Sets,
						Name = $"{Compiled.Name} {pass.Pass.Name}",
					};

					try
					{
						// GPU-driven passes first (most of what a map draws), then skinned ones, then the rest, across programs
						var order = pass.Pass.Name.Contains( ShaderPasses.GpuDriven, StringComparison.Ordinal ) ? 0 : pass.Pass.Name.Contains( ShaderPasses.Skinned, StringComparison.Ordinal ) ? 1 : 2;
						device.Gpu.QueuePipelineParts( info, parts, speculative: true, order );
					}
					catch ( Exception e ) when ( e is GpuException or ArgumentException or InvalidOperationException )
					{
						// Made again by the draw that needs it, which reports it
					}
				}
			} ) );
		}
	}

	// The passes (SurfacePassNotes) PrepareParts has queued guesses for, under the pipelines' lock
	uint guessedNotes;

	/// <summary>
	/// Make the manifest's pipelines for this program on workers (<see cref="SurfacePipelineManifest"/>), each through the
	/// building set as a specialised pipeline is: a draw that asks for one meanwhile waits for it if a worker has started
	/// it, or makes it itself. One that fails to make is dropped from the manifest.
	/// </summary>
	public void Prewarm( IReadOnlyList<SurfacePipelineManifest.Request> requests )
	{
		lock ( pipelines )
		{
			if ( disposed ) return;
			foreach ( var request in requests )
			{
				var r = request;
				var normals = r.Normals && r.Color != Format.Unknown && !r.Ids;
				var key = (r.Pass, r.Layout, r.Color, r.Depth, r.Samples, r.ConstantsKey, r.Ids, r.OpaqueFade, r.State, r.DepthEqual, 0ul, normals);
				if ( pipelines.ContainsKey( key ) || !building.Add( key ) ) continue;

				var ahead = new Prewarming();
				prewarming[key] = ahead;
				builds.RemoveAll( b => b.IsCompleted );
				builds.Add( ahead.Task = System.Threading.Tasks.Task.Run( () => MakeAhead( key, r, normals, ahead ) ) );
			}
		}
	}

	void MakeAhead( (PassObjects Pass, VertexLayout Layout, Format Color, Format Depth, int Samples, ulong Constants, bool Ids, bool OpaqueFade, SurfaceRenderState State, bool DepthEqual, ulong Features, bool Normals) key, SurfaceManifestRequest r, bool normals, Prewarming ahead )
	{
		lock ( pipelines )
		{
			// Made by a draw that didn't wait, or no longer wanted
			if ( disposed || pipelines.ContainsKey( key ) )
			{
				building.Remove( key );
				prewarming.Remove( key );
				return;
			}

			ahead.Started = true;
		}

		var made = default( PipelineHandle );
		try
		{
			using ( Zones.PipelineCreate.Start() ) made = device.Gpu.CreateGraphicsPipeline( Info( r.Pass, r.Layout, r.Color, r.Depth, r.Samples, r.VertexConstants, r.PixelConstants, r.State, r.Ids, r.OpaqueFade, r.DepthEqual, normals ) with { SkipUntilReady = false } );
		}
		catch ( Exception e ) when ( e is GpuException or InvalidOperationException or ArgumentException )
		{
			device.Surfaces.Manifest?.Drop( r, SourceFile );
		}

		lock ( pipelines )
		{
			building.Remove( key );
			prewarming.Remove( key );
			if ( !made.IsValid ) return;
			if ( disposed || pipelines.ContainsKey( key ) ) device.Gpu.Destroy( made );
			else
			{
				pipelines[key] = made;
				Prewarmed++;
			}
		}
	}

	// Pipeline's miss, apart so the hit (every draw) doesn't allocate the background build's closure. Under the pipelines' lock
	PipelineHandle MakePipeline( (PassObjects Pass, VertexLayout Layout, Format Color, Format Depth, int Samples, ulong Constants, bool Ids, bool OpaqueFade, SurfaceRenderState State, bool DepthEqual, ulong Features, bool Normals) key, PassObjects pass, VertexLayout layout, Format color, Format depth, int samples, SpecializationConstant[] vertexConstants, SpecializationConstant[] pixelConstants, ulong constantsKey, SurfaceRenderState state, bool ids, bool opaqueFade, bool depthEqual, SurfaceFeatures specialized, bool normals )
	{
		// What it draws, noted too, so the passes that go with it (its depth passes) are guessed
		if ( specialized is null && device.Surfaces.PrepareParts && device.Gpu.Capabilities.PipelineLibraries ) device.Surfaces.NoteDrawn( this, state, SurfacePassNotes.KindOf( pass.Pass.Name ) );

		PipelineHandle pipeline;
		{
			if ( specialized is not null )
			{
				var info = Info( pass, layout, color, depth, samples, vertexConstants, [.. pixelConstants ?? [], .. specialized.Constants], state, ids, opaqueFade, depthEqual, normals );
				if ( !device.Surfaces.SpecializeInBackground )
				{
					if ( BackendDevice.DumpSurfaceShaders is { } dumpNow ) Dump( dumpNow, pass, info.FragmentConstants, specialized );
					return pipelines[key] = device.Gpu.CreateGraphicsPipeline( info );
				}

				var unspecialized = Pipeline( pass, layout, color, depth, samples, vertexConstants, pixelConstants, constantsKey, state, ids, opaqueFade, depthEqual, normals: normals );
				if ( !disposed && building.Add( key ) )
				{
					builds.RemoveAll( b => b.IsCompleted );
					builds.Add( System.Threading.Tasks.Task.Run( () =>
					{
						if ( BackendDevice.DumpSurfaceShaders is { } dump ) Dump( dump, pass, info.FragmentConstants, specialized );

						// Made here, on a worker: the unspecialised one draws until then
						var whole = info with { SkipUntilReady = false };
						var made = device.Gpu.CreateGraphicsPipeline( whole );
						lock ( pipelines )
						{
							building.Remove( key );
							if ( disposed ) device.Gpu.Destroy( made );
							else pipelines[key] = made;
						}
					} ) );
				}

				return unspecialized;
			}

			using ( Zones.PipelineCreate.Start() ) pipeline = device.Gpu.CreateGraphicsPipeline( Info( pass, layout, color, depth, samples, vertexConstants, pixelConstants, state, ids, opaqueFade, depthEqual, normals ) );
			pipelines[key] = pipeline;

			// A miss, never a frame's every draw: kept for the next run to make ahead
			device.Surfaces.Manifest?.Record( SourceFile, new SurfaceManifestRequest( pass, layout, color, depth, samples, vertexConstants, pixelConstants, constantsKey, state, ids, opaqueFade, depthEqual, normals ) );
			return pipeline;
		}
	}

	void Dump( string folder, PassObjects pass, SpecializationConstant[] constants, SurfaceFeatures specialized )
	{
		var name = Path.Combine( folder, $"{Compiled.Name}_{pass.Pass.Name}_{specialized.Key:x16}" );
		if ( File.Exists( name + ".spv" ) || BackendDevice.Specialized( pass.Pass.Pixel.Spirv, constants ) is not { } spirv ) return;
		Directory.CreateDirectory( folder );
		File.WriteAllBytes( name + ".spv", spirv );
		var fields = Compiled.Material.Features;
		File.WriteAllText( name + ".txt", string.Join( ' ', specialized.Constants.Skip( 1 ).Select( ( c, i ) => $"{fields[i].Name}={c.Value}" ) ) );
	}

	/// <summary>
	/// Whether a pass's pixel stage writes the opaque pass's normals and roughness (<c>ForwardOutput</c>): a surface's
	/// opaque forward entries, which the engine generates. A raw module's entries are its author's, and write none.
	/// </summary>
	public bool WritesNormals( PassObjects pass ) => Compiled.Tier == ShaderTier.Surface
		&& pass.Pass.Pixel?.Entry is "ForwardPs" or "ForwardAlphaTestPs" or "ForwardPsGpuDriven" or "ForwardAlphaTestPsGpuDriven";

	GraphicsPipelineInfo Info( PassObjects pass, VertexLayout layout, Format color, Format depth, int samples, SpecializationConstant[] vertexConstants, SpecializationConstant[] pixelConstants, SurfaceRenderState state, bool ids, bool opaqueFade, bool depthEqual, bool normals )
	{
		// A vertex-cache draw reads its vertex ids from streams 2 and 3, as floats, and the default stream from 4, as
		// BackendDevice.Pipeline maps classic variants
		var vertexCache = layout.VertexCache;
		var inputs = pass.Pass.Vertex.Inputs.Select( i => new VertexInput( i.Location, Semantic( i.Semantic ), i.SemanticIndex, i.Kind == ScalarKind.Float ? SpirvScalar.Float : i.Kind == ScalarKind.Int ? SpirvScalar.Int : SpirvScalar.UInt, i.Components ) ).ToList();
		var attributes = ShaderDefaults.MapVertices( inputs, input =>
		{
			if ( input.Semantic == VertexSemantic.TexCoord && input.SemanticIndex == 13 ) return new VertexAttribute( 0, 1, VertexFormat.UInt, 0 );
			if ( vertexCache && input.Semantic == VertexSemantic.TexCoord && input.SemanticIndex is 14 or 15 ) return new VertexAttribute( 0, input.SemanticIndex == 14 ? 2 : 3, VertexFormat.Float, 0 );
			if ( layout.Find( input.Semantic, input.SemanticIndex ) is not { } field ) return null;
			if ( field.Format.IsInteger() != (input.Kind != SpirvScalar.Float) ) return null;
			return new VertexAttribute( 0, 0, field.Format, field.Offset );
		}, defaultBuffer: vertexCache ? 4 : 2 );

		var depthOnly = color == Format.Unknown && !ids;
		var pixel = pass.Pixel.IsValid;
		var info = new GraphicsPipelineInfo
		{
			VertexShader = pass.Vertex,
			VertexEntry = pass.Pass.Vertex.Entry,
			VertexConstants = vertexConstants,
			FragmentConstants = pixel ? pixelConstants : null,
			FragmentShader = pixel ? pass.Pixel : default,
			FragmentEntry = pixel ? pass.Pass.Pixel.Entry : null,
			Sets = pass.Sets,
			VertexBuffers = vertexCache
				? [new( layout.Stride ), new( 4, VertexStepRate.Instance ), new( 4 ), new( 4 ), new( 0 )]
				: [new( layout.Stride ), new( 4, VertexStepRate.Instance ), new( 0 )],
			Attributes = attributes,
			Topology = PrimitiveTopology.TriangleList,
			CullMode = state.TwoSided ? CullMode.None : CullMode.Back,
			FrontFace = FrontFace.CounterClockwise,
			DepthBias = depthOnly || ids,
			DepthTest = depth != Format.Unknown,
			DepthWrite = depth != Format.Unknown,
			DepthCompare = CompareFunction.GreaterEqual,
			Blend = ids ? [BlendState.Opaque, BlendState.Opaque] : [BlendState.Opaque],

			// Alpha-tested passes write today's alpha-to-coverage alpha (S_ALPHA_TEST), with multisampling only, as the
			// .shaders' AlphaToCoverageEnable render state does; so does a fading object's (D_OPAQUE_FADE), unless it blends
			AlphaToCoverage = samples > 1 && !state.DrawsTranslucent && (opaqueFade || pass.Pass.Name.Contains( "AlphaTest", StringComparison.Ordinal )),
			ColorFormats = ids ? [Format.R8Unorm, Format.R32Uint] : depthOnly ? [] : [color],
			DepthFormat = depth,
			Samples = samples,
			Name = $"{Compiled.Name} {pass.Pass.Name}",

			// A draw never waits for it: skipped until workers have made it
			SkipUntilReady = true,
		};

		if ( !depthOnly && !ids )
		{
			info.Blend = [Blend( state.Blend )];
			info.DepthTest = depth != Format.Unknown && state.DepthTest;
			info.DepthWrite = depth != Format.Unknown && state.DepthWrite;

			// Colour after a prepass that drew the same triangles with the same vertex stage: depth tested equal and not
			// written, as foliage.shader's colour pass, so an alpha-tested pass keeps early depth testing
			if ( depthEqual )
			{
				info.DepthWrite = false;
				info.DepthCompare = CompareFunction.Equal;
			}

			// The opaque pass's normals: written by an opaque surface (its pixel stage's output switched on), kept otherwise
			if ( normals ) BackendDevice.AddNormalsTarget( ref info, pixel && !state.DrawsTranslucent && WritesNormals( pass ) );
		}

		return info;
	}

	/// <summary>
	/// A blend mode's colour blending; alpha is blended as today's shaders leave it (source alpha, one), premultiplied
	/// alpha as glass sets it (one, one minus source alpha).
	/// </summary>
	internal static BlendState Blend( SurfaceBlend blend )
	{
		var (source, destination) = blend switch
		{
			SurfaceBlend.Alpha => (BlendFactor.SourceAlpha, BlendFactor.OneMinusSourceAlpha),
			SurfaceBlend.Premultiplied => (BlendFactor.One, BlendFactor.OneMinusSourceAlpha),
			SurfaceBlend.Additive => (BlendFactor.SourceAlpha, BlendFactor.One),
			SurfaceBlend.Multiply => (BlendFactor.Zero, BlendFactor.SourceColor),
			SurfaceBlend.Mod2x => (BlendFactor.DestinationColor, BlendFactor.SourceColor),
			SurfaceBlend.ModulateAdd => (BlendFactor.DestinationColor, BlendFactor.One),
			_ => (BlendFactor.One, BlendFactor.Zero),
		};

		if ( blend == SurfaceBlend.Opaque ) return BlendState.Opaque;
		var premultiplied = blend == SurfaceBlend.Premultiplied;
		return new BlendState
		{
			Enable = true,
			SourceColor = source,
			DestinationColor = destination,
			SourceAlpha = premultiplied ? BlendFactor.One : BlendFactor.SourceAlpha,
			DestinationAlpha = premultiplied ? BlendFactor.OneMinusSourceAlpha : BlendFactor.One,
		};
	}

	internal static VertexSemantic Semantic( string name ) => name switch
	{
		"POSITION" => VertexSemantic.Position,
		"BLENDWEIGHT" => VertexSemantic.BlendWeight,
		"BLENDINDICES" => VertexSemantic.BlendIndices,
		"NORMAL" => VertexSemantic.Normal,
		"TANGENT" => VertexSemantic.Tangent,
		"BINORMAL" => VertexSemantic.Binormal,
		"COLOR" => VertexSemantic.Color,
		_ => VertexSemantic.TexCoord,
	};

	public void Dispose()
	{
		// Pipelines still being made finish first: the device they're made on may go next
		System.Threading.Tasks.Task[] running;
		lock ( pipelines )
		{
			disposed = true;
			running = builds.ToArray();
		}

		System.Threading.Tasks.Task.WaitAll( running );
		lock ( pipelines )
		{
			foreach ( var pipeline in pipelines.Values ) device.Gpu.Destroy( pipeline );
			foreach ( var pipeline in toolsPipelines.Values ) device.Gpu.Destroy( pipeline );
			pipelines.Clear();
			toolsPipelines.Clear();
		}
	}
}

/// <summary>
/// Feature values surface pipelines are specialised for (<see cref="SurfaceProgram.Features"/>): a key, and the
/// specialization constants that say so and carry the values.
/// </summary>
internal sealed class SurfaceFeatures( ulong key, SpecializationConstant[] constants )
{
	/// <summary>Tells value sets apart; never 0.</summary>
	public ulong Key { get; } = key;

	/// <summary>The specialised flag and each feature's value.</summary>
	public SpecializationConstant[] Constants { get; } = constants;
}

/// <summary>
/// Every surface material's record, in one storage buffer the shaders read at a per-draw offset
/// (<c>g_SurfaceMaterials</c>, <c>sbox/material.slang</c>). A record is written when its material is first drawn and again,
/// at a new offset, when it changes; the buffer only grows, so frames in flight keep reading what they bound. This is
/// the one place that knows where records live: gpudriven's material table (binding 63) replaces it when it lands.
/// </summary>
internal sealed class SurfaceRecords( Device gpu ) : IDisposable
{
	readonly Dictionary<SurfaceMaterial, (int Version, uint Offset)> written = new( ReferenceEqualityComparer.Instance );
	readonly List<BufferHandle> retired = new();
	BufferHandle buffer;
	long size, used;

	/// <summary>The buffer, valid once a record is written.</summary>
	public BufferHandle Buffer
	{
		get
		{
			lock ( written ) return buffer;
		}
	}

	/// <summary>Where <paramref name="material"/>'s current record is, written now if it changed.</summary>
	public uint Offset( SurfaceMaterial material ) => Offset( material, out _ );

	/// <summary>
	/// Where <paramref name="material"/>'s current record is, written now if it changed, and the buffer that holds it: the one
	/// a draw of it binds, since writing it may have grown the records into a new buffer.
	/// </summary>
	public uint Offset( SurfaceMaterial material, out BufferHandle holder )
	{
		lock ( written )
		{
			var version = material.Version;
			if ( written.TryGetValue( material, out var known ) && known.Version == version )
			{
				holder = buffer;
				return known.Offset;
			}

			var bytes = (Math.Max( material.RecordSize, 4 ) + 15) & ~15;
			if ( !buffer.IsValid || used + bytes > size ) Grow( used + bytes );

			var offset = (uint)used;
			Span<byte> record = stackalloc byte[bytes];
			record.Clear();
			material.WriteRecord( record, static ( texture, srgb ) => texture.ViewIndex( srgb, TextureViewDimension.Default ) );
			record.CopyTo( gpu.Map( buffer )[(int)offset..] );
			used += bytes;
			written[material] = (version, offset);
			holder = buffer;
			return offset;
		}
	}

	void Grow( long needed )
	{
		var next = Math.Max( 64 * 1024, size * 2 );
		while ( next < needed ) next *= 2;
		var grown = gpu.CreateBuffer( new BufferInfo { Size = next, Usage = BufferUsage.Storage, Memory = MemoryLocation.Upload, Name = "Borealis surface material records" } );
		if ( buffer.IsValid )
		{
			// The old buffer is kept until the records are disposed: draws recorded earlier this frame bound it, and a
			// destroy is only deferred past what's been submitted. It only doubles a few times, so this is at most its size.
			gpu.Map( buffer )[..(int)used].CopyTo( gpu.Map( grown ) );
			retired.Add( buffer );
		}

		buffer = grown;
		size = next;
		Grows++;
	}

	/// <summary>Times the records moved into a bigger buffer, for tests.</summary>
	internal int Grows { get; private set; }

	public void Dispose()
	{
		lock ( written )
		{
			if ( buffer.IsValid ) gpu.Destroy( buffer );
			foreach ( var old in retired ) gpu.Destroy( old );
			retired.Clear();
			buffer = default;
		}
	}
}
