using UnityEngine;
using System.Collections.Generic;
using Pathfinding; // Necesario para referencia a AIPath en ApplyStun

public class CharacterData : MonoBehaviour // Asegúrate de que hereda de MonoBehaviour
{
    // --- STATS BASE (Evolucionan con nivel/mejoras) ---
    [Header("Base Stats (Evolucionan con Nivel)")]
    public float baseMaxHealth = 100f;
    public float baseMovementSpeed = 5f;
    public float baseAttackDamage = 10f;
    public float baseAttackRange = 1f;
    public float baseAttackCooldown = 1f;
    public float baseMaxStamina = 100f;
    public float baseStaminaRegenRate = 15f;
    public float baseDashSpeedMult = 5f;
    public float baseDashDuration = 0.2f;
    public float baseDashCost = 20f;
    public float baseDashCooldown = 1.5f;
    public float baseDashInvulnerabilityDuration = 0.2f;
    public float baseBlockStaminaDrain = 10f;
    public float baseBlockDamageMultiplier = 0.25f;
    public float baseBlockSuccessStaminaCostMult = 0.1f;
    public float baseBlockSpeedMultiplier = 0.5f;
    public float baseParryWindow = 0.15f;
    public float baseParryStunDuration = 1.0f;
    public float baseParryCost = 30f;
    public float baseParryCooldown = 2.0f;


    [Header("Progression")]
    public int level = 1;
    public float currentXP = 0f;
    public float xpToNextLevel = 100f;

    [Header("Equipment & Learned Skills")]
    public WeaponData equippedWeapon; // Referencia al arma equipada
    public List<SkillData> learnedSkills = new List<SkillData>(); // Habilidades activas aprendidas


    // --- ESTADO ACTUAL (Runtime) ---
    [Header("Current State (Runtime - Read Only)")]
    [SerializeField] private float _currentHealth;
    public float currentHealth { get => _currentHealth; private set => _currentHealth = value; }

    [SerializeField] private float _currentStamina;
    public float currentStamina { get => _currentStamina; private set => _currentStamina = value; }

    // --- COOLDOWN TIMERS (Runtime) ---
    [HideInInspector] public float attackAvailableTime = 0f;
    [HideInInspector] public float dashAvailableTime = 0f;
    [HideInInspector] public float parryAvailableTime = 0f;
    public Dictionary<string, float> skillAvailableTimes = new Dictionary<string, float>();


    // --- COMBAT STATE FLAGS (Runtime) ---
    public bool isBlocking { get; private set; } = false;
    public bool isDashing { get; private set; } = false;
    public bool isInvulnerable { get; private set; } = false;
    public bool isStunned { get; private set; } = false;
    public bool isAttemptingParry { get; private set; } = false;
    private float stunEndTime;


    // --- REFERENCIAS A COMPONENTES (Opcional, pero útil) ---
    private CharacterCombat combatController;
    private HealthSystem healthSystem;
    private AIPath aiPath; // Para stun
    private Rigidbody2D rb; // Para stun

    void Awake()
    {
        combatController = GetComponent<CharacterCombat>();
        healthSystem = GetComponent<HealthSystem>();
        aiPath = GetComponent<AIPath>();
        rb = GetComponent<Rigidbody2D>();

        if (equippedWeapon == null)
        {
            Debug.LogWarning($"CharacterData on {gameObject.name} has no equipped weapon assigned in Awake. Ensure it's set in the prefab or loaded.", this);
        }
    }

    void Start()
    {
        // La inicialización se hace DESPUÉS de cargar/copiar datos
        // desde BattleManager/FightManager
    }

    void Update()
    {
        CheckStunEnd();
        if (!isStunned && !isDashing && !isBlocking && currentStamina < baseMaxStamina)
        {
            RegenerateStamina(Time.deltaTime);
        }
    }

    // LLAMADO DESDE BattleManager/FightManager después de copiar datos
    public void InitializeResourcesAndCooldowns()
    {
        currentHealth = baseMaxHealth;
        currentStamina = baseMaxStamina;

        float startTime = Time.time;
        attackAvailableTime = startTime;
        dashAvailableTime = startTime;
        parryAvailableTime = startTime;

        skillAvailableTimes.Clear();
        foreach (var skill in learnedSkills)
        {
            if (skill != null && !skillAvailableTimes.ContainsKey(skill.skillID))
            {
                skillAvailableTimes.Add(skill.skillID, startTime);
            }
        }

        // Dispara evento para la UI
        healthSystem?.TriggerInitialHealthChangedEvent();

        // Actualiza velocidad de AIPath ahora que las stats están listas
        if (aiPath != null)
        {
            aiPath.maxSpeed = baseMovementSpeed;
        }

        Debug.Log($"[{gameObject.name}] Initialized. HP: {currentHealth}/{baseMaxHealth}, Stam: {currentStamina}/{baseMaxStamina}, Speed: {baseMovementSpeed}");
    }

    void RegenerateStamina(float deltaTime)
    {
        currentStamina += baseStaminaRegenRate * deltaTime;
        currentStamina = Mathf.Min(currentStamina, baseMaxStamina);
    }

    public bool ConsumeStamina(float amount)
    {
        if (amount <= 0) return true;
        if (currentStamina >= amount)
        {
            currentStamina -= amount;
            return true;
        }
        return false;
    }

