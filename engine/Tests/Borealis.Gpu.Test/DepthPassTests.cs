using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Sandbox;

namespace BorealisGpuTests;

/// <summary>
/// Depth passes on Borealis.Gpu keep the pixel shader of an alpha-tested material, and any that discards: its depth is what
/// it clips (alpha test, alpha to coverage, <c>clip</c>), so a prepass without it would hide what's behind the cut-out parts.
/// </summary>
[TestClass]
[DoNotParallelize]
public class DepthPassTests : GpuTest
{
	const int Width = 320, Height = 200;

	/// <summary>
	/// An alpha-tested box in front of a white one, multisampled, draws the same frame with the depth prepass as without it.
	/// </summary>
	[TestMethod]
	public void AlphaTestedDrawsMatchWithAndWithoutThePrepass()
	{
		using var backend = new BackendDevice( Device );
		backend.Surfaces.ConvertShaders = false;   // the .shader route's prepass
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			var cutOut = backend.LoadMaterial( "materials/dev/simple/simple_rail_32.vmat" );
			if ( cutOut is not BackendMaterial { Shader: not null, IsAlphaTest: true } ) Assert.Inconclusive( "No compiled alpha-tested material" );

			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 4, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Depth pass test" );
			var world = new RenderWorld();
			world.Lighting.SunShadowCascades = 0;

			// Flagged alpha-tested as a compiled model's draws are (BackendModel), so Borealis doesn't strip its pixel shader
			var box = ObjectAttributeTests.BoxMesh( backend, cutOut );
			var draw = box.DrawsForLod( 0 )[0] with { AlphaTest = true };
			world.Add( new MeshObject( new RenderMesh( box.Geometry, box.Bounds, [[draw]] ), Transform.Zero ) );
			world.Add( new MeshObject( ObjectAttributeTests.BoxMesh( backend, backend.CreateMaterial( "Behind", Color.White ) ), new Transform( new Vector3( 300, 0, 0 ), Rotation.Identity, 3 ) ) );

			var view = new RenderView { Name = "Depth pass test", FieldOfView = 60, ZNear = 4, ZFar = 4000, ClearColor = Color.Black, Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -200, 0, 0 );
			view.Rotation = Rotation.LookAt( Vector3.Forward );

			byte[] Frame( bool prepass )
			{
				system.DepthPrepass = prepass;
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
				var (texture, layout) = BackendDevice.Unwrap( target.Resolved );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Resolved, ImageLayout.CopySource );
				return pixels;
			}

			var without = Frame( false );
			var with = Frame( true );

			// Pixels (RGBA16F) that differ by more than a little in red
			var different = 0;
			for ( int i = 0; i + 8 <= with.Length; i += 8 )
			{
				if ( MathF.Abs( (float)BitConverter.ToHalf( with, i ) - (float)BitConverter.ToHalf( without, i ) ) > 0.02f ) different++;
			}

