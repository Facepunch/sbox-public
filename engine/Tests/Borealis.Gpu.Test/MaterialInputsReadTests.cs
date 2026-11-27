using Borealis.Source2;
using Sandbox.ShaderCompiler;
using System.IO;
using System.Text.RegularExpressions;

namespace BorealisGpuTests;

/// <summary>
/// The cheaper reads of a module's material inputs: <see cref="MaterialInputs.Code"/> made in one pass reads as the two
/// comment regexes did, and <see cref="MaterialInputs.ReadAttributes"/> as <see cref="MaterialInputs.Read"/>'s attributes.
/// Compiles nothing; no device.
/// </summary>
[TestClass]
public class MaterialInputsReadTests
{
	static string Shaders()
	{
		var core = ContentFiles.FindGame().Roots.FirstOrDefault( r => File.Exists( Path.Combine( r, "shaders", "sbox", "pass.slangh" ) ) );
		if ( core is null ) Assert.Inconclusive( "No core shaders" );
		return Path.Combine( core, "shaders" );
	}

	/// <summary>What <see cref="MaterialInputs.Code"/> made before: joined, block comments a space, then line comments out.</summary>
	static string RegexCode( IReadOnlyList<string> lines )
	{
		var text = string.Join( "\n", lines );
		text = Regex.Replace( text, @"/\*.*?\*/", " ", RegexOptions.Singleline );
		return Regex.Replace( text, @"//[^\n]*", "" );
	}

	[TestMethod]
	public void CodeReadsAsTheCommentRegexes()
	{
		string[][] cases =
		[
			[],
			[""],
			["", ""],
			["float a; // note", "float b;"],
			["/* one */ float a; /* two", "lines */ float b;"],
			["// line /* opens", "float a; */ float b;"],
			["/* a // b */ float c; // d"],
			["/*/ float a; */ b"],
			["/**/x/***/y/* */z"],
			["float a; /* never closed", "float b; // still"],
			["a / b * c // d", "e /", "* f */ g"],
			["///", "//", "/", "*/"],
			["Attribute( \"x//y\" ); /* \"*/\" */"],
		];

		foreach ( var lines in cases )
			Assert.AreEqual( RegexCode( lines ), MaterialInputs.Code( lines ), string.Join( "\\n", lines ) );
	}

	[TestMethod]
	public void CodeOfEveryCoreModuleReadsAsTheCommentRegexes()
	{
		var shaders = Shaders();
		foreach ( var path in Directory.GetFiles( shaders, "*.slang*", SearchOption.AllDirectories ) )
		{
			var lines = File.ReadAllText( path ).Replace( "\r\n", "\n" ).Split( '\n' );
			Assert.AreEqual( RegexCode( lines ), MaterialInputs.Code( lines ), path );
		}
	}

	/// <summary>Every core module's attributes, read alone, are the ones the whole read finds, for a few sets of feature values.</summary>
	[TestMethod]
	public void AttributesReadAloneAreTheWholeReadsAttributes()
	{
		var shaders = Shaders();
		Dictionary<string, int>[] sets =
		[
			new(),
			new() { ["F_TRANSLUCENT"] = 1, ["F_ALPHA_TEST"] = 1, ["F_RENDER_BACKFACES"] = 1 },
			new( StringComparer.OrdinalIgnoreCase ) { ["f_additive_blend"] = 1, ["F_TEXTURED"] = 1, ["F_OVERLAY"] = 1 },
		];

		var read = 0;
		foreach ( var path in Directory.GetFiles( shaders, "*.slang" ) )
		{
			PassModule module;
			try
			{
				module = PassModule.Read( path, [shaders] );
			}
			catch ( Exception )
			{
				continue;
			}

			foreach ( var features in sets )
			{
				MaterialInputs whole;
				try
				{
					whole = MaterialInputs.Read( module, features );
				}
				catch ( Exception )
				{
					continue;
				}

				var alone = MaterialInputs.ReadAttributes( module, features );
				Assert.AreEqual( Describe( whole.Attributes ), Describe( alone ), $"{path} {string.Join( ",", features )}" );
				read++;
			}
		}

		Assert.IsTrue( read > 10, $"only {read} reads" );
	}

	/// <summary>The directives as the preprocessor reads them in place: defines with and without values, function-like ones, undefs and includes.</summary>
	[TestMethod]
	public void DirectivesReadInPlace()
	{
		var text = string.Join( "\n",
			"#define PLAIN",
			"#  define VALUED   3   // three",
			"#define CALLED( x ) x",
			"#define =broken",
			"#ifdef PLAIN /* yes */",
			"plain",
			"#endif",
			"#if VALUED == 3 && CALLED",
			"valued called",
			"#elif 1",
			"not this",
			"#else",
			"nor this",
			"#endif",
			"#undef PLAIN",
			"#ifndef PLAIN",
			"undefined",
			"#endif",
			"#include \"quoted.h\"",
			"#include <angled.h> // comment",
			"#include \"\"",
			"#if MISSING",
			"#include \"never.h\"",
			"#endif",
			"#  ",
			"#pragma once",
			"last" );

		var included = new List<string>();
		var lines = PassPreprocessor.ActiveLines( "/m.slang", text, new Dictionary<string, int>(), ( from, name ) =>
		{
			included.Add( name );
			return name == "quoted.h" ? ("/quoted.h", "#define QUOTED\nfrom quoted") : null;
		} );

		CollectionAssert.AreEqual( new[] { "plain", "valued called", "undefined", "from quoted", "last" }, lines );
		CollectionAssert.AreEqual( new[] { "quoted.h", "angled.h" }, included );
	}

	static string Describe( IReadOnlyList<(string Name, string Kind, float[] Value)> attributes )
		=> string.Join( "\n", attributes.Select( a => $"{a.Kind} {a.Name} {string.Join( ",", a.Value )}" ) );
}
