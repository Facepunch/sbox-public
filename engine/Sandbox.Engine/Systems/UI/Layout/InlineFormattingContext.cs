using System.Text;
using Sandbox.Layout;

namespace Sandbox.UI;

/// <summary>
/// Shapes and wraps text from several inline panels as one paragraph.
/// </summary>
internal sealed class InlineFormattingContext : IInlineContent, IDisposable
{
	internal sealed record Run(
		Label Owner,
		string Text,
		int Start,
		List<(int Start, int End)> Sources,
		Topten.RichTextKit.Style Style );

	private static readonly string[] BoxProperties =
	[
		"padding-left", "padding-top", "padding-right", "padding-bottom",
		"margin-left", "margin-top", "margin-right", "margin-bottom",
		"border-left-width", "border-top-width", "border-right-width", "border-bottom-width",
		"outline-width", "width", "height", "min-width", "min-height", "max-width", "max-height"
	];

	private readonly Panel _panel;
	private readonly List<Run> _runs = new();
	private List<Panel> _participants = new();
	private List<Panel> _participantBuffer = new();
	private int? _contentHash;
	private bool _contentDirty;

	internal PanelLayout Root { get; }
	internal readonly TextBlock Text = new();

	internal Vector2 Origin
	{
		get
		{
			var node = Root.Node;
			var left = node.LayoutPadding( PhysicalEdge.Left ) + node.LayoutBorder( PhysicalEdge.Left );
			var top = node.LayoutPadding( PhysicalEdge.Top ) + node.LayoutBorder( PhysicalEdge.Top );
			return _panel.Box.Rect.Position + new Vector2( left, top ) - _panel.ScrollOffset;
		}
	}

	internal InlineFormattingContext( Panel panel )
	{
		_panel = panel;
		Root = panel.LayoutTree;
	}

	internal static bool CanFormat( Panel panel )
	{
		if ( panel.ComputedStyle?.Display != DisplayMode.Block ) return false;
		if ( panel.LayoutTree.IsMeasureDefined || !panel.HasChildren ) return false;
		if ( !SupportsTextStyle( panel.ComputedStyle ) ) return false;

		bool hasInline = false;
		foreach ( var child in panel._children )
		{
			if ( child?.LayoutTree is null ) continue;
			if ( child.ComputedStyle?.Display == DisplayMode.None ) continue;
			if ( !CanParticipate( child, panel.ComputedStyle ) ) return false;
			hasInline |= child.ComputedStyle?.Display == DisplayMode.Inline;
		}

		// Razor text alone must keep the old block layout. It needs an explicit inline sibling.
		return hasInline;
	}

	private static bool SupportsTextStyle( Styles style )
	{
		if ( style.TextAlign != TextAlign.Left || style.TextOverflow != TextOverflow.None ) return false;
		if ( style.WhiteSpace is not (WhiteSpace.Normal or WhiteSpace.NoWrap) ) return false;
		if ( style.TextTransform != TextTransform.None ) return false;
		if ( style.TextShadow is not null && !style.TextShadow.IsNone && style.TextShadow.Any() ) return false;
		if ( style.TextStrokeWidth?.Value != 0 || !style.TextGradient.ColorOffsets.IsDefaultOrEmpty ) return false;

		return style.BackgroundClip != BackgroundClip.Text;
	}

	private static bool CanParticipate( Panel child, Styles paragraphStyle )
	{
		if ( child.ComputedStyle is not { } style ) return false;
		if ( style.Display == DisplayMode.None ) return true;
		if ( !SupportsTextStyle( style ) ) return false;

		// These settings apply to the whole paragraph, so its spans must agree.
		if ( style.WhiteSpace != paragraphStyle.WhiteSpace || style.WordBreak != paragraphStyle.WordBreak ) return false;
		if ( style.FontSmooth != paragraphStyle.FontSmooth || style.TextFilter != paragraphStyle.TextFilter ) return false;
		if ( style.MixBlendMode != paragraphStyle.MixBlendMode ) return false;

		// Inline boxes aren't painted separately yet.
		if ( style.Position != PositionMode.Static || style.Overflow != OverflowMode.Visible ) return false;
		if ( style.Opacity != 1 || style.Transform?.IsEmpty() == false ) return false;
		if ( child.HasBackground || child.HasFilter || child.HasBackdropFilter || style.MaskImage is not null ) return false;
		if ( style.BoxShadow is not null && !style.BoxShadow.IsNone && style.BoxShadow.Any() ) return false;

		foreach ( var property in BoxProperties )
		{
			if ( !style.IsDefault( property ) ) return false;
		}

		if ( child is Label label )
		{
			if ( child.GetType() != typeof( Label ) || label.IsRich || !label.Multiline || child.Parent is TextEntry ) return false;
			return style.Display == DisplayMode.Inline || (style.Display == DisplayMode.Flex && label.IsGeneratedText);
		}

		if ( style.Display != DisplayMode.Inline || child.GetType() != typeof( Panel ) || child.LayoutTree.IsMeasureDefined ) return false;
		if ( child._children is null ) return true;

		foreach ( var descendant in child._children )
		{
			if ( descendant?.LayoutTree is not null && !CanParticipate( descendant, paragraphStyle ) ) return false;
		}

		return true;
	}

	internal void Invalidate()
	{
		_contentDirty = true;
		_panel.LayoutTree?.MarkDirty();
		_panel.SetNeedsPreLayout();
	}

