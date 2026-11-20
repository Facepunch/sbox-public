namespace Sandbox;

/// <summary>Deterministic, bounded recipes shared by the menu, host, clients and saved games.</summary>
public static class BuilderCatalog
{
	public const float Unit = 96f;
	public const string DefaultMaterial = "materials/construction/tiles/generic_tiles.vmat";
	public static readonly string[] Categories = ["Cubes", "Plates", "Triangles", "Tubes", "Misc"];
	public enum Shape
	{
		[Title( "#builder.shape.box" )] Box,
		[Title( "#builder.shape.boxframe" )] BoxFrame,
		[Title( "#builder.shape.triangle" )] Triangle,
		[Title( "#builder.shape.carved" )] Carved,
		[Title( "#builder.shape.trapezium" )] Trapezium,
		[Title( "#builder.shape.tube" )] Tube,
		[Title( "#builder.shape.halfpipe" )] HalfPipe,
		[Title( "#builder.shape.ring" )] Ring,
		[Title( "#builder.shape.bend" )] Bend,
		[Title( "#builder.shape.platehole" )] PlateHole,
		[Title( "#builder.shape.cone" )] Cone,
		[Title( "#builder.shape.mirroredcone" )] MirroredCone,
		[Title( "#builder.shape.sphere" )] Sphere,
		[Title( "#builder.shape.dome" )] Dome,
		[Title( "#builder.shape.quarterdome" )] QuarterDome,
		[Title( "#builder.shape.stairs" )] Stairs,
		[Title( "#builder.shape.parallelogram" )] Parallelogram,
		[Title( "#builder.shape.pentagon" )] Pentagon
	}
	public sealed record Piece( string Id, string Category, Shape Kind, Vector3 Size, float Angle = 0 );
	public static IReadOnlyList<Piece> All { get; } = Generate();
	static readonly Dictionary<string, Piece> ById = All.ToDictionary( x => x.Id );
	public static Piece Find( string id ) => id is not null && ById.TryGetValue( id, out var piece ) ? piece : null;

	static List<Piece> Generate()
	{
		var pieces = new List<Piece>();
		string N( float n ) => n switch { 0.25f => "025", 0.5f => "05", _ => n.ToString( "0.###", System.Globalization.CultureInfo.InvariantCulture ) };
		void Add( string id, string category, Shape shape, float x, float y, float z, float angle = 0 )
			=> pieces.Add( new( id, category, shape, new Vector3( x, y, z ), angle ) );
		float[] sizes = [0.25f, 0.5f, 1, 2, 3, 4, 8];
		foreach ( var x in sizes )
		foreach ( var y in sizes.Where( y => y >= x ) )
		foreach ( var z in sizes.Where( z => z <= x ) )
			Add( $"{N(x)}x{N(y)}x{N(z)}", "Cubes", Shape.Box, x, y, z );
		foreach ( var x in sizes )
		foreach ( var y in sizes.Where( y => y >= x ) )
		foreach ( var z in new[] { 0.0625f, 0.125f } )
			Add( $"plate{N(x)}x{N(y)}{(z < 0.1f ? "a" : "b")}", "Plates", Shape.Box, x, y, z );
		foreach ( var s in sizes.Where( s => s >= 0.5f ) )
			Add( $"frame{N(s)}x{N(s)}x{N(s)}", "Cubes", Shape.BoxFrame, s, s, s );
		foreach ( var s in sizes )
		{
			Add( $"triangle{N(s)}x{N(s)}", "Triangles", Shape.Triangle, s, s, 0.25f );
			foreach ( var depth in new[] { 0.25f, 0.5f, 1f }.Where( d => d <= s ) )
			{
				Add( $"triangle{N(s)}x{N(s)}x{N(depth)}", "Triangles", Shape.Triangle, s, s, depth );
				Add( $"trapezium{N(s)}x{N(s)}x{N(depth)}", "Triangles", Shape.Trapezium, s, s, depth );
			}
			if ( s >= 1 ) Add( $"{N(s)}x{N(s)}x1carved025", "Triangles", Shape.Carved, s, s, 1 );
			foreach ( var length in new[] { 0.25f, 0.5f, 1, 2, 3, 4, 5, 8 } )
			{
				Add( $"tube{N(s)}x{N(s)}x{N(length)}", "Tubes", Shape.Tube, s, s, length );
				Add( $"tube{N(s)}x{N(s)}x{N(length)}c", "Tubes", Shape.HalfPipe, s, s, length );
			}
			Add( $"platehole{N(s)}x{N(s)}a", "Tubes", Shape.PlateHole, s, s, 0.125f );
			Add( $"circle{N(s)}x{N(s)}b", "Tubes", Shape.Ring, s, s, 0.125f );
			foreach ( var angle in new[] { 45, 90, 180 } )
				Add( $"tubebend{N(s)}x{N(s)}x{angle}", "Tubes", Shape.Bend, s, s, 1, angle );
			foreach ( var height in new[] { s / 2, s }.Where( h => h >= 0.25f ) )
			{
				Add( $"cone{N(s)}x{N(height)}", "Misc", Shape.Cone, s, s, height );
				Add( $"cone{N(s)}x{N(height)}mirrored", "Misc", Shape.MirroredCone, s, s, height );
			}
			Add( $"sphere{N(s)}x{N(s)}", "Misc", Shape.Sphere, s, s, s );
			Add( $"shell{N(s)}x{N(s)}a", "Misc", Shape.Dome, s, s, s / 2 );
			Add( $"shell{N(s)}x{N(s)}c", "Misc", Shape.QuarterDome, s, s, s / 2 );
			Add( $"stair{N(s)}x{N(s)}x{N(s)}", "Misc", Shape.Stairs, s, s, s );
			Add( $"para{N(s)}x{N(s)}x1", "Misc", Shape.Parallelogram, s, s, 1 );
			Add( $"pent{N(s)}x{N(s)}x1", "Misc", Shape.Pentagon, s, s, 1 );
		}
		return pieces;
	}
}
