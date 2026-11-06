using Editor.AnimGraph.Nodes;
using System;
using System.Text.Json.Serialization;

namespace Editor.AnimGraph;

public enum AnimParameterType
{
	Float,
	Bool,
	ID,
	Vector,
	Target,
	BoneMask
}

public enum AnimTargetPreviewMode
{
	Unset,
	Transform,
	Bone
}

public interface IParameter
{
	string Identifier { get; }
	string Name { get; set; }
	string Group { get; set; }
	AnimParameterType Type { get; }
	bool IsVirtual { get; }
}

/// <summary>
/// A control parameter on the graph - an input that game code sets at runtime.
/// </summary>
public class Parameter : IParameter
{
	[Hide, JsonIgnore]
	public Data Graph { get; set; }

	[Hide]
	public string Identifier { get; set; } = Guid.NewGuid().ToString();

	[Property]
	public AnimParameterType Type { get; set; }

	[Property]
	public string Name { get; set; } = "";

	[Property]
	public string Group { get; set; } = "";

	[Hide, JsonIgnore]
	public bool IsVirtual => false;

	[Property, Group( "Runtime" ), Title( "Default Value" ), ShowIf( nameof( Type ), AnimParameterType.Float )]
	public float RuntimeDefaultFloat { get; set; }

	[Property, Group( "Runtime" ), Title( "Default Value" ), ShowIf( nameof( Type ), AnimParameterType.Bool )]
	public bool RuntimeDefaultBool { get; set; }

	[Property, Group( "Runtime" ), Title( "Default Value" ), ShowIf( nameof( Type ), AnimParameterType.ID )]
	public string RuntimeDefaultID { get; set; } = "";

	[Property, Group( "Runtime" ), Title( "Default Value" ), ShowIf( nameof( Type ), AnimParameterType.Vector )]
	public Vector3 RuntimeDefaultVector { get; set; }

	[Property, Group( "Preview" ), Title( "Start Value" ), ShowIf( nameof( Type ), AnimParameterType.Float ), JsonPropertyName( "DefaultFloat" )]
	public float PreviewStartFloat { get; set; }

	/// <summary>
	/// Range for float parameters. Min >= Max means unbounded.
	/// </summary>
	[Property, ShowIf( nameof( Type ), AnimParameterType.Float )]
	public float Min { get; set; }

	[Property, ShowIf( nameof( Type ), AnimParameterType.Float )]
	public float Max { get; set; } = 1f;

	[Hide, JsonIgnore]
	public bool HasRange => Type == AnimParameterType.Float && Min < Max;

	[Property, Group( "Preview" ), Title( "Start Value" ), ShowIf( nameof( Type ), AnimParameterType.Bool ), JsonPropertyName( "DefaultBool" )]
	public bool PreviewStartBool { get; set; }

	[Property, Group( "Preview" ), Title( "Start Value" ), ShowIf( nameof( IsFreeTextID ), true ), JsonPropertyName( "DefaultID" )]
	public string PreviewStartID { get; set; } = "";

	[Property, Group( "Preview" ), Title( "Start Value" ), Editor( "animgraph.options" ), ShowIf( nameof( IsOptionsID ), true ), JsonIgnore]
	public string PreviewStartOption { get => PreviewStartID; set => PreviewStartID = value; }

	/// <summary>
	/// Back this ID parameter with a C# enum - its values become the options.
	/// </summary>
	[Property, Title( "Enum" ), TargetType( typeof( Enum ) ), ShowIf( nameof( Type ), AnimParameterType.ID ), JsonIgnore]
	public Type EnumType
	{
		get => TypeLibrary.GetType( EnumTypeName )?.TargetType;
		set => EnumTypeName = value?.FullName ?? "";
	}

	[Hide]
	public string EnumTypeName { get; set; } = "";

	[Property, Title( "Expected Values" ), ShowIf( nameof( Type ), AnimParameterType.ID )]
	public List<string> ExpectedValues { get; set; } = [];

