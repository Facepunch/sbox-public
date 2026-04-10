namespace Sandbox;

public partial class Doo
{
	/// <summary>
	/// Find and delete this block from the Doo tree.
	/// </summary>
	public bool DeleteBlock( Block value )
	{
		return RemoveFromList( Body, value );
	}

	/// <summary>
	/// Remove a block from a list, searching recursively.
	/// </summary>
	bool RemoveFromList( List<Block> list, Block value )
	{
		if ( list == null ) return false;

		for ( int i = 0; i < list.Count; i++ )
		{
			if ( list[i] == value )
			{
				list.RemoveAt( i );
				return true;
			}

			// Search in nested blocks
			if ( RemoveFromList( list[i].Body, value ) )
				return true;
		}

		return false;
	}

	/// <summary>
	/// Insert a block before the target block.
	/// </summary>
	public bool InsertBefore( Block target, Block blockToInsert )
	{
		// Remove from current location first
		DeleteBlock( blockToInsert );

		return InsertRelative( Body, target, blockToInsert, 0 );
	}

	/// <summary>
	/// Insert a block after the target block.
	/// </summary>
	public bool InsertAfter( Block target, Block blockToInsert )
	{
		// Remove from current location first
		DeleteBlock( blockToInsert );

		return InsertRelative( Body, target, blockToInsert, 1 );
	}

	/// <summary>
	/// Insert relative to target (offset 0 = before, offset 1 = after).
	/// </summary>
	bool InsertRelative( List<Block> list, Block target, Block blockToInsert, int offset )
	{
		if ( list == null ) return false;

		for ( int i = 0; i < list.Count; i++ )
		{
			if ( list[i] == target )
			{
				list.Insert( i + offset, blockToInsert );
				return true;
			}

			// Search in nested blocks
			if ( InsertRelative( list[i].Body, target, blockToInsert, offset ) )
				return true;
		}

		return false;
	}

	/// <summary>
	/// Add a block as a child of the target block's body.
	/// </summary>
	public void AddChild( Block parent, Block blockToInsert )
	{
		// Remove from current location first
		DeleteBlock( blockToInsert );

		parent.Body ??= new List<Block>();
		parent.Body.Add( blockToInsert );
	}
}
