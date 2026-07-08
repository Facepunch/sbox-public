using System.Runtime.InteropServices;

namespace Editor;

public static partial class Theme
{
	public static Color32[] QtColors = new Color32[(int)QPalette.ColorRole.NumColorRoles];

	internal static void CollectQtColors()
	{
		QtColors[(int)QPalette.ColorRole.WindowText] = Text;
		QtColors[(int)QPalette.ColorRole.Mid] = Color.Transparent;
		QtColors[(int)QPalette.ColorRole.Text] = Text;
		QtColors[(int)QPalette.ColorRole.BrightText] = TextLight;
		QtColors[(int)QPalette.ColorRole.ButtonText] = TextButton;
		QtColors[(int)QPalette.ColorRole.Base] = Base;
		QtColors[(int)QPalette.ColorRole.AlternateBase] = BaseAlt;
		QtColors[(int)QPalette.ColorRole.Window] = WindowBackground;
		QtColors[(int)QPalette.ColorRole.Shadow] = Shadow;
		QtColors[(int)QPalette.ColorRole.Highlight] = Highlight;
		QtColors[(int)QPalette.ColorRole.HighlightedText] = TextHighlight;
		QtColors[(int)QPalette.ColorRole.Link] = TextLink;
		QtColors[(int)QPalette.ColorRole.LinkVisited] = TextLink;
		QtColors[(int)QPalette.ColorRole.NoRole] = SurfaceBackground;
		QtColors[(int)QPalette.ColorRole.ToolTipBase] = SurfaceBackground;
		QtColors[(int)QPalette.ColorRole.ToolTipText] = Text;
		QtColors[(int)QPalette.ColorRole.PlaceholderText] = TextLight;
		QtColors[(int)QPalette.ColorRole.Grid] = Base;
		QtColors[(int)QPalette.ColorRole.DarkSplitterHandle] = Border;
		QtColors[(int)QPalette.ColorRole.HeaderSectionBackground] = WindowBackground;
		QtColors[(int)QPalette.ColorRole.Black] = Color.Black;
		QtColors[(int)QPalette.ColorRole.Red] = Color.Red;
		QtColors[(int)QPalette.ColorRole.Green] = Color.Green;
		QtColors[(int)QPalette.ColorRole.Blue] = Color.Blue;
		QtColors[(int)QPalette.ColorRole.CheckmarkPossible] = Primary;
		QtColors[(int)QPalette.ColorRole.FlatSelection] = Color.Transparent;
		QtColors[(int)QPalette.ColorRole.DarkSplitter] = SurfaceBackground;
		QtColors[(int)QPalette.ColorRole.MenuBackground] = WindowBackground;
		QtColors[(int)QPalette.ColorRole.MenuBarBackground] = WindowBackground;
		QtColors[(int)QPalette.ColorRole.ToolBarBackground] = WindowBackground;
		QtColors[(int)QPalette.ColorRole.ViewportBackground] = WindowBackground;
		QtColors[(int)QPalette.ColorRole.ViewportText] = TextLight;
		QtColors[(int)QPalette.ColorRole.DockWidgetTitleBackground] = SurfaceBackground;
		QtColors[(int)QPalette.ColorRole.TabBarBackgroundNotSelected] = SurfaceBackground;
		QtColors[(int)QPalette.ColorRole.TabTextInactive] = TextLight;
		QtColors[(int)QPalette.ColorRole.TabBarBackground] = TabBarBackground;
		QtColors[(int)QPalette.ColorRole.Button] = ButtonBackground;
		QtColors[(int)QPalette.ColorRole.DefaultButtonBackground] = SurfaceBackground;
		QtColors[(int)QPalette.ColorRole.ButtonBackground] = ButtonBackground;
		QtColors[(int)QPalette.ColorRole.ButtonHighlight] = ButtonBackground;
		QtColors[(int)QPalette.ColorRole.ButtonPressed] = ButtonBackground;
		QtColors[(int)QPalette.ColorRole.DefaultButtonOuterBorder] = Border;
		QtColors[(int)QPalette.ColorRole.DefaultButtonInnerBorder] = Border;
		QtColors[(int)QPalette.ColorRole.ToolButtonBackground] = ButtonBackground;
		QtColors[(int)QPalette.ColorRole.ToolButtonActiveBackground] = ButtonBackground;
		QtColors[(int)QPalette.ColorRole.LightSplitterHandle] = Color.Transparent;
		QtColors[(int)QPalette.ColorRole.ScrollbarBackground] = Color.Transparent;
		QtColors[(int)QPalette.ColorRole.HeaderSectionLine] = Color.Transparent;
		QtColors[(int)QPalette.ColorRole.InnerHighlight] = Color.Transparent;
		QtColors[(int)QPalette.ColorRole.LighterLine] = BorderLight;
		QtColors[(int)QPalette.ColorRole.UpperLine] = BorderLight;
		QtColors[(int)QPalette.ColorRole.LowerLine] = BorderLight;
		QtColors[(int)QPalette.ColorRole.ToolButtonBorder] = Color.Transparent;
		QtColors[(int)QPalette.ColorRole.ToolBarBorderLight] = Color.Transparent;
		QtColors[(int)QPalette.ColorRole.ToolBarBorderDark] = Color.Transparent;
		QtColors[(int)QPalette.ColorRole.ButtonBorder] = BorderButton;
		QtColors[(int)QPalette.ColorRole.ToolButtonActiveBorder] = Border;
		QtColors[(int)QPalette.ColorRole.ViewportToolBarBorderLight] = BorderLight;
		QtColors[(int)QPalette.ColorRole.ViewportToolBarBorderDark] = SurfaceBackground;
		QtColors[(int)QPalette.ColorRole.Light] = BorderLight;
		QtColors[(int)QPalette.ColorRole.Midlight] = BorderLight;
		QtColors[(int)QPalette.ColorRole.Dark] = BorderLight;
	}

