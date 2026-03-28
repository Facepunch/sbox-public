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
using Sandbox.UI;

namespace Topten.RichTextKit
{
	/// <summary>
	/// A basic implementation of IStyle interface provides styling 
	/// information for a run of text.
	/// </summary>
	public class Style : IStyle
	{
		public Style Copy()
		{
			var s = new Style();
			s.FontFamily = FontFamily;
			s.FontSize = FontSize;
			s.FontWeight = FontWeight;
			s.FontItalic = FontItalic;
			s.Underline = Underline;
			s.StrikeThrough = StrikeThrough;
			s.LineHeight = LineHeight;
			s.TextColor = TextColor;
			s.UnderlineColor = UnderlineColor;
			s.StrokeThickness = StrokeThickness;
			s.StrokeInkSkip = StrokeInkSkip;
			s.UnderlineOffset = UnderlineOffset;
			s.OverlineOffset = OverlineOffset;
			s.StrikeThroughOffset = StrikeThroughOffset;
			s.UnderlineStrokeType = UnderlineStrokeType;
			s.BackgroundColor = BackgroundColor;
			s.LetterSpacing = LetterSpacing;
			s.WordSpacing = WordSpacing;
			s.FontVariant = FontVariant;
			s.FontVariantNumeric = FontVariantNumeric;
			s.TextDirection = TextDirection;
			s.ReplacementCharacter = ReplacementCharacter;
			s._textEffects = _textEffects?.ToList();
			return s;
		}

		/// <summary>
		/// The font family for text this text run (defaults to "Arial").
		/// </summary>
		public string FontFamily { get; set; } = "Arial";

		/// <summary>
		/// The font size for text in this run (defaults to 16).
		/// </summary>
		public float FontSize { get; set; } = 16;

		/// <summary>
		/// The font weight for text in this run (defaults to 400).
		/// </summary>
		public int FontWeight { get; set; } = 400;

		/// <summary>
		/// True if the text in this run should be displayed in an italic
		/// font; otherwise False (defaults to false).
		/// </summary>
		public bool FontItalic { get; set; }

		/// <summary>
		/// The underline style for text in this run (defaults to None).
		/// </summary>
		public UnderlineStyle Underline { get; set; } = UnderlineStyle.None;

		/// <summary>
		/// The strike through style for the text in this run (defaults to None).
		/// </summary>
		public StrikeThroughStyle StrikeThrough { get; set; } = StrikeThroughStyle.None;

		/// <summary>
		/// The line height for text in this run as a multiplier (defaults to 1.0).
		/// </summary>
		public float LineHeight { get; set; } = 1.0f;

		/// <summary>
		/// The text color for text in this run (defaults to black).
		/// </summary>
		public SKColor TextColor { get; set; } = new SKColor( 0xFF000000 );

		/// <summary>
		/// The underline color for the text in this run (defaults to the current text color)
		/// </summary>
		public SKColor? UnderlineColor { get; set; }

		/// <summary>
		/// Sets the underline or strike-through stroke thickness(defaults to the current strike-through or underline thickness depending on context)
		/// </summary>
		public float? StrokeThickness { get; set; }

		/// <summary>
		/// Sets whether to draw underlines or overlines over glyphs or not
		/// </summary>
		public bool StrokeInkSkip { get; set; } = true;

		/// <summary>
		/// Y Offset for the underline, this value is added to the base value!
		/// </summary>
		public float UnderlineOffset { get; set; }

		/// <summary>
		/// Y Offset for the overline, this value is added to the base value!
		/// </summary>
		public float OverlineOffset { get; set; }

		/// <summary>
		/// Y Offset for the strike-through, this value is added to the base value!
		/// </summary>
		public float StrikeThroughOffset { get; set; }

		/// <summary>
		/// Sets the stroke line style for underline/overline/strike throughs
		/// </summary>
		public UnderlineType UnderlineStrokeType { get; set; }

		/// <summary>
		/// The background color of this run (no background is painted by default).
		/// </summary>
		public SKColor BackgroundColor { get; set; } = SKColor.Empty;

		/// <summary>
		/// The character spacing for text in this run (defaults to 0).
		/// </summary>
		public float LetterSpacing { get; set; }

		/// <summary>
		/// The extra space between words in this run (defaults to 0).
		/// </summary>
		public float WordSpacing { get; set; }

		/// <summary>
		/// The font variant (ie: super/sub-script) for text in this run.
		/// </summary>
		public FontVariant FontVariant { get; set; } = FontVariant.Normal;

		/// <summary>
		/// Numeric variant selection (eg: tabular-width numerals).
		/// </summary>
		public FontVariantNumeric FontVariantNumeric { get; set; } = FontVariantNumeric.Normal;

		/// <summary>
		/// Text direction override for this span
		/// </summary>
		public TextDirection TextDirection { get; set; } = TextDirection.Auto;

		/// <inheritdoc />
		public char ReplacementCharacter { get; set; } = '\0';

		/// <summary>
		/// Add a text effect to this style
		/// </summary>
		public void AddEffect( TextEffect textEffect )
		{
			if ( _textEffects == null )
				_textEffects = new List<TextEffect>();

			_textEffects.Add( textEffect );
		}


		/// <summary>
		/// Remove all text effects
		/// </summary>
		public void ClearEffects()
		{
			_textEffects?.Clear();
		}

		/// <summary>
		/// Effects to apply
		/// </summary>
		public IEnumerable<TextEffect> TextEffects => _textEffects;
		List<TextEffect> _textEffects;

	}
}
