using Editor.Inspectors;

namespace Editor.ProjectSettingPages;

[Title( "Systems" )]
internal sealed class SystemsPage : ProjectSettingsWindow.Category
{
	SegmentedControl _modeControl;
	Layout _layout;
	ControlSheet _sheet;
	TypeDescription _currentType;
	bool _wantsEditScene;
	Scene _scene;
	Dictionary<(TypeDescription systemType, string propertyName), object> _scenePendingChanges = new();

	internal override bool ShowTitle => false;

	public override IEnumerable<TreeChildNode> GetTreeChildren()
	{
		// Get all systems with properties
		var types = TypeLibrary.GetTypes<GameObjectSystem>()
					.Where( t => t.Properties.Any( p => p.HasAttribute<PropertyAttribute>() ) )
					.OrderBy( x => x.Order )
					.ThenBy( x => x.Title );

		foreach ( var systemType in types )
		{
			yield return new TreeChildNode(
				systemType.Title ?? systemType.Name,
				systemType.Icon ?? "device_hub",
				category => ((SystemsPage)category)._currentType = systemType
			);
		}
	}

	public override void OnInit( Project project )
	{
		base.OnInit( project );

		_modeControl = new SegmentedControl();
		_modeControl.AddOption( "Global", "settings" );

		if ( SceneEditorSession.Active.Scene is not PrefabScene )
		{
			_modeControl.AddOption( "Current Scene", "map" );
		}

		_modeControl.SelectedIndex = 0;
		_modeControl.OnSelectedChanged += str => SwitchMode( _modeControl.SelectedIndex == 1 );
		BodyLayout.Add( _modeControl );

		// Create content layout that will be cleared on mode switch
		_layout = BodyLayout.AddColumn();

		RebuildContent();
	}

	void SwitchMode( bool sceneMode )
	{
		_wantsEditScene = sceneMode;
		_scene = sceneMode ? SceneEditorSession.Active?.Scene : null;

		// Clear pending changes when switching modes
		_scenePendingChanges.Clear();

		RebuildContent();
	}

	void RebuildContent()
	{
		_layout.Clear( true );

		// Get all systems with properties
		var types = TypeLibrary.GetTypes<GameObjectSystem>()
					.Where( t => t.Properties.Any( p => p.HasAttribute<PropertyAttribute>() ) )
					.OrderBy( x => x.Order )
					.ThenBy( x => x.Title )
					.ToList();

		// Show warning if no systems found
		if ( !types.Any() )
		{
			_modeControl.Visible = false;

			var warning = new WarningBox( "No configurable systems found. Add [Property] attributes to your GameObjectSystem properties to configure them here." );
			warning.Icon = "info";
			_layout.Add( warning );
			return;
		}

		_modeControl.Visible = true;

		if ( _currentType != null )
		{
			_sheet = new ControlSheet();
			_layout.Add( _sheet );
			RebuildSheet( _currentType );
		}
		else
		{
			// Show all systems with headers
			foreach ( var systemType in types )
			{
				var header = new Label.Header( systemType.Title ?? systemType.Name );
				_layout.Add( header );

				var sheet = new ControlSheet();
				_layout.Add( sheet );
				RebuildSheet( systemType, sheet );
			}
		}
	}

	void RebuildSheet( TypeDescription systemType, ControlSheet targetSheet = null )
	{
		targetSheet ??= _sheet;
		targetSheet.Clear( true );

		var serializedObject = new SystemSerializedObject( systemType, _wantsEditScene ? _scene : null, _wantsEditScene ? _scenePendingChanges : null );

		var properties = systemType.Properties
			.Where( p => p.HasAttribute<PropertyAttribute>() );

		foreach ( var prop in properties )
		{
			var serializedProp = new SystemPropertyWrapper( serializedObject, prop );
			serializedProp.OnChanged += StateHasChanged;

			targetSheet.AddRow( serializedProp );
		}
	}

	public override void OnSave()
	{
		if ( !_wantsEditScene )
		{
			EditorUtility.SaveProjectSettings( ProjectSettings.Systems, "Systems.config" );
		}
		else
		{
			//
			// Apply pending changes to the scene's GameObjectSystems
			//
			foreach ( var kvp in _scenePendingChanges )
			{
				var (systemType, propertyName) = kvp.Key;
				var value = kvp.Value;

				var prop = systemType.Properties.FirstOrDefault( p => p.Name == propertyName );
				if ( prop == null ) continue;

				var system = EditorUtility.GetGameObjectSystem( _scene, systemType );
				if ( system != null )
				{
					prop.SetValue( system, value );
				}
			}

			// Clear pending changes after applying
			_scenePendingChanges.Clear();

			//
			// save the scene
			//
			SceneEditorSession.Active?.Save( false );
		}

		base.OnSave();
	}
}

