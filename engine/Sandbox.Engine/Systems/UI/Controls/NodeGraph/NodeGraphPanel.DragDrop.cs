namespace Sandbox.UI.NodeGraph;

//
// Dropping node types dragged in from other panels or from outside the app.
//
public partial class NodeGraphPanel
{
	/// <summary>
	/// The node type a drag carries, or null when it carries none. <paramref name="e"/> is an
	/// in-app drag's "ondragenter" or "ondrop", whose target is the dragged panel, or a
	/// <see cref="DropEvent"/> from outside the app.
	/// </summary>
	protected virtual INodeType NodeTypeFromDrop( PanelEvent e ) => null;

	NodePanel _nodePreview;
	bool _previewFollowsMouse;

	/// <summary>
	/// Where a drag is, in canvas units.
	/// </summary>
	Vector2 DropCanvas( PanelEvent e ) => e is DropEvent drop ? ScreenToCanvas( drop.Position - Box.Rect.Position ) : MouseCanvas;

	void ShowDropPreview( INodeType type, Vector2 position )
	{
		ClearDropPreview();

		if ( Graph is null || type.CreateNode( Graph ) is not { } node ) return;

		node.Position = position.SnapToGrid( GridSize );
		_nodePreview = CreatePreview( node );
	}

	void ClearDropPreview()
	{
		_nodePreview?.Delete( true );
		_nodePreview = null;
	}

	protected override void OnDragEnter( PanelEvent e )
	{
		base.OnDragEnter( e );

		if ( _nodePreview.IsValid() || NodeTypeFromDrop( e ) is not { } type ) return;

		_previewFollowsMouse = true;
		ShowDropPreview( type, MouseCanvas );
	}

	protected override void OnDragLeave( PanelEvent e )
	{
		base.OnDragLeave( e );
		ClearDropPreview();
	}

	protected override void OnDrop( PanelEvent e )
	{
		base.OnDrop( e );

		var position = DropCanvas( e );

		// A drag from outside the app asks while it hovers: answer, and show where the node would go
		if ( e is DropEvent { IsDrop: false } hover )
		{
			if ( NodeTypeFromDrop( e ) is not { } hoverType ) return;

			hover.Action = DropAction.Copy;
			_previewFollowsMouse = false;

			if ( !_nodePreview.IsValid() ) ShowDropPreview( hoverType, position );
			else _nodePreview.MoveOriginTo( position );

			e.StopPropagation();
			return;
		}

		ClearDropPreview();

		if ( NodeTypeFromDrop( e ) is not { } type ) return;

		Focus();
		CreateNewNode( type, position, null );
		e.StopPropagation();
	}
}
