public partial class ViewModel
{
	/// <summary>
	/// Throwable type
	/// </summary>
	public enum Throwable
	{
		HEGrenade,
		SmokeGrenade,
		StunGrenade,
		Molotov,
		Flashbang
	}

	/// <summary>
	/// Is this a throwable?
	/// </summary>
	[Property, FeatureEnabled( "Throwables" )] public bool IsThrowable { get; set; }

	/// <summary>
	/// The throwable type
	/// </summary>
	[Property, Feature( "Throwables" )] public Throwable ThrowableType { get; set; }

	protected override void OnEnabled()
	{
		if ( IsThrowable )
		{
			Renderer?.Set( "throwable_type", (int)ThrowableType );
		}
	}
}
