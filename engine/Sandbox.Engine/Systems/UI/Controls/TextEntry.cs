using Microsoft.AspNetCore.Components;
using Sandbox.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Sandbox.Internal.GlobalGameNamespace;

using Sandbox.Rendering;
using Sandbox.UI.Construct;
using System.Globalization;

namespace Sandbox.UI;

/// <summary>
/// A <see cref="Panel"/> that the user can enter text into.
/// </summary>
[Library( "TextEntry" )]
[CustomEditor( typeof( string ) )]
public partial class TextEntry : BaseControl
{
	public override bool SupportsMultiEdit => true;

	/// <summary>
	/// Called when the text of this text entry is changed.
	/// </summary>
	[Parameter] public Action<string> OnTextEdited { get; set; }

	/// <summary>
	/// The <see cref="Label"/> that contains the text of this text entry.
	/// </summary>
	protected Label Label { get; init; }

	bool _disabled;

	/// <summary>
	/// Is the text entry disabled?
	/// If disabled, will add a "disabled" class and prevent focus.
	/// </summary>
	[Parameter]
	public bool Disabled
	{
		get => _disabled;
		set
		{
			_disabled = value;
			AcceptsFocus = !value;
			SetClass( "disabled", value );
		}
	}

	/// <summary>
	/// The text can be selected and copied, but not changed. Unlike <see cref="Disabled"/> this
	/// still takes focus, so it reads and behaves like text rather than like a dead control.
	/// </summary>
	[Parameter]
	public bool ReadOnly
	{
		get => _readOnly;
		set
		{
			_readOnly = value;
			SetClass( "readonly", value );
		}
	}

	bool _readOnly;

	/// <summary>
	/// Whether the text can be changed at all right now.
	/// </summary>
	protected bool CanEdit => !ReadOnly && !Disabled;

	/// <summary>
	/// Access to the raw text in the text entry.
	/// </summary>
	[Parameter]
	public string Text
	{
		get => Label.Text;
		set => Label.Text = value;
	}

	/// <summary>
	/// The value of the text entry. Returns <see cref="Text"/>, but does special logic when setting text.
	/// </summary>
	[Parameter]
	public string Value
	{
		get => Label.Text;
		set
		{
			// don't change the value
			// when we're editing it
			if ( HasFocus )
				return;

			if ( Label.Text == value ) return;

			Label.Text = value;
			if ( Numeric )
			{
				Label.Text = FixNumeric();
			}

			// Someone else replaced the text, so there's nothing of the user's left to undo
			ClearUndoHistory();
		}
	}

	/// <inheritdoc cref="Label.TextLength"/>
	public int TextLength
	{
		get => Label.TextLength;
	}

	/// <inheritdoc cref="Label.CaretPosition"/>
	public int CaretPosition
	{
		get => Label.CaretPosition;
		set => Label.CaretPosition = value;
	}


	/// <summary>
	/// Whether to allow automatic replacement of emoji codes with their actual unicode emoji characters. See <see cref="Emoji"/>.
	/// </summary>
	public bool AllowEmojiReplace { get; set; } = false;

	/// <summary>
	/// Allow <a href="https://en.wikipedia.org/wiki/Input_method">IME input</a> when this is focused.
	/// </summary>
	public override bool AcceptsImeInput => true;

	/// <summary>
	/// Affects formatting of the text when <see cref="Numeric"/> is enabled. Accepts any format that is supported by <see cref="float.ToString(string?)"/>. <a href="https://learn.microsoft.com/en-us/dotnet/standard/base-types/custom-numeric-format-strings">See examples here</a>.
	/// </summary>
	[Category( "Presentation" )]
	public string NumberFormat { get; set; } = null;

	private bool _multiline;

