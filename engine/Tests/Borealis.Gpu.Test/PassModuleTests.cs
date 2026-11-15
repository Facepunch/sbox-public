using Borealis.Backend;
using Borealis.Gpu;
using Borealis.Source2;
using Borealis.Source2.Shaders;
using Sandbox.ShaderCompiler;
using System.IO;

namespace BorealisGpuTests;

/// <summary>
/// Pass modules (docs/managed/shader-format-replacement.md, "Pass modules"): each core shader's module, compiled with
/// Slang, makes the same variants its compiled <c>.shader_c</c> does, binding for binding, constant for constant and
/// render state for render state, in every mode and over its dynamic combos.
/// </summary>
[TestClass]
[DoNotParallelize]
public class PassModuleTests : GpuTest
{
	static string Core => ContentFiles.FindGame().Roots.FirstOrDefault( r => File.Exists( Path.Combine( r, "shaders", "sbox", "pass.slangh" ) ) );

	/// <summary>Every pass module under core/shaders, as the shader path it stands beside.</summary>
	static List<string> Modules()
	{
		if ( Core is null ) Assert.Inconclusive( "No game content with pass modules" );
		var shaders = Path.Combine( Core, "shaders" );
		return Directory.EnumerateFiles( shaders, "*.slang", SearchOption.AllDirectories )
			.Where( f => File.ReadAllText( f ).Contains( "sbox/pass.slangh", StringComparison.Ordinal ) )
			.Select( f => Path.GetRelativePath( Core, Path.ChangeExtension( f, ".shader" ) ).Replace( '\\', '/' ) )
			.Order( StringComparer.Ordinal )
			.ToList();
	}

	[TestMethod]
	public void ConverterKeepsTheShadersCode()
	{
		const string shader = "HEADER\r\n{\r\n\tDescription = \"x\";\r\n}\r\nMODES\r\n{\r\n\tDefault();\r\n}\r\nCOMMON\r\n{\r\n\t#include \"system.fxc\"\r\n}\r\nCS\r\n{\r\n\tDynamicCombo( D_A, 0..1, Sys( ALL ) );\r\n\t[numthreads( 8, 8, 1 )]\r\n\tvoid MainCs( uint2 id : SV_DispatchThreadID )\r\n\t{\r\n\t\tMainCsHelper( id ); // not an entry\r\n\t}\r\n}\r\n";
		var module = PassConverter.Convert( shader, "x" );
		StringAssert.Contains( module, "#include \"sbox/pass.slangh\"" );
		StringAssert.Contains( module, "#if ( PROGRAM == VFX_PROGRAM_CS )" );
		StringAssert.Contains( module, "[shader( \"compute\" )] void MainCs( uint2 id : SV_DispatchThreadID )" );
		Assert.AreEqual( 1, module.Split( "[shader(" ).Length - 1 );

		var read = PassModule.FromText( "x.slang", module );
		Assert.IsTrue( read.Has( StageKind.Compute ) );
		Assert.IsFalse( read.Has( StageKind.Pixel ) );
		Assert.AreEqual( "D_A", read.Combos( StageKind.Compute ).Single().Name );

		// Code before MODES (line.shader includes its feature lines there) comes after the declarations' macros
		var early = PassConverter.Convert( "#include \"vr_common_features.fxc\"\r\n" + shader, "x" );
		Assert.IsTrue( early.IndexOf( "#include \"sbox/pass.slangh\"", StringComparison.Ordinal ) < early.IndexOf( "vr_common_features.fxc", StringComparison.Ordinal ) );
		Assert.AreEqual( 1, early.Split( "sbox/pass.slangh" ).Length - 1 );
	}

