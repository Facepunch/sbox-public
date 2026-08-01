using Sandbox.Clutter;

namespace Editor;

/// <summary>
/// Resource editor for ClutterDefinition using feature tabs.
/// </summary>
public class ClutterDefinitionEditor : BaseResourceEditor<ClutterDefinition>
{
	private ClutterDefinition _resource;
	private SerializedObject _serialized;
	private Layout _scattererProperties;
	private Type _currentScattererType;
	private Layout _entryProperties;
	private SerializedProperty _entriesProperty;

	protected override void Initialize( Asset asset, ClutterDefinition resource )
	{
		_resource = resource;

		Layout = Layout.Column();
		Layout.Spacing = 0;
		Layout.Margin = 0;

		_serialized = resource.GetSerialized();
		_serialized.OnPropertyChanged += ( prop ) =>
		{
			switch ( prop.Name )
			{
				case nameof( ClutterDefinition.Entries ):
				case nameof( ClutterDefinition.TileSizeEnum ):
				case nameof( ClutterDefinition.TileRadius ):
					NoteChanged( prop );
					break;

				case nameof( ClutterDefinition.Scatterer ):
					var newType = _resource.Scatterer.Value?.GetType();
					if ( newType != _currentScattererType )
					{
						NoteChanged( prop );
						RebuildScattererProperties();
					}
					break;
			}
		};

		var tabs = new TabWidget( this );
		tabs.VerticalSizeMode = SizeMode.CanGrow;
		tabs.AddPage( "Entries", "grass", CreateEntriesTab( _serialized ) );
		tabs.AddPage( "Scatterer", "scatter_plot", CreateScattererTab( _serialized ) );
		tabs.AddPage( "Streaming", "grid_view", CreateStreamingTab( _serialized ) );

		Layout.Add( tabs, 1 );
	}

	private Widget CreateEntriesTab( SerializedObject serialized )
	{
		var container = new Widget( null );
		container.Layout = Layout.Column();
		container.VerticalSizeMode = SizeMode.CanGrow;

		var sheet = new ControlSheet();
		_entriesProperty = serialized.GetProperty( nameof( ClutterDefinition.Entries ) );
		var gridWidget = sheet.AddControl<ClutterEntriesGridWidget>( _entriesProperty );
		gridWidget.OnEntrySelected = RebuildEntryProperties;

		container.Layout.Add( sheet, 1 );
		_entryProperties = container.Layout.AddColumn();

		return container;
	}

	private void RebuildEntryProperties( ClutterEntry entry )
	{
		_entryProperties?.Clear( true );

		if ( entry is null )
			return;

		var so = entry.GetSerialized();
		so.OnPropertyChanged += ( prop ) =>
		{
			if ( _entriesProperty is null )
				return;

			_entriesProperty.SetValue( _entriesProperty.GetValue<List<ClutterEntry>>() );
			_entriesProperty.Parent?.NoteChanged( _entriesProperty );
		};

		var sheet = new ControlSheet();
		sheet.AddRow( so.GetProperty( nameof( ClutterEntry.Weight ) ) );
		sheet.AddRow( so.GetProperty( nameof( ClutterEntry.LocalScale ) ) );
		sheet.AddRow( so.GetProperty( nameof( ClutterEntry.CastShadows ) ) );
		sheet.AddRow( so.GetProperty( nameof( ClutterEntry.EnablePhysics ) ) );
		_entryProperties.Add( sheet );
	}

	private Widget CreateScattererTab( SerializedObject serialized )
	{
		var container = new Widget( null );
		container.Layout = Layout.Column();
		container.VerticalSizeMode = SizeMode.CanGrow;

		var sheet = new ControlSheet();
		sheet.AddRow( serialized.GetProperty( nameof( ClutterDefinition.Scatterer ) ) );

		container.Layout.Add( sheet );
		_scattererProperties = container.Layout.AddColumn();
		container.Layout.AddStretchCell();

		RebuildScattererProperties();

		return container;
	}

	private void RebuildScattererProperties()
	{
		_currentScattererType = _resource.Scatterer.Value?.GetType();
		_scattererProperties?.Clear( true );

		if ( !_resource.Scatterer.HasValue )
			return;

		var so = _resource.Scatterer.Value.GetSerialized();
		if ( so is null )
			return;

		so.OnPropertyChanged += NoteChanged;
	}

	private Widget CreateStreamingTab( SerializedObject serialized )
	{
		var container = new Widget( null );
		container.Layout = Layout.Column();
		container.VerticalSizeMode = SizeMode.CanGrow;

		var sheet = new ControlSheet();
		sheet.AddRow( serialized.GetProperty( nameof( ClutterDefinition.TileSizeEnum ) ) );
		sheet.AddRow( serialized.GetProperty( nameof( ClutterDefinition.TileRadius ) ) );

		container.Layout.Add( sheet, 1 );
		return container;
	}
}
