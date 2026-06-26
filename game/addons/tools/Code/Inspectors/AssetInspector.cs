using Editor.Assets;
using System.IO;
using System.Text.Json.Serialization;

namespace Editor.Inspectors;

[Inspector( typeof( Asset ) )]
public class AssetInspector : InspectorWidget
{
	const float HeaderHeight = 64 + 8 + 8;

	public Asset Asset { get; set; }
	public Asset[] Assets { get; set; }
	public Action OnSave { get; set; }
	public Action OnReset { get; set; }

	ToolBar ToolBar;


	Splitter Splitter;
	AssetPreview AssetPreview;
	Widget Preview;
	Option SaveOption;
	ScrollArea Scroller;
	Layout ContentLayout;

	Option _openCode;

	public IResourceEditor ResourceEditor { get; private set; }

	public AssetInspector( SerializedObject so ) : base( so )
	{
		Asset = so.Targets.Cast<Asset>().First();
		Assets = so.Targets.Cast<Asset>().ToArray();

		SetContext( "asset", Asset );
		SetContext( "path", Asset.AbsolutePath );
		SetContext( "filename", System.IO.Path.GetFileName( Asset.AbsolutePath ) );
		SetContext( "folder", System.IO.Path.GetDirectoryName( Asset.AbsolutePath ) );

		Layout = Layout.Column();
		Layout.Margin = new( 0, HeaderHeight + 8, 0, 0 );
		SetSizeMode( SizeMode.CanGrow, SizeMode.CanGrow );

		Splitter = new Splitter( this );
		Splitter.IsVertical = true;

		Layout.Add( Splitter, 1 );

		AssetPreview = AssetPreview.CreateForAsset( Asset );

		{
			var mainWidget = new Widget( this );
			mainWidget.Layout = Layout.Column();

			ToolBar = new ToolBar( this );

			if ( !so.IsMultipleTargets )
			{
				ToolBar.AddOption( new Option( "Open In Editor", "edit", () => Asset.OpenInEditor() ) ).Enabled = !Asset.IsProcedural;
				ToolBar.AddOption( new Option( "Open Asset Location", "folder", () => EditorUtility.OpenFileFolder( Asset.AbsolutePath ) ) );
				ToolBar.AddSeparator();
			}

			_openCode = ToolBar.AddOption( new Option( "Jump to code", "code" ) );
			ToolBar.AddSeparator();

			if ( Asset.HasSourceFile && Asset.CanRecompile )
			{
				ToolBar.AddOption( new Option( "Full Recompile", "restart_alt", () => Asset.Compile( true ) ) );
			}

			{
				SaveOption = new Option( "Save", "save", () => OnSave?.Invoke() );
				SaveOption.ShortcutName = "editor.save";
				SaveOption.Enabled = false;
				ToolBar.AddOption( SaveOption );
			}

			Scroller = new ScrollArea( this );
			Scroller.Canvas = new Widget( this );
			Scroller.Canvas.SetSizeMode( SizeMode.Default, SizeMode.CanGrow );
			Scroller.Canvas.Layout = Layout.Column();
			mainWidget.Layout.Add( Scroller );

			if ( !so.IsMultipleTargets && !Asset.IsProcedural )
			{
				CreatePublishUI( Asset, Scroller.Canvas );
			}

			RebuildUI();

			Scroller.Canvas.Layout.AddStretchCell();

			Splitter.AddWidget( mainWidget );
		}

		var bottomTabs = new TabWidget( Splitter );
		bottomTabs.MinimumSize = 200;

		if ( AssetPreview is not null )
			Preview = new AssetPreviewWidget( AssetPreview );
		else
		{
			var label = new Label( "No preview available" );
			label.Alignment = TextFlag.Center;

			Preview = label;
		}

		bottomTabs.AddPage( "Preview", "visibility", Preview );

		Splitter.AddWidget( bottomTabs );

		{
			var count = Asset.GetReferences( false ).Count();
			bottomTabs.AddPage( "References", "attach_file", new AssetReferencesWidget( Asset, bottomTabs ), count );
		}

		{
			var count = Asset.GetDependants( false ).Count();
			bottomTabs.AddPage( "Used by", "link", new AssetDependancyWidget( Asset, bottomTabs ), count );
		}

		bottomTabs.StateCookie = "AssetInspector.Tab";

		Splitter.SetStretch( 0, 9 );
		Splitter.SetStretch( 1, 1 );

		Enabled = true;
	}

