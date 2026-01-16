using Sandbox.Html;
using System.Globalization;

namespace Sandbox.UI
{
	/// <summary>
	/// A generic text label. Can be made editable.
	/// </summary>
	[Library( "label" ), Alias( "text" ), Expose]
	public partial class Label : Panel
	{
		/// <summary>
		/// Information about the <see cref="Text"/> on a per-element scale. It handles multi-character Unicode units (graphemes) correctly.
		/// </summary>
		protected StringInfo StringInfo = new();

		internal string _textToken;
		internal string _text;
		internal Rect _textRect;
		internal TextBlock _textBlock;

		int layoutStateHash;
		bool sizeFinalized;
		Vector2 availableSpace;

		public override bool HasContent => true;

		[Category( "Selection" )]
		public bool ShouldDrawSelection
		{
			get => _textBlock?.ShouldDrawSelection ?? false;
			set
			{
				if ( _textBlock is null )
					return;

				if ( _textBlock.ShouldDrawSelection == Selectable && value )
					return;

				_textBlock.ShouldDrawSelection = Selectable && value;
				SetNeedsPreLayout();
			}
		}

		/// <summary>
		/// Can be selected
		/// </summary>
		[Category( "Selection" )]
		public bool Selectable { get; set; } = true;

		/// <summary>
		/// If true and the text starts with #, it will be treated as a language token.
		/// </summary>
		public bool Tokenize { get; set; } = true;

		[Hide]
		public int SelectionStart
		{
			get => _textBlock?.SelectionStart ?? 0;
			set
			{
				if ( _textBlock == null ) return;
				if ( _textBlock.SelectionStart == value ) return;

				_textBlock.SelectionStart = value;
				SetNeedsPreLayout();
			}
		}

		[Hide]
		public int SelectionEnd
		{
			get => _textBlock?.SelectionEnd ?? 0;
			set
			{
				if ( _textBlock == null ) return;
				if ( _textBlock.SelectionEnd == value ) return;

				_textBlock.SelectionEnd = value;
				SetNeedsPreLayout();
			}
		}

		/// <summary>
		/// The color used for text selection highlight
		/// </summary>
		[Category( "Selection" )]
		public Color SelectionColor
		{
			get => _textBlock?.SelectionColor ?? Color.Cyan.WithAlpha( 0.39f );
			set
			{
				if ( _textBlock == null ) return;
				if ( _textBlock.SelectionColor == value ) return;

				_textBlock.SelectionColor = value;
			}
		}

		public Label()
		{
			AddClass( "label" );
			YogaNode.SetMeasureFunction( MeasureText );
		}

		public Label( string text, string classname = null ) : this()
		{
			Text = text;
			AddClass( classname );
		}

		Vector2 MeasureText( YGNodeRef node, float width, YGMeasureMode widthMode, float height, YGMeasureMode heightMode )
		{
			try
			{
				if ( _textBlock == null ) return new Vector2( 2, 10 );

				availableSpace = new Vector2( width, height );

				Vector2 size;

				if ( sizeFinalized && _textBlock.IsTruncated )
				{
					size = _textBlock.BlockSize;
				}
				else
				{
					size = _textBlock.Measure( width, height );
				}

				return size;
			}
			catch ( System.Exception e )
			{
				NativeEngine.EngineGlobal.Plat_MessageBox( e.Message, e.StackTrace );
				return default;
			}
		}

		public override void OnDeleted()
		{
			base.OnDeleted();

			_textBlock?.Dispose();
			_textBlock = null;
		}

		/// <summary>
		/// Text to display on the label.
		/// </summary>
		public virtual string Text
		{
			get => _text;
			set
			{
				value ??= "";

				if ( Tokenize && value != null && value.Length > 1 && value[0] == '#' )
				{
					if ( _textToken == value ) return;
					_textToken = value;

					value = Language.GetPhrase( _textToken[1..] );
				}

				if ( _text == value )
					return;

				_text = value;
				StringInfo.String = value ?? string.Empty;
				CaretSantity();
				SetNeedsPreLayout();
			}
		}

		/// <summary>
		/// Set to true if this is rich text. This means it can support some inline html elements.
		/// </summary>
		public bool IsRich { get; set; }