	/// <summary>
	/// Makes it possible to enter new lines into the text entry. (By pressing the Enter key, which no longer acts as the submit key)
	/// </summary>
	[Property, Parameter]
	public bool Multiline
	{
		get => _multiline;
		set
		{
			if ( _multiline == value ) return;

			_multiline = value;

			// Straight through to the label rather than waiting for a tick - this decides
			// whether the text wraps, and a frame of wrapped text is a frame of wrong layout
			if ( Label.IsValid() ) Label.Multiline = value;
			SetClass( "is-multiline", value );
		}
	}

	/// <summary>
	/// If we're numeric, this is the lowest numeric value allowed
	/// </summary>
	public float? MinValue { get; set; }

	/// <summary>
	/// If we're numeric, this is the highest numeric value allowed
	/// </summary>
	public float? MaxValue { get; set; }

	/// <summary>
	/// Text to display when the text entry is empty. Typically a very short description of the expected contents or function of the text entry.
	/// </summary>
	[Parameter]
	public string Placeholder { get; set; }

	/// <summary>
	/// The <see cref="Label"/> that shows <see cref="Prefix"/> text.
	/// </summary>
	public Label PrefixLabel { get; protected set; }

	/// <summary>
	/// If set, will display given text before the text entry box.
	/// </summary>
	public string Prefix
	{
		get => PrefixLabel?.Text;
		set
		{
			if ( string.IsNullOrWhiteSpace( value ) )
			{
				PrefixLabel?.Delete();
				SetClass( "has-prefix", false );
				return;
			}

			PrefixLabel ??= Add.Label( value, "prefix-label" );
			PrefixLabel.Text = value;

			SetClass( "has-prefix", PrefixLabel.IsValid() );
		}
	}

	/// <summary>
	/// The <see cref="Label"/> that shows <see cref="Suffix"/> text.
	/// </summary>
	public Label SuffixLabel { get; protected set; }

	/// <summary>
	/// If set, will display given text after the text entry box.
	/// </summary>
	public string Suffix
	{
		get => SuffixLabel?.Text;
		set
		{
			if ( string.IsNullOrWhiteSpace( value ) )
			{
				SuffixLabel?.Delete();
				SetClass( "has-suffix", false );
				return;
			}

			SuffixLabel ??= Add.Label( value, "suffix-label" );
			SuffixLabel.Text = value;

			SetClass( "has-suffix", SuffixLabel.IsValid() );
		}
	}

	/// <summary>
	/// The color used for text selection highlight. Defaults to cyan with transparency.
	/// </summary>
	[Category( "Appearance" ), Parameter]
	public Color SelectionColor
	{
		get => Label?.SelectionColor ?? Color.Cyan.WithAlpha( 0.39f );
		set
		{
			if ( Label is not null )
				Label.SelectionColor = value;
		}
	}

	public TextEntry()
	{
		AcceptsFocus = true;
		AddClass( "textentry" );

		// Dragging selects text, it never scrolls
		CanDragScroll = false;

		Label = Add.Label( "", "content-label" );
		Label.Tokenize = false;
		Label.Style.WhiteSpace = WhiteSpace.Pre;
		Label.Multiline = _multiline;
	}

	public override void OnPaste( string text )
	{
		if ( !CanEdit ) return;

		RecordEdit( EditKind.Single );

		SetImePreview( "" );

		if ( Label.HasSelection() )
		{
			Label.ReplaceSelection( "" );
		}

		var pasteResult = new string( text.Where( CanEnterCharacter ).ToArray() );
		ReplaceEmojisInText( ref pasteResult );

		if ( MaxLength.HasValue )
		{
			// MaxLength and TextLength count text elements, not chars - truncate the
			// same way so we don't cut a surrogate pair or combining sequence in half
			var remaining = Math.Max( MaxLength.Value - TextLength, 0 );
			var info = new StringInfo( pasteResult );
			if ( info.LengthInTextElements > remaining )
			{
				pasteResult = remaining > 0 ? info.SubstringByTextElements( 0, remaining ) : "";
			}
		}

		Text ??= "";
		Label.InsertText( pasteResult, CaretPosition );

		// Caret positions count text elements, not chars - an emoji is one step, not two
		Label.MoveCaretPos( new StringInfo( pasteResult ).LengthInTextElements );

		OnValueChanged();
	}

