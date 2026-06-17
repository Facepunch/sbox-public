
namespace Sandbox;

partial class PartyRoom
{
	public Action<Friend, string> OnChatMessage { get; set; }
	public Action<Friend> OnJoin { get; set; }
	public Action<Friend> OnLeave { get; set; }
	public Action<Friend, byte[]> OnVoiceData { get; set; }

	public interface IEventListener
	{
		/// <summary>
		/// Called when we join a party.
		/// </summary>
		void OnJoinedParty( PartyRoom party ) { }

		/// <summary>
		/// Called when we leave a party.
		/// </summary>
		void OnLeftParty( PartyRoom party ) { }

		/// <summary>
		/// A lobby member has sent a chat message.
		/// </summary>
		void OnChatMessage( Friend sender, string message ) { }

		/// <summary>
		/// A lobby member has sent a voice packet.
		/// </summary>
		void OnVoiceMessage( Friend sender, byte[] data ) { }

		/// <summary>
		/// A lobby member has joined.
		/// </summary>
		void OnMemberJoin( Friend sender ) { }

		/// <summary>
		/// A lobby member has left.
		/// </summary>
		void OnMemberLeave( Friend sender ) { }
	}
}
