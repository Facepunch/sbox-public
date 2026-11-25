namespace Sandbox;

/// <summary>
/// This component should be added to stuff you want to be outlined. You will also need to 
/// add the Highlight component to the camera you want to render the outlines.
/// </summary>
[Title( "Highlight Outline" )]
[Category( "Rendering" )]
[Icon( "lightbulb_outline" )]
public class HighlightOutline : Component
{
	/// <summary>
	/// If defined, the glow will use this material rather than a generated one.
	/// </summary>
	[Property] public Material Material { get; set; }

	/// <summary>
	/// The colour of the glow outline
	/// </summary>
	[Property] public Color Color { get; set; } = Color.White;

	/// <summary>
	/// The colour of the glow when the mesh is obscured by something closer.
	/// </summary>
	[Property] public Color ObscuredColor { get; set; } = Color.Black * 0.4f;

	/// <summary>
	/// Color of the inside of the glow
	/// </summary>
	[Property] public Color InsideColor { get; set; } = Color.Transparent;

	/// <summary>
	/// Color of the inside of the glow when the mesh is obscured by something closer.
	/// </summary>
	[Property] public Color InsideObscuredColor { get; set; } = Color.Transparent;

	/// <summary>
	/// The width of the line of the glow
	/// </summary>
	[Property] public float Width { get; set; } = 0.25f;


	/// <summary>
	/// Specify targets of the outline manually
	/// </summary>
	[Property, FeatureEnabled( "Manual Targets" )] public bool OverrideTargets { get; set; } = false;

	/// <summary>
	/// Specify targets of the outline manually
	/// </summary>
	[Property, Feature( "Manual Targets" )] public List<Renderer> Targets { get; set; }

	/// <summary>
	/// Get a list of targets that we want to draw the outline around
	/// </summary>
	public IEnumerable<Renderer> GetOutlineTargets()
	{
		if ( OverrideTargets )
		{
			return Targets ?? Enumerable.Empty<Renderer>();
		}

		return Components.GetAll<Renderer>( FindMode.EnabledInSelfAndDescendants );
	}

	/// <summary>
	/// Append the same targets as <see cref="GetOutlineTargets()"/> to <paramref name="targets"/>.
	/// </summary>
	internal void GetOutlineTargets( List<Renderer> targets )
	{
		if ( OverrideTargets )
		{
			if ( Targets is not null )
				targets.AddRange( Targets );

			return;
		}

		Components.GetAll( targets, FindMode.EnabledInSelfAndDescendants );
	}
}