	public override string GetClipboardValue( bool cut )
	{
		var value = Label.GetClipboardValue( cut );

		if ( cut && CanEdit )
		{
			RecordEdit( EditKind.Single );

			Label.ReplaceSelection( "" );
			OnValueChanged();
		}

		return value;
	}
	public override void OnButtonEvent( ButtonEvent e )
	{
		// dont' send to parent
		e.StopPropagation = true;
	}

	protected override void OnEscape( PanelEvent e )
	{
		Cancel();
		e.StopPropagation();
	}


	public override void OnButtonTyped( ButtonEvent e )
	{
		e.StopPropagation = true;

		//Log.Info( $"OnButtonTyped {button}" );
		var button = e.Button;

		if ( Label.HasSelection() && (button == "delete" || button == "backspace") && CanEdit )
		{
			RecordEdit( EditKind.Single );

			Label.ReplaceSelection( "" );
			OnValueChanged();

			return;
		}

		if ( button == "delete" )
		{
			if ( CaretPosition < TextLength && CanEdit )
			{
				RecordEdit( EditKind.Deleting );

				if ( e.HasCtrl )
				{
					Label.MoveToWordBoundaryRight( true );
					Label.ReplaceSelection( string.Empty );
					OnValueChanged();
					return;
				}

				Label.RemoveText( CaretPosition, 1 );
				OnValueChanged();
			}

			return;
		}

		if ( button == "backspace" )
		{
			if ( CaretPosition > 0 && CanEdit )
			{
				RecordEdit( EditKind.Deleting );

				if ( e.HasCtrl )
				{
					Label.MoveToWordBoundaryLeft( true );
					Label.ReplaceSelection( string.Empty );
					OnValueChanged();
					return;
				}

				Label.MoveCaretPos( -1 );
				Label.RemoveText( CaretPosition, 1 );
				OnValueChanged();
			}

			return;
		}

		if ( button == "z" && e.HasCtrl )
		{
			if ( e.HasShift ) Redo(); else Undo();
			return;
		}

		if ( button == "y" && e.HasCtrl )
		{
			Redo();
			return;
		}

		if ( button == "a" && e.HasCtrl )
		{
			Label.SetSelection( 0, TextLength );

			// The caret goes to the end of what got selected, so typing replaces it and an
			// arrow key carries on from there
			CaretPosition = TextLength;
			return;
		}

		if ( button == "home" )
		{
			if ( !e.HasCtrl )
			{
				Label.MoveToLineStart( e.HasShift );
			}
			else
			{
				Label.SetCaretPosition( 0, e.HasShift );
			}
			return;
		}

		if ( button == "end" )
		{
			if ( !e.HasCtrl )
			{
				Label.MoveToLineEnd( e.HasShift );
			}
			else
			{
				Label.SetCaretPosition( TextLength, e.HasShift );
			}
			return;
		}

		if ( button == "left" )
		{
			if ( !e.HasCtrl )
			{
				// A plain arrow collapses the selection to its edge - with shift held it keeps extending
				if ( Label.HasSelection() && !e.HasShift )
					Label.SetCaretPosition( Math.Min( Label.SelectionStart, Label.SelectionEnd ) );
				else
					Label.MoveCaretPos( -1, e.HasShift );
			}
			else
			{
				Label.MoveToWordBoundaryLeft( e.HasShift );
			}
			return;
		}

		if ( button == "right" )
		{
			if ( !e.HasCtrl )
			{
				if ( Label.HasSelection() && !e.HasShift )
					Label.SetCaretPosition( Math.Max( Label.SelectionStart, Label.SelectionEnd ) );
				else
					Label.MoveCaretPos( 1, e.HasShift );
			}
			else
			{
				Label.MoveToWordBoundaryRight( e.HasShift );
			}
			return;
		}

		if ( button == "down" || button == "up" )
		{
			if ( AutoCompletePanel.IsValid() )
			{
				AutoCompletePanel.MoveSelection( button == "up" ? -1 : 1 );
				AutoCompleteSelectionChanged();
				return;
			}

			//
			// We have history items, autocomplete using those
			//
			if ( string.IsNullOrEmpty( Text ) && !AutoCompletePanel.IsValid() && _history.Count > 0 )
			{
				UpdateAutoComplete( _history.ToArray() );

				// select last item
				AutoCompletePanel.MoveSelection( -1 );
				AutoCompleteSelectionChanged();

				return;
			}

			Label.MoveCaretLine( button == "up" ? -1 : 1, e.HasShift );
			return;
		}

		if ( button == "enter" || button == "pad_enter" )
		{
			if ( Multiline )
			{
				OnKeyTyped( '\n' );
				return;
			}

			if ( AutoCompletePanel.IsValid() && AutoCompletePanel.SelectedChild.IsValid() )
			{
				DestroyAutoComplete();
			}

			Blur();
			CreateEvent( "onsubmit", Text );
			return;
		}

		if ( button == "escape" )
		{
			Cancel();
			return;
		}

		if ( button == "tab" )
		{
			if ( AutoCompletePanel.IsValid() )
			{
				AutoCompletePanel.MoveSelection( e.HasShift ? -1 : 1 );
				AutoCompleteSelectionChanged();
				return;
			}
		}

		base.OnButtonTyped( e );
	}

