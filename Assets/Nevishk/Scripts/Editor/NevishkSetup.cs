#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Nevishk.EditorTools
{
    public static class NevishkSetup
    {
        const string Root = "Assets/Nevishk";
        const string MatDir = Root + "/Materials";
        const string ScenePath = Root + "/Nevishk.unity";

        [MenuItem("Nevishk/1. Set Up Scene", priority = 1)]
        public static void SetUpScene()
        {
            if (!Directory.Exists(MatDir)) AssetDatabase.CreateFolder(Root, "Materials");

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
            {
                EditorUtility.DisplayDialog("Nevishk", "URP Lit shader not found. Create the project from the Universal 3D template.", "OK");
                return;
            }
            var litMat = GetOrCreateMat(MatDir + "/NevishkLit.mat", lit);
            var groundMat = GetOrCreateMat(MatDir + "/NevishkGround.mat", lit);

            ConfigureBaseMap(groundMat);
            RaiseShadowDistance(1600f);

            // Fresh scene so nothing from the template gets in the way.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var cam = Camera.main;
            if (cam != null && cam.GetComponent<MapCameraController>() == null) cam.gameObject.AddComponent<MapCameraController>();

            var go = new GameObject("Nevishk Map");
            var map = go.AddComponent<NevishkMap>();
            map.litTemplate = litMat;
            map.groundMaterial = groundMat;

            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            Selection.activeGameObject = go;
            Debug.Log("[Nevishk] Scene ready at " + ScenePath + ". Press Play.");
        }

        [MenuItem("Nevishk/2. Configure Web Build", priority = 2)]
        public static void ConfigureWeb()
        {
            var web = NamedBuildTarget.WebGL;
            PlayerSettings.productName = "Невишк";
            PlayerSettings.companyName = "Nevishk";
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = true;   // works on GitHub Pages / any static host
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
            PlayerSettings.stripEngineCode = true;
            PlayerSettings.SetManagedStrippingLevel(web, ManagedStrippingLevel.Medium);
            PlayerSettings.SetIl2CppCodeGeneration(web, Il2CppCodeGeneration.OptimizeSize);
            PlayerSettings.runInBackground = true;

            AlwaysInclude("TextMeshPro/Distance Field", "TextMeshPro/Mobile/Distance Field");
            KeepFogVariants();
            AssetDatabase.SaveAssets();
            Debug.Log("[Nevishk] Web build settings applied. Switch platform to Web in Build Profiles if you haven't.");
        }

        [MenuItem("Nevishk/3. Build for Web", priority = 3)]
        public static void BuildWeb()
        {
            ConfigureWeb();
            if (!File.Exists(ScenePath)) { EditorUtility.DisplayDialog("Nevishk", "Run Nevishk ▸ 1. Set Up Scene first.", "OK"); return; }
            string outDir = Path.GetFullPath("Builds/Web");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            });
            Debug.Log($"[Nevishk] Web build {report.summary.result}: {outDir} ({report.summary.totalSize / (1024 * 1024f):F1} MB)");
            if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorUtility.RevealInFinder(outDir);
        }

        // ---------------------------------------------------------------- helpers

        static Material GetOrCreateMat(string path, Shader shader)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static void ConfigureBaseMap(Material groundMat)
        {
            const string texPath = Root + "/Resources/nevishk_basemap.png";
            if (AssetImporter.GetAtPath(texPath) is TextureImporter ti)
            {
                ti.maxTextureSize = 2048;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Trilinear;
                ti.anisoLevel = 8;
                ti.mipmapEnabled = true;
                ti.SaveAndReimport();
            }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex != null) groundMat.SetTexture("_BaseMap", tex);
            EditorUtility.SetDirty(groundMat);
        }

        static void RaiseShadowDistance(float d)
        {
            var rp = GraphicsSettings.currentRenderPipeline ?? GraphicsSettings.defaultRenderPipeline;
            if (rp == null) return;
            var so = new SerializedObject(rp);
            var p = so.FindProperty("m_ShadowDistance");
            if (p != null && p.floatValue < d) { p.floatValue = d; so.ApplyModifiedProperties(); }
        }

        /// Fog is switched on from code at runtime, so "Automatic" stripping (which looks at the scene) would remove
        /// it from every shader in the build. Keep the linear fog variants explicitly.
        static void KeepFogVariants()
        {
            var gs = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
            if (gs == null) return;
            var so = new SerializedObject(gs);
            var strip = so.FindProperty("m_FogStripping");
            var linear = so.FindProperty("m_FogKeepLinear");
            if (strip != null) strip.intValue = 1;   // Custom
            if (linear != null) linear.boolValue = true;
            so.ApplyModifiedProperties();
        }

        static void AlwaysInclude(params string[] shaderNames)
        {
            var gs = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
            if (gs == null) return;
            var so = new SerializedObject(gs);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            foreach (var name in shaderNames)
            {
                var sh = Shader.Find(name);
                if (sh == null) continue;
                bool present = false;
                for (int i = 0; i < arr.arraySize; i++)
                    if (arr.GetArrayElementAtIndex(i).objectReferenceValue == sh) present = true;
                if (present) continue;
                arr.InsertArrayElementAtIndex(arr.arraySize);
                arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
            }
            so.ApplyModifiedProperties();
        }
    }
}
#endif
