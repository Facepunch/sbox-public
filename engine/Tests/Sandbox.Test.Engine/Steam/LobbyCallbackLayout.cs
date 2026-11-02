using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Steamworks;
using Steamworks.Data;

namespace SteamTests;

[TestClass]
public class LobbyCallbackLayout
{
	[TestMethod]
	public unsafe void CreatedLobbyPreservesNativeSteamId()
	{
		const ulong lobbyId = 109775244746026750;
		var idOffset = OperatingSystem.IsWindows() ? 8 : 4;
		var nativeSize = idOffset + sizeof( ulong );
		Span<byte> data = stackalloc byte[16];
		data.Clear();
		BinaryPrimitives.WriteInt32LittleEndian( data, (int)Result.OK );
		BinaryPrimitives.WriteUInt64LittleEndian( data[idOffset..], lobbyId );

		fixed ( byte* ptr = data )
		{
			var result = Marshal.PtrToStructure<LobbyCreated_t>( (IntPtr)ptr );
			Assert.AreEqual( Result.OK, result.Result );
			Assert.AreEqual( lobbyId, result.SteamIDLobby );
			Assert.AreEqual( nativeSize, result.DataSize );
		}
	}

	/// <summary>
	/// Shipped Linux builds use the Windows-built managed assemblies, so both layouts must
	/// decode no matter which OS compiled or runs the test.
	/// </summary>
	[TestMethod]
	[DataRow( true, 4 )]
	[DataRow( false, 8 )]
	public unsafe void CreatedLobbyDecodesBothPackings( bool packSmall, int idOffset )
	{
		const ulong lobbyId = 109775244746026750;
		Span<byte> data = stackalloc byte[16];
		data.Clear();
		BinaryPrimitives.WriteInt32LittleEndian( data, (int)Result.OK );
		BinaryPrimitives.WriteUInt64LittleEndian( data[idOffset..], lobbyId );

		fixed ( byte* ptr = data )
		{
			var result = Marshal.PtrToStructure<LobbyCreated_t>( (IntPtr)ptr );
			Assert.AreEqual( Result.OK, result.Result );
			Assert.AreEqual( lobbyId, result.GetSteamIDLobby( packSmall ) );
		}
	}

	[TestMethod]
	public unsafe void EnteredLobbyMatchesNativeCallbackSize()
	{
		const ulong lobbyId = 109775244746026750;
		Span<byte> data = stackalloc byte[24];
		data.Clear();
		BinaryPrimitives.WriteUInt64LittleEndian( data, lobbyId );
		data[12] = 1;
		BinaryPrimitives.WriteUInt32LittleEndian( data[16..], (uint)RoomEnter.Success );

		fixed ( byte* ptr = data )
		{
			var result = Marshal.PtrToStructure<LobbyEnter_t>( (IntPtr)ptr );
			Assert.AreEqual( lobbyId, result.SteamIDLobby );
			Assert.IsTrue( result.Locked );
			Assert.AreEqual( (uint)RoomEnter.Success, result.EChatRoomEnterResponse );
			Assert.AreEqual( OperatingSystem.IsWindows() ? 24 : 20, result.DataSize );
		}
	}
}
