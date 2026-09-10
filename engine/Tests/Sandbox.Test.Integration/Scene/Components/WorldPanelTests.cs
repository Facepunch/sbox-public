using Sandbox.VR;

namespace SceneTests.Components;

[TestClass]
public class WorldPanelComponentTest
{
	/// <summary>
	/// Enabling the WorldPanel component creates a world-space root panel
	/// (Sandbox.UI.WorldPanel) at the GameObject's transform, marked as a world
	/// panel that renders manually, with the default interaction range and the
	/// world panel's default 2x panel scale.
	/// </summary>
	[TestMethod]
	public void EnableCreatesWorldSpacePanel()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 100, 200, 300 );

		var wp = go.Components.Create<WorldPanel>();
		var panel = wp.GetPanel() as Sandbox.UI.WorldPanel;

		Assert.IsNotNull( panel, "GetPanel should return the world space root panel" );
		Assert.IsTrue( panel.IsWorldPanel );
		Assert.IsTrue( panel.RenderedManually );
		Assert.AreEqual( go, panel.GameObject );
		Assert.IsTrue( panel.Position.Distance( go.WorldPosition ) < 0.001f );
		Assert.AreEqual( 1000.0f, panel.MaxInteractionDistance );
		Assert.AreEqual( 2.0f, panel.Scale );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// The world panel's transform is re-copied from the GameObject during the
	/// pre-render stage, so moving and rotating the GameObject moves the panel
	/// after a tick.
	/// </summary>
	[TestMethod]
	public void TransformFollowsGameObjectAfterTick()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var wp = go.Components.Create<WorldPanel>();
		var panel = (Sandbox.UI.WorldPanel)wp.GetPanel();

		go.WorldPosition = new Vector3( 50, -20, 10 );
		go.WorldRotation = Rotation.From( 0, 90, 0 );

		for ( int i = 0; i < 5; i++ ) scene.GameTick();

		Assert.IsTrue( panel.Position.Distance( go.WorldPosition ) < 0.001f, "Panel should follow the GameObject position" );
		Assert.IsTrue( Vector3.Dot( panel.Rotation.Forward, go.WorldRotation.Forward ) > 0.999f, "Panel should follow the GameObject rotation" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// PanelSize and the alignment properties drive the panel bounds: the default
	/// Center/Center alignment centers the rect around the origin, while Left/Top
	/// anchors it at the origin.
	/// </summary>
	[TestMethod]
	public void PanelSizeAndAlignmentDriveBounds()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var wp = go.Components.Create<WorldPanel>();
		var panel = (Sandbox.UI.WorldPanel)wp.GetPanel();

		Assert.AreEqual( new Vector2( 512 ), wp.PanelSize );

		scene.GameTick();

		var b = panel.PanelBounds;
		Assert.AreEqual( -256f, b.Left );
		Assert.AreEqual( -256f, b.Top );
		Assert.AreEqual( 512f, b.Width );
		Assert.AreEqual( 512f, b.Height );

		wp.HorizontalAlign = WorldPanel.HAlignment.Left;
		wp.VerticalAlign = WorldPanel.VAlignment.Top;
		scene.GameTick();

		b = panel.PanelBounds;
		Assert.AreEqual( 0f, b.Left );
		Assert.AreEqual( 0f, b.Top );
		Assert.AreEqual( 512f, b.Width );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// RenderScale scales the world transform of the panel up while dividing the
	/// panel bounds down, so the panel renders larger without changing its layout
	/// resolution.
	/// </summary>
	[TestMethod]
	public void RenderScaleScalesTransformAndBounds()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var wp = go.Components.Create<WorldPanel>();
		var panel = (Sandbox.UI.WorldPanel)wp.GetPanel();

		wp.RenderScale = 2.0f;
		scene.GameTick();

		Assert.AreEqual( 2.0f, panel.WorldScale, 0.001f );

		var b = panel.PanelBounds;
		Assert.AreEqual( -128f, b.Left );
		Assert.AreEqual( -128f, b.Top );
		Assert.AreEqual( 256f, b.Width );
		Assert.AreEqual( 256f, b.Height );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// With LookAtCamera set the panel rotates to face the scene's main camera
	/// during pre-render instead of using the GameObject rotation.
	/// </summary>
	[TestMethod]
	public void LookAtCameraFacesMainCamera()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var camGo = scene.CreateObject();
		camGo.WorldPosition = new Vector3( 500, 200, 100 );
		camGo.Components.Create<CameraComponent>();

		var go = scene.CreateObject();
		var wp = go.Components.Create<WorldPanel>();
		var panel = (Sandbox.UI.WorldPanel)wp.GetPanel();

		wp.LookAtCamera = true;
		scene.GameTick();

		var expected = (camGo.WorldPosition - go.WorldPosition).Normal;
		Assert.IsTrue( Vector3.Dot( panel.Rotation.Forward, expected ) > 0.99f, "Panel should face the camera" );

		go.Destroy();
		camGo.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// InteractionRange writes straight through to the live world panel's
	/// MaxInteractionDistance while the component is enabled.
	/// </summary>
	[TestMethod]
	public void InteractionRangePropagatesLive()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var wp = go.Components.Create<WorldPanel>();
		var panel = (Sandbox.UI.WorldPanel)wp.GetPanel();

		Assert.AreEqual( 1000.0f, wp.InteractionRange );
		Assert.AreEqual( 1000.0f, panel.MaxInteractionDistance );

		wp.InteractionRange = 250.0f;
		Assert.AreEqual( 250.0f, panel.MaxInteractionDistance, "Setter should update the live panel" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// GameObject tags are copied onto the world panel's scene object at enable,
	/// and tag changes made afterwards propagate through OnTagsChanged.
	/// </summary>
	[TestMethod]
	public void TagsPropagateToSceneObject()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.Tags.Add( "hud" );

		var wp = go.Components.Create<WorldPanel>();
		var panel = (Sandbox.UI.WorldPanel)wp.GetPanel();

		Assert.IsTrue( panel.Tags.Has( "hud" ), "Tags present at enable should be copied" );

		go.Tags.Add( "extra" );
		scene.GameTick();

		Assert.IsTrue( panel.Tags.Has( "extra" ), "Tag changes should propagate to the world panel" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// Disabling the component deletes the world panel immediately and nulls
	/// GetPanel. Re-enabling creates a brand new world panel instance.
	/// </summary>
	[TestMethod]
	public void DisableTearsDownPanel()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var wp = go.Components.Create<WorldPanel>();
		var panel = (Sandbox.UI.WorldPanel)wp.GetPanel();

		Assert.IsTrue( panel.IsValid );

		wp.Enabled = false;

		Assert.IsNull( wp.GetPanel(), "Disable should null the world panel" );
		Assert.IsFalse( panel.IsValid, "Disable should delete the world panel immediately" );

		wp.Enabled = true;

		var second = wp.GetPanel();
		Assert.IsNotNull( second );
		Assert.AreNotSame( panel, second, "Re-enable should create a new world panel" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// WorldPanel properties survive a serialize / deserialize round trip of the
	/// owning GameObject.
	/// </summary>
	[TestMethod]
	public void SerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var wp = go.Components.Create<WorldPanel>();
		wp.PanelSize = new Vector2( 256, 128 );
		wp.RenderScale = 0.5f;
		wp.LookAtCamera = true;
		wp.HorizontalAlign = WorldPanel.HAlignment.Left;
		wp.VerticalAlign = WorldPanel.VAlignment.Bottom;
		wp.InteractionRange = 123.0f;

		var json = go.Serialize().ToJsonString();
		var jsonObject = Json.ParseToJsonObject( json );
		Assert.IsNotNull( jsonObject );
		SceneUtility.MakeIdGuidsUnique( jsonObject );

		var copy = new GameObject( false );
		copy.Deserialize( jsonObject );
		copy.Enabled = true;

		var wp2 = copy.GetComponent<WorldPanel>();
		Assert.IsNotNull( wp2 );
		Assert.AreEqual( new Vector2( 256, 128 ), wp2.PanelSize );
		Assert.AreEqual( 0.5f, wp2.RenderScale );
		Assert.IsTrue( wp2.LookAtCamera );
		Assert.AreEqual( WorldPanel.HAlignment.Left, wp2.HorizontalAlign );
		Assert.AreEqual( WorldPanel.VAlignment.Bottom, wp2.VerticalAlign );
		Assert.AreEqual( 123.0f, wp2.InteractionRange );

		go.Destroy();
		copy.Destroy();
		scene.ProcessDeletes();
	}
}

[TestClass]
public class WorldInputComponentTest
{
	/// <summary>
	/// WorldInput exposes sensible defaults: Attack1/Attack2 as the click actions,
	/// the left VR hand as input source, no hovered panel, and a live
	/// WorldPanelInput state object.
	/// </summary>
	[TestMethod]
	public void DefaultPropertySurface()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var wi = go.Components.Create<WorldInput>();

		Assert.AreEqual( "Attack1", wi.LeftMouseAction );
		Assert.AreEqual( "Attack2", wi.RightMouseAction );
		Assert.AreEqual( VRHand.HandSources.Left, wi.VRHandSource );
		Assert.IsNull( wi.Hovered );
		Assert.IsNotNull( wi.WorldPanelInput );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// Every update the component aims its WorldPanelInput ray from the GameObject
	/// position along its forward direction (no camera exists in this scene, so
	/// the mouse-cursor override path can't kick in).
	/// </summary>
	[TestMethod]
	public void UpdateAssignsRayFromTransform()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 10, 20, 30 );
		go.WorldRotation = Rotation.From( 10, 45, 0 );

		var wi = go.Components.Create<WorldInput>();

		scene.GameTick();

		var ray = wi.WorldPanelInput.Ray;
		Assert.IsTrue( ray.Position.Distance( go.WorldPosition ) < 0.001f, "Ray should start at the GameObject position" );
		Assert.IsTrue( Vector3.Dot( ray.Forward, go.WorldRotation.Forward ) > 0.999f, "Ray should point along the GameObject forward" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// WorldInput properties survive a serialize / deserialize round trip of the
	/// owning GameObject.
	/// </summary>
	[TestMethod]
	public void SerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var wi = go.Components.Create<WorldInput>();
		wi.LeftMouseAction = "Use";
		wi.RightMouseAction = "Menu";
		wi.VRHandSource = VRHand.HandSources.Right;

		var json = go.Serialize().ToJsonString();
		var jsonObject = Json.ParseToJsonObject( json );
		Assert.IsNotNull( jsonObject );
		SceneUtility.MakeIdGuidsUnique( jsonObject );

		var copy = new GameObject( false );
		copy.Deserialize( jsonObject );
		copy.Enabled = true;

		var wi2 = copy.GetComponent<WorldInput>();
		Assert.IsNotNull( wi2 );
		Assert.AreEqual( "Use", wi2.LeftMouseAction );
		Assert.AreEqual( "Menu", wi2.RightMouseAction );
		Assert.AreEqual( VRHand.HandSources.Right, wi2.VRHandSource );

		go.Destroy();
		copy.Destroy();
		scene.ProcessDeletes();
	}
}
