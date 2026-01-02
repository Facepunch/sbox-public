using System;
using System.Globalization;
using System.Text;

namespace ValveKeyValue;

internal sealed class KeyValues( string name, string value = null )
{
	private static readonly string[] ApplyConditionals =
	[
		">=dx90_20b", ">=dx90", ">=dx80", ">=dx70",
		">dx90", ">dx80", ">dx70", "dx9",
		"hdr", "hdr_dx9", "srgb",
		"gpu>=1", "gpu>=2", "gpu>=3",
	];

	private static readonly string[] RemoveConditionals =
	[
		"<dx95", "<dx90", "<=dx90", "<dx90_20b", "<dx80", "<dx70",
		"ldr", "360", "sonyps3", "gameconsole",
		"gpu<1", "gpu<2", "gpu<3",
	];

	public string Name { get; } = name;
	public string Value { get; private set; } = value;
	public List<KeyValues> Children { get; } = [];

	public KeyValues this[string key]
	{
		get
		{
			foreach ( var child in Children )
			{
				if ( child.Name.Equals( key, StringComparison.OrdinalIgnoreCase ) )
					return child;
			}
			return null;
		}
	}

	public string GetString( string key, string defaultValue = null )
	{
		var child = this[key];
		return child?.Value ?? defaultValue;
	}

	public int GetInt( string key, int defaultValue = 0 )
	{
		var child = this[key];
		return child?.Value == null
			? defaultValue
			: int.TryParse( child.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result ) ? result : defaultValue;
	}

	public float GetFloat( string key, float defaultValue = 0f )
	{
		var child = this[key];
		return child?.Value == null
			? defaultValue
			: float.TryParse( child.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result ) ? result : defaultValue;
	}

	public bool GetBool( string key, bool defaultValue = false )
	{
		var child = this[key];
		if ( child?.Value == null )
			return defaultValue;

		if ( child.Value == "1" || child.Value.Equals( "true", StringComparison.OrdinalIgnoreCase ) )
			return true;
		if ( child.Value == "0" || child.Value.Equals( "false", StringComparison.OrdinalIgnoreCase ) )
			return false;

		return defaultValue;
	}

	public void MergeConditionals()
	{
		foreach ( var blockName in ApplyConditionals )
		{
			var block = FindChildIgnoreCase( blockName );
			if ( block == null )
				continue;

			foreach ( var child in block.Children )
			{
				Children.RemoveAll( c => c.Name.Equals( child.Name, StringComparison.OrdinalIgnoreCase ) );
				Children.Add( child );
			}

			Children.Remove( block );
		}

		foreach ( var blockName in RemoveConditionals )
		{
			var block = FindChildIgnoreCase( blockName );
			if ( block != null )
				Children.Remove( block );
		}

		ProcessInlineConditionals();
	}

	private void ProcessInlineConditionals()
	{
		var toAdd = new List<KeyValues>();
		var toRemove = new List<KeyValues>();

		foreach ( var child in Children )
		{
			if ( child.Name == null || !child.Name.Contains( '?' ) )
				continue;

			var parts = child.Name.Split( '?', 2 );
			if ( parts.Length != 2 )
				continue;

			var condition = parts[0].ToLowerInvariant();
			var paramName = parts[1];

			if ( EvaluateCondition( condition ) )
			{
				toRemove.Add( child );
				toAdd.Add( new KeyValues( paramName, child.Value ) );
			}
			else
			{
				toRemove.Add( child );
			}
		}

		foreach ( var child in toRemove )
			Children.Remove( child );

		foreach ( var child in toAdd )
		{
			Children.RemoveAll( c => c.Name.Equals( child.Name, StringComparison.OrdinalIgnoreCase ) );
			Children.Add( child );
		}
	}

