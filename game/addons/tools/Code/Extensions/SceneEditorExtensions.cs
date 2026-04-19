namespace Editor;

public static class SceneEditorExtensions
{
	private static readonly Pixmap EyeCursor = Pixmap.FromFile( "toolimages:scene_view/cursor_eye.png" );

	public static void UpdateInputs( this Gizmo.Instance self, SceneCamera camera, Widget canvas = null, bool hasMouseFocus = true )
	{
		// Original aus OLD - unverändert
		ArgumentNullException.ThrowIfNull( camera );
		self.Input.Camera = camera;
		self.Input.Modifiers = Application.KeyboardModifiers;
		if ( !hasMouseFocus )
		{
			self.Input.CursorRay = new Ray();
			return;
		}
		self.Input.CursorPosition = Application.CursorPosition;
		self.Input.LeftMouse = Application.MouseButtons.HasFlag( MouseButtons.Left );
		self.Input.RightMouse = Application.MouseButtons.HasFlag( MouseButtons.Right );
		if ( canvas.IsValid() )
		{
			self.Input.CursorPosition -= canvas.ScreenPosition;
			self.Input.CursorRay = camera.GetRay( self.Input.CursorPosition, canvas.Size );
			if ( !self.Input.IsHovered )
			{
				self.Input.LeftMouse = false;
				self.Input.RightMouse = false;
			}
		}
	}

	public static bool LockCursorToCanvas( Widget canvas, int margin = 16 )
	{
		// Original aus OLD - unverändert
		var rect = canvas.LocalRect.Shrink( margin );
		var pos = canvas.FromScreen( Application.CursorPosition );
		if ( rect.IsInside( pos ) )
			return false;
		var newPos = new Vector2(
			pos.x < rect.Left ? rect.Right : pos.x > rect.Right ? rect.Left : pos.x,
			pos.y < rect.Top ? rect.Bottom : pos.y > rect.Bottom ? rect.Top : pos.y );
		Application.UnscaledCursorPosition += (newPos - pos) * canvas.DpiScale;
		return true;
	}

	private static float RoundToNearest( float value, float step )
	{
		return (float)Math.Round( value / step ) * step;
	}

