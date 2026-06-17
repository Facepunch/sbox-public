namespace Editor;

/// <summary>
/// Implemented by any TreeView that owns an ActiveFilterSet.
/// FacetValueNode resolves its filters through this interface at paint/click time.
/// </summary>
public interface IFilterHost
{
	ActiveFilterSet ActiveFilters { get; }
}

/// <summary>
/// Tracks multi-select facet filter state independently of context navigation.
/// Supports inclusion (category:nature) and exclusion (-category:nature).
/// Compose with BaseQuery to build the full search query.
/// </summary>
public class ActiveFilterSet
{
	record Entry( string FacetName, string Value, bool Excluded );

	readonly List<Entry> _entries = new();

	/// <summary>
	/// Fired when the set changes. Wire this to UpdateAssetList.
	/// Not fired by Clear() — callers that clear on context change drive the update themselves.
	/// </summary>
	public Action OnChanged;

	/// <summary>
	/// Toggle an entry. Clicking the same value again removes it.
	/// Clicking with a different excluded state flips the mode rather than stacking.
	/// </summary>
	public void Toggle( string facetName, string value, bool excluded = false )
	{
		var existing = _entries.FirstOrDefault( x => x.FacetName == facetName && x.Value == value );

		if ( existing != null )
		{
			_entries.Remove( existing );
			if ( existing.Excluded != excluded )
				_entries.Add( existing with { Excluded = excluded } );
		}
		else
		{
			_entries.Add( new( facetName, value, excluded ) );
		}

		OnChanged?.Invoke();
	}

	/// <summary>
	/// Remove all entries without firing OnChanged.
	/// Callers are expected to trigger their own update (e.g. UpdateAssetList) after clearing.
	/// </summary>
	public void Clear() => _entries.Clear();

	/// <summary>
	/// Remove all entries for a single facet dimension without firing OnChanged.
	/// Used by the filter bar's ✕ button — the caller fires the update once after.
	/// </summary>
	public void ClearFacet( string facetName ) => _entries.RemoveAll( x => x.FacetName == facetName );

	public bool IsEmpty => _entries.Count == 0;
	public bool IsActive( string facetName, string value ) => _entries.Any( x => x.FacetName == facetName && x.Value == value );
	public bool IsIncluded( string facetName, string value ) => _entries.Any( x => x.FacetName == facetName && x.Value == value && !x.Excluded );
	public bool IsExcluded( string facetName, string value ) => _entries.Any( x => x.FacetName == facetName && x.Value == value && x.Excluded );

	/// <summary>Builds the query fragment, e.g. "category:nature -rating:poor size:small"</summary>
	public string ToQueryString() =>
		string.Join( " ", _entries.Select( e => e.Excluded ? $"-{e.FacetName}:{e.Value}" : $"{e.FacetName}:{e.Value}" ) );
}
