using Sandbox;

namespace Editor;

public class HullColliderTool : EditorTool<HullCollider>
{
	private IDisposable _componentUndoScope;

	public override void OnUpdate()
	{
		if ( !Gizmo.Pressed.Any )
		{
			_componentUndoScope?.Dispose();
			_componentUndoScope = null;
		}

		var hullCollider = GetSelectedComponent<HullCollider>();
		if ( hullCollider == null )
			return;

		if ( hullCollider.Type == HullCollider.PrimitiveType.Box )
		{
			var currentBox = BBox.FromPositionAndSize( hullCollider.Center, hullCollider.BoxSize );

			using ( Gizmo.Scope( "Hull Collider Editor", hullCollider.WorldTransform ) )
			{
				if ( Gizmo.Control.BoundingBox( "Bounds", currentBox, out var newBox ) )
				{
					_componentUndoScope ??= SceneEditorSession.Active.UndoScope( "Resize Hull Collider" )
						.WithComponentChanges( hullCollider ).Push();

					hullCollider.Center = newBox.Center;
					hullCollider.BoxSize = newBox.Size;
				}
			}
		}
	}
}
