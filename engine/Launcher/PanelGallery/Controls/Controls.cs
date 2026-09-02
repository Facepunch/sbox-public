namespace Sandbox.PanelGallery;

/// <summary>
/// Small panel helpers the example builds its UI out of. All ordinary <see cref="Panel"/> code -
/// nothing here knows it's running in an editor.
/// </summary>
public static class Controls
{
	/// <summary>
	/// Material icon glyph. The engine ships the font, so the text is the ligature name.
	/// </summary>
	public static Sandbox.UI.Label Icon( this Panel parent, string icon, string classname = null )
	{
		var label = parent.Add.Label( icon, "icon" );
		if ( classname is not null ) label.AddClass( classname );
		return label;
	}

	/// <summary>
	/// A panel with a click handler and a class.
	/// </summary>
	public static Panel Clickable( this Panel parent, string classname, Action onClick )
	{
		var panel = parent.Add.Panel( classname );
		panel.AddEventListener( "onclick", onClick );
		return panel;
	}

	/// <summary>
	/// Square icon button, like the ones in a toolbar.
	/// </summary>
	public static Panel IconButton( this Panel parent, string icon, Action onClick )
	{
		var button = parent.Clickable( "iconbutton", onClick );
		button.Icon( icon );
		return button;
	}

	/// <summary>
	/// A row of options where one is picked.
	/// </summary>
	public static Panel Segmented( this Panel parent, string[] options, int selected, Action<int> onChange )
	{
		var group = parent.Add.Panel( "segmented" );

		for ( int i = 0; i < options.Length; i++ )
		{
			var index = i;
			var segment = group.Add.Label( options[i], "segment" );
			segment.SetClass( "active", i == selected );
			segment.AddEventListener( "onclick", () =>
			{
				foreach ( var child in group.Children ) child.SetClass( "active", child == segment );
				onChange( index );
			} );
		}

		return group;
	}
}

/// <summary>
/// An on/off switch that slides.
/// </summary>
public class Toggle : Panel
{
	public bool Value { get; private set; }
	public Action<bool> OnChange { get; set; }

	public Toggle( bool value )
	{
		AddClass( "toggle" );
		Add.Panel( "nub" );

		Value = value;
		SetClass( "on", value );

		AddEventListener( "onclick", () =>
		{
			Value = !Value;
			SetClass( "on", Value );
			OnChange?.Invoke( Value );
		} );
	}
}

/// <summary>
/// A square tick box, the way an inspector wants it - a sliding switch is far too loud for a row
/// that appears twenty times.
/// </summary>
public class Checkbox : Panel
{
	public bool Value { get; private set; }
	public Action<bool> OnChange { get; set; }

	readonly Sandbox.UI.Label tick;

	public Checkbox( bool value )
	{
		AddClass( "checkbox" );

		tick = this.Icon( "check" );

		Value = value;
		SetClass( "on", value );

		AddEventListener( "onclick", () =>
		{
			Value = !Value;
			SetClass( "on", Value );
			OnChange?.Invoke( Value );
		} );
	}
}

/// <summary>
/// Drag left and right to change a number. Keeps a mouse capture so the drag survives leaving
/// the panel.
/// </summary>
public class NumberBox : Panel
{
	public float Value { get; private set; }
	public Action<float> OnChange { get; set; }

	readonly Sandbox.UI.Label valueLabel;
	readonly float step;

	float dragStartValue;
	float dragStartX;
	bool dragging;

	public NumberBox( string axis, float value, float step = 0.05f )
	{
		this.step = step;

		AddClass( "numberbox" );

		if ( !string.IsNullOrEmpty( axis ) )
		{
			var label = Add.Label( axis, "axis" );
			label.AddClass( axis.ToLowerInvariant() );
		}

		valueLabel = Add.Label( "", "value" );

		Value = value;
		UpdateText();
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		dragging = true;
		dragStartValue = Value;
		dragStartX = MousePosition.x;
		SetMouseCapture( true );
		SetClass( "dragging", true );
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		dragging = false;
		SetMouseCapture( false );
		SetClass( "dragging", false );
	}

	public override void Tick()
	{
		if ( !dragging ) return;

		Value = dragStartValue + (MousePosition.x - dragStartX) * step;
		UpdateText();
		OnChange?.Invoke( Value );
	}

