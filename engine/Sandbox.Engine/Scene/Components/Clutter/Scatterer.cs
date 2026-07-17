namespace Sandbox.Clutter;

/// <summary>
/// Represents a single clutter instance to be spawned.
/// </summary>
public struct ClutterInstance
{
	public Transform Transform { get; set; }
	public ClutterEntry Entry { get; set; }
	public readonly bool IsModel => Entry is { Model: not null, Prefab: null };
}

/// <summary>
/// Base class to override if you want to create custom scatterer logic.
/// Provides utility methods for entry selection and common operations.
/// </summary>
[Expose]
public abstract class Scatterer
{
	[Hide]
	protected Random Random { get; private set; }

	/// <summary>
	/// Generates clutter instances for the given bounds.
	/// The Random property is initialized before this is called.
	/// </summary>
	/// <param name="bounds">World-space bounds to scatter within</param>
	/// <param name="clutter">The clutter containing objects to scatter</param>
	/// <param name="scene">Scene to use for tracing (null falls back to Game.ActiveScene)</param>
	/// <returns>Collection of clutter instances to spawn</returns>
	protected abstract List<ClutterInstance> Generate( BBox bounds, ClutterDefinition clutter, Scene scene = null );

	/// <summary>
	/// Public entry point for scattering. Creates Random from seed and calls Generate().
	/// </summary>
	/// <param name="bounds">World-space bounds to scatter within</param>
	/// <param name="clutter">The clutter containing objects to scatter</param>
	/// <param name="seed">Seed for deterministic random generation</param>
	/// <param name="scene">Scene to use for tracing (required in editor mode)</param>
	/// <returns>Collection of clutter instances to spawn</returns>
	public List<ClutterInstance> Scatter( BBox bounds, ClutterDefinition clutter, int seed, Scene scene = null )
	{
		Random = new Random( seed );

		return Generate( bounds, clutter, scene );
	}

	/// <summary>
	/// Generates a hash from all serializable fields and properties using TypeLibrary.
	/// Override this if you need custom hash generation logic.
	/// </summary>
	public override int GetHashCode()
	{
		HashCode hash = new();
		var typeDesc = Game.TypeLibrary.GetType( GetType() );

		if ( typeDesc == null )
			return base.GetHashCode();

		hash.Add( GetType().Name );

		foreach ( var property in typeDesc.Properties )
		{
			if ( !property.HasAttribute<PropertyAttribute>() )
				continue;

			var value = property.GetValue( this );
			HashValue( ref hash, value );
		}

		return hash.ToHashCode();
	}

	private static void HashValue( ref HashCode hash, object value )
	{
		if ( value == null )
		{
			hash.Add( 0 );
			return;
		}

		if ( value is System.Collections.IEnumerable enumerable && value is not string )
		{
			foreach ( var item in enumerable )
			{
				HashValue( ref hash, item );
			}
			return;
		}

		hash.Add( value.GetHashCode() );
	}

	/// <summary>
	/// Selects a random entry from the clutter based on weights.
	/// Returns null if no valid entries exist.
	/// </summary>
	protected ClutterEntry GetRandomEntry( ClutterDefinition clutter )
	{
		if ( clutter.IsEmpty )
			return null;

		var totalWeight = 0f;
		foreach ( var entry in clutter.Entries )
		{
			if ( entry?.HasAsset is true && entry.Weight > 0 )
				totalWeight += entry.Weight;
		}

		if ( totalWeight is 0 ) return null;

		var randomValue = Random.Float( 0f, totalWeight );
		var currentWeight = 0f;

		foreach ( var entry in clutter.Entries )
		{
			if ( entry?.HasAsset is not true || entry.Weight <= 0 )
				continue;

			currentWeight += entry.Weight;
			if ( randomValue <= currentWeight )
				return entry;
		}

		return null;
	}

	/// <summary>
	/// Creates a rotation aligned to a surface normal with random yaw.
	/// </summary>
	protected static Rotation GetAlignedRotation( Vector3 normal, float yawDegrees )
	{
		var alignToSurface = Rotation.FromToRotation( Vector3.Up, normal );
		var yawRotation = Rotation.FromAxis( normal, yawDegrees );
		return yawRotation * alignToSurface;
	}

	/// <summary>
	/// Helper to perform a ground trace at a position.
	/// </summary>
	protected static SceneTraceResult TraceGround( Scene scene, Vector3 position, BBox sceneBounds )
	{
		var traceStart = position.WithZ( sceneBounds.Maxs.z );
		var traceEnd = position.WithZ( sceneBounds.Mins.z );

		return scene.Trace
			.Ray( traceStart, traceEnd )
			.WithoutTags( "player", "trigger", "clutter" )
			.Run();
	}

