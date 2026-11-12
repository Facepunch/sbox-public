using Sandbox;

namespace MenuProject.Platform;

/// <summary>
/// The Avatar page's you - the Body slot's skin, the Eyes slot's colour, and the colours of what's on
/// </summary>
partial class AvatarShelf
{
	bool BodyOpen;

	void OpenBody()
	{
		PickCategory( null );
		BodyOpen = true;
	}

	static Clothing WornSkin => Draft.Container.Clothing.Select( x => x.Clothing ).FirstOrDefault( x => x is { HasHumanSkin: true } );

	/// <summary>
	/// Skins - one block for each body, as the avatar editor has them: male, female, then the rest
	/// </summary>
	static IEnumerable<IGrouping<int, Clothing>> SkinGroups => AvatarCatalog.All
		.Where( x => x.HasHumanSkin )
		.GroupBy( x => x.HumanSkinTags is not { } tags ? 2 : tags.Has( "male" ) ? 0 : tags.Has( "female" ) ? 1 : 2 )
		.OrderBy( x => x.Key );

	static bool IsEyes( AvatarClothingCategory.Category category ) => category?.Name == "eyes";

	static readonly float[] EyeShades = Enumerable.Range( 0, 12 ).Select( x => x / 11f ).ToArray();

	/// <summary>
	/// An iris of <paramref name="shade"/> on the eye colour ramp - a dark pupil, the colour round it darkening to its
	/// rim, as a background
	/// </summary>
	static string Iris( float shade )
	{
		var colour = Dresser.EyeColorGradient.Evaluate( shade );
		var light = colour.Hex;
		var dark = new Color( colour.r * 0.45f, colour.g * 0.45f, colour.b * 0.45f ).Hex;
		var rim = new Color( colour.r * 0.2f, colour.g * 0.2f, colour.b * 0.2f ).Hex;

		return $"radial-gradient( circle, #050505 0%, #050505 24%, {light} 30%, {light} 46%, {dark} 62%, {rim} 68%, rgba( 0, 0, 0, 0 ) 70% )";
	}

	/// <summary>
	/// Something on that comes in more than one colour - where it is on its ramp, to bind a picker to
	/// </summary>
	sealed class Tint( Clothing clothing )
	{
		public Clothing Clothing => clothing;

		public float Value
		{
			get => Draft.GetTint( clothing );
			set => Draft.SetTint( clothing, value );
		}
	}

	static List<Tint> Tintable( AvatarClothingCategory.Category category )
	{
		var covers = Covers( category );

		return Draft.Container.Clothing
			.Select( x => x.Clothing )
			.Where( x => x is { AllowTintSelect: true } && covers.Contains( x.Category ) )
			.Select( x => new Tint( x ) )
			.ToList();
	}
}
