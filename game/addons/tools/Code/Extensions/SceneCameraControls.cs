namespace Editor;

public static class SceneCameraControls
{
    private static SceneViewportWidget CurrentViewport => SceneViewWidget.Current?.LastSelectedViewportWidget;
    
    // Zustandsvariablen für die verschiedenen Operationen
    private static bool _isOrbiting;
    private static bool _isPanning;
    private static bool _isZooming;
    
    // Orbit Zustand
    private static float _lastOrbitMouseX, _lastOrbitMouseY;
    private static Vector3 _orbitParentPosition;
    private static Vector3 _orbitRelativePosition;
    private static float _orbitStartYaw, _orbitStartPitch;
    private static float _currentYaw, _currentPitch;
    private static float _orbitDistance;
    private static bool _orbitRayHit;
    private static float _lastHitDistance;
    private static Vector3 _orbitMouseDirection;
    private static bool _orbitUseFixedDistance;
    private static bool _orbitInvertHorizontal, _orbitInvertVertical;
    
    // Pan Zustand (aus FirstPersonCamera extrahiert)
    private static bool _hasStoredPanDistance;
    private static float _lastPanDistance;
    private static Vector3 _panStartCameraPosition;
    private static Rotation _panStartCameraRotation;
    private static Vector3 _panHitPointWorld;
    private static Vector2 _panTotalDelta;
    private static Vector2 _panLastMousePos;
    private static bool _panRayHit;
    private static float _panNoHitSpeed;
    
    [Shortcut("Scene.Camera Orbit", "ALT+MOUSE1")]
    public static void OrbitCamera()
    {
        var viewport = CurrentViewport;
        if (viewport?.Renderer?.Camera is not CameraComponent camera) return;
        if (!viewport.Renderer.IsUnderMouse) return;
        
        var leftMouse = Application.MouseButtons.HasFlag(MouseButtons.Left);
        var rightMouse = Application.MouseButtons.HasFlag(MouseButtons.Right);
        
        if (leftMouse || rightMouse)
        {
            if (!_isOrbiting)
            {
                InitializeOrbit(viewport, camera);
            }
            
            if (leftMouse && !camera.Orthographic)
            {
                UpdateOrbitRotation(viewport, camera);
            }
            else if (rightMouse)
            {
                UpdateOrbitZoom(viewport, camera);
            }
        }
        else
        {
            ResetOrbit();
        }
    }
    
    [Shortcut("Scene.Camera Pan", "ALT+MOUSE3")]
    public static void PanCamera()
    {
        var viewport = CurrentViewport;
        if (viewport?.Renderer?.Camera is not CameraComponent camera) return;
        if (!viewport.Renderer.IsUnderMouse) return;
        
        var middleMouse = Application.MouseButtons.HasFlag(MouseButtons.Middle);
        
        if (middleMouse)
        {
            if (!_isPanning)
            {
                InitializePan(viewport, camera);
                _isPanning = true;
            }
            UpdatePan(viewport, camera);
            
            if (EditorPreferences.HidePanCursor)
                viewport.Renderer.Cursor = CursorShape.Blank;
            else
                viewport.Renderer.Cursor = CursorShape.ClosedHand;
        }
        else
        {
            ResetPan();
        }
    }
    
    [Shortcut("Scene.Camera Zoom", "ALT+MOUSE2")]
    public static void ZoomCamera()
    {
        var viewport = CurrentViewport;
        if (viewport?.Renderer?.Camera is not CameraComponent camera) return;
        if (!viewport.Renderer.IsUnderMouse) return;
        
        // Zoom Logik hier - kann ähnlich wie Orbit Zoom oder eigenständig sein
        var delta = Application.MouseWheelDelta.y;
        if (Math.Abs(delta) > 0.001f)
        {
            const float zoomSpeed = 24.0f;
            if (camera.Orthographic)
            {
                var canvasCursor = Application.CursorPosition - viewport.Renderer.ScreenPosition;
                Vector3 worldBefore = camera.ScreenToWorld(canvasCursor);
                camera.OrthographicHeight -= delta * zoomSpeed * 2 * (camera.OrthographicHeight / viewport.Renderer.Height);
                camera.OrthographicHeight = camera.OrthographicHeight.Clamp(32.0f, 8192.0f);
                Vector3 worldAfter = camera.ScreenToWorld(canvasCursor);
                camera.WorldPosition -= worldAfter - worldBefore;
            }
            else
            {
                camera.WorldPosition += camera.WorldRotation.Forward * delta * zoomSpeed;
            }
        }
    }
    
    #region Orbit Implementation
    
