using System.Text;
using Sandbox.Layout;

namespace Sandbox.UI;

/// <summary>
/// Bridges a real panel subtree to one shaped paragraph. Only text and non-replaced inline panels are
/// accepted. Labels remain measured leaves in the tree, but their parent context owns line fitting.
/// Supports left-aligned normal/nowrap text, font styles and simple decorations. Inline box styles,
/// transforms, mixed block content, replaced content, per-span whitespace and text-transform are not implemented.
/// </summary>
internal sealed class InlineParagraph : IInlineContent, IDisposable
{
	internal sealed record Run( Label Owner, string Text, int Start, List<(int Start, int End)> Sources,
		Topten.RichTextKit.Style Style );

	private static readonly string[] BoxProperties =
	[
		"padding-left", "padding-top", "padding-right", "padding-bottom",
		"margin-left", "margin-top", "margin-right", "margin-bottom", "border-left-width", "border-top-width",
		"border-right-width", "border-bottom-width", "outline-width", "width", "height", "min-width", "min-height", "max-width", "max-height"
	];

	private readonly Panel _panel;
	internal readonly TextBlock Text = new();
	private readonly List<Run> _runs = new();
	private List<Panel> _participants = new();
	private List<Panel> _nextParticipants = new();
	private int _contentHash;
	private bool _initialized;
	private bool _contentDirty;
	private string _mixBlendMode;
	private InlineContentLayout _finalLayout;
	private Vector2 _finalOrigin;
	internal Vector2 Origin => _panel.Box.Rect.Position + new Vector2(
		_panel.LayoutTree.Node.LayoutPadding( PhysicalEdge.Left ) + _panel.LayoutTree.Node.LayoutBorder( PhysicalEdge.Left ),
		_panel.LayoutTree.Node.LayoutPadding( PhysicalEdge.Top ) + _panel.LayoutTree.Node.LayoutBorder( PhysicalEdge.Top ) ) - _panel.ScrollOffset;

	internal InlineParagraph( Panel panel )
	{
		_panel = panel;
		Text.OnTextureChanged = panel.MarkRenderDirty;
	}

	internal static bool CanFormat( Panel panel )
	{
		if ( panel.ComputedStyle?.Display != DisplayMode.Block || panel.LayoutTree.IsMeasureDefined || !panel.HasChildren
			|| !SupportsTextStyle( panel.ComputedStyle ) ) return false;
		bool hasInline = false;
		foreach ( var child in panel._children )
		{
			if ( child?.LayoutTree is null ) continue;
			if ( child.ComputedStyle?.Display == DisplayMode.None ) continue;
			if ( !CanParticipate( child, panel.ComputedStyle ) ) return false;
			// Generated text can join explicit inline siblings, but must not opt legacy blocks in.
			hasInline |= child.ComputedStyle?.Display == DisplayMode.Inline;
		}
		return hasInline;
	}

	private static bool SupportsTextStyle( Styles style ) =>
		style.TextAlign == TextAlign.Left && style.TextOverflow == TextOverflow.None
		&& style.WhiteSpace is WhiteSpace.Normal or WhiteSpace.NoWrap
		&& style.TextTransform == TextTransform.None
		&& (style.TextShadow is null || style.TextShadow.IsNone || !style.TextShadow.Any())
		&& style.TextStrokeWidth?.Value == 0 && style.TextGradient.ColorOffsets.IsDefaultOrEmpty
		&& style.BackgroundClip != BackgroundClip.Text;

	private static bool CanParticipate( Panel child, Styles paragraphStyle )
	{
		if ( child.ComputedStyle is not { } style ) return false;
		if ( style.Display == DisplayMode.None ) return true;
		if ( style.Position != PositionMode.Static || !SupportsTextStyle( style )
			|| style.WhiteSpace != paragraphStyle.WhiteSpace || style.FontSmooth != paragraphStyle.FontSmooth
			|| style.TextFilter != paragraphStyle.TextFilter || style.WordBreak != paragraphStyle.WordBreak
			|| style.Opacity != 1 || !(style.Transform?.IsEmpty() ?? true)
			|| child.HasBackground || child.HasFilter || child.HasBackdropFilter || style.MaskImage is not null
			|| (style.BoxShadow is not null && !style.BoxShadow.IsNone && style.BoxShadow.Any())
			|| style.Overflow != OverflowMode.Visible || style.MixBlendMode != paragraphStyle.MixBlendMode ) return false;
		// These boxes cannot be represented by text fragments yet. Fall back rather than erase them.
		foreach ( var property in BoxProperties )
			if ( !style.IsDefault( property ) ) return false;
		if ( child is Label label )
			return child.GetType() == typeof( Label ) && !label.IsRich && label.Multiline && child.Parent is not TextEntry
				&& (style.Display == DisplayMode.Inline || style.Display == DisplayMode.Flex && label.IsGeneratedText);
		if ( style.Display != DisplayMode.Inline || child.GetType() != typeof( Panel ) || child.LayoutTree.IsMeasureDefined ) return false;
		if ( child._children is not null )
			foreach ( var descendant in child._children )
				if ( descendant?.LayoutTree is not null && !CanParticipate( descendant, paragraphStyle ) ) return false;
		return true;
	}

