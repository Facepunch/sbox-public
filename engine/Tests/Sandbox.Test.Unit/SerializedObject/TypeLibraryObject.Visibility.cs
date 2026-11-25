namespace SerializedObjectTests;

public partial class SerializedObjectTest
{
	/// <summary>
	/// A property is hidden by [Hide], or when it has visibility conditions and every one of them says hide.
	/// </summary>
	[TestMethod]
	public void ShouldShowFollowsHideAndConditions()
	{
		var so = typeLibrary.GetSerializedObject( new VisibilityProperties() );

		Assert.IsTrue( so.GetProperty( nameof( VisibilityProperties.Plain ) ).ShouldShow() );
		Assert.IsFalse( so.GetProperty( nameof( VisibilityProperties.Hidden ) ).ShouldShow() );
		Assert.IsFalse( so.GetProperty( nameof( VisibilityProperties.HiddenWithShowingCondition ) ).ShouldShow() );
		Assert.IsFalse( so.GetProperty( nameof( VisibilityProperties.ConditionHides ) ).ShouldShow() );
		Assert.IsTrue( so.GetProperty( nameof( VisibilityProperties.ConditionShows ) ).ShouldShow() );
		Assert.IsTrue( so.GetProperty( nameof( VisibilityProperties.OneOfTwoConditionsShows ) ).ShouldShow() );
		Assert.IsFalse( so.GetProperty( nameof( VisibilityProperties.BothConditionsHide ) ).ShouldShow() );
	}

	/// <summary>
	/// Inspectors ask every property several times a second: asking costs at most the attribute list's enumerator.
	/// </summary>
	[TestMethod]
	[TestCategory( "Allocations" )]
	public void ShouldShowAllocatesLittle()
	{
		var so = typeLibrary.GetSerializedObject( new VisibilityProperties() );
		var property = so.GetProperty( nameof( VisibilityProperties.OneOfTwoConditionsShows ) );

		for ( int i = 0; i < 100; i++ ) property.ShouldShow();

		var before = System.GC.GetAllocatedBytesForCurrentThread();
		for ( int i = 0; i < 100; i++ ) property.ShouldShow();
		var perCall = (System.GC.GetAllocatedBytesForCurrentThread() - before) / 100.0;

		Assert.IsTrue( perCall <= 64, $"{perCall} bytes a call" );
	}

	[System.AttributeUsage( System.AttributeTargets.Property, AllowMultiple = true )]
	class HideWhenAttribute( bool hide ) : InspectorVisibilityAttribute
	{
		public override bool TestCondition( SerializedObject so ) => hide;
	}

	class VisibilityProperties
	{
		public float Plain { get; set; }

		[Hide]
		public float Hidden { get; set; }

		[Hide, HideWhen( false )]
		public float HiddenWithShowingCondition { get; set; }

		[HideWhen( true )]
		public float ConditionHides { get; set; }

		[HideWhen( false )]
		public float ConditionShows { get; set; }

		[HideWhen( true ), HideWhen( false )]
		public float OneOfTwoConditionsShows { get; set; }

		[HideWhen( true ), HideWhen( true )]
		public float BothConditionsHide { get; set; }
	}
}
