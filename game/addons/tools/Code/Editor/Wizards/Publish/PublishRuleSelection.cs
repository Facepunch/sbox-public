using static Editor.ProjectPublisher.PackageManifest;

namespace Editor;

/// <summary>Reversible preview additions, replayed over untouched pre-existing rules.</summary>
internal sealed class PublishRuleSelection
{
	string baseline;
	readonly List<string> additions = new();
	readonly HashSet<string> existing = new( StringComparer.OrdinalIgnoreCase );
	static string Key( string rule ) => rule.Replace( '\\', '/' ).Trim().TrimStart( '/' );
	public bool Contains( string rule ) => additions.Any( x => string.Equals( Key( x ), Key( rule ), StringComparison.OrdinalIgnoreCase ) );
	public bool IsSelected( string[] patterns ) => patterns.Any( Contains ) && patterns.All( x => Contains( x ) || existing.Contains( Key( x ) ) );

	public void Reset()
	{
		baseline = null;
		additions.Clear();
		existing.Clear();
	}

	public string Toggle( string current, string[] patterns )
	{
		if ( patterns.Length == 0 ) return current;
		var original = baseline ?? current ?? "";
		var next = additions.ToList();
		var unchanged = existing.ToHashSet( StringComparer.OrdinalIgnoreCase );
		if ( IsSelected( patterns ) )
		{
			var removing = patterns.Select( Key ).ToHashSet( StringComparer.OrdinalIgnoreCase );
			next.RemoveAll( x => removing.Contains( Key( x ) ) );
		}
		else
		{
			var value = AddResourceRules( original, next );
			foreach ( var pattern in patterns.Where( x => !Contains( x ) ) )
			{
				var updated = AddResourceRules( value, [pattern] );
				if ( updated == value )
				{
					unchanged.Add( Key( pattern ) );
					continue;
				}
				next.Add( pattern );
				value = updated;
			}
		}
		var result = next.Count == 0 ? original : AddResourceRules( original, next );
		baseline = original;
		additions.Clear();
		additions.AddRange( next );
		existing.UnionWith( unchanged );
		return result;
	}
}
