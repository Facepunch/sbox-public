using System.Text.Json.Serialization;

namespace Sandbox.WeaponIcons;

/// <summary>
/// Stored in project cookies under the icon's name, so you can always get settings used to generate an icon last time
/// </summary>
public class WeaponIconSettings
{
	[Title( "Icon Model" ), JsonIgnore]
	public Model Model
	{
		get => string.IsNullOrEmpty( ModelPath ) ? null : Model.Load( ModelPath );
		set => ModelPath = value?.ResourcePath;
	}

	[Hide] public string ModelPath { get; set; }

	/// <summary>Optional model sequence used to pose animated items for their icon.</summary>
	public string AnimationSequence { get; set; }

	public Vector3 PositionOffset { get; set; }

	public Angles Rotation { get; set; } = new Angles( 0, 90, 0 );

	[Range( 0.1f, 10f )] public float CameraZoom { get; set; } = 1f;

	[Range( 0f, 4f )] public float OutlineWidth { get; set; } = 1.5f;

	public Color OutlineColor { get; set; } = Color.White; // probably not needed, don't rely on this
}
