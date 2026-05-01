using System.Text.Json.Serialization;

namespace Sandbox;

public partial class Sprite
{
	/// <summary>
	/// The different loop modes for sprite animation.
	/// </summary>
	public enum LoopMode
	{
		/// <summary>
		/// The animation will play from start to finish and then stop.
		/// </summary>
		[Icon( "not_interested" )]
		None,

		/// <summary>
		/// The animation will play from start to finish and then loop back to the start.
		/// </summary>
		[Icon( "loop" )]
		Loop,

		/// <summary>
		/// The animation will play from start to finish and then backwards from finish to start before looping.
		/// </summary>
		[Icon( "sync_alt" )]
		PingPong
	}

	public enum BroadcastEventType
	{
		/// <summary>
		/// A custom string will be broadcast by invoking an Action on the SpriteRenderer
		/// </summary>
		[Icon( "message" )]
		CustomMessage,

		/// <summary>
		/// Plays a sound from the sprite's position
		/// </summary>
		[Icon( "volume_up" )]
		PlaySound,

		/// <summary>
		/// Spawns a prefab at the sprite's position
		/// </summary>
		[Icon( "library_add" )]
		SpawnPrefab
	}

	/// <summary>
	/// Describes a single animation frame
	/// </summary>
	public class Frame
	{
		[KeyProperty]
		public Texture Texture { get; set; } = Texture.Transparent;

		public List<BroadcastEvent> BroadcastMessages { get; set; } = new();
	}

	/// <summary>
	/// A message that is broadcast when a frame is displayed.
	/// </summary>
	public class BroadcastEvent
	{
		[KeyProperty, EnumDropdown]
		public BroadcastEventType Type { get; set; } = BroadcastEventType.CustomMessage;

		[KeyProperty, ShowIf( nameof( Type ), BroadcastEventType.CustomMessage )]
		public string Message { get; set; }

		[KeyProperty, ShowIf( nameof( Type ), BroadcastEventType.PlaySound )]
		public SoundEvent Sound { get; set; }

		[KeyProperty, ShowIf( nameof( Type ), BroadcastEventType.SpawnPrefab )]
		public GameObject Prefab { get; set; }

		public override string ToString()
		{
			switch ( Type )
			{
				case BroadcastEventType.PlaySound:
					return Sound?.ResourceName ?? "no sound";
				case BroadcastEventType.SpawnPrefab:
					return Prefab?.Name ?? "no prefab";
				default:
					return Message;
			}
		}
	}

	/// <summary>
	/// Contains one or multiple frames that can be played in sequence.
	/// </summary>
	public class Animation
	{
		/// <summary>
		/// The name of the animation. Allows you to play specific animations by name.
		/// </summary>
		public string Name { get; set; } = "Default";

		/// <summary>
		/// The speed of the animation in frames per second.
		/// </summary>
		[ShowIf( nameof( IsAnimated ), true )]
		public float FrameRate { get; set; } = 15.0f;

		/// <summary>
		/// The point at which the rendered sprite is anchored from. This means scaling/rotating a sprite will do so around the origin.
		/// </summary>
		[Range( 0, 1 )]
		public Vector2 Origin { get; set; } = new Vector2( 0.5f, 0.5f );

		/// <summary>
		/// The loop mode of the animation. This determines what should happen when the animation reaches the final frame in playback.
		/// </summary>
		[ShowIf( nameof( IsAnimated ), true )]
		public LoopMode LoopMode { get; set; } = LoopMode.Loop;

		/// <summary>
		/// A list of frames that make up the animation. Each frame is a texture that will be displayed in sequence.
		/// </summary>
		[Group( "Frames", StartFolded = true ), WideMode( HasLabel = false )]
		public List<Frame> Frames { get; set; } = new();

		/// <summary>
		/// True if we have more than one frame
		/// </summary>
		[Hide, JsonIgnore]
		public bool IsAnimated => Frames?.Count > 1;
	}
}
