using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

[ScriptedImporter(1, "photonvolume")]
public sealed class PhotonVolumeImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext context)
    {
        var bytes = File.ReadAllBytes(context.assetPath);
        if (bytes.Length != 64 * 64 * 64)
            throw new InvalidDataException("Expected a 64 cubed R8 volume.");
        var volume = new Texture3D(64, 64, 64, TextureFormat.R8, false)
        {
            name = Path.GetFileNameWithoutExtension(context.assetPath),
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear
        };
        volume.SetPixelData(bytes, 0);
        volume.Apply(false, true);
        context.AddObjectToAsset("volume", volume);
        context.SetMainObject(volume);
    }
}
