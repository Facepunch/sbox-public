using System.Collections;
using System.Collections.Frozen;

namespace Sandbox;

// TODo - rename this now it's not an interface (BaseTagSet?), or maybe make everything just derive from TagSet?
public abstract class ITagSet : IEnumerable<string>
{
	/// <summary>
	/// Remove all tags from the set.
	/// </summary>
	public abstract void RemoveAll();

	/// <summary>
	/// Does this set have the specified tag?
	/// </summary>
	/// <param name="tag"></param>
	public abstract bool Has( string tag );

	/// <summary>
	/// Add a tag to the set.
	/// </summary>
	/// <param name="tag"></param>
	public abstract void Add( string tag );

	/// <summary>
	/// Remove a tag from the set.
	/// </summary>
	/// <param name="tag"></param>
	public abstract void Remove( string tag );

	/// <summary>
	/// Add or remove this tag, based on state
	/// </summary>
	public virtual void Set( string tag, bool state )
	{
		if ( state ) Add( tag );
		else Remove( tag );
	}

	/// <summary>
	/// Try to get all tags in the set.
	/// </summary>
	public abstract IEnumerable<string> TryGetAll();

	/// <summary>
	/// Try to get all tags in the set.
	/// </summary>
	public virtual IReadOnlySet<uint> GetTokens() => TryGetAll().Select( x => StringToken.FindOrCreate( x ) ).Distinct().ToFrozenSet();

	/// <summary>
	/// Get all default tags for this set.
	/// </summary>
	public virtual IEnumerable<string> GetSuggested() => Enumerable.Empty<string>();

	/// <summary>
	/// Set the tags to match this other tag set
	/// </summary>
	public virtual void SetFrom( ITagSet set )
	{
		if ( set is null )
			return;

		if ( set == this )
			return;

		//
		// Remove missing tags
		//
		List<string> toRemove = null;
		foreach ( var t in TryGetAll() )
		{
			if ( !set.Has( t ) )
			{
				(toRemove ??= []).Add( t );
			}
		}

		if ( toRemove is not null )
		{
			foreach ( var t in toRemove )
			{
				Remove( t );
			}
		}

		//
		// add new tags
		//
		foreach ( var t in set.TryGetAll() )
		{
			if ( Has( t ) ) continue;
			Add( t );
		}

	}

	/// <summary>
	/// Add the tags from another set, to this set
	/// </summary>
	public virtual void Add( ITagSet set )
	{
		if ( set is null )
			return;

		if ( set == this )
			return;

		foreach ( var t in set.TryGetAll() )
		{
			if ( Has( t ) ) continue;
			Add( t );
		}

	}

	/// <summary>
	/// If this tag is already here, remove it, else add it.
	/// </summary>
	public virtual void Toggle( string tag )
	{
		Set( tag, !Has( tag ) );
	}

	/// <summary>
	/// Does this set have any of the specified tag?
	/// </summary>
	public virtual bool HasAny( IEnumerable<string> tags )
	{
		foreach ( var tag in tags )
		{
			if ( Has( tag ) ) return true;
		}

		return false;
	}

	/// <inheritdoc cref="HasAny( IEnumerable{string} )"/>
	public virtual bool HasAny( ITagSet other ) => HasAny( other.TryGetAll() );

	/// <inheritdoc cref="HasAny( IEnumerable{string} )"/>
	public virtual bool HasAny( params string[] tags ) => HasAny( tags.AsEnumerable() );

	/// <summary>
	/// Does this set have all of the specified tags?
	/// </summary>
	public virtual bool HasAll( IEnumerable<string> tags )
	{
		foreach ( var tag in tags )
		{
			if ( !Has( tag ) ) return false;
		}

		return true;
	}

	/// <inheritdoc cref="HasAll( IEnumerable{string} )"/>
	public virtual bool HasAll( ITagSet other ) => HasAll( other.TryGetAll() );

	/// <inheritdoc cref="HasAll( ITagSet )"/>
	[Pure, ActionGraphInclude]
	public virtual bool HasAll( params string[] tags ) => HasAll( tags.AsEnumerable() );

	/// <inheritdoc />
	public IEnumerator<string> GetEnumerator()
	{
		return TryGetAll().GetEnumerator();
	}

	/// <inheritdoc />
	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}

	public override string ToString()
	{
		return string.Join( ", ", TryGetAll() );
	}
}
