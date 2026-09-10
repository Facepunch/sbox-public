using Sandbox.UI;

namespace SceneTests.Components;

/// <summary>
/// A minimal PanelComponent used to observe the panel lifecycle and the razor
/// tree build callbacks. The build hash is exposed so tests can force rebuilds.
/// </summary>
public sealed class HudProbePanel : PanelComponent
{
	/// <summary>
	/// How many times OnTreeFirstBuilt has fired.
	/// </summary>
	public int FirstBuilds { get; private set; }

	/// <summary>
	/// How many times OnTreeBuilt has fired.
	/// </summary>
	public int Builds { get; private set; }

	/// <summary>
	/// Value returned from BuildHash - changing this should trigger a rebuild.
	/// </summary>
	public int HashValue { get; set; }

	/// <summary>
	/// Counts the first tree build.
	/// </summary>
	protected override void OnTreeFirstBuilt()
	{
		FirstBuilds++;
	}

	/// <summary>
	/// Counts every tree build.
	/// </summary>
	protected override void OnTreeBuilt()
	{
		Builds++;
	}

	/// <summary>
	/// Feeds <see cref="HashValue"/> into the render hash so tests can dirty the tree.
	/// </summary>
	protected override int BuildHash() => HashValue;
}

[TestClass]
public class ScreenPanelTest
{
	/// <summary>
	/// Creating a ScreenPanel immediately creates its internal GameRootPanel on awake.
	/// The root is rendered manually (not by the normal panel system), is not a world
	/// panel, is linked back to the owning GameObject and starts displayed (Flex)
	/// with the default property values pushed into the panel state.
	/// </summary>
	[TestMethod]
	public void EnableCreatesRootPanel()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var sp = go.Components.Create<ScreenPanel>();

		var root = sp.GetPanel();

		Assert.IsNotNull( root, "Root panel should be created on awake" );
		Assert.IsInstanceOfType( root, typeof( GameRootPanel ) );
		Assert.IsTrue( root.IsValid );
		Assert.IsTrue( ((RootPanel)root).RenderedManually );
		Assert.IsFalse( ((RootPanel)root).IsWorldPanel );
		Assert.AreEqual( go, root.GameObject );
		Assert.AreEqual( DisplayMode.Flex, root.Style.Display );

		// defaults pushed by the OnUpdate call in OnAwake
		Assert.AreEqual( 100, root.Style.ZIndex );
		Assert.AreEqual( 1.0f, root.Style.Opacity );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// A PanelComponent on the same GameObject as a ScreenPanel parents its panel
	/// to the screen panel's root, because IRootPanelComponent lookup on self wins.
	/// </summary>
	[TestMethod]
	public void PanelComponentParentsToRootOnSameObject()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var sp = go.Components.Create<ScreenPanel>();
		var probe = go.Components.Create<HudProbePanel>();

		Assert.IsNotNull( probe.Panel, "PanelComponent should create its panel on enable" );
		Assert.AreEqual( sp.GetPanel(), probe.Panel.Parent );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// A PanelComponent on a child GameObject finds the ScreenPanel in its ancestors
	/// and parents its panel to that root panel.
	/// </summary>
	[TestMethod]
	public void PanelComponentParentsToAncestorRoot()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var sp = go.Components.Create<ScreenPanel>();

		var child = scene.CreateObject();
		child.Parent = go;
		var probe = child.Components.Create<HudProbePanel>();

		Assert.IsNotNull( probe.Panel );
		Assert.AreEqual( sp.GetPanel(), probe.Panel.Parent );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// When a PanelComponent exists before any root, its panel is orphaned. Adding
	/// a ScreenPanel afterwards adopts it - OnEnabled walks self and descendants
	/// calling EnsureParentPanel, which re-parents the orphan to the new root.
	/// </summary>
	[TestMethod]
	public void RootPanelAdoptsExistingPanelComponents()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var probe = go.Components.Create<HudProbePanel>();

		Assert.IsNotNull( probe.Panel );
		Assert.IsNull( probe.Panel.Parent, "Panel should be orphaned without a root panel" );

		var sp = go.Components.Create<ScreenPanel>();

