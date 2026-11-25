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
    /// 动画来源：Assets/Art/Characters/DogAnims/ 下四个单独导出的动作文件
    ///   RobotPuppy_Walk.fbx / RobotPuppy_TailWag.fbx / RobotPuppy_HeadTilt.fbx / RobotPuppy_Jump.fbx
    /// 每个文件只含一条 take，片段名与文件名后缀一致；若某个文件缺失，自动回退到
    /// RobotPuppy.fbx 里内嵌的同名 take，保证缺一个也不影响其它动作可用。
    ///
    /// 做什么：
    /// 1. 给动作 FBX 设导入参数：Generic、只导动画（关相机/灯光/可见性/形态键/材质）、
    ///    移除常量缩放曲线（关键：MechaPuppy_Rig bind pose 缩放是 100，否则狗会被缩成 1/100）；
    /// 2. 片段标记：Walk / TailWag 循环，Jump / HeadTilt 单次；根位移 XZ 与根旋转锁进 pose，
    ///    动画只驱动骨骼，不抢 CompanionDog 的球面跟随；
    /// 3. 生成 AnimatorController（Assets/Art/Animators/DogAnimator.controller）：
    ///    Idle_Wag(尾摆) ⇄ Walk，任意状态可被 Jump / HeadTilt 打断后回 Idle；
    /// 4. 把 Animator 挂到预制体的 Model 子物体上，模型自带 Avatar 就一并挂上（拿不到也能按
    ///    transform 路径绑定，动作照样播）。
    ///
    /// 不做什么：不动 CompanionDog 的跟随/探路/叼回逻辑，不动体高与其它数值。
    /// 没接动画（没有 Animator 或控制器为空）时，CompanionDog 仍按原来的程序化动作跑。
    ///
    /// 菜单：Tools/垃圾星球白膜/㉕ 给小狗接骨骼动画
    /// </summary>
    public static class DogAnimatorSetup
    {
        const string FbxPath = "Assets/Art/Characters/RobotPuppy.fbx";
        const string AnimFolder = "Assets/Art/Characters/DogAnims";
        const string PrefabPath = "Assets/Prefabs/Whitebox/chr_dog_whitebox.prefab";
        const string AnimatorFolder = "Assets/Art/Animators";
        const string ControllerPath = AnimatorFolder + "/DogAnimator.controller";
        const string ModelName = "Model";
        const string SessionKey = "TrashPlanet.DogAnimatorInstalled.v2";

        // take 名形如 "MechaPuppy_Rig|Walk"，按关键字匹配，不写死前缀；也是动作文件名后缀
        const string ClipWalk = "Walk";
        const string ClipWag = "TailWag";
        const string ClipJump = "Jump";
        const string ClipTilt = "HeadTilt";

        static readonly string[] ClipKeys = { ClipWalk, ClipWag, ClipJump, ClipTilt };

        static int retry;

        /// <summary>单独导出的动作文件路径（约定：RobotPuppy_&lt;关键字&gt;.fbx）。</summary>
        static string AnimFbx(string keyword) => AnimFolder + "/RobotPuppy_" + keyword + ".fbx";

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

            // 刚编译完时资源可能还在导入，稍后再来：延时回调按帧重试
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
            // 刚复制进来的动作 FBX 可能还在导入：等导完再接线，否则会退回内嵌片段
            if (!AnimFilesReady())
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
            if (++retry > 300)
            {
                Debug.Log("[DogAnimator] 编辑器一直没就绪，先跳过自动接动画；需要时手动点菜单 ㉕ 给小狗接骨骼动画");
                SessionState.SetBool(SessionKey, true);
                return;
            }
            EditorApplication.delayCall += TryAutoInstall;
        }

        /// <summary>动作 FBX 是否都已就绪（磁盘上没有的按「缺失」处理，走内嵌片段回退）。</summary>
        static bool AnimFilesReady()
        {
            for (int i = 0; i < ClipKeys.Length; i++)
            {
                string path = AnimFbx(ClipKeys[i]);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) continue;
                if (!FileOnDisk(path)) continue;      // 文件本身不存在 → 认为「没有这个动作」
                return false;                          // 文件在、但 Unity 还没导完
            }
            return true;
        }

        static bool FileOnDisk(string assetPath)
        {
            string projectRoot = System.IO.Directory.GetParent(Application.dataPath).FullName;
            return System.IO.File.Exists(System.IO.Path.Combine(projectRoot, assetPath));
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
            VerifyBindings(walk);
            VerifyBindings(wag);
            Avatar avatar = FindAvatar();
            ApplyToPrefab(controller, avatar);

            Debug.Log("[DogAnimator] 完成：控制器 " + ControllerPath
                      + "；Walk=" + Describe(walk) + " / Idle=" + Describe(wag)
                      + " / Jump=" + (jump != null ? Describe(jump) : "缺")
                      + " / HeadTilt=" + (tilt != null ? Describe(tilt) : "缺")
                      + "；Avatar=" + (avatar != null ? avatar.name : "无（按 transform 路径绑定）")
                      + "；Animator 已挂到 " + PrefabPath + " 的 " + ModelName + " 上");
        }

        static string Describe(AnimationClip c)
        {
            return c.name + "(" + c.length.ToString("0.00") + "s)";
        }

        /// <summary>
        /// 核对片段曲线路径在 Model 下是否存在。动作文件与模型骨架路径不一致时，
        /// 播放不会报错、但对不上的骨骼就是不动（腿不迈、尾巴不摇），提前在 Console 说清楚。
        /// </summary>
        static void VerifyBindings(AnimationClip clip)
        {
            if (clip == null) return;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Transform model = prefab != null ? prefab.transform.Find(ModelName) : null;
            if (model == null) return;

            EditorCurveBinding[] binds = AnimationUtility.GetCurveBindings(clip);
            int missing = 0;
            string sample = null;
            for (int i = 0; i < binds.Length; i++)
            {
                string path = binds[i].path;
                if (string.IsNullOrEmpty(path)) continue;
                if (model.Find(path) != null) continue;
                missing++;
                if (sample == null) sample = path;
            }

            if (missing > 0)
                Debug.LogWarning("[DogAnimator] " + clip.name + " 有 " + missing + "/" + binds.Length
                                 + " 条曲线在 " + ModelName + " 下找不到对应节点（例：" + sample
                                 + "）——动作文件与模型骨架路径不一致，这些骨骼不会动");
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
            // 模型本体：还要当「动作文件缺失」时的回退来源，导入动画但不动外观相关设置
            ModelImporter model = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
            if (model != null)
            {
                bool changed = false;
                if (!model.importAnimation) { model.importAnimation = true; changed = true; }
                if (model.animationType != ModelImporterAnimationType.Generic)
                {
                    model.animationType = ModelImporterAnimationType.Generic;
                    changed = true;
                }
                if (!model.removeConstantScaleCurves) { model.removeConstantScaleCurves = true; changed = true; }
                if (ApplyClipFlags(model)) changed = true;

                if (changed)
                {
                    model.SaveAndReimport();
                    Debug.Log("[DogAnimator] 模型包含的片段已打上循环/锁根标记：" + FbxPath);
                }
            }

            for (int i = 0; i < ClipKeys.Length; i++)
                ConfigureClipFbx(AnimFbx(ClipKeys[i]));
        }

        /// <summary>动作 FBX：只导动画，尽量不产生多余的网格/材质负担。</summary>
        static void ConfigureClipFbx(string path)
        {
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) return;   // 文件不存在或还没导入：交给回退逻辑

            bool changed = false;

            if (importer.animationType != ModelImporterAnimationType.Generic)
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                changed = true;
            }
            if (!importer.importAnimation) { importer.importAnimation = true; changed = true; }
            if (importer.importCameras) { importer.importCameras = false; changed = true; }
            if (importer.importLights) { importer.importLights = false; changed = true; }
            if (importer.importVisibility) { importer.importVisibility = false; changed = true; }
            if (importer.importBlendShapes) { importer.importBlendShapes = false; changed = true; }

            // 动作文件里自带一套网格/20 个材质槽，工程里只用动画，别跟着导入材质
            if (importer.materialImportMode != ModelImporterMaterialImportMode.None)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                changed = true;
            }

            // 关键：bind pose 缩放 100（cm→m），常量缩放曲线会让整只狗缩成 1/100
            if (!importer.removeConstantScaleCurves)
            {
                importer.removeConstantScaleCurves = true;
                changed = true;
            }

            if (ApplyClipFlags(importer)) changed = true;

            if (changed)
            {
                importer.SaveAndReimport();
                Debug.Log("[DogAnimator] 动作文件导入设置已更新：" + path);
            }
        }

        /// <summary>给 FBX 里的每条 take 打循环/锁根标记，返回是否与已存设置不同。</summary>
        static bool ApplyClipFlags(ModelImporter importer)
        {
            ModelImporterClipAnimation[] want = importer.defaultClipAnimations;
            if (want == null || want.Length == 0) return false;

            ModelImporterClipAnimation[] have = importer.clipAnimations;
            bool changed = have == null || have.Length != want.Length;

            for (int i = 0; i < want.Length; i++)
            {
                string label = string.IsNullOrEmpty(want[i].name) ? want[i].takeName : want[i].name;
                bool loop = IsLoopClip(label);

                want[i].loopTime = loop;
                want[i].loopPose = loop;
                // 动画只驱动骨骼：根位移 XZ 与根旋转烘焙进 pose，避免与球面跟随打架
                want[i].lockRootPositionXZ = true;
                want[i].lockRootRotation = true;

                if (!changed && i < have.Length)
                {
                    if (have[i].loopTime != loop || have[i].loopPose != loop
                        || !have[i].lockRootPositionXZ || !have[i].lockRootRotation)
                        changed = true;
                }
            }

            if (changed) importer.clipAnimations = want;
            return changed;
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

        /// <summary>优先取单独导出的动作文件，缺失时回退到 RobotPuppy.fbx 内嵌 take。</summary>
        static AnimationClip FindClip(string keyword)
        {
            AnimationClip clip = LoadClip(AnimFbx(keyword), keyword);
            if (clip != null && clip.length > 0.0001f) return clip;
            return LoadClip(FbxPath, keyword);
        }

        static AnimationClip LoadClip(string path, string keyword)
        {
            UnityEngine.Object[] all = AssetDatabase.LoadAllAssetsAtPath(path);
            if (all == null) return null;
            for (int i = 0; i < all.Length; i++)
            {
                AnimationClip c = all[i] as AnimationClip;
                if (c == null) continue;
                if (c.name.StartsWith("__preview__", StringComparison.Ordinal)) continue;
                if (c.name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) return c;
            }
            return null;
        }

        /// <summary>
        /// 只用模型本体（RobotPuppy.fbx）的 Avatar：它和 Model 实例是同一份骨架，绑定一定成立。
        /// 动作文件里也有 Avatar，但那是另一份导入产物，挂上来反而可能触发「Avatar 不匹配」；
        /// 拿不到就留空 —— Generic 模式下没有 Avatar 也会按 transform 路径绑定，动作照样播。
        /// </summary>
        static Avatar FindAvatar()
        {
            return FindAvatarAt(FbxPath);
        }

        static Avatar FindAvatarAt(string path)
        {
            UnityEngine.Object[] all = AssetDatabase.LoadAllAssetsAtPath(path);
            if (all == null) return null;
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

            // 每次重建，保证状态机与代码一致（预制体里引用的是路径，重建后由 ApplyToPrefab 重新挂上）
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
