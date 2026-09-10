namespace Sandbox.UI;

public abstract partial class BaseStyles : ICloneable
{
	internal BorderShape _bordershape;

	/// <summary>
	/// Represents the <c>border-shape</c> CSS property.
	/// </summary>
	public BorderShape BorderShape
	{
		get => _bordershape;
		set
		{
			if ( Equals( _bordershape, value ) ) return;
			_bordershape = value;
			Dirty();
		}
	}

	/// <summary>
	/// Called when any CSS properties are changed.
	/// </summary>
	public abstract void Dirty();

	/// <summary>
	/// Represents the <c>overflow</c> CSS property.
	/// </summary>
	public OverflowMode? Overflow
	{
		get
		{
			if ( _overflowx.HasValue && _overflowx.Value == OverflowMode.Scroll ) return OverflowMode.Scroll;
			if ( _overflowy.HasValue && _overflowy.Value == OverflowMode.Scroll ) return OverflowMode.Scroll;

			return _overflowx ?? _overflowy;
		}
		set
		{
			if ( _overflowx == value && _overflowy == value ) return;

			_overflowx = value;
			_overflowy = value;

			Dirty();
		}
	}

	/// <summary>
	/// Copy over only the styles that are set.
	/// </summary>
	public virtual void Add( BaseStyles bs )
	{
		AddGenerated( bs );
		if ( bs._bordershape != null ) _bordershape = bs._bordershape;

		if ( bs._backgroundImage != null ) _backgroundImage = bs._backgroundImage;
		if ( bs._maskImage != null ) _maskImage = bs._maskImage;
		if ( bs._borderImageSource != null ) _borderImageSource = bs._borderImageSource;
		if ( bs._backgroundPlaybackPaused.HasValue ) _backgroundPlaybackPaused = bs._backgroundPlaybackPaused;

		if ( CssWide != null || bs.CssWide != null )
			MergeCssWide( bs );

		if ( bs.HasCurrentColor ) HasCurrentColor = true;
	}

	/// <summary>
	/// Copy all styles from given style set.
	/// </summary>
	public virtual void From( BaseStyles bs )
	{
		FromGenerated( bs );
		_bordershape = bs._bordershape;

		_backgroundImage = bs._backgroundImage;
		_maskImage = bs._maskImage;
		_borderImageSource = bs._borderImageSource;
		_backgroundPlaybackPaused = bs._backgroundPlaybackPaused;

		CssWide = bs.CssWide == null ? null : new System.Collections.Generic.Dictionary<string, CssWideKeyword>( bs.CssWide );
		HasCurrentColor = bs.HasCurrentColor;
	}

	/// <summary>
	/// Copy all styles from given style set.
	/// </summary>
	public virtual bool Set( string property, string value )
	{
		if ( SetGenerated( property, value ) )
			return true;

		switch ( property )
		{
			case "overflow":
				return SetOverflow( value, x => Overflow = x );
			case "overflow-x":
				return SetOverflow( value, x => OverflowX = x );
			case "overflow-y":
				return SetOverflow( value, x => OverflowY = x );
			case "scrollbar-width":
				return SetScrollbarWidth( value );
			case "scrollbar-gutter":
				return SetScrollbarGutter( value );
			case "scrollbar-color":
				return SetScrollbarColor( value );
		}

		return false;
	}

	public void FillDefaults()
	{
		_bordershape ??= UI.BorderShape.None;
		_overflowx ??= Overflow ?? OverflowMode.Visible;
		_overflowy ??= Overflow ?? OverflowMode.Visible;

		FillDefaultsGenerated();
	}


