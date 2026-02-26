namespace Sandbox.UI;

internal partial class PanelRenderer
{
	internal Matrix Matrix;
	Stack<Matrix> MatrixStack = new Stack<Matrix>();

	internal void PopMatrix()
	{
		MatrixStack.Pop();
		Matrix = MatrixStack.Peek();
	}

	internal void PushMatrix( Matrix mat )
	{
		MatrixStack.Push( mat );
		Matrix = mat;
	}

	/// <summary>
	/// Calculate and store the transform matrix for a panel during build phase.
	/// The TransformMat attribute is stored in the panel's TransformCommandList.
	/// </summary>
	private void BuildTransformCommandList( Panel panel )
	{
		panel.TransformCommandList.Reset();

		panel.GlobalMatrix = panel.Parent?.GlobalMatrix ?? null;
		panel.LocalMatrix = null;

		// Root panels need LayerMat initialized
		if ( panel is RootPanel )
		{
			panel.TransformCommandList.Attributes.Set( "LayerMat", Matrix.Identity );
		}

		var style = panel.ComputedStyle;

		if ( style.Transform.Value.IsEmpty() || panel.TransformMatrix == Matrix.Identity )
		{
			// No transform, just inherit parent's matrix
			var mat = panel.GlobalMatrix?.Inverted ?? Matrix.Identity;
			panel.TransformCommandList.Attributes.Set( "TransformMat", mat );
			return;
		}

		Vector3 origin = panel.Box.Rect.Position;
		origin.x += style.TransformOriginX.Value.GetPixels( panel.Box.Rect.Width, 0.0f );
		origin.y += style.TransformOriginY.Value.GetPixels( panel.Box.Rect.Height, 0.0f );

		// Transform origin from parent's untransformed space to parent's transformed space
		Vector3 transformedOrigin = panel.Parent?.GlobalMatrix?.Inverted.Transform( origin ) ?? origin;

		var mat2 = panel.GlobalMatrix?.Inverted ?? Matrix.Identity;
		mat2 *= Matrix.CreateTranslation( -transformedOrigin );
		mat2 *= panel.TransformMatrix;
		mat2 *= Matrix.CreateTranslation( transformedOrigin );

		var mi = mat2.Inverted;

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

		panel.TransformCommandList.Attributes.Set( "TransformMat", mat2 );
	}

	/// <summary>
	/// Push matrix state during render phase (for culling calculations only).
	/// The actual TransformMat attribute is already in the panel's command list.
	/// </summary>
	private bool PushMatrix( Panel panel )
	{
		var style = panel.ComputedStyle;

		// GlobalMatrix should already be set during build phase
		if ( style.Transform.Value.IsEmpty() ) return false;
		if ( panel.TransformMatrix == Matrix.Identity ) return false;

		// Just update the matrix stack for culling purposes
		var mat = panel.GlobalMatrix?.Inverted ?? Matrix.Identity;
		PushMatrix( mat );

		return true;
	}
}
