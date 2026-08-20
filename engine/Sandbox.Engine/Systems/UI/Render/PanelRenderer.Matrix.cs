using Sandbox.Rendering;

namespace Sandbox.UI;

internal partial class PanelRenderer
{
	internal Matrix Matrix;

	/// <summary>
	/// Calculate and store the transform matrix for a panel during build phase.
	/// The transform is cached on the panel and applied to the global CL during gather.
	/// </summary>
	private void BuildTransformState( Panel panel )
	{
		var globalMat = panel.Parent?.GlobalMatrix;
		var globalMatInverted = panel.Parent?.GlobalMatrixInverted;

		var style = panel.ComputedStyle;
		Matrix transformMat;
		Matrix? localMat = null;

		if ( style.Transform.Value.IsEmpty() || panel.TransformMatrix == Matrix.Identity )
		{
			transformMat = globalMatInverted ?? Matrix.Identity;
		}
		else
		{
			Vector3 origin = panel.Box.Rect.Position;
			origin.x += style.TransformOriginX.Value.GetPixels( panel.Box.Rect.Width, 0.0f );
			origin.y += style.TransformOriginY.Value.GetPixels( panel.Box.Rect.Height, 0.0f );

			Vector3 transformedOrigin = globalMatInverted?.Transform( origin ) ?? origin;

			transformMat = globalMatInverted ?? Matrix.Identity;
			transformMat *= Matrix.CreateTranslation( -transformedOrigin );
			transformMat *= panel.TransformMatrix;
			transformMat *= Matrix.CreateTranslation( transformedOrigin );

			var mi = transformMat.Inverted;

			localMat = globalMatInverted.HasValue ? globalMatInverted.Value * mi : mi;

			globalMat = mi;
			globalMatInverted = transformMat;
		}

		// Most panels have no transform anywhere in their chain, so these stores
		// are usually all no-ops - only write when the value actually changed.
		if ( panel.GlobalMatrix != globalMat )
			panel.SetGlobalMatrix( globalMat, globalMatInverted );

		if ( panel.LocalMatrix != localMat )
			panel.LocalMatrix = localMat;

		// CachedDescriptors must always exist with a valid TransformMat after this -
		// pooled RenderLayers carry a stale TransformMat from their previous owner.
		if ( panel.CachedDescriptors is null )
			panel.CachedDescriptors = new() { TransformMat = transformMat };
		else if ( panel.CachedDescriptors.TransformMat != transformMat )
			panel.CachedDescriptors.TransformMat = transformMat;
	}
}
