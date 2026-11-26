namespace EngineTests;

/// <summary>
/// A model moved right before its world's frame is drawn, as the editor's tools do before the viewport's frame, is drawn at its
/// new place in that frame - and so is the gizmo drawn at its new place, not the one it had a frame before.
/// </summary>
[TestClass]
public class MovedModelDrawsNowTest
{
	const int Size = 96;

	static int Lit( Color32[] pixels ) => pixels.Count( p => p.r > 8 );

	static int Differing( Color32[] a, Color32[] b )
	{
		Assert.AreEqual( a.Length, b.Length );
		var count = 0;
		for ( int i = 0; i < a.Length; i++ )
		{
			if ( !a[i].Equals( b[i] ) ) count++;
		}

		return count;
	}

	/// <summary>
	/// Frames until the box is drawn (its model loads on workers), then <paramref name="framesBefore"/> more; returns the last.
	/// </summary>
	static Color32[] DrawnFor( System.Func<Color32[]> frame, int framesBefore )
	{
		Color32[] pixels = null;
		for ( int i = 0; i < 300 && (pixels is null || Lit( pixels ) < 100); i++ ) pixels = frame();
		Assert.IsTrue( Lit( pixels ) > 100, $"the box draws ({Lit( pixels )} pixels)" );
		for ( int i = 0; i < framesBefore; i++ ) pixels = frame();
		return pixels;
	}

	/// <summary>
	/// Moves the box in turn, checking that the frame right after each move is the picture every frame after it draws (nothing
	/// else changes), and that the box did move on screen.
	/// </summary>
	static void MovesAreDrawnAtOnce( System.Func<Color32[]> frame, System.Action<Vector3> move, int framesBefore )
	{
		var before = DrawnFor( frame, framesBefore );
		foreach ( var y in new[] { 60f, -60f, 30f } )
		{
			move( new Vector3( 0, y, 0 ) );
			var moved = frame();

			Color32[] settled = null;
			for ( int i = 0; i < 5; i++ ) settled = frame();

			Assert.IsTrue( Differing( before, settled ) > 20, $"y={y}: the box moved on screen" );
			Assert.AreEqual( 0, Differing( moved, settled ), $"y={y}: the frame right after the move draws what the frames after it do" );
			before = settled;
		}
	}

	/// <summary>
	/// A scene model made just before, or one that has kept still long enough to be resident, static or not, is drawn at its new
	/// place in the frame right after it moves.
	/// </summary>
	[TestMethod]
	[DataRow( false, 1 )]
	[DataRow( false, 100 )]
	[DataRow( true, 1 )]
	[DataRow( true, 100 )]
	public void TheFrameRightAfterAMoveDrawsTheSceneModelThere( bool isStatic, int framesBefore )
	{
		TestGraphics.Require();
		ThreadSafe.MarkMainThread();

		var world = new SceneWorld();
		try
		{
			_ = new SceneDirectionalLight( world, Rotation.From( 45, 30, 0 ), Color.White );
			var box = new SceneModel( world, Model.Load( "models/dev/box.vmdl" ), Transform.Zero );
			box.Flags.IsStatic = isStatic;

			using var camera = new SceneCamera( "moved model" ) { World = world, Position = new Vector3( -200, 0, 0 ), Rotation = Rotation.Identity, FieldOfView = 60, BackgroundColor = Color.Black };
			camera.DebugMode = SceneCameraDebugMode.FullBright;
			using var bitmap = new Bitmap( Size, Size );

			MovesAreDrawnAtOnce( () =>
			{
				System.Threading.Thread.Sleep( 20 );
				camera.RenderToBitmap( bitmap );
				return bitmap.GetPixels32();
			}, position => box.Position = position, framesBefore );
		}
		finally
		{
			world.Delete();
		}
	}

	/// <summary>
	/// The same through a scene, as the editor's viewport draws one: a game object with a model renderer moved by its world
	/// position, a camera component drawing after the scene's render preparation, and a gizmo frame open around the camera's
	/// drawing, which the tools draw their box around the object into (<see cref="Gizmo.Draw.LineBBox"/>) just before it. The
	/// gizmo's lines are in the frame that draws them, where the object is.
	/// </summary>
	[TestMethod]
	[DataRow( 1, false )]
	[DataRow( 100, false )]
	[DataRow( 1, true )]
	[DataRow( 100, true )]
	public void TheFrameRightAfterAMoveDrawsTheGameObjectAndItsGizmoThere( int framesBefore, bool withGizmo )
	{
		TestGraphics.Require();

		var scene = new Scene();
		using var scope = scene.Push();
		try
		{
			var camera = scene.CreateObject().Components.Create<CameraComponent>();
			camera.WorldPosition = new Vector3( -200, 0, 0 );
			camera.WorldRotation = Rotation.Identity;
			camera.FieldOfView = 60;
			camera.BackgroundColor = Color.Black;
			camera.AutoExposure.Enabled = false;
			camera.SceneCamera.Attributes.Set( "msaa", 0 );
			scene.CreateObject().Components.Create<AmbientLight>().Color = Color.White;

			var go = scene.CreateObject();
			go.Components.Create<ModelRenderer>().Model = Model.Load( "models/dev/box.vmdl" );
			using var bitmap = new Bitmap( Size, Size );
			using var gizmo = new Gizmo.Instance();

			MovesAreDrawnAtOnce( () =>
			{
				System.Threading.Thread.Sleep( 20 );
				scene.GameTick( 1.0 / 60 );
				scene.PreRender();
				using ( gizmo.PushScope() )
				{
					if ( withGizmo ) Gizmo.Draw.LineBBox( new BBox( go.WorldPosition - 25, go.WorldPosition + 25 ) );
					camera.RenderToBitmap( bitmap, false );
				}

				var pixels = bitmap.GetPixels32();
				EngineLoop.DrainFrameEndDisposables();
				RenderTarget.EndOfFrame();
				EngineLoop.RunAsyncTasks();
				return pixels;
			}, position => go.WorldPosition = position, framesBefore );
		}
		finally
		{
			scene.Destroy();
		}
	}
}