	[EditorEvent.Hotload]
	void RebuildUI()
	{
		if ( SerializedObject.IsMultipleTargets )
		{
			CreateMultiContentUI( SerializedObject );
		}
		else
		{
			CreateContentUI( Asset, AssetPreview );
		}
	}

	private void CreatePublishUI( Asset target, Widget canvas )
	{
		canvas.Layout.Add( new AssetPublishWidget( canvas, target ) );
	}

	protected override bool OnInspectorClose( object newObj = null )
	{
		var generic = !Asset.TryLoadResource<GameResource>( out var gameResource ) || !gameResource.HasUnsavedChanges;
		if ( generic && !Asset.HasUnsavedChanges )
			return true;
		if ( generic && OnSave is null && OnReset is null )
			return true;

		var popup = new PopupDialogWidget( "💾" );
		popup.FixedWidth = 462;
		popup.WindowTitle = $"Unsaved Changes";
		popup.MessageLabel.Text = $"Do you want to save the changes you made to \n{Asset.Name}?";

		popup.ButtonLayout.Spacing = 4;
		popup.ButtonLayout.AddStretchCell();
		popup.ButtonLayout.Add( new Button( "Save" )
		{
			Clicked = () =>
			{
				if ( generic )
					OnSave?.Invoke();
				else
					SaveAsset( Asset, gameResource );
				popup.Destroy();
				// After we are done with the popup, retrigger the inspector change since we previously blocked it.
				EditorUtility.InspectorObject = newObj;
			}
		} );

		popup.ButtonLayout.Add( new Button( "Don't Save" )
		{
			Clicked = () =>
			{
				if ( generic )
					OnReset?.Invoke();
				else
					ResetAsset( Asset, gameResource );
				popup.Destroy();
				EditorUtility.InspectorObject = newObj;
			}
		} );
		popup.ButtonLayout.Add( new Button( "Cancel" ) { Clicked = () => { popup.Destroy(); } } );

		popup.SetModal( true, true );
		popup.Hide();
		popup.Show();

		return false;
	}

	private void CreateContentLayout()
	{
		if ( !(ContentLayout?.IsValid ?? false) )
		{
			ContentLayout = Layout.Column();
			Scroller.Canvas.Layout.Add( ContentLayout );
			Scroller.Canvas.Layout.AddStretchCell();
		}
		else
		{
			ContentLayout?.Clear( true );
		}
	}

	Action saveAction = null;

