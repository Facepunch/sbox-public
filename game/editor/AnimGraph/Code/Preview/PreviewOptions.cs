namespace Editor.AnimGraph;

/// <summary>
/// The visualization options a preview offers. Only what a tool can actually show - a clip
/// preview has no graph to name active animations from.
/// </summary>
[Flags]
internal enum PreviewOption
{
	None = 0,
	Bones = 1,
	Ground = 2,
	RootMotion = 4,
	FocusBone = 8,
	Camera = 16,

	/// <summary>Everything a plain model preview can show.</summary>
	All = Bones | Ground | RootMotion | FocusBone | Camera
}

/// <summary>
/// The display and camera controls shared by every 3D preview - what to draw over the model,
/// where to point the camera, how wide a lens to use. Each tool remembers its own settings, so
/// turning bones on in the skeleton editor doesn't turn them on in the graph.
/// </summary>
internal sealed class PreviewOptions
{
	private readonly PreviewSceneWidget _preview;
	private readonly string _tool;
	private readonly PreviewOption _options;
	private readonly List<(string Title, string Icon, Func<bool> Get, Action<bool> Set)> _extra = [];

	private FocusBoneSettings _focusSettings;

	/// <summary>
	/// The bar's toolbar, so a tool can hang its own buttons off the same strip.
	/// </summary>
	public PreviewToolbar Toolbar { get; } = new();

	/// <summary>
	/// Opens the camera settings, when a preview has more of them than a field of view.
	/// </summary>
	public Action<Widget> CameraSettings { get; set; }

	/// <summary>
	/// The bone picker, so a preview whose camera is locked can hide it.
	/// </summary>
	public Widget FocusPicker { get; private set; }

	/// <param name="preview">The preview these options drive.</param>
	/// <param name="tool">Names the tool's saved settings, so each one remembers its own.</param>
	/// <param name="options">What this preview can actually show.</param>
	/// <param name="bonesByDefault">Start with the skeleton drawn, where that's the point of the tool.</param>
	public PreviewOptions( PreviewSceneWidget preview, string tool, PreviewOption options = PreviewOption.All, bool bonesByDefault = false )
	{
		_preview = preview;
		_tool = tool;
		_options = options;

		preview.Options = this;

		if ( Has( PreviewOption.Bones ) )
			preview.ShowBones = Cookie( "bones", bonesByDefault );

		if ( Has( PreviewOption.Ground ) )
			preview.ShowGround = Cookie( "ground", true );

		if ( Has( PreviewOption.RootMotion ) )
			preview.ShowRootMotion = Cookie( "rootmotion", false );

		if ( Has( PreviewOption.Camera ) )
			preview.FieldOfView = Cookie( "fov", 60f );
	}

	private bool Has( PreviewOption option ) => _options.HasFlag( option );

	public T Cookie<T>( string key, T fallback ) => EditorCookie.Get( $"animgraph.preview.{_tool}.{key}", fallback );
	public void SetCookie<T>( string key, T value ) => EditorCookie.Set( $"animgraph.preview.{_tool}.{key}", value );

	/// <summary>
	/// Add an option of the tool's own to the display menu, like the graph's active animations.
	/// </summary>
	public void AddDisplayOption( string title, string icon, Func<bool> getValue, Action<bool> setValue )
	{
		_extra.Add( (title, icon, getValue, setValue) );
	}

	/// <summary>
	/// The whole bar, for previews with nothing else to put on one. Display options on the left,
	/// camera controls on the right.
	/// </summary>
	public Widget CreateBar( Widget parent )
	{
		var bar = Toolbar.CreateBar( parent, out var row );

		AddDisplayGroup( row );
		row.AddStretchCell();
		AddCameraGroup( row );

		return bar;
	}

	/// <summary>
	/// The button that opens what to draw over the model.
	/// </summary>
	public void AddDisplayGroup( Layout row )
	{
		var group = PreviewToolbar.AddGroup( row );

		var button = Toolbar.AddButton( group, "visibility", "Preview display options", null );
		button.Action = () => OpenDisplayOptions( button );
	}

