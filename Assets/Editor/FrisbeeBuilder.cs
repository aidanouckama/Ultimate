#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor utility that "unpacks" the runtime GameBootstrap into real project
/// assets: material assets, prefabs (Field, Disc, PlayerHome, PlayerAway) and a
/// saved, fully-wired scene. Run it once from the menu, then you can tweak
/// everything in the Inspector instead of in code.
///
/// Menu: Frisbee ▸ Build Assets & Scene
/// </summary>
public static class FrisbeeBuilder
{
    const string Root      = "Assets/Frisbee";
    const string MatDir    = Root + "/Materials";
    const string PrefabDir = Root + "/Prefabs";
    const string SceneDir  = Root + "/Scenes";
    const string ScenePath = SceneDir + "/Frisbee.unity";

    // field dimensions (kept in sync with the Field component defaults)
    const float Length = 100f, Width = 37f, EndZone = 18f;
    const int PerTeam = 5;

    static readonly Color HomeColor = new Color(0.20f, 0.45f, 0.95f);
    static readonly Color AwayColor = new Color(0.92f, 0.27f, 0.27f);

    [MenuItem("Frisbee/Build Assets & Scene")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        EnsureFolders();

        // --- materials -----------------------------------------------------
        var grass    = MakeMat("Grass",    new Color(0.22f, 0.55f, 0.25f));
        var line     = MakeMat("Line",     Color.white);
        var ezHome   = MakeMat("EndZoneHome", Color.Lerp(new Color(0.18f, 0.45f, 0.20f), HomeColor, 0.25f));
        var ezAway   = MakeMat("EndZoneAway", Color.Lerp(new Color(0.18f, 0.45f, 0.20f), AwayColor, 0.25f));
        var discMat  = MakeMat("Disc",     Color.white);
        var homeMat  = MakeMat("Home",     HomeColor);
        var awayMat  = MakeMat("Away",     AwayColor);

        // --- prefabs -------------------------------------------------------
        var fieldPrefab  = BuildFieldPrefab(grass, line, ezHome, ezAway);
        var discPrefab   = BuildDiscPrefab(discMat);
        var homePrefab   = BuildPlayerPrefab("PlayerHome", Team.Home, homeMat);
        var awayPrefab   = BuildPlayerPrefab("PlayerAway", Team.Away, awayMat);

        AssetDatabase.SaveAssets();

        // --- scene ---------------------------------------------------------
        BuildScene(fieldPrefab, discPrefab, homePrefab, awayPrefab);

        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Frisbee",
            "Built materials, prefabs, and the scene at\n" + ScenePath +
            "\n\nThe scene is open and ready — press Play.", "Nice");
    }

    // ---- prefab builders --------------------------------------------------

    static GameObject BuildFieldPrefab(Material grass, Material line,
                                       Material ezHome, Material ezAway)
    {
        var root = new GameObject("Field");
        var f = root.AddComponent<Field>();
        f.length = Length; f.width = Width; f.endZoneDepth = EndZone;

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.SetParent(root.transform, false);
        ground.transform.localScale = new Vector3(Width / 10f, 1f, Length / 10f);
        ground.GetComponent<Renderer>().sharedMaterial = grass;
        // keep the ground collider so it reads as a real floor in the editor

        var mid = MakeBox("Midline", root.transform,
            new Vector3(0f, 0.02f, 0f), new Vector3(Width, 0.04f, 0.4f), line);

        float zHome = Length / 2f - EndZone / 2f;
        MakeBox("EndZoneHome", root.transform,
            new Vector3(0f, 0.01f, zHome), new Vector3(Width, 0.02f, EndZone), ezHome);
        MakeBox("EndZoneAway", root.transform,
            new Vector3(0f, 0.01f, -zHome), new Vector3(Width, 0.02f, EndZone), ezAway);

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + "/Field.prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }

    static GameObject BuildDiscPrefab(Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "Disc";
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.localScale = new Vector3(0.55f, 0.05f, 0.55f);
        go.GetComponent<Renderer>().sharedMaterial = mat;
        go.AddComponent<Rigidbody>();
        go.AddComponent<Disc>();

        var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabDir + "/Disc.prefab");
        Object.DestroyImmediate(go);
        return prefab;
    }

    static GameObject BuildPlayerPrefab(string name, Team team, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.localScale = new Vector3(0.9f, 1f, 0.9f);
        go.GetComponent<Renderer>().sharedMaterial = mat;

        var hand = new GameObject("Hand");
        hand.transform.SetParent(go.transform, false);
        hand.transform.localPosition = new Vector3(0.6f, 0.9f, 0.5f);

        var p = go.AddComponent<Player>();
        p.team = team;
        p.HandPoint = hand.transform;
        go.AddComponent<AIController>();

        var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabDir + "/" + name + ".prefab");
        Object.DestroyImmediate(go);
        return prefab;
    }

    // ---- scene builder ----------------------------------------------------

    static void BuildScene(GameObject fieldPrefab, GameObject discPrefab,
                           GameObject homePrefab, GameObject awayPrefab)
    {
        var scene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // field + disc
        var field = (GameObject)PrefabUtility.InstantiatePrefab(fieldPrefab);
        var disc  = (GameObject)PrefabUtility.InstantiatePrefab(discPrefab);
        disc.transform.position = new Vector3(0f, 0.06f, 0f);

        // sun
        var sun = new GameObject("Directional Light");
        var light = sun.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.color = new Color(1f, 0.97f, 0.9f);
        sun.transform.rotation = Quaternion.Euler(55f, -30f, 0f);

        // camera
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        camGo.AddComponent<AudioListener>();
        camGo.AddComponent<CameraRig>();
        camGo.transform.position = new Vector3(0f, 30f, -26f);
        cam.farClipPlane = 500f;
        cam.backgroundColor = new Color(0.45f, 0.62f, 0.78f);

        // game manager (match + input + hud)
        var gm = new GameObject("GameManager");
        var mm = gm.AddComponent<MatchManager>();
        mm.field = field.GetComponent<Field>();
        mm.disc  = disc.GetComponent<Disc>();
        var hc = gm.AddComponent<HumanController>();
        hc.cam = cam;
        gm.AddComponent<Hud>();

        // players in an opening formation (the match re-lines them on kickoff)
        SpawnLine(homePrefab, -15f);
        SpawnLine(awayPrefab,  15f);

        // flat ambient so it's lit without baking
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.5f, 0.55f, 0.6f);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
    }

    static void SpawnLine(GameObject prefab, float z)
    {
        for (int i = 0; i < PerTeam; i++)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            float t = PerTeam == 1 ? 0.5f : i / (float)(PerTeam - 1);
            float x = Mathf.Lerp(-Width * 0.35f, Width * 0.35f, t);
            go.transform.position = new Vector3(x, 1f, z);
        }
    }

    // ---- helpers ----------------------------------------------------------

    static GameObject MakeBox(string name, Transform parent, Vector3 pos,
                              Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    static Material MakeMat(string name, Color c)
    {
        string path = MatDir + "/" + name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) { existing.color = c; return existing; }

        Shader s = Shader.Find("Universal Render Pipeline/Lit");
        if (s == null) s = Shader.Find("Standard");
        var m = new Material(s) { color = c };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static void EnsureFolders()
    {
        EnsureFolder("Assets", "Frisbee");
        EnsureFolder(Root, "Materials");
        EnsureFolder(Root, "Prefabs");
        EnsureFolder(Root, "Scenes");
    }

    static void EnsureFolder(string parent, string child)
    {
        string full = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(full))
            AssetDatabase.CreateFolder(parent, child);
    }
}
#endif
