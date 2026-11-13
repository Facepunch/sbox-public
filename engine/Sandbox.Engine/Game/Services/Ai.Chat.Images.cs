using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox.Services;

public static partial class Ai
{
	public sealed partial class Chat
	{
		/// <summary>
		/// Bounds image content across all turns while retaining the newest images and every message's metadata.
		/// Pruning the stored history also keeps retries identical after a failed submission.
		/// </summary>
		void TrimImageHistory()
		{
			var remainingBytes = Query.MaxImagePayloadBytes;

			for ( var messageIndex = conversation.Messages.Count - 1; messageIndex >= 0; messageIndex-- )
			{
				var message = conversation.Messages[messageIndex];
				if ( !message.TryGetProperty( "content", out var content ) || content.ValueKind != JsonValueKind.Array )
					continue;

				JsonObject trimmed = null;

				for ( var partIndex = content.GetArrayLength() - 1; partIndex >= 0; partIndex-- )
				{
					var part = content[partIndex];
					if ( !part.TryGetProperty( "type", out var type ) || type.GetString() != "image_url" )
						continue;

					var imageBytes = Encoding.UTF8.GetByteCount( part.GetRawText() );
					if ( imageBytes <= remainingBytes )
					{
						remainingBytes -= imageBytes;
						continue;
					}

					// Keep a contiguous suffix of images. Text and tool-call IDs must survive pruning.
					remainingBytes = 0;
					trimmed ??= JsonNode.Parse( message.GetRawText() ).AsObject();
					trimmed["content"][partIndex] = new JsonObject
					{
						["type"] = "text",
						["text"] = "[Earlier image omitted to limit conversation size.]"
					};
				}

				if ( trimmed is not null )
					conversation.Messages[messageIndex] = JsonSerializer.SerializeToElement( trimmed );
			}
		}
	}
}
