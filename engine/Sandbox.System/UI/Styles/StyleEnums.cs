
namespace Sandbox.UI;

/// <summary>
/// Possible values for the "overflow" CSS rule, dictating what to do with content that is outside of a panels bounds.
/// </summary>
public enum OverflowMode
{
	/// <summary>
	/// Overflowing content is visible at all times.
	/// </summary>
	Visible = 0,

	/// <summary>
	/// Overflowing contents are hidden at all times.
	/// </summary>
	Hidden = 1,

	/// <summary>
	/// Overflowing contents are hidden, but can be scrolled to.
	/// </summary>
	Scroll = 2,

	/// <summary>
	/// Overflowing contents are clipped, but unlike <see cref="Hidden"/>, does not create a scroll container and does not affect layout.
	/// </summary>
	Clip = 3,

	/// <summary>
	/// Child elements that extend outside the panel's bounds are hidden entirely, rather than pixel-clipped.
	/// Does not create a scroll container and does not affect layout.
	/// </summary>
	ClipWhole = 4
}

/// <summary>
/// Possible values for <c>align-items</c> CSS property.
/// </summary>
public enum Align
{
	Auto = 0,
	FlexStart = 1,
	Center = 2,
	FlexEnd = 3,
	Stretch = 4,
	Baseline = 5,
	SpaceBetween = 6,
	SpaceAround = 7,
	SpaceEvenly = 8
}

/// <summary>
/// Possible values for <c>position</c> CSS property.
/// </summary>
public enum PositionMode
{
	/// <summary>
	/// Default, the <c>top</c>, <c>right</c>, <c>bottom</c>, <c>left</c>, and <c>z-index</c> properties have no effect.
	/// </summary>
	Static = 0,

	/// <summary>
	/// Enables <c>top</c>, <c>right</c>, <c>bottom</c>, <c>left</c>, and <c>z-index</c> to offset the element from its
	/// would-be position with <see cref="Static"/>.
	/// </summary>
	Relative = 1,

	/// <summary>
	/// Same as <see cref="Relative"/>, but the elements size does not affect other elements at all.
	/// </summary>
	Absolute = 2
}


/// <summary>
/// Possible values for <c>flex-direction</c> CSS property.
/// </summary>
public enum FlexDirection
{
	/// <summary>
	/// A column, align items from top to bottom.
	/// </summary>
	Column = 0,

	/// <summary>
	/// A reverse column, align items from bottom to top.
	/// </summary>
	ColumnReverse = 1,

	/// <summary>
	/// A row, align items from left to right.
	/// </summary>
	Row = 2,

	/// <summary>
	/// A reverse row, align items from right to left.
	/// </summary>
	RowReverse = 3
}

/// <summary>
/// Possible values for <c>justify-content</c> CSS property.
/// </summary>
public enum Justify
{
	/// <summary>
	/// [OOOO            ]
	/// </summary>
	FlexStart = 0,

	/// <summary>
	/// [      OOOO      ]
	/// </summary>
	Center = 1,

	/// <summary>
	/// [            OOOO]
	/// </summary>
	FlexEnd = 2,

	/// <summary>
	/// [O    O    O    O]
	/// </summary>
	SpaceBetween = 3,

	/// <summary>
	/// [ O   O   O   O ]
	/// </summary>
	SpaceAround = 4,

	/// <summary>
	/// [  O  O  O  O  ]
	/// </summary>
	SpaceEvenly = 5
}

/// <summary>
/// Possible values for <c>display</c> CSS property.
/// </summary>
public enum DisplayMode
{
	/// <summary>
	/// Display via CSS flexbox.
	/// </summary>
	Flex = 0,

	/// <summary>
	/// Do not display at all.
	/// </summary>
	None = 1,

	/// <summary>
	/// Causes an element's children to appear as if they were direct children of the element's parent, ignoring the element itself. This can be useful when a wrapper element should be ignored.
	/// </summary>
	Contents = 2
}

/// <summary>
/// Possible values for <c>pointer-events</c> CSS property.
/// </summary>
public enum PointerEvents
{
	/// <summary>
	/// Accept all events in all cases.
	/// </summary>
	All = 0,

	/// <summary>
	/// Do not accept any pointer events.
	/// </summary>
	None = 1
}

/// <summary>
/// Possible values for <c>flex-wrap</c> CSS property.
/// </summary>
public enum Wrap
{
	/// <summary>
	/// Elements will be laid out in a single line.
	/// </summary>
	NoWrap = 0,

	/// <summary>
	/// Elements will be moved to subsequent lines on overflow.
	/// </summary>
	Wrap = 1,

