using System;


[Flags]
enum TextInteractionFlags
{
	NoTextInteraction = 0,
	TextSelectableByMouse = 1,
	TextSelectableByKeyboard = 2,
	LinksAccessibleByMouse = 4,
	LinksAccessibleByKeyboard = 8,
	TextEditable = 16,

	TextEditorInteraction = TextSelectableByMouse | TextSelectableByKeyboard | TextEditable,
	TextBrowserInteraction = TextSelectableByMouse | LinksAccessibleByMouse | LinksAccessibleByKeyboard
};

enum LineWrapMode
{
	NoWrap,
	WidgetWidth
};


enum QTextOptionWrapMode
{
	NoWrap,
	WordWrap,
	ManualWrap,
	WrapAnywhere,
	WrapAtWordBoundaryOrAnywhere
};

namespace Editor
{
	/// <summary>
	/// A multi-line text entry. See <see cref="LineEdit"/> for a single line version.
	/// </summary>
	public class TextEdit : Widget
	{
		Native.QPlainTextEdit _pte;

		/// <inheritdoc cref="OnTextChanged"/>
		public Action<string> TextChanged;

		/// <summary>
		/// The text entry received keyboard focus.
		/// </summary>
		public event Action OnEditingStarted;

		/// <summary>
		/// The text entry lost keyboard focus.
		/// </summary>
		public event Action OnEditingFinished;

		internal TextEdit( Native.QPlainTextEdit widget ) : base( false )
		{
			NativeInit( widget );
		}

		public TextEdit( Widget parent = null ) : base( false )
		{
			Sandbox.InteropSystem.Alloc( this );
			NativeInit( CPlainTextEdit.Create( parent?._widget ?? default, this ) );

			VerticalScrollbar = new ScrollBar( _pte.verticalScrollBar() );
			HorizontalScrollbar = new ScrollBar( _pte.horizontalScrollBar() );
		}

		internal override void NativeInit( IntPtr ptr )
		{
			_pte = ptr;
			base.NativeInit( ptr );
		}

		internal override void NativeShutdown()
		{
			base.NativeShutdown();
			_pte = default;
		}

		public bool TextSelectable
		{
			get => (_pte.textInteractionFlags() & TextInteractionFlags.TextSelectableByMouse) != 0;
			set
			{
				var flags = _pte.textInteractionFlags();
				if ( value )
				{
					flags |= TextInteractionFlags.TextSelectableByMouse;
					flags |= TextInteractionFlags.TextSelectableByKeyboard;
				}
				else
				{
					flags &= ~TextInteractionFlags.TextSelectableByMouse;
					flags &= ~TextInteractionFlags.TextSelectableByKeyboard;

				}

				_pte.setTextInteractionFlags( flags );
			}
		}

		public bool LinksClickable
		{
			get => (_pte.textInteractionFlags() & TextInteractionFlags.LinksAccessibleByMouse) != 0;
			set
			{
				var flags = _pte.textInteractionFlags();
				if ( value )
				{
					flags |= TextInteractionFlags.LinksAccessibleByMouse;
					flags |= TextInteractionFlags.LinksAccessibleByKeyboard;
				}
				else
				{
					flags &= ~TextInteractionFlags.LinksAccessibleByMouse;
					flags &= ~TextInteractionFlags.LinksAccessibleByKeyboard;

				}

				_pte.setTextInteractionFlags( flags );
			}
		}

		public bool Editable
		{
			get => (_pte.textInteractionFlags() & TextInteractionFlags.TextEditable) != 0;
			set
			{
				var flags = _pte.textInteractionFlags();
				if ( value )
				{
					flags |= TextInteractionFlags.TextEditable;
				}
				else
				{
					flags &= ~TextInteractionFlags.TextEditable;

				}

				_pte.setTextInteractionFlags( flags );
			}
		}

		public ScrollBar VerticalScrollbar { get; init; }
		public ScrollBar HorizontalScrollbar { get; init; }

