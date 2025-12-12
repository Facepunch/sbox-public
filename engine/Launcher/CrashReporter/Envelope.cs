using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CrashReporter;

public record EnvelopeException( string? Type, string? Value );
public record FormattedEnvelopeItem( string Header, string Payload );
public record FormattedEnvelope( string Header, List<FormattedEnvelopeItem> Items );
public record Attachment( string Filename, byte[] Data );

public sealed class EnvelopeItem( JsonObject header, byte[] payload )
{
	public JsonObject Header { get; } = header;
	public byte[] Payload { get; } = payload;

	public string? TryGetType()
	{
		return TryGetHeader( "type" );
	}

	public string? TryGetHeader( string key )
	{
		if ( Header.TryGetPropertyValue( key, out var node ) && node is JsonValue value &&
			value.TryGetValue( out string? result ) )
		{
			return result;
		}

		return null;
	}

	public string? TryGetTag( string tag )
	{
		if ( !Header.TryGetPropertyValue( "tags", out var tagsNode ) || tagsNode is not JsonObject tags )
		{
			return null;
		}

		if ( tags.TryGetPropertyValue( tag, out var tagNode ) && tagNode is JsonValue value && value.TryGetValue( out string? result ) )
		{
			return result;
		}

		return null;
	}

	public JsonObject? TryParseAsJson()
	{
		try
		{
			return JsonNode.Parse( Payload )?.AsObject();
		}
		catch ( JsonException )
		{
			return null;
		}
	}

	public FormattedEnvelopeItem Format( JsonSerializerOptions? options = null )
	{
		options ??= new JsonSerializerOptions { WriteIndented = true };
		var header = Header.ToJsonString( options );
		try
		{
			var json = JsonNode.Parse( Payload );
			var payload = json?.AsObject().ToJsonString( options );
			return new FormattedEnvelopeItem( header, payload ?? string.Empty );
		}
		catch ( JsonException )
		{
			const int maxLen = 32;
			var hex = BitConverter.ToString( Payload.Take( maxLen ).ToArray() ).Replace( "-", " " );
			if ( Payload.Length > maxLen )
			{
				hex += "...";
			}
			return new FormattedEnvelopeItem( header, hex );
		}
	}

	internal async Task SerializeAsync(
		Stream stream,
		CancellationToken cancellationToken = default )
	{
		var json = Encoding.UTF8.GetBytes( Header.ToJsonString() );
		await stream.WriteLineAsync( json.AsMemory(), cancellationToken ).ConfigureAwait( false );

		await stream.WriteLineAsync( Payload.AsMemory(), cancellationToken ).ConfigureAwait( false );
	}

	internal static async Task<EnvelopeItem> DeserializeAsync(
		Stream stream, CancellationToken cancellationToken = default )
	{
		var buffer = await stream.ReadLineAsync( cancellationToken ).ConfigureAwait( false ) ??
					 throw new InvalidOperationException( "Envelope item is malformed." );
		var header = JsonNode.Parse( buffer )?.AsObject() ?? throw new InvalidOperationException( "Envelope item is malformed." );

		if ( header.TryGetPropertyValue( "length", out var node ) && node?.AsValue()?.TryGetValue( out long length ) == true )
		{
			var payload = new byte[length];
			var pos = 0;
			while ( pos < payload.Length )
			{
				var read = await stream.ReadAsync( payload, pos, payload.Length - pos, cancellationToken ).ConfigureAwait( false );
				if ( read == 0 )
				{
					throw new InvalidOperationException( "Envelope item payload is malformed." );
				}
				pos += read;
			}
			return new EnvelopeItem( header, payload );
		}
		else
		{
			var payload = await stream.ReadLineAsync( cancellationToken ).ConfigureAwait( false ) ??
						  throw new InvalidOperationException( "Envelope item payload is malformed." );
			return new EnvelopeItem( header, Encoding.UTF8.GetBytes( payload ) );
		}
	}
}

public sealed class Envelope( JsonObject header, IReadOnlyList<EnvelopeItem> items )
{
	public string? FilePath { get; internal set; }
	public JsonObject Header { get; } = header;
	public IReadOnlyList<EnvelopeItem> Items { get; } = items;

	public string? TryGetDsn()
	{
		return TryGetHeader( "dsn" );
	}

	public string? TryGetEventId()
	{
		return TryGetHeader( "event_id" );
	}

	public string? TryGetHeader( string key )
	{
		if ( Header.TryGetPropertyValue( key, out var node ) && node is JsonValue value &&
			value.TryGetValue( out string? result ) )
		{
			return result;
		}

		return null;
	}

	public EnvelopeItem? TryGetEvent()
	{
		return Items.FirstOrDefault( i => i.TryGetType() == "event" );
	}

