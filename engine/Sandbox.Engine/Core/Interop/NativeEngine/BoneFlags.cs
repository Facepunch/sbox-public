
namespace NativeEngine;

[Flags]
internal enum BoneFlags : uint
{
	FLAG_NO_BONE_FLAGS = 0,

	// 		FLAG_SIMULATION		= ( 1 << 0 ),
	// 		FLAG_RENDERING		= ( 1 << 1 ),
	FLAG_BONEFLEXDRIVER = (1 << 2), // bone is used to drive flexes
	FLAG_PHYSICS = (1 << 4),    // bone is used by the physics system  (SIM only)
	FLAG_ATTACHMENT = (1 << 5), // bone is used by an attachment point (SIM only)
	FLAG_ANIMATION = (1 << 6),  // animation system has saved data about this bone
	FLAG_MESH = (1 << 7),   // bone is used by a mesh
	FLAG_HITBOX = (1 << 8), // bone is used by a hitbox (SIM only)
	FLAG_RETARGET_SRC = (1 << 9),   // bone is a source of retargeting animation
	FLAG_BONE_USED_BY_VERTEX_LOD0 = (1 << 10), // bone (or child) is used by the top level model via skinned vertex (typically the most vertices)
	FLAG_BONE_USED_BY_VERTEX_LOD1 = (1 << 11), // bone (or child) is used by the next level down.  Forced to be part of LOD0 by modelbuilder
	FLAG_BONE_USED_BY_VERTEX_LOD2 = (1 << 12), // etc.
	FLAG_BONE_USED_BY_VERTEX_LOD3 = (1 << 13),
	FLAG_BONE_USED_BY_VERTEX_LOD4 = (1 << 14),
	FLAG_BONE_USED_BY_VERTEX_LOD5 = (1 << 15),
	FLAG_BONE_USED_BY_VERTEX_LOD6 = (1 << 16),
	FLAG_BONE_HIDDEN = (1 << 17), // bone (or child) is used by the bottom level mesh, typically the least vertices.
	FLAG_BONE_MERGE_READ = (1 << 18),   // dynamically set by CSkeletonInstance when it determines that this bone is read by bone merge
	FLAG_BONE_MERGE_WRITE = (1 << 19),  // dynamically set by CSkeletonInstance when it determines that this bone is written by bone merge

	FLAG_ALL_BONE_FLAGS = 0xfffff, // this needs to change if the below flags change their start bit

	// animation encoding flags
	BLEND_PREALIGNED = (1 << 20), // quaternions blend longest distance, code assumes they're stored already aligned
	FLAG_RIGIDLENGTH = (1 << 21), // bone length should never animate
	FLAG_PROCEDURAL = (1 << 22), // bone is evaluated by a procedural animation
}
