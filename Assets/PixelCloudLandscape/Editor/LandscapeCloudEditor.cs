using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TA.PixelCloudLandscape
{
    [CustomEditor(typeof(LandscapeClouds))]
    public sealed class LandscapeCloudEditor : Editor
    {
        public const string Folder="Assets/PixelCloudLandscape/";
        static readonly Vector3 focus = new(1860,950,-190);
        static readonly Quaternion angle = Quaternion.Euler(45,24,0);
        static readonly string[] Weather = { "晴天", "多云", "阴天", "雨层云", "雷暴云", "雪天层云" };
        // Coverage, layered shape, darkening, base altitude, depth.
        static readonly float[,] Profiles = {
            {0.44f,0.05f,0,940,530}, {0.53f,0.15f,0.04f,680,780},
            {0.82f,0.82f,0.29f,540,800}, {0.90f,0.85f,0.63f,350,1040},
            {0.83f,0.18f,0.91f,280,1840}, {0.87f,0.94f,0.22f,430,630}
        };

        [MenuItem("Tools/Photon Clouds/Open Pixel Landscape")]
        static void Open()
        {
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(Folder+"Pixel Cloud Landscape.unity");
            var clouds=Object.FindAnyObjectByType<LandscapeClouds>();
            if(!clouds) return;
            Selection.activeGameObject=clouds.gameObject;
            AlignSceneView(clouds.transform);
        }

        public static void PlaceCamera(Transform camera,float distance)
        {
            camera.SetPositionAndRotation(focus-angle*Vector3.forward*distance,angle);
        }

        static void AlignSceneView(Transform camera)
        {
            var view=SceneView.lastActiveSceneView;
            if(!view) return;
            view.in2DMode=false;
            view.cameraSettings.dynamicClip=false;
            view.cameraSettings.nearClip=0.5f;
            view.cameraSettings.farClip=80000;
            view.sceneLighting=true;
            view.cameraSettings.fieldOfView=camera.GetComponent<Camera>().fieldOfView;
            view.sceneViewState.alwaysRefresh=true;
            view.LookAt(camera.position+camera.forward*20,camera.rotation,10,false,true);
            view.pivot=camera.position+camera.forward*view.cameraDistance;
            view.camera.transform.SetPositionAndRotation(camera.position,camera.rotation);
            view.camera.nearClipPlane=view.cameraSettings.nearClip;
            view.camera.farClipPlane=view.cameraSettings.farClip;
            view.Repaint();
        }

        public override void OnInspectorGUI()
        {
            var clouds=(LandscapeClouds)target;
            EditorGUILayout.LabelField("观察同一片云",EditorStyles.boldLabel);
            int shot=GUILayout.SelectionGrid(-1,new[]{"高空总览","中景","近看云缘"},3);
            float current=Vector3.Distance(clouds.transform.position,focus);
            EditorGUI.BeginChangeCheck();
            float log=EditorGUILayout.Slider("推近 / 拉远",Mathf.Log10(Mathf.Max(100,current)),2,4.2f);
            if(EditorGUI.EndChangeCheck() || shot>=0)
            {
                float distance=shot switch {0=>9000,1=>3100,2=>820,_=>Mathf.Pow(10,log)};
                Undo.RecordObject(clouds.transform,"Move cloud camera");
                PlaceCamera(clouds.transform,distance);
                AlignSceneView(clouds.transform);
                EditorSceneManager.MarkSceneDirty(clouds.gameObject.scene);
            }
            EditorGUILayout.LabelField("云况",EditorStyles.boldLabel);
            int w=GUILayout.SelectionGrid(-1,Weather,3);
            if(w>=0)
            {
                Undo.RecordObject(clouds,"Change cloud profile");
                clouds.cover=Profiles[w,0]; clouds.layered=Profiles[w,1]; clouds.storm=Profiles[w,2];
                clouds.cloudBase=Profiles[w,3]; clouds.thickness=Profiles[w,4];
                EditorUtility.SetDirty(clouds);
                EditorSceneManager.MarkSceneDirty(clouds.gameObject.scene);
                SceneView.RepaintAll();
            }
            serializedObject.Update();
            var enabledBands=serializedObject.FindProperty("useThicknessBands");
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(enabledBands,new GUIContent("明暗与透明度分阶"));
            if(enabledBands.boolValue)
            {
                var preview=serializedObject.FindProperty("thicknessPreview");
                preview.enumValueIndex=EditorGUILayout.Popup("预览",preview.enumValueIndex,
                    new[]{"最终合成","受光明暗","视线厚度"});
                EditorGUILayout.PropertyField(serializedObject.FindProperty("lightSamples"),new GUIContent("光照遮挡采样数"));
                var bezier = serializedObject.FindProperty("useBezierLighting");
                EditorGUILayout.PropertyField(bezier, new GUIContent("贝塞尔光照映射"));
                if (bezier.boolValue)
                {
                    var steps = serializedObject.FindProperty("colorSteps");
                    EditorGUILayout.PropertyField(steps, new GUIContent("云色阶数"));
                    curvePlot.Draw(serializedObject.FindProperty("darkControl"),
                        serializedObject.FindProperty("lightControl"), steps.intValue);
                }
                else
                    DrawBands(serializedObject.FindProperty("brightnessBands"),true);
                DrawBands(serializedObject.FindProperty("viewBands"),false);
            }
            EditorGUILayout.Space();
            DrawPropertiesExcluding(serializedObject,"m_Script","useThicknessBands","thicknessPreview","lightSamples","brightnessBands","viewBands",
                "useBezierLighting","colorSteps","darkControl","lightControl");
            if(serializedObject.ApplyModifiedProperties())
            {
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();
            }
        }

        readonly CloudBezierPlot curvePlot = new();
        readonly CloudBandPlot lightPlot=new(true);
        readonly CloudBandPlot opacityPlot=new(false);

        void DrawBands(SerializedProperty bands,bool light)
        {
            EditorGUILayout.LabelField(light ? "受光亮度 → 云色" : "视线厚度 → 不透明度",EditorStyles.boldLabel);
            (light ? lightPlot : opacityPlot).Draw(bands);
            EditorGUILayout.LabelField(light ? "横拖调分界，上下调颜色明度；点击下方色块选色" : "横拖方点调厚度，上下拖动调不透明度",EditorStyles.miniLabel);
            EditorGUILayout.LabelField(light ? "亮度 0–1              颜色" : "起点 τ                 0 透明 · 1 不透明",EditorStyles.miniLabel);
            for(int i=0;i<bands.arraySize;i++)
            {
                var entry=bands.GetArrayElementAtIndex(i);
                var start=entry.FindPropertyRelative("start");
                EditorGUILayout.BeginHorizontal();
                using(new EditorGUI.DisabledScope(i==0))
                {
                    float input=EditorGUILayout.DelayedFloatField(start.floatValue,GUILayout.Width(62));
                    if(i>0 && float.IsFinite(input) && input!=start.floatValue)
                    {
                        float min=bands.GetArrayElementAtIndex(i-1).FindPropertyRelative("start").floatValue;
                        float max=i+1<bands.arraySize ? bands.GetArrayElementAtIndex(i+1).FindPropertyRelative("start").floatValue : float.PositiveInfinity;
                        if(input>min && input<max && (!light || input<=1)) start.floatValue=input;
                    }
                }
                if(light)
                {
                    var color=entry.FindPropertyRelative("color");
                    color.colorValue=EditorGUILayout.ColorField(GUIContent.none,color.colorValue,true,false,false);
                }
                else
                {
                    var alpha=entry.FindPropertyRelative("opacity");
                    alpha.floatValue=EditorGUILayout.Slider(alpha.floatValue,0,1);
                }
                bool insert,remove;
                using(new EditorGUI.DisabledScope(bands.arraySize>=LandscapeClouds.MaxBands || (light && start.floatValue>=1)))
                    insert=GUILayout.Button(new GUIContent("+","在这一阶内添加新阶"),GUILayout.Width(24));
                using(new EditorGUI.DisabledScope(bands.arraySize<=1))
                    remove=GUILayout.Button(new GUIContent("−","删除这一阶"),GUILayout.Width(24));
                EditorGUILayout.EndHorizontal();
                if(insert)
                {
                    float a=start.floatValue;
                    float mid=i+1<bands.arraySize ? Mathf.Lerp(a,bands.GetArrayElementAtIndex(i+1).FindPropertyRelative("start").floatValue,0.5f) : light ? (a+1)*0.5f : a+1;
                    bands.InsertArrayElementAtIndex(i);
                    bands.GetArrayElementAtIndex(i+1).FindPropertyRelative("start").floatValue=mid;
                    break;
                }
                if(remove)
                {
                    bands.DeleteArrayElementAtIndex(i);
                    bands.GetArrayElementAtIndex(0).FindPropertyRelative("start").floatValue=0;
                    break;
                }
            }
        }
    }
}






