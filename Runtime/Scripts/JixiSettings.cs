using System;
using UnityEngine;
using System.Collections.Generic;

// Holds API key + workflows; saved as a Resources asset.
namespace JixiAI
{
    public class JixiSettings : ScriptableObject
    {
        public string apiKey;
        public List<JixiWorkflow> workflows = new();

        public Dictionary<string, string> BuildMap()
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var w in workflows)
                if (!string.IsNullOrEmpty(w.name) && !d.ContainsKey(w.name))
                    d[w.name] = w.url;
            return d;
        }

        public const string DefaultResourcesPath = "JixiSettings";

#if UNITY_EDITOR
        public static JixiSettings LoadOrCreateInResources()
        {
            var settings = Resources.Load<JixiSettings>(DefaultResourcesPath);
            if (settings != null) return settings;

            settings = ScriptableObject.CreateInstance<JixiSettings>();
            System.IO.Directory.CreateDirectory("Assets/Resources");
            UnityEditor.AssetDatabase.CreateAsset(settings, "Assets/Resources/JixiSettings.asset");
            UnityEditor.AssetDatabase.SaveAssets();
            return settings;
        }
#endif
    }
}