	/// <summary>
	/// Takes care of the lookAround, wasd/wasd+shift and movement speed logic for the flying camera.
	/// This is basically the same code but I removed the panning and unified it with OrbitCamera - Fabian F.
	/// </summary>
	public static bool FirstPersonCamera( this Gizmo.Instance self, CameraComponent camera, Widget canvas, bool lockCursor = false )
	{
		ArgumentNullException.ThrowIfNull( camera );
		ArgumentNullException.ThrowIfNull( canvas );

		var cameraTarget = self.GetValue<Vector3?>( "CameraTarget" );
		var cameraVelocity = self.GetValue<Vector3>( "CameraVelocity" );

		bool moved = false;

		var rightMouse = Application.MouseButtons.HasFlag( MouseButtons.Right );
		var middleMouse = Application.MouseButtons.HasFlag( MouseButtons.Middle );

		if ( ((rightMouse && !camera.Orthographic) || middleMouse) && self.Input.IsHovered )
		{
			EditorShortcuts.AllowShortcuts = false;
			canvas.Focus();
			var delta = Application.CursorDelta * 0.1f;
			if ( lockCursor && LockCursorToCanvas( canvas ) )
				delta = Vector2.Zero;
			if ( self.ControlMode != "firstperson" )
			{
				delta = 0;
				self.ControlMode = "firstperson";
				self.StompCursorPosition( Application.CursorPosition );
			}
			var moveSpeed = EditorPreferences.CameraSpeed;
			if ( EditorShortcuts.IsDown( "scene.movement-quick" ) ) moveSpeed *= 8.0f;
			if ( EditorShortcuts.IsDown( "scene.movement-slow" ) ) moveSpeed /= 8.0f;

			// RIGHT MOUSE - Rotation
			if ( rightMouse && !camera.Orthographic )
			{
				if ( Application.MouseWheelDelta.y != 0.0f )
				{
					var currentSpeed = EditorPreferences.CameraSpeed;
					var adjustment = (currentSpeed < 5.0f) ? 0.25f :
									(currentSpeed < 20.0f) ? 1.0f :
									RoundToNearest( currentSpeed * 0.1f, 2.5f );
					currentSpeed += adjustment * Math.Sign( Application.MouseWheelDelta.y );
					currentSpeed = Math.Clamp( currentSpeed, 0.25f, 100.0f );
					EditorPreferences.CameraSpeed = currentSpeed;
					SceneViewWidget.Current?.LastSelectedViewportWidget?.timeSinceCameraSpeedChange = 0;
				}
				var sens = EditorPreferences.CameraSensitivity;
				var angles = camera.WorldRotation.Angles();
				angles.roll = 0;
				angles.yaw -= delta.x * sens;
				angles.pitch += delta.y * sens;
				angles.pitch = angles.pitch.Clamp( -89, 89 );
				if ( !delta.IsNearZeroLength )
					camera.WorldRotation = angles;
				if ( EditorPreferences.HideRotateCursor )
					canvas.Cursor = CursorShape.Blank;
				else
					canvas.PixmapCursor = EyeCursor;
			}

			var move = Vector3.Zero;
			if ( EditorShortcuts.IsDown( "scene.move-forward" ) ) move += camera.WorldRotation.Forward;
			if ( EditorShortcuts.IsDown( "scene.move-backward" ) ) move += camera.WorldRotation.Backward;
			if ( EditorShortcuts.IsDown( "scene.move-left" ) ) move += camera.WorldRotation.Left;
			if ( EditorShortcuts.IsDown( "scene.move-right" ) ) move += camera.WorldRotation.Right;
			if ( EditorShortcuts.IsDown( "scene.move-down" ) ) move += Vector3.Down;
			if ( EditorShortcuts.IsDown( "scene.move-up" ) ) move += Vector3.Up;
			if ( !move.IsNearZeroLength )
			{
				move = move.Normal;
				cameraTarget ??= camera.WorldPosition;
				cameraTarget += move * RealTime.Delta * 100.0f * moveSpeed;
			}
			moved = true;
		}
		else
		{
			canvas.Cursor = CursorShape.None;
			if ( self.ControlMode != "mouse" )
			{
				self.ControlMode = "mouse";
			}
		}
		if ( self.Input.IsHovered && !rightMouse && Math.Abs( Application.MouseWheelDelta.y ) > 0.001f )
		{
			float zoomSpeedSet = 24.0f;
			float zoomSpeed = zoomSpeedSet * EditorPreferences.ScrollZoomSpeed;
			if ( camera.Orthographic )
			{
				var canvasCursor = Application.CursorPosition - canvas.ScreenPosition;
				Vector3 worldBefore = camera.ScreenToWorld( canvasCursor );
				camera.OrthographicHeight -= Application.MouseWheelDelta.y * zoomSpeed * 2 * (camera.OrthographicHeight / canvas.Height);
				camera.OrthographicHeight = camera.OrthographicHeight.Clamp( 32.0f, 8192.0f );
				Vector3 worldAfter = camera.ScreenToWorld( canvasCursor );
				camera.WorldPosition -= worldAfter - worldBefore;
			}
			else
			{
				camera.WorldPosition += camera.WorldRotation.Forward * Application.MouseWheelDelta.y * zoomSpeed;
			}
			cameraTarget = default;
		}
		if ( cameraTarget.HasValue )
		{
			Vector3 vel = cameraVelocity;
			camera.WorldPosition = Vector3.SmoothDamp( camera.WorldPosition, cameraTarget.Value, ref vel, EditorPreferences.CameraMovementSmoothing.Clamp( 0.0f, 1.0f ), RealTime.Delta );
			cameraVelocity = vel;
			if ( camera.WorldPosition.AlmostEqual( cameraTarget.Value, 0.01f ) )
			{
				camera.WorldPosition = cameraTarget.Value;
				cameraTarget = default;
				cameraVelocity = default;
			}
		}
		self.SetValue( "CameraTarget", cameraTarget );
		self.SetValue( "CameraVelocity", cameraVelocity );

		return moved;
	}