	void Cancel()
	{
		if ( AutoCompletePanel.IsValid() )
		{
			AutoCompleteCancel();
			return;
		}

		Blur();
		CreateEvent( "oncancel" );
	}

	bool IsPressOnSelection()
	{
		if ( !Label.HasSelection() ) return false;

		var letter = Label.GetLetterAtScreenPosition( ScreenMousePosition );
		if ( letter < 0 ) return false;

		var start = Math.Min( Label.SelectionStart, Label.SelectionEnd );
		var end = Math.Max( Label.SelectionStart, Label.SelectionEnd );

		return letter >= start && letter < end;
	}

	// This press landed on the selection, so moving away from it carries the text out rather
	// than starting a new selection. Where it went down, to measure that move against.
	bool _pressedOnSelection;
	Vector2 _pressPosition;

	/// <summary>
	/// Pressing on selected text and moving away carries it out of the entry, the way it does
	/// anywhere else - so a selection can be dropped in another entry, or another app.
	/// </summary>
	void DragSelectionOut()
	{
		var selected = Label.GetClipboardValue( false );

		_pressedOnSelection = false;
		if ( string.IsNullOrEmpty( selected ) ) return;

		var drag = new Drag( this );
		drag.SetText( selected );

		// A move means it was taken somewhere else, so it leaves here
		if ( drag.Start() == DropAction.Move && !Disabled )
		{
			Label.ReplaceSelection( "" );
			OnValueChanged();
		}
	}

