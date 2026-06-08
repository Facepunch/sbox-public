
// RichTextKit
// Copyright © 2019-2020 Topten Software. All Rights Reserved.
// 
// Licensed under the Apache License, Version 2.0 (the "License"); you may 
// not use this product except in compliance with the License. You may obtain 
// a copy of the License at
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software 
// distributed under the License is distributed on an "AS IS" BASIS, WITHOUT 
// WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the 
// License for the specific language governing permissions and limitations 
// under the License.

using SkiaSharp;
using Topten.RichTextKit.Utils;

namespace Topten.RichTextKit
{
	/// <summary>
	/// Represents a block of formatted, laid out and measurable text
	/// </summary>
	public class TextBlock : StyledText
	{
		/// <summary>
		/// Constructor
		/// </summary>
		public TextBlock()
		{
		}

		/// <summary>
		/// The max width property sets the maximum width of a line, after which 
		/// the line will be wrapped onto the next line.
		/// </summary>
		/// <remarks>
		/// This property can be set to null, in which case lines won't be wrapped.
		/// </remarks>
		public float? MaxWidth
		{
			get => _maxWidth;
			set
			{
				if ( value.HasValue && value.Value < 0 )
					value = 0;
				if ( _maxWidth != value )
				{
					_maxWidth = value;
					InvalidateLayout();
				}
			}
		}

		/// <summary>
		/// The maximum height of the TextBlock after which lines will be 
		/// truncated and the final line will be appended with an 
		/// ellipsis (`...`) character.
		/// </summary>
		/// <remarks>
		/// This property can be set to null, in which case the vertical height of the text block
		/// won't be capped.
		/// </remarks>
		public float? MaxHeight
		{
			get => _maxHeight;
			set
			{
				if ( value.HasValue && value.Value < 0 )
					value = 0;

				if ( value != _maxHeight )
				{
					_maxHeight = value;
					InvalidateLayout();
				}
			}
		}

		/// <summary>
		/// The maximum number of lines after which lines will be 
		/// truncated and the final line will be appended with an 
		/// ellipsis (`...`) character.
		/// </summary>
		/// <remarks>
		/// This property can be set to null, in which case the vertical height of 
		/// the text block won't be capped.
		/// </remarks>
		public int? MaxLines
		{
			get => _maxLines;
			set
			{
				if ( value.HasValue && value.Value < 0 )
					value = 0;

				if ( value != _maxLines )
				{
					_maxLines = value;
					InvalidateLayout();
				}
			}
		}

		/// <summary>
		/// Prevents wrapping to a new line when reaching max width
		/// </summary>
		public bool NoWrap
		{
			get => _noWrap;
			set
			{
				if ( value == _noWrap )
					return;

				_noWrap = value;
				InvalidateLayout();
			}
		}

		/// <summary>
		/// Sets the left, right or center alignment of the text block.
		/// </summary>
		/// <remarks>
		/// Set this property to <see cref="TextAlignment.Auto"/> to align
		/// the paragraph according to the <see cref="BaseDirection"/>.
		/// 
		/// * If the <see cref="MaxWidth"/> property has been set this will 
		///   be used for alignment calculations.  
		/// * If the <see cref="MaxWidth"/> property has not been set, the 
		///   width of the longest line will be used.
		/// </remarks>
		public TextAlignment Alignment
		{
			get => _textAlignment;
			set
			{
				if ( _textAlignment != value )
				{
					_textAlignment = value;
					InvalidateLayout();
				}
			}
		}

		/// <summary>
		/// Sets how a text block will overflow.
		/// <remarks>
		/// <see cref="TextOverflow.Ellipsis"/> will append an ellipsis.
		/// <see cref="TextOverflow.Clip"/> will clip without appending anything.
		/// </remarks>
		/// </summary>
		public TextOverflow Overflow
		{
			get => _overflow;
			set
			{
				if ( _overflow != value )
				{
					_overflow = value;
					InvalidateLayout();
				}
			}
		}

		/// <summary>
		/// Sets how a text block will break on overflow.
		/// </summary>
		public WordBreakMode WordBreak
		{
			get => _breakMode;
			set
			{
				if ( _breakMode != value )
				{
					_breakMode = value;
					InvalidateLayout();
				}
			}
		}

		/// <summary>
		/// The base directionality of this text block (whether text is laid out 
		/// left to right, or right to left)
		/// </summary>
		public TextDirection BaseDirection
		{
			get => _baseDirection;
			set
			{
				if ( _baseDirection != value )
				{
					_baseDirection = value;
					InvalidateLayout();
				}
			}
		}

		/// <summary>
		/// Clear the content of this text block
		/// </summary>
		public override void Clear()
		{
			// Reset everything
			FontRun.Pool.Value.ReturnAndClear( _fontRuns );
			TextLine.Pool.Value.ReturnAndClear( _lines );
			_textShapingBuffers.Clear();
			base.Clear();
		}

		/// <summary>
		/// Split this text block at the specified code point index
		/// </summary>
		/// <param name="from">The code point index to copy from</param>
		/// <param name="length">The number of code points to copy</param>
		/// <returns>A new text block with the RHS split part of the text</returns>
		public TextBlock Copy( int from, int length )
		{
			// Create a new text block with the same attributes as this one
			var other = new TextBlock();
			other.Alignment = this.Alignment;
			other.BaseDirection = this.BaseDirection;
			other.MaxWidth = this.MaxWidth;
			other.MaxHeight = this.MaxHeight;
			other.MaxLines = this.MaxLines;

			// Copy text to the new paragraph
			foreach ( var subRun in _styleRuns.GetInterectingRuns( from, length ) )
			{
				var sr = _styleRuns[subRun.Index];
				other.AddText( sr.CodePoints.SubSlice( subRun.Offset, subRun.Length ), sr.Style );
			}

			return other;
		}

		/// <inheritdoc />
		protected override void OnChanged()
		{
			InvalidateLayout();
			base.OnChanged();
		}


		/// <summary>
		/// Appends an ellipsis to this text block
		/// </summary>
		/// <remarks>
		/// This method checks if the text block has already been truncated and if
		/// not appends an ellipsis without changing the measured vertical layout of the
		/// text block.  The ellipsis only remains in effect until the block's layout
		/// is recalculated.
		/// 
		/// The text block must have at least one line.  If the block contains no text,
		/// then use AddText("\n", style) to create a single line with an attached style
		/// but no text.
		/// 
		/// The intended purpose of this is to included an ellipsis on this text block
		/// when a following text block doesn't fit.
		/// </remarks>
		public void AddEllipsis()
		{
			// Make sure laid out
			Layout();

			// Already truncated?
			if ( _truncated )
				return;

			if ( _lines.Count == 0 )
				throw new InvalidOperationException( "Ellipsis can't be appended to a text block with no lines" );

			// Append the ellipsis
			var line = _lines[_lines.Count - 1];

			// Because adorning the line with ellipsis resets the XCoord of each font run
			// to be left aligned, we need to move the glyphs to be left aligned too
			for ( int frIndex = 0; frIndex < line.Runs.Count; frIndex++ )
			{
				var fr = line.Runs[frIndex];
				fr.MoveGlyphs( -fr.XCoord, 0 );
			}

			// Append the ellipsis to the line and relayout theline
			AdornLineWithOverflowContent( line, true );

			// Work out the new x-alignment
			var ta = ResolveTextAlignment();
			float xAdjust = 0;
			switch ( ta )
			{
				case TextAlignment.Right:
					xAdjust = (_maxWidth ?? _measuredWidth) - line.Width;
					break;

				case TextAlignment.Center:
					xAdjust = ((_maxWidth ?? _measuredWidth) - line.Width) / 2;
					break;
			}

			// Adjust the measured width if the adorned line is the widest line
			if ( line.Width > _measuredWidth )
				_measuredWidth = line.Width;

			// Reposition each run to the correct location
			for ( int frIndex = 0; frIndex < line.Runs.Count; frIndex++ )
			{
				var fr = line.Runs[frIndex];
				fr.Line = line;
				fr.XCoord += xAdjust;
				if ( fr.RunKind == FontRunKind.Ellipsis )
				{
					// The ellipsis has it's xcoord setup, but it's glyphs have never
					// been positioned so need to handle this a little differently
					fr.MoveGlyphs( fr.XCoord, line.YCoord + line.BaseLine );
				}
				else
				{
					// Move the glyphs back to corectly aligned position
					fr.MoveGlyphs( fr.XCoord, 0 );
				}
			}
		}

