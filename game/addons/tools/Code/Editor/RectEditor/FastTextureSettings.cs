namespace Editor.RectEditor;

public enum MappingMode
{
	/// <summary>
	/// Unwrap the selected faces and attempt to fit them into the current rectangle.
	/// Forces the faces into a grid-like alignment (straightens curves).
	/// </summary>
	[Icon( "auto_awesome_mosaic" )]
	UnwrapSquare,

	/// <summary>
	/// Unwrap the selected faces while maintaining their shape and angles.
	/// Best for organic shapes or when distortion should be minimized.
	/// </summary>
	[Icon( "texture" )]
	UnwrapConforming,

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

public enum ScaleMode
{
	[Title( "Fit To Rectangle" ), Icon( "aspect_ratio" )]
	Fit,

	[Title( "World Scale" ), Icon( "public" )]
	WorldScale,

	[Title( "Tile U" ), Icon( "view_column" )]
	TileU,

	[Title( "Tile V" ), Icon( "view_stream" )]
	TileV
}

public enum TileMode
{
	[Title( "Maintain Aspect" ), Icon( "crop_free" )]
	MaintainAspect,

	[Title( "Repeat" ), Icon( "repeat" )]
	Repeat
}

public enum DebugMode
{
	[Title( "Default" ), Icon( "texture" )]
	Default,

	[Title( "Roughness" ), Icon( "grain" )]
	Roughness,

	[Title( "Normals" ), Icon( "waves" )]
	Normals
}

public class FastTextureSettings
{
	[Hide] private MappingMode _mapping = MappingMode.UnwrapSquare;
	[Hide] private AlignmentMode _alignment = AlignmentMode.UAxis;

	[Hide] private ScaleMode _scaleMode = ScaleMode.Fit;
	[Hide] private TileMode _tileMode = TileMode.MaintainAspect;
	[Hide] private DebugMode _debugMode = DebugMode.Default;
	[Hide] private float _repeat = 1.0f;

	[Hide] private bool _isTileView;
	[Hide] private bool _showRects;
	[Hide] private bool _isFlippedHorizontal;
	[Hide] private bool _isFlippedVertical;
	[Hide] private float _insetX;
	[Hide] private float _insetY;

	[Hide] private Vector2 _savedRectMin = Vector2.Zero;
	[Hide] private Vector2 _savedRectMax = Vector2.One;

	public FastTextureSettings()
	{
		Load();
	}

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

	[Category( "Tiling" ), WideMode( HasLabel = false )]
	public ScaleMode ScaleMode
	{
		get => _scaleMode;
		set
		{
			if ( _scaleMode == value ) return;
			_scaleMode = value;
			OnSettingsChanged?.Invoke();
		}
	}

	[Category( "Tiling" )]
	[HideIf( nameof( ScaleMode ), ScaleMode.Fit )]
	[WideMode( HasLabel = false )]
	public TileMode TileMode
	{
		get => _tileMode;
		set
		{
			if ( _tileMode == value ) return;
			_tileMode = value;
			OnSettingsChanged?.Invoke();
		}
	}

	[Category( "Tiling" )]
	[HideIf( nameof( ScaleMode ), ScaleMode.Fit )]
	[WideMode]
	[Range( 0.125f, 8.0f ), Step( 0.125f )]
	public float Repeat
	{
		get => _repeat;
		set
		{
			if ( _repeat == value ) return;
			_repeat = value;
			OnSettingsChanged?.Invoke();
		}
	}

	[Category( "Debug" ), WideMode( HasLabel = false )]
	public DebugMode DebugMode
	{
		get => _debugMode;
		set
		{
			if ( _debugMode == value ) return;
			_debugMode = value;
			OnSettingsChanged?.Invoke();
		}
	}

	[Category( "Inset" )]
	[WideMode( HasLabel = false )]
	[Range( 0.0f, 32.0f ), Step( 1.0f )]
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
	[WideMode( HasLabel = false )]
	[Range( 0.0f, 32.0f ), Step( 1.0f )]
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
	public Vector2 SavedRectMin
	{
		get => _savedRectMin;
		set
		{
			if ( _savedRectMin != value )
			{
				_savedRectMin = value;
			}
		}
	}

	[Hide]
	public Vector2 SavedRectMax
	{
		get => _savedRectMax;
		set
		{
			if ( _savedRectMax != value )
			{
				_savedRectMax = value;
			}
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

	private struct FastTextureSettingsDto
	{
		public MappingMode Mapping { get; set; }
		public AlignmentMode Alignment { get; set; }
		public ScaleMode ScaleMode { get; set; }
		public TileMode TileMode { get; set; }
		public DebugMode DebugMode { get; set; }
		public float Repeat { get; set; }
		public bool IsTileView { get; set; }
		public bool ShowRects { get; set; }
		public bool IsFlippedHorizontal { get; set; }
		public bool IsFlippedVertical { get; set; }
		public float InsetX { get; set; }
		public float InsetY { get; set; }
		public Vector2 SavedRectMin { get; set; }
		public Vector2 SavedRectMax { get; set; }
	}

	public void Load()
	{
		var json = ProjectCookie.Get( "FastTexture.Settings", string.Empty );
		if ( string.IsNullOrWhiteSpace( json ) )
			return;

		try
		{
			var dto = JsonSerializer.Deserialize<FastTextureSettingsDto>( json );

			_mapping = dto.Mapping;
			_alignment = dto.Alignment;
			_scaleMode = dto.ScaleMode;
			_tileMode = dto.TileMode;
			_debugMode = dto.DebugMode;
			_repeat = dto.Repeat;
			_isTileView = dto.IsTileView;
			_showRects = dto.ShowRects;
			_isFlippedHorizontal = dto.IsFlippedHorizontal;
			_isFlippedVertical = dto.IsFlippedVertical;
			_insetX = dto.InsetX;
			_insetY = dto.InsetY;
			_savedRectMin = dto.SavedRectMin;
			_savedRectMax = dto.SavedRectMax;
		}
		catch
		{
		}
	}

	public void Save()
	{
		var dto = new FastTextureSettingsDto
		{
			Mapping = _mapping,
			Alignment = _alignment,
			ScaleMode = _scaleMode,
			TileMode = _tileMode,
			DebugMode = _debugMode,
			Repeat = _repeat,
			IsTileView = _isTileView,
			ShowRects = _showRects,
			IsFlippedHorizontal = _isFlippedHorizontal,
			IsFlippedVertical = _isFlippedVertical,
			InsetX = _insetX,
			InsetY = _insetY,
			SavedRectMin = _savedRectMin,
			SavedRectMax = _savedRectMax
		};

		var json = JsonSerializer.Serialize( dto );
		ProjectCookie.Set( "FastTexture.Settings", json );
	}
}
