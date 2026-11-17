using System.IO;
using System.Runtime.InteropServices;

namespace Sandbox.ShaderCompiler.Slang;

/// <summary>
/// The engine's <c>slang.dll</c> through its flat C API: compile requests, entry point code and reflection. Loaded by
/// path, once; function pointers rather than a generated binding, since only these few are needed.
/// </summary>
internal static unsafe class SlangApi
{
	public const int LanguageSlang = 1;
	public const uint CompileFlagNoCodegen = 1 << 4;
	public const int StageVertex = 1, StagePixel = 5, StageCompute = 6, StageMesh = 13, StageAmplification = 14;
	public const int TypeKindStruct = 1, TypeKindArray = 2, TypeKindMatrix = 3, TypeKindVector = 4, TypeKindScalar = 5;
	public const int ScalarBool = 2, ScalarInt32 = 3, ScalarUInt32 = 4, ScalarFloat32 = 8;

	static readonly object loadLock = new();
	static IntPtr library;
	static readonly Stack<IntPtr> idle = new();
	static int sessions;

	public static delegate* unmanaged< byte*, IntPtr > CreateSession;
	public static delegate* unmanaged< IntPtr, IntPtr > CreateCompileRequest;
	public static delegate* unmanaged< IntPtr, void > DestroyCompileRequest;
	public static delegate* unmanaged< IntPtr, IntPtr, void > SetFileSystem;
	public static delegate* unmanaged< IntPtr, uint, void > SetCompileFlags;
	public static delegate* unmanaged< IntPtr, byte**, int, int > ProcessCommandLineArguments;
	public static delegate* unmanaged< IntPtr, int, byte*, int > AddTranslationUnit;
	public static delegate* unmanaged< IntPtr, int, byte*, byte*, void > AddTranslationUnitSourceString;
	public static delegate* unmanaged< IntPtr, int, byte*, int, int > AddEntryPoint;
	public static delegate* unmanaged< IntPtr, int > Compile;
	public static delegate* unmanaged< IntPtr, byte* > GetDiagnosticOutput;
	public static delegate* unmanaged< IntPtr, int, nuint*, void* > GetEntryPointCode;
	public static delegate* unmanaged< IntPtr, int, int, IntPtr*, int > GetEntryPointCodeBlob;
	public static delegate* unmanaged< IntPtr, int, IntPtr*, int > GetTargetCodeBlob;
	public static delegate* unmanaged< IntPtr, IntPtr > GetReflection;
	public static delegate* unmanaged< IntPtr, IntPtr, IntPtr*, int > ReflectionToJson;
	public static delegate* unmanaged< void*, nuint, IntPtr > CreateBlob;

	public static delegate* unmanaged< IntPtr, byte*, IntPtr > FindTypeByName;
	public static delegate* unmanaged< IntPtr, int > TypeGetKind;
	public static delegate* unmanaged< IntPtr, uint > TypeGetFieldCount;
	public static delegate* unmanaged< IntPtr, uint, IntPtr > TypeGetFieldByIndex;
	public static delegate* unmanaged< IntPtr, int > TypeGetScalarType;
	public static delegate* unmanaged< IntPtr, uint > TypeGetRowCount;
	public static delegate* unmanaged< IntPtr, uint > TypeGetColumnCount;
	public static delegate* unmanaged< IntPtr, nuint > TypeGetElementCount;
	public static delegate* unmanaged< IntPtr, IntPtr > TypeGetElementType;
	public static delegate* unmanaged< IntPtr, byte* > TypeGetName;
	public static delegate* unmanaged< IntPtr, uint > TypeGetUserAttributeCount;
	public static delegate* unmanaged< IntPtr, uint, IntPtr > TypeGetUserAttribute;
	public static delegate* unmanaged< IntPtr, byte* > VariableGetName;
	public static delegate* unmanaged< IntPtr, IntPtr > VariableGetType;
	public static delegate* unmanaged< IntPtr, uint > VariableGetUserAttributeCount;
	public static delegate* unmanaged< IntPtr, uint, IntPtr > VariableGetUserAttribute;
	public static delegate* unmanaged< IntPtr, byte > VariableHasDefaultValue;
	public static delegate* unmanaged< IntPtr, float*, int > VariableGetDefaultValueFloat;
	public static delegate* unmanaged< IntPtr, long*, int > VariableGetDefaultValueInt;
	public static delegate* unmanaged< IntPtr, byte* > UserAttributeGetName;
	public static delegate* unmanaged< IntPtr, uint > UserAttributeGetArgumentCount;
	public static delegate* unmanaged< IntPtr, uint, int*, int > UserAttributeGetArgumentValueInt;
	public static delegate* unmanaged< IntPtr, uint, float*, int > UserAttributeGetArgumentValueFloat;
	public static delegate* unmanaged< IntPtr, uint, nuint*, byte* > UserAttributeGetArgumentValueString;