	[TestMethod]
	public void RenderStatesFollowTheCombos()
	{
		const string text = "#include \"sbox/pass.slangh\"\n#if ( PROGRAM == VFX_PROGRAM_PS )\nDynamicCombo( D_X, 0..2, Sys( ALL ) );\nFeature( F_BACK, 0..1, \"x\" );\n#if ( D_X == 2 )\nRenderState( BlendEnable, true );\n#else\nRenderState( BlendEnable, false );\n#endif\nRenderState( CullMode, F_BACK ? NONE : BACK );\n[shader( \"pixel\" )] float4 MainPs() : SV_Target0 { return 0; }\n#endif\n";
		var module = PassModule.FromText( "x.slang", text );
		Assert.AreEqual( 2, module.Combos( StageKind.Pixel ).Single().Max );
		CollectionAssert.AreEqual( new[] { ("BlendEnable", "1"), ("CullMode", "BACK") }, module.RenderStates( new Dictionary<string, int> { ["D_X"] = 2 } ).ToArray() );
		CollectionAssert.AreEqual( new[] { ("BlendEnable", "0"), ("CullMode", "NONE") }, module.RenderStates( new Dictionary<string, int> { ["F_BACK"] = 1 } ).ToArray() );
	}

	/// <summary>Each module's variants against its compiled shader's: the same bindings, constants, inputs and render state.</summary>
	[TestMethod]
	public void PassModulesMatchTheirShaders()
	{
		var modules = Modules();
		if ( modules.Count == 0 ) Assert.Inconclusive( "No pass modules" );

		using var backend = new BackendDevice( Device );
		var errors = new List<string>();
		backend.Passes.Error = e => errors.Add( e );
		var differences = new List<string>();
		var compared = 0;
		foreach ( var path in modules )
		{
			var shader = backend.Shaders.Load( path );
			if ( shader is null )
			{
				differences.Add( $"{path}: no compiled .shader_c to compare with" );
				continue;
			}

			foreach ( var mode in shader.Modes )
			{
				if ( !string.IsNullOrEmpty( mode.Fallback ) ) continue;
				foreach ( var combos in ComboSets( shader ) )
				{
					int? Combo( string name ) => combos.TryGetValue( name, out var v ) ? v : null;
					ShaderVariant classic;
					backend.SlangPasses = false;
					try
					{
						classic = backend.Shaders.Variant( shader, mode.Name, [], Combo );
					}
					catch ( InvalidDataException )
					{
						continue;
					}

					backend.SlangPasses = true;
					var module = backend.Shaders.Variant( shader, mode.Name, [], Combo );
					var where = $"{path} {mode.Name} {string.Join( ' ', combos.Where( c => c.Value != 0 ).Select( c => $"{c.Key}={c.Value}" ) )}";
					if ( module is not { FromModule: true } )
					{
						differences.Add( $"{where}: not drawn from its module" );
						continue;
					}

					compared++;
					differences.AddRange( Compare( classic, module ).Select( d => $"{where}: {d}" ) );

					// The first mode's first combos as SPIR-V, both ways, for register counts: PASS_MODULE_SPIRV=<folder>
					if ( Environment.GetEnvironmentVariable( "PASS_MODULE_SPIRV" ) is { Length: > 0 } dump && mode == shader.Modes[0] && combos.Values.All( v => v == 0 ) )
					{
						var stem = Path.Combine( dump, path.Replace( "shaders/", "" ).Replace( ".shader", "" ).Replace( '/', '_' ) );
						foreach ( var (stage, a, b) in new[] { ("ps", classic.Pixel, module.Pixel), ("cs", classic.Compute, module.Compute) } )
						{
							if ( a?.Bytecode is null || b?.Bytecode is null ) continue;
							Directory.CreateDirectory( dump );
							File.WriteAllBytes( $"{stem}.{stage}.classic.spv", a.Bytecode.Shared.Spirv );
							File.WriteAllBytes( $"{stem}.{stage}.module.spv", b.Bytecode.Spirv );
						}
					}
				}
			}
		}

		// Every difference, for working through them: PASS_MODULE_REPORT=<file>
		if ( Environment.GetEnvironmentVariable( "PASS_MODULE_REPORT" ) is { Length: > 0 } report )
			File.WriteAllLines( report, [$"{modules.Count} modules, {compared} variants", .. errors, .. differences] );

		Assert.AreEqual( 0, errors.Count, string.Join( "\n", errors.Take( 5 ) ) );
		Assert.AreEqual( 0, differences.Count, $"{differences.Count} differences over {compared} variants:\n" + string.Join( "\n", differences.Take( 60 ) ) );
		TestContext.WriteLine( $"{modules.Count} modules, {compared} variants compared" );
	}

