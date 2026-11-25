using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace ResourceTests;

[TestClass]
public class SvgSanitizerTests
{
	const string Png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

	static string[] ImageHrefs( string svg )
	{
		var document = SvgSanitizer.Parse( svg );
		return document.Descendants().OfType<Svg.SvgImage>().Select( x => x.Href ).ToArray();
	}

	[DataTestMethod]
	[DataRow( "file:///C:/Users/victim/Pictures/secret.png" )]
	[DataRow( "file://attacker/share/x.png" )]
	[DataRow( "https://attacker.example/x.png" )]
	[DataRow( "data:image/svg+xml;base64,PHN2Zy8+" )]
	public void ExternalImagesAreRemoved( string href )
	{
		var hrefs = ImageHrefs( $"""<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="8" height="8"><image width="8" height="8" href="{href}"/><image width="8" height="8" xlink:href="{href}"/></svg>""" );

		Assert.IsTrue( hrefs.All( x => x is null ), string.Join( ", ", hrefs ) );
	}

	[DataTestMethod]
	[DataRow( """<image width="8" height="8" style="href:file://attacker/share/x.png"/>""" )]
	[DataRow( """<style>image { href: https://attacker.example/x.png; }</style><image width="8" height="8"/>""" )]
	public void ImagesSetFromCssAreRemoved( string body )
	{
		var hrefs = ImageHrefs( $"""<svg xmlns="http://www.w3.org/2000/svg" width="8" height="8">{body}</svg>""" );

		Assert.IsTrue( hrefs.All( x => x is null ), string.Join( ", ", hrefs ) );
	}

	[TestMethod]
	public void GzippedSvgBehindARasterMimeTypeIsRemoved()
	{
		var inner = Encoding.UTF8.GetBytes( """<svg xmlns="http://www.w3.org/2000/svg"><image href="file://attacker/share/x.png"/></svg>""" );
		using var stream = new MemoryStream();
		using ( var gzip = new GZipStream( stream, CompressionLevel.Fastest, true ) )
			gzip.Write( inner );

		var href = "data:image/png;base64," + Convert.ToBase64String( stream.ToArray() );

		Assert.IsFalse( SvgSanitizer.IsEmbeddedImage( href ) );
	}

	[TestMethod]
	public void EmbeddedRasterImagesAreKept()
	{
		var hrefs = ImageHrefs( $"""<svg xmlns="http://www.w3.org/2000/svg" width="8" height="8"><image width="8" height="8" href="{Png}"/></svg>""" );

		CollectionAssert.AreEqual( new[] { Png }, hrefs );
	}

	[TestMethod]
	public void DocumentTypeHeadersStillLoad()
	{
		var svg = """<!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd"><svg xmlns="http://www.w3.org/2000/svg" width="8" height="8"><rect width="8" height="8"/></svg>""";

		Assert.AreEqual( 8, SvgSanitizer.Parse( svg ).Width.Value );
	}
}
