using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace TA.PixelRender.Editor
{
    [InitializeOnLoad]
    public static class PixelSceneBuilder
    {
        const string Root = "Assets/PixelRender";
        const string ScenePath = Root + "/PixelRenderDemo.unity";
        static PixelSceneBuilder() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists("Library/PixelRender.build-request")) return;
            File.Delete("Library/PixelRender.build-request");
            try { Build(); }
            catch (Exception e) { File.WriteAllText("Library/PixelRender-validation.txt", e.ToString()); Debug.LogException(e); }
        }
        [MenuItem("Tools/TA/Pixel Render/Open Demo")]
        public static void Open()
        {
            if (!File.Exists(ScenePath)) Build();
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }
        [MenuItem("Tools/TA/Pixel Render/Build Demo")]
        public static void Build()
        {
            Directory.CreateDirectory(Root + "/Materials");
            Directory.CreateDirectory(Root + "/Preview");
            AssetDatabase.Refresh();
            Shader shader = Shader.Find("TA/PixelRender/Toon");
            if (!shader || ShaderUtil.ShaderHasError(shader)) throw new Exception("Pixel toon shader missing or invalid");
            Scene original = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            var enabled = Array.ConvertAll(lights, l => l.enabled);
            for (int i=0; i<lights.Length; i++) lights[i].enabled = false;
            try
            {
                SceneManager.SetActiveScene(scene);
                RenderSettings.skybox = null;
                RenderSettings.fog = false;
                Material grass=Mat(shader,"Grass","7da15a"), earth=Mat(shader,"Earth","6c5145"), wall=Mat(shader,"Plaster","ecd4a2"), roof=Mat(shader,"Roof","c06851"), wood=Mat(shader,"Wood","765443"), leaves=Mat(shader,"Leaves","537a53"), stone=Mat(shader,"Stone","a3adac"), glass=Mat(shader,"Window","80c6d3");
                Shape("Floating earth",PrimitiveType.Cube,new Vector3(0,-0.5f,0),new Vector3(10,1,8),earth);
                Shape("Grass",PrimitiveType.Cube,new Vector3(0,0.03f,0),new Vector3(10,0.12f,8),grass);
                Shape("Cottage",PrimitiveType.Cube,new Vector3(-0.8f,1,0.3f),new Vector3(2.8f,1.9f,2.5f),wall);
                // Two pitched roof slabs; ridge runs along Z.
                for(int side=-1;side<=1;side+=2)
                {
                    var r=Shape("Pitched roof",PrimitiveType.Cube,new Vector3(-0.8f+side*0.78f,2.3f,0.3f),new Vector3(1.95f,0.18f,2.95f),roof);
                    r.transform.rotation=Quaternion.Euler(0,0,-side*35);
                }
                Shape("Chimney",PrimitiveType.Cube,new Vector3(-1.65f,2.7f,0.85f),new Vector3(0.4f,1.1f,0.4f),earth);
                Shape("Door",PrimitiveType.Cube,new Vector3(-0.8f,0.68f,-0.97f),new Vector3(0.65f,1.3f,0.09f),wood);
                Shape("Door handle",PrimitiveType.Sphere,new Vector3(-0.58f,0.7f,-1.04f),Vector3.one*0.09f,wall);
                foreach(float x in new[]{-1.75f,0.15f})
                {
                    Shape("Window frame",PrimitiveType.Cube,new Vector3(x,1.25f,-0.98f),new Vector3(0.6f,0.65f,0.1f),wood);
                    Shape("Blue window",PrimitiveType.Cube,new Vector3(x,1.25f,-1.04f),new Vector3(0.43f,0.48f,0.03f),glass);
                }
                for(int i=0;i<5;i++) Shape("Path stone",PrimitiveType.Cube,new Vector3(-0.8f+Mathf.Sin(i)*0.15f,0.13f,-1.5f-i*0.49f),new Vector3(0.65f,0.12f,0.35f),stone);
                Vector3[] trees={new Vector3(2.8f,0,1.6f),new Vector3(-3.5f,0,1.8f),new Vector3(3.6f,0,-0.7f)};
                foreach(var p in trees)
                {
                    Shape("Tree trunk",PrimitiveType.Cylinder,p+Vector3.up*0.8f,new Vector3(0.23f,0.8f,0.23f),wood);
                    for(int i=0;i<3;i++) Shape("Tree canopy",PrimitiveType.Sphere,p+new Vector3(i==1?0.35f:-0.2f,1.7f+i*0.4f,0),Vector3.one*(1.65f-i*0.25f),leaves);
                }
                for(int i=0;i<6;i++)
                {
                    var rock=Shape("Rock",PrimitiveType.Sphere,new Vector3(1.7f+i*0.35f,0.2f,-2.1f+Mathf.Sin(i)*0.3f),new Vector3(0.5f,0.4f,0.4f),stone);
                    rock.transform.rotation=Quaternion.Euler(i*27,i*43,15);
                }
                for(int i=0;i<5;i++)
                {
                    float x=-3.6f+i*0.55f;
                    Shape("Fence post",PrimitiveType.Cube,new Vector3(x,0.5f,-2.3f),new Vector3(0.12f,0.9f,0.12f),wood);
                }
                Shape("Fence rail",PrimitiveType.Cube,new Vector3(-2.5f,0.65f,-2.3f),new Vector3(2.4f,0.12f,0.1f),wood);
                var sun=new GameObject("Sun").AddComponent<Light>();
                sun.type=LightType.Directional; sun.intensity=1.3f; sun.color=new Color(1,0.9f,0.75f); sun.shadows=LightShadows.Hard;
                sun.transform.rotation=Quaternion.Euler(45,-35,0); RenderSettings.sun=sun;
                var camera=new GameObject("Pixel Camera").AddComponent<Camera>();
                camera.tag="MainCamera"; camera.transform.position=new Vector3(10,9,-13);
                camera.transform.LookAt(new Vector3(0,0.7f,0)); camera.orthographic=true; camera.orthographicSize=6.1f;
                camera.nearClipPlane=0.1f; camera.farClipPlane=60; camera.clearFlags=CameraClearFlags.SolidColor;
                camera.backgroundColor=new Color(0.16f,0.22f,0.30f); camera.cullingMask=1<<29; camera.allowHDR=false; camera.allowMSAA=false;
                var data=camera.GetUniversalAdditionalCameraData(); data.renderPostProcessing=false; data.antialiasing=AntialiasingMode.None;
                EditorSceneManager.SaveScene(scene,ScenePath);
                Capture(camera);
                camera.gameObject.AddComponent<PixelCamera>();
                EditorSceneManager.SaveScene(scene,ScenePath);
                AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
                File.WriteAllText("Library/PixelRender-validation.txt","PASS: Scene saved; URP 320x180 preview rendered; toon shader errors: " + ShaderUtil.ShaderHasError(shader));
            }
            finally
            {
                for(int i=0;i<lights.Length;i++) if(lights[i]) lights[i].enabled=enabled[i];
                if(original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
                EditorSceneManager.CloseScene(scene,true);
            }
        }
        static void Capture(Camera camera)
        {
            var rt=RenderTexture.GetTemporary(320,180,24,RenderTextureFormat.ARGB32);
            var previous=RenderTexture.active;
            var texture=new Texture2D(320,180,TextureFormat.RGB24,false);
            try
            {
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest { destination=rt });
                RenderTexture.active=rt; texture.ReadPixels(new Rect(0,0,320,180),0,0); texture.Apply();
                File.WriteAllBytes(Root+"/Preview/PixelRender.png",texture.EncodeToPNG());
            }
            finally { RenderTexture.active=previous; RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.DestroyImmediate(texture); }
        }
        static Material Mat(Shader shader,string name,string hex)
        {
            string path=Root+"/Materials/"+name+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!m) { m=new Material(shader); AssetDatabase.CreateAsset(m,path); }
            ColorUtility.TryParseHtmlString("#"+hex,out var c); m.SetColor("_BaseColor",c); EditorUtility.SetDirty(m); return m;
        }
        static GameObject Shape(string name,PrimitiveType type,Vector3 position,Vector3 scale,Material material)
        {
            var go=GameObject.CreatePrimitive(type); go.name=name; go.layer=29;
            go.transform.position=position; go.transform.localScale=scale;
            go.GetComponent<Renderer>().sharedMaterial=material;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>()); return go;
        }
    }
}