	/// <summary>
	/// Traces the ground at multiple positions at once.
	/// </summary>
	protected static SceneTraceResult[] BatchTraceGround( Scene scene, IReadOnlyList<Vector3> positions, BBox sceneBounds )
	{
		var results = new SceneTraceResult[positions.Count];
		var physicsWorld = scene.PhysicsWorld;

		// even though this is on the main thread, it's safe to do since nothing will change 
		// the physics world between now and when this completes
		Parallel.For( 0, positions.Count, i =>
		{
			var position = positions[i];
			var traceStart = position.WithZ( sceneBounds.Maxs.z );
			var traceEnd = position.WithZ( sceneBounds.Mins.z );

			var physicsResult = physicsWorld.Trace
				.Ray( traceStart, traceEnd )
				.WithoutTags( "player", "trigger", "clutter" )
				.Run();

			results[i] = SceneTraceResult.From( scene, physicsResult );
		} );

		return results;
	}

	/// <summary>
	/// Generates a deterministic seed from tile coordinates and base seed.
	/// Use this to create unique seeds for different tiles.
	/// </summary>
	public static int GenerateSeed( int baseSeed, int x, int y )
	{
		int seed = baseSeed;
		seed = (seed * 397) ^ x;
		seed = (seed * 397) ^ y;
		return seed;
	}

	/// <summary>
	/// Grid dimensions for roughly pointCount jittered points across bounds. Use JitteredGridPoints
	/// unless you need the raw cell counts.
	/// </summary>
	protected static void GetJitteredGridSize( BBox bounds, int pointCount, out int cellsX, out int cellsY )
	{
		if ( pointCount <= 0 )
		{
			cellsX = 0;
			cellsY = 0;
			return;
		}

		var aspect = MathF.Max( bounds.Size.x, 0.0001f ) / MathF.Max( bounds.Size.y, 0.0001f );
		cellsY = Math.Max( 1, (int)MathF.Round( MathF.Sqrt( pointCount / aspect ) ) );
		cellsX = Math.Max( 1, (int)MathF.Round( pointCount / (float)cellsY ) );
	}

	/// <summary>
	/// One jittered point per grid cell across bounds, roughly pointCount total. Even coverage,
	/// no clumping or gaps. Z is always 0.
	/// </summary>
	protected Vector3[] JitteredGridPoints( BBox bounds, int pointCount )
	{
		GetJitteredGridSize( bounds, pointCount, out int cellsX, out int cellsY );
		if ( cellsX <= 0 || cellsY <= 0 )
			return [];

		var cellWidth = bounds.Size.x / cellsX;
		var cellHeight = bounds.Size.y / cellsY;

		var points = new Vector3[cellsX * cellsY];
		var index = 0;

		for ( int cy = 0; cy < cellsY; cy++ )
			for ( int cx = 0; cx < cellsX; cx++ )
			{
				points[index++] = new Vector3(
					bounds.Mins.x + cx * cellWidth + Random.Float( cellWidth ),
					bounds.Mins.y + cy * cellHeight + Random.Float( cellHeight ),
					0f
				);
			}

		return points;
	}

	/// <summary>
	/// Calculates the number of points to scatter based on density and area.
	/// Caps at maxPoints to prevent engine freezing.
	/// </summary>
	/// <param name="bounds">Bounds to scatter in</param>
	/// <param name="density">Points per square meter</param>
	/// <param name="maxPoints">Maximum points to cap at (default 10000)</param>
	/// <returns>Number of points to generate</returns>
	protected int CalculatePointCount( BBox bounds, float density, int maxPoints = 10000 )
	{
		// Convert bounds from engine units (inches) to meters
		// 1 inch = 0.0254 meters
		var widthMeters = bounds.Size.x.InchToMeter();
		var depthMeters = bounds.Size.y.InchToMeter();
		var areaSquareMeters = widthMeters * depthMeters;

		var desiredCount = areaSquareMeters * density / 10f;

		// Handle fractional points probabilistically
		// 1.3 points = 1 guaranteed + 30% chance of 1 more
		var guaranteedPoints = (int)desiredCount;
		var fractionalPart = desiredCount - guaranteedPoints;

		var finalCount = guaranteedPoints;
		if ( Random.Float( 0f, 1f ) < fractionalPart )
		{
			finalCount++;
		}

		var clampedCount = Math.Clamp( finalCount, 0, maxPoints );

		if ( desiredCount > maxPoints )
		{
			Log.Warning( $"Scatterer: Density would generate {desiredCount:F0} points, capped to {maxPoints} to prevent freezing." );
		}

		return clampedCount;
	}
}
