using Borealis.Backend;
using Borealis.Source2;
using Borealis.Source2.Shaders;
using Sandbox.ShaderCompiler;
using System.IO;

namespace BorealisGpuTests;

/// <summary>
/// Standalone modules (docs/reference/borealis-standalone-modules.md): a module that declares its modes describes its shader itself, so
/// Borealis.Gpu reads no <c>.shader_c</c> for it, and makes its GPU-driven form from the same source. depth_only is one:
/// its variants, classic and GPU-driven, are its <c>.shader_c</c>'s and <c>.gpudriven.shader_c</c>'s, binding for binding.
/// </summary>
[TestClass]
[DoNotParallelize]
public class StandaloneModuleTests : GpuTest
{
	const string DepthOnly = "shaders/depth_only.shader";

	/// <summary>The modes, features and GPU-driven condition read from a module's declarations.</summary>
	[TestMethod]
	public void DeclarationsDescribeTheShader()
	{
		const string text = "#include \"sbox/pass.slangh\"\nMode( Depth );\nMode( Tools, S_MODE_TOOLS, \"other.shader\" );\nFeature( F_A, 0..1, \"x\" );\nStaticCombo( S_A, F_A, Sys( ALL ) );\n"
			+ "#if ( PROGRAM == VFX_PROGRAM_VS )\nGpuDrivenVariant( !S_A );\n[shader( \"vertex\" )] float4 MainVs() : SV_Position { return 0; }\n#endif\n";
		var module = PassModule.FromText( "test.slang", text );
		Assert.IsTrue( module.Standalone );
		CollectionAssert.AreEqual( new[] { "Depth", "Tools" }, module.Modes.Select( m => m.Name ).ToArray() );
		CollectionAssert.AreEqual( new[] { "S_MODE_TOOLS", "\"other.shader\"" }, module.Modes[1].Arguments );
		Assert.IsTrue( module.HasGpuDriven( StageKind.Vertex ) );
		Assert.IsFalse( module.HasGpuDriven( StageKind.Pixel ) );
		Assert.IsTrue( module.Combos( StageKind.Vertex ).Any( c => c.Name == PassModule.GpuDrivenCombo && !c.Static ) );

		var on = new Dictionary<string, int> { [PassModule.GpuDrivenCombo] = 1, ["S_A"] = 0 };
		Assert.IsTrue( module.Allows( StageKind.Vertex, on ) );
		Assert.IsFalse( module.Allows( StageKind.Vertex, new Dictionary<string, int>( on ) { ["S_A"] = 1 } ), "the condition rules the GPU-driven form out" );
		Assert.IsFalse( PassModule.FromText( "converted.slang", "#include \"sbox/pass.slangh\"\n// MODES: Depth();\n" ).Standalone );
	}

	/// <summary>depth_only loads from its module alone: its modes and features, no compiled programs.</summary>
	[TestMethod]
	public void DepthOnlyIsStandalone()
	{
		using var backend = new BackendDevice( Device );
		var shader = backend.Shaders.Load( DepthOnly );
		Assert.IsNotNull( shader );
		Assert.IsTrue( shader.ModuleOnly, "described by depth_only.slang, not its .shader_c" );
		CollectionAssert.AreEqual( new[] { "Depth" }, shader.Modes.Select( m => m.Name ).ToArray() );
		foreach ( var feature in (string[])["F_ALPHA_TEST", "F_TRANSLUCENT", "F_RENDER_BACKFACES"] ) Assert.IsTrue( shader.FeatureIndex( feature ) >= 0, feature );
		Assert.AreSame( shader, backend.Shaders.GpuDriven( shader ), "its GPU-driven form is the module's" );

		// Skipped, it's the .shader_c again: the A/B switch
		using var skipped = new BackendDevice( Device );
		skipped.Passes.Skip.Add( "depth_only" );
		Assert.IsFalse( skipped.Shaders.Load( DepthOnly ).ModuleOnly );
	}

