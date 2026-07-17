using System;
using System.Linq;

namespace Facepunch.InteropGen;

/// <summary>
/// Resolves a parsed <see cref="Definition"/>: links base classes, pulls inherited functions down,
/// and converts every <see cref="ArgUnknown"/> into a concrete <see cref="Arg"/> by looking the type
/// up among the known classes / structs / function pointers / delegates.
/// </summary>
internal static class TypeResolver
{
	public static void Resolve( Definition d )
	{
		foreach ( Class c in d.Classes )
		{
			//
			// This class has a baseclass, find the actual class and set it.
			//
			if ( !string.IsNullOrWhiteSpace( c.BaseClassName ) )
			{
				c.BaseClass = d.Classes.FirstOrDefault( x => x.ManagedNameWithNamespace == c.BaseClassName );

				c.BaseClass ??= d.Classes.SingleOrDefault( x => x.ManagedName == c.BaseClassName );

				if ( c.BaseClass == null )
				{
					throw new Exception( $"{c.ManagedNameWithNamespace} has unknown baseclass: {c.BaseClassName}" );
				}
			}

			Class baseclass = c.BaseClass;

			while ( baseclass != null )
			{
				c.ClassDepth++;
				c.Functions.AddRange( baseclass.Functions.Where( x => !x.Static ) );
				baseclass = baseclass.BaseClass;
			}

			//
			// Fix the functions up
			//
			foreach ( Function f in c.Functions )
			{
				try
				{
					f.Return = ResolveArg( d, f.Return );

					for ( int i = 0; i < f.Parameters.Length; i++ )
					{
						f.Parameters[i] = ResolveArg( d, f.Parameters[i] );
					}
				}
				catch ( System.Exception e )
				{
					Log.Warning( $"[{e.Message}] in {c.ManagedNameWithNamespace}.{f.Name}" );
					throw new Exception( $"Unhandled Type [{e.Message}] in {c.ManagedNameWithNamespace}.{f.Name}" );
				}
			}

			//
			// Fix the variables up
			//
			foreach ( Variable v in c.Variables )
			{
				try
				{
					v.Return = ResolveArg( d, v.Return );
				}
				catch ( System.Exception e )
				{
					Log.Warning( $"[{e.Message}] in {c.ManagedNameWithNamespace}.{v.Name}" );
					throw new Exception( $"Unhandled Type [{e.Message}] in {c.ManagedNameWithNamespace}.{v.Name}" );
				}
			}
		}

		foreach ( Class c in d.Classes )
		{
			c.Functions = c.Functions.Distinct().Select( x => x.Copy() ).ToList();
			c.Children = d.Classes.Where( x => x.DerivesFrom( c ) ).ToList();
		}
	}

	private static Arg ResolveArg( Definition d, Arg arg )
	{
		if ( arg is not ArgUnknown au )
		{
			return arg;
		}

		// A class is wrapped differently depending on whether it lives natively or in managed.
		Arg AsClass( Class c )
		{
			if ( c == null )
			{
				return null;
			}

			return c.Native ? ClassArgument( au, c ) : au.Wrap( new ArgManagedClass( c, au.Name, au.Flags ) );
		}

		Arg AsStruct( Struct s )
		{
			return s == null ? null : au.Wrap( s.IsEnum ? new ArgEnum( s, au.Name ) : new ArgDefinedStruct( s, au.Name, au.Flags ) );
		}

		// Resolve by full name first, then by short name, then delegates.
		return AsClass( d.Classes.SingleOrDefault( x => x.ManagedNameWithNamespace == au.Type ) )
			?? AsStruct( d.Structs.SingleOrDefault( x => x.ManagedNameWithNamespace == au.Type ) )
			?? AsClass( d.Classes.SingleOrDefault( x => x.ManagedName == au.Type ) )
			?? AsStruct( d.Structs.SingleOrDefault( x => x.ManagedName == au.Type || x.NativeNameWithNamespace == au.Type || x.NativeName == au.Type ) )
			?? (d.Delegates.Contains( au.Type ) ? au.Wrap( new ArgDelegate( au.Type, au.Name, au.Flags ) )
				: throw new System.Exception( $"Unknown Type {au.Type}" ));
	}

	private static Arg ClassArgument( ArgUnknown au, Class c )
	{
		return c.HasAttribute( "SharedDataPointer" )
			? au.Wrap( new ArgSharedDataPointer( c, au.Name, au.Flags ) )
			: au.Wrap( new ArgDefinedClass( c, au.Name, au.Flags ) );
	}
}