	protected override void OnDrop( PanelEvent e )
	{
		if ( e is not DropEvent drop ) return;
		if ( string.IsNullOrEmpty( drop.Text ) ) return;
		if ( !CanEdit ) return;

		drop.Action = DropAction.Copy;
		drop.StopPropagation();

		if ( !drop.IsDrop ) return;

		Focus();

		// The text lands where it was dropped, not wherever the caret happened to be left
		var letter = Label.GetLetterAtScreenPosition( drop.Position );
		if ( letter >= 0 ) Label.SetCaretPosition( letter );

		OnPaste( drop.Text );
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		// Stopping a press on the scrollbar here would stop its drag from starting
		if ( ScrollBar.Owns( e.Target ) ) return;

		e.StopPropagation();

		// Shift extends what's already selected instead of starting again, so a click picks
		// the far end of the selection and keeps the anchor where it was
		if ( e.HasShift && !string.IsNullOrEmpty( Text ) )
		{
			var to = Label.GetLetterAtScreenPosition( ScreenMousePosition );
			if ( to < 0 ) return;

			// Without a selection to grow, the caret is the anchor
			var anchor = Label.HasSelection() ? Label.SelectionStart : CaretPosition;

			Label.SelectionStart = anchor;
			Label.SelectionEnd = to;
			Label.CaretPosition = to;

			Label.ScrollToCaret();
			return;
		}

		// Pressing on a selection might be the start of carrying it out - the selection has
		// to survive the press for that, so this one leaves it alone
		_pressedOnSelection = IsPressOnSelection();
		_pressPosition = ScreenMousePosition;

		if ( _pressedOnSelection )
			return;

		if ( string.IsNullOrEmpty( Text ) )
			return;

		var pos = Label.GetLetterAtScreenPosition( ScreenMousePosition );

		Label.SelectionStart = 0;
		Label.SelectionEnd = 0;

		if ( pos >= 0 )
		{
			Label.SetCaretPosition( pos );
		}

		Label.ScrollToCaret();

	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		if ( ScrollBar.Owns( e.Target ) ) return;

		SelectingWords = false;

		// Released on the selection without having dragged it anywhere - that's a plain click,
		// so it collapses the selection and places the caret
		if ( _pressedOnSelection )
		{
			_pressedOnSelection = false;

			var letter = Label.GetLetterAtScreenPosition( ScreenMousePosition );

			Label.SelectionStart = 0;
			Label.SelectionEnd = 0;

			if ( letter >= 0 ) Label.SetCaretPosition( letter );

			Label.ScrollToCaret();
			e.StopPropagation();
			return;
		}

		// A drag already put the caret at the selection's focus end - only a plain click
		// places it here
		if ( !Label.HasSelection() )
		{
			var pos = Label.GetLetterAtScreenPosition( ScreenMousePosition );
			if ( pos >= 0 )
				Label.SetCaretPosition( pos );
		}

		Label.ScrollToCaret();
		e.StopPropagation();
	}

	protected override void OnMouseMove( MousePanelEvent e )
	{
		base.OnMouseMove( e );

		if ( ScrollBar.Owns( e.Target ) ) return;

		e.StopPropagation();

		// Far enough from a press that grabbed the selection - that's a drag, not a click
		if ( _pressedOnSelection && (ScreenMousePosition - _pressPosition).Length > 5.0f )
		{
			DragSelectionOut();
		}
	}

	protected override void OnFocus( PanelEvent e )
	{
		UpdateAutoComplete();
		TimeSinceCaretMoved = 0;
	}

	protected override void OnBlur( PanelEvent e )
	{
		//UpdateAutoComplete();

		if ( Numeric )
		{
			Text = FixNumeric();
		}
	}

	/// <summary>
	/// A third click takes the whole line - which is everything, when there's only one line.
	/// </summary>
	protected override void OnTripleClick( MousePanelEvent e )
	{
		if ( string.IsNullOrEmpty( Text ) ) return;
		if ( e.Button != "mouseleft" ) return;

		var letter = Label.GetLetterAtScreenPosition( ScreenMousePosition );
		if ( letter >= 0 ) Label.CaretPosition = letter;

		Label.MoveToLineStart();
		Label.MoveToLineEnd( true );

		SelectingWords = false;
		e.StopPropagation();
	}

	private bool SelectingWords = false;
	protected override void OnDoubleClick( MousePanelEvent e )
	{
		if ( string.IsNullOrEmpty( Text ) )
			return;

		if ( e.Button == "mouseleft" )
		{
			Label.SelectWord( Label.GetLetterAtScreenPosition( ScreenMousePosition ) );
			SelectingWords = true;
		}
	}

	char _pendingSurrogate;

