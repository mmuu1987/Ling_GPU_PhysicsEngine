# AgentData Contract - Stage5

`AgentData` is the shared memory layout used by:

- `Scripts/GPUInstancingManager_Stage5.cs`
- `Shaders/AgentComputeShader_Stage5.compute`
- `Shaders/InstancedAgentShader_Stage5.shader`
- `Shaders/LitInstancedAgentShader_Stage5.shader`
- `Shaders/BillboardInstancedAgentShader_Stage5.shader`

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

Stage5 combat data is intentionally kept out of `AgentData` so rendering shaders do not inherit combat-only layout churn. The combat pass uses separate compute-only buffers:

| Field | Type | Purpose |
| --- | --- | --- |
| `teamIdBuffer` | `int` | Faction ownership. `0` attacker, `1` defender. |
| `hpBuffer` | `int` | Current health. `<= 0` means dead. |
| `targetAgentIndexBuffer` | `int` | Current target, `-1` when none. |
| `attackCooldownBuffer` | `float` | Remaining attack cooldown. |
| `homePositionBuffer` | `Vector3` / `float3` | Defender guard/home position and spawn anchor. |
| `pendingDamageBuffer` | `int` | Per-frame accumulated damage resolved after the state pass. |

Current `currentState` values:

| Value | State | Meaning |
| ---: | --- | --- |
| `0` | `Idle` | Alive but stationary, usually guarding home. |
| `1` | `Move` | Flow-field movement or return-home movement. |
| `2` | `Engage` | Chasing a target. |
| `3` | `Attack` | In attack range and accumulating damage on cooldown. |
| `4` | `Dead` | No targeting, collision, attack, or far LOD billboard. |

When this contract changes, update every shader and the C# struct in the same commit.
