/// <summary>
/// Remembers the eye's previous pose so reset also works after saving or duplicating a character.
/// </summary>
[Hide]
public sealed class EyePoseOverride : Component
{
	/// <summary>
	/// The rotation the eye had before it was posed.
	/// </summary>
	[Property] public Rotation PreviousRotation { get; set; }

	/// <summary>
	/// Whether another pose was already controlling this bone.
	/// </summary>
	[Property] public bool WasProcedural { get; set; }

	/// <summary>
	/// Restores the previous pose and releases an animated eye back to the model.
	/// </summary>
	public void Restore()
	{
		GameObject.LocalRotation = PreviousRotation;
		if ( !WasProcedural ) GameObject.Flags = GameObject.Flags.WithFlag( GameObjectFlags.ProceduralBone, false );
		Destroy();
	}
}
