using Borealis.Gpu;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Borealis.Source2.Shaders;

/// <summary>
/// s&amp;box's compiled shaders on a <see cref="Device"/>: loads <c>.shader_c</c> files by path, picks variants, and
/// owns the shader modules and binding layouts made for them (one module per distinct SPIR-V, one layout per distinct
/// set). Thread-safe.
/// </summary>
public sealed class ShaderLibrary : IDisposable
{
	readonly Device gpu;
	readonly Dictionary<string, ShaderFile> files = new( StringComparer.OrdinalIgnoreCase );

	// Loads in progress, so threads asking for the same shader at once load (and recompile) it once
	readonly Dictionary<string, Lazy<(ShaderFile Shader, bool Found)>> loading = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<byte[], ShaderHandle> modules = new( ReferenceEqualityComparer.Instance );

	// Modules with outputs left out (ModuleWithoutOutputs), by SPIR-V, then the locations left out
	readonly Dictionary<byte[], Dictionary<uint, ShaderHandle>> fittedModules = new( ReferenceEqualityComparer.Instance );
	readonly ConditionalWeakTable<byte[], SpirvReflection> reflections = new();
	readonly Dictionary<string, BindingLayoutHandle> layouts = new();

	/// <summary>Where shaders are found.</summary>
	public ContentFiles Files { get; }

	/// <summary>The device modules and layouts are made on.</summary>
	public Device Gpu => gpu;

	/// <summary>Load shaders from <paramref name="files"/> onto <paramref name="gpu"/>.</summary>
	public ShaderLibrary( Device gpu, ContentFiles files )
	{
		this.gpu = gpu;
		Files = files;
		if ( gpu.PartManifestEnabled && gpu.PipelineCachePath is { } cache ) moduleManifest = new ShaderModuleManifest( Path.Combine( Path.GetDirectoryName( cache ) ?? ".", "shader-modules.txt" ) );
	}

	// The modules variants made, so the next run makes them as their files load (ShaderModuleManifest): with the device's
	// part manifest only, which makes their pipeline parts once they exist
	readonly ShaderModuleManifest moduleManifest;
	readonly List<Task> modulesAhead = new();

	/// <summary>Write the module manifest beside the pipeline cache if it changed; false when nothing was written.</summary>
	public bool SaveModuleManifest() => moduleManifest?.Save() ?? false;

	/// <summary>What the module manifest held and did, for reports: lines read, modules made ahead, lines dropped, modules recorded.</summary>
	public (int Loaded, int Made, int Dropped, int Recorded) ModuleManifest => moduleManifest is { } m ? (m.Loaded, m.Made, m.Dropped, m.Recorded) : default;

	/// <summary>A variant's stage made its module: kept for the next run (once per module, never per frame).</summary>
	internal void Remember( ShaderProgram program, ulong staticCombo, ulong dynamicCombo )
	{
		if ( moduleManifest is { } m && program.Shader?.Name is { Length: > 0 } file ) m.Use( file, new ShaderModuleManifest.Entry( program.Type, staticCombo, dynamicCombo ) );
	}

	/// <summary>A compiled file loaded: the modules earlier runs' variants made from it are made now, on a worker.</summary>
	void MakeRecordedModules( ShaderFile shader )
	{
		if ( shader is null || moduleManifest?.Take( shader.Name ) is not { } entries ) return;
		var task = Task.Run( () =>
		{
			foreach ( var entry in entries )
			{
				try
				{
					var program = shader[entry.Type];
					var bytecode = program?.LoadCombo( entry.Static ).Dynamic( entry.Dynamic )?.Shader;
					if ( bytecode is null )
					{
						moduleManifest.Drop( shader.Name, entry );
						continue;
					}

					Module( bytecode, $"{shader.Name} {entry.Type}" );
					System.Threading.Interlocked.Increment( ref moduleManifest.Made );
				}
				catch ( Exception e ) when ( e is KeyNotFoundException or InvalidDataException or IndexOutOfRangeException )
				{
					moduleManifest.Drop( shader.Name, entry );
				}
				catch ( Exception e ) when ( e is GpuException or ArgumentException or ObjectDisposedException )
				{
					// The device or library went meanwhile
					return;
				}
			}
		} );

		lock ( modulesAhead )
		{
			modulesAhead.RemoveAll( t => t.IsCompleted );
			modulesAhead.Add( task );
		}
	}

	/// <summary>
	/// A shader by path (<c>shaders/complex.shader</c>, with or without <c>_c</c>) or by bare name (<c>downsample_spd</c>),
	/// loaded once; null if there's no compiled file for it. Threads asking for one at once share one load.
	/// </summary>
	public ShaderFile Load( string path )
	{
		var key = path.Replace( '\\', '/' );
		if ( key.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) ) key = key[..^2];
		Lazy<(ShaderFile Shader, bool Found)> load;
		lock ( files )
		{
			if ( files.TryGetValue( key, out var cached ) ) return cached;
			if ( !loading.TryGetValue( key, out load ) ) loading[key] = load = new( () => LoadUncached( key ) );
		}