	private static GCHandle PaletteHandle;
	private static unsafe Color32* PalettePtr;

	internal static unsafe Color32* GetPaletteColors()
	{
		return PalettePtr;
	}

	internal static string GetDefaultFont()
	{
		return DefaultFont;
	}

	public static Color Blue;
	public static Color Green;
	public static Color Red;
	public static Color Yellow;
	public static Color Pink;

	public static Color Prefab;
	public static Color Folder;

	public static Color TabBackground;
	public static Color TabBarBackground;
	public static Color TabInactiveBackground;
	public static Color SurfaceBackground;
	public static Color SurfaceLightBackground;
	public static Color SidebarBackground;
	public static Color WindowBackground;
	public static Color WidgetBackground;
	public static Color ControlBackground;
	public static Color ButtonBackground;
	public static Color SelectedBackground;
	public static Color StatusBarBackground;
	public static Color Text;
	public static Color TextControl;
	public static Color TextLight;
	public static Color TextWidget;
	public static Color TextButton;
	public static Color TextSelected;
	public static Color TextLink;
	public static Color TextHighlight;
	public static Color TextDisabled;
	public static Color Border;
	public static Color BorderLight;
	public static Color BorderButton;
	public static Color Shadow;
	public static Color Primary;
	public static Color Overlay;
	public static Color MultipleValues;
	public static Color Highlight;
	public static Color TextDark;
	public static Color Base;
	public static Color BaseAlt;

	public static Color ToggleEnabled;
	public static Color ToggleDisabled;

	public static float ControlRadius;
	public static float ControlHeight;
	public static float RowHeight;

	public static string HeadingFont;
	public static string DefaultFont;
	public static string MonospaceFont;

	static unsafe Theme()
	{
		LoadFromFile();

		CollectQtColors();

		PaletteHandle = GCHandle.Alloc( QtColors, GCHandleType.Pinned );
		PalettePtr = (Color32*)PaletteHandle.AddrOfPinnedObject();
	}

