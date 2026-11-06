using System.Collections.Generic;
using System.IO;
using System.Linq;
using GestureBenchmark.Core;
using GestureBenchmark.Dots;
using GestureBenchmark.Mono;
using GestureBenchmark.Runner;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Extensions.PerformanceSettings;

namespace GestureBenchmark.EditorTools
{
    /// <summary>
    /// Menu: Penelitian > Setup Testbed
    /// Membuat material, prefab cube Mono, tiga scene (Runner, Mono, DOTS),
    /// mengisi Build Settings, dan memeriksa pengaturan performa Quest.
    /// Aman dijalankan ulang: aset yang sudah ada akan ditimpa dengan versi baru.
    /// Scene yang sedang Bapak buka tidak disentuh.
    /// </summary>
    public static class PenelitianSetup
    {
        const string Root      = "Assets/Penelitian";
        const string DataPath  = Root + "/P01_20260930_225934.csv";
        const string MatPath   = Root + "/Materials/BenchmarkCube.mat";
        const string PrefabPath= Root + "/Prefabs/MonoCube.prefab";
        const string SceneDir  = Root + "/Scenes";

        [MenuItem("Penelitian/Setup Testbed")]
        public static void Setup()
        {
            Directory.CreateDirectory(Root + "/Materials");
            Directory.CreateDirectory(Root + "/Prefabs");
            Directory.CreateDirectory(SceneDir);
            AssetDatabase.Refresh();

            if (EditorApplication.isPlaying) { Debug.LogError("[Setup] Hentikan Play Mode dulu."); return; }

            // Scene lain yang belum disimpan tidak boleh hilang. Perubahan di scene penelitian
            // sendiri dibuang karena ketiganya dibuat ulang dari kode.
            string previous = null;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var sc = SceneManager.GetSceneAt(i);
                bool ours = sc.path.StartsWith(SceneDir);
                if (!ours && sc.isDirty)
                {
                    Debug.LogError("[Setup] Simpan dulu scene yang sedang dibuka: " + (sc.path == "" ? "(Untitled)" : sc.path));
                    return;
                }
                if (!ours && previous == null && !string.IsNullOrEmpty(sc.path)) previous = sc.path;
            }

            var dataset = AssetDatabase.LoadAssetAtPath<TextAsset>(DataPath);
            if (dataset == null) { Debug.LogError("[Setup] Dataset tidak ditemukan: " + DataPath); return; }

            var mat    = CreateMaterial();
            var mesh   = GetCubeMesh();
            var prefab = CreateMonoCubePrefab(mat, mesh);

            BuildRunnerScene();
            BuildMonoScene(dataset, prefab);
            BuildDotsScene(dataset, mesh, mat);
            SetBuildScenes();
            ConfigurePlayerAndXR();

            AssetDatabase.SaveAssets();

            // kembali ke scene yang tadi dibuka, atau ke scene Runner
            string back = previous ?? $"{SceneDir}/Penelitian_Runner.unity";
            if (File.Exists(back)) EditorSceneManager.OpenScene(back, OpenSceneMode.Single);
            Debug.Log("[Setup] Testbed siap. Scene ada di " + SceneDir);
        }

        // ---------------------------------------------------------------- aset

