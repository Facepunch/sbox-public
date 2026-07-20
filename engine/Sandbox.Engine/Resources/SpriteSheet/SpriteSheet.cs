using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// A source image plus the named rectangles it has been cut into. Each slice carries its own pivot,
/// which is what lets a sheet of body parts become a rig - a thigh pivots at the hip, a calf at the
/// knee.
/// <para>
/// The sheet is the editable record of how an image was cut. It does not render directly: slices
/// reach the renderer as ordinary textures through <c>SpriteSheetSliceGenerator</c>, so re-slicing
/// updates everything already using it.
/// </para>
/// </summary>
// "sheet" rather than "spritesheet": resource extensions have to be at most 8 letters, and the
// native asset compiler quietly produces nothing for one that is longer.
[AssetType( Name = "Sprite Sheet", Extension = "sheet", Category = "Rendering" )]
public sealed partial class SpriteSheet : GameResource
{
	/// <summary>
	/// The image being cut up, relative to the project's assets.
	/// </summary>
	[Property, TextureImagePath]
	public string ImagePath { get; set; }

	/// <summary>
	/// Treat a solid background colour as empty space.
	/// </summary>
	/// <remarks>
	/// Artwork does not always arrive with an alpha channel - a JPEG never has one, and scans and
	/// exports from paint packages are routinely flat white behind the drawing. Without this, every
	/// pixel counts as artwork, automatic slicing finds one island covering the whole image, and every
	/// part renders as a rectangle of background.
	/// </remarks>
	[Property, ToggleGroup( "KeyBackground", Label = "Background Colour" )]
	public bool KeyBackground { get; set; }

	/// <summary>
	/// The colour to treat as empty. <see cref="DetectBackgroundColor"/> can guess it.
	/// </summary>
	[Property, Group( "KeyBackground" ), Title( "Colour" )]
	public Color BackgroundColor { get; set; } = Color.White;

	/// <summary>
	/// How far a pixel may stray from the background colour and still count as background. Needs to
	/// be above zero for anything that has been through JPEG, which smears colours near edges.
	/// </summary>
	[Property, Group( "KeyBackground" ), Title( "Tolerance" ), Range( 0, 1 )]
	public float BackgroundTolerance { get; set; } = 0.08f;

	/// <summary>
	/// Every rectangle this sheet has been cut into.
	/// </summary>
	[Property, InlineEditor, WideMode]
	public List<Slice> Slices { get; set; } = new();

	/// <summary>
	/// How to tell artwork from empty space on this sheet, in a form the slicing and texture code can
	/// use without knowing about the resource.
	/// </summary>
	[Hide, System.Text.Json.Serialization.JsonIgnore]
	public BackgroundKey Background => new()
	{
		Enabled = KeyBackground,
		Color = new Color32(
			(byte)(BackgroundColor.r * 255f),
			(byte)(BackgroundColor.g * 255f),
			(byte)(BackgroundColor.b * 255f),
			255 ),
		Tolerance = (byte)(BackgroundTolerance.Clamp( 0f, 1f ) * 255f)
	};

	/// <summary>
	/// Which pixels count as empty space rather than artwork.
	/// </summary>
	public readonly struct BackgroundKey
	{
		public bool Enabled { get; init; }
		public Color32 Color { get; init; }
		public byte Tolerance { get; init; }

		/// <summary>
		/// Whether a pixel is background - either see-through, or near enough the keyed colour.
		/// </summary>
		/// <param name="pixel">The pixel to test</param>
		/// <param name="alphaTolerance">Alpha at or below this is see-through regardless of colour</param>
		public bool IsEmpty( Color32 pixel, byte alphaTolerance )
		{
			if ( pixel.a <= alphaTolerance ) return true;
			if ( !Enabled ) return false;

			// Per-channel rather than euclidean: cheap, and it behaves predictably when someone drags
			// the tolerance slider looking for the point where the drawing stops disappearing.
			return Math.Abs( pixel.r - Color.r ) <= Tolerance
				&& Math.Abs( pixel.g - Color.g ) <= Tolerance
				&& Math.Abs( pixel.b - Color.b ) <= Tolerance;
		}
	}

	/// <summary>
	/// Guess the background colour by looking at the corners, which is where artwork almost never is.
	/// Returns null if the corners disagree, rather than guessing wrongly.
	/// </summary>
	/// <param name="pixels">The source image, row-major from the top-left</param>
	/// <param name="width">Source image width in pixels</param>
	/// <param name="height">Source image height in pixels</param>
	public static Color? DetectBackgroundColor( Color32[] pixels, int width, int height )
	{
		if ( pixels is null || width < 2 || height < 2 ) return null;

		// Inset a little - a stray dark edge pixel from a scan or a border would otherwise decide it.
		var inset = Math.Max( 1, Math.Min( width, height ) / 64 );

		var corners = new[]
		{
			Sample( inset, inset ),
			Sample( width - 1 - inset, inset ),
			Sample( inset, height - 1 - inset ),
			Sample( width - 1 - inset, height - 1 - inset )
		};

		Color32 Sample( int x, int y )
		{
			var index = (Math.Clamp( y, 0, height - 1 ) * width) + Math.Clamp( x, 0, width - 1 );
			return index >= 0 && index < pixels.Length ? pixels[index] : default;
		}

		var first = corners[0];

		foreach ( var corner in corners )
		{
			// Generous, because JPEG will not give four identical corners even on flat white.
			if ( Math.Abs( corner.r - first.r ) > 12 ) return null;
			if ( Math.Abs( corner.g - first.g ) > 12 ) return null;
			if ( Math.Abs( corner.b - first.b ) > 12 ) return null;
		}

		// Already see-through: there is nothing to key, alpha is doing the job.
		if ( first.a == 0 ) return null;

		return new Color( first.r / 255f, first.g / 255f, first.b / 255f );
	}

