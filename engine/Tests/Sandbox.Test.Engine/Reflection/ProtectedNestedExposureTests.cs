using System;

namespace ReflectionTests;

/// <summary>
/// Protected UI extension types retain their explicitly supported reflection metadata.
/// </summary>
[TestClass]
public class ProtectedNestedExposureTests
{
	/// <summary>
	/// Explicit opt-ins preserve inspection of protected nested objects.
	/// </summary>
	[TestMethod]
	[DataRow( "Sandbox.UI.BaseTreeView+RowLayout" )]
	[DataRow( "Sandbox.UI.Navigation.NavigationHost+HistoryItem" )]
	public void ProtectedUiExtensionTypesRemainInspectable( string typeName )
	{
		var type = typeof( Sandbox.UI.Panel ).Assembly.GetType( typeName, throwOnError: true );
		var library = SharedTypeLibrary.Engine;

		Assert.IsNotNull( library.GetType( type ) );
		Assert.IsNotNull( library.GetSerializedObject( Activator.CreateInstance( type, nonPublic: true ) ) );
	}
}
