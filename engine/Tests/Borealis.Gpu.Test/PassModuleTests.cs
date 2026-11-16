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

	[TestMethod]
	public void CombosFollowTheirRules()
	{
		const string text = "#include \"sbox/pass.slangh\"\n#if ( PROGRAM == VFX_PROGRAM_PS )\nStaticCombo( S_TOOLS, 0..1, Sys( ALL ) );\nDynamicCombo( D_FOG, 0..2, Sys( ALL ) );\nDynamicCombo( D_ANGLE, 0..1, Sys( ALL ) );\nDynamicCombo( D_QUAD, 0..1, Sys( ALL ) );\nDynamicComboRule( Requires1( D_ANGLE, D_FOG == 2 ) );\nDynamicComboRule( Allow1( S_TOOLS, D_FOG ) );\nDynamicComboRule( Requires1( D_QUAD, S_TOOLS ) );\n[shader( \"pixel\" )] float4 MainPs() : SV_Target0 { return 0; }\n#endif\n";
		var module = PassModule.FromText( "x.slang", text );
		bool Allows( int tools, int fog, int angle, int quad ) => module.Allows( StageKind.Pixel, new Dictionary<string, int> { ["S_TOOLS"] = tools, ["D_FOG"] = fog, ["D_ANGLE"] = angle, ["D_QUAD"] = quad } );
		Assert.IsTrue( Allows( 0, 2, 1, 0 ) );
		Assert.IsFalse( Allows( 0, 1, 1, 0 ) );
		Assert.IsFalse( Allows( 1, 1, 0, 0 ) );
		Assert.IsFalse( Allows( 0, 0, 0, 1 ) );
		Assert.IsTrue( Allows( 1, 0, 0, 1 ) );
		Assert.AreEqual( 3, module.Combos( StageKind.Pixel ).Count( c => !c.Static ) );
	}

	[TestMethod]
	public void ExpansionFollowsTheCombos()
	{
		const string text = "#include \"sbox/pass.slangh\"\n#if ( PROGRAM == VFX_PROGRAM_VS )\nDynamicCombo( D_THICK, 0..1, Sys( ALL ) );\n#if ( D_THICK )\nExpandPrimitives( LINES, 4, TRIANGLE_STRIP );\n#endif\n[shader( \"vertex\" )] float4 MainVs() : SV_Position { return 0; }\n#endif\n";
		var module = PassModule.FromText( "x.slang", text );
		Assert.IsNull( module.Expansion( new Dictionary<string, int> { ["D_THICK"] = 0 } ) );
		var (from, vertices, topology) = module.Expansion( new Dictionary<string, int> { ["D_THICK"] = 1 } ).Value;
		var expansion = PrimitiveExpansion.Parse( from, vertices, topology );
		Assert.AreEqual( new PrimitiveExpansion( ExpandedPrimitive.Lines, 4, PrimitiveTopology.TriangleStrip ), expansion );

		// A line list steps two vertices a line, a strip one; a line's second end is read at semantic indices 4 up
		Assert.AreEqual( 3, expansion.Primitives( PrimitiveTopology.LineList, 6 ) );
		Assert.AreEqual( 5, expansion.Primitives( PrimitiveTopology.LineStrip, 6 ) );
		Assert.AreEqual( -1, expansion.Primitives( PrimitiveTopology.TriangleList, 6 ) );
		Assert.AreEqual( 2, expansion.Step( PrimitiveTopology.LineList ) );
		Assert.AreEqual( 1, expansion.Step( PrimitiveTopology.LineStrip ) );
		var second = new VertexInput( 3, VertexSemantic.Color, 4, SpirvScalar.Float, 4 );
		Assert.AreEqual( 1, expansion.Corner( second ) );
		Assert.AreEqual( 0, expansion.SemanticIndex( second ) );
		Assert.AreEqual( 0, expansion.Corner( new VertexInput( 4, VertexSemantic.TexCoord, 13, SpirvScalar.UInt, 1 ) ) );
	}

	/// <summary>The engine's <c>Vertex</c>, 76 bytes, as <see cref="VertexStreamLayout.EngineVertex"/> reads it.</summary>
	[System.Runtime.InteropServices.StructLayout( System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4 )]
	struct EngineVertex
	{
		public System.Numerics.Vector3 Position;
		public uint Color;
		public System.Numerics.Vector3 Normal;
		public System.Numerics.Vector4 TexCoord0, TexCoord1, Tangent;
	}

	/// <summary>
	/// gizmo_line and gizmo_sprite drawn from their modules, whose vertex stages make each line's and point's quad where the
	/// .shader's geometry stages did: a line list 5 pixels thick, a strip of the same, and points 8 pixels square.
	/// </summary>
	[TestMethod]
	public void ExpandedPrimitivesDraw()
	{
		const int Size = 64;
		using var backend = new BackendDevice( Device );
		var errors = new List<string>();
		backend.Passes.Error = e => errors.Add( e );
		using var drawer = new ShaderDrawer( backend );
		var line = backend.Shaders.Load( "shaders/gizmo/gizmo_line.shader" );
		var sprite = backend.Shaders.Load( "shaders/gizmo/gizmo_sprite.shader" );
		if ( line is null || sprite is null ) Assert.Inconclusive( "gizmo_line or gizmo_sprite isn't compiled" );

		// World space is clip space: x and y -1 to 1 across the target, depth 0.5
		var view = new Borealis.Gpu.ViewConstants { InvViewportSize = new Vector2( 1.0f / Size, 1.0f / Size ), ViewportSize = new Vector2( Size, Size ), CameraDirWs = new Vector3( 0, 0, 1 ), CameraUpDirWs = new Vector3( 0, 1, 0 ) };
		System.Runtime.CompilerServices.Unsafe.As<Matrix, System.Numerics.Matrix4x4>( ref view.WorldToProjection ) = System.Numerics.Matrix4x4.Identity;
		var viewBytes = new byte[Borealis.Gpu.ViewConstants.NativeSize];
		System.Runtime.InteropServices.MemoryMarshal.Write( viewBytes, in view );

		static EngineVertex V( float x, float y, System.Numerics.Vector4 uv = default ) => new() { Position = new( x, y, 0.5f ), Color = 0xFFFFFFFF, TexCoord0 = uv };
		EngineVertex[] lines = [V( -0.5f, -0.5f ), V( 0.5f, -0.5f )];
		EngineVertex[] strip = [V( -0.5f, 0.25f ), V( 0, 0.25f ), V( 0.5f, 0.25f )];
		EngineVertex[] points = [V( 0.5f, 0.75f, new( 8, 8, 0, 0 ) )];

		var vertices = Device.CreateBuffer( new BufferInfo { Size = 76 * 8, Usage = BufferUsage.Vertex } );
		Device.WriteBuffer<EngineVertex>( vertices, [.. lines, .. strip, .. points] );

		var white = Device.CreateTexture( TextureInfo.Texture2D( 1, 1, Format.RGBA8Unorm, name: "White" ) );
		var attributes = new ShaderAttributes();
		attributes.SetData( "PerViewConstantBuffer_t", viewBytes );
		attributes.Set( "LineThickness", 5.0f );
		attributes.Set( "TextureColor", white );

		// The lines' object transform: the identity, alpha 1
		var transform = Device.CreateBuffer( new BufferInfo { Size = 64, Usage = BufferUsage.Storage } );
		Device.WriteBuffer<float>( transform, [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0] );
		attributes.Set( "g_TransformBuffer", transform );

		var color = Device.CreateTexture( TextureInfo.RenderTarget( Size, Size, Format.RGBA8Unorm, name: "Expanded" ) );
		var list = Device.BeginCommandList( QueueType.Graphics, "Expanded" );
		list.Barrier( TextureBarrier.Transition( white, ImageLayout.Undefined, ImageLayout.CopyDestination, PipelineStage.None, MemoryAccess.None, PipelineStage.Transfer, MemoryAccess.TransferWrite ) );
		list.UpdateTexture( white, (ReadOnlySpan<byte>)[255, 255, 255, 255], 0, 0, 1, 1 );
		list.Barrier( TextureBarrier.Transition( white, ImageLayout.CopyDestination, ImageLayout.ShaderRead, PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.FragmentShader, MemoryAccess.ShaderRead ) );
		list.Barrier( TextureBarrier.Transition( color, ImageLayout.Undefined, ImageLayout.ColorTarget, PipelineStage.None, MemoryAccess.None, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite ) );
		list.BeginRendering( [ColorTarget.Of( color, System.Numerics.Vector4.Zero )] );
		var viewport = new System.Numerics.Vector4( 0, 0, Size, Size );
		Assert.IsTrue( drawer.Draw( list, line, attributes, Format.RGBA8Unorm, 1, vertices, 0, VertexStreamLayout.EngineVertex, 2, PrimitiveTopology.LineList, viewport ) );
		Assert.IsTrue( drawer.Draw( list, line, attributes, Format.RGBA8Unorm, 1, vertices, 76 * 2, VertexStreamLayout.EngineVertex, 3, PrimitiveTopology.LineStrip, viewport ) );
		Assert.IsTrue( drawer.Draw( list, sprite, attributes, Format.RGBA8Unorm, 1, vertices, 76 * 5, VertexStreamLayout.EngineVertex, 1, PrimitiveTopology.PointList, viewport ) );
		list.EndRendering();
		Device.Wait( Device.Submit( list ) );

		var pixels = ReadBack( color, ImageLayout.ColorTarget );
		Save( pixels, Size, Size, "expanded-primitives" );
		Assert.AreEqual( 0, errors.Count, string.Join( "\n", errors ) );

		int Columns( int y, int from, int to ) => Enumerable.Range( from, to - from ).Count( x => Pixel( pixels, Size, x, y ).A > 0 );

		// The list's line and the strip's two segments, five pixels thick each, wherever the rows fall on the screen
		var lineRows = Enumerable.Range( 0, Size ).Where( y => Pixel( pixels, Size, 24, y ).A > 0 ).ToList();
		var segmentRows = Enumerable.Range( 0, Size ).Where( y => Pixel( pixels, Size, 40, y ).A > 0 ).ToList();
		TestContext.WriteLine( $"rows at x 24: {string.Join( ',', lineRows )}; at x 40: {string.Join( ',', segmentRows )}" );
		Assert.AreEqual( 10, lineRows.Count, "two lines, five pixels thick each, cross x 24" );
		Assert.AreEqual( 10, segmentRows.Count, "the list's line and the strip's second segment cross x 40" );
		Assert.AreEqual( 0, Pixel( pixels, Size, 8, 48 ).A, "nothing past the lines' ends" );

		// The point's quad: 8 by 8 pixels round (48, 8 or 56)
		var spriteRows = Enumerable.Range( 0, Size ).Where( y => Pixel( pixels, Size, 48, y ).A > 0 && !lineRows.Contains( y ) ).ToList();
		Assert.AreEqual( 8, spriteRows.Count, "a point eight pixels tall" );
		Assert.AreEqual( 8, Columns( spriteRows[0], 40, Size ), "a point eight pixels wide" );
	}

	/// <summary>
	/// A material's compiled values reach the constants of draws of its shader (<see cref="ShaderDrawer.Material"/>), as
	/// native binds a material's mode: gizmo_line's <c>g_DepthBiasAmount</c>, a material variable, is the material's, and the
	/// shader's default without one.
	/// </summary>
	[TestMethod]
	public void MaterialValuesReachConstants()
	{
		using var backend = new BackendDevice( Device );
		using var drawer = new ShaderDrawer( backend );
		var line = backend.Shaders.Load( "shaders/gizmo/gizmo_line.shader" );
		if ( line is null ) Assert.Inconclusive( "gizmo_line isn't compiled" );

		var attributes = new ShaderAttributes();
		var stage = drawer.Variant( line, attributes ).Vertex;
		var (_, offset) = stage.MaterialConstants.First( c => c.Variable.Name == "g_DepthBiasAmount" );
		var data = new byte[stage.MaterialBuffer.Size];

		drawer.WriteConstants( data, stage, attributes, default );
		Assert.AreEqual( 0f, BitConverter.ToSingle( data, offset ), "the shader's default without a material" );

		drawer.Material = new BackendMaterial( "test", line, null, null, new Dictionary<string, float[]>( StringComparer.OrdinalIgnoreCase ) { ["g_DepthBiasAmount"] = [0.25f] } );
		drawer.WriteConstants( data, stage, attributes, default );
		Assert.AreEqual( 0.25f, BitConverter.ToSingle( data, offset ), "the material's value" );

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

			// A module stage whose code reads no constants has no $Globals (Slang drops what's never read; vfx keeps a
			// combo's declared constants, as tools_sprite's border colour in its depth pass): nothing to bind, nothing to differ
			if ( a.MaterialBuffer.Set >= 0 && b.MaterialBuffer.Set < 0 ) continue;

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
				if ( r.Kind == ShaderResourceKind.MaterialConstants && !module.Resources.Any( x => x.Kind == ShaderResourceKind.MaterialConstants && x.Stages == r.Stages ) ) continue;
				yield return $"binding {r} missing";
				continue;
			}

			if ( r.Type != m.Type || r.Kind != m.Kind || r.Dimension != m.Dimension || r.Count != m.Count || r.Stages != m.Stages ) yield return $"binding {r} ({r.Dimension}, {r.Stages}) vs {m} ({m.Dimension}, {m.Stages})";
			else if ( r.Kind != ShaderResourceKind.MaterialConstants && r.Kind != ShaderResourceKind.Unknown && !string.Equals( r.Name, m.Name, StringComparison.OrdinalIgnoreCase ) ) yield return $"binding {r} named {m.Name}";
			else if ( r.Kind == ShaderResourceKind.Texture && r.Variable.RenderStateId == 1 != (m.Variable.RenderStateId == 1) ) yield return $"binding {r} sRGB {r.Variable.RenderStateId == 1} vs {m.Variable.RenderStateId == 1}";
		}

		// A vertex stage that expands primitives does the .shader's geometry stage's work, so it reads what that stage read
		// (the view constants), which the .shader's vertex stage didn't
		foreach ( var (at, m) in moduleResources )
		{
			if ( !classicResources.ContainsKey( at ) && !(module.Expansion.Expands && m.Stages == ShaderStages.Vertex) ) yield return $"binding {m} only in the module";
		}

		// Vertex inputs. A stage that expands primitives (in place of a geometry stage) reads its primitive's first vertex
		// as the .shader's stage reads its vertex, and the others besides, at their own locations
		var expansion = module.Expansion;
		static string Input( VertexInput i ) => $"{i.Semantic}{i.SemanticIndex}:{i.Kind}x{i.Components}";
		var inputs = expansion.Expands ? string.Join( ' ', classic.VertexInputs.Select( Input ).Order( StringComparer.Ordinal ) ) : string.Join( ' ', classic.VertexInputs );
		var moduleInputs = expansion.Expands
			? string.Join( ' ', module.VertexInputs.Where( i => expansion.Corner( i ) == 0 ).Select( Input ).Order( StringComparer.Ordinal ) )
			: string.Join( ' ', module.VertexInputs );
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