		public ScrollbarMode HorizontalScrollbarMode
		{
			get => _pte.horizontalScrollBarPolicy();
			set => _pte.setHorizontalScrollBarPolicy( value );
		}

		public ScrollbarMode VerticalScrollbarMode
		{
			get => _pte.verticalScrollBarPolicy();
			set => _pte.setVerticalScrollBarPolicy( value );
		}

		public void ScrollToBottom()
		{
			VerticalScrollbar.SliderPosition = VerticalScrollbar.Maximum;
		}

		public string PlainText
		{
			get => _pte.toPlainText();
			set => _pte.setPlainText( value );
		}

		public string Html
		{
			get => _pte.document().toHtml();
			set => _pte.document().setHtml( value );
		}

		public string PlaceholderText
		{
			get => _pte.placeholderText();
			set => _pte.setPlaceholderText( value );
		}

		public void AppendHtml( string html )
		{
			// Strip invalid control characters as Qt just shites the bed https://bugreports.qt.io/browse/QTBUG-95926
			html = System.Text.RegularExpressions.Regex.Replace( html, @"[\p{C}-[\u0020\u0009\u000A\u000C\u000D]]", "" );
			_pte.appendHtml( html );
		}
		public void AppendPlainText( string text ) => _pte.appendPlainText( text );
		public virtual void Clear() => _pte.clear();
		public void SelectAll() => _pte.selectAll();
		public void CenterOnCursor() => _pte.centerCursor();

		public bool CenterOnScroll
		{
			get => _pte.centerOnScroll();
			set => _pte.setCenterOnScroll( value );
		}

		public bool BackgroundVisible
		{
			get => _pte.backgroundVisible();
			set => _pte.setBackgroundVisible( value );
		}

		public int MaximumBlockCount
		{
			get => _pte.maximumBlockCount();
			set => _pte.setMaximumBlockCount( value );
		}

		public float TabSize
		{
			get => _pte.tabStopDistance();
			set => _pte.setTabStopDistance( value );
		}

		public override bool ReadOnly
		{
			get => _pte.isReadOnly();
			set => _pte.setReadOnly( value );
		}

		public void SetTextCursor( TextCursor cursor )
		{
			_pte.setTextCursor( cursor );
		}

		public TextCursor GetCursorAtPosition( Vector2 position )
		{
			return _pte.cursorForPosition( position );
		}

		public TextCursor GetTextCursor()
		{
			return _pte.textCursor();
		}

		public Rect GetCursorRect( TextCursor cursor )
		{
			return _pte.cursorRect( cursor ).Rect;
		}

		public string GetAnchorAt( Vector2 point )
		{
			return _pte.anchorAt( point );
		}

		public override CursorShape Cursor
		{
			get => base.Cursor;
			set
			{
				if ( base.Cursor == value )
					return;

				base.Cursor = value;

				var w = _pte.viewport();

				if ( Cursor == CursorShape.None )
				{
					w.unsetCursor();
					return;
				}

				w.setCursor( Cursor );
			}
		}

		public TextCursor GetCursorAtBlock( int block )
		{
			// todo - make sure this block is valid etc?
			using var blockObj = _pte.document().findBlockByNumber( block );
			return TextCursor.CreateFromBlock( blockObj );
		}

		internal void InternalTextChanged() => OnTextChanged( PlainText );

		/// <summary>
		/// Called when text changed.
		/// </summary>
		/// <param name="value"></param>
		protected virtual void OnTextChanged( string value )
		{
			TextChanged?.Invoke( value );
		}

		protected override void OnFocus( FocusChangeReason reason )
		{
			base.OnFocus( reason );

			OnEditingStarted?.Invoke();
		}

		protected override void OnBlur( FocusChangeReason reason )
		{
			base.OnBlur( reason );

			OnEditingFinished?.Invoke();
		}
	}
}
