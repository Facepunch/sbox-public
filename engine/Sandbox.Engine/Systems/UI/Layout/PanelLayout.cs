using System.Globalization;
using Sandbox.Layout;

namespace Sandbox.UI;

/// <summary>
/// A panel's node in the managed layout tree (<see cref="Sandbox.Layout"/>). Converts the panel's computed
/// <see cref="Styles"/> into layout-engine style values and hands the results back as rects.
/// </summary>
[SkipHotload]
internal sealed class PanelLayout
{
	/// <summary>
	/// Measures a panel's content: returns the content size (excluding padding and border) for the
	/// given constraints. See <see cref="MeasureMode"/>.
	/// </summary>
	internal delegate Vector2 MeasureDelegate( float width, MeasureMode widthMode, float height, MeasureMode heightMode );

	private Sandbox.Layout.LayoutNode _node;
	private Panel _panel;
	private MeasureDelegate _measureFunc;

	internal Sandbox.Layout.LayoutNode Node => _node ?? throw new Exception( "Tried to access destroyed node" );

	private PanelLayout Parent => _panel?.Parent?.LayoutTree;

	// Fixed lengths use the current viewport, including on first layout and resize; others use the owner.
	private float ParentWidth => _panel?.IsFixed == true ? _panel.FindRootPanel()?.PanelBounds.Width ?? 0 : Parent?.LayoutWidth ?? 0;
	private float ParentHeight => _panel?.IsFixed == true ? _panel.FindRootPanel()?.PanelBounds.Height ?? 0 : Parent?.LayoutHeight ?? 0;
	private Sandbox.Layout.FlexDirection ParentDirection => Node.Owner?.Style.FlexDirection ?? Sandbox.Layout.FlexDirection.Row;

	[Flags]
	private enum Dependencies { None = 0, Width = 1, Height = 2, Font = 4, RootFont = 8, ViewWidth = 16, ViewHeight = 32, Direction = 64 }
	private Dependencies _dependencies;
	private ResolutionContext _context;
	private readonly record struct ResolutionContext( float Width, float Height, float Font, float RootFont, Vector2 Viewport, Sandbox.Layout.FlexDirection Direction )
	{
		internal bool Changed( ResolutionContext other, Dependencies dependencies ) =>
			((dependencies & Dependencies.Width) != 0 && Width != other.Width)
			|| ((dependencies & Dependencies.Height) != 0 && Height != other.Height)
			|| ((dependencies & Dependencies.Font) != 0 && Font != other.Font)
			|| ((dependencies & Dependencies.RootFont) != 0 && RootFont != other.RootFont)
			|| ((dependencies & Dependencies.ViewWidth) != 0 && Viewport.x != other.Viewport.x)
			|| ((dependencies & Dependencies.ViewHeight) != 0 && Viewport.y != other.Viewport.y)
			|| ((dependencies & Dependencies.Direction) != 0 && Direction != other.Direction);
	}

	private ResolutionContext CurrentContext => new( ParentWidth, ParentHeight, _panel?.ComputedStyle?.FontSize?.Value ?? Length.CurrentFontSize.Value,
		Length.RootFontSize.Value, Length.RootSize, ParentDirection );

	internal bool ReferenceSizeChanged => _dependencies != Dependencies.None && _context.Changed( CurrentContext, _dependencies );
	internal int StyleUpdateCount { get; private set; }
	internal int GridParseCount { get; private set; }

	internal void BeginStyleUpdate()
	{
		_dependencies = Dependencies.None;
		StyleUpdateCount++;
	}

	internal void CaptureReferenceSize()
	{
		_context = CurrentContext;
	}

	public PanelLayout( Panel panel )
	{
		_node = new Sandbox.Layout.LayoutNode() { Context = panel };
		_panel = panel;
	}

	public void Dispose()
	{
		if ( _node is null ) return;

		_node.Owner?.RemoveChild( _node );
		_node.RemoveAllChildren();
		_node.Context = null;
		_node.MeasureFunc = null;
		_node.InlineContent = null;
		_node = null;
		_panel = null;
		_measureFunc = null;
	}

	private Rect _rect;
	internal Margin Margin;
	internal Margin Padding;
	internal Margin Border;

	public bool HasNewLayout => Node.HasNewLayout;

	/// <summary>
	/// Whether this node needs a layout calculation. Dirtiness propagates to the root, so a clean root
	/// means the whole tree is clean.
	/// </summary>
	public bool IsDirty => Node.IsDirty;