	/// <summary>
	/// Same as <see cref="Wrap"/>, but the line order will be reversed, i.e. if one item overflows the width,
	/// it will be placed on the first line, and the others will be placed on the second line.
	/// </summary>
	WrapReverse = 2
}

/// <summary>
/// Possible values for <c>text-align</c> CSS property.
/// </summary>
public enum TextAlign
{
	/// <summary>
	/// Unused.
	/// </summary>
	Auto = 0,

	/// <summary>
	/// Align the text to the left.
	/// </summary>
	Left = 1,

	/// <summary>
	/// Align the text to the horizontal center.
	/// </summary>
	Center = 2,

	/// <summary>
	/// Align the text to the right.
	/// </summary>
	Right = 3
}

/// <summary>
/// Possible values for <c>text-overflow</c> CSS property.
/// </summary>
public enum TextOverflow
{
	/// <summary>
	/// Display overflown text.
	/// </summary>
	None = 0,

	/// <summary>
	/// Replace part of the text near the overflow point with ellipsis, and cut off the rest.
	/// </summary>
	Ellipsis = 1,

	/// <summary>
	/// Visually cut off the overflowing text.
	/// </summary>
	Clip = 2
}

/// <summary>
/// Possible values for <c>word-break</c> CSS property.
/// </summary>
public enum WordBreak
{
	/// <summary>
	/// Break overflowing lines at the closest word.
	/// </summary>
	Normal,

	/// <summary>
	/// Break overflowing lines at the closest character.
	/// </summary>
	BreakAll
}


/// <summary>
/// Possible values for <c>text-transform</c> CSS property.
/// </summary>
public enum TextTransform
{
	/// <summary>
	/// No change, default.
	/// </summary>
	None = 0,

	/// <summary>
	/// Capitalize each word.
	/// </summary>
	Capitalize = 1,

	/// <summary>
	/// Make every character capital.
	/// </summary>
	Uppercase = 2,

	/// <summary>
	/// Make every character lowercase.
	/// </summary>
	Lowercase = 3
}

/// <summary>
/// Possible values for <c>text-decoration-skip-ink</c> CSS property.
/// </summary>
public enum TextSkipInk
{
	/// <summary>
	/// Don't overlap any glyphs.
	/// </summary>
	All = 0,

	/// <summary>
	/// Overlap all glyphs.
	/// </summary>
	None = 1,
}

/// <summary>
/// Possible values for <c>text-decoration-style</c> CSS property.
/// </summary>
public enum TextDecorationStyle
{
	/// <summary>
	/// Draw a single solid line.
	/// </summary>
	Solid = 0,

	/// <summary>
	/// Draw two solid lines.
	/// </summary>
	Double = 1,

	/// <summary>
	/// Draw a dotted line.
	/// </summary>
	Dotted = 2,

	/// <summary>
	/// Draw a dashed line.
	/// </summary>
	Dashed = 3,

	/// <summary>
	/// Draw a wavy/squiggly line.
	/// </summary>
	Wavy = 4,
}

/// <summary>
/// Possible values for <c>text-decoration</c> CSS property.
/// </summary>
[Flags]
public enum TextDecoration
{
	/// <summary>
	/// No decoration, default.
	/// </summary>
	None = 0,

	/// <summary>
	/// Underline the text.
	/// </summary>
	Underline = 2,

	/// <summary>
	/// Strike through, a line in the middle of the text.
	/// </summary>
	LineThrough = 4,

	/// <summary>
	/// A line above the text.
	/// </summary>
	Overline = 8,
}

/// <summary>
/// Possible values for <c>white-space</c> CSS property.
/// </summary>
public enum WhiteSpace
{
	/// <summary>
	/// Sequences of white spaces are collapsed, text will wrap when necessary.  Default.
	/// </summary>
	Normal = 0,

	/// <summary>
	/// Sequences of white spaces are collapsed and linebreaks are suppressed.
	/// </summary>
	NoWrap = 1,

	/// <summary>
	/// Sequences of white spaces are collapsed, text will wrap when necessary, linebreaks are preserved.
	/// </summary>
	PreLine = 2,

	/// <summary>
	/// Sequences of white space are preserved, lines are only broken at newline characters in the source.
	/// </summary>
	Pre = 3
}

/// <summary>
/// Possible values for <c>font-style</c> CSS property.
/// </summary>
[Flags]
public enum FontStyle
{
	/// <summary>
	/// No font styling, default.
	/// </summary>
	None = 0,

	/// <summary>
	/// Italic/cursive slanted text.
	/// </summary>
	Italic = 2,

	/// <summary>
	/// Non cursive slanted text, if the font supports it, italic otherwise.
	/// </summary>
	Oblique = 4,
}

