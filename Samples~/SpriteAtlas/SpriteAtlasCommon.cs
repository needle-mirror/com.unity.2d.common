using UnityEngine;
using UnityEngine.U2D;
using Unity.Collections;
using Unity.Mathematics;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Assertions;

namespace UnityEngine.U2D.Common.Samples
{
    public static class SpriteAtlasCommon
    {

        /// <summary>
        /// Internal class to represent a sprite's position and size in an atlas.
        /// </summary>
        internal class AtlasFit
        {
            public int2 position;
            public int2 size;
            public int index;
        };

        /// <summary>
        /// Internal class to manage sprite packing data for a single atlas page.
        /// </summary>
        internal class AtlasPageData
        {
            public List<AtlasFit> packs;
            public int2 size;
            public int2 filled;

            public void Pack(AtlasFit atlasFit)
            {
                filled.x = filled.x > (atlasFit.position.x + atlasFit.size.x) ? filled.x : atlasFit.position.x + atlasFit.size.x;
                filled.y = filled.y > (atlasFit.position.y + atlasFit.size.y) ? filled.y : atlasFit.position.y + atlasFit.size.y;
                packs.Add(atlasFit);
            }
        };

        /// <summary>
        /// Resizes a texture to specified dimensions using a temporary render texture with trilinear filtering.
        /// </summary>
        /// <param name="tex">Texture to resize</param>
        /// <param name="width">New width of the texture</param>
        /// <param name="height">New height of the texture</param>
        internal static void Scale(Texture2D tex, int width, int height)
        {
            Rect texR = new(0, 0, width, height);
            Blitter(tex, width, height);
            tex.Reinitialize(width, height);
            tex.ReadPixels(texR, 0, 0, true);
            tex.Apply(true);
        }

        /// <summary>
        /// Copies source texture into render texture with trilinear filtering for smooth scaling.
        /// </summary>
        /// <param name="src">Source texture to copy</param>
        /// <param name="width">Width of the render texture</param>
        /// <param name="height">Height of the render texture</param>
        internal static void Blitter(Texture2D src, int width, int height)
        {
            src.filterMode = FilterMode.Trilinear;
            src.Apply(true);
            RenderTexture rtt = new(width, height, 32);
            Graphics.SetRenderTarget(rtt);
            GL.LoadPixelMatrix(0, 1, 1, 0);
            GL.Clear(true, true, new Color(0, 0, 0, 0));
            Graphics.DrawTexture(new Rect(0, 0, 1, 1), src);
        }

        /// <summary>
        /// Copies sprite texture from source to atlas with boundary handling.
        /// </summary>
        /// <param name="dstAtlas">Destination atlas texture</param>
        /// <param name="srcTexture">Source sprite texture</param>
        /// <param name="sx">Source sprite X coordinate</param>
        /// <param name="sy">Source sprite Y coordinate</param>
        /// <param name="width">Width of the sprite</param>
        /// <param name="height">Height of the sprite</param>
        /// <param name="dx">Destination X coordinate</param>
        /// <param name="dy">Destination Y coordinate</param>
        internal static void BlitSpriteToAtlas(Texture2D dstAtlas, Texture2D srcTexture, int sx, int sy, int width, int height, int dx, int dy)
        {
            dstAtlas.CopyPixels(srcTexture, 0, 0, sx, sy, width, height, 0, dx, dy);
            dstAtlas.CopyPixels(srcTexture, 0, 0, sx, sy, width, 1, 0, dx, dy);
            dstAtlas.CopyPixels(srcTexture, 0, 0, sx, sy, 1, height, 0, dx, dy);
            dstAtlas.CopyPixels(srcTexture, 0, 0, sx + width - 1, sy, 1, height - 1, 0, dx + width + 1, dy + 1);
            dstAtlas.CopyPixels(srcTexture, 0, 0, sx, sy + height - 1, width - 1, 1, 0, dx + 1, dy + height + 1);
        }

        /// <summary>
        /// Validates if two rectangles are non-overlapping for proper sprite placement.
        /// </summary>
        /// <param name="rect">Sprite rectangle</param>
        /// <param name="atlasRect">Atlas page rectangle</param>
        /// <returns>True if rectangles are non-overlapping, false otherwise</returns>
        internal static bool CheckValid(Rect rect, Rect atlasRect)
        {
            return (rect.x > (atlasRect.x + atlasRect.width) || rect.y > (atlasRect.y + atlasRect.height) || atlasRect.x > (rect.x + rect.width) || atlasRect.y > (rect.y + rect.height));
        }

