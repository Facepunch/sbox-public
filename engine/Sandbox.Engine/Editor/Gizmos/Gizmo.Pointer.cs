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
			else if ( pressOwner == this && !MouseReleased )
			{
				current.HoveredPath = current.PressedPath;
			}
		}

		internal void ClearPress()
		{
			if ( pressOwner == this ) pressOwner = null;
			cancelled = current.Input.LeftMouse;
			captureHasShape = false;
			current.PressedPath = null;
			pressed = default;
		}

		void Pick()
		{
			current.HoveredPath = null;
			current.HitDistance = float.MaxValue;
			if ( !current.Input.IsHovered || current.Input.Camera is null ) return;

			using var savedScope = BlankScope();
			var oldLineScope = lineScope;
			var debug = Hitbox.Debug;
			Hitbox.Debug = false;
			IsPicking = true;
			try
			{
				foreach ( var shape in displayedHitShapes )
				{
					if ( !shape.CanInteract ) continue;
					scope.Transform = shape.Transform;
					scope.Path = shape.Path;
					scope.HitDepthBias = shape.DepthBias;
					scope.CanInteract = true;
					lineScope.Enabled = shape.Type == HitShapeType.Line;
					lineScope.LinePath = shape.Path;
					shape.Trace();
				}
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