	private void CreateContentUI( Asset target, AssetPreview preview )
	{
		CreateContentLayout();

		saveAction = null;

		var assetType = $"asset:{target.AssetType.FileExtension.ToLower()}";
		var isGameResource = target.TryLoadResource<GameResource>( out var gameResource );

		if ( isGameResource )
		{
			var custom = InspectorWidget.Create( gameResource.GetSerialized(), ignore: typeof( AssetInspector ) );
			if ( custom.IsValid() )
			{
				ContentLayout.Add( custom );

				saveAction = () => SaveAsset( target, gameResource );

				if ( !AssetSystem.IsCloudInstalled( target.Package ) && target.HasSourceFile )
				{
					SaveOption.Bind( "Enabled" ).ReadOnly().From( () => gameResource.HasUnsavedChanges, null );
				}

				if ( custom is IAssetInspector customInspector )
				{
					customInspector.SetAsset( target );
					customInspector.SetAssetPreview( preview );
					customInspector.SetInspector( this );
				}

				return; // Don't need the rest
			}
		}

		var editor = CanEditAttribute.CreateEditorFor( assetType );
		_openCode.Enabled = false;

		if ( editor is IAssetInspector inspector )
		{
			inspector.SetAsset( target );
			inspector.SetAssetPreview( preview );
			inspector.SetInspector( this );
		}

		if ( editor.IsValid() )
		{
			ContentLayout.Add( editor );
		}

		bool isReadOnly = AssetSystem.IsCloudInstalled( target.Package );
		isReadOnly = isReadOnly || !target.HasSourceFile;

		//
		// If true then this is a JSON type object
		//
		if ( isGameResource )
		{
			saveAction = () => SaveAsset( target, gameResource );

			if ( !editor.IsValid() )
			{
				var resourceType = gameResource.GetType();
				var typeInfo = EditorTypeLibrary.GetType( resourceType );
				if ( typeInfo.SourceFile is not null )
				{
					bool isPackage = resourceType.Assembly.IsPackage();
					var filename = System.IO.Path.GetFileName( typeInfo.SourceFile );
					_openCode.Enabled = CodeEditor.CanOpenFile( typeInfo.SourceFile );
					_openCode.Triggered = () => CodeEditor.OpenFile( typeInfo.SourceFile, typeInfo.SourceLine + 1 /* skip attrib */ );
				}

				var t = typeof( BaseResourceEditor<> );
				var implementations = EditorTypeLibrary.GetTypes( t.MakeGenericType( resourceType ) );

				{
					// try to find an impl. for an inherited type
					var baseType = resourceType;
					while ( !implementations.Any() )
					{
						baseType = baseType.BaseType;
						if ( baseType is null || !typeof( Resource ).IsAssignableFrom( baseType ) )
							break;
						implementations = EditorTypeLibrary.GetTypes( t.MakeGenericType( baseType ) );
					}
				}

				if ( implementations.Any() )
				{
					foreach ( var implementation in implementations )
					{
						var i = implementation.Create<Widget>( null );
						ContentLayout.Add( i );

						if ( i is not IResourceEditor resourceEditor )
						{
							continue;
						}

						ResourceEditor = resourceEditor;

						try
						{
							resourceEditor.Initialize( target, gameResource, this );
							resourceEditor.Changed += x =>
							{
								SaveOption.Enabled = true;
							};
						}
						catch ( System.Exception e )
						{
							Log.Warning( e, "Error calling Initialize" );
							continue;
						}

						saveAction += resourceEditor.SavedToDisk;
					}

					ContentLayout.AddStretchCell();
				}
				else
				{
					var sheet = new ControlSheet();
					sheet.IncludePropertyNames = true;

					var so = gameResource.GetSerialized();

					so.OnPropertyChanged += x =>
					{
						if ( isReadOnly ) return;

						// TODO: this inspector should have its own undo system for edits to this
						gameResource.StateHasChanged();
					};

					if ( isReadOnly )
					{
						//todo
						//so = so.AsReadOnly();
					}

					sheet.AddObject( so, filter: FilterProperties );
					ContentLayout.Add( sheet );
					ContentLayout.AddStretchCell();
				}
			}
		}

		if ( !isReadOnly && saveAction is not null && gameResource is not null )
		{
			SaveOption.Bind( "Enabled" ).ReadOnly().From( () => gameResource.HasUnsavedChanges, null );
		}

		if ( saveAction != null )
		{
			SaveOption.Triggered = saveAction;
		}

		ContentLayout.AddStretchCell();
		Scroller?.Canvas?.Parent?.Update();
	}

	bool FilterProperties( SerializedProperty prop )
	{
		if ( prop.IsMethod ) return true;

		// only show stuff that'll actually be serialised
		if ( !prop.IsProperty ) return false;
		if ( prop.HasAttribute<JsonIgnoreAttribute>() ) return false;

		return prop.IsPublic || prop.HasAttribute<JsonIncludeAttribute>();
	}

	[Shortcut( "editor.save", "CTRL+S", ShortcutType.Window )]
	protected void Save()
	{
		if ( !SaveOption.IsValid() )
			return;

		if ( !SaveOption.Enabled )
			return;

		SaveOption.Triggered?.Invoke();
	}

	/// <summary>
	/// Builds the editing UI when multiple assets are selected. Game resources are edited together via a
	/// <see cref="MultiSerializedObject"/>; raw assets whose settings live in metadata (e.g. vsnd, jpg) are
	/// handled by their <see cref="IAssetInspector"/> if every selection shares the same type.
	/// Doesn't handle BaseResourceEditor.
	/// </summary>
	private void CreateMultiContentUI( SerializedObject target )
	{
		CreateContentLayout();

		if ( TryCreateMultiAssetInspector() )
			return;

		MultiSerializedObject mso = new MultiSerializedObject();

		Action saveAction = () => { };

		foreach ( var entry in target.Targets )
		{
			if ( entry is not Asset asset ) continue;
			if ( asset.TryLoadResource<GameResource>( out var assetObject ) )
			{
				mso.Add( assetObject.GetSerialized() );
				saveAction += () => SaveAsset( asset, assetObject );
			}
		}

		mso.Rebuild();

		mso.OnPropertyChanged += x => SaveOption.Enabled = true;

		var sheet = new ControlSheet();
		sheet.IncludePropertyNames = true;

		sheet.AddObject( mso );
		ContentLayout.Add( sheet );
		ContentLayout.AddStretchCell();

		SaveOption.Triggered = saveAction;

		Scroller?.Canvas?.Parent?.Update();
	}

