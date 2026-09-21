namespace Sandbox.PanelGallery;

/// <summary>
/// A square preview with a title and description.
/// </summary>
public sealed class GalleryExample : Panel
{
	public Panel Result { get; }

	public GalleryExample( Panel parent, string title, string description, float height = 240 )
	{
		parent.AddChild( this );
		AddClass( "reference-row" );
		var frame = Add.Panel( "reference-result" );
		Result = new GalleryScaledPreview { PreviewSize = new Vector2( 420, height ) };
		frame.AddChild( Result );
		var caption = Add.Panel( "reference-description" );
		caption.Tooltip = description;
		caption.Add.Label( title, "reference-title" );
		if ( !string.IsNullOrWhiteSpace( description ) ) caption.Add.Label( description, "reference-note" );
	}
}

/// <summary>
/// Retains the specimen's drawing dimensions while fitting it into a compact result cell.
/// </summary>
public sealed class GalleryScaledPreview : Panel
{
	Vector2 previewSize = new( 420, 240 );
	float lastScale = -1;

	[Microsoft.AspNetCore.Components.Parameter]
	public bool FitContent { get; set; }

	[Microsoft.AspNetCore.Components.Parameter]
	public bool ActualSize { get; set; }

	[Microsoft.AspNetCore.Components.Parameter]
	public Vector2 PreviewSize
	{
		get => previewSize;
		set { previewSize = value; Style.Width = value.x; Style.Height = value.y; }
	}

	public GalleryScaledPreview()
	{
		AddClass( "scaled-preview" );
		PreviewSize = previewSize;
	}

	public override void Tick()
	{
		base.Tick();
		if ( Parent is null ) return;
		var available = Parent.Box.RectInner.Size / ScaleToScreen;
		var extent = PreviewSize;
		if ( FitContent )
		{
			var children = Children.Count() == 1 && Children.First().HasClass( "row" ) ? Children.First().Children : Children;
			float left = float.MaxValue, top = float.MaxValue, right = float.MinValue, bottom = float.MinValue;
			foreach ( var child in children )
			{
				var rect = child.Box.Rect;
				if ( rect.Width <= 0 || rect.Height <= 0 ) continue;
				left = MathF.Min( left, rect.Left ); top = MathF.Min( top, rect.Top );
				right = MathF.Max( right, rect.Right ); bottom = MathF.Max( bottom, rect.Bottom );
			}
			if ( right > left && bottom > top ) extent = new Vector2( right - left, bottom - top ) / ScaleToScreen;
		}
		float scale = ActualSize ? 1 : MathF.Min( 1, MathF.Min( available.x / extent.x, available.y / extent.y ) );
		if ( scale <= 0 || scale == lastScale ) return;
		lastScale = scale;
		Style.Set( "transform", FormattableString.Invariant( $"scale({scale})" ) );
	}
}