	/// <summary>
	/// Every depth_only variant (opaque, alpha-tested, translucent; each dynamic combo) from the module binds what the
	/// <c>.shader_c</c>'s does; the GPU-driven forms (opaque, and fading) bind what the <c>.gpudriven.shader_c</c>'s do; the
	/// alpha-tested and translucent ones have no GPU-driven form.
	/// </summary>
	[TestMethod]
	public void DepthOnlyMatchesItsShader()
	{
		using var backend = new BackendDevice( Device );
		var errors = new List<string>();
		backend.Passes.Error = errors.Add;
		var module = backend.Shaders.Load( DepthOnly );
		var compiledPath = backend.Content.Find( DepthOnly ) ?? throw new AssertInconclusiveException( "depth_only.shader isn't compiled" );
		var compiled = ShaderFile.Load( compiledPath );
		var gpuDrivenPath = compiledPath[..^".shader_c".Length] + ".gpudriven.shader_c";
		var hasGpuDriven = File.Exists( gpuDrivenPath );

		var differences = new List<string>();
		var compared = 0;
		foreach ( var (name, featureName) in new[] { ("opaque", null), ("alpha test", "F_ALPHA_TEST"), ("translucent", "F_TRANSLUCENT") } )
		{
			var features = new int[compiled.Features.StaticCombos.Length];
			var moduleFeatures = new int[module.Features.StaticCombos.Length];
			if ( featureName is not null )
			{
				features[compiled.FeatureIndex( featureName )] = 1;
				moduleFeatures[module.FeatureIndex( featureName )] = 1;
			}

			foreach ( var combos in PassModuleTests.ComboSets( compiled ) )
			{
				int? Combo( string combo ) => combos.TryGetValue( combo, out var v ) ? v : null;
				var where = $"{name} {string.Join( ' ', combos.Where( c => c.Value != 0 ).Select( c => $"{c.Key}={c.Value}" ) )}";

				backend.SlangPasses = false;
				ShaderVariant classic;
				try
				{
					classic = backend.Shaders.Variant( compiled, "Depth", features, Combo );
				}
				catch ( InvalidDataException )
				{
					continue;
				}

				backend.SlangPasses = true;
				var fromModule = backend.Shaders.Variant( module, "Depth", moduleFeatures, Combo );
				if ( fromModule is not { FromModule: true } )
				{
					differences.Add( $"{where}: not drawn from the module" );
					continue;
				}

				compared++;
				differences.AddRange( ByName( PassModuleTests.Compare( classic, fromModule ) ).Select( d => $"{where}: {d}" ) );

				// GPU-driven: the opaque and fading forms, skinned ones from the vertex cache included, not alpha-tested or translucent
				var gpuDriven = backend.Shaders.Variant( module, "Depth", moduleFeatures, Combo, null, gpuDriven: true );
				if ( featureName is not null )
				{
					if ( gpuDriven is not null ) differences.Add( $"{where}: a GPU-driven form where none should be" );
					continue;
				}

				if ( gpuDriven is null )
				{
					differences.Add( $"{where}: no GPU-driven form" );
					continue;
				}

				if ( !gpuDriven.Vertex.GpuDriven ) differences.Add( $"{where}: GPU-driven vertex stage not marked" );
				if ( gpuDriven.Pixel?.Bytecode is { } && !gpuDriven.Pixel.GpuDriven ) differences.Add( $"{where}: GPU-driven pixel stage not marked" );
				if ( gpuDriven.VertexInputs.Count != 0 ) differences.Add( $"{where}: GPU-driven vertex stage reads vertex inputs {string.Join( ' ', gpuDriven.VertexInputs )}" );
				if ( !hasGpuDriven ) continue;

				backend.SlangPasses = false;
				var vfx = backend.Shaders.Variant( compiled, "Depth", features, Combo, null, gpuDriven: true );
				backend.SlangPasses = true;
				if ( vfx is null ) continue;
				compared++;
				// The tables' constant buffer is where Slang numbers it rather than after vfx's own; it binds by name either way
				differences.AddRange( ByName( PassModuleTests.Compare( vfx, gpuDriven ) ).Select( d => $"{where} GPU-driven: {d}" ) );
				if ( !gpuDriven.Resources.Any( r => r.Name == "BorealisGpuDriven_t" ) ) differences.Add( $"{where} GPU-driven: no BorealisGpuDriven_t" );
			}
		}

		Assert.AreEqual( 0, errors.Count, string.Join( "\n", errors.Take( 5 ) ) );
		Assert.AreEqual( 0, differences.Count, $"{differences.Count} differences over {compared} variants:\n" + string.Join( "\n", differences.Take( 40 ) ) );
		Assert.IsTrue( compared > 0 );
		TestContext.WriteLine( $"{compared} variants compared" );
	}

