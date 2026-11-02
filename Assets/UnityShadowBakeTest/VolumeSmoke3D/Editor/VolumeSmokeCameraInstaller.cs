using System.IO;
using UnityEditor;
using UnityEngine;

namespace TA.VolumeSmoke3D.Editor
{
    /// <summary>Upgrades an already-open demo without interrupting its running simulation.</summary>
    [InitializeOnLoad]
    public static class VolumeSmokeCameraInstaller
    {
        static VolumeSmokeCameraInstaller()
        {
            EditorApplication.delayCall += Install;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode) Install();
            };
        }

        [MenuItem("Tools/TA/Volume Smoke 3D/Attach Free Camera to Open Demo")]
        public static void Install()
        {
            int cameras = 0, added = 0, missing = 0;
            foreach (var camera in Object.FindObjectsByType<VolumeSmokeCamera>())
            {
                if (camera.gameObject.scene.path != "Assets/VolumeSmoke3D/VolumeSmoke3DDemo.unity") continue;
                cameras++;
                if (!camera.TryGetComponent<VolumeSmokeFreeCamera>(out _))
                {
                    camera.gameObject.AddComponent<VolumeSmokeFreeCamera>(); added++;
                }
                if(camera.simulation)
                {
                    if(!camera.simulation.sourceCamera)camera.simulation.sourceCamera=camera.GetComponent<Camera>();
                    if(camera.simulation.source&&!camera.simulation.source.TryGetComponent<VolumeSmokeSourceMotion>(out _))
                        camera.simulation.source.gameObject.AddComponent<VolumeSmokeSourceMotion>();
                }
                foreach (var root in camera.gameObject.scene.GetRootGameObjects())
                    foreach (var child in root.GetComponentsInChildren<Transform>(true))
                        missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject);
            }
            if (cameras > 0) File.WriteAllText("Library/VolumeSmoke3D-controls.txt",
                $"Free camera active on {cameras} demo camera(s); newly attached: {added}; missing scene scripts: {missing}. RMB + WASD, Q/E, Shift, mouse wheel.");
        }
    }
}