		public override void SetProperty( string name, string value )
		{
			if ( name == "text" )
			{
				Text = value;
				return;
			}

			if ( name == "selectable" )
			{
				//Selectable = value.ToBool();
				return;
			}

			base.SetProperty( name, value );
		}

		public override void SetContent( string value )
		{
			// alex: This value gets trimmed inside TextBlock based on the WhiteSpace
			// style value for this label
			Text = value ?? "";
		}

		/// <summary>
		/// Position of the text cursor/caret within the text, at which newly typed characters are inserted.
		/// </summary>
		public int CaretPosition { get; set; }

		/// <summary>
		/// Amount of characters in the text of the text entry. Not bytes.
		/// </summary>
		public int TextLength => StringInfo.LengthInTextElements;

		/// <summary>
		/// Ensure the text caret and selection are in sane positions, that is, not outside of the text bounds.
		/// </summary>
		protected void CaretSantity()
		{
			if ( CaretPosition > TextLength )
			{
				CaretPosition = TextLength;
				ScrollToCaret();
			}
			if ( SelectionStart > TextLength )
			{
				SelectionStart = TextLength;
				ScrollToCaret();
			}
			if ( SelectionEnd > TextLength )
			{
				SelectionEnd = TextLength;
				ScrollToCaret();
			}
		}

		/// <summary>
		/// Returns the selected text.
		/// </summary>
		public string GetSelectedText()
		{
			if ( TextLength == 0 ) return "";
			if ( !HasSelection() ) return "";

			CaretSantity();

			var s = Math.Min( SelectionStart, SelectionEnd );
			var e = Math.Max( SelectionStart, SelectionEnd );

			return StringInfo.SubstringByTextElements( s, e - s );
		}

		public override string GetClipboardValue( bool cut )
		{
			if ( !HasSelection() )
				return null;

			var txt = GetSelectedText();

			return txt;
		}

		public Rect GetCaretRect( int i )
		{
			var rect = _textBlock.CaretRect( i );
			rect.Position += _textRect.Position - caretScroll;
			rect.Width = 2;

			return rect;
		}

		internal override void PreLayout( LayoutCascade cascade )
		{
			base.PreLayout( cascade );

			string styleContent = null;

			if ( ComputedStyle.Content != null )
			{
				styleContent = ComputedStyle.Content;

				if ( styleContent.Length > 1 && styleContent[0] == '#' )
				{
					styleContent = Language.GetPhrase( styleContent[1..] );
				}
			}

			var text = styleContent ?? Text ?? string.Empty;

			if ( _textBlock is null )
			{
				_textBlock = new TextBlock();
				_textBlock.LookupStyles = HtmlStyleLookup;
			}

			_textBlock.NoWrap = !Multiline;

			if ( IsRich )
			{
				_textBlock.SetHtml( text );
				_textBlock.NoWrap = false;
			}
			else
			{
				_textBlock.SetText( text );
			}

			int newStateHash = HashCode.Combine( (int)(availableSpace.x * 100), ScaleToScreen, _textBlock.IsTruncated, hoveredNode );

			if ( newStateHash != layoutStateHash )
			{
				layoutStateHash = newStateHash;
				sizeFinalized = false;
			}

			if ( _textBlock.UpdateStyles( ComputedStyle ) )
			{
				YogaNode.MarkDirty();
				sizeFinalized = false;
			}
		}
		private Styles HtmlStyleLookup( INode node )
		{
			if ( node.GetAttribute( "style", null ) is string styles )
			{
				Log.Warning( "TODO: Apply Html Styles" );
			}

			var blocks = AllStyleSheets
							.SelectMany( x => x.Nodes )
							.Select( x => x.Test( node ) )
							.Where( x => x is not null )
							.ToList();

			if ( blocks.Count == 0 )
				return null;

			blocks.Sort( StyleOrderer.Instance );

			var s = new Styles();

			foreach ( var entry in blocks )
			{
				s.Add( entry.Block.Styles );
			}

			s.ApplyScale( FindRootPanel().ScaleToScreen );

			return s;
		}

