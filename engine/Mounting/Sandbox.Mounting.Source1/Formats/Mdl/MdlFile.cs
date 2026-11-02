using System;

namespace Sandbox.Mounting.Source1;

internal sealed partial class MdlFile
{
	private readonly Header _header;

	public Vector3 EyePosition => _header.EyePosition;
	public Vector3 IlluminationPosition => _header.IllumPosition;
	public int IlluminationAttachment { get; }
	public float? ConstantDirectionalLightDot => (Flags & (1 << 13)) != 0 ? _header.ConstDirectionLightDot / 255f : null;
	public Vector3 ViewMin => _header.ViewBbMin;
	public Vector3 ViewMax => _header.ViewBbMax;

	private readonly StudioTexture[] _materials;
	private readonly string[] _materialNames;
	private readonly string[] _materialPaths;
	private readonly short[] _defaultSkin;
	private readonly short[] _skinFamilies;

	private readonly StudioBodyPart[] _bodyParts;
	private readonly string[] _bodyPartNames;

	private readonly StudioModel[] _models;
	private readonly StudioMesh[] _meshes;
	private readonly StudioEyeball[] _eyeballs;

	private readonly StudioBone[] _bones;
	private readonly string[] _boneNames;
	private readonly string[] _boneSurfaceProperties;

	private readonly StudioAttachment[] _attachments;
	private readonly string[] _attachmentNames;

	private readonly string[] _flexNames;
	private readonly StudioFlex[] _flexes;
	private readonly StudioVertAnim[] _vertAnims;

	public int Version => _header.Version;
	public int Checksum => _header.Checksum;
	public int NumTextures => _header.NumTextures;
	public int Flags => _header.Flags;
	public bool IsStaticProp => (Flags & 0x10) != 0;
	public float VertAnimFixedPointScale => (Flags & 0x00200000) != 0 ? _header.VertAnimFixedPointScale : 1.0f / 4096.0f;
	public Vector3 HullMin => _header.HullMin;
	public Vector3 HullMax => _header.HullMax;
	public string SurfaceProperty { get; }
	public IReadOnlyList<VmtNode> KeyValues { get; }

	public int TotalVertices => _meshes.Sum( x => x.NumVertices );

	public int BoneCount => _bones.Length;
	public StudioBone[] Bones => _bones;

	public IEnumerable<StudioAttachment> Attachments => _attachments;

	public IEnumerable<string> FlexNames => _flexNames;

	public int BodyPartCount => _bodyParts.Length;

	public string GetBoneName( int index )
	{
		return _boneNames[index];
	}

	public string GetBoneSurfaceProperty( int index ) => _boneSurfaceProperties[index];

	public string GetAttachmentName( int index )
	{
		return _attachmentNames[index];
	}

	public string GetBodyPartName( int index )
	{
		return _bodyPartNames[index];
	}

	public StudioBodyPart GetBodyPart( int index )
	{
		return _bodyParts[index];
	}

	public StudioModel GetModel( int bodyPartIndex, int index )
	{
		return _models[_bodyParts[bodyPartIndex].ModelIndex + index];
	}

	public StudioEyeball GetEyeball( int bodyPartIndex, int modelIndex, int eyeIndex )
	{
		var model = GetModel( bodyPartIndex, modelIndex );
		if ( (uint)eyeIndex >= model.NumEyeBalls )
			throw new ArgumentOutOfRangeException( nameof( eyeIndex ) );
		return _eyeballs[model.EyeBallIndex + eyeIndex];
	}

	private static bool IsFinite( Vector3 value ) => float.IsFinite( value.x ) && float.IsFinite( value.y ) && float.IsFinite( value.z );

	public StudioMesh GetMesh( int bodyPartIndex, int modelIndex, int meshIndex )
	{
		return _meshes[_models[_bodyParts[bodyPartIndex].ModelIndex + modelIndex].MeshIndex + meshIndex];
	}

	public StudioFlex GetMeshFlex( StudioMesh mesh, int flexIndex )
	{
		return _flexes[mesh.FlexIndex + flexIndex];
	}

	public ReadOnlySpan<StudioVertAnim> GetFlexVertAnims( StudioFlex flex )
	{
		return _vertAnims.AsSpan( flex.VertIndex, flex.NumVerts );
	}

	public int MaterialCount => _materials.Length;
	public int MaterialPathCount => _materialPaths.Length;
	public int SkinFamilyCount => _header.NumSkinFamilies;

	public string GetMaterialName( int index ) => _materialNames[index];

	internal int GetSkinMaterial( int meshMaterialIndex, int skinFamily )
	{
		if ( skinFamily < 0 || skinFamily >= Math.Max( 1, SkinFamilyCount ) )
			throw new ArgumentOutOfRangeException( nameof( skinFamily ) );
		if ( (uint)meshMaterialIndex >= (_defaultSkin.Length > 0 ? _header.NumSkinRef : _header.NumTextures) )
			throw new ArgumentOutOfRangeException( nameof( meshMaterialIndex ) );
		return _defaultSkin.Length > 0 ? _skinFamilies[skinFamily * _header.NumSkinRef + meshMaterialIndex] : meshMaterialIndex;
	}

	public string GetMaterialFullPath( int index, int pathIndex )
	{
		return GetMaterialFullPath( _materialNames[index], pathIndex );
	}

	internal string GetMaterialFullPath( string name, int pathIndex )
	{
		if ( pathIndex < 0 || pathIndex >= _materialPaths.Length ) throw new Exception( "Material path index out of range." );

		var path = _materialPaths[pathIndex];
		var fullPath = path.Length == 0 ? name : $"{path.TrimEnd( '/' )}/{name}";
		if ( !fullPath.StartsWith( "materials/", StringComparison.OrdinalIgnoreCase ) ) fullPath = $"materials/{fullPath}";

		return fullPath;
	}
}