	/*
	public Minidump? TryGetMinidump()
	{
		var item = Items.FirstOrDefault( i => i.TryGetHeader( "attachment_type" ) == "event.minidump" );
		if ( item is null )
		{
			return null;
		}

		return Minidump.FromBytes( item.Payload );
	}

	public List<Attachment> TryGetAttachments()
	{
		return Items
			.Where( s => s.TryGetType() == "attachment" )
			.Select( s => new Attachment( s.Header.TryGetString( "filename" ) ?? string.Empty, s.Payload ) )
			.Where( a => !string.IsNullOrEmpty( a.Filename ) )
			.ToList();
	}

	public EnvelopeException? TryGetException()
	{
		var payload = TryGetEvent()?.TryParseAsJson();
		var os = payload?.TryGetString( "contexts.os.name" );

		if ( payload?.TryGetProperty( "exception.values" )?.AsArray().FirstOrDefault()?.AsObject() is { } inproc )
		{
			return new EnvelopeException( inproc.TryGetString( "type" ), inproc.TryGetString( "value" ) );
		}

		if ( TryGetMinidump()?.Streams.Select( s => s.Data )
				.OfType<Minidump.ExceptionStream>()
				.FirstOrDefault() is { } minidump )
		{
			var code = minidump.ExceptionRec.Code.AsExceptionCode( os ?? string.Empty );
			return new EnvelopeException( code?.Type, code?.Value );
		}

		return null;
	}
	*/

	public FormattedEnvelope Format( JsonSerializerOptions? options = null )
	{
		options ??= new JsonSerializerOptions { WriteIndented = true };
		var header = Header.ToJsonString( options );

		var items = new List<FormattedEnvelopeItem>();
		foreach ( var item in Items )
		{
			items.Add( item.Format( options ) );
		}

		return new FormattedEnvelope( header, items );
	}

	public async Task SerializeAsync(
		Stream stream,
		CancellationToken cancellationToken = default )
	{
		var json = Encoding.UTF8.GetBytes( Header.ToJsonString() );
		await stream.WriteLineAsync( json.AsMemory(), cancellationToken ).ConfigureAwait( false );

		foreach ( var item in Items )
		{
			await item.SerializeAsync( stream, cancellationToken ).ConfigureAwait( false );
		}
	}

	public static async Task<Envelope> FromFileStreamAsync(
		FileStream fileStream,
		CancellationToken cancellationToken = default )
	{
		var envelope = await DeserializeAsync( fileStream, cancellationToken );
		envelope.FilePath = fileStream.Name;
		return envelope;
	}

	public static async Task<Envelope> DeserializeAsync(
		Stream stream,
		CancellationToken cancellationToken = default )
	{
		var buffer = await stream.ReadLineAsync( cancellationToken ).ConfigureAwait( false ) ??
					 throw new InvalidOperationException( "Envelope header is malformed." );
		var header = JsonNode.Parse( buffer )?.AsObject() ?? throw new InvalidOperationException( "Envelope header is malformed." );

		var items = new List<EnvelopeItem>();
		while ( stream.Position < stream.Length )
		{
			var item = await EnvelopeItem.DeserializeAsync( stream, cancellationToken ).ConfigureAwait( false );
			items.Add( item );
			await stream.ConsumeEmptyLinesAsync( cancellationToken );
		}

		return new Envelope( header, items );
	}

	public static Envelope FromJson( JsonObject header, IEnumerable<(JsonObject Header, JsonObject Payload)> items )
	{
		var envelopeItems = items.Select( item => new EnvelopeItem( item.Header, Encoding.UTF8.GetBytes( item.Payload.ToJsonString() ) ) );
		return new Envelope( header, envelopeItems.ToList() );
	}
}

internal static class StreamExtensions
{
	private const byte NewLine = (byte)'\n';

	public static async Task<string?> ReadLineAsync( this Stream stream, CancellationToken cancellationToken = default )
	{
		var line = new List<byte>();
		var buffer = new byte[1];
		while ( true )
		{
			var read = await stream.ReadAsync( buffer, 0, 1, cancellationToken ).ConfigureAwait( false );
			if ( read == 0 || buffer[0] == NewLine )
			{
				break;
			}
			line.Add( buffer[0] );
		}
		return Encoding.UTF8.GetString( line.ToArray() );
	}

	public static async Task WriteLineAsync( this Stream stream, ReadOnlyMemory<byte> line, CancellationToken cancellationToken = default )
	{
		await stream.WriteAsync( line, cancellationToken ).ConfigureAwait( false );
		await stream.WriteAsync( new byte[] { NewLine }, cancellationToken ).ConfigureAwait( false );
	}

	public static async Task ConsumeEmptyLinesAsync( this Stream stream, CancellationToken cancellationToken = default )
	{
		while ( await stream.PeekAsync( cancellationToken ) == NewLine )
		{
			await stream.ReadLineAsync( cancellationToken ).ConfigureAwait( false );
		}
	}

	public static async Task<int> PeekAsync( this Stream stream, CancellationToken cancellationToken = default )
	{
		var pos = stream.Position;
		var buffer = new byte[1];
		var read = await stream.ReadAsync( buffer, 0, 1, cancellationToken ).ConfigureAwait( false );
		stream.Position = pos;
		return read == 0 ? -1 : buffer[0];
	}
}
