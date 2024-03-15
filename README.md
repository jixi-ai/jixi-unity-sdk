# Jixi Unity SDK

Jixi is an all-in-one AI development platform. This SDK lets you easily add AI features to Unity projects.


<img width="954" alt="Screenshot 2024-03-15 at 10 45 36 AM" src="https://github.com/jixi-ai/jixi-unity-sdk/assets/2688048/7a60a1bd-cf05-44de-8415-dca8b4ab36d7">


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

For a full reference on Jixi features go to: https://docs.jixi.ai

### Get API key
1. Create an account on: https://app.jixi.ai/
2. Go to `API Keys` and click `New Key`
3. Give it a name (ex `Unity Key`) and copy the value. ** This API key will only be displayed once so make sure to save it **
### Create an Action
1. Go to `Action` and click `Create new action`
2. Give your action a name
3. Click on the action. The `URL` at the top of the page is the URL that runs the action
  <img width="1697" alt="Screenshot 2024-03-15 at 11 24 25 AM" src="https://github.com/jixi-ai/jixi-unity-sdk/assets/2688048/8226219c-6f9f-4c3a-a2c9-71222f342141">

## Usage

1. Add the `Jixi AI` prefab into your root scene
2. Paste in your API Key in the inspector
3. Add your action URLS to the map. It's good practice for the name's to be the same, but it's not required

  <img width="351" alt="Screenshot 2024-03-15 at 11 31 14 AM" src="https://github.com/jixi-ai/jixi-unity-sdk/assets/2688048/86b1a0be-e4e2-4169-85ee-4baa0794f5ec">


4. Call the action
   
   ```csharp
    // This class matches the response format created in Jixi. (see the code from the screenshot above)
    public class AIResponse {
      public string answer;
    }

    // Call the Jixi action
    Jixi.Instance.Call(jixiAIConfig.GetActionUrl("My Unity AI Action"), (AIResponse response) =>
    {
        Debug.Log(response.answer);
    });

   // (Optional) playerInputJson gets passed to the action
    var playerInputJson = JsonUtility.ToJson(new PlayerInput(input));
   
    Jixi.Instance.Call(jixiAIConfig.GetActionUrl("My Unity AI Action"), playerInputJson, (AIResponse response) =>
    {
        Debug.Log(response.answer);
    });   
   ```

