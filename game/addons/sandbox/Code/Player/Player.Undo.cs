public sealed partial class Player
{
	/// <summary>
	/// Access the undo system for this player
	/// </summary>
	internal UndoSystem.PlayerStack Undo => UndoSystem.Current.For( Network.Owner.SteamId );
}
