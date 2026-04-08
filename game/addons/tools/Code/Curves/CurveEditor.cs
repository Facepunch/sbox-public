using Editor.GraphicsItems;
using Sandbox.Diagnostics;

namespace Editor;

/// <summary>
/// A widget which contains an editable curve
/// </summary>
public class CurveEditor : GraphicsView
{
	GraphicsItems.ChartBackground Background;

	public Vector2 AxisX
	{
		get
		{
			foreach ( var editableCurve in Items.OfType<GraphicsItems.EditableCurve>() )
			{
				if ( editableCurve.TimeRange != Vector2.Zero )
					return editableCurve.TimeRange;
			}
			return Background.RangeX;
		}
	}
	public Vector2 AxisY
	{
		get
		{
			foreach ( var editableCurve in Items.OfType<GraphicsItems.EditableCurve>() )
			{
				if ( editableCurve.ValueRange != Vector2.Zero )
					return editableCurve.ValueRange;
			}
			return Background.RangeY;
		}
	}

	readonly Widget TimeMaxWidget;
	readonly Widget TimeMinWidget;
	readonly Widget ValueMaxWidget;
	readonly Widget ValueMinWidget;
	readonly Widget ButtonsWidget;

	bool MoveRangeWhenPanning = false;
	bool MoveRangeWhenSettingMinMax = false;

	public bool CanEditTimeRange => TimeMinWidget.Enabled && TimeMaxWidget.Enabled;
	public bool CanEditValueRange => ValueMinWidget.Enabled && ValueMaxWidget.Enabled;

	float TimeMin
	{
		get => AxisX.x;
		set
		{
			var axis = AxisX;
			if ( value >= axis.y )
				return;
			var diff = axis.x - value;
			foreach ( var editableCurve in Items.OfType<GraphicsItems.EditableCurve>() )
			{
				var timeRange = editableCurve.TimeRange;
				editableCurve.UpdateTimeRange( new Vector2( timeRange.x - diff, timeRange.y ), MoveRangeWhenSettingMinMax, false );
				editableCurve.UpdateZoomRangeX( new Vector2( editableCurve.ViewportRangeX.x - diff, editableCurve.ViewportRangeX.y ) );
				editableCurve.UpdateHandlePositions();
				UpdateBackgroundFromCurve( editableCurve );
			}
		}
	}

	float TimeMax
	{
		get => AxisX.y;
		set
		{
			var axis = AxisX;
			if ( value <= axis.x )
				return;
			var diff = value - axis.y;
			foreach ( var editableCurve in Items.OfType<GraphicsItems.EditableCurve>() )
			{
				var timeRange = editableCurve.TimeRange;
				editableCurve.UpdateTimeRange( new Vector2( timeRange.x, timeRange.y + diff ), MoveRangeWhenSettingMinMax, false );
				editableCurve.UpdateZoomRangeX( new Vector2( editableCurve.ViewportRangeX.x, editableCurve.ViewportRangeX.y + diff ) );
				editableCurve.UpdateHandlePositions();
				UpdateBackgroundFromCurve( editableCurve );
			}
		}
	}

	float ValueMin
	{
		get => AxisY.x;
		set
		{
			var axis = AxisY;
			if ( value >= axis.y )
				return;
			var diff = axis.x - value;
			foreach ( var editableCurve in Items.OfType<GraphicsItems.EditableCurve>() )
			{
				var valueRange = editableCurve.ValueRange;
				editableCurve.UpdateValueRange( new Vector2( valueRange.x - diff, valueRange.y ), MoveRangeWhenSettingMinMax, false );
				editableCurve.UpdateZoomRangeY( new Vector2( editableCurve.ViewportRangeY.x - diff, editableCurve.ViewportRangeY.y ) );
				editableCurve.UpdateHandlePositions();
				UpdateBackgroundFromCurve( editableCurve );
			}
		}
	}

	float ValueMax
	{
		get => AxisY.y;
		set
		{
			var axis = AxisY;
			if ( value <= axis.x )
				return;
			var diff = value - axis.y;
			foreach ( var editableCurve in Items.OfType<GraphicsItems.EditableCurve>() )
			{
				var valueRange = editableCurve.ValueRange;
				editableCurve.UpdateValueRange( new Vector2( valueRange.x, valueRange.y + diff ), MoveRangeWhenSettingMinMax, false );
				editableCurve.UpdateZoomRangeY( new Vector2( editableCurve.ViewportRangeY.x, editableCurve.ViewportRangeY.y + diff ) );
				editableCurve.UpdateHandlePositions();
				UpdateBackgroundFromCurve( editableCurve );
			}
		}
	}