	bool SetOverflow( string value, Action<OverflowMode> set )
	{
		switch ( value )
		{
			case "hidden":
				set( OverflowMode.Hidden );
				return true;
			case "auto":
			case "scroll":
				// We have no "scroll only when needed" mode, so auto maps to scroll.
				set( OverflowMode.Scroll );
				return true;
			case "clip":
				set( OverflowMode.Clip );
				return true;
			case "clip-whole":
				set( OverflowMode.ClipWhole );
				return true;
			case "visible":
				set( OverflowMode.Visible );
				return true;
			default:
				Log.Warning( $"Unhandled overflow property: {value}" );
				return false;
		}
	}

	/// <summary>
	/// The <c>none</c> and <c>thin</c> keywords. Lengths and <c>auto</c> are handled by the generated setter.
	/// </summary>
	bool SetScrollbarWidth( string value )
	{
		switch ( value )
		{
			case "none":
				ScrollbarWidth = 0;
				return true;
			case "thin":
				ScrollbarWidth = ScrollBar.ThinThickness;
				return true;
			default:
				Log.Warning( $"Unhandled scrollbar-width property: {value}" );
				return false;
		}
	}

	bool SetScrollbarGutter( string value )
	{
		var words = SplitTopLevel( value );
		var stable = words.Contains( "stable" );
		var bothEdges = words.Contains( "both-edges" );

		if ( words.Count == 1 && words[0] == "auto" )
		{
			ScrollbarGutter = UI.ScrollbarGutter.Auto;
			return true;
		}

		if ( stable && words.Count == (bothEdges ? 2 : 1) )
		{
			ScrollbarGutter = bothEdges ? UI.ScrollbarGutter.StableBothEdges : UI.ScrollbarGutter.Stable;
			return true;
		}

		Log.Warning( $"Unhandled scrollbar-gutter property: {value}" );
		return false;
	}

	/// <summary>
	/// Thumb colour then track colour, like the web. <c>auto</c> clears both.
	/// </summary>
	bool SetScrollbarColor( string value )
	{
		if ( value == "auto" )
		{
			ScrollbarThumbColor = null;
			ScrollbarTrackColor = null;
			return true;
		}

		var parts = SplitTopLevel( value );
		if ( parts.Count is 1 or 2 )
		{
			var thumb = Color.Parse( parts[0] );
			var track = parts.Count == 2 ? Color.Parse( parts[1] ) : null;

			if ( thumb.HasValue && (parts.Count == 1 || track.HasValue) )
			{
				ScrollbarThumbColor = thumb;
				ScrollbarTrackColor = track;
				return true;
			}
		}

		Log.Warning( $"Unhandled scrollbar-color property: {value}" );
		return false;
	}

	/// <summary>
	/// Split a value on whitespace outside parentheses
	/// </summary>
	static List<string> SplitTopLevel( string value )
	{
		var parts = new List<string>();
		var depth = 0;
		var start = -1;

		for ( int i = 0; i < value.Length; i++ )
		{
			var c = value[i];

			if ( c == '(' ) depth++;
			else if ( c == ')' ) depth--;

			var space = depth == 0 && char.IsWhiteSpace( c );

			if ( !space && start < 0 ) start = i;

			if ( space && start >= 0 )
			{
				parts.Add( value.Substring( start, i - start ) );
				start = -1;
			}
		}

		if ( start >= 0 ) parts.Add( value.Substring( start ) );

		return parts;
	}

	/// <summary>
	/// Set Left, Right, Width and Height based on this rect. Scale can be used to scale the rect (maybe you want to use Panel.ScaleFromScreen etc)
	/// </summary>
	public void SetRect( in Rect r, float scale = 1.0f )
	{
		Top = Length.Pixels( r.Top * scale );
		Left = Length.Pixels( r.Left * scale );
		Width = Length.Pixels( r.Width * scale );
		Height = Length.Pixels( r.Height * scale );
	}


	public override int GetHashCode()
	{
		var generated_hash = GetHashCodeGenerated();

		generated_hash = HashCode.Combine( generated_hash, _backgroundImage, _borderImageSource, _maskImage, _backgroundPlaybackPaused, _bordershape );

		return generated_hash;
	}
}
