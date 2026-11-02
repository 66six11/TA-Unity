using UnityEngine;

namespace TA.ShadowChromaticDemo
{
    public sealed class ChromaticShadowControls : MonoBehaviour
    {
        public Renderer[] receivers;
        public Light sun;
        public Color sunColor = new Color(1, 0.83f, 0.64f);
        public Color skyColor = new Color(0.48f, 0.65f, 1);
        public Color groundColor = new Color(0.2f, 0.17f, 0.14f);
        [Range(0, 8)] public float sunIntensity = 4;
        [Range(0, 3)] public float skyIntensity = 0.4f;
        [Range(0.01f, 0.5f)] public float softness = 0.22f;
        public bool neutralLighting;
        [Range(0, 3)] public int viewMode;
        MaterialPropertyBlock properties;

        void OnEnable() => Apply();
        void OnValidate() => Apply();

        public void Apply()
        {
            properties ??= new MaterialPropertyBlock();
            Color upper = neutralLighting ? Neutral(skyColor) : skyColor.linear;
            Color lower = neutralLighting ? Neutral(groundColor) : groundColor.linear;
            Color solar = (neutralLighting ? Neutral(sunColor) : sunColor.linear) * sunIntensity;
            properties.SetVector("_SunIrradiance", new Vector4(solar.r, solar.g, solar.b, 1));
            properties.SetVector("_SkyRadiance", new Vector4(upper.r, upper.g, upper.b, 1));
            properties.SetVector("_GroundRadiance", new Vector4(lower.r, lower.g, lower.b, 1));
            properties.SetFloat("_SkyIntensity", skyIntensity);
            properties.SetFloat("_Softness", softness);
            properties.SetFloat("_ViewMode", viewMode);
            if (sun)
            {
                sun.color = neutralLighting ? Neutral(sunColor).gamma : sunColor;
                sun.intensity = sunIntensity;
            }
            if (receivers == null) return;
            foreach (var receiver in receivers)
                if (receiver) receiver.SetPropertyBlock(properties);
        }

        // Preserve linear luminance when removing color for the comparison.
        static Color Neutral(Color srgb)
        {
            Color c = srgb.linear;
            float y = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
            return new Color(y, y, y, 1);
        }

        void OnGUI()
        {
            var previousMatrix = GUI.matrix;
            float scale = Mathf.Clamp(Screen.width / 1280f, 0.65f, 2);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            GUILayout.BeginArea(new Rect(20, 20, 385, 310), GUI.skin.box);
            GUILayout.Label("WARM SUN + COOL SKY / SCALAR PCF");
            GUILayout.Label("Shadow color comes from the light balance.");
            GUI.changed = false;
            neutralLighting = GUILayout.Toggle(neutralLighting, "Neutral lights (same luminance comparison)");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Combined")) viewMode = 0;
            if (GUILayout.Button("Sun")) viewMode = 1;
            if (GUILayout.Button("Sky")) viewMode = 2;
            if (GUILayout.Button("PCF")) viewMode = 3;
            GUILayout.EndHorizontal();
            GUILayout.Label($"Sun irradiance scale: {sunIntensity:F2}");
            sunIntensity = GUILayout.HorizontalSlider(sunIntensity, 0, 8);
            GUILayout.Label($"Sky radiance scale: {skyIntensity:F2}");
            skyIntensity = GUILayout.HorizontalSlider(skyIntensity, 0, 3);
            GUILayout.Label($"PCF filter radius: {softness:F3} world units");
            softness = GUILayout.HorizontalSlider(softness, 0.01f, 0.5f);
            if (GUI.changed) Apply();
            GUILayout.EndArea();
            GUI.matrix = previousMatrix;
        }
    }
}

