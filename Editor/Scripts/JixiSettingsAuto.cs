#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace JixiAI.Editor
{
    // Runs whenever the editor reloads scripts / domain
    [InitializeOnLoad]
    public static class JixiSettingsAuto
    {
        static JixiSettingsAuto()
        {
            EnsureSettingsAsset();
        }

        /// <summary>
        /// Guarantees a valid JixiSettings asset at Assets/Resources/JixiSettings.asset.
        /// If an old asset exists with a missing script binding, migrates its data.
        /// </summary>
        public static JixiSettings EnsureSettingsAsset()
        {
            // Check Resources folder exists
            var resourcesPath = "Assets/Resources";
            if (!AssetDatabase.IsValidFolder(resourcesPath))
            {
                Directory.CreateDirectory(resourcesPath);
                AssetDatabase.Refresh();
            }

            var targetPath = "Assets/Resources/JixiSettings.asset";

            // Try load a valid settings asset
            var loaded = Resources.Load<JixiSettings>(JixiSettings.DefaultResourcesPath);
            if (loaded != null)
            {
                // Already valid & bound
                return loaded;
            }

            // f not found/valid, search for any file named JixiSettings.asset anywhere
            var guids = AssetDatabase.FindAssets("JixiSettings t:Object");
            string brokenPath = null;
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(path).Equals("JixiSettings.asset", StringComparison.OrdinalIgnoreCase))
                {
                    // Try to load as JixiSettings
                    var maybe = AssetDatabase.LoadAssetAtPath<JixiSettings>(path);
                    if (maybe != null)
                    {
                        // We found a valid one outside Resources → move it to Resources
                        if (!path.Equals(targetPath, StringComparison.Ordinal))
                        {
                            AssetDatabase.MoveAsset(path, targetPath);
                            AssetDatabase.SaveAssets();
                            Debug.Log("[Jixi] Moved JixiSettings.asset into Resources.");
                        }
                        return Resources.Load<JixiSettings>(JixiSettings.DefaultResourcesPath);
                    }
                    else
                    {
                        brokenPath = path;
                    }
                }
            }

            // If we have a broken asset, try to migrate data by parsing YAML
            string apiKey = "";
            var workflows = new List<JixiWorkflow>();
            if (!string.IsNullOrEmpty(brokenPath) && File.Exists(brokenPath))
            {
                try
                {
                    var yaml = File.ReadAllText(brokenPath);

                    // apiKey: <value>
                    var apiMatch = Regex.Match(yaml, @"^\s*apiKey:\s*(.+)\s*$", RegexOptions.Multiline);
                    if (apiMatch.Success) apiKey = apiMatch.Groups[1].Value.Trim();

                    // workflows:
                    //   - name: ...
                    //     url: ...
                    var wfBlock = Regex.Matches(yaml, @"-\s*name:\s*(.+?)\s*\r?\n\s*url:\s*(.+?)\s*(\r?\n|$)", RegexOptions.Multiline);
                    foreach (Match m in wfBlock)
                    {
                        var name = m.Groups[1].Value.Trim();
                        var url  = m.Groups[2].Value.Trim();
                        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(url))
                            workflows.Add(new JixiWorkflow { name = name, url = url });
                    }

                    Debug.Log($"[Jixi] Migrated JixiSettings from broken asset. apiKey? {(string.IsNullOrEmpty(apiKey) ? "NO" : "YES")}, workflows={workflows.Count}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Jixi] Failed to parse old JixiSettings YAML for migration: " + e.Message);
                }
            }

            // Create a fresh, correctly bound asset in Resources and seed it with migrated data (if any)
            var fresh = ScriptableObject.CreateInstance<JixiSettings>();
            fresh.apiKey = apiKey;
            fresh.workflows = workflows;

            AssetDatabase.CreateAsset(fresh, targetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[Jixi] Created JixiSettings at Assets/Resources/JixiSettings.asset");

            return Resources.Load<JixiSettings>(JixiSettings.DefaultResourcesPath);
        }
    }
}
#endif