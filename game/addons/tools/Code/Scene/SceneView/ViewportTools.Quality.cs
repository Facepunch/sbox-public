using Sandbox.Engine.Settings;

namespace Editor;

partial class ViewportTools
{
	// Global settings shared with the game, not a per-viewport override.
	void OpenQualityMenu()
	{
		var menu = new ContextMenu( this );
		var settings = EditorUtility.RenderSettings;
		var current = EditorUtility.CurrentGraphicsPreset;

		foreach ( var preset in QualityPresets )
		{
			var option = menu.AddOption( preset.ToString(), null, () => EditorUtility.ApplyGraphicsPreset( preset ) );
			option.Checkable = true;
			option.Checked = current == preset;
		}

		menu.AddSeparator();

		menu.AddOption( "Auto-Detect", "auto_awesome", () => EditorUtility.ApplyGraphicsPreset( EditorUtility.DetectGraphicsPreset() ) );

		menu.AddSeparator();

		{
			var widget = new Widget( menu );
			widget.OnPaintOverride = () =>
			{
				Paint.SetBrushAndPen( Theme.WidgetBackground.WithAlpha( 0.5f ) );
				Paint.DrawRect( widget.LocalRect.Shrink( 2 ), 2 );
				return true;
			};

			var so = settings.GetSerialized();

			// Anti-aliasing only lands through OnVideoSettingsChanged, which Apply fires.
			so.OnPropertyChanged += _ => settings.Apply();

			var cs = new ControlSheet();

			cs.AddRow( so.GetProperty( nameof( RenderSettings.TextureQuality ) ) );
			cs.AddRow( so.GetProperty( nameof( RenderSettings.ShadowQuality ) ) );
			cs.AddRow( so.GetProperty( nameof( RenderSettings.VolumetricFogQuality ) ) );
			cs.AddRow( so.GetProperty( nameof( RenderSettings.PostProcessQuality ) ) );
			cs.AddRow( so.GetProperty( nameof( RenderSettings.AntiAliasQuality ) ) );

			widget.Layout = cs;
			widget.MaximumWidth = 400;

			menu.AddWidget( widget );
		}

		menu.OpenAtCursor();
	}

	static readonly GraphicsPreset[] QualityPresets = [GraphicsPreset.Low, GraphicsPreset.Medium, GraphicsPreset.High, GraphicsPreset.Ultra];
}
