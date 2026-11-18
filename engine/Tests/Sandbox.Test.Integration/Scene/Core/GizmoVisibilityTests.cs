using System;

namespace SceneTests;

[TestClass, DoNotParallelize]
public class GizmoVisibilityTests
{
	[TestInitialize]
	public void Initialize() => ThreadSafe.MarkMainThread();

	sealed class View : IDisposable
	{
		readonly Sandbox.Internal.TypeLibrary _library = Game.TypeLibrary;
		internal readonly Scene Scene;
		internal readonly Gizmo.Instance Gizmo = new();
		internal readonly SceneCamera Camera = new()
		{
			Position = new Vector3( -1000, 0, 0 ),
			Rotation = Rotation.Identity,
			Ortho = true,
			OrthoHeight = 512,
			Size = 512,
			ZNear = 1,
			ZFar = 4096
		};

		internal View()
		{
			Game.TypeLibrary = SharedTypeLibrary.EngineAndTests;
			Scene = new Scene( true, false );
		}

		internal Gizmo.Inputs Input( bool down = false, float y = 0 ) => new()
		{
			Camera = Camera,
			IsHovered = true,
			LeftMouse = down,
			CursorPosition = new Vector2( y, 0 ),
			CursorRay = new Ray( new Vector3( -1000, y, 0 ), Vector3.Forward )
		};

		internal void Frame( GameObject reported = null, float y = 0 )
		{
			Gizmo.Input = Input( y: y );
			using var scene = Scene.PushScope();
			using var gizmo = Gizmo.PushScope();
			if ( reported is not null )
			{
				using var scope = Sandbox.Gizmo.ObjectScope( reported, reported.WorldTransform );
				Sandbox.Gizmo.Hitbox.TrySetHovered( 1000 );
			}
			GizmoBenchmarkProbe.Calls = 0;
			Scene.DrawGizmos();
		}

		public void Dispose()
		{
			Gizmo.Dispose();
			Camera.Dispose();
			Scene.Destroy();
			Game.TypeLibrary = _library;
		}
	}

	[TestMethod]
	public void DisabledGizmosSkipCallbacksButKeepSceneSelection()
	{
		using var view = new View();
		var target = view.Scene.CreateObject();
		target.Components.Create<GizmoBenchmarkProbe>();
		view.Frame();
		view.Gizmo.Settings.GizmosEnabled = false;
		view.Frame();
		Assert.AreEqual( 0, GizmoBenchmarkProbe.Calls );
		Assert.AreEqual( 0, view.Gizmo.hitShapes.Count );
		Assert.AreEqual( 0, view.Gizmo.World.SceneObjects.Count( x => x.RenderingEnabled ) );
		view.Gizmo.QueuePointerEvent( view.Input( true ) );
		view.Gizmo.QueuePointerEvent( view.Input( false ) );
		view.Frame( target );
		Assert.IsTrue( view.Gizmo.Selection.Contains( target ) );
		Assert.AreEqual( 0, GizmoBenchmarkProbe.Calls );
	}

	[TestMethod]
	[DataRow( "object" )]
	[DataRow( "component" )]
	[DataRow( "type" )]
	[DataRow( "disabled" )]
	[DataRow( "inactive" )]
	public void HiddenGizmosDoNotPickPreviousGeometry( string hide )
	{
		using var view = new View();
		var target = view.Scene.CreateObject();
		var probe = target.Components.Create<GizmoBenchmarkProbe>();
		view.Frame();
		view.Frame();
		Assert.AreSame( target, view.Gizmo.current.HoveredObject );
		switch ( hide )
		{
			case "object": target.Flags |= GameObjectFlags.Hidden; break;
			case "component": probe.Flags |= ComponentFlags.Hidden; break;
			case "type": view.Gizmo.Settings.SetGizmoEnabled( probe.GetType(), false ); break;
			case "disabled": view.Gizmo.Settings.GizmosEnabled = false; break;
			case "inactive": probe.Enabled = false; break;
		}
		view.Frame();
		Assert.IsNull( view.Gizmo.current.HoveredObject );
		Assert.AreEqual( 0, GizmoBenchmarkProbe.Calls );
		Assert.AreEqual( 0, view.Gizmo.World.SceneObjects.Count( x => x.RenderingEnabled ) );
	}

	[TestMethod]
	public void StationaryPickingReusesResultsAndInvalidatesForInputViewAndGeometry()
	{
		using var view = new View();
		var target = view.Scene.CreateObject();
		target.Components.Create<GizmoBenchmarkProbe>();
		view.Frame();
		view.Frame();
		var traces = view.Gizmo.PickTraceCount;
		view.Frame();
		Assert.AreEqual( traces, view.Gizmo.PickTraceCount );
		Assert.AreSame( target, view.Gizmo.current.HoveredObject );
		view.Frame( y: 100 );
		Assert.IsTrue( view.Gizmo.PickTraceCount > traces );
		Assert.IsNull( view.Gizmo.current.HoveredObject );
		traces = view.Gizmo.PickTraceCount;
		view.Camera.OrthoHeight *= 2;
		view.Frame( y: 100 );
		Assert.IsTrue( view.Gizmo.PickTraceCount > traces );
		target.WorldPosition = Vector3.Left * 100;
		view.Frame( y: 100 );
		view.Frame( y: 100 );
		Assert.AreSame( target, view.Gizmo.current.HoveredObject );
	}

	[TestMethod]
	public void CachedComponentsRefreshAfterAdditionRemovalAndTypeToggle()
	{
		using var view = new View();
		var target = view.Scene.CreateObject();
		view.Frame();
		var probe = target.Components.Create<GizmoBenchmarkProbe>();
		view.Frame();
		Assert.AreEqual( 1, GizmoBenchmarkProbe.Calls );
		probe.Destroy();
		view.Frame();
		Assert.AreEqual( 0, GizmoBenchmarkProbe.Calls );
		target.Components.Create<GizmoBenchmarkProbe>();
		view.Gizmo.Settings.SetGizmoEnabled( typeof( GizmoBenchmarkProbe ), false );
		view.Frame();
		Assert.AreEqual( 0, GizmoBenchmarkProbe.Calls );
		view.Gizmo.Settings.SetGizmoEnabled( typeof( GizmoBenchmarkProbe ), true );
		view.Frame();
		Assert.AreEqual( 1, GizmoBenchmarkProbe.Calls );
	}

	[TestMethod, TestCategory( Allocations.Category )]
	[DataRow( false )]
	[DataRow( true )]
	public void ShortVisibilityChangesReusePathsAndBuffers( bool toggleType )
	{
		using var view = new View();
		for ( var i = 0; i < 100; ++i ) view.Scene.CreateObject().Components.Create<GizmoBenchmarkProbe>();
		view.Frame();
		var original = view.Gizmo.World.SceneObjects.Single();
		Allocations.AssertSettles( "hide/show without rebuilding scopes or GPU batches", () =>
		{
			if ( toggleType ) view.Gizmo.Settings.SetGizmoEnabled( typeof( GizmoBenchmarkProbe ), false );
			else view.Gizmo.Settings.GizmosEnabled = false;
			view.Frame();
			if ( toggleType ) view.Gizmo.Settings.SetGizmoEnabled( typeof( GizmoBenchmarkProbe ), true );
			else view.Gizmo.Settings.GizmosEnabled = true;
			view.Frame();
		} );
		Assert.AreSame( original, view.Gizmo.World.SceneObjects.Single() );
		Assert.IsTrue( original.RenderingEnabled );
	}
}