	void UpdateText() => valueLabel.Text = Value.ToString( "0.###" );
}

/// <summary>
/// A horizontal slider with a readout.
/// </summary>
public class Slider : Panel
{
	public float Value { get; private set; }
	public Action<float> OnChange { get; set; }

	readonly Panel fill;
	readonly Panel knob;
	readonly Sandbox.UI.Label readout;
	readonly float min;
	readonly float max;

	bool dragging;

	public Slider( float min, float max, float value )
	{
		this.min = min;
		this.max = max;

		AddClass( "slider" );

		var row = Add.Panel( "track" );
		fill = row.Add.Panel( "fill" );
		knob = row.Add.Panel( "knob" );
		readout = Add.Label( "", "readout" );

		Value = value;
		UpdateVisuals();
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		dragging = true;
		SetMouseCapture( true );
		DragTo();
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		dragging = false;
		SetMouseCapture( false );
	}

	public override void Tick()
	{
		if ( dragging ) DragTo();
	}

	void DragTo()
	{
		var width = Box.Rect.Width;
		if ( width <= 0 ) return;

		var fraction = MathX.Clamp( MousePosition.x / width, 0, 1 );

		Value = min + (max - min) * fraction;
		UpdateVisuals();
		OnChange?.Invoke( Value );
	}

	void UpdateVisuals()
	{
		var fraction = max > min ? MathX.Clamp( (Value - min) / (max - min), 0, 1 ) : 0;

		fill.Style.Width = Length.Percent( fraction * 100.0f );
		knob.Style.Left = Length.Percent( fraction * 100.0f );
		readout.Text = Value.ToString( "0.00" );
	}
}

/// <summary>
/// A text field. The editing itself - caret, selection, word boundaries - is the engine's, on
/// <see cref="Sandbox.UI.Label"/>; this drives it from the keyboard and the mouse.
/// </summary>
public class TextInput : Panel
{
	public string Value => label.Text ?? "";

	/// <summary>
	/// The text changed.
	/// </summary>
	public Action<string> OnChange { get; set; }

	/// <summary>
	/// Enter was pressed.
	/// </summary>
	public Action OnSubmit { get; set; }

	/// <summary>
	/// A key was pressed before we look at it. Return true to say it's been dealt with.
	/// </summary>
	public Func<string, bool> OnButton { get; set; }

	readonly Sandbox.UI.Label label;
	readonly Sandbox.UI.Label placeholderLabel;
	readonly string placeholder;

	bool dragging;
	RealTimeSince timeSinceFocused;

	/// <summary>
	/// A text field. <paramref name="field"/> styles it as an inspector row rather than a search
	/// box, and starts it with the given text instead of using it as a placeholder.
	/// </summary>
	public TextInput( string text, string icon = null, bool field = false )
	{
		AddClass( field ? "textinput" : "searchbox" );
		AcceptsFocus = true;

		if ( !field ) placeholder = text;

		if ( icon is not null ) this.Icon( icon );

		label = Add.Label( field ? text ?? "" : "", "value" );
		label.Selectable = true;
		label.Multiline = false;

		placeholderLabel = Add.Label( placeholder ?? "", "placeholder" );

		UpdateText();
	}

	/// <summary>
	/// Replace what's in the box, without telling anyone it changed.
	/// </summary>
	public void SetValue( string value )
	{
		label.Text = value ?? "";
		label.SetSelection( 0, 0 );
		label.CaretPosition = label.TextLength;

		UpdateText();
	}

	//
	// Mouse. Click puts the caret where you clicked, drag selects, double click takes the word -
	// the same as any other text field.
	//

	protected override void OnMouseDown( MousePanelEvent e )
	{
		Focus();

		label.SetSelection( 0, 0 );
		label.SetCaretPosition( LetterUnderCursor() );
		label.ScrollToCaret();

		dragging = true;
		SetMouseCapture( true );
	}

	protected override void OnMouseMove( MousePanelEvent e )
	{
		if ( !dragging ) return;

		label.ShouldDrawSelection = true;
		label.SetCaretPosition( LetterUnderCursor(), true );
		label.ScrollToCaret();
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		dragging = false;
		SetMouseCapture( false );
	}

