# squad-formation-companyofheroes

A sandbox project for building squad formation and squad movement like Company of Heroes, to learn how the mechanics work. It is a learning exercise. The code is not meant to be production-ready.

Features covered:

- **Squad makeup:** each squad has a core, a left flank and a right flank. Each of these elements has its own leader.
- **Move out:** soldiers head for formation offsets placed around where the leader is predicted to be, and adjust their speed to keep in shape.
- **Softening the shape:** formation spots that can't be reached are moved to nearby reachable points, and each soldier drifts a little at random.
- **Formation context:** the squad uses a broad or tight wedge depending on the terrain it is crossing.
- **Leapfrogging:** element leaders break off to cover points and hold there for a while. A virtual leader keeps the rest of the squad on course.
- **Destination formation:** soldiers reserve their final spots, look for nearby cover and face the direction you choose (or the nearest enemies).
- **Destruction:** obstacles can be destroyed at runtime and squads re-path around the change.

## Running

1. Install **Godot 4.7 .NET (C#)** and the **.NET 8 SDK**.
2. Open `project.godot` in Godot.
3. Press **F5** (or Build, then Play). The game starts in `Scenes/Levels/TestLevel.tscn`.

## Controls

Press **H** in-game to show or hide the help overlay.

| Category | Input | Action |
|---|---|---|
| Camera | WASD / arrow keys | Pan (hold Shift to pan faster) |
| | Q / E, or middle mouse drag | Rotate |
| | Mouse wheel | Zoom |
| Selection | Left click / left drag | Select squad / box select (Shift adds to selection) |
| | 1, 2, 3 | Select a squad by number (Shift adds) |
| | Ctrl+A / Esc | Select all / clear selection |
| Orders | Right click | Move |
| | Right drag | Move and set the facing direction at the destination |
| Sandbox | K | Kill a random soldier in the selected squads |
| | R | Reinforce the selected squads |
| | X | Destroy the obstacle under the cursor |
| Time | Space | Pause |
| | `,` / `.` / `/` | Slow down / speed up / reset to normal speed |
| Debug | F1 | Toggle debug drawing |
| | F2 | Toggle soldier labels |
| | F3 | Toggle navigation mesh overlay |
| | F4 | Toggle cover point markers |

Keys are read by physical position, so on an AZERTY keyboard the camera keys are ZQSD.

## Playing

There is no win condition. Select a squad, give it move orders across different terrain, and watch how the formation behaves. Things to try:

- Move between open ground and tight areas to see the formation switch between wedges.
- Kill and reinforce soldiers to see roles get reassigned.
- Destroy obstacles while a squad is moving.
- Slow down time and use the F1 to F4 overlays to see the movement targets and paths.

## AI usage
I used AI tools to generate boilerplate code for this project (project setup, camera, selection and other scaffolding). This let me focus my own effort on the actual subject of the sandbox: squad formation and squad movement. The formation and movement logic is my own work.
