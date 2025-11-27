namespace Sandbox.UI
{
	internal partial class PanelRenderer
	{
		internal Matrix Matrix;
		Stack<Matrix> MatrixStack = new Stack<Matrix>();

		internal void PopMatrix()
		{
			MatrixStack.Pop();
			Matrix = MatrixStack.Peek();

			SetMatrix( Matrix );
		}

		internal void PushMatrix( Matrix mat )
		{
			MatrixStack.Push( mat );
			SetMatrix( mat );
		}

		void SetMatrix( Matrix mat )
		{
			Matrix = mat;

			Graphics.Attributes.Set( "TransformMat", mat );
		}

		private bool PushMatrix( Panel panel )
		{
			var style = panel.ComputedStyle;

			panel.GlobalMatrix = panel.Parent?.GlobalMatrix ?? null;
			panel.LocalMatrix = null;

			if ( style.Transform.Value.IsEmpty() ) return false;
			if ( panel.TransformMatrix == Matrix.Identity ) return false;

			Vector3 origin = panel.Box.Rect.Position;

			origin.x += style.TransformOriginX.Value.GetPixels( panel.Box.Rect.Width, 0.0f );
			origin.y += style.TransformOriginY.Value.GetPixels( panel.Box.Rect.Height, 0.0f );

			// Transform origin from parent's untransformed space to parent's transformed space
			Vector3 transformedOrigin = panel.Parent?.GlobalMatrix?.Inverted.Transform( origin ) ?? origin;

			Matrix *= Matrix.CreateTranslation( -transformedOrigin );
			Matrix *= panel.TransformMatrix;
			Matrix *= Matrix.CreateTranslation( transformedOrigin );

			var mi = Matrix.Inverted;

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
			PushMatrix( Matrix );

			return true;
		}


	}
}
