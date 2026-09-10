using Editor.AssetPickers;

/// <summary>
/// Shows a control for editing embedded resources. This is basically a dropdown, when you click on it
/// the popup editor will show up with the properties of the embedded resource - with a toolbar.
/// </summary>
public class EmbeddedResourceControlWidget : StickyPopupControlWidget
{
	TypeDescription _typeDescription;
	AssetType _assetType;
	public override bool SupportsMultiEdit => true;
	public override bool IsControlButton => !IsControlDisabled;

	Resource Resource => SerializedProperty.GetValue<Resource>( null );

	/// <summary>
	/// A list of supported types (if any) that we can switch this embedded resource to.
	/// </summary>
	List<TypeDescription> SupportedTypes
	{
		get
		{
			return EditorTypeLibrary.GetTypes( SerializedProperty.PropertyType )
				.Where( x => x.TargetType.IsPublic && !x.IsAbstract && x.TargetType.IsSubclassOf( typeof( GameResource ) ) )
				.Where( x => x.TargetType != Resource?.GetType() )
				.OrderBy( x => x.Name )
				.ToList();
		}
	}

	public EmbeddedResourceControlWidget( SerializedProperty property ) : base( property )
	{

		//
		// If we're opening the popup editor, we need to ensure that the resource is valid
		//

		var resource = Resource;
		if ( resource is null && !SerializedProperty.PropertyType.IsAbstract )
		{
			resource = EditorTypeLibrary.Create<Resource>( SerializedProperty.PropertyType.Name );
			SerializedProperty.SetValue( resource );
		}

		SerializedProperty.OnChanged = OnChanged;

		Reset( resource, SerializedProperty );
		InitializeInlineEditor();
	}

	/// <summary>
	/// Resets the resource's embed info to default
	/// </summary>
	/// <param name="resource"></param>
	/// <param name="property"></param>
	private void Reset( Resource resource, SerializedProperty property )
	{
		//
		// This can be null if the resource's type is abstract
		//
		if ( resource.IsValid() )
		{
			var type = Game.TypeLibrary.GetType( resource.GetType() );
			string typeName = null;

			if ( !property.PropertyType.Equals( type.TargetType ) )
			{
				typeName = type.Name;
			}

			resource.EmbeddedResource = new()
			{
				ResourceCompiler = "embed",
				TypeName = typeName
			};
		}

		_typeDescription = Game.TypeLibrary.GetType( resource.IsValid() ? resource.GetType() : property.PropertyType );
		_assetType = AssetType.FromType( _typeDescription?.TargetType ?? property.PropertyType );
	}

	void OnChanged( SerializedProperty prop )
	{
		// Rebuild popup if external changes were made
		RebuildEditor();
	}



	protected override void OnPaint()
	{
		if ( IsInlineEditor )
		{
			PaintInlineEditorHeader();
			return;
		}
		var asset = Resource.IsValid() ? AssetSystem.FindByPath( Resource.ResourcePath ) : null;
		var pickerName = DisplayInfo.ForType( SerializedProperty.PropertyType ).Name;
		if ( _assetType is not null ) pickerName = _assetType.FriendlyName;

		var text = $"Embedded {pickerName}";

		if ( SerializedProperty.IsMultipleDifferentValues )
		{
			text = $"Multiple Values";
		}

		if ( _assetType?.Icon64 is Pixmap icon )
		{
			Theme.DrawDropdown( LocalRect, text, "", _popup.IsValid(), IsControlDisabled || !Resource.IsValid() );
			Paint.Draw( new Rect( 2, Height - 4 ), icon, Paint.HasMouseOver ? 1f : 0.7f );
		}
		else
		{
			Theme.DrawDropdown( LocalRect, text, "edit_note", _popup.IsValid(), IsControlDisabled );
		}
	}

	void Reset()
	{
		var target = SerializedProperty.GetValue<Resource>();
		if ( !target.IsValid() ) return;

		PropertyStartEdit();

		//
		// Clean slate
		//
		var newResource = EditorTypeLibrary.Create<Resource>( SerializedProperty.PropertyType.Name );
		Reset( newResource, SerializedProperty );

		SerializedProperty.SetValue( newResource );

		SignalValuesChanged();
		PropertyFinishEdit();
	}



	void SetType( TypeDescription type )
	{
		PropertyStartEdit();

		var resource = EditorTypeLibrary.Create<Resource>( type.Name );
		Reset( resource, SerializedProperty );
		SerializedProperty.SetValue( resource );

		RebuildEditor();
		SignalValuesChanged();

		PropertyFinishEdit();
	}

	void OpenTypePopup()
	{
		var menu = new ContextMenu( IsInlineEditor ? this : _popup );
		var resourceType = Resource?.GetType() ?? SerializedProperty.PropertyType;

		foreach ( var type in SupportedTypes )
		{
			var isCurrent = type.TargetType.Equals( resourceType );
			var item = menu.AddOption( type.Title, action: () => SetType( type ) );
			item.Icon = type.Icon ?? "category";
			item.Enabled = !isCurrent;
		}

		menu.OpenAt( _toolbar.ScreenRect.BottomLeft );
	}

