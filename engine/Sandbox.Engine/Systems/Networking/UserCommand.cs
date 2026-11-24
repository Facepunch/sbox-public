using System.Runtime.CompilerServices;

namespace Sandbox;

/// <summary>
/// An input sample sent to the host at the scene's network update rate.
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

	internal Vector2 MouseDelta;
	internal Vector2 MouseWheel;

	internal const int AnalogCount = 6;

	private InlineArray6<short> _analog;

	/// <summary>
	/// Input fields present after the command number.
	/// </summary>
	[Flags]
	private enum InputFlags : ushort
	{
		Actions = 1 << 0,
		// Bits 1–6 follow InputAnalog order.
		MouseDelta = 1 << 7,
		MouseWheel = 1 << 8,
		AimAngles = 1 << 9
	}

	private static InputFlags AnalogFlag( int index ) => (InputFlags)(1 << (index + 1));

	/// <summary>
	/// Read a normalized controller axis, or zero for an invalid axis.
	/// </summary>
	internal readonly float GetAnalog( InputAnalog analog )
	{
		var index = (int)analog;
		return index >= 0 && index < AnalogCount ? _analog[index] / (float)short.MaxValue : 0f;
	}

	/// <summary>
	/// Quantize a finite normalized axis to a signed 16-bit value.
	/// </summary>
	internal void SetAnalog( InputAnalog analog, float value )
	{
		var index = (int)analog;
		if ( index < 0 || index >= AnalogCount )
			return;

		_analog[index] = float.IsFinite( value ) ? (short)MathF.Round( Math.Clamp( value, -1f, 1f ) * short.MaxValue ) : (short)0;
	}

	/// <summary>
	/// Serialize this <see cref="UserCommand"/> to the specified <see cref="ByteStream"/>.
	/// </summary>
	internal void Serialize( ref ByteStream bs )
	{
		InputFlags flags = 0;

		if ( Actions != 0 )
			flags |= InputFlags.Actions;

		for ( var i = 0; i < AnalogCount; i++ )
		{
			if ( _analog[i] != 0 )
				flags |= AnalogFlag( i );
		}

		if ( MouseDelta != Vector2.Zero )
			flags |= InputFlags.MouseDelta;

		if ( MouseWheel != Vector2.Zero )
			flags |= InputFlags.MouseWheel;

		if ( AimAngles.HasValue )
			flags |= InputFlags.AimAngles;

		bs.Write( flags );
		bs.Write( CommandNumber );

		if ( (flags & InputFlags.Actions) != 0 )
			bs.Write( Actions );

		for ( var i = 0; i < AnalogCount; i++ )
		{
			if ( (flags & AnalogFlag( i )) != 0 )
				bs.Write( _analog[i] );
		}

		if ( (flags & InputFlags.MouseDelta) != 0 )
			bs.Write( MouseDelta );

		if ( (flags & InputFlags.MouseWheel) != 0 )
			bs.Write( MouseWheel );

		if ( AimAngles is { } aim )
			bs.Write( aim );
	}

	/// <summary>
	/// Deserialize this <see cref="UserCommand"/> from specified <see cref="ByteStream"/>.
	/// </summary>
	internal void Deserialize( ref ByteStream bs )
	{
		this = default;
		var flags = bs.Read<InputFlags>();
		CommandNumber = bs.Read<uint>();

		if ( (flags & InputFlags.Actions) != 0 )
			Actions = bs.Read<ulong>();

		for ( var i = 0; i < AnalogCount; i++ )
		{
			if ( (flags & AnalogFlag( i )) != 0 )
				_analog[i] = Math.Max( bs.Read<short>(), (short)-short.MaxValue );
		}

		var delta = (flags & InputFlags.MouseDelta) != 0 ? bs.Read<Vector2>() : default;
		var wheel = (flags & InputFlags.MouseWheel) != 0 ? bs.Read<Vector2>() : default;
		MouseDelta = delta.IsFinite ? delta : default;
		MouseWheel = wheel.IsFinite ? wheel : default;

		if ( (flags & InputFlags.AimAngles) != 0 )
		{
			var aim = bs.Read<Angles>();
			if ( aim.AsVector3().IsFinite )
				AimAngles = aim.Normal;
		}
	}

	/// <summary>
	/// Reset command numbering for a new host.
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
