using System.Text.Json.Serialization;

/// <summary>
/// We want to save the properties of this instance as cookies.
/// </summary>
public interface ICookieSource
{
	/// <summary>
	/// Prefix to put before the names of any cookies, without a trailing period.
	/// For example: <c>"tool.balloon"</c>
	/// </summary>
	string CookiePrefix { get; }
}

/// <summary>
/// Extension methods for <see cref="ICookieSource"/>.
/// </summary>
public static class CookieSourceExtensions
{
	private static bool IsCookieProperty( PropertyDescription property )
	{
		// TODO: maybe we want to support static properties too?

		if ( property.IsStatic ) return false;
		if ( !property.HasAttribute<PropertyAttribute>() ) return false;
		if ( property.HasAttribute<JsonIgnoreAttribute>() ) return false;
		if ( property.HasAttribute<HideAttribute>() ) return false;
		if ( !property.CanWrite || !property.CanRead ) return false;

		return true;
	}

	private static string GetCookieName( PropertyDescription property, string prefix )
	{
		var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name.ToLowerInvariant();

		return !string.IsNullOrEmpty( prefix ) ? $"{prefix}.{name}" : name;
	}

	extension( ICookieSource source )
	{
		private IEnumerable<(string CookieName, PropertyDescription Property)> GetCookieProperties()
		{
			var typeDesc = TypeLibrary.GetType( source.GetType() );
			if ( typeDesc is null ) return [];

			var prefix = source.CookiePrefix;

			return typeDesc.Properties.Where( IsCookieProperty )
				.Select( x => (GetCookieName( x, prefix ), x) );
		}

		/// <summary>
		/// Saves any instance properties with a <see cref="PropertyAttribute"/>, excluding any
		/// with <see cref="JsonIgnoreAttribute"/> or <see cref="HideAttribute"/>, into <see cref="Game.Cookies"/>.
		/// </summary>
		public void SaveCookies()
		{
			foreach ( var (cookieName, property) in source.GetCookieProperties() )
			{
				try
				{
					var cookieValue = property.GetValue( source );

					Game.Cookies.SetString( cookieName, Json.ToNode( cookieValue, property.PropertyType )?.ToJsonString() ?? "null" );
				}
				catch ( Exception ex )
				{
					Log.Warning( ex, $"Exception while saving cookie \"{cookieName}\"." );
				}
			}
		}

		/// <summary>
		/// Loads any instance properties with a <see cref="PropertyAttribute"/>, excluding any
		/// with <see cref="JsonIgnoreAttribute"/> or <see cref="HideAttribute"/>, from <see cref="Game.Cookies"/>.
		/// </summary>
		public void LoadCookies()
		{
			foreach ( var (cookieName, property) in source.GetCookieProperties() )
			{
				if ( !Game.Cookies.TryGetString( cookieName, out var jsonString ) ) continue;

				try
				{
					var cookieValue = Json.Deserialize( jsonString, property.PropertyType );

					property.SetValue( source, cookieValue );
				}
				catch ( Exception ex )
				{
					Log.Warning( ex, $"Exception while loading cookie \"{cookieName}\"." );
				}
			}
		}
	}
}
