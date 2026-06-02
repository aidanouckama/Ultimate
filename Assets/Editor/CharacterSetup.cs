#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// One-shot helper that turns the staged Quaternius assets in Assets/Characters
/// into playable team players:
///   1. Sets the body + animation FBX rigs to Humanoid (so anims retarget).
///   2. Picks idle / run / throw clips out of the animation library.
///   3. Builds a small Animator Controller (Idle &lt;-&gt; Run, Any -&gt; Throw).
///   4. Makes flat blue/red team materials.
///   5. Rebuilds the EXISTING PlayerHome / PlayerAway prefabs (same GUIDs) so every
///      instance already in the scene swaps from capsule to character automatically.
///
/// Menu: Frisbee ▸ Setup Characters. Run once, eyeball the result, then delete this
/// file — it does not regenerate the scene, so it won't clobber your work later.
/// </summary>
public static class CharacterSetup
{
    const string CharRoot   = "Assets/Characters";
    const string MaleFbx    = CharRoot + "/Models/Superhero_Male_FullBody.fbx";
    const string FemaleFbx  = CharRoot + "/Models/Superhero_Female_FullBody.fbx";
    const string AnimFbx    = CharRoot + "/Animations/UAL2_Standard.fbx";
    const string MatDir     = CharRoot + "/Materials";
    const string ControllerPath = CharRoot + "/PlayerAnimator.controller";

    // overwrite the capsule prefabs in place → scene instances update for free
    const string HomePrefab = "Assets/Frisbee/Prefabs/PlayerHome.prefab";
    const string AwayPrefab = "Assets/Frisbee/Prefabs/PlayerAway.prefab";

    static readonly Color HomeColor = new Color(0.20f, 0.45f, 0.95f);
    static readonly Color AwayColor = new Color(0.92f, 0.27f, 0.27f);

    // Up-scale the character so it reads at the right size next to the disc (the disc
    // is ~0.55 units across). Bump this and re-run Setup Characters to resize.
    const float ModelScale = 1.8f;

