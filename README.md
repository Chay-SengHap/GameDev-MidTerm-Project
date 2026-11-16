# Delivery Rush

> Pick up parcels, navigate city traffic, and deliver all three packages before time runs out!

Delivery Rush is a 3D delivery game developed in Unity for our Game Development mid-term project. Players ride a scooter through a low-poly city, avoid obstacles, and deliver parcels to customers.

## Game Objective

Complete **three deliveries on time** to win.

Each delivery has its own countdown. If the timer reaches zero before you hand the parcel to the customer, you lose and can restart the mission.

## Quick Link

| Deliverable | Link |
|---|---|
|  Gameplay video | Here:  |
|  Playable build (ZIP) |Here:  |
|Git repository |Here: |

---

## How to Play

1. **Pick up a parcel.** Walk to the parcel shop and press **E** near the parcel.
2. **Load the scooter.** Press **E** beside the scooter to put the parcel in the cargo box. The timer starts when the cargo lid closes.
3. **Ride to the customer.** Follow the navigation arrow, the route on the minimap, and the blue destination beam.
4. **Avoid obstacles.** Navigate around traffic, pedestrians, and other objects on the road.
5. **Park at the destination.** Stop the scooter inside the marked delivery zone.
6. **Deliver the parcel.** Press **E** to get off the scooter, take the parcel, and press **E** near the customer to hand it over.
7. **Collect the next parcel.** After the first and second deliveries, return to the parcel shop.
8. **Win the game.** Successfully deliver the third parcel before its timer runs out.

The HUD shows your delivery progress, such as **1/3 delivered**. Destinations are selected randomly and do not repeat back to back.

### Winning and Losing

- **Win:** Deliver all three parcels before their individual timers run out.
- **Lose:** Let the timer reach zero before handing over a parcel.
- **Retry:** Press **R** or select **Retry** on the result screen. The mission restarts from **0/3 deliveries**.

### Rewards

You earn stars and cash based on how much time remains when you complete a delivery. Your cash balance is saved between sessions.

| Time remaining | Stars | Cash reward |
|---|---:|---:|
| 30 seconds or more | 5 | 500 |
| 20 seconds or more | 4 | 400 |
| 10 seconds or more | 3 | 300 |
| 5 seconds or more | 2 | 200 |
| Under 5 seconds | 1 | 100 |

## Controls

### Scooter

| Action | Key |
|---|---|
| Throttle | W / Up Arrow / Shift |
| Brake / reverse | S / Down Arrow |
| Steer | A / D / Left Arrow / Right Arrow |
| Rear brake | Space |
| Get on / off, load or hand over parcel | E |
| Horn | H |
| Headlight | L |
| Set scooter upright | F |
| Wheelie | Hold Q |

### On Foot

| Action | Key |
|---|---|
| Walk | W / A / S / D |
| Run | Shift |
| Jump | Space |
| Crouch | C to toggle / Ctrl to hold |
| Look around | Mouse |
| Pick up or hand over parcel | E |

### Menus

| Action | Key |
|---|---|
| Pause | Esc / Start |
| Retry after winning or losing | R |

## Game Features

- **Three-delivery mission** with a progress counter, win condition, and failure condition.
- **Timed deliveries** with parcel pickup, scooter loading, and customer handover.
- **Rewards** based on delivery speed.
- **Navigation guide** with an arrow, destination beam, and minimap route.
- **Physics-based scooter** with steering, leaning, braking, wheelies, and collisions.
- **City traffic** with cars that follow roads and respond to intersections.
- **Pedestrians** walking and sitting around the city.
- **Menus** for playing, pausing, restarting, viewing controls, and quitting.

## Gameplay Video

Watch our gameplay recording here:

**[Gameplay Video](<ADD_GAMEPLAY_VIDEO_LINK>)**

The video demonstrates the controls, obstacles, physics interactions, winning or losing, and restarting the game.

## Playable Build

Download the standalone game here:

**[Download Delivery Rush](<ADD_BUILD_DOWNLOAD_LINK>)**

### How to Run the Build

1. Download the ZIP file from the link above.
2. Extract the **entire ZIP file**.
3. Open the extracted folder.
4. Run the game executable.
5. Select **Play** from the main menu.

Keep the executable and all other extracted files and folders together. The game can run without opening Unity.

**Platform:** `<Windows or macOS>`

## Getting Started in Unity

### Requirements

- **Unity 6000.3.8f1** (Unity 6.3).
- Unity Hub.
- Universal Render Pipeline 17.3, Input System 1.19, and the other packages listed in `Packages/manifest.json`.

Unity installs the required packages when the project is opened.

### Run the Project

1. Clone the repository:

   ```bash
   git clone <REPOSITORY_URL>
   cd GameDev-MidTerm-Project
   ```

2. Open Unity Hub and select **Add > Add project from disk**.
3. Select the project folder and open it with Unity `6000.3.8f1`.
4. Wait for Unity to finish importing the assets.
5. Open `Assets/Scenes/CityScence.unity`.
6. Press **Play** in the Unity Editor.
7. Select **Play** from the main menu.

### Build the Game

1. Open **File > Build Profiles**.
2. Open the **Scene List**.
3. Make sure `CityScence` is enabled and is the first scene, or disable `SampleScene`.
4. Select your target platform and build the game.
5. Test the build and include all generated files when creating the ZIP archive.

## Project Structure

```text
Assets/
  Scenes/            Main game scene and sample scene
  Scripts/
    Gameplay/        Deliveries, parcels, timer, pedestrians, and game rules
    Navigation/      Road pathfinding and navigation
    Traffic/         Traffic cars, intersections, and signals
    Vehicle/         Scooter movement, input, camera, collisions, and sounds
    UI/              Menus, delivery HUD, and minimap
    Editor/          Editor tools for scene and character setup
  Prefabs/           Game object prefabs
  Resources/         Runtime resources
  Settings/          Render pipeline assets and settings
ProjectSettings/     Unity project settings
Packages/            Unity package manifest
```

## Team and Contributions

- **Chay Senghap**
- **Ly Srongchhay**
- **Sor Channorakpitou**
- **Yann Sokmeng**

## Assets and References

The following third-party assets were used in this project:

- [SimplePoly City - Low Poly Assets](https://assetstore.unity.com/packages/3d/environments/simplepoly-city-low-poly-assets-58899) by [VenCreations](https://assetstore.unity.com/publishers/19573) — city buildings, roads, vehicles, and props.
- [Casual Character Collection 3](https://assetstore.unity.com/packages/3d/characters/humanoids/fantasy/casual-character-collection-3-399046) by [Hodaart](https://assetstore.unity.com/publishers/136210) — pedestrian characters and animations.
- [Low Poly Character Collection 3](https://assetstore.unity.com/packages/3d/characters/humanoids/low-poly-character-collection-3-388758) by [Hodaart](https://assetstore.unity.com/publishers/136210) — characters and animations.
- Boxes by [PolyOne Studio](https://assetstore.unity.com/publishers/105194) — parcel and box models.

