namespace Sandbox;

public partial class Surface
{
	/// <summary>
	/// Holds a dictionary of common sounds associated with a surface. This allows you to pick and choose an appropriate sound.
	/// </summary>
	public struct SurfaceSoundCollection
	{
		/// <summary>
		/// Left footstep sound.
		/// </summary>
		public SoundEvent FootLeft { get; set; }

		/// <summary>
		/// Right footstep sound.
		/// </summary>
		public SoundEvent FootRight { get; set; }

		/// <summary>
		/// Jump sound for this surface.
		/// </summary>
		public SoundEvent FootLaunch { get; set; }

		/// <summary>
		/// Landing sound for this surface.
		/// </summary>
		public SoundEvent FootLand { get; set; }

		/// <summary>
		/// Bullet impact sound for this surface.
		/// </summary>
		public SoundEvent Bullet { get; set; }

		/// <summary>
		/// Hard, high velocity impact sound.
		/// </summary>
		public SoundEvent ImpactHard { get; set; }

		/// <summary>
		/// Soft, low velocity impact sound.
		/// </summary>
		public SoundEvent ImpactSoft { get; set; }

		/// <summary>
		/// Rough scraping sound when scraping against another surface.
		/// </summary>
		public SoundEvent ScrapeRough { get; set; }

		/// <summary>
		/// Smooth scraping sound when scraping against another surface.
		/// </summary>
		public SoundEvent ScrapeSmooth { get; set; }

		/// <summary>
		/// Sound to play when an object made of this breaks
		/// </summary>
		public SoundEvent Break { get; set; }
	}

	/// <summary>
	/// Sounds for this surface material
	/// </summary>
	[InlineEditor, Title( "Sounds" )]
	public SurfaceSoundCollection SoundCollection
	{
		get => _sounds;
		set
		{
			_sounds = value;
			if ( !_nativeSurface.IsNull )
				_nativeSurface.m_bHasScrapeSounds = HasScrapeSounds;
		}
	}

	SurfaceSoundCollection _sounds;

	internal bool HasScrapeSounds => _sounds.ScrapeRough is not null || _sounds.ScrapeSmooth is not null;
}
