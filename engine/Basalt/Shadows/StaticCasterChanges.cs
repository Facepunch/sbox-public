namespace Basalt.Shadows;

/// <summary>
/// Where a world's static shadow casters have changed: added, removed, moved, hidden, made dynamic or changed in how they
/// cast. A light's static shadow cache is redrawn when a change since it was drawn reaches the light, and kept otherwise,
/// so a change on one side of a map leaves the other side's caches alone.
/// </summary>
internal sealed class StaticCasterChanges
{
	/// <summary>
	/// The most recent changes kept. A cache older than all of them is redrawn.
	/// </summary>
	const int Capacity = 256;

	readonly Vector3[] mins = new Vector3[Capacity];
	readonly Vector3[] maxs = new Vector3[Capacity];

	/// <summary>
	/// How many changes there have been.
	/// </summary>
	public long Revision { get; private set; }

	/// <summary>
	/// Note a change within a box.
	/// </summary>
	public void Add( Vector3 center, Vector3 extents )
	{
		var slot = (int)(Revision % Capacity);
		mins[slot] = center - extents;
		maxs[slot] = center + extents;
		Revision++;
	}

	/// <summary>
	/// Whether any change after revision <paramref name="since"/> touches a sphere.
	/// </summary>
	public bool Touches( long since, Vector3 center, float radius )
	{
		if ( Revision - since > Capacity ) return true;

		var radiusSquared = radius * radius;
		for ( var revision = since; revision < Revision; revision++ )
		{
			var slot = (int)(revision % Capacity);
			if ( center.Clamp( mins[slot], maxs[slot] ).DistanceSquared( center ) <= radiusSquared ) return true;
		}

		return false;
	}

	/// <summary>
	/// The boxes of the changes after revision <paramref name="since"/>, or false when some of them are no longer kept.
	/// </summary>
	public bool ChangesSince( long since, List<(Vector3 Min, Vector3 Max)> boxes )
	{
		boxes.Clear();
		if ( Revision - since > Capacity ) return false;

		for ( var revision = since; revision < Revision; revision++ )
		{
			var slot = (int)(revision % Capacity);
			boxes.Add( (mins[slot], maxs[slot]) );
		}

		return true;
	}
}