    private static void InitializeOrbit(SceneViewportWidget viewport, CameraComponent camera)
    {
        _lastOrbitMouseX = (float)Application.CursorPosition.x;
        _lastOrbitMouseY = (float)Application.CursorPosition.y;
        
        var cursorPos = viewport.Renderer.FromScreen(Application.CursorPosition);
        var ray = camera.ScreenPixelToRay(cursorPos);
        var tr = camera.Scene.Trace.Ray(ray, 10000f)
            .UseRenderMeshes(true)
            .UsePhysicsWorld(false)
            .Run();
        
        if (tr.Hit)
        {
            _orbitHitPointWorld = tr.HitPosition;
            _lastHitDistance = Vector3.DistanceBetween(camera.WorldPosition, _orbitHitPointWorld);
            _orbitUseFixedDistance = false;
        }
        else
        {
            float defaultOrbitDistance = 500.0f;
            _orbitHitPointWorld = camera.WorldPosition + camera.WorldRotation.Forward * defaultOrbitDistance;
            _lastHitDistance = defaultOrbitDistance;
            _orbitUseFixedDistance = true;
            _orbitMouseDirection = ray.Forward;
        }
        
        _orbitParentPosition = _orbitHitPointWorld;
        _orbitRelativePosition = camera.WorldPosition - _orbitParentPosition;
        
        Angles cameraAngles = camera.WorldRotation.Angles();
        _orbitStartYaw = cameraAngles.yaw;
        _orbitStartPitch = cameraAngles.pitch;
        _currentYaw = _orbitStartYaw;
        _currentPitch = _orbitStartPitch;
        
        _orbitInvertHorizontal = false;
        _orbitInvertVertical = Application.KeyboardModifiers.HasFlag(KeyboardModifiers.Shift);
        
        _orbitDistance = _orbitRelativePosition.Length;
        _isOrbiting = true;
    }
    
    private static Vector3 _orbitHitPointWorld;
    
    private static void UpdateOrbitRotation(SceneViewportWidget viewport, CameraComponent camera)
    {
        float currentMouseX = (float)Application.CursorPosition.x;
        float currentMouseY = (float)Application.CursorPosition.y;
        
        float mouseDeltaX = currentMouseX - _lastOrbitMouseX;
        float mouseDeltaY = currentMouseY - _lastOrbitMouseY;
        
        const float orbitSensitivity = 0.2f;
        mouseDeltaX *= orbitSensitivity;
        mouseDeltaY *= orbitSensitivity;
        
        if (_orbitInvertHorizontal) mouseDeltaX = -mouseDeltaX;
        if (_orbitInvertVertical) mouseDeltaY = -mouseDeltaY;
        
        _currentYaw -= mouseDeltaX;
        _currentPitch += mouseDeltaY;
        _currentPitch = Math.Clamp(_currentPitch, -89f, 89f);
        
        Rotation newCameraRotation = Rotation.From(_currentPitch, _currentYaw, 0);
        Rotation startRotation = Rotation.From(_orbitStartPitch, _orbitStartYaw, 0);
        Rotation deltaRotation = newCameraRotation * startRotation.Inverse;
        
        Vector3 newRelativePosition = deltaRotation * _orbitRelativePosition;
        camera.WorldPosition = _orbitParentPosition + newRelativePosition;
        camera.WorldRotation = newCameraRotation;
        
        _lastOrbitMouseX = currentMouseX;
        _lastOrbitMouseY = currentMouseY;
        _orbitDistance = newRelativePosition.Length;
        _lastHitDistance = _orbitDistance;
    }
    
    private static void UpdateOrbitZoom(SceneViewportWidget viewport, CameraComponent camera)
    {
        float currentMouseX = (float)Application.CursorPosition.x;
        float currentMouseY = (float)Application.CursorPosition.y;
        
        float deltaX = currentMouseX - _lastOrbitMouseX;
        float deltaY = currentMouseY - _lastOrbitMouseY;
        
        _lastOrbitMouseX = currentMouseX;
        _lastOrbitMouseY = currentMouseY;
        
        if (SceneEditorExtensions.LockCursorToCanvas(viewport.Renderer))
        {
            deltaX = 0;
            deltaY = 0;
        }
        
        float mouseDelta = Math.Abs(deltaX) > Math.Abs(deltaY) ? deltaX : -deltaY;
        const float zoomSensitivity = 0.005f;
        mouseDelta *= zoomSensitivity;
        
        if (_orbitRayHit)
        {
            float zoomFactor = 1 - mouseDelta;
            zoomFactor = Math.Clamp(zoomFactor, 0.1f, 1.9f);
            _orbitDistance *= zoomFactor;
            Vector3 direction = _orbitRelativePosition.Normal;
            _orbitRelativePosition = direction * _orbitDistance;
            camera.WorldPosition = _orbitParentPosition + _orbitRelativePosition;
            _lastHitDistance = _orbitDistance;
        }
        else
        {
            float referenceDistance = _lastHitDistance > 0 ? _lastHitDistance : 500.0f;
            float baseSpeed = 25000.0f;
            float moveSpeed = baseSpeed * (referenceDistance / 500.0f);
            float moveAmount = mouseDelta * moveSpeed * RealTime.Delta;
            camera.WorldPosition += _orbitMouseDirection * moveAmount;
            _orbitParentPosition = camera.WorldPosition + _orbitMouseDirection * referenceDistance;
            _orbitRelativePosition = camera.WorldPosition - _orbitParentPosition;
            _orbitDistance = _orbitRelativePosition.Length;
        }
    }
    