	/// <summary>
	/// The converter makes a converted module standalone: each <c>// MODES:</c> line a <c>Mode</c> declaration after the include
	/// that defines it, trailing comments dropped, and nothing else changed.
	/// </summary>
	[TestMethod]
	public void ConverterMakesModulesStandalone()
	{
		const string converted = "// x.shader as a pass module: Borealis.Gpu draws this; the .shader stays for the other routes\r\n// and as the fallback (docs/reference/borealis-slang-materials.md, \"Pass modules\").\r\n"
			+ "// HEADER: Description = \"x\";\r\n\r\n// MODES: Forward();\r\n// MODES: Depth( S_MODE_DEPTH ); // its own depth\r\n// MODES: ToolsShadingComplexity( \"tools_shading_complexity.shader\" );\r\n\r\n#include \"sbox/pass.slangh\"\r\n\r\n#include \"system.fxc\"\r\n";
		var text = PassConverter.Standalone( converted, "x" );
		StringAssert.StartsWith( text, "// x: a standalone module" );
		StringAssert.Contains( text, "#include \"sbox/pass.slangh\"\r\n\r\nMode( Forward );\r\nMode( Depth, S_MODE_DEPTH );\r\nMode( ToolsShadingComplexity, \"tools_shading_complexity.shader\" );\r\n" );
		StringAssert.Contains( text, "// HEADER: Description = \"x\";" );
		Assert.IsFalse( text.Contains( "MODES:" ) );
		Assert.IsFalse( text.Replace( "\r\n", "" ).Contains( '\n' ), "line endings kept" );
		Assert.AreEqual( text, PassConverter.Standalone( text, "x" ), "standalone already" );

		var module = PassModule.FromText( "x.slang", text );
		Assert.IsTrue( module.Standalone );
		var modes = PassModules.Modes( module.Modes );
		CollectionAssert.AreEqual( new[] { "Forward", "Depth", "ToolsShadingComplexity" }, modes.Select( m => m.Name ).ToArray() );
		Assert.AreEqual( ("S_MODE_DEPTH", 1), modes[1].Settings.Single() );
		Assert.AreEqual( "tools_shading_complexity.shader", modes[2].Fallback );

		// No modes but comments: the Default mode
		var none = PassConverter.Standalone( "// MODES: // nothing here\n#include \"sbox/pass.slangh\"\n", "y" );
		StringAssert.Contains( none, "Mode( Default );" );
	}

	/// <summary>
	/// Each standalone module that still has a <c>.shader_c</c> beside it (Source 2's twin, or the last one built before its
	/// <c>.shader</c> went) describes the same shader: modes, features and each stage's combos. And its variants in each mode
	/// bind what the compiled file's do, with the dynamic combos at their first values.
	/// </summary>
	[TestMethod]
	public void StandaloneModulesMatchTheirShaders() => CompareStandalone( firstCombos: true );

	/// <summary><see cref="StandaloneModulesMatchTheirShaders"/> over each value of each dynamic combo in turn.</summary>
	[TestMethod]
	[TestCategory( "Slow" )]
	public void StandaloneModuleCombosMatchTheirShaders() => CompareStandalone( firstCombos: false );