		public override void FinalLayout( Vector2 offset )
		{
			base.FinalLayout( offset );

			if ( !IsVisible ) return;
			if ( ComputedStyle is null ) return;

			_textBlock?.SizeFinalized( Box.RectInner.Width, Box.RectInner.Height );

			if ( !sizeFinalized )
			{
				sizeFinalized = true;
				YogaNode.MarkDirty();
			}

			_textRect = Box.RectInner;

			if ( ComputedStyle.TextAlign == TextAlign.Center )
			{
				_textRect.Left += (_textRect.Width - _textBlock.BlockSize.x) * 0.5f;
			}
			else if ( ComputedStyle.TextAlign == TextAlign.Right )
			{
				_textRect.Left = _textRect.Right - _textBlock.BlockSize.x;
			}

			if ( ComputedStyle.AlignItems == Align.Center )
			{
				_textRect.Top += (_textRect.Height - _textBlock.BlockSize.y) * 0.5f;
			}
			else if ( ComputedStyle.AlignItems == Align.FlexEnd )
			{
				_textRect.Top = _textRect.Bottom - _textBlock.BlockSize.y;
			}

			_textRect.Size = _textBlock.BlockSize;
		}

		internal override void DrawContent( PanelRenderer renderer, ref RenderState state )
		{
			var rect = Box.RectInner;
			rect.Position -= caretScroll;
			_textBlock?.Render( renderer, ref state, ComputedStyle, rect, Opacity * state.RenderOpacity );
		}

		public int GetLetterAt( Vector2 pos )
		{
			if ( _textBlock == null ) return -1;

			return _textBlock.GetLetterAt( pos );
		}

		public int GetLetterAtScreenPosition( Vector2 pos ) => GetLetterAt( ScreenPositionToTextRectPosition( pos ) );

		Vector2 ScreenPositionToTextRectPosition( Vector2 pos )
		{
			if ( GlobalMatrix.HasValue )
			{
				pos = GlobalMatrix.Value.Transform( pos );
			}

			var x = pos.x - _textRect.Left;
			var y = pos.y - _textRect.Top;

			return new Vector2( x, y ) + caretScroll;
		}

		public bool HasSelection() => ShouldDrawSelection && SelectionStart != SelectionEnd;

		/// <summary>
		/// When the language changes, if we're token based we need to update to the new phrase.
		/// </summary>
		public override void LanguageChanged()
		{
			if ( _textToken == null ) return;
			if ( !Tokenize ) return;

			var token = _textToken;
			_textToken = null; // skip cache
			Text = token;
		}

		INode hoveredNode;

		protected override void OnMouseMove( MousePanelEvent e )
		{
			base.OnMouseMove( e );

			if ( _textBlock is null || !IsRich )
			{
				hoveredNode = default;
				return;
			}

			var hov = _textBlock.GetSpanAt( e.LocalPosition )?.node;
			if ( hov == hoveredNode ) return;

			if ( hoveredNode is not null )
			{
				hoveredNode.SetPseudoClass( PseudoClass.None );
			}

			hoveredNode = hov;

			if ( hoveredNode is not null )
			{
				hoveredNode.SetPseudoClass( PseudoClass.Hover );
			}

			Style.Cursor = (hoveredNode?.Name == "a") ? "pointer" : null;
			_textBlock.Dirty();
			SetNeedsPreLayout();
		}

		protected override void OnClick( MousePanelEvent e )
		{
			base.OnClick( e );

			if ( hoveredNode is not null && hoveredNode.GetAttribute( "href", null ) is { } url )
			{
				bool isValid = Uri.TryCreate( url, UriKind.Absolute, out var parsedUri ) && (parsedUri.Scheme == "http" || parsedUri.Scheme == "https");

				if ( !isValid )
				{
					Log.Warning( $"Blocked URL: {url}" );
					return;
				}

				//
				// Modal popup, are you sure etc?
				//

				System.Diagnostics.Process.Start( new System.Diagnostics.ProcessStartInfo()
				{
					FileName = parsedUri.ToString(),
					UseShellExecute = true,
					Verb = "open"
				} );
			}
		}
	}

	namespace Construct
	{
		public static class LabelConstructor
		{
			/// <summary>
			/// Create a simple text label with given text and CSS classname.
			/// </summary>
			public static Label Label( this PanelCreator self, string text = null, string classname = null )
			{
				var control = self.panel.AddChild<Label>();

				if ( text != null )
					control.Text = text;

				if ( classname != null )
					control.AddClass( classname );

				return control;
			}
		}
	}
}