	private static void LoadFromFile()
	{
		var themeJson = FileSystem.Root.ReadAllText( "/addons/tools/assets/styles/theme.json" );
		var theme = string.IsNullOrWhiteSpace( themeJson )
			? null
			: Json.Deserialize<Dictionary<string, string>>( themeJson );

		// The editor theme file isn't shipped with a STANDALONE build (and a hand-edited file may be missing
		// keys), so every lookup is defensive: a missing file or key returns null and falls through to the inline
		// default below. Without this, the static ctor threw on every field the moment anything touched Editor.Theme
		// in a non-editor process (e.g. the shutdown reflection sweep), spamming the log.
		string Get( string key ) => theme is not null && theme.TryGetValue( key, out var value ) ? value : null;

		TabBackground = Color.Parse( Get( "TabBackground" ) ) ?? Color.Parse( "#3b3b3b" ).Value;
		TabBarBackground = Color.Parse( Get( "TabBarBackground" ) ) ?? Color.Parse( "#242424" ).Value;
		TabInactiveBackground = Color.Parse( Get( "TabInactiveBackground" ) ) ?? Color.Parse( "#242424" ).Value;
		SurfaceBackground = Color.Parse( Get( "SurfaceBackground" ) ) ?? Color.Parse( "#3b3b3b" ).Value;
		SurfaceLightBackground = Color.Parse( Get( "SurfaceLightBackground" ) ) ?? Color.Parse( "#696969" ).Value;
		SidebarBackground = Color.Parse( Get( "SidebarBackground" ) ) ?? Color.Parse( "#242424" ).Value;
		WindowBackground = Color.Parse( Get( "WindowBackground" ) ) ?? Color.Parse( "#181818" ).Value;
		WidgetBackground = Color.Parse( Get( "WidgetBackground" ) ) ?? Color.Parse( "#242424" ).Value;
		ControlBackground = Color.Parse( Get( "ControlBackground" ) ) ?? Color.Parse( "#181818" ).Value;
		ButtonBackground = Color.Parse( Get( "ButtonBackground" ) ) ?? Color.Parse( "#181818" ).Value;
		SelectedBackground = Color.Parse( Get( "SelectedBackground" ) ) ?? Color.Parse( "#808080" ).Value;
		StatusBarBackground = Color.Parse( Get( "StatusBarBackground" ) ) ?? Color.Parse( "#242424" ).Value;
		Text = Color.Parse( Get( "Text" ) ) ?? Color.Parse( "#FFFFFF" ).Value;
		TextControl = Color.Parse( Get( "TextControl" ) ) ?? Color.Parse( "#FFFFFF" ).Value;
		TextLight = Color.Parse( Get( "TextLight" ) ) ?? Color.Parse( "#9E9E9E" ).Value;
		TextWidget = Color.Parse( Get( "TextWidget" ) ) ?? Color.Parse( "#FFFFFF" ).Value;
		TextButton = Color.Parse( Get( "TextButton" ) ) ?? Color.Parse( "#FFFFFF" ).Value;
		TextSelected = Color.Parse( Get( "TextSelected" ) ) ?? Color.Parse( "#66a3ff" ).Value;
		TextLink = Color.Parse( Get( "TextLink" ) ) ?? Color.Parse( "#FFFFFF" ).Value;
		TextHighlight = Color.Parse( Get( "TextHighlight" ) ) ?? Color.Parse( "#66a3ff" ).Value;
		TextDisabled = Color.Parse( Get( "TextDisabled" ) ) ?? Color.Parse( "#FFFFFF55" ).Value;
		Border = Color.Parse( Get( "Border" ) ) ?? Color.Parse( "#525252" ).Value;
		BorderLight = Color.Parse( Get( "BorderLight" ) ) ?? Color.Parse( "#696969" ).Value;
		BorderButton = Color.Parse( Get( "BorderButton" ) ) ?? Color.Parse( "#696969" ).Value;
		Shadow = Color.Parse( Get( "Shadow" ) ) ?? Color.Parse( "#242424" ).Value;
		Primary = Color.Parse( Get( "Primary" ) ) ?? Color.Parse( "#5a8deb" ).Value;
		Overlay = Color.Parse( Get( "Overlay" ) ) ?? Color.Parse( "#242424" ).Value;
		MultipleValues = Color.Parse( Get( "MultipleValues" ) ) ?? Color.Parse( "#808080" ).Value;
		Highlight = Color.Parse( Get( "Highlight" ) ) ?? Color.Parse( "#9E9E9E" ).Value;
		TextDark = Color.Parse( Get( "TextDark" ) ) ?? Color.Parse( "#000000" ).Value;
		Base = Color.Parse( Get( "Base" ) ) ?? Color.Parse( "#202020" ).Value;
		BaseAlt = Color.Parse( Get( "BaseAlt" ) ) ?? Color.Parse( "#242424" ).Value;

		ToggleEnabled = Color.Parse( Get( "ToggleEnabled" ) ) ?? Color.Parse( "#5aeb5c" ).Value;
		ToggleDisabled = Color.Parse( Get( "ToggleDisabled" ) ) ?? Color.Parse( "#566e56" ).Value;

		Blue = Color.Parse( Get( "Blue" ) ) ?? Color.Parse( "#3273EB" ).Value;
		Green = Color.Parse( Get( "Green" ) ) ?? Color.Parse( "#B0E24D" ).Value;
		Red = Color.Parse( Get( "Red" ) ) ?? Color.Parse( "#FB5A5A" ).Value;
		Yellow = Color.Parse( Get( "Yellow" ) ) ?? Color.Parse( "#E6DB74" ).Value;
		Pink = Color.Parse( Get( "Pink" ) ) ?? Color.Parse( "#DF9194" ).Value;

		Prefab = Color.Parse( Get( "Prefab" ) ) ?? Color.Parse( "#3273EB" ).Value;
		Folder = Color.Parse( Get( "Folder" ) ) ?? Color.Parse( "#E6DB74" ).Value;

		if ( !float.TryParse( Get( "ControlRadius" ), out ControlRadius ) )
			ControlRadius = 3.0f;

		if ( !float.TryParse( Get( "ControlHeight" ), out ControlHeight ) )
			ControlHeight = 18.0f;

		if ( !float.TryParse( Get( "RowHeight" ), out RowHeight ) )
			RowHeight = 16.0f;

		HeadingFont = Get( "HeadingFont" );
		DefaultFont = Get( "DefaultFont" );
		MonospaceFont = Get( "MonospaceFont" );
	}

