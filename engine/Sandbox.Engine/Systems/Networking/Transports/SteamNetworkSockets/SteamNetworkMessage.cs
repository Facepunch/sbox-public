namespace Sandbox.Network;

internal unsafe struct SteamNetworkMessage
{
	public byte* Data;
	public int Size;

	public HSteamNetConnection Connection;

	public int IdentityType;
	public int IdentitySize; // 16 = steam
	public ulong IdentitySteamId;
	public fixed int IdentityPadding[32 - 2]; // -2 here for the steamid, their entity struct is a pain

	public ulong ConnectionData;
	public long TimeReceived;

	public long MessageNumber;

	public nint FreeDataFnPtr;
	public nint ReleaseFnPtr;

	public int Channel;
	public int Flags;
	public long UserData;
}

internal struct IncomingSteamMessage
{
	public HSteamNetConnection Connection { get; set; }
	public byte[] Data { get; set; }
}

internal struct OutgoingSteamMessage
{
	public HSteamNetConnection Connection { get; set; }
	public int Channel { get; set; }
	public NetPacket Packet { get; set; }
	public int Flags { get; set; }
}