	internal void Invalidate()
	{
		// Deferred deletion can remove owners after PreLayout, before the layout engine measures us.
		_contentDirty = true;
		_panel.LayoutTree?.MarkDirty();
		_panel.SetNeedsPreLayout();
		_panel.MarkRenderDirty();
	}

	internal void Update( bool preparedOnly = false )
	{
		_contentDirty = false;
		Collect( _panel, _nextParticipants, preparedOnly );
		foreach ( var old in _participants )
		{
			if ( _nextParticipants.Contains( old ) ) continue;
			if ( old.InlineOwner != this ) continue;
			old.InlineOwner = null;
			old.LayoutTree?.MarkDirty();
			old.MarkRenderDirty();
		}
		_participants.Clear();
		(_participants, _nextParticipants) = (_nextParticipants, _participants);
		var hash = new HashCode();
		// Position, opacity and box styling affect descriptors, not the shaped text or selection.
		var style = _panel.ComputedStyle;
		if ( _mixBlendMode != style.MixBlendMode )
		{
			_mixBlendMode = style.MixBlendMode;
			_panel.MarkRenderDirty();
		}
		hash.Add( HashCode.Combine( style.FontFamily, style.FontSize, style.FontWeight, style.FontStyle,
			style.FontVariantNumeric, style.FontColor, style.WhiteSpace, style.WordBreak ) );
		hash.Add( HashCode.Combine( style.LetterSpacing, style.WordSpacing, style.LineHeight, style.FontSmooth,
			style.TextFilter, style.TextDecorationLine, style.TextDecorationColor, style.TextDecorationStyle ) );
		hash.Add( HashCode.Combine( style.TextDecorationThickness, style.TextDecorationSkipInk,
			style.TextUnderlineOffset, style.TextOverlineOffset, style.TextLineThroughOffset ) );
		foreach ( var item in _participants )
		{
			item.InlineOwner = this;
			hash.Add( item );
			if ( item is Label label )
			{
				hash.Add( label._textBlock?.Text );
				hash.Add( label._textBlock?.InlineStyleHash );
			}
		}
		var value = hash.ToHashCode();
		if ( _initialized && value == _contentHash ) return;
		_initialized = true;
		_contentHash = value;
		_runs.Clear();
		bool previousSpace = true;
		int codepoint = 0;
		foreach ( var item in _participants )
		{
			if ( item is not Label label ) continue;
			var source = label._textBlock.Text ?? "";
			var text = new StringBuilder();
			var mapping = new List<(int Start, int End)>();
			int offset = 0;
			foreach ( var rune in source.EnumerateRunes() )
			{
				var end = offset + rune.Utf16SequenceLength;
				bool space = rune.Value is ' ' or '\t' or '\r' or '\n' or '\f';
				if ( !space || !previousSpace )
				{
					text.Append( space ? " " : rune.ToString() );
					mapping.Add( (offset, end) );
				}
				else if ( space && mapping.Count > 0 )
					mapping[^1] = (mapping[^1].Start, end);
				previousSpace = space;
				offset = end;
			}
			if ( mapping.Count == 0 ) continue;
			_runs.Add( new Run( label, text.ToString(), codepoint, mapping, label._textBlock.InlineStyle ) );
			codepoint += mapping.Count;
		}
		if ( _runs.Count > 0 && _runs[^1].Text.EndsWith( ' ' ) )
		{
			var last = _runs[^1];
			last.Sources.RemoveAt( last.Sources.Count - 1 );
			_runs[^1] = last with { Text = last.Text[..^1] };
		}
		Text.SetInlineRuns( _panel.ComputedStyle, _runs );
		// A style/font change can change cluster boundaries even when the string is unchanged.
		Text.ShouldDrawSelection = false;
		_panel.LayoutTree.MarkDirty();
		_panel.SetNeedsFinalLayout();
		_panel.MarkRenderDirty();
		foreach ( var item in _participants ) item.MarkRenderDirty();
	}

