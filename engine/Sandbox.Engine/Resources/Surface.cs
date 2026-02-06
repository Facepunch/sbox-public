using NativeEngine;
using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// A physics surface. This is applied to each <see cref="PhysicsShape">PhysicsShape</see> and controls its physical properties and physics related sounds.
/// </summary>
[AssetType( Name = "Surface Description", Extension = "surface", Category = "Physics", Flags = AssetTypeFlags.NoEmbedding )]
public partial class Surface : GameResource
{
	internal static Dictionary<int, Surface> All = new Dictionary<int, Surface>();

	[Hide]
	[JsonIgnore]
	public uint NameHash { get; internal set; }

	[Hide]
	[JsonIgnore]
	public int Index { get; internal set; }

	/// <summary>
	/// Filepath of the base surface. Use <see cref="SetBaseSurface">SetBaseSurface</see> and <see cref="GetBaseSurface">GetBaseSurface</see>.
	/// </summary>
	[ResourceType( "surface" ), Description( "Fallback surface for empty fields on this surface." )]
	public string BaseSurface { get; set; }

	/// <summary>
	/// Defines the audio properties of this surface for Steam Audio
	/// </summary>
	public AudioSurface AudioSurface { get; set; }

	/// <summary>
	/// A concise description explaining what this surface property should be used for.
	/// </summary>
	public string Description { get; set; }

	/// <summary>
	/// Friction of this surface material.
	/// </summary>
	[Range( 0, 1 ), Category( "Physics" ), DefaultValue( 0.8f )]
	public float Friction { get; set; } = 0.8f;

	/// <summary>
	/// Controls bounciness.
	/// </summary>
	[Range( 0, 1 ), Category( "Physics" ), DefaultValue( 0.25f )]
	public float Elasticity { get; set; } = 0.25f;

	/// <summary>
	/// Density of this surface material. This affects things like automatic mass calculation.
	/// Density is in kg/m^3.
	/// </summary>
	[Range( 0, 4000 ), Category( "Physics" ), DefaultValue( 2000.0f )]
	public float Density { get; set; } = 2000;

	/// <summary>
	/// Controls how easily rolling shapes (sphere, capsule) roll on surfaces.
	/// </summary>
	[Range( 0, 1 ), Category( "Physics" ), DefaultValue( 0.0f )]
	public float RollingResistance { get; set; } = 0;

	/// <summary>
	/// Velocity threshold, below which objects will not bounce due to their elasticity.
	/// </summary>
	[Range( 0, 100 ), Category( "Physics" ), DefaultValue( 40.0f )]
	public float BounceThreshold { get; set; } = 40;

	/// <summary>
	/// Linear drag applied when submerged.
	/// </summary>
	[Category( "Fluid" ), Title( "Linear Drag" ), Range( 0, 20 ), DefaultValue( 0.1f )]
	public float FluidLinearDrag { get; set; } = 0.1f;

	/// <summary>
	/// Angular drag applied when submerged.
	/// </summary>
	[Category( "Fluid" ), Title( "Angular Drag" ), Range( 0, 20 ), DefaultValue( 0.1f )]
	public float FluidAngularDrag { get; set; } = 0.1f;

	/// <summary>
	/// Returns the base surface of this surface, or null if we are the default surface.
	/// </summary>
	public Surface GetBaseSurface()
	{
		var baseSurf = All.Where( x => x.Value.ResourcePath == BaseSurface && x.Value != this ).FirstOrDefault().Value;
		if ( baseSurf == null && Index != 0 ) return All[0]; // If not found, fallback to default unless we are the default.
		return baseSurf;
	}

	/// <summary>
	/// Sets the base surface by name.
	/// </summary>
	public void SetBaseSurface( string name )
	{
		BaseSurface = All.Where( x => x.Value.ResourceName == name ).FirstOrDefault().Value.ResourcePath;
	}

	protected override void PostLoad()
	{
		Create();
	}

	protected override void PostReload()
	{
		Create( true );
	}

	void Create( bool reload = false )
	{
		var controller = g_pPhysicsSystem.GetSurfacePropertyController();
		CPhysSurfaceProperties props = controller.AddProperty( ResourceName, "default", Description ?? "" );

		NameHash = props.m_nameHash;
		Index = props.m_nIndex;

		// Get tags string and create a HashSet full of string tokens
		Tags?.Split( ' ' )
			.ToList()
			.ForEach( x => TagList.Add( StringToken.FindOrCreate( x ) ) );

		props.UpdatePhysics( Friction, Elasticity, Density, RollingResistance, BounceThreshold );
		props.m_AudioSurface = (int)AudioSurface;

		if ( reload )
		{
			g_pPhysicsSystem.UpdateSurfaceProperties( props );
		}

		All[Index] = this;
	}

	/// <summary>
	/// Find a surface by its index in the array. This is the fastest way to lookup, so it's
	/// passed from things like Traces since the index is going to be the same. It's important to
	/// know that this index shouldn't be saved or networked because it could differ between loads or clients.
	/// Instead send the name hash and look up using that.
	/// </summary>
	internal static Surface FindByIndex( int index )
	{
		if ( All.TryGetValue( index, out var val ) )
			return val;

		foreach ( var v in All )
		{
			return v.Value;
		}

		throw new System.Exception( "Default Surface not found!" );
	}

	/// <summary>
	/// Returns a Surface from its name, or null
	/// </summary>
	/// <param name="name">The name of a surface property to look up</param>
	/// <returns>The surface with given name, or null if such surface property doesn't exist</returns>
	public static Surface FindByName( string name )
	{
		return All.FirstOrDefault( x => x.Value.ResourceName == name ).Value;
	}

	protected override Bitmap CreateAssetTypeIcon( int width, int height )
	{
		return CreateSimpleAssetTypeIcon( "airline_stops", width, height, "#4596ec", "#a9c9ec" );
	}
}

/// <summary>
/// Defines acoustic properties of a surface, which defines how sound will bounce
/// </summary>
[Expose]
public enum AudioSurface
{
	Generic,
	Brick,
	Concrete,
	Ceramic,
	Gravel,
	Carpet,
	Glass,
	Plaster,
	Wood,
	Metal,
	Rock,
	Fabric,
	Foam,
	Sand,
	Snow,
	Soil,
	Curtain,
	Steel,
	AcousticTile,
	Leather,
	Linoleum,
	Asphalt,
	Water,
	Marble,
	Paper
}
