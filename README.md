# Jixi Unity SDK

Jixi is an all-in-one AI development platform. This SDK lets you easily add AI features to Unity projects.

<img width="954" alt="Screenshot 2024-03-15 at 10 45 36 AM" src="https://github.com/jixi-ai/jixi-unity-sdk/assets/2688048/7a60a1bd-cf05-44de-8415-dca8b4ab36d7">

## Requirements
1. Unity 2020.3+ (URP/Standard OK)

## Installation
You can install the Jixi Unity SDK through the UPM or `.unitypackage`
### Package Manager
1. Open the `Package Manager` in Unity by going to `Window -> Package Manager`
2. Click `Add package from GIT URL`
  <img width="496" alt="Screenshot 2024-03-15 at 11 06 49 AM" src="https://github.com/jixi-ai/jixi-unity-sdk/assets/2688048/258d9ab4-bad2-42b7-9ab4-67907927c518">

3. Paste the SDK URL `https://github.com/jixi-ai/jixi-unity-sdk.git`
  <img width="504" alt="Screenshot 2024-03-15 at 11 07 41 AM" src="https://github.com/jixi-ai/jixi-unity-sdk/assets/2688048/21cc153d-51d2-4c31-8763-c170c80360c8">

4. Click `Add`

### Unity Package
1. Navigate to `https://github.com/jixi-ai/jixi-unity-sdk/releases/`
2. Download the `.unitypackage` file from the latest release
3. Drag the `.unitypackage` file into the Unity project window
4. Click `Import`
  <img width="350" alt="Screenshot 2024-03-15 at 11 13 34 AM" src="https://github.com/jixi-ai/jixi-unity-sdk/assets/2688048/41e83200-6bb9-4901-a676-7fa44b54f435">

## Setup Jixi

### Get API key
1. Create an account on: https://app.jixi.ai/
2. Go to `API Keys` and click `New Key`
3. Give it a name (ex `Unity Key`) and copy the value. ** This API key will only be displayed once so make sure to save it **

## Unity Project Setup
1.	Open the settings menu `Menu → Jixi AI → Setup`
2.	Paste your Jixi API Key
3.	Click Fetch to pull and cache your workflows
(This creates/updates Assets/Resources/JixiSettings.asset, which stores the API key and a name→URL map for your workflows.)

# Usage
## Call a Workflow
You can call a workflow by its name

1. JSON payload only
```
using JixiAI;
using UnityEngine;
using System;

public class HelloWorld : MonoBehaviour
{
    [Serializable]
    public class HelloResponse
    {
        public string message;
    }

    [Serializable]
    public class HelloPayload
    {
        public string name;
    }

    void Start()
    {
        var payload = new HelloPayload { name = "Robb" };

        // If your workflow returns JSON, use a typed response:
        Jixi.Instance.StartWorkflow<HelloResponse>(
            "My Hello Workflow",
            payload,
            res =>
            {
                if (res == null) { Debug.LogError("Workflow failed"); return; }
                Debug.Log($"Server says: {res.message}");
            });
    }
}
```

2. Expecting a string
(e.g., direct text or a JSON with { "url": "..." }). This works for audio, images, and other media.
```
Jixi.Instance.StartWorkflow<string>(
    "Generate a 'Hero' Voice",
    new { name = "Astra" },
    urlOrText =>
    {
        if (string.IsNullOrEmpty(urlOrText)) { Debug.LogError("No result"); return; }
        Debug.Log("Audio URL: " + urlOrText);
    });
```

3. Uploading a file path (multipart/form-data)
```
Jixi.Instance.StartWorkflow<string>(
    "Image-To-Voice",
    "{\"note\":\"process this file\"}",
    filePath: Application.persistentDataPath + "/input.png",
    onCompleted: url =>
    {
        Debug.Log("Result URL: " + url);
    });
```

4. Uploading bytes (no disk IO)
```
byte[] pngBytes = /* your in-memory bytes */;
Jixi.Instance.StartWorkflowBytes<string>(
    "Image-To-Voice",
    jsonData: "{\"note\":\"process raw bytes\"}",
    fileBytes: pngBytes,
    fileName: "frame.png",
    onCompleted: url =>
    {
        Debug.Log("Result URL: " + url);
    });
```

# Useful Examples
### Load a RenderTexture from a URL
```
Jixi.Instance.LoadRenderTextureFromUrl(
    "https://example.com/image.png",
    rt =>
    {
        if (rt == null) { Debug.LogError("Failed to load RT"); return; }
        // Use the RT (apply to material, UI RawImage, etc.)
    });
```
### Sign Jixi S3 URLs
By default, media Jixi generates expires after 5 minutes. Signing a stored URL reactivates it.
```
// Single
Jixi.Instance.SignJixiS3Url(
    originalUrl: "s3://jixi-app/path/to/file.png",
    onDone: signed =>
    {
        Debug.Log("Signed URL: " + signed);
    });

// Multiple
Jixi.Instance.SignJixiS3Urls(
    new[] { "s3://jixi-app/a.png", "s3://jixi-app/b.png" },
    onDone: list =>
    {
        if (list == null) { Debug.LogError("Signing failed"); return; }
        foreach (var s in list) Debug.Log("Signed: " + s);
    },
    endpoint: "https://api.jixi.ai/sign-url",   // customize if self-hosted
    timeoutSeconds: 15,
    expiresInSeconds: 1800);
```

# Troubleshooting
1. 401 Unauthorized / empty result
* Check your API key in Resources/JixiSettings.asset. Make sure Menu → Jixi AI → Setup → Save worked.
2. “No workflow X found in settings.”
* Open Setup → Fetch. Ensure the workflow name matches exactly.
3. Callbacks never fire / threading issues
* Confirm JixiRunner.cs exists and its Update() calls Jixi.Instance.Tick(). The SDK auto-creates a hidden JixiRunner at runtime, but if you disabled object creation, make sure one exists in the scene.
4. WebGL CORS
* Allow your WebGL domain in your Jixi API CORS configuration. Use HTTPS.
5. Parsing fails with typed DTO
* Test your workflow with string first and Debug.Log the response to confirm the JSON shape. Then map to a [Serializable] DTO and switch to StartWorkflow<YourDto>.
6. File not found
* When using the file-path overload, ensure the file exists on the target platform and path.

## Minimal API Reference
```
// JSON-only payload
void StartWorkflow<T>(string name, Action<T> onCompleted);
void StartWorkflow<T>(string name, object payload, Action<T> onCompleted);
void StartWorkflow<T>(string name, string jsonData = "{}", string filePath = null, Action<T> onCompleted = null);

// Multipart (bytes, no disk)
void StartWorkflowBytes<T>(string name, string jsonData, byte[] fileBytes, string fileName, Action<T> onCompleted);

// Helpers
void LoadRenderTextureFromUrl(string url, Action<RenderTexture> onDone);
void SignJixiS3Url(string originalUrl, Action<string> onDone, string endpoint = "...", int timeoutSeconds = 15, int expiresInSeconds = 60);
void SignJixiS3Urls(IEnumerable<string> originalUrls, Action<List<string>> onDone, string endpoint = "...", int timeoutSeconds = 15, int expiresInSeconds = 1800);
```
