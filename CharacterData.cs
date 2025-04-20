// File: CharacterData.cs
using UnityEngine;
using System.Collections.Generic;
using Pathfinding;

public class CharacterData : MonoBehaviour
{


    [Header("Base Stats (Evolucionan con Nivel)")]
public float baseMaxHealth = 100f;
public float baseMovementSpeed = 5f;
public float baseAttackDamage = 10f;
public float baseAttackRange = 1f;
public float baseAttackCooldown = 1f;
public float baseMaxStamina = 100f;
public float baseStaminaRegenRate = 15f;
// Añade otras stats base que necesites (dash, block, parry costs/cooldowns si quieres que evolucionen)

[Header("Progression")]
public int level = 1;
public float currentXP = 0f;
public float xpToNextLevel = 100f; // Podrías calcular esto basado en el nivel

[Header("Equipment & Skills")]
public WeaponData equippedWeapon; // ¡Importante! Referencia al arma
public List<SkillData> learnedSkills = new List<SkillData>(); // Para habilidades activas
// Más adelante añadiremos pasivas y mascotas

    [Header("Configuration")]
        public List<SkillData> skills;

    [Header("Current State (Runtime - Do Not Modify Directly)")]
    [SerializeField] private float _currentHealth;
    public float currentHealth { get => _currentHealth; private set => _currentHealth = value; }

    [SerializeField] private float _currentStamina;
    public float currentStamina { get => _currentStamina; private set => _currentStamina = value; }

    // Cooldown Timers
    private float attackAvailableTime = 0f;
    private float dashAvailableTime = 0f;
    private float parryAvailableTime = 0f;
    public Dictionary<string, float> skillAvailableTimes = new Dictionary<string, float>();

    // Combat State Flags
    public bool isBlocking { get; private set; } = false;
    public bool isDashing { get; private set; } = false;
    public bool isInvulnerable { get; private set; } = false;
    public bool isStunned { get; private set; } = false;
    public bool isAttemptingParry { get; private set; } = false;
    private float stunEndTime;

    // --- Referencias ---
    private CharacterCombat combatController;
    private HealthSystem healthSystem; // Referencia para disparar evento inicial
    private AIPath aiPath;
    private Rigidbody2D rb;

    void Awake()
    {
        combatController = GetComponent<CharacterCombat>();
        healthSystem = GetComponent<HealthSystem>(); // Obtener referencia
        aiPath = GetComponent<AIPath>();
        rb = GetComponent<Rigidbody2D>();

        if (baseStats == null) {
            Debug.LogWarning("CharacterStats not assigned in prefab before Awake/Start.", this);
        }
    }

    void Start()
    {
        InitializeResourcesAndCooldowns();
        // Disparar el evento inicial de cambio de vida para la UI
        healthSystem?.TriggerInitialHealthChangedEvent(); // <-- LLAMADA A HealthSystem
    }


    void Update()
    {
        CheckStunEnd();
        if (!isStunned && !isDashing) {
            RegenerateStamina(Time.deltaTime);
        }
    }

    public void InitializeResourcesAndCooldowns() {
        if (baseStats != null) {
            currentHealth = baseStats.maxHealth;
            currentStamina = baseStats.maxStamina;
            float startTime = Time.time;
            attackAvailableTime = startTime; dashAvailableTime = startTime; parryAvailableTime = startTime;
            skillAvailableTimes.Clear();
            foreach (var skill in skills) {
                if (skill != null && !skillAvailableTimes.ContainsKey(skill.skillID)) {
                    skillAvailableTimes.Add(skill.skillID, startTime);
                }
            }
             // Dispara el evento de HealthSystem aquí también por si Start no se ha llamado aún cuando BattleManager configura
            // healthSystem?.TriggerInitialHealthChangedEvent(); // Doble seguridad
        } else {
            Debug.LogError("¡CharacterStats es nulo en InitializeResourcesAndCooldowns!", this);
            currentHealth = 1; currentStamina = 0;
        }
    }

    void RegenerateStamina(float deltaTime) {
        if (baseStats != null && !isBlocking && currentStamina < baseStats.maxStamina) {
            currentStamina += baseStats.staminaRegenRate * deltaTime;
            currentStamina = Mathf.Min(currentStamina, baseStats.maxStamina);
        }
    }

    public bool ConsumeStamina(float amount) {
        if (baseStats == null || amount <= 0) return true;
        if (currentStamina >= amount) {
            currentStamina -= amount; return true;
        }
        // Debug.Log($"{gameObject.name} no tiene suficiente stamina (Necesita: {amount}, Tiene: {currentStamina})");
        return false;
    }

    public bool IsAttackReady() => baseStats != null && Time.time >= attackAvailableTime;
    public bool IsDashReady() => baseStats != null && Time.time >= dashAvailableTime;
    public bool IsParryReady() => baseStats != null && Time.time >= parryAvailableTime;
    public bool IsSkillReady(SkillData skill) => baseStats != null && skill != null && skillAvailableTimes.TryGetValue(skill.skillID, out float availableTime) && Time.time >= availableTime;

    public void PutAttackOnCooldown() { if (baseStats != null) attackAvailableTime = Time.time + baseStats.attackCooldown; }
    public void PutDashOnCooldown() { if (baseStats != null) dashAvailableTime = Time.time + baseStats.dashCooldown; }
    public void PutParryOnCooldown() { if (baseStats != null) parryAvailableTime = Time.time + baseStats.parryCooldown; }
    public void PutSkillOnCooldown(SkillData skill) { if (baseStats != null && skill != null && skillAvailableTimes.ContainsKey(skill.skillID)) { skillAvailableTimes[skill.skillID] = Time.time + skill.cooldown; } }

    public void SetBlocking(bool state) { if (isStunned && state) return; isBlocking = state; }
    public void SetDashing(bool state) => isDashing = state;
    public void SetInvulnerable(bool state) => isInvulnerable = state;
    public void SetAttemptingParry(bool state) => isAttemptingParry = state;

    public void ApplyStun(float duration) {
        if (duration <= 0) return; isStunned = true;
        stunEndTime = Mathf.Max(stunEndTime, Time.time + duration);
        combatController?.InterruptActions();
        if (aiPath != null) aiPath.canMove = false; // Detener A* explícitamente durante stun
        if (rb != null) rb.linearVelocity = Vector2.zero;
        SetBlocking(false); SetDashing(false); SetAttemptingParry(false);
        Debug.Log($"{gameObject.name} está ATURDIDO por {duration}s (hasta {stunEndTime})");
    }

    void CheckStunEnd() {
        if (isStunned && Time.time >= stunEndTime) {
            isStunned = false;
             if (aiPath != null) aiPath.canMove = false; // Asegura que siga parado hasta que la IA decida moverse
            Debug.Log($"{gameObject.name} ya NO está aturdido.");
        }
    }

    public void RestoreHealth(float amount) {
        if (baseStats == null) return;
        currentHealth += amount;
        currentHealth = Mathf.Clamp(currentHealth, 0, baseStats.maxHealth);
    }
    public void RestoreFullHealth() { if(baseStats != null) RestoreHealth(baseStats.maxHealth); }

    public void SetCurrentHealth(float value) {
         if (baseStats == null) return;
         currentHealth = Mathf.Clamp(value, 0, baseStats.maxHealth);
    }
}