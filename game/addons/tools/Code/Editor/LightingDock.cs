namespace Editor;

/// <summary>
/// Edits lighting and tracks the active scene's lightmap bake.
/// </summary>
[Dock( "Editor", "Lighting", "wb_twilight", DockArea.Right )]
public sealed class LightingDock : Widget
{
	readonly Widget settings;
	readonly Widget toolbar;
	Widget progressBar;
	BakedLightmap errorSource;
	string error;

	/// <summary>
	/// Keeps bake actions and progress above the scrollable settings.
	/// </summary>
	public LightingDock( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		toolbar = Layout.Add( new Widget( this ) { Layout = Layout.Column() } );
		toolbar.OnPaintOverride = () =>
		{
			Paint.SetBrushAndPen( Theme.SidebarBackground );
			Paint.DrawRect( toolbar.LocalRect );
			return true;
		};
		toolbar.Layout.Margin = new Sandbox.UI.Margin( 14, 10, 14, 10 );
		toolbar.Layout.Spacing = 8;

		var scroll = Layout.Add( new ScrollArea( this ), 1 );
		scroll.Canvas = settings = new Widget( scroll ) { Layout = Layout.Column() };
		settings.Layout.Margin = 14;
		settings.Layout.Spacing = 6;
	}

	static SceneEditorSession ActiveSession => SceneEditorSession.Active is { IsPlaying: false, IsPrefabSession: false } session ? session : null;

	static BakedLightmap EditableLightmap => ActiveSession is { IsMounted: false, Scene.Lightmap: { BakeProgress: null } lightmap } ? lightmap : null;

	void AddHeader( SceneEditorSession session, bool editable )
	{
		var scene = session.Scene;
		var row = toolbar.Layout.AddRow();
		row.Spacing = 6;
		row.Add( new IconLabel( "movie" ) { IconSize = 18, Foreground = Theme.TextLight } );
		row.Add( new Label.Subtitle( scene.Name ) { Color = Theme.Text, ToolTip = scene.Name }, 1 );

		var mode = row.Add( new ComboBox { ToolTip = "Lighting mode", Enabled = editable } );
		foreach ( var (value, info) in DisplayInfo.ForEnumValues<SceneLightingMode>() )
		{
			mode.AddItem( info.Name, info.Icon, selected: scene.LightingMode == value, description: info.Description, onSelected: () =>
			{
				if ( scene.LightingMode == value ) return;

				scene.LightingMode = value;
				session.HasUnsavedChanges = true;
			} );
		}
	}

	[EditorEvent.Frame]
	void UpdateScene()
	{
		if ( !Visible )
			return;

		var session = ActiveSession;
		var scene = session?.Scene;
		var lightmap = scene?.Lightmap;
		var baking = lightmap?.BakeProgress is not null;
		if ( errorSource is not null && errorSource != lightmap )
		{
			error = null;
			errorSource = null;
		}

		if ( SetContentHash( HashCode.Combine( scene, scene?.Name, scene?.Source, lightmap, baking, lightmap?.Textures, lightmap?.LuxelSize, error ), 0 ) )
		{
			settings.Layout.Clear( true );
			toolbar.Layout.Clear( true );
			progressBar = null;
			toolbar.Visible = scene is not null;

			if ( scene is null )
			{
				settings.Layout.Add( new Label( "Open a scene in edit mode to change its lighting." ) );
			}
			else
			{
				var editable = !baking && !session.IsMounted;
				AddHeader( session, editable );

				if ( lightmap is not null )
				{
					var controls = settings.Layout.Add( new Widget { Layout = Layout.Column(), Enabled = editable } );
					controls.Layout.Spacing = 6;
					AddSettings( controls.Layout, session, lightmap );
					AddActions( session, lightmap, baking );
				}
				else
				{
					settings.Layout.Add( new Label( "The scene uses its lights, sky, and environment probes." ) { WordWrap = true, Color = Theme.TextControl } );
				}
			}

			settings.Layout.AddStretchCell();

			if ( lightmap is not null && !baking )
			{
				AddSection( settings.Layout, "Baked output", "texture" );
				settings.Layout.Add( new LightmapOutputView( scene, lightmap ) );
			}
		}

		if ( lightmap?.BakeProgress is { } progress )
			progressBar?.SetContentHash( progress.GetHashCode(), 0 );
	}

	static void AddSection( Layout layout, string title, string icon )
	{
		var heading = layout.AddRow();
		heading.Spacing = 6;
		heading.Add( new IconLabel( icon ) { Foreground = Theme.TextLight, IconSize = 16 } );
		heading.Add( new Label.Header( title ) );
		heading.Add( new Separator( 1 ) { Color = Theme.Text.WithAlpha( 0.12f ) }, 1 );
	}