	void CompareStandalone( bool firstCombos )
	{
		using var backend = new BackendDevice( Device );
		var errors = new List<string>();
		backend.Passes.Error = errors.Add;

		// Shaders with a geometry stage compare their programs' vertex and pixel stages too
		backend.Shaders.GeometryPrograms = true;
		var core = backend.Content.Roots.FirstOrDefault( r => File.Exists( Path.Combine( r, "shaders", "sbox", "pass.slangh" ) ) ) ?? throw new AssertInconclusiveException( "No core shaders" );
		var differences = new List<string>();
		var described = 0;
		var compared = 0;
		foreach ( var file in Directory.EnumerateFiles( Path.Combine( core, "shaders" ), "*.slang", SearchOption.AllDirectories ).Order( StringComparer.Ordinal ) )
		{
			var compiledPath = Path.ChangeExtension( file, ".shader_c" );
			if ( !File.Exists( compiledPath ) ) continue;
			var text = File.ReadAllText( file );
			if ( !text.Contains( "sbox/pass.slangh", StringComparison.Ordinal ) || PassModule.FromText( file, text ) is not { Standalone: true } passModule ) continue;

			var path = Path.GetRelativePath( core, Path.ChangeExtension( file, ".shader" ) ).Replace( '\\', '/' );
			var module = backend.Shaders.Load( path );
			if ( module is not { ModuleOnly: true } )
			{
				differences.Add( $"{path}: not described by its module" );
				continue;
			}

			ShaderFile compiled;
			try
			{
				compiled = ShaderFile.Load( compiledPath );
			}
			catch ( InvalidDataException )
			{
				continue;
			}

			described++;
			var before = differences.Count;
			differences.AddRange( Describe( compiled, module, passModule.Has( StageKind.Mesh ) ).Select( d => $"{path}: {d}" ) );

			// Code sets values by token (StringToken.Literal) and the game's backend turns tokens back into names from the shader:
			// every name the compiled file's variables and combos give, the module's binding names give too. Not a .vmat's input
			// textures (TextureColor: the material compile's, never drawn) or vfx's own default sampler, which nothing sets
			var names = backend.Passes.BindingNames( module );
			var missing = compiled.Programs.Where( p => p is not null )
				.SelectMany( p => p.Variables.Where( v => v.Register != RegisterType.InputTexture ).SelectMany( v => new[] { v.Name, v.Attribute } ).Concat( p.DynamicCombos.Select( c => c.Name ) ) )
				.Where( n => !string.IsNullOrEmpty( n ) && n != "DefaultSamplerState" && !names.Contains( n ) ).Distinct().Order( StringComparer.Ordinal ).ToList();
			if ( missing.Count > 0 ) differences.Add( $"{path}: names not among the module's binding names: {string.Join( ' ', missing )}" );

			if ( differences.Count > before ) continue;

			foreach ( var mode in compiled.Modes )
			{
				if ( !string.IsNullOrEmpty( mode.Fallback ) ) continue;
				foreach ( var combos in firstCombos ? PassModuleTests.ComboSets( compiled ).Take( 1 ) : PassModuleTests.ComboSets( compiled ).Skip( 1 ) )
				{
					int? Combo( string name ) => combos.TryGetValue( name, out var v ) ? v : null;
					var where = $"{path} {mode.Name} {string.Join( ' ', combos.Where( c => c.Value != 0 ).Select( c => $"{c.Key}={c.Value}" ) )}";
					ShaderVariant classic;
					backend.SlangPasses = false;
					try
					{
						classic = backend.Shaders.Variant( compiled, mode.Name, [], Combo );
					}
					catch ( InvalidDataException )
					{
						continue;
					}
					finally
					{
						backend.SlangPasses = true;
					}

					if ( classic is null ) continue;

					var fromModule = backend.Shaders.Variant( module, mode.Name, [], Combo );
					if ( fromModule is not { FromModule: true } )
					{
						differences.Add( $"{where}: not drawn from its module" );
						continue;
					}

					// A module drawn with task and mesh stages has no .shader counterpart: its .shader only names it
					if ( fromModule.Mesh is not null ) continue;
					compared++;
					differences.AddRange( ByName( PassModuleTests.Compare( classic, fromModule ) ).Select( d => $"{where}: {d}" ) );
				}
			}
		}

		if ( described == 0 ) Assert.Inconclusive( "No standalone module has a .shader_c beside it" );
		Assert.AreEqual( 0, errors.Count, string.Join( "\n", errors.Take( 5 ) ) );
		Assert.AreEqual( 0, differences.Count, $"{differences.Count} differences over {described} shaders, {compared} variants:\n" + string.Join( "\n", differences.Take( 60 ) ) );
		TestContext.WriteLine( $"{described} shaders described, {compared} variants compared" );
	}