	protected override void OnDoubleClick( MousePanelEvent e )
	{
		label.ShouldDrawSelection = true;
		label.SelectWord( LetterUnderCursor() );
	}

	/// <summary>
	/// Which letter the cursor is over. The label wants a position in window pixels, and in a
	/// window that isn't the game's, the window is the only thing that knows where the cursor is.
	/// </summary>
	int LetterUnderCursor()
	{
		if ( ContextMenu.WindowOf( this ) is not { } window ) return label.TextLength;

		var letter = label.GetLetterAtScreenPosition( window.MousePosition );

		return letter < 0 ? label.TextLength : letter;
	}

	//
	// Keyboard
	//

	public override void OnKeyTyped( char key )
	{
		if ( key < 32 ) return;

		label.ReplaceSelection( key.ToString() );
		Changed();
	}

	public override void OnButtonTyped( ButtonEvent e )
	{
		if ( OnButton is not null && OnButton( e.Button ) ) return;

		if ( e.HasCtrl && HandleCtrl( e ) ) return;
		if ( HandleEditing( e ) ) return;
		if ( HandleMovement( e ) ) return;

		if ( e.Button == "enter" )
		{
			OnSubmit?.Invoke();
			return;
		}

		// Anything we don't use is somebody else's shortcut
		base.OnButtonTyped( e );
	}

	bool HandleCtrl( ButtonEvent e )
	{
		switch ( e.Button )
		{
			case "a":
				label.ShouldDrawSelection = true;
				label.SetSelection( 0, label.TextLength );
				return true;

			case "c":
			case "x":
				var cut = e.Button == "x";
				var copied = label.GetClipboardValue( cut );

				if ( !string.IsNullOrEmpty( copied ) ) EditorUtility.Clipboard.Copy( copied );

				if ( cut )
				{
					label.ReplaceSelection( "" );
					Changed();
				}

				return true;

			case "v":
				var paste = EditorUtility.Clipboard.Paste();
				if ( string.IsNullOrEmpty( paste ) ) return true;

				// One line only - a pasted newline is just text you can't see
				paste = paste.Replace( "\r", "" ).Replace( "\n", " " );

				label.ReplaceSelection( paste );
				Changed();
				return true;
		}

		return false;
	}

	bool HandleEditing( ButtonEvent e )
	{
		if ( e.Button != "backspace" && e.Button != "delete" ) return false;

		if ( label.HasSelection() )
		{
			label.ReplaceSelection( "" );
			Changed();
			return true;
		}

		if ( e.Button == "backspace" )
		{
			if ( label.CaretPosition <= 0 ) return true;

			if ( e.HasCtrl )
			{
				label.MoveToWordBoundaryLeft( true );
				label.ReplaceSelection( "" );
			}
			else
			{
				label.MoveCaretPos( -1 );
				label.RemoveText( label.CaretPosition, 1 );
			}
		}
		else
		{
			if ( label.CaretPosition >= label.TextLength ) return true;

			if ( e.HasCtrl )
			{
				label.MoveToWordBoundaryRight( true );
				label.ReplaceSelection( "" );
			}
			else
			{
				label.RemoveText( label.CaretPosition, 1 );
			}
		}

		Changed();
		return true;
	}

	bool HandleMovement( ButtonEvent e )
	{
		var select = e.HasShift;

		if ( select ) label.ShouldDrawSelection = true;

		switch ( e.Button )
		{
			case "left":
				if ( e.HasCtrl ) label.MoveToWordBoundaryLeft( select );
				else label.MoveCaretPos( -1, select );
				break;

			case "right":
				if ( e.HasCtrl ) label.MoveToWordBoundaryRight( select );
				else label.MoveCaretPos( 1, select );
				break;

			case "home":
				label.MoveToLineStart( select );
				break;

			case "end":
				label.MoveToLineEnd( select );
				break;

			default:
				return false;
		}

		if ( !select ) label.SetSelection( 0, 0 );

		label.ScrollToCaret();
		return true;
	}

	void Changed()
	{
		UpdateText();
		OnChange?.Invoke( Value );
	}

	protected override void OnFocus( PanelEvent e )
	{
		timeSinceFocused = 0;
	}

	public override void Tick()
	{
		SetClass( "focused", HasFocus );
	}