	/// <summary>
	/// If every selected asset is the same type and that type has a custom <see cref="IAssetInspector"/>,
	/// create it once and hand it the whole selection via <see cref="IAssetInspector.SetAssets"/>. Returns
	/// false (leaving the caller to fall back to the generic path) for mixed selections, game resources, or
	/// types without a custom inspector.
	/// </summary>
	private bool TryCreateMultiAssetInspector()
	{
		if ( Assets is null || Assets.Length < 2 )
			return false;

		// Only when every selected asset is the exact same type.
		if ( Assets.Select( x => x.AssetType ).Distinct().Count() != 1 )
			return false;

		// Game resources have their own MultiSerializedObject path below.
		if ( Assets.Any( x => x.TryLoadResource<GameResource>( out _ ) ) )
			return false;

		var assetType = $"asset:{Assets[0].AssetType.FileExtension.ToLower()}";
		var editor = CanEditAttribute.CreateEditorFor( assetType );

		// Only use the type editor if it genuinely supports multi-select - otherwise editing it would
		// silently affect just one asset while the header says "N Assets", which is confusing.
		if ( editor is not IAssetInspector inspector )
		{
			editor?.Destroy();
			return false;
		}

		inspector.SetInspector( this );

		if ( !inspector.SetAssets( Assets ) )
		{
			editor?.Destroy();
			return false;
		}

		if ( editor.IsValid() )
			ContentLayout.Add( editor );

		ContentLayout.AddStretchCell();
		Scroller?.Canvas?.Parent?.Update();

		return true;
	}

	public void SaveAsset( Asset target, Sandbox.GameResource assetObject )
	{
		if ( target.SaveToDisk( assetObject ) )
		{
			if ( IsValid )
			{
				SaveOption.Enabled = false;
			}
		}
	}

	private void ResetAsset( Asset target, Sandbox.GameResource assetObject )
	{
		// Reload from disk
		assetObject.LoadFromJson( File.ReadAllText( target.GetSourceFile( true ) ) );
		// Save to clear changed flags
		SaveAsset( target, assetObject );
	}

	protected override void OnMouseRightClick( MouseEvent e )
	{
		if ( e.LocalPosition.y <= HeaderHeight )
		{
			var m = new ContextMenu();
			m.AddOption( "Copy Asset Name", "content_copy", () => EditorUtility.Clipboard.Copy( Asset.Name ) );
			m.AddOption( "Copy Path", "file_copy", () => EditorUtility.Clipboard.Copy( Asset.RelativePath ) );
			m.OpenAt( e.ScreenPosition );
		}

		base.OnMouseRightClick( e );
	}

	Pixmap _cachedResizedIcon;

	protected override void OnPaint()
	{
		//Paint.SetBrushAndPen( Theme.ControlBackground.WithAlpha( 0.2f ) );
		//Paint.ClearPen();
		//Paint.DrawRect( new Rect( new Vector2( 0, 0 ), new Vector2( Width, HeaderHeight ) ) );

		Paint.SetBrushAndPen( Theme.ControlBackground.WithAlpha( 0.4f ) );
		Paint.ClearPen();
		Paint.DrawRect( new Rect( new Vector2( 0, HeaderHeight - 26 ), new Vector2( Width, 26 ) ) );

		Paint.RenderMode = RenderMode.Screen;
		var pos = new Vector2( 64 + 16 + 4, 8 );
		Paint.SetPen( Theme.Text );
		Paint.SetHeadingFont( 13, 450 );

		var title = Asset.Name;
		var subtitle = Asset.RelativePath;
		Pixmap icon = Asset.GetAssetThumb( true );
		if ( Assets.Length > 1 )
		{
			var typeGroup = Assets.GroupBy( x => x.AssetType );
			title = $"{Assets.Length} Assets";
			subtitle = string.Join( ", ", typeGroup.Select( x => $"{x.Count()} x {x.Key.FriendlyName}" ) );
			icon = typeGroup.First().Key.Icon64;
		}

		var r = Paint.DrawText( pos, title );
		pos.y = r.Bottom;

		Paint.SetPen( Theme.Primary.WithAlpha( 0.9f ) );
		Paint.SetDefaultFont();
		Paint.DrawText( pos, subtitle );

		if ( icon != null )
		{
			if ( icon.Size != 64 )
			{
				_cachedResizedIcon ??= icon.Resize( 64, 64 );
			}

			Paint.RenderMode = RenderMode.Normal;
			Paint.Draw( new Rect( 8, 64 ), _cachedResizedIcon ?? icon );
		}
	}