		try
		{
			var (shader, found) = load.Value;
			lock ( files )
			{
				if ( found ) files[key] = shader;
			}

			MakeRecordedModules( shader );
			return shader;
		}
		finally
		{
			lock ( files ) loading.Remove( key );
		}
	}

	readonly Dictionary<string, byte[]> added = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// A shader compiled while running (the engine's <c>Shader.CompileAsync</c>, a hotloaded <c>.shader</c>), by the path its
	/// materials name, from its compiled bytes: it replaces whatever was loaded or added under that path, so later lookups
	/// and variants draw the new one. False when these bytes are already the ones there. Throws as
	/// <see cref="ShaderFile.Read"/> does for bytes that aren't a shader this engine reads.
	/// </summary>
	public bool Add( string path, byte[] compiled )
	{
		ArgumentNullException.ThrowIfNull( compiled );
		var key = path.Replace( '\\', '/' );
		if ( key.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) ) key = key[..^2];
		var hash = System.Security.Cryptography.SHA256.HashData( compiled );
		lock ( files )
		{
			if ( added.TryGetValue( key, out var known ) && known.AsSpan().SequenceEqual( hash ) ) return false;
		}

		var shader = ShaderFile.Read( compiled, key );
		lock ( files )
		{
			files[key] = shader;
			added[key] = hash;
		}

		return true;
	}

	(ShaderFile Shader, bool Found) LoadUncached( string key )
	{
		// A bare name (downsample_spd), as the engine's Material.FromShader takes one, is a shader under shaders/
		var full = Files.Find( key );
		var bare = !key.Contains( '/' ) && !key.EndsWith( ".shader", StringComparison.OrdinalIgnoreCase );
		if ( full is null && bare ) full = Files.Find( $"shaders/{key}.shader" );
		if ( full is not null )
		{
			// A standalone module describes its shader itself: the compiled file beside it is only Source 2's renderer's
			if ( Modules?.DescribeStandalone( full ) is { } standalone ) return (standalone, true);
			return (LoadOrUpgrade( full ), true);
		}

		// No compiled file: a shader with only a Slang module, where its .shader_c would be
		if ( Modules is not { } modules ) return (null, false);
		foreach ( var root in Files.Roots )
		{
			var compiled = Path.Combine( root, (bare ? $"shaders/{key}.shader" : key).TrimStart( '/' ) + "_c" );
			if ( modules.Describe( Path.GetFullPath( compiled ) ) is { } described ) return (described, true);
		}

		return (null, false);
	}

	/// <summary>
	/// Recompiles a shader an older engine compiled (<see cref="ShaderOutdatedException"/>, a package's) from the source
	/// embedded in it: given its compiled file and where to write the new one, true once written. Null recompiles
	/// nothing, and such a shader doesn't load: its materials draw the error shader rather than read bindings that moved.
	/// </summary>
	public Func<string, string, bool> Upgrader { get; set; }

	/// <summary>The folder <see cref="Upgrader"/>'s shaders are cached in, by name, the old file's hash and <see cref="UpgraderVersion"/>.</summary>
	public string UpgradeCache { get; set; }

	/// <summary>What the cached shaders were recompiled by: a different version recompiles them.</summary>
	public string UpgraderVersion { get; set; } = "";

	/// <summary>
	/// Told once for each shader that won't load (one an older engine compiled that nothing recompiles, or whose recompile
	/// failed), with why: its materials draw the error shader, as native's do.
	/// </summary>
	public Action<string> Unloaded { get; set; }

	readonly HashSet<string> unloaded = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// What a material whose shader won't load draws with, as native's material system draws it: the error shader, in its one
	/// mode (it stands in for every mode). Null without one.
	/// </summary>
	ShaderVariant ErrorVariant( Func<string, int?> dynamicCombos, Func<string, int?> attributes )
	{
		if ( Load( ErrorShader ) is not { Outdated: null } error || error.Modes.Count == 0 ) return null;
		return Variant( error, error.Modes[0].Name, [], dynamicCombos, attributes );
	}

	/// <summary>The shader native's material system falls back to.</summary>
	public const string ErrorShader = "shaders/error.shader";

	ShaderFile NotLoaded( string path, string why )
	{
		bool first;
		lock ( unloaded ) first = unloaded.Add( path );
		if ( first ) Unloaded?.Invoke( $"{path} won't load, so its materials draw the error shader: {why}" );
		return null;
	}

	ShaderFile LoadOrUpgrade( string path )
	{
		try
		{
			return ShaderFile.Load( path );
		}
		catch ( ShaderOutdatedException outdated )
		{
			// Its stages from a module made from the source embedded in it, compiled here: nothing recompiles the file
			if ( Modules is { } modules )
			{
				ShaderFile old = null;
				try
				{
					old = ShaderFile.LoadOutdated( path );
				}
				catch ( InvalidDataException )
				{
				}

				if ( old is not null && modules.Has( old ) ) return old;
			}

			if ( Upgrader is null || string.IsNullOrEmpty( UpgradeCache ) ) return NotLoaded( path, $"{outdated.Message}, and nothing here recompiles shaders (no ShaderCompiler)" );
		}

		return Upgraded( path );
	}

	readonly Dictionary<ShaderFile, Task<ShaderFile>> upgradedOf = new( ReferenceEqualityComparer.Instance );

	/// <summary>
	/// Whether <see cref="UpgradedOf"/>'s recompiles run on a worker, the shader drawing nothing meanwhile (the game's), rather
	/// than on the thread that first draws it: a package shader's took 9 s on the main thread.
	/// </summary>
	public bool UpgradeInBackground { get; set; }

	/// <summary>
	/// An outdated shader drawn from its embedded source whose module didn't compile: recompiled through
	/// <see cref="Upgrader"/> after all, once; null when that can't be had either, or with <paramref name="pending"/> while it's
	/// recompiling in the background (<see cref="UpgradeInBackground"/>).
	/// </summary>
	ShaderFile UpgradedOf( ShaderFile shader, out bool pending )
	{
		Task<ShaderFile> upgrade;
		var made = false;
		lock ( upgradedOf )
		{
			if ( !upgradedOf.TryGetValue( shader, out upgrade ) )
			{
				upgradedOf[shader] = upgrade = new Task<ShaderFile>( () => Upgrader is null || string.IsNullOrEmpty( UpgradeCache ) || !File.Exists( shader.Name )
					? NotLoaded( shader.Name, $"{shader.Outdated}, its embedded source doesn't compile here and nothing here recompiles shaders (no ShaderCompiler)" )
					: Upgraded( shader.Name ) );
				made = true;
			}
		}

		// Run outside the lock: a recompile takes seconds, and other shaders' are asked for meanwhile
		if ( made )
		{
			if ( UpgradeInBackground ) upgrade.Start();
			else upgrade.RunSynchronously();
		}

		pending = UpgradeInBackground && !upgrade.IsCompleted;
		return pending ? null : upgrade.GetAwaiter().GetResult();
	}

	/// <summary>Wait for every recompile started in the background (<see cref="UpgradeInBackground"/>). For tests and tools.</summary>
	public void WaitForUpgrades()
	{
		Task<ShaderFile>[] running;
		lock ( upgradedOf ) running = [.. upgradedOf.Values];
		Task.WaitAll( running );
	}

	/// <summary>An outdated shader file recompiled through <see cref="Upgrader"/> into <see cref="UpgradeCache"/>, or null.</summary>
	ShaderFile Upgraded( string path )
	{
		// By name and contents, so a changed package shader recompiles
		var hash = Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( File.ReadAllBytes( path ) ), 0, 8 );
		var version = Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( System.Text.Encoding.UTF8.GetBytes( UpgraderVersion ) ), 0, 4 );
		var name = System.Text.RegularExpressions.Regex.Replace( Path.GetFileNameWithoutExtension( path ), "[^a-zA-Z0-9_]", "_" );
		var upgraded = Path.Combine( UpgradeCache, $"{name}.{hash}.{version}.shader_c" ).ToLowerInvariant();

		// Failed with this compiler before: it stays unloaded
		if ( !File.Exists( upgraded ) && !File.Exists( upgraded + ".failed" ) )
		{
			bool written;
			try
			{
				Directory.CreateDirectory( UpgradeCache );
				written = Upgrader( path, upgraded ) && File.Exists( upgraded );
			}
			catch ( Exception )
			{
				written = false;
			}

			if ( !written )
			{
				try
				{
					Directory.CreateDirectory( UpgradeCache );
					File.WriteAllText( upgraded + ".failed", UpgraderVersion );
				}
				catch ( IOException )
				{
				}
			}
		}

		try
		{
			return File.Exists( upgraded ) ? ShaderFile.Load( upgraded ) : NotLoaded( path, $"recompiling it from its embedded source failed (see {upgraded}.failed)" );
		}
		catch ( InvalidDataException invalid )
		{
			return NotLoaded( path, $"its recompiled copy {upgraded} doesn't load: {invalid.Message}" );
		}
	}

	/// <summary>
	/// A GPU-driven variant's material textures are texture variables at this register plus their word in the material
	/// record (<c>BOREALIS_GPU_DRIVEN_TEXTURE_REGISTER</c>, <c>src/vfx/vfx_vulkan/borealis_gpu_driven.h</c>).
	/// </summary>
	public const int GpuDrivenTextureRegister = 1024;

	/// <summary>
	/// The GPU-driven variant's form: what vfx_vulkan's rewrite (<c>src/vfx/vfx_vulkan/borealis_gpu_driven.cpp</c>) makes
	/// of a shader. Raise it with any change there, so variants compiled at load and cached are compiled again.
	/// </summary>
	public const int GpuDrivenVersion = 4;

	readonly Dictionary<ShaderFile, ShaderFile> gpuDriven = new( ReferenceEqualityComparer.Instance );

	/// <summary>
	/// Compiles a shader's GPU-driven variant when there's none beside it (a package's shader): given the shader's
	/// compiled file and where to write the variant, true once written. Runs on a worker thread. Null compiles nothing.
	/// </summary>
	public Func<string, string, bool> GpuDrivenCompiler { get; set; }

	/// <summary>
	/// The folder <see cref="GpuDrivenCompiler"/>'s variants are cached in, by shader name, the shader file's hash and
	/// <see cref="GpuDrivenCompilerVersion"/>.
	/// </summary>
	public string GpuDrivenCache { get; set; }

	/// <summary>What the cached variants were compiled by: a different version compiles them again.</summary>
	public string GpuDrivenCompilerVersion { get; set; } = "";

	string GpuDrivenVersionTag => Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( System.Text.Encoding.UTF8.GetBytes( GpuDrivenCompilerVersion ) ), 0, 4 ).ToLowerInvariant();

	/// <summary>Delete <see cref="GpuDrivenCache"/>'s variants (and failure markers) from other compiler versions.</summary>
	public void PruneGpuDrivenCache()
	{
		if ( string.IsNullOrEmpty( GpuDrivenCache ) || !Directory.Exists( GpuDrivenCache ) ) return;

		var current = $".{GpuDrivenVersionTag}.gpudriven.shader_c";
		foreach ( var file in Directory.EnumerateFiles( GpuDrivenCache, "*.gpudriven.shader_c*" ) )
		{
			if ( file.Contains( current, StringComparison.OrdinalIgnoreCase ) ) continue;
			try
			{
				File.Delete( file );
			}
			catch ( IOException )
			{
				// Another process has it open: next time
			}
			catch ( UnauthorizedAccessException )
			{
			}
		}
	}

	readonly Dictionary<ShaderFile, string> gpuDrivenCachePaths = new( ReferenceEqualityComparer.Instance );
	readonly HashSet<ShaderFile> gpuDrivenPending = new( ReferenceEqualityComparer.Instance );
	readonly HashSet<ShaderFile> gpuDrivenCompiled = new( ReferenceEqualityComparer.Instance );

	/// <summary>
	/// A shader's GPU-driven variant (<c>ShaderCompiler -gpudriven</c>): <c>name.gpudriven.shader_c</c> beside its
	/// <c>.shader_c</c>, or one <see cref="GpuDrivenCompiler"/> made, loaded once; null when there's none. Without one
	/// beside it, the first call starts compiling it and returns null until it's ready (<see cref="GpuDrivenPending"/>).
	/// Same modes, features and combos as the shader.
	/// </summary>
	public ShaderFile GpuDriven( ShaderFile shader )
	{
		if ( shader is null ) return null;

		// A shader with only a module has its GPU-driven form there, if any (a .gpudriven.shader_c beside it is Source 2's
		// compile's, unread like its .shader_c)
		if ( shader.ModuleOnly ) return Modules?.HasGpuDriven( shader ) == true ? shader : null;

		lock ( gpuDriven )
		{
			if ( gpuDriven.TryGetValue( shader, out var cached ) ) return cached;
		}

		ShaderFile variant = null;
		const string suffix = ".shader_c";
		if ( shader.Name.EndsWith( suffix, StringComparison.OrdinalIgnoreCase ) )
		{
			var path = shader.Name[..^suffix.Length] + ".gpudriven" + suffix;
			if ( File.Exists( path ) )
			{
				variant = ShaderFile.Load( path );
			}
			else
			{
				if ( !CompiledGpuDriven( shader, out var compiled ) ) return null;
				if ( compiled is not null ) variant = ShaderFile.Load( compiled );
			}
		}

		lock ( gpuDriven ) gpuDriven[shader] = variant;
		MakeRecordedModules( variant );
		return variant;
	}

	/// <summary>Whether <paramref name="shader"/>'s GPU-driven variant is being compiled, so a null one isn't final yet.</summary>
	public bool GpuDrivenPending( ShaderFile shader )
	{
		lock ( gpuDrivenPending ) return shader is not null && gpuDrivenPending.Contains( shader );
	}

	/// <summary>
	/// The cached compiled variant of a shader with none beside it, in <paramref name="path"/> (null when it can't be
	/// had); false while it's compiling.
	/// </summary>
	bool CompiledGpuDriven( ShaderFile shader, out string path )
	{
		path = null;
		if ( GpuDrivenCompiler is not { } compiler || string.IsNullOrEmpty( GpuDrivenCache ) || !File.Exists( shader.Name ) ) return true;

		lock ( gpuDrivenPending )
		{
			// Pending until a lookup sees it finished, so nothing settles on null in between
			if ( gpuDrivenPending.Contains( shader ) )
			{
				if ( !gpuDrivenCompiled.Remove( shader ) ) return false;
				gpuDrivenPending.Remove( shader );
			}
			if ( !gpuDrivenCachePaths.TryGetValue( shader, out path ) )
			{
				// By name and contents, so a changed package shader compiles again
				var hash = Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( File.ReadAllBytes( shader.Name ) ), 0, 8 );
				var version = GpuDrivenVersionTag;
				var name = System.Text.RegularExpressions.Regex.Replace( Path.GetFileNameWithoutExtension( shader.Name ), "[^a-zA-Z0-9_]", "_" );
				path = Path.Combine( GpuDrivenCache, $"{name}.{hash}.{version}.gpudriven.shader_c" ).ToLowerInvariant();
				gpuDrivenCachePaths[shader] = path;
			}

			if ( File.Exists( path ) ) return true;

			// Failed with this compiler before: classic it stays
			if ( File.Exists( path + ".failed" ) )
			{
				path = null;
				return true;
			}

			gpuDrivenPending.Add( shader );
		}

		var source = shader.Name;
		var output = path;
		Task.Run( () =>
		{
			var written = false;
			try
			{
				written = compiler( source, output ) && File.Exists( output );
			}
			catch ( Exception )
			{
				written = false;
			}

			if ( !written )
			{
				try
				{
					Directory.CreateDirectory( Path.GetDirectoryName( output ) );
					File.WriteAllText( output + ".failed", GpuDrivenCompilerVersion );
				}
				catch ( IOException )
				{
				}
			}

			lock ( gpuDrivenPending ) gpuDrivenCompiled.Add( shader );
		} );

		path = null;
		return false;
	}

	/// <summary>
	/// The variant of <paramref name="shader"/> that draws <paramref name="mode"/> for a material with these feature
	/// values (by feature index; see <see cref="ShaderFile.FeatureIndex"/>), with dynamic combos from
	/// <paramref name="dynamicCombos"/> and specialization attributes from <paramref name="attributes"/>. Null when the
	/// shader has no such mode. Throws if the combo was never compiled. With <paramref name="gpuDriven"/>, from the
	/// shader's GPU-driven file (a mode's fallback shader's too); null when there's none.
	/// </summary>
	public ShaderVariant Variant( ShaderFile shader, string mode, ReadOnlySpan<int> features, Func<string, int?> dynamicCombos = null, Func<string, int?> attributes = null, bool gpuDriven = false )
		=> Variant( shader, mode, features, dynamicCombos, attributes, gpuDriven, out _ );

	/// <summary>
	/// <see cref="Variant(ShaderFile, string, ReadOnlySpan{int}, Func{string, int?}, Func{string, int?}, bool)"/>, saying
	/// with <paramref name="pending"/> that the shader's module is still compiling (<see cref="ModuleProgram.Pending"/>):
	/// the variant is then its compiled programs', or null when it has none, and isn't the one to keep.
	/// </summary>
	public ShaderVariant Variant( ShaderFile shader, string mode, ReadOnlySpan<int> features, Func<string, int?> dynamicCombos, Func<string, int?> attributes, bool gpuDriven, out bool pending )
	{
		pending = false;
		var drawMode = shader.Mode( mode );
		if ( drawMode is null ) return null;
		if ( !string.IsNullOrEmpty( drawMode.Fallback ) )
		{
			var fallback = Load( drawMode.Fallback.Contains( '/' ) ? drawMode.Fallback : $"shaders/{drawMode.Fallback}" );
			return fallback is null ? null : Variant( fallback, mode, [], dynamicCombos, attributes, gpuDriven, out pending );
		}

		// A shader with a module draws from it; one whose module doesn't compile draws from its programs, and so does one
		// whose module is compiling in the background (one with no programs to draw from draws nothing meanwhile)
		if ( !gpuDriven && Modules is { } modules && modules.Has( shader ) && modules.Compile( shader, drawMode, features, dynamicCombos, attributes ) is { } compiled )
		{
			if ( !ReferenceEquals( compiled, ModuleProgram.Pending ) ) return ModuleVariant( shader, drawMode, compiled );
			pending = true;
			if ( shader.Outdated is not null || shader.ModuleOnly ) return null;
		}

		// An outdated shader's programs follow other bindings: one whose embedded source didn't compile here (or with modules
		// off) is recompiled through the ShaderCompiler after all. Its GPU-driven variant is compiled from its file as any is
		if ( shader.Outdated is not null && !gpuDriven )
		{
			// Nothing drawn while it recompiles in the background, and not kept: it's asked again. One that won't load draws
			// the error shader
			var upgraded = UpgradedOf( shader, out var upgrading );
			if ( upgrading ) pending = true;
			return upgraded is not null ? Variant( upgraded, mode, features, dynamicCombos, attributes ) : upgrading ? null : ErrorVariant( dynamicCombos, attributes );
		}

		// A shader with only a module has no programs to fall back to (modules off, or its module doesn't compile); its
		// GPU-driven form is the module's
		if ( shader.ModuleOnly )
		{
			if ( !gpuDriven || Modules is not { } own || !own.HasGpuDriven( shader ) ) return null;
			var form = own.CompileGpuDriven( shader, drawMode, features, dynamicCombos, attributes );
			if ( ReferenceEquals( form, ModuleProgram.Pending ) )
			{
				pending = true;
				return null;
			}

			return form is null ? null : ModuleVariant( shader, drawMode, form );
		}

		if ( gpuDriven )
		{
			shader = GpuDriven( shader );
			if ( shader is null ) return null;
			drawMode = shader.Mode( mode );
			if ( drawMode is null ) return null;
		}

		// A shader with a geometry stage draws only from its module, whose vertex stage makes the primitives instead: its
		// programs' vertex stage alone would draw what it's given (points as points), so it draws nothing until then
		if ( shader[ShaderProgramType.Geometry] is not null && !GeometryPrograms ) return null;

		ShaderStage Stage( ShaderProgramType type, ReadOnlySpan<int> features )
		{
			var program = shader[type];
			if ( program is null ) return null;

			var (combo, dynamic) = Combos( shader, drawMode, program, features, dynamicCombos );
			return new ShaderStage( this, type, program, combo, dynamic, attributes, features );
		}

		var vertex = Stage( ShaderProgramType.Vertex, features );
		var pixel = Stage( ShaderProgramType.Pixel, features );
		var compute = Stage( ShaderProgramType.Compute, features );

		// Materials that resolve to the same combos and constants share one variant, so they share its layouts and
		// pipelines, and draws of them one after another don't switch pipeline. A GPU-driven variant (its combos are the
		// GPU-driven file's) holds nothing of a material's and is always shared: its draws share a multi-draw
		if ( !ShareVariants && !gpuDriven ) return new ShaderVariant( this, shader, drawMode, vertex, pixel, compute );
		var key = new VariantKey( drawMode, vertex?.Dynamic, pixel?.Dynamic, compute?.Dynamic, ConstantsKey( vertex, pixel, compute ) );
		lock ( variants )
		{
			if ( variants.TryGetValue( key, out var shared ) ) return shared;
			var variant = new ShaderVariant( this, shader, drawMode, vertex, pixel, compute );
			variants[key] = variant;
			return variant;
		}
	}

	/// <summary>
	/// A static and dynamic combo of one of <paramref name="shader"/>'s programs for a mode and feature values; with
	/// <paramref name="statesOnly"/>, without their shaders (<see cref="ShaderProgram.LoadComboStates"/>). Throws if the
	/// static combo was never compiled.
	/// </summary>
	static (StaticCombo Static, DynamicCombo Dynamic) Combos( ShaderFile shader, ShaderMode drawMode, ShaderProgram program, ReadOnlySpan<int> features, Func<string, int?> dynamicCombos, bool statesOnly = false )
	{
		var staticId = shader.StaticComboId( program, drawMode, features );
		if ( !program.HasCombo( staticId ) ) staticId = Allowed( shader, drawMode, program, features, staticId );
		if ( !program.HasCombo( staticId ) ) throw new InvalidDataException( $"{shader.Name}: {program.Type} static combo {staticId} for mode {drawMode.Name} wasn't compiled (skipped by its rules)" );
		var combo = statesOnly ? program.LoadComboStates( staticId ) : program.LoadCombo( staticId );

		var dynamicId = ShaderFile.DynamicComboId( program, dynamicCombos, features );
		var dynamic = combo.Dynamic( dynamicId ) ?? combo.Dynamic( 0 ) ?? throw new InvalidDataException( $"{shader.Name}: {program.Type} has no dynamic combo {dynamicId}" );
		return (combo, dynamic);
	}

	/// <summary>
	/// A combo its rules skip (eyeball.shader's Allow1( S_MODE_DEPTH, S_SPECULAR_CUBE_MAP ) in Depth mode): the feature
	/// combos the rules forbid there taken back to 0, the last first, as the rule leaves only one of them set.
	/// </summary>
	static ulong Allowed( ShaderFile shader, ShaderMode drawMode, ShaderProgram program, ReadOnlySpan<int> features, ulong id )
	{
		var values = features.ToArray();
		var combos = program.StaticCombos;
		for ( int c = combos.Length - 1; c >= 0; c-- )
		{
			var feature = combos[c].FeatureIndex;
			if ( combos[c].Source != 0 || feature < 0 || feature >= values.Length || values[feature] == 0 ) continue;
			values[feature] = 0;
			var allowed = shader.StaticComboId( program, drawMode, values );
			if ( program.HasCombo( allowed ) ) return allowed;
		}

		return id;
	}

	/// <summary>
	/// The render state of <see cref="Variant(ShaderFile, string, ReadOnlySpan{int}, Func{string, int?}, Func{string, int?}, bool)"/>'s
	/// variant for <paramref name="mode"/> with these feature values and no dynamic combos set (<see cref="ShaderVariant.State"/>),
	/// read without making it: no shaders read (the shared modules they reference stay compressed), no shader modules,
	/// layouts or reflection, and no module compile where the shader's module gives its render states
	/// (<see cref="IShaderModules.PixelRenderStates"/>). Null when the shader has no such mode. Throws as the variant does
	/// when the combo was never compiled.
	/// </summary>
	public ShaderPipelineState PixelState( ShaderFile shader, string mode, ReadOnlySpan<int> features ) => PixelState( shader, mode, features, out _ );

	/// <summary>
	/// <see cref="PixelState(ShaderFile, string, ReadOnlySpan{int})"/>, saying with <paramref name="pending"/> that the shader
	/// is still recompiling in the background (<see cref="UpgradeInBackground"/>): null then, and asked again later.
	/// </summary>
	public ShaderPipelineState PixelState( ShaderFile shader, string mode, ReadOnlySpan<int> features, out bool pending )
	{
		pending = false;
		var drawMode = shader.Mode( mode );
		if ( drawMode is null ) return null;
		if ( !string.IsNullOrEmpty( drawMode.Fallback ) )
		{
			var fallback = Load( drawMode.Fallback.Contains( '/' ) ? drawMode.Fallback : $"shaders/{drawMode.Fallback}" );
			return fallback is null ? null : PixelState( fallback, mode, [], out pending );
		}

		if ( Modules is { } modules && modules.Has( shader ) )
		{
			if ( modules.PixelRenderStates( shader, drawMode, features ) is { } states ) return ShaderPipelineState.Resolve( null, states, null );
			return Variant( shader, mode, features )?.State;
		}

		if ( shader.Outdated is not null ) return UpgradedOf( shader, out pending ) is { } upgraded ? PixelState( upgraded, mode, features, out pending ) : null;
		if ( shader.ModuleOnly ) return null;

		var program = shader[ShaderProgramType.Pixel];
		if ( program is null ) return ShaderPipelineState.Resolve( null );
		var (combo, dynamic) = Combos( shader, drawMode, program, features, null, statesOnly: true );
		return ShaderStage.PixelState( program, combo, dynamic, features );
	}

	/// <summary>
	/// Whether <see cref="PixelState(ShaderFile, string, ReadOnlySpan{int})"/>'s state needs a stencil
	/// (<see cref="ShaderPipelineState.NeedsStencil"/>). Throws as it does.
	/// </summary>
	public bool NeedsStencil( ShaderFile shader, string mode, ReadOnlySpan<int> features ) => NeedsStencil( shader, mode, features, out _ );

	/// <summary>
	/// <see cref="NeedsStencil(ShaderFile, string, ReadOnlySpan{int})"/>, false with <paramref name="pending"/> while the shader
	/// recompiles in the background: the answer isn't known yet.
	/// </summary>
	public bool NeedsStencil( ShaderFile shader, string mode, ReadOnlySpan<int> features, out bool pending ) => PixelState( shader, mode, features, out pending )?.NeedsStencil == true;

	/// <summary>
	/// Where shaders that have source modules are compiled from (pass modules: docs/reference/borealis-slang-materials.md);
	/// null draws every shader from its compiled programs.
	/// </summary>
	public IShaderModules Modules { get; set; }

	/// <summary>
	/// Whether a shader with a geometry stage gives variants from its programs (their vertex and pixel stages, without it),
	/// for comparing them with its module's; off, it draws only from its module.
	/// </summary>
	public bool GeometryPrograms { get; set; }

	readonly Dictionary<string, ShaderVariant> moduleVariants = new( StringComparer.Ordinal );

	ShaderVariant ModuleVariant( ShaderFile shader, ShaderMode mode, ModuleProgram program )
	{
		var key = $"{shader.Name}|{mode.Name}|{program.Key}";
		lock ( moduleVariants )
		{
			if ( moduleVariants.TryGetValue( key, out var known ) ) return known;
		}

		ShaderStage Stage( ModuleStage stage ) => stage is null ? null : new ShaderStage( this, shader, stage );
		var variant = new ShaderVariant( this, shader, mode, Stage( program.Vertex ), Stage( program.Pixel ), Stage( program.Compute ), fromModule: true, task: Stage( program.Task ), mesh: Stage( program.Mesh ) );
		lock ( moduleVariants ) return moduleVariants.TryAdd( key, variant ) ? variant : moduleVariants[key];
	}

	readonly record struct VariantKey( ShaderMode Mode, DynamicCombo Vertex, DynamicCombo Pixel, DynamicCombo Compute, string Constants );

	readonly Dictionary<VariantKey, ShaderVariant> variants = new();

	/// <summary>Whether materials that resolve to the same variant share it (on); off, each asks for its own. For comparisons.</summary>
	public bool ShareVariants { get; set; } = true;

	/// <summary>Variants made: one per distinct mode, combos and specialization constants, however many materials draw it.</summary>
	public int VariantCount
	{
		get
		{
			lock ( variants ) return variants.Count;
		}
	}

	static string ConstantsKey( ShaderStage vertex, ShaderStage pixel, ShaderStage compute )
	{
		var key = new System.Text.StringBuilder();
		foreach ( var stage in (ReadOnlySpan<ShaderStage>)[vertex, pixel, compute] )
		{
			key.Append( '|' );
			if ( stage is null ) continue;
			foreach ( var constant in stage.Constants ) key.Append( constant.Id ).Append( '=' ).Append( constant.Value ).Append( ',' );
			key.Append( stage.ExpressionStates );
		}

		return key.ToString();
	}

	/// <summary>The device module for a shader's SPIR-V, made once per distinct module.</summary>
	internal ShaderHandle Module( ShaderBytecode bytecode, string name )
	{
		var spirv = bytecode.Shared.Spirv;
		lock ( modules )
		{
			if ( modules.TryGetValue( spirv, out var module ) ) return module;
			module = gpu.CreateShader( spirv, name );
			modules[spirv] = module;
			return module;
		}
	}

	/// <summary>
	/// A stage's module without its outputs at <paramref name="locations"/> (a bit per location; <see cref="SpirvReflection.WithoutOutputs"/>),
	/// made once per module and locations: for a pipeline whose targets don't take them. The stage's own module where it
	/// has none there or they can't be left out.
	/// </summary>
	public ShaderHandle ModuleWithoutOutputs( ShaderStage stage, uint locations )
	{
		if ( stage?.Bytecode?.Shared.Spirv is not { } spirv || locations == 0 ) return stage?.Module ?? default;
		lock ( modules )
		{
			if ( !fittedModules.TryGetValue( spirv, out var fitted ) ) fittedModules[spirv] = fitted = new();
			// None made: the stage's own
			if ( !fitted.TryGetValue( locations, out var module ) )
			{
				module = SpirvReflection.WithoutOutputs( spirv, locations ) is { } changed ? gpu.CreateShader( changed, $"{stage} without outputs {locations:x}" ) : default;
				fitted[locations] = module;
			}

			return module.IsValid ? module : stage.Module;
		}
	}

	/// <summary>A shader's reflection, made once per distinct module.</summary>
	internal SpirvReflection Reflect( ShaderBytecode bytecode ) => reflections.GetValue( bytecode.Shared.Spirv, spirv => SpirvReflection.Read( spirv ) );

	/// <summary>A binding layout for these slots, shared by every variant that has the same set.</summary>
	internal BindingLayoutHandle Layout( List<BindingSlot> slots )
	{
		var key = string.Join( ';', slots.ConvertAll( s => $"{s.Binding},{(int)s.Type},{(int)s.Stages},{s.Count}" ) );
		lock ( layouts )
		{
			if ( layouts.TryGetValue( key, out var layout ) ) return layout;
			layout = gpu.CreateBindingLayout( [.. slots] );
			layouts[key] = layout;
			return layout;
		}
	}

	/// <summary>Destroy every module and layout made here.</summary>
	public void Dispose()
	{
		Task[] ahead;
		lock ( modulesAhead ) ahead = [.. modulesAhead];
		Task.WaitAll( ahead );
		SaveModuleManifest();
		lock ( modules )
		{
			foreach ( var module in modules.Values ) gpu.Destroy( module );
			modules.Clear();
			foreach ( var fitted in fittedModules.Values )
			{
				foreach ( var (_, module) in fitted )
				{
					if ( module.IsValid ) gpu.Destroy( module );
				}
			}

			fittedModules.Clear();
		}

		lock ( layouts )
		{
			foreach ( var layout in layouts.Values ) gpu.Destroy( layout );
			layouts.Clear();
		}
	}
}