/// <summary>
/// Possible values for <c>font-variant-numeric</c> CSS property.
/// </summary>
public enum FontVariantNumeric
{
	/// <summary>
	/// Default numeric glyph behavior.
	/// </summary>
	Normal = 0,

	/// <summary>
	/// Use tabular-width digits if the font provides them.
	/// </summary>
	TabularNums = 1,
}

/// <summary>
/// Possible values for <c>image-rendering</c> CSS property.
/// </summary>
public enum ImageRendering
{
	/// <summary>
	/// <a href="https://en.wikipedia.org/wiki/Anisotropic_filtering">Anisotropic</a> filtering.
	/// </summary>
	Anisotropic = 0,

	/// <summary>
	/// <a href="https://en.wikipedia.org/wiki/Bilinear_interpolation">Anisotropic</a> interpolation/filtering.
	/// </summary>
	Bilinear = 1,

	/// <summary>
	/// <a href="https://en.wikipedia.org/wiki/Trilinear_filtering">Trilinear</a> filtering.
	/// </summary>
	Trilinear = 2,

	/// <summary>
	/// No filtering.
	/// </summary>
	Point = 3,
}

/// <summary>
/// State of <c>fill</c> setting of <c>border-image-slice</c> (<c>border-image</c>) CSS property.
/// </summary>
public enum BorderImageFill
{
	/// <summary>
	/// Do not fill the middle of the container with the border's background image.
	/// </summary>
	Unfilled,

	/// <summary>
	/// Do fill the middle of the container with the border's background image.
	/// </summary>
	Filled
};

/// <summary>
/// Possible values for <c>border-image-repeat</c> (<c>border-image</c>) CSS property.
/// </summary>
public enum BorderImageRepeat
{
	/// <summary>
	/// The source image's edge regions are stretched to fill the gap between each border.
	/// </summary>
	Stretch,

	/// <summary>
	/// The source image's edge regions are tiled (repeated) to fill the gap between each border. Tiles may be stretched to achieve the proper fit.
	/// </summary>
	Round

	// Missing Repeat
	// Missing Space
};


/// <summary>
/// Possible values for <c>background-repeat</c> CSS property.
/// </summary>
public enum BackgroundRepeat
{
	/// <summary>
	/// Repeat the background image on X and Y axises.
	/// </summary>
	Repeat = 0,

	/// <summary>
	/// Repeat the background image on X axis.
	/// </summary>
	RepeatX = 1,

	/// <summary>
	/// Repeat the background image on Y axis.
	/// </summary>
	RepeatY = 2,

	/// <summary>
	/// Do not repeat the background image.
	/// </summary>
	NoRepeat = 3,

	/// <summary>
	/// Stretch the edges of the image to fill empty space.
	/// </summary>
	Clamp = 4
}

/// <summary>
/// Possible values for <c>mask-mode</c> CSS property.
/// </summary>
public enum MaskMode
{
	/// <summary>
	/// If the mask-image property is of type 'mask-source', the luminance values of the mask layer image should be used as the mask values.
	/// If it is of type 'image', the alpha values of the mask layer image should be used as the mask values.
	/// </summary>
	MatchSource = 0,

	/// <summary>
	/// The alpha channel values of the mask layer image should be used as the mask values.
	/// </summary>
	Alpha = 1,

	/// <summary>
	/// The luminance values of the mask layer image should be used as the mask values.
	/// </summary>
	Luminance = 2
}

/// <summary>
/// Possible values for <c>mask-scope</c> CSS property.
/// </summary>
public enum MaskScope
{
	/// <summary>
	/// Standard mask.
	/// </summary>
	Default,

	/// <summary>
	/// Mask used for filters.
	/// </summary>
	Filter
}

/// <summary>
/// Possible values for <c>font-smooth</c> CSS property.
/// </summary>
public enum FontSmooth
{
	/// <summary>
	/// Let us decide (we'll anti-alias where available)
	/// </summary>
	Auto,

	/// <summary>
	/// Turn font smoothing off
	/// </summary>
	Never,

	/// <summary>
	/// Always anti-alias
	/// </summary>
	Always
}

public enum ObjectFit
{
	/// <summary>
	/// The content is sized to fill the element's content box. This does not preserve aspect ratio.
	/// </summary>
	Fill,

	/// <summary>
	/// The content is scaled to maintain its aspect ratio while fitting within the element's content box
	/// </summary>
	Contain,

	/// <summary>
	/// The content is sized to maintain its aspect ratio while filling the element's entire content box
	/// </summary>
	Cover,

	/// <summary>
	/// The content is not resized
	/// </summary>
	None
}
