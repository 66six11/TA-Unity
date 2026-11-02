using UnityEngine;

namespace TA.SdfOutline
{
    [DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    public sealed class SdfOutlineControls : MonoBehaviour
    {
        public bool singleConvexTarget = true;
        public Renderer convexTarget;
        public SdfOutlineFeature.Display display;
        public SdfOutlineFeature.Placement placement;
        [Range(0, 64)] public float width = 5;
        [Range(0.5f, 4)] public float softness = 1;
        [Range(4, 256)] public float distanceRange = 32;
        public Color outlineColor = new Color(1, 0.57f, 0.08f, 1);
                public bool showPanel = true;
        public bool paperEnabled = true;
        [Range(0, 1)] public float paperStrength = 0.45f;
        [Range(0.25f, 4)] public float paperScale = 1;
        [Range(0, 3)] public float edgeRoughness = 0.8f;
                public bool linesEnabled = true;
        public enum LineRegion { Outside, Inside, Both }
        public LineRegion lineRegion;
        [Range(3, 32)] public float lineSpacing = 9;
        [Range(0.5f, 4)] public float lineWidth = 1;
        [Range(8, 192)] public float lineRange = 64;
        [Range(0, 1)] public float lineOpacity = 0.7f;
        public Color lineColor = new Color(0.25f, 0.68f, 0.75f, 1);
        public bool dashesEnabled = true;
        [Range(1,64)] public int dashCount=20;
        [Range(0,1)] public float dashDuty=0.65f;
        [Range(0,1)] public float dashPhase;
        [Range(0,180)] public float projectionAngle;
        public bool spikeDashesEnabled=true;
        [Range(2,128)] public int spikeCount=48;
        [Range(0,1)] public float spikePhase;
        [Range(0,48)] public float spikeHeight=18;
        [Range(0,24)] public float spikeOffset=3;
        [Range(0,1)] public float spikeRandomness=0.65f;
        [Range(0,999)] public int spikeSeed=17;
        static readonly string[] LineRegions = { "Outside", "Inside", "Both" };
        static readonly string[] Views = { "Composite", "Mask", "Signed SDF", "JFA Seeds", "SDF Bands", "Original", "Paper Grain", "Contour Lines", "Projection U", "Spike Dashes", "Spike Shape" };
        static readonly string[] Placements = { "Outside", "Inside", "Centered" };
        Vector2 scroll;
        void OnGUI()
        {
            float scale = Mathf.Min(1, Screen.width / 720f, Screen.height / 540f);
            var previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            float x = Screen.width / scale - 302;
            if (GUI.Button(new Rect(x, 12, 290, 28), showPanel ? "SDF CONTROLS  /  Hide" : "SDF CONTROLS  /  Show")) showPanel = !showPanel;
            if (showPanel)
            {
                var panelRect = new Rect(x, 44, 290, Mathf.Min(650, Screen.height / scale - 56));
                var previousColor = GUI.color;
                GUI.color = new Color(0.055f, 0.07f, 0.09f, 0.97f);
                GUI.DrawTexture(panelRect, Texture2D.whiteTexture);
                GUI.color = previousColor;
                GUILayout.BeginArea(panelRect, GUI.skin.box);
                scroll = GUILayout.BeginScrollView(scroll);
                GUILayout.Label("SINGLE CONVEX TARGET");
                singleConvexTarget=GUILayout.Toggle(singleConvexTarget,"Restrict mask to selected convex target");
                GUILayout.Label(convexTarget ? "Target: "+convexTarget.name : "Assign Convex Target in camera Inspector.");
                GUILayout.Label("Fully visible convex silhouette only; avoid occlusion.");
                GUILayout.Label("RENDER VIEW");
                if((int)display>10)display=SdfOutlineFeature.Display.Composite;
                display = (SdfOutlineFeature.Display)GUILayout.SelectionGrid((int)display, Views, 2, GUILayout.Height(168));
                GUILayout.Space(8);
                GUILayout.Label(Description(display));
                if (display == SdfOutlineFeature.Display.SignedDistance || display == SdfOutlineFeature.Display.DistanceBands)
                {
                    GUILayout.Label($"SDF display range: {distanceRange:F0} px");
                    distanceRange = GUILayout.HorizontalSlider(distanceRange, 4, 256);
                    GUILayout.Label("Blue: inside (-) / Orange: outside (+)");
                }
                GUILayout.Space(8);
                GUILayout.Label("OUTLINE");
                placement = (SdfOutlineFeature.Placement)GUILayout.SelectionGrid((int)placement, Placements, 3);
                GUILayout.Label($"Width: {width:F1} px"); width = GUILayout.HorizontalSlider(width, 0, 32);
                GUILayout.Label($"Softness: {softness:F1} px"); softness = GUILayout.HorizontalSlider(softness, 0.5f, 4);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Amber")) outlineColor = new Color(1, 0.57f, 0.08f, 1);
                if (GUILayout.Button("Cyan")) outlineColor = Color.cyan;
                if (GUILayout.Button("White")) outlineColor = Color.white;
                GUILayout.EndHorizontal();
                GUILayout.Space(8);
                GUILayout.Label("DIRECTIONAL PROJECTION DASHES");
                dashesEnabled=GUILayout.Toggle(dashesEnabled,"Enable approximate dashes");
                GUILayout.Label($"Dash count: {dashCount}");dashCount=Mathf.RoundToInt(GUILayout.HorizontalSlider(dashCount,1,64));
                GUILayout.Label($"Visible fraction: {dashDuty:F2}");dashDuty=GUILayout.HorizontalSlider(dashDuty,0,1);
                GUILayout.Label($"Phase: {dashPhase:F2}");dashPhase=GUILayout.HorizontalSlider(dashPhase,0,1);
                GUILayout.Label($"Projection axis: {projectionAngle:F0} deg");projectionAngle=GUILayout.HorizontalSlider(projectionAngle,0,180);
                GUILayout.Label("Shared projection bounds. Concave shapes may fold.");
                GUILayout.Label("SPIKE SHAPE / SAME DASH MASK");
                spikeDashesEnabled=GUILayout.Toggle(spikeDashesEnabled,"Shape dashed strokes into spikes");
                GUILayout.Label($"Spike count: {spikeCount}");spikeCount=Mathf.RoundToInt(GUILayout.HorizontalSlider(spikeCount,2,128));
                GUILayout.Label($"Spike phase: {spikePhase:F2}");spikePhase=GUILayout.HorizontalSlider(spikePhase,0,1);
                GUILayout.Label($"Spike height: {spikeHeight:F1} px");spikeHeight=GUILayout.HorizontalSlider(spikeHeight,0,48);
                GUILayout.Label($"Outer offset: {spikeOffset:F1} px");spikeOffset=GUILayout.HorizontalSlider(spikeOffset,0,24);
                GUILayout.Label($"Randomness: {spikeRandomness:F2}");spikeRandomness=GUILayout.HorizontalSlider(spikeRandomness,0,1);
                GUILayout.Label($"Random seed: {spikeSeed}");spikeSeed=Mathf.RoundToInt(GUILayout.HorizontalSlider(spikeSeed,0,999));
                GUILayout.Label("Spike pattern is independent of dash spacing.");
                GUILayout.Label("CONTOUR LINES");
                linesEnabled = GUILayout.Toggle(linesEnabled, "Enable contour lines");
                lineRegion = (LineRegion)GUILayout.SelectionGrid((int)lineRegion, LineRegions, 3);
                GUILayout.Label($"Spacing: {lineSpacing:F1} px"); lineSpacing = GUILayout.HorizontalSlider(lineSpacing, 3, 32);
                GUILayout.Label($"Line width: {lineWidth:F1} px"); lineWidth = GUILayout.HorizontalSlider(lineWidth, 0.5f, 4);
                GUILayout.Label($"Range: {lineRange:F0} px"); lineRange = GUILayout.HorizontalSlider(lineRange, 8, 192);
                GUILayout.Label($"Opacity: {lineOpacity:F2}"); lineOpacity = GUILayout.HorizontalSlider(lineOpacity, 0, 1);
                GUILayout.Label("PAPER TEXTURE");
                paperEnabled = GUILayout.Toggle(paperEnabled, "Enable paper roughness");
                GUILayout.Label($"Grain strength: {paperStrength:F2}"); paperStrength = GUILayout.HorizontalSlider(paperStrength, 0, 1);
                GUILayout.Label($"Texture scale: {paperScale:F2}"); paperScale = GUILayout.HorizontalSlider(paperScale, 0.25f, 4);
                GUILayout.Label($"Edge roughness: {edgeRoughness:F1} px"); edgeRoughness = GUILayout.HorizontalSlider(edgeRoughness, 0, 3);
                if (GUILayout.Button("Reset")) ResetSettings();
                GUILayout.Label("Parameters apply to this camera.\nReset restores the demo defaults.");
                GUILayout.EndScrollView(); GUILayout.EndArea();
            }
            GUI.matrix = previous;
        }
        public void ResetSettings()
        {
            display = SdfOutlineFeature.Display.Composite; placement = SdfOutlineFeature.Placement.Outside;
            spikeDashesEnabled=true;spikeCount=48;spikePhase=0;spikeHeight=18;spikeOffset=3;spikeRandomness=0.65f;spikeSeed=17;
            dashesEnabled=true;dashCount=20;dashDuty=0.65f;dashPhase=0;projectionAngle=0;
            linesEnabled = true; lineRegion = LineRegion.Outside; lineSpacing = 9; lineWidth = 1; lineRange = 64; lineOpacity = 0.7f;
            paperEnabled = true; paperStrength = 0.45f; paperScale = 1; edgeRoughness = 0.8f;
            width = 5; softness = 1; distanceRange = 32; outlineColor = new Color(1, 0.57f, 0.08f, 1);
        }
        static string Description(SdfOutlineFeature.Display mode)
        {
            switch (mode)
            {
                case SdfOutlineFeature.Display.SpikeShape: return "Unmasked spike shape: dash settings have no effect here.";
                case SdfOutlineFeature.Display.SpikeDashes: return "Spiked stroke coverage multiplied by the existing dash mask.";
                case SdfOutlineFeature.Display.ProjectionCoordinate: return "Two-branch projection U: blue=0, red=1.";
                case SdfOutlineFeature.Display.ContourLines: return "SDF level sets following the visible silhouette.";
                case SdfOutlineFeature.Display.PaperGrain: return "Tileable paper fibers before compositing.";
                case SdfOutlineFeature.Display.Mask: return "Visible opaque targets, after depth occlusion.";
                case SdfOutlineFeature.Display.SignedDistance: return "Signed pixel distance from the silhouette.";
                case SdfOutlineFeature.Display.Seeds: return "Nearest boundary coordinate: R=X, G=Y.";
                case SdfOutlineFeature.Display.DistanceBands: return "Distance contours spaced every 4 pixels.";
                case SdfOutlineFeature.Display.Original: return "Camera color before the outline composite.";
                default: return "Camera color with the SDF outline.";
            }
        }
    }
}
