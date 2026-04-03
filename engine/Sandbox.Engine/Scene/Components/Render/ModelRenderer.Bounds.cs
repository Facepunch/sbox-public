namespace Sandbox;

partial class ModelRenderer
{
	BBox IHasBounds.LocalBounds => LocalBounds;

	public BBox Bounds => GetWorldBoundsInternal();

	public BBox LocalBounds => GetLocalBoundsInternal();

	internal virtual BBox GetWorldBoundsInternal()
	{
		return LocalBounds.Transform( WorldTransform );
	}

	internal virtual BBox GetLocalBoundsInternal()
	{
		if ( Model is null )
			return BBox.FromPositionAndSize( WorldPosition, 16 );

		return Model.RenderBounds;
	}
}
