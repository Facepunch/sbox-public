using Sandbox;

namespace Editor;

public class VolumetricFogVolumeTool : EditorTool<VolumetricFogVolume>
{
	private IDisposable _componentUndoScope;

	public override void OnUpdate()
	{
		if ( !Gizmo.Pressed.Any )
		{
			_componentUndoScope?.Dispose();
			_componentUndoScope = null;
		}

		var volumetricFogVolume = GetSelectedComponent<VolumetricFogVolume>();
		if ( volumetricFogVolume == null )
			return;

		var currentBounds = volumetricFogVolume.Bounds;

		using ( Gizmo.Scope( "Volumetric Fog Volume Editor", volumetricFogVolume.WorldTransform ) )
		{
			if ( Gizmo.Control.BoundingBox( "Bounds", currentBounds, out var newBounds ) )
			{
				_componentUndoScope ??= SceneEditorSession.Active.UndoScope( "Resize Volumetric Fog Volume Bounds" )
					.WithComponentChanges( volumetricFogVolume ).Push();

				volumetricFogVolume.Bounds = newBounds;
			}
		}
	}
}
