using System;
using System.IO;
using static Editor.AboutWidget;

namespace SystemTest;

[TestClass]
public class ThirdPartyLegalTest
{
	[TestMethod]
	public void CheckJsonValidity()
	{
		File.ReadAllText( "thirdpartylegalnotices/dependency_index.json" );
		var fileData = File.ReadAllText( "thirdpartylegalnotices/dependency_index.json" );
		var indexData = Json.Deserialize<DependencyIndex>( fileData );
		// If we got this far without an exception, the JSON is valid
		Assert.IsNotNull( indexData );
	}

	[TestMethod]
	public void CheckAllLicensesExist()
	{
		var fileData = File.ReadAllText( "thirdpartylegalnotices/dependency_index.json" );
		var indexData = Json.Deserialize<DependencyIndex>( fileData );
		foreach ( var component in indexData.Components )
		{
			// We don't require a license for public domain components
			if ( component.License.ToLower() == "public-domain" || component.License.ToLower() == "proprietary" ) continue;
			var licensePath = Path.Combine( "thirdpartylegalnotices/licenses/", component.Name.ToLower().Replace( " ", "-" ) );
			Assert.IsTrue( File.Exists( licensePath ), $"License file missing: {licensePath}" );
		}
	}
}
