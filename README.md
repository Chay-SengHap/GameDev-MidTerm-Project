# Delivery Rush

> Deliver across the city. Pick up a parcel, load it on your scooter, and beat the clock through a living low-poly city full of traffic and pedestrians.

A 3D delivery game built in Unity as our Game Development mid-term project.


## Gameplay

You play a courier working out of a parcel shop. Each delivery is a race against the clock:

1. **Pick up the parcel.** Walk to the shop parcel and press **E**.
2. **Load the scooter.** Press **E** beside the scooter. When the cargo lid closes, the countdown starts and the navigation guide appears.
3. **Ride to the customer.** Follow the guide arrow, the route line on the minimap, and the blue beam that marks the destination. The clock keeps running while you mount, wait in traffic, or get off.
4. **Park in the delivery zone.** Stop the scooter inside the marked zone, press **E** to get off, then walk to the waiting customer.
5. **Hand over the parcel.** Press **E** next to the customer. The clock stops and you are paid based on the time you have left.
6. **Return to the parcel shop** to collect the next parcel. Destinations are picked at random and never repeat back to back.

If the timer reaches zero, the screen shows **Time's up**. Press **R** to retry. The scene reloads for a clean reset.

### Rewards

Faster deliveries earn more stars and more cash. Your cash balance is saved between sessions.

| Time left on the clock | Stars | Reward |
|---|---|---|
| 30 s or more | 5 | 500 |
| 20 s or more | 4 | 400 |
| 10 s or more | 3 | 300 |
| 5 s or more | 2 | 200 |
| Under 5 s | 1 | 100 |

## Controls

### Scooter

| Action | Keyboard |
|---|---|---|
| Throttle | W / Up / Shift |
| Brake / reverse | S / Down |
| Steer | A D / Left Right |
| Rear brake | Space |
| Get on / off, load and hand over parcel | E |
| Horn | H |
| Headlight | L |
| Set the scooter upright | F |
| Wheelie (hold) | Q |

### On foot

| Action | Keys |
|---|---|
| Walk | W A S D |
| Run | Shift |
| Jump | Space |
| Crouch | C (toggle) / Ctrl (hold) |
| Look around | Mouse |

### Menus

| Action | Keys |
|---|---|
| Pause menu | Esc / Start |
| Retry after time runs out | R |

## Features

- **Timed delivery missions** with a pickup, load, ride, and hand-over loop, plus a star and cash reward system.
- **Turn-by-turn navigation.** A pathfinder plans the shortest route along the city's roads and updates it as you drive. It is shown as a HUD arrow and as a route line on a rotating minimap.
- **Physics-based scooter** with wheel colliders, lean into corners, wheelies, a rear brake, and a fold-away side stand. The rider has procedural body motion.
- **AI traffic.** Cars follow the roads, keep a safe gap, obey intersections and signals, and honk when you block them.
- **City pedestrians.** Dozens of walkers populate the streets, and some sit at cafe chairs.
- **In-game menus.** Title and pause menus are built in code, with Resume, Restart, Controls, Main Menu, and Quit.
- **Cargo animations** for picking up, carrying, and stowing the parcel, and a customer who receives it.

## Getting Started

### Requirements

- **Unity 6000.3.8f1** (Unity 6.3). Install it through Unity Hub.
- Universal Render Pipeline 17.3, Input System 1.19, and the other packages listed in `Packages/manifest.json`. Unity installs them automatically when you open the project.

### Run the game

1. Clone the repository:
   ```bash
   git clone <repo-url>
   cd GameDev-MidTerm-Project
   ```
2. Open the folder in Unity Hub (**Add > Add project from disk**) using Unity `6000.3.8f1`.
3. Wait for the first import to finish. It can take a few minutes because of the large asset packs.
4. Open `Assets/Scenes/CityScence.unity`.
5. Press **Play**. The main menu appears over the city, and the game starts when you choose **Play**.

### Build a standalone player

`Assets/Scenes/SampleScene.unity` is listed first in **File > Build Profiles > Scene List**. Before building, move `CityScence` to the top of the list or untick `SampleScene`, so the game starts in the city.

## Project Structure

```
Assets/
  Scenes/            CityScence (main game scene), SampleScene
  Scripts/
    Gameplay/        Delivery missions, parcel handling, timer, pedestrians, game rules
    Navigation/      Road pathfinding, checkpoints, navigation arrow
    Traffic/         AI cars, intersections, traffic signals
    Vehicle/         Scooter physics, input, camera, collisions, sounds
    UI/              Main and pause menu, delivery HUD, minimap
    Editor/          Editor tools for scene and character setup
  Prefabs/           Scooter, courier, street lights, trees, road ends
  Resources/         Pedestrian collection
  Settings/          URP render pipeline assets and volume profiles
  Branding/          VET logo and terminal facade assets
ProjectSettings/     Unity project settings
Packages/            Package manifest
```

## Design Notes

The original plan for the timed delivery feature is in [`delivery-rush-timed-delivery-plan.md`](delivery-rush-timed-delivery-plan.md). It covers the player flow, the timer and navigation design, the implementation order, and a playtest checklist.

## Team

- [Chhay Senghap]
- [Ly Srongchhay]
- [Sor Channorakpitou]
- [Yann Sokmeng]
## Credits

Third-party assets used in this project, all from the Unity Asset Store and used under the [Standard Unity Asset Store EULA](https://unity.com/legal/as-terms):

- [SimplePoly City - Low Poly Assets](https://assetstore.unity.com/packages/3d/environments/simplepoly-city-low-poly-assets-58899) by [VenCreations](https://assetstore.unity.com/publishers/19573): city buildings, roads, vehicles, and props
- [Casual Character Collection 3](https://assetstore.unity.com/packages/3d/characters/humanoids/fantasy/casual-character-collection-3-399046) by [Hodaart](https://assetstore.unity.com/publishers/136210): pedestrian characters and animations
- [Low Poly Character Collection 3](https://assetstore.unity.com/packages/3d/characters/humanoids/low-poly-character-collection-3-388758) by [Hodaart](https://assetstore.unity.com/publishers/136210): characters and animations
- Boxes by [PolyOne Studio](https://assetstore.unity.com/publishers/105194): parcel and box models
