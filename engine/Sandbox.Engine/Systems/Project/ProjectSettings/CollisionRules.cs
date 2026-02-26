using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Sandbox.Physics;

/// <summary>
/// This is a JSON serializable description of the physics's collision rules. This allows us to send it
/// to the engine - and store it in a string table (which is networked to the client). You shouldn't really
/// ever have to mess with this, it's just used internally.
/// </summary>
[Expose]
public class CollisionRules : ConfigData
{
	/// <summary>
	/// Result of a collision between <see cref="Pair">two objects</see>.
	/// </summary>
	public enum Result
	{
		/// <summary>
		/// Fallback to default behavior.
		/// </summary>
		Unset,

		/// <summary>
		/// Collide.
		/// </summary>
		Collide,

		/// <summary>
		/// Do not collide, but trigger touch callbacks.
		/// </summary>
		Trigger,

		/// <summary>
		/// Do not collide.
		/// </summary>
		Ignore
	}

	public override int Version => 2;

	private record struct SerializedPair(
		[property: JsonPropertyName( "a" )] string Left,
		[property: JsonPropertyName( "b" )] string Right,
		[property: JsonPropertyName( "r" )] Result Result );

	/// <summary>
	/// A pair of case- and order-insensitive tags, used as a key to look up a <see cref="Result"/>.
	/// </summary>
	public readonly struct Pair : IEquatable<Pair>, IEnumerable<string>
	{
		/// <summary>
		/// Initializes from a pair of tags.
		/// </summary>
		public static implicit operator Pair( (string Left, string Right) tuple )
		{
			return new Pair( tuple.Left, tuple.Right );
		}

		/// <summary>
		/// First of the two tags.
		/// </summary>
		public string Left { get; }

		/// <summary>
		/// Second of the two tags.
		/// </summary>
		public string Right { get; }

		/// <summary>
		/// Initializes from a pair of tags.
		/// </summary>
		public Pair( string left, string right )
		{
			Left = left;
			Right = right;
		}

		/// <summary>
		/// Returns true if either <see cref="Left"/> or <see cref="Right"/> matches the given tag.
		/// </summary>
		public bool Contains( string tag )
		{
			return string.Equals( Left, tag, StringComparison.OrdinalIgnoreCase )
				|| string.Equals( Right, tag, StringComparison.OrdinalIgnoreCase );
		}

		/// <inheritdoc />
		public bool Equals( Pair other )
		{
			return string.Equals( Left, other.Left, StringComparison.OrdinalIgnoreCase )
				&& string.Equals( Right, other.Right, StringComparison.OrdinalIgnoreCase )
				|| string.Equals( Left, other.Right, StringComparison.OrdinalIgnoreCase )
				&& string.Equals( Right, other.Left, StringComparison.OrdinalIgnoreCase );
		}

		/// <inheritdoc />
		public override bool Equals( object obj )
		{
			return obj is Pair other && Equals( other );
		}

		/// <inheritdoc />
		public IEnumerator<string> GetEnumerator()
		{
			yield return Left;
			yield return Right;
		}

		/// <inheritdoc />
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

		/// <inheritdoc />
		public override int GetHashCode()
		{
			return StringComparer.OrdinalIgnoreCase.GetHashCode( Left )
				+ StringComparer.OrdinalIgnoreCase.GetHashCode( Right );
		}

		public override string ToString() => $"{Left}, {Right}";
	}

	private readonly record struct RuntimePair( StringToken Left, StringToken Right )
	{
		public bool Equals( RuntimePair other ) =>
			(Left.Value == other.Left.Value && Right.Value == other.Right.Value) ||
			(Left.Value == other.Right.Value && Right.Value == other.Left.Value);

		public override int GetHashCode() => unchecked((int)(Left.Value + Right.Value));
	}

	public CollisionRules()
	{
		OnValidate();
	}

	/// <summary>
	/// If no pair matching is found, this is what we'll use
	/// </summary>
	public Dictionary<string, Result> Defaults { get; set; }

	/// <summary>
	/// What happens when a pair collides
	/// </summary>
	[JsonIgnore]
	public Dictionary<Pair, Result> Pairs { get; set; }

	/// <summary>
	/// All tags with either an entry in <see cref="Defaults"/> or <see cref="Pairs"/>.
	/// </summary>
	[JsonIgnore]
	public IEnumerable<string> Tags => Defaults.Keys.Union( Pairs.Keys.SelectMany( x => x ) );

	private HashSet<StringToken> _runtimeTags;
	private Dictionary<StringToken, Result> _runtimeDefaults;
	private Dictionary<RuntimePair, Result> _runtimePairs;

