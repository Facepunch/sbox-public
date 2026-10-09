namespace Editor;

public sealed class ScenePositionPicker
{
	private readonly SceneViewportWidget viewport;
	private readonly ColorSampler sampler = new();
	private readonly Func<Vector3, string> previewText;
	private readonly Action<Vector3> onPreview;
	private readonly Action<Vector3> onPicked;
	private readonly Action onCancelled;
	private readonly EditorTool activeTool;
	private readonly bool allowedGameObjectSelection;
	private bool completed;

	public ScenePositionPicker(
		SceneViewportWidget viewport,
		Func<Vector3, string> previewText,
		Action<Vector3> onPreview,
		Action<Vector3> onPicked,
		Action onCancelled
	)
	{
		ArgumentNullException.ThrowIfNull( viewport );
		ArgumentNullException.ThrowIfNull( previewText );
		ArgumentNullException.ThrowIfNull( onPreview );
		ArgumentNullException.ThrowIfNull( onPicked );
		ArgumentNullException.ThrowIfNull( onCancelled );

		this.viewport = viewport;
		this.previewText = previewText;
		this.onPreview = onPreview;
		this.onPicked = onPicked;
		this.onCancelled = onCancelled;

		activeTool = viewport.SceneView.Tools.CurrentTool;
		if ( activeTool is not null )
		{
			allowedGameObjectSelection = activeTool.AllowGameObjectSelection;
			activeTool.AllowGameObjectSelection = false;
		}

		sampler.OnPositionPreview = Preview;
		sampler.OnPositionPicked = Pick;
		sampler.OnPositionPaint = Paint;
		sampler.OnCancelled = Cancel;
		sampler.ShowPositionPicker( () => viewport.ScreenRect );
	}

	public float GetViewDepth( Vector3 position )
	{
		var camera = viewport.Renderer.Camera;
		if ( !camera.IsValid() )
			return 0.0f;

		return Vector3.Dot( position - camera.WorldPosition, camera.WorldRotation.Forward );
	}

	private void Preview( Vector2 screenPosition )
	{
		viewport.GizmoInstance.Input.IsHovered = false;
		viewport.GizmoInstance.Input.LeftMouse = false;

		if ( TryGetTracePosition( screenPosition, out var trace ) )
			onPreview( trace.HitPosition );
	}

	private bool Pick( Vector2 screenPosition )
	{
		if ( !TryGetTracePosition( screenPosition, out var trace ) )
			return false;

		Complete();
		onPicked( trace.HitPosition );
		return true;
	}

	private void Paint( Vector2 screenPosition, Rect screenRect )
	{
		var cursor = screenPosition - screenRect.Position;
		var hasTarget = TryGetTracePosition( screenPosition, out var trace );
		var label = hasTarget ? previewText( trace.HitPosition ) : "No focus target";

		Editor.Paint.Antialiasing = true;
		Editor.Paint.SetPen( Color.White.WithAlpha( 0.9f ), 2 );
		Editor.Paint.ClearBrush();
		Editor.Paint.DrawCircle( cursor, 7 );
		Editor.Paint.DrawLine( cursor - Vector2.Right * 12, cursor - Vector2.Right * 4 );
		Editor.Paint.DrawLine( cursor + Vector2.Right * 4, cursor + Vector2.Right * 12 );
		Editor.Paint.DrawLine( cursor - Vector2.Down * 12, cursor - Vector2.Down * 4 );
		Editor.Paint.DrawLine( cursor + Vector2.Down * 4, cursor + Vector2.Down * 12 );

		var labelRect = new Rect( cursor + new Vector2( 16, 16 ), new Vector2( 120, 28 ) );
		labelRect.Left = labelRect.Left.Clamp( 0, Math.Max( 0, screenRect.Width - labelRect.Width ) );
		labelRect.Top = labelRect.Top.Clamp( 0, Math.Max( 0, screenRect.Height - labelRect.Height ) );

		Editor.Paint.ClearPen();
		Editor.Paint.SetBrush( Theme.SurfaceBackground.WithAlpha( 0.95f ) );
		Editor.Paint.DrawRect( labelRect, 4 );
		Editor.Paint.SetPen( Color.White );
		Editor.Paint.DrawText( labelRect, label, TextFlag.Center );
	}

	private bool TryGetTracePosition( Vector2 screenPosition, out SceneTraceResult result )
	{
		var camera = viewport.Renderer.Camera;
		if ( !camera.IsValid() )
		{
			result = default;
			return false;
		}

		var localPosition = viewport.Renderer.FromScreen( screenPosition );
		var ray = camera.ScreenPixelToRay( localPosition * viewport.DpiScale );
		var rayDepth = camera.ZFar + MathF.Abs( camera.ZNear );
		var trace = viewport.SceneView.Session.Scene.Trace.Ray( ray, rayDepth )
			.UseRenderMeshes( true )
			.UsePhysicsWorld( false )
			.Run();

		if ( trace.Hit )
		{
			result = trace;
			return true;
		}

		var plane = new Plane( Vector3.Up, 0.0f );
		if ( plane.TryTrace( ray, out var point, true, rayDepth ) )
		{
			result = default;
			result.Hit = true;
			result.HitPosition = point;
			return true;
		}

		result = default;
		return false;
	}

	private void Cancel()
	{
		if ( completed )
			return;

		Complete();
		onCancelled();
	}

	private void Complete()
	{
		if ( completed )
			return;

		completed = true;
		viewport.GizmoInstance.Input.IsHovered = false;
		viewport.GizmoInstance.Input.LeftMouse = false;
		_ = RestoreSelection();
	}

	private async Task RestoreSelection()
	{
		await Task.Delay( 100 );

		if ( activeTool is not null )
			activeTool.AllowGameObjectSelection = allowedGameObjectSelection;
	}
}
