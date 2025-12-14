namespace Editor.RectEditor;

public enum MappingMode
{
	/// <summary>
	/// Unwrap the selected faces and attempt to fit them into the current rectangle
	/// </summary>
	[Icon( "auto_awesome_mosaic" )]
	UnwrapSquare,

	/// <summary>
	/// Planar Project the selected faces based on the current view. Can be selected again to update from a new view.
	/// </summary>
	[Icon( "video_camera_back" )]
	Planar,

	/// <summary>
	/// Use the existing UVs of the selected faces, but fit them to the current rectangle
	/// </summary>
	[Icon( "view_in_ar" )]
	UseExisting
}

public enum AlignmentMode
{
	[Title( "U Axis" ), Icon( "keyboard_tab" )]
	UAxis,

	[Title( "V Axis" ), Icon( "vertical_align_bottom" )]
	VAxis
}

public class FastTextureSettings
{
	[Hide] private MappingMode _mapping = MappingMode.UnwrapSquare;
	[Hide] private AlignmentMode _alignment = AlignmentMode.UAxis;
	[Hide] private bool _isTileView;
	[Hide] private bool _showRects;
	[Hide] private bool _isFlippedHorizontal;
	[Hide] private bool _isFlippedVertical;
	[Hide] private float _insetX;
	[Hide] private float _insetY;

	[Category( "UV Mapping" ), WideMode( HasLabel = false )]
	public MappingMode Mapping
	{
		get => _mapping;
		set
		{
			if ( _mapping != value )
			{
				_mapping = value;
				OnMappingChanged?.Invoke();
			}
		}
	}

	[Category( "UV Mapping" )]
	public bool ShowRects
	{
		get => _showRects;
		set
		{
			if ( _showRects != value )
			{
				_showRects = value;
				OnSettingsChanged?.Invoke();
			}
		}
	}

	[Category( "UV Mapping" ), Title( "Tile View" )]
	public bool IsTileView
	{
		get => _isTileView;
		set
		{
			if ( _isTileView != value )
			{
				_isTileView = value;
				OnSettingsChanged?.Invoke();
			}
		}
	}

	[Category( "Alignment" ), WideMode( HasLabel = false )]
	public AlignmentMode Alignment
	{
		get => _alignment;
		set
		{
			if ( _alignment != value )
			{
				_alignment = value;
				OnSettingsChanged?.Invoke();
			}
		}
	}

	[Category( "Alignment" ), Title( "Horizontal Flip" )]
	public bool IsFlippedHorizontal
	{
		get => _isFlippedHorizontal;
		set
		{
			if ( _isFlippedHorizontal != value )
			{
				_isFlippedHorizontal = value;
				OnSettingsChanged?.Invoke();
			}
		}
	}

	[Category( "Alignment" ), Title( "Vertical Flip" )]
	public bool IsFlippedVertical
	{
		get => _isFlippedVertical;
		set
		{
			if ( _isFlippedVertical != value )
			{
				_isFlippedVertical = value;
				OnSettingsChanged?.Invoke();
			}
		}
	}

	[Category( "Inset" )]
	public float InsetX
	{
		get => _insetX;
		set
		{
			if ( _insetX == value )
				return;

			_insetX = value;
			OnSettingsChanged?.Invoke();
		}
	}

	[Category( "Inset" )]
	public float InsetY
	{
		get => _insetY;
		set
		{
			if ( _insetY == value )
				return;

			_insetY = value;
			OnSettingsChanged?.Invoke();
		}
	}

	[Hide]
	public bool IsPickingEdge { get; set; }

	[Category( "Alignment" ), Button( "Pick Edge", "border_vertical" )]
	public void PickEdge()
	{
		IsPickingEdge = !IsPickingEdge;
	}

	[Hide]
	public Action OnMappingChanged { get; set; }

	[Hide]
	public Action OnSettingsChanged { get; set; }
}