	/// <summary>
	/// How a module's description differs from a compiled file's: modes, features, and each stage's combos. A module drawn
	/// with task and mesh stages has no vertex stage, where its <c>.shader</c> has one only to name it.
	/// </summary>
	static IEnumerable<string> Describe( ShaderFile compiled, ShaderFile module, bool meshStages )
	{
		// Mode names are looked up ignoring case (vfx names Depth() DEPTH when it sets S_MODE_DEPTH)
		static string Mode( ShaderMode m ) => $"{m.Name.ToUpperInvariant()}({m.Fallback}{string.Concat( m.Settings.Select( s => $",{s.Combo}={s.Value}" ) )})";
		var modes = string.Join( ' ', compiled.Modes.Select( Mode ) );
		var moduleModes = string.Join( ' ', module.Modes.Select( Mode ) );
		if ( modes != moduleModes ) yield return $"modes {modes} vs {moduleModes}";

		static string Combo( ShaderCombo c ) => $"{c.Name}:{c.Min}..{c.Max}";
		var features = string.Join( ' ', (compiled.Features?.StaticCombos ?? []).Select( Combo ) );
		var moduleFeatures = string.Join( ' ', (module.Features?.StaticCombos ?? []).Select( Combo ) );
		if ( features != moduleFeatures ) yield return $"features {features} vs {moduleFeatures}";

		foreach ( var type in new[] { ShaderProgramType.Vertex, ShaderProgramType.Pixel, ShaderProgramType.Compute } )
		{
			var a = compiled[type];
			var b = module[type];
			if ( meshStages && type == ShaderProgramType.Vertex && b is null ) continue;
			if ( (a is null) != (b is null) )
			{
				yield return $"{type} program {(a is null ? "only in the module" : "missing from the module")}";
				continue;
			}

			if ( a is null ) continue;

			// Static combos: the range, and what sets them (a feature by name, the mode, or the system)
			static string Static( ShaderFile shader, ShaderCombo c ) => $"{Combo( c )}:{(c.FeatureIndex >= 0 ? shader.Features.StaticCombos[c.FeatureIndex].Name : c.Source == 0 ? "mode" : "system")}";
			var statics = string.Join( ' ', a.StaticCombos.Select( c => Static( compiled, c ) ).Order( StringComparer.Ordinal ) );
			var moduleStatics = string.Join( ' ', b.StaticCombos.Select( c => Static( module, c ) ).Order( StringComparer.Ordinal ) );
			if ( statics != moduleStatics ) yield return $"{type} static combos {statics} vs {moduleStatics}";

			var dynamics = string.Join( ' ', a.DynamicCombos.Select( Combo ).Order( StringComparer.Ordinal ) );
			var moduleDynamics = string.Join( ' ', b.DynamicCombos.Select( Combo ).Order( StringComparer.Ordinal ) );
			if ( dynamics != moduleDynamics ) yield return $"{type} dynamic combos {dynamics} vs {moduleDynamics}";
		}
	}

