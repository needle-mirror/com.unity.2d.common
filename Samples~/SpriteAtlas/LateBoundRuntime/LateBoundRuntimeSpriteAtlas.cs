using UnityEngine;
using UnityEngine.U2D;
using System.Collections.Generic;

namespace UnityEngine.U2D.Common.Samples
{
    public class LateBoundRuntimeSpriteAtlas : MonoBehaviour
    {

        // List of Sprites to be packed in Runtime.
        public List<Sprite> m_RuntimeAtlasedSprites = new List<Sprite>();
        public float multiplier = 1;

        void OnEnable()
        {
            SpriteAtlasManager.atlasRequested += RequestLateBindingAtlas;
            SpriteAtlasManager.atlasRegistered += AtlasRegistered;
        }

        void OnDisable()
        {
            SpriteAtlasManager.atlasRequested -= RequestLateBindingAtlas;
            SpriteAtlasManager.atlasRegistered -= AtlasRegistered;
        }

        void RequestLateBindingAtlas(string tag, System.Action<SpriteAtlas> callback)
        {
            if (tag == "LateBoundRuntime")
            {
                var sa = UnityEngine.Resources.Load<SpriteAtlas>("LateBoundRuntime");
                callback(sa);
                SpriteAtlasCommon.CreateSpriteAtlasAtRuntimeDemo("SimpleLateBoundRuntimeAtlasSample",
                    m_RuntimeAtlasedSprites.ToArray(), TextureFormat.RGBA32, 1024, 1024, multiplier);
            }
            else
                Debug.Log("Error: Late binding callback with wrong atlas tag of " + tag);
        }

        void AtlasRegistered(SpriteAtlas spriteAtlas)
        {
            Debug.LogFormat("Registered {0}.", spriteAtlas.name);
        }
    }

}
