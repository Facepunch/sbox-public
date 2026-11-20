using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Sandbox;

namespace BorealisGpuTests;

/// <summary>
/// The editor's view modes as engine passes (<see cref="ToolsViews"/>): the geometric ones draw every draw with its own vertex
/// stage and the engine's <c>tools_view</c> pixel stage, the same for a <c>.shader</c> material, its Slang surface, and both
/// GPU-driven; and the material ones reach custom objects' draws (<see cref="ShaderDrawer"/>) as they change.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ToolsViewTests : GpuTest
{
	const int Width = 320, Height = 240;

	/// <summary>
	/// Two boxes on the camera's axis, the far one hidden behind the near one. Overdraw counts both where they overlap and one
	/// around it, over black; quad overdraw draws there too; shader IDs fill both flat in one colour; wireframe draws white
	/// lines and leaves the faces' insides the clear colour. Each for complex as its .shader and as its surface, classic and
	/// GPU-driven, with no validation errors.
	/// </summary>
	[TestMethod]
	[DataRow( false, false, DisplayName = ".shader, classic" )]
	[DataRow( true, false, DisplayName = "surface, classic" )]
	[DataRow( false, true, DisplayName = ".shader, GPU-driven" )]
	[DataRow( true, true, DisplayName = "surface, GPU-driven" )]
	public void GeometricViewsDrawEveryDraw( bool surface, bool gpuDriven )
	{
		using var backend = new BackendDevice( Device ) { GpuDriven = gpuDriven, GpuDrivenGeometry = gpuDriven };
		backend.Surfaces.ConvertShaders = surface;
		if ( backend.Shaders.Load( "shaders/tools_view.shader" ) is null ) Assert.Inconclusive( "tools_view.slang isn't built" );
		if ( !surface && backend.Shaders.Load( "shaders/complex.shader" ) is null ) Assert.Inconclusive( "complex.shader is gone (standard's surface draws its materials): nothing for the .shader route" );
		if ( gpuDriven && !surface && backend.Shaders.GpuDriven( backend.Shaders.Load( "shaders/complex.shader" ) ) is null ) Assert.Inconclusive( "complex has no GPU-driven variant: ShaderCompiler -gpudriven" );

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Tools view test" );
			var material = backend.LoadMaterial( "materials/default.vmat" );
			Assert.AreEqual( surface, material is SurfaceMaterial, "the material is the kind asked for" );

			var world = new RenderWorld();
			world.Lighting.SunShadowCascades = 0;
			var box = ObjectAttributeTests.BoxMesh( backend, material );
			world.Add( new MeshObject( box, new Transform( Vector3.Zero ) ) );
			world.Add( new MeshObject( box, new Transform( new Vector3( 300, 0, 0 ) ) ) );

			var view = new RenderView { Name = "Tools view test", FieldOfView = 60, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.25f, 0.5f, 0.75f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -300, 0, 0 );
			view.Rotation = Rotation.LookAt( Vector3.Forward );

			byte[] Frame( int mode, bool wireframe = false, DrawCensus census = null )
			{
				view.ToolsVisMode = mode;
				view.Wireframe = wireframe;
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				backend.Census = census;
				try
				{
					system.Render( world, view, target );
				}
				finally
				{
					backend.Census = null;
				}

				Device.WaitIdle();
				var (texture, layout) = BackendDevice.Unwrap( target.Color );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );
				return pixels;
			}

			// The near box's front face, 250 units from the camera, covers 42 pixels each way from the middle; the far one's, 550
			// away, 19. Off the faces' diagonals, where a triangle edge runs
			var middle = (Width / 2 + 4, Height / 2 - 8);
			var nearOnly = (Width / 2 + 12, Height / 2 + 30);
			var outside = (8, 8);

			var lit = Frame( 0 );
			var census = gpuDriven ? new DrawCensus() : null;
			var overdraw = Frame( ToolsViews.Overdraw, census: census );
			if ( census is not null )
			{
				census.Resolve( Device );
				var forward = census.Draws.Where( d => d.Pass.EndsWith( "Opaque Forward", StringComparison.Ordinal ) ).ToArray();
				Assert.IsTrue( forward.Length > 0 && forward.All( d => d.Path != DrawCensus.Path.Classic ), "the boxes drew GPU-driven" );
			}

			var quads = Frame( ToolsViews.QuadOverdraw );
			var ids = Frame( ToolsViews.ShaderIds );
			var wireframe = Frame( 0, wireframe: true );
			var litAgain = Frame( 0 );

			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
			Assert.IsTrue( Pixel( lit, middle ).R > 0.01f, "the lit box is lit" );

			// Overdraw: one count per surface over a pixel, depth untested, over black
			AreClose( 0.24f, Pixel( overdraw, middle ).R, "both boxes count in the middle" );
			AreClose( 0.12f, Pixel( overdraw, nearOnly ).R, "the near box alone counts once" );
			AreClose( 0f, Pixel( overdraw, outside ).R, "nothing counts outside, and no sky or clear colour" );

			// Quad overdraw: whole quads inside the faces, twice in the middle
			Assert.IsTrue( Pixel( quads, middle ).G > Pixel( quads, nearOnly ).G && Pixel( quads, nearOnly ).G > 0.05f, "quads count per surface" );
			AreClose( 0f, Pixel( quads, outside ).G, "no quads outside" );

			// Shader IDs: the same flat colour across both boxes, not the lit one
			var colour = Pixel( ids, middle );
			Assert.IsTrue( colour.R + colour.G + colour.B > 0.5f, $"the shader's colour is bright ({colour})" );
			AreClose( colour.R, Pixel( ids, nearOnly ).R, "one shader, one colour" );
			AreClose( colour.G, Pixel( ids, nearOnly ).G, "one shader, one colour" );
			Assert.AreNotEqual( Pixel( lit, middle ), colour, "not the lit colour" );

			// Wireframe: white lines, the faces' insides the clear colour
			AreClose( Pixel( lit, outside ).B, Pixel( wireframe, nearOnly ).B, "inside a face is the clear colour, as around the boxes" );
			var white = 0;
			for ( int y = 0; y < Height; y++ )
			{
				for ( int x = 0; x < Width; x++ )
				{
					if ( Pixel( wireframe, (x, y) ) is { R: > 0.99f, G: > 0.99f, B: > 0.99f } ) white++;
				}
			}

			Assert.IsTrue( white > 100 && white < Width * Height / 10, $"the edges are white lines ({white} pixels)" );

			Assert.AreEqual( 0, Different( lit, litAgain ), "with no view on, the frame is the lit one again" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// A custom object's draws (<see cref="ShaderDrawer"/>, the terrain clipmap's) take the view's <c>ToolsVisMode</c> as it
	/// changes: its specialization attributes key the variant as combos do, rather than the first value drawn with sticking.
	/// </summary>
	[TestMethod]
	public void CustomDrawsTakeTheViewMode()
	{
		using var backend = new BackendDevice( Device );
		using var drawer = new ShaderDrawer( backend );
		var shader = backend.Shaders.Load( "shaders/complex.shader" );
		if ( shader is null ) Assert.Inconclusive( "complex.shader isn't built" );

		var attributes = new ShaderAttributes();
		attributes.Set( "ToolsVisMode", 0f );
		var lit = drawer.Variant( shader, attributes, "Forward" );
		attributes.Set( "ToolsVisMode", 10f );
		var albedo = drawer.Variant( shader, attributes, "Forward" );
		attributes.Set( "ToolsVisMode", 0f );
		var litAgain = drawer.Variant( shader, attributes, "Forward" );

		Assert.IsNotNull( lit );
		Assert.AreNotSame( lit, albedo, "the albedo view is another variant" );
		Assert.AreSame( lit, litAgain, "the lit variant again" );
	}

	readonly record struct Rgb( float R, float G, float B );

	static Rgb Pixel( byte[] pixels, (int X, int Y) at )
	{
		var i = (at.Y * Width + at.X) * 8;
		return new Rgb( (float)BitConverter.ToHalf( pixels, i ), (float)BitConverter.ToHalf( pixels, i + 2 ), (float)BitConverter.ToHalf( pixels, i + 4 ) );
	}

	static void AreClose( float expected, float actual, string message ) => Assert.AreEqual( expected, actual, 0.01f, message );

	static int Different( byte[] a, byte[] b )
	{
		Assert.AreEqual( a.Length, b.Length );
		var count = 0;
		for ( int i = 0; i < a.Length; i++ )
		{
			if ( a[i] != b[i] ) count++;
		}

		return count;
	}
}
