using Sandbox.Rendering;
using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// A solid color, image or gradient fill. Defaults to no fill.
/// </summary>
/// <remarks>
/// Gradients accept 2 to 8 stops ordered by offset in [0, 1]; equal offsets make hard transitions.
/// Colors interpolate in premultiplied sRGB. Vector2 points and absolute lengths use drawing coordinates;
/// percentages and calc expressions resolve from the shape's top-left against its width (X) and height (Y).
/// Gradient points must be finite and distinct; Auto and null are unsupported. Painter.Transform applies after resolution.
/// </remarks>
[Expose]
public readonly partial struct Fill
{
	readonly Color _color;
	readonly Texture _texture;
	readonly Length _imageWidth;
	readonly Length _imageHeight;
	readonly Length _imageOffsetX;
	readonly Length _imageOffsetY;
	readonly GradientCoordinates _gradientCoordinates;
	readonly BackgroundRepeat _repeat;
	readonly FilterMode _filter;
	readonly GradientInfo _gradient;
	readonly bool _hasLayoutRect;
	float Rotation { get; init; }
	Color Background { get; init; }
	BlendMode BackgroundBlend { get; init; }

	/// <summary>
	/// No fill. Equivalent to the default value.
	/// </summary>
	public static Fill None => default;

	/// <summary>
	/// Creates a solid fill.
	/// </summary>
	public static Fill Solid( Color color ) => new( color );

	/// <summary>
	/// Creates a solid fill.
	/// </summary>
	public Fill( Color color )
	{
		_color = color;
	}

	Fill( Texture texture, Color tint, Length width, Length height, Length offsetX, Length offsetY, BackgroundRepeat repeat, FilterMode filter )
	{
		_texture = texture;
		_color = tint;
		_imageWidth = width;
		_imageHeight = height;
		_imageOffsetX = offsetX;
		_imageOffsetY = offsetY;
		_repeat = repeat;
		_filter = filter;
	}

	Fill( GradientInfo gradient, GradientCoordinates coordinates = null )
	{
		_gradient = gradient;
		_gradientCoordinates = coordinates;
		_color = Color.White;
		_repeat = BackgroundRepeat.Clamp;
	}

	internal Fill( Color color, Texture texture, in GradientInfo gradient, Vector4 tile, Color tint,
		BackgroundRepeat repeat, FilterMode filter, float angle, BlendMode blend )
	{
		_texture = texture;
		_gradient = gradient;
		_color = texture is not null || !gradient.ColorOffsets.IsDefaultOrEmpty ? tint : color;
		Background = color;
		_repeat = repeat;
		_filter = filter;
		Rotation = angle;
		BackgroundBlend = blend;
		_imageWidth = tile.z;
		_imageHeight = tile.w;
		_imageOffsetX = tile.x;
		_imageOffsetY = tile.y;
		_hasLayoutRect = true;
	}

	/// <summary>
	/// Converts a color to a solid fill.
	/// </summary>
	public static implicit operator Fill( Color color ) => new( color );

	/// <summary>
	/// Converts a texture to an untinted image fill, or Fill.None when null.
	/// </summary>
	public static implicit operator Fill( Texture texture ) => texture is null ? None : Image( texture );

	/// <summary>
	/// Creates an image fill, stretched to the shape bounds by default.
	/// Specify one dimension to preserve the aspect ratio, or both to set the size.
	/// </summary>
	/// <remarks>
	/// Sizes and offsets use pixels or percentages of the shape bounds. Offsets start at the top-left.
	/// Length.Auto preserves the aspect ratio; using Auto for both dimensions uses the texture's natural size.
	/// The default tint is white, with bilinear sampling and clamped edges.
	/// </remarks>
	/// <example>
	/// <code>
	/// painter.Fill = Fill.Image( texture, width: 100, offsetX: 50, repeat: BackgroundRepeat.Repeat );
	/// painter.Rect( bounds );
	/// </code>
	/// </example>
	public static Fill Image( Texture texture, Color? tint = null, Length? width = null, Length? height = null, Length? offsetX = null, Length? offsetY = null, BackgroundRepeat repeat = BackgroundRepeat.Clamp, FilterMode filter = FilterMode.Bilinear )
	{
		ArgumentNullException.ThrowIfNull( texture );
		if ( !width.HasValue && !height.HasValue ) width = height = Length.Percent( 100 );
		var w = width ?? Length.Auto;
		var h = height ?? Length.Auto;
		var x = offsetX ?? 0;
		var y = offsetY ?? 0;
		ValidateImageLength( w, nameof( width ), size: true );
		ValidateImageLength( h, nameof( height ), size: true );
		ValidateImageLength( x, nameof( offsetX ), size: false );
		ValidateImageLength( y, nameof( offsetY ), size: false );
		if ( !Enum.IsDefined( repeat ) ) throw new ArgumentOutOfRangeException( nameof( repeat ) );
		if ( !Enum.IsDefined( filter ) ) throw new ArgumentOutOfRangeException( nameof( filter ) );

		return new Fill( texture, tint ?? Color.White, w, h, x, y, repeat, filter );
	}

	/// <summary>
	/// Rotate an image or gradient tile around its center, in degrees, without rotating the shape.
	/// </summary>
	public Fill WithRotation( float degrees )
	{
		if ( !float.IsFinite( degrees ) ) throw new ArgumentOutOfRangeException( nameof( degrees ) );
		return this with { Rotation = degrees.DegreeToRadian() };
	}

	static void ValidateImageLength( Length length, string parameter, bool size )
	{
		if ( size && length.Unit == LengthUnit.Auto ) return;
		if ( !(length.Unit is LengthUnit.Pixels or LengthUnit.Percentage || length.Unit.IsDynamic())
			|| !float.IsFinite( length.Value ) || (size && length.Unit != LengthUnit.Expression && length.Value <= 0) )
			throw new ArgumentOutOfRangeException( parameter, "Expected a finite numeric length, positive for image sizes, or Auto for an image size." );
	}

	Vector4 GetImageRect( Rect bounds )
	{
		var autoWidth = _imageWidth.Unit == LengthUnit.Auto;
		var autoHeight = _imageHeight.Unit == LengthUnit.Auto;
		float w = autoWidth ? _texture.Width : _imageWidth.GetPixels( bounds.Width );
		float h = autoHeight ? _texture.Height : _imageHeight.GetPixels( bounds.Height );
		if ( autoWidth && !autoHeight ) w = h * _texture.Width / _texture.Height;
		if ( autoHeight && !autoWidth ) h = w * _texture.Height / _texture.Width;
		float x = _imageOffsetX.GetPixels( bounds.Width );
		float y = _imageOffsetY.GetPixels( bounds.Height );
		if ( !float.IsFinite( w ) || !float.IsFinite( h ) || w <= 0 || h <= 0
			|| !float.IsFinite( bounds.Width / w ) || !float.IsFinite( bounds.Height / h )
			|| !float.IsFinite( x / w ) || !float.IsFinite( y / h ) )
			throw new ArgumentOutOfRangeException( nameof( bounds ), "Image lengths must resolve to finite offsets and positive sizes within the shape's bounds." );
		return new Vector4( x, y, w, h );
	}

	internal bool IsTransparent => _color.a == 0 && Background.a == 0;

	internal bool TryGetSolidColor( out Color color )
	{
		color = _color;
		return _texture is null && _gradient.ColorOffsets.IsDefaultOrEmpty;
	}

	internal Painter.BoxDescriptor CreateDescriptor( Rect bounds, Painter.Context buffer, bool clipFill = true )
	{
		ref var state = ref buffer.State;
		return CreateDescriptor( bounds, buffer.InheritedOpacity, state.OverrideBlendMode, state.FillInsets, state.FillMask, state.FillMaskRect, clipFill );
	}

	internal Painter.BoxDescriptor CreateDescriptor( Rect bounds, float opacity, BlendMode blendMode, in Vector4 fillInsets, Texture fillMask = null, Rect fillMaskRect = default, bool clipFill = true )
	{
		var hasImage = _texture != null;
		var hasBackground = hasImage || !_gradient.ColorOffsets.IsDefaultOrEmpty;
		var backgroundRect = _hasLayoutRect
			? new Vector4( _imageOffsetX.Value, _imageOffsetY.Value, _imageWidth.Value, _imageHeight.Value )
			: hasImage ? GetImageRect( bounds ) : new Vector4( 0, 0, bounds.Width, bounds.Height );
		var backgroundGradient = _gradient;
		_gradientCoordinates?.Resolve( bounds, ref backgroundGradient, out backgroundRect );

		return new Painter.BoxDescriptor( bounds, hasBackground ? Background.WithAlphaMultiplied( opacity ) : _color.WithAlphaMultiplied( opacity ) )
		{
			BackgroundImage = _texture,
			BackgroundAngle = Rotation,
			BackgroundBlendMode = BackgroundBlend,
			BackgroundClip = clipFill && fillMask is not null ? BackgroundClip.Text : clipFill && fillInsets != Vector4.Zero ? BackgroundClip.ContentBox : BackgroundClip.BorderBox,
			BackgroundClipInset = fillInsets,
			TextMask = clipFill ? fillMask : null,
			TextMaskRect = new Vector4( fillMaskRect.Left - bounds.Left, fillMaskRect.Top - bounds.Top, fillMaskRect.Width, fillMaskRect.Height ),
			BackgroundGradient = backgroundGradient,
			BackgroundRect = backgroundRect,
			BackgroundTint = hasBackground ? _color.WithAlphaMultiplied( opacity ) : Color.Transparent,
			BackgroundRepeat = _repeat,
			FilterMode = _filter,
			OverrideBlendMode = blendMode,
		};
	}
}
