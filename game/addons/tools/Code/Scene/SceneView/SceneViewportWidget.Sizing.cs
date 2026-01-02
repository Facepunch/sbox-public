namespace Editor;

public partial class SceneViewportWidget
{
	Vector2? ForcedSize { get; set; }
	float? ForcedAspectRatio { get; set; }

	//
	// Qt constraint -- unsets FixedSize when set to this value
	//
	const float QT_MAX_SIZE = (1 << 24) - 1;

	/// <summary>
	/// Set the forced size to defaults (free size)
	/// </summary>
	public void SetDefaultSize()
	{
		ForcedSize = null;
		ForcedAspectRatio = null;

		MinimumSize = Vector2.Zero;
		MaximumSize = QT_MAX_SIZE;
		FixedSize = QT_MAX_SIZE;

		SetSizeMode( SizeMode.CanGrow, SizeMode.CanGrow );
	}

	/// <summary>
	/// Set the viewport to a specific aspect ratio
	/// </summary>
	/// <param name="aspectRatio"></param>
	public void SetAspectRatio( float aspectRatio )
	{

		MinimumSize = Vector2.Zero;
		MaximumSize = QT_MAX_SIZE;
		FixedSize = QT_MAX_SIZE;

		// Don't update if the aspect ratio hasn't changed
		if ( ForcedAspectRatio.HasValue && ForcedAspectRatio.Value.AlmostEqual( aspectRatio ) && !ForcedSize.HasValue )
		{
			return;
		}

		ForcedAspectRatio = aspectRatio;
		ForcedSize = null;

		UpdateSizeConstraints();
	}

	/// <summary>
	/// Tries to set the viewport to a specific resolution
	/// </summary>
	/// <param name="resolution"></param>
	public void SetResolution( Vector2 resolution )
	{
		ForcedSize = resolution;
		ForcedAspectRatio = null;

		UpdateSizeConstraints();
	}

	private void UpdateSizeConstraints()
	{
		if ( ForcedSize.HasValue )
		{
			var size = ForcedSize.Value;

			// For fixed resolution, use exact size constraints
			MaximumSize = size;
			FixedSize = size;
		}
		else if ( ForcedAspectRatio.HasValue )
		{
			if ( Parent != null )
			{
				var contentRect = Parent.ContentRect;
				var parentSize = new Vector2( contentRect.Width, contentRect.Height );
				MaximumSize = parentSize;
			}
			else
			{
				MaximumSize = new Vector2( QT_MAX_SIZE, QT_MAX_SIZE );
			}
			FixedSize = QT_MAX_SIZE;
		}
		else
		{
			// For aspect ratio mode or free, don't lock the size - let it be dynamic
			MaximumSize = new Vector2( QT_MAX_SIZE, QT_MAX_SIZE );
			FixedSize = QT_MAX_SIZE;
		}

		Layout.SizeConstraint = SizeConstraint.SetDefaultConstraint;
		SetSizeMode( SizeMode.Expand, SizeMode.Expand );
	}

	Vector2? defaultSize;

	protected override Vector2 SizeHint()
	{
		if ( ForcedSize.HasValue )
		{
			return ForcedSize.Value;
		}

		if ( !ForcedAspectRatio.HasValue )
		{
			return base.SizeHint();
		}

		if ( defaultSize == null )
		{
			defaultSize = Size;
		}

		var availableSize = defaultSize.Value;
		if ( availableSize.x <= 0 || availableSize.y <= 0 )
		{
			return base.SizeHint();
		}

		var aspectRatio = ForcedAspectRatio.Value;

		if ( aspectRatio <= 0 || float.IsNaN( aspectRatio ) || float.IsInfinity( aspectRatio ) )
		{
			return availableSize;
		}

		var availableAspect = availableSize.x / availableSize.y;
		Vector2 result;
		if ( aspectRatio > availableAspect )
		{
			// Fit to width
			result = new Vector2( availableSize.x, availableSize.x / aspectRatio );
		}
		else
		{
			// Fit to height
			result = new Vector2( availableSize.y * aspectRatio, availableSize.y );
		}

		if ( MaximumSize.x > 0 && MaximumSize.y > 0 )
		{
			result = new Vector2( MathF.Min( result.x, MaximumSize.x ), MathF.Min( result.y, MaximumSize.y ) );
		}

		return result;
	}
}
