namespace NetSim;

public enum SimTeam { None, Red, Blue }

/// <summary>
/// Game-like components with many saved properties of mixed types, like a player prefab's.
/// </summary>
public sealed class SimStats : Component
{
	[Property] public string DisplayName { get; set; } = "player";
	[Property] public float Health { get; set; } = 100;
	[Property] public float MaxHealth { get; set; } = 100;
	[Property] public int Kills { get; set; }
	[Property] public int Deaths { get; set; }
	[Property] public SimTeam Team { get; set; } = SimTeam.Red;
	[Property] public Color Tint { get; set; } = Color.White;
	[Property] public Vector3 SpawnOffset { get; set; } = new( 0, 0, 16 );
	[Property] public Angles ViewAngles { get; set; }
	[Property] public float? Armor { get; set; }
	[Property] public GameObject Target { get; set; }
	[Property] public List<string> Badges { get; set; } = new() { "first", "second" };
	[Property] public Curve Falloff { get; set; } = new( new Curve.Frame( 0, 1 ), new Curve.Frame( 1, 0 ) );
	[Property] public string Bio { get; set; } = "A player with a description long enough to look like real text.";
}

public sealed class SimInventory : Component
{
	[Property] public List<string> Items { get; set; } = new() { "crowbar", "pistol", "physgun", "toolgun" };
	[Property] public int Slots { get; set; } = 9;
	[Property] public int ActiveSlot { get; set; } = 2;
	[Property] public Dictionary<string, int> Ammo { get; set; } = new() { ["pistol"] = 30, ["smg"] = 90 };
	[Property] public Component ActiveWeapon { get; set; }
	[Property] public bool AllowDrop { get; set; } = true;
	[Property] public float SwitchDelay { get; set; } = 0.25f;
}

public sealed class SimMovement : Component
{
	[Property] public float WalkSpeed { get; set; } = 110;
	[Property] public float RunSpeed { get; set; } = 320;
	[Property] public float DuckedSpeed { get; set; } = 70;
	[Property] public float JumpSpeed { get; set; } = 300;
	[Property] public float Acceleration { get; set; } = 10;
	[Property] public float AirAcceleration { get; set; } = 2;
	[Property] public float Friction { get; set; } = 4;
	[Property] public float StepHeight { get; set; } = 18;
	[Property] public float GroundAngle { get; set; } = 45;
	[Property] public Vector3 Gravity { get; set; } = new( 0, 0, -800 );
	[Property] public BBox Hull { get; set; } = new( new Vector3( -16, -16, 0 ), new Vector3( 16, 16, 72 ) );
	[Property] public bool UseInput { get; set; } = true;
	[Property] public string RunAction { get; set; } = "Run";
	[Property] public string JumpAction { get; set; } = "Jump";
}

public sealed class SimCosmetics : Component
{
	[Property] public List<string> Clothing { get; set; } = new() { "hat", "shirt", "trousers", "shoes", "gloves" };
	[Property] public float Height { get; set; } = 1;
	[Property] public float Age { get; set; } = 0.5f;
	[Property] public Color SkinTint { get; set; } = new( 0.9f, 0.7f, 0.6f );
	[Property] public string Workshop { get; set; } = "facepunch.citizen";
}

/// <summary>
/// Struct sync values: a record struct like sandbox's physgun GrabState, and a plain struct without its own hash.
/// </summary>
public record struct SimGrabState
{
	public bool Active { get; set; }
	public bool Pulling { get; set; }
	public GameObject GameObject { get; set; }
	public Vector3 LocalOffset { get; set; }
	public Vector3 LocalNormal { get; set; }
	public Rotation GrabOffset { get; set; }
	public float GrabDistance { get; set; }
	public Vector3 PullApex { get; set; }
	public float PullStrength { get; set; }
}

public struct SimPlainState
{
	public Vector3 Position;
	public int Count;
	public string Name;
}

public sealed class SimPointList : Component
{
	[Sync] public List<SimPlainState> Points { get; set; } = new();
}

public sealed class SimGrabber : Component
{
	[Sync] public SimGrabState Grab { get; set; }
	[Sync] public SimPlainState Plain { get; set; }
}
