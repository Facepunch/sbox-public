using System;
using System.IO;
using static Editor.AboutWidget;

namespace SystemTests;

[TestClass]
public class ThirdPartyLegalTest
{
	[TestMethod]
	public void CheckJsonValidity()
	{
		var fileData = File.ReadAllText( "thirdpartylegalnotices/dependency_index.json" );
		var indexData = Json.Deserialize<DependencyIndex>( fileData );
		// If we got this far without an exception, the JSON is valid
		Assert.IsNotNull( indexData );
		Assert.IsTrue( indexData.Components.Any(), "Dependency index deserialized to an empty component list" );
	}

	[TestMethod]
	public void CheckAllLicensesExist()
	{
		var fileData = File.ReadAllText( "thirdpartylegalnotices/dependency_index.json" );
		var indexData = Json.Deserialize<DependencyIndex>( fileData );
		Assert.IsTrue( indexData.Components.Any(), "Dependency index deserialized to an empty component list" );
		foreach ( var component in indexData.Components )
		{
			// We don't require a license for public domain components
			if ( component.License.ToLower() == "public-domain" || component.License.ToLower() == "proprietary" ) continue;
			var licensePath = Path.Combine( "thirdpartylegalnotices/licenses/", component.Name.ToLower().Replace( " ", "-" ) );
			Assert.IsTrue( File.Exists( licensePath ), $"License file missing: {licensePath}" );
		}
	}
}