		/// <summary>
		/// Updates the internal layout of the text block
		/// </summary>
		/// <remarks>
		/// Generally you don't need to call this method as the layout
		/// will be automatically updated as needed.
		/// </remarks>
		public void Layout( bool force = false )
		{
			// Needed?
			if ( !force && !_needsLayout )
				return;
			_needsLayout = false;

			// Resolve max width/height
			_maxWidthResolved = _maxWidth ?? float.MaxValue;
			_maxHeightResolved = _maxHeight ?? float.MaxValue;
			_maxLinesResolved = _maxLines ?? int.MaxValue;

			if ( _noWrap && _overflow == TextOverflow.None )
			{
				_maxWidthResolved = float.MaxValue;
			}

			// Reset layout state
			_textShapingBuffers.Clear();
			_fontRuns.Clear();
			_lines.Clear();
			_caretIndicies.Clear();
			_wordBoundaryIndicies.Clear();
			_measuredHeight = 0;
			_measuredWidth = 0;
			_leftOverhang = null;
			_rightOverhang = null;
			_truncated = false;

			// Only layout if actually have some text
			if ( _codePoints.Length != 0 )
			{
				// Build font runs
				BuildFontRuns();

				// Break font runs into lines
				BreakLines();

				// Finalize lines
				FinalizeLines();
			}
		}

		/// <summary>
		/// Get all font runs for this text block
		/// </summary>
		public IReadOnlyList<FontRun> FontRuns
		{
			get
			{
				Layout();
				return _fontRuns;
			}
		}

		/// <summary>
		/// Get all the lines for this text block
		/// </summary>
		public IReadOnlyList<TextLine> Lines
		{
			get
			{
				Layout();
				return _lines;
			}
		}

		/// <summary>
		/// Paint this text block
		/// </summary>
		/// <param name="canvas">The Skia canvas to paint to</param>
		/// <param name="options">Options controlling the paint operation</param>
		public void Paint( SKCanvas canvas, TextPaintOptions options = null )
		{
			// Ensure have options
			if ( options == null )
				options = TextPaintOptions.Default;

			// Ensure layout done
			Layout();

			// Create context
			var ctx = new PaintTextContext()
			{
				Canvas = canvas,
				Options = options,
			};

			ctx.Shader = options.TextGradient?.CreateShader( MeasuredWidth, MeasuredHeight, MeasuredPadding.Left );

			// Prepare selection
			if ( options.Selection.HasValue )
			{
				ctx.SelectionStart = options.Selection.Value.Minimum;
				ctx.SelectionEnd = options.Selection.Value.Maximum;
				ctx.PaintSelectionBackground = new SKPaint()
				{
					Color = options.SelectionColor,
					IsStroke = false,
					IsAntialias = false,
				};
				if ( options.SelectionHandleScale != 0 && options.SelectionHandleColor.Alpha > 0 )
				{
					ctx.SelectionHandleScale = options.SelectionHandleScale;
					ctx.PaintSelectionHandle = new SKPaint()
					{
						Color = options.SelectionHandleColor,
						IsStroke = false,
						IsAntialias = true,
					};
				}
			}
			else
			{
				ctx.SelectionStart = -1;
				ctx.SelectionEnd = -1;
			}

			foreach ( var l in _lines )
			{
				l.PaintBackground( ctx );
			}

			// Paint each line
			foreach ( var l in _lines )
			{
				l.Paint( ctx );
			}

			// Clean up
			ctx.PaintSelectionBackground?.Dispose();
			ctx.Shader?.Dispose();
		}

		/// <summary>
		/// Paint this text block
		/// </summary>
		/// <param name="canvas">The Skia canvas to paint to</param>
		/// <param name="position">The top left position within the canvas to draw at</param>
		/// <param name="options">Options controlling the paint operation</param>
		public void Paint( SKCanvas canvas, SKPoint position, TextPaintOptions options = null )
		{
			// Translate
			canvas.Save();
			canvas.Translate( position.X, position.Y );

			// Paint it
			Paint( canvas, options );

			// Restore and done!
			canvas.Restore();
		}

		/// <summary>
		/// The total height of all lines.
		/// </summary>
		public float MeasuredHeight
		{
			get
			{
				Layout();
				return _measuredHeight;
			}
		}

		/// <summary>
		/// The length of the displayed text (in code points)
		/// </summary>
		/// <remarks>
		/// If the text is truncated, this is the index of the point
		/// at which the ellipsis was inserted.  If the text it not
		/// truncated, is the length of all added text.
		/// </remarks>
		public int MeasuredLength
		{
			get
			{
				Layout();
				if ( _lines.Count == 0 )
					return 0;
				return _lines[_lines.Count - 1].End;
			}
		}


		/// <summary>
		/// The number of lines in the text
		/// </summary>
		public int LineCount
		{
			get
			{
				Layout();
				return _lines.Count;
			}
		}

		/// <summary>
		/// The width of the widest line of text.
		/// </summary>
		/// <remarks>
		/// The returned width does not include any overhang.
		/// </remarks>
		public float MeasuredWidth
		{
			get
			{
				Layout();
				return _measuredWidth;
			}
		}

		/// <summary>
		/// Indicates if the text was truncated due to max height or max lines
		/// constraints
		/// </summary>
		public bool Truncated
		{
			get
			{
				Layout();
				return _truncated;
			}
		}


		/// <summary>
		/// Gets the size of any unused space around the text.
		/// </summary>
		/// <remarks>
		/// If MaxWidth is not set, the left and right padding will always be zero.
		/// 
		/// This property also returns a bottom padding amount if MaxHeight is set.
		/// 
		/// The returned top padding is always zero.
		/// 
		/// The return rectangle describes padding amounts for each edge - not 
		/// rectangle co-ordinates.
		/// </remarks>
		public SKRect MeasuredPadding
		{
			get
			{
				var r = new SKRect();

				// Bottom padding?
				if ( _maxHeight.HasValue )
				{
					r.Bottom = _maxHeight.Value - _measuredHeight;
				}

				if ( !_maxWidth.HasValue )
					return r;

				Layout();

				switch ( ResolveTextAlignment() )
				{
					case TextAlignment.Left:
					case TextAlignment.Justify: // justified text is anchored left and fills the width
						r.Left = 0;
						r.Right = _maxWidthResolved - _measuredWidth;
						return r;

					case TextAlignment.Right:
						r.Left = _maxWidthResolved - _measuredWidth;
						r.Right = 0;
						return r;

					case TextAlignment.Center:
						r.Left = (_maxWidthResolved - _measuredWidth) / 2;
						r.Right = (_maxWidthResolved - _measuredWidth) / 2;
						return r;
				}

				throw new InvalidOperationException();
			}
		}


		/// <summary>
		/// Gets the actual measured overhang in each direction based on the 
		/// fonts used, and the supplied text.
		/// </summary>
		/// <remarks>
		/// The return rectangle describes overhang amounts for each edge - not 
		/// rectangle co-ordinates.
		/// </remarks>
		public SKRect MeasuredOverhang
		{
			get
			{
				Layout();
				if ( !_leftOverhang.HasValue )
				{
					var right = _maxWidth ?? MeasuredWidth;
					float leftOverhang = 0;
					float rightOverhang = 0;
					foreach ( var l in _lines )
					{
						l.UpdateOverhang( right, ref leftOverhang, ref rightOverhang );
					}
					_leftOverhang = leftOverhang;
					_rightOverhang = rightOverhang;
				}
				return new SKRect( _leftOverhang.Value, 0, _rightOverhang.Value, 0 );
			}
		}

		/// <summary>
		/// Hit test this block of text
		/// </summary>
		/// <param name="lineIndex">The line to be hit test</param>
		/// <param name="x">The x-coordinate relative to top left of the block</param>
		/// <returns>A HitTestResult</returns>
		public HitTestResult HitTestLine( int lineIndex, float x )
		{
			return _lines[lineIndex].HitTest( x );
		}