        /// <summary>
        /// Attempts to place sprite in atlas page using random positions with collision checks.
        /// </summary>
        /// <param name="atlasFit">Sprite to place</param>
        /// <param name="atlasPage">Atlas page to place in</param>
        /// <param name="randomFitTries">Number of random position attempts</param>
        /// <returns>Valid position (x,y) if found, otherwise (-1,-1)</returns>
        internal static int2 FitSprite(AtlasFit atlasFit, AtlasPageData atlasPage, int randomFitTries)
        {
            var fitPosition = new int2(-1, -1);
            var fitFound = false;
            var filled = atlasPage.filled;

            for (int test = 0; test < randomFitTries; ++test)
            {

                var tx = Random.Range(0, filled.x);
                var ty = Random.Range(0, filled.y);

                if (tx + atlasFit.size.x > atlasPage.size.x || ty + atlasFit.size.y > atlasPage.size.y)
                {
                    continue;
                }
                var fit = true;

                for (int check = 0; check < atlasPage.packs.Count; ++check)
                {
                    var packed = atlasPage.packs[check];
                    var RectSprite = new Rect(tx, ty, atlasFit.size.x, atlasFit.size.y);
                    var PackSprite = new Rect(packed.position.x, packed.position.y, packed.size.x, packed.size.y);
                    if (!CheckValid(RectSprite,PackSprite))
                    {
                        fit = false;
                        break;
                    }
                }

                if (fit)
                {
                    fitFound = true;
                    filled.x = fitPosition.x = tx;
                    filled.y = fitPosition.y = ty;
                }

            }

            if (fitFound)
            {
                return fitPosition;
            }

            if (atlasFit.size.x < atlasPage.size.x - filled.x && atlasFit.size.y < atlasPage.size.y - filled.y)
            {
                fitPosition.x = filled.x;
                fitPosition.y = filled.y;
            }
            return fitPosition;
        }

        /// <summary>
        /// Packs sprite into available atlas pages with overflow handling.
        /// </summary>
        /// <param name="atlasFit">Sprite to pack</param>
        /// <param name="atlasPages">List of atlas pages</param>
        /// <param name="randomFitTries">Number of random position attempts</param>
        /// <param name="inputSpriteCount">Total number of sprites</param>
        /// <param name="maxTextureSize">Maximum size of an atlas page</param>
        internal static void PackSprite(AtlasFit atlasFit, List<AtlasPageData> atlasPages, int randomFitTries, int inputSpriteCount, int maxTextureSize)
        {
            var fit = false;

            foreach (var atlasPage in atlasPages)
            {
                var atlasPageData = atlasPage;
                var fitPosition = FitSprite(atlasFit, atlasPage, randomFitTries);
                if (fitPosition.x != -1)
                {
                    atlasFit.position = fitPosition;
                    atlasPage.Pack(atlasFit);
                    fit = true;
                }
            }

            if (!fit)
            {
                var atlasPage = new AtlasPageData();
                atlasPage.packs = new List<AtlasFit>();
                atlasPage.size = new int2(maxTextureSize, maxTextureSize);
                atlasFit.position = new int2(0, 0);
                atlasPage.Pack(atlasFit);
                atlasPages.Add(atlasPage);
            }
        }

        /// <summary>
        /// Creates a sprite atlas at runtime by packing sprites into optimized texture pages and generating a final atlas texture with specified dimensions and scaling.
        /// </summary>
        /// <param name="nameTag">The name of the atlas to create</param>
        /// <param name="inputSprites">Array of sprites to pack into the atlas</param>
        /// <param name="fmt">Texture format for the atlas</param>
		/// <param name="width">Max Texture Width</param>
		/// <param name="height">Max Texture Height</param>
		/// <param name="multiplier">Atlas Scale Multiplier</param>
        public static void CreateSpriteAtlasAtRuntimeDemo(string nameTag, Sprite[] inputSprites, TextureFormat fmt, int width, int height, float multiplier)
        {

            var spriteCount = inputSprites.Length;
            var atlasPages = new List<AtlasPageData>();
            var spriteIndex = 0;
            inputSprites = inputSprites.OrderBy(x => x.textureRect.width * x.textureRect.height).Reverse().ToArray();

            // Pack First
            foreach (var sprite in inputSprites)
            {
                if (null != sprite && null != sprite.texture)
                {
                    var atlasFit = new AtlasFit();
                    atlasFit.index = spriteIndex;
                    atlasFit.size = new int2((int)sprite.rect.size.x, (int)sprite.rect.size.y);
                    PackSprite(atlasFit, atlasPages, 128, spriteCount, 1024);
                }
                spriteIndex++;
            }

            var atlasPacked = new AtlasPage[atlasPages.Count];
            var pageIndex = 0;

            // Create AtlasPages.
            foreach (var inputAtlasPage in atlasPages)
            {
                var atlasPage = new AtlasPage();
                var packedTexture = new Texture2D(width, height, fmt, false);
                packedTexture.name = nameTag;
                atlasPage.assets = new ObjectData[inputAtlasPage.packs.Count];
                for (int i = 0; i < inputAtlasPage.packs.Count; ++i)
                {
                    var atlasFit = inputAtlasPage.packs[i];
                    var srcRect = inputSprites[atlasFit.index].textureRect;
                    BlitSpriteToAtlas(packedTexture, inputSprites[atlasFit.index].texture, (int)srcRect.x, (int)srcRect.y, (int)srcRect.width, (int)srcRect.height, atlasFit.position.x, atlasFit.position.y);
                    var objectData = new ObjectData();
                    objectData.asset = inputSprites[atlasFit.index].GetEntityId();
                    objectData.packInfo = new Vector4(atlasFit.position.x, atlasFit.position.y, 0, 0);
                    atlasPage.assets[i] = objectData;
                }
                var textureData = new TextureData[1];
                Scale(packedTexture, (int)((float)packedTexture.width * multiplier), (int)((float)packedTexture.height * multiplier));
                textureData[0].texture = packedTexture.GetEntityId();
                textureData[0].mapName = "_MainTex";
                atlasPage.packedTextures = textureData;
                atlasPacked[pageIndex++] = atlasPage;
            }

            var config = new SpriteAtlasRuntimeConfig();
            config.scaleMultiplier = multiplier;
            SpriteAtlasManager.CreateSpriteAtlas(nameTag, config, atlasPacked);
        }
    }

}
