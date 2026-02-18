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
#if WIN
		internal const int StructPlatformPackSize = 8;
		internal const string LibraryName = "steam_api64";
#else
		internal const int StructPlatformPackSize = 4;
		internal const string LibraryName = "steam_api";
#endif

		internal const CallingConvention CC = CallingConvention.Cdecl;
		internal const int StructPackSize = 4;
	}
}
