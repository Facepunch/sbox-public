using System;

namespace SceneTests;

[TestClass, DoNotParallelize]
public class GizmoInputTests
{
	sealed class View : IDisposable
	{
		internal readonly SceneCamera Camera = new()
		{
			Position = new Vector3( -1000, 0, 0 ),
			Rotation = Rotation.Identity,
			Size = new Vector2( 512, 512 ),
			Ortho = true,
			OrthoHeight = 512
		};
		internal readonly Gizmo.Instance Gizmo = new();
		internal Vector3 Position;

		internal Sandbox.Gizmo.Inputs Input( float y, bool down, bool hovered = true ) => new()
		{
			Camera = Camera,
			CursorRay = new Ray( new Vector3( -1000, y, 0 ), Vector3.Forward ),
			CursorPosition = Camera.ToScreen( new Vector3( 0, y, 0 ) ),
			LeftMouse = down,
			IsHovered = hovered
		};

		internal void Pointer( float y, bool down, bool hovered = true ) => Gizmo.QueuePointerEvent( Input( y, down, hovered ) );

		internal bool Frame( float y, bool down, bool draw = true, bool interact = true, bool snap = false )
		{
			Gizmo.Input = Input( y, down );
			using var instance = Gizmo.PushScope();
			using var scope = Sandbox.Gizmo.Scope( "tool", new Transform( Position ) );
			Sandbox.Gizmo.Hitbox.CanInteract = interact;
			var changed = Sandbox.Gizmo.Control.UpdateDragSquare( "handle", Rotation.Identity, out var movement );
			Position += movement;
			if ( snap ) Position = Position.SnapToGrid( 16 );
			Sandbox.Gizmo.Transform = new Transform( Position );
			if ( draw ) Sandbox.Gizmo.Draw.DragSquare( "handle", 2, Rotation.Identity );
			return changed;
		}

		public void Dispose()
		{
			Gizmo.Dispose();
			Camera.Dispose();
		}
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void DragUsesDownAndUpPositionsNotLaterCursor( bool entireGestureBetweenFrames )
	{
		using var view = new View();
		view.Frame( 200, false );
		view.Pointer( 0, true );
		if ( !entireGestureBetweenFrames )
		{
			Assert.IsTrue( view.Frame( 50, true ) );
			Assert.AreEqual( 50f, view.Position.y, 0.001f );
		}
		view.Pointer( 60, false );
		Assert.IsTrue( view.Frame( 400, false ) );
		Assert.AreEqual( 60f, view.Position.y, 0.001f );
		Assert.IsFalse( view.Frame( -400, false ) );
		Assert.AreEqual( 60f, view.Position.y, 0.001f );
	}

