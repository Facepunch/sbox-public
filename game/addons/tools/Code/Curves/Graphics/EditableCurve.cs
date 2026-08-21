namespace Editor.GraphicsItems;

/// <summary>
/// Anatomy of an EditableCurve: https://files.facepunch.com/garry/72bd3a5f-c9e4-40fe-8748-05e782d3a230.png
/// </summary>
public partial class EditableCurve : GraphicsItem
{
	public List<Handle> Handles { get; } = new();
	public Vector2 TimeRange => _curve.TimeRange;
	public Vector2 ValueRange => _curve.ValueRange;
	public Color CurveColor { get; set; } = Theme.Green;
	public Color SelectionColor { get; set; } = Color.White;
	public float CurveThickness { get; set; } = 1.0f;
	public float HandleDistance { get; set; } = 50.0f;

	public bool IsPartOfRange { get; set; }
	public bool CanEditTimeRange { get; set; } = true;
	public bool CanEditValueRange { get; set; } = true;

	public Vector4 Viewport { get; set; } = Vector4.Zero;
	public Vector2 ViewportRangeX => new Vector2( Viewport.x, Viewport.z );
	public Vector2 ViewportRangeY => new Vector2( Viewport.y, Viewport.w );


	CurveEditor editor;
	internal CurveEditorHistory History => editor?.History;


	Curve _curve;
	public Curve Value
	{
		get { return _curve; }
		set
		{
			_curve = value;
			_curve.Fix();

			FitViewportToCurve();
			DeleteAllHandles();

			foreach ( var k in _curve.Frames )
			{
				var handle = new Handle( this, k );
				Handles.Add( handle );
			}
			editor?.UpdateBackgroundFromCurve( this );

			Update();
		}
	}

	public EditableCurve( CurveEditor parent ) : base( null )
	{
		editor = parent;
		HoverEvents = true;
		Cursor = CursorShape.DragCopy;
		Clip = true;
		//ClipChildren = true;
	}

	protected override void OnPaint()
	{
		PaintCurve();
		PaintHandleExtras();

		base.OnPaint();
	}

	void PaintCurve()
	{
		if ( Handles.Count == 0 )
			return;

		float alpha = 0.8f;

		if ( IsPartOfRange ) alpha *= 0.5f;
		if ( !Hovered ) alpha *= 0.5f;

		Paint.SetPen( CurveColor.WithAlpha( alpha ), CurveThickness, PenStyle.Solid );

		var viewportCurve = GetViewportAdjustedCurve();
		viewportCurve.DrawLine( LocalRect, 3.0f );
	}

	void PaintHandleExtras()
	{
		foreach ( var handle in Handles )
		{
			Paint.ResetTransform();
			Paint.Translate( Position );
			Paint.Translate( handle.Position );

			handle.PaintExtras();
		}
		Paint.ResetTransform();
	}

	public void DeleteAllHandles()
	{
		foreach ( var handle in Handles )
		{
			handle?.Destroy();
		}

		Handles.Clear();
		UpdateTooltip();
	}

	public void OnEdited()
	{
		SaveCurve();
		BindSystem.Flush();
	}

	public void OnHandleMoved()
	{
		OnEdited();
		UpdateTooltip();
	}

	public void UpdateZoomRangeX( Vector2 range )
	{
		// Zoom the viewport to the new range
		Viewport = new Vector4(
			range.x, Viewport.y,
			range.y, Viewport.w
		);
	}

	public void UpdateZoomRangeY( Vector2 range )
	{
		// Zoom the viewport to the new range
		Viewport = new Vector4(
			Viewport.x, range.x,
			Viewport.z, range.y
		);
	}

	public void FitViewportToCurve()
	{
		Viewport = new Vector4(
			TimeRange.x, ValueRange.x,
			TimeRange.y, ValueRange.y
		);
	}

	/// <summary>
	/// Write from editor to the curve (_value)
	/// </summary>
	public void SaveCurve()
	{
		if ( Value.Length > 0 )
		{
			var timeRange = new Vector2( float.MaxValue, float.MinValue );
			var valueRange = new Vector2( float.MaxValue, float.MinValue );

			foreach ( var frame in Value.Frames )
			{
				var time = frame.Time.Remap( 0, 1, TimeRange.x, TimeRange.y, false );
				var value = frame.Value.Remap( 0, 1, ValueRange.x, ValueRange.y, false );
				timeRange = timeRange.WithX( System.MathF.Min( timeRange.x, time ) )
										.WithY( System.MathF.Max( timeRange.y, time ) );
				valueRange = valueRange.WithX( System.MathF.Min( valueRange.x, value ) )
										.WithY( System.MathF.Max( valueRange.y, value ) );
			}

			if ( !CanEditTimeRange || IsPartOfRange )
			{
				timeRange = TimeRange;
			}

			if ( !CanEditValueRange || IsPartOfRange )
			{
				valueRange = ValueRange;
			}

			var newCurve = Value.WithFrames( new List<Curve.Frame>() );
			var timeRangeLength = timeRange.y - timeRange.x;
			var valueRangeLength = valueRange.y - valueRange.x;

			foreach ( var handle in Handles.OrderBy( x => x.Position.x ) )
			{
				newCurve.AddPoint( handle.GetKeyFrame() );

				if ( timeRangeLength != 0 )
				{
					handle.Frame.Time = handle.Frame.Time.Remap( 0, 1, MathF.Min( TimeRange.x, TimeRange.y ), MathF.Max( TimeRange.x, TimeRange.y ), false );
					handle.Frame.Time = handle.Frame.Time.Remap( timeRange.x, timeRange.y, 0, 1, false );
				}

				if ( valueRangeLength != 0 )
				{
					handle.Frame.Value = handle.Frame.Value.Remap( 0, 1, MathF.Min( ValueRange.x, ValueRange.y ), MathF.Max( ValueRange.x, ValueRange.y ), false );
					handle.Frame.Value = handle.Frame.Value.Remap( valueRange.x, valueRange.y, 0, 1, false );
				}
			}
			if ( timeRangeLength != 0 )
			{
				newCurve.UpdateTimeRange( timeRange, true );
			}
			if ( valueRangeLength != 0 )
			{
				newCurve.UpdateValueRange( valueRange, true );
			}

			_curve = newCurve;
		}

		editor?.UpdateBackgroundFromCurve( this );
		Update();
	}