	public override void OnKeyTyped( char k )
	{
		// A character outside the basic plane arrives as two surrogate chars - hold the
		// first until its partner lands, then insert them as one character
		if ( char.IsHighSurrogate( k ) )
		{
			_pendingSurrogate = k;
			return;
		}

		if ( char.IsLowSurrogate( k ) )
		{
			if ( _pendingSurrogate == default ) return;

			var pair = $"{_pendingSurrogate}{k}";
			_pendingSurrogate = default;

			if ( CanEnterPair( pair ) )
				InsertTyped( pair );

			return;
		}

		_pendingSurrogate = default;

		if ( !CanEnterCharacter( k ) )
			return;

		InsertTyped( k.ToString() );
	}

	// CanEnterCharacter for a character that doesn't fit in one char
	bool CanEnterPair( string pair )
	{
		if ( Numeric ) return false;
		if ( CharacterRegex != null && !System.Text.RegularExpressions.Regex.IsMatch( pair, CharacterRegex ) ) return false;

		return true;
	}

	void InsertTyped( string text )
	{
		if ( !CanEdit ) return;

		// Whitespace ends a run, so undo takes back a word at a time rather than the lot
		var whitespace = text.Length > 0 && char.IsWhiteSpace( text[0] );
		RecordEdit( whitespace ? EditKind.Single : EditKind.Typing );

		// A committed IME string arrives as typed text while its preview can still be spliced
		// in - the preview goes first, the commit replaces it
		SetImePreview( "" );

		// Replacing a selection never grows the text, so that's fine at max length
		if ( MaxLength.HasValue && TextLength >= MaxLength && !Label.HasSelection() )
			return;

		if ( Label.HasSelection() )
		{
			Label.ReplaceSelection( text );
		}
		else
		{
			Text ??= "";
			Label.InsertText( text, CaretPosition );
			Label.MoveCaretPos( 1 );
		}

		if ( text == ":" )
		{
			RealtimeEmojiReplace();
		}

		OnValueChanged();
	}


	/// <summary>
	/// How long the caret sits solid after typing or moving before it starts blinking again,
	/// the way a native text box does it.
	/// </summary>
	const float CaretSolidTime = 0.5f;
	const float CaretBlinkRate = 1.0f;

	public override void OnDraw()
	{
		Label.ShouldDrawSelection = HasFocus;

		if ( !HasFocus )
			return;

		if ( !Label.HasSelection() )
		{
			var caret = Label.GetCaretRect( CaretPosition );
			caret.Left = MathF.Floor( caret.Left ); // avoid subpixel positions (blurry and ass)
			caret.Width = 1;

			// The caret belongs to the text, so it's only drawn where the text is - and trimmed
			// to the edge rather than hanging outside the box
			var visible = Box.RectInner;

			caret.Left = MathF.Max( caret.Left, visible.Left );
			caret.Top = MathF.Max( caret.Top, visible.Top );
			caret.Right = MathF.Min( caret.Right, visible.Right );
			caret.Bottom = MathF.Min( caret.Bottom, visible.Bottom );

			if ( caret.Width > 0 && caret.Height > 0 )
			{
				// Solid right after doing something, blinking once it's been left alone
				var solid = TimeSinceCaretMoved < CaretSolidTime;
				var blink = ((TimeSinceCaretMoved - CaretSolidTime) * CaretBlinkRate) % 1.0f < 0.5f;

				var color = ComputedStyle.CaretColor ?? ComputedStyle.FontColor ?? Color.Black;
				color.a *= (solid || blink) ? 1.0f : 0f;

				Draw.Rect( caret, color );
			}
		}

		// Redraw every frame while focused, so the caret blinks
		MarkRenderDirty();
	}

	void RealtimeEmojiReplace()
	{
		if ( !AllowEmojiReplace )
			return;

		if ( CaretPosition == 0 )
			return;

		// The char index just past the ':' that was typed, which sits right before the caret
		var arr = StringInfo.ParseCombiningCharacters( Text );
		var caretChar = arr[CaretPosition - 1] + 1;

		string lookup = null;
		var start = 0;

		for ( int i = caretChar - 3; i >= 0; i-- )
		{
			var c = Text[i];

			if ( char.IsWhiteSpace( c ) )
				return;

			if ( c == ':' )
			{
				start = i;
				lookup = Text[i..caretChar];
				break;
			}
		}

		if ( lookup == null )
			return;

		var replace = Emoji.FindEmoji( lookup );
		if ( replace == null )
			return;

		// Splice just this occurrence, and step the caret in text elements - the emoji is
		// one element however many chars it takes
		var caret = CaretPosition - new StringInfo( lookup ).LengthInTextElements + new StringInfo( replace ).LengthInTextElements;

		Text = string.Concat( Text.AsSpan( 0, start ), replace, Text.AsSpan( caretChar ) );
		CaretPosition = caret;
	}