		Assert.AreEqual( sp.GetPanel(), probe.Panel.Parent, "ScreenPanel enable should adopt orphaned panels" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// Plain panels can be manually parented to the screen panel's root and show
	/// up in its child list.
	/// </summary>
	[TestMethod]
	public void ManualChildPanelCanBeAdded()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var sp = go.Components.Create<ScreenPanel>();

		var child = new Panel { Parent = sp.GetPanel() };

		Assert.AreEqual( sp.GetPanel(), child.Parent );
		Assert.IsTrue( sp.GetPanel().Children.Contains( child ) );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// Opacity, Scale, AutoScreenScale, ScaleStrategy and ZIndex are copied into the
	/// root panel state by OnUpdate, so changes become visible after a game tick.
	/// </summary>
	[TestMethod]
	public void PropertyChangesPropagateOnUpdate()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var sp = go.Components.Create<ScreenPanel>();
		var root = (GameRootPanel)sp.GetPanel();

		sp.Opacity = 0.25f;
		sp.Scale = 2.0f;
		sp.AutoScreenScale = false;
		sp.ScaleStrategy = ScreenPanel.AutoScale.FollowDesktopScaling;
		sp.ZIndex = 7;

		scene.GameTick();

		Assert.AreEqual( 0.25f, root.Style.Opacity );
		Assert.AreEqual( 7, root.Style.ZIndex );
		Assert.AreEqual( 2.0f, root.ManualScale );
		Assert.IsFalse( root.AutoScale );
		Assert.AreEqual( ScreenPanel.AutoScale.FollowDesktopScaling, root.ScaleStrategy );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// With AutoScreenScale off the root panel uses the manual Scale verbatim when
	/// the layout pass recomputes scaling from the screen rect.
	/// </summary>
	[TestMethod]
	public void ManualScaleAppliedDuringLayout()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var sp = go.Components.Create<ScreenPanel>();
		var root = (GameRootPanel)sp.GetPanel();

		sp.AutoScreenScale = false;
		sp.Scale = 3.0f;
		scene.GameTick();

		root.PreLayout( new Rect( 0, 0, 1920, 1080 ) );
		Assert.AreEqual( 3.0f, root.Scale );

		// Screen size shouldn't matter for manual scale
		root.PreLayout( new Rect( 0, 0, 960, 540 ) );
		Assert.AreEqual( 3.0f, root.Scale );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// The default ConsistentHeight auto scale strategy scales relative to a 1080p
	/// screen height and multiplies in the manual scale.
	/// </summary>
	[TestMethod]
	public void AutoScaleConsistentHeightTracksScreenHeight()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var sp = go.Components.Create<ScreenPanel>();
		var root = (GameRootPanel)sp.GetPanel();

		Assert.IsTrue( sp.AutoScreenScale, "AutoScreenScale should default on" );
		Assert.AreEqual( ScreenPanel.AutoScale.ConsistentHeight, sp.ScaleStrategy );

		root.PreLayout( new Rect( 0, 0, 1920, 1080 ) );
		Assert.AreEqual( 1.0f, root.Scale, 0.001f );

		root.PreLayout( new Rect( 0, 0, 960, 540 ) );
		Assert.AreEqual( 0.5f, root.Scale, 0.001f );

		// manual scale multiplies into the auto scale
		sp.Scale = 2.0f;
		scene.GameTick();

		root.PreLayout( new Rect( 0, 0, 960, 540 ) );
		Assert.AreEqual( 1.0f, root.Scale, 0.001f );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// Disabling the component hides the root panel by switching its display mode
	/// to None, and re-enabling restores Flex on the same root panel instance.
	/// </summary>
	[TestMethod]
	public void DisableHidesRootPanel()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var sp = go.Components.Create<ScreenPanel>();
		var root = sp.GetPanel();

		Assert.AreEqual( DisplayMode.Flex, root.Style.Display );

		sp.Enabled = false;
		Assert.AreEqual( DisplayMode.None, root.Style.Display );
		Assert.IsTrue( root.IsValid, "Disable should hide, not delete, the root panel" );

		sp.Enabled = true;
		Assert.AreEqual( DisplayMode.Flex, root.Style.Display );
		Assert.AreSame( root, sp.GetPanel(), "Root panel should survive a disable/enable cycle" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// Destroying the GameObject deletes the root panel immediately - the panel
	/// becomes invalid and the component reports a null panel.
	/// </summary>
	[TestMethod]
	public void DestroyDeletesRootPanel()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var sp = go.Components.Create<ScreenPanel>();
		var root = sp.GetPanel();

		Assert.IsTrue( root.IsValid );

		go.Destroy();
		scene.ProcessDeletes();

		Assert.IsNull( sp.GetPanel(), "Destroy should null the root panel" );
		Assert.IsFalse( root.IsValid, "Destroy should delete the root panel immediately" );
	}

	/// <summary>
	/// Each ScreenPanel pushes its ZIndex into its root panel's style. The layering
	/// itself - CameraComponent's UI render pass ordering the scene's ScreenPanels
	/// by ZIndex (the OrderBy in CameraComponent.cs) so the lower index renders
	/// first (underneath) - happens during rendering and isn't assertable headless.
	/// </summary>
	[TestMethod]
	public void TwoScreenPanelsLayerByZIndex()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var goA = scene.CreateObject();
		var spA = goA.Components.Create<ScreenPanel>();
		spA.ZIndex = 10;

		var goB = scene.CreateObject();
		var spB = goB.Components.Create<ScreenPanel>();
		spB.ZIndex = 5;

		scene.GameTick();

		Assert.AreEqual( 10, spA.GetPanel().Style.ZIndex );
		Assert.AreEqual( 5, spB.GetPanel().Style.ZIndex );

		goA.Destroy();
		goB.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// ScreenPanel properties survive a serialize / deserialize round trip of the
	/// owning GameObject.
	/// </summary>
	[TestMethod]
	public void SerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var sp = go.Components.Create<ScreenPanel>();
		sp.Opacity = 0.5f;
		sp.Scale = 2.0f;
		sp.AutoScreenScale = false;
		sp.ScaleStrategy = ScreenPanel.AutoScale.FollowDesktopScaling;
		sp.ZIndex = 42;

		var json = go.Serialize().ToJsonString();
		var jsonObject = Json.ParseToJsonObject( json );
		Assert.IsNotNull( jsonObject );
		SceneUtility.MakeIdGuidsUnique( jsonObject );

		var copy = new GameObject( false );
		copy.Deserialize( jsonObject );
		copy.Enabled = true;

		var sp2 = copy.GetComponent<ScreenPanel>();
		Assert.IsNotNull( sp2 );
		Assert.AreEqual( 0.5f, sp2.Opacity );
		Assert.AreEqual( 2.0f, sp2.Scale );
		Assert.IsFalse( sp2.AutoScreenScale );
		Assert.AreEqual( ScreenPanel.AutoScale.FollowDesktopScaling, sp2.ScaleStrategy );
		Assert.AreEqual( 42, sp2.ZIndex );

		go.Destroy();
		copy.Destroy();
		scene.ProcessDeletes();
	}
}

