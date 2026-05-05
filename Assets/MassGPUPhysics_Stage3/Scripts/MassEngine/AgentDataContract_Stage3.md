# AgentData Contract - Stage3

`AgentData` is the shared memory layout used by:

- `Scripts/GPUInstancingManager_Stage3.cs`
- `Shaders/AgentComputeShader_Stage3.compute`
- `Shaders/InstancedAgentShader_Stage3.shader`
- `Shaders/LitInstancedAgentShader_Stage3.shader`
- `Shaders/BillboardInstancedAgentShader_Stage3.shader`

The field order, field type, and total stride must stay identical across all of them.

Current layout:

| Field | Type | Bytes | Purpose |
| --- | --- | ---: | --- |
| `position` | `Vector3` / `float3` | 12 | World position |
| `rotation` | `Vector3` / `float3` | 12 | Euler rotation in degrees |
| `scale` | `Vector3` / `float3` | 12 | Per-agent scale |
| `velocity` | `Vector3` / `float3` | 12 | XZ movement velocity |
| `currentState` | `int` | 4 | Animation or logic state |
| `currentAnimationTime` | `float` | 4 | VAT playback time |

Total stride: 56 bytes.

Future combat/state-machine fields should be added deliberately and in the same order everywhere, for example:

| Field | Type | Purpose |
| --- | --- | --- |
| `teamId` | `int` | Faction/team ownership |
| `hp` | `float` | Health |
| `targetAgentIndex` | `int` | Current combat target, `-1` when none |
| `attackCooldown` | `float` | Remaining attack cooldown |

When this contract changes, update every shader and the C# struct in the same commit.