	static void AddSettings( Layout layout, SceneEditorSession session, BakedLightmap lightmap )
	{
		var properties = lightmap.GetSerialized();
		properties.OnPropertyChanged = _ => session.HasUnsavedChanges = true;

		void Choice( string name, string title )
		{
			var property = properties.GetProperty( name );
			layout.Add( new Label( title ) { ToolTip = property.Description, Color = Theme.TextControl } );
			layout.Add( ControlWidget.Create( property ) ).FixedHeight = 26;
		}

		void Toggle( string name, string title )
		{
			var property = properties.GetProperty( name );
			var check = layout.Add( new Checkbox( title ) { ToolTip = property.Description } );
			check.Bind( nameof( Checkbox.Value ) ).From( () => property.As.Bool, value => property.As.Bool = value );
		}

		AddSection( layout, "Sampling", "grid_on" );
		Choice( nameof( lightmap.Size ), "Atlas resolution" );
		Choice( nameof( lightmap.Quality ), "Bake quality" );
		Toggle( nameof( lightmap.Denoise ), "Denoise" );

		layout.AddSpacingCell( 6 );
		AddSection( layout, "Light transport", "wb_sunny" );
		var transport = new ControlSheet { VerticalSpacing = 3 };
		transport.AddRow( properties.GetProperty( nameof( lightmap.Bounces ) ) );
		transport.AddRow( properties.GetProperty( nameof( lightmap.AmbientOcclusion ) ) );
		layout.Add( transport );
		Toggle( nameof( lightmap.RealtimeSunShadows ), "Realtime sun shadows" );

		layout.AddSpacingCell( 6 );
		AddSection( layout, "Volumes", "view_in_ar" );
		var volumes = new ControlSheet();
		volumes.AddRow( properties.GetProperty( nameof( lightmap.ProbeSpacing ) ) );
		layout.Add( volumes );
		Toggle( nameof( lightmap.BakeFog ), "Volumetric fog" );
	}

	void AddActions( SceneEditorSession session, BakedLightmap lightmap, bool baking )
	{
		if ( !baking && error is not null )
		{
			toolbar.Layout.Add( new WarningBox( error ) { BackgroundColor = Theme.Red, Icon = "error" } );
		}
		else if ( !baking && session.Scene.Source is null )
		{
			toolbar.Layout.Add( new Label( "Save the scene to bake a lightmap." ) { WordWrap = true } );
		}

		const int actionHeight = 32;
		var actions = toolbar.Layout.AddRow();
		actions.Spacing = 4;

		if ( baking )
		{
			var bar = actions.Add( new Widget( toolbar ) { FixedHeight = 24 }, 1 );
			progressBar = bar;
			bar.OnPaintOverride = () =>
			{
				if ( lightmap.BakeProgress is not { } progress )
					return false;

				var rect = bar.LocalRect;
				var track = new Rect( 0, rect.Height - 4, rect.Width, 4 );
				Paint.SetPen( Theme.Text );
				Paint.SetDefaultFont();
				var percent = Paint.DrawText( rect, $"{progress.Fraction:P0}", TextFlag.RightTop );
				var stage = Paint.GetElidedText( progress.Stage, Math.Max( 0, rect.Width - percent.Width - 8 ) );
				Paint.DrawText( rect, stage, TextFlag.LeftTop );
				Paint.ClearPen();
				Paint.SetBrush( Theme.ControlBackground );
				Paint.DrawRect( track, 2 );
				Paint.SetBrush( Theme.Primary );
				track.Width *= progress.Fraction;
				Paint.DrawRect( track, 2 );
				return true;
			};

			actions.Add( new Button( "Cancel", "stop" ) { FixedHeight = actionHeight, Clicked = lightmap.Stop } );
			return;
		}

		actions.Add( new Button( "Bake lightmap", "wb_twilight" )
		{
			FixedHeight = actionHeight,
			Tint = Color.Lerp( Theme.SidebarBackground, Theme.Primary, 0.55f ),
			Enabled = !session.IsMounted && session.Scene.Source is not null,
			Clicked = Bake
		}, 1 );

		actions.Add( new Button( "", "delete_outline" )
		{
			FixedSize = actionHeight,
			ToolTip = "Clear baked lightmap",
			Tint = Theme.SurfaceLightBackground,
			Enabled = EditableLightmap?.Textures is not null,
			Clicked = Clear
		} );

		if ( lightmap.Textures is not null )
		{
			var preview = toolbar.Layout.Add( new Checkbox( "Show lightmaps" ) { ToolTip = "Preview baked lighting in the scene view" } );
			preview.Bind( nameof( Checkbox.Value ) ).From( () => lightmap.PreviewEnabled, value => lightmap.PreviewEnabled = value );
		}
	}

	static void Clear()
	{
		if ( EditableLightmap is { Textures: not null } lightmap )
			lightmap.Clear();
	}

	async void Bake()
	{
		if ( EditableLightmap is not { } lightmap || ActiveSession.Scene.Source is null )
			return;

		error = null;
		try
		{
			await lightmap.Bake();
		}
		catch ( OperationCanceledException )
		{
			// Cancelling from the dock or closing the scene ends this bake.
		}
		catch ( Exception e )
		{
			errorSource = lightmap;
			error = e.Message;
			Log.Warning( e );
		}
	}
}
