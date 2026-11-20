namespace Sandbox;

/// <summary>
/// Shares generated builder models between every block with the same recipe.
/// Catalog pieces stay cached for the session. Custom sizes are leased by the blocks using them and
/// dropped once nothing has leased them for <see cref="IdleSeconds"/>, so undo/redo, stacking and
/// prewarmed dupes reuse a model instead of building it again.
/// </summary>
public static class BuilderModelCache
{
	const float IdleSeconds = 30f;

	sealed class Entry
	{
		public Model Model;
		public BuilderFace[] Faces;
		public int Leases;
		public RealTimeSince Idle;
	}

	static readonly Dictionary<BuilderShapeKey, Entry> Entries = new();
	static readonly List<BuilderShapeKey> Expired = new();
	static int _version = BuilderGeometry.GeometryVersion;

	/// <summary>
	/// Changes whenever the cache is thrown away, so a lease taken before can't be released against a new entry.
	/// </summary>
	public static int Generation { get; private set; }

	public static bool Contains( BuilderShapeKey key )
	{
		Validate();
		return Entries.ContainsKey( key );
	}

	/// <summary>The flat rectangular faces of a cached model, for snapping. False if it isn't built yet.</summary>
	public static bool TryGetFaces( BuilderShapeKey key, out BuilderFace[] faces )
	{
		Validate();
		faces = Entries.TryGetValue( key, out var entry ) ? entry.Faces : null;
		return faces is not null;
	}

	public static bool TryGet( BuilderShapeKey key, out Model model )
	{
		Validate();

		if ( Entries.TryGetValue( key, out var entry ) )
		{
			model = entry.Model;
			return true;
		}

		model = null;
		return false;
	}

	/// <summary>
	/// The model for <paramref name="key"/>, built now if it isn't cached. Prefer <see cref="BuilderBuildQueue"/>
	/// when many models may be needed at once.
	/// </summary>
	public static Model Get( BuilderShapeKey key ) => GetEntry( key ).Model;

	/// <summary>
	/// Like <see cref="Get"/>, but keeps the model cached until a matching <see cref="Release"/>.
	/// </summary>
	public static Model Acquire( BuilderShapeKey key )
	{
		var entry = GetEntry( key );
		entry.Leases++;
		return entry.Model;
	}

	/// <param name="key">The key passed to <see cref="Acquire"/>.</param>
	/// <param name="generation">The <see cref="Generation"/> when it was acquired.</param>
	public static void Release( BuilderShapeKey key, int generation )
	{
		Validate();
		if ( generation != Generation ) return;
		if ( !Entries.TryGetValue( key, out var entry ) || entry.Leases <= 0 ) return;

		if ( --entry.Leases == 0 )
			entry.Idle = 0;
	}

	/// <summary>
	/// Forget custom models nobody has leased for a while. Components still holding them keep them alive.
	/// </summary>
	public static void Sweep()
	{
		foreach ( var (key, entry) in Entries )
		{
			if ( key.Fit && entry.Leases == 0 && entry.Idle > IdleSeconds )
				Expired.Add( key );
		}

		foreach ( var key in Expired )
			Entries.Remove( key );

		Expired.Clear();
	}

	static Entry GetEntry( BuilderShapeKey key )
	{
		Validate();

		if ( Entries.TryGetValue( key, out var entry ) )
			return entry;

		var faces = new List<BuilderFace>();
		entry = new Entry { Model = BuilderGeometry.Create( key, true, faces ) ?? Model.Error, Faces = faces.ToArray(), Idle = 0 };
		Entries[key] = entry;
		return entry;
	}

	static void Validate()
	{
		if ( _version == BuilderGeometry.GeometryVersion ) return;

		Entries.Clear();
		_version = BuilderGeometry.GeometryVersion;
		Generation++;
	}
}
