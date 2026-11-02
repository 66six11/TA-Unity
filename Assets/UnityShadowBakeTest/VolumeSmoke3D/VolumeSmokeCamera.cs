using UnityEngine;

namespace TA.VolumeSmoke3D
{
    [RequireComponent(typeof(Camera)),DisallowMultipleComponent]
    public sealed class VolumeSmokeCamera : MonoBehaviour
    {
        public enum SourceDisplay { Smoke, Mask, Depth, Normals, Motion }
        public enum VolumeDisplay { Composite, DensityIntegral, ViewTransmittance, Scattering, DensitySlice, IncidentLightSlice, LightTransmittanceSlice, ObstacleSlice, SourceSlice, VelocitySlice, DivergenceSlice }
        public enum SliceAxis { X, Y, Z }
        public VolumeSmokeSimulation simulation;
        [Range(64,1024)]public int captureHeight=256;
        public SourceDisplay sourceDisplay;
        [HideInInspector]public float validationCaptureDeltaTime;
        public Color smokeColor=new Color(0.35f,0.65f,0.78f);
        [Range(0.1f,8)]public float extinction=1.8f;
        [Range(32,192)]public int raySteps=128;
        [Header("Scene volume lighting")]
        public Color ambientLight=new Color(0.08f,0.1f,0.13f);
        [Range(0,4)]public float scatteringAlbedo=0.9f;
        [Range(-0.8f,0.8f)]public float anisotropy=0.2f;
        [Range(4,32)]public int lightSteps=12;
        [Range(1,8)]public int maxVolumeLights=8;
        [Header("Intermediate views (source view takes priority)")]
        public VolumeDisplay volumeDisplay;
        public SliceAxis sliceAxis=SliceAxis.Z;
        [Range(0,1)]public float slicePosition=0.5f;
        [Range(0.1f,20)]public float debugGain=2;
        [System.NonSerialized]public int activeVolumeLights;
        public bool showControls=true;
        static readonly string[] Displays={"Smoke","Mask","Depth","Normals","Motion"};
        static readonly string[] VolumeDisplays={"Composite","Density ray","View T","Scattering","Density slice","Light slice","Light T slice","Obstacle","Source band","Velocity","Divergence"};
        void OnGUI()
        {
            if(!showControls||!simulation)return;
            GUILayout.BeginArea(new Rect(18,18,340,610),GUI.skin.box);
            GUILayout.Label("SCREEN SOURCE -> 3D VOLUME SMOKE");
            GUILayout.Label($"Grid {simulation.AllocatedResolution} cubed  /  Steps {simulation.StepCount}");
            GUILayout.Label($"Source captures: {simulation.SourceCaptureCount}");
            var selectedSource=(SourceDisplay)GUILayout.SelectionGrid((int)sourceDisplay,Displays,3);
            if(selectedSource!=sourceDisplay)volumeDisplay=VolumeDisplay.Composite;
            sourceDisplay=selectedSource;
            GUILayout.Label($"Scene lights: {activeVolumeLights} / {maxVolumeLights}");
            volumeDisplay=(VolumeDisplay)GUILayout.SelectionGrid((int)volumeDisplay,VolumeDisplays,3);
            if(volumeDisplay!=VolumeDisplay.Composite)sourceDisplay=SourceDisplay.Smoke;
            GUILayout.Label($"Slice {(SliceAxis)sliceAxis}  {slicePosition:F2} / gain {debugGain:F1}");
            sliceAxis=(SliceAxis)GUILayout.SelectionGrid((int)sliceAxis,new[]{"X","Y","Z"},3);
            slicePosition=GUILayout.HorizontalSlider(slicePosition,0,1);
            debugGain=GUILayout.HorizontalSlider(debugGain,0.1f,20);
            GUILayout.Label("Orbit keeps the same smoke volume.");
            GUILayout.Label($"Vorticity  {simulation.vorticity:F1}");
            simulation.vorticity=GUILayout.HorizontalSlider(simulation.vorticity,0,8);
            GUILayout.Label($"Updraft  {simulation.buoyancy:F1}");
            simulation.buoyancy=GUILayout.HorizontalSlider(simulation.buoyancy,0,3);
            GUILayout.Label($"Opacity  {extinction:F1}");
            extinction=GUILayout.HorizontalSlider(extinction,0.1f,5);
            if(GUILayout.Button(simulation.paused?"Resume":"Pause"))simulation.paused=!simulation.paused;
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("Orbit -30"))Orbit(-30);
            if(GUILayout.Button("Orbit +30"))Orbit(30);
            GUILayout.EndHorizontal();
            if(GUILayout.Button("Reset smoke"))simulation.ResetSimulation();
            if(simulation.source&&simulation.source.TryGetComponent<VolumeSmokeSourceMotion>(out var motion))
                if(GUILayout.Button(motion.animate?"Stop source movement":"Animate moving source"))motion.animate=!motion.animate;
            GUILayout.EndArea();
        }
        void Orbit(float angle)
        {
            Vector3 center=simulation.IsInitialized?simulation.DomainBounds.center:simulation.transform.position;
            transform.RotateAround(center,Vector3.up,angle);
        }
    }
}
