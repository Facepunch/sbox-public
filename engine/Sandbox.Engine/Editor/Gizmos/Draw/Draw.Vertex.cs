using Microsoft.Win32;
using System.Collections.Immutable;

namespace Sandbox;

public static partial class Gizmo
{
	public sealed partial class GizmoDraw
	{
		ScopeState _lastState;


		private bool CanReuseVertexObject( Graphics.PrimitiveType type, Material material, Texture texture )
		{
			if ( !_vertexObject.IsValid() ) return false;
			if ( !ReferenceEquals( _vertexObjectMaterial, material ) ) return false;
			if ( _vertexObject.PrimitiveType != type ) return false;
			if ( _lastState.LineThickness != LineThickness ) return false;
			if ( _lastState.IgnoreDepth != IgnoreDepth ) return false;

			if ( type == Graphics.PrimitiveType.Points )
				return ReferenceEquals( _vertexObjectTexture, texture );

			if ( _vertexObjectPath != Path ) return false;
			if ( _vertexObject.Transform != Transform ) return false;

			return true;
		}

		VertexSceneObject VertexObject( Graphics.PrimitiveType type, Material material, Texture texture = null )
		{
			// Keep accumulating into the same object while it's reusable; flushing mid-batch would clobber it (Write does a native Begin that clears the buffer).
			if ( CanReuseVertexObject( type, material, texture ) )
			{
				// Still a new object to this scope, so unnamed scopes and pool keys after it don't shift
				if ( type == Graphics.PrimitiveType.Points && (_vertexObjectPath != Path || _lastState.Transform != Transform || _lastState.Color != Color) )
				{
					Active.scope.Create++;
					_vertexObjectPath = Path;
					_lastState = Active.scope;
				}

				return _vertexObject;
			}

			// Switching to a different object, so flush the batch we just finished.
			_vertexObject?.Write();

			_vertexObjectPath = Path;
			_vertexObjectMaterial = material;
			_vertexObjectTexture = texture;

			var so = Active.FindOrCreate<VertexSceneObject>( $"line", () => new VertexSceneObject( World ) );
			_vertexObject = so;

			// Cheap state that can change every draw (transform/color animate, hover/selection recolor).
			so.PrimitiveType = type;
			so.Transform = Transform;
			so.Vertices.Clear();
			so.ColorTint = Color;

			// Native config rarely changes and persists across frames (Begin() resets geometry only), so only
			// re-apply it when an input actually changed - re-setting it every frame is the dominant native cost.
			// https://github.com/orgs/Facepunch/projects/17/views/1?pane=issue&itemId=22115064
			bool needsConfig =
				!so.ConfigApplied ||
				so.ConfigType != type ||
				!ReferenceEquals( so.ConfigMaterial, material ) ||
				!ReferenceEquals( so.ConfigTexture, texture ) ||
				so.ConfigIgnoreDepth != IgnoreDepth ||
				so.ConfigCullBackfaces != CullBackfaces ||
				(type == Graphics.PrimitiveType.Lines && so.ConfigLineThickness != LineThickness);

			if ( needsConfig )
			{
				so.Material = material;
				so.Bounds = BBox.FromPositionAndSize( 0, float.MaxValue );
				so.RenderLayer = IgnoreDepth ? SceneRenderLayer.OverlayWithoutDepth : SceneRenderLayer.OverlayWithDepth;

				// todo - tell VO to determine flags from material
				if ( !IgnoreDepth )
				{
					so.Flags.IsTranslucent = false;
					so.Flags.IsOpaque = true;
				}

				if ( type == Graphics.PrimitiveType.Lines )
				{
					so.Attributes.Set( "LineThickness", LineThickness );
					//so.Attributes.Set( "PatternType", LineSettings.Dashed ? 1.0f : 0.0f );
				}

				if ( type == Graphics.PrimitiveType.Points )
				{
					so.Flags.IsTranslucent = true;
					so.Flags.IsOpaque = false;
					so.Attributes.Set( "TextureColor", texture ?? Texture.White );
				}

				so.Attributes.SetCombo( "D_NO_ZTEST", IgnoreDepth ? 1 : 0 );
				so.Attributes.SetCombo( "D_NO_CULLING", CullBackfaces ? 0 : 1 );
				so.Attributes.SetCombo( "D_SNAP_TO_SCREEN_PIXELS", 0 );
				so.Attributes.SetCombo( "D_SHADED", 0 );
				so.Attributes.SetCombo( "D_DEPTH_BIAS", 1 );

				so.ConfigApplied = true;
				so.ConfigType = type;
				so.ConfigMaterial = material;
				so.ConfigTexture = texture;
				so.ConfigIgnoreDepth = IgnoreDepth;
				so.ConfigCullBackfaces = CullBackfaces;
				so.ConfigLineThickness = LineThickness;
			}

			_lastState = Active.scope;

			return _vertexObject;
		}

	}



}
