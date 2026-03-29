namespace Sandbox;
/// <summary>
/// Entity Tags are strings you can set and check for on any entity. Internally
/// these strings are tokenized and networked so they're also available clientside.
/// </summary>
[Expose, ActionGraphIgnore]
public class GameTags : ITagSet
{
	// Lazily allocated: most GameObjects never have tags, so don't pay for two HashSets upfront.
	private HashSet<uint> _lazyTokens;
	private HashSet<string> _lazyTags;

	// Cache all our tokens and ancestors when dirty.
	HashSet<uint> _allTokens;
	bool _tokensDirty = true;
	GameObject _cachedParent;

	private HashSet<uint> _tokens => _lazyTokens ??= new();
	private HashSet<string> _tags => _lazyTags ??= new HashSet<string>( StringComparer.OrdinalIgnoreCase );

	private static readonly IReadOnlySet<uint> EmptyTokens = System.Collections.Frozen.FrozenSet<uint>.Empty;

	GameObject target;

	internal GameTags( GameObject target )
	{
		this.target = target;
	}

	/// <summary>
	/// Returns all the tags this object has.
	/// </summary>
	public override IEnumerable<string> TryGetAll()
	{
		if ( target.Parent is null || target.Parent is Scene )
			return _lazyTags ?? Enumerable.Empty<string>();

		// Avoid allocating an empty HashSet when _lazyTags is null – just return the parent chain directly.
		if ( _lazyTags is null )
			return target.Parent.Tags.TryGetAll();

		return _lazyTags.Concat( target.Parent.Tags.TryGetAll() ).Distinct();
	}

	/// <summary>
	/// Returns all the tags this object has.
	/// </summary>
	[Pure, ActionGraphInclude]
	public IEnumerable<string> TryGetAll( bool includeAncestors )
	{
		if ( !includeAncestors ) return _lazyTags ?? Enumerable.Empty<string>();
		return TryGetAll();
	}

	/// <summary>
	/// Returns true if this object (or its parents) has given tag.
	/// </summary>
	public override bool Has( string tag )
	{
		if ( _lazyTags?.Contains( tag ) == true )
			return true;

		return target.Parent?.Tags.Has( tag ) ?? false;
	}

	/// <summary>
	/// Returns true if this object has given tag.
	/// </summary>
	[Pure, ActionGraphInclude]
	public bool Has( string tag, bool includeAncestors )
	{
		if ( !includeAncestors ) return _lazyTags?.Contains( tag ) ?? false;
		return Has( tag );
	}

	/// <summary>
	/// Returns true if this object has one or more tags from given tag list.
	/// </summary>
	public bool HasAny( HashSet<string> tagList )
	{
		return tagList.Any( Has );
	}

	bool AddSingle( string tag )
	{
		if ( string.IsNullOrWhiteSpace( tag ) ) return false;
		if ( Has( tag ) ) return false;

		tag = tag.ToLowerInvariant();

		if ( !tag.IsValidTag() )
		{
			Log.Warning( $"Ignoring tag '{tag}' - invalid" );
			return false;
		}

		_tokens.Add( StringToken.FindOrCreate( tag ) );

		return _tags.Add( tag );
	}

	/// <summary>
	/// Try to add the tag to this object.
	/// </summary>
	public override void Add( string tag )
	{
		if ( AddSingle( tag ) )
		{
			MarkDirty();
		}
	}

	/// <summary>
	/// Adds multiple tags. Calls <see cref="Add(string)">EntityTags.Add</see> for each tag.
	/// </summary>
	[ActionGraphInclude]
	public void Add( params string[] tags )
	{
		if ( tags == null || tags.Length == 0 )
			return;

		bool changes = false;

		foreach ( var tag in tags )
		{
			changes = AddSingle( tag ) || changes;
		}

		if ( changes )
		{
			MarkDirty();
		}
	}

	/// <summary>
	/// Try to remove the tag from this entity.
	/// </summary>
	[ActionGraphInclude]
	public override void Remove( string tag )
	{
		if ( _lazyTags is null || !_lazyTags.Remove( tag ) )
			return;

		_lazyTokens?.Remove( StringToken.FindOrCreate( tag ) );

		MarkDirty();
	}

	/// <summary>
	/// Remove all tags
	/// </summary>
	[ActionGraphInclude]
	public override void RemoveAll()
	{
		_lazyTokens?.Clear();
		_lazyTags?.Clear();

		MarkDirty();
	}

	internal void SetAll( string tags )
	{
		RemoveAll();
		Add( tags.SplitQuotesStrings() );
	}

	internal void CloneFrom( GameTags source )
	{
		if ( source is null || source == this ) return;

		var sourceOwn = source._lazyTags;
		if ( _lazyTags is null && sourceOwn is null ) return;

		_lazyTokens?.Clear();
		_lazyTags?.Clear();

		if ( sourceOwn is not null )
		{
			foreach ( var t in sourceOwn )
				AddSingle( t );
		}

		MarkDirty();
	}

	void MarkDirty()
	{
		if ( !target.IsValid )
			return;

		_tokensDirty = true;
		_allTokens = null;

		target.OnTagsUpdatedInternal();

		foreach ( var c in target.Children )
		{
			c.Tags.MarkDirty();
		}
	}

	[System.Obsolete( "No need to call this now, tags are set immediately" )]
	public void Flush()
	{

	}

	/// <summary>
	/// Returns a list of ints, representing the tags. These are used internally by the engine.
	/// </summary>
	public override IReadOnlySet<uint> GetTokens()
	{
		var parent = target.Parent;

		if ( parent is null || parent is Scene )
		{
			if ( !_tokensDirty && _allTokens != null )
				return _allTokens;

			_allTokens = _lazyTokens;
			_tokensDirty = false;

			return _lazyTokens ?? EmptyTokens;
		}

		if ( parent != _cachedParent )
		{
			_cachedParent = parent;
			_tokensDirty = true;
		}

		if ( !_tokensDirty && _allTokens is not null )
			return _allTokens;

		var parentTokens = parent.Tags.GetTokens();

		if ( _lazyTokens is null || _lazyTokens.Count == 0 )
		{
			_tokensDirty = false;
			return parentTokens;
		}

		if ( parentTokens.Count == 0 )
		{
			_allTokens = _lazyTokens;
			_tokensDirty = false;
			return _lazyTokens;
		}

		// Build into a local first so _allTokens is never visible to other threads in a
		// partially-constructed state (a single reference write is atomic on all CLR platforms).
		var merged = new HashSet<uint>( parentTokens );
		merged.UnionWith( _lazyTokens );
		_allTokens = merged;

		_tokensDirty = false;
		return merged;
	}

	/// <summary>
	/// Get all potential suggested tags that someone might want to add to this set.
	/// </summary>
	public override IEnumerable<string> GetSuggested()
	{
		var collisionTags = ProjectSettings.Collision?.Tags ?? Enumerable.Empty<string>();
		var sceneTags = target.IsValid()
			? target.Scene.GetAllObjects( true )
				.SelectMany( x => x.Tags.TryGetAll() ?? Enumerable.Empty<string>() )
			: Enumerable.Empty<string>();

		return collisionTags
			.Concat( sceneTags )
			.Distinct();
	}
}