	void ReplaceEmojisInText( ref string text )
	{
		if ( !AllowEmojiReplace || string.IsNullOrEmpty( text ) )
			return;

		text = System.Text.RegularExpressions.Regex.Replace( text, @":\w+:", match =>
		{
			string lookup = match.Value;
			string replace = Emoji.FindEmoji( lookup );
			return replace ?? lookup; // Use the emoji if found; otherwise, keep the original
		} );
	}


	/// <summary>
	/// Called when the text entry's value changes.
	/// </summary>
	public virtual void OnValueChanged()
	{
		UpdateAutoComplete();
		UpdateValidation();

		if ( Property is not null )
		{
			Property.As.String = Text;
		}

		if ( Numeric )
		{
			// with numberic, we don't ever want to
			// send out invalid values to binds
			var text = FixNumeric();
			CreateEvent( "onchange" );
			CreateValueEvent( "value", text );
			OnTextEdited?.Invoke( text );
		}
		else
		{
			CreateEvent( "onchange" );
			CreateValueEvent( "value", Text );
			OnTextEdited?.Invoke( Text );
		}

		EmptyStateChanged();
	}

	/// <summary>
	/// How long since the caret last moved or the text changed. The caret stays solid for a
	/// moment after either, so it isn't blinking out from under someone who is typing.
	/// </summary>
	protected RealTimeSince TimeSinceCaretMoved;

	int _lastCaretPosition;
	string _lastText;

	public override void Tick()
	{
		base.Tick();

		if ( Property is not null && !HasFocus )
		{
			Value = Property.As.String;
		}

		bool isPlaceholder = string.IsNullOrEmpty( Text ) && !string.IsNullOrEmpty( Placeholder );
		Label.SetClass( "placeholder", isPlaceholder );
		Label.Style.Content = isPlaceholder ? Placeholder : null;
		Label.Selectable = !isPlaceholder;

		// Anything that moves the caret or changes the text restarts the blink, whichever of
		// the many ways in it took
		if ( _lastCaretPosition != CaretPosition || _lastText != Text )
		{
			// Moving the caret without changing the text is navigation, and that ends the run -
			// so typing, clicking elsewhere, then typing again is two undo steps
			if ( _lastText == Text ) BreakEditRun();

			_lastCaretPosition = CaretPosition;
			_lastText = Text;
			TimeSinceCaretMoved = 0;
		}

		if ( !HasFocus )
			TimeSinceCaretMoved = 0;
	}

	public override void SetProperty( string name, string value )
	{
		base.SetProperty( name, value );

		if ( name == "placeholder" )
		{
			Placeholder = value;
		}

		if ( name == "numeric" )
		{
			Numeric = value.ToBool();
		}

		if ( name == "format" )
		{
			NumberFormat = value;
		}

		if ( name == "value" && !HasFocus )
		{
			//
			// When setting tha value, and we're numeric, convert it to a number
			//
			if ( Numeric )
			{
				if ( !float.TryParse( value, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue ) )
					return;

				Text = floatValue.ToString( NumberFormat, CultureInfo.InvariantCulture );
				return;
			}

			Text = value;
		}

		if ( name == "disabled" )
		{
			Disabled = value.ToBool();
		}
	}

