using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace MindAttic.Chain.EditorTools
{
    /// <summary>
    /// UNI-T38: the project metadata RECONSTRUCTION.md's "Project settings" row found missing (1600x900
    /// resizable, no icon, applicationIdentifier empty) -- set in code rather than clicked together in Player
    /// Settings, so a fresh clone (or CI) reaches the same state and nothing depends on remembering to tick a
    /// box. Idempotent: run again after moving or replacing the icon art, from the menu or batch mode via
    /// <c>-executeMethod MindAttic.Chain.EditorTools.ProjectSettingsBuilder.Apply</c>. The window's 4:3 lock
    /// while it is being resized is <see cref="WindowAspectLockBehaviour"/>'s job at runtime, not a Player
    /// Setting; this only sets the 640x480 starting shape (the original's client size, Form1.vb).
    /// </summary>
    public static class ProjectSettingsBuilder
    {
        private const string IconPath = "Assets/Unity.Rea.Chain/Art/Icon/battle.png";

        // The original's and the port's own: https://github.com's reverse-DNS-style identifier for a product
        // named "Unity.Rea.Chain" by "MindAttic" (UnityRonin, the closest sibling to set one, follows the same
        // lowercased-and-despaced scheme).
        private const string ApplicationIdentifier = "com.mindattic.unityreachain";

        private const string DefaultBundleVersion = "0.1.0";

        [MenuItem("MindAttic/Chain/Apply Project Settings")]
        public static void Apply()
        {
            PlayerSettings.companyName = "MindAttic";
            PlayerSettings.productName = "Unity.Rea.Chain";

            // Form1.vb: Me.ClientSize = New Size(640, 480); windowed (not maximized, not fullscreen), still
            // resizable so WindowAspectLockBehaviour has something to correct and UNI-T59's picker still works.
            PlayerSettings.defaultScreenWidth = 640;
            PlayerSettings.defaultScreenHeight = 480;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;

            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, ApplicationIdentifier);

            // A fresh Unity project always has some non-empty bundleVersion of its own, so guarding this on
            // "is it empty" never ran: set it outright, so the version really is this ticket's, not whatever
            // the template happened to start with.
            PlayerSettings.bundleVersion = DefaultBundleVersion;

            ApplyIcon();
            NameBackdropLayer();

            AssetDatabase.SaveAssets();
            Debug.Log($"ProjectSettingsBuilder: window (640x480 windowed, resizable), companyName/productName, " +
                       $"applicationIdentifier ({ApplicationIdentifier}), version ({PlayerSettings.bundleVersion}) " +
                       $"and the Standalone icon ({IconPath}) applied.");
        }

        /// <summary>Names <see cref="AreaBackdrop.Layer"/> "Backdrop": the layer a paused frame shows alone (U.CHN-T78).</summary>
        private static void NameBackdropLayer()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0)
            {
                return;
            }

            var tagManager = new SerializedObject(assets[0]);
            var layers = tagManager.FindProperty("layers");
            if (layers == null || layers.arraySize <= AreaBackdrop.Layer)
            {
                return;
            }

            var layer = layers.GetArrayElementAtIndex(AreaBackdrop.Layer);
            if (layer.stringValue != "Backdrop")
            {
                layer.stringValue = "Backdrop";
                tagManager.ApplyModifiedProperties();
            }
        }

        private static void ApplyIcon()
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(IconPath);
            if (importer == null)
            {
                Debug.LogWarning($"ProjectSettingsBuilder: {IconPath} not found; the window icon was not set.");
                return;
            }

            // battle.ico (RECONSTRUCTION.md 3.10: "window icon") is a pixel-art icon with a hard alpha mask;
            // keep it crisp and readable so Unity can bake it into the player's icon resource.
            if (importer.textureType != TextureImporterType.Default ||
                !importer.isReadable ||
                importer.textureCompression != TextureImporterCompression.Uncompressed ||
                importer.filterMode != FilterMode.Point ||
                importer.mipmapEnabled ||
                importer.alphaIsTransparency != true)
            {
                importer.textureType = TextureImporterType.Default;
                importer.isReadable = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }

            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon == null)
            {
                Debug.LogWarning($"ProjectSettingsBuilder: {IconPath} did not import as a Texture2D; the window icon was not set.");
                return;
            }

            var sizes = PlayerSettings.GetIconSizes(NamedBuildTarget.Standalone, IconKind.Any);
            var icons = new Texture2D[sizes.Length];
            for (var i = 0; i < icons.Length; i++)
            {
                icons[i] = icon;
            }

            PlayerSettings.SetIcons(NamedBuildTarget.Standalone, icons, IconKind.Any);
        }
    }
}