	private void Collect( Panel parent, List<Panel> into, bool preparedOnly )
	{
		if ( parent._children is null ) return;
		foreach ( var child in parent._children )
		{
			if ( child?.LayoutTree is null ) continue;
			if ( child.ComputedStyle?.Display == DisplayMode.None ) continue;
			// Removal callbacks can insert new children after style preparation. Recovery may only
			// use participants validated by PreLayout, not adopt a new or unsupported subtree.
			if ( (preparedOnly && !_participants.Contains( child )) || child.ComputedStyle is null
				|| child is Label { _textBlock: null } )
			{
				child.SetNeedsPreLayout();
				_panel.SetNeedsPreLayout();
				continue;
			}
			into.Add( child );
			Collect( child, into, preparedOnly );
		}
	}

	/// <summary>Measures text without allocating owner fragments or publishing descendant geometry.</summary>
	public LayoutSize Measure( float width, bool minContent )
	{
		if ( _contentDirty ) Update( preparedOnly: true );
		return Text.MeasureInline( minContent ? 0 : width );
	}

	/// <summary>Returns paragraph-local fragments at the given content width, reusing unchanged geometry.</summary>
	public InlineContentLayout Layout( float width )
	{
		if ( _contentDirty ) Update( preparedOnly: true );
		return Text.LayoutInline( width, _runs );
	}

	internal void Select( Vector2 start, Vector2 end )
	{
		if ( _contentDirty ) Update( preparedOnly: true );
		Text.MeasureInline( Text.InlineFinalWidth );
		if ( _panel.GlobalMatrix is { } matrix )
		{
			start = matrix.Transform( start );
			end = matrix.Transform( end );
		}
		SetSelection( Text.GetLetterAt( start - Origin ), Text.GetLetterAt( end - Origin ) );
	}

	internal void SetSelection( int start, int end )
	{
		if ( _contentDirty ) Update( preparedOnly: true );
		var length = Text.InlineCaretCount;
		start = Math.Clamp( start, 0, length );
		end = Math.Clamp( end, 0, length );
		if ( Text.SelectionStart == start && Text.SelectionEnd == end && Text.ShouldDrawSelection == (start != end) ) return;
		Text.SelectionStart = start;
		Text.SelectionEnd = end;
		Text.ShouldDrawSelection = start != end;
		Text.InvalidateInlineSelection();
		_panel.MarkRenderDirty();
	}

	internal string SelectedText
	{
		get
		{
			if ( _contentDirty ) Update( preparedOnly: true );
			if ( !Text.ShouldDrawSelection ) return null;
			var start = Math.Min( Text.SelectionStart, Text.SelectionEnd );
			var end = Math.Max( Text.SelectionStart, Text.SelectionEnd );
			return Text.GetInlineSelectedText( start, end );
		}
	}

	internal bool Contains( Panel owner, Vector2 position )
	{
		position -= Origin;
		foreach ( var fragment in owner.LayoutTree.Node.InlineFragments )
			if ( position.x >= fragment.X && position.x < fragment.X + fragment.Width
				&& position.y >= fragment.Y && position.y < fragment.Y + fragment.Height ) return true;
		return false;
	}

	internal void FinalizeLayout()
	{
		var node = _panel.LayoutTree.Node;
		var width = node.LayoutWidth - node.LayoutPadding( PhysicalEdge.Left ) - node.LayoutPadding( PhysicalEdge.Right )
			- node.LayoutBorder( PhysicalEdge.Left ) - node.LayoutBorder( PhysicalEdge.Right );
		Text.FinalizeInlineWidth( MathF.Max( 0, width ) );
		var layout = Layout( MathF.Max( 0, width ) );
		var origin = Origin;
		if ( ReferenceEquals( layout, _finalLayout ) && origin == _finalOrigin ) return;
		_finalLayout = layout;
		_finalOrigin = origin;
		_panel.MarkRenderDirty();
	}

	internal void Draw()
	{
		// Intrinsic sizing may have last shaped a different width without publishing fragments.
		if ( _contentDirty ) Update( preparedOnly: true );
		Text.MeasureInline( Text.InlineFinalWidth );
		Text.SizeFinalized( Text.InlineFinalWidth, Text.MeasuredSize.y );
		// Texture coordinates already contain paragraph line alignment; don't apply Label's alignment again.
		Text.BuildDescriptors( _panel.CachedDescriptors, _panel.CachedOverrideBlendMode, null,
			new Rect( Origin, Text.MeasuredSize ), _panel.CachedRenderOpacity );
	}

	/// <summary>Releases paragraph rendering resources and restores independent layout of its participants.</summary>
	public void Dispose()
	{
		foreach ( var item in _participants )
		{
			if ( item.InlineOwner != this ) continue;
			item.InlineOwner = null;
			item.LayoutTree?.MarkDirty();
			item.MarkRenderDirty();
		}
		_participants.Clear();
		_nextParticipants.Clear();
		_runs.Clear();
		_finalLayout = null;
		Text.Dispose();
	}
}
