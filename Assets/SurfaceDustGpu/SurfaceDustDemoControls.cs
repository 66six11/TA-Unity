using UnityEngine;

namespace TA.SurfaceDustGpu
{
    public sealed class SurfaceDustDemoControls : MonoBehaviour
    {
        public SurfaceDustEmitter emitter;
        bool visible = true;

        void OnGUI()
        {
            if (!emitter) return;
            if (!visible)
            {
                if (GUI.Button(new Rect(18, 18, 130, 30), "Show controls")) visible = true;
                return;
            }
            GUILayout.BeginArea(new Rect(18, 18, 300, 305), GUI.skin.box);
            GUILayout.Label("SURFACE DUST / GPU");
            GUILayout.Label("Mesh surface -> compute -> billboards");
            GUILayout.Label($"GPU particle slots: {emitter.AllocatedCount:N0}");
            GUILayout.Space(8);
            GUILayout.Label($"Outward speed  {emitter.outwardSpeed:F2}");
            emitter.outwardSpeed = GUILayout.HorizontalSlider(emitter.outwardSpeed, 0.05f, 1.5f);
            GUILayout.Label($"Turbulence  {emitter.turbulence:F2}");
            emitter.turbulence = GUILayout.HorizontalSlider(emitter.turbulence, 0, 2);
            GUILayout.Label($"Updraft  {emitter.wind.y:F2}");
            var wind = emitter.wind;
            wind.y = GUILayout.HorizontalSlider(wind.y, -0.3f, 1);
            emitter.wind = wind;
            GUILayout.Space(8);
            if (GUILayout.Button(emitter.paused ? "Resume" : "Pause")) emitter.paused = !emitter.paused;
            if (GUILayout.Button("Restart emission")) emitter.Initialize();
            if (GUILayout.Button("Hide controls")) visible = false;
            GUILayout.EndArea();
        }
    }
}