	protected override void DoLayout()
	{
		base.DoLayout();

		ToolBar.SetIconSize( 23 );
		ToolBar.Position = new Vector2( 64 + 16, HeaderHeight - 26 );
		ToolBar.MaximumSize = new Vector2( Width, 26 );
		ToolBar.Size = new Vector2( Width, 26 );
	}

	/// <summary>
	/// Make the Save Option enabled state track the Asset's HasUnsavedChanges property.
	/// </summary>
	public void BindSaveToUnsavedChanges()
	{
		SaveOption.Bind( "Enabled" ).ReadOnly().From( () => { return Asset.HasUnsavedChanges; }, null );
	}

	/// <summary>
	/// Implemented by a widget that edits a particular asset type (matched via <see cref="CanEditAttribute"/>,
	/// e.g. <c>[CanEdit( "asset:vsnd" )]</c>). The hosting <see cref="AssetInspector"/> creates the widget and
	/// feeds it the current selection through these calls.
	/// </summary>
	public interface IAssetInspector
	{
		/// <summary>
		/// Called when a single asset is selected. Bind your editing UI to it.
		/// </summary>
		public void SetAsset( Asset asset );

		/// <summary>
		/// Called instead of <see cref="SetAsset"/> when multiple assets of the same type are selected.
		/// Return true if this inspector applied the whole selection (i.e. it supports multi-select editing).
		/// The default returns false, meaning "I only handle a single asset" - the caller then shows the
		/// header-only multi view rather than silently editing just the first asset.
		/// </summary>
		public bool SetAssets( Asset[] assets ) => false;

		/// <summary>
		/// Gives the inspector the shared <see cref="AssetPreview"/> for the selected asset, so it can drive
		/// or react to the preview. Optional - the default does nothing.
		/// </summary>
		public void SetAssetPreview( AssetPreview preview ) { }

		/// <summary>
		/// Gives the inspector a reference back to the hosting <see cref="AssetInspector"/>, e.g. to toggle its
		/// Save option or read its context. Optional - the default does nothing.
		/// </summary>
		public void SetInspector( AssetInspector inspector ) { }
	}

}

file class AssetDependancyWidget : Widget
{
	AssetList Dependants;

	public AssetDependancyWidget( Asset asset, Widget parent ) : base( parent )
	{
		Dependants = new AssetList( this );
		Dependants.OnHighlight = a => EditorUtility.InspectorObject = (a as AssetEntry)?.Asset;
		Dependants.SingleColumnMode = true;

		Layout = Layout.Column();
		Layout.Add( Dependants );

		Dependants.SetItems( asset.GetDependants( false ).OrderBy( x => x.AssetType.FileExtension ).ThenBy( x => x.Name ).Select( x => new AssetEntry( x ) ) );
	}
}


file class AssetReferencesWidget : Widget
{
	AssetList References;

	public AssetReferencesWidget( Asset asset, Widget parent ) : base( parent )
	{
		References = new AssetList( this );
		References.OnHighlight = a => EditorUtility.InspectorObject = (a as AssetEntry)?.Asset;
		References.SingleColumnMode = true;

		Layout = Layout.Column();

		Layout.Add( References );

		var references = asset.GetReferences( false ).Where( x => !string.IsNullOrWhiteSpace( x.AbsolutePath ) ).OrderBy( x => x.AssetType.FileExtension ).ThenBy( x => x.Name ).Select( x => new AssetEntry( x ) ).ToList();

		// Add unrecognized references to the bottom of the list
		var unknown = asset.GetUnrecognizedReferencePaths().Select( x => new AssetEntry( new FileInfo( x ), null ) );
		references.AddRange( unknown );

		References.SetItems( references );
	}
}
