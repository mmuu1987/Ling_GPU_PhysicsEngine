using System.Collections.Generic;
using UnityEngine;

namespace MassGPUPhysics.Stage7
{
    public interface ISpawnModule
    {
        SpawnConfig Config { get; }
        void GenerateAgents(AgentData[] buffer, int offset, int count, int teamId);
    }

    public interface IMovementModule
    {
        MovementConfig Config { get; }
        FlowFieldTarget Target { get; }
        void SetTargetPoint(Vector3 point);
        void SetTargetArea(Vector3 center, Vector3 size);
        Vector3 ComputeDesiredVelocity(Vector3 agentPosition, Vector2 flowDirection, float flowWeight);
    }

    public interface IFlockingModule
    {
        FlockingConfig Config { get; }
        Vector3 ComputeSeparationForce(Vector3 agentPosition, Vector3 neighborPosition, float agentRadius, float separationStrength);
        Vector3 ComputeAttractionForce(Vector3 agentPosition, Vector3 targetPosition, float attractionStrength);
    }

    public interface IAnimationModule
    {
        AnimationConfig Config { get; }
        VATClipParams GetClipForState(AgentState agentState);
        float AdvanceAnimationTime(float currentTime, AgentState agentState, float deltaTime, int lodLevel);
    }

    public interface ICombatModule
    {
        CombatConfig Config { get; }
        int FindNearestEnemy(int agentIndex, int agentTeamId, SpatialHashQuery query, int[] teamIds, int[] hpValues);
        bool IsInAttackRange(Vector3 agentPosition, Vector3 targetPosition, float attackRange);
        int ComputeDamage(float cooldownRemaining, float attackInterval, int attackDamage);
        AgentState ResolvePostDamageState(int hp);
    }

    public interface IFlowFieldVisualizer
    {
        bool IsEnabled { get; set; }
        FlowFieldPreviewMode PreviewMode { get; set; }
        void Render(ComputeBuffer flowFieldBuffer, RenderTexture target, int resolutionX, int resolutionZ);
        void Release();
    }

    public enum FlowFieldPreviewMode
    {
        FlowDirection = 0,
        DensityTarget = 1
    }

    public enum FlowFieldTargetMode
    {
        Point = 0,
        Area = 1
    }

    public struct FlowFieldTarget
    {
        public FlowFieldTargetMode mode;
        public Vector3 center;
        public Vector3 size;
    }

    public struct SpatialHashQuery
    {
        public Vector3[] positions;
        public IReadOnlyList<int> candidateIndices;
    }
}