	[Hide, JsonIgnore]
	public IReadOnlyList<string> Options
	{
		get
		{
			var enumValues = EnumType is { } e ? Enum.GetNames( e ) : [];
			return [.. enumValues.Concat( ExpectedValues ).Distinct( StringComparer.Ordinal )];
		}
	}

	[Hide, JsonPropertyName( "ResolvedOptions" )]
	public IReadOnlyList<string> ResolvedOptions => Options;

	[Hide, JsonIgnore]
	public bool IsOptionsID => Type == AnimParameterType.ID && Options.Count > 0;

	[Hide, JsonIgnore]
	public bool IsFreeTextID => Type == AnimParameterType.ID && Options.Count == 0;

	[Property, Group( "Preview" ), Title( "Start Value" ), ShowIf( nameof( Type ), AnimParameterType.Vector ), JsonPropertyName( "DefaultVector" )]
	public Vector3 PreviewStartVector { get; set; }

	[Property, Group( "Preview" ), Title( "Set" ), ShowIf( nameof( Type ), AnimParameterType.Target )]
	public bool PreviewStartTargetIsSet { get; set; }

	[Property, Group( "Preview" ), Title( "Bone Target" ), ShowIf( nameof( HasPreviewStartTarget ), true )]
	public bool PreviewStartTargetIsBone { get; set; }

	[Property, Group( "Preview" ), Title( "Bone" ), Editor( "animgraph.bone" ), ShowIf( nameof( HasPreviewStartBoneTarget ), true )]
	public string PreviewStartBone { get; set; } = "";

	[Property, Group( "Preview" ), Title( "Position" ), ShowIf( nameof( HasPreviewStartTransformTarget ), true ), JsonIgnore]
	public Vector3 PreviewStartPosition { get; set; }

	[Property, Group( "Preview" ), Title( "Rotation" ), ShowIf( nameof( HasPreviewStartTransformTarget ), true ), JsonIgnore]
	public Rotation PreviewStartRotation { get; set; } = Rotation.Identity;