    [MenuItem("Frisbee/Setup Characters")]
    public static void Setup()
    {
        if (AssetImporter.GetAtPath(MaleFbx) == null)
        {
            EditorUtility.DisplayDialog("Frisbee",
                "Couldn't find " + MaleFbx + ".\nMake sure the Quaternius assets are " +
                "imported under Assets/Characters first.", "OK");
            return;
        }

        // 1. character rigs → Humanoid
        MakeHumanoid(MaleFbx,   loopLocomotion: false);
        MakeHumanoid(FemaleFbx, loopLocomotion: false);

        // every FBX in the Animations folder becomes a Humanoid source (so a Mixamo
        // run dropped in there is rigged + looped automatically), and we pool ALL their
        // clips. Each clip remembers its source filename, since Mixano names clips
        // unhelpfully ("mixamo.com") — we match a run by file name as well as clip name.
        var animPaths = AssetDatabase.FindAssets("t:Model", new[] { CharRoot + "/Animations" })
                        .Select(AssetDatabase.GUIDToAssetPath).Distinct().ToList();
        var entries = new List<(AnimationClip clip, string file)>();
        foreach (var p in animPaths)
        {
            MakeHumanoid(p, loopLocomotion: true);
            string fn = System.IO.Path.GetFileNameWithoutExtension(p).ToLowerInvariant();
            foreach (var cl in AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>())
                if (!cl.name.StartsWith("__preview")) entries.Add((cl, fn));
        }
        Debug.Log($"[CharacterSetup] {entries.Count} clips: " +
                  string.Join(", ", entries.Select(e => e.clip.name)));

        // match by clip name OR source filename, in priority order
        System.Func<string[], AnimationClip> pick = keys =>
        {
            foreach (var k in keys)
            {
                var hit = entries.FirstOrDefault(e =>
                    e.clip.name.ToLowerInvariant().Contains(k) || e.file.Contains(k));
                if (hit.clip != null) return hit.clip;
            }
            return null;
        };

        AnimationClip idle  = pick(new[] { "idle", "breath", "stand" });
        // A dropped-in "run"/"sprint"/"jog" wins; otherwise fall back to a forward
        // walk (sped up via LegSpeed). Avoid Walk_Carry (cradling pose).
        AnimationClip run   = pick(new[] { "run", "sprint", "jog", "walk_fwd", "zombie_walk", "walk" });
        AnimationClip throwC = pick(new[] { "throw", "pitch", "overhand", "pickup", "interact" });
        if (idle == null) idle = entries.Select(e => e.clip).FirstOrDefault();
        if (run  == null) run  = idle;
        Debug.Log($"[CharacterSetup] idle={Name(idle)}  run={Name(run)}  throw={Name(throwC)}");

        // 3. animator controller
        var controller = BuildController(idle, run, throwC);

        // 4. team materials
        EnsureFolder(CharRoot, "Materials");
        var homeMat = MakeMat("TeamHome", HomeColor);
        var awayMat = MakeMat("TeamAway", AwayColor);

        // 5. rebuild prefabs in place
        BuildPlayerPrefab(HomePrefab, "PlayerHome", Team.Home, MaleFbx, homeMat, controller);
        BuildPlayerPrefab(AwayPrefab, "PlayerAway", Team.Away, MaleFbx, awayMat, controller);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Frisbee",
            "Characters set up:\n" +
            "• rigs → Humanoid\n" +
            "• animator: Idle / Run / Throw\n" +
            "• blue/red team materials\n" +
            "• PlayerHome & PlayerAway prefabs rebuilt\n\n" +
            "Open Assets/Frisbee/Scenes/Frisbee.unity and press Play.\n" +
            "Check the Console for the clip names that were matched.", "Nice");
    }

    // ---- importer -------------------------------------------------------------

    static void MakeHumanoid(string path, bool loopLocomotion)
    {
        var mi = AssetImporter.GetAtPath(path) as ModelImporter;
        if (mi == null) { Debug.LogWarning("[CharacterSetup] no model importer at " + path); return; }

        // Only reimport when something actually changed, so re-running this to tweak
        // sizes/materials is fast (the 24 MB animation FBX is slow to reimport).
        bool needsReimport = mi.animationType != ModelImporterAnimationType.Human;
        mi.animationType = ModelImporterAnimationType.Human;
        mi.avatarSetup   = ModelImporterAvatarSetup.CreateFromThisModel;

        if (loopLocomotion && (mi.clipAnimations == null || mi.clipAnimations.Length == 0))
        {
            var defs = mi.defaultClipAnimations;          // start from the auto-split clips
            for (int i = 0; i < defs.Length; i++)
            {
                string n = defs[i].name.ToLowerInvariant();
                if (n.Contains("idle") || n.Contains("walk") || n.Contains("run") ||
                    n.Contains("jog")  || n.Contains("sprint") || n.Contains("breath"))
                    defs[i].loopTime = true;
            }
            mi.clipAnimations = defs;                     // commit overrides with loop enabled
            needsReimport = true;
        }
        if (needsReimport) mi.SaveAndReimport();
    }

    // ---- clip selection -------------------------------------------------------

    static string Name(Object o) => o == null ? "(none)" : o.name;

    // ---- animator controller --------------------------------------------------

    static AnimatorController BuildController(AnimationClip idle, AnimationClip run, AnimationClip throwC)
    {
        var c = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        c.AddParameter("Speed", AnimatorControllerParameterType.Float);
        c.AddParameter("Throw", AnimatorControllerParameterType.Trigger);
        // playback-speed multiplier for the run cycle, so foot cadence matches the
        // (script-driven) ground speed instead of skating. Player feeds it each frame.
        c.AddParameter(new AnimatorControllerParameter
        {
            name = "LegSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f
        });

        var sm = c.layers[0].stateMachine;
        var sIdle = sm.AddState("Idle"); sIdle.motion = idle;
        var sRun  = sm.AddState("Run");  sRun.motion  = run;
        sRun.speedParameterActive = true;          // Run plays at LegSpeed×
        sRun.speedParameter = "LegSpeed";
        sm.defaultState = sIdle;

        var toRun = sIdle.AddTransition(sRun);
        toRun.hasExitTime = false; toRun.duration = 0.12f;
        toRun.AddCondition(AnimatorConditionMode.Greater, 0.6f, "Speed");

        var toIdle = sRun.AddTransition(sIdle);
        toIdle.hasExitTime = false; toIdle.duration = 0.12f;
        toIdle.AddCondition(AnimatorConditionMode.Less, 0.6f, "Speed");

        if (throwC != null)
        {
            var sThrow = sm.AddState("Throw"); sThrow.motion = throwC;
            var any = sm.AddAnyStateTransition(sThrow);
            any.hasExitTime = false; any.duration = 0.05f; any.canTransitionToSelf = false;
            any.AddCondition(AnimatorConditionMode.If, 0f, "Throw");

            var back = sThrow.AddTransition(sIdle);
            back.hasExitTime = true; back.exitTime = 0.85f; back.duration = 0.12f;
        }

        EditorUtility.SetDirty(c);
        return c;
    }

    // ---- prefab ---------------------------------------------------------------

    static void BuildPlayerPrefab(string prefabPath, string name, Team team,
                                  string modelFbx, Material mat, AnimatorController controller)
    {
        var root = new GameObject(name);

        // A plain "Model" container carries the position offset AND the scale. Scaling
        // this regular transform persists reliably; setting scale directly on the FBX
        // nested-prefab-instance root does NOT get saved (Unity drops the override).
        // Player math treats the root pivot as the body centre at StandHeight; the
        // model's pivot is at the feet, so drop it by StandHeight to stand on the ground,
        // and scale about that foot pivot so the character grows taller but stays grounded.
        var modelRoot = new GameObject("Model");
        modelRoot.transform.SetParent(root.transform, false);
        modelRoot.transform.localPosition = new Vector3(0f, -Player.StandHeight, 0f);
        modelRoot.transform.localScale = Vector3.one * ModelScale;

        var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelFbx);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
        model.transform.SetParent(modelRoot.transform, false);

        var animator = model.GetComponent<Animator>();
        if (animator == null) animator = model.AddComponent<Animator>();
        if (animator.avatar == null)
            animator.avatar = AssetDatabase.LoadAllAssetsAtPath(modelFbx).OfType<Avatar>().FirstOrDefault();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;   // we drive position by script

        // flat team colour on every body submesh (keeps it readable from the broadcast cam)
        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var arr = smr.sharedMaterials;
            for (int i = 0; i < arr.Length; i++) arr[i] = mat;
            smr.sharedMaterials = arr;
        }

        // hand point under the right-hand bone so the held disc rides the animated hand
        Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        var hp = new GameObject("DiscHand").transform;
        if (hand != null)
        {
            hp.SetParent(hand, false);
            hp.localPosition = new Vector3(0f, 0f, 0.08f);
        }
        else
        {
            hp.SetParent(root.transform, false);
            hp.localPosition = new Vector3(0.4f, 0.4f, 0.3f);
            Debug.LogWarning("[CharacterSetup] RightHand bone not found on " + name +
                             "; using a fallback hand point.");
        }

        var player = root.AddComponent<Player>();
        player.team = team;
        player.HandPoint = hp;
        player.animator = animator;

        root.AddComponent<AIController>();

        // report the on-ground height so the disc-to-player size ratio is easy to judge
        Bounds b = default; bool has = false;
        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (!has) { b = smr.bounds; has = true; } else b.Encapsulate(smr.bounds);
        }
        if (has) Debug.Log($"[CharacterSetup] {name} height ≈ {b.size.y:F2} units " +
                           $"(ModelScale {ModelScale}); disc is ~0.55 across.");

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
    }

    // ---- helpers --------------------------------------------------------------

    static Material MakeMat(string n, Color col)
    {
        string path = MatDir + "/" + n + ".mat";
        Shader s = Shader.Find("Universal Render Pipeline/Lit");
        if (s == null) s = Shader.Find("Standard");

        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        var m = existing != null ? existing : new Material(s);
        m.shader = s;
        m.color = col;
        if (m.HasProperty("_BaseColor"))  m.SetColor("_BaseColor", col);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);

        if (existing == null) AssetDatabase.CreateAsset(m, path);
        else EditorUtility.SetDirty(m);
        return m;
    }

    static void EnsureFolder(string parent, string child)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + child))
            AssetDatabase.CreateFolder(parent, child);
    }
}
#endif
