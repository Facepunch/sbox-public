namespace Sandbox;

/// <summary>
/// A User Command that will be sent to the current host every tick.
/// </summary>
internal struct UserCommand( uint commandNumber )
{
	private static uint s_nextAvailableCommandNumber = 1;

	/// <summary>
	/// The command number of this <see cref="UserCommand"/>.
	/// </summary>
	public uint CommandNumber { get; private set; } = commandNumber;

	/// <summary>
	/// Which actions are currently being held down.
	/// </summary>
	public ulong Actions;

	/// <summary>
	/// World-space view angles of the local player, when available.
	/// </summary>
	internal Angles? AimAngles;

	/// <summary>
	/// Serialize this <see cref="UserCommand"/> to the specified <see cref="ByteStream"/>.
	/// </summary>
	internal void Serialize( ref ByteStream bs )
	{
		bs.Write( CommandNumber );
		bs.Write( Actions );

		if ( AimAngles is { } aim )
			bs.Write( aim );
	}

	/// <summary>
	/// Deserialize this <see cref="UserCommand"/> from specified <see cref="ByteStream"/>.
	/// </summary>
	internal void Deserialize( ref ByteStream bs )
	{
		CommandNumber = bs.Read<uint>();
		Actions = bs.Read<ulong>();
		AimAngles = bs.TryRead( out Angles aim ) && float.IsFinite( aim.pitch ) && float.IsFinite( aim.yaw ) && float.IsFinite( aim.roll )
			? aim.Normal : null;
	}

	/// <summary>
	/// Reset the next available command number back to zero.
	/// </summary>
	internal static void Reset()
	{
		s_nextAvailableCommandNumber = 1;
	}

	/// <summary>
	/// Create a new <see cref="UserCommand"/> with the next available command number.
	/// </summary>
	/// <returns></returns>
	public static UserCommand Create()
	{
		return new UserCommand( s_nextAvailableCommandNumber++ );
	}
}
