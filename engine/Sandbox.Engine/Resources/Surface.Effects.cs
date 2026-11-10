namespace Sandbox;

public partial class Surface
{
	public struct ScrapeEffectData
	{
		[DefaultValue( 1.0f )]
		public float RoughnessFactor { get; set; } = 1.0f;

		[DefaultValue( 0.5f )]
		public float RoughThreshold { get; set; } = 0.5f;

		[Obsolete, Hide, ResourceType( "vpcf" )]
		public List<string> SmoothParticles { get; set; }

		[Obsolete, Hide, ResourceType( "vpcf" )]
		public List<string> RoughParticles { get; set; }

		[Obsolete, Hide, ResourceType( "decal" )]
		public List<string> SmoothDecal { get; set; }

		[Obsolete, Hide, ResourceType( "decal" )]
		public List<string> RoughDecal { get; set; }

		public ScrapeEffectData() { }
	}

	[InlineEditor, Title( "Scraping" )]
	public ScrapeEffectData ScrapeEffects { get; set; } = new();

	/// <summary>
	/// Play a collision sound based on this shape's surface. Can return null if sound is invalid, or too quiet to play.
	/// </summary>
	public SoundHandle PlayCollisionSound( Vector3 position, float speed = 320.0f )
	{
		float volume = speed / 1000.0f;
		if ( volume > 1.0f ) volume = 1.0f;

		// I have an inkling that this would be aweomse
		// Scale volume by the mass of the object
		//float mass = self.Body.Mass;
		//if ( mass == 0 ) mass = 100; // static objects don't have a mass
		//float massScale = mass.Remap( 0, 1500, 0, 1 );
		//volume *= massScale;

		if ( volume < 0.001f ) return default;

		var sound = SoundCollection.ImpactHard;

		if ( speed < 130f || sound == null )
		{
			sound = SoundCollection.ImpactSoft;
		}

		if ( sound == null )
			return default;

		var s = Sound.Play( sound, position );
		if ( s is not null )
		{
			s.Volume *= volume;
		}
		return s;
	}

	internal SoundEvent GetScrapeSound( Surface other )
	{
		var smooth = other is not null && other.ScrapeEffects.RoughnessFactor < ScrapeEffects.RoughThreshold;
		return smooth
			? _sounds.ScrapeSmooth ?? _sounds.ScrapeRough
			: _sounds.ScrapeRough ?? _sounds.ScrapeSmooth;
	}

	internal SoundEvent GetScrapeSound( int otherSurfaceIndex )
	{
		if ( _sounds.ScrapeRough is null )
			return _sounds.ScrapeSmooth;
		if ( _sounds.ScrapeSmooth is null )
			return _sounds.ScrapeRough;
		return GetScrapeSound( All.GetValueOrDefault( otherSurfaceIndex ) );
	}
}