	/// <summary>Every dynamic combo at its first value, then each value of each combo with the rest at their first.</summary>
	static IEnumerable<Dictionary<string, int>> ComboSets( ShaderFile shader )
	{
		var combos = new List<ShaderCombo>();
		foreach ( var type in new[] { ShaderProgramType.Vertex, ShaderProgramType.Pixel, ShaderProgramType.Compute } )
		{
			foreach ( var combo in shader[type]?.DynamicCombos ?? [] )
			{
				if ( !combos.Exists( c => c.Name == combo.Name ) ) combos.Add( combo );
			}
		}

		var first = combos.ToDictionary( c => c.Name, c => c.Min );
		yield return first;
		foreach ( var combo in combos )
		{
			for ( int value = combo.Min + 1; value <= combo.Max; value++ )
				yield return new Dictionary<string, int>( first ) { [combo.Name] = value };
		}
	}

	static IEnumerable<string> Compare( ShaderVariant classic, ShaderVariant module )
	{
		foreach ( var (name, a, b) in new[] { ("vertex", classic.Vertex, module.Vertex), ("pixel", classic.Pixel, module.Pixel), ("compute", classic.Compute, module.Compute) } )
		{
			if ( (a?.Bytecode is null) != (b?.Bytecode is null) )
			{
				yield return $"{name} stage {(a?.Bytecode is null ? "only in the module" : "missing from the module")}";
				continue;
			}

			if ( a?.Bytecode is null ) continue;
			if ( name == "compute" && a.Bytecode.ThreadGroupSize != b.Bytecode.ThreadGroupSize ) yield return $"thread group {a.Bytecode.ThreadGroupSize} vs {b.Bytecode.ThreadGroupSize}";

			// Material constants the classic stage reads: the same offset and source in the module's $Globals
			var moduleConstants = b.MaterialConstants.ToDictionary( c => c.Variable.Name, c => c );
			foreach ( var (variable, offset) in a.MaterialConstants )
			{
				if ( !moduleConstants.TryGetValue( variable.Name, out var other ) ) yield return $"{name} constant {variable.Name} missing";
				else if ( other.Offset != offset ) yield return $"{name} constant {variable.Name} at {offset} vs {other.Offset}";
				else if ( other.Variable.Source != variable.Source && !(variable.Source == VariableSource.Material && other.Variable.Source == VariableSource.Attribute) ) yield return $"{name} constant {variable.Name} from {variable.Source} vs {other.Variable.Source}";
				else if ( !string.Equals( other.Variable.Attribute ?? "", variable.Attribute ?? "", StringComparison.OrdinalIgnoreCase ) && variable.Source == VariableSource.Attribute ) yield return $"{name} constant {variable.Name} attribute {variable.Attribute} vs {other.Variable.Attribute}";
			}

			if ( a.MaterialBuffer.Set >= 0 && (a.MaterialBuffer.Set, a.MaterialBuffer.Binding) != (b.MaterialBuffer.Set, b.MaterialBuffer.Binding) )
				yield return $"{name} material constants at {a.MaterialBuffer} vs {b.MaterialBuffer}";
		}

		// Samplers: the same states in each set. Slang numbers a sampler a .shader leaves unregistered after every sampler
		// the includes declare, where vfx's parser gives it the first free register; each binds where its own SPIR-V says
		static string Samplers( ShaderVariant v, int set ) => string.Join( ';', v.Resources.Where( r => r.Set == set && r.Kind == ShaderResourceKind.Sampler ).Select( r => State( r.Sampler ) ).Order( StringComparer.Ordinal ) );
		static string State( SamplerInfo s ) => $"{s.MinFilter}/{s.MagFilter}/{s.MipFilter}/{s.AddressU}/{s.AddressV}/{s.AddressW}/{s.Compare}/{s.MaxAnisotropy}";
		foreach ( var set in classic.Resources.Concat( module.Resources ).Select( r => r.Set ).Distinct() )
		{
			if ( Samplers( classic, set ) != Samplers( module, set ) ) yield return $"set {set} samplers {Samplers( classic, set )} vs {Samplers( module, set )}";
		}

		// Bindings: where, what type, and the name they bind by
		var classicResources = classic.Resources.Where( r => r.Kind != ShaderResourceKind.Sampler ).ToDictionary( r => (r.Set, r.Binding) );
		var moduleResources = module.Resources.Where( r => r.Kind != ShaderResourceKind.Sampler ).ToDictionary( r => (r.Set, r.Binding) );
		foreach ( var (at, r) in classicResources )
		{
			if ( !moduleResources.TryGetValue( at, out var m ) )
			{
				yield return $"binding {r} missing";
				continue;
			}

			if ( r.Type != m.Type || r.Kind != m.Kind || r.Dimension != m.Dimension || r.Count != m.Count || r.Stages != m.Stages ) yield return $"binding {r} ({r.Dimension}, {r.Stages}) vs {m} ({m.Dimension}, {m.Stages})";
			else if ( r.Kind != ShaderResourceKind.MaterialConstants && r.Kind != ShaderResourceKind.Unknown && !string.Equals( r.Name, m.Name, StringComparison.OrdinalIgnoreCase ) ) yield return $"binding {r} named {m.Name}";
			else if ( r.Kind == ShaderResourceKind.Texture && r.Variable.RenderStateId == 1 != (m.Variable.RenderStateId == 1) ) yield return $"binding {r} sRGB {r.Variable.RenderStateId == 1} vs {m.Variable.RenderStateId == 1}";
		}

		foreach ( var (at, m) in moduleResources )
		{
			if ( !classicResources.ContainsKey( at ) ) yield return $"binding {m} only in the module";
		}

		// Vertex inputs
		var inputs = string.Join( ' ', classic.VertexInputs );
		var moduleInputs = string.Join( ' ', module.VertexInputs );
		if ( inputs != moduleInputs ) yield return $"vertex inputs {inputs} vs {moduleInputs}";

		// Render state
		var s = classic.State;
		var t = module.State;
		if ( s.Cull != t.Cull ) yield return $"cull {s.Cull} vs {t.Cull}";
		if ( s.Wireframe != t.Wireframe ) yield return $"wireframe {s.Wireframe} vs {t.Wireframe}";
		if ( s.DepthTest != t.DepthTest || s.DepthWrite != t.DepthWrite || (s.DepthTest && s.DepthCompare != t.DepthCompare) ) yield return $"depth {s.DepthTest}/{s.DepthWrite}/{s.DepthCompare} vs {t.DepthTest}/{t.DepthWrite}/{t.DepthCompare}";
		if ( s.StencilTest != t.StencilTest || (s.StencilTest && (s.StencilFront != t.StencilFront || s.StencilBack != t.StencilBack || s.StencilReference != t.StencilReference || s.StencilReadMask != t.StencilReadMask || s.StencilWriteMask != t.StencilWriteMask)) ) yield return "stencil differs";
		if ( s.AlphaToCoverage != t.AlphaToCoverage ) yield return $"alpha to coverage {s.AlphaToCoverage} vs {t.AlphaToCoverage}";
		if ( s.DepthBiasConstant != t.DepthBiasConstant || s.DepthBiasSlope != t.DepthBiasSlope ) yield return "depth bias differs";
		if ( s.Blend.Length != t.Blend.Length ) yield return $"{s.Blend.Length} blend targets vs {t.Blend.Length}";
		else
		{
			for ( int i = 0; i < s.Blend.Length; i++ )
			{
				var x = s.Blend[i];
				var y = t.Blend[i];
				if ( x.Enable != y.Enable || x.WriteMask != y.WriteMask || (x.Enable && !x.Equals( y )) ) yield return $"blend {i}: {x} vs {y}";
			}
		}
	}
}
