using Sandbox.Rendering;

namespace Sandbox.UI;

internal partial class PanelRenderer
{
	internal Matrix Matrix;
	Stack<Matrix> MatrixStack = new Stack<Matrix>();

	/// <summary>
	/// Calculate and store the transform matrix for a panel during build phase.
	/// The TransformMat attribute is stored in the panel's TransformCommandList.
	/// </summary>
	private void BuildTransformCommandList( Panel panel )
	{
		panel.GlobalMatrix = panel.Parent?.GlobalMatrix ?? null;
		panel.LocalMatrix = null;

		var style = panel.ComputedStyle;
		Matrix transformMat;

		if ( style.Transform.Value.IsEmpty() || panel.TransformMatrix == Matrix.Identity )
		{
			// No transform, just inherit parent's matrix
			transformMat = panel.GlobalMatrix?.Inverted ?? Matrix.Identity;
		}
		else
		{
			Vector3 origin = panel.Box.Rect.Position;
			origin.x += style.TransformOriginX.Value.GetPixels( panel.Box.Rect.Width, 0.0f );
			origin.y += style.TransformOriginY.Value.GetPixels( panel.Box.Rect.Height, 0.0f );

			// Transform origin from parent's untransformed space to parent's transformed space
			Vector3 transformedOrigin = panel.Parent?.GlobalMatrix?.Inverted.Transform( origin ) ?? origin;

			transformMat = panel.GlobalMatrix?.Inverted ?? Matrix.Identity;
			transformMat *= Matrix.CreateTranslation( -transformedOrigin );
			transformMat *= panel.TransformMatrix;
			transformMat *= Matrix.CreateTranslation( transformedOrigin );

			var mi = transformMat.Inverted;

			// Local is current takeaway parent
			if ( panel.GlobalMatrix.HasValue )
			{
				panel.LocalMatrix = panel.GlobalMatrix.Value.Inverted * mi;
			}
			else
			{
				panel.LocalMatrix = mi;
			}

			panel.GlobalMatrix = mi;
		}

		// Skip command list rebuild if transform and root status unchanged
		var isRoot = panel is RootPanel;
		if ( panel._lastTransformMat == transformMat && panel._lastTransformIsRoot == isRoot )
			return;

		panel._lastTransformMat = transformMat;
		panel._lastTransformIsRoot = isRoot;

		panel.TransformCommandList.Reset();

		if ( isRoot )
		{
			panel.TransformCommandList.Attributes.Set( "LayerMat", Matrix.Identity );
		}

		panel.TransformCommandList.Attributes.Set( "TransformMat", transformMat );
	}

}