	public CurveEditor( Widget parent ) : base( parent )
	{
		SceneRect = new( 0, Size );
		HorizontalScrollbar = ScrollbarMode.Off;
		VerticalScrollbar = ScrollbarMode.Off;
		Scale = 1;
		CenterOn( new Vector2( 100, 10 ) );

		Background = new GraphicsItems.ChartBackground
		{
			Size = SceneRect.Size,
			RangeX = new Vector2( 0, 1 ),
			RangeY = new Vector2( 1, 0 )
		};
		Background.AxisX.Width = 100;
		Add( Background );

		var so = this.GetSerialized();

		TimeMinWidget = new Widget( this );
		TimeMinWidget.Layout = Layout.Row();
		var xMinWidget = TimeMinWidget.Layout.Add( new FloatControlWidget( so.GetProperty( nameof( TimeMin ) ) ) );
		xMinWidget.Label = null;
		xMinWidget.Icon = "first_page";
		xMinWidget.HighlightColor = Theme.Red;

		TimeMaxWidget = new Widget( this );
		TimeMaxWidget.Layout = Layout.Row();
		var xMaxWidget = TimeMaxWidget.Layout.Add( new FloatControlWidget( so.GetProperty( nameof( TimeMax ) ) ) );
		xMaxWidget.Label = null;
		xMaxWidget.Icon = "last_page";
		xMaxWidget.HighlightColor = Theme.Red;

		ValueMinWidget = new Widget( this );
		ValueMinWidget.Layout = Layout.Row();
		var yMinWidget = ValueMinWidget.Layout.Add( new FloatControlWidget( so.GetProperty( nameof( ValueMin ) ) ) );
		yMinWidget.Label = null;
		yMinWidget.Icon = "vertical_align_bottom";

		ValueMaxWidget = new Widget( this );
		ValueMaxWidget.Layout = Layout.Row();
		var yMaxWidget = ValueMaxWidget.Layout.Add( new FloatControlWidget( so.GetProperty( nameof( ValueMax ) ) ) );
		yMaxWidget.Label = null;
		yMaxWidget.Icon = "vertical_align_top";

		ButtonsWidget = new Widget( this );
		ButtonsWidget.Layout = Layout.Row();
		ButtonsWidget.Layout.Spacing = 4;
		var btnPanTool = ButtonsWidget.Layout.Add( new IconButton( "pan_tool" ) );
		btnPanTool.ToolTip = "Change Range when Panning/Zooming";
		btnPanTool.OnClick = () =>
		{
			MoveRangeWhenPanning = !MoveRangeWhenPanning;
			btnPanTool.Background = MoveRangeWhenPanning ? Theme.Highlight : Theme.ButtonBackground;
		};
		var btnChangeRange = ButtonsWidget.Layout.Add( new IconButton( "height" ) );
		btnChangeRange.ToolTip = "Retain Values when changing Min/Max";
		btnChangeRange.OnClick = () =>
		{
			MoveRangeWhenSettingMinMax = !MoveRangeWhenSettingMinMax;
			btnChangeRange.Background = MoveRangeWhenSettingMinMax ? Theme.Highlight : Theme.ButtonBackground;
		};
		var btnFitToScreen = ButtonsWidget.Layout.Add( new IconButton( "settings_overscan" ) );
		btnFitToScreen.ToolTip = "Fit to Screen";
		btnFitToScreen.OnClick = () =>
		{
			foreach ( var editableCurve in Items.OfType<GraphicsItems.EditableCurve>() )
			{
				editableCurve.FitViewportToCurve();
				editableCurve.UpdateHandlePositions();
				UpdateBackgroundFromCurve( editableCurve );
			}
		};
	}

	protected override void OnMouseWheel( WheelEvent e )
	{
		base.OnMouseWheel( e );

		if ( e.Delta != 0 )
		{
			var mult = MathF.Sign( e.Delta ) * 0.1f;
			foreach ( var editableCurve in Items.OfType<GraphicsItems.EditableCurve>() )
			{
				if ( e.HasCtrl || (!e.HasCtrl && !e.HasAlt && !e.HasShift) )
				{
					if ( MoveRangeWhenPanning && CanEditTimeRange )
					{
						var timeRange = editableCurve.TimeRange;
						var tLen = MathF.Abs( timeRange.x - timeRange.y );
						editableCurve.UpdateTimeRange( new Vector2(
							timeRange.x + (tLen * mult),
							timeRange.y - (tLen * mult)
						), false, false );
					}
					var rangeX = editableCurve.ViewportRangeX;
					var xLen = MathF.Abs( rangeX.x - rangeX.y );
					editableCurve.UpdateZoomRangeX( new Vector2(
						rangeX.x + (xLen * mult),
						rangeX.y - (xLen * mult)
					) );
				}
				if ( e.HasShift || (!e.HasCtrl && !e.HasAlt && !e.HasShift) )
				{
					if ( MoveRangeWhenPanning && CanEditValueRange )
					{
						var valueRange = editableCurve.ValueRange;
						var vLen = MathF.Abs( valueRange.x - valueRange.y );
						editableCurve.UpdateValueRange( new Vector2(
							valueRange.x + (vLen * mult),
							valueRange.y - (vLen * mult)
						), false, false );
					}
					var rangeY = editableCurve.ViewportRangeY;
					var yLen = MathF.Abs( rangeY.x - rangeY.y );
					editableCurve.UpdateZoomRangeY( new Vector2(
						rangeY.x + (yLen * mult),
						rangeY.y - (yLen * mult)
					) );
				}
				editableCurve.Update();
				foreach ( var handle in editableCurve.Handles )
				{
					handle.UpdatePositionFromValue();
				}
				UpdateBackgroundFromCurve( editableCurve );
			}
			e.Accept();
		}
	}