	private static bool EvaluateCondition( string condition )
	{
		bool negate = condition.StartsWith( '!' );
		if ( negate )
			condition = condition[1..];

		bool result = condition switch
		{
			"hdr" or "srgb" or "srgb_pc" => true,
			"ldr" or "360" or "sonyps3" or "gameconsole" or "srgb_gameconsole" => false,
			"lowfill" => false,
			"highqualitycsm" => true,
			"lowqualitycsm" => false,
			_ when condition.StartsWith( "gpu>=" ) => true,
			_ when condition.StartsWith( "gpu<" ) => false,
			_ => false
		};

		return negate ? !result : result;
	}

	private KeyValues FindChildIgnoreCase( string name )
	{
		foreach ( var child in Children )
		{
			if ( child.Name.Equals( name, StringComparison.OrdinalIgnoreCase ) )
				return child;
		}
		return null;
	}

	public static KeyValues Parse( string text )
	{
		var reader = new KeyValuesReader( text );
		return reader.Parse();
	}

	public static KeyValues Parse( byte[] data )
	{
		return Parse( Encoding.UTF8.GetString( data ) );
	}

	public static KeyValues Parse( Stream stream )
	{
		using var reader = new StreamReader( stream, Encoding.UTF8 );
		return Parse( reader.ReadToEnd() );
	}

	private sealed class KeyValuesReader( string text )
	{
		private readonly string _text = text ?? string.Empty;
		private int _pos = 0;

		public KeyValues Parse()
		{
			SkipWhitespaceAndComments();

			var name = ReadToken();
			if ( name == null )
				return null;

			SkipWhitespaceAndComments();

			if ( _pos < _text.Length && _text[_pos] == '{' )
			{
				_pos++;
				var kv = new KeyValues( name );
				ParseChildren( kv );
				return kv;
			}

			var value = ReadToken();
			return new KeyValues( name, value );
		}

		private void ParseChildren( KeyValues parent )
		{
			while ( _pos < _text.Length )
			{
				SkipWhitespaceAndComments();

				if ( _pos >= _text.Length )
					break;

				if ( _text[_pos] == '}' )
				{
					_pos++;
					break;
				}

				var key = ReadToken();
				if ( key == null )
					break;

				SkipWhitespaceAndComments();

				if ( _pos < _text.Length && _text[_pos] == '{' )
				{
					_pos++;
					var child = new KeyValues( key );
					ParseChildren( child );
					parent.Children.Add( child );
				}
				else
				{
					var value = ReadToken();
					parent.Children.Add( new KeyValues( key, value ) );
				}
			}
		}

		private string ReadToken()
		{
			SkipWhitespaceAndComments();

			return _pos >= _text.Length ? null : _text[_pos] == '"' ? ReadQuotedString() : ReadUnquotedString();
		}

		private string ReadQuotedString()
		{
			_pos++;

			var sb = new StringBuilder();
			while ( _pos < _text.Length )
			{
				var c = _text[_pos];

				if ( c == '"' )
				{
					_pos++;
					break;
				}

				sb.Append( c );
				_pos++;
			}

			return sb.ToString();
		}

		private string ReadUnquotedString()
		{
			var start = _pos;
			while ( _pos < _text.Length )
			{
				var c = _text[_pos];
				if ( char.IsWhiteSpace( c ) || c == '{' || c == '}' || c == '"' )
					break;
				_pos++;
			}

			return _text[start.._pos];
		}

		private void SkipWhitespaceAndComments()
		{
			while ( _pos < _text.Length )
			{
				var c = _text[_pos];

				if ( char.IsWhiteSpace( c ) )
				{
					_pos++;
					continue;
				}

				// Line comment
				if ( c == '/' && _pos + 1 < _text.Length && _text[_pos + 1] == '/' )
				{
					_pos += 2;
					while ( _pos < _text.Length && _text[_pos] != '\n' )
						_pos++;
					continue;
				}

				// Block comment
				if ( c == '/' && _pos + 1 < _text.Length && _text[_pos + 1] == '*' )
				{
					_pos += 2;
					while ( _pos + 1 < _text.Length )
					{
						if ( _text[_pos] == '*' && _text[_pos + 1] == '/' )
						{
							_pos += 2;
							break;
						}
						_pos++;
					}
					continue;
				}

				break;
			}
		}
	}
}
