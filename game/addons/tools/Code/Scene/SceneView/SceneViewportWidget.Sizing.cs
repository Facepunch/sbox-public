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
		else
		{
			// For aspect ratio mode or free, don't lock the size - let it be dynamic
			MaximumSize = new Vector2( QT_MAX_SIZE, QT_MAX_SIZE );
			FixedSize = QT_MAX_SIZE;
		}

		Layout.SizeConstraint = SizeConstraint.SetDefaultConstraint;
		SetSizeMode( SizeMode.Expand, SizeMode.Expand );

		UpdateGeometry();
		AdjustSize();
	}

	protected override Vector2 SizeHint()
	{
		// Exact size, easy
		if ( ForcedSize.HasValue )
		{
			return ForcedSize.Value;
		}

		// Free
		if ( !ForcedAspectRatio.HasValue )
		{
			return base.SizeHint();
		}

		// Dynamically calculate size based on current parent size
		var parentSize = Parent?.Size ?? base.SizeHint();
		var parentAspect = parentSize.x / parentSize.y;
		var aspectRatio = ForcedAspectRatio.Value;

		if ( aspectRatio > parentAspect )
		{
			// Fit to width
			return new Vector2( parentSize.x, parentSize.x / aspectRatio );
		}
		else
		{
			// Fit to height
			return new Vector2( parentSize.y * aspectRatio, parentSize.y );
		}


	}
}
