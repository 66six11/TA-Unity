using UnityEngine;

namespace TA.ShadowChromaticDemo
{
    public sealed class UnityMixedShadowComparison : MonoBehaviour
    {
        public Renderer[] receivers;
        public Material[] nativeMaterials;
        public Material[] customMaterials;
        public Light mixedSun;
        public bool useNativeLit;
        [Range(0,4)] public int viewMode;
        public bool artEnabled=true;
        [Range(0,1)] public float artStrength=0.65f;
        [Range(0.1f,1)] public float artTerminatorWidth=0.6f;
        [Tooltip("Dark-side RGB response control; not an additive edge color.")]
        public Color shadowEdgeColor=new Color(0.55f,0.2f,0.78f);
        [Tooltip("Light-side RGB response control; not an additive edge color.")]
        public Color lightEdgeColor=new Color(1,0.48f,0.12f);

        void OnEnable() => Apply();
        void OnValidate() => Apply();
        void OnDisable()
        {
            Shader.SetGlobalFloat("_TAReceiverView",0);
            Shader.SetGlobalFloat("_TAArtStrength",0);
        }

        public void Apply()
        {
            Shader.SetGlobalFloat("_TAReceiverView",useNativeLit ? 0 : viewMode);
            Shader.SetGlobalFloat("_TAArtStrength",artEnabled && !useNativeLit ? artStrength : 0);
            Shader.SetGlobalFloat("_TAArtTerminatorWidth",artTerminatorWidth);
            Shader.SetGlobalVector("_TAArtShadowColor",LinearColor(shadowEdgeColor));
            Shader.SetGlobalVector("_TAArtLightColor",LinearColor(lightEdgeColor));
            int channel = mixedSun ? mixedSun.bakingOutput.occlusionMaskChannel : 0;
            Shader.SetGlobalFloat("_TAReceiverMaskChannel",Mathf.Max(0,channel));
            if (receivers == null || nativeMaterials == null || customMaterials == null) return;
            for (int i=0;i<receivers.Length;i++)
                if (receivers[i] && i<nativeMaterials.Length && i<customMaterials.Length)
                    receivers[i].sharedMaterial=useNativeLit ? nativeMaterials[i] : customMaterials[i];
        }

        static Vector4 LinearColor(Color color)
        {
            color=color.linear;
            return new Vector4(color.r,color.g,color.b,1);
        }

        void OnGUI()
        {
            var previous=GUI.matrix;
            float scale=Mathf.Clamp(Screen.width/1280f,0.65f,2);
            GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));
            GUILayout.BeginArea(new Rect(20,20,450,390),GUI.skin.box);
            GUILayout.Label("UNITY LIGHTMAP / SH + MIXED SHADOW RECEIVER");
            GUILayout.Label("Warm direct light + baked environment / indirect light");
            GUI.changed=false;
            useNativeLit=GUILayout.Toggle(useNativeLit,"Native URP Lit baseline (off = custom receiver)");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Combined")) viewMode=0;
            if (GUILayout.Button("Baked GI / SH")) viewMode=1;
            if (GUILayout.Button("Mixed visibility")) viewMode=2;
            if (GUILayout.Button("Shadowmask")) viewMode=3;
            if (GUILayout.Button("Response area")) viewMode=4;
            GUILayout.EndHorizontal();
            artEnabled=GUILayout.Toggle(artEnabled,"Unified self + cast shadow colors");
            GUILayout.Label($"Color strength: {artStrength:F2}");
            artStrength=GUILayout.HorizontalSlider(artStrength,0,1);
            GUILayout.Label($"Surface falloff range (N dot L): {artTerminatorWidth:F2}");
            artTerminatorWidth=GUILayout.HorizontalSlider(artTerminatorWidth,0.1f,1);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Warm"))
            {
                shadowEdgeColor=new Color(0.75f,0.22f,0.12f);
                lightEdgeColor=new Color(1,0.58f,0.15f);
            }
            if (GUILayout.Button("Cool"))
            {
                shadowEdgeColor=new Color(0.36f,0.15f,0.8f);
                lightEdgeColor=new Color(0.15f,0.7f,0.85f);
            }
            if (GUILayout.Button("Purple / orange"))
            {
                shadowEdgeColor=new Color(0.55f,0.2f,0.78f);
                lightEdgeColor=new Color(1,0.48f,0.12f);
            }
            GUILayout.EndHorizontal();
            if (GUI.changed) Apply();
            GUILayout.EndArea();
            GUI.matrix=previous;
        }
    }
}
