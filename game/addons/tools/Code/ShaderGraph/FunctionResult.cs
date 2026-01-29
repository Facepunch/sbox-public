
using Editor.NodeEditor;
using Sandbox.Resources;
using System.Text;
using System.Text.Json.Serialization;

namespace Editor.ShaderGraph;

public interface IErroringNode
{
	List<string> GetErrors();
}

/// <summary>
/// Final result
/// </summary>
[Title( "Result" ), Icon( "output" )]
public sealed class FunctionResult : BaseResult, IErroringNode
{
	[Title( "Outputs" )]
	public List<FunctionOutput> FunctionOutputs { get; set; }

	[Hide]
	private List<IPlugIn> InternalInputs = new();

	[Hide]
	public override IEnumerable<IPlugIn> Inputs => InternalInputs;

	[Hide, JsonIgnore]
	int _lastHashCode = 0;

	[Hide, JsonIgnore]
	public override Color PrimaryColor => Color.Lerp( Theme.Blue, Color.White, 0.25f );

	public override void OnFrame()
	{
		var hashCode = 0;
		int index = 1;
		foreach ( var output in FunctionOutputs )
		{
			hashCode += output.GetHashCode() * index;
			index++;
		}
		if ( hashCode != _lastHashCode )
		{
			_lastHashCode = hashCode;

			CreateInputs();
			Update();
		}
	}

	public void CreateInputs()
	{
		var plugs = new List<IPlugIn>();
		if ( FunctionOutputs == null )
		{
			InternalInputs = new();
		}
		else
		{
			foreach ( var output in FunctionOutputs.OrderBy( x => x.Priority ) )
			{
				var outputType = output.Type;
				if ( outputType == typeof( ColorTextureGenerator ) )
				{
					outputType = typeof( Color );
				}
				if ( outputType is null ) continue;
				var info = new PlugInfo()
				{
					Id = output.Id,
					Name = output.Name,
					Type = outputType,
					DisplayInfo = new()
					{
						Name = output.Name,
						Fullname = outputType.FullName
					}
				};
				var oldPlug = InternalInputs.FirstOrDefault( x => x is BasePlugIn plugIn && plugIn.Info.Id == info.Id ) as BasePlugIn;
				if ( oldPlug is not null )
				{
					oldPlug.Info.Name = info.Name;
					oldPlug.Info.Type = info.Type;
					oldPlug.Info.DisplayInfo = info.DisplayInfo;
					plugs.Add( oldPlug );
				}
				else
				{
					var plug = new BasePlugIn( this, info, info.Type );
					plugs.Add( plug );
				}
			}
			InternalInputs = plugs;
		}
	}

	public List<string> GetErrors()
	{
		var errors = new List<string>();
		if ( FunctionOutputs == null || FunctionOutputs.Count == 0 )
		{
			errors.Add( "No outputs defined" );
		}
		else
		{
			Dictionary<string, int> namesSoFar = new();
			foreach ( var output in FunctionOutputs )
			{
				if ( output.Name is null )
				{
					errors.Add( $"{output.TypeName} Output has no name" );
					continue;
				}
				if ( !namesSoFar.ContainsKey( output.Name ) )
				{
					namesSoFar.Add( output.Name, 1 );
				}
				else
				{
					namesSoFar[output.Name]++;
				}
				if ( string.IsNullOrEmpty( output.Name ) )
				{
					errors.Add( $"{output.TypeName} Output has no name" );
				}
				if ( string.IsNullOrEmpty( output.TypeName ) )
				{
					errors.Add( $"Output '{output.Name}' has no type" );
				}
			}
			foreach ( var name in namesSoFar )
			{
				if ( name.Value > 1 )
				{
					errors.Add( $"Output name '{name.Key}' is used {name.Value} times" );
				}
			}
		}
		return errors;
	}

	public override NodeInput GetAlbedo()
	{
		var input = GetInputFromPreview( FunctionOutput.PreviewType.Albedo );
		if ( input is not null )
		{
			return input.Value;
		}
		return default;
	}

	public override NodeInput GetEmission()
	{
		var input = GetInputFromPreview( FunctionOutput.PreviewType.Emission );
		if ( input is not null )
		{
			return input.Value;
		}
		return default;
	}

	public override NodeInput GetOpacity()
	{
		var input = GetInputFromPreview( FunctionOutput.PreviewType.Opacity );
		if ( input is not null )
		{
			return input.Value;
		}
		return default;
	}

	public override NodeInput GetNormal()
	{
		var input = GetInputFromPreview( FunctionOutput.PreviewType.Normal );
		if ( input is not null )
		{
			return input.Value;
		}
		return default;
	}

	public override NodeInput GetRoughness()
	{
		var input = GetInputFromPreview( FunctionOutput.PreviewType.Roughness );
		if ( input is not null )
		{
			return input.Value;
		}
		return default;
	}

	public override NodeInput GetMetalness()
	{
		var input = GetInputFromPreview( FunctionOutput.PreviewType.Metalness );
		if ( input is not null )
		{
			return input.Value;
		}
		return default;
	}