	/// <summary>
	/// Get a slice by its stable id. This is how anything holding a reference to a slice should look
	/// it up - names change, ids do not. Returns null if not found.
	/// </summary>
	/// <param name="id">The id of the slice</param>
	public Slice GetSlice( Guid id )
	{
		if ( id == Guid.Empty ) return null;

		foreach ( var slice in Slices )
		{
			if ( slice.Id == id ) return slice;
		}
		return null;
	}

	/// <summary>
	/// Get the index of a slice by name. Returns -1 if not found.
	/// </summary>
	/// <param name="name">The name of the slice</param>
	public int GetSliceIndex( string name )
	{
		for ( int i = 0; i < Slices.Count; i++ )
		{
			if ( string.Equals( Slices[i].Name, name, StringComparison.OrdinalIgnoreCase ) )
			{
				return i;
			}
		}
		return -1; // Not found
	}

	/// <summary>
	/// Get a slice by name. Returns null if not found.
	/// </summary>
	/// <param name="name">The name of the slice</param>
	public Slice GetSlice( string name )
	{
		int index = GetSliceIndex( name );
		if ( index == -1 )
		{
			return null;
		}
		return Slices[index];
	}

	/// <summary>
	/// Makes <paramref name="name"/> unique among the existing slices by appending a number, leaving
	/// it alone if nothing else is using it. Slices are addressed by name, so two sharing one would
	/// make the second unreachable.
	/// </summary>
	/// <param name="name">The desired name</param>
	/// <param name="ignore">A slice allowed to keep the name - pass the one being renamed</param>
	public string GetUniqueName( string name, Slice ignore = null )
	{
		if ( string.IsNullOrWhiteSpace( name ) ) name = "slice";

		bool Taken( string candidate )
		{
			foreach ( var slice in Slices )
			{
				if ( slice == ignore ) continue;
				if ( string.Equals( slice.Name, candidate, StringComparison.OrdinalIgnoreCase ) ) return true;
			}
			return false;
		}

		if ( !Taken( name ) ) return name;

		for ( int i = 1; ; i++ )
		{
			var candidate = $"{name}_{i}";
			if ( !Taken( candidate ) ) return candidate;
		}
	}

	/// <summary>
	/// The slices hanging directly off <paramref name="parentId"/>, in sheet order.
	/// Pass <see cref="Guid.Empty"/> for the roots.
	/// </summary>
	/// <param name="parentId">The parent to list the children of</param>
	public IEnumerable<Slice> GetChildren( Guid parentId )
		=> Slices.Where( s => s.ParentId == parentId );

	/// <summary>
	/// Point one slice at another as its parent, refusing anything that would make a loop.
	/// </summary>
	/// <remarks>
	/// A loop is not a hypothetical: the parent list is a set of independent dropdowns, so making a
	/// thigh the child of its own calf takes two ordinary-looking clicks. Building that would recurse
	/// until the stack ran out.
	/// </remarks>
	/// <param name="slice">The slice to reparent</param>
	/// <param name="parentId">The proposed parent, or <see cref="Guid.Empty"/> for none</param>
	/// <returns>True if the parent was set</returns>
	public bool TrySetParent( Slice slice, Guid parentId )
	{
		if ( slice is null ) return false;

		if ( parentId == Guid.Empty )
		{
			slice.ParentId = Guid.Empty;
			return true;
		}

		if ( parentId == slice.Id ) return false;

		// Walk up from the proposed parent; if we meet this slice, the link would close a loop.
		var seen = 0;
		var walk = GetSlice( parentId );

		while ( walk is not null )
		{
			if ( walk == slice ) return false;
			if ( ++seen > Slices.Count ) return false; // already looped somehow - refuse rather than hang

			walk = walk.ParentId == Guid.Empty ? null : GetSlice( walk.ParentId );
		}

		slice.ParentId = parentId;
		return true;
	}

	/// <summary>
	/// Slices in the order a rig should be built: parents before their children, so a child always
	/// has something to attach to.
	/// </summary>
	public List<Slice> InHierarchyOrder()
	{
		var ordered = new List<Slice>( Slices.Count );

		void Walk( Guid parentId )
		{
			foreach ( var slice in GetChildren( parentId ) )
			{
				ordered.Add( slice );
				Walk( slice.Id );
			}
		}

		Walk( Guid.Empty );

		// Anything left over pointed at a parent that has since been deleted. Treat it as a root
		// rather than dropping it silently.
		foreach ( var slice in Slices )
		{
			if ( !ordered.Contains( slice ) ) ordered.Add( slice );
		}

		return ordered;
	}

	protected override Bitmap CreateAssetTypeIcon( int width, int height )
	{
		var svg = "<svg viewBox=\"0 0 24 24\" xmlns=\"http://www.w3.org/2000/svg\"><g fill=\"none\" stroke=\"#8fe637\" stroke-width=\"1.6\" stroke-linejoin=\"round\"><rect x=\"2.5\" y=\"2.5\" width=\"19\" height=\"19\" rx=\"1.5\"/><path d=\"M8.833 2.5v19M15.167 2.5v19M2.5 8.833h19M2.5 15.167h19\"/></g><rect x=\"9.4\" y=\"9.4\" width=\"5.2\" height=\"5.2\" fill=\"#8fe637\"/></svg>";
		return Bitmap.CreateFromSvgString( svg, width, height );
	}
}
