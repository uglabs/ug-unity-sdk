# Welcome to UG-Unity-SDK!

Please follow [docs.uglabs.io](https://docs.uglabs.io/) for more information.

## To add the SDK to your project:
1. Open Unity -> Window -> Package Manager -> Add package from git url..
2. Paste this repository's url: **https://github.com/uglabs/ug-unity-sdk.git**

## Setup
1. Go to **Tools → UG Labs → Settings → Create settings file**
2. A settings file will be created under `Assets/Resources/UGSDKSettings.asset`
3. Fill in the following fields:
   * **Host:** For the Host URL, use: [https://pug.stg.uglabs.app](https://pug.stg.uglabs.app/)
   * **API Key:** To get a service account API key, go to [https://pug-playground.stg.uglabs.app/service-accounts](https://pug-playground.stg.uglabs.app/service-accounts) → click "Create service account" → enter its name → copy the key
   * **Federated Player ID:** To get a player ID, go to [https://pug-playground.stg.uglabs.app/players](https://pug-playground.stg.uglabs.app/players) → click "Create player" → enter the player's external ID → copy the player's federated ID
   * **Team Name:** You can leave Team Name empty unless it was created from the API and a specific team name was used
4. Done! You should be able to run conversations - check a basic example from the UGSDK package in Unity Package Manager, or try running a [demo project](https://github.com/uglabs/ug-demos-unity). Full documentation is available at [https://docs.uglabs.io/sdk/unity/](https://docs.uglabs.io/sdk/unity/)
