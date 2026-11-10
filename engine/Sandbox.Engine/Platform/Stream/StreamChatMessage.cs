namespace Sandbox;

public static partial class Streamer
{
	public struct ChatMessage
	{
		/// <summary>
		/// The viewer who sent the message. Their display name, color and badges live here.
		/// </summary>
		public Viewer Viewer { get; internal set; }

		/// <summary>
		/// The message text.
		/// </summary>
		public string Message { get; internal set; }

		/// <summary>
		/// The channel the message was sent in.
		/// </summary>
		public string Channel { get; internal set; }

		/// <summary>
		/// Bits cheered with this message, or 0 if it wasn't a cheer.
		/// </summary>
		public int Bits { get; internal set; }

		/// <summary>
		/// Whether this is the viewer's first ever message in this chat.
		/// </summary>
		public bool IsFirstMessage { get; internal set; }

		/// <summary>
		/// When we received the message.
		/// </summary>
		public DateTimeOffset Time { get; internal set; }
	}
}