	public static Color GetTint( EditorTint tint )
	{
		return tint switch
		{
			EditorTint.Blue => Blue,
			EditorTint.Green => Green,
			EditorTint.Red => Red,
			EditorTint.Yellow => Yellow,
			EditorTint.Pink => Pink,
			_ => Color.White
		};
	}

	public static void DrawFilename( Rect rect, string filename, TextFlag flags, Color color )
	{
		var dir = System.IO.Path.GetDirectoryName( filename ) + "/";
		var file = System.IO.Path.GetFileNameWithoutExtension( filename );
		var extension = System.IO.Path.GetExtension( filename );

		// if we really cared we could do this better
		var size = Paint.MeasureText( rect, filename, flags );
		var overshoot = (size.Width - rect.Width) + 5;
		if ( overshoot > 0 )
		{
			overshoot += 10;
			var startIndex = (overshoot / 4).CeilToInt();

			if ( startIndex < dir.Length )
				dir = ".." + dir.Substring( startIndex );
			else
				dir = "";
		}

		dir = dir.Replace( '\\', '/' );

		Paint.SetPen( color.Darken( 0.3f ) );
		var r = Paint.DrawText( rect, dir, flags );
		rect.Left += r.Width;
		Paint.SetPen( color );
		r = Paint.DrawText( rect, file, flags );
		rect.Left += r.Width;
		Paint.SetPen( color.Darken( 0.1f ) );
		r = Paint.DrawText( rect, extension, flags );
	}

