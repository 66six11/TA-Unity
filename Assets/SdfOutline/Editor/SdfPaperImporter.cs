using UnityEditor;
using UnityEngine;

namespace TA.SdfOutline.Editor
{
    [InitializeOnLoad]
    public sealed class SdfPaperImporter : AssetPostprocessor
    {
        const string Path = "Assets/SdfOutline/Resources/SdfPaperGrain.png";
        static SdfPaperImporter() { EditorApplication.delayCall += Configure; }
        void OnPreprocessTexture()
        {
            if (assetPath == Path) Apply((TextureImporter)assetImporter);
        }
        static void Apply(TextureImporter importer)
        {
            importer.sRGBTexture = false;
            // Mirror tiles keep the generated image continuous at every join.
            importer.wrapMode = TextureWrapMode.Mirror;
            importer.filterMode = FilterMode.Trilinear;
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.npotScale = TextureImporterNPOTScale.None;
        }
        static void Configure()
        {
            var importer = AssetImporter.GetAtPath(Path) as TextureImporter;
            if (!importer || (!importer.sRGBTexture && importer.wrapMode == TextureWrapMode.Mirror && importer.mipmapEnabled && importer.textureCompression == TextureImporterCompression.Uncompressed)) return;
            Apply(importer); importer.SaveAndReimport();
        }
    }
}