		/// <summary>
		/// Hit test this block of text
		/// </summary>
		/// <param name="x">The x-coordinate relative to top left of the block</param>
		/// <param name="y">The x-coordinate relative to top left of the block</param>
		/// <returns>A HitTestResult</returns>
		public HitTestResult HitTest( float x, float y )
		{
			Layout();

			var htr = new HitTestResult();

			// Work out which line number we're over
			htr.OverLine = -1;
			htr.OverCodePointIndex = -1;
			for ( int i = 0; i < _lines.Count; i++ )
			{
				var l = _lines[i];
				if ( y >= l.YCoord && y < l.YCoord + l.Height )
				{
					htr.OverLine = i;
				}
			}

			// Work out the closest line
			if ( htr.OverLine >= 0 )
			{
				htr.ClosestLine = htr.OverLine;
			}
			else if ( y < 0 )
			{
				htr.ClosestLine = 0;
			}
			else
			{
				htr.ClosestLine = _lines.Count - 1;
			}

			// Hit test each cluster
			if ( htr.ClosestLine >= 0 && htr.ClosestLine < _lines.Count )
			{
				// Hit test the line
				var l = _lines[htr.ClosestLine];
				l.HitTest( x, ref htr );
			}

			// If we're not over the line, we're also not over the character
			if ( htr.OverLine < 0 )
				htr.OverCodePointIndex = -1;

			if ( htr.ClosestCodePointIndex < 0 )
			{
				if ( htr.ClosestLine == _lines.Count - 1 && _lines.Count > 0 )
					htr.ClosestCodePointIndex = _lines[_lines.Count - 1].End - 1;
				else
					htr.ClosestCodePointIndex = 0;
			}

			return htr;
		}


		/// <summary>
		/// Build map of all caret positions 
		/// </summary>
		void BuildCaretIndicies()
		{
			Layout();
			if ( _caretIndicies.Count == 0 )
			{
				foreach ( var r in _lines.SelectMany( x => x.Runs ) )
				{
					for ( int i = 0; i < r.Clusters.Length; i++ )
					{
						_caretIndicies.Add( r.Clusters[i] );
					}
				}
				_caretIndicies.Add( MeasuredLength );
				_caretIndicies = _caretIndicies.OrderBy( x => x ).Distinct().ToList();
			}
		}

		/// <summary>
		/// Retrieves a list of all valid caret positions
		/// </summary>
		public IReadOnlyList<int> CaretIndicies
		{
			get
			{
				BuildCaretIndicies();
				return _caretIndicies;
			}
		}

		/// <summary>
		/// Retrieves a list of all valid caret positions
		/// </summary>
		public IReadOnlyList<int> WordBoundaryIndicies
		{
			get
			{
				// Find word boundaries (if not already done)
				if ( _wordBoundaryIndicies.Count == 0 )
				{
					_wordBoundaryIndicies = WordBoundaryAlgorithm.FindWordBoundaries( _codePoints.AsSlice() ).ToList();
				}
				return _wordBoundaryIndicies;
			}
		}


		/// <summary>
		/// Retrieves a list of the indicies of the first code point in each line
		/// </summary>
		public IReadOnlyList<int> LineIndicies
		{
			get
			{
				return _lines.Select( x => x.Start ).ToList();
			}
		}


		/// <summary>
		/// Given a code point index, find the index in the CaretIndicies
		/// </summary>
		/// <param name="codePointIndex">The code point index to lookup</param>
		/// <returns>The index in the code point idnex in the CaretIndicies array</returns>
		public int LookupCaretIndex( int codePointIndex )
		{
			BuildCaretIndicies();
			int index = _caretIndicies.BinarySearch( codePointIndex );
			if ( index < 0 )
				index = ~index;
			return index;
		}

		/// <summary>
		/// Calculates useful information for displaying a caret
		/// </summary>
		/// <remarks>
		/// When altPosition is true, if the code point index indicates the first
		/// code point after a line break, the returned caret position will be the
		/// end of the previous line (instead of the start of the next line)
		/// </remarks>
		/// <param name="position">The caret position</param>
		/// <returns>A CaretInfo struct</returns>
		public CaretInfo GetCaretInfo( CaretPosition position )
		{
			// Empty text block?
			if ( _codePoints.Length == 0 || position.CodePointIndex < 0 || _lines.Count == 0 )
			{
				return CaretInfo.None;
			}

			// Past the measured length?
			if ( position.CodePointIndex > MeasuredLength )
			{
				return CaretInfo.None;
			}

			// Look up the caret index
			int cpii = LookupCaretIndex( position.CodePointIndex );

			// Create caret info
			var ci = new CaretInfo();
			ci.CodePointIndex = _caretIndicies[cpii];

			var frIndex = FindFontRunForCodePointIndex( position.CodePointIndex );
			FontRun fr = null;
			if ( frIndex >= 0 )
			{
				fr = _fontRuns[frIndex];

				if ( fr.Start == position.CodePointIndex && frIndex > 0 )
				{
					var frPrior = _fontRuns[frIndex - 1];
					if ( frPrior.End == position.CodePointIndex )
					{
						if ( position.AltPosition ||
							(frPrior.Direction == TextDirection.RTL &&
							 frPrior.RunKind != FontRunKind.TrailingWhitespace) )
						{
							fr = frPrior;
						}
					}
				}
			}
			else
			{
				var lastLine = _lines[_lines.Count - 1];
				if ( lastLine.RunsInternal.Count > 0 )
					fr = lastLine.RunsInternal[lastLine.RunsInternal.Count - 1];
			}

			if ( fr == null )
				return CaretInfo.None;

			// Setup caret coordinates
			ci.CaretXCoord = ci.CodePointIndex < 0 ? 0 : fr.GetXCoordOfCodePointIndex( ci.CodePointIndex );
			ci.CaretRectangle = CalculateCaretRectangle( ci, fr );
			ci.LineIndex = _lines.IndexOf( fr.Line );

			return ci;
		}

		SKRect CalculateCaretRectangle( CaretInfo ci, FontRun fr )
		{
			if ( ci.CodePointIndex < 0 )
				return SKRect.Empty;

			// Get the font run to be used for caret metrics
			fr = GetFontRunForCaretMetrics( ci, fr );

			// Setup the basic rectangle
			var rect = new SKRect();
			rect.Left = ci.CaretXCoord;
			rect.Top = fr.Line.YCoord + fr.Line.BaseLine + fr.Ascent;
			rect.Right = rect.Left;
			rect.Bottom = fr.Line.YCoord + fr.Line.BaseLine + fr.Descent;

			// Apply slant if italic
			if ( fr.Style.FontItalic )
			{
				rect.Left -= rect.Height / 14;
				rect.Right = rect.Left + rect.Height / 5;
			}

			return rect;
		}

		/// <summary>
		/// Internal helper to get the font run that should
		/// be used for caret metrics.
		/// </summary>
		/// <remarks>
		/// The returned font run is the font run of the previous
		/// character, or the same character if the first font run
		/// on the line.
		/// </remarks>
		/// <returns>The determined font run</returns>
		FontRun GetFontRunForCaretMetrics( CaretInfo ci, FontRun fr )
		{
			// Same font run?
			if ( ci.CodePointIndex > fr.Start )
				return fr;

			// Try to get the previous font run in this line
			var lineRuns = fr.Line.Runs as List<FontRun>;
			int index = lineRuns.IndexOf( fr );
			if ( index <= 0 )
				return fr;

			// Use the previous font run
			return lineRuns[index - 1];
		}


		/// <summary>
		/// Find the font run holding a code point index
		/// </summary>
		/// <param name="codePointIndex"></param>
		/// <returns></returns>
		public int FindFontRunForCodePointIndex( int codePointIndex )
		{
			// Past end of text?
			if ( codePointIndex > MeasuredLength )
				return -1;

			// Look up font run
			int frIndex = FontRuns.BinarySearch( codePointIndex, ( run, value ) =>
			{
				return (run.End - 1) - codePointIndex;
			} );
			if ( frIndex < 0 )
				frIndex = ~frIndex;

			if ( frIndex == _fontRuns.Count )
				frIndex = _fontRuns.Count - 1;

			if ( frIndex < 0 )
				return -1;

			// Return the font run
			var fr = _fontRuns[frIndex];
			System.Diagnostics.Debug.Assert( codePointIndex >= fr.Start );
			System.Diagnostics.Debug.Assert( codePointIndex <= fr.End );
			return frIndex;
		}

		/// <summary>
		/// Invalidate the layout
		/// </summary>
		void InvalidateLayout()
		{
			// Make sure style runs are valid (debug only)
			_styleRuns.CheckValid( _codePoints.Length );

			// Set layout flag
			_needsLayout = true;
		}

		/// <summary>
		/// Set if the current layout is dirty
		/// </summary>
		bool _needsLayout = true;

		/// <summary>
		/// Maximum width (wrap point, or null for no wrapping)
		/// </summary>
		float? _maxWidth;

		/// <summary>
		/// Width at which to wrap content
		/// </summary>
		float _maxWidthResolved = float.MaxValue;

		/// <summary>
		/// Maximum height (crop lines after this)
		/// </summary>
		float? _maxHeight;

		/// <summary>
		/// Maximum layout height
		/// </summary>
		float _maxHeightResolved = float.MaxValue;

		/// <summary>
		/// Prevents wrapping to a new line when reaching max width
		/// </summary>
		bool _noWrap;

