using System.Runtime.InteropServices;

namespace Sandbox;

public static partial class Gizmo
{
	public partial class Instance
	{
		static Instance pressOwner;
		readonly Queue<Inputs> pointerEvents = [];
		internal List<HitShape> hitShapes = [];
		List<HitShape> displayedHitShapes = [];
		bool usesPointerEvents;
		bool eventsProcessed;
		bool captureHasShape;
		bool cancelled;
		Inputs releaseInput;

		internal bool IsPicking { get; private set; }
		internal bool MousePressed { get; private set; }
		internal bool MouseReleased { get; private set; }
		internal bool HasReleaseInput { get; private set; }
		internal static bool HasActivePress => !string.IsNullOrEmpty( pressOwner?.current.PressedPath );
		internal bool IsReportedPress => pressOwner == this && !captureHasShape && MousePressed && !cancelled;

		internal void QueuePointerEvent( Inputs input )
		{
			usesPointerEvents = true;
			pointerEvents.Enqueue( input );
		}

		internal void CancelPointerInput()
		{
			pointerEvents.Clear();
			ClearPress();
			HasReleaseInput = false;
		}

		internal void RefreshInput()
		{
			current.Input = Input;
			if ( !eventsProcessed )
			{
				eventsProcessed = true;
				ReadPointerEvents();
			}
			if ( usesPointerEvents )
				current.Input.DoubleClick = MousePressed && pressed.Input.DoubleClick;

			if ( HasReleaseInput )
			{
				current.Input.CursorPosition = releaseInput.CursorPosition;
				current.Input.CursorRay = releaseInput.CursorRay;
				current.Input.Modifiers = releaseInput.Modifiers;
				current.Input.IsHovered = releaseInput.IsHovered;
				current.Input.LeftMouse = false;
			}
			if ( !usesPointerEvents )
			{
				MousePressed = !previous.Input.LeftMouse && current.Input.LeftMouse;
				MouseReleased = previous.Input.LeftMouse && !current.Input.LeftMouse;
			}

			Pick();
			if ( pressOwner is not null && pressOwner != this ) return;
			if ( !usesPointerEvents && MousePressed && current.Input.IsHovered && !cancelled && pressOwner is null )
				CapturePress();
			UpdateCapture();
		}

		void ReadPointerEvents()
		{
			var input = current.Input;
			while ( pointerEvents.TryPeek( out var next ) )
			{
				if ( next.LeftMouse && (MousePressed || HasReleaseInput) ) break;
				pointerEvents.Dequeue();
				if ( pressOwner is not null && pressOwner != this ) continue;

				if ( next.LeftMouse )
				{
					while ( pointerEvents.TryPeek( out var duplicate ) && duplicate.LeftMouse )
					{
						pointerEvents.Dequeue();
						next.DoubleClick |= duplicate.DoubleClick;
					}

					current.Input = next;
					Pick();
					cancelled = false;
					CapturePress();
					MousePressed = true;
				}
				else
				{
					releaseInput = next;
					HasReleaseInput = true;
					MouseReleased = true;
				}
			}
			current.Input = input;
			// The polled buttons can lag the press event (a widget renders before its owner copies Qt's buttons into
			// Input), and reading that as a release would drop the press in the frame it arrived.
			if ( MousePressed && !HasReleaseInput ) current.Input.LeftMouse = true;
		}

		void CapturePress()
		{
			current.PressedPath = current.HoveredPath;
			pressed = current;
			captureHasShape = !string.IsNullOrEmpty( current.PressedPath );
			pressOwner = this;
		}

		internal void UpdateCapture( bool reportedHit = false )
		{
			if ( reportedHit && IsReportedPress )
			{
				current.PressedPath = current.HoveredPath;
				pressed.PressedPath = current.HoveredPath;
			}
			current.Click = MouseReleased && current.Input.IsHovered
				&& !string.IsNullOrEmpty( pressed.PressedPath ) && current.HoveredPath == pressed.PressedPath;

			if ( !current.Input.LeftMouse && !HasReleaseInput )
			{
				cancelled = false;
				current.PressedPath = null;
				if ( pressOwner == this ) pressOwner = null;
			}
			else if ( pressOwner == this && (!MouseReleased || string.IsNullOrEmpty( pressed.PressedPath )) )
			{
				current.HoveredPath = current.PressedPath;
				current.HoveredObject = pressed.HoveredObject;
			}
		}