	/// <summary>
	/// Focusing the camera on a bone, framing it on the model, and the lens it looks through.
	/// </summary>
	public void AddCameraGroup( Layout row )
	{
		if ( Has( PreviewOption.FocusBone ) )
		{
			_focusSettings = new FocusBoneSettings { Bone = _preview.FocusBone };
			var so = _focusSettings.GetSerialized();
			so.OnPropertyChanged += _ => SetFocusBone( _focusSettings.Bone );

			FocusPicker = row.Add( new FocusBonePicker( so.GetProperty( nameof( FocusBoneSettings.Bone ) ), _preview )
			{
				FixedWidth = 160,
				ToolTip = "Focus the camera on a bone",
			} );
		}

		var view = PreviewToolbar.AddGroup( row );
		Toolbar.AddButton( view, "center_focus_strong", "Frame camera on model", _preview.FrameCamera );

		if ( !Has( PreviewOption.Camera ) )
			return;

		var camera = PreviewToolbar.AddGroup( row );
		var button = Toolbar.AddButton( camera, "video_settings", "Camera settings", null );
		button.Action = () => OpenCameraSettings( button );
	}

	/// <summary>
	/// Show the focus bone the preview ended up with - a saved one that the model doesn't have
	/// is dropped.
	/// </summary>
	public void RefreshFocusBone()
	{
		if ( _focusSettings is not null )
			_focusSettings.Bone = _preview.FocusBone;
	}

	private void SetFocusBone( string bone )
	{
		_preview.SetFocusBone( bone );
		RefreshFocusBone();
	}

	private void OpenDisplayOptions( Widget button )
	{
		var popup = new PopupWidget( button )
		{
			Layout = Layout.Column(),
			MinimumWidth = 220
		};

		popup.Layout.Margin = 8;
		popup.Layout.Spacing = 4;

		if ( Has( PreviewOption.RootMotion ) )
			AddCheckbox( popup, "Root Motion Velocity", "north_east", () => _preview.ShowRootMotion, value => SetCookie( "rootmotion", _preview.ShowRootMotion = value ) );

		foreach ( var option in _extra )
			AddCheckbox( popup, option.Title, option.Icon, option.Get, option.Set );

		if ( Has( PreviewOption.Bones ) )
			AddCheckbox( popup, "Bones", "device_hub", () => _preview.ShowBones, value => SetCookie( "bones", _preview.ShowBones = value ) );

		if ( Has( PreviewOption.Ground ) )
			AddCheckbox( popup, "Ground", "grid_4x4", () => _preview.ShowGround, value => SetCookie( "ground", _preview.ShowGround = value ) );

		popup.AdjustSize();
		popup.OpenAt( button.ScreenRect.BottomLeft, false );
	}

	private static void AddCheckbox( PopupWidget popup, string title, string icon, Func<bool> getValue, Action<bool> setValue )
	{
		var checkbox = popup.Layout.Add( new Checkbox( title, icon, popup ) { Value = getValue() } );
		checkbox.Toggled = () => setValue( checkbox.Value );
	}

	private void OpenCameraSettings( Widget button )
	{
		if ( CameraSettings is not null )
		{
			CameraSettings( button );
			return;
		}

		var settings = new CameraSettings { FieldOfView = _preview.FieldOfView };
		var so = settings.GetSerialized();
		so.OnPropertyChanged += property => SetCookie( "fov", _preview.FieldOfView = property.GetValue<float>() );

		OpenSettingsPopup( button, so );
	}

	/// <summary>
	/// The popup camera settings are edited in, so a preview with its own settings object still
	/// gets the same panel.
	/// </summary>
	public static void OpenSettingsPopup( Widget button, SerializedObject settings )
	{
		var popup = new PopupWidget( button )
		{
			Layout = Layout.Column(),
			MinimumWidth = 420
		};

		popup.Layout.Margin = 4;

		var sheet = new ControlSheet();
		sheet.AddObject( settings );
		popup.Layout.Add( sheet );

		popup.Position = button.ScreenRect.BottomLeft;
		popup.Visible = true;
		popup.AdjustSize();
		popup.ConstrainToScreen();
	}
}

internal class FocusBoneSettings
{
	public string Bone { get; set; }
}

internal class CameraSettings
{
	[Range( 10f, 90f ), Step( 1f )]
	public float FieldOfView { get; set; } = 60f;
}

/// <summary>
/// Picks the preview model's camera focus bone.
/// </summary>
internal class FocusBonePicker : BonePickerControlWidget
{
	private readonly PreviewSceneWidget _preview;

	public FocusBonePicker( SerializedProperty property, PreviewSceneWidget preview ) : base( property )
	{
		_preview = preview;
	}

	// The preview's own model, which the graph's target model isn't while a variation is previewed
	protected override Model TargetModel => _preview.PreviewModel;

	// The toolbar control is narrow, the bone names in it aren't
	protected override float PickerWidth => 380f;
}
