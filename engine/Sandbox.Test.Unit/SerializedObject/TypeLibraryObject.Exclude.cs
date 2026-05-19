namespace SerializedObject;


public partial class From_TypeLibrary
{
	[TestMethod]
	public void Exclude_EqualityContract()
	{
		var source = new MyRecord();

		var obj = typeLibrary.GetSerializedObject( source );

		var prop = obj.GetProperty( "EqualityContract" );

		Assert.IsNull( prop, "Property should be excluded" );
	}
}
