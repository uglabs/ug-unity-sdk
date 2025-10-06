using UnityEngine;
using UG.Exceptions;

namespace UG.Services
{
    public static class VADModelLoader
    {
        private static readonly string ModelAssetPath = "UG/silero_vad";
        private static readonly TextAsset ModelAsset = 
            UnityEngine.Resources.Load<TextAsset>(ModelAssetPath);
        
        public static byte[] LoadModel()
        {
            if (ModelAsset == null)
            {
                throw new UGSDKException("VAD model asset not found");
            }
            return ModelAsset.bytes;
        }
    }
}