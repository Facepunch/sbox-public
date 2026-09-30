namespace Sandbox;

public partial class Shader
{
	/// <summary>
	/// Returns a schema representing the variables and combos in this shader.
	/// This is used by the material editor to show UI for editing shader parameters.
	/// </summary>
	public ShaderSchema Schema
	{
		get
		{
			string json = native.GetPropertiesJson();
			var data = Sandbox.Json.Deserialize<ShaderSchema>( json );

			//data.Clean();

			return data;
		}
	}

	public struct VariableDescription
	{
		public string Name { get; set; }
		public string Type { get; set; }
		public string AttributeName { get; set; }
		public string SourceType { get; set; }
		public string UiType { get; set; }
		public string UiGroup { get; set; }
		public float UiStep { get; set; }
		public Vector4 FloatMin { get; set; }
		public Vector4 FloatMax { get; set; }

		public string DefaultInputTexture { get; set; }
		public Vector4 FloatDefault { get; set; }
		public Vector4 IntDefault { get; set; }

		public string TextureEnding { get; set; }
	}

	public struct ComboDescription
	{
		public string Name { get; set; }
		public string Group { get; set; }
		public int Min { get; set; }
		public int Max { get; set; }
		public int Index { get; set; }
		public string[] Values { get; set; }
	}

	public class ShaderSchema
	{
		public List<VariableDescription> Variables { get; set; }
		public List<ComboDescription> Combos { get; set; }
	}

}
