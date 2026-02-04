namespace Editor.MeshEditor;

partial class MeshTool
{
	private EditorToolButton vertexSnapButton;
	private EditorToolButton overlayButton;

	public bool VertexSnappingEnabled { get; set; } = false;
	public bool OverlaySelection { get; set; } = true;

	public override Widget CreateToolbarWidget()
	{
		var group = new Widget();
		group.FixedHeight = Theme.RowHeight;
		group.Layout = Layout.Row();
		group.Layout.Spacing = 6;
		group.Layout.Margin = new( 2, 0 );

		group.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground );
			Paint.DrawRect( group.LocalRect, Theme.ControlRadius );
			return true;
		};

		vertexSnapButton = new EditorToolButton();
		vertexSnapButton.GetIcon = () => "trip_origin";
		vertexSnapButton.ToolTip = "Toggle Vertex Snapping";
		vertexSnapButton.Action = ToggleVertexSnapping;
		vertexSnapButton.IsActive = () => VertexSnappingEnabled;

		group.Layout.Add( vertexSnapButton );

		overlayButton = new EditorToolButton();
		overlayButton.GetIcon = () => "blur_on";
		overlayButton.ToolTip = "Toggle Selection Overlay";
		overlayButton.Action = ToggleOverlaySelection;
		overlayButton.IsActive = () => OverlaySelection;

		group.Layout.Add( overlayButton );

		return group;
	}

	private void ToggleVertexSnapping()
	{
		VertexSnappingEnabled = !VertexSnappingEnabled;
		SaveVertexSnapping();
	}

	private void ToggleOverlaySelection()
	{
		OverlaySelection = !OverlaySelection;
		SaveOverlaySelection();
	}

	private void SaveVertexSnapping()
	{
		EditorCookie.Set( "MeshTool.VertexSnapping", VertexSnappingEnabled );
	}

	private void SaveOverlaySelection()
	{
		EditorCookie.Set( "MeshTool.OverlaySelection", OverlaySelection );
	}

	private void LoadToolbarCookies()
	{
		OverlaySelection = EditorCookie.Get( "MeshTool.OverlaySelection", true );
		VertexSnappingEnabled = EditorCookie.Get( "MeshTool.VertexSnapping", false );
	}
}
