# Static Obstacles

The War Sandbox can upload up to eight axis-aligned XZ rectangles through
`MassEngineManager.SetStaticObstacles`. They are runtime state and never modify scenario
or configuration assets.

Navigation uses the existing direct/dynamic flow-field architecture:

- each cell-to-target segment is tested against the active rectangles;
- a blocked segment steers toward the cheapest visible expanded corner;
- combat locomotion performs a short-range detour check;
- final position integration pushes an agent-radius footprint outside every wall.

With zero active obstacles the loops terminate immediately and the existing navigation
path remains unchanged. In the War Sandbox deployment phase, press `O` or use the panel
toggle to enable the two default wall sections. Walls appear as world blocks and grey
rectangles on the tactical minimap. A move target inside a wall is projected to the
nearest safe edge.
