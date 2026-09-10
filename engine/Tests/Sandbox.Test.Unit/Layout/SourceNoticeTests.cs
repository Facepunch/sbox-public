using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LayoutTests;

[TestClass]
public class SourceNoticeTests
{
	[TestMethod]
	[DataRow( "Yoga", new[] { "Facebook, Inc. and its affiliates." } )]
	[DataRow( "Taffy", new[] { "2018 Visly Inc.", "2026 Taffy Authors" } )]
	public void ShippedNoticesRetainSourceLicense( string name, string[] copyrights )
	{
		var root = new DirectoryInfo( AppContext.BaseDirectory );
		while ( root is not null && !Directory.Exists( Path.Combine( root.FullName, "game", "thirdpartylegalnotices" ) ) )
			root = root.Parent;
		Assert.IsNotNull( root, "Run source notice tests from a repository checkout." );
		var notices = Path.Combine( root.FullName, "game", "thirdpartylegalnotices" );

		using var index = JsonDocument.Parse( File.ReadAllText( Path.Combine( notices, "dependency_index.json" ) ) );
		var component = index.RootElement.GetProperty( "Components" ).EnumerateArray()
			.Single( x => x.GetProperty( "Name" ).GetString() == name );
		Assert.AreEqual( "MIT", component.GetProperty( "License" ).GetString() );
		CollectionAssert.AreEqual( copyrights, component.GetProperty( "Copyright" ).EnumerateArray().Select( x => x.GetString() ).ToArray() );
		CollectionAssert.Contains( component.GetProperty( "UsedBy" ).EnumerateArray().Select( x => x.GetString() ).ToArray(), "Sandbox.Layout" );

		// Match the About dialog's component-name-to-license-file convention.
		var notice = File.ReadAllText( Path.Combine( notices, "licenses", name.ToLowerInvariant().Replace( " ", "-" ) ) );
		var expected = "MIT License\n\n" + string.Join( "\n", copyrights.Select( x => $"Copyright (c) {x}" ) ) + "\n\n" + """
			Permission is hereby granted, free of charge, to any person obtaining a copy
			of this software and associated documentation files (the "Software"), to deal
			in the Software without restriction, including without limitation the rights
			to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
			copies of the Software, and to permit persons to whom the Software is
			furnished to do so, subject to the following conditions:

			The above copyright notice and this permission notice shall be included in all
			copies or substantial portions of the Software.

			THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
			IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
			FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
			AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
			LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
			OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
			SOFTWARE.
			""";
		Assert.AreEqual( Regex.Replace( expected, @"\s+", " " ).Trim(), Regex.Replace( notice, @"\s+", " " ).Trim() );
	}
}
