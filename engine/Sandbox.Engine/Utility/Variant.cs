using System.Text.Json;

namespace Sandbox;

/// <summary>
/// A Variant is a type that can hold any value, and also keeps track of the type of the value it holds.
/// It's useful for cases where you want to store a value of an unknown type, or when you want to 
/// serialize/deserialize values of various types in a generic way.
/// </summary>
[Expose]
public struct Variant : IJsonConvert, IEquatable<Variant>
{
	/// <summary>
	/// The type of the value currently stored in the Variant. This is automatically set when you assign a value to the Variant.
	/// </summary>
	public Type Type
	{
		get;

		private set
		{
			if ( value is null )
				return;

			//
			// We don't want the type to be super specific
			// when it comes to Components and Resources,
			// since that would make it harder to use in a generic way.
			//

			if ( value.IsAssignableTo( typeof( Component ) ) )
			{
				field = typeof( Component );
				return;
			}

			if ( value.IsAssignableTo( typeof( Resource ) ) )
			{
				field = typeof( Resource );
				return;
			}

			field = value;
		}
	}

	public static implicit operator Variant( float n ) => new( n );
	public static implicit operator Variant( double n ) => new( n );
	public static implicit operator Variant( string s ) => new( s );
	public static implicit operator Variant( bool b ) => new( b );
	public static implicit operator Variant( int b ) => new( b );
	public static implicit operator Variant( Vector2 v ) => new( v );
	public static implicit operator Variant( Vector3 v ) => new( v );
	public static implicit operator Variant( Vector4 v ) => new( v );
	public static implicit operator Variant( Color c ) => new( c );
	public static implicit operator Variant( Component c ) => new( c );
	public static implicit operator Variant( GameObject c ) => new( c );
	public static implicit operator Variant( Resource c ) => new( c );

	/// <summary>
	/// Gets or sets the value associated with this instance.
	/// </summary>
	public object Value
	{
		get;

		set
		{
			if ( value is not null )
			{
				Type = value?.GetType();
			}

			field = value;
		}
	}

	public Variant( object o, Type t = null )
	{
		Value = o;
		Type = t;
	}

	public T Get<T>() => (T)Value;

	public override string ToString() => Value?.ToString();

	public bool Equals( Variant other ) => Equals( Value, other.Value );
	public override bool Equals( object obj ) => obj is Variant other && Equals( other );
	public override int GetHashCode() => Value?.GetHashCode() ?? 0;

	public static bool operator ==( Variant left, Variant right ) => left.Equals( right );
	public static bool operator !=( Variant left, Variant right ) => !left.Equals( right );

	public static object JsonRead( ref Utf8JsonReader reader, Type typeToConvert )
	{
		if ( reader.TokenType == JsonTokenType.Null )
			return new Variant();

		if ( reader.TokenType == JsonTokenType.String )
		{
			return new Variant { Value = reader.GetString() };
		}

		if ( reader.TokenType != JsonTokenType.StartObject )
			throw new JsonException( "Variant JSON must be an object." );

		// snapshot the reader at StartObject
		var depth = reader.CurrentDepth;
		var snapshot = reader;

		// first pass: scan for "t" only
		Type resolvedType = null;
		while ( reader.Read() && reader.TokenType != JsonTokenType.EndObject )
		{
			if ( reader.TokenType == JsonTokenType.PropertyName && reader.GetString() == "t" )
			{
				reader.Read();
				var typeDesc = Game.TypeLibrary.GetType( reader.GetString(), true );

				if ( typeDesc is null )
				{
					throw new JsonException( "Unknown type in Variant JSON: " + reader.GetString() );
				}

				resolvedType = typeDesc?.TargetType;
				break;
			}
			reader.Read();
			reader.Skip();
		}

		if ( resolvedType == null )
		{
			// if we couldn't resolve the type, skip the value and return an empty Variant
			while ( reader.TokenType != JsonTokenType.EndObject || reader.CurrentDepth != depth )
				reader.Read();

			return new Variant();
		}

		// second pass: rewind, parse "v" with the type known
		reader = snapshot;
		object resolvedValue = null;
		while ( reader.Read() && reader.TokenType != JsonTokenType.EndObject )
		{
			if ( reader.TokenType == JsonTokenType.PropertyName && reader.GetString() == "v" )
			{
				reader.Read();
				if ( resolvedType != null )
					resolvedValue = Json.Deserialize( ref reader, resolvedType );
				else
					reader.Skip();
				break;
			}
			reader.Read();
			reader.Skip();
		}

		// consume to end of outer object
		while ( reader.TokenType != JsonTokenType.EndObject || reader.CurrentDepth != depth )
			reader.Read();

		return new Variant { Value = resolvedValue, Type = resolvedType };
	}

	public static void JsonWrite( object value, Utf8JsonWriter writer )
	{
		var v = (Variant)value;
		var t = v.Type;

		if ( t == null )
		{
			writer.WriteNullValue();
			return;
		}

		if ( v.Value is string str )
		{
			writer.WriteStringValue( str );
			return;
		}

		writer.WriteStartObject();
		{
			{
				writer.WriteString( "t", t.FullName );
			}

			{
				writer.WritePropertyName( "v" );
				Json.Serialize( writer, v.Value ); // calls JsonSerializer.Serialize under the hood
			}
		}
		writer.WriteEndObject();
	}
}
