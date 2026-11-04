using Sandbox.UI;

namespace Editor.MeshEditor;

partial class PrimitiveTool
{
	public override Widget CreateToolSidebar()
	{
		return new PrimitiveToolWidget( this );
	}

	public class PrimitiveToolWidget : ToolSidebarWidget
	{
		readonly PrimitiveTool _tool;
		readonly Widget _settingsWidget;
		readonly Button _createButton;
		readonly Button _cancelButton;
		readonly IconButton _iconLabel;
		readonly Label.Header _titleLabel;

		void OnEditorSelected( Type type )
		{
			_tool.SelectEditor( type );

			UpdateTitle();
			BuildSettings();
		}

		void BuildSettings()
		{
			using var x = SuspendUpdates.For( this );

			var widget = _tool.Editor?.CreateWidget();
			if ( widget is null )
			{
				_settingsWidget.Hide();
			}
			else
			{
				_settingsWidget.Layout.Clear( true );
				_settingsWidget.Layout.Add( widget );
				_settingsWidget.Show();
			}
		}

		[EditorEvent.Frame]
		public void Frame()
		{
			UpdateButtons();
		}

		[EditorEvent.Hotload]
		public void Hotload()
		{
			UpdateTitle();
			BuildSettings();
		}

		void UpdateButtons()
		{
			_createButton?.Enabled = _tool.Editor is not null && _tool.Editor.CanBuild;
			_cancelButton?.Enabled = _tool.Editor is not null && _tool.Editor.InProgress;
		}

		void UpdateTitle()
		{
			var type = EditorTypeLibrary.GetType( _tool.Editor?.GetType() );
			if ( type is null ) return;

			_iconLabel?.Icon = type.Icon;
			_titleLabel?.Text = $"Create {type.Title}";
		}

		public PrimitiveToolWidget( PrimitiveTool tool ) : base()
		{
			_tool = tool;

			{
				var titleRow = Layout.AddRow();
				titleRow.Margin = new Margin( 0, 0, 0, 8 );
				titleRow.Spacing = 4;

				_iconLabel = titleRow.Add( new IconButton( null ), 0 );
				_iconLabel.IconSize = 18;
				_iconLabel.Background = Color.Transparent;
				_iconLabel.Foreground = Theme.Blue;
				_titleLabel = titleRow.Add( new Label.Header( null ), 1 );

				UpdateTitle();
			}

			{
				var group = AddGroup( "Creation Mode" );
				var picker = group.Add( new PrimitiveTypePicker( this, "Primitive" ) );
				picker.SetItems( GetBuilderTypes() );
				picker.SelectType( tool.Editor?.GetType() );
				picker.TypeSelected = type => OnEditorSelected( type.TargetType );
			}

			{
				_settingsWidget = new ToolSidebarWidget( this );
				_settingsWidget.Layout.Margin = 0;
				Layout.Add( _settingsWidget );

				BuildSettings();
			}

			{
				Layout.AddSpacingCell( 8 );

				var row = Layout.AddRow();
				row.Spacing = 4;

				_createButton = row.Add( new Button( "Create", "done" )
				{
					Clicked = Create,
					ToolTip = "[Create " + EditorShortcuts.GetKeys( "mesh.primitive-tool-create" ) + "]",
				} );

				_cancelButton = row.Add( new Button( "Cancel", "close" )
				{
					Clicked = Cancel,
					ToolTip = "[Cancel " + EditorShortcuts.GetKeys( "mesh.primitive-tool-cancel" ) + "]"
				} );

				UpdateButtons();
			}

			Layout.AddStretchCell();
		}

		[Shortcut( "mesh.primitive-tool-create", "enter", typeof( SceneViewWidget ) )]
		void Create() => _tool.Create();

		[Shortcut( "mesh.primitive-tool-cancel", "ESC", typeof( SceneViewWidget ) )]
		void Cancel() => _tool.Cancel();

		static IEnumerable<TypeDescription> GetBuilderTypes()
		{
			return EditorTypeLibrary.GetTypes<PrimitiveEditor>()
				.Where( x => !x.IsAbstract );
		}
	}
}

internal sealed class PrimitiveTypePicker : Button
{
	readonly string _typeName;
	IReadOnlyList<TypeDescription> _items = [];

	public Action<TypeDescription> TypeSelected { get; set; }

	public PrimitiveTypePicker( Widget parent, string typeName ) : base( parent )
	{
		_typeName = typeName;
		FixedHeight = Theme.RowHeight;
		HorizontalSizeMode = SizeMode.Flexible;
		Clicked = OpenPicker;
	}

	public void SetItems( IEnumerable<TypeDescription> items )
	{
		_items = items.OrderBy( x => x.Title ).ToArray();
	}

	public void SelectType( Type type )
	{
		var selected = _items.FirstOrDefault( x => x.TargetType == type );
		Text = selected is null ? $"Select {_typeName}..." : $"{selected.Title}  ▾";
		Icon = selected?.Icon ?? "category";
		ToolTip = selected?.Description;
	}

	void OpenPicker()
	{
		var popup = new AdvancedDropdownPopup( this );
		popup.Dropdown.RootTitle = $"{_typeName} Types";
		popup.Dropdown.SearchPlaceholderText = $"Find {_typeName} Types";
		popup.Dropdown.OnBuildItems = root =>
		{
			foreach ( var type in _items )
			{
				root.Add( new AdvancedDropdownItem( type.Title, type.Icon, type )
				{
					Description = type.Description,
					Tooltip = type.Description
				} );
			}
		};

		popup.OnSelect = value =>
		{
			if ( value is not TypeDescription type )
				return;

			SelectType( type.TargetType );
			TypeSelected?.Invoke( type );
		};
		popup.Dropdown.Rebuild();
		popup.OpenAt( ScreenRect.BottomLeft, animateOffset: new Vector2( 0, -4 ) );
	}
}