	[Hide, JsonPropertyName( "PreviewPosition" ), JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	public Vector3? SerializedPreviewStartPosition
	{
		get => HasPreviewStartTransformTarget ? PreviewStartPosition : null;
		set
		{
			if ( value is not { } position )
				return;

			PreviewStartTargetIsSet = true;
			PreviewStartTargetIsBone = false;
			PreviewStartPosition = position;
		}
	}

	[Hide, JsonPropertyName( "PreviewRotation" ), JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	public Rotation? SerializedPreviewStartRotation
	{
		get => HasPreviewStartTransformTarget ? PreviewStartRotation : null;
		set
		{
			if ( value is not { } rotation )
				return;

			PreviewStartTargetIsSet = true;
			PreviewStartTargetIsBone = false;
			PreviewStartRotation = rotation;
		}
	}

	[Hide, JsonIgnore]
	public bool HasPreviewStartTarget => Type == AnimParameterType.Target && PreviewStartTargetIsSet;

	[Hide, JsonIgnore]
	public bool HasPreviewStartBoneTarget => HasPreviewStartTarget && PreviewStartTargetIsBone;

	[Hide, JsonIgnore]
	public bool HasPreviewStartTransformTarget => HasPreviewStartTarget && !PreviewStartTargetIsBone;

	private float? _previewFloat;
	private bool? _previewBool;
	private string _previewID;
	private Vector3? _previewVector;
	private bool? _previewTargetIsSet;
	private bool? _previewTargetIsBone;
	private string _previewBone;
	private Vector3? _previewPosition;
	private Rotation? _previewRotation;

	/// <summary>
	/// Transient values that drive the editor preview. Latched from the preview
	/// start value on first read, independent after that.
	/// </summary>
	[Hide, JsonIgnore]
	public float PreviewFloat { get => _previewFloat ??= PreviewStartFloat; set => _previewFloat = value; }

	[Hide, JsonIgnore]
	public bool PreviewBool { get => _previewBool ??= PreviewStartBool; set => _previewBool = value; }

	[Hide, JsonIgnore]
	public string PreviewID { get => _previewID ??= PreviewStartID ?? ""; set => _previewID = value; }

	[Hide, JsonIgnore]
	public Vector3 PreviewVector { get => _previewVector ??= PreviewStartVector; set => _previewVector = value; }

	[Hide, JsonIgnore]
	public bool PreviewTargetIsSet { get => _previewTargetIsSet ??= PreviewStartTargetIsSet; set => _previewTargetIsSet = value; }

	[Hide, JsonIgnore]
	public bool PreviewTargetIsBone { get => _previewTargetIsBone ??= PreviewStartTargetIsBone; set => _previewTargetIsBone = value; }

	[Hide, JsonIgnore, EnumDropdown]
	public AnimTargetPreviewMode PreviewTargetMode
	{
		get => !PreviewTargetIsSet ? AnimTargetPreviewMode.Unset
			: PreviewTargetIsBone ? AnimTargetPreviewMode.Bone
			: AnimTargetPreviewMode.Transform;
		set
		{
			PreviewTargetIsSet = value != AnimTargetPreviewMode.Unset;
			PreviewTargetIsBone = value == AnimTargetPreviewMode.Bone;
		}
	}

	[Hide, JsonIgnore, Editor( "animgraph.bone" )]
	public string PreviewBone { get => _previewBone ??= PreviewStartBone ?? ""; set => _previewBone = value; }

	[Hide, JsonIgnore]
	public Vector3 PreviewPosition { get => _previewPosition ??= PreviewStartPosition; set => _previewPosition = value; }

	[Hide, JsonIgnore]
	public Rotation PreviewRotation { get => _previewRotation ??= PreviewStartRotation; set => _previewRotation = value; }

	public void ResetPreview()
	{
		_previewFloat = null;
		_previewBool = null;
		_previewID = null;
		_previewVector = null;
		_previewTargetIsSet = null;
		_previewTargetIsBone = null;
		_previewBone = null;
		_previewPosition = null;
		_previewRotation = null;
	}

	public void RandomizePreview()
	{
		switch ( Type )
		{
			case AnimParameterType.Float:
				_previewFloat = HasRange ? Random.Shared.Float( Min, Max ) : Random.Shared.Float( 0f, 1f );
				break;
			case AnimParameterType.Bool:
				_previewBool = Random.Shared.Float() > 0.5f;
				break;
			case AnimParameterType.Vector:
				_previewVector = Random.Shared.VectorInSphere();
				break;
		}
	}

	public bool ReflectPreviewValues( Parameter other )
	{
		if ( Type != AnimParameterType.ID )
			return false;

		var changed = false;

		foreach ( var value in other.ExpectedValues )
		{
			if ( string.IsNullOrWhiteSpace( value ) || Options.Contains( value, StringComparer.Ordinal ) )
				continue;

			ExpectedValues.Add( value );
			changed = true;
		}

		return changed;
	}

	/// <summary>
	/// Copies another parameter's saved values onto this instance, keeping
	/// object identity (and transient preview values) intact.
	/// </summary>
	public Parameter CopyFrom( Parameter other )
	{
		Identifier = other.Identifier;
		Name = other.Name;
		Group = other.Group;
		Type = other.Type;
		PreviewStartFloat = other.PreviewStartFloat;
		Min = other.Min;
		Max = other.Max;
		PreviewStartBool = other.PreviewStartBool;
		PreviewStartID = other.PreviewStartID;
		PreviewStartVector = other.PreviewStartVector;
		EnumTypeName = other.EnumTypeName;
		ExpectedValues = [.. other.ExpectedValues];
		PreviewStartTargetIsSet = other.PreviewStartTargetIsSet;
		PreviewStartTargetIsBone = other.PreviewStartTargetIsBone;
		PreviewStartBone = other.PreviewStartBone;
		PreviewStartPosition = other.PreviewStartPosition;
		PreviewStartRotation = other.PreviewStartRotation;

		return this;
	}

	public Parameter Duplicate()
	{
		var copy = new Parameter().CopyFrom( this );
		copy.Identifier = Guid.NewGuid().ToString();
		return copy;
	}
}

public enum VirtualParameterLifetime
{
	Graph,
	[Title( "Referenced Branches" )]
	Branch
}

[JsonPolymorphic( TypeDiscriminatorPropertyName = "_class" )]
[JsonDerivedType( typeof( BoolVirtualParameter ), nameof( BoolVirtualParameter ) )]
[JsonDerivedType( typeof( IDVirtualParameter ), nameof( IDVirtualParameter ) )]
[JsonDerivedType( typeof( FloatVirtualParameter ), nameof( FloatVirtualParameter ) )]
[JsonDerivedType( typeof( VectorVirtualParameter ), nameof( VectorVirtualParameter ) )]
[JsonDerivedType( typeof( TargetVirtualParameter ), nameof( TargetVirtualParameter ) )]
[JsonDerivedType( typeof( BoneMaskVirtualParameter ), nameof( BoneMaskVirtualParameter ) )]
public abstract class VirtualParameter : IParameter
{
	[Hide]
	public string Identifier { get; set; } = Guid.NewGuid().ToString();

	[Property]
	public string Name { get; set; } = "Parameter";

	[Property]
	public string Group { get; set; } = "";

	[Property]
	public VirtualParameterLifetime Lifetime { get; set; } = VirtualParameterLifetime.Graph;

	[Hide, JsonIgnore]
	public abstract AnimParameterType Type { get; }

	[Hide, JsonIgnore]
	public bool IsVirtual => true;

	[Hide]
	public Data Graph { get; set; }

	protected VirtualParameter()
	{
		Graph = CreateGraph();
	}

	protected abstract Data CreateGraph();

	internal VirtualParameter CopyFrom( VirtualParameter other )
	{
		Identifier = other.Identifier;
		Name = other.Name;
		Group = other.Group;
		Lifetime = other.Lifetime;
		Graph = other.Graph;
		return this;
	}

	public VirtualParameter Duplicate()
	{
		var copy = (VirtualParameter)MemberwiseClone();
		copy.Identifier = Guid.NewGuid().ToString();
		copy.Graph = Graph.Clone( true );
		return copy;
	}
}

public sealed class BoolVirtualParameter : VirtualParameter
{
	public override AnimParameterType Type => AnimParameterType.Bool;
	protected override Data CreateGraph() => Data.CreateSubGraph<BoolResultNode>( GraphKind.VirtualParameterValueTree );
}

public sealed class IDVirtualParameter : VirtualParameter
{
	public override AnimParameterType Type => AnimParameterType.ID;
	protected override Data CreateGraph() => Data.CreateSubGraph<IDResultNode>( GraphKind.VirtualParameterValueTree );
}

public sealed class FloatVirtualParameter : VirtualParameter
{
	public override AnimParameterType Type => AnimParameterType.Float;
	protected override Data CreateGraph() => Data.CreateSubGraph<FloatResultNode>( GraphKind.VirtualParameterValueTree );
}

public sealed class VectorVirtualParameter : VirtualParameter
{
	public override AnimParameterType Type => AnimParameterType.Vector;
	protected override Data CreateGraph() => Data.CreateSubGraph<VectorResultNode>( GraphKind.VirtualParameterValueTree );
}

public sealed class TargetVirtualParameter : VirtualParameter
{
	public override AnimParameterType Type => AnimParameterType.Target;
	protected override Data CreateGraph() => Data.CreateSubGraph<TargetResultNode>( GraphKind.VirtualParameterValueTree );
}

public sealed class BoneMaskVirtualParameter : VirtualParameter
{
	public override AnimParameterType Type => AnimParameterType.BoneMask;
	protected override Data CreateGraph() => Data.CreateSubGraph<BoneMaskResultNode>( GraphKind.VirtualParameterValueTree );
}
