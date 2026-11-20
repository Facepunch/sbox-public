using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Sandbox.ShaderCompiler.Slang;

namespace Sandbox.ShaderCompiler;

/// <summary>What a compile is doing, for <see cref="SlangCompiler.Profile"/>.</summary>
public enum CompilePhase
{
	/// <summary>A surface or raw module, all its stages.</summary>
	Surface,

	/// <summary>One stage of a pass module.</summary>
	PassStage,

	/// <summary>A surface's material struct, reflected.</summary>
	Reflect,

	/// <summary>One stage's request: Slang, the optimizer and reflection.</summary>
	Stage,

	/// <summary>Slang compiling a request.</summary>
	Slang,

	/// <summary>The SPIR-V optimizer.</summary>
	Optimize,

	/// <summary>Reading a stage's bindings and inputs.</summary>
	Describe,

	/// <summary>Waiting for a Slang session.</summary>
	Session,

	/// <summary>Waiting for a surface's stages on the compile threads.</summary>
	Wait,

	/// <summary>Looking for a compiled shader in the disk cache.</summary>
	CacheLoad,

	/// <summary>Writing a compiled shader to the disk cache.</summary>
	CacheSave,

	/// <summary>Preprocessing a pass module for some combo values (what a variant's entry, render states and expansion read).</summary>
	Preprocess,
}

/// <summary>
/// Compiles new-tier shaders with the engine's Slang. A surface (<c>[Material]</c> struct implementing <c>ISurface</c>)
/// gets the engine's passes, instantiated for it in a generated instance file; a raw module is compiled as written, its
/// <c>[shader]</c> entry points mapped to passes by name. Both come out the same: SPIR-V per pass, bindings, and the
/// material record's layout. See docs/reference/borealis-slang-materials.md.
/// </summary>
public sealed partial class SlangCompiler
{
	readonly string slangDirectory;
	readonly List<string> searchPaths;

	/// <summary>
	/// A compiler using <c>slang.dll</c> from <paramref name="slangDirectory"/> (the engine's <c>game/bin/win64</c>),
	/// finding includes and modules in <paramref name="searchPaths"/> (the engine's <c>game/core/shaders</c> first).
	/// </summary>
	public SlangCompiler( string slangDirectory, IEnumerable<string> searchPaths )
	{
		this.slangDirectory = slangDirectory;
		this.searchPaths = searchPaths.Select( p => Path.GetFullPath( p ) ).ToList();
	}

	/// <summary>
	/// Where compiled shaders are kept between runs (null: nowhere). An entry is used only while the compiler
	/// (<see cref="Version"/>, its Slang and optimizer), the search paths, the source and every file the compile read are
	/// as they were.
	/// </summary>
	public string CacheFolder { get; init; }

	/// <summary>
	/// What this compiler makes of a source, for the caches' keys in place of this assembly's build, so a build that
	/// changes nothing here keeps every compiled shader. Raise it with any change to what a compile emits: the options,
	/// defines and passes, the generated instance and GPU-driven files, the source rewrites, reflection. A change to the
	/// generated text alone fails <c>GeneratedCodeNeedsAVersion</c> until <see cref="GeneratedFingerprint"/> is updated here too.
	/// </summary>
	public const int Version = 11;

	/// <summary>The <see cref="Fingerprint"/> of the generated code <see cref="Version"/> was raised for.</summary>
	internal const string GeneratedFingerprint = "A3C07B8547152C1E";

