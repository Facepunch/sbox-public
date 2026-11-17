using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Borealis.Gpu;
using Borealis.Source2.Shaders;
using Sandbox.ShaderCompiler;

namespace Borealis.Backend;

/// <summary>
/// Pass modules on a device: a <c>.shader</c> that isn't a material (post-processing, UI, tools, compute, the engine's
/// fullscreen passes) with a Slang module beside it (<c>pp_vignette.slang</c> beside <c>pp_vignette.shader</c>, including
/// <c>sbox/pass.slangh</c>) draws from that module, compiled per stage and combo values with the engine's Slang. A module
/// that doesn't compile is reported once (<see cref="Error"/>) and its shader draws from its compiled programs, as with
/// <see cref="BackendDevice.SlangPasses"/> off. See docs/managed/shader-format-replacement.md ("Pass modules").
/// </summary>
public sealed class PassModules : IShaderModules
{
	readonly BackendDevice device;
	readonly Dictionary<string, (DateTime Time, long Checked, PassModule Module)> modules = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<string, (ModuleStage Stage, IReadOnlyList<SpecializationSlot> Slots)> stages = new( StringComparer.Ordinal );
	readonly HashSet<string> reported = new( StringComparer.Ordinal );
	readonly Dictionary<string, ShaderCompileException> failed = new( StringComparer.Ordinal );
	readonly Dictionary<string, Task<CompiledPassStage>> running = new( StringComparer.Ordinal );
	SlangCompiler compiler;

	internal PassModules( BackendDevice device ) => this.device = device;

	/// <summary>Defined (1) for vertex stages on devices without wave operations there (<see cref="DeviceCapabilities.VertexWaveOps"/>).</summary>
	internal const string NoVertexWaves = "BOREALIS_NO_VERTEX_WAVES";

	/// <summary>
	/// Told once for each module and combo values that don't compile, with the compiler's messages; the console's error
	/// stream unless set.
	/// </summary>
	public Action<string> Error { get; set; } = message => Console.Error.WriteLine( $"Borealis.Gpu: {message}" );

	/// <summary>Stages compiled (or read from the cache) so far.</summary>
	public int StagesCompiled { get; private set; }

	/// <summary>
	/// Whether a variant whose stages are still compiling draws from its shader's compiled programs meanwhile (nothing, for a
	/// shader made from embedded source; a module-only shader's, and Borealis's own passes', are waited for), as with
	/// <c>-no-slang-passes</c>, instead of waiting for them; from <see cref="SurfaceShaders.CompileInBackgroundByDefault"/> as
	/// the device is made.
	/// </summary>
	public bool CompileInBackground { get; set; } = SurfaceShaders.CompileInBackgroundByDefault;

	/// <summary>Variants asked for while their stages compiled in the background, for reports and tests.</summary>
	public int PendingAsks => pendingAsks;

	int pendingAsks;

	/// <summary>
	/// Where compiled stages are kept between runs; <c>game/.source2/borealispasses</c> beside the engine's own caches unless
	/// set before the first compile.
	/// </summary>
	public string CacheFolder { get; set; }

	/// <summary>The engine's core content folder: the one with <c>shaders/sbox/pass.slangh</c>.</summary>
	string Core => core ??= device.Content.Roots.FirstOrDefault( r => File.Exists( Path.Combine( r, "shaders", "sbox", "pass.slangh" ) ) ) ?? device.Content.Roots.FirstOrDefault()
		?? throw new InvalidOperationException( "No content folder to find the engine's shaders in" );

	string core;

	SlangCompiler Compiler()
	{
		if ( compiler is not null ) return compiler;
		var game = Path.GetDirectoryName( Core );
		CompileZones.Install();
		return compiler = new SlangCompiler( EngineFolders.NativeIn( game ), [Path.Combine( Core, "shaders" )] )
		{
			CacheFolder = CacheFolder ?? Path.Combine( game, ".source2", "borealispasses" ),
		};
	}

	/// <summary>
	/// The pass module beside a shader's source, or null. Its file is looked at again at most once a second, and read again
	/// when it changed.
	/// </summary>
	public PassModule ModuleOf( ShaderFile shader )
	{
		return shader.Outdated is not null ? EmbeddedModule( shader ) : ModuleBeside( shader.Name );
	}

