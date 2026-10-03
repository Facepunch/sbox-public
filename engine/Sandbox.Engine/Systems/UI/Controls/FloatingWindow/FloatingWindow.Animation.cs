namespace Sandbox.UI;

/// <summary>
/// Animates the window between its normal bounds and its icon at the bottom of the screen.
/// </summary>
public partial class FloatingWindow
{
	const float AnimationDuration = 0.32f;
	float animationElapsed;
	Rect expandedRect;
	Rect animationDockRect;
	PanelTransform? previousTransform;
	Length? previousOriginX;
	Length? previousOriginY;

	/// <summary>
	/// Whether the window is travelling between its expanded position and the minimized row.
	/// </summary>
	public bool IsAnimating { get; private set; }

	void BeginWindowAnimation( Rect expanded )
	{
		expandedRect = expanded;
		animationElapsed = 0;
		previousTransform = Style.Transform;
		previousOriginX = Style.TransformOriginX;
		previousOriginY = Style.TransformOriginY;
		IsAnimating = true;
		SetClass( "animating", true );
		BringToFront();
		ApplyWindowAnimation();
		StateHasChanged();
		SetNeedsFinalLayout();
	}

	void TickWindowAnimation()
	{
		if ( !IsAnimating ) return;

		animationElapsed += RealTime.Delta;
		if ( !IsVisible || animationElapsed >= AnimationDuration )
		{
			FinishWindowAnimation();
			return;
		}

		ApplyWindowAnimation();
		SetNeedsFinalLayout();
	}

	void ApplyWindowAnimation()
	{
		if ( expandedRect.Width <= 0 || expandedRect.Height <= 0 ) return;

		var progress = Math.Clamp( animationElapsed / AnimationDuration, 0, 1 );
		var amount = IsMinimized ? progress : 1 - progress;
		var travel = amount * amount * (3 - 2 * amount);
		var narrowing = 1 - (1 - amount) * (1 - amount);
		var dock = IsMinimized ? GetMinimizedRect() : animationDockRect;

		// Pull the sides inward first, then carry the whole window down into its icon.
		var width = MathX.Lerp( expandedRect.Width, dock.Width, narrowing );
		var height = MathX.Lerp( expandedRect.Height, dock.Height, travel );
		var center = Vector2.Lerp( expandedRect.Center, dock.Center, travel );
		var topLeft = center - new Vector2( width, height ) * 0.5f;
		var offset = (topLeft - expandedRect.Position) * ScaleFromScreen;
		var transform = new PanelTransform();
		transform.AddTranslate( offset.x, offset.y );
		transform.AddScale( new Vector3( width / expandedRect.Width, height / expandedRect.Height, 1 ) );

		Style.TransformOriginX = 0;
		Style.TransformOriginY = 0;
		Style.Transform = transform;
	}

	void FinishWindowAnimation()
	{
		IsAnimating = false;
		Style.Transform = previousTransform;
		Style.TransformOriginX = previousOriginX;
		Style.TransformOriginY = previousOriginY;

		if ( IsMinimized )
		{
			Style.Width = MinimizedSize;
			Style.Height = MinimizedSize;
		}

		SetClass( "animating", false );
		SetClass( "minimized", IsMinimized );
		StateHasChanged();
		SetNeedsFinalLayout();
	}
}
