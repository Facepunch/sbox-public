using Borealis.Backend;
using Borealis.Source2;
using Borealis.Source2.Shaders;
using Sandbox.ShaderCompiler;
using System.IO;

namespace BorealisGpuTests;

/// <summary>
/// Standalone modules (docs/managed/standalone-modules.md): a module that declares its modes describes its shader itself, so
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

				// GPU-driven: only where the vfx variant pulls positions alone (the opaque and fading forms)
				var gpuDriven = backend.Shaders.Variant( module, "Depth", moduleFeatures, Combo, null, gpuDriven: true );
				if ( featureName is not null || combos.GetValueOrDefault( "D_CS_VERTEX_ANIMATION" ) != 0 )
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