	[TestMethod]
	public void PressEventSurvivesAFrameWhosePolledButtonsLag()
	{
		using var view = new View();
		view.Frame( 200, false );
		view.Pointer( 0, true );
		// The widget renders before the editor copies Qt's buttons into Input
		view.Frame( 0, false );
		Assert.IsTrue( view.Frame( 50, true ) );
		Assert.AreEqual( 50f, view.Position.y, 0.001f );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void RelinquishingAClickPreservesThePressCoordinatesForBoxSelection( bool entireGestureBetweenFrames )
	{
		using var view = new View();
		view.Frame( 0, false );
		view.Pointer( 0, true );
		if ( entireGestureBetweenFrames ) view.Pointer( 20, false );
		view.Gizmo.Input = view.Input( 200, !entireGestureBetweenFrames );
		using ( view.Gizmo.PushScope() )
		{
			Assert.IsTrue( Gizmo.Pressed.Any );
			view.Gizmo.ClearPress( releasePointer: false );
			Assert.IsFalse( Gizmo.Pressed.Any );
			Assert.IsFalse( Gizmo.HasClicked );
			Assert.IsTrue( Gizmo.WasLeftMousePressed );
			Assert.AreEqual( view.Input( 0, true ).CursorPosition, Gizmo.Pressed.CursorPosition );
			Assert.AreEqual( view.Input( 0, true ).CursorRay, Gizmo.Pressed.Ray );
		}
		Assert.IsFalse( view.Frame( 200, false ) );
		Assert.AreEqual( Vector3.Zero, view.Position );
	}

	[TestMethod]
	public void RelinquishingAClickCancelsItsPendingReleaseAction()
	{
		using var view = new View();
		view.Frame( 0, false );
		view.Pointer( 0, true );
		view.Pointer( 0, false );
		view.Gizmo.Input = view.Input( 0, false );
		using var instance = view.Gizmo.PushScope();
		Assert.IsTrue( Gizmo.HasClicked );
		Gizmo.Pressed.ClearPath();
		Assert.IsFalse( Gizmo.HasClicked );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void BackgroundDragsSuppressHoverUntilReleaseRegardlessOfWhereTheyStarted( bool startOverHandle )
	{
		using var view = new View();
		view.Frame( 0, false );
		var start = startOverHandle ? 0 : 100;
		view.Pointer( start, true );
		view.Gizmo.Input = view.Input( start, true );
		using ( view.Gizmo.PushScope() )
		{
			if ( startOverHandle ) view.Gizmo.ClearPress( releasePointer: false );
			Assert.IsFalse( Gizmo.HasHovered );
			using var scope = Gizmo.Scope( "tool", Transform.Zero );
			Gizmo.Draw.DragSquare( "handle", 2, Rotation.Identity );
		}
		Assert.IsFalse( view.Frame( 0, true ) );
		Assert.IsNull( view.Gizmo.current.HoveredPath );
		Assert.IsNull( view.Gizmo.current.HoveredObject );
		view.Pointer( 0, false );
		Assert.IsFalse( view.Frame( 0, false ) );
		Assert.IsFalse( view.Gizmo.current.Click );
		Assert.IsNull( view.Gizmo.current.HoveredPath );
		view.Frame( 0, false );
		Assert.IsNotNull( view.Gizmo.current.HoveredPath );
	}

	[TestMethod]
	public void PublicClearPathReleasesThePointerAndAllowsHoverWhileHeld()
	{
		using var view = new View();
		view.Frame( 0, false );
		view.Pointer( 0, true );
		view.Gizmo.Input = view.Input( 100, true );
		using ( view.Gizmo.PushScope() )
		{
			Gizmo.Pressed.ClearPath();
			using var scope = Gizmo.Scope( "tool", Transform.Zero );
			Gizmo.Draw.DragSquare( "handle", 2, Rotation.Identity );
		}
		Assert.IsFalse( view.Frame( 0, true ) );
		Assert.IsNotNull( view.Gizmo.current.HoveredPath );
	}

	[TestMethod]
	public void PublicClearPathAllowsAnotherViewportToTakeThePointer()
	{
		using var first = new View();
		using var second = new View();
		first.Frame( 0, false );
		second.Frame( 0, false );
		first.Pointer( 0, true );
		first.Gizmo.Input = first.Input( 0, true );
		using ( first.Gizmo.PushScope() ) Gizmo.Pressed.ClearPath();
		second.Pointer( 0, true );
		Assert.IsTrue( second.Frame( 20, true ) );
		Assert.AreEqual( 20f, second.Position.y, .001f );
	}

	[TestMethod]
	public void MissedPressDoesNotCaptureAfterMovingOntoHandle()
	{
		using var view = new View();
		view.Frame( 0, false );
		view.Pointer( 100, true );
		Assert.IsFalse( view.Frame( 0, true ) );
		Assert.IsFalse( view.Frame( 1, true ) );
		Assert.AreEqual( Vector3.Zero, view.Position );
	}

	[TestMethod]
	public void DrawingAndPickingUseFinalSnappedPosition()
	{
		using var view = new View();
		view.Frame( 0, false );
		view.Pointer( 0, true );
		view.Pointer( 23, false );
		Assert.IsTrue( view.Frame( 200, false, snap: true ) );
		Assert.AreEqual( 16f, view.Position.y );
		Assert.AreEqual( view.Position, view.Gizmo.hitShapes.Single().Transform.Position );
		view.Pointer( 16, true );
		Assert.IsTrue( view.Frame( 20, true ) );
		Assert.AreEqual( 20f, view.Position.y, 0.001f );
	}

	[TestMethod]
	public void DisabledPickingPreservesExistingDrag()
	{
		using var view = new View();
		view.Frame( 0, false );
		view.Pointer( 0, true );
		Assert.IsTrue( view.Frame( 5, true, interact: false ) );
		Assert.IsTrue( view.Frame( 10, true, interact: false ) );
		view.Pointer( 15, false, hovered: false );
		Assert.IsTrue( view.Frame( 100, false, interact: false ) );
		Assert.AreEqual( 15f, view.Position.y, 0.001f );
	}

	[TestMethod]
	public void OtherViewportCannotStealCapture()
	{
		using var first = new View();
		using var second = new View();
		first.Frame( 0, false );
		second.Frame( 0, false );
		first.Pointer( 0, true );
		first.Frame( 5, true );
		second.Pointer( 0, true );
		Assert.IsFalse( second.Frame( 20, true ) );
		Assert.IsTrue( first.Frame( 10, true ) );
		Assert.AreEqual( 10f, first.Position.y, 0.001f );
		Assert.AreEqual( Vector3.Zero, second.Position );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void RemovedOrCancelledControlCannotResumeWhileHeld( bool cancel )
	{
		using var view = new View();
		view.Frame( 0, false );
		view.Pointer( 0, true );
		view.Frame( 5, true, draw: cancel );
		if ( cancel ) view.Gizmo.CancelPointerInput();
		Assert.IsFalse( view.Frame( 10, true ) );
		Assert.AreEqual( 5f, view.Position.y, 0.001f );
	}

	[TestMethod]
	public void MultipleGesturesAreNotMerged()
	{
		using var view = new View();
		view.Frame( 0, false );
		view.Pointer( 0, true );
		view.Pointer( 1, false );
		view.Pointer( 1, true );
		view.Pointer( 2, false );
		view.Frame( 100, false );
		Assert.AreEqual( 1f, view.Position.y, 0.001f );
		view.Frame( 100, false );
		Assert.AreEqual( 2f, view.Position.y, 0.001f );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void RegrabAfterReleaseIsDeferredToNextFrame( bool entireRegrabBetweenFrames )
	{
		using var view = new View();
		view.Frame( 0, false );
		view.Pointer( 0, true );
		Assert.IsTrue( view.Frame( 10, true ) );

		view.Pointer( 11, false );
		view.Pointer( 11, true );
		if ( entireRegrabBetweenFrames )
			view.Pointer( 20, false );

		Assert.IsTrue( view.Frame( 100, !entireRegrabBetweenFrames ) );
		Assert.AreEqual( 11f, view.Position.y, 0.001f );
		Assert.IsFalse( view.Gizmo.MousePressed );
		Assert.IsTrue( view.Gizmo.MouseReleased );

		Assert.IsTrue( view.Frame( 100, !entireRegrabBetweenFrames ) );
		Assert.AreEqual( entireRegrabBetweenFrames ? 20f : 100f, view.Position.y, 0.001f );

		if ( entireRegrabBetweenFrames )
		{
			Assert.IsFalse( view.Frame( 200, false ) );
			Assert.AreEqual( 20f, view.Position.y, 0.001f );
			return;
		}

		Assert.IsTrue( view.Frame( 110, true ) );
		Assert.AreEqual( 110f, view.Position.y, 0.001f );
		view.Pointer( 120, false );
		Assert.IsTrue( view.Frame( 400, false ) );
		Assert.AreEqual( 120f, view.Position.y, 0.001f );
	}

	[TestMethod]
	public void InputRefreshDoesNotReplacePressOrReleaseRay()
	{
		using var view = new View();
		view.Frame( 0, false );
		view.Pointer( 0, true );
		view.Pointer( 10, false );
		view.Gizmo.Input = view.Input( 100, false );
		using var instance = view.Gizmo.PushScope();
		view.Gizmo.Input = view.Input( 200, false );
		view.Gizmo.RefreshInput();
		using var scope = Gizmo.Scope( "tool" );
		Assert.IsTrue( Gizmo.Control.UpdateDragSquare( "handle", Rotation.Identity, out var delta ) );
		Assert.AreEqual( 10f, delta.y, 0.001f );
		Assert.AreEqual( view.Input( 10, false ).CursorPosition, Gizmo.CursorPosition );
		Assert.AreEqual( view.Input( 0, true ).CursorRay, Gizmo.Pressed.Ray );
	}

	[TestMethod]
	public void BoundsCaptureStartsFromCurrentBoxAndDrawsAcceptedBounds()
	{
		using var view = new View();
		view.Gizmo.Settings.SnapToGrid = false;
		var bounds = new BBox( new Vector3( -20 ), new Vector3( 20 ) );
		view.Gizmo.Input = view.Input( 20, false );
		using ( view.Gizmo.PushScope() )
			Gizmo.Draw.BoundingBox( "bounds", bounds );
		view.Pointer( 20, true );
		view.Pointer( 35, false );
		view.Gizmo.Input = view.Input( 200, false );
		using ( view.Gizmo.PushScope() )
		{
			Assert.IsTrue( Gizmo.Control.UpdateBoundingBox( "bounds", bounds, out bounds ) );
			Assert.AreEqual( 35f, bounds.Maxs.y, 0.001f );
			Assert.AreEqual( -20f, bounds.Mins.y );
			bounds.Maxs.y = MathF.Min( bounds.Maxs.y, 30 );
			Gizmo.Draw.BoundingBox( "bounds", bounds );
			Assert.AreEqual( 30f, view.Gizmo.hitShapes.First( x => x.Path.Contains( "/Left/" ) ).Transform.Position.y, 0.001f );
		}
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void SceneHitCanSelectOnRelease( bool entireClickBetweenFrames )
	{
		using var view = new View();
		var target = new object();
		view.Pointer( 0, true );
		if ( !entireClickBetweenFrames )
		{
			view.Gizmo.Input = view.Input( 0, true );
			using ( view.Gizmo.PushScope() )
			using ( Gizmo.ObjectScope( target, Transform.Zero ) )
			{
				Gizmo.Hitbox.TrySetHovered( 1000 );
				Assert.IsTrue( Gizmo.Pressed.This );
				Assert.IsFalse( Gizmo.WasClicked );
			}
		}
		view.Pointer( 0, false );
		view.Gizmo.Input = view.Input( 200, false );
		using ( view.Gizmo.PushScope() )
		using ( Gizmo.ObjectScope( target, Transform.Zero ) )
		{
			Gizmo.Hitbox.TrySetHovered( 1000 );
			Assert.IsTrue( Gizmo.WasClicked );
			Gizmo.Select();
			Assert.IsTrue( view.Gizmo.Selection.Contains( target ) );
		}
	}

	[TestMethod]
	public void ReleaseOnAnotherObjectDoesNotClickIt()
	{
		using var view = new View();
		var first = new object();
		var second = new object();
		view.Pointer( 0, true );
		view.Gizmo.Input = view.Input( 0, true );
		using ( view.Gizmo.PushScope() )
		using ( Gizmo.ObjectScope( first, Transform.Zero ) )
			Gizmo.Hitbox.TrySetHovered( 1000 );
		view.Pointer( 20, false );
		view.Gizmo.Input = view.Input( 200, false );
		using ( view.Gizmo.PushScope() )
		using ( Gizmo.ObjectScope( second, Transform.Zero ) )
		{
			Gizmo.Hitbox.TrySetHovered( 1000 );
			Assert.IsFalse( Gizmo.WasClicked );
		}
	}

	[TestMethod]
	public void FlushKeepsGizmoWorldAvailableToCamera()
	{
		var scene = new Scene();
		try
		{
			var camera = scene.CreateObject().AddComponent<CameraComponent>();
			using var view = new View();
			view.Gizmo.Input = view.Input( 0, false );
			using var instance = view.Gizmo.PushScope();
			Gizmo.Draw.Position( "position" );
			Gizmo.Draw.End();
			camera.UpdateSceneCamera( view.Camera );
			Assert.IsTrue( view.Camera.DebugWorlds.Contains( view.Gizmo.World ) );
		}
		finally
		{
			scene.Destroy();
		}
	}
}