	/// <summary>
	/// Called to ensure the <see cref="Text"/> is absolutely in the correct format, in this case - a valid number format.
	/// </summary>
	/// <returns>The correctly formatted version of <see cref="Text"/>.</returns>
	public virtual string FixNumeric()
	{
		// Invariant culture with comma tolerated as a decimal separator, so the result
		// doesn't change with the user's locale
		var text = Text?.Replace( ',', '.' );

		if ( !float.TryParse( text, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue ) )
			floatValue = 0;

		floatValue = floatValue.Clamp( MinValue ?? floatValue, MaxValue ?? floatValue );

		if ( WholeNumbers )
			return MathF.Round( floatValue ).ToString( "0", CultureInfo.InvariantCulture );

		return floatValue.ToString( NumberFormat, CultureInfo.InvariantCulture );
	}

	protected override void OnDragSelect( SelectionEvent e )
	{
		if ( string.IsNullOrEmpty( Text ) )
			return;

		if ( ScrollBar.Owns( e.Target ) )
			return;

		// This press grabbed the selection to carry it somewhere - it mustn't turn into a
		// new selection under the cursor
		if ( _pressedOnSelection )
			return;

		Label.ShouldDrawSelection = true;

		// The selection runs from where the drag started to where the mouse is - a rectangle's
		// corners can't describe that once the drag spans lines
		var anchor = Label.GetLetterAtScreenPosition( e.StartPoint );
		var focus = Label.GetLetterAtScreenPosition( e.EndPoint );

		if ( SelectingWords )
		{
			// Grow both ends outward to whole words
			var boundaries = Label.GetWordBoundaryIndices();
			var left = boundaries.LastOrDefault( x => x <= Math.Min( anchor, focus ) );
			var right = boundaries.FirstOrDefault( x => x >= Math.Max( anchor, focus ) );

			(anchor, focus) = focus >= anchor ? (left, right) : (right, left);
		}

		Label.SelectionStart = anchor;
		Label.SelectionEnd = focus;
		Label.CaretPosition = focus;
		Label.ScrollToCaret();
	}

	// The composition preview currently spliced into the text - where it starts and how many
	// elements it covers. Start is -1 while nothing is spliced in.
	int _imePreviewStart = -1;
	int _imePreviewLength;

	internal override Rect ImeCaretRect
		=> Label._textBlock is not null ? Label.GetCaretRect( CaretPosition ) : Box.Rect;

	/// <summary>
	/// Splice the IME composition preview into the text at the caret, replacing the previous
	/// one. The committed text never comes through here - it arrives as ordinary typed text,
	/// which is why removal has to be by position rather than restoring a snapshot.
	/// </summary>
	void SetImePreview( string text )
	{
		if ( _imePreviewStart >= 0 && _imePreviewLength > 0 )
		{
			Label.RemoveText( _imePreviewStart, _imePreviewLength );
			CaretPosition = _imePreviewStart;
		}

		if ( string.IsNullOrEmpty( text ) )
		{
			_imePreviewStart = -1;
			_imePreviewLength = 0;
			return;
		}

		_imePreviewStart = CaretPosition;
		_imePreviewLength = new StringInfo( text ).LengthInTextElements;

		Text ??= "";
		Label.InsertText( text, CaretPosition );
		CaretPosition = _imePreviewStart + _imePreviewLength;
	}

	protected override void OnEvent( PanelEvent e )
	{
		// Composing replaces the selection, the same as typing does
		if ( e.Name == "onimestart" && CanEdit )
		{
			if ( Label.HasSelection() )
			{
				Label.ReplaceSelection( "" );
				OnValueChanged();
			}

			_imePreviewStart = -1;
			_imePreviewLength = 0;
		}

		if ( e.Name == "onime" && CanEdit )
		{
			SetImePreview( (string)e.Value );
		}

		if ( e.Name == "onimeend" )
		{
			SetImePreview( "" );
		}

		base.OnEvent( e );
	}

	/// <summary>
	/// The TextEntry has the :empty style when the text is unset
	/// </summary>
	protected override bool IsPanelEmpty()
	{
		return TextLength == 0;
	}

}
