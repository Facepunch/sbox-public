using Sandbox.Internal;
using System.Collections.Generic;

namespace SerializedObjectTests;

public partial class SerializedObjectTest
{
	/// <summary>
	/// Explicit group order can precede ungrouped properties while retaining the order of members and other groups.
	/// </summary>
	[TestMethod]
	public void ExplicitGroupOrderPrecedesUngroupedProperties()
	{
		var sheet = new GroupOrderSheet();
		var properties = typeLibrary.GetSerializedObject( new GroupOrderProperties() ).ToList();
		IControlSheet.FilterSortAndAdd( sheet, properties );

		CollectionAssert.AreEqual( new[] { "Color", "", "Early", "Late" }, sheet.Groups.Select( x => x.Name ).ToArray() );
		CollectionAssert.AreEqual( new[] { "Temperature", "Tint", "Intensity" }, sheet.Groups[0].Properties.Select( x => x.Name ).ToArray() );
	}

	/// <summary>
	/// Without an explicit group order, ungrouped properties still precede named groups, even with negative member orders.
	/// </summary>
	[TestMethod]
	public void DefaultGroupOrderPreservesExistingLayouts()
	{
		var sheet = new GroupOrderSheet();
		var properties = typeLibrary.GetSerializedObject( new GroupOrderProperties() ).Where( x => x.GroupName != "Color" ).ToList();
		IControlSheet.FilterSortAndAdd( sheet, properties );

		CollectionAssert.AreEqual( new[] { "", "Early", "Late" }, sheet.Groups.Select( x => x.Name ).ToArray() );
	}

	class GroupOrderSheet : IControlSheet
	{
		internal List<IControlSheet.Group> Groups { get; } = new();

		void IControlSheet.AddGroup( IControlSheet.Group group ) => Groups.Add( group );
		void IControlSheet.AddFeature( IControlSheet.Feature feature ) => Assert.Fail( "Unexpected feature" );
		bool IControlSheet.TestFilter( SerializedProperty property ) => property.IsProperty;
	}

	class GroupOrderProperties
	{
		/// <summary>
		/// An ordinary ungrouped property.
		/// </summary>
		public float Radius { get; set; }

		/// <summary>
		/// The middle member of the explicitly ordered group.
		/// </summary>
		[Group( "Color", Order = -1 ), Order( -2 )]
		public float Tint { get; set; }

		/// <summary>
		/// The last member of the explicitly ordered group.
		/// </summary>
		[Group( "Color", Order = -1 ), Order( -1 )]
		public float Intensity { get; set; }

		/// <summary>
		/// The first member of the explicitly ordered group.
		/// </summary>
		[Group( "Color", Order = -1 ), Order( -3 )]
		public float Temperature { get; set; }

		/// <summary>
		/// A negative member order alone must not place this group before ungrouped properties.
		/// </summary>
		[Group( "Early" ), Order( -10 )]
		public float Early { get; set; }

		/// <summary>
		/// The final group under the existing member order rules.
		/// </summary>
		[Group( "Late" ), Order( 10 )]
		public float Late { get; set; }
	}
}