	/// <summary>
	/// All known tags as <see cref="StringToken"/> values. Built during validation.
	/// </summary>
	[JsonIgnore]
	internal IReadOnlySet<StringToken> RuntimeTags => _runtimeTags;

	/// <summary>
	/// Gets or sets <see cref="Pairs"/> in its serialized form for JSON.
	/// </summary>
	[JsonInclude, JsonPropertyName( "Pairs" )]
	public JsonNode SerializedPairs
	{
		get => JsonSerializer.SerializeToNode( Pairs?.Select( SerializePair ).ToArray(), Json.options );

		private set
		{
			if ( value is null )
			{
				Pairs = null;
				return;
			}

			var pairs = new Dictionary<Pair, Result>();

			foreach ( var item in value.Deserialize<SerializedPair[]>( Json.options ) )
			{
				if ( item.Result == Result.Unset )
				{
					continue;
				}

				var key = new Pair( item.Left, item.Right );

				// Pick least colliding of any duplicates

				pairs[key] = pairs.TryGetValue( key, out var existing ) ? LeastColliding( item.Result, existing ) : item.Result;
			}

			Pairs = pairs;
		}
	}

	private static SerializedPair SerializePair( KeyValuePair<Pair, Result> keyValue )
	{
		return new SerializedPair( keyValue.Key.Left, keyValue.Key.Right, keyValue.Value );
	}

	/// <summary>
	/// Selects the result with the highest precedence (least colliding).
	/// </summary>
	private static Result LeastColliding( Result a, Result b )
	{
		return a >= b ? a : b;
	}

	/// <summary>
	/// Gets the specific collision rule for a pair of tags.
	/// </summary>
	public Result GetCollisionRule( string left, string right )
	{
		var key = new Pair( left, right );

		if ( !Pairs.TryGetValue( key, out var result ) )
		{
			result = LeastColliding( Defaults.GetValueOrDefault( left ), Defaults.GetValueOrDefault( right ) );
		}

		// If unset, collide

		return LeastColliding( result, Result.Collide );
	}

	/// <summary>
	/// Gets the collision rule for a pair of <see cref="StringToken"/> values.
	/// </summary>
	internal Result GetCollisionRule( StringToken left, StringToken right )
	{
		var key = new RuntimePair( left, right );

		if ( !_runtimePairs.TryGetValue( key, out var result ) )
		{
			result = LeastColliding( _runtimeDefaults.GetValueOrDefault( left ), _runtimeDefaults.GetValueOrDefault( right ) );
		}

		return LeastColliding( result, Result.Collide );
	}

	/// <summary>
	/// Remove duplicates etc
	/// </summary>
	[Obsolete]
	public void Clean()
	{
		OnValidate();
	}

	private void BuildRuntimeData()
	{
		_runtimeDefaults = new Dictionary<StringToken, Result>( Defaults.Count );
		foreach ( var (tag, result) in Defaults ) _runtimeDefaults[tag] = result;

		_runtimePairs = new Dictionary<RuntimePair, Result>( Pairs.Count );
		foreach ( var (pair, result) in Pairs )
		{
			var key = new RuntimePair( pair.Left, pair.Right );
			_runtimePairs[key] = _runtimePairs.TryGetValue( key, out var existing ) ? LeastColliding( result, existing ) : result;
		}

		_runtimeTags = [];
		foreach ( var tag in Tags ) _runtimeTags.Add( tag );
	}

	protected override void OnValidate()
	{
		Pairs ??= new()
		{
			[("solid", "solid")] = Result.Collide,
			[("trigger", "playerclip")] = Result.Ignore,
			[("trigger", "solid")] = Result.Trigger,
			[("playerclip", "solid")] = Result.Collide,
		};

		Defaults ??= new()
		{
			["solid"] = Result.Collide,
			["world"] = Result.Collide,
			["trigger"] = Result.Trigger,
			["ladder"] = Result.Ignore,
			["water"] = Result.Trigger,
		};

		BuildRuntimeData();
	}

	public override int GetHashCode()
	{
		HashCode hc = default;

		foreach ( var (key, value) in Pairs )
		{
			hc.Add( key.Left );
			hc.Add( key.Right );
			hc.Add( value );
		}

		foreach ( var (key, value) in Defaults )
		{
			hc.Add( key );
			hc.Add( value );
		}

		return hc.ToHashCode();
	}

	// Upgrader to add the sound tag to existing projects that don't already have it
	[Expose, JsonUpgrader( typeof( CollisionRules ), 2 )]
	static void Upgrader_v2( JsonObject json )
	{

	}
}