	internal float LayoutX => Node.LayoutLeft;
	internal float LayoutY => Node.LayoutTop;
	internal float LayoutWidth => Node.LayoutWidth;
	internal float LayoutHeight => Node.LayoutHeight;
	internal Margin LayoutMargin => new( Node.LayoutMargin( PhysicalEdge.Left ), Node.LayoutMargin( PhysicalEdge.Top ), Node.LayoutMargin( PhysicalEdge.Right ), Node.LayoutMargin( PhysicalEdge.Bottom ) );
	internal Margin LayoutPadding => new( Node.LayoutPadding( PhysicalEdge.Left ), Node.LayoutPadding( PhysicalEdge.Top ), Node.LayoutPadding( PhysicalEdge.Right ), Node.LayoutPadding( PhysicalEdge.Bottom ) );
	internal Margin LayoutBorder => new( Node.LayoutBorder( PhysicalEdge.Left ), Node.LayoutBorder( PhysicalEdge.Top ), Node.LayoutBorder( PhysicalEdge.Right ), Node.LayoutBorder( PhysicalEdge.Bottom ) );

	/// <summary>
	/// The laid-out rect relative to the parent (viewport for fixed panels), refreshed on new layout.
	/// </summary>
	public Rect LayoutRect
	{
		get
		{
			if ( !HasNewLayout )
				return _rect;

			Node.HasNewLayout = false;

			_rect = new Rect( LayoutX, LayoutY, LayoutWidth, LayoutHeight );

			Margin = LayoutMargin;
			Padding = LayoutPadding;
			Border = LayoutBorder;

			return _rect;
		}
	}

	internal bool IsMeasureDefined => _measureFunc is not null;

	internal void SetMeasureFunction( MeasureDelegate target )
	{
		_measureFunc = target;
		Node.MeasureFunc = target is null ? null : Measure;
	}

	private LayoutSize Measure( Sandbox.Layout.LayoutNode node, float width, MeasureMode widthMode, float height, MeasureMode heightMode )
	{
		var size = _measureFunc( width, widthMode, height, heightMode );
		return new LayoutSize( size.x, size.y );
	}

	internal void RemoveChild( PanelLayout child ) => Node.RemoveChild( child.Node );

	internal void AddChild( PanelLayout child )
	{
		Node.AddChild( child.Node );
	}

	internal void AddChild( int index, PanelLayout child )
	{
		// Logical panel indexes can include entries whose layout nodes have already been removed.
		Node.InsertChild( child.Node, Math.Clamp( index, 0, Node.ChildCount ) );
	}

	internal void CalculateLayout( float width = float.NaN, float height = float.NaN )
	{
		Node.CalculateLayout( width, height, Direction.LTR );
	}

	internal void MarkDirty()
	{
		Node.MarkDirty();
	}

	internal bool Initialized;

	private enum Axis { Width, Height }

	// Parent's laid-out size along an axis - the reference for resolving relative units.
	private float Reference( Axis axis ) => axis == Axis.Width ? ParentWidth : ParentHeight;

	/// <summary>
	/// Collapse a Length into a layout length. The parent dimension is only read for relative units
	/// (em/rem/calc); percentages are passed through and resolved by the layout engine.
	/// </summary>
	private StyleLength Resolve( Length? value, Axis axis )
	{
		if ( !value.HasValue ) return StyleLength.Undefined;

		var length = value.Value;
		_dependencies |= GetDependencies( length, axis );
		switch ( length.Unit )
		{
			case LengthUnit.Undefined: return StyleLength.Undefined;
			case LengthUnit.Auto: return StyleLength.Auto;
			case LengthUnit.Percentage: return StyleLength.Percent( length.Value );
			case LengthUnit.Pixels: return StyleLength.Points( length.Value );
			case LengthUnit.ViewWidth:
			case LengthUnit.ViewHeight:
			case LengthUnit.ViewMin:
			case LengthUnit.ViewMax: return StyleLength.Points( length.GetPixels( 0 ) );
			default: return StyleLength.Points( length.GetPixels( Reference( axis ) ) ); // em, rem, calc
		}
	}

	private static Dependencies GetDependencies( Length length, Axis axis ) => length.Unit switch
	{
		LengthUnit.Em => Dependencies.Font,
		LengthUnit.RootEm => Dependencies.RootFont,
		LengthUnit.ViewWidth => Dependencies.ViewWidth,
		LengthUnit.ViewHeight => Dependencies.ViewHeight,
		LengthUnit.ViewMin or LengthUnit.ViewMax => Dependencies.ViewWidth | Dependencies.ViewHeight,
		LengthUnit.Expression => GetExpressionDependencies( length.ToString(), axis ),
		_ => Dependencies.None
	};

