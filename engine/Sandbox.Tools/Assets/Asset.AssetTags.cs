using System;

namespace Editor;

public partial class Asset
{
	/// <summary>
	/// Represents a collection of tags for an asset.
	/// This is only necessary so we can save tags as soon as they are added.
	/// </summary>
	public readonly struct AssetTags : IEnumerable<string>
	{
		readonly HashSet<string> tags = new( StringComparer.OrdinalIgnoreCase );
		readonly Asset asset;

		internal AssetTags( Asset asset )
		{
			this.asset = asset;
		}

		/// <summary>
		/// Remove all tags.
		/// </summary>
		internal void Clear() => tags.Clear();

		/// <summary>
		/// Add a single tag.
		/// </summary>
		public void Add( string tag )
		{
			AssetTagSystem.EnsureRegistered( tag );
			tags.Add( tag );
			asset.SaveUserTags();
		}

		/// <summary>
		/// Add multiple tags at once.
		/// </summary>
		public void Add( string[] in_tags )
		{
			foreach ( var tag in in_tags )
			{
				AssetTagSystem.EnsureRegistered( tag );
				tags.Add( tag );
			}

			asset.SaveUserTags();
		}

		/// <summary>
		/// Remove given tag from the asset.
		/// </summary>
		public void Remove( string tag )
		{
			tags.Remove( tag );
			asset.SaveUserTags();
		}

		/// <summary>
		/// Remove the tag if present, add if not.
		/// </summary>
		public void Toggle( string tag )
		{
			if ( Contains( tag ) ) { Remove( tag ); } else { Add( tag ); }
		}

		/// <summary>
		/// Set or remove the tag based on second argument.
		/// </summary>
		public void Set( string tag, bool set )
		{
			if ( set ) { Add( tag ); } else { Remove( tag ); }
		}

		/// <summary>
		/// Returns whether this asset has given tag.
		/// </summary>
		public bool Contains( string tag ) => tags.Contains( tag );

		/// <summary>
		/// Returns all tags of this asset.
		/// </summary>
		public string[] GetAll() => tags.ToArray();

		public IEnumerator<string> GetEnumerator() => tags.GetEnumerator();
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}

	bool suppressTagSaving = false;
	internal void UpdateAutoTags()
	{
		// this hasn't been initialised yet
		if ( AssetType is null )
			return;

		suppressTagSaving = true;

		foreach ( var tag in AssetTagSystem.All.Where( x => x.AutoTag ) )
		{
			if ( tag.Filter( this ) )
			{
				Tags.Add( tag.Tag );
			}
			else
			{
				Tags.Remove( tag.Tag );
			}
		}

		suppressTagSaving = false;
	}

	protected void LoadUserTags()
	{
		suppressTagSaving = true;

		var savedTags = MetaData?.Get<string[]>( "tags" );
		if ( savedTags != null ) Tags.Add( savedTags );

		suppressTagSaving = false;
	}

	internal void SaveUserTags()
	{
		if ( suppressTagSaving ) return;

		var tags = Tags.GetAll().Where( tag => !AssetTagSystem.IsAutoTag( tag ) ).ToHashSet( StringComparer.OrdinalIgnoreCase );
		if ( !tags.Any() )
		{
			// Check if we had tags before, do not create empty .meta files
			var savedTags = MetaData.Get<string[]>( "tags" );
			if ( savedTags == null ) return;
		}

		MetaData.Set( "tags", tags );
	}
}
