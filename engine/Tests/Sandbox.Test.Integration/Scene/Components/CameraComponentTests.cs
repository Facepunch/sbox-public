namespace SceneTests.Components;

[TestClass]
public class CameraComponentExtrasTest
{
	/// <summary>
	/// Creates a camera at the scene origin with identity rotation, a 90 degree
	/// horizontal field of view and a fixed 800x600 custom size so the screen
	/// math in these tests is deterministic.
	/// </summary>
	static CameraComponent CreateTestCamera( Scene scene )
	{
		var go = scene.CreateObject();
		var cam = go.Components.Create<CameraComponent>();
		cam.FieldOfView = 90;
		cam.CustomSize = new Vector2( 800, 600 );
		return cam;
	}

	/// <summary>
	/// A world point straight ahead of the camera maps to the centre of the
	/// screen: (0.5, 0.5) in normalized coordinates and half the custom size in
	/// pixel coordinates.
	/// </summary>
	[TestMethod]
	public void CenterPointMapsToScreenCenter()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var cam = CreateTestCamera( scene );

		var n = cam.PointToScreenNormal( new Vector3( 500, 0, 0 ) );
		Assert.AreEqual( 0.5f, n.x, 0.01f );
		Assert.AreEqual( 0.5f, n.y, 0.01f );