	/// <summary>
	/// A shader its module describes has no compiled variables, so the names values set by token (<c>StringToken.Literal</c>)
	/// are turned back into are the module's (<see cref="PassModules.BindingNames"/>): every name depth_only's <c>.shader_c</c>
	/// variables and combos give, less a .vmat's input textures.
	/// </summary>
	[TestMethod]
	public void ModuleNamesCoverTheCompiledNames()
	{
		using var backend = new BackendDevice( Device );
		var module = backend.Shaders.Load( DepthOnly );
		var compiledPath = backend.Content.Find( DepthOnly ) ?? throw new AssertInconclusiveException( "depth_only.shader isn't compiled" );
		var compiled = ShaderFile.Load( compiledPath );
		var names = backend.Passes.BindingNames( module );
		Assert.IsTrue( names.Contains( "D_OPAQUE_FADE" ) );
		var missing = compiled.Programs.Where( p => p is not null )
			.SelectMany( p => p.Variables.Where( v => v.Register != RegisterType.InputTexture ).SelectMany( v => new[] { v.Name, v.Attribute } ).Concat( p.DynamicCombos.Select( c => c.Name ) ) )
			.Where( n => !string.IsNullOrEmpty( n ) && n != "DefaultSamplerState" && !names.Contains( n ) ).Distinct().ToList();
		Assert.AreEqual( 0, missing.Count, string.Join( ' ', missing ) );
	}

	/// <summary>
	/// A mode that names no combo sets its own where the module has one (<c>Mode( Depth );</c> sets <c>S_MODE_DEPTH</c>), as vfx
	/// does; a mode's combo no mode sets is left out of the description, as vfx leaves it out.
	/// </summary>
	[TestMethod]
	public void DepthModeSetsItsCombo()
	{
		var folder = Path.Combine( Path.GetTempPath(), "standalone-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( folder );
		try
		{
			File.WriteAllText( Path.Combine( folder, "x.slang" ), "#include \"sbox/pass.slangh\"\nMode( Forward );\nMode( Depth );\nStaticCombo( S_MODE_DEPTH, 0..1, Sys( ALL ) );\nStaticCombo( S_MODE_TOOLS_WIREFRAME, 0..1, Sys( ALL ) );\n"
				+ "#if ( PROGRAM == VFX_PROGRAM_PS )\n[shader( \"pixel\" )] float4 MainPs() : SV_Target0 { return 0; }\n#endif\n" );
			using var backend = new BackendDevice( Device );
			var shader = backend.Passes.Describe( Path.Combine( folder, "x.shader_c" ) );
			Assert.AreEqual( ("S_MODE_DEPTH", 1), shader.Mode( "Depth" ).Settings.Single() );
			Assert.AreEqual( 0, shader.Mode( "Forward" ).Settings.Length );
			CollectionAssert.AreEqual( new[] { "S_MODE_DEPTH" }, shader[ShaderProgramType.Pixel].StaticCombos.Select( c => c.Name ).ToArray(), "a mode's combo no mode sets is left out" );
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}

	/// <summary>
	/// <see cref="PassModuleTests.Compare"/>'s differences less where things are numbered: the module includes less than the
	/// <c>.shader</c> (no vr_lighting.fxc), so its <c>$Globals</c> members sit at other offsets and Slang numbers its textures
	/// and the GPU-driven tables' buffer differently. Each binds by name and each stage's own offsets fill its constants, so
	/// a binding missing at one number and present at another under the same name, kind and type is the same binding.
	/// </summary>
	static IEnumerable<string> ByName( IEnumerable<string> differences )
	{
		var list = differences.Where( d => !System.Text.RegularExpressions.Regex.IsMatch( d, @"constant \w+ at \d+ vs \d+$" ) ).ToList();
		static string Named( string d ) => System.Text.RegularExpressions.Regex.Replace( d, @"^binding \d+\.\d+ (.*) (missing|only in the module)$", "$1" );
		var missing = list.Where( d => d.EndsWith( " missing", StringComparison.Ordinal ) && d.StartsWith( "binding ", StringComparison.Ordinal ) ).Select( Named ).ToHashSet();
		var only = list.Where( d => d.EndsWith( " only in the module", StringComparison.Ordinal ) ).Select( Named ).ToHashSet();
		return list.Where( d => !(d.StartsWith( "binding ", StringComparison.Ordinal ) && missing.Contains( Named( d ) ) && only.Contains( Named( d ) )) );
	}
}