    private static void ResetOrbit()
    {
        _isOrbiting = false;
        _orbitParentPosition = Vector3.Zero;
        _orbitRelativePosition = Vector3.Zero;
        _orbitRayHit = false;
        _orbitUseFixedDistance = false;
    }
    
    #endregion
    
    #region Pan Implementation (aus FirstPersonCamera extrahiert)
    
    private static void InitializePan(SceneViewportWidget viewport, CameraComponent camera)
    {
        Vector2 currentAbsoluteMousePos = Application.CursorPosition;
        Vector2 currentMousePos = currentAbsoluteMousePos;
        if (viewport.Renderer.IsValid())
        {
            currentMousePos -= viewport.Renderer.ScreenPosition;
        }
        
        var ray = camera.ScreenPixelToRay(currentMousePos);
        var tr = camera.Scene.Trace.Ray(ray, 10000f)
            .UseRenderMeshes(true)
            .UsePhysicsWorld(false)
            .Run();
        
        _panRayHit = tr.Hit;
        
        if (tr.Hit)
        {
            _panHitPointWorld = tr.HitPosition;
            _lastPanDistance = Vector3.Dot(_panHitPointWorld - camera.WorldPosition, camera.WorldRotation.Forward);
            _panNoHitSpeed = 1.0f;
        }
        else
        {
            _panHitPointWorld = Vector3.Zero;
            _lastPanDistance = Vector3.Dot(_panHitPointWorld - camera.WorldPosition, camera.WorldRotation.Forward);
            float minPanDistance = 50f;
            float maxPanDistance = 500f;
            _lastPanDistance = Math.Clamp(_lastPanDistance, minPanDistance, maxPanDistance);
            _panNoHitSpeed = 1f;
        }
        
        _hasStoredPanDistance = true;
        _panStartCameraPosition = camera.WorldPosition;
        _panStartCameraRotation = camera.WorldRotation;
        _panTotalDelta = Vector2.Zero;
        _panLastMousePos = currentAbsoluteMousePos;
    }
    
    private static void UpdatePan(SceneViewportWidget viewport, CameraComponent camera)
    {
        Vector2 currentAbsoluteMousePos = Application.CursorPosition;
        Vector2 mouseDelta = currentAbsoluteMousePos - _panLastMousePos;
        
        bool mouseWrapped = Math.Abs(mouseDelta.x) > 500 || Math.Abs(mouseDelta.y) > 500;
        if (!mouseWrapped)
        {
            _panTotalDelta += mouseDelta;
        }
        
        _panLastMousePos = currentAbsoluteMousePos;
        
        Vector3 cameraToHit = _panHitPointWorld - _panStartCameraPosition;
        Vector3 cameraForward = _panStartCameraRotation.Forward;
        float depthAlongView = Vector3.Dot(cameraToHit, cameraForward);
        
        if (depthAlongView <= 0.1f)
        {
            depthAlongView = (_panStartCameraPosition - _panHitPointWorld).Length;
        }
        
        float hFovRad = camera.FieldOfView * MathF.PI / 180f;
        float visibleWidthAtDepth = 2f * MathF.Tan(hFovRad / 2f) * depthAlongView;
        float panSensitivity = visibleWidthAtDepth / viewport.Renderer.Width;
        float userPanSensitivity = 1.0f;
        
        if (_panRayHit)
        {
            panSensitivity *= userPanSensitivity;
        }
        else
        {
            panSensitivity *= (userPanSensitivity * _panNoHitSpeed);
        }
        
        float moveX = -_panTotalDelta.x * panSensitivity;
        float moveY = _panTotalDelta.y * panSensitivity;
        
        Vector3 cameraRight = _panStartCameraRotation.Right;
        Vector3 cameraUp = _panStartCameraRotation.Up;
        
        camera.WorldPosition = new Vector3(
            _panStartCameraPosition.x + (moveX * cameraRight.x) + (moveY * cameraUp.x),
            _panStartCameraPosition.y + (moveX * cameraRight.y) + (moveY * cameraUp.y),
            _panStartCameraPosition.z + (moveX * cameraRight.z) + (moveY * cameraUp.z)
        );
    }
    
    private static void ResetPan()
    {
        _hasStoredPanDistance = false;
        _lastPanDistance = 0f;
        _panStartCameraPosition = Vector3.Zero;
        _panStartCameraRotation = Rotation.Identity;
        _panHitPointWorld = Vector3.Zero;
        _panTotalDelta = Vector2.Zero;
        _panLastMousePos = Vector2.Zero;
        _isPanning = false;
    }
    
    #endregion
}