	/// <summary>
	/// Draw a button with a background color, text, and an optional icon. We'll use Paint.HasPressed and Paint.HasMouseOver to determine the button's state.
	/// </summary>
	public static void DrawButton( Rect LocalRect, string text = null, string Icon = null, bool Enabled = true, Color? Tint = null )
	{
		var c = Tint?.ToHsv() ?? Color.Parse( "#48494c" )?.ToHsv() ?? default;
		var bg = c;

		if ( Enabled )
		{
			if ( Paint.HasPressed )
			{
				bg = c with { Value = (c.Value + 0.1f) };
			}
			else if ( Paint.HasMouseOver )
			{
				bg = c with { Value = (c.Value + 0.2f) };
			}
		}
		else
		{
			bg = c = Theme.SurfaceLightBackground;
		}

		if ( !Enabled )
		{
			c = c.WithSaturation( 0.1f ).WithAlpha( 0.5f );
			bg = c.WithAlpha( 0.2f );
		}

		if ( bg.Alpha > 0 )
		{
			float radius = 3;
			Paint.Antialiasing = true;

			Paint.ClearPen();
			Paint.SetBrush( bg with { Value = (bg.Value + 0.04f), Saturation = (c.Saturation * 0.8f) } );
			Paint.DrawRect( LocalRect, radius );

			Paint.SetBrushLinear( LocalRect.TopLeft, LocalRect.BottomRight, bg, bg with { Value = (bg.Value - 0.03f) } );
			Paint.DrawRect( LocalRect.Shrink( 1, 1, 1, 1 ), radius );
		}
		else
		{
			c = Color.White.WithAlpha( 0.5f );
		}

		Paint.SetDefaultFont();

		if ( !string.IsNullOrWhiteSpace( text ) )
		{
			var textSize = Paint.MeasureText( text );
			float iconSize = 16;
			float spacing = 4;

			float startX = 8;
			float centerY = LocalRect.Center.y;

			Paint.SetPen( c with { Value = 0.99f, Saturation = c.Saturation * 0.20f } );

			if ( !string.IsNullOrWhiteSpace( Icon ) )
			{
				var iconRect = new Rect( startX, centerY - iconSize / 2, iconSize, iconSize );
				Paint.DrawIcon( iconRect, Icon, iconSize );
				startX += iconSize + spacing;
			}

			Paint.SetPen( c with { Value = 0.99f, Saturation = c.Saturation * 0.20f, Alpha = 0.75f } );

			var textRect = new Rect( startX, LocalRect.Top, textSize.x, LocalRect.Height );
			Paint.DrawText( textRect, text, TextFlag.Center );
		}
	}

	/// <summary>
	/// Draw a dropdown control with a text label and an icon. Has a up/down chevron icon to indicate the dropdown state.
	/// </summary>
	public static void DrawDropdown( Rect rect, string text, string icon, bool open, bool disabled = false )
	{
		Paint.ClearPen();

		if ( open ) Paint.SetBrush( Theme.ControlBackground.LerpTo( Theme.Green, 0.05f ) );
		else Paint.SetBrush( Theme.ControlBackground );

		Paint.DrawRect( rect, Theme.ControlRadius );

		// cap off the bottom
		if ( open )
		{
			Paint.DrawRect( rect.Shrink( 0, 4, 0, 0 ) );
		}

		if ( disabled ) Paint.SetPen( Theme.Text.WithAlpha( 0.2f ) );
		else if ( open ) Paint.SetPen( Theme.Green );
		else if ( Paint.HasMouseOver ) Paint.SetPen( Theme.TextButton.WithAlpha( 0.7f ) );
		else Paint.SetPen( Theme.TextButton.WithAlpha( 0.5f ) );

		Paint.DrawIcon( rect.Shrink( 0, 0, 4, 0 ), open ? "expand_less" : "expand_more", rect.Height - 3, TextFlag.RightCenter );
		Paint.DrawIcon( rect.Shrink( 4, 0, 0, 0 ), icon, 16, TextFlag.LeftCenter );

		Paint.SetDefaultFont();
		Paint.DrawText( rect.Shrink( 30, 0, 26, 0 ), text, TextFlag.LeftCenter );
	}

	internal static string ParseVariables( string input )
	{
		var values = new Dictionary<string, string>();
		var fields = typeof( Theme ).GetFields();

		foreach ( var field in fields )
		{
			var value = field.GetValue( null );

			if ( value is Color color )
			{
				values.Add( $"${field.Name}", color.Hex );
			}
			else if ( value is float f )
			{
				values.Add( $"${field.Name}", $"{f}px" );
			}
			else if ( value is string s )
			{
				values.Add( $"${field.Name}", s );
			}
		}

		// Sort by length so we don't stomp stuff, e.g. $Surface and $SurfaceSubtle
		values = values.OrderByDescending( x => x.Key.Length ).ToDictionary();

		foreach ( var color in values )
		{
			input = input.Replace( color.Key, color.Value );
		}

		return input;
	}
}
