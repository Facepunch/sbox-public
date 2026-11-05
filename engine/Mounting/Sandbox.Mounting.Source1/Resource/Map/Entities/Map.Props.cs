using System;
using System.Collections.Generic;
using System.IO;
using Sandbox.Mounting;
using static Sandbox.Mounting.Source1.BspGeometry;

namespace Sandbox.Mounting.Source1;

partial class MapLoader
{
	private readonly List<Model> _propModels = [];
	private sealed partial class EntityBuilder( MapLoader owner, GameObject world, BspFile bsp, bool skybox = false )
	{
		public GameObject World { get; } = world;

		private readonly Dictionary<string, Model> models = new( StringComparer.OrdinalIgnoreCase );
		private readonly Dictionary<string, SourceModel> sourceModels = new( StringComparer.OrdinalIgnoreCase );
		private readonly HashSet<string> warnings = new( StringComparer.Ordinal );
		private readonly GameObject parent = new( false, "props" )
		{
			Parent = world
		};
		public void BuildProp( MapProp prop )
		{
			if ( string.IsNullOrWhiteSpace( prop.Model ) || !prop.Model.EndsWith( ".mdl", StringComparison.OrdinalIgnoreCase ) )
			{
				warnings.Add( $"Prop '{prop.Name}' has no supported MDL model; omitted." );
				return;
			}

			if ( !float.IsFinite( prop.Scale ) || prop.Scale <= 0 || prop.Skin < 0 )
				throw new InvalidDataException( $"{owner.FullPath}: prop '{prop.Name}' has invalid scale or skin." );
			if ( !float.IsFinite( prop.MassScale ) )
				throw new InvalidDataException( $"{owner.FullPath}: prop '{prop.Name}' has invalid mass scale." );
			var path = prop.Model.Replace( '\\', '/' ).TrimStart( '/' );
			Model model = null;
			if ( prop.StaticPropIndex >= 0 )
			{
				var resource = owner.Host.GetContentPath( path, owner.ContentRoot );
				if ( resource is null )
				{
					warnings.Add( $"Prop model '{path}' is missing; its instances were omitted." );
					return;
				}

				try
				{
					if ( !sourceModels.TryGetValue( path, out var source ) )
						sourceModels.Add( path, source = owner.Host.ReadSourceModel( path, owner.ContentRoot, readLighting: true ) );
					var litModel = source.BuildStaticProp( owner.ContentRoot, prop.StaticPropIndex, prop.StaticPropFlags, bsp.Hdr && (bsp.MapFlags & 2) != 0, (bsp.MapFlags & 3) != 0, new Transform( prop.Position, Rotation.From( prop.Angles ), prop.Scale ), (prop.StaticPropFlags & 2) != 0 ? prop.LightingOrigin : null, origin => bsp.GetPropLighting( origin, owner._lighting ), owner._bakedShadowBindings, owner._vertexLitMaterials );
					if ( litModel != null )
					{
						owner._propModels.Add( litModel );
						model = litModel;
					}
				}
				catch ( Exception e ) when ( e is IOException or InvalidDataException or NotSupportedException or InvalidOperationException )
				{
					warnings.Add( $"Prop model '{path}' could not be imported: {e.Message}; its instances were omitted." );
					return;
				}
			}

			model ??= LoadModel( path );
			if ( model is null )
				return;
			ReportPhysicsSettings( prop, model, path );
			var useGameplay = !skybox && CanBuildGameplayProp( prop, model, path );
			var isStatic = prop.Physics == PropPhysics.Static && !useGameplay;
			var instance = new GameObject( true, prop.Name )
			{
				Parent = parent,
				IsStatic = isStatic,
				LocalPosition = prop.Position,
				LocalRotation = Rotation.From( prop.Angles ),
				LocalScale = new Vector3( prop.Scale )
			};
			if ( isStatic )
				instance.Tags.Add( "world" );
			var materialGroup = GetSkin( model, prop.Skin, path );
			if ( useGameplay )
			{
				AddGameplayProp( prop, instance, model, materialGroup );
				return;
			}

			var ragdollRenderer = prop.Physics == PropPhysics.Ragdoll ? instance.AddComponent<SkinnedModelRenderer>() : null;
			var renderer = ragdollRenderer ?? instance.AddComponent<ModelRenderer>();
			renderer.Model = model;
			renderer.Tint = prop.Tint;
			renderer.Enabled = prop.Visible;
			renderer.MaterialGroup = materialGroup;
			if ( prop.StaticPropIndex >= 0 && (prop.StaticPropFlags & 0x10) != 0 )
				renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
			AddPhysics( prop, renderer, ragdollRenderer, path );
		}

		public void Finish()
		{
			if ( !skybox )
			{
				BuildPostProcessing();
				WireEntityIO();
			}
			parent.Enabled = true;
			foreach ( var (entity, projected) in authoredDecals )
				BuildAuthoredDecal( entity, projected );
			foreach ( var warning in warnings )
				Log.Warning( $"{owner.FullPath}: {warning}" );
		}

		private Model LoadModel( string path )
		{
			if ( models.TryGetValue( path, out var model ) )
				return model;
			var resource = owner.Host.GetContentPath( path, owner.ContentRoot );
			model = resource is null ? null : Model.Load( $"{resource}.vmdl" );
			if ( model is null || !model.IsValid || model.IsError )
			{
				warnings.Add( $"Prop model '{path}' is missing or could not be loaded; its instances were omitted." );
				model = null;
			}

			models.Add( path, model );
			return model;
		}

		private string GetSkin( Model model, int index, string path )
		{
			if ( index == 0 )
				return null;
			var skin = $"Skin {index}";
			var groupCount = model.MaterialGroupCount;
			for ( var i = 0; i < groupCount; i++ )
			{
				if ( model.GetMaterialGroupName( i ) == skin )
				{
					return skin;
				}
			}

			warnings.Add( $"Prop model '{path}' has no skin {index}; using the default skin." );
			return null;
		}
	}
}
