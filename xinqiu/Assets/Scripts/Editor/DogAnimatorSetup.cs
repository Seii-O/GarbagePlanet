// @trace ART-011, ART-014
using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace TrashPlanet.Whitebox.Editor
{
    /// <summary>
    /// 给已换成 RobotPuppy.fbx 的伙伴狗接骨骼动画（ART-014 由程序化改为骨骼驱动）。
    ///
    /// 做什么：
    /// 1. 给 FBX 设好循环标记：Walk / TailWag 循环，Jump / HeadTilt 单次；
    ///    同时把根位移 XZ 与根旋转锁进 pose，动画只驱动骨骼，不抢 CompanionDog 的球面跟随；
    /// 2. 生成 AnimatorController（Assets/Art/Animators/DogAnimator.controller）：
    ///    Idle_Wag(尾摆) ⇄ Walk，任意状态可被 Jump / HeadTilt 打断后回 Idle；
    /// 3. 把 Animator 挂到预制体的 Model 子物体上（骨骼路径相对 Model 根，能正确绑定）。
    ///
    /// 不做什么：不动 CompanionDog 的跟随/探路/叼回逻辑，不动体高与其它数值。
    /// 没接动画（没有 Animator 或控制器为空）时，CompanionDog 仍按原来的程序化动作跑。
    ///
    /// 菜单：Tools/垃圾星球白膜/㉕ 给小狗接骨骼动画
    /// </summary>
    public static class DogAnimatorSetup
    {
        const string FbxPath = "Assets/Art/Characters/RobotPuppy.fbx";
        const string PrefabPath = "Assets/Prefabs/Whitebox/chr_dog_whitebox.prefab";
        const string AnimatorFolder = "Assets/Art/Animators";
        const string ControllerPath = AnimatorFolder + "/DogAnimator.controller";
        const string ModelName = "Model";
        const string SessionKey = "TrashPlanet.DogAnimatorInstalled.v1";

        // FBX 里的 take 名形如 "MechaPuppy_Rig|Walk"，按关键字匹配，不写死前缀
        const string ClipWalk = "Walk";
        const string ClipWag = "TailWag";
        const string ClipJump = "Jump";
        const string ClipTilt = "HeadTilt";

        static int retry;

        [InitializeOnLoadMethod]
        static void AutoInstallOnce()
        {
            if (SessionState.GetBool(SessionKey, false)) return;
            EditorApplication.delayCall += TryAutoInstall;
        }

        static void TryAutoInstall()
        {
            if (SessionState.GetBool(SessionKey, false)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            // 刚编译完时资源可能还在导入，稍后再来：延时回调按帧重试，最多 60 帧
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                Retry();
                return;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Retry();
                return;
            }
            if (prefab.transform.Find(ModelName) == null)
            {
                // ㉔ 替换外观可能还在排队，等一下再看
                Retry();
                return;
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath) == null)
            {
                Retry();
                return;
            }

            SessionState.SetBool(SessionKey, true);
            try
            {
                Install();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DogAnimator] 自动接动画失败，可手动点菜单 ㉕：" + e.Message);
            }
        }

        static void Retry()
        {
            if (++retry > 60)
            {
                Debug.Log("[DogAnimator] 编辑器一直没就绪，先跳过自动接动画；需要时手动点菜单 ㉕ 给小狗接骨骼动画");
                SessionState.SetBool(SessionKey, true);
                return;
            }
            EditorApplication.delayCall += TryAutoInstall;
        }

        [MenuItem("Tools/垃圾星球白膜/㉕ 给小狗接骨骼动画")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[DogAnimator] 请先退出 Play 模式再接动画");
                return;
            }

            ConfigureClips();

            AnimationClip walk = FindClip(ClipWalk);
            AnimationClip wag = FindClip(ClipWag);
            AnimationClip jump = FindClip(ClipJump);
            AnimationClip tilt = FindClip(ClipTilt);
            if (walk == null || wag == null)
            {
                Debug.LogWarning("[DogAnimator] 找不到 Walk / TailWag 片段，保留程序化动作，不接骨骼动画");
                return;
            }

            AnimatorController controller = BuildController(walk, wag, jump, tilt);
            Avatar avatar = FindAvatar();
            ApplyToPrefab(controller, avatar);

            Debug.Log("[DogAnimator] 完成：控制器 " + ControllerPath
                      + "；片段 Walk=" + walk.name + " / Idle=" + wag.name
                      + (jump != null ? " / Jump=" + jump.name : " / Jump=缺")
                      + (tilt != null ? " / HeadTilt=" + tilt.name : " / HeadTilt=缺")
                      + "；Avatar=" + (avatar != null ? avatar.name : "无")
                      + "；Animator 已挂到 " + PrefabPath + " 的 " + ModelName + " 上");
        }

        /// <summary>批量入口（-executeMethod 用）。</summary>
        public static void Run()
        {
            Install();
        }

        // ---------------------------------------------------------------
        // 导入设置：循环标记 + 锁根位移
        // ---------------------------------------------------------------

        static void ConfigureClips()
        {
            ModelImporter importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
            if (importer == null) return;

            // 这条很关键：FBX 里 MechaPuppy_Rig 的 bind pose 缩放是 100（cm→m），
            // 若动画里带一条常量缩放曲线（常见是 1），播放时整只狗会被缩成 1/100。
            // 去掉常量缩放曲线后，缩放只由 bind pose 决定。
            bool changed = false;
            if (!importer.removeConstantScaleCurves)
            {
                importer.removeConstantScaleCurves = true;
                changed = true;
            }

            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0)
            {
                if (changed) importer.SaveAndReimport();
                return;
            }

            ModelImporterClipAnimation[] existing = importer.clipAnimations;
            if (existing == null || existing.Length != clips.Length) changed = true;

            for (int i = 0; i < clips.Length; i++)
            {
                string label = string.IsNullOrEmpty(clips[i].name) ? clips[i].takeName : clips[i].name;
                bool loop = IsLoopClip(label);

                if (clips[i].loopTime != loop) { clips[i].loopTime = loop; changed = true; }
                if (loop && !clips[i].loopPose) { clips[i].loopPose = true; changed = true; }

                // 动画只驱动骨骼：根位移 XZ 与根旋转烘焙进 pose，避免与球面跟随打架
                if (!clips[i].lockRootPositionXZ) { clips[i].lockRootPositionXZ = true; changed = true; }
                if (!clips[i].lockRootRotation) { clips[i].lockRootRotation = true; changed = true; }

                if (existing != null && i < existing.Length && existing[i].loopTime != clips[i].loopTime)
                    changed = true;
            }

            if (changed) importer.clipAnimations = clips;
            if (!changed) return;

            importer.SaveAndReimport();
            Debug.Log("[DogAnimator] 片段导入设置已更新：Walk/TailWag 循环，Jump/HeadTilt 单次，"
                      + "根位移已锁，常量缩放曲线已移除");
        }

        static bool IsLoopClip(string clipName)
        {
            if (string.IsNullOrEmpty(clipName)) return false;
            return clipName.IndexOf(ClipWalk, StringComparison.OrdinalIgnoreCase) >= 0
                || clipName.IndexOf(ClipWag, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ---------------------------------------------------------------
        // 资源查找
        // ---------------------------------------------------------------

        static AnimationClip FindClip(string keyword)
        {
            UnityEngine.Object[] all = AssetDatabase.LoadAllAssetsAtPath(FbxPath);
            for (int i = 0; i < all.Length; i++)
            {
                AnimationClip c = all[i] as AnimationClip;
                if (c == null) continue;
                if (c.name.StartsWith("__preview__", StringComparison.Ordinal)) continue;
                if (c.name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) return c;
            }
            return null;
        }

        static Avatar FindAvatar()
        {
            UnityEngine.Object[] all = AssetDatabase.LoadAllAssetsAtPath(FbxPath);
            for (int i = 0; i < all.Length; i++)
            {
                Avatar a = all[i] as Avatar;
                if (a != null) return a;
            }
            return null;
        }

        // ---------------------------------------------------------------
        // 控制器
        // ---------------------------------------------------------------

        static AnimatorController BuildController(AnimationClip walk, AnimationClip wag,
            AnimationClip jump, AnimationClip tilt)
        {
            if (!AssetDatabase.IsValidFolder(AnimatorFolder))
                AssetDatabase.CreateFolder("Assets/Art", "Animators");

            // 每次重建，保证状态机与代码一致（预制体里引用的是路径，不受重建影响）
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
                AssetDatabase.DeleteAsset(ControllerPath);

            AnimatorController c = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            c.AddParameter(DogAnimatorParams.Speed, AnimatorControllerParameterType.Float);
            c.AddParameter(DogAnimatorParams.Jump, AnimatorControllerParameterType.Trigger);
            c.AddParameter(DogAnimatorParams.Tilt, AnimatorControllerParameterType.Trigger);

            AnimatorStateMachine sm = c.layers[0].stateMachine;

            AnimatorState idle = sm.AddState("Idle_Wag");
            idle.motion = wag;
            sm.defaultState = idle;

            AnimatorState walkState = sm.AddState("Walk");
            walkState.motion = walk;

            // Idle → Walk：Speed 越过 0.35 才起步，避免边界抖动
            AnimatorStateTransition toWalk = idle.AddTransition(walkState);
            toWalk.hasExitTime = false;
            toWalk.hasFixedDuration = true;
            toWalk.duration = 0.15f;
            toWalk.AddCondition(AnimatorConditionMode.Greater, 0.35f, DogAnimatorParams.Speed);

            // Walk → Idle：Speed 掉到 0.15 以下才收步（带回滞）
            AnimatorStateTransition toIdle = walkState.AddTransition(idle);
            toIdle.hasExitTime = false;
            toIdle.hasFixedDuration = true;
            toIdle.duration = 0.2f;
            toIdle.AddCondition(AnimatorConditionMode.Less, 0.15f, DogAnimatorParams.Speed);

            AddOneShot(sm, "Jump", jump, DogAnimatorParams.Jump, idle);
            AddOneShot(sm, "HeadTilt", tilt, DogAnimatorParams.Tilt, idle);

            EditorUtility.SetDirty(c);
            return c;
        }

        /// <summary>插一条「任意状态 → 单次动作 → 回 Idle」的分支。</summary>
        static void AddOneShot(AnimatorStateMachine sm, string stateName, AnimationClip clip,
            string trigger, AnimatorState idle)
        {
            if (clip == null) return;

            AnimatorState state = sm.AddState(stateName);
            state.motion = clip;

            AnimatorStateTransition enter = sm.AddAnyStateTransition(state);
            enter.hasExitTime = false;
            enter.hasFixedDuration = true;
            enter.duration = 0.08f;
            enter.canTransitionToSelf = false;
            enter.AddCondition(AnimatorConditionMode.If, 0f, trigger);

            AnimatorStateTransition back = state.AddTransition(idle);
            back.hasExitTime = true;
            back.exitTime = 0.85f;             // 播到 85% 就开始过渡，收尾不硬切
            back.hasFixedDuration = true;
            back.duration = 0.1f;
        }

        // ---------------------------------------------------------------
        // 挂到预制体
        // ---------------------------------------------------------------

        static void ApplyToPrefab(AnimatorController controller, Avatar avatar)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Transform model = root.transform.Find(ModelName);
                if (model == null)
                    throw new InvalidOperationException("预制体里没有 " + ModelName + " 子物体，先执行 ㉔ 替换外观");

                Animator animator = model.GetComponent<Animator>();
                if (animator == null) animator = model.gameObject.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                if (avatar != null) animator.avatar = avatar;
                animator.applyRootMotion = false;               // 位置由球面跟随算，动画不接管根节点
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.updateMode = AnimatorUpdateMode.Normal;
                EditorUtility.SetDirty(animator);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
        }
    }
}
