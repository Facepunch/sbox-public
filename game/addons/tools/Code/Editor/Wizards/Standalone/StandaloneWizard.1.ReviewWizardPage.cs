using Sandbox.UI;
using System.IO;

namespace Editor.Wizards;

partial class StandaloneWizard
{
	class ReviewWizardPage : StandaloneWizardPage
	{
		public override string PageTitle => "Export";
		public override string PageSubtitle => "Please follow the steps below to export your game.";

		private Group CreateGroup( string icon, string title )
		{
			var group = new Group( this )
			{
				Title = title,
				Icon = icon
			};

			group.Layout = Layout.Column();
			group.Layout.AddSpacingCell( 32 );
			group.Layout.Margin = new Margin( 0, 0, 0, 4 );

			return group;
		}

		private record Preset( string Name, ExportConfig Config );

		private string PresetsCookie = $"Standalone.Presets";

		private List<Preset> GetPresets()
		{
			if ( string.IsNullOrEmpty( PresetsCookie ) )
				return new();

			return ProjectCookie.Get<List<Preset>>( PresetsCookie, [] );
		}

		private void AddPreset( Preset preset )
		{
			if ( string.IsNullOrEmpty( PresetsCookie ) )
				return;

			var presets = GetPresets();
			presets.Add( preset );

			ProjectCookie.Set<List<Preset>>( PresetsCookie, presets );
		}

		private void RemovePreset( Preset preset )
		{
			if ( string.IsNullOrEmpty( PresetsCookie ) )
				return;

			// The stored presets are fresh copies, so find this one by name
			var presets = GetPresets();
			var index = presets.FindIndex( x => x.Name == preset.Name );
			if ( index < 0 ) return;

			presets.RemoveAt( index );
			ProjectCookie.Set<List<Preset>>( PresetsCookie, presets );
		}

