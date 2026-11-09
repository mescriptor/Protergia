using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace ReferenceHouseTools
{
    [Serializable] public class HouseManifest { public int version; public HouseMesh[] meshes; public HouseMaterial[] materials; public HouseCamera camera; public HouseLight[] lights; }
    [Serializable] public class HouseMesh { public string name, file, collection; public float[] position; public string[] materials; }
    [Serializable] public class HouseMaterial { public string name, kind, baseColorTexture, normalTexture; public float[] baseColor, emission, textureScale; public float metallic, roughness, normalStrength=.35f, alpha = 1; }
    [Serializable] public class HouseCamera { public float[] position, target; public float lens=65, sensorWidth=36, shiftY=-.075f; }
    [Serializable] public class HouseLight { public string name, type; public float[] position, color, direction; public float intensity, power, range, spotAngle; }

    public static class ReferenceHouseImporter
    {
        const string Root="Assets/ReferenceHouse";
        const string Export="C:/Protergia/Blender/ReferenceHouse/UnityExport";
        const string ScenePath=Root+"/Scenes/ReferenceHouse.unity";
        static readonly string Output="C:/Protergia/Blender/ReferenceHouse";
        static Camera captureCamera;
        static bool temporaryCapture;
        static int waitFrames;
        static string capturePath;
        static HouseManifest manifest;
        static readonly Dictionary<string,Material> materials=new Dictionary<string,Material>();
        static readonly Dictionary<string,Texture2D> textureCache=new Dictionary<string,Texture2D>();

        public static void CompileCheck()
        {
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output,"Unity_CompileCheck.txt"),Application.unityVersion+"\nURP="+typeof(UniversalRenderPipelineAsset).Assembly.GetName().Version+"\nGraphics="+SystemInfo.graphicsDeviceName);
            Debug.Log("REFERENCE_HOUSE_EDITOR_READY");
        }

        [MenuItem("Reference House/Import detailed model and lighting")]
        public static void Build()
        {
            try
            {
                if(!Application.isBatchMode && Enumerable.Range(0,SceneManager.sceneCount).Any(i=>SceneManager.GetSceneAt(i).isDirty))
                    throw new InvalidOperationException("Save your open scene before importing the reference house. No scene was changed.");
                string editedExport=Path.Combine(Output,"UnityFullV8");
                if(!File.Exists(Path.Combine(editedExport,"scene.json"))) editedExport=Path.Combine(Output,"UnityFullV7");
                string modelDirectory=File.Exists(Path.Combine(editedExport,"scene.json")) ? editedExport : Export;
                manifest=JsonUtility.FromJson<HouseManifest>(File.ReadAllText(Path.Combine(modelDirectory,"scene.json")));
                if(manifest.version!=1 || manifest.meshes==null || manifest.meshes.Length==0) throw new Exception("Missing or unsupported house export");
                foreach(var d in new[]{Root,Root+"/Meshes",Root+"/Materials",Root+"/Textures",Root+"/Settings",Root+"/Scenes",Root+"/Prefabs"}) Directory.CreateDirectory(d);
                AssetDatabase.Refresh();
                ConfigurePipeline();
                materials.Clear();textureCache.Clear();
                foreach(var m in manifest.materials) materials[m.name]=MakeMaterial(m);
                var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var renderSettings=new GameObject("Rendering | house URP profile").AddComponent<ReferenceHouseRenderSettings>();
                renderSettings.pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(Root+"/Settings/ReferenceHouse_URP.asset");
                var house=new GameObject("Reference House | detailed model");
                var groups=new Dictionary<string,Transform>();
                int triangleCount=0;
                for(int i=0;i<manifest.meshes.Length;i++)
                {
                    var entry=manifest.meshes[i];
                    if(!groups.TryGetValue(entry.collection,out Transform group))
                    {
                        group=new GameObject(entry.collection).transform;group.parent=house.transform;groups.Add(entry.collection,group);
                    }
                    string assetPath=Root+"/Meshes/"+i.ToString("D5")+"_"+Safe(entry.name)+".asset";
                    var mesh=ReadMesh(Path.Combine(modelDirectory,entry.file),entry.name);
                    triangleCount+=mesh.triangles.Length/3;
                    mesh=SaveOrUpdate(mesh,assetPath);
                    var go=new GameObject(entry.name);go.transform.parent=group;go.transform.localPosition=V(entry.position);
                    go.AddComponent<MeshFilter>().sharedMesh=mesh;
                    var renderer=go.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials=entry.materials.Select(n=>materials.ContainsKey(n)?materials[n]:materials.Values.First()).ToArray();
                    renderer.shadowCastingMode=ShadowCastingMode.On;renderer.receiveShadows=true;
                    if(entry.name.Contains("cells tone")||entry.name.Contains("cell busbars")){renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;}
                    renderer.lightProbeUsage=LightProbeUsage.BlendProbes;renderer.reflectionProbeUsage=ReflectionProbeUsage.BlendProbes;
                    GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.BatchingStatic|StaticEditorFlags.ReflectionProbeStatic);
                    if(entry.collection.StartsWith("01_") || entry.collection.StartsWith("02_") || entry.collection.StartsWith("03_") || entry.collection.StartsWith("05_"))
                        go.AddComponent<MeshCollider>().sharedMesh=mesh;
                }
                PrefabUtility.SaveAsPrefabAssetAndConnect(house,Root+"/Prefabs/ReferenceHouse.prefab",InteractionMode.AutomatedAction);
                CreateEnvironment();
                captureCamera=CreateCamera(manifest.camera);
                CreateLighting(manifest.lights);
                CreateVolume();
                scene.name="ReferenceHouse";
                EditorSceneManager.SaveScene(scene,ScenePath);
                AssetDatabase.SaveAssets();
                var report=new ImportReport{unityVersion=Application.unityVersion,meshCount=manifest.meshes.Length,materialCount=materials.Count,triangleCount=triangleCount,scene=ScenePath,prefab=Root+"/Prefabs/ReferenceHouse.prefab",camera=captureCamera.name,lights=UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Length,missingMaterials=UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Sum(r=>r.sharedMaterials.Count(m=>m==null)),errors=new string[0]};
                File.WriteAllText(Path.Combine(Output,"Unity_ImportReport.json"),JsonUtility.ToJson(report,true));
                Debug.Log("REFERENCE_HOUSE_IMPORT_COMPLETE "+JsonUtility.ToJson(report));
                ScheduleCapture(Path.Combine(Output,"Preview_Unity.png"));
            }
            catch(Exception e){Failure(e);}
        }

        static void ConfigurePipeline()
        {
            string rendererPath=Root+"/Settings/ReferenceHouse_Renderer.asset";
            string pipelinePath=Root+"/Settings/ReferenceHouse_URP.asset";
            if(!File.Exists(rendererPath))AssetDatabase.CopyAsset("Assets/Settings/PC_Renderer.asset",rendererPath);
            if(!File.Exists(pipelinePath))AssetDatabase.CopyAsset("Assets/Settings/PC_RPAsset.asset",pipelinePath);
            var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            var rp=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            var so=new SerializedObject(rp);
            so.FindProperty("m_RendererDataList").GetArrayElementAtIndex(0).objectReferenceValue=renderer;
            so.FindProperty("m_MainLightShadowmapResolution").intValue=4096;
            so.FindProperty("m_AdditionalLightsShadowmapResolution").intValue=4096;
            so.FindProperty("m_AdditionalLightsPerObjectLimit").intValue=8;
            so.FindProperty("m_ShadowDistance").floatValue=65;
            so.FindProperty("m_MSAA").intValue=4;
            so.FindProperty("m_RenderScale").floatValue=1;
            so.ApplyModifiedPropertiesWithoutUndo();
            GraphicsSettings.defaultRenderPipeline=rp;QualitySettings.SetQualityLevel(1,false);QualitySettings.renderPipeline=rp;
            QualitySettings.shadowDistance=65;QualitySettings.shadows=UnityEngine.ShadowQuality.All;QualitySettings.shadowResolution=UnityEngine.ShadowResolution.VeryHigh;
            QualitySettings.lodBias=2;QualitySettings.vSyncCount=0;
            EditorUtility.SetDirty(rp);
        }

        static Material MakeMaterial(HouseMaterial m)
        {
            var mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));mat.name=m.name;
            var baseCol=C(m.baseColor,Color.gray);baseCol.a=m.alpha>0?m.alpha:baseCol.a;
            // Blender's shader colors are linear; Unity serializes material Color properties in sRGB.
            Color srgb=baseCol.gamma;srgb.a=baseCol.a;mat.SetColor("_BaseColor",srgb);
            mat.SetFloat("_Metallic",Mathf.Clamp01(m.metallic));mat.SetFloat("_Smoothness",1-Mathf.Clamp01(m.roughness));
            mat.SetFloat("_SpecularHighlights",1);mat.SetFloat("_EnvironmentReflections",1);
            string kind=(m.kind??"").ToLowerInvariant();
            if(kind=="foliage" || m.name.StartsWith("Foliage /",StringComparison.OrdinalIgnoreCase))mat.SetFloat("_Cull",(float)CullMode.Off);
            if(kind=="fabric"){mat.SetFloat("_Smoothness",.06f);mat.SetFloat("_SpecularHighlights",0);mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");}
            if(kind=="solar"){mat.SetFloat("_Smoothness",.28f);mat.SetFloat("_SpecularHighlights",0);mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");}
            if(kind=="solar"||m.name.Contains("PV fine")){mat.SetFloat("_ReceiveShadows",0);mat.EnableKeyword("_RECEIVE_SHADOWS_OFF");}
            if(m.name.Contains("PV fine")){mat.SetFloat("_Metallic",0);mat.SetFloat("_Smoothness",.08f);mat.SetFloat("_SpecularHighlights",0);mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");mat.SetColor("_BaseColor",new Color(.32f,.39f,.47f,1));}
            if(kind=="glass" || m.name.IndexOf("glazing",StringComparison.OrdinalIgnoreCase)>=0 && m.roughness<.2f)
            {
                mat.SetFloat("_Surface",1);mat.SetFloat("_Blend",0);mat.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);mat.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite",0);mat.SetFloat("_Cull",(float)CullMode.Off);mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");mat.renderQueue=(int)RenderQueue.Transparent;
                srgb.a=.24f;mat.SetColor("_BaseColor",srgb);mat.SetFloat("_Smoothness",.94f);
            }
            if(m.emission!=null && m.emission.Length>=3 && m.emission.Max()>.001f)
            {
                mat.shader=Shader.Find("Universal Render Pipeline/Unlit");mat.shaderKeywords=Array.Empty<string>();
                mat.SetColor("_BaseColor",m.name.Contains("indicator")?new Color(.12f,1.65f,.32f,1):new Color(1.8f,1.3f,.65f,1));
                mat.SetFloat("_Surface",0);mat.SetFloat("_ZWrite",1);mat.renderQueue=(int)RenderQueue.Geometry;
            }
            if(!string.IsNullOrEmpty(m.baseColorTexture))
            {
                var tex=ImportTexture(m.baseColorTexture,false);mat.SetTexture("_BaseMap",tex);
                if(m.textureScale!=null && m.textureScale.Length>=2)mat.SetTextureScale("_BaseMap",new Vector2(m.textureScale[0],m.textureScale[1]));
            }
            if(!string.IsNullOrEmpty(m.normalTexture))
            {
                var tex=ImportTexture(m.normalTexture,true);mat.SetTexture("_BumpMap",tex);mat.SetFloat("_BumpScale",m.normalStrength);mat.EnableKeyword("_NORMALMAP");
            }
            mat.enableInstancing=true;
            return SaveOrUpdate(mat,Root+"/Materials/"+Safe(m.name)+".mat");
        }

        static Texture2D ImportTexture(string path,bool normal)
        {
            if(textureCache.TryGetValue(path,out var cached))return cached;
            string target=Root+"/Textures/"+Path.GetFileName(path);File.Copy(Path.Combine(Export,path),target,true);AssetDatabase.ImportAsset(target);
            var importer=(TextureImporter)AssetImporter.GetAtPath(target);importer.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
            importer.sRGBTexture=!normal;importer.maxTextureSize=2048;importer.anisoLevel=4;importer.wrapMode=TextureWrapMode.Repeat;importer.SaveAndReimport();
            var result=AssetDatabase.LoadAssetAtPath<Texture2D>(target);textureCache[path]=result;return result;
        }

        internal static Mesh ReadMesh(string path,string name)
        {
            using(var br=new BinaryReader(File.OpenRead(path)))
            {
                if(br.ReadInt32()!=1)throw new Exception("Invalid mesh version: "+path);
                int vc=br.ReadInt32(),sc=br.ReadInt32();
                if(vc<=0 || vc>10000000 || sc<1 || sc>200)throw new Exception("Invalid mesh counts: "+path);
                var vertices=new Vector3[vc];var normals=new Vector3[vc];var uv=new Vector2[vc];
                for(int i=0;i<vc;i++){vertices[i]=new Vector3(br.ReadSingle(),br.ReadSingle(),br.ReadSingle());normals[i]=new Vector3(br.ReadSingle(),br.ReadSingle(),br.ReadSingle());uv[i]=new Vector2(br.ReadSingle(),br.ReadSingle());}
                var mesh=new Mesh{name=name,indexFormat=vc>65535?IndexFormat.UInt32:IndexFormat.UInt16};mesh.vertices=vertices;mesh.normals=normals;mesh.uv=uv;mesh.subMeshCount=sc;
                for(int s=0;s<sc;s++){int count=br.ReadInt32();var indices=new int[count];for(int j=0;j<count;j++)indices[j]=br.ReadInt32();mesh.SetTriangles(indices,s,false);}
                if(br.BaseStream.Position!=br.BaseStream.Length)throw new Exception("Unexpected bytes in mesh: "+path);
                mesh.RecalculateBounds();mesh.RecalculateTangents();return mesh;
            }
        }

        static Camera CreateCamera(HouseCamera info)
        {
            var go=new GameObject("Camera | reference composition");go.tag="MainCamera";
            var cam=go.AddComponent<Camera>();cam.transform.position=V(info.position);cam.transform.LookAt(V(info.target));
            cam.usePhysicalProperties=true;cam.sensorSize=new Vector2(info.sensorWidth,info.sensorWidth*.75f);cam.focalLength=info.lens;cam.lensShift=new Vector2(0,info.shiftY/.75f);
            cam.gateFit=Camera.GateFitMode.Horizontal;cam.nearClipPlane=1;cam.farClipPlane=120;cam.allowHDR=true;cam.allowMSAA=true;
            cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.65f,.69f,.71f);cam.depth=-1;
            var data=cam.GetUniversalAdditionalCameraData();data.renderPostProcessing=true;data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;data.antialiasingQuality=AntialiasingQuality.High;
            go.AddComponent<AudioListener>();return cam;
        }

        static void CreateEnvironment()
        {
            var sky=new Material(Shader.Find("Skybox/Procedural")){name="Clear Athens daylight"};sky.SetFloat("_SunSize",.025f);sky.SetFloat("_AtmosphereThickness",.8f);sky.SetColor("_SkyTint",new Color(.55f,.65f,.78f));sky.SetColor("_GroundColor",new Color(.39f,.38f,.32f));sky.SetFloat("_Exposure",1.0f);
            sky=SaveOrUpdate(sky,Root+"/Materials/EnvironmentSky.mat");RenderSettings.skybox=sky;
            RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(.57f,.65f,.76f);RenderSettings.ambientEquatorColor=new Color(.54f,.53f,.48f);RenderSettings.ambientGroundColor=new Color(.27f,.25f,.22f);RenderSettings.ambientIntensity=.85f;RenderSettings.reflectionIntensity=.7f;
            RenderSettings.fog=false;
            var ground=new GameObject("Presentation | curved neutral backdrop");
            var vertices=new List<Vector3>{new Vector3(-150,-2.14f,-100),new Vector3(150,-2.14f,-100)};
            for(int i=0;i<=32;i++){float a=i*Mathf.PI/64;float y=-2.14f+20*(1-Mathf.Cos(a));float z=18+20*Mathf.Sin(a);vertices.Add(new Vector3(-150,y,z));vertices.Add(new Vector3(150,y,z));}
            vertices.Add(new Vector3(-150,80,38));vertices.Add(new Vector3(150,80,38));
            var triangles=new List<int>();for(int i=0;i<vertices.Count/2-1;i++){int k=i*2;triangles.AddRange(new[]{k,k+2,k+1,k+1,k+2,k+3});}
            var backdrop=new Mesh{name="Smooth cyclorama"};backdrop.SetVertices(vertices);backdrop.SetTriangles(triangles,0);backdrop.RecalculateNormals();backdrop.RecalculateBounds();backdrop=SaveOrUpdate(backdrop,Root+"/Meshes/PresentationBackdrop.asset");ground.AddComponent<MeshFilter>().sharedMesh=backdrop;
            var mat=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="Presentation ground"};mat.SetColor("_BaseColor",new Color(.69f,.71f,.70f));mat.SetFloat("_Smoothness",0);mat.SetFloat("_SpecularHighlights",0);mat.SetFloat("_EnvironmentReflections",0);mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");mat=SaveOrUpdate(mat,Root+"/Materials/PresentationGround.mat");ground.AddComponent<MeshRenderer>().sharedMaterial=mat;
        }

        static void CreateLighting(HouseLight[] exported)
        {
            var rig=new GameObject("Lighting | daylight and warm practicals").transform;
            var sun=MakeLight("Sun | warm afternoon",LightType.Directional,new Vector3(-8,15,-6),new Color(1,.94f,.84f),1.65f,0,rig);
            sun.transform.rotation=Quaternion.Euler(52,145,0);sun.shadows=LightShadows.Soft;sun.shadowBias=.035f;sun.shadowNormalBias=.25f;RenderSettings.sun=sun;
            var broadFill=MakeLight("Open sky | soft frontal fill",LightType.Directional,new Vector3(0,9,-10),new Color(.84f,.9f,1),.55f,0,rig);broadFill.transform.rotation=Quaternion.Euler(30,0,0);
            MakeLight("Front daylight bounce",LightType.Point,new Vector3(-1,4.5f,-5),new Color(.79f,.87f,1),9,13,rig);
            MakeLight("Living room soft fill",LightType.Point,new Vector3(-3,2.75f,1),new Color(1,.89f,.76f),.85f,4.8f,rig);
            MakeLight("Kitchen soft fill",LightType.Point,new Vector3(2.8f,2.8f,1.5f),new Color(1,.89f,.76f),.9f,4.2f,rig);
            MakeLight("Bedroom soft fill",LightType.Point,new Vector3(-2.6f,6.2f,.4f),new Color(1,.89f,.78f),.9f,4.5f,rig);
            MakeLight("Study soft fill",LightType.Point,new Vector3(3.2f,6.2f,1.0f),new Color(1,.90f,.78f),.75f,3.7f,rig);
            MakeLight("Basement daylight bounce",LightType.Point,new Vector3(0,-.1f,-1),new Color(.92f,.94f,1),3,6.5f,rig);
            if(exported!=null && exported.Length>0)
            {
                foreach(var l in exported)
                {
                    var light=MakeLight(l.name,LightType.Spot,V(l.position),C(l.color,new Color(1,.82f,.62f)),l.intensity>0?l.intensity:l.power>0?Mathf.Clamp(l.power*.10f,1,10):3,l.range>0?l.range:4,rig);
                    light.transform.rotation=Quaternion.LookRotation(l.direction!=null?V(l.direction):Vector3.down);light.spotAngle=l.spotAngle>0?l.spotAngle:82;light.innerSpotAngle=light.spotAngle*.65f;
                    light.shadows=LightShadows.Soft;
                }
            }
            else
            {
                foreach(var p in new[]{new Vector3(-3.2f,3.48f,.3f),new Vector3(-3.2f,3.48f,2.1f),new Vector3(.5f,3.48f,.15f),new Vector3(3,3.48f,.5f),new Vector3(-3,6.68f,.1f),new Vector3(-1.2f,6.68f,1),new Vector3(1,6.68f,.3f),new Vector3(3.25f,6.68f,1.4f)})
                {
                    var spot=MakeLight("Recessed warm downlight",LightType.Spot,p,new Color(1,.82f,.61f),28,4.8f,rig);spot.transform.rotation=Quaternion.Euler(90,0,0);spot.spotAngle=85;spot.innerSpotAngle=55;spot.shadows=LightShadows.Soft;
                }
                foreach(float x in new[]{1.1f,2.2f})
                {
                    var spot=MakeLight("Dining pendant",LightType.Spot,new Vector3(x,2.48f,.65f),new Color(1,.78f,.51f),18,3.2f,rig);spot.transform.rotation=Quaternion.Euler(90,0,0);spot.spotAngle=80;spot.innerSpotAngle=55;
                }
            }
            var probe=new GameObject("Reflection probe | whole house").AddComponent<ReflectionProbe>();probe.transform.position=new Vector3(.5f,3,2.5f);probe.size=new Vector3(17,13,16);probe.boxProjection=true;probe.resolution=128;probe.mode=ReflectionProbeMode.Realtime;probe.refreshMode=ReflectionProbeRefreshMode.OnAwake;probe.timeSlicingMode=ReflectionProbeTimeSlicingMode.NoTimeSlicing;probe.cullingMask=-1;probe.intensity=.65f;probe.clearFlags=ReflectionProbeClearFlags.Skybox;probe.transform.parent=rig;
        }

        static Light MakeLight(string name,LightType type,Vector3 pos,Color color,float power,float range,Transform parent)
        {
            var o=new GameObject(name);o.transform.parent=parent;o.transform.position=pos;var l=o.AddComponent<Light>();l.type=type;l.color=color;l.intensity=power;l.range=range;l.renderMode=LightRenderMode.ForcePixel;l.shadows=LightShadows.None;return l;
        }

        static void CreateVolume()
        {
            var profile=ScriptableObject.CreateInstance<VolumeProfile>();profile.name="Reference House look";
            var tone=profile.Add<Tonemapping>(true);tone.mode.Override(TonemappingMode.ACES);
            var color=profile.Add<ColorAdjustments>(true);color.postExposure.Override(.05f);color.contrast.Override(8);color.saturation.Override(3);
            var balance=profile.Add<WhiteBalance>(true);balance.temperature.Override(5);balance.tint.Override(0);
            var bloom=profile.Add<Bloom>(true);bloom.threshold.Override(1.2f);bloom.intensity.Override(.12f);bloom.scatter.Override(.5f);
            string path=Root+"/Settings/ReferenceHouse_Volume.asset";
            if(File.Exists(path))AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(profile,path);foreach(var c in profile.components)AssetDatabase.AddObjectToAsset(c,profile);
            var vol=new GameObject("Post processing | restrained warm daylight").AddComponent<Volume>();vol.isGlobal=true;vol.sharedProfile=profile;
        }

        [MenuItem("Reference House/Render current scene")]
        public static void CaptureExisting()
        {
            try{EditorSceneManager.OpenScene(ScenePath);ConfigurePipeline();captureCamera=Camera.main;ScheduleCapture(Path.Combine(Output,"Preview_Unity.png"));}
            catch(Exception e){Failure(e);}
        }
        public static void RefineLighting()
        {
            try
            {
                EditorSceneManager.OpenScene(ScenePath);ConfigurePipeline();
                var sun=UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).First(l=>l.name=="Sun | warm afternoon");sun.transform.rotation=Quaternion.Euler(52,145,0);
                var existingFill=GameObject.Find("Open sky | soft frontal fill");
                var fill=existingFill?existingFill.GetComponent<Light>():MakeLight("Open sky | soft frontal fill",LightType.Directional,new Vector3(0,9,-10),new Color(.84f,.9f,1),.55f,0,sun.transform.parent);fill.transform.rotation=Quaternion.Euler(30,0,0);
                captureCamera=Camera.main;captureCamera.lensShift=new Vector2(0,-.1f);captureCamera.nearClipPlane=1;captureCamera.farClipPlane=120;
                foreach(string guid in AssetDatabase.FindAssets("t:Material",new[]{Root+"/Materials"}))
                {
                    var mat=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));string name=mat.name.ToLowerInvariant().Replace('_',' ');
                    if(name.Contains("upholstery")||name.Contains("bedding")||name.Contains("fabric")||name.Contains("linen")||name.Contains("bed cover"))
                    {mat.SetFloat("_Smoothness",.06f);mat.SetFloat("_SpecularHighlights",0);mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");EditorUtility.SetDirty(mat);}
                    if(name.StartsWith("pv blue silicon")||name.StartsWith("pv dark blue"))
                    {mat.SetFloat("_Smoothness",.28f);mat.SetFloat("_SpecularHighlights",0);mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");EditorUtility.SetDirty(mat);}
                    if(name.StartsWith("pv blue silicon")||name.StartsWith("pv dark blue")||name.StartsWith("pv fine"))
                    {mat.SetFloat("_ReceiveShadows",0);mat.EnableKeyword("_RECEIVE_SHADOWS_OFF");EditorUtility.SetDirty(mat);}
                    if(name.StartsWith("pv fine"))
                    {mat.SetFloat("_Metallic",0);mat.SetFloat("_Smoothness",.08f);mat.SetFloat("_SpecularHighlights",0);mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");mat.SetColor("_BaseColor",new Color(.32f,.39f,.47f,1));EditorUtility.SetDirty(mat);}
                    if(name.Contains("warm diffuser")||name.Contains("charger indicator"))
                    {
                        mat.shader=Shader.Find("Universal Render Pipeline/Unlit");mat.shaderKeywords=Array.Empty<string>();
                        mat.SetColor("_BaseColor",name.Contains("indicator")?new Color(.12f,1.65f,.32f,1):new Color(1.8f,1.3f,.65f,1));
                        mat.SetFloat("_Surface",0);mat.SetFloat("_ZWrite",1);mat.renderQueue=(int)RenderQueue.Geometry;EditorUtility.SetDirty(mat);
                    }
                    if(name.Contains("presentationground")||name.Contains("presentation ground"))
                    {mat.SetFloat("_Smoothness",0);mat.SetFloat("_SpecularHighlights",0);mat.SetFloat("_EnvironmentReflections",0);mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");EditorUtility.SetDirty(mat);}
                }
                foreach(var renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                    if(renderer.name.Contains("cells tone")||renderer.name.Contains("cell busbars")){renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;}
                foreach(var root in SceneManager.GetActiveScene().GetRootGameObjects())
                    if(PrefabUtility.IsPartOfPrefabInstance(root))PrefabUtility.ApplyPrefabInstance(root,InteractionMode.AutomatedAction);
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene());AssetDatabase.SaveAssets();ScheduleCapture(Path.Combine(Output,"Preview_Unity.png"));
            }
            catch(Exception e){Failure(e);}
        }
        internal static void CaptureTemporary(Camera camera,string path)
        {
            captureCamera=camera;
            temporaryCapture=true;
            ScheduleCapture(path);
        }
        static void ScheduleCapture(string path)
        {
            capturePath=path;waitFrames=0;EditorApplication.update-=CaptureUpdate;EditorApplication.update+=CaptureUpdate;
            SceneView.RepaintAll();EditorApplication.QueuePlayerLoopUpdate();
        }
        static void CaptureUpdate()
        {
            if(EditorApplication.isCompiling || EditorApplication.isUpdating)return;
            EditorApplication.QueuePlayerLoopUpdate();
            if(++waitFrames<100)return;
            EditorApplication.update-=CaptureUpdate;
            try
            {
                ShaderUtil.allowAsyncCompilation=false;
                var rt=new RenderTexture(3600,2700,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB){antiAliasing=1};rt.Create();
                var req=new UniversalRenderPipeline.SingleCameraRequest{destination=rt};
                if(RenderPipeline.SupportsRenderRequest(captureCamera,req))RenderPipeline.SubmitRenderRequest(captureCamera,req);else{captureCamera.targetTexture=rt;captureCamera.Render();captureCamera.targetTexture=null;}
                var down=RenderTexture.GetTemporary(1800,1350,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);rt.filterMode=FilterMode.Bilinear;Graphics.Blit(rt,down);
                var prev=RenderTexture.active;RenderTexture.active=down;var png=new Texture2D(down.width,down.height,TextureFormat.RGB24,false,false);png.ReadPixels(new Rect(0,0,down.width,down.height),0,0);png.Apply();File.WriteAllBytes(capturePath,png.EncodeToPNG());RenderTexture.active=prev;UnityEngine.Object.DestroyImmediate(png);RenderTexture.ReleaseTemporary(down);rt.Release();UnityEngine.Object.DestroyImmediate(rt);
                if(temporaryCapture)
                {
                    temporaryCapture=false;
                    UnityEngine.Object.DestroyImmediate(captureCamera.gameObject);
                    Debug.Log("REFERENCE_HOUSE_INSPECTION_RENDER_COMPLETE "+capturePath);
                    if(Application.isBatchMode)EditorApplication.Exit(0);
                    return;
                }
                File.WriteAllText(Path.Combine(Output,"Unity_RenderStatus.txt"),"SUCCESS\n"+DateTime.UtcNow.ToString("O")+"\n"+capturePath+"\nGraphics: "+SystemInfo.graphicsDeviceName);
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene());AssetDatabase.SaveAssets();Debug.Log("REFERENCE_HOUSE_RENDER_COMPLETE "+capturePath);
                string reportPath=Path.Combine(Output,"Unity_ImportReport.json");if(File.Exists(reportPath)){var report=JsonUtility.FromJson<ImportReport>(File.ReadAllText(reportPath));report.lights=UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Length;File.WriteAllText(reportPath,JsonUtility.ToJson(report,true));}
                if(Application.isBatchMode)EditorApplication.Exit(0);
            }
            catch(Exception e){Failure(e);}
        }

        static void Failure(Exception e)
        {
            Debug.LogException(e);Directory.CreateDirectory(Output);File.WriteAllText(Path.Combine(Output,"Unity_Error.txt"),e.ToString());if(Application.isBatchMode)EditorApplication.Exit(1);
        }
        internal static string Safe(string n){string clean=new string(n.Select(c=>char.IsLetterOrDigit(c)||c=='_'||c=='-'?c:'_').ToArray());return clean.Length>100?clean.Substring(0,100):clean;}
        static Vector3 V(float[] v){return v!=null && v.Length>=3?new Vector3(v[0],v[1],v[2]):Vector3.zero;}
        static Color C(float[] c,Color fallback){return c!=null && c.Length>=3?new Color(c[0],c[1],c[2],c.Length>3?c[3]:1):fallback;}
        static T SaveOrUpdate<T>(T value,string path)where T:UnityEngine.Object
        {
            var old=AssetDatabase.LoadAssetAtPath<T>(path);if(old){EditorUtility.CopySerialized(value,old);UnityEngine.Object.DestroyImmediate(value);EditorUtility.SetDirty(old);return old;}AssetDatabase.CreateAsset(value,path);return value;
        }
        [Serializable] class ImportReport { public string unityVersion,scene,prefab,camera; public int meshCount,materialCount,triangleCount,lights,missingMaterials; public string[] errors; }
    }
}
