using System.Collections.Generic;
using UnityEditor.U2D.Sprites;

namespace UnityEditor.U2D.Common
{
    internal interface ISpriteCustomMetadataContainer
    {
        IDictionary<string, string> spriteCustomMetadata { get; }
    }

    internal class SpriteCustomMetadataProvider : ISpriteCustomDataProvider
    {
        public ISpriteCustomMetadataContainer dataContainer;

        public IEnumerable<string> GetKeys() => dataContainer.spriteCustomMetadata?.Keys;

        public void SetData(string key, string data)
        {
            if (dataContainer.spriteCustomMetadata != null)
                dataContainer.spriteCustomMetadata[key] = data;
        }

        public void RemoveData(string key)
        {
            dataContainer.spriteCustomMetadata?.Remove(key);
        }

        public bool GetData(string key, out string data)
        {
            data = null;
            return dataContainer.spriteCustomMetadata?.TryGetValue(key, out data) ?? false;
        }
    }
}
