using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;

namespace Steamworks
{
	internal static class Platform
	{
#if STEAMWORKS_POSIX
		internal const int StructPlatformPackSize = 4;
#else
		internal const int StructPlatformPackSize = 8;
#endif
		internal const string LibraryName = "steam_api64";

		internal const CallingConvention CC = CallingConvention.Cdecl;
		internal const int StructPackSize = 4;
	}
}
