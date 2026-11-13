# Delivery Rush — Timed Delivery Feature Plan

## Player flow

1. Press **E** near the shop parcel to pick it up. Show the objective **Load the parcel onto the scooter**.
2. Press **E** beside the scooter to load the parcel into its rear box. When the loading animation finishes and the lid closes, start the countdown and reveal the blue destination beam and guiding arrow together.
3. Mount the scooter and follow the arrow through the city. The clock keeps running while mounting, waiting in traffic, or getting off.
4. Park in the marked delivery zone with the parcel still loaded. Stop the countdown, hide the navigation cues, and show **Delivery complete** with the time remaining.
5. If the clock reaches zero first, show **Time's up** and a **Retry** button. Retry resets the parcel, scooter, timer, and navigation.

## Timer and navigation

| Element | Planned behavior |
|---|---|
| Countdown | Start at **1:30** as an initial tuning value. Display minutes and seconds and highlight the last 15 seconds. |
| Blue destination beam | Place a tall translucent blue beam at the final destination. Make it visible above nearby buildings and mark the exact parking spot at its base. |
| Guiding arrow | Show an arrow near the top of the screen. Rotate it relative to the active camera so it points toward the next turn, then toward the delivery zone on the last stretch. |
| Delivery zone | Place it near the customer or entrance with enough room to stop outside traffic. |

## Implementation order

1. **Connect the loading trigger.** Use the existing DeliveryPackageController transition to **Stowed**, which happens after the cargo lid closes. Start the mission once per attempt.
2. **Connect the timer and HUD.** Reuse CountdownTimer with automatic start disabled. Use four mission states — waiting for package, delivering, completed, and failed. Check how the existing GameManager starts its timer and handles checkpoints so the delivery mission has a single countdown and finish condition.
3. **Place one destination in CityScence.** Choose a reachable customer location, mark the stopping zone, and place the beam. Keep the countdown and navigation hidden until the mission begins.
4. **Set up the route.** Place waypoints at major turns along driveable roads. Advance the arrow as the player reaches each waypoint. If the player misses a turn, guide them back to that waypoint. Let the player finish if they reach the delivery zone by a shortcut.
5. **Connect the result and retry.** Require an active mission, a stopped scooter inside the delivery zone, and a loaded parcel. End the attempt once on success or timeout. Reload the scene on retry for a full reset.

## Playtest checklist

- [ ] Picking up the parcel leaves the timer and navigation hidden.
- [ ] Finishing the loading animation starts the timer once and reveals both navigation cues.
- [ ] Mounting, dismounting, and rotating the camera keep the clock running and the arrow oriented correctly.
- [ ] The arrow advances at turns and the beam marks the final stopping zone.
- [ ] Riding through the zone at speed keeps the mission active. Parking with the loaded parcel completes it.
- [ ] Timeout shows a retry option, and retry restores the starting state.

**First-version choices** — Use one destination, a world-space blue beam, hand-placed waypoints, and a parking-based finish. Tune the 90-second limit after riding the route with traffic.