		/// <summary>
		/// Maximum number of lines
		/// </summary>
		int? _maxLines;

		/// <summary>
		/// Maximum number of lines
		/// </summary>
		int _maxLinesResolved = int.MaxValue;

		/// <summary>
		/// Text alignment
		/// </summary>
		TextAlignment _textAlignment = TextAlignment.Auto;

		/// <summary>
		/// Base direction as set by user
		/// </summary>
		TextDirection _baseDirection = TextDirection.Auto;

		/// <summary>
		/// Base direction as resolved if auto
		/// </summary>
		TextDirection _resolvedBaseDirection;

		/// <summary>
		/// How text blocks will overflow
		/// </summary>
		TextOverflow _overflow;

		/// <summary>
		/// How text blocks will overflow
		/// </summary>
		WordBreakMode _breakMode;

		/// <summary>
		/// Re-usable buffers for text shaping results
		/// </summary>
		TextShaper.ResultBufferSet _textShapingBuffers = new TextShaper.ResultBufferSet();

		/// <summary>
		/// Reusable buffer for bidi data
		/// </summary>
		BidiData _bidiData = new BidiData();

		/// <summary>
		/// A list of font runs, after splitting by directionality, user styles and font fallback
		/// </summary>
		List<FontRun> _fontRuns = new List<FontRun>();

		/// <summary>
		/// Helper for splitting code into linebreaks
		/// </summary>
		LineBreaker _lineBreaker = new LineBreaker();

		/// <summary>
		/// The measured height
		/// </summary>
		float _measuredHeight;

		/// <summary>
		/// The measured width
		/// </summary>
		float _measuredWidth;

		/// <summary>
		/// The required left overhang
		/// </summary>
		float? _leftOverhang = null;

		/// <summary>
		/// The required left overhang
		/// </summary>
		float? _rightOverhang = null;

		/// <summary>
		/// Indicates if the text was truncated by max height/max lines limitations
		/// </summary>
		bool _truncated;

		/// <summary>
		/// The final laid out set of lines
		/// </summary>
		List<TextLine> _lines = new List<TextLine>();

		/// <summary>
		/// Calculated valid caret indicies
		/// </summary>
		List<int> _caretIndicies = new List<int>();

		/// <summary>
		/// Calculated word boundary caret indicies
		/// </summary>
		List<int> _wordBoundaryIndicies = new List<int>();

		/// <summary>
		/// Resolve the text alignment when set to Auto
		/// </summary>
		/// <returns>Resolved text alignment (left, right or center)</returns>
		internal TextAlignment ResolveTextAlignment()
		{
			if ( _textAlignment == TextAlignment.Auto )
				return _resolvedBaseDirection == TextDirection.LTR ? TextAlignment.Left : TextAlignment.Right;
			else
				return _textAlignment;
		}

		// Use the shared Bidi algo instance
		Bidi _bidi = Bidi.Instance.Value;

		/// <summary>
		/// Split into runs based on directionality and style switch points
		/// </summary>
		void BuildFontRuns()
		{
			var originalLength = _codePoints.Length;
			try
			{
				// Clearn unshaped run buffer
				_unshapedRuns.Clear();

				// Break supplied text into directionality runs
				_bidiData.Init( _codePoints.AsSlice(), (sbyte)_baseDirection );

				// If we have embedded directional overrides then change those
				// ranges to neutral
				if ( _hasTextDirectionOverrides )
				{
					// Save types
					_bidiData.SaveTypes();

					for ( int i = 0; i < _styleRuns.Count; i++ )
					{
						// Get the run
						var sr = _styleRuns[i];

						// Does it have a direction override?
						if ( sr.Style.TextDirection == TextDirection.Auto )
							continue;

						// Change the range to neutral with no brackets
						_bidiData.Types.SubSlice( sr.Start, sr.Length ).Fill( Directionality.ON );
						_bidiData.PairedBracketTypes.SubSlice( sr.Start, sr.Length ).Fill( PairedBracketType.n );
					}
				}

				// Process bidi
				_bidi.Process( _bidiData );

				var resolvedLevels = _bidi.ResolvedLevels;

				// Get resolved direction
				_resolvedBaseDirection = (TextDirection)_bidi.ResolvedParagraphEmbeddingLevel;

				// Now process the embedded runs
				if ( _hasTextDirectionOverrides )
				{
					// Restore types
					_bidiData.RestoreTypes();

					// Process each run individually
					for ( int i = 0; i < _styleRuns.Count; i++ )
					{
						// Get the run
						var sr = _styleRuns[i];

						// Does it have a direction override?
						if ( sr.Style.TextDirection == TextDirection.Auto )
							continue;

						// Get the style run bidi data
						var types = _bidiData.Types.SubSlice( sr.Start, sr.Length );
						var pbts = _bidiData.PairedBracketTypes.SubSlice( sr.Start, sr.Length );
						var pbvs = _bidiData.PairedBracketValues.SubSlice( sr.Start, sr.Length );

						// Get a temp buffer to store the results
						// (We can't use the Bidi's built in buffer because we're about to patch it)
						var levels = _bidiData.GetTempLevelBuffer( sr.Length );

						// Process this style run
						_bidi.Process( types, pbts, pbvs, (sbyte)sr.Style.TextDirection, _bidiData.HasBrackets, _bidiData.HasEmbeddings, _bidiData.HasIsolates, levels );

						// Copy result levels back to the full level set
						resolvedLevels.SubSlice( sr.Start, sr.Length ).Set( levels );
					}
				}

				// Get the list of directional runs
				var bidiRuns = BidiRun.CoalescLevels( resolvedLevels ).ToList();

				// Split...
				var pos = 0;
				int bidiRun = 0;
				int styleRun = 0;
				while ( pos < _codePoints.Length )
				{
					// Move to next bidi/style run
					if ( pos == bidiRuns[bidiRun].End )
						bidiRun++;
					if ( pos == _styleRuns[styleRun].End )
						styleRun++;

					// Work out where this run ends
					int nextPos = Math.Min( bidiRuns[bidiRun].End, _styleRuns[styleRun].End );

					// Add the run
					var dir = bidiRuns[bidiRun].Direction == Directionality.L ? TextDirection.LTR : TextDirection.RTL;
					AddDirectionalRun( _styleRuns[styleRun], pos, nextPos - pos, dir, _styleRuns[styleRun].Style );

					// Move to next position
					pos = nextPos;
				}

				System.Diagnostics.Debug.Assert( bidiRun == bidiRuns.Count - 1 );
				System.Diagnostics.Debug.Assert( styleRun == _styleRuns.Count - 1 );

				// Add the final run
				var dir2 = bidiRuns[bidiRun].Direction == Directionality.L ? TextDirection.LTR : TextDirection.RTL;
				AddDirectionalRun( _styleRuns[_styleRuns.Count - 1], pos, _codePoints.Length - pos, dir2, _styleRuns[styleRun].Style );

				// Flush runs
				FlushUnshapedRuns();

				// Split tabs
				SplitTabs();
			}
			catch ( Exception x )
			{
				throw new InvalidOperationException( $"Exception in BuildFontRuns() with original length of {originalLength} now {_codePoints.Length}, style run count {_styleRuns.Count}, font run count {_fontRuns.Count}, direction overrides: {_hasTextDirectionOverrides}", x );
			}
		}

		void SplitTabs()
		{
			int i = 0;
			while ( i < _fontRuns.Count )
			{
				var fr = _fontRuns[i];

				if ( FindFirstTabRun( fr, out var start, out var length ) )
				{
					var end = start + length;

					if ( start == fr.Start && end < fr.End )
					{
						var trailingRun = fr.Split( start + length );
						fr.RunKind = FontRunKind.Tabs;

						_fontRuns.Insert( ++i, trailingRun );
					}
					else if ( start > fr.Start && end < fr.End )
					{
						var tabRun = fr.Split( start );
						var trailingRun = tabRun.Split( start + length );
						tabRun.RunKind = FontRunKind.Tabs;

						_fontRuns.Insert( ++i, tabRun );
						_fontRuns.Insert( ++i, trailingRun );
					}
					else if ( start != fr.Start && end == fr.End )
					{
						var tabRun = fr.Split( start );
						tabRun.RunKind = FontRunKind.Tabs;

						_fontRuns.Insert( ++i, tabRun );
					}
					else
					{
						if ( start == fr.Start && end == fr.End )
						{
							fr.RunKind = FontRunKind.Tabs;
						}
						i++;
					}
				}
				else
				{
					i++;
				}
			}
		}

