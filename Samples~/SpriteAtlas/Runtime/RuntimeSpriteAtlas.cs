using UnityEngine;
using UnityEngine.U2D;
using System.Collections.Generic;

namespace UnityEngine.U2D.Common.Samples
{
    public class RuntimeSpriteAtlas : MonoBehaviour
    {

        // List of Sprites to be packed in Runtime.
        public List<Sprite> m_RuntimeAtlasedSprites = new List<Sprite>();
        public float multiplier = 1;

        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {
            SpriteAtlasCommon.CreateSpriteAtlasAtRuntimeDemo("SimpleRuntimeAtlasSample",
                m_RuntimeAtlasedSprites.ToArray(), TextureFormat.RGBA32, 1024, 1024, multiplier);
        }

        // Update is called once per frame
        void Update()
        {

        }
    }

}
