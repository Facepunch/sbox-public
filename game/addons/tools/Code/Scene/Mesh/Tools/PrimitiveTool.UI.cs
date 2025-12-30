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
			_tool.Editor = EditorTypeLibrary.Create<PrimitiveEditor>( type, [_tool] );

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
				var list = new PrimitiveListView( this );
				list.FixedWidth = 200;
				list.SetItems( GetBuilderTypes() );
				list.SelectItem( list.Items.FirstOrDefault( x => (x as TypeDescription).TargetType == tool.Editor?.GetType() ) );
				list.ItemSelected = ( e ) => OnEditorSelected( (e as TypeDescription).TargetType );
				list.BuildLayout();

				var group = AddGroup( "Primitive Type" );
				group.Add( list );
			}

			{
				Layout.AddSpacingCell( 4 );

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

				Layout.AddSpacingCell( 4 );
			}

			{
				_settingsWidget = new ToolSidebarWidget( this );
				_settingsWidget.Layout.Margin = 0;
				Layout.Add( _settingsWidget );

				BuildSettings();
			}

			Layout.AddStretchCell();
		}

		[Shortcut( "mesh.primitive-tool-create", "enter", ShortcutType.Application )]
		void Create() => _tool.Create();

		[Shortcut( "mesh.primitive-tool-cancel", "ESC", ShortcutType.Application )]
		void Cancel() => _tool.Cancel();

		static IEnumerable<TypeDescription> GetBuilderTypes()
		{
			return EditorTypeLibrary.GetTypes<PrimitiveEditor>()
				.Where( x => !x.IsAbstract )
				.OrderBy( x => x.Name );
		}
	}
}

public class PrimitiveListView : ListView
{
	public PrimitiveListView( Widget parent ) : base( parent )
	{
		ItemSpacing = 0;
		ItemSize = 32;
		Margin = 0;

		HorizontalScrollbarMode = ScrollbarMode.Off;
		VerticalScrollbarMode = ScrollbarMode.Off;
	}

	protected override void DoLayout()
	{
		base.DoLayout();

		BuildLayout();
	}

	public void BuildLayout()
	{
		var rect = CanvasRect;
		var itemSize = ItemSize;
		var itemSpacing = ItemSpacing;
		var itemsPerRow = 1;
		var itemCount = Items.Count();

		if ( itemSize.x > 0 ) itemsPerRow = ((rect.Width + itemSpacing.x) / (itemSize.x + itemSpacing.x)).FloorToInt();
		itemsPerRow = Math.Max( 1, itemsPerRow );

		var rowCount = MathX.CeilToInt( itemCount / (float)itemsPerRow );
		FixedHeight = rowCount * (itemSize.y + itemSpacing.y) + Margin.EdgeSize.y;
	}

	protected override string GetTooltip( object obj )
	{
		var builder = obj as TypeDescription;
		var displayInfo = DisplayInfo.ForType( builder.TargetType );
		return displayInfo.Name;
	}

	protected override void PaintItem( VirtualWidget item )
	{
		if ( item.Selected )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.Blue );
			Paint.DrawRect( item.Rect, 4 );
		}

		var builder = item.Object as TypeDescription;
		var displayInfo = DisplayInfo.ForType( builder.TargetType );

		Paint.SetPen( item.Selected || item.Hovered ? Color.White : Color.Gray );
		Paint.DrawIcon( item.Rect, displayInfo.Icon ?? "square", HeaderBarStyle.IconSize );
	}
}
