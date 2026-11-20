using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TrashPlanet.Whitebox.Editor
{
    /// <summary>
    /// Replaces only the visual/collider children of the gameplay trash prefabs.
    /// TrashItem category, durability, yield and prefab references stay untouched.
    /// </summary>
    public static class TrashArtReplacer
    {
        const string PrefabRoot = "Assets/Prefabs/Whitebox/";
        const string OutlineMaterial = "Assets/Art/Materials/M_Highlight_Whitebox.mat";
        const string MetalMaterial = "Assets/Art/Materials/M_Trash_Fridge_LowPoly.mat";
        const string FridgeTexture = "Assets/Art/Garbage/T_Fridge_Damaged_LowPoly.png";
        const string CanGreenTexture = "Assets/Art/Garbage/T_Can_CitrusGreen_LowPoly.png";
        const string CanRedTexture = "Assets/Art/Garbage/T_Can_ClassicRed_LowPoly.png";
        const string RustMaterial = "Assets/Art/Materials/M_Rust_Whitebox.mat";

        struct Replacement
        {
            public string Prefab;
            public string Model;
            public float Size;
            public Vector3 Euler;
            public float NaturalTilt;
            public float GroundContactDepth;

            public Replacement(string prefab, string model, float size, Vector3 euler, float naturalTilt,
                float groundContactDepth)
            {
                Prefab = prefab;
                Model = model;
                Size = size;
                Euler = euler;
                NaturalTilt = naturalTilt;
                GroundContactDepth = groundContactDepth;
            }
        }

        static readonly Replacement[] Replacements =
        {
            // Plastic bottle -> Plastic category.
            new Replacement("prop_trash_plastic_whitebox.prefab",
                "Assets/Art/Garbage/su-liao-ping/su-liao-ping/suliao1.fbx", 0.24f,
                new Vector3(0f, 0f, 90f), 1.5f, 0.075f),

            // Dry branch -> Organic category.
            new Replacement("prop_trash_organic_whitebox.prefab",
                "Assets/Art/Garbage/tree (2)/tree/tree.fbx", 0.75f,
                new Vector3(90f, 0f, 0f), 3f, 0.045f),

            // Battery -> Electronic category.
            new Replacement("prop_trash_electronic_whitebox.prefab",
                "Assets/Art/Garbage/电池/dc/dd.fbx", 0.16f,
                new Vector3(0f, 0f, 90f), 2f, 0.04f),

            // A trash bag is predominantly plastic; this prefab is the large Plastic slot.
            new Replacement("prop_trash_large_tire_whitebox.prefab",
                "Assets/Art/Garbage/lajidaifbx/lajidai.fbx", 0.70f,
                new Vector3(0f, 0f, 0f), 4f, 0.06f),

            // Cardboard/paper is assigned to the Organic recycling stream used by this project.
            new Replacement("prop_trash_large_appliance_whitebox.prefab",
                "Assets/Art/Garbage/zhixiang/zhixiang/zhixiang3.fbx", 0.75f,
                new Vector3(0f, 0f, 0f), 0f, 0.04f),
        };

        [MenuItem("Trash Planet/Art/Replace Trash Whiteboxes")]
        public static void ReplaceTrashWhiteboxes()
        {
            Material outlineMaterial = AssetDatabase.LoadAssetAtPath<Material>(OutlineMaterial);
            if (outlineMaterial == null)
                throw new InvalidOperationException("Missing outline material: " + OutlineMaterial);

            foreach (Replacement replacement in Replacements)
                Replace(replacement, outlineMaterial);
            ReplaceLargeMetal(outlineMaterial);
            ReplaceNormalMetalCan(outlineMaterial);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[TrashArtReplacer] Replaced 7 category-safe trash visuals, including a damaged refrigerator and two unbranded can looks.");
        }

        // Batch-mode entry point.
        public static void Run()
        {
            ReplaceTrashWhiteboxes();
        }

        static void Replace(Replacement replacement, Material outlineMaterial)
        {
            string prefabPath = PrefabRoot + replacement.Prefab;
            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(replacement.Model);
            if (modelAsset == null)
                throw new InvalidOperationException("Missing model: " + replacement.Model);

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Transform oldBody = root.transform.Find("Body");
                Transform oldOutline = root.transform.Find("Outline");
                if (oldBody != null) UnityEngine.Object.DestroyImmediate(oldBody.gameObject);
                if (oldOutline != null) UnityEngine.Object.DestroyImmediate(oldOutline.gameObject);

                GameObject body = InstantiateVisual(modelAsset, root.transform, "Body", replacement);
                Bounds bodyBounds = CalculateBounds(body);
                BoxCollider collider = body.AddComponent<BoxCollider>();
                collider.center = body.transform.InverseTransformPoint(bodyBounds.center);
                collider.size = WorldSizeToLocal(body.transform, bodyBounds.size);

                GameObject outline = UnityEngine.Object.Instantiate(body, root.transform);
                outline.name = "Outline";
                UnityEngine.Object.DestroyImmediate(outline.GetComponent<BoxCollider>());
                outline.transform.localScale *= 1.067f;
                foreach (Renderer renderer in outline.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] materials = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
                    for (int i = 0; i < materials.Length; i++) materials[i] = outlineMaterial;
                    renderer.sharedMaterials = materials;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
                outline.SetActive(false);

                TrashItem item = root.GetComponent<TrashItem>();
                SerializedObject serializedItem = new SerializedObject(item);
                serializedItem.FindProperty("outline").objectReferenceValue = outline;
                serializedItem.FindProperty("naturalTiltDegrees").floatValue = replacement.NaturalTilt;
                serializedItem.FindProperty("groundContactDepth").floatValue = replacement.GroundContactDepth;
                serializedItem.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log("[TrashArtReplacer] " + replacement.Prefab + " <- " + replacement.Model);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static GameObject InstantiateVisual(GameObject asset, Transform parent, string name, Replacement replacement)
        {
            GameObject visual = UnityEngine.Object.Instantiate(asset, parent);
            visual.name = name;
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.Euler(replacement.Euler);
            visual.transform.localScale = Vector3.one;

            Bounds bounds = CalculateBounds(visual);
            float largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (largest <= 0.0001f)
                throw new InvalidOperationException("Model has no renderable bounds: " + replacement.Model);
            visual.transform.localScale = Vector3.one * (replacement.Size / largest);

            bounds = CalculateBounds(visual);
            Vector3 localCenter = parent.InverseTransformPoint(bounds.center);
            float localBottom = parent.InverseTransformPoint(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z)).y;
            visual.transform.localPosition += new Vector3(-localCenter.x, -localBottom, -localCenter.z);
            return visual;
        }

        static void ReplaceLargeMetal(Material outlineMaterial)
        {
            const string prefabName = "prop_trash_large_metal_device_whitebox.prefab";
            string prefabPath = PrefabRoot + prefabName;
            Material metal = GetOrCreateMetalMaterial();
            Material rust = AssetDatabase.LoadAssetAtPath<Material>(RustMaterial);
            if (metal == null || rust == null)
                throw new InvalidOperationException("Missing metal/rust material for large metal trash");

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Transform oldBody = root.transform.Find("Body");
                Transform oldOutline = root.transform.Find("Outline");
                if (oldBody != null) UnityEngine.Object.DestroyImmediate(oldBody.gameObject);
                if (oldOutline != null) UnityEngine.Object.DestroyImmediate(oldOutline.gameObject);

                var body = new GameObject("Body");
                body.transform.SetParent(root.transform, false);

                Material dark = GetOrCreateFlatMaterial("Assets/Art/Materials/M_Fridge_DarkParts.mat",
                    new Color(0.10f, 0.12f, 0.12f, 1f), 0.45f, 0.12f);
                Material badge = GetOrCreateFlatMaterial("Assets/Art/Materials/M_Fridge_GenericBadge.mat",
                    new Color(0.10f, 0.42f, 0.62f, 1f), 0.25f, 0.30f);

                // Real-world compact refrigerator proportions: 0.82 m wide × 1.50 m tall × 0.74 m deep.
                // The doors are deliberately misaligned so the silhouette reads as discarded rather than new.
                Part(body.transform, "FridgeCabinet", PrimitiveType.Cube,
                    new Vector3(0f, 0.75f, 0f), new Vector3(0.80f, 1.46f, 0.70f),
                    new Vector3(0f, 0f, -1.5f), metal);
                Part(body.transform, "FreezerDoor_Ajar", PrimitiveType.Cube,
                    new Vector3(0.035f, 1.20f, -0.375f), new Vector3(0.73f, 0.48f, 0.075f),
                    new Vector3(0f, -7f, 2.5f), metal);
                Part(body.transform, "MainDoor_Damaged", PrimitiveType.Cube,
                    new Vector3(-0.025f, 0.57f, -0.385f), new Vector3(0.73f, 0.72f, 0.075f),
                    new Vector3(0f, 11f, -2f), metal);
                Part(body.transform, "DoorGap", PrimitiveType.Cube,
                    new Vector3(0f, 0.91f, -0.423f), new Vector3(0.70f, 0.035f, 0.025f),
                    Vector3.zero, dark);
                Part(body.transform, "FreezerHandle", PrimitiveType.Cube,
                    new Vector3(0.27f, 1.15f, -0.445f), new Vector3(0.055f, 0.28f, 0.055f),
                    new Vector3(0f, -7f, 2.5f), dark);
                Part(body.transform, "BrokenMainHandle", PrimitiveType.Cube,
                    new Vector3(0.27f, 0.66f, -0.455f), new Vector3(0.055f, 0.23f, 0.055f),
                    new Vector3(16f, 11f, -9f), rust);
                Part(body.transform, "BentTop", PrimitiveType.Cube,
                    new Vector3(-0.08f, 1.49f, 0.02f), new Vector3(0.72f, 0.065f, 0.61f),
                    new Vector3(3f, 0f, -4f), rust);
                Part(body.transform, "BottomRust", PrimitiveType.Cube,
                    new Vector3(0.18f, 0.075f, -0.36f), new Vector3(0.38f, 0.11f, 0.035f),
                    new Vector3(0f, 4f, -3f), rust);

                // Generic three-square badge: no text, trademark, brand typeface or protected logo geometry.
                for (int i = 0; i < 3; i++)
                    Part(body.transform, "GenericBadge_" + i, PrimitiveType.Cube,
                        new Vector3(-0.08f + i * 0.08f, 1.30f + (i == 1 ? 0.025f : 0f), -0.426f),
                        new Vector3(0.045f, 0.045f, 0.014f), Vector3.zero, badge);

                // Exposed rear condenser: simple dark bars keep the polygon count low and read clearly at distance.
                for (int i = 0; i < 4; i++)
                    Part(body.transform, "RearCoil_" + i, PrimitiveType.Cube,
                        new Vector3(0f, 0.39f + i * 0.24f, 0.371f), new Vector3(0.57f, 0.035f, 0.035f),
                        new Vector3(0f, 0f, i % 2 == 0 ? 3f : -3f), dark);
                Part(body.transform, "RearCoil_Left", PrimitiveType.Cube,
                    new Vector3(-0.27f, 0.75f, 0.372f), new Vector3(0.035f, 0.82f, 0.035f),
                    Vector3.zero, dark);
                Part(body.transform, "RearCoil_Right", PrimitiveType.Cube,
                    new Vector3(0.27f, 0.75f, 0.372f), new Vector3(0.035f, 0.82f, 0.035f),
                    Vector3.zero, dark);

                BoxCollider collider = body.AddComponent<BoxCollider>();
                collider.center = new Vector3(0f, 0.75f, 0f);
                collider.size = new Vector3(0.88f, 1.50f, 0.82f);

                GameObject outline = UnityEngine.Object.Instantiate(body, root.transform);
                outline.name = "Outline";
                foreach (Collider c in outline.GetComponentsInChildren<Collider>(true))
                    UnityEngine.Object.DestroyImmediate(c);
                outline.transform.localScale = Vector3.one * 1.045f;
                foreach (Renderer renderer in outline.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterial = outlineMaterial;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
                outline.SetActive(false);

                TrashItem item = root.GetComponent<TrashItem>();
                SerializedObject serializedItem = new SerializedObject(item);
                serializedItem.FindProperty("outline").objectReferenceValue = outline;
                serializedItem.FindProperty("naturalTiltDegrees").floatValue = 0.5f;
                serializedItem.FindProperty("groundContactDepth").floatValue = 0.045f;
                serializedItem.FindProperty("randomlyLayOnSide").boolValue = true;
                serializedItem.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log("[TrashArtReplacer] " + prefabName + " <- textured low-poly damaged refrigerator");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void ReplaceNormalMetalCan(Material outlineMaterial)
        {
            const string prefabName = "prop_trash_metal_whitebox.prefab";
            string prefabPath = PrefabRoot + prefabName;
            Material green = GetOrCreateCanMaterial("Assets/Art/Materials/M_Can_CitrusGreen.mat",
                CanGreenTexture, new Color(0.08f, 0.48f, 0.20f, 1f), new Color(0.78f, 0.95f, 0.22f, 1f));
            Material red = GetOrCreateCanMaterial("Assets/Art/Materials/M_Can_ClassicRed.mat",
                CanRedTexture, new Color(0.68f, 0.045f, 0.035f, 1f), new Color(0.96f, 0.80f, 0.66f, 1f));
            Material aluminum = GetOrCreateFlatMaterial("Assets/Art/Materials/M_Can_Aluminum.mat",
                new Color(0.64f, 0.69f, 0.70f, 1f), 0.82f, 0.36f);

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Transform oldBody = root.transform.Find("Body");
                Transform oldOutline = root.transform.Find("Outline");
                if (oldBody != null) UnityEngine.Object.DestroyImmediate(oldBody.gameObject);
                if (oldOutline != null) UnityEngine.Object.DestroyImmediate(oldOutline.gameObject);

                var body = new GameObject("Body");
                body.transform.SetParent(root.transform, false);
                // Gameplay-readable oversized can: previous 13 cm length ×3, previous 9 cm diameter ×3.5.
                const float canRadius = 0.1575f; // 31.5 cm diameter
                const float canHalfLength = 0.195f; // 39 cm total length
                const float canCenterY = 0.16f;
                GameObject shell = Part(body.transform, "CanShell", PrimitiveType.Cylinder,
                    new Vector3(0f, canCenterY, 0f), new Vector3(canRadius, canHalfLength, canRadius),
                    new Vector3(0f, 0f, 90f), green);
                Part(body.transform, "CanTop", PrimitiveType.Cylinder,
                    new Vector3(0.199f, canCenterY, 0f), new Vector3(0.162f, 0.006f, 0.162f),
                    new Vector3(0f, 0f, 90f), aluminum);
                Part(body.transform, "CanBottom", PrimitiveType.Cylinder,
                    new Vector3(-0.199f, canCenterY, 0f), new Vector3(0.162f, 0.006f, 0.162f),
                    new Vector3(0f, 0f, 90f), aluminum);
                Part(body.transform, "PullTab", PrimitiveType.Cube,
                    new Vector3(0.207f, 0.225f, 0f), new Vector3(0.018f, 0.055f, 0.026f),
                    new Vector3(0f, 0f, 18f), aluminum);

                BoxCollider collider = body.AddComponent<BoxCollider>();
                collider.center = new Vector3(0f, canCenterY, 0f);
                collider.size = new Vector3(0.41f, 0.325f, 0.325f);

                GameObject outline = UnityEngine.Object.Instantiate(body, root.transform);
                outline.name = "Outline";
                foreach (Collider c in outline.GetComponentsInChildren<Collider>(true))
                    UnityEngine.Object.DestroyImmediate(c);
                outline.transform.localScale = Vector3.one * 1.055f;
                foreach (Renderer renderer in outline.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterial = outlineMaterial;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
                outline.SetActive(false);

                TrashItem item = root.GetComponent<TrashItem>();
                SerializedObject serializedItem = new SerializedObject(item);
                serializedItem.FindProperty("outline").objectReferenceValue = outline;
                serializedItem.FindProperty("naturalTiltDegrees").floatValue = 2f;
                serializedItem.FindProperty("groundContactDepth").floatValue = 0.015f;
                serializedItem.FindProperty("randomlyLayOnSide").boolValue = false;
                SetObjectArray(serializedItem.FindProperty("variantRenderers"), new UnityEngine.Object[] { shell.GetComponent<Renderer>() });
                SetObjectArray(serializedItem.FindProperty("visualMaterials"), new UnityEngine.Object[] { green, red });
                serializedItem.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log("[TrashArtReplacer] " + prefabName + " <- unbranded low-poly citrus/cola can variants");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void SetObjectArray(SerializedProperty property, UnityEngine.Object[] values)
        {
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        static GameObject Part(Transform parent, string name, PrimitiveType type, Vector3 position,
            Vector3 scale, Vector3 euler, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        static Material GetOrCreateMetalMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MetalMaterial);
            Texture2D texture = GetOrCreateFridgeTexture();
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                material = new Material(shader) { name = "M_Trash_Fridge_LowPoly" };
                AssetDatabase.CreateAsset(material, MetalMaterial);
            }

            Color dirtyEnamel = new Color(0.82f, 0.81f, 0.72f, 1f);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", dirtyEnamel);
            if (material.HasProperty("_Color")) material.SetColor("_Color", dirtyEnamel);
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.28f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.16f);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material GetOrCreateFlatMaterial(string path, Color color, float metallic, float smoothness)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material GetOrCreateCanMaterial(string materialPath, string texturePath, Color baseColor, Color accent)
        {
            Texture2D texture = GetOrCreateCanTexture(texturePath, baseColor, accent);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(materialPath) };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.62f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.30f);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Texture2D GetOrCreateCanTexture(string path, Color baseColor, Color accent)
        {
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
            Color dark = Color.Lerp(baseColor, Color.black, 0.32f);
            Color silver = new Color(0.72f, 0.76f, 0.75f, 1f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / 127f;
                float v = y / 127f;
                float wave = 0.52f + Mathf.Sin(u * Mathf.PI * 3.2f + v * Mathf.PI * 1.4f) * 0.12f;
                float band = Mathf.SmoothStep(0.035f, 0f, Mathf.Abs(v - wave));
                float secondBand = Mathf.SmoothStep(0.018f, 0f,
                    Mathf.Abs(v - (0.25f + Mathf.Sin(u * Mathf.PI * 2.1f) * 0.055f)));
                float dent = Mathf.PerlinNoise(x * 0.15f + 4f, y * 0.12f + 9f);
                Color c = Color.Lerp(baseColor, accent, band * 0.82f);
                c = Color.Lerp(c, silver, secondBand * 0.55f);
                c = Color.Lerp(c, dark, Mathf.Clamp01((dent - 0.66f) * 1.5f) * 0.24f);
                // Thin metallic rings at top and bottom; no text or brand-shaped graphics.
                if (y < 5 || y > 122) c = Color.Lerp(c, silver, 0.72f);
                texture.SetPixel(x, y, c);
            }
            texture.Apply(false, false);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Bilinear;
                importer.maxTextureSize = size;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Texture2D GetOrCreateFridgeTexture()
        {
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(FridgeTexture);
            if (existing != null) return existing;

            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
            Color enamel = new Color(0.78f, 0.76f, 0.66f, 1f);
            Color grime = new Color(0.28f, 0.25f, 0.19f, 1f);
            Color rust = new Color(0.50f, 0.18f, 0.08f, 1f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float broad = Mathf.PerlinNoise(x * 0.055f + 3.1f, y * 0.055f + 8.7f);
                float fine = Mathf.PerlinNoise(x * 0.19f + 11f, y * 0.19f + 2f);
                float edge = Mathf.Max(Mathf.Abs(x - 63.5f), Mathf.Abs(y - 63.5f)) / 63.5f;
                float bottom = 1f - y / 127f;
                float grimeMask = Mathf.Clamp01((broad - 0.58f) * 2.2f + bottom * 0.18f);
                float rustMask = Mathf.Clamp01((fine - 0.68f) * 3.4f + edge * 0.18f + bottom * 0.28f);
                Color c = Color.Lerp(enamel, grime, grimeMask * 0.42f);
                c = Color.Lerp(c, rust, rustMask * 0.78f);
                texture.SetPixel(x, y, c);
            }
            texture.Apply(false, false);
            File.WriteAllBytes(FridgeTexture, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(FridgeTexture, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(FridgeTexture) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Bilinear;
                importer.maxTextureSize = size;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(FridgeTexture);
        }

        static Bounds CalculateBounds(GameObject visual)
        {
            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(visual.transform.position, Vector3.zero);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        static Vector3 WorldSizeToLocal(Transform transform, Vector3 worldSize)
        {
            Vector3 scale = transform.lossyScale;
            return new Vector3(
                worldSize.x / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
                worldSize.y / Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
                worldSize.z / Mathf.Max(0.0001f, Mathf.Abs(scale.z)));
        }
    }
}
