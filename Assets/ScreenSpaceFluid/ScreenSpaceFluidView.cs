using TA.SurfaceDustGpu;
using UnityEngine;

namespace TA.ScreenSpaceFluid
{
    /// <summary>Camera-local settings. Only cameras with this component run the fluid feature.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    public sealed class ScreenSpaceFluidView : MonoBehaviour
    {
        public enum DisplayMode { Liquid, ParticleDepth, SmoothDepth, Normals, Thickness }
        public SurfaceDustEmitter emitter;
        public DisplayMode display;
        [Range(1, 5)] public int smoothingIterations = 4;
        [Range(1, 8)] public float filterRadius = 6;
        [Range(0.01f, 0.6f)] public float depthSigma = 0.2f;
        [Range(0.001f, 1)] public float thicknessScale = 0.12f;
        [Range(0, 0.1f)] public float refraction = 0.025f;
        public Color waterColor = new Color(0.025f, 0.42f, 0.55f);
        public Vector3 absorption = new Vector3(2.8f, 0.8f, 0.45f);
        public bool showControls = true;
        static readonly string[] Modes = { "Liquid", "Raw depth", "Smooth depth", "Normals", "Thickness" };

        void OnGUI()
        {
            if (!showControls || !emitter) return;
            GUILayout.BeginArea(new Rect(18, 18, 290, 310), GUI.skin.box);
            GUILayout.Label("SCREEN SPACE FLUID");
            GUILayout.Label("Particle depth -> smooth -> normal -> liquid");
            display = (DisplayMode)GUILayout.SelectionGrid((int)display, Modes, 2);
            GUILayout.Label($"Smooth radius  {filterRadius:F1} px");
            filterRadius = GUILayout.HorizontalSlider(filterRadius, 1, 8);
            GUILayout.Label($"Refraction  {refraction:F3}");
            refraction = GUILayout.HorizontalSlider(refraction, 0, 0.06f);
            GUILayout.Label($"Flow strength  {emitter.turbulence:F2}");
            emitter.turbulence = GUILayout.HorizontalSlider(emitter.turbulence, 0, 0.8f);
            if (GUILayout.Button(emitter.paused ? "Resume" : "Pause")) emitter.paused = !emitter.paused;
            if (GUILayout.Button("Restart")) emitter.Initialize();
            GUILayout.EndArea();
        }
    }
}
