using Sandbox.UI;
using SceneTests.Components;
using System;

namespace SceneTests.Core;

/// <summary>
/// Suspension retains the scene and its UI without advancing their engine-driven work.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SceneSuspensionTest : SceneTest
{
	/// <summary>
	/// Suspension freezes the clock and updates without disabling components or creating render worlds.
	/// </summary>
	[TestMethod]
	public void SuspendsUpdatesWithoutChangingComponentLifecycle()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		try
		{
			var component = scene.CreateObject().Components.Create<LifecycleProbeComponent>();
			var updates = 0;
			using var hook = scene.AddHook( GameObjectSystem.Stage.StartUpdate, 0, () => updates++, "test", "Count updates" );
			scene.GameTick( 0.25 );

			scene.IsSuspended = true;
			scene.GameTick( 100 );
			scene.Render( default, null );
			scene.RenderEnvmaps();

			Assert.AreEqual( 0.25, scene.TimeNow );
			Assert.AreEqual( 1, updates );
			Assert.IsTrue( component.Active );
			Assert.AreEqual( 1, component.EnabledCalls );
			Assert.AreEqual( 0, component.DisabledCalls );
			Assert.IsFalse( scene.HasSceneWorld );

			scene.IsSuspended = false;
			scene.GameTick( 0.25 );

			Assert.AreEqual( 0.5, scene.TimeNow );
			Assert.AreEqual( 2, updates );
			Assert.AreEqual( 1, component.EnabledCalls );
		}
		finally
		{
			scene.Destroy();
		}
	}

	/// <summary>
	/// Scene UI preserves its state, releases input and ignores new focus requests while overlays keep ticking.
	/// </summary>
	[TestMethod]
	public void SuspendsScenePanelsButKeepsOverlaysActive()
	{
		var scene = new Scene();
		var ui = new UISystem();
		var root = new RootPanel( ui ) { GameObject = scene };
		var page = new TickCounter { Parent = root, AcceptsFocus = true };
		var overlay = new RootPanel( ui ) { AcceptsFocus = true };
		var overlayContent = new TickCounter { Parent = overlay };

		try
		{
			ui.TickPanels();
			var pageTicks = page.Ticks;
			var overlayTicks = overlayContent.Ticks;
			page.ScrollOffset = new Vector2( 0, 128 );
			ui.SetFocus( page );
			ui.TickFocus();
			page.SetMouseCapture( true );
			ui.Input.SetHovered( page );

			scene.IsSuspended = true;
			ui.TickPanels();
			ui.PreLayout();
			ui.Layout();
			ui.PostLayout();
			root.Render();

			Assert.AreEqual( pageTicks, page.Ticks );
			Assert.IsTrue( overlayContent.Ticks > overlayTicks );
			Assert.AreEqual( new Vector2( 0, 128 ), page.ScrollOffset );
			Assert.AreSame( root, page.Parent );
			Assert.IsFalse( root.IsActive );
			Assert.IsTrue( overlay.IsActive );
			Assert.IsNull( ui.CurrentFocus );
			Assert.IsNull( ui.Input.Hovered );
			Assert.IsFalse( page.HasMouseCapture );
			Assert.IsFalse( ui.SetFocus( page ) );
			Assert.IsFalse( ui.MoveFocus( page, false ) );

			ui.SetFocus( overlay );
			ui.TickFocus();
			Assert.AreSame( overlay, ui.CurrentFocus );

			var lateRoot = new RootPanel( ui ) { GameObject = scene };
			Assert.IsFalse( lateRoot.IsActive );

			scene.IsSuspended = false;
			ui.TickPanels();

			Assert.IsTrue( root.IsActive );
			Assert.IsTrue( lateRoot.IsActive );
			Assert.IsTrue( page.Ticks > pageTicks );
			Assert.AreSame( root, page.Parent );
			scene.IsSuspended = true;
		}
		finally
		{
			ui.Clear();
			scene.Destroy();
		}

		Assert.IsFalse( root.IsValid );
		Assert.IsFalse( overlay.IsValid );
	}

	/// <summary>
	/// Panel callbacks can replace another root without interrupting the remaining UI ticks.
	/// </summary>
	[TestMethod]
	public void CanReplaceRootsDuringTick()
	{
		var ui = new UISystem();
		var page = new TickCounter { Parent = new RootPanel( ui ) };
		var removed = new RootPanel( ui );
		var remaining = new TickCounter { Parent = new RootPanel( ui ) };
		TickCounter added = null;

		try
		{
			page.OnTick = () =>
			{
				removed.Delete( true );
				added = new TickCounter { Parent = new RootPanel( ui ) };
			};

			ui.TickPanels();

			Assert.IsFalse( removed.IsValid );
			Assert.AreEqual( 1, remaining.Ticks );
			Assert.AreEqual( 1, added.Ticks );
		}
		finally
		{
			ui.Clear();
		}
	}

	/// <summary>
	/// Starting a game keeps retained scene listeners; destroying the scene removes them.
	/// </summary>
	[TestMethod]
	public void KeepsListenersAcrossSessionsAndCanDestroyWhileSuspended()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var listener = new Sandbox.Audio.Listener( scene );
		scene.Listener = listener;

		try
		{
			scene.IsSuspended = true;
			Sound.Clear();
			Assert.IsTrue( Sandbox.Audio.Listener.ActiveList.Contains( listener ) );

			scene.IsSuspended = false;
			Assert.AreEqual( 1, Sandbox.Audio.Listener.ActiveList.Count( x => x == listener ) );

			scene.IsSuspended = true;
		}
		finally
		{
			scene.Destroy();
		}

		Assert.IsFalse( scene.IsValid );
		Assert.IsFalse( listener.IsValid );
		Assert.IsFalse( Sandbox.Audio.Listener.ActiveList.Contains( listener ) );
	}

	class TickCounter : Panel
	{
		/// <summary>
		/// Number of UI ticks received.
		/// </summary>
		public int Ticks { get; private set; }

		internal Action OnTick { get; set; }

		/// <inheritdoc />
		public override void Tick()
		{
			Ticks++;
			OnTick?.Invoke();
		}
	}
}