[TestClass]
public class PanelComponentLifecycleTest
{
	/// <summary>
	/// Enabling a PanelComponent creates its panel with the element name set to the
	/// lowercase component type name, linked to the GameObject and parented to the
	/// root panel found on the same object.
	/// </summary>
	[TestMethod]
	public void PanelCreatedOnEnable()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var sp = go.Components.Create<ScreenPanel>();
		var probe = go.Components.Create<HudProbePanel>();

		Assert.IsNotNull( probe.Panel );
		Assert.IsTrue( probe.Panel.IsValid );
		Assert.AreEqual( "hudprobepanel", probe.Panel.ElementName );
		Assert.AreEqual( go, probe.Panel.GameObject );
		Assert.AreEqual( sp.GetPanel(), probe.Panel.Parent );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// Disabling a PanelComponent deletes its panel (deferred) and nulls the Panel
	/// property. Re-enabling creates a brand new panel which re-parents itself
	/// to the root.
	/// </summary>
	[TestMethod]
	public void DisableDestroysPanel()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var sp = go.Components.Create<ScreenPanel>();
		var probe = go.Components.Create<HudProbePanel>();

		var firstPanel = probe.Panel;
		Assert.IsNotNull( firstPanel );

		probe.Enabled = false;

		Assert.IsNull( probe.Panel, "Panel should be nulled on disable" );
		Assert.IsTrue( firstPanel.IsDeleting, "Old panel should be marked for deletion" );

		probe.Enabled = true;

		Assert.IsNotNull( probe.Panel );
		Assert.AreNotSame( firstPanel, probe.Panel, "Re-enable should create a fresh panel" );
		Assert.AreEqual( sp.GetPanel(), probe.Panel.Parent );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// The razor tree build lifecycle: the first layout pass builds the tree
	/// (OnTreeFirstBuilt fires exactly once), repeated passes settle and stop
	/// rebuilding while the build hash is stable, a hash change triggers exactly
	/// one rebuild and so does an explicit StateHasChanged call.
	/// </summary>
	[TestMethod]
	public void TreeBuildAndRebuild()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var sp = go.Components.Create<ScreenPanel>();
		var probe = go.Components.Create<HudProbePanel>();

		var root = (RootPanel)sp.GetPanel();

		// First layout pass builds the tree
		root.Layout();
		Assert.AreEqual( 1, probe.FirstBuilds );
		Assert.IsTrue( probe.Builds >= 1 );

		// Let the build hash prime, then verify the tree settles
		root.Layout();
		root.Layout();
		var stable = probe.Builds;

		root.Layout();
		Assert.AreEqual( stable, probe.Builds, "Tree should not rebuild while the hash is stable" );

		// Changing the build hash forces a rebuild
		probe.HashValue = 7;
		root.Layout();
		Assert.AreEqual( stable + 1, probe.Builds, "Hash change should rebuild the tree" );

		// StateHasChanged dirties the tree manually
		probe.StateHasChanged();
		root.Layout();
		Assert.AreEqual( stable + 2, probe.Builds, "StateHasChanged should rebuild the tree" );

		Assert.AreEqual( 1, probe.FirstBuilds, "First-build callback should only fire once" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// The class accessors on PanelComponent forward to the underlying panel:
	/// AddClass / HasClass / SetClass / RemoveClass manipulate panel classes.
	/// </summary>
	[TestMethod]
	public void ClassAccessorsForwardToPanel()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.Components.Create<ScreenPanel>();
		var probe = go.Components.Create<HudProbePanel>();

		Assert.IsFalse( probe.HasClass( "open" ) );

		probe.AddClass( "open" );
		Assert.IsTrue( probe.HasClass( "open" ) );
		Assert.IsTrue( probe.Panel.HasClass( "open" ) );

		probe.SetClass( "open", false );
		Assert.IsFalse( probe.HasClass( "open" ) );

		probe.AddClass( "closed" );
		probe.RemoveClass( "closed" );
		Assert.IsFalse( probe.HasClass( "closed" ) );

		go.Destroy();
		scene.ProcessDeletes();
	}
}
