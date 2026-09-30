using Sandbox;

namespace Editor;

public class EnvmapProbeTool : EditorTool<EnvmapProbe>
{
	private IDisposable _componentUndoScope;

	public override void OnUpdate()
	{
		if ( !Gizmo.Pressed.Any )
		{
			_componentUndoScope?.Dispose();
			_componentUndoScope = null;
		}

		var envmapProbe = GetSelectedComponent<EnvmapProbe>();
		if ( envmapProbe == null )
			return;

		var currentBounds = envmapProbe.Bounds;

		using ( Gizmo.Scope( "EnvmapProbe Collider Editor", envmapProbe.WorldTransform ) )
		{
			if ( Gizmo.Control.BoundingBox( "Bounds", currentBounds, out var newBounds ) )
			{
				_componentUndoScope ??= SceneEditorSession.Active.UndoScope( "Resize EnvmapProbe Bounds" )
					.WithComponentChanges( envmapProbe ).Push();

				envmapProbe.Bounds = newBounds;
			}
		}
	}
}