		bool FindFirstTabRun( FontRun fr, out int start, out int length )
		{
			start = 0;
			length = 0;

			for ( int i = fr.Start; i < fr.Start + fr.Length; i++ )
			{
				if ( _codePoints[i] == '\t' )
				{
					start = i;
					for ( int j = i; j < fr.Start + fr.Length; j++ )
					{
						if ( _codePoints[j] != '\t' ) break;
						length++;
					}
					break;
				}
			}

			return length > 0;
		}

		float CalculateTabWidth( FontRun fontRun )
		{
			const int tabSpaceCount = 4;

			var xcoord = fontRun.XCoord;
			var tabStopWidth = tabSpaceCount * fontRun.SpaceWidth;
			var tabStopsPassed = (int)(xcoord / tabStopWidth);
			var endingTabStopXPosition = (tabStopsPassed + fontRun.Length) * tabStopWidth;
			var requiredWidth = endingTabStopXPosition - xcoord;

			return requiredWidth;
		}

		/// <summary>
		/// Gets the Skia type face for a IStyle
		/// </summary>
		/// <param name="style">The style</param>
		/// <param name="ignoreFontVariants">When true, doesn't embolden super/sub scripts</param>
		/// <returns>The Skia typeface</returns>
		SKTypeface TypefaceFromStyle( IStyle style, bool ignoreFontVariants = false )
		{
			return (FontMapper ?? FontMapper.Default).TypefaceFromStyle( style, ignoreFontVariants );
		}


		/// <summary>
		/// Gets or sets the font mapper to be used by this TextBlock instance
		/// </summary>
		/// <remarks>
		/// When null, the default font mapper (FontMapper.Default) is used.
		/// </remarks>
		public FontMapper FontMapper
		{
			get;
			set;
		}

		/// <summary>
		/// Adds a run of directional text
		/// </summary>
		/// <param name="styleRun">The style run the directional run was created from</param>
		/// <param name="start">Index of the first code point _codePoints buffer</param>
		/// <param name="length">Number of code points in this run</param>
		/// <param name="direction">The direction of the text</param>
		/// <param name="style">The user supplied style for this run</param>
		void AddDirectionalRun( StyleRun styleRun, int start, int length, TextDirection direction, IStyle style )
		{
			// Quit if redundant...
			if ( length == 0 )
				return;

			// Get the typeface
			var typeface = TypefaceFromStyle( style );

			// Get the slice of code points
			var codePointsSlice = _codePoints.SubSlice( start, length );

			// Split into font fallback runs
			foreach ( var fontRun in FontFallback.GetFontRuns( codePointsSlice, typeface, style.ReplacementCharacter ) )
			{
				AddFontRun( styleRun, start + fontRun.Start, fontRun.Length, direction, style, fontRun.Typeface, typeface );
			}
		}

		/// <summary>
		/// Adds a run of single font text
		/// </summary>
		/// <param name="styleRun">The style run the directional run was created from</param>
		/// <param name="start">Index of the first code point _codePoints buffer</param>
		/// <param name="length">Number of code points in this run</param>
		/// <param name="direction">The direction of the text</param>
		/// <param name="style">The user supplied style for this run</param>
		/// <param name="typeface">The typeface of the run</param>
		/// <param name="asFallbackFor">The typeface this is a fallback for</param>
		void AddFontRun( StyleRun styleRun, int start, int length, TextDirection direction, IStyle style, SKTypeface typeface, SKTypeface asFallbackFor )
		{
			// Coalesc added font runs such that those with the exact same font, direction etc... are
			// all shaped together such that kerning is correct for the entire run - regardless of
			// non-typographic style changes (eg: underline, color etc...).
			var usr = new UnshapedRun()
			{
				styleRun = styleRun,
				start = start,
				length = length,
				direction = direction,
				style = style,
				typeface = typeface,
				asFallbackFor = asFallbackFor,
			};

			// If this run can't be shaped with the previous one then flush those accumlated so far
			if ( _unshapedRuns.Count > 0 && !_unshapedRuns[_unshapedRuns.Count - 1].CanShapeWith( usr ) )
			{
				FlushUnshapedRuns();
			}

			// Store it for now, actually shape it once we determine the next run is really
			// a different font
			_unshapedRuns.Add( usr );
		}

		void FlushUnshapedRuns()
		{
			// Quit if nothing to do?
			if ( _unshapedRuns.Count == 0 )
				return;

			if ( _unshapedRuns.Count == 1 )
			{
				// If there's only one accumulated shape run then we can shape and add it directly
				var usr = _unshapedRuns[0];
				_fontRuns.Add( CreateFontRun( usr.styleRun, _codePoints.SubSlice( usr.start, usr.length ), usr.direction, usr.style, usr.typeface, usr.asFallbackFor ) );
			}
			else
			{
				// There are multiple styled runs, but they're all using the exact same type face
				// so shape them all together to maintain correct kerning, but then break them
				// apart so they're rendered separately with correct style for each piece.
				var first = _unshapedRuns[0];
				var last = _unshapedRuns[_unshapedRuns.Count - 1];
				var spanningRun = CreateFontRun( first.styleRun, _codePoints.SubSlice( first.start, last.start + last.length - first.start ), first.direction, first.style, first.typeface, first.asFallbackFor );

				for ( int i = 1; i < _unshapedRuns.Count; i++ )
				{
					var newRun = spanningRun.Split( _unshapedRuns[i].start );

					spanningRun.StyleRun = _unshapedRuns[i - 1].styleRun;
					spanningRun.Style = _unshapedRuns[i - 1].style;

					_fontRuns.Add( spanningRun );
					spanningRun = newRun;
				}
				spanningRun.StyleRun = _unshapedRuns[_unshapedRuns.Count - 1].styleRun;
				spanningRun.Style = _unshapedRuns[_unshapedRuns.Count - 1].style;
				_fontRuns.Add( spanningRun );
			}

			_unshapedRuns.Clear();
		}

		List<UnshapedRun> _unshapedRuns = new List<UnshapedRun>();

		class UnshapedRun
		{
			public StyleRun styleRun;
			public int start;
			public int length;
			public TextDirection direction;
			public IStyle style;
			public SKTypeface typeface;
			public SKTypeface asFallbackFor;

			// Check if this unshaped run can be shaped with the next one
			public bool CanShapeWith( UnshapedRun next )
			{
				return typeface == next.typeface &&
					   style.FontSize == next.style.FontSize &&
						asFallbackFor == next.asFallbackFor &&
						direction == next.direction &&
						start + length == next.start;
			}
		}


		/// <summary>
		/// Helper to create a font run
		/// </summary>
		/// <param name="styleRun">The style run owning this font run</param>
		/// <param name="codePoints">The code points of the run</param>
		/// <param name="direction">The run direction</param>
		/// <param name="style">The user supplied style for this run</param>
		/// <param name="typeface">The typeface of the run</param>
		/// <param name="asFallbackFor">The original typeface this is a fallback for</param>
		/// <returns>A FontRun</returns>
		FontRun CreateFontRun( StyleRun styleRun, Slice<int> codePoints, TextDirection direction, IStyle style, SKTypeface typeface, SKTypeface asFallbackFor )
		{
			// Shape the text
			var shaper = TextShaper.ForTypeface( typeface );
			TextShaper.Result shaped;
			if ( style.ReplacementCharacter == '\0' )
				shaped = shaper.Shape( _textShapingBuffers, codePoints, style, direction, codePoints.Start, asFallbackFor, ResolveTextAlignment() );
			else
				shaped = shaper.ShapeReplacement( _textShapingBuffers, codePoints, style, codePoints.Start );

			// Create the run
			var fontRun = FontRun.Pool.Value.Get();
			fontRun.StyleRun = styleRun;
			fontRun.CodePointBuffer = _codePoints;
			fontRun.Start = codePoints.Start;
			fontRun.Length = codePoints.Length;
			fontRun.Style = style;
			fontRun.Direction = direction;
			fontRun.Typeface = typeface;
			fontRun.Glyphs = shaped.GlyphIndicies;
			fontRun.GlyphPositions = shaped.GlyphPositions;
			fontRun.RelativeCodePointXCoords = shaped.CodePointXCoords;
			fontRun.Clusters = shaped.Clusters;
			fontRun.Ascent = shaped.Ascent;
			fontRun.Descent = shaped.Descent;
			fontRun.Leading = shaped.Leading;
			fontRun.Width = shaped.EndXCoord.X;
			fontRun.SpaceWidth = typeface.ToFont( fontRun.Style.FontSize ).MeasureText( spaceCharacter );

			return fontRun;
		}