	/// <summary>
	/// Most global sessions made at once: each compiles on one thread at a time (Slang's sessions aren't thread-safe), so
	/// this many compiles run in parallel. Each holds Slang's core module.
	/// </summary>
	public static int MaxSessions { get; } = Math.Clamp( Environment.ProcessorCount / 2, 1, 8 );

	/// <summary>
	/// A global session of <c>slang.dll</c> in <paramref name="directory"/> (loaded on first use) for this thread alone until
	/// it's given back with <see cref="Return"/>: an idle one, or a new one while fewer than <see cref="MaxSessions"/> exist,
	/// else the next one given back.
	/// </summary>
	public static IntPtr Rent( string directory )
	{
		Load( directory );
		lock ( idle )
		{
			while ( true )
			{
				if ( idle.Count > 0 ) return idle.Pop();
				if ( sessions < MaxSessions ) break;
				System.Threading.Monitor.Wait( idle );
			}

			sessions++;
		}

		var made = CreateSession( null );
		if ( made != IntPtr.Zero ) return made;
		lock ( idle )
		{
			sessions--;
			System.Threading.Monitor.Pulse( idle );
		}

		throw new InvalidOperationException( "Slang didn't create a session" );
	}

	/// <summary>Give back a session <see cref="Rent"/> gave, for the next compile.</summary>
	public static void Return( IntPtr session )
	{
		lock ( idle )
		{
			idle.Push( session );
			System.Threading.Monitor.Pulse( idle );
		}
	}