	/// <summary>
	/// A hash of what the compiler generates for fixed inputs (the Slang command lines, instance and GPU-driven files and
	/// source rewrites), to catch a change to them made without raising <see cref="Version"/>.
	/// </summary>
	internal static string Fingerprint()
	{
		var text = new StringBuilder();
		Dictionary<string, string>[] sets = [null, AlphaTested, Translucent, Lightmapped, VertexAnimation, GpuDriven, AlphaTestedDepth, With( GpuDriven, Lightmapped ), With( GpuDriven, VertexAnimation )];
		foreach ( var stage in Enum.GetValues<StageKind>() )
		{
			foreach ( var set in sets )
			{
				var defines = Defines( stage );
				foreach ( var (name, value) in set ?? [] ) defines[name] = value;
				text.AppendJoin( ' ', SlangRequest.Arguments( ["include"], defines, stage == StageKind.Vertex, true ) ).Append( '\n' );
			}
		}

		MaterialField[] features = [new( "Feature", MaterialFieldKind.Bool, 1, 0, [0], null, null, null, false ), new( "Count", MaterialFieldKind.UInt, 1, 4, [0], null, null, null, false )];
		for ( int flags = 0; flags < 1 << 10; flags++ )
		{
			bool Flag( int bit ) => (flags & (1 << bit)) != 0;
			text.Append( InstanceFile( "module", "Material", Flag( 0 ), Flag( 1 ), Flag( 2 ) ? "Opacity" : null, Flag( 3 ), Flag( 4 ), Flag( 5 ), Flag( 6 ), Flag( 7 ) ? [("A", "1")] : null, Flag( 8 ) ? features : null, Flag( 9 ) ) );
		}

		// Surfaces that own their vertex stage, shade themselves, or both
		foreach ( var (stage, shaded, clip) in new[] { (true, false, 0), (true, true, 2), (false, true, 0) } )
			text.Append( InstanceFile( "module", "Material", false, false, "Opacity", stage: stage, shaded: shaded, varyingFields: stage ? ["A", "B"] : null, clipDistances: clip ) );

		VertexInputSlot[] inputs = [new( 0, "POSITION", 0, ScalarKind.Float, 3 ) { Field = "i.vPositionOs" }, new( 1, "TEXCOORD", 13, ScalarKind.UInt, 1 ) { Field = "i.nInstance" }, new( 2, "BLENDINDICES", 0, ScalarKind.UInt, 4 ) { Field = "i.vIndices" }];
		foreach ( var (displaces, interpolated) in new[] { (false, false), (true, false), (false, true), (true, true) } ) text.Append( GpuDrivenVertex( "Material", displaces, interpolated, inputs ) );

		const string sample = "ExternalDescriptorSet g_viewSet Slot 2;\nfloat3 Tint < Attribute( \"Tint\" ); Default3( 1, 1, 1 ); >;\n"
			+ "CreateTexture2D( g_tColor ) < Attribute( \"Color\" ); SrgbRead( true ); Filter( MIN_MAG_MIP_LINEAR ); >;\n"
			+ "[Material] public struct Material : ISurface { [Default( 1, 1, 1 )] float3 Tint; Texture2D Color; float4 Shade( SurfaceInput i ) { return 1; } };\n"
			+ "struct V\n{\n\tfloat2 A;\n\tnointerpolation uint B;\n};\n[Material] public struct Staged : IVertexStageSurface\n{\n\ttypealias Varyings = V;\n\tfloat X;\n};\n";
		text.Append( VfxIncludes.Rewrite( sample, new() ) ).Append( MaterialSource.Rewrite( sample, out _ ) );
		foreach ( var header in SlangHeaders.Replaced.Keys.Order( StringComparer.OrdinalIgnoreCase ) ) text.Append( VfxIncludes.Rewrite( $"#include \"{header}\"\n", new() ) );
		return Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( Encoding.UTF8.GetBytes( text.ToString() ) ) )[..16];
	}

	/// <summary>
	/// Opened on the thread doing each phase of a compile and disposed when it ends, for a profiler (the engine's Tracy
	/// zones); null for none.
	/// </summary>
	public static Func<CompilePhase, IDisposable> Profile { get; set; }

	internal static IDisposable Zone( CompilePhase phase ) => Profile?.Invoke( phase );

	/// <summary>
	/// A shader source changed on disk (a watcher saw it): the next cache check reads every file's time again rather than
	/// trusting the last look, which is kept for up to <see cref="PassModule.IncludeCheckMs"/>.
	/// </summary>
	public static void SourceChanged() => PassCache.Recheck();

	/// <summary>
	/// How many requests compile at once, one Slang session each: half the cores, at most 8, unless set before the first
	/// compile (an offline compile uses every core).
	/// </summary>
	public static int CompileThreads
	{
		get => SlangApi.MaxSessions;
		set => SlangApi.MaxSessions = Math.Max( value, 1 );
	}

	/// <summary>Compiles in this process so far, by kind, from the compiler or from its cache, for reports and tests.</summary>
	public static (int Surfaces, int Stages, int CachedSurfaces, int CachedStages) Counts => (surfaces, stages, cachedSurfaces, cachedStages);

	static int surfaces, stages, cachedSurfaces, cachedStages;

	/// <summary>
	/// The result of <paramref name="task"/>, waited for in a <see cref="CompilePhase.Wait"/> zone: the synchronous form of
	/// each compile. Its continuations never need the waiting thread.
	/// </summary>
	static T Wait<T>( Task<T> task )
	{
		if ( !task.IsCompleted )
		{
			using ( Zone( CompilePhase.Wait ) ) ((IAsyncResult)task).AsyncWaitHandle.WaitOne();
		}

		return task.GetAwaiter().GetResult();
	}

	/// <summary>A task that completes when all of <paramref name="tasks"/> have, without throwing their exceptions.</summary>
	static Task Settled( Task[] tasks ) => Task.WhenAll( tasks ).ContinueWith( static _ => { }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default );

	/// <summary>A file read set safe to add to from several compile threads at once.</summary>
	sealed class ReadFiles( string first )
	{
		readonly HashSet<string> files = new( StringComparer.OrdinalIgnoreCase ) { first };

		public void Add( string path )
		{
			lock ( files ) files.Add( path );
		}

		public List<string> Sorted()
		{
			lock ( files ) return files.Order( StringComparer.OrdinalIgnoreCase ).ToList();
		}
	}

	/// <summary>
	/// Whether compiles read a source's offline compile (<see cref="SlangOutput"/>: <c>name.slang_c</c> beside it, or in the
	/// first search path for a source made at run time) before <see cref="CacheFolder"/>.
	/// </summary>
	public bool UseOffline { get; init; } = true;

	/// <summary>
	/// Told what's about to compile with Slang here, not read from an offline compile or the cache: a surface's or raw
	/// module's path, or a pass module's file, stage and combo values. For logging run-time compiles.
	/// </summary>
	public Action<string> Compiling { get; init; }

	/// <summary>The offline compiles of the source at <paramref name="path"/> that exist.</summary>
	IReadOnlyList<SlangOutput> Offline( string path )
	{
		if ( !UseOffline ) return [];
		var beside = SlangOutput.At( SlangOutput.PathOf( path ) );
		var shipped = searchPaths.Count > 0 ? SlangOutput.At( SlangOutput.PathOf( Path.Combine( searchPaths[0], Path.GetFileName( path ) ) ) ) : null;
		return beside is null ? (shipped is null ? [] : [shipped]) : shipped is null || ReferenceEquals( shipped, beside ) ? [beside] : [beside, shipped];
	}

	/// <summary>Where a compile of the module at <paramref name="path"/> found what it read, for naming it in cache entries.</summary>
	DependencyPaths Paths( string path ) => new( searchPaths, Path.GetDirectoryName( Path.GetFullPath( path ) ) );

	/// <summary>Told of every cache entry made or found, while an offline compile is made (<see cref="CompileOfflineAsync"/>).</summary>
	Action<string, byte[], List<(string Name, string Hash)>> saved;

	async Task<CompiledShader> Cached( string path, ShaderTier tier, Func<string, Task<CompiledShader>> compile )
	{
		path = Path.GetFullPath( path );
		var text = File.ReadAllText( path );
		using ( Zone( CompilePhase.CacheLoad ) )
		{
			if ( CompileCache.Load( CacheFolder, Offline( path ), Paths( path ), slangDirectory, tier, path, text ) is { } cached )
			{
				Interlocked.Increment( ref cachedSurfaces );
				if ( saved is not null ) CompileCache.Save( null, Paths( path ), slangDirectory, path, text, cached, saved: saved );
				return cached;
			}
		}

		Compiling?.Invoke( path );
		var compiled = await compile( path ).ConfigureAwait( false );
		using ( Zone( CompilePhase.CacheSave ) ) CompileCache.Save( CacheFolder, Paths( path ), slangDirectory, path, text, compiled, saved: saved );
		return compiled;
	}

	/// <summary>Whether a module's text is a raw module's (it declares <c>[shader( ... )]</c> entry points) rather than a surface's.</summary>
	public static bool IsRaw( string text ) => RawEntry().IsMatch( text );

	[GeneratedRegex( @"\[\s*shader\s*\(" )]
	private static partial Regex RawEntry();

	/// <summary>
	/// A surface's or raw module's name and material layout as its compile gives them, without its stages (no passes): from
	/// the cache's layout entry when it's there, else one Slang request on a compile thread. For loading materials while
	/// the stages compile. Faults with <see cref="ShaderCompileException"/> where the compile would.
	/// </summary>
	public Task<CompiledShader> ReflectAsync( string path, CompilePriority priority = null )
	{
		path = Path.GetFullPath( path );
		var text = File.ReadAllText( path );
		var tier = IsRaw( text ) ? ShaderTier.Raw : ShaderTier.Surface;
		using ( Zone( CompilePhase.CacheLoad ) )
		{
			if ( CompileCache.Load( CacheFolder, Offline( path ), Paths( path ), slangDirectory, tier, path, text, layout: true ) is { } cached ) return Task.FromResult( cached );
		}

		var source = MaterialSource.Rewrite( text, out var declared );
		if ( tier == ShaderTier.Surface ? declared.Count != 1 : declared.Count > 1 )
			return Task.FromException<CompiledShader>( new ShaderCompileException( path, $"A {(tier == ShaderTier.Surface ? "surface" : "raw")} module declares {(tier == ShaderTier.Surface ? "one" : "at most one")} [Material] struct; this one declares {declared.Count}" ) );

		var name = declared.Count == 1 ? declared[0].Name : Path.GetFileNameWithoutExtension( path );
		if ( declared.Count == 0 ) return Task.FromResult( new CompiledShader { Name = name, Tier = tier, Path = path, Material = MaterialLayout.Create( "", [] ), Passes = new Dictionary<string, CompiledPass>(), Dependencies = [path] } );

		return SlangWorkers.Run( () =>
		{
			var read = new ReadFiles( path );
			var layout = Reflected( path, source, name, tier, read );
			return new CompiledShader { Name = name, Tier = tier, Path = path, Material = layout, Passes = new Dictionary<string, CompiledPass>(), Dependencies = read.Sorted() };
		}, priority );
	}

	/// <summary>Defines vfx gives every program, which the core includes test.</summary>
	static Dictionary<string, string> Defines( StageKind stage ) => new()
	{
		["SHADER_MODEL"] = "60",
		["PROGRAM"] = stage switch { StageKind.Vertex => "1", StageKind.Compute => "6", StageKind.Task => "9", StageKind.Mesh => "10", _ => "2" },
		["VFX_PROGRAM_TS"] = "9",
		["VFX_PROGRAM_MS"] = "10",
		["D3D"] = "1",
		["VULKAN"] = "1",
		["DXC"] = "1",
		["SLANG"] = "1",
	};

	/// <summary>
	/// What Slang compiles for each file: vfx's include rewrite for HLSL, the material rewrite for modules, whose globals'
	/// annotations (a sampler's <c>Filter</c>) are recorded as the includes' are.
	/// </summary>
	static string Rewrite( string path, string text, Dictionary<string, Annotations> annotations )
	{
		if ( path.EndsWith( ".slang", StringComparison.OrdinalIgnoreCase ) ) return VfxIncludes.Rewrite( MaterialSource.Rewrite( text, out _ ), annotations );
		return VfxIncludes.Rewrite( text, annotations );
	}

	SlangRequest Request( StageKind stage, IReadOnlyList<string> extraSearchPaths, ReadFiles read, IReadOnlyDictionary<string, string> extraDefines = null )
	{
		Dictionary<string, Annotations> annotations = null;
		var defines = Defines( stage );
		foreach ( var (name, value) in extraDefines ?? new Dictionary<string, string>() ) defines[name] = value;
		var request = new SlangRequest( slangDirectory, [.. extraSearchPaths, .. searchPaths], defines, stage == StageKind.Vertex, SpirvOptimizer.Available( slangDirectory ), ( path, text ) =>
		{
			read?.Add( Path.GetFullPath( path ) );
			return Rewrite( path, text, annotations );
		} );
		annotations = request.Annotations;
		return request;
	}

	/// <summary>
	/// Compile a surface: the module at <paramref name="path"/> with one <c>[Material]</c> struct implementing
	/// <c>ISurface</c>. Throws <see cref="ShaderCompileException"/> with Slang's messages on errors.
	/// </summary>
	public CompiledShader CompileSurface( string path ) => Wait( CompileSurfaceAsync( path ) );

	/// <summary>
	/// <see cref="CompileSurface"/> on the compile threads, with its stages in parallel; <paramref name="priority"/> (null: urgent)
	/// orders it among the other compiles. Faults with <see cref="ShaderCompileException"/> on errors.
	/// </summary>
	public Task<CompiledShader> CompileSurfaceAsync( string path, CompilePriority priority = null ) => Cached( path, ShaderTier.Surface, p => CompileSurfaceNow( p, priority ) );

	async Task<CompiledShader> CompileSurfaceNow( string path, CompilePriority priority )
	{
		Interlocked.Increment( ref surfaces );
		var clock = Stopwatch.StartNew();
		var source = MaterialSource.Rewrite( File.ReadAllText( path ), out var declared );
		if ( declared.Count != 1 ) throw new ShaderCompileException( path, $"A surface module declares one [Material] struct; this one declares {declared.Count}" );

		var material = declared[0];
		var directory = Path.GetDirectoryName( path );
		var module = Path.GetFileNameWithoutExtension( path );
		if ( !ModuleName().IsMatch( module ) ) throw new ShaderCompileException( path, $"{module} isn't a valid module name: name surface files like identifiers" );

		{
			var read = new ReadFiles( path );
			// A surface that owns its vertex stage gets its Varyings' derivatives: their float fields, by reflection
			var varyingFields = material.Interfaces.Contains( "IVertexStageSurface" ) ? new List<string>() : null;
			var varyingsType = varyingFields is null ? null : VaryingsAlias().Match( source ) is { Success: true } alias ? alias.Groups["type"].Value : $"{material.Name}.Varyings";
			var layout = await SlangWorkers.Run( () => Reflected( path, source, material.Name, ShaderTier.Surface, read, varyingsType, varyingFields ), priority ).ConfigureAwait( false );
			var interpolatedDisplaces = material.Interfaces.Contains( "IInterpolatedVertexSurface" );
			var displaces = interpolatedDisplaces || material.Interfaces.Contains( "IVertexSurface" );
			var lit = material.Interfaces.Contains( "ILitSurface" );
			var interpolatedLayered = material.Interfaces.Contains( "IInterpolatedLayeredSurface" );
			var interpolatedCombiner = interpolatedLayered || material.Interfaces.Contains( "IInterpolatedCombinerSurface" );
			var interpolated = interpolatedCombiner || interpolatedDisplaces || material.Interfaces.Contains( "IInterpolatedSurface" );
			var sunLit = material.Interfaces.Contains( "ISunLitSurface" );
			var composite = material.Interfaces.Contains( "ICompositeSurface" );
			var layered = interpolatedLayered || material.Interfaces.Contains( "ILayeredSurface" );
			var combiner = layered || interpolatedCombiner || material.Interfaces.Contains( "ICombinerSurface" );
			var stage = material.Interfaces.Contains( "IVertexStageSurface" );
			var stageShaded = material.Interfaces.Contains( "IShadedVertexStageSurface" );
			var shaded = stageShaded || material.Interfaces.Contains( "IShadedSurface" );
			if ( !displaces && !lit && !interpolated && !sunLit && !composite && !combiner && !stage && !shaded && !material.Interfaces.Contains( "ISurface" ) )
				throw new ShaderCompileException( path, $"{material.Name} is a [Material] struct but implements none of ISurface, IInterpolatedSurface, IVertexSurface, ILitSurface, ISunLitSurface, ICompositeSurface, ICombinerSurface, IInterpolatedCombinerSurface, ILayeredSurface, IInterpolatedLayeredSurface, IVertexStageSurface, IShadedSurface or IShadedVertexStageSurface" );
			if ( stageShaded && !stage )
				throw new ShaderCompileException( path, $"{material.Name}: an IShadedVertexStageSurface is also an IVertexStageSurface, for its vertex stage" );
			if ( stage && material.Interfaces.Contains( "IShadedSurface" ) )
				throw new ShaderCompileException( path, $"{material.Name}: a surface that owns its vertex stage shades itself as an IShadedVertexStageSurface, not an IShadedSurface" );
			if ( (stage || shaded) && (displaces || lit || interpolated || sunLit || composite || combiner) )
				throw new ShaderCompileException( path, $"{material.Name}: an IVertexStageSurface or IShadedSurface can't also be an IVertexSurface, IInterpolatedSurface, ILitSurface, ISunLitSurface, ICompositeSurface or a combiner surface" );
			if ( interpolatedLayered && !material.Interfaces.Contains( "IInterpolatedSurface" ) && !interpolatedDisplaces )
				throw new ShaderCompileException( path, $"{material.Name}: an IInterpolatedLayeredSurface is also an IInterpolatedSurface or IInterpolatedVertexSurface, for its vertex stage" );
			if ( layered && interpolated && !interpolatedLayered )
				throw new ShaderCompileException( path, $"{material.Name}: an ILayeredSurface can't also be an IInterpolatedSurface (it's an IInterpolatedLayeredSurface)" );
			if ( combiner && (lit || sunLit || composite || (interpolated && !interpolatedCombiner)) )
				throw new ShaderCompileException( path, $"{material.Name}: an ICombinerSurface can't also be an ILitSurface, ISunLitSurface, ICompositeSurface or IInterpolatedSurface yet (an IInterpolatedCombinerSurface is both)" );
			if ( interpolated && ((displaces && !interpolatedDisplaces) || lit || sunLit || composite) )
				throw new ShaderCompileException( path, $"{material.Name}: an IInterpolatedSurface can't also be an IVertexSurface (it displaces as an IInterpolatedVertexSurface), ILitSurface, ISunLitSurface or ICompositeSurface yet" );
			if ( lit && sunLit )
				throw new ShaderCompileException( path, $"{material.Name}: a surface is an ILitSurface (every light) or an ISunLitSurface (the sun alone), not both" );
			if ( composite && (lit || sunLit) )
				throw new ShaderCompileException( path, $"{material.Name}: an ICompositeSurface can't also be an ILitSurface or ISunLitSurface yet" );

			var defines = CombinerDefines( source );
			if ( defines.Count > 0 && !combiner ) throw new ShaderCompileException( path, $"{material.Name}: [Combiner( ... )] is for an ICombinerSurface or IInterpolatedCombinerSurface" );
			defines.AddRange( LightingDefines( source ) );
			var clipDistances = ClipDistances( source );
			if ( clipDistances is < 0 or > 4 ) throw new ShaderCompileException( path, $"{material.Name}: [ClipDistances( n )] takes 1 to 4" );
			if ( clipDistances > 0 && !stage ) throw new ShaderCompileException( path, $"{material.Name}: [ClipDistances( n )] is for an IVertexStageSurface" );
			var instance = InstanceFile( module, material.Name, displaces, lit, layout.Fields.FirstOrDefault( f => f.Opacity )?.Name, interpolated, sunLit, composite, combiner, defines, layout.Features, layered, stage, shaded, varyingFields, clipDistances );
			var instancePath = Path.Combine( directory, $"{module}.instance.slang" );

			// Every stage at once, each a request on its own compile thread; the GPU-driven vertex stages once the classic ones
			// they're generated from are done. All are waited for before anything is thrown, the first failure in this order
			Task<CompiledStage> Entry( string text, string name, StageKind stage, IReadOnlyDictionary<string, string> defines = null ) =>
				SlangWorkers.Run( () => CompileEntry( instancePath, text, name, stage, directory, read, defines ), priority );
			var vertexJob = Entry( instance, "MainVs", StageKind.Vertex );
			var vertexLightmappedJob = stage ? vertexJob : Entry( instance, "MainVs", StageKind.Vertex, Lightmapped );
			var forwardJob = Entry( instance, "ForwardPs", StageKind.Pixel );
			var forwardAlphaTestJob = Entry( instance, "ForwardAlphaTestPs", StageKind.Pixel, AlphaTested );
			var depthAlphaTestJob = Entry( instance, "DepthAlphaTestPs", StageKind.Pixel, AlphaTestedDepth );
			var forwardTranslucentJob = Entry( instance, "ForwardTranslucentPs", StageKind.Pixel, Translucent );
			var depthVertexJob = Entry( instance, "DepthVs", StageKind.Vertex );
			var vertexSkinnedJob = stage ? null : Entry( instance, "MainVs", StageKind.Vertex, VertexAnimation );
			var depthVertexSkinnedJob = stage ? null : Entry( instance, "DepthVs", StageKind.Vertex, VertexAnimation );
			var gpuForwardJob = stage ? null : Entry( instance, "ForwardPsGpuDriven", StageKind.Pixel, GpuDriven );
			var gpuForwardAlphaTestJob = stage ? null : Entry( instance, "ForwardAlphaTestPsGpuDriven", StageKind.Pixel, With( GpuDriven, AlphaTested ) );
			var gpuDepthAlphaTestJob = stage ? null : Entry( instance, "DepthAlphaTestPsGpuDriven", StageKind.Pixel, With( GpuDriven, AlphaTestedDepth ) );
			var gpuForwardTranslucentJob = stage ? null : Entry( instance, "ForwardTranslucentPsGpuDriven", StageKind.Pixel, With( GpuDriven, Translucent ) );
			Task<CompiledStage> GpuVertex( Task<CompiledStage> classic, IReadOnlyDictionary<string, string> defines ) => classic.ContinueWith( done =>
				done.IsCompletedSuccessfully ? Entry( instance + GpuDrivenVertex( material.Name, displaces, interpolated, done.Result.Inputs ), "MainVsGpuDriven", StageKind.Vertex, defines ) : done, TaskScheduler.Default ).Unwrap();
			var gpuVertexJob = stage ? null : GpuVertex( vertexJob, GpuDriven );
			var gpuVertexLightmappedJob = stage ? null : GpuVertex( vertexLightmappedJob, With( GpuDriven, Lightmapped ) );
			var gpuVertexSkinnedJob = stage ? null : GpuVertex( vertexSkinnedJob, With( GpuDriven, VertexAnimation ) );
			Task<CompiledStage>[] jobs = [vertexJob, vertexLightmappedJob, forwardJob, forwardAlphaTestJob, depthAlphaTestJob, forwardTranslucentJob, depthVertexJob, vertexSkinnedJob, depthVertexSkinnedJob,
				gpuForwardJob, gpuForwardAlphaTestJob, gpuDepthAlphaTestJob, gpuForwardTranslucentJob, gpuVertexJob, gpuVertexLightmappedJob, gpuVertexSkinnedJob];
			await Settled( [.. jobs.Where( j => j is not null ).Distinct()] ).ConfigureAwait( false );

			var vertex = vertexJob.GetAwaiter().GetResult();
			var vertexLightmapped = vertexLightmappedJob.GetAwaiter().GetResult();
			var forward = forwardJob.GetAwaiter().GetResult();
			var forwardAlphaTest = forwardAlphaTestJob.GetAwaiter().GetResult();
			var depthAlphaTest = depthAlphaTestJob.GetAwaiter().GetResult();
			var forwardTranslucent = forwardTranslucentJob.GetAwaiter().GetResult();
			var depthVertex = depthVertexJob.GetAwaiter().GetResult();
			var vertexSkinned = vertexSkinnedJob?.GetAwaiter().GetResult();
			var depthVertexSkinned = depthVertexSkinnedJob?.GetAwaiter().GetResult();

			// Each pass twice: as compiled, and with the vertex stage that passes on the mesh's lightmap uv
			var passes = new Dictionary<string, CompiledPass>();
			string gpuDrivenError = null;
			foreach ( var (v, suffix) in new[] { (vertex, ""), (vertexLightmapped, ShaderPasses.Lightmapped) } )
			{
				foreach ( var (name, pixel) in new[] { (ShaderPasses.Forward, forward), (ShaderPasses.Forward + "AlphaTest", forwardAlphaTest), (ShaderPasses.Forward + "Translucent", forwardTranslucent), (ShaderPasses.Depth, null), (ShaderPasses.DepthAlphaTest, depthAlphaTest) } )
					passes[name + suffix] = new() { Name = name + suffix, Vertex = name == ShaderPasses.Depth ? depthVertex : v, Pixel = pixel };
			}

			// And with the vertex stage of skinned draws from the vertex cache, which reads the skinned vertices by vertex id. A
			// surface that owns its vertex stage has neither these nor GPU-driven passes: its draws go the classic way
			foreach ( var (name, pixel) in stage ? [] : new[] { (ShaderPasses.Forward, forward), (ShaderPasses.Forward + "AlphaTest", forwardAlphaTest), (ShaderPasses.Forward + "Translucent", forwardTranslucent), (ShaderPasses.Depth, null), (ShaderPasses.DepthAlphaTest, depthAlphaTest) } )
				passes[name + ShaderPasses.Skinned] = new() { Name = name + ShaderPasses.Skinned, Vertex = name == ShaderPasses.Depth ? depthVertexSkinned : vertexSkinned, Pixel = pixel };

			// The same passes GPU-driven, each with a vertex entry generated from what the classic one reads; a surface
			// whose GPU-driven compile fails draws the classic way
			if ( !stage )
			{
				try
				{
					var gpuVertex = gpuVertexJob.GetAwaiter().GetResult();
					var gpuVertexLightmapped = gpuVertexLightmappedJob.GetAwaiter().GetResult();
					var gpuVertexSkinned = gpuVertexSkinnedJob.GetAwaiter().GetResult();
					var gpuForward = gpuForwardJob.GetAwaiter().GetResult();
					var gpuForwardAlphaTest = gpuForwardAlphaTestJob.GetAwaiter().GetResult();
					var gpuDepthAlphaTest = gpuDepthAlphaTestJob.GetAwaiter().GetResult();
					var gpuForwardTranslucent = gpuForwardTranslucentJob.GetAwaiter().GetResult();
					// Skinned draws from the vertex cache too: the vertex stage generated from the skinned one, which pulls the vertex
					// ids its classic draw binds (GpuDrivenTables.VertexCacheGeometry)
					foreach ( var (v, suffix) in new[] { (gpuVertex, ShaderPasses.GpuDriven), (gpuVertexLightmapped, ShaderPasses.GpuDriven + ShaderPasses.Lightmapped), (gpuVertexSkinned, ShaderPasses.GpuDriven + ShaderPasses.Skinned) } )
					{
						foreach ( var (name, pixel) in new[] { (ShaderPasses.Forward, gpuForward), (ShaderPasses.Forward + "AlphaTest", gpuForwardAlphaTest), (ShaderPasses.Forward + "Translucent", gpuForwardTranslucent), (ShaderPasses.Depth, null), (ShaderPasses.DepthAlphaTest, gpuDepthAlphaTest) } )
							passes[name + suffix] = new() { Name = name + suffix, Vertex = v, Pixel = pixel };
					}
				}
				catch ( ShaderCompileException e )
				{
					gpuDrivenError = e.Errors;
				}
			}

			return new CompiledShader
			{
				Name = material.Name,
				Tier = ShaderTier.Surface,
				Path = path,
				Material = layout,
				Passes = passes,
				GpuDrivenError = gpuDrivenError,
				Generated = instance,
				Milliseconds = clock.Elapsed.TotalMilliseconds,
				Dependencies = read.Sorted(),
			};
		}
	}

	/// <summary>
	/// Compile a raw module: its author owns the entry points. <c>MainVs</c> and <c>MainPs</c> make the forward pass,
	/// <c>MainVs</c> alone the depth pass, and <c>DepthPs</c>, when there is one, the alpha-tested depth pass. Its
	/// parameters are a <c>[Material]</c> struct read with <c>LoadMaterial&lt;T&gt;()</c>, as surfaces' are.
	/// </summary>
	public CompiledShader CompileRaw( string path ) => Wait( CompileRawAsync( path ) );

	/// <summary><see cref="CompileRaw"/> on the compile threads, as <see cref="CompileSurfaceAsync"/>.</summary>
	public Task<CompiledShader> CompileRawAsync( string path, CompilePriority priority = null ) => Cached( path, ShaderTier.Raw, p => CompileRawNow( p, priority ) );

	async Task<CompiledShader> CompileRawNow( string path, CompilePriority priority )
	{
		Interlocked.Increment( ref surfaces );
		var clock = Stopwatch.StartNew();
		var original = File.ReadAllText( path );
		var source = MaterialSource.Rewrite( original, out var declared );
		if ( declared.Count > 1 ) throw new ShaderCompileException( path, $"A raw module declares at most one [Material] struct; this one declares {declared.Count}" );

		var entries = EntryPoints().Matches( original ).Select( m => (Stage: m.Groups["stage"].Value, Name: m.Groups["name"].Value) ).ToList();
		bool Has( string name, string stage ) => entries.Exists( e => e.Name == name && e.Stage == stage );
		if ( !Has( "MainVs", "vertex" ) || !Has( "MainPs", "pixel" ) ) throw new ShaderCompileException( path, "A raw module has [shader(\"vertex\")] MainVs and [shader(\"pixel\")] MainPs" );

		var directory = Path.GetDirectoryName( path );
		{
			var read = new ReadFiles( path );
			var layoutJob = declared.Count == 1 ? SlangWorkers.Run( () => Reflected( path, source, declared[0].Name, ShaderTier.Raw, read ), priority ) : Task.FromResult( MaterialLayout.Create( "", [] ) );
			var vertexJob = SlangWorkers.Run( () => CompileEntry( path, source, "MainVs", StageKind.Vertex, directory, read ), priority );
			var forwardJob = SlangWorkers.Run( () => CompileEntry( path, source, "MainPs", StageKind.Pixel, directory, read ), priority );
			var depthJob = Has( "DepthPs", "pixel" ) ? SlangWorkers.Run( () => CompileEntry( path, source, "DepthPs", StageKind.Pixel, directory, read ), priority ) : null;
			await Settled( depthJob is null ? [layoutJob, vertexJob, forwardJob] : [layoutJob, vertexJob, forwardJob, depthJob] ).ConfigureAwait( false );

			var layout = layoutJob.GetAwaiter().GetResult();
			var vertex = vertexJob.GetAwaiter().GetResult();
			var forward = forwardJob.GetAwaiter().GetResult();
			var passes = new Dictionary<string, CompiledPass>
			{
				[ShaderPasses.Forward] = new() { Name = ShaderPasses.Forward, Vertex = vertex, Pixel = forward },
				[ShaderPasses.Depth] = new() { Name = ShaderPasses.Depth, Vertex = vertex },
			};

			if ( depthJob is not null )
				passes[ShaderPasses.DepthAlphaTest] = new() { Name = ShaderPasses.DepthAlphaTest, Vertex = vertex, Pixel = depthJob.GetAwaiter().GetResult() };

			return new CompiledShader
			{
				Name = declared.Count == 1 ? declared[0].Name : Path.GetFileNameWithoutExtension( path ),
				Tier = ShaderTier.Raw,
				Path = path,
				Material = layout,
				Passes = passes,
				Milliseconds = clock.Elapsed.TotalMilliseconds,
				Dependencies = read.Sorted(),
			};
		}
	}

	/// <summary>
	/// A combiner surface's <c>[Combiner( "A, B = C" )]</c>: the final combiner's compile-time features it lights with, each
	/// a define before the engine's includes (a name alone is 1).
	/// </summary>
	internal static List<(string Name, string Value)> CombinerDefines( string source )
	{
		var defines = new List<(string, string)>();
		foreach ( Match m in CombinerAttribute().Matches( source ) )
		{
			foreach ( var entry in m.Groups["defines"].Value.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) )
			{
				var parts = entry.Split( '=', 2, StringSplitOptions.TrimEntries );
				defines.Add( (parts[0], parts.Length > 1 ? parts[1] : "1") );
			}
		}

		return defines;
	}

	[GeneratedRegex( @"\bCombiner\s*\(\s*""(?<defines>[^""]*)""\s*\)" )]
	private static partial Regex CombinerAttribute();

	/// <summary>
	/// A surface's <c>[Lighting( "A, B = C" )]</c>: defines before the engine's includes, as a .shader's COMMON block sets
	/// them for its lighting (<c>S_DIFFUSE_TWO_SIDED</c>); a name alone is 1.
	/// </summary>
	internal static List<(string Name, string Value)> LightingDefines( string source )
	{
		var defines = new List<(string, string)>();
		foreach ( Match m in LightingAttribute().Matches( source ) )
		{
			foreach ( var entry in m.Groups["defines"].Value.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) )
			{
				var parts = entry.Split( '=', 2, StringSplitOptions.TrimEntries );
				defines.Add( (parts[0], parts.Length > 1 ? parts[1] : "1") );
			}
		}

		return defines;
	}

	[GeneratedRegex( @"\bLighting\s*\(\s*""(?<defines>[^""]*)""\s*\)" )]
	private static partial Regex LightingAttribute();

	/// <summary>A vertex-stage surface's <c>[ClipDistances( n )]</c>: how many clip distances it writes, 0 without one.</summary>
	internal static int ClipDistances( string source ) => ClipDistancesAttribute().Match( source ) is { Success: true } m ? int.Parse( m.Groups["count"].Value, System.Globalization.CultureInfo.InvariantCulture ) : 0;

	[GeneratedRegex( @"\bClipDistances\s*\(\s*(?<count>-?\d+)\s*\)" )]
	private static partial Regex ClipDistancesAttribute();

	/// <summary>An IVertexStageSurface's <c>typealias Varyings = X;</c>: the struct its derivatives are taken of.</summary>
	[GeneratedRegex( @"\btypealias\s+Varyings\s*=\s*(?<type>[\w.]+)\s*;" )]
	private static partial Regex VaryingsAlias();

	[GeneratedRegex( @"^[A-Za-z_]\w*$" )]
	private static partial Regex ModuleName();

	[GeneratedRegex( @"\[\s*shader\s*\(\s*""(?<stage>\w+)""\s*\)\s*\][^(]*?\b(?<name>\w+)\s*\(" )]
	private static partial Regex EntryPoints();

	/// <summary>
	/// <see cref="ReflectMaterial"/> as a compile's first step, its layout entry written at once (<see cref="ReflectAsync"/>),
	/// so a material that loads while the stages compile has its layout without a request of its own.
	/// </summary>
	MaterialLayout Reflected( string path, string source, string type, ShaderTier tier, ReadFiles read, string varyingsType = null, List<string> varyingFields = null )
	{
		var layout = ReflectMaterial( path, source, type, Path.GetDirectoryName( path ), read, varyingsType, varyingFields );
		if ( CacheFolder is null ) return layout;

		var shader = new CompiledShader { Name = type, Tier = tier, Path = path, Material = layout, Passes = new Dictionary<string, CompiledPass>(), Dependencies = read.Sorted() };
		using ( Zone( CompilePhase.CacheSave ) ) CompileCache.Save( CacheFolder, Paths( path ), slangDirectory, path, File.ReadAllText( path ), shader, layout: true );
		return layout;
	}

	/// <summary>Phase 1: the module alone, to reflect its <c>[Material]</c> struct's fields.</summary>
	MaterialLayout ReflectMaterial( string path, string source, string type, string directory, ReadFiles read, string varyingsType = null, List<string> varyingFields = null )
	{
		using var zone = Zone( CompilePhase.Reflect );
		using var request = Request( StageKind.Pixel, [directory], read );
		request.AddSource( path, source );
		request.SkipCodegen();
		request.Compile( path );
		if ( varyingsType is not null ) varyingFields?.AddRange( MaterialReflection.FloatFields( request.Reflection, varyingsType, path ) );
		return MaterialReflection.Read( request.Reflection, type, path );
	}

	/// <summary>Alpha-tested passes compile with today's alpha test (S_ALPHA_TEST): Shade runs alpha to coverage.</summary>
	static readonly Dictionary<string, string> AlphaTested = new() { ["S_ALPHA_TEST"] = "1" };

	/// <summary>The translucent forward pass compiles with today's S_TRANSLUCENT: opacity kept in alpha, no screen-space occlusion.</summary>
	static readonly Dictionary<string, string> Translucent = new() { ["S_TRANSLUCENT"] = "1" };

	/// <summary>
	/// The vertex stage of lightmapped draws: today's dynamic combo that reads the mesh's lightmap uv (TEXCOORD3) and passes
	/// it on. The pixel stage is the same; it samples the lightmap when the object has one.
	/// </summary>
	static readonly Dictionary<string, string> Lightmapped = new() { ["D_BAKED_LIGHTING_FROM_LIGHTMAP"] = "1" };

	/// <summary>
	/// The vertex stage of skinned draws from the vertex cache: today's dynamic combo that reads the skinned position and
	/// tangent frame from the cache by the draw's vertex ids (TEXCOORD14, TEXCOORD15).
	/// </summary>
	static readonly Dictionary<string, string> VertexAnimation = new() { ["D_CS_VERTEX_ANIMATION"] = "1" };

	/// <summary>GPU-driven passes (<c>sbox/surface_passes.slangh</c>, docs/reference/borealis-gpu-driven.md).</summary>
	static readonly Dictionary<string, string> GpuDriven = new() { ["SURFACE_GPU_DRIVEN"] = "1" };

	static Dictionary<string, string> With( Dictionary<string, string> a, Dictionary<string, string> b )
	{
		var both = new Dictionary<string, string>( a );
		foreach ( var (name, value) in b ) both[name] = value;
		return both;
	}

	/// <summary>The alpha-tested depth pass also in Depth mode: Shade returns the coverage before lighting.</summary>
	static readonly Dictionary<string, string> AlphaTestedDepth = new() { ["S_ALPHA_TEST"] = "1", ["S_MODE_DEPTH"] = "1" };

	CompiledStage CompileEntry( string path, string source, string entry, StageKind stage, string directory, ReadFiles read, IReadOnlyDictionary<string, string> defines = null )
	{
		using var zone = Zone( CompilePhase.Stage );
		Interlocked.Increment( ref stages );
		using var request = Request( stage, [directory], read, defines );
		request.AddSource( path, source );
		request.AddEntryPoint( entry, stage );
		request.Compile( path );

		var spirv = request.Code() ?? throw new ShaderCompileException( path, $"{entry}: Slang returned no code. {request.Diagnostics}" );
		using ( Zone( CompilePhase.Optimize ) ) spirv = SpirvOptimizer.Optimize( slangDirectory, spirv, out _ );

		// The pixel stage's own resources go in set 1, as vfx puts them, so the two stages' bindings never collide
		if ( stage == StageKind.Pixel ) spirv = Spirv.MoveSet( spirv, 0, 1 );

		using ( Zone( CompilePhase.Describe ) ) return StageReflection.Describe( stage, entry, spirv, request.ReflectionJson(), request.Annotations );
	}

	/// <summary>
	/// The instance file for a <c>[Material]</c> struct: the engine's passes, named as entry points. Each pixel entry loads
	/// the record at the offset the vertex stage passed on (or the draw's, per pipeline) and hands the passes the slot of
	/// the material's <c>[Opacity]</c> texture, when it has one. A lit surface's (<c>ILitSurface</c>) also turns on the
	/// engine light loop's hook (<c>SURFACE_LIGHT_HOOK</c>) and hands it the material, so <c>Light</c> runs per light. An
	/// interpolated surface's (<c>IInterpolatedSurface</c>) vertex stage also reads its <c>Varyings</c> and passes them on. A
	/// composite surface's (<c>ICompositeSurface</c>) forward passes call its <c>Composite</c> on the lit colour, and a
	/// combiner surface's (<c>ICombinerSurface</c>) shade through complex.shader's final combiner (<c>SURFACE_FINAL_COMBINER</c>),
	/// a layered one's (<c>ILayeredSurface</c> or <c>IInterpolatedLayeredSurface</c>, <paramref name="layered"/>) through its own Shade over that lighting.
	/// Pixel entries read the material's <paramref name="features"/> as specialization constants instead of from the record
	/// when the pipeline is specialised for its material (<see cref="MaterialLayout.SpecializedId"/>), so its branches fold.
	/// A surface that owns its vertex stage (<c>IVertexStageSurface</c>, <paramref name="stage"/>) has entries that run its
	/// Vertex and hand its Evaluate the Varyings with the derivatives of <paramref name="varyingFields"/> (its float fields),
	/// and writes <paramref name="clipDistances"/> clip distances; one that shades itself (<c>IShadedSurface</c> or
	/// <c>IShadedVertexStageSurface</c>, <paramref name="shaded"/>) is lit by its own Shade.
	/// </summary>
	internal static string InstanceFile( string module, string type, bool displaces, bool lit, string opacityField = null, bool interpolated = false, bool sunLit = false, bool composite = false, bool combiner = false, IReadOnlyList<(string Name, string Value)> combinerDefines = null, IReadOnlyList<MaterialField> features = null, bool layered = false, bool stage = false, bool shaded = false, IReadOnlyList<string> varyingFields = null, int clipDistances = 0 )
	{
		var specialize = features is { Count: > 0 } ? " SurfaceSpecialize( m );" : "";
		var forward = shaded ? "SurfaceForwardShaded" : sunLit ? "SurfaceForwardSunLit" : composite ? "SurfaceForwardComposite" : layered ? "SurfaceForwardLayered" : combiner ? "SurfaceForwardCombiner" : "SurfaceForward";
		var vertex = displaces ? $"SurfaceVertexDisplaced<{type}>( i )" : "SurfaceVertex( i )";
		var opacity = opacityField is null ? "0" : $"m.{opacityField}.Slot";
		var keep = lit ? " s_LitSurface = m;" : "";
		var output = stage ? $"SurfaceStageOutput<SurfaceStageVaryings<{type}>>" : interpolated ? $"SurfaceVertexOutputWith<SurfaceVaryings<{type}>>" : "SurfaceVertexOutput";
		var surface = stage ? (shaded ? $"SurfaceWithStageShaded<{type}, {type}>" : $"SurfaceWithStage<{type}>")
			: interpolated ? $"{(layered ? "SurfaceWithVaryingsLayered" : combiner ? "SurfaceWithVaryingsCombiner" : "SurfaceWithVaryings")}<{type}>" : type;
		var wrap = stage ? $" {surface} s; s.Surface = m; s.Values = i.Varyings; s.Derivs = SurfaceStageDerivatives( i.Varyings );{(shaded ? " s.Shading = m; s.ShadeValues = i.Varyings; s.ShadeDerivs = s.Derivs;" : "")}"
			: interpolated ? $" {surface} s; s.Surface = m; s.Values = i.Varyings;" : "";
		var argument = interpolated || stage ? "s" : "m";
		string Shade( bool alphaTest ) => $"{type} m = SurfaceRecord<{type}>( PixelMaterialOffset( i.MaterialOffset ) );{specialize}{keep}{wrap} return {forward}<{surface}, {(alphaTest ? "true" : "false")}>( i.Pixel, {argument}, {opacity} );";
		string Shaded( string entry, bool alphaTest ) => $"float4 Shaded{entry}( {output} i ) {{ {Shade( alphaTest )} }}\n";

		// The record offset (and on GPU-driven passes the draw) reaches the pixel stage flat, so per lane: shaded with it as
		// it is, every record field and texture slot would be read per lane, and each texture sample would loop over the
		// slots in the wave. The wave shades its distinct values one at a time instead, each wave-uniform. A quad's lanes
		// share a primitive, so a value; its helper lanes, which wave operations may leave out, take the turn from the
		// quad's other lanes, so derivatives stay whole. A wave holds at most 64 distinct values.
		static string ForEachInWave( string key, string body ) =>
			$"[loop] for ( uint n = 0; n < 64; n++ ) {{ uint first = WaveReadLaneFirst( {key} ); uint turn = first == {key} ? 1u : 0u; " +
			$"turn |= QuadReadAcrossX( turn ) | QuadReadAcrossY( turn ) | QuadReadAcrossDiagonal( turn ); if ( turn != 0 ) {{ {body} break; }} }}";

		// Every pixel entry takes the facing, so a two-sided material's back faces shade with the normal turned to the viewer.
		// The opaque forward entries also write the normal and roughness at target 1 (ForwardOutput, sbox/forward_normals.slangh),
		// which pipelines with the opaque pass's normals target switch on (kForwardNormalsOutput)
		static string Returns( bool normals ) => normals ? "ForwardOutput" : "float4";
		static string Target( bool normals ) => normals ? "" : " : SV_Target0";
		static string Result( bool normals ) => normals ? "SurfaceForwardOut( color )" : "color";
		string Pixel( string entry, bool alphaTest, bool normals = false ) => Shaded( entry, alphaTest ) +
			$"[shader( \"pixel\" )] {Returns( normals )} {entry}( {output} i, bool front : SV_IsFrontFace ){Target( normals )} {{ s_SurfaceFrontFace = front; float4 color = 0; {ForEachInWave( "i.MaterialOffset", $"{output} w = i; w.MaterialOffset = first; color = Shaded{entry}( w );" )} return {Result( normals )}; }}\n";

		// GPU-driven: the draw's lighting record first; the alpha-tested depth pass also writes the visibility id (the vertex
		// bit + 1) at target 1, which the visibility pass's prepass reads and other passes have no target for. A draw has
		// one material record, so the draw is the value the wave goes through
		string GpuDrivenShade( string entry, string result ) =>
			ForEachInWave( "v.Draw", $"SurfaceFetchLighting( first ); {output} i = v.o; i.MaterialOffset = WaveReadLaneFirst( i.MaterialOffset ); {result} = Shaded{entry}( i );" );
		string GpuDrivenPixel( string entry, bool alphaTest, bool ids, bool normals = false ) => Shaded( entry, alphaTest ) + (ids
			? $"[shader( \"pixel\" )] SurfaceIdsOutput {entry}( SurfaceGpuDrivenVertexOutput v, bool front : SV_IsFrontFace ) {{ s_SurfaceFrontFace = front; SurfaceIdsOutput o; o.Color = 0; {GpuDrivenShade( entry, "o.Color" )} o.Id = v.VertexBit + 1; return o; }}\n"
			: $"[shader( \"pixel\" )] {Returns( normals )} {entry}( SurfaceGpuDrivenVertexOutput v, bool front : SV_IsFrontFace ){Target( normals )} {{ s_SurfaceFrontFace = front; float4 color = 0; {GpuDrivenShade( entry, "color" )} return {Result( normals )}; }}\n");

		var text = new StringBuilder();
		text.Append( "// Generated by Sandbox.ShaderCompiler for the [Material] struct " ).Append( type ).Append( " in " ).Append( module ).Append( ".slang.\n" );
		if ( opacityField is not null ) text.Append( "#define SURFACE_OPACITY_TEXTURE 1\n" );
		if ( lit ) text.Append( "#define SURFACE_LIGHT_HOOK 1\n" );
		if ( combiner ) text.Append( "#define SURFACE_FINAL_COMBINER 1\n" );
		if ( clipDistances > 0 ) text.Append( "#define SURFACE_CLIP_DISTANCES " ).Append( clipDistances ).Append( '\n' );
		foreach ( var (name, value) in combinerDefines ?? [] ) text.Append( "#define " ).Append( name ).Append( ' ' ).Append( value ).Append( '\n' );
		text.Append( "#include \"sbox/surface_passes.slangh\"\n" );
		text.Append( "import " ).Append( module ).Append( ";\n\n" );
		text.Append( "#ifdef SURFACE_GPU_DRIVEN\n" );
		text.Append( "// What a GPU-driven vertex stage hands the pixel stage: the draw and the vertex's visibility bit, flat, first\n" );
		text.Append( "struct SurfaceGpuDrivenVertexOutput { nointerpolation uint Draw : BOREALIS_DRAW; nointerpolation uint VertexBit : BOREALIS_VERTEX; " ).Append( output ).Append( " o; };\n" );
		text.Append( "struct SurfaceIdsOutput { float4 Color : SV_Target0; uint Id : SV_Target1; };\n" );
		text.Append( "#endif\n\n" );
		text.Append( "#if ( PROGRAM == VFX_PROGRAM_VS )\n" );
		if ( stage )
		{
			// The surface's own vertex stage, in every pass: its vertex input, the instance transform and the ids
			const string inputs = "uint transform : TEXCOORD13, uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID";
			text.Append( $"[shader( \"vertex\" )] {output} MainVs( SurfaceStageVertexInput<{type}> v, {inputs} ) {{ return SurfaceStageVertex<{type}>( v, transform, vertexId, instanceId ); }}\n" );
			text.Append( "// Opaque depth: the same vertex stage's position and clip distances alone\n" );
			text.Append( $"[shader( \"vertex\" )] SurfaceStageDepthOutput DepthVs( SurfaceStageVertexInput<{type}> v, {inputs} ) {{ return SurfaceStageDepth<{type}>( v, transform, vertexId, instanceId ); }}\n" );
		}
		else
		{
			var varyingsInput = interpolated ? $", SurfaceVaryings<{type}> varyings" : "";
			text.Append( $"[shader( \"vertex\" )] {output} MainVs( VertexInput i{varyingsInput} ) {{ return {VertexStage( type, displaces, interpolated )}; }}\n" );
			text.Append( "// Opaque depth: the position alone, so its draws can read a positions-only vertex copy\n" );
			var depthInput = displaces && interpolated ? varyingsInput : "";
			var depthPosition = displaces && interpolated ? $"{VertexStage( type, displaces, interpolated )}.Pixel" : vertex;
			text.Append( $"[shader( \"vertex\" )] float4 DepthVs( VertexInput i{depthInput} ) : SV_Position {{ return {depthPosition}.vPositionPs; }}\n" );
		}

		text.Append( "#else\n" );
		if ( stage )
		{
			// The Varyings' derivatives, field by field: the float ones in hardware, the rest zero
			var varyings = $"SurfaceStageVaryings<{type}>";
			text.Append( $"Derivatives<{varyings}> SurfaceStageDerivatives( {varyings} v ) {{ Derivatives<{varyings}> d = {{}};" );
			foreach ( var field in varyingFields ?? [] ) text.Append( $" d.Ddx.{field} = ddx( v.{field} ); d.Ddy.{field} = ddy( v.{field} );" );
			text.Append( " return d; }\n" );
		}

		if ( lit )
		{
			text.Append( "// The engine's light loop calls SurfaceLight for every light it shades the pixel with\n" );
			text.Append( "static " ).Append( type ).Append( " s_LitSurface;\n" );
			text.Append( "float3 SurfaceLight( LightSample light ) { return s_LitSurface.Light( SurfaceLitMaterial(), SurfaceLitInput(), light ); }\n" );
		}

		if ( features is { Count: > 0 } ) text.Append( Specialization( type, features ) );

		text.Append( Pixel( "ForwardPs", false, normals: true ) );
		text.Append( Pixel( "ForwardAlphaTestPs", true, normals: true ) );
		text.Append( "// Compiled with S_TRANSLUCENT, for blended materials\n" );
		text.Append( Pixel( "ForwardTranslucentPs", false ) );
		text.Append( "// Compiled in Depth mode: Shade returns the alpha-tested coverage before lighting\n" );
		text.Append( Pixel( "DepthAlphaTestPs", true ) );
		if ( stage )
		{
			text.Append( "#endif\n" );
			return text.ToString();
		}

		text.Append( "#ifdef SURFACE_GPU_DRIVEN\n" );
		text.Append( GpuDrivenPixel( "ForwardPsGpuDriven", false, false, normals: true ) );
		text.Append( GpuDrivenPixel( "ForwardAlphaTestPsGpuDriven", true, false, normals: true ) );
		text.Append( GpuDrivenPixel( "ForwardTranslucentPsGpuDriven", false, false ) );
		text.Append( GpuDrivenPixel( "DepthAlphaTestPsGpuDriven", true, true ) );
		text.Append( "#endif\n" );
		text.Append( "#endif\n" );
		return text.ToString();
	}

	/// <summary>
	/// A pixel stage's feature constants: the flag that the pipeline is specialised for one material, each feature's value,
	/// and <c>SurfaceSpecialize</c>, which puts them in the record's place. Unspecialised, the record's values stand.
	/// </summary>
	static string Specialization( string type, IReadOnlyList<MaterialField> features )
	{
		var text = new StringBuilder();
		text.Append( "// The material's features as constants, when the pipeline is specialised for it\n" );
		text.Append( $"[vk::constant_id( {MaterialLayout.SpecializedId} )] const bool kSurfaceSpecialized = false;\n" );
		for ( int n = 0; n < features.Count; n++ ) text.Append( $"[vk::constant_id( {MaterialLayout.FirstFeatureId + n} )] const int kSurfaceFeature{n} = 0;\n" );
		text.Append( $"void SurfaceSpecialize( inout {type} m )\n{{\n\tif ( !kSurfaceSpecialized ) return;\n" );
		for ( int n = 0; n < features.Count; n++ )
		{
			var field = features[n];
			var value = field.Kind switch
			{
				MaterialFieldKind.Bool => $"kSurfaceFeature{n} != 0",
				MaterialFieldKind.UInt => $"uint( kSurfaceFeature{n} )",
				_ => $"kSurfaceFeature{n}",
			};
			text.Append( $"\tm.{field.Name} = {value};\n" );
		}

		text.Append( "}\n" );
		return text.ToString();
	}

	/// <summary>
	/// What a surface's vertex entries return: the engine's stage (moved by an <c>IVertexSurface</c>'s Displace) with the
	/// record offset, the <c>Varyings</c> too for an interpolated surface, which an <c>IInterpolatedVertexSurface</c>
	/// displaces with in hand.
	/// </summary>
	static string VertexStage( string type, bool displaces, bool interpolated )
	{
		if ( displaces && interpolated ) return $"SurfaceOutputDisplacedWith<{type}>( i, varyings )";
		var vertex = displaces ? $"SurfaceVertexDisplaced<{type}>( i )" : "SurfaceVertex( i )";
		return interpolated ? $"SurfaceOutputWith<{type}>( {vertex}, varyings )" : $"SurfaceOutput( {vertex} )";
	}

	/// <summary>Usages in the order GPU-driven geometry records number semantics (<c>usage * 16 + index</c>).</summary>
	static readonly string[] Usages = ["POSITION", "BLENDWEIGHT", "BLENDINDICES", "NORMAL", "PSIZE", "TEXCOORD", "TANGENT", "BINORMAL", "POSITIONT", "COLOR"];

	/// <summary>
	/// A surface's GPU-driven vertex entry (<c>MainVsGpuDriven</c>), generated from what its vertex stage reads
	/// (<paramref name="inputs"/>, from reflection): each input pulled from the draw's geometry by its semantic, the
	/// transform slot from the instance record, the tangent frame's encoding from the draw's flags, then the same stage.
	/// Appended to the instance file for a GPU-driven compile (<c>SURFACE_GPU_DRIVEN</c>).
	/// </summary>
	internal static string GpuDrivenVertex( string type, bool displaces, bool interpolated, IReadOnlyList<VertexInputSlot> inputs )
	{
		var text = new StringBuilder();
		text.Append( "\n#if ( PROGRAM == VFX_PROGRAM_VS )\n" );
		text.Append( "[shader( \"vertex\" )] SurfaceGpuDrivenVertexOutput MainVsGpuDriven( uint vertex : SV_VulkanVertexID, uint instance : SV_VulkanInstanceID )\n{\n" );
		text.Append( "\tuint2 record = BorealisGpuDrivenInstance( instance );\n" );
		text.Append( "\tuint4 draw = BorealisGpuDrivenDraw( record.y );\n" );
		text.Append( "\ts_SurfaceDrawRecord = draw.y;\n" );
		text.Append( "\tg_bUncompressedTangentFrame = ( draw.w & 1 ) != 0;\n" );
		text.Append( "\tSurfaceFetchBakedLighting( record.y );\n" );
		text.Append( "\tVertexInput i = {};\n" );
		if ( interpolated ) text.Append( $"\tSurfaceVaryings<{type}> varyings = {{}};\n" );
		foreach ( var input in inputs )
		{
			if ( input.Field is null ) throw new InvalidOperationException( $"vertex input {input.Semantic}{input.SemanticIndex} has no field" );
			if ( input.Semantic == "TEXCOORD" && input.SemanticIndex == 13 )
			{
				text.Append( $"\t{input.Field} = record.x;\n" );
				continue;
			}

			var usage = Array.IndexOf( Usages, input.Semantic );
			if ( usage < 0 || input.SemanticIndex >= 16 ) throw new InvalidOperationException( $"vertex input {input.Field} : {input.Semantic}{input.SemanticIndex} has no stream semantic" );
			var isFloat = input.Kind == ScalarKind.Float;
			var convert = isFloat ? "asfloat" : input.Kind == ScalarKind.Int ? "asint" : "";
			var swizzle = "xyzw"[..input.Components];
			text.Append( $"\t{input.Field} = {convert}( BorealisGpuDrivenFetch( draw.x, {usage * 16 + input.SemanticIndex}, vertex, {(isFloat ? "true" : "false")} ) ).{swizzle};\n" );
		}

		text.Append( "\tSurfaceGpuDrivenVertexOutput o;\n" );
		text.Append( $"\to.o = {VertexStage( type, displaces, interpolated )};\n" );
		text.Append( "\to.o.MaterialOffset = draw.z;\n" );
		text.Append( "\to.Draw = record.y;\n" );
		text.Append( "\to.VertexBit = BorealisGpuDrivenVertexBit( instance, vertex );\n" );
		text.Append( "\treturn o;\n}\n#endif\n" );
		return text.ToString();
	}
}