	private static Dependencies GetExpressionDependencies( string expression, Axis axis )
	{
		var dependencies = Dependencies.None;
		var text = expression.AsSpan();
		for ( int i = 0; i < text.Length; i++ )
		{
			if ( text[i] == '%' ) dependencies |= axis == Axis.Width ? Dependencies.Width : Dependencies.Height;
			if ( !char.IsLetter( text[i] ) ) continue;
			var start = i;
			while ( i + 1 < text.Length && char.IsLetter( text[i + 1] ) ) i++;
			var unit = text.Slice( start, i - start + 1 );
			if ( unit.Equals( "em", StringComparison.OrdinalIgnoreCase ) ) dependencies |= Dependencies.Font;
			else if ( unit.Equals( "rem", StringComparison.OrdinalIgnoreCase ) ) dependencies |= Dependencies.RootFont;
			else if ( unit.Equals( "vw", StringComparison.OrdinalIgnoreCase ) || unit.Equals( "dvw", StringComparison.OrdinalIgnoreCase )
				|| unit.Equals( "svw", StringComparison.OrdinalIgnoreCase ) || unit.Equals( "lvw", StringComparison.OrdinalIgnoreCase ) ) dependencies |= Dependencies.ViewWidth;
			else if ( unit.Equals( "vh", StringComparison.OrdinalIgnoreCase ) || unit.Equals( "dvh", StringComparison.OrdinalIgnoreCase )
				|| unit.Equals( "svh", StringComparison.OrdinalIgnoreCase ) || unit.Equals( "lvh", StringComparison.OrdinalIgnoreCase ) ) dependencies |= Dependencies.ViewHeight;
			else if ( unit.Equals( "vmin", StringComparison.OrdinalIgnoreCase ) || unit.Equals( "vmax", StringComparison.OrdinalIgnoreCase ) ) dependencies |= Dependencies.ViewWidth | Dependencies.ViewHeight;
		}
		return dependencies;
	}

	/// <summary>Like <see cref="Resolve"/> but with no <c>auto</c> (padding, border, position).</summary>
	private StyleLength ResolveNoAuto( Length? value, Axis axis )
	{
		var resolved = Resolve( value, axis );
		return resolved.IsAuto ? StyleLength.Undefined : resolved;
	}

	// Resolves units the grid parser doesn't know (em, rem, vw, ...) to pixels through Length.
	private GridParser.UnitResolver _unitResolver;
	private Dependencies _gridDependencies;
	private float? ResolveGridUnit( float value, string unit )
	{
		var length = Length.Parse( value.ToString( CultureInfo.InvariantCulture ) + unit );
		if ( length.HasValue ) _gridDependencies |= GetDependencies( length.Value, Axis.Width );
		return length?.GetPixels( 0 );
	}

	private LayoutStyle Style => Node.Style;

	public Length? Width { set => Style.Width = Resolve( value, Axis.Width ); }
	public Length? Height { set => Style.Height = Resolve( value, Axis.Height ); }
	public Length? MaxWidth { set => Style.MaxWidth = ResolveNoAuto( value, Axis.Width ); }
	public Length? MaxHeight { set => Style.MaxHeight = ResolveNoAuto( value, Axis.Height ); }
	public Length? MinWidth { set => Style.MinWidth = ResolveNoAuto( value, Axis.Width ); }
	public Length? MinHeight { set => Style.MinHeight = ResolveNoAuto( value, Axis.Height ); }

	public DisplayMode? Display { set => Style.Display = (Display)(int)(value ?? DisplayMode.Flex); }

	public Length? Left { set => Style.SetPosition( Edge.Left, ResolveNoAuto( value, Axis.Width ) ); }
	public Length? Right { set => Style.SetPosition( Edge.Right, ResolveNoAuto( value, Axis.Width ) ); }
	public Length? Top { set => Style.SetPosition( Edge.Top, ResolveNoAuto( value, Axis.Height ) ); }
	public Length? Bottom { set => Style.SetPosition( Edge.Bottom, ResolveNoAuto( value, Axis.Height ) ); }

	public Length? MarginLeft { set => Style.SetMargin( Edge.Left, Resolve( value, Axis.Width ) ); }
	public Length? MarginRight { set => Style.SetMargin( Edge.Right, Resolve( value, Axis.Width ) ); }
	public Length? MarginTop { set => Style.SetMargin( Edge.Top, Resolve( value, Axis.Height ) ); }
	public Length? MarginBottom { set => Style.SetMargin( Edge.Bottom, Resolve( value, Axis.Height ) ); }

