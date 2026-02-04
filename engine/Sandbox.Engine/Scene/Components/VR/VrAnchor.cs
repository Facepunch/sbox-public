namespace Sandbox.VR;

/// <summary>
/// Updates the the VR anchor based on a GameObject's transform.
/// </summary>
[Title( "VR Anchor" )]
[Category( "VR" )]
[EditorHandle( "materials/gizmo/anchor.png" )]
[Icon( "anchor" )]
public class VRAnchor : Component
{
	/// <summary>
	/// Update the VR anchor based on the GameObject's transform
	/// </summary>
	private void UpdateAnchor()
	{
		Input.VR.Anchor = GameObject.WorldTransform;
	}

	protected override void OnUpdate()
	{
		if ( !Enabled || Scene.IsEditor || !Game.IsRunningInVR )
			return;

		if ( IsProxy )
			return;

		UpdateAnchor();
	}

	protected override void OnPreRender()
	{
		if ( !Enabled || Scene.IsEditor || !Game.IsRunningInVR )
			return;

		if ( IsProxy )
			return;

		UpdateAnchor();
	}
}