		private void CreateLayout()
		{
			BodyLayout?.Clear( true );
			BodyLayout.Margin = new Sandbox.UI.Margin( 0, 0 );

			BodyLayout.AddStretchCell();

			{
				BodyLayout.Spacing = 2;

				{
					var row = new Widget();
					var rowLayout = BodyLayout.AddLayout( Layout.Row(), 1 );
					rowLayout.AddStretchCell();

					var button = rowLayout.Add( new Button( $"Export Presets", "expand_more" ) );
					button.Clicked = () =>
					{
						var menu = new Menu();

						foreach ( var preset in GetPresets() )
						{
							var option = menu.AddWidget( new PresetMenuOption( preset.Name ) );

							option.OnLoad = () =>
							{
								// Every wizard page shares the one config, so copy the preset into it
								// rather than replacing this page's reference - otherwise the export never sees it
								PublishConfig.ExecutableName = preset.Config.ExecutableName;
								PublishConfig.TargetDir = preset.Config.TargetDir;
								PublishConfig.TargetIcon = preset.Config.TargetIcon;
								PublishConfig.StartupImage = preset.Config.StartupImage;
								PublishConfig.AppId = preset.Config.AppId;
								menu.Close();
								CreateLayout();
							};

							option.OnRemove = () =>
							{
								RemovePreset( preset );
								option.Destroy();
								menu.AdjustSize();
							};
						}

						menu.AddSeparator();

						menu.AddOption( "New Preset...", "save", () =>
						{
							var popup = new PopupWidget( this );
							popup.Layout = Layout.Column();
							popup.Layout.Margin = 16;
							popup.Layout.Spacing = 8;

							popup.Layout.Add( new Label( "What would you like to call the preset?" ) );

							var button = new Button.Primary( "Confirm" );

							LineEdit entry = null;

							button.MouseClick += () =>
							{
								AddPreset( new Preset( entry.Value, PublishConfig ) );
								CreateLayout();
								popup.Close();
							};

							entry = new LineEdit( popup ) { Text = "My Preset" };
							entry.SelectAll();
							entry.ReturnPressed += () => button.MouseClick?.Invoke();

							popup.Layout.Add( entry );
							entry.Focus();

							var bottomBar = popup.Layout.AddRow();
							bottomBar.AddStretchCell();
							bottomBar.Add( button );

							popup.OpenAt( menu.ScreenPosition );
						} );

						menu.OpenAt( button.ScreenRect.BottomLeft );
					};
				}

				BodyLayout.AddSpacingCell( 8 );

				BodyLayout.AddSeparator( true );

				BodyLayout.AddSpacingCell( 8 );

				BodyLayout.Add( new InformationBox( """
					Game exporting is in technical preview.
					- Some things might not work
					- Please do not distribute your exported game yet
					- Please do not put your game on Steam yet
					""" ) );

				BodyLayout.AddSpacingCell( 2 );

				if ( !Project.Config.IsStandaloneOnly )
				{
					BodyLayout.Add( new WarningBox( $"""
						Your app is not marked as <b>Standalone Only</b>. Some advanced settings will be restricted. You can change this in the <b>Project Setup</b> page.						
						""" ) );
				}

				BodyLayout.AddSpacingCell( 8 );

				{
					var basicLayout = BodyLayout.AddLayout( Layout.Row(), 1 );
					basicLayout.Spacing = 4;

					{
						var group = basicLayout.Add( new Group( this )
						{
							Title = "Executable",
							Icon = "settings"
						} );

						group.Layout = Layout.Column();
						group.Layout.Margin = new Margin( 12, 40, 12, 16 );
						group.Layout.Spacing = 4;

						group.Layout.AddSpacingCell( 4 );

						{
							var row = group.Layout.Add( Layout.Row() );
							row.Spacing = 4;
							row.Margin = new Margin( 4, 4, 4, 4 );

							//
							// Icon
							//
							var iconInput = row.Add( new BrandingInput( group, ".ico", new( 48, 48 ) ), 0 );
							iconInput.IconSize = 16;
							iconInput.Value = PublishConfig.TargetIcon;
							iconInput.OnSelected = () => { PublishConfig.TargetIcon = iconInput.Value; };

							//
							// 
							//
							var col = row.Add( Layout.Column() );
							col.Alignment = TextFlag.Center;
							col.Spacing = 4;

							//
							// Executable name and path
							//
							var sheet = new ControlSheet();
							sheet.Margin = new Margin( 0 );

							sheet.AddProperty( PublishConfig, x => x.ExecutableName );
							sheet.AddProperty( PublishConfig, x => x.TargetDir );

							col.Add( sheet );
						}

						group.Layout.AddStretchCell();
						basicLayout.Add( group, 2 );
					}
				}


				{
					var basicLayout = BodyLayout.AddLayout( Layout.Row(), 1 );
					basicLayout.Spacing = 4;

					{
						var group = basicLayout.Add( new Group( this )
						{
							Title = "Branding / Icons",
							Icon = "image",
						} );

						group.Layout = Layout.Row();
						group.Layout.Margin = new Margin( 16, 40, 16, 16 );
						group.Layout.Spacing = 4;

						{
							var w = new Widget( group );
							w.Layout = Layout.Column();
							w.Layout.Spacing = 4;
							w.Layout.Add( new Label( "Startup Image" ) );

							var startupInput = w.Layout.Add( new BrandingInput( w, ".vtex", new( 512, 512 ) ), 0 );
							startupInput.Value = PublishConfig.StartupImage;
							startupInput.OnSelected = () => { PublishConfig.StartupImage = startupInput.Value; };

							w.Layout.AddStretchCell();
							group.Layout.Add( w );
						}
					}

					{
						var group = basicLayout.Add( CreateGroup( "cloud", "Storefronts" ) );
						var sheet = new ControlSheet();

						sheet.AddProperty( PublishConfig, x => x.AppId );

						group.Layout.Add( sheet );
						group.Layout.AddStretchCell();
					}
				}
			}

			BodyLayout.AddStretchCell();

			Visible = true;
			GetPackage();

		}

		public override async Task OpenAsync()
		{
			CreateLayout();
			await Task.CompletedTask;
		}

		public override void ChildValuesChanged( Widget source )
		{
			GetPackage();
		}

		Package Package;
		Task PackageTask;

		void GetPackage()
		{
			PackageTask = UpdatePackage();
		}

		async Task UpdatePackage()
		{
			// complete in order
			if ( PackageTask != null )
				await PackageTask;

			Package = await Package.FetchAsync( Project.Config.FullIdent, true, useCache: false );

			if ( !IsValid )
				return;
		}

		public override bool CanProceed()
		{
			if ( (PackageTask?.IsCompleted ?? true) == false ) return false;
			if ( Package != null && Package.TypeName != Project.Config.Type ) return false;

			if ( Project.Config.Ident == null ) return false;
			if ( !EditorTypeLibrary.CheckValidationAttributes( Project.Config ) ) return false;

			return true;
		}
	}
}

file class ControlSheet : Editor.ControlSheet
{
	public ControlSheet() : base()
	{
		HorizontalSpacing = 8;
		Margin = new Margin( 8 );
	}
}

