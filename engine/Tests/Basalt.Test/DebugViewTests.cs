using Basalt;
using Basalt.Features;
using Basalt.Gpu;
using BasaltTests.Headless;
using static BasaltTests.TestScenes;

namespace BasaltTests;

/// <summary>
/// Basalt's own debug views: LOD colours and cull reasons tint meshes through their transform entries, the shadow map view
/// draws a thumbnail per shadow map, and each fills the frame's report. Off, frames are as they were.
/// </summary>
// The views are the host's, which every render system reads, and shadows go through the static ShadowMapper
[TestClass]
[DoNotParallelize]
public class DebugViewTests
{
	const string ThumbnailShader = "shaders/debug_shadow_map.shader";

	[TestCleanup]
	public void ViewsOff() => HeadlessRenderHost.Views = DebugViews.None;

	static RenderMesh Box( int lods )
	{
		var draws = new RenderMesh.Draw[lods][];
		for ( int i = 0; i < lods; i++ )
			draws[i] = [new RenderMesh.Draw( 0, 0, 0, 36, 24, 0, null )];
		return new RenderMesh( new HeadlessMesh( "box" ), UnitBox, draws );
	}

	static void Render( RenderSystem system, RenderWorld world, RenderView view, ViewTarget target )
	{
		using var _ = HeadlessRenderHost.Headless.Capture();
		RenderHost.Current.NextFrame();
		system.Render( world, view, target );
	}

	static uint Tint( RenderSystem system, RenderObject obj ) => system.Transforms[obj.Slot].TintRgb888;

	static uint Packed( Color color ) => TransformBuffer.MakeEntry( Matrix.Identity, color ).TintRgb888;

	[TestMethod]
	public void LodColorsTintMeshesByTheirLod()
	{
		var lodded = Box( 3 );
		var world = new RenderWorld();
		var near = new MeshObject( lodded, new Transform( new Vector3( 20, -4, 0 ) ) ) { LodOverride = 0 };
		var far = new MeshObject( lodded, new Transform( new Vector3( 20, 0, 0 ) ) ) { LodOverride = 2 };
		var single = new MeshObject( Box( 1 ), new Transform( new Vector3( 20, 4, 0 ) ) ) { Tint = new Color( 1, 0, 0 ) };
		world.Add( near );
		world.Add( far );
		world.Add( single );

		using var target = ViewTarget.Create( new Vector2Int( 64, 64 ) );
		using var system = new RenderSystem();

		Render( system, world, LookingDownX(), target );
		Assert.AreEqual( Packed( Color.White ), Tint( system, near ), "off, each keeps its own tint" );
		Assert.AreEqual( Packed( Color.White ), Tint( system, far ) );

		HeadlessRenderHost.Views = DebugViews.LodColors;
		Render( system, world, LookingDownX(), target );
		Assert.AreEqual( Packed( DebugViewReport.LodColor( 0 ) ), Tint( system, near ) );
		Assert.AreEqual( Packed( DebugViewReport.LodColor( 2 ) ), Tint( system, far ) );
		Assert.AreEqual( Packed( new Color( 1, 0, 0 ) ), Tint( system, single ), "one LOD keeps its own" );

		var report = system.DebugReport;
		Assert.AreEqual( 1, report.Lods[0] );
		Assert.AreEqual( 0, report.Lods[1] );
		Assert.AreEqual( 1, report.Lods[2] );
		Assert.AreEqual( 1, report.SingleLod );
	}

