using Sandbox.Volumes;

namespace Sandbox;

/// <summary>
/// A volume that defines a region in the scene where post processing effects will be applied.
/// </summary>
[EditorHandle( Icon = "contrast" )]
[Category( "Post Processing" )]
[Icon( "contrast" )]
public class PostProcessVolume : VolumeComponent, Component.ExecuteInEditor
{
	/// <summary>
	/// Higher priority volumes override lower priority ones. The default priority is 0.
	/// </summary>
	[Property]
	public int Priority { get; set; } = 0;

	/// <summary>
	/// Allows fading in and out
	/// </summary>
	[Property]
	[Range( 0, 1 )]
	public float BlendWeight { get; set; } = 1.0f;

	/// <summary>
	/// Distance from the edge of the volume where blending starts.
	/// 0 means hard edge, higher values create softer transitions.
	/// </summary>
	[Property]
	[Range( 0, 500 )]
	[HideIf( "IsInfinite", true )]
	public float BlendDistance { get; set; } = 50.0f;

	/// <summary>
	/// Preview the post processing when this object is selected in the editor, or when the editor camera is inside the volume.
	/// </summary>
	[Property]
	public bool EditorPreview { get; set; } = true;

	/// <summary>
	/// Get weight based on position
	/// </summary>
	public float GetWeight( Vector3 pos )
	{
		if ( IsInfinite )
		{
			return BlendWeight;
		}

		var distance = GetEdgeDistance( pos );
		return MathX.Remap( distance, 0, BlendDistance, 0, BlendWeight );
	}
}
