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

		// Lines fade over a pixel at their edges: what they cover down a column adds up to their thickness
		float Covered( int x ) => Enumerable.Range( 0, Size ).Sum( y => Pixel( pixels, Size, x, y ).R / 255f );
		Assert.AreEqual( 10, Covered( 24 ), 0.25f, "two lines, five pixels thick each, cross x 24" );
		Assert.AreEqual( 10, Covered( 40 ), 0.25f, "the list's line and the strip's second segment cross x 40" );
		Assert.IsTrue( Enumerable.Range( 0, Size ).Any( y => Pixel( pixels, Size, 24, y ).R is > 0 and < 255 ), "edges partly covered: antialiased without multisampling" );
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

	/// <summary>
	/// An expanding module's draw with lent instance ids (a dynamic object's, inside a frame) reads its object's transform from
	/// the first id for every primitive, as native binds one transform slot for the object: a line moved half the screen up.
	/// </summary>
	[TestMethod]
	public void ExpandedPrimitivesReadTheLentTransform()
	{
		const int Size = 64;
		using var backend = new BackendDevice( Device );
		var errors = new List<string>();
		backend.Passes.Error = e => errors.Add( e );
		using var drawer = new ShaderDrawer( backend );
		var line = backend.Shaders.Load( "shaders/gizmo/gizmo_line.shader" );
		if ( line is null ) Assert.Inconclusive( "gizmo_line isn't compiled" );

		// World space is clip space: x and y -1 to 1 across the target, depth 0.5
		var view = new Borealis.Gpu.ViewConstants { InvViewportSize = new Vector2( 1.0f / Size, 1.0f / Size ), ViewportSize = new Vector2( Size, Size ), CameraDirWs = new Vector3( 0, 0, 1 ), CameraUpDirWs = new Vector3( 0, 1, 0 ) };
		System.Runtime.CompilerServices.Unsafe.As<Matrix, System.Numerics.Matrix4x4>( ref view.WorldToProjection ) = System.Numerics.Matrix4x4.Identity;
		var viewBytes = new byte[Borealis.Gpu.ViewConstants.NativeSize];
		System.Runtime.InteropServices.MemoryMarshal.Write( viewBytes, in view );

		static EngineVertex V( float x, float y ) => new() { Position = new( x, y, 0.5f ), Color = 0xFFFFFFFF };
		// Room for a whole step past the last line, as the engine's uploads leave (a device may check bounds by the step)
		var vertices = Device.CreateBuffer( new BufferInfo { Size = 76 * 6, Usage = BufferUsage.Vertex } );
		Device.WriteBuffer<EngineVertex>( vertices, [V( -0.5f, -0.5f ), V( 0.5f, -0.5f ), V( -0.5f, -0.75f ), V( 0.5f, -0.75f )] );

		// Two transforms, the identity and one moving up by 0.4 (a fifth of the screen); the lent ids start at the second
		var transforms = Device.CreateBuffer( new BufferInfo { Size = 128, Usage = BufferUsage.Storage } );
		Device.WriteBuffer<float>( transforms, [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0.4f, 0, 0, 1, 0, 1, 0, 0, 0] );
		var ids = Device.CreateBuffer( new BufferInfo { Size = 8, Usage = BufferUsage.Vertex } );
		Device.WriteBuffer<uint>( ids, [0, 1] );
		drawer.InstanceIds = (ids, sizeof( uint ));

		var attributes = new ShaderAttributes();
		attributes.SetData( "PerViewConstantBuffer_t", viewBytes );
		attributes.Set( "LineThickness", 3.0f );
		attributes.Set( "g_TransformBuffer", transforms );

		var color = Device.CreateTexture( TextureInfo.RenderTarget( Size, Size, Format.RGBA8Unorm, name: "Expanded with ids" ) );
		var list = Device.BeginCommandList( QueueType.Graphics, "Expanded with ids" );
		list.Barrier( TextureBarrier.Transition( color, ImageLayout.Undefined, ImageLayout.ColorTarget, PipelineStage.None, MemoryAccess.None, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite ) );
		list.BeginRendering( [ColorTarget.Of( color, System.Numerics.Vector4.Zero )] );
		Assert.IsTrue( drawer.Draw( list, line, attributes, Format.RGBA8Unorm, 1, vertices, 0, VertexStreamLayout.EngineVertex, 4, PrimitiveTopology.LineList, new System.Numerics.Vector4( 0, 0, Size, Size ) ) );
		list.EndRendering();
		Device.Wait( Device.Submit( list ) );

		var pixels = ReadBack( color, ImageLayout.ColorTarget );
		Save( pixels, Size, Size, "expanded-lent-transform" );
		Assert.AreEqual( 0, errors.Count, string.Join( "\n", errors ) );

		// Both lines moved by the second transform, to y -0.1 and -0.35: no row where the identity puts them (y -0.5 and -0.75,
		// rows 16 and 8 from one edge or the other)
		var rows = Enumerable.Range( 0, Size ).Where( y => Pixel( pixels, Size, 32, y ).A > 0 ).ToList();
		TestContext.WriteLine( $"rows at x 32: {string.Join( ',', rows )}" );
		Assert.AreEqual( 6, rows.Sum( y => Pixel( pixels, Size, 32, y ).R / 255f ), 0.25f, "two lines, three pixels thick each" );
		var identityRows = Enumerable.Range( 0, Size ).Where( y => new[] { 8, 16, Size - 16, Size - 8 }.Any( r => Math.Abs( y + 0.5f - r ) < 3 ) ).ToList();
		Assert.IsFalse( rows.Any( identityRows.Contains ), $"no line where the identity would put it ({string.Join( ',', identityRows )})" );
	}

	/// <summary>
	/// ObjectHighlight's outline drawn from a model's vertices laid out one triangle after another (what Borealis.Gpu draws a
	/// highlighted model with): each corner of the expanded triangle reads its own place in the vertex cache (the skinned
	/// citizen's path, <c>TEXCOORD15</c> held again as <c>TEXCOORD3</c>) and its own bone, not the first corner's. With no
	/// stencil the outline is a band two pixels round each of the triangle's edges.
	/// </summary>
	[TestMethod]
	public void HighlightOutlineCornersReadTheirOwnVertices()
	{
		const int Size = 64;
		using var backend = new BackendDevice( Device );
		var errors = new List<string>();
		backend.Passes.Error = e => errors.Add( e );
		using var drawer = new ShaderDrawer( backend );
		var highlight = backend.Shaders.Load( "shaders/postprocess/ObjectHighlight/ObjectHighlight.shader" );
		if ( highlight is null ) Assert.Inconclusive( "ObjectHighlight isn't compiled" );

		// World space is clip space: x and y -1 to 1 across the target, depth 0.5
		var view = new Borealis.Gpu.ViewConstants { InvViewportSize = new Vector2( 1.0f / Size, 1.0f / Size ), ViewportSize = new Vector2( Size, Size ), CameraDirWs = new Vector3( 0, 0, 1 ), CameraUpDirWs = new Vector3( 0, 1, 0 ) };
		System.Runtime.CompilerServices.Unsafe.As<Matrix, System.Numerics.Matrix4x4>( ref view.WorldToProjection ) = System.Numerics.Matrix4x4.Identity;
		var viewBytes = new byte[Borealis.Gpu.ViewConstants.NativeSize];
		System.Runtime.InteropServices.MemoryMarshal.Write( viewBytes, in view );

		// A triangle in the target's right half: (0.2, -0.6), (0.8, -0.6), (0.5, 0.6)
		float[][] corners = [[0.2f, -0.6f], [0.8f, -0.6f], [0.5f, 0.6f]];

		// The vertex cache holds the triangle; the vertices' own positions are mirrored into the left half, as a skinned
		// mesh's bind pose is elsewhere
		var first = backend.AllocateVertexCache( 3 );
		var cached = new byte[3 * BackendDevice.VertexCacheStride];
		for ( int i = 0; i < 3; i++ )
		{
			BitConverter.TryWriteBytes( cached.AsSpan( i * BackendDevice.VertexCacheStride ), corners[i][0] );
			BitConverter.TryWriteBytes( cached.AsSpan( i * BackendDevice.VertexCacheStride + 4 ), corners[i][1] );
			BitConverter.TryWriteBytes( cached.AsSpan( i * BackendDevice.VertexCacheStride + 8 ), 0.5f );
		}
		Device.WriteBuffer( backend.VertexCacheBuffer, cached, (long)first * BackendDevice.VertexCacheStride );

		var cacheLayout = new VertexStreamLayout( 16,
			new( VertexSemantic.Position, 0, VertexFormat.Float3, 0 ),
			new( VertexSemantic.TexCoord, 15, VertexFormat.Float, 12 ),
			new( VertexSemantic.TexCoord, 3, VertexFormat.Float, 12 ) );
		var cacheVertices = new float[6 * 4];
		for ( int i = 0; i < 3; i++ ) (cacheVertices[i * 4], cacheVertices[i * 4 + 1], cacheVertices[i * 4 + 2], cacheVertices[i * 4 + 3]) = (-corners[i][0], corners[i][1], 0.5f, i);

		// Rigid skinning: every vertex on bone 1, which moves the left half's triangle right by 1; bone 0 leaves it there
		var boneLayout = new VertexStreamLayout( 16,
			new( VertexSemantic.Position, 0, VertexFormat.Float3, 0 ),
			new( VertexSemantic.BlendIndices, 0, VertexFormat.UByte4, 12 ) );
		var boneVertices = new float[6 * 4];
		for ( int i = 0; i < 3; i++ ) (boneVertices[i * 4], boneVertices[i * 4 + 1], boneVertices[i * 4 + 2], boneVertices[i * 4 + 3]) = (corners[i][0] - 1, corners[i][1], 0.5f, BitConverter.Int32BitsToSingle( 1 ));

		// Slot 0 reads the cache from its first vertex; slot 1 is a skeleton (it and one more before its bones), bone 1 moving x by 1
		float[] identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0];
		float[] right = [1, 0, 0, 1, 0, 1, 0, 0, 0, 0, 1, 0];
		var entries = new byte[6 * 64];
		void Entry( int slot, float[] rows, uint cacheOffset, uint blendWeights )
		{
			Buffer.BlockCopy( rows, 0, entries, slot * 64, 48 );
			BitConverter.TryWriteBytes( entries.AsSpan( slot * 64 + 48 ), 1.0f );
			BitConverter.TryWriteBytes( entries.AsSpan( slot * 64 + 52 ), 0xFFFFFFu );
			BitConverter.TryWriteBytes( entries.AsSpan( slot * 64 + 56 ), cacheOffset );
			BitConverter.TryWriteBytes( entries.AsSpan( slot * 64 + 60 ), blendWeights );
		}
		Entry( 0, identity, (uint)first, 0 );
		Entry( 1, identity, 0, 1 );
		Entry( 2, identity, 0, 0 );
		Entry( 3, identity, 0, 0 );
		Entry( 4, right, 0, 0 );
		Entry( 5, identity, 0, 0 );
		var transforms = Device.CreateBuffer( new BufferInfo { Size = entries.Length, Usage = BufferUsage.Storage } );
		Device.WriteBuffer( transforms, entries );
		var ids = Device.CreateBuffer( new BufferInfo { Size = 8, Usage = BufferUsage.Vertex } );
		Device.WriteBuffer<uint>( ids, [0, 1] );

		byte[] Draw( float[] vertexData, VertexStreamLayout layout, int cache, int slot, string name )
		{
			var vertices = Device.CreateBuffer( new BufferInfo { Size = vertexData.Length * 4, Usage = BufferUsage.Vertex } );
			Device.WriteBuffer<float>( vertices, vertexData );
			drawer.InstanceIds = (ids, slot * sizeof( uint ));

			var attributes = new ShaderAttributes();
			attributes.SetData( "PerViewConstantBuffer_t", viewBytes );
			attributes.Set( "g_TransformBuffer", transforms );
			attributes.SetCombo( "D_OUTLINE_PASS", 1 );
			attributes.SetCombo( "D_CS_VERTEX_ANIMATION", cache );
			attributes.Set( "Color", 1, 1, 1, 1 );
			attributes.Set( "ObscuredColor", 1, 1, 1, 1 );
			// Two pixels: LineWidth / 64 of the normalized inverse viewport size, in projection space
			attributes.Set( "LineWidth", 2.0f * 64 * MathF.Sqrt( 2 ) / Size );

			var color = Device.CreateTexture( TextureInfo.RenderTarget( Size, Size, Format.RGBA8Unorm, name: name ) );
			var list = Device.BeginCommandList( QueueType.Graphics, name );
			list.Barrier( TextureBarrier.Transition( color, ImageLayout.Undefined, ImageLayout.ColorTarget, PipelineStage.None, MemoryAccess.None, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite ) );
			list.BeginRendering( [ColorTarget.Of( color, System.Numerics.Vector4.Zero )] );
			Assert.IsTrue( drawer.Draw( list, highlight, attributes, Format.RGBA8Unorm, 1, vertices, 0, layout, 3, PrimitiveTopology.TriangleList, new System.Numerics.Vector4( 0, 0, Size, Size ) ) );
			list.EndRendering();
			Device.Wait( Device.Submit( list ) );

			var pixels = ReadBack( color, ImageLayout.ColorTarget );
			Save( pixels, Size, Size, name );
			return pixels;
		}

		foreach ( var (pixels, what) in new[] { (Draw( cacheVertices, cacheLayout, 1, 0, "highlight-vertex-cache" ), "from the vertex cache"), (Draw( boneVertices, boneLayout, 0, 1, "highlight-bones" ), "on bone 1") } )
		{
			var left = 0;
			var rightHalf = 0;
			for ( int y = 0; y < Size; y++ )
			{
				for ( int x = 0; x < Size; x++ )
				{
					if ( Pixel( pixels, Size, x, y ).A == 0 ) continue;
					if ( x < Size / 2 ) left++;
					else rightHalf++;
				}
			}

			// The triangle's edges, about 19 + 2 * 40 pixels long, with a band of 2.5 pixels each side round them
			TestContext.WriteLine( $"{what}: {rightHalf} pixels in the right half, {left} in the left" );
			Assert.AreEqual( 0, left, $"every corner {what}: nothing in the left half" );
			Assert.IsTrue( rightHalf > 300, $"round the whole triangle {what} ({rightHalf} pixels)" );
			Assert.AreEqual( 0, Pixel( pixels, Size, 48, 32 ).A, $"a band round its edges, not the triangle filled {what}" );
		}

		Assert.AreEqual( 0, errors.Count, string.Join( "\n", errors ) );
	}

	/// <summary>
	/// Which shaders' draws need a stencil in the depth they draw into (<see cref="ShaderPipelineState.NeedsStencil"/>): not a
	/// standard material's (its test passes on zero and it writes nothing), nor gizmo_line's (it writes zero); tools_solid's
	/// (it writes one) and the highlight's inside pass (it writes one).
	/// </summary>
	[TestMethod]
	public void StencilNeedsFollowTheShaders()
	{
		using var backend = new BackendDevice( Device );
		bool Needs( string path, int outlinePass = 0 )
		{
			var shader = backend.Shaders.Load( path );
			if ( shader is null ) Assert.Inconclusive( $"{path} isn't compiled" );
			var variant = backend.Shaders.Variant( shader, shader.Modes[0].Name, [], name => name == "D_OUTLINE_PASS" ? outlinePass : null );
			Assert.IsNotNull( variant, path );
			return variant.State.NeedsStencil;
		}

		Assert.IsFalse( Needs( "shaders/complex.shader" ), "complex: tests NOT_EQUAL 3 against bit 0, writes nothing" );
		Assert.IsFalse( Needs( "shaders/gizmo/gizmo_line.shader" ), "gizmo_line writes zero" );
		Assert.IsTrue( Needs( "shaders/tools_solid.shader" ), "tools_solid writes one" );
		Assert.IsTrue( Needs( "shaders/postprocess/ObjectHighlight/ObjectHighlight.shader" ), "the highlight's inside pass writes one" );
	}

	/// <summary>
	/// A shader's render state read without making its variant (<see cref="ShaderLibrary.PixelState"/>, which a material's
	/// stencil check reads) is its variant's in every mode, for shaders drawn from their compiled programs (complex) and from
	/// modules (the rest), and reading it compiles and makes nothing.
	/// </summary>
	[TestMethod]
	public void PixelStateMatchesTheVariantWithoutMakingIt()
	{
		string[] paths = ["shaders/complex.shader", "shaders/gizmo/gizmo_line.shader", "shaders/tools_solid.shader", "shaders/glass.shader", "shaders/postprocess/ObjectHighlight/ObjectHighlight.shader"];
		using var backend = new BackendDevice( Device );
		var library = backend.Shaders;
		var modules = (PassModules)library.Modules;
		var (variantsBefore, stagesBefore) = (library.VariantCount, modules.StagesCompiled);

		var read = new List<(string What, ShaderFile Shader, string Mode, ShaderPipelineState State, bool Throws)>();
		foreach ( var path in paths )
		{
			var shader = library.Load( path );
			if ( shader is null ) Assert.Inconclusive( $"{path} isn't compiled" );
			foreach ( var mode in shader.Modes )
			{
				try
				{
					read.Add( ($"{path} {mode.Name}", shader, mode.Name, library.PixelState( shader, mode.Name, [] ), false) );
				}
				catch ( InvalidDataException )
				{
					read.Add( ($"{path} {mode.Name}", shader, mode.Name, null, true) );
				}
			}
		}

		Assert.AreEqual( variantsBefore, library.VariantCount, "no variants made" );
		Assert.AreEqual( stagesBefore, modules.StagesCompiled, "no module stages compiled" );

		foreach ( var (what, shader, mode, state, throws) in read )
		{
			ShaderPipelineState expected;
			try
			{
				expected = library.Variant( shader, mode, [] )?.State;
			}
			catch ( InvalidDataException )
			{
				Assert.IsTrue( throws, $"{what}: the variant throws, so does its state" );
				continue;
			}

			Assert.IsFalse( throws, $"{what}: the state throws, the variant doesn't" );
			Assert.AreEqual( expected is null, state is null, $"{what}: both or neither" );
			if ( expected is null ) continue;
			Assert.AreEqual( expected.Cull, state.Cull, $"{what} cull" );
			Assert.AreEqual( expected.DepthTest, state.DepthTest, $"{what} depth test" );
			Assert.AreEqual( expected.DepthWrite, state.DepthWrite, $"{what} depth write" );
			Assert.AreEqual( expected.DepthCompare, state.DepthCompare, $"{what} depth compare" );
			Assert.AreEqual( expected.StencilTest, state.StencilTest, $"{what} stencil test" );
			Assert.AreEqual( expected.StencilFront, state.StencilFront, $"{what} stencil front" );
			Assert.AreEqual( expected.StencilBack, state.StencilBack, $"{what} stencil back" );
			Assert.AreEqual( expected.StencilReadMask, state.StencilReadMask, $"{what} stencil read mask" );
			Assert.AreEqual( expected.StencilWriteMask, state.StencilWriteMask, $"{what} stencil write mask" );
			Assert.AreEqual( expected.StencilReference, state.StencilReference, $"{what} stencil reference" );
			CollectionAssert.AreEqual( expected.Blend, state.Blend, $"{what} blend" );
			Assert.AreEqual( expected.AlphaToCoverage, state.AlphaToCoverage, $"{what} alpha to coverage" );
			Assert.AreEqual( expected.NeedsStencil, state.NeedsStencil, $"{what} needs a stencil" );
		}
	}

	/// <summary>
	/// Each module's variant in each mode, its dynamic combos at their first values, against its compiled shader's: the same
	/// bindings, constants, inputs and render state.
	/// </summary>
	[TestMethod]
	public void PassModulesMatchTheirShaders() => CompareModules( firstCombos: true );

	/// <summary>
	/// The rest of each module's variants: each value of each dynamic combo with the others at their first, as
	/// <see cref="PassModulesMatchTheirShaders"/> compares them. Several times the compiles, so slow.
	/// </summary>
	[TestMethod]
	[TestCategory( "Slow" )]
	public void PassModuleCombosMatchTheirShaders() => CompareModules( firstCombos: false );

	void CompareModules( bool firstCombos )
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

			// Borealis's own passes have no .shader: nothing to compare with
			if ( shader.ModuleOnly ) continue;

			foreach ( var mode in shader.Modes )
			{
				if ( !string.IsNullOrEmpty( mode.Fallback ) ) continue;
				foreach ( var combos in firstCombos ? ComboSets( shader ).Take( 1 ) : ComboSets( shader ).Skip( 1 ) )
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

					// A module drawn with task and mesh stages has no .shader counterpart: its .shader only names it
					if ( module.Mesh is not null ) continue;

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
	internal static IEnumerable<Dictionary<string, int>> ComboSets( ShaderFile shader )
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

	/// <summary>How a module's variant differs from a compiled shader's: bindings, constants, inputs and render state.</summary>
	internal static IEnumerable<string> Compare( ShaderVariant classic, ShaderVariant module )
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