		var px = cam.PointToScreenPixels( new Vector3( 500, 0, 0 ) );
		Assert.AreEqual( 400f, px.x, 1.0f );
		Assert.AreEqual( 300f, px.y, 1.0f );
	}

	/// <summary>
	/// World-to-screen mapping respects the engine axis conventions: points to the
	/// left of the camera (+Y) land on the left half of the screen (x &lt; 0.5)
	/// and points above (+Z) land on the upper half (y &lt; 0.5).
	/// </summary>
	[TestMethod]
	public void ScreenMappingDirections()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var cam = CreateTestCamera( scene );

		var left = cam.PointToScreenNormal( new Vector3( 500, 100, 0 ) );
		Assert.IsTrue( left.x < 0.5f, $"Point to the left should be on the left half: {left}" );

		var right = cam.PointToScreenNormal( new Vector3( 500, -100, 0 ) );
		Assert.IsTrue( right.x > 0.5f, $"Point to the right should be on the right half: {right}" );

		var up = cam.PointToScreenNormal( new Vector3( 500, 0, 100 ) );
		Assert.IsTrue( up.y < 0.5f, $"Point above should be on the upper half: {up}" );

		var down = cam.PointToScreenNormal( new Vector3( 500, 0, -100 ) );
		Assert.IsTrue( down.y > 0.5f, $"Point below should be on the lower half: {down}" );
	}

	/// <summary>
	/// The isBehind out parameter reports whether the queried world position is
	/// behind the camera plane.
	/// </summary>
	[TestMethod]
	public void PointBehindCameraReportsBehind()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var cam = CreateTestCamera( scene );

		cam.PointToScreenNormal( new Vector3( -500, 0, 0 ), out var behind );
		Assert.IsTrue( behind );

		cam.PointToScreenNormal( new Vector3( 500, 0, 0 ), out var front );
		Assert.IsFalse( front );
	}

	/// <summary>
	/// Perspective ScreenPixelToRay: the centre pixel produces a ray along the
	/// camera forward from the camera position, and with a 90 degree horizontal
	/// FOV the left screen edge produces a ray at exactly 45 degrees.
	/// </summary>
	[TestMethod]
	public void PerspectivePixelRays()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var cam = CreateTestCamera( scene );

		var center = cam.ScreenPixelToRay( new Vector2( 400, 300 ) );
		Assert.IsTrue( center.Position.Distance( cam.WorldPosition ) < 0.001f, "Perspective rays start at the camera" );
		Assert.IsTrue( center.Forward.Distance( Vector3.Forward ) < 0.001f, "Centre pixel ray should be the camera forward" );

		var leftEdge = cam.ScreenPixelToRay( new Vector2( 0, 300 ) );
		var expected = new Vector3( 1, 1, 0 ).Normal;
		Assert.IsTrue( leftEdge.Forward.Distance( expected ) < 0.001f, $"Left edge ray should be 45 degrees: {leftEdge.Forward}" );
	}

	/// <summary>
	/// ScreenNormalToRay is the normalized-coordinate version of ScreenPixelToRay -
	/// both produce the same ray for equivalent inputs.
	/// </summary>
	[TestMethod]
	public void NormalRayMatchesPixelRay()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var cam = CreateTestCamera( scene );

		var pixel = cam.ScreenPixelToRay( new Vector2( 200, 150 ) );
		var normal = cam.ScreenNormalToRay( new Vector3( 0.25f, 0.25f, 0 ) );

		Assert.IsTrue( pixel.Position.Distance( normal.Position ) < 0.0001f );
		Assert.IsTrue( pixel.Forward.Distance( normal.Forward ) < 0.0001f );
	}

	/// <summary>
	/// In orthographic mode all screen rays are parallel to the camera forward;
	/// the centre ray starts on the near plane and edge rays are offset
	/// perpendicular to the view direction by half the ortho width.
	/// </summary>
	[TestMethod]
	public void OrthographicRaysAreParallel()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var cam = CreateTestCamera( scene );
		cam.Orthographic = true;
		cam.OrthographicHeight = 600;
		cam.ZNear = 10;

		var center = cam.ScreenPixelToRay( new Vector2( 400, 300 ) );
		var edge = cam.ScreenPixelToRay( new Vector2( 0, 300 ) );

		Assert.IsTrue( center.Forward.Distance( edge.Forward ) < 0.0001f, "Orthographic rays should be parallel" );
		Assert.IsTrue( center.Position.Distance( new Vector3( 10, 0, 0 ) ) < 0.001f, "Centre ray starts on the near plane along forward" );

		// 600 ortho height on a 800x600 screen = 800 ortho width, so the edge is 400 away
		var delta = edge.Position - center.Position;
		Assert.AreEqual( 400f, delta.Length, 0.1f );
		Assert.AreEqual( 0f, Vector3.Dot( delta, center.Forward ), 0.001f );
	}

	/// <summary>
	/// The component pushes FieldOfView, ZNear, ZFar, Size and the orthographic
	/// settings into its internal SceneCamera whenever the screen math APIs
	/// refresh the camera transform.
	/// </summary>
	[TestMethod]
	public void PropertiesPropagateToSceneCamera()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var cam = CreateTestCamera( scene );
		cam.FieldOfView = 75;
		cam.ZNear = 5;
		cam.ZFar = 2048;

		cam.PointToScreenNormal( new Vector3( 100, 0, 0 ) );

		Assert.AreEqual( 75f, cam.SceneCamera.FieldOfView );
		Assert.AreEqual( 5f, cam.SceneCamera.ZNear );
		Assert.AreEqual( 2048f, cam.SceneCamera.ZFar );
		Assert.AreEqual( new Vector2( 800, 600 ), cam.SceneCamera.Size );
		Assert.IsFalse( cam.SceneCamera.Ortho );

		cam.Orthographic = true;
		cam.OrthographicHeight = 512;

		cam.PointToScreenNormal( new Vector3( 100, 0, 0 ) );

		Assert.IsTrue( cam.SceneCamera.Ortho );
		Assert.AreEqual( 512f, cam.SceneCamera.OrthoHeight );
	}

	/// <summary>
	/// ScreenToWorld converts the centre of the screen to a world position on the
	/// near frustum plane, directly ahead of the camera.
	/// </summary>
	[TestMethod]
	public void ScreenToWorldProjectsAlongForward()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var cam = CreateTestCamera( scene );
		cam.ZNear = 10;

		var world = cam.ScreenToWorld( new Vector2( 400, 300 ) );
		var dir = (world - cam.WorldPosition).Normal;

		Assert.IsTrue( dir.Distance( Vector3.Forward ) < 0.01f, $"Centre of screen should project straight ahead: {world}" );
		Assert.IsTrue( world.Distance( cam.WorldPosition ) < 100.0f, "Projected point should be near the camera" );
	}

	/// <summary>
	/// CameraComponent projection properties survive a serialize / deserialize
	/// round trip of the owning GameObject.
	/// </summary>
	[TestMethod]
	public void SerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var cam = go.Components.Create<CameraComponent>();
		cam.FieldOfView = 75;
		cam.FovAxis = CameraComponent.Axis.Vertical;
		cam.ZNear = 5;
		cam.ZFar = 4096;
		cam.Orthographic = true;
		cam.OrthographicHeight = 640;

		var json = go.Serialize().ToJsonString();
		var jsonObject = Json.ParseToJsonObject( json );
		Assert.IsNotNull( jsonObject );
		SceneUtility.MakeIdGuidsUnique( jsonObject );

		var copy = new GameObject( false );
		copy.Deserialize( jsonObject );
		copy.Enabled = true;

		var cam2 = copy.GetComponent<CameraComponent>();
		Assert.IsNotNull( cam2 );
		Assert.AreEqual( 75f, cam2.FieldOfView );
		Assert.AreEqual( CameraComponent.Axis.Vertical, cam2.FovAxis );
		Assert.AreEqual( 5f, cam2.ZNear );
		Assert.AreEqual( 4096f, cam2.ZFar );
		Assert.IsTrue( cam2.Orthographic );
		Assert.AreEqual( 640f, cam2.OrthographicHeight );

		go.Destroy();
		copy.Destroy();
		scene.ProcessDeletes();
	}
}