	public Length? PaddingLeft { set => Style.SetPadding( Edge.Left, ResolveNoAuto( value, Axis.Width ) ); }
	public Length? PaddingRight { set => Style.SetPadding( Edge.Right, ResolveNoAuto( value, Axis.Width ) ); }
	public Length? PaddingTop { set => Style.SetPadding( Edge.Top, ResolveNoAuto( value, Axis.Height ) ); }
	public Length? PaddingBottom { set => Style.SetPadding( Edge.Bottom, ResolveNoAuto( value, Axis.Height ) ); }

	// Borders are always point values; a percentage resolves against the parent up front.
	private StyleLength ResolveBorder( Length? value, Axis axis )
	{
		var resolved = ResolveNoAuto( value, axis );
		if ( resolved.IsPercent )
		{
			_dependencies |= axis == Axis.Width ? Dependencies.Width : Dependencies.Height;
			return StyleLength.Points( value.Value.GetPixels( Reference( axis ) ) );
		}
		return resolved;
	}

	private Margin _gutter;
	private Length? _borderLeft;
	private Length? _borderRight;

	/// <summary>
	/// Extra left and right space between the padding box and the border, for a scrollbar gutter.
	/// Added to the layout border, which is always in points. <see cref="Border"/> includes it.
	/// </summary>
	public Margin Gutter
	{
		get => _gutter;
		set
		{
			if ( _gutter.Left == value.Left && _gutter.Right == value.Right ) return;
			_gutter = value;

			Style.SetBorder( Edge.Left, ResolveBorderWithGutter( _borderLeft, _gutter.Left ) );
			Style.SetBorder( Edge.Right, ResolveBorderWithGutter( _borderRight, _gutter.Right ) );
		}
	}

	private StyleLength ResolveBorderWithGutter( Length? value, float gutter )
	{
		var resolved = ResolveBorder( value, Axis.Width );
		if ( gutter <= 0 ) return resolved;
		return StyleLength.Points( (resolved.IsDefined ? resolved.Value : 0) + gutter );
	}

	public Length? BorderLeftWidth { set { _borderLeft = value; Style.SetBorder( Edge.Left, ResolveBorderWithGutter( value, _gutter.Left ) ); } }
	public Length? BorderRightWidth { set { _borderRight = value; Style.SetBorder( Edge.Right, ResolveBorderWithGutter( value, _gutter.Right ) ); } }
	public Length? BorderTopWidth { set => Style.SetBorder( Edge.Top, ResolveBorder( value, Axis.Height ) ); }
	public Length? BorderBottomWidth { set => Style.SetBorder( Edge.Bottom, ResolveBorder( value, Axis.Height ) ); }

	public PositionMode? PositionType { set => Style.PositionType = (PositionType)(int)(value ?? PositionMode.Static); }
	public float? AspectRatio { set => Style.AspectRatio = value ?? float.NaN; }
	public float? FlexGrow { set => Style.FlexGrow = value ?? 0; }
	public float? FlexShrink { set => Style.FlexShrink = value ?? 1; }

	public Length? FlexBasis
	{
		set
		{
			// A flex item's basis follows its container's main axis, not its own direction.
			var axis = ParentDirection is Sandbox.Layout.FlexDirection.Column or Sandbox.Layout.FlexDirection.ColumnReverse ? Axis.Height : Axis.Width;
			if ( value?.Unit == LengthUnit.Expression ) _dependencies |= Dependencies.Direction;
			Style.FlexBasis = Resolve( value, axis );
		}
	}

	public Wrap? Wrap { set => Style.FlexWrap = (Sandbox.Layout.Wrap)(int)(value ?? UI.Wrap.NoWrap); }
	public Align? AlignContent { set => Style.AlignContent = (Sandbox.Layout.Align)(int)(value ?? UI.Align.FlexStart); }
	public Align? AlignItems { set => Style.AlignItems = (Sandbox.Layout.Align)(int)(value ?? UI.Align.Stretch); }
	public Align? AlignSelf { set => Style.AlignSelf = (Sandbox.Layout.Align)(int)(value ?? UI.Align.Auto); }
	public Align? JustifyItems { set => Style.JustifyItems = (Sandbox.Layout.Align)(int)(value ?? UI.Align.Auto); }
	public Align? JustifySelf { set => Style.JustifySelf = (Sandbox.Layout.Align)(int)(value ?? UI.Align.Auto); }
	public FlexDirection? FlexDirection { set => Style.FlexDirection = (Sandbox.Layout.FlexDirection)(int)(value ?? UI.FlexDirection.Row); }
	public Justify? JustifyContent { set => Style.JustifyContent = (Sandbox.Layout.Justify)(int)(value ?? UI.Justify.Stretch); }

