namespace Editor.NodeEditor;

public class IntEditor : ValueEditor
{
	public string Title { get; set; }
	public float Value { get; set; }
	public int Min { get; set; } = 0;
	public int Max { get; set; } = 1;
	
	public NodeUI Node { get; set; }
	private bool IsRange => Min != Max;

	private Vector2 _lastCursorPos;

	public IntEditor( GraphicsItem parent ) : base( parent )
	{
		HoverEvents = true;
		Cursor = CursorShape.Finger;
	}

	protected override void OnPaint()
	{
		if ( !Enabled )
			return;

		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;

		var isRange = IsRange;
		var sliderBg = Theme.Blue.WithAlpha( 0.6f );
		var bg = Theme.ControlBackground.WithAlpha( 0.4f );
		var fg = Theme.TextControl;

		if ( !Paint.HasMouseOver )
		{
			bg = bg.Darken( 0.2f );
			fg = fg.Darken( 0.1f );
			sliderBg = sliderBg.WithAlpha( 0.5f );
		}

		var rect = LocalRect.Shrink( 1 );

		Paint.ClearPen();
		Paint.SetBrush( bg );
		Paint.DrawRect( rect, 2 );

		if ( isRange && Value >= Min )
		{
			Paint.SetBrush( sliderBg.WithAlpha( 0.2f ) );
			Paint.DrawRect( rect.Shrink( 1, 1, (Value <= Max) ? 40 : 1, 1 ), 2 );

			var sliderRect = rect.Shrink( 1, 1, (Value <= Max) ? 40 : 1, 1 );
			sliderRect.Right = sliderRect.Left.LerpTo( sliderRect.Right, (Value - Min) / (Max - Min) );

			Paint.SetBrush( sliderBg );
			Paint.DrawRect( sliderRect, 2 );
		}

		if ( !isRange )
		{
			Paint.SetBrush( Paint.HasPressed ? sliderBg : sliderBg.WithAlpha( 0.2f ) );
			Paint.DrawRect( rect.Shrink( 1 ), 2 );

			if ( Paint.HasMouseOver )
			{
				Paint.SetPen( fg );
				Paint.DrawIcon( rect.Shrink( 1 ), "navigate_before", 10, TextFlag.LeftCenter );
				Paint.DrawIcon( rect.Shrink( 1 ), "navigate_next", 10, TextFlag.RightCenter );
			}
		}

		Paint.SetDefaultFont();
		Paint.SetPen( fg );

		var shrink = isRange ? 4 : 10;

		if ( !string.IsNullOrWhiteSpace( Title ) )
		{
			Paint.DrawText( rect.Shrink( shrink, 0, shrink, 0 ), Title, TextFlag.LeftCenter );
		}

		Paint.DrawText( rect.Shrink( shrink, 0, shrink, 0 ), $"{Value:0}", TextFlag.RightCenter );
	}

	protected override void OnMousePressed( GraphicsMouseEvent e )
	{
		base.OnMousePressed( e );

		if ( !Enabled )
			return;

		if ( !e.LeftMouseButton )
			return;

		if ( IsRange )
		{
			UpdateValue( e.LocalPosition.x );
		}
		else
		{
			_lastCursorPos = Application.UnscaledCursorPosition;
		}

		e.Accepted = true;
	}

	protected override void OnMouseReleased( GraphicsMouseEvent e )
	{
		base.OnMouseReleased( e );

		Cursor = IsRange ? CursorShape.Finger : CursorShape.SizeH;

		Update();
	}

	protected override void OnHoverEnter( GraphicsHoverEvent e )
	{
		base.OnHoverEnter( e );

		Cursor = IsRange ? CursorShape.Finger : CursorShape.SizeH;
	}

	protected override void OnMouseMove( GraphicsMouseEvent e )
	{
		base.OnMouseMove( e );

		if ( !Enabled )
			return;

		if ( !e.LeftMouseButton )
			return;

		if ( IsRange )
		{
			Cursor = CursorShape.Finger;
			UpdateValue( e.LocalPosition.x );
		}
		else
		{
			Cursor = CursorShape.Blank;

			var cursorPos = Application.UnscaledCursorPosition;
			var delta = cursorPos - _lastCursorPos;
			Application.UnscaledCursorPosition = _lastCursorPos;

			Value += delta.x * 0.01f;
			Node.Graph.ChildValuesChanged( null );
			Node.Update();
		}

		e.Accepted = true;
	}

	private void UpdateValue( float position )
	{
		Value = (position - 1) / (Size.x - 43);
		Value = ((float)Min).LerpTo( Max, Value ).Clamp( Min, Max );
		Value = (float)Math.Round( Value / 1.0f ) * 1.0f;

		Node.Graph.ChildValuesChanged( null );
		Node.Update();
	}
}