    // --- Checks de Cooldown ---
    public bool IsAttackReady() => Time.time >= attackAvailableTime;
    public bool IsDashReady() => Time.time >= dashAvailableTime;
    public bool IsParryReady() => Time.time >= parryAvailableTime;
    public bool IsSkillReady(SkillData skill)
    {
        if (skill == null) return false;
        return skillAvailableTimes.TryGetValue(skill.skillID, out float availableTime) && Time.time >= availableTime;
    }

    // --- Poner en Cooldown ---
    // Attack cooldown se establece desde CharacterCombat usando attackAvailableTime
    public void PutDashOnCooldown() { dashAvailableTime = Time.time + baseDashCooldown; }
    public void PutParryOnCooldown() { parryAvailableTime = Time.time + baseParryCooldown; }
    public void PutSkillOnCooldown(SkillData skill)
    {
        if (skill != null && learnedSkills.Contains(skill)) // Verifica si la habilidad está aprendida
        {
            if (!skillAvailableTimes.ContainsKey(skill.skillID))
            {
                 // Añade la habilidad al diccionario si no estaba (puede pasar si se aprende en runtime)
                 skillAvailableTimes.Add(skill.skillID, Time.time + skill.cooldown);
            } else {
                 skillAvailableTimes[skill.skillID] = Time.time + skill.cooldown;
            }
        }
         else if (skill != null)
        {
             Debug.LogWarning($"Tried to put skill '{skill.skillName}' on cooldown, but it's not in the learned skills list for {gameObject.name}");
        }
    }

    // --- Setters de Estado ---
    public void SetBlocking(bool state) { if (isStunned && state) return; isBlocking = state; }
    public void SetDashing(bool state) => isDashing = state;
    public void SetInvulnerable(bool state) => isInvulnerable = state;
    public void SetAttemptingParry(bool state) => isAttemptingParry = state;

    // --- Stun ---
    public void ApplyStun(float duration)
    {
        if (duration <= 0) return;
        isStunned = true;
        stunEndTime = Mathf.Max(stunEndTime, Time.time + duration);
        combatController?.InterruptActions();
        if (aiPath != null) aiPath.canMove = false; // Detener A*
        if (rb != null) rb.linearVelocity = Vector2.zero;
        SetBlocking(false); SetDashing(false); SetInvulnerable(false); SetAttemptingParry(false); // Limpia todos los estados
        Debug.Log($"{gameObject.name} STUNNED for {duration}s (until {stunEndTime})");
        // combatController?.SetAnimatorBool("IsStunned", true); // O trigger
    }

    void CheckStunEnd()
    {
        if (isStunned && Time.time >= stunEndTime)
        {
            isStunned = false;
            if (aiPath != null) aiPath.canMove = true; // Permite que la IA decida moverse
            Debug.Log($"{gameObject.name} NO LONGER stunned.");
            // combatController?.SetAnimatorBool("IsStunned", false);
        }
    }

    // --- Vida ---
    public void RestoreHealth(float amount)
    {
        if (amount <= 0) return;
        currentHealth += amount;
        currentHealth = Mathf.Clamp(currentHealth, 0, baseMaxHealth);
        healthSystem?.TriggerInitialHealthChangedEvent(); // Notifica cambio a UI
    }
    public void RestoreFullHealth() { RestoreHealth(baseMaxHealth); }

    public void SetCurrentHealth(float value)
    {
        currentHealth = Mathf.Clamp(value, 0, baseMaxHealth);
         healthSystem?.TriggerInitialHealthChangedEvent(); // Notifica cambio a UI
    }

     // --- Copiar Datos (Para Carga/Instanciación) ---
     public void CopyFrom(CharacterData source)
     {
         if (source == null) { Debug.LogError("Source CharacterData is null in CopyFrom!"); return; }

         this.baseMaxHealth = source.baseMaxHealth;
         this.baseMovementSpeed = source.baseMovementSpeed;
         this.baseAttackDamage = source.baseAttackDamage;
         this.baseAttackRange = source.baseAttackRange;
         this.baseAttackCooldown = source.baseAttackCooldown;
         this.baseMaxStamina = source.baseMaxStamina;
         this.baseStaminaRegenRate = source.baseStaminaRegenRate;
         this.baseDashSpeedMult = source.baseDashSpeedMult;
         this.baseDashDuration = source.baseDashDuration;
         this.baseDashCost = source.baseDashCost;
         this.baseDashCooldown = source.baseDashCooldown;
         this.baseDashInvulnerabilityDuration = source.baseDashInvulnerabilityDuration;
         this.baseBlockStaminaDrain = source.baseBlockStaminaDrain;
         this.baseBlockDamageMultiplier = source.baseBlockDamageMultiplier;
         this.baseBlockSuccessStaminaCostMult = source.baseBlockSuccessStaminaCostMult;
         this.baseBlockSpeedMultiplier = source.baseBlockSpeedMultiplier;
         this.baseParryWindow = source.baseParryWindow;
         this.baseParryStunDuration = source.baseParryStunDuration;
         this.baseParryCost = source.baseParryCost;
         this.baseParryCooldown = source.baseParryCooldown;
         this.level = source.level;
         this.currentXP = source.currentXP;
         this.xpToNextLevel = source.xpToNextLevel;
         this.equippedWeapon = source.equippedWeapon;
         this.learnedSkills = new List<SkillData>(source.learnedSkills);

         Debug.Log($"Copied data from {source.name} to {this.name}. Weapon: {this.equippedWeapon?.weaponName ?? "None"}, Lvl: {this.level}");
     }
}