	public OverflowMode? Overflow
	{
		set
		{
			// Clip and ClipWhole behave like Visible for layout purposes - they only affect rendering, not layout.
			Style.Overflow = value switch
			{
				OverflowMode.Scroll => Sandbox.Layout.Overflow.Scroll,
				OverflowMode.Hidden => Sandbox.Layout.Overflow.Hidden,
				_ => Sandbox.Layout.Overflow.Visible,
			};
		}
	}

	public Length? RowGap { set => Style.SetGap( Sandbox.Layout.Gutter.Row, ResolveGap( value ) ); }
	public Length? ColumnGap { set => Style.SetGap( Sandbox.Layout.Gutter.Column, ResolveGap( value ) ); }

	private StyleLength ResolveGap( Length? value )
	{
		if ( !value.HasValue || value.Value.Unit == LengthUnit.Auto || value.Value.Unit == LengthUnit.Undefined ) return StyleLength.Undefined;
		if ( value.Value.Unit == LengthUnit.Percentage ) return StyleLength.Percent( value.Value.Value );
		_dependencies |= GetDependencies( value.Value, Axis.Width ) & ~(Dependencies.Width | Dependencies.Height);
		return StyleLength.Points( value.Value.GetPixels( 0 ) );
	}

	public GridAutoFlow? GridAutoFlow { set => Style.GridAutoFlow = (Sandbox.Layout.GridAutoFlow)(int)(value ?? UI.GridAutoFlow.Row); }

	private TrackCache<TrackList> _templateColumns, _templateRows;
	private TrackCache<TrackSizingFunction[]> _autoColumns, _autoRows;
	private struct TrackCache<T>
	{
		internal string Text;
		internal T Value;
		internal bool Initialized;
		internal Dependencies Dependencies;
		internal ResolutionContext Context;
	}

	public string GridTemplateColumns { set => Style.GridTemplateColumns = ParseTracks( value, ref _templateColumns ); }
	public string GridTemplateRows { set => Style.GridTemplateRows = ParseTracks( value, ref _templateRows ); }
	public string GridAutoColumns { set => Style.GridAutoColumns = ParseTracks( value, ref _autoColumns ); }
	public string GridAutoRows { set => Style.GridAutoRows = ParseTracks( value, ref _autoRows ); }
	public string GridColumnStart { set => Style.GridColumnStart = ParsePlacement( value ); }
	public string GridColumnEnd { set => Style.GridColumnEnd = ParsePlacement( value ); }
	public string GridRowStart { set => Style.GridRowStart = ParsePlacement( value ); }
	public string GridRowEnd { set => Style.GridRowEnd = ParsePlacement( value ); }

	private T ParseTracks<T>( string value, ref TrackCache<T> cache )
	{
		var context = CurrentContext;
		if ( !cache.Initialized || !ReferenceEquals( value, cache.Text ) || cache.Context.Changed( context, cache.Dependencies ) )
		{
			GridParseCount++;
			_unitResolver ??= ResolveGridUnit;
			_gridDependencies = Dependencies.None;
			cache.Value = typeof( T ) == typeof( TrackList ) ? (T)(object)ParseTrackList( value ) : (T)(object)ParseTrackSizes( value );
			cache.Text = value;
			cache.Initialized = true;
			cache.Dependencies = _gridDependencies;
			cache.Context = context;
		}

		_dependencies |= cache.Dependencies;
		return cache.Value;
	}

	private TrackList ParseTrackList( string value )
	{
		if ( string.IsNullOrWhiteSpace( value ) ) return TrackList.None;
		if ( GridParser.TryParseTrackList( value, out var list, _unitResolver ) ) return list;

		Log.Warning( $"Invalid grid track list: {value}" );
		return TrackList.None;
	}

	private TrackSizingFunction[] ParseTrackSizes( string value )
	{
		if ( string.IsNullOrWhiteSpace( value ) ) return Array.Empty<TrackSizingFunction>();
		if ( GridParser.TryParseTrackSizes( value, out var sizes, _unitResolver ) ) return sizes;

		Log.Warning( $"Invalid grid track sizes: {value}" );
		return Array.Empty<TrackSizingFunction>();
	}

	private static GridPlacement ParsePlacement( string value )
	{
		if ( string.IsNullOrWhiteSpace( value ) ) return GridPlacement.Auto;
		if ( GridParser.TryParsePlacement( value, out var placement ) ) return placement;

		Log.Warning( $"Invalid grid placement: {value}" );
		return GridPlacement.Auto;
	}
}