	internal Curve GetViewportAdjustedCurve()
	{
		var viewportCurve = Value.WithFrames( Value.Frames );
		viewportCurve.UpdateTimeRange( ViewportRangeX, true );
		viewportCurve.UpdateValueRange( ViewportRangeY, true );
		return viewportCurve;
	}

	internal void Delete( Handle editableCurveHandle )
	{
		// Keep at least one around
		if ( Handles.Count == 1 )
			return;

		using var scope = History?.Push( "Delete Keyframe" );

		if ( Handles.Remove( editableCurveHandle ) )
		{
			editableCurveHandle.Destroy();
			UpdateTooltip();
			SaveCurve();
		}

	}

	internal void FocusedHandleKeyPress( KeyEvent e )
	{
		var selectedHandles = Handles.Where( x => x.Selected ).ToArray();

		if ( e.Key == KeyCode.Delete )
		{
			using var scope = History?.Push( "Delete Keyframes" );

			foreach ( var selected in selectedHandles )
			{
				Delete( selected );
			}
		}
	}

	public void UpdateTimeRange( Vector2 range, bool retainTimes, bool fitViewport = false )
	{
		var oldViewport = Viewport;
		var c = Value;
		c.UpdateTimeRange( range, retainTimes );
		Value = c;
		if ( !fitViewport )
		{
			Viewport = oldViewport;
		}
	}

	public void UpdateValueRange( Vector2 range, bool retainValues, bool fitViewport = false )
	{
		var oldViewport = Viewport;
		var c = Value;
		c.UpdateValueRange( range, retainValues );
		Value = c;
		if ( !fitViewport )
		{
			Viewport = oldViewport;
		}
	}

	public override bool Contains( Vector2 localPos )
	{
		var x = localPos.x / Size.x;

		var curve = GetViewportAdjustedCurve();

		var f = curve.Evaluate( ViewportRangeX.x.LerpTo( ViewportRangeX.y, x ) );
		f = f.LerpInverse( ViewportRangeY.y, ViewportRangeY.x );
		f = f * Size.y;

		var distance = System.MathF.Abs( f - localPos.y );
		return distance < 10.0f;
	}

	protected override void OnMousePressed( GraphicsMouseEvent e )
	{
		base.OnMousePressed( e );

		if ( e.LeftMouseButton )
		{
			using var scope = History?.Push( "Add Keyframe" );

			var curve = GetViewportAdjustedCurve();

			var x = e.LocalPosition.x / Size.x;
			x = x.Remap( 0, 1, ViewportRangeX.x, ViewportRangeX.y, false );
			x = x.Remap( TimeRange.x, TimeRange.y, 0, 1, false );

			var v = Value.EvaluateDelta( x );
			var k = new Curve.Frame( x, v );

			var handle = new Handle( this, k );
			Handles.Add( handle );
			SaveCurve();
		}
	}

	HandlePopup ValuePopup;
	Handle popupTarget;

	public virtual void HandleSelectionChanged()
	{
		UpdateTooltip();
	}

	internal void UpdateHandlePositions()
	{
		Update();
		foreach ( var handle in Handles )
		{
			handle.UpdatePositionFromValue();
		}
	}

	public void UpdateTooltip()
	{
		if ( !GraphicsView.IsValid() )
			return;

		popupTarget = Handles.Where( x => x.Selected || x.Hovered ).OrderBy( x => !x.Hovered ).FirstOrDefault();

		if ( popupTarget.IsValid() && GraphicsView.IsValid() )
		{
			ValuePopup ??= new HandlePopup( GraphicsView );
			ValuePopup.UpdateFrom( popupTarget );
			ValuePopup.ConstrainTo( GraphicsView.LocalRect.Shrink( 8, 2 ) );
			ValuePopup.Visible = true;
		}
		else
		{
			ValuePopup?.Destroy();
			ValuePopup = null;
		}
	}
}
//}
