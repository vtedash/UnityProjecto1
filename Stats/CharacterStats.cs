// File: Stats/CharacterStats.cs
using UnityEngine;

[CreateAssetMenu(fileName = "NewCharacterStats", menuName = "MyGame/Character Stats")]
public class CharacterStats : ScriptableObject
{
    [Header("Core Stats")]
    [Tooltip("Vida máxima del personaje.")]
    public float maxHealth = 100f;
    [Tooltip("Velocidad base de movimiento horizontal (usada por AIPath y movimiento manual).")]
    public float movementSpeed = 3f;
    [Tooltip("Distancia máxima para ataques básicos cuerpo a cuerpo.")]
    public float attackRange = 1f;
    [Tooltip("Daño base de los ataques básicos.")]
    public float attackDamage = 10f;
    [Tooltip("Tiempo mínimo (segundos) entre ataques básicos.")]
    public float attackCooldown = 1f;

    [Header("Resources")]
    [Tooltip("Estamina máxima.")]
    public float maxStamina = 100f;
    [Tooltip("Estamina regenerada por segundo.")]
    public float staminaRegenRate = 15f;
    // public float maxMana = 100f; // Si usas mana
    // public float manaRegenRate = 5f; // Si usas mana

    [Header("Movement Actions")]
    // --- Dash ---
    [Tooltip("Multiplicador de la velocidad de movimiento base durante el dash.")]
    public float dashSpeedMult = 5f; // <--- AJUSTA VELOCIDAD DASH
    [Tooltip("Duración del dash en segundos.")]
    public float dashDuration = 0.2f;// <--- AJUSTA DURACIÓN DASH
    [Tooltip("Costo de estamina para realizar un dash.")]
    public float dashCost = 20f;     // <--- AJUSTA COSTE DASH
    [Tooltip("Tiempo de espera (cooldown) en segundos después de un dash.")]
    public float dashCooldown = 1.5f;// <--- AJUSTA COOLDOWN DASH
    [Tooltip("Duración de la invulnerabilidad (en segundos) durante el dash.")]
    public float dashInvulnerabilityDuration = 0.2f; // <--- AJUSTA INVULNERABILIDAD DASH

    [Header("Defensive Actions")]
    // --- Blocking ---
    [Tooltip("Estamina consumida por segundo mientras se bloquea activamente.")]
    public float blockStaminaDrain = 10f;
    [Tooltip("Multiplicador de daño recibido mientras se bloquea (0=100% reducción, 1=0% reducción).")]
    [Range(0f, 1f)] public float blockDamageMultiplier = 0.25f;
    [Tooltip("Costo extra de estamina al bloquear un golpe con éxito (multiplicador sobre el daño original).")]
    public float blockSuccessStaminaCostMult = 0.1f;
    [Tooltip("Multiplicador de la velocidad de movimiento mientras se bloquea.")]
    [Range(0f, 1f)] public float blockSpeedMultiplier = 0.5f;
    // --- Parry ---
    [Tooltip("Ventana de tiempo (segundos) en la que un parry puede tener éxito.")]
    public float parryWindow = 0.15f;
    [Tooltip("Duración (segundos) del aturdimiento aplicado al atacante tras un parry exitoso.")]
    public float parryStunDuration = 1.0f;
    [Tooltip("Costo de estamina para intentar un parry.")]
    public float parryCost = 30f;
    [Tooltip("Tiempo de espera (cooldown) en segundos después de intentar un parry.")]
    public float parryCooldown = 2.0f;

    

    // Puedes añadir más stats aquí (ej. Resistencia, Prob. Crítico, etc.)
}