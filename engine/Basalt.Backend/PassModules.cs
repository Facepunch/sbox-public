using System.IO;
using System.Linq;
using Basalt.Gpu;
using Basalt.Source2.Shaders;
using Sandbox.ShaderCompiler;

namespace Basalt.Backend;

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
	SlangCompiler compiler;

	internal PassModules( BackendDevice device ) => this.device = device;

	/// <summary>
	/// Told once for each module and combo values that don't compile, with the compiler's messages; the console's error
	/// stream unless set.
	/// </summary>
	public Action<string> Error { get; set; } = message => Console.Error.WriteLine( $"Basalt.Gpu: {message}" );

	/// <summary>Stages compiled (or read from the cache) so far.</summary>
	public int StagesCompiled { get; private set; }

	/// <summary>
	/// Where compiled stages are kept between runs; <c>game/.source2/basaltpasses</c> beside the engine's own caches unless
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
		return compiler = new SlangCompiler( Path.Combine( game, "bin", "win64" ), [Path.Combine( Core, "shaders" )] )
		{
			CacheFolder = CacheFolder ?? Path.Combine( game, ".source2", "basaltpasses" ),
		};
	}

	/// <summary>
	/// The pass module beside a shader's source, or null. Its file is looked at again at most once a second, and read again
	/// when it changed.
	/// </summary>
	public PassModule ModuleOf( ShaderFile shader )
	{
		var name = shader.Name;
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
			if ( used.Add( Path.GetFileNameWithoutExtension( module.Path ) ) && LogUse ) Console.WriteLine( $"Basalt.Gpu: {Path.GetFileName( module.Path )} draws {Path.GetFileName( shader.Name )}" );
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

		ModuleStage Stage( StageKind kind, out Dictionary<string, int> values )
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

			return Compiled( shader, module, kind, values, attributes );
		}

		try
		{
			var vertex = Stage( StageKind.Vertex, out _ );
			var pixel = Stage( StageKind.Pixel, out var pixelValues );
			var compute = Stage( StageKind.Compute, out _ );
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
				Key = $"{module.Text.GetHashCode()}|{vertex?.Name}|{pixel?.Name}|{compute?.Name}|{Key( pixel?.RenderStates )}|{Key( vertex )}{Key( pixel )}{Key( compute )}",
			};
		}
		catch ( ShaderCompileException e )
		{
			bool first;
			lock ( reported ) first = reported.Add( $"{module.Path}|{e.Errors}" );
			if ( first ) Error?.Invoke( $"{Path.GetFileName( module.Path )} doesn't compile, so {Path.GetFileName( shader.Name )} draws from its compiled programs: {e.Errors}" );
			return null;
		}
	}

	static string Key( IReadOnlyList<(int State, ShaderVariable Variable)> states ) => states is null ? "" : string.Join( ',', states.Select( s => $"{s.State}={s.Variable.FloatDefault[0]}" ) );

	static string Key( ModuleStage stage ) => stage is null ? "|" : string.Join( ',', stage.Constants.Select( c => $"{c.Id}={c.Value}" ) ) + "|";

	ModuleStage Compiled( ShaderFile shader, PassModule module, StageKind kind, Dictionary<string, int> values, Func<string, int?> attributes )
	{
		var key = $"{module.Path}|{module.Text.GetHashCode()}|{kind}|{string.Join( ';', values.OrderBy( v => v.Key, StringComparer.Ordinal ).Select( v => $"{v.Key}={v.Value}" ) )}";
		(ModuleStage Stage, IReadOnlyList<SpecializationSlot> Slots) entry;
		lock ( stages ) stages.TryGetValue( key, out entry );
		if ( entry.Stage is null )
		{
			var compiled = Compiler().CompilePass( module, kind, values );
			entry = (Describe( shader, kind, compiled, key ), compiled.Stage.Specializations);
			lock ( stages ) stages[key] = entry;
			StagesCompiled++;
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

	static ModuleStage Copy( ModuleStage stage, SpecializationConstant[] constants = null, IReadOnlyList<(int, ShaderVariable)> renderStates = null ) => new()
	{
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
					// An append buffer's counter is named nothing (SlangCompiler.CompilePass), as in a compiled program's tables
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
				case "ShaderIdColor": source = VariableSource.ShaderIdColor; break;
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
			Type = kind switch { StageKind.Vertex => ShaderProgramType.Vertex, StageKind.Compute => ShaderProgramType.Compute, _ => ShaderProgramType.Pixel },
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
