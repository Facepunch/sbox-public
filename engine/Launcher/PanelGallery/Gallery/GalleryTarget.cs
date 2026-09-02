namespace Sandbox.PanelGallery;

/// <summary>
/// Something for the property driven controls to edit. The controls that build themselves from
/// a <see cref="SerializedProperty"/> - vectors, enums, switches - need a real object behind
/// them, so the gallery keeps one of these around and binds to its properties.
/// </summary>
public class GalleryTarget
{
	public enum Facing
	{
		North,
		East,
		South,
		West
	}

	public enum Quality
	{
		Low,
		Medium,
		High,
		Ultra,
		Cinematic
	}

	public float Scale { get; set; } = 1.0f;
	public int Count { get; set; } = 4;

	public Vector2 Offset { get; set; } = new Vector2( 10, 20 );
	public Vector3 Position { get; set; } = new Vector3( 1, 2, 3 );
	public Vector4 Tint { get; set; } = new Vector4( 1, 1, 1, 1 );

	// Four or fewer options renders as a button group, more becomes a dropdown
	public Facing Direction { get; set; } = Facing.North;
	public Quality Detail { get; set; } = Quality.High;

	public bool Enabled { get; set; } = true;
	public Color Colour { get; set; } = Color.Orange;

	/// <summary>
	/// A property of this object, for binding a control to.
	/// </summary>
	public SerializedProperty Property( string name )
	{
		return Game.TypeLibrary.GetSerializedObject( this ).GetProperty( name );
	}
}
