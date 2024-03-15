using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace JixiAI
{
    public class Jixi
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private static Jixi instance = new Jixi();
        private string apiKey;

        // Queue for actions to be executed on the main thread
        private ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();

        private Jixi() { }

        public static Jixi Instance
        {
            get { return instance; }
        }

        // Initialize method to set the API key
        public void Initialize(string apiKey)
        {
            this.apiKey = apiKey;
        }

        public void Call<T>(string action, string jsonData, Action<T> onCompleted) where T : class
        {
            Task.Run(async () =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(apiKey))
                    {
                        httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                    }

                    var response = await httpClient.PostAsync(action, new StringContent(jsonData, Encoding.UTF8, "application/json"));
                    if (response.IsSuccessStatusCode)
                    {
                        var responseString = await response.Content.ReadAsStringAsync();
                        T responseObject = JsonUtility.FromJson<T>(responseString);
                        // Queue callback to be executed on the main thread
                        mainThreadActions.Enqueue(() => onCompleted?.Invoke(responseObject));
                    }
                    else
                    {
                        Debug.LogError($"Error: {response.ReasonPhrase}");
                        mainThreadActions.Enqueue(() => onCompleted?.Invoke(null));
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Exception: {e.Message}");
                    mainThreadActions.Enqueue(() => onCompleted?.Invoke(null));
                }
            });
        }

        public void Call<T>(string action, Action<T> onCompleted) where T : class
        {
            Call<T>(action, "{}", onCompleted);
        }
        
        // Runs all callbacks on the main Unity thread
        public void Update()
        {
            while (mainThreadActions.TryDequeue(out var action))
            {
                action.Invoke();
            }
        }
    }
}
