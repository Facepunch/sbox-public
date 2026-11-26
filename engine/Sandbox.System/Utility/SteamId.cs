using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// Represents a Steam ID (64-bit unique identifier for Steam accounts).
/// Provides type-safe storage and conversion between long/ulong representations.
/// </summary>
[JsonConverter( typeof( SteamIdJsonConverter ) )]
public struct SteamId : IEquatable<SteamId>
{
	private readonly ulong _id;

	/// <summary>
	/// Gets the Steam ID as a signed 64-bit integer.
	/// </summary>
	public long Value => (long)_id;

	/// <summary>
	/// Gets the Steam ID as an unsigned 64-bit integer.
	/// </summary>
	public ulong ValueUnsigned => _id;

	/// <summary>
	/// Creates a Steam ID from an unsigned 64-bit integer.
	/// </summary>
	public SteamId( ulong id ) => _id = id;

	/// <summary>
	/// Creates a Steam ID from a signed 64-bit integer.
	/// </summary>
	public SteamId( long id ) => _id = (ulong)id;

	/// <summary>
	/// Implicitly converts a long to a SteamId.
	/// </summary>
	public static implicit operator SteamId( long id ) => new SteamId( id );

	/// <summary>
	/// Implicitly converts a ulong to a SteamId.
	/// </summary>
	public static implicit operator SteamId( ulong id ) => new SteamId( id );

	/// <summary>
	/// You shouldn't be doing this - Steam IDs require 64 bits.
	/// </summary>
	[Obsolete( "SteamIds should never be referenced as an int. They are SteamId or a long." )]
	public static implicit operator SteamId( int id ) => new SteamId( id );

	/// <summary>
	/// Implicitly converts a SteamId to a long.
	/// </summary>
	public static implicit operator long( SteamId steamId ) => (long)steamId._id;

	/// <summary>
	/// Implicitly converts a SteamId to a ulong.
	/// </summary>
	public static implicit operator ulong( SteamId steamId ) => steamId._id;

	/// <summary>
	/// Returns the Steam ID as a string.
	/// </summary>
	public override string ToString() => _id.ToString();

	public readonly bool Equals( SteamId other ) => _id == other._id;

	public override readonly bool Equals( object obj ) => obj is SteamId other && _id == other._id;

	public override readonly int GetHashCode() => _id.GetHashCode();

	/// <summary>
	/// The different types of Steam accounts.
	/// </summary>
	public enum AccountTypes : uint
	{
		/// <summary>Invalid or uninitialized account</summary>
		Invalid = 0,
		/// <summary>Single user account</summary>
		Individual = 1,
		/// <summary>Multiseat account (e.g. cybercafe)</summary>
		Multiseat = 2,
		/// <summary>Game server account</summary>
		GameServer = 3,
		/// <summary>Anonymous game server account</summary>
		AnonGameServer = 4,
		/// <summary>Pending account</summary>
		Pending = 5,
		/// <summary>Content server account</summary>
		ContentServer = 6,
		/// <summary>Steam group/clan</summary>
		Clan = 7,
		/// <summary>Game lobby</summary>
		Lobby = 8,
		/// <summary>Console user (PSN/Xbox Live)</summary>
		ConsoleUser = 9,
		/// <summary>Anonymous user</summary>
		AnonUser = 10,
	}

	/// <summary>
	/// Gets the type of Steam account this ID represents.
	/// </summary>
	[JsonIgnore]
	public readonly AccountTypes AccountType => (AccountTypes)(byte)((_id >> 52) & 0xF);
}

file class SteamIdJsonConverter : JsonConverter<SteamId>
{
	public override SteamId Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
	{
		if ( reader.TokenType == JsonTokenType.Number )
		{
			// Try to read as ulong first (since Steam IDs can be very large)
			if ( reader.TryGetUInt64( out ulong ulongValue ) )
			{
				return new SteamId( ulongValue );
			}
			// Fall back to long if needed
			if ( reader.TryGetInt64( out long longValue ) )
			{
				return new SteamId( longValue );
			}
		}
		else if ( reader.TokenType == JsonTokenType.String )
		{
			// Also support string format (common for large numbers in JSON)
			string stringValue = reader.GetString();
			if ( ulong.TryParse( stringValue, out ulong ulongValue ) )
			{
				return new SteamId( ulongValue );
			}
		}

		throw new JsonException( $"Unable to convert to SteamId from {reader.TokenType}" );
	}

	public override void Write( Utf8JsonWriter writer, SteamId value, JsonSerializerOptions options )
	{
		writer.WriteNumberValue( value.ValueUnsigned );
	}
}