	/// <summary>Load <c>slang.dll</c> from <paramref name="directory"/>, once.</summary>
	static void Load( string directory )
	{
		lock ( loadLock )
		{
			if ( library != IntPtr.Zero ) return;

			var name = OperatingSystem.IsWindows() ? "slang.dll" : OperatingSystem.IsMacOS() ? "libslang.dylib" : "libslang.so";
			var path = Path.Combine( directory, name );
			if ( !File.Exists( path ) ) throw new FileNotFoundException( $"The engine's Slang isn't at {path}", path );
			library = NativeLibrary.Load( path );

			CreateSession = (delegate* unmanaged< byte*, IntPtr >)Export( "spCreateSession" );
			CreateCompileRequest = (delegate* unmanaged< IntPtr, IntPtr >)Export( "spCreateCompileRequest" );
			DestroyCompileRequest = (delegate* unmanaged< IntPtr, void >)Export( "spDestroyCompileRequest" );
			SetFileSystem = (delegate* unmanaged< IntPtr, IntPtr, void >)Export( "spSetFileSystem" );
			SetCompileFlags = (delegate* unmanaged< IntPtr, uint, void >)Export( "spSetCompileFlags" );
			ProcessCommandLineArguments = (delegate* unmanaged< IntPtr, byte**, int, int >)Export( "spProcessCommandLineArguments" );
			AddTranslationUnit = (delegate* unmanaged< IntPtr, int, byte*, int >)Export( "spAddTranslationUnit" );
			AddTranslationUnitSourceString = (delegate* unmanaged< IntPtr, int, byte*, byte*, void >)Export( "spAddTranslationUnitSourceString" );
			AddEntryPoint = (delegate* unmanaged< IntPtr, int, byte*, int, int >)Export( "spAddEntryPoint" );
			Compile = (delegate* unmanaged< IntPtr, int >)Export( "spCompile" );
			GetDiagnosticOutput = (delegate* unmanaged< IntPtr, byte* >)Export( "spGetDiagnosticOutput" );
			GetEntryPointCode = (delegate* unmanaged< IntPtr, int, nuint*, void* >)Export( "spGetEntryPointCode" );
			GetEntryPointCodeBlob = (delegate* unmanaged< IntPtr, int, int, IntPtr*, int >)Export( "spGetEntryPointCodeBlob" );
			GetTargetCodeBlob = (delegate* unmanaged< IntPtr, int, IntPtr*, int >)Export( "spGetTargetCodeBlob" );
			GetReflection = (delegate* unmanaged< IntPtr, IntPtr >)Export( "spGetReflection" );
			ReflectionToJson = (delegate* unmanaged< IntPtr, IntPtr, IntPtr*, int >)Export( "spReflection_ToJson" );
			CreateBlob = (delegate* unmanaged< void*, nuint, IntPtr >)Export( "slang_createBlob" );

			FindTypeByName = (delegate* unmanaged< IntPtr, byte*, IntPtr >)Export( "spReflection_FindTypeByName" );
			TypeGetKind = (delegate* unmanaged< IntPtr, int >)Export( "spReflectionType_GetKind" );
			TypeGetFieldCount = (delegate* unmanaged< IntPtr, uint >)Export( "spReflectionType_GetFieldCount" );
			TypeGetFieldByIndex = (delegate* unmanaged< IntPtr, uint, IntPtr >)Export( "spReflectionType_GetFieldByIndex" );
			TypeGetScalarType = (delegate* unmanaged< IntPtr, int >)Export( "spReflectionType_GetScalarType" );
			TypeGetRowCount = (delegate* unmanaged< IntPtr, uint >)Export( "spReflectionType_GetRowCount" );
			TypeGetColumnCount = (delegate* unmanaged< IntPtr, uint >)Export( "spReflectionType_GetColumnCount" );
			TypeGetElementCount = (delegate* unmanaged< IntPtr, nuint >)Export( "spReflectionType_GetElementCount" );
			TypeGetElementType = (delegate* unmanaged< IntPtr, IntPtr >)Export( "spReflectionType_GetElementType" );
			TypeGetName = (delegate* unmanaged< IntPtr, byte* >)Export( "spReflectionType_GetName" );
			TypeGetUserAttributeCount = (delegate* unmanaged< IntPtr, uint >)Export( "spReflectionType_GetUserAttributeCount" );
			TypeGetUserAttribute = (delegate* unmanaged< IntPtr, uint, IntPtr >)Export( "spReflectionType_GetUserAttribute" );
			VariableGetName = (delegate* unmanaged< IntPtr, byte* >)Export( "spReflectionVariable_GetName" );
			VariableGetType = (delegate* unmanaged< IntPtr, IntPtr >)Export( "spReflectionVariable_GetType" );
			VariableGetUserAttributeCount = (delegate* unmanaged< IntPtr, uint >)Export( "spReflectionVariable_GetUserAttributeCount" );
			VariableGetUserAttribute = (delegate* unmanaged< IntPtr, uint, IntPtr >)Export( "spReflectionVariable_GetUserAttribute" );
			VariableHasDefaultValue = (delegate* unmanaged< IntPtr, byte >)Export( "spReflectionVariable_HasDefaultValue" );
			VariableGetDefaultValueFloat = (delegate* unmanaged< IntPtr, float*, int >)Export( "spReflectionVariable_GetDefaultValueFloat" );
			VariableGetDefaultValueInt = (delegate* unmanaged< IntPtr, long*, int >)Export( "spReflectionVariable_GetDefaultValueInt" );
			UserAttributeGetName = (delegate* unmanaged< IntPtr, byte* >)Export( "spReflectionUserAttribute_GetName" );
			UserAttributeGetArgumentCount = (delegate* unmanaged< IntPtr, uint >)Export( "spReflectionUserAttribute_GetArgumentCount" );
			UserAttributeGetArgumentValueInt = (delegate* unmanaged< IntPtr, uint, int*, int >)Export( "spReflectionUserAttribute_GetArgumentValueInt" );
			UserAttributeGetArgumentValueFloat = (delegate* unmanaged< IntPtr, uint, float*, int >)Export( "spReflectionUserAttribute_GetArgumentValueFloat" );
			UserAttributeGetArgumentValueString = (delegate* unmanaged< IntPtr, uint, nuint*, byte* >)Export( "spReflectionUserAttribute_GetArgumentValueString" );
		}
	}

	static IntPtr Export( string name ) => NativeLibrary.GetExport( library, name );

	/// <summary>A null-terminated UTF-8 string as a .NET string; null for a null pointer.</summary>
	public static string String( byte* text ) => text is null ? null : Marshal.PtrToStringUTF8( (IntPtr)text );

	/// <summary>An <c>ISlangBlob</c>'s bytes, then release it.</summary>
	public static byte[] ReadBlob( IntPtr blob )
	{
		if ( blob == IntPtr.Zero ) return null;
		var vtable = *(IntPtr**)blob;
		var release = (delegate* unmanaged< IntPtr, uint >)vtable[2];
		var pointer = (delegate* unmanaged< IntPtr, void* >)vtable[3];
		var size = (delegate* unmanaged< IntPtr, nuint >)vtable[4];
		var data = new ReadOnlySpan<byte>( pointer( blob ), (int)size( blob ) ).ToArray();
		release( blob );
		return data;
	}
}
