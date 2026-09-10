using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Editor.MovieMaker;

#nullable enable

public sealed class FromThemeAttribute : Attribute
{
	public string? Key { get; init; }

	private const string ThemePath = "/editor/moviemaker/assets/styles/theme.json";

	private static IEnumerable<(PropertyInfo Property, string Key)> FindProperties()
	{
		foreach ( var type in typeof( MovieEditor ).Assembly.GetTypes() )
		{
			var staticProperties = type.GetProperties(
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly );

			foreach ( var property in staticProperties )
			{
				if ( property.GetCustomAttribute<FromThemeAttribute>() is not { } attrib )
				{
					continue;
				}

				var key = attrib.Key ?? GetDefaultKey( property );

				yield return (property, key);
			}
		}
	}

	[ConCmd( "moviemaker_save_theme" )]
	public static void Save()
	{
		var json = new JsonObject();

		foreach ( var (property, key) in FindProperties().OrderBy( x => x.Key ) )
		{
			try
			{
				var value = property.GetValue( null );

				if ( value is Color color )
				{
					json.TryAdd( key, color.Hex );
				}
				else
				{
					var valueString = JsonSerializer.Serialize( value, property.PropertyType, EditorJsonOptions );

					json.TryAdd( key, valueString );
				}
			}
			catch
			{
				//
			}
		}

		FileSystem.Root.WriteAllText( ThemePath,
			json.ToJsonString( new JsonSerializerOptions { WriteIndented = true } ) );
	}

	[ConCmd( "moviemaker_load_theme" )]
	public static void Apply()
	{
		var themeJson = FileSystem.Root.ReadAllText( ThemePath );

		if ( themeJson is null )
		{
			return;
		}

		var theme = Json.Deserialize<Dictionary<string, string>>( themeJson );
		var hasMissingKeys = false;

		foreach ( var (property, key) in FindProperties() )
		{
			if ( !theme.TryGetValue( key, out var valueString ) )
			{
				hasMissingKeys = true;
				continue;
			}

			try
			{
				var value = property.PropertyType == typeof( Color )
					? Color.Parse( valueString )!.Value
					: Json.Deserialize( valueString, property.PropertyType );

				property.SetValue( null, value );
			}
			catch ( Exception ex )
			{
				Log.Warning( ex, $"Couldn't set property \"{property}\" from theme." );
			}
		}

		if ( hasMissingKeys )
		{
			Save();
		}
	}

	private static string GetDefaultKey( PropertyInfo property )
	{
		var key = $"{property.DeclaringType?.Name}{property.Name}";

		if ( key.EndsWith( "Color" ) )
		{
			key = key[..^"Color".Length];
		}

		return key;
	}
}
