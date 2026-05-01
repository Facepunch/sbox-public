using Sandbox.UI;
namespace Editor.DooEditor;

/// <summary>
/// Main window for editing Doo scripts.
/// </summary>
public class DooEditorWidget : PopupWidget
{
	public static DooEditorWidget Open( SerializedObject so, string title = "Doo Editor" )
	{
		var window = new DooEditorWidget( EditorWindow, so, title );
		window.WindowFlags = WindowFlags.Tool | WindowFlags.Customized | WindowFlags.CloseButton;
		window.OpenAtCursor( true, new Vector2( -600, -250 ) );
		window.WindowTitle = $"{so.ParentProperty?.Name} - Doo Editor";
		return window;
	}

	public SerializedObject SerializedObject { get; private set; }
	public Doo Target => SerializedObject.Targets.FirstOrDefault() as Doo;
	public DooToolbox Toolbox { get; private set; }
	public DooInspector Inspector { get; private set; }
	public BlockTree BlockTree { get; private set; }

	public Doo.ArgumentHintAttribute[] ArgumentHints { get; private set; }

	public DooEditorWidget( Widget parent, SerializedObject so, string title ) : base( parent )
	{
		SerializedObject = so;

		DeleteOnClose = true;
		Size = new Vector2( 350, 600 );
		Layout = Layout.Row();
		AcceptDrops = true;

		MinimumWidth = 450;
		MinimumHeight = 400;

		ArgumentHints = so.ParentProperty?.GetAttributes<Doo.ArgumentHintAttribute>().ToArray();

		SetWindowIcon( "rebase_edit" );

		RebuildUI();
	}

	Layout _rightColumn;

	void RebuildUI()
	{
		Toolbox = new DooToolbox( this );
		Layout.Add( Toolbox );
		Layout.Margin = 4;

		BlockTree = new BlockTree( Target )
		{
			Margin = new Margin( 0, 16, 8, 16 ),
			ContentMargins = 2,
			MinimumWidth = 400
		};
		Inspector = new DooInspector( this );
		Inspector.FixedWidth = 400;

		_rightColumn = Layout.AddRow();

		var contentColumn = _rightColumn.AddColumn();

		var args = CreateArgumentHeader();
		if ( args != null )
		{
			contentColumn.Add( args );
		}

		contentColumn.Add( BlockTree, 1 );

		_rightColumn.Add( Inspector );
	}

	private Layout CreateArgumentHeader()
	{
		if ( ArgumentHints?.Length == 0 ) return null;

		var col = Layout.Row();
		col.Margin = new Margin( 8, 8, 8, 0 );
		col.Spacing = 4;
		col.Add( new Label( "Args:" ) { HorizontalSizeMode = SizeMode.CanShrink, Color = Theme.Text.WithAlpha( 0.5f ) } );

		foreach ( var hint in ArgumentHints )
		{
			var w = new Widget();
			w.ToolTip = $"{hint.Help}";
			w.Name = "arg";
			w.SetStyles( "#arg { background-color: #333333; border-radius: 4px; }" );
			w.Layout = Layout.Row();
			w.Layout.Margin = 4;
			w.Layout.Spacing = 4;

			w.Layout.Add( new Label( hint.Hint.Name.ToString() ) { HorizontalSizeMode = SizeMode.CanShrink, Color = Theme.Text.WithAlpha( 0.5f ) } );
			w.Layout.Add( new Label( hint.Name ) { HorizontalSizeMode = SizeMode.CanShrink } );

			col.Add( w );
		}

		col.AddStretchCell();

		return col;
	}

	protected override void OnPaint()
	{
		Paint.SetBrushAndPen( Theme.WidgetBackground );
		Paint.DrawRect( LocalRect, 4 );

		Paint.SetBrushAndPen( Theme.ControlBackground );
		Paint.DrawRect( _rightColumn.OuterRect, 4 );
	}

	public IEnumerable<string> GetArguments()
	{
		HashSet<string> arguments = [];

		foreach ( var o in Target.Body )
		{
			o.CollectArguments( arguments );
		}

		return arguments;
	}
}
