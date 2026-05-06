using UnityEngine;

[CreateAssetMenu(fileName = "Stage6UnitConfig", menuName = "MassGPUPhysics/Stage6/Config/Unit Config")]
public sealed class Stage6UnitConfig_Stage6 : ScriptableObject
{
    [Header("Identity")]
    public string unitName = "Melee Infantry";
    public Stage6RenderConfig_Stage6 renderConfig;

    [Header("Combat")]
    [Min(1)] public int maxHp = 100;
    [Min(0.01f)] public float maxSpeed = 6f;
    [Min(0.1f)] public float targetAcquireRadius = 18f;
    [Min(0.05f)] public float attackRange = 1.35f;
    [Min(1)] public int attackDamage = 10;
    [Min(0.01f)] public float attackInterval = 0.8f;

    [Header("Crowd")]
    [Min(0.01f)] public float agentRadius = 0.45f;
    [Min(0f)] public float separationStrength = 18f;
    [Range(0f, 20f)] public float velocityDamping = 5f;

    public void ApplyTo(ref GPUInstancingManager_Stage6.TeamCombatSettings settings)
    {
        settings.targetAcquireRadius = targetAcquireRadius;
        settings.attackRange = attackRange;
        settings.attackDamage = attackDamage;
        settings.attackInterval = attackInterval;
        settings.maxHp = maxHp;
        settings.maxSpeed = maxSpeed;
        settings.agentRadius = agentRadius;
        settings.separationStrength = separationStrength;
        settings.velocityDamping = velocityDamping;
        settings.Normalize();
    }

    private void OnValidate()
    {
        maxHp = Mathf.Max(1, maxHp);
        maxSpeed = Mathf.Max(0.01f, maxSpeed);
        targetAcquireRadius = Mathf.Max(0.1f, targetAcquireRadius);
        attackRange = Mathf.Max(0.05f, attackRange);
        attackDamage = Mathf.Max(1, attackDamage);
        attackInterval = Mathf.Max(0.01f, attackInterval);
        agentRadius = Mathf.Max(0.01f, agentRadius);
        separationStrength = Mathf.Max(0f, separationStrength);
        velocityDamping = Mathf.Clamp(velocityDamping, 0f, 20f);
    }
}