        static Material CreateMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, MatPath);
            }
            mat.shader = shader;
            mat.SetColor("_BaseColor", new Color(0.25f, 0.6f, 0.95f));
            mat.enableInstancing = false;   // Mono: SRP Batcher, DOTS: BatchRendererGroup
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Mesh GetCubeMesh()
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var mesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(tmp);
            return mesh;
        }

        static GameObject CreateMonoCubePrefab(Material mat, Mesh mesh)
        {
            var go = new GameObject("MonoCube");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            go.AddComponent<MonoCube>();           // tanpa collider: tidak ada beban physics
            go.transform.localScale = Vector3.one * CubeLayout.BaseSize;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        // ---------------------------------------------------------------- scene

        static Scene NewScene(string name, out string path)
        {
            path = $"{SceneDir}/{name}.unity";
            // Single: scene baru menggantikan scene yang terbuka (sudah dipastikan tidak ada yang belum disimpan)
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = name;
            return scene;
        }

        static void Save(Scene scene, string path)
        {
            EditorSceneManager.SaveScene(scene, path);
        }

        static void Put(Scene s, GameObject go) => SceneManager.MoveGameObjectToScene(go, s);

        static void AddCommon(Scene s)
        {
            // Kamera XR: posisi/rotasi kepala dari HMD
            var cam = new GameObject("XR Camera");
            var c = cam.AddComponent<Camera>();
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = new Color(0.08f, 0.08f, 0.1f);
            c.nearClipPlane = 0.05f;
            c.farClipPlane = 50f;
            cam.tag = "MainCamera";
            cam.transform.position = new Vector3(0f, 1.6f, 0f);
            var tpd = cam.AddComponent<TrackedPoseDriver>();
            tpd.positionInput = new InputActionProperty(new InputAction("HeadPosition", InputActionType.Value,
                "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
            tpd.rotationInput = new InputActionProperty(new InputAction("HeadRotation", InputActionType.Value,
                "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
            Put(s, cam);

            var light = new GameObject("Directional Light");
            var l = light.AddComponent<Light>();
            l.type = LightType.Directional;
            l.shadows = LightShadows.None;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            Put(s, light);
        }

        static void BuildRunnerScene()
        {
            var s = NewScene("Penelitian_Runner", out var path);
            AddCommon(s);

            var runnerGo = new GameObject("BenchmarkRunner");
            var runner = runnerGo.AddComponent<BenchmarkRunner>();

            var textGo = new GameObject("Status");
            textGo.transform.SetParent(runnerGo.transform, false);
            textGo.transform.position = new Vector3(0f, 1.6f, 2f);
            var tm = textGo.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font;
            tm.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            tm.fontSize = 48;
            tm.characterSize = 0.01f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.text = "Penelitian DOTS vs Mono";
            runner.statusText = tm;
            Put(s, runnerGo);

            Save(s, path);
        }

        static void BuildMonoScene(TextAsset dataset, GameObject prefab)
        {
            var s = NewScene("Penelitian_Mono", out var path);
            AddCommon(s);

            var go = new GameObject("Benchmark (Mono)");
            var mgr = go.AddComponent<MonoBenchmarkManager>();
            mgr.cubePrefab = prefab;
            var ctl = go.AddComponent<BenchmarkController>();
            ctl.target = mgr;
            ctl.dataset = dataset;
            Put(s, go);

            Save(s, path);
        }

        static void BuildDotsScene(TextAsset dataset, Mesh mesh, Material mat)
        {
            var s = NewScene("Penelitian_DOTS", out var path);
            AddCommon(s);

            var go = new GameObject("Benchmark (DOTS)");
            var bridge = go.AddComponent<DotsBenchmarkBridge>();
            bridge.cubeMesh = mesh;
            bridge.cubeMaterial = mat;
            var ctl = go.AddComponent<BenchmarkController>();
            ctl.target = bridge;
            ctl.dataset = dataset;
            Put(s, go);

            Save(s, path);
        }

        static void SetBuildScenes()
        {
            var ours = new[] { "Penelitian_Runner", "Penelitian_Mono", "Penelitian_DOTS" }
                .Select(n => new EditorBuildSettingsScene($"{SceneDir}/{n}.unity", true)).ToList();
            var ourPaths = new HashSet<string>(ours.Select(o => o.path));
            // scene lama tetap terdaftar tapi DINONAKTIFKAN supaya tidak ikut ter-build
            var others = EditorBuildSettings.scenes.Where(x => !ourPaths.Contains(x.path))
                .Select(x => new EditorBuildSettingsScene(x.path, false));
            EditorBuildSettings.scenes = ours.Concat(others).ToArray();
        }

        // ---------------------------------------------------------------- pengaturan

        static void ConfigurePlayerAndXR()
        {
            PlayerSettings.enableFrameTimingStats = true;
            EditorUserBuildSettings.development = false;

            var xr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (xr == null) { Debug.LogWarning("[Setup] OpenXR settings Android tidak ditemukan"); return; }

            var perf = xr.GetFeature<XrPerformanceSettingsFeature>();
            if (perf != null && !perf.enabled) { perf.enabled = true; EditorUtility.SetDirty(xr); }

            foreach (var f in xr.GetFeatures())
            {
                string t = f.GetType().Name;
                if (t == "SpaceWarpFeature" || t == "AutomaticDynamicResolutionFeature" || t == "FoveatedRenderingFeature")
                {
                    if (f.enabled) { f.enabled = false; EditorUtility.SetDirty(xr); }
                    Debug.Log($"[Setup] {t}: OFF");
                }
            }
            Debug.Log("[Setup] Frame Timing Stats: ON | Development Build: OFF | XrPerformanceSettings: ON");
        }
    }
}