	bool isDraggingBackground = false;
	Vector2 dragStartPosition;
	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress( e );

		if ( e.MiddleMouseButton )
		{
			isDraggingBackground = true;
			dragStartPosition = e.LocalPosition;
		}
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		base.OnMouseReleased( e );
		if ( isDraggingBackground )
		{
			isDraggingBackground = false;
			return;
		}
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		base.OnMouseMove( e );

		if ( isDraggingBackground )
		{
			var delta = e.LocalPosition - dragStartPosition;
			//delta *= new Vector2( Background.RangeX.Length, Background.RangeY.Length ) * 1.33f;
			dragStartPosition = e.LocalPosition;

			foreach ( var editableCurve in Items.OfType<GraphicsItems.EditableCurve>() )
			{
				var rangeX = editableCurve.ViewportRangeX;
				var rangeY = editableCurve.ViewportRangeY;
				var xLen = MathF.Abs( rangeX.x - rangeX.y );
				var yLen = MathF.Abs( rangeY.x - rangeY.y );
				// Remap the delta based on the current viewport size
				var _deltaX = delta.x / SceneRect.Size.x * xLen;
				var _deltaY = delta.y / SceneRect.Size.y * yLen;

				if ( MoveRangeWhenPanning )
				{
					var timeRange = editableCurve.TimeRange;
					var valueRange = editableCurve.ValueRange;
					if ( CanEditTimeRange )
					{
						editableCurve.UpdateTimeRange( new Vector2( timeRange.x - _deltaX, timeRange.y - _deltaX ), false, false );
					}
					if ( CanEditValueRange )
					{
						editableCurve.UpdateValueRange( new Vector2( valueRange.x + _deltaY, valueRange.y + _deltaY ), false, false );
					}
				}

				editableCurve.UpdateZoomRangeX( new Vector2( rangeX.x - _deltaX, rangeX.y - _deltaX ) );
				editableCurve.UpdateZoomRangeY( new Vector2( rangeY.x + _deltaY, rangeY.y + _deltaY ) );
				editableCurve.UpdateHandlePositions();
				UpdateBackgroundFromCurve( editableCurve );
			}
		}
	}

	protected override void OnResize()
	{
		base.OnResize();

		foreach ( var editableCurve in Items.OfType<GraphicsItems.EditableCurve>() )
		{
			editableCurve.UpdateHandlePositions();
			UpdateBackgroundFromCurve( editableCurve );
		}
	}

	protected override void DoLayout()
	{
		base.DoLayout();

		SceneRect = new Rect( 0, 0, Width, Height );
		Background.Position = new Vector2( 0, 0 );
		Background.Size = SceneRect.Size - new Vector2( 28, 32 );

		float textHeight = 22;
		float textWidth = Background.AxisX.Width - 20;

		ValueMinWidget.Position = new Vector2( 8, Background.Position.y + Background.Size.y - (textHeight / 2f) - Background.AxisY.Width );
		ValueMinWidget.Size = new Vector2( textWidth - 4, textHeight );

		ValueMaxWidget.Position = new Vector2( 8, 8 + Background.Position.y );
		ValueMaxWidget.Size = new Vector2( textWidth - 4, textHeight );

		TimeMaxWidget.Position = new Vector2( Background.Position.x + Background.Size.x - textWidth, Background.Position.y + Background.Size.y );
		TimeMaxWidget.Size = new Vector2( textWidth, textHeight );

		TimeMinWidget.Position = new Vector2( Background.Position.x + Background.AxisX.Width, Background.Position.y + Background.Size.y );
		TimeMinWidget.Size = new Vector2( textWidth, textHeight );

		ButtonsWidget.Position = new Vector2( Background.Position.x + 8, Background.Position.y + Background.Size.y );

		foreach ( var i in Items )
		{
			//
			// When resizing the curve we save the curve, resize, then set the curve back
			//
			if ( i is GraphicsItems.EditableCurve curve )
			{
				if ( curve.SceneRect == Background.ChartRect ) continue;

				var c = curve.Value;
				curve.SceneRect = Background.ChartRect;
				curve.Value = c;
			}

			if ( i is GraphicsItems.RangePolygon poly )
			{
				if ( poly.SceneRect == Background.ChartRect ) continue;

				poly.SceneRect = Background.ChartRect;
			}
		}
	}

	public void UpdateBackgroundFromCurve( EditableCurve editableCurve )
	{
		if ( editableCurve is null ) return;

		Background.Highlight = new Vector4(
			editableCurve.TimeRange.x,
			editableCurve.ValueRange.x,
			editableCurve.TimeRange.y,
			editableCurve.ValueRange.y
		);
		Background.RangeX = editableCurve.ViewportRangeX;
		Background.RangeY = editableCurve.ViewportRangeY;
		Background.Update();
	}

	public float GetBackgroundIncrementX() => Background.GetCurrentIncrementX();
	public float GetBackgroundIncrementY() => Background.GetCurrentIncrementY();

	internal void AddCurve( Func<Curve> get, Action<Curve> set )
	{
		var curve = new GraphicsItems.EditableCurve( this );
		Add( curve );

		curve.SceneRect = Background.ChartRect;
		curve.Size = Background.ChartRect.Size;
		curve.CanEditTimeRange = CanEditTimeRange;
		curve.CanEditValueRange = CanEditValueRange;
		var c = get();
		curve.Value = c;
		curve.Bind( "Value" ).From( get, set );
		curve.FitViewportToCurve();
	}

	public void UpdateTimeRange( Vector2 r, bool retainValues = false )
	{
		foreach ( var i in Items.OfType<GraphicsItems.EditableCurve>() )
		{
			i.UpdateTimeRange( r, retainValues, false );
			i.UpdateHandlePositions();
			UpdateBackgroundFromCurve( i );
		}
	}

	public void UpdateValueRange( Vector2 r, bool retainValues = false )
	{
		foreach ( var i in Items.OfType<GraphicsItems.EditableCurve>() )
		{
			i.UpdateValueRange( r, retainValues, false );
			i.UpdateHandlePositions();
			UpdateBackgroundFromCurve( i );
		}
	}

	public void ClearHandles()
	{
		Curve c = 0.5f;

		foreach ( var i in Items.OfType<GraphicsItems.EditableCurve>() )
		{
			i.Value = i.Value.WithFrames( c.Frames );

			i.Update();
		}
	}

	/// <summary>
	/// Set this editor to be a range editor
	/// </summary>
	public void SetIsRange()
	{
		var items = Items.OfType<GraphicsItems.EditableCurve>().ToList();
		Assert.True( items.Count() == 2 );

		foreach ( var item in items )
		{
			item.IsPartOfRange = true;
		}

		var e = new GraphicsItems.RangePolygon( items[0], items[1] );
		e.SceneRect = Background.ChartRect;
		Add( e );
	}

	/// <summary>
	/// Set whether or not the user can edit the time range of the curves in this editor.
	/// </summary>
	public void SetCanEditTimeRange( bool canEdit )
	{
		TimeMinWidget.Enabled = canEdit;
		TimeMaxWidget.Enabled = canEdit;
		foreach ( var i in Items.OfType<GraphicsItems.EditableCurve>() )
		{
			i.CanEditTimeRange = canEdit;
		}
	}

	/// <summary>
	/// Set whether or not the user can edit the value range of the curves in this editor.
	/// </summary>
	public void SetCanEditValueRange( bool canEdit )
	{
		ValueMinWidget.Enabled = canEdit;
		ValueMaxWidget.Enabled = canEdit;
		foreach ( var i in Items.OfType<GraphicsItems.EditableCurve>() )
		{
			i.CanEditValueRange = canEdit;
		}
	}


	[WidgetGallery]
	[Title( "Curve Editor" )]
	[Icon( "web" )]
	internal static Widget WidgetGallery()
	{
		var canvas = new CurveEditor( null );

		var a = new Curve( new Curve.Frame( 0.0f, 0.5f ), new Curve.Frame( 0.5f, 1.0f ), new Curve.Frame( 1.0f, 0.8f ) ) { ValueRange = new( 0, 10 ) };
		canvas.AddCurve( () => a, v => a = v );

		var b = new Curve( new Curve.Frame( 0.0f, 0.2f ), new Curve.Frame( 1.0f, 0.8f ) );
		canvas.AddCurve( () => b, v => b = v );

		canvas.SetIsRange();
		canvas.SetCanEditTimeRange( false );

		return canvas;
	}
}
