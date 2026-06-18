using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Facepunch.InteropGen;

/// <summary>
/// A class exposed across the interop boundary - either a native class we import into managed, or a
/// managed class we export to native. Holds its names/namespaces, base class, functions and variables.
/// </summary>
public class Class
{
	private static readonly Regex ClassParseRegex = new(
		@"([\w<>\*.:\(\)]+)( [\s+]?as [\s+]?([\w.:]+))?(.+)?",
		RegexOptions.IgnoreCase
	);

	private static readonly Regex ExtraParseRegex = new(
		@"[\s+]?: [\s+]?([\w.:]+)",
		RegexOptions.IgnoreCase
	);

	private static readonly char[] NamespaceSeparators = ['.', ':'];

	public string NativeName { get; set; }
	public string ManagedName { get; set; }

	public string NativeNamespace { get; set; }
	public string ManagedNamespace { get; set; }
	public bool Native { get; private set; }
	public bool Static { get; private set; }
	public bool Accessor { get; private set; }

	public Class BaseClass { get; internal set; }
	public string BaseClassName { get; private set; }

	public int ClassDepth { get; set; }

	public string NativeNameWithNamespace => string.IsNullOrEmpty( NativeNamespace ) ? NativeName : $"{NativeNamespace}::{NativeName}";
	public string ManagedNameWithNamespace => string.IsNullOrEmpty( ManagedNamespace ) ? ManagedName : $"{ManagedNamespace}.{ManagedName}";

	public List<Function> Functions = [];
	public List<Variable> Variables = [];

	internal static Class Parse( bool isNative, bool isStatic, string type, string line )
	{
		Match match = ClassParseRegex.Match( line );
		if ( !match.Success )
		{
			Log.Warning( $"Couldn't parse class definition: {line}" );
			return null;
		}

		string className = match.Groups[1].Value.Trim();
		string aliasName = match.Groups[3].Value.Trim();
		string extraInfo = match.Groups[4].Value;

		if ( aliasName == className )
		{
			Log.Warning( $"Redundant 'as' on class {className}" );
		}

		if ( string.IsNullOrWhiteSpace( aliasName ) )
		{
			aliasName = className;
		}

		if ( !isNative )
		{
			(aliasName, className) = (className, aliasName);
		}

		Class f = new()
		{
			Native = isNative,
			Accessor = type == "accessor"
		};
		f.Static = isStatic || f.Accessor;
		f.NativeName = GetClassName( className );
		f.ManagedName = GetClassName( aliasName );
		f.NativeNamespace = GetNamespace( className ).Replace( ".", "::" );
		f.ManagedNamespace = GetNamespace( aliasName );
		f.ParseExtra( extraInfo );

		return f;
	}

	public List<string> Attributes = [];

	internal void TakeAttributes( List<string> attributes, Definition owner )
	{
		Attributes.AddRange( attributes );
		attributes.Clear();

		if ( IsHandleType )
		{
			Class c = new()
			{
				Native = false,
				Accessor = false,
				Static = true,
				NativeName = GetClassName( HandleIndex ),
				ManagedName = GetClassName( HandleIndex ),
				NativeNamespace = GetNamespace( HandleIndex ).Replace( ".", "::" ) + $"::{owner.Ident}",
				ManagedNamespace = GetNamespace( HandleIndex )
			};

			owner.Classes.Add( c );
		}

		if ( IsResourceHandle )
		{
			string strong = $"{ResourceHandleName}Strong";

			void AddHandleFunction( string name, Arg returnArg, string nativeCall, bool nogc )
			{
				Function func = new()
				{
					Name = name,
					Class = this,
					NativeCallReplacement = nativeCall
				};

				if ( returnArg != null )
				{
					func.Return = returnArg;
				}

				if ( nogc )
				{
					func.Attributes.Add( "nogc" );
				}

				Functions.Add( func );
			}

			// Destroy the handle
			AddHandleFunction( "DestroyStrongHandle", null, $"delete (({strong}*)self);", false );

			// Does the handle have data
			AddHandleFunction( "IsStrongHandleValid", new ArgBool(), $"return (({strong}*)self)->HasData();", true );

			// Is the handle errored
			AddHandleFunction( "IsError", new ArgBool(), $"return (({strong}*)self)->IsError();", true );

			// Has the handle loaded
			AddHandleFunction( "IsStrongHandleLoaded", new ArgBool(), $"return (({strong}*)self)->IsLoaded();", true );

			// Create a copy of the handle
			AddHandleFunction( "CopyStrongHandle", new ArgDefinedClass( this, "return", null ), $"return new {strong}( ( ({strong}*) self)->GetHandle() );", true );

			// Get a pointer to the binding
			AddHandleFunction( "GetBindingPtr", new ArgPointer(), $"return (({strong}*) self)->GetBinding();", true );
		}
	}

	internal bool HasAttribute( string name )
	{
		return Attributes.Contains( name );
	}

	internal int NativeOrder()
	{
		// hack hack hack
		// put static classes last because they might rely on real classes
		// but the real classes won't rely on the static classes.
		return Static ? 1000 : BaseClasses.Count();
	}

	/// <summary>
	/// Parses the extra information at the end of a class name. Usually " : BaseClass" if anything.
	/// </summary>
	private void ParseExtra( string value )
	{
		if ( string.IsNullOrWhiteSpace( value ) )
		{
			return;
		}

		Match baseclass = ExtraParseRegex.Match( value );
		if ( baseclass.Success )
		{
			BaseClassName = baseclass.Groups[1].Value.Trim();
		}
	}

	private static string GetNamespace( string name )
	{
		int index = name.LastIndexOfAny( NamespaceSeparators );
		return index <= 0 ? "" : name[..index].Trim( '.', ':' );
	}

	private static string GetClassName( string name )
	{
		int index = name.LastIndexOfAny( NamespaceSeparators );
		return index <= 0 ? name : name[index..].Trim( '.', ':' );
	}

	public IEnumerable<Arg> SelfArg( bool native, bool memberIsStatic )
	{
		if ( Static || Accessor || memberIsStatic )
		{
			yield break;
		}

		yield return Native
			? new ArgPointer { Name = "self", IsSelf = true }
			: new ArgUInt { Name = native ? "m_ObjectId" : "self", IsSelf = true };
	}

	public List<Class> Children { get; set; }

	public bool DerivesFrom( Class c )
	{
		return c == this || (BaseClass != null && BaseClass.DerivesFrom( c ));
	}

	public IEnumerable<Class> BaseClasses
	{
		get
		{
			Class c = BaseClass;

			while ( c != null )
			{
				yield return c;
				c = c.BaseClass;
			}
		}
	}

	public bool IsHandleType => Attributes.Any( x => x.StartsWith( "Handle:" ) );
	public bool IsChildHandleType => BaseClass != null && BaseClass.IsHandleType;
	public string HandleIndex => Attributes.First( x => x.StartsWith( "Handle:" ) )["Handle:".Length..];

	public bool IsResourceHandle => Attributes.Any( x => x.StartsWith( "ResourceHandle:" ) );
	public string ResourceHandleName => Attributes.First( x => x.StartsWith( "ResourceHandle:" ) )["ResourceHandle:".Length..];
}