		/// <summary>
		/// Break the list of font runs into lines
		/// </summary>
		void BreakLines()
		{
			// Work out possible line break positions
			_lineBreaker.Reset( _codePoints.AsSlice() );

			var mandatoryOnly = !_maxWidth.HasValue || _breakMode == WordBreakMode.Character;
			var lineBreakPositions = _lineBreaker.GetBreaks( mandatoryOnly );

			int frIndexStartOfLine = 0;     // Index of the first font run in the current line
			int frIndex = 0;                // Index of the current font run
			int lbrIndex = 0;               // Index of the next unconsumed linebreak
			float consumedWidth = 0;        // Total width of all font runs placed on this line
			int frSplitIndex = -1;          // Index of the last font run that contained fitting break point
			int codePointIndexSplit = -1;   // Code point index of the last fitting break point measure position
			int codePointIndexWrap = -1;    // Code point index of the last fitting breaj point wrap position

			if ( !CheckHeightConstraints() )
				return;

			while ( frIndex < _fontRuns.Count )
			{
				// Get the font run, update it's position
				// and move to next
				var fr = _fontRuns[frIndex];
				fr.XCoord = consumedWidth;

				if ( fr.RunKind == FontRunKind.Tabs )
				{
					fr.Width = CalculateTabWidth( fr );
				}

				consumedWidth += fr.Width;

				float totalWidthToThisBreakPoint = 0;

				// Skip line breaks
				bool breakLine = false;
				while ( lbrIndex < lineBreakPositions.Count )
				{
					// Past this run?
					var lbr = lineBreakPositions[lbrIndex];
					if ( lbr.PositionMeasure < fr.Start )
					{
						lbrIndex++;
						continue;
					}
					if ( lbr.PositionMeasure >= fr.End )
						break;

					// Do we need to break
					totalWidthToThisBreakPoint = fr.XCoord + fr.LeadingWidth( lbr.PositionMeasure );
					if ( totalWidthToThisBreakPoint > _maxWidthResolved )
					{
						breakLine = true;
						break;
					}

					// It fits, remember that
					lbrIndex++;

					// Only mark that we have something that fits if we actually know something has (or will be) been consumed
					if ( totalWidthToThisBreakPoint > 0 || lbr.PositionWrap > lbr.PositionMeasure )
					{
						frSplitIndex = frIndex;
						codePointIndexSplit = lbr.PositionMeasure;
						codePointIndexWrap = lbr.PositionWrap;
					}

					if ( lbr.Required )
					{
						breakLine = true;
						break;
					}
				}

				// If we're on the last run and we've exceeded the width limit then force a break
				if ( !breakLine && frIndex + 1 == _fontRuns.Count && consumedWidth > _maxWidthResolved )
					breakLine = true;

				// Break the line here?
				if ( !breakLine )
				{
					frIndex++;
					continue;
				}

				// If there wasn't a line break anywhere in the line, then we need to force one
				// on a character boundary.  Also do this is we know we're on the last available line.
				var onLastLine = _maxLines.HasValue && _lines.Count == _maxLines.Value - 1;
				if ( frSplitIndex < 0 || onLastLine )
				{
					// Jake: Pulled from upstream repo after spending the whole
					//		 day trying to comprehend this big filthy function
					while ( frIndex > frIndexStartOfLine && _fontRuns[frIndex].XCoord > _maxWidthResolved )
					{
						frIndex--;
					}

					fr = _fontRuns[frIndex];
					var room = _maxWidthResolved - fr.XCoord;
					frSplitIndex = frIndex;
					codePointIndexSplit = fr.FindBreakPosition( room, frSplitIndex == frIndexStartOfLine );
					codePointIndexWrap = codePointIndexSplit;
					while ( codePointIndexWrap < _codePoints.Length && UnicodeClasses.LineBreakClass( _codePoints[codePointIndexWrap] ) == LineBreakClass.SP )
						codePointIndexWrap++;
				}

				// Split it
				fr = _fontRuns[frSplitIndex];
				if ( codePointIndexSplit == fr.Start )
				{
					// Split exactly before the run
				}
				else if ( codePointIndexSplit == fr.End )
				{
					// Split exactly after the run
					frSplitIndex++;
				}
				else
				{
					// Split in the middle of the run
					frSplitIndex++;
					_fontRuns.Insert( frSplitIndex, fr.Split( codePointIndexSplit ) );
				}

				// Trailing whitespace 
				int frTrailingWhiteSpaceIndex = frSplitIndex;
				while ( frTrailingWhiteSpaceIndex < _fontRuns.Count && _fontRuns[frTrailingWhiteSpaceIndex].Start < codePointIndexWrap )
				{
					if ( codePointIndexWrap < _fontRuns[frTrailingWhiteSpaceIndex].End )
					{
						var fr2 = _fontRuns[frTrailingWhiteSpaceIndex].Split( codePointIndexWrap );
						_fontRuns.Insert( frTrailingWhiteSpaceIndex + 1, fr2 );
					}

					frTrailingWhiteSpaceIndex++;
				}

				// Build the final line
				BuildLine( frIndexStartOfLine, frSplitIndex, frTrailingWhiteSpaceIndex );

				// Reset for the next line
				frSplitIndex = -1;
				frIndex = frTrailingWhiteSpaceIndex;
				frIndexStartOfLine = frIndex;
				consumedWidth = 0;

				// Check height constraints and quit if finished
				if ( !CheckHeightConstraints() )
					return;
			}

			// Build the final line
			if ( frIndexStartOfLine < _fontRuns.Count )
			{
				BuildLine( frIndexStartOfLine, _fontRuns.Count, _fontRuns.Count );
				CheckHeightConstraints();
			}
		}

		/// <summary>
		/// Construct a single line from the specified font run indicies
		/// </summary>
		/// <param name="frIndexStartOfLine">Index of the first font run in the line</param>
		/// <param name="frSplitIndex">Index of the last type</param>
		/// <param name="frTrailingWhiteSpaceIndex"></param>
		void BuildLine( int frIndexStartOfLine, int frSplitIndex, int frTrailingWhiteSpaceIndex )
		{
			// Create the line

			var line = TextLine.Pool.Value.Get();
			line.TextBlock = this;
			line.YCoord = _measuredHeight;

			// Add runs
			for ( int i = frIndexStartOfLine; i < frTrailingWhiteSpaceIndex; i++ )
			{
				// Tag trailing whitespace appropriately
				if ( i >= frSplitIndex )
				{
					var fr = _fontRuns[i];
					fr.RunKind = FontRunKind.TrailingWhitespace;
					if ( fr.Direction != _resolvedBaseDirection )
					{
						// What is this a fallback for?
						var asFallbackFor = TypefaceFromStyle( fr.Style, false );

						// Create a new font run over the same text span but using the base direction
						_fontRuns[i] = CreateFontRun( fr.StyleRun, fr.CodePoints, _resolvedBaseDirection, fr.Style, fr.Typeface, asFallbackFor );
					}
				}

				line.RunsInternal.Add( _fontRuns[i] );
			}

			// Add the line to the collection
			_lines.Add( line );

			// Lay it out
			LayoutLine( line );

			// Update y position
			_measuredHeight += line.Height;
		}

		/// <summary>
		/// Layout the font runs within a line
		/// </summary>
		/// <param name="line">The line to layout</param>
		void LayoutLine( TextLine line )
		{
			// How much to x-adjust all runs by to position correctly
			float xAdjust;

			// Which direction?
			if ( _resolvedBaseDirection == TextDirection.LTR )
			{
				xAdjust = LayoutLineLTR( line );
			}
			else
			{
				/*
                if (_useMSWordStyleRtlLayout)
                {
                    xAdjust = LayoutLineRTLWordStyle(line);
                }
                else
                */
				{
					xAdjust = LayoutLineRTL( line );
				}
			}

			// Process all runs, setting their final left-aligned x-position and 
			// calculating metrics for the line as a whole
			float maxAbove = 0;
			float maxBelow = 0;
			float maxAscent = 0;
			float maxDescent = 0;
			for ( int frIndex = 0; frIndex < line.Runs.Count; frIndex++ )
			{
				// Get the run
				var fr = line.Runs[frIndex];

				// Adjust run position
				fr.XCoord += xAdjust;

				var above = -fr.Ascent + fr.HalfLeading;
				var below = fr.Descent + fr.HalfLeading;

				if ( above > maxAbove )
					maxAbove = above;
				if ( below > maxBelow )
					maxBelow = below;

				if ( fr.Ascent < maxAscent )
					maxAscent = fr.Ascent;
				if ( fr.Descent > maxDescent )
					maxDescent = fr.Descent;
			}

			// Store line metrics
			line.MaxAscent = maxAscent;
			line.MaxDescent = maxDescent;
			line.Height = maxAbove + maxBelow;
			line.BaseLine = maxAbove;
		}