/// <summary>
/// Lets us wrap a GameObjectSystem type as a SerializedObject without instantiating it
/// </summary>
file class SystemSerializedObject : SerializedObject
{
	private readonly TypeDescription _systemType;
	private readonly Scene _scene;
	private readonly Dictionary<(TypeDescription, string), object> _pendingChanges;

	public SystemSerializedObject( TypeDescription systemType, Scene scene = null, Dictionary<(TypeDescription, string), object> pendingChanges = null )
	{
		_systemType = systemType;
		_scene = scene;
		_pendingChanges = pendingChanges;
	}

	public TypeDescription SystemType => _systemType;
	public Scene Scene => _scene;

	public override IEnumerable<object> Targets
	{
		get { yield return this; }
	}

	public object GetValue( string propertyName )
	{
		var prop = _systemType.Properties.FirstOrDefault( p => p.Name == propertyName );
		if ( prop == null ) return null;

		object value;
		if ( _scene.IsValid() )
		{
			// Check if there's a pending change first
			var key = (_systemType, propertyName);
			if ( _pendingChanges.TryGetValue( key, out value ) )
			{
				return value;
			}

			// Get from GameObjectSystem
			var system = EditorUtility.GetGameObjectSystem( _scene, _systemType );
			value = system != null ? prop.GetValue( system ) : null;
		}
		else
		{
			value = ProjectSettings.Systems.GetPropertyValue( _systemType, prop );
		}

		// Handle null value types by creating a default instance
		if ( value == null && prop.PropertyType.IsValueType )
		{
			value = Activator.CreateInstance( prop.PropertyType );
		}

		return value;
	}

	public void SetValue( string propertyName, object value )
	{
		var prop = _systemType.Properties.FirstOrDefault( p => p.Name == propertyName );
		if ( prop == null ) return;

		if ( _scene.IsValid() )
		{
			_pendingChanges[(_systemType, propertyName)] = value;
		}
		else
		{
			ProjectSettings.Systems.SetPropertyValue( _systemType, prop, value );
		}
	}
}

/// <summary>
/// Wraps a property of a GameObjectSystem type, but we don't always use the instance
/// </summary>
file class SystemPropertyWrapper : SerializedProperty
{
	private readonly SystemSerializedObject _parent;
	private readonly PropertyDescription _property;
	private readonly DisplayInfo _displayInfo;

	public SystemPropertyWrapper( SystemSerializedObject parent, PropertyDescription property )
	{
		_parent = parent;
		_property = property;
		_displayInfo = property.GetDisplayInfo();
	}

	public override SerializedObject Parent => _parent;
	public override string Name => _property.Name;
	public override string DisplayName => _displayInfo.Name;
	public override string Description => _displayInfo.Description;
	public override string GroupName => _displayInfo.Group;
	public override int Order => _displayInfo.Order;
	public override Type PropertyType => _property.PropertyType;
	public override string SourceFile => _property.SourceFile;
	public override int SourceLine => _property.SourceLine;

	public override object GetDefault()
	{
		if ( !_parent.Scene.IsValid() )
		{
			return base.GetDefault();
		}

		if ( ProjectSettings.Systems.TryGetPropertyValue( _parent.SystemType, _property, out var value ) )
		{
			return value;
		}

		return base.GetDefault();
	}

	public override bool TryGetAsObject( out SerializedObject obj )
	{
		var value = _parent.GetValue( _property.Name );

		if ( value == null )
		{
			obj = null;
			return false;
		}

		var serializedValue = value.GetSerialized();
		serializedValue.OnPropertyChanged += ( prop ) =>
		{
			var modifiedValue = serializedValue.Targets.FirstOrDefault();
			if ( modifiedValue != null )
			{
				_parent.SetValue( _property.Name, modifiedValue );
				NoteChanged();
			}
		};

		obj = serializedValue;
		return true;
	}

	public override T GetValue<T>( T defaultValue = default )
	{
		var value = _parent.GetValue( _property.Name );
		return ValueToType<T>( value );
	}

	public override void SetValue<T>( T value )
	{
		NotePreChange();
		_parent.SetValue( _property.Name, value );
		NoteChanged();
	}

	public override IEnumerable<Attribute> GetAttributes()
	{
		return _property.Attributes;
	}
}