/// <summary>
/// A preset in the Export Presets menu: click it to load it, or the cross on the right to delete it.
/// </summary>
file class PresetMenuOption : Widget
{
	public string Text { get; set; }
	public Action OnLoad { get; set; }
	public Action OnRemove { get; set; }

	bool hoveringRemove;

	public PresetMenuOption( string text ) : base( null )
	{
		Text = text;
		MinimumWidth = 196;
		FixedHeight = 26;
		MouseTracking = true;
		Cursor = CursorShape.Finger;
	}

	Rect RemoveRect => LocalRect.Shrink( 0, 0, 6, 0 ).Align( 20, TextFlag.RightCenter );

	protected override void OnMouseMove( MouseEvent e )
	{
		base.OnMouseMove( e );

		var over = RemoveRect.IsInside( e.LocalPosition );
		if ( over == hoveringRemove ) return;

		hoveringRemove = over;
		Update();
	}

	protected override void OnMouseLeave()
	{
		base.OnMouseLeave();
		hoveringRemove = false;
		Update();
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		e.Accepted = true; // Handle it here, so removing a preset doesn't close the menu

		if ( RemoveRect.IsInside( e.LocalPosition ) ) OnRemove?.Invoke();
		else OnLoad?.Invoke();
	}

	// Mimic Option styling, like MenuCheckableOption
	protected override void OnPaint()
	{
		var hovered = Paint.HasMouseOver;

		Paint.ClearPen();
		Paint.SetBrush( hovered && !hoveringRemove ? Theme.Primary : Color.Parse( "#444748" ) ?? default );
		Paint.DrawRect( LocalRect );

		Paint.SetPen( hovered ? Color.White : Color.White.WithAlphaMultiplied( 0.7f ) );
		Paint.SetDefaultFont( 8 );
		Paint.DrawText( LocalRect.Shrink( 12, 0, 32, 0 ), Text, TextFlag.LeftCenter | TextFlag.SingleLine );

		Paint.SetPen( hoveringRemove ? Theme.Red : Color.White.WithAlphaMultiplied( hovered ? 0.7f : 0.3f ) );
		Paint.DrawIcon( RemoveRect, "close", 14 );
	}
}

file class BrandingInput : Widget
{
	private Vector2 _targetSize;
	private string _extension;

	public string Value { get; set; }
	public Action OnSelected { get; set; }

	public float IconSize { get; set; } = 32;

	public BrandingInput( Widget parent, string extension, Vector2 targetSize ) : base( parent )
	{
		_targetSize = targetSize;
		_extension = extension;

		Cursor = CursorShape.Finger;
		FixedSize = CalculateSize();
	}

	private Vector2 CalculateSize()
	{
		var aspect = _targetSize.x / _targetSize.y;

		var maxWidth = 256f;
		if ( _targetSize.x > maxWidth )
			return new Vector2( maxWidth, (maxWidth * aspect) );

		return _targetSize;
	}


	protected override Vector2 SizeHint()
	{
		return CalculateSize();
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		var fd = new FileDialog( null );
		fd.Title = "Select File...";
		fd.Directory = Path.GetDirectoryName( Project.Current.GetAssetsPath() );
		fd.DefaultSuffix = _extension;

		fd.SetFindFile();
		fd.SetModeOpen();
		fd.SetNameFilter( $"Image (*{_extension})" );

		if ( !fd.Execute() )
			return;

		Value = fd.SelectedFile;
		OnSelected?.Invoke();
	}

	protected override void OnPaint()
	{
		//
		// Background
		//
		{
			Paint.ClearPen();
			Paint.ClearBrush();

			if ( Paint.HasMouseOver )
				Paint.SetBrush( Theme.ControlBackground.Lighten( 0.5f ) );
			else
				Paint.SetBrush( Theme.ControlBackground );

			var r = ContentRect;
			r.Width = SizeHint().x;
			Paint.DrawRect( r, 4f );
		}

		// 
		// Text
		//
		{
			Paint.ClearPen();
			Paint.SetPen( Theme.TextControl );

			var spacing = 16;
			var r = ContentRect;
			r.Width = SizeHint().x;

			if ( Width >= 128 )
				r.Top -= 24;

			bool hasValue = !string.IsNullOrEmpty( Value );

			void AddText( string text, float size, Color color )
			{
				Paint.SetDefaultFont( size );
				Paint.SetPen( color );
				r.Top += Paint.DrawText( r, $"{text}" ).Height + spacing;
			}

			r.Top += Paint.DrawIcon( r, "image", IconSize ).Height + spacing;

			if ( Width < 128 )
				return;

			if ( hasValue )
			{
				AddText( $"{Path.GetFileName( Value )}", 8f, Theme.TextControl );
			}
			else
			{
				AddText( $"Recommended: {_targetSize.x}x{_targetSize.y}", 7f, Theme.TextControl );
				AddText( $"Click to browse", 7f, Theme.TextControl.WithAlpha( 0.5f ) );
			}
		}
	}
}