		/// <summary>
		/// Layout a line (LTR edition)
		/// </summary>
		/// <param name="line">The line to be laid out</param>
		/// <returns>A final x-adjustment to be applied to the line's font runs</returns>
		private float LayoutLineLTR( TextLine line )
		{
			float x = 0;
			float trailingWhitespaceWidth = 0;
			for ( int i = 0; i < line.Runs.Count; i++ )
			{
				var fr = line.Runs[i];
				if ( fr.Direction == TextDirection.LTR )
				{
					fr.XCoord = x;
					x += fr.Width;

					if ( fr.RunKind == FontRunKind.TrailingWhitespace )
						trailingWhitespaceWidth += fr.Width;
				}
				else
				{
					System.Diagnostics.Debug.Assert( fr.RunKind == FontRunKind.Normal );

					int j = i;
					while ( j + 1 < line.Runs.Count && line.Runs[j + 1].Direction == TextDirection.RTL )
						j++;

					int continueFrom = j;

					while ( j >= i )
					{
						fr = line.Runs[j];
						fr.XCoord = x;
						x += fr.Width;
						j--;
					}

					i = continueFrom;
				}
			}

			// Store content width
			line.Width = x - trailingWhitespaceWidth;

			return 0;
		}

		/// <summary>
		/// Layout a line (RTL edition)
		/// </summary>
		/// <param name="line">The line to be laid out</param>
		/// <returns>A final x-adjustment to be applied to the line's font runs</returns>
		private float LayoutLineRTL( TextLine line )
		{
			float x = 0;
			float trailingWhitespaceWidth = 0;
			for ( int i = 0; i < line.Runs.Count; i++ )
			{
				var fr = line.Runs[i];
				if ( fr.Direction == TextDirection.RTL )
				{
					x -= fr.Width;
					fr.XCoord = x;

					if ( fr.RunKind == FontRunKind.TrailingWhitespace )
						trailingWhitespaceWidth += fr.Width;
				}
				else
				{
					System.Diagnostics.Debug.Assert( fr.RunKind == FontRunKind.Normal );

					int j = i;
					while ( j + 1 < line.Runs.Count && line.Runs[j + 1].Direction == TextDirection.LTR )
						j++;

					int continueFrom = j;

					while ( j >= i )
					{
						fr = line.Runs[j];
						x -= fr.Width;
						fr.XCoord = x;
						j--;
					}

					i = continueFrom;
				}
			}

			// Work out final width
			float totalWidth = -x - trailingWhitespaceWidth;

			// Store content width
			line.Width = totalWidth;

			// Everything is currently laid out from 0 to the left, so we 
			// need to shift right to make it left aligned
			return totalWidth;
		}

		/// <summary>
		/// Layout a line (MS Word RTL edition)
		/// </summary>
		/// <param name="line">The line to be laid out</param>
		/// <returns>A final x-adjustment to be applied to the line's font runs</returns>
		private float LayoutLineRTLWordStyle( TextLine line )
		{
			float xPos = 0;
			float totalWidth = 0;
			float trailingWhitespaceWidth = 0;
			for ( int i = 0; i < line.Runs.Count; i++ )
			{
				// Get the direction of this group
				var groupDirection = line.Runs[i].Direction;

				// Count the number of runs with the same directionality
				int j = i;
				float groupWidth = 0;
				while ( j < line.Runs.Count && line.Runs[j].Direction == groupDirection )
				{
					if ( line.Runs[i].RunKind == FontRunKind.TrailingWhitespace )
						trailingWhitespaceWidth += line.Runs[i].Width;

					groupWidth += line.Runs[j].Width;
					j++;
				}

				totalWidth += groupWidth;

				// How many runs in the group
				var runsInGroup = j - i;

				// Place the runs in the group
				if ( groupDirection == TextDirection.LTR )
				{
					// Left to right group
					for ( ; i < j; i++ )
					{
						line.Runs[i].XCoord = xPos;
						xPos += line.Runs[i].Width;
					}
				}
				else
				{
					// Right to left group
					xPos += groupWidth;
					var x = xPos;
					for ( ; i < j; i++ )
					{
						x -= line.Runs[i].Width;
						line.Runs[i].XCoord = x;
					}
				}

				i = j - 1;
			}

			line.Width = xPos - trailingWhitespaceWidth;

			return _maxWidthResolved - (xPos - trailingWhitespaceWidth);
		}

		/// <summary>
		/// Finalize lines, positioning for horizontal alignment and 
		/// moving all glyphs into position (relative to the text block top left)
		/// </summary>
		void FinalizeLines()
		{
			// Work out the measured width
			foreach ( var l in _lines )
			{
				if ( l.Width > _measuredWidth )
					_measuredWidth = l.Width;
			}

			var ta = ResolveTextAlignment();
			foreach ( var line in _lines )
			{
				// Work out x-alignement adjust for this line
				float xAdjust = 0;
				switch ( ta )
				{
					case TextAlignment.Right:
						xAdjust = (_maxWidth ?? _measuredWidth) - line.Width;
						break;

					case TextAlignment.Center:
						xAdjust = ((_maxWidth ?? _measuredWidth) - line.Width) / 2;
						break;

					case TextAlignment.Justify:
						// Justify works by widening the spaces in-place rather than shifting the whole
						// line, so it leaves xAdjust at 0 and adjusts glyph positions itself.
						JustifyLine( line );
						break;
				}

				// Position each run
				for ( int frIndex = 0; frIndex < line.Runs.Count; frIndex++ )
				{
					var fr = line.Runs[frIndex];
					fr.Line = line;
					fr.XCoord += xAdjust;
					fr.MoveGlyphs( fr.XCoord, line.YCoord + line.BaseLine );
				}
			}

			// Remove the font runs that weren't assigned to a line because the text
			// was truncated
			while ( _fontRuns.Count > 0 && _fontRuns[_fontRuns.Count - 1].Line == null )
			{
				_fontRuns.RemoveAt( _fontRuns.Count - 1 );
			}
		}

		/// <summary>
		/// Distributes the slack on a line across its inter-word spaces so the line fills the available
		/// width (CSS <c>text-align: justify</c>). The last line of a paragraph is left unjustified and
		/// only left-to-right content is handled. Mutates glyph positions and code-point x-coords together
		/// so rendering and hit-testing stay consistent. Called from <see cref="FinalizeLines"/> while the
		/// glyph positions are still relative to their run, before they're moved into final position.
		/// </summary>
		void JustifyLine( TextLine line )
		{
			var runs = line.Runs;
			if ( runs.Count == 0 )
				return;

			// The last line of the block isn't justified.
			if ( line == _lines[_lines.Count - 1] )
				return;

			// Don't justify a line that ends at a hard line break (the last line of a paragraph).
			var lastCps = runs[runs.Count - 1].CodePoints;
			if ( lastCps.Length > 0 )
			{
				int last = lastCps[lastCps.Length - 1];
				if ( last == '\n' || last == '\r' || last == 0x2028 || last == 0x2029 )
					return;
			}

			// Right-to-left justification isn't implemented; leave those lines as laid out.
			for ( int i = 0; i < runs.Count; i++ )
			{
				if ( runs[i].Direction == TextDirection.RTL )
					return;
			}

			// How much width to make up, and how many spaces to spread it across.
			float target = _maxWidth ?? _measuredWidth;
			float slack = target - line.Width;
			if ( slack <= 0.01f )
				return;

			int gaps = 0;
			for ( int i = 0; i < runs.Count; i++ )
			{
				var run = runs[i];
				if ( run.RunKind == FontRunKind.TrailingWhitespace )
					break;

				var cps = run.CodePoints;
				for ( int c = 0; c < cps.Length; c++ )
				{
					if ( cps[c] == ' ' )
						gaps++;
				}
			}

			if ( gaps == 0 )
				return;

			float extra = slack / gaps;

			// Walk the runs left-to-right. Within a run, each code point is shifted right by the space
			// opened up before it; 'lineShift' carries that running total forward between runs.
			float lineShift = 0;
			for ( int i = 0; i < runs.Count; i++ )
			{
				var run = runs[i];
				run.XCoord += lineShift;

				if ( run.RunKind == FontRunKind.TrailingWhitespace )
					continue;

				var cps = run.CodePoints;

				// Per code-point shift: a space adds 'extra' to everything after it.
				var shift = new float[cps.Length];
				float acc = 0;
				for ( int c = 0; c < cps.Length; c++ )
				{
					shift[c] = acc;
					if ( cps[c] == ' ' )
						acc += extra;
				}

				// Glyph positions (rendering): map each glyph to its code point via the cluster.
				for ( int g = 0; g < run.GlyphPositions.Length; g++ )
				{
					int cp = run.Clusters[g] - run.Start;
					if ( cp >= 0 && cp < shift.Length )
						run.GlyphPositions[g].X += shift[cp];
				}

				// Code-point x-coords (hit-testing / caret) shifted the same way.
				for ( int c = 0; c < run.RelativeCodePointXCoords.Length && c < shift.Length; c++ )
					run.RelativeCodePointXCoords[c] += shift[c];

				run.Width += acc;
				lineShift += acc;
			}
		}

