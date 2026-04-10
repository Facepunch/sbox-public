namespace Sandbox.UI;

public abstract partial class BaseStyles : ICloneable
{
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

		if ( bs._backgroundImage != null ) _backgroundImage = bs._backgroundImage;
		if ( bs._maskImage != null ) _maskImage = bs._maskImage;
		if ( bs._borderImageSource != null ) _borderImageSource = bs._borderImageSource;
		if ( bs._backgroundPlaybackPaused.HasValue ) _backgroundPlaybackPaused = bs._backgroundPlaybackPaused;
	}

	/// <summary>
	/// Copy all styles from given style set.
	/// </summary>
	public virtual void From( BaseStyles bs )
	{
		FromGenerated( bs );

		_backgroundImage = bs._backgroundImage;
		_maskImage = bs._maskImage;
		_borderImageSource = bs._borderImageSource;
		_backgroundPlaybackPaused = bs._backgroundPlaybackPaused;
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
		}

		return false;
	}

	public void FillDefaults()
	{
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
			case "scroll":
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

		generated_hash = HashCode.Combine( generated_hash, _backgroundImage, _borderImageSource, _maskImage, _backgroundPlaybackPaused );

		return generated_hash;
	}
}
