using System;
using System.Collections.Generic;
using System.IO;
using Sandbox.Mounting;
using static Sandbox.Mounting.Source1.BspGeometry;

namespace Sandbox.Mounting.Source1;

internal enum PropPhysics
{
	Static,
	Rigid,
	Ragdoll
}

internal readonly record struct MapProp( string Model, string Name, Vector3 Position, Angles Angles, float Scale, int Skin, int Solid, Color Tint, bool Visible = true, PropPhysics Physics = PropPhysics.Static, bool StartAsleep = false, bool MotionEnabled = true, float MassScale = 1, bool Gameplay = false, int StaticPropIndex = -1, uint StaticPropFlags = 0, Vector3 LightingOrigin = default, ushort[] Leaves = null, string RagdollPose = null );
