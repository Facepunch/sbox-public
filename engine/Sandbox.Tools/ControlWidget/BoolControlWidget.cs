namespace Editor;

[CustomEditor( typeof( bool ) )]
public class BoolControlWidget : ControlWidget
{
	public string Icon { get; set; }

	public bool IsChecked => SerializedProperty.As.Bool;

	public override bool SupportsMultiEdit => true;

	public override TextFlag CellAlignment => TextFlag.LeftCenter;

	public BoolControlWidget( SerializedProperty property ) : base( property )
	{
		Tint = Theme.Blue;
		Cursor = CursorShape.Finger;
		MinimumWidth = Theme.RowHeight;
		HorizontalSizeMode = SizeMode.CanShrink; // don't grow this

		if ( property.TryGetAttribute<IconAttribute>( out var icon ) )
		{
			Icon = icon.Value;
			Tint = icon.ForegroundColor ?? Tint;
		}
	}

	public override void StartEditing()
	{
		if ( IsControlDisabled ) return;

		PropertyStartEdit();
		SerializedProperty.As.Bool = !SerializedProperty.As.Bool;
		PropertyFinishEdit();
		SignalValuesChanged();
	}

	protected override Vector2 SizeHint()
	{
		return new Vector2( Theme.RowHeight, Theme.RowHeight );
	}

	protected override Vector2 MinimumSizeHint()
	{
		return new Vector2( Theme.RowHeight, Theme.RowHeight );
	}

	protected override void OnDoubleClick( MouseEvent e )
	{
		// ignore
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( IsControlDisabled ) return;

		PropertyStartEdit();
		e.Accepted = true;
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		if ( IsControlDisabled ) return;

		SerializedProperty.As.Bool = !SerializedProperty.As.Bool;
		e.Accepted = true;
		PropertyFinishEdit();
		SignalValuesChanged();
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;

		var alpha = IsControlDisabled ? 0.5f : 1.0f;

		var rect = LocalRect.Shrink( 2 );

		if ( Icon is not null )
		{
			Paint.SetPen( Tint.WithAlpha( 0.3f ) );
			Paint.DrawIcon( rect, Icon, 13, TextFlag.Center );
		}
		else
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground.Lighten( ReadOnly ? 0.5f : 0 ).WithAlphaMultiplied( alpha ) );
			Paint.DrawRect( rect, 2 );
		}

		if ( SerializedProperty.IsMultipleDifferentValues )
		{
			Paint.SetPen( Tint.WithAlphaMultiplied( alpha ) );
			Paint.DrawIcon( rect, "remove", 13, TextFlag.Center );
		}
		else if ( SerializedProperty.As.Bool )
		{
			//	if ( Icon is null )
			{
				Paint.SetPen( Tint.WithAlpha( 0.3f * alpha ), 1 );
				Paint.SetBrush( Tint.WithAlpha( 0.2f * alpha ) );
				Paint.DrawRect( rect, 2 );
			}

			Paint.SetPen( Tint.WithAlphaMultiplied( alpha ) );
			Paint.DrawIcon( rect, Icon ?? "done", 13, TextFlag.Center );
		}

		if ( IsControlHovered && !ReadOnly )
		{
			Paint.SetPen( Tint.WithAlpha( (IsPressed ? 0.75f : 0.5f) * alpha ), 1 );
			Paint.ClearBrush();
			Paint.DrawRect( rect, 1 );
		}
	}
}
