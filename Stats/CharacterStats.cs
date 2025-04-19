using UnityEngine;

[CreateAssetMenu(fileName = "NewCharacterStats", menuName = "MyGame/Character Stats")]
public class CharacterStats : ScriptableObject
{
    [Header("Core Stats")]
    public float maxHealth = 100f;
    public float movementSpeed = 3f; // Velocidad base de AIPath
    public float attackRange = 1f;
    public float attackDamage = 10f;
    public float attackCooldown = 1f;

    [Header("Resources")]
    public float maxStamina = 100f;
    [Tooltip("Stamina regenerated per second.")]
    public float staminaRegenRate = 15f;
    // public float maxMana = 100f; // Si usas mana
    // public float manaRegenRate = 5f; // Si usas mana

    [Header("Movement Actions")]
    [Tooltip("Speed multiplier during dash.")]
    public float dashSpeedMult = 5f; // Multiplicador sobre movementSpeed base
    public float dashDuration = 0.2f;
    public float dashCost = 20f;
    public float dashCooldown = 1.5f;
    [Tooltip("Invulnerability frames duration during dash.")]
    public float dashInvulnerabilityDuration = 0.2f;

    [Header("Defensive Actions")]
    [Tooltip("Stamina drained per second while blocking.")]
    public float blockStaminaDrain = 10f;
    [Tooltip("Damage reduction multiplier while blocking (0 = 100% reduction, 1 = 0% reduction).")]
    [Range(0f, 1f)] public float blockDamageMultiplier = 0.25f;
    [Tooltip("Extra stamina cost on successful block (multiplier of original damage).")]
    public float blockSuccessStaminaCostMult = 0.1f;
    [Tooltip("Movement speed multiplier while blocking.")]
    [Range(0f, 1f)] public float blockSpeedMultiplier = 0.5f;

    [Tooltip("Duration in seconds where a parry attempt can succeed.")]
    public float parryWindow = 0.15f;
    [Tooltip("Duration in seconds the attacker is stunned after being parried.")]
    public float parryStunDuration = 1.0f;
    public float parryCost = 30f;
    public float parryCooldown = 2.0f;

    // Añadir más stats aquí si son necesarios (ej. Crit Chance, Armor, etc.)
}