	protected override void BuildEditor( Widget target, bool isPopup )
	{
		if ( !target.IsValid() ) return;

		PrepareEditor( target, isPopup );

		if ( SupportedTypes.Any() )
			_toolbar.AddOption( "Type", "type_specimen", action: OpenTypePopup ).Enabled = !target.ReadOnly;

		var so = Resource?.GetSerialized();
		if ( so.IsValid() )
		{
			if ( _assetType?.HasEditor ?? false )
				_toolbar.AddOption( "Editor", "dvr", action: OpenInEditor ).Enabled = !target.ReadOnly;

			_toolbar.AddOption( "Save As", "drive_file_move", action: ConvertToFile ).Enabled = !target.ReadOnly;
			_toolbar.AddOption( "Load", "swap_horiz", action: LoadFromFile ).Enabled = !target.ReadOnly;
			AddClipboardOptions( target, RebuildEditor );
			_toolbar.AddSeparator();
			_toolbar.AddOption( "Clear", "delete", action: Reset ).Enabled = !target.ReadOnly;
		}

		FinishEditor( target, isPopup, so );
	}



	private async void ConvertToFile()
	{
		var fd = new FileDialog( null );
		fd.Title = "Convert embedded resource to file..";
		fd.DefaultSuffix = _assetType.FileExtension;
		fd.Directory = Project.Current.GetAssetsPath();
		fd.SelectFile( $"resource.{_assetType.FileExtension}" );
		fd.SetFindFile();
		fd.SetModeSave();
		fd.SetNameFilter( $"Resource  (*.{_assetType.FileExtension})" );

		if ( !fd.Execute() )
			return;

		var asset = AssetSystem.CreateResource( _assetType.FileExtension, fd.SelectedFile );
		await asset.CompileIfNeededAsync();

		if ( asset.TryLoadResource<GameResource>( out var gr ) )
		{
			gr.CopyFrom( SerializedProperty.GetValue<GameResource>() );

			// Cull any notion of an embedded resource, as it's not one anymore
			gr.EmbeddedResource = null;

			asset.SaveToDisk( gr );
		}

		// Link to the GameResource
		SerializedProperty.SetValue( gr );
		MainAssetBrowser.Instance?.Local.UpdateAssetList();

		_popup?.Destroy();
		_popup = null;
	}

	void LoadFromFile()
	{
		var picker = new ResourcePicker( Parent, _assetType );
		var query = $"t:{_assetType.FileExtension}";

		picker.OnAssetPicked += x =>
		{
			var asset = x.FirstOrDefault();
			if ( asset?.TryLoadResource<GameResource>( out var gr ) ?? false )
			{
				CreateEmbeddedFromFile( SerializedProperty, gr );
			}

			RebuildEditor();
		};

		picker.Title = $"Select {SerializedProperty.DisplayName}";
		picker.Show();
	}

	public static EmbeddedResourceControlWidget CreateWidget( SerializedProperty target )
	{
		var customType = EditorTypeLibrary.GetTypes( typeof( EmbeddedResourceControlWidget ) )
			.FirstOrDefault( x =>
			{
				if ( !x.TargetType.IsAssignableTo( typeof( EmbeddedResourceControlWidget ) ) )
					return false;
				var attr = x.GetAttribute<CustomEmbeddedEditorAttribute>();
				if ( attr is null ) return false;
				return attr.TargetType == target.PropertyType;
			} );

		if ( customType is null )
			return new EmbeddedResourceControlWidget( target );

		return EditorTypeLibrary.Create<EmbeddedResourceControlWidget>( customType.FullName, [target] );
	}

	/// <summary>
	/// Creates a new embedded resource from the given file resource.
	/// </summary>
	/// <param name="target"></param>
	/// <param name="source"></param>
	/// <returns></returns>
	public static GameResource CreateEmbeddedFromFile( SerializedProperty target, GameResource source )
	{
		if ( target.PropertyType.IsAbstract ) return null;

		var data = source.Serialize();

		//
		// Create a fresh resource when switching to embedded
		//
		var resource = EditorTypeLibrary.Create<GameResource>( target.PropertyType.Name );
		resource.CopyFrom( source );

		resource.EmbeddedResource = new()
		{
			ResourceCompiler = "embed",
			Data = data
		};

		target.SetValue( resource );

		return resource;
	}

	void OpenInEditor()
	{
		IAssetEditor.OpenInEditor( AssetSystem.CreateEmbeddedAsset( SerializedProperty ), out var editor );

		_popup?.Destroy();
		_popup = null;
	}
}

file class ResourcePicker : SimplePicker
{
	public ResourcePicker( Widget parent, AssetType assetType ) : base( parent, assetType, new() )
	{
		Title = $"Select {assetType.FriendlyName}";
	}
}
