using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sandbox;

[Expose, JsonConverter( typeof( TagSet.JsonConvert ) )]
public class TagSet : ITagSet, BytePack.ISerializer
{
	private HashSet<uint> _tokens = new();
	private HashSet<string> Tags { get; set; } = new( StringComparer.OrdinalIgnoreCase );

	public bool IsEmpty => Tags.Count == 0;

	// Iterated as a HashSet so the enumerator isn't boxed
	internal HashSet<uint> TokenSet => _tokens;

	public TagSet() { }

	public TagSet( IEnumerable<string> tags )
	{
		foreach ( var tag in tags )
		{
			Add( tag );
		}
	}

	public override void Add( string tag )
	{
		if ( Tags.Add( tag ) )
		{
			_tokens.Add( StringToken.FindOrCreate( tag ) );
		}
	}

	public override IEnumerable<string> TryGetAll() => Tags.AsEnumerable();

	public override bool Has( string tag ) => Tags.Contains( tag );

	public override bool HasAny( ITagSet other )
	{
		if ( other is TagSet ) return AnyHasIn( other );

		// Same tags as GameTags.TryGetAll, which stops at the scene
		if ( other is GameTags gameTags )
		{
			var tokens = gameTags.GetTokens();
			foreach ( var token in _tokens )
			{
				if ( tokens.Contains( token ) ) return true;
			}

			return false;
		}

		return base.HasAny( other );
	}

	// Ask the other set about ours, enumerating it would box an enumerator
	internal bool AnyHasIn( ITagSet other )
	{
		foreach ( var tag in Tags )
		{
			if ( other.Has( tag ) ) return true;
		}

		return false;
	}

	public override void Remove( string tag )
	{
		if ( Tags.Remove( tag ) )
		{
			_tokens.Remove( StringToken.FindOrCreate( tag ) );
		}
	}

	public override void RemoveAll()
	{
		Tags.Clear();
		_tokens.Clear();
	}

	public override int GetHashCode()
	{
		return HashCode.Combine( Tags, Tags.Count );
	}

	static object BytePack.ISerializer.BytePackRead( ref ByteStream bs, Type targetType )
	{
		var set = new TagSet();
		foreach ( var tag in bs.Read<string>().Split( ',', StringSplitOptions.RemoveEmptyEntries ) )
		{
			set.Add( tag );
		}

		return set;
	}

	static void BytePack.ISerializer.BytePackWrite( object obj, ref ByteStream bs )
	{
		if ( obj is not TagSet value )
			throw new NotImplementedException();

		var tags = value.TryGetAll();
		bs.Write( string.Join( ",", tags ) );
	}

	/// <summary>
	/// Returns a list of ints, representing the tags. These are used internally by the engine.
	/// </summary>
	public override IReadOnlySet<uint> GetTokens() => _tokens;

	public class JsonConvert : JsonConverter<TagSet>
	{
		public override TagSet Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
		{
			if ( reader.TokenType == JsonTokenType.String )
			{
				var parts = reader.GetString().Split( ',', StringSplitOptions.RemoveEmptyEntries );

				var set = new TagSet();

				foreach ( var tag in parts )
					set.Add( tag );

				return set;
			}

			return new TagSet();
		}

		public override void Write( Utf8JsonWriter writer, TagSet val, JsonSerializerOptions options )
		{
			writer.WriteStringValue( string.Join( ",", val.TryGetAll() ) );
		}
	}
}
