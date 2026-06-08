using Sandbox;

namespace Editor;

public class BoxColliderTool : EditorTool<BoxCollider>
{
	private IDisposable _componentUndoScope;

	public override void OnUpdate()
	{
		var boxCollider = GetSelectedComponent<BoxCollider>();
		if ( boxCollider == null )
			return;

		var currentBox = BBox.FromPositionAndSize( boxCollider.Center, boxCollider.Scale );

		using ( Gizmo.Scope( "Box Collider Editor", boxCollider.WorldTransform ) )
		{
			if ( Gizmo.Control.BoundingBox( "Bounds", currentBox, out var newBox ) )
			{
				_componentUndoScope ??= SceneEditorSession.Active.UndoScope( "Resize Box Collider" )
					.WithComponentChanges( boxCollider ).Push();

				boxCollider.Center = newBox.Center;
				boxCollider.Scale = newBox.Size;
			}

			if ( Gizmo.WasLeftMouseReleased )
			{
				_componentUndoScope?.Dispose();
				_componentUndoScope = null;
			}
		}
	}
}
