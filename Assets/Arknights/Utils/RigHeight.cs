using Spine;
using Spine.Unity;
using UnityEngine;

namespace Tools {
    /// <summary>
    /// 按看得见的部分量身高 / How tall a Spine rig stands, by what can be seen.
    ///
    /// The height a Spine export records counts effects and invisible attachments - one rig here claims
    /// 7.16 units for a figure 3.84 tall - so a rig is measured instead: the top of the attachments it
    /// draws in its idle pose, above its origin, which is its feet. Scaled by that, every operator stands
    /// about as tall as the others, whatever size its rig was drawn at.
    ///
    /// 一处定义 / One definition for every place an operator stands: the rhythm stage
    /// (CharacterPresenter, a SkeletonAnimation in world units) and the Manufacture room (a
    /// SkeletonGraphic in canvas pixels). Each poses its own rig, asks for the top, and scales by it.
    /// It lives here, in Arknights, because both can see this assembly and neither can see the other.
    /// </summary>
    public static class RigHeight {
        /// <summary>
        /// The top of everything the skeleton draws as it is posed now, above its origin - its feet - in the
        /// rig's own units. Attachments at zero alpha are effects waiting their turn and do not count; neither
        /// does anything hanging below the feet, like a weapon held low. 0 when nothing is drawn.
        /// </summary>
        public static float VisibleTop(Skeleton skeleton) {
            float top = 0f;
            float[] vertices = new float[256];
            foreach (Slot slot in skeleton.DrawOrder) {
                if (!slot.Bone.Active) continue;

                int count;
                if (slot.Attachment is RegionAttachment region) {
                    if (slot.A * region.A <= 0.02f) continue;
                    region.ComputeWorldVertices(slot.Bone, vertices, 0);
                    count = 8;
                } else if (slot.Attachment is MeshAttachment mesh) {
                    if (slot.A * mesh.A <= 0.02f) continue;
                    count = mesh.WorldVerticesLength;
                    if (vertices.Length < count) vertices = new float[count];
                    mesh.ComputeWorldVertices(slot, vertices);
                } else continue;

                for (int i = 1; i < count; i += 2) top = Mathf.Max(top, vertices[i]);
            }

            return top;
        }

        /// <summary>
        /// The height to scale a rig by: what <see cref="VisibleTop"/> measured, or the export's own height
        /// for a rig with nothing visible to measure.
        /// </summary>
        public static float Standing(float visibleTop, SkeletonDataAsset rig, SkeletonData data) {
            return visibleTop > 0.01f ? visibleTop : data.Height * rig.scale;
        }

        /// <summary>
        /// The scale that makes a rig <paramref name="height"/> tall stand <paramref name="target"/> tall,
        /// times the operator's own <paramref name="heightScale"/> (OperatorMotion; 1 when unset). 1 for a
        /// rig with no height to go by.
        /// </summary>
        public static float ScaleFor(float height, float target, float heightScale) {
            float own = heightScale > 0f ? heightScale : 1f;
            return height > 0.01f ? target * own / height : 1f;
        }
    }
}