		/// <summary>
		/// Finds the font run containing a specified code point index
		/// </summary>
		/// <param name="from">The font run index to start from</param>
		/// <param name="codePointIndex">The code point index being searched for</param>
		/// <returns>The index of the font run containing the specified code point 
		/// index, or font run count if past the end</returns>
		int FindFontRunForCodePointIndex( int from, int codePointIndex )
		{
			int frIndex = from;
			while ( true )
			{
				var fr = _fontRuns[frIndex];
				if ( codePointIndex < fr.Start )
				{
					System.Diagnostics.Debug.Assert( frIndex > 0 );
					frIndex--;
				}
				else if ( codePointIndex >= fr.End )
				{
					frIndex++;
					if ( frIndex == _fontRuns.Count )
					{
						System.Diagnostics.Debug.Assert( codePointIndex == _codePoints.Length );
						return _codePoints.Length;
					}
				}
				else
				{
					return frIndex;
				}
			}
		}

		/// <summary>
		/// Re-usable buffer holding just the ellipsis character
		/// </summary>
		static Utf32Buffer ellipsis = new Utf32Buffer( "…" );

		static ushort[] spaceCharacter = { ' ' };

		/// <summary>
		/// Create a special font run containing the ellipsis character
		/// based on an existing run
		/// </summary>
		/// <param name="basedOn">The run to base the styling on</param>
		/// <returns>A new font run containing the ellipsis character</returns>
		FontRun CreateOverflowRun( FontRun basedOn )
		{
			// Get the type face
			var typeface = TypefaceFromStyle( basedOn.Style, true );

			// Split into font fallback runs (there should only ever be just one)
			var fontRun = FontFallback.GetFontRuns( ellipsis.AsSlice(), typeface ).Single();

			// Create the new run and mark is as a special run type for ellipsis
			var fr = CreateFontRun( basedOn.StyleRun, ellipsis.SubSlice( fontRun.Start, fontRun.Length ), _resolvedBaseDirection, basedOn.Style, fontRun.Typeface, typeface );
			fr.RunKind = FontRunKind.Ellipsis;

			// Done
			return fr;
		}

		/// <summary>
		/// Update the current line with a new font run containing the trailing ellipsis
		/// </summary>
		/// <param name="line">The line to be updated</param>
		/// <param name="postLayout">True if the ellipsis is being added post layout via a user call to AddEllipsis()</param>
		void AdornLineWithOverflowContent( TextLine line, bool postLayout = false )
		{
			var lastRun = line.Runs[line.Runs.Count - 1];

			// Don't adorn if the last run actually
			// has all the text...
			if ( !postLayout && lastRun.End == _codePoints.Length )
				return;

			// Don't adorn if overflow is set to clip
			if ( _overflow == TextOverflow.Clip )
				return;

			// Remove all trailing whitespace from the line
			for ( int i = line.Runs.Count - 1; i >= 0; i-- )
			{
				var r = line.Runs[i];
				if ( r.RunKind == FontRunKind.TrailingWhitespace )
				{
					line.RunsInternal.RemoveAt( i );
					if ( postLayout )
					{
						_fontRuns.Remove( r );
					}
				}
			}

			// Calculate the total width of the line
			float totalWidth = 0;
			for ( int i = 0; i < line.Runs.Count; i++ )
			{
				totalWidth += line.Runs[i].Width;
			}

			// Get the new last run (if any)
			if ( line.Runs.Count > 0 )
				lastRun = line.Runs[line.Runs.Count - 1];

			// Create a new run for the ellipsis
			var ellipsisRun = CreateOverflowRun( lastRun );

			// Work out how much room we've got.  If this is user request
			// to append the ellipsis and MaxWidth isn't set then make sure
			// we don't change our previously measured width be using that
			// previous measurement as the limit
			var maxWidth = _maxWidthResolved;

			var removeWidth = totalWidth + ellipsisRun.Width - maxWidth;
			if ( removeWidth > 0 )
			{
				for ( int i = line.Runs.Count - 1; i >= 0; i-- )
				{
					var fr = line.Runs[i];

					// Does this run have enough to remove?
					// No, remove it all
					if ( fr.Width < removeWidth )
					{
						removeWidth -= fr.Width;
						line.RunsInternal.RemoveAt( i );
						if ( postLayout )
							_fontRuns.Remove( fr );
						continue;
					}

					// Work out where to split this run
					int pos = fr.FindBreakPosition( fr.Width - removeWidth, false );
					if ( pos == fr.Start )
					{
						// Nothing fits, remove it all
						line.RunsInternal.RemoveAt( i );
						if ( postLayout )
							_fontRuns.Remove( fr );
					}
					else
					{
						// Split it
						var remaining = fr.Split( pos );

						// Keep the remaining part in case we need it later (not sure why,
						// but seems wise).
						if ( !postLayout )
						{
							_fontRuns.Insert( _fontRuns.IndexOf( fr ) + 1, remaining );
							//                            _fontRuns.Remove(fr);
						}
					}

					break;
				}
			}

			// Add it to the line
			line.RunsInternal.Add( ellipsisRun );

			if ( postLayout )
			{
				// No layout adjustmenst if this is post layout change
				LayoutLine( line );
			}
			else
			{
				// Remember old line height
				var oldHeight = line.Height;
				_measuredHeight -= line.Height;

				// Layout the line again
				LayoutLine( line );

				// Adjust height just in case it changed
				_measuredHeight += line.Height;
			}
		}

		/// <summary>
		/// Check if the current layout has exceeded any height restrictions
		/// and if so, remove any offending lines and optionally create the
		/// ellipsis at the end indicating the text has been truncated
		/// </summary>
		/// <returns>True if can continue adding lines; otherwise false</returns>
		bool CheckHeightConstraints()
		{
			// Special case for the first line not fitting
			if ( _lines.Count == 1 && _measuredHeight > _maxHeightResolved )
			{
				_truncated = true;
				_measuredWidth = 0;
				_lines.RemoveAt( 0 );
				return false;
			}

			// Have we exceeded the height limit
			if ( _measuredHeight > _maxHeightResolved || _maxHeightResolved <= 0 )
			{
				// Remove the last line (unless it's the only line)
				if ( _lines.Count > 1 )
				{
					_measuredHeight -= _lines[_lines.Count - 1].Height;
					_lines.RemoveAt( _lines.Count - 1 );
				}
				if ( _lines.Count > 0 )
				{
					AdornLineWithOverflowContent( _lines[_lines.Count - 1] );
				}
				_truncated = true;
				return false;
			}

			// Have we hit the line count limit?
			if ( _lines.Count >= _maxLinesResolved )
			{
				if ( _lines.Count > 0 )
				{
					AdornLineWithOverflowContent( _lines[_lines.Count - 1] );
				}
				_truncated = true;
				return false;
			}

			// Are we nowrap and overflowed?
			if ( _noWrap && _overflow != TextOverflow.None && _lines.Count > 0 )
			{
				AdornLineWithOverflowContent( _lines[_lines.Count - 1] );
				_truncated = true;
				return false;
			}

			return true;
		}

		/// <summary>
		/// Resets and internal object and memory pools.
		/// </summary>
		/// <remarks>
		/// For performance reasons and to reduce pressure on the 
		/// garbage collector, RichTextKit maintains several internal
		/// per-thread memory and object pools.
		/// 
		/// If you create a very large text block, these pools will be 
		/// enlarged to cope with content of larger text blocks.
		/// 
		/// This method can be used to reset those pools to reclaim the 
		/// extra memory they consumed.
		/// 
		/// In general you can ignore this method, unless you know you're
		/// working with very large text blocks (which you shouldn't be
		/// anyway, since a text block is only supposed to be a single
		/// paragraph).
		/// /// </remarks>
		public static void ResetPooledMemory()
		{
			TextLine.Pool.Value = new ObjectPool<TextLine>();
			FontRun.Pool.Value = new ObjectPool<FontRun>();
			StyleRun.Pool.Value = new ObjectPool<StyleRun>();
			Bidi.Instance.Value = new Bidi();
		}



	}
}