			Assert.IsTrue( different < Width * Height / 200, $"{different} pixels differ with the prepass" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// A depth draw keeps a pixel shader that discards even when it was asked to go without (its draw isn't flagged
	/// alpha-tested, as a package shader's that cuts holes with <c>clip</c> isn't): complex's alpha-tested depth variant
	/// discards and keeps it, its plain one doesn't and goes without.
	/// </summary>
	[TestMethod]
	public void DepthDrawsKeepAPixelShaderThatDiscards()
	{
		using var backend = new BackendDevice( Device );
		var shader = backend.Shaders.Load( "shaders/complex.shader" );
		var features = new int[shader.Features.StaticCombos.Length];
		var plain = backend.Shaders.Variant( shader, "Depth", features );
		features[shader.FeatureIndex( "F_ALPHA_TEST" )] = 1;
		var cutOut = backend.Shaders.Variant( shader, "Depth", features );
		Assert.IsFalse( plain.Pixel?.Reflection is { Discards: true }, "complex's plain depth variant doesn't discard" );
		Assert.IsTrue( cutOut.Pixel?.Reflection is { Discards: true }, "its alpha-tested one does" );

		var opaque = (BackendMaterial)backend.CreateMaterial( "Opaque", Color.White );
		Assert.IsFalse( opaque.IsAlphaTest || opaque.IsTranslucent );
		Assert.IsTrue( BackendContext.StripsPixelShader( opaque, plain, noColor: true, stripPixelShader: true ) );
		Assert.IsFalse( BackendContext.StripsPixelShader( opaque, cutOut, noColor: true, stripPixelShader: true ), "asked to strip, it keeps a pixel shader that discards" );
		Assert.IsTrue( BackendContext.StripsPixelShader( opaque, plain, noColor: true, stripPixelShader: false ), "a prepass of an opaque material goes without" );
		Assert.IsFalse( BackendContext.StripsPixelShader( opaque, plain, noColor: false, stripPixelShader: false ), "a colour pass keeps it" );
	}

	/// <summary>
	/// A compiled model's depth-only prepass draws read a positions-only copy of its vertices (a 12-byte layout), and the
	/// frame is the same, bit for bit, as when they read the full vertices: forward tests depth-equal against what they wrote.
	/// </summary>
	[TestMethod]
	public void DepthOnlyDrawsReadPositionsOnly()
	{
		byte[] Frame( bool positions, out bool positionLayout, out long copied )
		{
			using var backend = new BackendDevice( Device ) { PositionStreams = positions, GpuDriven = false };

			// The .shader route's prepass: surfaces read the full vertices
			backend.Surfaces.ConvertShaders = false;
			var previousHost = RenderHost.Current;
			RenderHost.Current = new BackendRenderHost( backend );
			try
			{
				var mesh = backend.LoadModel( "models/citizen_props/trashcan01.vmdl" );
				if ( mesh is null ) Assert.Inconclusive( "models/citizen_props/trashcan01.vmdl isn't built" );

				using var system = new RenderSystem { ParallelRecording = false, DepthPrepass = true };
				using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 4, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Positions-only depth test" );
				var world = new RenderWorld();
				world.Lighting.SunShadowCascades = 0;
				world.Add( new MeshObject( mesh, Transform.Zero ) );

				var view = new RenderView { Name = "Positions-only depth test", FieldOfView = 60, ClearColor = Color.Black, Viewport = new Rect( 0, 0, Width, Height ) };
				var size = mesh.Bounds.Size.Length;
				view.Rotation = Rotation.From( 20, 160, 0 );
				view.Position = mesh.Bounds.Center - view.Rotation.Forward * size * 1.2f;
				view.ZNear = size * 0.05f;
				view.ZFar = size * 10;

				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
				var (texture, layout) = BackendDevice.Unwrap( target.Resolved );
				var pixels = ReadBack( texture, layout );

				positionLayout = backend.PipelineLayouts.Any( l => l is { Stride: 12, Fields: [{ Semantic: Borealis.Source2.Shaders.VertexSemantic.Position }] } );
				copied = backend.PositionStreamBytes;
				return pixels;
			}
			finally
			{
				Device.WaitIdle();
				RenderHost.Current = previousHost;
			}
		}

		var full = Frame( false, out var fullUsedPositions, out var fullCopied );
		var positionsOnly = Frame( true, out var usedPositions, out var copied );

		Assert.IsFalse( fullUsedPositions, "with the copies off, nothing draws from them" );
		Assert.AreEqual( 0, fullCopied );
		Assert.IsTrue( copied > 0, "the model's vertices were copied as positions" );
		Assert.IsTrue( usedPositions, "the prepass drew from the positions-only copy" );
		Assert.IsTrue( full.AsSpan().SequenceEqual( positionsOnly ), "the frames are identical" );
		Assert.IsTrue( full.Any( b => b != 0 ), "the model drew" );
	}
}