	/// <summary>The pass module beside a compiled file's path (<c>x.slang</c> for <c>x.shader_c</c>), or null.</summary>
	PassModule ModuleBeside( string compiledPath )
	{
		var name = compiledPath;
		if ( name.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) ) name = name[..^2];
		var path = Path.ChangeExtension( name, ".slang" );

		var now = Environment.TickCount64;
		lock ( modules )
		{
			if ( modules.TryGetValue( path, out var known ) && now - known.Checked < 1000 ) return known.Module;
		}

		var info = new FileInfo( path );
		lock ( modules )
		{
			if ( !info.Exists )
			{
				modules[path] = (default, now, null);
				return null;
			}

			if ( modules.TryGetValue( path, out var known ) && known.Time == info.LastWriteTimeUtc )
			{
				modules[path] = known with { Checked = now };
				return known.Module;
			}

			var text = File.ReadAllText( path );
			var module = text.Contains( "sbox/pass.slangh", StringComparison.Ordinal ) ? PassModule.FromText( info.FullName, text, [Path.Combine( Core, "shaders" )] ) : null;
			modules[path] = (info.LastWriteTimeUtc, now, module);
			return module;
		}
	}

	/// <inheritdoc/>
	public ShaderFile Describe( string compiledPath )
	{
		if ( ModuleBeside( compiledPath ) is not { } module ) return null;

		var features = module.Features.Select( ( f, i ) => new ShaderCombo { Name = f.Name, Min = f.Min, Max = f.Max, FeatureIndex = i, Strings = [] } ).ToArray();
		ShaderCombo Combo( PassCombo c, bool isStatic ) => new()
		{
			Name = c.Name,
			Min = c.Min,
			Max = c.Max,
			// Static combos that follow a feature take its value; the rest are the mode's (S_MODE_*) or the system's
			FeatureIndex = c.Feature is null ? -1 : module.Features.ToList().FindIndex( f => f.Name == c.Feature ),
			Source = isStatic && c.Feature is null && !c.Name.StartsWith( "S_MODE_", StringComparison.Ordinal ) ? 1 : 0,
			Strings = [],
		};

		var programs = new List<(ShaderProgramType, ShaderCombo[], ShaderCombo[])>();
		foreach ( var (kind, type) in new[] { (StageKind.Vertex, ShaderProgramType.Vertex), (StageKind.Pixel, ShaderProgramType.Pixel), (StageKind.Compute, ShaderProgramType.Compute) } )
		{
			if ( !module.Has( kind ) ) continue;
			var combos = module.Combos( kind );
			programs.Add( (type, combos.Where( c => c.Static ).Select( c => Combo( c, true ) ).ToArray(), combos.Where( c => !c.Static ).Select( c => Combo( c, false ) ).ToArray()) );
		}

		return ShaderFile.FromModule( compiledPath, Modes( module.Text ), features, programs );
	}

	/// <summary>
	/// A module's modes, from the <c>// MODES:</c> lines its <c>.shader</c>'s MODES block became (<c>Depth( S_MODE_DEPTH );</c>
	/// sets that combo, <c>ToolsWireframe( "tools_wireframe.shader" );</c> names a fallback); <c>Default</c> without any.
	/// </summary>
	internal static IReadOnlyList<ShaderMode> Modes( string text )
	{
		var modes = new List<ShaderMode>();
		foreach ( System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches( text, @"^[ \t]*//[ \t]*MODES:[ \t]*(?<name>\w+)[ \t]*\((?<args>[^)]*)\)", System.Text.RegularExpressions.RegexOptions.Multiline ) )
		{
			var fallback = "";
			var settings = new List<(string, int)>();
			foreach ( var argument in m.Groups["args"].Value.Split( ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries ) )
			{
				if ( argument.StartsWith( '"' ) ) fallback = argument.Trim( '"' );
				else settings.Add( (argument, 1) );
			}

			modes.Add( new ShaderMode { Name = m.Groups["name"].Value, Fallback = fallback, Settings = [.. settings] } );
		}

		return modes.Count > 0 ? modes : [new ShaderMode { Name = "Default", Fallback = "", Settings = [] }];
	}

	readonly System.Runtime.CompilerServices.ConditionalWeakTable<ShaderFile, System.Runtime.CompilerServices.StrongBox<PassModule>> embedded = new();

	/// <summary>
	/// Whether a shader an older engine compiled (<see cref="ShaderFile.Outdated"/>, a package's) draws from a module made
	/// from the source embedded in it (on), compiled here like any module, rather than being recompiled through the
	/// ShaderCompiler (<see cref="ShaderLibrary.Upgrader"/>), which it falls back to when the module doesn't compile.
	/// <c>-no-embedded-modules</c> turns it off, for comparisons.
	/// </summary>
	public bool FromEmbeddedSource { get; set; } = !Environment.GetCommandLineArgs().Any( a => a.Equals( "-no-embedded-modules", StringComparison.OrdinalIgnoreCase ) );

	/// <summary>
	/// Where modules made from shaders' embedded source are written, by name and contents, for the compiler to read and
	/// for looking at; <c>game/.source2/borealisembedded</c> unless set before the first.
	/// </summary>
	public string EmbeddedFolder { get; set; }

	/// <summary>
	/// The module for an outdated shader's embedded source (<see cref="PassConverter.FromEmbedded"/>), made once per shader;
	/// null when it has none, or it's of a kind modules don't draw (a geometry stage).
	/// </summary>
	PassModule EmbeddedModule( ShaderFile shader )
	{
		if ( !FromEmbeddedSource || shader.EmbeddedSource is not { } source || PassConverter.RefuseEmbedded( source ) is not null ) return null;
		return embedded.GetValue( shader, s => new( MakeEmbedded( s, source ) ) ).Value;
	}

	PassModule MakeEmbedded( ShaderFile shader, IReadOnlyDictionary<string, string> source )
	{
		// A package's file is name.hash.shader_c
		var stem = System.Text.RegularExpressions.Regex.Replace( Path.GetFileName( shader.Name ).Split( '.' )[0], "[^a-zA-Z0-9_]", "_" );
		var features = (shader.Features?.StaticCombos ?? []).Select( c => (c.Name, c.Min, c.Max) );
		var text = PassConverter.FromEmbedded( source, features, stem );
		var hash = Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( System.Text.Encoding.UTF8.GetBytes( text ) ), 0, 8 ).ToLowerInvariant();
		var folder = EmbeddedFolder ?? Path.Combine( Path.GetDirectoryName( Core ), ".source2", "borealisembedded" );
		var path = Path.Combine( folder, $"{stem}.{hash}.slang" );

		try
		{
			Directory.CreateDirectory( folder );
			if ( !File.Exists( path ) ) File.WriteAllText( path, text );
		}
		catch ( IOException )
		{
			// Another process is writing the same module
		}
		catch ( UnauthorizedAccessException )
		{
		}

		return PassModule.FromText( path, text, [Path.Combine( Core, "shaders" )] );
	}

	/// <inheritdoc/>
	public bool Has( ShaderFile shader ) => !Skipped( shader ) && ModuleOf( shader ) is not null;

	/// <summary>
	/// Shaders drawn from their <c>.shader_c</c> although they have a module, by file name without extension, for finding
	/// which module a difference comes from: <c>-no-slang-pass a,b</c> on the command line, or set here.
	/// </summary>
	public HashSet<string> Skip { get; } = new( (Environment.GetCommandLineArgs().SkipWhile( a => !a.Equals( "-no-slang-pass", StringComparison.OrdinalIgnoreCase ) ).Skip( 1 ).FirstOrDefault() ?? "")
		.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ), StringComparer.OrdinalIgnoreCase );

	readonly HashSet<string> used = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>The modules variants were asked of so far, by file name.</summary>
	public IReadOnlyCollection<string> Used
	{
		get
		{
			lock ( used ) return [.. used];
		}
	}

	/// <summary>Print a line the first time each module draws (<c>-log-slang-passes</c>), to see which a run used.</summary>
	public static bool LogUse { get; set; } = Environment.GetCommandLineArgs().Any( a => a.Equals( "-log-slang-passes", StringComparison.OrdinalIgnoreCase ) );

	bool Skipped( ShaderFile shader ) => Skip.Count > 0 && Skip.Contains( Path.GetFileName( shader.Name ).Split( '.' )[0] );

	/// <inheritdoc/>
	public ModuleProgram Compile( ShaderFile shader, ShaderMode mode, ReadOnlySpan<int> features, Func<string, int?> dynamicCombos, Func<string, int?> attributes )
	{
		var module = ModuleOf( shader );
		if ( module is null ) return null;
		lock ( used )
		{
			if ( used.Add( Path.GetFileNameWithoutExtension( module.Path ) ) && LogUse ) Console.WriteLine( $"Borealis.Gpu: {Path.GetFileName( module.Path )} draws {Path.GetFileName( shader.Name )}" );
		}

		// Features by name, for static combos that follow them and render states that read them
		var featureValues = new Dictionary<string, int>( StringComparer.Ordinal );
		foreach ( var feature in module.Features )
		{
			var index = shader.FeatureIndex( feature.Name );
			featureValues[feature.Name] = index >= 0 && index < features.Length ? features[index] : feature.Min;
		}

		var settings = new Dictionary<string, int>( StringComparer.Ordinal );
		foreach ( var (combo, value) in mode.Settings ?? [] ) settings[combo] = value;

		var waiting = false;
		ModuleStage Stage( StageKind kind, out Dictionary<string, int> values, bool start = false )
		{
			values = null;
			if ( !module.Has( kind ) ) return null;
			values = new Dictionary<string, int>( settings, StringComparer.Ordinal );
			foreach ( var combo in module.Combos( kind ) )
			{
				int value;
				if ( settings.TryGetValue( combo.Name, out var set ) ) value = set;
				else if ( combo.Static ) value = combo.Feature is not null && featureValues.TryGetValue( combo.Feature, out var f ) ? f : combo.Min;
				else value = dynamicCombos?.Invoke( combo.Name ) ?? combo.Min;
				values[combo.Name] = Math.Clamp( value, combo.Min, combo.Max );
			}

			// Combo values the rules forbid weren't compiled by vfx: those draw the first dynamic combo, as a compiled program's lookup does
			if ( !module.Allows( kind, values ) )
			{
				foreach ( var combo in module.Combos( kind ) )
				{
					if ( !combo.Static && !settings.ContainsKey( combo.Name ) ) values[combo.Name] = combo.Min;
				}
			}

			// No entry point with these values (a depth pass without a pixel shader): a stage with no code, for the render states
			if ( !module.Has( kind, values ) ) return kind == StageKind.Pixel ? new ModuleStage { Type = ShaderProgramType.Pixel, Name = $"{Path.GetFileName( module.Path )} pixel (none)" } : null;

			// A device without wave operations in vertex shaders (Lavapipe) rejects vertex modules that use them: modules
			// leave them out where this is set
			if ( kind == StageKind.Vertex && !device.Gpu.Capabilities.VertexWaveOps ) values[NoVertexWaves] = 1;
			if ( start )
			{
				waiting |= Start( module, kind, values ) is { IsCompleted: false };
				return null;
			}

			return Compiled( shader, module, kind, values, attributes );
		}

		try
		{
			// Every stage it needs compiles at once, on the compile threads; then each is waited for in turn, or, in the
			// background, none is: the shader's programs draw until they're done
			foreach ( var kind in Kinds ) Stage( kind, out _, start: true );
			if ( waiting && CompileInBackground && !shader.ModuleOnly && !Borealis( module ) )
			{
				System.Threading.Interlocked.Increment( ref pendingAsks );
				return ModuleProgram.Pending;
			}
			var vertex = Stage( StageKind.Vertex, out var vertexValues );
			if ( vertex?.Spirv is not null && module.Expansion( vertexValues ) is { } expansion )
				vertex = Copy( vertex, expansion: PrimitiveExpansion.Parse( expansion.From, expansion.Vertices, expansion.Topology ) );
			var pixel = Stage( StageKind.Pixel, out var pixelValues );
			var compute = Stage( StageKind.Compute, out _ );
			var task = Stage( StageKind.Task, out _ );
			var mesh = Stage( StageKind.Mesh, out _ );
			if ( pixel is not null )
			{
				var all = new Dictionary<string, int>( pixelValues );
				foreach ( var (name, value) in featureValues ) all[name] = value;
				pixel = WithRenderStates( pixel, module.RenderStates( all ) );
			}

			return new ModuleProgram
			{
				Vertex = vertex,
				Pixel = pixel,
				Compute = compute,
				Task = task,
				Mesh = mesh,
				Key = $"{module.Text.GetHashCode()}|{vertex?.Name}|{pixel?.Name}|{compute?.Name}|{task?.Name}|{mesh?.Name}|{Key( pixel?.RenderStates )}|{Key( vertex )}{Key( pixel )}{Key( compute )}{Key( task )}{Key( mesh )}",
			};
		}
		catch ( ShaderCompileException e )
		{
			bool first;
			lock ( reported ) first = reported.Add( $"{module.Path}|{e.Errors}" );
			if ( first ) Error?.Invoke( $"{Path.GetFileName( module.Path )} doesn't compile, so {Path.GetFileName( shader.Name )} {(shader.Outdated is null ? "draws from its compiled programs" : "is recompiled through the ShaderCompiler")}: {e.Errors}" );
			return null;
		}
	}

	static string Key( IReadOnlyList<(int State, ShaderVariable Variable)> states ) => states is null ? "" : string.Join( ',', states.Select( s => $"{s.State}={s.Variable.FloatDefault[0]}" ) );

	static string Key( ModuleStage stage ) => stage is null ? "|" : string.Join( ',', stage.Constants.Select( c => $"{c.Id}={c.Value}" ) ) + "|";

	static readonly StageKind[] Kinds = [StageKind.Vertex, StageKind.Pixel, StageKind.Compute, StageKind.Task, StageKind.Mesh];

	/// <summary>
	/// Borealis's own passes (<c>borealis_*</c>: GPU-driven culling, ids, shadow tiles): waited for even in the background,
	/// since the draws that follow read exactly what they write, and their compiled programs may be older than the module.
	/// </summary>
	static bool Borealis( PassModule module ) => Path.GetFileName( module.Path ).StartsWith( "borealis_", StringComparison.OrdinalIgnoreCase );

	static string StageKey( PassModule module, StageKind kind, Dictionary<string, int> values ) =>
		$"{module.Path}|{module.Text.GetHashCode()}|{kind}|{string.Join( ';', values.OrderBy( v => v.Key, StringComparer.Ordinal ).Select( v => $"{v.Key}={v.Value}" ) )}";

	/// <summary>Start compiling a stage on the compile threads unless it's compiled, failed or compiling; its task.</summary>
	Task<CompiledPassStage> Start( PassModule module, StageKind kind, Dictionary<string, int> values )
	{
		var key = StageKey( module, kind, values );
		lock ( stages )
		{
			if ( stages.ContainsKey( key ) || failed.ContainsKey( key ) ) return null;
			if ( running.TryGetValue( key, out var known ) ) return known;
		}

		var task = Compiler().CompilePassAsync( module, kind, values );
		lock ( stages ) return running.TryAdd( key, task ) ? task : running[key];
	}

	ModuleStage Compiled( ShaderFile shader, PassModule module, StageKind kind, Dictionary<string, int> values, Func<string, int?> attributes )
	{
		var key = StageKey( module, kind, values );
		(ModuleStage Stage, IReadOnlyList<SpecializationSlot> Slots) entry;
		ShaderCompileException failure;
		lock ( stages )
		{
			stages.TryGetValue( key, out entry );
			failed.TryGetValue( key, out failure );
		}

		// Not compiled again each time it's asked for: a changed module is another key
		if ( failure is not null ) throw failure;
		if ( entry.Stage is null )
		{
			var task = Start( module, kind, values );
			CompiledPassStage compiled = null;
			try
			{
				if ( task is not null )
				{
					using ( Zones.PassCompile.Start() ) compiled = task.GetAwaiter().GetResult();
				}
			}
			catch ( ShaderCompileException e )
			{
				lock ( stages )
				{
					failed[key] = e;
					running.Remove( key );
				}

				throw;
			}

			lock ( stages )
			{
				running.Remove( key );
				if ( compiled is null )
				{
					// Another caller recorded it meanwhile
					if ( failed.TryGetValue( key, out failure ) ) throw failure;
					entry = stages[key];
				}
				else if ( stages.TryGetValue( key, out var made ) )
				{
					entry = made;
				}
				else
				{
					entry = (Describe( shader, kind, compiled, key ), compiled.Stage.Specializations);
					stages[key] = entry;
					StagesCompiled++;
				}
			}
		}

		// Specialization constants (SpecializedConstant() globals) take their attributes' values, as for compiled programs
		if ( entry.Slots.Count == 0 || attributes is null ) return entry.Stage;
		var constants = entry.Slots.Select( s => attributes( s.Attribute ) is int value
			? new SpecializationConstant( (uint)s.Id, (uint)value )
			: new SpecializationConstant( (uint)s.Id, (uint)(int)(s.Annotations?.Default is { Length: > 0 } d ? d[0] : 0) ) ).ToArray();
		return Copy( entry.Stage, constants: constants );
	}

	static ModuleStage WithRenderStates( ModuleStage stage, IReadOnlyList<(string State, string Value)> states )
	{
		var literals = new List<(int, ShaderVariable)>();
		foreach ( var (state, value) in states )
		{
			if ( RenderStateNames.TryParse( state, value, out var id, out var variable ) ) literals.Add( (id, variable) );
		}

		return Copy( stage, renderStates: literals );
	}

	static ModuleStage Copy( ModuleStage stage, SpecializationConstant[] constants = null, IReadOnlyList<(int, ShaderVariable)> renderStates = null, PrimitiveExpansion? expansion = null ) => new()
	{
		Expansion = expansion ?? stage.Expansion,
		Type = stage.Type,
		Name = stage.Name,
		Spirv = stage.Spirv,
		AttributeMap = stage.AttributeMap,
		ThreadGroupSize = stage.ThreadGroupSize,
		Constants = constants ?? stage.Constants,
		Resources = stage.Resources,
		Globals = stage.Globals,
		GlobalMembers = stage.GlobalMembers,
		ConstantBuffers = stage.ConstantBuffers,
		RenderStates = renderStates ?? stage.RenderStates,
	};

	/// <summary>A compiled stage as a shader file's tables would say it: each binding's variable, the <c>$Globals</c> members' sources.</summary>
	static ModuleStage Describe( ShaderFile shader, StageKind kind, CompiledPassStage compiled, string key )
	{
		var stage = compiled.Stage;
		var resources = new List<ModuleResource>();
		var buffers = new List<ConstantBufferLayout>();
		(int, int, int) globals = (-1, -1, 0);
		foreach ( var b in stage.Bindings )
		{
			if ( b.Set >= ShaderAbi.BindlessSet ) continue;
			switch ( b.Kind )
			{
				case BindingKind.Globals:
					globals = (b.Set, b.Binding, stage.Globals?.Size ?? 0);
					continue;
				case BindingKind.Sampler:
					resources.Add( new ModuleResource( b.Set, b.Binding, Variable( b, VariableType.SamplerState, RegisterType.SamplerState ), BackendContext.SurfaceSampler( b.Annotations ) ) );
					continue;
				case BindingKind.ConstantBuffer:
					buffers.Add( new ConstantBufferLayout { Name = b.Name, Size = b.Size, Members = [] } );
					resources.Add( new ModuleResource( b.Set, b.Binding, Variable( b, VariableType.ConstantBuffer, RegisterType.ConstantBuffer ) ) );
					continue;
				case BindingKind.Buffer:
				case BindingKind.MaterialRecords:
					// An append buffer's counter (X.counter, SlangCompiler.AppendBuffers) names no variable but its buffer's
					if ( b.Variable.EndsWith( CounterSuffix, StringComparison.Ordinal ) )
					{
						resources.Add( new ModuleResource( b.Set, b.Binding, null, CounterOf: b.Variable[..^CounterSuffix.Length] ) );
						continue;
					}

					resources.Add( new ModuleResource( b.Set, b.Binding, b.Variable.Length == 0 ? null : Variable( b, VariableType.StructuredBuffer, RegisterType.Uav ) ) );
					continue;
				case BindingKind.StorageTexture:
					resources.Add( new ModuleResource( b.Set, b.Binding, Variable( b, VariableType.RWTexture2D, RegisterType.Uav ) ) );
					continue;
				default:
					resources.Add( new ModuleResource( b.Set, b.Binding, Variable( b, b.Shape switch
					{
						TextureShape.Cube => VariableType.TextureCube,
						TextureShape.CubeArray => VariableType.TextureCubeArray,
						TextureShape.Texture3D => VariableType.Texture3D,
						TextureShape.Texture2DArray => VariableType.Texture2DArray,
						_ => VariableType.Texture2D,
					}, RegisterType.Texture ) ) );
					continue;
			}
		}

		var members = new List<(ShaderVariable, int)>();
		foreach ( var member in stage.Globals?.Members ?? [] )
		{
			var a = member.Annotations;
			var source = a?.Attribute is not null ? VariableSource.Attribute : VariableSource.Material;
			var sourceIndex = -1;
			switch ( a?.Source?.Trim() )
			{
				case "Viewport": source = VariableSource.Viewport; break;
				case "InvViewportSize": source = VariableSource.InvViewportSize; break;
				case "TextureDim": source = VariableSource.TextureDimensions; break;
				case "InvTextureDim": source = VariableSource.InvTextureDimensions; break;
				case "TextureDimLog2": source = VariableSource.TextureDimensionsLog2; break;
				case "ShadingComplexity": source = VariableSource.ShadingComplexity; break;
				case "ShaderIdColor" or "ShaderIDColor": source = VariableSource.ShaderIdColor; break;
			}

			// A texture-derived value names its texture by the texture's index in the stage's variables (its named resources first)
			if ( a?.SourceArg is { } argument )
				sourceIndex = resources.Where( r => r.Variable is not null ).ToList().FindIndex( r => r.Variable.Name == argument.Trim() );

			var defaults = a?.Default ?? [];
			members.Add( (new ShaderVariable
			{
				Name = member.Name,
				Attribute = a?.Attribute ?? "",
				Source = source,
				SourceIndex = sourceIndex,
				Type = Type( member ),
				Register = member.Kind == ScalarKind.Float ? RegisterType.Float4 : member.Kind == ScalarKind.Bool ? RegisterType.Bool : RegisterType.Int4,
				IntDefault = [.. Enumerable.Range( 0, 4 ).Select( i => i < defaults.Length ? (int)MathF.Round( defaults[i] ) : 0 )],
				FloatDefault = [.. Enumerable.Range( 0, 4 ).Select( i => i < defaults.Length ? defaults[i] : 0f )],
			}, member.Offset) );
		}

		// Vertex inputs' semantics as a compiled program's attribute map: usage << 4 | index, by location
		byte[] map = [];
		if ( kind == StageKind.Vertex && stage.Inputs.Count > 0 )
		{
			map = new byte[stage.Inputs.Max( i => i.Location ) + 1];
			foreach ( var input in stage.Inputs )
			{
				var usage = Array.IndexOf( Usages, input.Semantic );
				map[input.Location] = (byte)((Math.Max( usage, 0 ) << 4) | (input.SemanticIndex & 15));
			}
		}

		return new ModuleStage
		{
			Type = kind switch { StageKind.Vertex => ShaderProgramType.Vertex, StageKind.Compute => ShaderProgramType.Compute, StageKind.Task => ShaderProgramType.Task, StageKind.Mesh => ShaderProgramType.Mesh, _ => ShaderProgramType.Pixel },
			Name = $"{Path.GetFileName( shader.Name )} {stage.Entry} ({key.GetHashCode():x8})",
			Spirv = stage.Spirv,
			AttributeMap = map,
			ThreadGroupSize = compiled.ThreadGroupSize,
			Constants = [.. stage.Specializations.Select( s => new SpecializationConstant( (uint)s.Id, (uint)(int)(s.Annotations?.Default is { Length: > 0 } d ? d[0] : 0) ) )],
			Resources = resources,
			Globals = globals,
			GlobalMembers = members,
			ConstantBuffers = buffers,
		};
	}

	/// <summary>Usages in <c>SemanticUsageVulkan_t</c> order.</summary>
	static readonly string[] Usages = ["POSITION", "BLENDWEIGHT", "BLENDINDICES", "NORMAL", "PSIZE", "TEXCOORD", "TANGENT", "BINORMAL", "POSITIONT", "COLOR"];

	const string CounterSuffix = ".counter";

	static ShaderVariable Variable( ShaderBinding b, VariableType type, RegisterType register ) => new()
	{
		Name = b.Variable,
		Attribute = b.Annotations?.Attribute ?? "",
		Source = b.Annotations?.Attribute is not null ? VariableSource.Attribute : VariableSource.Material,
		Type = type,
		Register = register,
		RenderStateId = register == RegisterType.Texture && (b.Annotations?.SrgbRead ?? false) ? 1 : 0,
		IntDefault = [0, 0, 0, 0],
		FloatDefault = [0, 0, 0, 0],
	};

	static VariableType Type( GlobalMember member )
	{
		var components = Math.Clamp( member.Components, 1, 16 );
		if ( components > 4 ) return components == 9 ? VariableType.Matrix3x3 : components == 12 ? VariableType.Matrix3x4 : VariableType.Matrix4x4;
		var first = member.Kind switch { ScalarKind.Float => VariableType.Float, ScalarKind.Bool => VariableType.Bool, _ => VariableType.Int };
		return first + (components - 1);
	}
}