	internal void Update( bool existingOnly = false )
	{
		_contentDirty = false;
		UpdateParticipants( existingOnly );

		var hash = GetContentHash();
		if ( hash == _contentHash ) return;
		_contentHash = hash;

		BuildRuns();
		Text.SetInlineRuns( _panel.ComputedStyle, _runs );

		// Font changes can move caret boundaries even when the text hasn't changed.
		Text.ShouldDrawSelection = false;
		Root.MarkDirty();
		_panel.SetNeedsFinalLayout();
	}

	private void UpdateParticipants( bool existingOnly )
	{
		Collect( _panel, _participantBuffer, existingOnly );

		foreach ( var panel in _participants )
		{
			if ( !_participantBuffer.Contains( panel ) )
				panel.LayoutTree?.LeaveInlineContext( this );
		}

		_participants.Clear();
		(_participants, _participantBuffer) = (_participantBuffer, _participants);

		foreach ( var panel in _participants )
		{
			panel.LayoutTree.JoinInlineContext( this );
		}
	}

	private int GetContentHash()
	{
		var hash = new HashCode();

		// Only text changes should reshape the paragraph and clear its selection.
		var style = _panel.ComputedStyle;
		hash.Add( HashCode.Combine( style.FontFamily, style.FontSize, style.FontWeight, style.FontStyle,
			style.FontVariantNumeric, style.FontColor, style.WhiteSpace, style.WordBreak ) );
		hash.Add( HashCode.Combine( style.LetterSpacing, style.WordSpacing, style.LineHeight, style.FontSmooth,
			style.TextFilter, style.TextDecorationLine, style.TextDecorationColor, style.TextDecorationStyle ) );
		hash.Add( HashCode.Combine( style.TextDecorationThickness, style.TextDecorationSkipInk,
			style.TextUnderlineOffset, style.TextOverlineOffset, style.TextLineThroughOffset ) );

		foreach ( var panel in _participants )
		{
			hash.Add( panel );
			if ( panel is Label label )
			{
				hash.Add( label._textBlock?.Text );
				hash.Add( label._textBlock?.InlineStyleHash );
			}
		}

		return hash.ToHashCode();
	}

	private void BuildRuns()
	{
		_runs.Clear();
		// Keep whitespace collapse continuous across label boundaries.
		bool previousSpace = true;
		int codepoint = 0;

		foreach ( var panel in _participants )
		{
			if ( panel is not Label label ) continue;

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
				else if ( mapping.Count > 0 )
				{
					mapping[^1] = (mapping[^1].Start, end);
				}

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
	}

	private void Collect( Panel parent, List<Panel> result, bool existingOnly )
	{
		if ( parent._children is null ) return;

		foreach ( var child in parent._children )
		{
			if ( child?.LayoutTree is null ) continue;
			if ( child.ComputedStyle?.Display == DisplayMode.None ) continue;

			// Children added by removal callbacks need a PreLayout pass before we can use them.
			if ( (existingOnly && !_participants.Contains( child )) || child.ComputedStyle is null
				|| child is Label { _textBlock: null } )
			{
				child.SetNeedsPreLayout();
				_panel.SetNeedsPreLayout();
				continue;
			}

			result.Add( child );
			Collect( child, result, existingOnly );
		}
	}

	private void EnsureContent()
	{
		// Deletion can happen between layout passes. Drop removed panels now; new ones must wait.
		if ( _contentDirty ) Update( existingOnly: true );
	}

	public LayoutSize Measure( float width, bool minContent )
	{
		EnsureContent();
		return Text.MeasureInline( minContent ? 0 : width );
	}

	public InlineContentLayout Layout( float width )
	{
		EnsureContent();
		return Text.LayoutInline( width, _runs );
	}

	internal void Select( Vector2 start, Vector2 end )
	{
		EnsureContent();
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
		EnsureContent();

		var length = Text.InlineCaretCount;
		start = Math.Clamp( start, 0, length );
		end = Math.Clamp( end, 0, length );
		if ( Text.SelectionStart == start && Text.SelectionEnd == end && Text.ShouldDrawSelection == (start != end) ) return;

		Text.SelectionStart = start;
		Text.SelectionEnd = end;
		Text.ShouldDrawSelection = start != end;
		Text.InvalidateInlineSelection();
	}

	internal string SelectedText
	{
		get
		{
			EnsureContent();
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
		{
			if ( position.x >= fragment.X && position.x < fragment.X + fragment.Width
				&& position.y >= fragment.Y && position.y < fragment.Y + fragment.Height ) return true;
		}

		return false;
	}

	internal void FinalizeLayout()
	{
		var node = Root.Node;
		var width = node.LayoutWidth - node.LayoutPadding( PhysicalEdge.Left ) - node.LayoutPadding( PhysicalEdge.Right )
			- node.LayoutBorder( PhysicalEdge.Left ) - node.LayoutBorder( PhysicalEdge.Right );

		width = MathF.Max( 0, width );
		Text.FinalizeInlineWidth( width );
		Layout( width );
	}

	internal void Draw( Painter painter )
	{
		EnsureContent();

		// A measurement may have changed the text width since FinalizeLayout.
		Text.MeasureInline( Text.InlineFinalWidth );
		Text.SizeFinalized( Text.InlineFinalWidth, Text.MeasuredSize.y );
		Text.Draw( painter, _panel.CachedOverrideBlendMode, null,
			new Rect( Origin, Text.MeasuredSize ), _panel.CachedRenderOpacity );
	}

	public void Dispose()
	{
		foreach ( var panel in _participants )
		{
			panel.LayoutTree?.LeaveInlineContext( this );
		}

		_participants.Clear();
		_participantBuffer.Clear();
		_runs.Clear();
		Text.Dispose();
	}
}
