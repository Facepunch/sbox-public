
namespace Editor.ShaderGraph;

public class BlackboardView : Widget
{
	private Button.Primary _addButton;
	private Button.Danger _deleteButton;
	private BlackboardParameterList _parameterListView;

	private readonly MainWindow _window;
	private readonly UndoStack _undoStack;

	private BlackboardParameter _selectedParameter;

	/// <summary>
	/// Called when a blackboard parameter is selected.
	/// </summary>
	public Action<BlackboardParameter> OnParameterSelected { get; set; }

	public BlackboardView( Widget parent, MainWindow window ) : base( parent )
	{
		Layout = Layout.Row();
		FocusMode = FocusMode.TabOrClickOrWheel;

		_window = window;
		_undoStack = window.UndoStack;

		var canvas = new Widget( null );
		canvas.Layout = Layout.Row();
		canvas.Layout.Spacing = 4;

		var leftColumn = canvas.Layout.AddColumn( 1, false );
		leftColumn.Spacing = 4;

		var leftColumnTopLayout = leftColumn.AddRow( 1, false );
		leftColumnTopLayout.Spacing = 4;

		leftColumnTopLayout.AddStretchCell();

		_deleteButton = new Button.Danger( "Delete", "delete" );
		_deleteButton.Enabled = false;
		_deleteButton.ToolTip = $"Delete selected blackboard parameter";
		_deleteButton.Clicked += () =>
		{
			throw new NotImplementedException();
		};

		leftColumnTopLayout.Add( _deleteButton );

		_addButton = new Button.Primary( "Add", "new_label" );
		_addButton.Enabled = true;
		_addButton.ToolTip = $"Add new blackboard parameter";
		_addButton.Clicked += () =>
		{
			throw new NotImplementedException();
		};

		leftColumnTopLayout.Add( _addButton );

		_parameterListView = leftColumn.Add( new BlackboardParameterList( null ), 1 );
		_parameterListView.ItemClicked = ( item ) =>
		{
			throw new NotImplementedException();
		};
		_parameterListView.ItemSelected = ( item ) =>
		{
			_selectedParameter = item as BlackboardParameter;

			OnParameterSelected?.Invoke( _selectedParameter );
		};
		_parameterListView.ItemDrag = ( a ) =>
		{
			throw new NotImplementedException();
		};

		Layout.Add( canvas );
	}
}

class BlackboardParameterList : ListView
{
	public BlackboardParameterList( Widget widget ) : base( widget )
	{
		Margin = 6;
		ItemSpacing = 4;
		ItemSize = new Vector2( 0, 24 );
		AcceptDrops = false;
	}

	protected override void PaintItem( VirtualWidget item )
	{
		var variable = item.Object as BlackboardParameter;
		var rect = item.Rect;
		var textColor = Theme.TextControl;
		var itemColor = Theme.ControlBackground;
		var typeColor = Color.White;

		//if ( ShaderGraphTheme.BlackboardConfigs.TryGetValue( variable.GetType(), out var blackboardConfig ) )
		//{
		//	typeColor = blackboardConfig.Color;
		//}

		if ( item.Hovered )
		{
			textColor = Color.White;
			itemColor = Theme.Primary.Lighten( 0.1f ).Desaturate( 0.3f ).WithAlpha( 0.4f * 0.6f );
		}
		if ( item.Selected )
		{
			textColor = Theme.TextControl;
			itemColor = Theme.Primary;
		}

		Paint.ClearPen();
		Paint.SetBrush( itemColor );
		Paint.DrawRect( rect, Theme.ControlRadius );

		var iconRect = rect.Shrink( 4, 0, 0, 0 );
		Paint.SetPen( typeColor );
		Paint.DrawIcon( iconRect, "circle", 12f, TextFlag.LeftCenter );
		rect.Left += 24f;

		Paint.SetPen( textColor.WithAlpha( 0.7f ) );
		Paint.SetBrush( textColor.WithAlpha( 0.7f ) );

		var textRect = Paint.DrawText( rect.Shrink( 4, 0, 0, 0 ), $"{variable.Name}", TextFlag.LeftCenter );
		var typeRect = Paint.DrawText( rect.Shrink( 0, 0, 4, 0 ), $"{DisplayInfo.ForType( variable.GetType() ).Name}", TextFlag.RightCenter );

		//Paint.SetPen( Color.Gray.WithAlpha( 0.25f ) );
		//Paint.SetBrush( Color.Gray.WithAlpha( 0.25f ) );
		//Paint.DrawRect( typeRect.Grow( 2 ), Theme.ControlRadius );
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( LocalRect, 4 );

		base.OnPaint();
	}
}
