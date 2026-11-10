using System;
using System.Collections.Generic;
using System.Linq;

namespace Facepunch.InteropGen;

/// <summary>
/// One argument (parameter, return value or variable type) and the rules for marshalling it across
/// the boundary. Each concrete type knows its managed/native type names and how to convert a value
/// <see cref="ToInterop"/> / <see cref="FromInterop"/> on each <see cref="Side"/>. Parsed from a .def
/// token by <see cref="Parse"/>.
/// </summary>
public class Arg
{
	/// <summary>
	/// Maps a [TypeName] string (e.g. "int", "float") to the Arg type that handles it.
	/// Built once from reflection and only read afterwards.
	/// </summary>
	private static readonly Dictionary<string, Type> TypesByName = BuildTypeMap();

	private static Dictionary<string, Type> BuildTypeMap()
	{
		Dictionary<string, Type> map = new( StringComparer.OrdinalIgnoreCase );

		foreach ( Type t in typeof( Arg ).Assembly.GetTypes().Where( x => typeof( Arg ).IsAssignableFrom( x ) ) )
		{
			foreach ( TypeNameAttribute a in t.GetCustomAttributes( typeof( TypeNameAttribute ), false ) )
			{
				_ = map.TryAdd( a.TypeName, t );
			}
		}

		return map;
	}

	public bool IsSelf { get; set; }
	public virtual string Name { get; set; }

	/// <summary>
	/// The wrapper type applied to arrays (ArgArray), or null for a plain argument.
	/// </summary>
	public Type ArrayWrapper { get; private set; }
	public string[] Flags { get; set; }

	/// <summary>
	/// The managed class whose helpers marshal strings across the boundary.
	/// </summary>
	internal const string StringTools = "Sandbox.Interop";

	public bool HasFlag( string flag )
	{
		return Flags != null && Flags.Contains( flag );
	}

	public virtual string ManagedType => "!!UNKNOWN!!";
	public virtual string NativeType => ManagedType;
	public virtual string ManagedDelegateType => ManagedType;
	public virtual string NativeDelegateType => NativeType;
	public virtual bool IsVoid => false;
	public virtual string DefaultValue => "0";
	public bool IsReturn => Name == "returnvalue";

	/// <summary>
	/// The type used in the unmanaged function-pointer signature for this argument, for the given
	/// side and direction. Defaults to ManagedDelegateType / NativeDelegateType; types with
	/// direction-sensitive marshalling (structs, handles, pointers) override this.
	/// </summary>
	public virtual string DelegateType( Side side, Dir dir )
	{
		return side == Side.Managed ? ManagedDelegateType : NativeDelegateType;
	}

	/// <summary>
	/// If set to false it won't be provided as an argument.
	/// This is used for things like passing literals
	/// </summary>
	public virtual bool IsRealArgument => true;

	public virtual string ToInterop( Side side, string code = null )
	{
		code ??= Name;
		return code;
	}

	public virtual string FromInterop( Side side, string code = null )
	{
		code ??= Name;
		return code;
	}

	/// <summary>
	/// Parse a single argument like "const char* name" or "[literal]" into an Arg.
	/// </summary>
	internal static Arg Parse( string line )
	{
		string type = line.Trim();

		// A literal, e.g. [true] - passed straight through, not a real argument
		if ( type.StartsWith( '[' ) && type.EndsWith( ']' ) )
		{
			return new ArgLiteral( type[1..^1] );
		}

		// The name is whatever follows the last space
		string name = "none";
		int lastSpaceIndex = type.LastIndexOf( ' ' );
		if ( lastSpaceIndex > 0 )
		{
			name = type[(lastSpaceIndex + 1)..];
			type = type[..lastSpaceIndex];
		}

		if ( type == "const" )
		{
			throw new Exception( "Invalid argument" );
		}

		// Anything still before a space is a flag (out, ref, const, etc.)
		string[] flags = null;
		if ( type.Contains( ' ' ) )
		{
			int lastSpace = type.LastIndexOf( ' ' );
			flags = type[..lastSpace].Split( ' ', StringSplitOptions.RemoveEmptyEntries );
			type = type[(lastSpace + 1)..];
		}

		// A trailing [] makes it an array
		Type arrayWrapper = null;
		if ( type.EndsWith( "[]" ) )
		{
			type = type[..^2];
			arrayWrapper = typeof( ArgArray );
		}

		if ( TypesByName.TryGetValue( type, out Type argType ) )
		{
			Arg arg = (Arg)Activator.CreateInstance( argType );
			arg.Name = name;
			arg.ArrayWrapper = arrayWrapper;
			arg.Flags = flags;

			return arg.Wrap( arg );
		}

		return new ArgUnknown
		{
			Type = type,
			Name = name,
			ArrayWrapper = arrayWrapper,
			Flags = flags
		};
	}

	/// <summary>
	/// Wraps a call expression into the statement that returns its result, e.g.
	/// <c>funccall( args )</c> becomes <c>return funccall( args );</c>. Types that need to convert the
	/// return value (strings, structs, casts) override this.
	/// </summary>
	public virtual string ReturnWrapCall( string functionCall, Side side )
	{
		return $"return {functionCall};";
	}

	/// <summary>
	/// Parse a comma-separated parameter list into its arguments.
	/// </summary>
	internal static Arg[] ParseMany( string value )
	{
		if ( string.IsNullOrWhiteSpace( value ) )
		{
			return Array.Empty<Arg>();
		}

		return value.Split( ',', StringSplitOptions.RemoveEmptyEntries )
			.Select( x => Parse( x.Trim() ) )
			.ToArray();
	}

	public virtual string WrapFunctionCall( string functionCall, Side side )
	{
		return functionCall;
	}

	/// <summary>
	/// True when <see cref="WrapFunctionCall"/> wraps the managed call in real code (string
	/// marshalling, try/finally). Wrappers with a fat body like that aren't worth force-inlining.
	/// </summary>
	public virtual bool WrapsManagedCall => false;

	/// <summary>
	/// Wrap a freshly-parsed argument: arrays get their ArrayWrapper, everything else gets the
	/// flags wrapper that applies out/ref/cast/etc.
	/// </summary>
	public Arg Wrap( Arg arg )
	{
		return ArrayWrapper != null ? (Arg)Activator.CreateInstance( ArrayWrapper, arg ) : new ArgFlagsWrapper( arg );
	}
}
