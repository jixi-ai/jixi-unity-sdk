using UnityEngine;
using System.Collections.Generic;

namespace JixiAI
{
    [System.Serializable]
    public class JixiAction
    {
        public string name;
        public string url;
    }

    public class JixiAIConfig : MonoBehaviour
    {
        public static JixiAIConfig Instance { get; private set; }

        [Header("Authorization")]
        public string apiKey;

        [Header("Action Map")]
        public List<JixiAction> actions = new ();
        
        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject); // Make this instance persistent across scenes
                
                // Initialize Jixi with apiKey
                Jixi.Instance.Initialize(apiKey);
            }
            else if (Instance != this)
            {
                Destroy(gameObject); // Destroy any duplicate instances
            }
        }

        // Finds an action by its name and returns its URL
        public string GetActionUrl(string name)
        {
            foreach (var action in actions)
            {
                if (action.name == name)
                    return action.url;
            }
            Debug.LogWarning($"No action ${name} found in config");
            return null;
        }
        
        void Update()
        {
            Jixi.Instance.Update();
        }
    }
}
