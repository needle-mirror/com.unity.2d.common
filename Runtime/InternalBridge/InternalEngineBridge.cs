using System;
using UnityEngine.UIElements;
using Unity.Collections;

namespace UnityEngine.U2D.Common
{
    // SRP-Batcher compatibility of a SpriteRenderer as a tri-state. Unlike IsSRPBatchingEnabled (bool),
    // this distinguishes "not computed yet" (Undetermined) from a determined "Incompatible", so callers
    // can defer a decision instead of treating an unknown as a permanent No.
    internal enum SpriteSRPBatchingState
    {
        Undetermined = 0,
        Incompatible = 1,
        Compatible = 2
    }

    internal static class InternalEngineBridge
    {
        public static void SetLocalAABB(SpriteRenderer spriteRenderer, Bounds aabb)
        {
            spriteRenderer.SetLocalAABB(aabb);
        }

        public static void SetDeformableBuffer(SpriteRenderer spriteRenderer, NativeArray<byte> src)
        {
            spriteRenderer.SetDeformableBuffer(src);
        }

        public static bool IsUsingDeformableBuffer(SpriteRenderer spriteRenderer, IntPtr buffer)
        {
            return spriteRenderer.IsUsingDeformableBuffer(buffer);
        }

        public static void SetupMaterialProperties(SpriteRenderer spriteRenderer)
        {
            SpriteRendererDataAccessExtensions.SetupMaterialProperties(spriteRenderer);
        }

        public static bool IsGPUSkinningEnabled(SpriteRenderer spriteRenderer)
        {
            return SpriteRendererDataAccessExtensions.IsGPUSkinningEnabled(spriteRenderer);
        }

        public static bool IsSRPBatchingEnabled(SpriteRenderer spriteRenderer)
        {
            return SpriteRendererDataAccessExtensions.IsSRPBatchingEnabled(spriteRenderer);
        }

        public static SpriteSRPBatchingState GetSRPBatchingState(SpriteRenderer spriteRenderer)
        {
#if UNITY_6000_7_OR_NEWER
            return (SpriteSRPBatchingState)SpriteRendererDataAccessExtensions.GetSRPBatchingState(spriteRenderer);
#else
            // Older editors lack the tri-state engine API; collapse to a determined result.
            return SpriteRendererDataAccessExtensions.IsSRPBatchingEnabled(spriteRenderer)
                ? SpriteSRPBatchingState.Compatible
                : SpriteSRPBatchingState.Incompatible;
#endif
        }

        public static void SetBatchDeformableBufferAndLocalAABBArray(SpriteRenderer[] spriteRenderers, NativeArray<IntPtr> buffers, NativeArray<int> bufferSizes, NativeArray<Bounds> bounds)
        {
            SpriteRendererDataAccessExtensions.SetBatchDeformableBufferAndLocalAABBArray(spriteRenderers, buffers, bufferSizes, bounds);
        }

        public static void SetBatchBoneTransformIndexAndLocalAABBArray(SpriteRenderer[] spriteRenderers, NativeArray<int> boneTransformIndices, NativeArray<Bounds> bounds)
        {
            SpriteRendererDataAccessExtensions.SetBatchBoneTransformIndexAndLocalAABBArray(spriteRenderers, boneTransformIndices, bounds);
        }

#if UNITY_EDITOR
        public static void SetLocalEulerHint(Transform t)
        {
            t.SetLocalEulerHint(t.GetLocalEulerAngles(t.rotationOrder));
        }


#endif

        public static int ConvertFloatToInt(float f)
        {
            return Animations.DiscreteEvaluationAttributeUtilities.ConvertFloatToDiscreteInt(f);
        }

        public static float ConvertIntToFloat(int i)
        {
            return Animations.DiscreteEvaluationAttributeUtilities.ConvertDiscreteIntToFloat(i);
        }

        public static void MarkDirty(this UnityEngine.Object obj)
        {
            obj.MarkDirty();
        }
    }
}