	/// <summary>
	/// Takes care of the Cameras' Navigation within the Viewport (orbit, pan, zoom)
	/// Did a lot of hacks to have the Navigation Style work with the shortscuts within SceneViewportWidget.cs - Fabian F.
	/// </summary>
	public static bool OrbitCamera( this Gizmo.Instance self, CameraComponent camera, Widget canvas, ref float distance )
	{

		var preset = EditorPreferences.NavigationStyle;

		//Mouse
		bool leftMouse = Application.MouseButtons.HasFlag( MouseButtons.Left );
		bool rightMouse = Application.MouseButtons.HasFlag( MouseButtons.Right );
		bool middleMouse = Application.MouseButtons.HasFlag( MouseButtons.Middle );
		//Keyboard
		bool shiftDown = Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Shift );
		bool ctrlDown = Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Ctrl ) || Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Ctrl );
		bool altDown = Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Alt );

		bool orbitActive = false;
		bool panActive = false;
		bool zoomActive = false;



		switch ( preset )
		{
			case EditorPreferences.NavigationStyleList.Blender:
				if ( !camera.Orthographic )
				{
					orbitActive = middleMouse && !shiftDown && !ctrlDown;
				}
				panActive = middleMouse && shiftDown;
				zoomActive = middleMouse && ctrlDown;
				break;

			case EditorPreferences.NavigationStyleList.Cinema4D:
			case EditorPreferences.NavigationStyleList.Maya:
				if ( !camera.Orthographic )
				{
					orbitActive = altDown && leftMouse;
				}
				panActive = altDown && middleMouse;
				zoomActive = altDown && rightMouse;
				break;
		}

		if ( !(orbitActive || panActive || zoomActive) || !self.Input.IsHovered )
		{
			if ( self.GetValue<bool>( "IsOrbiting" ) ||
				 self.GetValue<bool>( "HasStoredPanDistance" ) ||
				 self.GetValue<bool>( "IsZooming" ) )
			{
				// --- PAN RESET ---
				self.SetValue( "LastPanDistance", 0f );
				self.SetValue( "PanStartCameraPosition", Vector3.Zero );
				self.SetValue( "PanStartCameraRotation", Rotation.Identity );
				self.SetValue( "PanHitPointWorld", Vector3.Zero );
				self.SetValue( "HasStoredPanDistance", false );
				self.SetValue( "PanLastMousePos", Vector2.Zero );
				self.SetValue( "PanTotalDelta", Vector2.Zero );

				// --- ORBIT RESET ---
				self.SetValue( "IsOrbiting", false );
				self.SetValue( "OrbitParentPosition", Vector3.Zero );
				self.SetValue( "OrbitRelativePosition", Vector3.Zero );

				// --- ZOOM RESET ---
				self.SetValue( "IsZooming", false );
				self.SetValue( "OrbitRayHit", false );
				self.SetValue( "LastHitDistance", 0f );
				self.SetValue( "OrbitDistance", 0f );
				self.SetValue( "OrbitMouseDirection", Vector3.Zero );

				// --- General ---
				self.SetValue( "LastAction", "" );
				canvas.Cursor = CursorShape.None;
				if ( self.ControlMode != "mouse" )
					self.ControlMode = "mouse";
				self.SetValue<Vector3?>( "CameraTarget", null );
				self.SetValue( "CameraVelocity", Vector3.Zero );
			}
			return false;
		}


		EditorShortcuts.AllowShortcuts = false;

		canvas.Focus();

		bool moved = false;

		// ===== ORBIT =====
		if ( orbitActive && !camera.Orthographic )
		{
			bool isOrbiting = self.GetValue<bool>( "IsOrbiting" );

			if ( !isOrbiting )
			{
				self.SetValue( "LastOrbitMouseX", (float)Application.CursorPosition.x );
				self.SetValue( "LastOrbitMouseY", (float)Application.CursorPosition.y );

				var cursorPos = self.Input.CursorPosition;

				var ray = camera.ScreenPixelToRay( cursorPos );
				var tr = camera.Scene.Trace.Ray( ray, 10000f )
					.UseRenderMeshes( true )
					.UsePhysicsWorld( false )
					.Run();


				Vector3 hitPoint;
				bool rayHit = tr.Hit;

				self.SetValue( "OrbitRayHit", rayHit );

				if ( rayHit )
				{
					hitPoint = tr.HitPosition;

					float hitDist = Vector3.DistanceBetween( camera.WorldPosition, hitPoint );

					self.SetValue( "LastHitDistance", hitDist );

				}
				else
				{

					float defaultOrbitDistance = distance > 0 ? distance : 500.0f;

					hitPoint = camera.WorldPosition + camera.WorldRotation.Forward * defaultOrbitDistance;

					self.SetValue( "LastHitDistance", defaultOrbitDistance );
					self.SetValue( "OrbitMouseDirection", ray.Forward );

				}

				Vector3 newOrbitParentPos = hitPoint;
				Vector3 newOrbitRelativePos = camera.WorldPosition - newOrbitParentPos;

				Angles cameraAngles = camera.WorldRotation.Angles();


				self.SetValue( "OrbitParentPosition", newOrbitParentPos );
				self.SetValue( "OrbitRelativePosition", newOrbitRelativePos );

				self.SetValue( "OrbitStartYaw", cameraAngles.yaw );
				self.SetValue( "OrbitStartPitch", cameraAngles.pitch );

				self.SetValue( "CurrentYaw", cameraAngles.yaw );
				self.SetValue( "CurrentPitch", cameraAngles.pitch );

				self.SetValue( "IsOrbiting", true );

				self.SetValue( "LastAction", "orbit" );
			}

			float OrbitSensitivity = 0.2f * EditorPreferences.OrbitSensitivity;

			float mouseDeltaX;
			float mouseDeltaY;
			float lastMouseX = self.GetValue<float>( "LastOrbitMouseX" );
			float lastMouseY = self.GetValue<float>( "LastOrbitMouseY" );
			float currentMouseX = (float)Application.CursorPosition.x;
			float currentMouseY = (float)Application.CursorPosition.y;

			mouseDeltaX = (currentMouseX - lastMouseX) * OrbitSensitivity;
			mouseDeltaY = (currentMouseY - lastMouseY) * OrbitSensitivity;

			float currentYaw = self.GetValue<float>( "CurrentYaw" );
			float currentPitch = self.GetValue<float>( "CurrentPitch" );


			if ( EditorPreferences.OrbitInvertHorizontal == true )
			{
				currentYaw -= mouseDeltaX;
			}
			else
			{
				currentYaw += mouseDeltaX;
			}

			if ( EditorPreferences.OrbitInvertVertical == true )
			{
				currentPitch += mouseDeltaY;
			}
			else
			{
				currentPitch -= mouseDeltaY;
			}

			currentPitch = Math.Clamp( currentPitch, -89f, 89f );

			self.SetValue( "CurrentYaw", currentYaw );
			self.SetValue( "CurrentPitch", currentPitch );


			Rotation newCameraRotation = Rotation.From( currentPitch, currentYaw, 0 );

			Vector3 orbitRelativePos = self.GetValue<Vector3>( "OrbitRelativePosition" );
			Vector3 orbitParentPos = self.GetValue<Vector3>( "OrbitParentPosition" );

			float startYaw = self.GetValue<float>( "OrbitStartYaw" );
			float startPitch = self.GetValue<float>( "OrbitStartPitch" );


			Rotation startRotation = Rotation.From( startPitch, startYaw, 0 );
			Rotation deltaRotation = newCameraRotation * startRotation.Inverse;

			Vector3 newRelativePosition = deltaRotation * orbitRelativePos;

			camera.WorldPosition = orbitParentPos + newRelativePosition;
			camera.WorldRotation = newCameraRotation;

			self.SetValue( "LastOrbitMouseX", currentMouseX );
			self.SetValue( "LastOrbitMouseY", currentMouseY );


			float newDistance = newRelativePosition.Length;

			distance = newDistance;
			self.SetValue( "LastHitDistance", newDistance );

			if ( EditorPreferences.CameraCursor )

				canvas.Cursor = CursorShape.Blank;

			else

				canvas.PixmapCursor = EyeCursor;

			moved = true;
		}

		else if ( panActive )
		{
			float lastPanDistance = self.GetValue<float>( "LastPanDistance" );
			bool hasStoredDistance = self.GetValue<bool>( "HasStoredPanDistance" );

			Vector2 currentAbsoluteMousePos = Application.CursorPosition;
			Vector2 currentMousePos = currentAbsoluteMousePos;

			if ( canvas.IsValid() )
			{
				currentMousePos -= canvas.ScreenPosition;
			}

			if ( !hasStoredDistance )
			{
				var ray = camera.ScreenPixelToRay( currentMousePos );
				var tr = camera.Scene.Trace.Ray( ray, 10000f )
					.UseRenderMeshes( true )
					.UsePhysicsWorld( false )
					.Run();

				Vector3 hitPointWorld;
				bool rayHit = tr.Hit;

				self.SetValue( "PanRayHit", rayHit );

				if ( rayHit )
				{
					hitPointWorld = tr.HitPosition;
					lastPanDistance = Vector3.Dot( hitPointWorld - camera.WorldPosition, camera.WorldRotation.Forward );
					self.SetValue( "PanNoHitSpeed", 1.0f );
				}
				else
				{
					// NEU: Wenn kein Hit, verwende die aktuelle Orbit-Distanz als Referenz
					float orbitDistance = distance > 0 ? distance : 500.0f;

					// Berechne eine sinnvolle Pan-Ebene in Blickrichtung
					hitPointWorld = camera.WorldPosition + camera.WorldRotation.Forward * orbitDistance;
					lastPanDistance = orbitDistance;
					self.SetValue( "PanNoHitSpeed", 1.0f );
				}

				hasStoredDistance = true;

				self.SetValue( "LastPanDistance", lastPanDistance );
				self.SetValue( "HasStoredPanDistance", hasStoredDistance );

				self.SetValue( "PanStartCameraPosition", camera.WorldPosition );
				self.SetValue( "PanStartCameraRotation", camera.WorldRotation );

				self.SetValue( "PanHitPointWorld", hitPointWorld );
				self.SetValue( "PanTotalDelta", Vector2.Zero );
				self.SetValue( "PanLastMousePos", currentAbsoluteMousePos );

				self.SetValue( "LastAction", "pan" );
			}

			Vector3 storedHitPoint = self.GetValue<Vector3>( "PanHitPointWorld" );
			Vector3 startCameraPos = self.GetValue<Vector3>( "PanStartCameraPosition" );
			Rotation startCameraRot = self.GetValue<Rotation>( "PanStartCameraRotation" );

			Vector2 totalDelta = self.GetValue<Vector2>( "PanTotalDelta" );
			Vector2 lastMousePos = self.GetValue<Vector2>( "PanLastMousePos" );
			Vector2 mouseDelta = currentAbsoluteMousePos - lastMousePos;

			bool mouseWrapped = Math.Abs( mouseDelta.x ) > 500 || Math.Abs( mouseDelta.y ) > 500;

			if ( !mouseWrapped )
			{
				totalDelta += mouseDelta;

				self.SetValue( "PanTotalDelta", totalDelta );

			}

			self.SetValue( "PanLastMousePos", currentAbsoluteMousePos );

			Vector3 cameraToHit = storedHitPoint - startCameraPos;
			Vector3 cameraForward = startCameraRot.Forward;

			float depthAlongView = Vector3.Dot( cameraToHit, cameraForward );
			if ( depthAlongView <= 0.1f )
			{
				depthAlongView = (startCameraPos - storedHitPoint).Length;
			}

			float panSensitivity = 0;
			float userPanSensitivity = 0;

			if ( !camera.Orthographic )
			{
				float hFovRad = camera.FieldOfView * MathF.PI / 180f;
				float visibleWidthAtDepth = 2f * MathF.Tan( hFovRad / 2f ) * depthAlongView;
				panSensitivity = visibleWidthAtDepth / canvas.Width;
				userPanSensitivity = EditorPreferences.PanSensitivity;
			}
			else
			{
				float aspect = canvas.Width / (float)canvas.Height;
				float visibleWidthAtDepth = camera.OrthographicHeight * aspect;
				panSensitivity = visibleWidthAtDepth / canvas.Width;
				userPanSensitivity = EditorPreferences.PanSensitivity;
			}

			bool panRayHit = self.GetValue<bool>( "PanRayHit" );
			float noHitSpeed = self.GetValue<float>( "PanNoHitSpeed" );

			if ( panRayHit )
				panSensitivity *= userPanSensitivity;
			else
				panSensitivity *= userPanSensitivity * noHitSpeed;


			float moveX;
			float moveY;


			if ( EditorPreferences.PanInvertHorizontal == true )

				moveX = totalDelta.x * panSensitivity;

			else

				moveX = -totalDelta.x * panSensitivity;


			if ( EditorPreferences.PanInvertVertical == true )

				moveY = -totalDelta.y * panSensitivity;

			else

				moveY = totalDelta.y * panSensitivity;







			Vector3 cameraRight = startCameraRot.Right;
			Vector3 cameraUp = startCameraRot.Up;

			camera.WorldPosition = new Vector3(

				startCameraPos.x + (moveX * cameraRight.x) + (moveY * cameraUp.x),
				startCameraPos.y + (moveX * cameraRight.y) + (moveY * cameraUp.y),
				startCameraPos.z + (moveX * cameraRight.z) + (moveY * cameraUp.z)

			);

			if ( EditorPreferences.CameraCursor )

				canvas.Cursor = CursorShape.Blank;

			else

				canvas.Cursor = CursorShape.ClosedHand;

			moved = true;
		}

		// ===== ZOOM =====
		else if ( zoomActive )
		{
			bool isZooming = self.GetValue<bool>( "IsZooming" );
			if ( !isZooming )
			{
				self.SetValue( "LastOrbitMouseX", (float)Application.CursorPosition.x );
				self.SetValue( "LastOrbitMouseY", (float)Application.CursorPosition.y );

				var cursorPos = self.Input.CursorPosition;

				Vector3 hitPoint;

				if ( camera.Orthographic )
				{
					float currentZoomLevel = camera.OrthographicHeight;
					hitPoint = camera.WorldPosition + camera.WorldRotation.Forward * currentZoomLevel;

					self.SetValue( "LastHitDistance", currentZoomLevel );
					self.SetValue( "OrbitParentPosition", hitPoint );
					self.SetValue( "OrbitRelativePosition", camera.WorldPosition - hitPoint );
					self.SetValue( "OrbitRayHit", false );
				}
				else
				{
					var ray = camera.ScreenPixelToRay( cursorPos );
					var tr = camera.Scene.Trace.Ray( ray, 10000f )
						.UseRenderMeshes( true )
						.UsePhysicsWorld( false )
						.Run();

					bool zoomRayHit = tr.Hit;
					self.SetValue( "OrbitRayHit", zoomRayHit );

					if ( zoomRayHit )
					{
						hitPoint = tr.HitPosition;
						float hitDist = Vector3.DistanceBetween( camera.WorldPosition, hitPoint );
						self.SetValue( "LastHitDistance", hitDist );
						self.SetValue( "OrbitParentPosition", hitPoint );
						self.SetValue( "OrbitRelativePosition", camera.WorldPosition - hitPoint );
					}
					else
					{
						float defaultDistance = distance > 0 ? distance : 500.0f;
						hitPoint = camera.WorldPosition + camera.WorldRotation.Forward * defaultDistance;
						self.SetValue( "LastHitDistance", defaultDistance );
						self.SetValue( "OrbitParentPosition", hitPoint );
						self.SetValue( "OrbitRelativePosition", camera.WorldPosition - hitPoint );
						self.SetValue( "OrbitMouseDirection", ray.Forward );
					}
				}

				self.SetValue( "IsZooming", true );
				self.SetValue( "LastAction", "" );
			}

			float lastMouseX = self.GetValue<float>( "LastOrbitMouseX" );
			float lastMouseY = self.GetValue<float>( "LastOrbitMouseY" );

			float currentMouseX = (float)Application.CursorPosition.x;
			float currentMouseY = (float)Application.CursorPosition.y;

			float deltaX = currentMouseX - lastMouseX;
			float deltaY = currentMouseY - lastMouseY;

			self.SetValue( "LastOrbitMouseX", currentMouseX );
			self.SetValue( "LastOrbitMouseY", currentMouseY );


			float mouseDelta = Math.Abs( deltaX ) > Math.Abs( deltaY ) ? deltaX : -deltaY;
			float zoomSensitivity = 0.004f * EditorPreferences.ZoomSensitivity;
			mouseDelta = mouseDelta * zoomSensitivity;

			if ( camera.Orthographic )
			{
				float zoomFactor = 1 - mouseDelta;
				zoomFactor = Math.Clamp( zoomFactor, 0.1f, 1.9f );

				float newHeight = camera.OrthographicHeight * zoomFactor;
				newHeight = Math.Clamp( newHeight, 32f, 8192f );

				var cursorPos = self.Input.CursorPosition;
				Vector3 worldBefore = camera.ScreenToWorld( cursorPos );
				camera.OrthographicHeight = newHeight;
				Vector3 worldAfter = camera.ScreenToWorld( cursorPos );
				camera.WorldPosition -= worldAfter - worldBefore;

				distance = newHeight;

				moved = true;
			}
			else
			{
				Vector3 parentPosition = self.GetValue<Vector3>( "OrbitParentPosition" );
				Vector3 relativePosition = self.GetValue<Vector3>( "OrbitRelativePosition" );
				bool zoomRayHitStored = self.GetValue<bool>( "OrbitRayHit" );
				string lastAction = self.GetValue<string>( "LastAction" );
				bool wasOrbitOrPan = (lastAction == "orbit" || lastAction == "pan");

				if ( zoomRayHitStored )
				{
					float currentDist = relativePosition.Length;
					float zoomFactor = 0;

					if ( EditorPreferences.ZoomInvert == true )
					{
						zoomFactor = 1 + mouseDelta;
					}
					else
					{
						zoomFactor = 1 - mouseDelta;
					}

					zoomFactor = Math.Clamp( zoomFactor, 0.1f, 1.9f );
					currentDist = currentDist * zoomFactor;
					Vector3 direction = relativePosition.Normal;
					Vector3 newRelativePosition = direction * currentDist;
					Vector3 newPos = parentPosition + newRelativePosition;

					camera.WorldPosition = newPos;

					self.SetValue( "OrbitRelativePosition", newRelativePosition );
					self.SetValue( "OrbitDistance", currentDist );
					self.SetValue( "LastHitDistance", currentDist );

					distance = currentDist;
				}
				else
				{
					float referenceDistance;

					if ( wasOrbitOrPan )
					{
						referenceDistance = self.GetValue<float>( "LastHitDistance" );
						if ( referenceDistance <= 0 ) referenceDistance = 500.0f;
					}
					else
					{
						referenceDistance = 500.0f;
					}

					float baseSpeed = 25000.0f;
					float moveSpeed = baseSpeed * (referenceDistance / 500.0f);
					float moveAmount = mouseDelta * moveSpeed * RealTime.Delta;

					Vector3 savedMouseDirection = self.GetValue<Vector3>( "OrbitMouseDirection" );
					camera.WorldPosition += savedMouseDirection * moveAmount;

					Vector3 newParentPosition = camera.WorldPosition + savedMouseDirection * referenceDistance;
					self.SetValue( "OrbitParentPosition", newParentPosition );
					Vector3 newRelativePosition = camera.WorldPosition - newParentPosition;

					self.SetValue( "OrbitRelativePosition", newRelativePosition );
					self.SetValue( "OrbitDistance", newRelativePosition.Length );

					distance = newRelativePosition.Length;
				}
				moved = true;
			}

			self.SetValue( "LastAction", "zoom" );

			if ( EditorPreferences.CameraCursor )
				canvas.Cursor = CursorShape.Blank;
			else
				canvas.Cursor = CursorShape.SizeBDiag;
		}


		return moved;
	}

	/// <summary>
	/// Stop this bone being procedural
	/// </summary>
	public static void BreakProceduralBone( this GameObject go )
	{
		GameObjectFlags flags = go.Flags;

		if ( !flags.Contains( GameObjectFlags.Bone ) )
			return;

		if ( flags.Contains( GameObjectFlags.ProceduralBone ) )
			return;

		flags |= GameObjectFlags.ProceduralBone;
		go.Flags = flags;
	}

	#region Dispatch Edited

	/// <summary>
	/// Run the <see cref="EditorEvent.ISceneEdited.GameObjectPreEdited"/> or <see cref="EditorEvent.ISceneEdited.ComponentPreEdited"/>
	/// event for the given property.
	/// </summary>
	public static void DispatchPreEdited( this SerializedProperty property )
	{
		if ( property.FindPathInScene() is not { } path ) return;

		foreach ( var target in path.Targets )
		{
			switch ( target )
			{
				case GameObject go:
					DispatchPreEdited( go, path.FullName );
					break;
				case Component cmp:
					DispatchPreEdited( cmp, path.FullName );
					break;
			}
		}
	}

	/// <summary>
	/// Run the <see cref="EditorEvent.ISceneEdited.GameObjectEdited"/> or <see cref="EditorEvent.ISceneEdited.ComponentEdited"/>
	/// event for the given property.
	/// </summary>
	public static void DispatchEdited( this SerializedProperty property )
	{
		if ( property.FindPathInScene() is not { } path ) return;

		foreach ( var target in path.Targets )
		{
			switch ( target )
			{
				case GameObject go:
					DispatchEdited( go, path.FullName );
					break;
				case Component cmp:
					DispatchEdited( cmp, path.FullName );
					break;
			}
		}
	}

	/// <summary>
	/// Run the <see cref="EditorEvent.ISceneEdited.GameObjectPreEdited"/> event for the given property.
	/// </summary>
	public static void DispatchPreEdited( this GameObject go, string propertyName ) =>
		EditorEvent.RunInterface<EditorEvent.ISceneEdited>( i => i.GameObjectPreEdited( go, propertyName ) );

	/// <summary>
	/// Run the <see cref="EditorEvent.ISceneEdited.ComponentPreEdited"/> event for the given property.
	/// </summary>
	public static void DispatchPreEdited( this Component cmp, string propertyName ) =>
		EditorEvent.RunInterface<EditorEvent.ISceneEdited>( i => i.ComponentPreEdited( cmp, propertyName ) );

	/// <summary>
	/// Run the <see cref="EditorEvent.ISceneEdited.GameObjectEdited"/> event for the given property.
	/// </summary>
	public static void DispatchEdited( this GameObject go, string propertyName ) =>
		EditorEvent.RunInterface<EditorEvent.ISceneEdited>( i => i.GameObjectEdited( go, propertyName ) );

	/// <summary>
	/// Run the <see cref="EditorEvent.ISceneEdited.ComponentEdited"/> event for the given property.
	/// </summary>
	public static void DispatchEdited( this Component cmp, string propertyName ) =>
		EditorEvent.RunInterface<EditorEvent.ISceneEdited>( i => i.ComponentEdited( cmp, propertyName ) );

	/// <inheritdoc cref="DispatchPreEdited(GameObject, string)"/>
	public static void DispatchPreEdited( this IEnumerable<GameObject> gos, string propertyName )
	{
		foreach ( var go in gos ) go.DispatchPreEdited( propertyName );
	}

	/// <inheritdoc cref="DispatchPreEdited(Component, string)"/>
	public static void DispatchPreEdited( this IEnumerable<Component> cmps, string propertyName )
	{
		foreach ( var cmp in cmps ) cmp.DispatchPreEdited( propertyName );
	}

	/// <inheritdoc cref="DispatchEdited(GameObject, string)"/>
	public static void DispatchEdited( this IEnumerable<GameObject> gos, string propertyName )
	{
		foreach ( var go in gos ) go.DispatchEdited( propertyName );
	}

	/// <inheritdoc cref="DispatchEdited(Component, string)"/>
	public static void DispatchEdited( this IEnumerable<Component> cmps, string propertyName )
	{
		foreach ( var cmp in cmps ) cmp.DispatchEdited( propertyName );
	}

	#endregion
}
