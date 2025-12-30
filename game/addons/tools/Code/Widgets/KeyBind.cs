namespace Editor;

[CustomEditor( typeof( string ), NamedEditor = "keybind" )]
public class KeyBindControlWidget : ControlWidget
{
	private readonly KeyBind widget;

	public KeyBindControlWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Row();
		widget = Layout.Add( new KeyBind( property.GetValue<string>(), this ) );
		widget.ValueChanged = ( v ) => SerializedProperty.As.String = v;
	}

	protected override void OnValueChanged()
	{
		base.OnValueChanged();

		widget.Value = SerializedProperty.As.String;
	}
}

[CanEdit( "keybind" )]
public class KeyBind : Widget
{
	public string Value { get; set; }
	protected bool IsTrapping { get; set; } = false;

	protected virtual bool AllowModifiers => true;

	public KeyBind( string key = null, Widget parent = null ) : this( parent )
	{
		Value = key;
	}

	public KeyBind( Widget parent ) : base( parent )
	{
		Cursor = CursorShape.Finger;
		FocusMode = FocusMode.Click;
	}

	protected override void OnWheel( WheelEvent e )
	{
		base.OnWheel( e );

		if ( IsTrapping )
		{
			var keyStr = e.Delta > 0 ? "MWHEELUP" : "MWHEELDOWN";
			keyStr = AddModifiers( keyStr, e.KeyboardModifiers );
			SetValue( keyStr );
			e.Accept();
		}
	}

	protected override Vector2 SizeHint()
	{
		return Theme.RowHeight;
	}

	public System.Action<string> ValueChanged;

	protected virtual void OnValueChanged( string key )
	{
		ValueChanged?.Invoke( key );
	}

	protected override void OnKeyPress( KeyEvent e )
	{
		base.OnKeyPress( e );

		if ( !IsTrapping ) return;

		// Cancel with ESC
		if ( e.Key == KeyCode.Escape )
		{
			e.Accepted = true;
			IsTrapping = false;
			Update();

			return;
		}
	}

	string AddModifiers( string keyStr, KeyboardModifiers modifiers )
	{
		if ( !AllowModifiers )
			return keyStr;

		if ( (keyStr.Contains( "CTRL" ) || keyStr.Contains( "ALT" ) || keyStr.Contains( "SHIFT" )) )
			return keyStr;
		if ( modifiers.HasFlag( KeyboardModifiers.Shift ) )
			keyStr = "SHIFT+" + keyStr;
		if ( modifiers.HasFlag( KeyboardModifiers.Alt ) )
			keyStr = "ALT+" + keyStr;
		if ( modifiers.HasFlag( KeyboardModifiers.Ctrl ) )
			keyStr = "CTRL+" + keyStr;
		return keyStr;
	}

	protected override void OnKeyRelease( KeyEvent e )
	{
		base.OnKeyRelease( e );

		if ( !IsTrapping ) return;

		var keyStr = GetKeyName( e );
		keyStr = AddModifiers( keyStr, e.KeyboardModifiers );
		SetValue( keyStr );
	}

	protected virtual string GetKeyName( KeyEvent e )
	{
		// Bit shit, but Qt doesn't support these keys without looking up scan codes.
		switch ( e.NativeScanCode )
		{
			case 285:
				return "RCTRL";
			case 54:
				return "RSHIFT";
		}

		return e.GetButtonCodeName().ToUpperInvariant();
	}

	protected void SetValue( string value )
	{
		Value = value;
		OnValueChanged( Value );
		IsTrapping = false;
		Update();
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		if ( IsTrapping )
		{
			var value = e.Button switch
			{
				MouseButtons.Left => "MOUSE1",
				MouseButtons.Middle => "MOUSE3",
				MouseButtons.Right => "MOUSE2",
				MouseButtons.Back => "MOUSE4",
				MouseButtons.Forward => "MOUSE5",
				_ => "MOUSE1"
			};

			e.Accepted = true;
			SetValue( value );

			return;
		}

		base.OnMouseReleased( e );
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		base.OnMouseClick( e );

		IsTrapping = !IsTrapping;
		e.Accepted = true;
	}

	protected override void OnPaint()
	{
		Paint.ClearBrush();
		Paint.ClearPen();

		Paint.SetBrush( Theme.ControlBackground.Lighten( IsUnderMouse ? 0.3f : 0.0f ) );
		Paint.DrawRect( LocalRect, 2.0f );

		Paint.ClearBrush();
		Paint.ClearPen();

		Paint.SetPen( Theme.TextControl );
		Paint.DrawText( LocalRect, IsTrapping ? "PRESS KEY..." : $"{Value}" );
	}

	protected override bool FocusNext() => IsTrapping;
	protected override bool FocusPrevious() => IsTrapping;
}
