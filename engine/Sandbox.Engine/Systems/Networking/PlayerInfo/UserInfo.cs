namespace Sandbox;

[Expose]
internal struct UserInfo
{
	public SteamId SteamId { get; set; }
	public string DisplayName { get; set; }
	public DateTimeOffset ConnectionTime { get; set; }
	public Dictionary<string, string> UserData { get; set; }
	public int EngineVersion { get; internal set; }
	public SteamId PartyId { get; internal set; }
	public byte[] AuthTicket { get; set; }
	public Guid HandshakeId { get; set; }
	public bool IsVr { get; set; }
	public byte[] InventoryBlob { get; set; }

	/// <summary>
	/// Build info for the local user, which will then get sent to the server and possibly shared between all clients
	/// </summary>
	internal static UserInfo Local
	{
		get
		{
			var ui = new UserInfo
			{
				ConnectionTime = DateTime.UtcNow,
				SteamId = Utility.Steam.SteamId,
				DisplayName = Utility.Steam.PersonaName,
				EngineVersion = Engine.Protocol.Network,
				UserData = new(),
				PartyId = PartyRoom.Current?.Id ?? new( 0 ),
				IsVr = VR.VRSystem.IsActive,
				InventoryBlob = Services.Inventory.CurrentBlob
			};

			//
			// Put all the userinfo vars in
			//
			foreach ( var convar in ConVarSystem.Members.Values.Where( x => x.IsUserInfo ) )
			{
				ui.UserData[convar.Name] = convar.Value;
			}

			return ui;
		}
	}
}
