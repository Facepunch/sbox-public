using Sandbox;

namespace Editor;

public class VolumetricFogVolumeTool : EditorTool<VolumetricFogVolume>
{
	private IDisposable _componentUndoScope;

	public override void OnUpdate()
	{
		var volumetricFogVolume = GetSelectedComponent<VolumetricFogVolume>();
		if ( volumetricFogVolume == null )
			return;

		var currentBounds = volumetricFogVolume.Bounds;

		using ( Gizmo.Scope( "Volumetric Fog Volume Editor", volumetricFogVolume.WorldTransform ) )
		{
			if ( Gizmo.Control.BoundingBox( "Bounds", currentBounds, out var newBounds ) )
			{
				if ( Gizmo.WasLeftMousePressed )
				{
					_componentUndoScope = SceneEditorSession.Active.UndoScope( "Resize Volumetric Fog Volume Bounds" ).WithComponentChanges( volumetricFogVolume ).Push();
				}
				volumetricFogVolume.Bounds = newBounds;
			}

			if ( Gizmo.WasLeftMouseReleased )
			{
				_componentUndoScope?.Dispose();
				_componentUndoScope = null;
			}
		}
	}
}