	public override NodeInput GetAmbientOcclusion()
	{
		var input = GetInputFromPreview( FunctionOutput.PreviewType.AmbientOcclusion );
		if ( input is not null )
		{
			return input.Value;
		}
		return default;
	}

	public override NodeInput GetPositionOffset()
	{
		var input = GetInputFromPreview( FunctionOutput.PreviewType.PositionOffset );
		if ( input is not null )
		{
			return input.Value;
		}
		return default;
	}

	public override Color GetDefaultAlbedo()
	{
		return base.GetDefaultAlbedo();
	}

	public void AddMaterialOutputs( GraphCompiler compiler, StringBuilder sb, HashSet<string> visited )
	{
		if ( !visited.Contains( "Albedo" ) )
		{
			var albedoResult = GetAlbedoResult( compiler );

			if ( !albedoResult.IsValid )
				return;

			sb.AppendLine( $"m.Albedo = {albedoResult.Cast( 3 )};" );
		}
		if ( !visited.Contains( "Emission" ) )
		{
			var emissionResult = GetEmissionResult( compiler );

			if ( !emissionResult.IsValid )
				return;

			sb.AppendLine( $"m.Emission = {emissionResult.Cast( 3 )};" );
		}
		if ( !visited.Contains( "Opacity" ) )
		{
			var opacityResult = GetOpacityResult( compiler );

			if ( !opacityResult.IsValid )
				return;

			sb.AppendLine( $"m.Opacity = {opacityResult.Cast( 1 )};" );
		}
		if ( !visited.Contains( "Normal" ) )
		{
			var normalResult = GetNormalResult( compiler );

			if ( !normalResult.IsValid )
				return;

			sb.AppendLine( $"m.Normal = {normalResult.Cast( 3 )};" );
		}
		if ( !visited.Contains( "Roughness" ) )
		{
			var roughnessResult = GetRoughnessResult( compiler );

			if ( !roughnessResult.IsValid )
				return;

			sb.AppendLine( $"m.Roughness = {roughnessResult.Cast( 1 )};" );
		}
		if ( !visited.Contains( "Metalness" ) )
		{
			var metalnessResult = GetMetalnessResult( compiler );

			if ( !metalnessResult.IsValid )
				return;

			sb.AppendLine( $"m.Metalness = {metalnessResult.Cast( 1 )};" );
		}
		if ( !visited.Contains( "AmbientOcclusion" ) )
		{
			var ambientOcclusionResult = GetAmbientOcclusionResult( compiler );

			if ( !ambientOcclusionResult.IsValid )
				return;

			sb.AppendLine( $"m.AmbientOcclusion = {ambientOcclusionResult.Cast( 1 )};" );
		}
	}

	private NodeInput? GetInputFromPreview( FunctionOutput.PreviewType previewType )
	{
		var albedoOutput = FunctionOutputs.FirstOrDefault( x => x.Preview == previewType );
		if ( albedoOutput is not null )
		{
			var input = Inputs.FirstOrDefault( x => x is BasePlugIn plugIn && plugIn.Info.Id == albedoOutput.Id );
			if ( input is BasePlugIn plugIn )
			{
				if ( plugIn.Info.ConnectedPlug is not null )
				{
					return new NodeInput
					{
						Identifier = plugIn.Info.ConnectedPlug.Node.Identifier,
						Output = plugIn.Info.ConnectedPlug.Identifier
					};
				}
				return plugIn.Info.GetInput( plugIn.Node );
			}
		}
		return null;
	}
}

public class FunctionOutput
{
	[Hide]
	public Guid Id { get; } = Guid.NewGuid();

	[KeyProperty]
	public string Name { get; set; }

	[Hide, JsonIgnore]
	public Type Type
	{
		get
		{
			if ( string.IsNullOrEmpty( TypeName ) ) return null;
			var typeName = TypeName;
			if ( typeName == "float" ) typeName = typeof( float ).FullName;
			if ( typeName == "int" ) typeName = typeof( int ).FullName;
			if ( typeName == "bool" ) typeName = typeof( bool ).FullName;
			var type = TypeLibrary.GetType( typeName ).TargetType;
			return type;
		}
	}

	[KeyProperty, Editor( "shadertype" ), JsonPropertyName( "Type" )]
	public string TypeName { get; set; }

	public PreviewType Preview { get; set; }

	public int Priority { get; set; }

	public override int GetHashCode()
	{
		return System.HashCode.Combine( Id, Name, TypeName, Priority );
	}

	public enum PreviewType
	{
		[Icon( "clear" )]
		None,
		[Icon( "palette" )]
		Albedo,
		[Icon( "brightness_5" )]
		Emission,
		[Icon( "opacity" )]
		Opacity,
		[Icon( "texture" )]
		Normal,
		[Icon( "terrain" )]
		Roughness,
		[Icon( "auto_awesome" )]
		Metalness,
		[Icon( "tonality" )]
		AmbientOcclusion,
		[Icon( "arrow_forward" )]
		PositionOffset
	}
}