	[TestMethod]
	public void CullReasonsDrawCulledMeshesTinted()
	{
		var mesh = Box( 1 );
		var world = new RenderWorld();
		var drawn = new MeshObject( mesh, new Transform( new Vector3( 20, 0, 0 ) ) );
		var tiny = new MeshObject( mesh, new Transform( new Vector3( 900, 0, 0 ) ) );
		var tagged = new MeshObject( mesh, new Transform( new Vector3( 20, 4, 0 ) ) ) { Tags = [new StringToken( "hidden" ).Value] };
		var behind = new MeshObject( mesh, new Transform( new Vector3( -20, 0, 0 ) ) );
		world.Add( drawn );
		world.Add( tiny );
		world.Add( tagged );
		world.Add( behind );

		var view = LookingDownX();
		view.SizeCullThreshold = 0.01f;
		view.ExcludeTags = [new StringToken( "hidden" ).Value];

		using var target = ViewTarget.Create( new Vector2Int( 64, 64 ) );
		using var system = new RenderSystem();
		var meshes = system.GetFeature<MeshRenderFeature>();

		Render( system, world, view, target );
		CollectionAssert.AreEquivalent( new[] { drawn.Index }, system.MainPass.Visible( meshes ).ToArray(), "off, only what's drawn" );
		Assert.AreEqual( 1, system.Stats.ObjectsSizeCulled );
		Assert.AreEqual( 1, system.Stats.ObjectsTagCulled );

		HeadlessRenderHost.Views = DebugViews.CullReasons;
		Render( system, world, view, target );
		CollectionAssert.AreEquivalent( new[] { drawn.Index, tiny.Index, tagged.Index }, system.MainPass.Visible( meshes ).ToArray(), "culled meshes drawn anyway" );
		Assert.AreEqual( Packed( Color.White ), Tint( system, drawn ) );
		Assert.AreEqual( Packed( DebugViewReport.CullColor( CullReason.Size ) ), Tint( system, tiny ) );
		Assert.AreEqual( Packed( DebugViewReport.CullColor( CullReason.Tags ) ), Tint( system, tagged ) );

		var report = system.DebugReport;
		Assert.AreEqual( 4, report.Objects );
		Assert.AreEqual( 1, report.Visible, "only the drawn one counts visible" );
		Assert.AreEqual( 1, report.Culled[(int)CullReason.Frustum] );
		Assert.AreEqual( 1, report.Culled[(int)CullReason.Size] );
		Assert.AreEqual( 1, report.Culled[(int)CullReason.Tags] );
	}

	[TestMethod]
	public void ShadowMapViewDrawsAThumbnailPerMap()
	{
		var world = new RenderWorld();
		world.Add( new MeshObject( Box( 1 ), new Transform( new Vector3( 40, 0, -10 ) ) ) );
		world.Add( new LightObject( LightObject.LightKind.Point, new Transform( new Vector3( 40, 0, 10 ) ) ) { Radius = 50 } );
		world.Add( new LightObject( LightObject.LightKind.Spot, new Transform( new Vector3( 20, 0, 0 ) ) ) { Radius = 80 } );

		using var target = ViewTarget.Create( new Vector2Int( 1280, 720 ) );
		using var system = new RenderSystem();
		var view = LookingDownX();
		view.Viewport = new Rect( 0, 0, 1280, 720 );
		view.Update();
		Basalt.Shadows.ShadowMapper.ResetCache();

		using ( var frame = HeadlessRenderHost.Headless.Capture() )
		{
			RenderHost.Current.NextFrame();
			system.Render( world, view, target );
			Assert.AreEqual( 0, frame.Commands.Count( c => c.Material?.Name == ThumbnailShader ), "off, no thumbnails" );
		}

		HeadlessRenderHost.Views = DebugViews.ShadowMaps;
		using ( var frame = HeadlessRenderHost.Headless.Capture() )
		{
			RenderHost.Current.NextFrame();
			system.Render( world, view, target );

			var maps = system.DebugReport.ShadowMaps;
			Assert.IsTrue( maps.Count( m => m.Kind == ShadowMapKind.Cascade ) > 0, "the sun's cascades" );
			Assert.AreEqual( 1, maps.Count( m => m.Kind == ShadowMapKind.Spot ) );
			Assert.AreEqual( 1, maps.Count( m => m.Kind == ShadowMapKind.Point ) );
			Assert.IsTrue( maps.All( m => m.Rendered && m.Resolution > 0 && m.Texture is not null ) );

			// Inside the view, and none over another
			foreach ( var map in maps )
			{
				Assert.IsTrue( map.Thumbnail.Left >= 0 && map.Thumbnail.Right <= 1280 && map.Thumbnail.Top >= 0 && map.Thumbnail.Bottom <= 720, $"{map.Kind} {map.Index} at {map.Thumbnail}" );
				Assert.IsFalse( maps.Any( other => !other.Equals( map ) && other.Thumbnail.IsInside( map.Thumbnail ) ) );
			}

			var thumbnails = frame.Commands.Where( c => c.Material?.Name == ThumbnailShader ).ToList();
			Assert.AreEqual( maps.Count, thumbnails.Count );
			Assert.IsTrue( thumbnails.All( c => c.Op == GpuOp.DrawFullscreen && c.Texture == target.Color && c.Depth is null ), "into the output, without depth" );
		}
	}
}