	/// <summary>
	/// The caret, blinking, where the label says it is.
	/// </summary>
	public override void OnDraw()
	{
		label.ShouldDrawSelection = HasFocus && label.HasSelection();

		if ( !HasFocus || label.HasSelection() ) return;

		const float blinkRate = 0.8f;
		var on = (timeSinceFocused * blinkRate) % blinkRate < blinkRate * 0.5f;

		if ( on )
		{
			var caret = label.GetCaretRect( label.CaretPosition );
			caret.Left = MathX.FloorToInt( caret.Left );
			caret.Width = 1;

			Draw.Rect( caret, ComputedStyle?.FontColor ?? Color.White );
		}

		MarkRenderDirty();
	}

	void UpdateText()
	{
		placeholderLabel.Style.Display = string.IsNullOrEmpty( Value ) ? DisplayMode.Flex : DisplayMode.None;
	}
}

/// <summary>
/// A popup that keeps itself inside the window. Absolute panels lay out against their parent, so
/// this waits until it knows how big it is and then clamps its own rect.
/// </summary>
public class Dropdown : Panel
{
	/// <summary>
	/// How close to the window edge it's allowed to get.
	/// </summary>
	public float Edge { get; set; } = 6.0f;

	/// <summary>
	/// Screen rect of whatever opened us. If there's no room below it, we flip above it.
	/// </summary>
	public Rect Anchor { get; set; }

	public Dropdown()
	{
		AddClass( "dropdown" );
	}

	public override void OnLayout( ref Rect layoutRect )
	{
		if ( Parent is null ) return;

		var bounds = Parent.Box.Rect;
		var edge = Edge * ScaleToScreen;
		var position = layoutRect.Position;

		if ( position.x + layoutRect.Width > bounds.Right - edge )
			position.x = bounds.Right - edge - layoutRect.Width;

		if ( position.y + layoutRect.Height > bounds.Bottom - edge )
		{
			var above = Anchor.Top - layoutRect.Height;
			position.y = above > bounds.Top + edge ? above : bounds.Bottom - edge - layoutRect.Height;
		}

		position.x = MathF.Max( position.x, bounds.Left + edge );
		position.y = MathF.Max( position.y, bounds.Top + edge );

		layoutRect.Position = position;
	}
}

/// <summary>
/// Drag to resize the pane next to it. Works on either axis.
/// </summary>
public class Splitter : Panel
{
	readonly float min;
	readonly float max;

	bool dragging;
	float grabOffset;

	/// <summary>
	/// The pane this resizes.
	/// </summary>
	public Panel Target { get; set; }

	/// <summary>
	/// True when the pane is after us, so dragging towards it makes it bigger.
	/// </summary>
	public bool Inverted { get; set; }

	/// <summary>
	/// Drag up and down to resize the pane's height rather than its width.
	/// </summary>
	public bool Vertical { get; set; }

	public Splitter( Panel target, float min, float max, bool vertical = false )
	{
		Target = target;
		Vertical = vertical;
		this.min = min;
		this.max = max;

		AddClass( "splitter" );
		SetClass( "horizontal", vertical );
	}

	float Cursor => Vertical ? MousePosition.y : MousePosition.x;

	protected override void OnMouseDown( MousePanelEvent e )
	{
		dragging = true;
		grabOffset = Cursor;
		SetMouseCapture( true );
		SetClass( "dragging", true );
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		dragging = false;
		SetMouseCapture( false );
		SetClass( "dragging", false );
	}

	public override void Tick()
	{
		if ( !dragging ) return;
		if ( Target is null ) return;

		// We move with the pane, so the cursor drifts back to where it was grabbed - which makes
		// this self correcting rather than something that has to track an absolute position
		var delta = (Cursor - grabOffset) * ScaleFromScreen;
		if ( MathF.Abs( delta ) < 0.5f ) return;

		if ( Inverted ) delta = -delta;

		if ( Vertical )
		{
			var height = Target.Box.Rect.Height * ScaleFromScreen + delta;
			Target.Style.Height = MathX.Clamp( height, min, max );
		}
		else
		{
			var width = Target.Box.Rect.Width * ScaleFromScreen + delta;
			Target.Style.Width = MathX.Clamp( width, min, max );
		}
	}
}
