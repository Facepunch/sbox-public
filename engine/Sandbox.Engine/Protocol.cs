namespace Sandbox.Engine;

/// <summary>
/// A centralized place to access the protocols
/// </summary>
public static class Protocol
{
	/// <summary>
	/// We cannot play packages with an Api version higher than this.
	/// </summary>
	public static int Api => 24;

	/// <summary>
	/// We cannot talk to servers or clients with a network protocol different to this.
	/// </summary>
	public static int Network => 1099;
}

// Api Versions
// 24. 21 January 2026 - Weekly update
// 21. 12 July 2025 - Monthly update
// 20. 01 July 2025 - Monthly update
// 19. 06 May 2025 - New prefabs
// 18. 10 Mar 2025 - First staging version 


// Network Versions
// 1099. 24th Feburary 2026 - ResourceId is now a long
// 1098. 20th Feburary 2026 - Send network time as double
// 1097. 13rd January 2026 - Support for binary blobs
// 1096. 05th December 2025 - NetworkFlags + Transform Sync Flags
// 1095. 23rd November 2025 - Snapshot parent salt
// 1094. 10th November 2025 - Network visibility
// 1093. 13th October 2025 - LZ4 compression
// 1092. 1st October 2025 - Networking optimizations
// 1091. 5th Sept 2025 - FastHash changes
// 1090. 12th August - Delta snapshot changes
// 1087. 14 May 2025 - GameObject (reference) ByteReader/Writer improvements
// 1086. 8 May 2025 - Server time changed to double
// 1085. 10 Mar 2025 - First staging version 
