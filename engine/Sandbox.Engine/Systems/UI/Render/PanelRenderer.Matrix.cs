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
		panel.GlobalMatrix = panel.Parent?.GlobalMatrix ?? null;
		panel.LocalMatrix = null;

		var style = panel.ComputedStyle;
		Matrix transformMat;

		if ( style.Transform.Value.IsEmpty() || panel.TransformMatrix == Matrix.Identity )
		{
			transformMat = panel.GlobalMatrix?.Inverted ?? Matrix.Identity;
		}
		else
		{
			Vector3 origin = panel.Box.Rect.Position;
			origin.x += style.TransformOriginX.Value.GetPixels( panel.Box.Rect.Width, 0.0f );
			origin.y += style.TransformOriginY.Value.GetPixels( panel.Box.Rect.Height, 0.0f );

			Vector3 transformedOrigin = panel.Parent?.GlobalMatrix?.Inverted.Transform( origin ) ?? origin;

			transformMat = panel.GlobalMatrix?.Inverted ?? Matrix.Identity;
			transformMat *= Matrix.CreateTranslation( -transformedOrigin );
			transformMat *= panel.TransformMatrix;
			transformMat *= Matrix.CreateTranslation( transformedOrigin );

			var mi = transformMat.Inverted;

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

		panel.CachedDescriptors ??= new();
		panel.CachedDescriptors.TransformMat = transformMat;
	}
}
