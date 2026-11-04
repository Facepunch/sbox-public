using System.ComponentModel;

namespace Sandbox;

public partial class Surface
{
	[Obsolete]
	public float Dampening { get; set; } = 0.0f;

	[Obsolete]
	public struct ImpactEffectData
	{
		/// <summary>
		/// Spawn one of these particles on impact.
		/// </summary>

		[ResourceType( "vpcf" )] public List<string> Regular { get; set; }

		/// <summary>
		/// Spawn one of these particles when hit by a bullet.
		/// </summary>
		[ResourceType( "vpcf" )] public List<string> Bullet { get; set; }

		/// <summary>
		/// Use one of these as the bullet impact decal.
		/// </summary>
		[ResourceType( "decal" )] public List<string> BulletDecal { get; set; }

		/// <summary>
		/// Spawn one of these particles on impact.
		/// </summary>
		[ResourceType( "vpcf" )] public List<string> SoftParticles { get; set; }

		/// <summary>
		/// Use one of these as a physics impact decal.
		/// </summary>
		[ResourceType( "decal" )] public List<string> SoftDecal { get; set; }

		/// <summary>
		/// Spawn one of these particles on impact.
		/// </summary>
		[ResourceType( "vpcf" )] public List<string> HardParticles { get; set; }


		/// <summary>
		/// Use one of these as a physics impact decal.
		/// </summary>
		[ResourceType( "decal" )] public List<string> HardDecal { get; set; }
	}

	/// <summary>
	/// Impact effects of this surface material.
	/// </summary>
	[Obsolete, Hide, EditorBrowsable( EditorBrowsableState.Never )]
	public ImpactEffectData ImpactEffects { get; set; }

	[Obsolete]
	public struct OldSoundData
	{
		/// <summary>
		/// Left footstep sound.
		/// </summary>
		[ResourceType( "sound" )] public string FootLeft { get; set; }

		/// <summary>
		/// Right footstep sound.
		/// </summary>
		[ResourceType( "sound" )] public string FootRight { get; set; }

		/// <summary>
		/// Jump sound for this surface.
		/// </summary>
		[ResourceType( "sound" )] public string FootLaunch { get; set; }

		/// <summary>
		/// Landing sound for this surface.
		/// </summary>
		[ResourceType( "sound" )] public string FootLand { get; set; }

		/// <summary>
		/// Bullet impact sound for this surface.
		/// </summary>
		[ResourceType( "sound" )] public string Bullet { get; set; }

		/// <summary>
		/// Hard, high velocity impact sound.
		/// </summary>
		[ResourceType( "sound" )] public string ImpactHard { get; set; }

		/// <summary>
		/// Soft, low velocity impact sound.
		/// </summary>
		[ResourceType( "sound" )] public string ImpactSoft { get; set; }

		/// <summary>
		/// Rough scraping sound when scraping against another surface.
		/// </summary>
		[ResourceType( "sound" )] public string ScrapeRough { get; set; }

		/// <summary>
		/// Smooth scraping sound when scraping against another surface.
		/// </summary>
		[ResourceType( "sound" )] public string ScrapeSmooth { get; set; }
	}

	/// <summary>
	/// Sounds associated with this surface material.
	/// </summary>
	[Obsolete, Hide, EditorBrowsable( EditorBrowsableState.Never )]
	public OldSoundData Sounds { get; set; }
}
