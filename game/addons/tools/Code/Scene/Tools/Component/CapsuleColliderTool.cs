using Sandbox;

namespace Editor;

public class CapsuleColliderTool : EditorTool<CapsuleCollider>
{
	private IDisposable _componentUndoScope;

	public override void OnUpdate()
	{
		if ( !Gizmo.Pressed.Any )
		{
			_componentUndoScope?.Dispose();
			_componentUndoScope = null;
		}

		var capsuleCollider = GetSelectedComponent<CapsuleCollider>();
		if ( capsuleCollider == null )
			return;

		using ( Gizmo.Scope( "Capsule Collider Editor", capsuleCollider.WorldTransform ) )
		{
			var currentCapsule = new Capsule( capsuleCollider.Start, capsuleCollider.End, capsuleCollider.Radius );
			if ( Gizmo.Control.Capsule( "capsule", currentCapsule, out var newCapsule, Gizmo.Colors.Green ) )
			{
				_componentUndoScope ??= SceneEditorSession.Active.UndoScope( "Resize Capsule Collider" )
					.WithComponentChanges( capsuleCollider ).Push();

				capsuleCollider.Start = newCapsule.CenterA;
				capsuleCollider.End = newCapsule.CenterB;
				capsuleCollider.Radius = newCapsule.Radius;
			}
		}
	}
}
