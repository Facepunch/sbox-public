using Sandbox.Internal;

namespace Editor.PanelInspector;

[Dock( "Editor", "UI Panels", "web_asset" )]
public partial class PanelInspectorWidget : Widget
{
	LineEdit Filter;
	TreeView TreeView;

	public PanelInspectorWidget( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Spacing = 4;

		CreateToolBar();

		TreeView = new TreeView( this );
		TreeView.ExpandForSelection = true;

		Layout.Add( TreeView, 1 );

		TreeView.SelectionOverride = () => EditorUtility.InspectorObject;
		TreeView.ItemSelected += ( o ) =>
		{
			if ( o is IPanel panel )
			{
				SelectedPanel = panel;
				EditorUtility.InspectorObject = panel;
			}
		};

		TreeView.ItemHoverEnter += OnHoverNode;
		TreeView.ItemHoverLeave += o => OnHoverNode( null );
	}

	Option HighlightSelected;
	Option PickerOption;

	void CreateToolBar()
	{
		var toolbar = new ToolBar( this );
		toolbar.SetIconSize( 18 );

		Filter = new LineEdit( this );
		Filter.PlaceholderText = "Filter Panels..";
		Filter.TextEdited += OnTextEdited;
		toolbar.AddWidget( Filter );

		PickerOption = toolbar.AddOption( "Pick", "colorize", OnPickerToggle );
		PickerOption.Checkable = true;

		HighlightSelected = toolbar.AddOption( "Highlight Selected", "preview" );
		HighlightSelected.Checkable = true;
		HighlightSelected.Checked = true;

		Layout.Add( toolbar );
	}

	private void OnTextEdited( string filterText )
	{
		TreeView.Dirty();
	}

	GameScenePicker picker;

	void OnPickerToggle()
	{
		picker?.Destroy();

		if ( PickerOption.Checked )
		{
			picker = new GameScenePicker();
			picker.MouseMove = x => HighlightPanelAt( x * DpiScale );
			picker.MouseClick = SelectHighlightedPanel;
			picker.Destroyed = () => PickerOption.Checked = false;
		}
	}

	void HighlightPanelAt( Vector2 pos )
	{
		foreach ( var panel in EditorUtility.GetRootPanels() )
		{
			var p = panel.GetPanelAt( pos, true, true );
			if ( !p.IsValid() )
				continue;

			HoveredPanel = p;
			return;
		}
	}

	void SelectHighlightedPanel()
	{
		if ( HoveredPanel != null )
		{
			TreeView.SelectItem( HoveredPanel, false );
			TreeView.UpdateIfDirty();
			TreeView.ScrollTo( HoveredPanel );
		}

		SelectedPanel = HoveredPanel;
	}

	long lastHash;

	[EditorEvent.Frame]
	public void Frame()
	{
		if ( !Visible )
			return;

		var hash = EditorUtility.GetRootPanels().Sum( x => (long)x.GetHashCode() );
		if ( hash == lastHash )
			return;

		lastHash = hash;
		TreeView.Clear();

		foreach ( var panel in EditorUtility.GetRootPanels() )
		{
			TreeView.AddItem( new PanelTreeNode( panel ) );
		}
	}

	protected override void OnVisibilityChanged( bool visible )
	{
		base.OnVisibilityChanged( visible );

		if ( !visible )
		{
			SelectedPanel = null;
			HoveredPanel = null;
		}
	}

	protected override void OnMouseLeave()
	{
		base.OnMouseLeave();
	}

	private void OnHoverNode( object target )
	{
		HoveredPanel = null;

		if ( target is PanelTreeNode node )
			HoveredPanel = node.Value;
	}

	Sandbox.Internal.IPanel SelectedPanel;
	Sandbox.Internal.IPanel HoveredPanel;

	void DrawBoxSize( Rect inner, Rect outer, Rect rect, Color color )
	{
		var pos = outer.TopLeft;
		pos.y -= 20;

		if ( pos.x < 4 ) pos.x = 4;
		if ( pos.y < 4 ) pos.y = 4;

		var margin = "";
		if ( outer != rect )
		{
			margin = $" margin[{rect.Left - outer.Left:n0},{rect.Top - outer.Top:n0},{outer.Right - rect.Right:n0},{outer.Bottom - rect.Bottom:n0}]";
		}

		var padding = "";
		if ( inner != rect )
		{
			padding = $" padding[{inner.Left - rect.Left:n0},{inner.Top - rect.Top:n0},{rect.Right - inner.Right:n0},{rect.Bottom - inner.Bottom:n0}]";
		}

		Paint.SetBrush( color.WithAlpha( 0.9f ) );
		Paint.ClearPen();
		Paint.DrawTextBox( new Rect( pos, new Vector2( 1000, 32 ) ), $"{rect.Width:n0}x{rect.Height:n0}{margin}{padding}", Color.Black, new Sandbox.UI.Margin( 5.0f, 2.0f ), 0, TextFlag.LeftTop );
		Paint.ClearBrush();
	}

	void DrawPanelHighlight( IPanel panel, Color color )
	{
		if ( !panel.IsValid() || !panel.IsVisible )
			return;

		Paint.SetPen( color.WithAlpha( 0.8f ), 1.0f, PenStyle.Dash );
		Paint.DrawRect( panel.InnerRect.Shrink( 0, 0, 1, 1 ) );

		Paint.SetPen( color.WithAlpha( 0.8f ), 1.0f );
		Paint.DrawRect( panel.OuterRect.Shrink( 0, 0, 1, 1 ) );

		Paint.SetPen( color.WithAlpha( 0.8f ), 1.0f, PenStyle.Dot );
		Paint.DrawRect( panel.Rect.Shrink( 0, 0, 1, 1 ) );

		DrawBoxSize( panel.InnerRect, panel.OuterRect, panel.Rect, color );
	}

	[Event( "sceneview.paintoverlay" )]
	public void DrawGameOverlay()
	{
		Paint.Scale( 1.0f / DpiScale, 1.0f / DpiScale );

		if ( HighlightSelected.Checked )
		{
			DrawPanelHighlight( SelectedPanel, Color.Cyan );

			if ( HoveredPanel == SelectedPanel )
				return;
		}

		DrawPanelHighlight( HoveredPanel, Color.Yellow );
	}
}

