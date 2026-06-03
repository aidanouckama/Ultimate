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
///   2. Picks idle / run / backward / strafe / throw clips out of the library.
///   3. Builds a small Animator Controller: a 2D directional blend tree
///      (forward / backpedal / strafe, driven by MoveX·MoveZ) plus Any -&gt; Throw / Dive / Jump.
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
        // Forward locomotion (two speeds) feeds the +Z axis of a 2D directional tree.
        AnimationClip slowRun = pick(new[] { "slow run", "slow_run", "slowrun", "jog" });
        AnimationClip fastRun = pick(new[] { "fast run", "fast_run", "fastrun", "sprint" });
        AnimationClip anyRun  = pick(new[] { "running", "run", "walk_fwd", "zombie_walk", "walk" });
        // Directional clips fill the other axes of the 2D tree. "backward" (not bare
        // "back", which would grab "Run Look Back"); "right"/"left" take a turn or a
        // strafe. Left is usually absent (mirror-export a Right, or grab a Left Strafe).
        AnimationClip backward = pick(new[] { "backward", "back run", "run back", "backpedal" });
        AnimationClip right    = pick(new[] { "strafe right", "right strafe", "right turn", "right" });
        AnimationClip left     = pick(new[] { "strafe left", "left strafe", "left turn", "left" });
        AnimationClip throwC   = pick(new[] { "throw", "pitch", "overhand", "pickup", "interact" });
        AnimationClip diveC    = pick(new[] { "dive", "layout" });          // horizontal layout
        AnimationClip jumpC    = pick(new[] { "jump_start", "jump", "leap" });   // vertical jump
        if (idle == null) idle = entries.Select(e => e.clip).FirstOrDefault();
        // Degrade gracefully: missing a fast clip → use any run; missing slow → reuse fast.
        if (fastRun == null) fastRun = anyRun ?? slowRun ?? idle;
        if (slowRun == null) slowRun = fastRun;
        Debug.Log($"[CharacterSetup] idle={Name(idle)}  slow={Name(slowRun)}  fast={Name(fastRun)}  " +
                  $"back={Name(backward)}  right={Name(right)}  left={Name(left)}  " +
                  $"throw={Name(throwC)}  dive={Name(diveC)}  jump={Name(jumpC)}");

        // 3. animator controller (2D directional blend tree + any-state throw/dive/jump)
        var controller = BuildController(idle, slowRun, fastRun, backward, right, left, throwC, diveC, jumpC);

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
            "• animator: 2D directional tree (fwd/back/strafe) + Throw + Dive + Jump\n" +
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
            // Mixamo names every clip "mixamo.com", so the clip name alone won't tell us
            // it's a run. Fall back to the source file name (e.g. "Fast Run.fbx").
            string fn = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            bool fileIsLoco = fn.Contains("run") || fn.Contains("walk") || fn.Contains("jog") ||
                              fn.Contains("sprint") || fn.Contains("idle");
            // A file tagged "(mirror)" is a left/right flip of another clip — Unity's
            // humanoid retargeting can mirror it for us, so we don't need a real
            // mirror-exported FBX (which Mixamo's toggle doesn't always produce).
            bool fileIsMirror = fn.Contains("mirror");
            // One-shots must NOT loop even though their name contains "run" (e.g. "Run To
            // Dive", "Run Look Back") — they play once and hand back to the blend tree.
            bool fileIsOneShot = fn.Contains("dive") || fn.Contains("look back");

            var defs = mi.defaultClipAnimations;          // start from the auto-split clips
            for (int i = 0; i < defs.Length; i++)
            {
                string n = defs[i].name.ToLowerInvariant();
                bool clipIsLoco = n.Contains("idle") || n.Contains("walk") || n.Contains("run") ||
                                  n.Contains("jog")  || n.Contains("sprint") || n.Contains("breath");
                if ((clipIsLoco || fileIsLoco) && !fileIsOneShot) defs[i].loopTime = true;
                if (fileIsMirror)                                 defs[i].mirror   = true;
            }
            mi.clipAnimations = defs;                     // commit overrides with loop enabled
            needsReimport = true;
        }
        if (needsReimport) mi.SaveAndReimport();
    }

    // ---- clip selection -------------------------------------------------------

    static string Name(Object o) => o == null ? "(none)" : o.name;

    // ---- animator controller --------------------------------------------------

    // Blend-tree positions, in the same units as the player's measured ground speed
    // (Player.moveSpeed is 7.5, AI cutters push ~1.2× that). MoveX/MoveZ are the body's
    // local velocity, so each clip plants its feet at the pace it was authored for
    // instead of skating, and the tree picks the right one by direction.
    const float SlowRunSpeed = 3.75f;
    const float FastRunSpeed = 7.5f;
    // Start the layout clip this far in (0–1) to skip "Run To Dive"'s run-up. ~0 with a
    // dedicated flat-dive clip.
    const float DiveStartOffset = 0.5f;

    static AnimatorController BuildController(AnimationClip idle, AnimationClip slowRun,
                                              AnimationClip fastRun, AnimationClip backward,
                                              AnimationClip right, AnimationClip left,
                                              AnimationClip throwC, AnimationClip diveC,
                                              AnimationClip jumpC)
    {
        var c = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        c.AddParameter("MoveX", AnimatorControllerParameterType.Float);   // strafe (local +x = right)
        c.AddParameter("MoveZ", AnimatorControllerParameterType.Float);   // fwd/back (local +z = forward)
        c.AddParameter("Throw", AnimatorControllerParameterType.Trigger);
        // Declared even if no clip matched, so Player's SetTrigger calls never warn.
        c.AddParameter("Dive",  AnimatorControllerParameterType.Trigger);
        c.AddParameter("Jump",  AnimatorControllerParameterType.Trigger);

        var sm = c.layers[0].stateMachine;
        var sLoco = c.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
        tree.useAutomaticThresholds = false;

        bool has2D = backward != null || right != null || left != null;
        if (has2D)
        {
            // 2D directional: the body can run forward, backpedal, or shuffle sideways,
            // chosen by which way it's actually moving relative to where it faces.
            tree.blendType = BlendTreeType.FreeformDirectional2D;
            tree.blendParameter  = "MoveX";
            tree.blendParameterY = "MoveZ";
            tree.AddChild(idle, new Vector2(0f, 0f));
            if (slowRun != null) tree.AddChild(slowRun, new Vector2(0f,  SlowRunSpeed));
            if (fastRun != null) tree.AddChild(fastRun, new Vector2(0f,  FastRunSpeed));
            if (backward != null) tree.AddChild(backward, new Vector2(0f, -FastRunSpeed));
            if (right != null)    tree.AddChild(right,    new Vector2( FastRunSpeed, 0f));
            if (left != null)     tree.AddChild(left,     new Vector2(-FastRunSpeed, 0f));
        }
        else
        {
            // Only forward clips on hand → a plain speed tree along the forward axis.
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "MoveZ";
            tree.AddChild(idle, 0f);
            if (slowRun != null && slowRun != fastRun) tree.AddChild(slowRun, SlowRunSpeed);
            if (fastRun != null)      tree.AddChild(fastRun, FastRunSpeed);
            else if (slowRun != null) tree.AddChild(slowRun, FastRunSpeed);
        }
        sm.defaultState = sLoco;

        if (throwC != null)
        {
            var sThrow = sm.AddState("Throw"); sThrow.motion = throwC;
            var any = sm.AddAnyStateTransition(sThrow);
            any.hasExitTime = false; any.duration = 0.05f; any.canTransitionToSelf = false;
            any.AddCondition(AnimatorConditionMode.If, 0f, "Throw");

            var back = sThrow.AddTransition(sLoco);   // return to the blend tree when done
            back.hasExitTime = true; back.exitTime = 0.85f; back.duration = 0.12f;
        }

        if (diveC != null)
        {
            // One-shot layout (horizontal dive). The only dive clip we have is "Run To
            // Dive", which runs up first — so start playback partway through (transition
            // offset) to skip the run-up and land on the dive itself. Tune DiveStartOffset
            // or swap in a dedicated flat-dive clip for a cleaner look.
            var sDive = sm.AddState("Dive"); sDive.motion = diveC;
            var any = sm.AddAnyStateTransition(sDive);
            any.hasExitTime = false; any.duration = 0.05f; any.canTransitionToSelf = false;
            any.offset = DiveStartOffset;
            any.AddCondition(AnimatorConditionMode.If, 0f, "Dive");

            var back = sDive.AddTransition(sLoco);
            back.hasExitTime = true; back.exitTime = 0.9f; back.duration = 0.2f;
        }

        if (jumpC != null)
        {
            // One-shot vertical jump (UAL2 "NinjaJump_Start" — the leap up).
            var sJump = sm.AddState("Jump"); sJump.motion = jumpC;
            var any = sm.AddAnyStateTransition(sJump);
            any.hasExitTime = false; any.duration = 0.05f; any.canTransitionToSelf = false;
            any.AddCondition(AnimatorConditionMode.If, 0f, "Jump");

            var back = sJump.AddTransition(sLoco);
            back.hasExitTime = true; back.exitTime = 0.8f; back.duration = 0.15f;
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
