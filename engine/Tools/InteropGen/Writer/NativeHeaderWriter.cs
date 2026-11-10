using System;
using System.Collections.Generic;
using System.Linq;

namespace Facepunch.InteropGen;

/// <summary>
/// Emits the native (C++) header: the declarations of the managed classes native imports, split into
/// one #include'd sub-file per class.
/// </summary>
internal class NativeHeaderWriter : BaseWriter
{
	public NativeHeaderWriter( Definition definitions, string targetName ) : base( definitions, targetName )
	{
	}

	public override void Generate()
	{
		string headerDef = "_" + definitions.Filename.Replace( ".def", "" ).Replace( ".", "_" ).ToUpper() + "_H";

		WriteLine( $"#ifndef {headerDef}" );
		WriteLine( $"#define {headerDef}" );
		WriteLine( $"#pragma once" );
		WriteLine( "" );

		foreach ( string inc in definitions.Includes.Distinct() )
		{
			if ( Skip.ShouldSkipInclude( inc ) )
			{
				continue;
			}

			WriteLine( $"#include \"{inc}\"" );
		}


		{

			WriteLine( "" );

			StartBlock( "namespace Sandbox" );
			WriteLine( "class IHost;" );
			EndBlock();

			WriteLine( "" );

			string[] namespaces = definitions.ManagedNamespace.Split( "." );

			foreach ( string ns in namespaces )
			{
				StartBlock( $"namespace {ns}" );
			}

			{
				WriteLine( "" );
				WriteLine( "" );
				WriteLine( "//" );
				WriteLine( "// Will return true if all the binds are setup and ready to call" );
				WriteLine( "// Will return false after application shutdown." );
				WriteLine( "//" );
				WriteLine( "bool IsReady();" );
				WriteLine( "" );
				WriteLine( "//" );
				WriteLine( "// Will set ready to false, indicating that managed bindings are no longer available." );
				WriteLine( "// This is called after application shutdown." );
				WriteLine( "//" );
				WriteLine( "void Shutdown();" );
			}
			foreach ( string ns in namespaces )
			{
				EndBlock();
			}
		}

		Imports();

		WriteLine( "" );
		WriteLine( "#endif" );
	}

	private void Imports()
	{
		WriteLine( "//" );
		WriteLine( "// IMPORTS" );
		WriteLine( "// " );
		WriteLine( "// Functions that we're getting from managed" );
		WriteLine( "//" );
		WriteLine();

		foreach ( Class c in definitions.ManagedClasses.OrderBy( x => x.NativeOrder() ) )
		{
			if ( Skip.ShouldSkip( c ) )
			{
				continue;
			}

			StartSubFile();

			WriteLine( $"#pragma once" );
			WriteLine( $"" );

			foreach ( string ns in c.NativeNamespace.Split( ":", StringSplitOptions.RemoveEmptyEntries ) )
			{
				StartBlock( $"namespace {ns}" );
			}

			string baseclass = c.BaseClass != null ? $" : public {c.BaseClass.NativeNameWithNamespace}" : "";

			StartBlock( $"class {c.NativeName}{baseclass}" );
			{
				WriteLine( "public:" );
				Indent++;

				if ( !c.Static )
				{
					WriteLine( $"{c.NativeName}() {{ m_ObjectId = 0;  }}" );
					WriteLine( $"{c.NativeName}( unsigned int id ) {{ m_ObjectId = id;  }}" );
					WriteLine( $"unsigned int m_ObjectId = 0;" );
					WriteLine( $"operator unsigned int() const {{ return m_ObjectId; }}" );
					WriteLine( $"unsigned int ptr(){{ return m_ObjectId; }}" );
					WriteLine( $"bool HasObject(){{ return m_ObjectId > 0; }}" );
				}

				foreach ( Function f in c.Functions )
				{
					string sttic = (c.Static || f.Static) ? "static " : "";
					string pv = (c.Static || f.Static) ? "" : " const";

					IEnumerable<string> nativeArgs = f.Parameters.Select( x => $"{x.NativeType} {x.Name}" );
					string nativeArgS = string.Join( ",", nativeArgs );

					WriteLine( $"{sttic}{f.Return.NativeType} {f.Name}( {nativeArgS} ){pv};" );
				}

				Indent--;
			}
			EndBlock( ";" );

			foreach ( string ns in c.NativeNamespace.Split( ":", StringSplitOptions.RemoveEmptyEntries ) )
			{
				EndBlock();
			}

			WriteLine();

			string fileOut = EndSubFile( c.NativeName.ToLower() );
			WriteLine( $"#include \"{fileOut}\"" );
		}
	}

}
