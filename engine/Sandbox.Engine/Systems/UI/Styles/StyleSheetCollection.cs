
namespace Sandbox.UI;

/// <summary>
/// A collection of <see cref="StyleSheet"/> objects applied directly to a panel.
/// See <see cref="Panel.StyleSheet"/>.
/// </summary>
public struct StyleSheetCollection
{
	internal List<StyleSheet> List;
	internal readonly Panel Owner;

	internal StyleSheetCollection( Panel owner ) : this()
	{
		Owner = owner;
	}

	/// <summary>
	/// Add a stylesheet directly
	/// </summary>
	public void Add( StyleSheet sheet )
	{
		if ( sheet is null ) return;

		List ??= new List<StyleSheet>();
		if ( List.Contains( sheet ) ) return;

		List.Insert( 0, sheet );
		Owner?.Style?.Dirty();
		Owner?.Style?.InvalidateBroadphase();
	}

	/// <summary>
	/// Load the stylesheet from a file.
	/// </summary>
	public void Load( string filename, bool inheritVariables = true, bool failSilently = false )
	{
		Add( StyleSheet.FromFile( filename, inheritVariables ? CollectVariables() : null, failSilently ) );
	}

	/// <summary>
	/// Add a stylesheet parsed from a string. The parsed sheet is cached under
	/// <paramref name="key"/> and shared between all panels using the same key.
	/// </summary>
	public void AddInline( string content, string key )
	{
		Add( StyleSheet.FromInline( content, key ) );
	}

	/// <summary>
	/// Load the stylesheet from a string.
	/// </summary>
	public void Parse( string stylesheet, bool inheritVariables = true )
	{
		Remove( "string" );
		Add( StyleSheet.FromString( stylesheet, "string", inheritVariables ? CollectVariables() : null ) );
	}

	/// <summary>
	/// Remove a specific <see cref="StyleSheet"/> from the collection.
	/// </summary>
	public void Remove( StyleSheet sheet )
	{
		if ( List is null )
			return;

		if ( List.Remove( sheet ) )
		{
			Owner?.Style?.InvalidateBroadphase();
		}
	}

	/// <summary>
	/// Remove all stylesheets whose filename matches this wildcard glob.
	/// </summary>
	public void Remove( string wildcardGlob )
	{
		if ( (List?.RemoveAll( x => x.FileName != null && x.FileName.WildcardMatch( wildcardGlob ) ) ?? 0) > 0 )
		{
			Owner?.Style?.Dirty();
			Owner?.Style?.InvalidateBroadphase();
		}
	}

	/// <summary>
	/// Returns all CSS variables from the owning panel and its ancestors.
	/// </summary>
	public IEnumerable<(string key, string value)> CollectVariables()
	{
		if ( Owner == null )
			yield break;


		foreach ( var sheet in Owner.AllStyleSheets )
		{
			if ( sheet.Variables == null ) continue;

			foreach ( var v in sheet.Variables )
			{
				yield return (v.Key, v.Value);
			}
		}

	}
}
