internal class ConstraintCleanup : Component
{
	[Property]
	public GameObject Attachment { get; set; }

	[Property]
	public bool DestroyOnDetach { get; set; } = true;

	protected override void OnDestroy()
	{
		if ( Attachment.IsValid() )
		{
			Attachment.Destroy();
		}

		base.OnDestroy();
	}

	protected override void OnUpdate()
	{
		if ( Attachment.IsValid() ) return;

		if ( DestroyOnDetach )
		{
			DestroyGameObject();
			return;
		}

		GetComponent<SpringJoint>()?.Destroy();
		GetComponent<VerletRope>()?.Destroy();
		GetComponent<LineRenderer>()?.Destroy();
		Destroy();
	}
}