		internal void ClearPress( bool releasePointer = true )
		{
			if ( releasePointer && pressOwner == this ) pressOwner = null;
			cancelled = current.Input.LeftMouse;
			captureHasShape = false;
			current.PressedPath = null;
			current.Click = false;
			if ( !releasePointer )
			{
				current.HoveredPath = null;
				current.HoveredObject = null;
			}
			pressed = releasePointer ? default : new Frame { Input = pressed.Input };
		}

		readonly record struct PickView( SceneCamera Camera, Vector3 Position, Rotation Rotation, Vector2 Size,
			Rect Rect, float FieldOfView, float OrthoHeight, bool Ortho, float Near, float Far, Matrix? Projection );
		readonly List<bool> _pickVisibility = new();
		PickView _pickView;
		Ray _pickRay;
		int _hitRevision;
		int _pickRevision = -1;
		int _pickSettingsGeneration;
		bool _pickGizmosEnabled;
		bool _containsModelHits;
		Frame _pickResult;
		internal int PickTraceCount { get; private set; }

		void UpdateHitRevision()
		{
			if ( !CollectionsMarshal.AsSpan( hitShapes ).SequenceEqual( CollectionsMarshal.AsSpan( displayedHitShapes ) ) ) _hitRevision++;
		}

		bool CanPick( in HitShape shape )
		{
			if ( !shape.CanInteract ) return false;
			if ( !shape.IsSceneGizmo ) return true;
			return Settings.GizmosEnabled && (shape.Object is not GameObject go || go.IsGizmoVisible)
				&& (shape.Component is not { } c || c.IsValid() && c.Active && !c.Flags.Contains( ComponentFlags.Hidden ) && Settings.IsGizmoEnabled( c.GetType() ));
		}

		bool CanReusePick( in PickView view )
		{
			// Model resources can change their mesh without replacing the managed Model object.
			if ( _containsModelHits || _pickRevision != _hitRevision || _pickView != view || _pickRay != CurrentRay
				|| _pickSettingsGeneration != GizmoTypeGeneration || _pickGizmosEnabled != Settings.GizmosEnabled
				|| _pickVisibility.Count != displayedHitShapes.Count ) return false;
			for ( var i = 0; i < displayedHitShapes.Count; ++i )
				if ( _pickVisibility[i] != CanPick( displayedHitShapes[i] ) ) return false;
			return true;
		}

		void Pick()
		{
			current.HoveredPath = null;
			current.HoveredObject = null;
			current.HitDistance = float.MaxValue;
			if ( !current.Input.IsHovered || current.Input.Camera is not { } camera )
			{
				_pickResult = default;
				_pickRevision = -1;
				return;
			}
			var view = new PickView( camera, camera.Position, camera.Rotation, camera.Size, camera.Rect,
				camera.FieldOfView, camera.OrthoHeight, camera.Ortho, camera.ZNear, camera.ZFar, camera.CustomProjectionMatrix );
			if ( CanReusePick( view ) )
			{
				current.HoveredPath = _pickResult.HoveredPath;
				current.HoveredObject = _pickResult.HoveredObject;
				current.HitDistance = _pickResult.HitDistance;
				return;
			}

			_pickVisibility.Clear();
			_containsModelHits = false;
			using var savedScope = BlankScope();
			var oldLineScope = lineScope;
			var debug = Hitbox.Debug;
			Hitbox.Debug = false;
			IsPicking = true;
			try
			{
				foreach ( var shape in displayedHitShapes )
				{
					var visible = CanPick( shape );
					_pickVisibility.Add( visible );
					_containsModelHits |= shape.Type == HitShapeType.Model;
					if ( !visible ) continue;
					scope.Transform = shape.Transform;
					scope.Path = shape.Path;
					scope.Object = shape.Object;
					scope.HitDepthBias = shape.DepthBias;
					scope.CanInteract = true;
					lineScope.Enabled = shape.Type == HitShapeType.Line;
					lineScope.LinePath = shape.Path;
					shape.Trace();
					PickTraceCount++;
				}
				_pickResult = current;
				_pickView = view;
				_pickRay = CurrentRay;
				_pickRevision = _hitRevision;
				_pickSettingsGeneration = GizmoTypeGeneration;
				_pickGizmosEnabled = Settings.GizmosEnabled;
			}
			finally
			{
				IsPicking = false;
				lineScope = oldLineScope;
				Hitbox.Debug = debug;
			}
		}
	}
}
