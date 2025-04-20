using UnityEngine;
using System.Collections.Generic;
using Pathfinding; // Necesario para AIPath

public class CharacterData : MonoBehaviour
{
    // --- STATS BASE ---
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

    // --- PROGRESIÓN ---
    [Header("Progression")]
    public int level = 1;
    public float currentXP = 0f;
    public float xpToNextLevel = 100f;

    // --- EQUIPO Y SKILLS ---
    [Header("Equipment & Learned Skills")]
    public WeaponData equippedWeapon;
    public List<SkillData> learnedSkills = new List<SkillData>();

    // --- ESTADO ACTUAL (Runtime) ---
    [Header("Current State (Runtime - Read Only)")]
    [SerializeField] private float _currentHealth;
    public float currentHealth { get => _currentHealth; private set => _currentHealth = value; }
    [SerializeField] private float _currentStamina;
    public float currentStamina { get => _currentStamina; private set => _currentStamina = value; }

    // --- TIMERS (Runtime) ---
    [HideInInspector] public float attackAvailableTime = 0f;
    [HideInInspector] public float dashAvailableTime = 0f;
    [HideInInspector] public float parryAvailableTime = 0f;
    public Dictionary<string, float> skillAvailableTimes = new Dictionary<string, float>();

    // --- FLAGS (Runtime) ---
    public bool isBlocking { get; private set; } = false;
    public bool isDashing { get; private set; } = false;
    public bool isInvulnerable { get; private set; } = false;
    public bool isStunned { get; private set; } = false;
    public bool isAttemptingParry { get; private set; } = false;
    private float stunEndTime;

    // --- REFERENCIAS ---
    private CharacterCombat combatController;
    private HealthSystem healthSystem;
    private AIPath aiPath;
    private Rigidbody2D rb;

    void Awake()
    {
        combatController = GetComponent<CharacterCombat>();
        healthSystem = GetComponent<HealthSystem>();
        aiPath = GetComponent<AIPath>();
        rb = GetComponent<Rigidbody2D>();
        if (equippedWeapon == null) Debug.LogWarning($"[{gameObject.name}] No weapon assigned in Awake.", this);
    }

    void Update()
    {
        CheckStunEnd();
        if (!isStunned && !isDashing && !isBlocking && currentStamina < baseMaxStamina) {
            RegenerateStamina(Time.deltaTime);
        }
    }

    public void InitializeResourcesAndCooldowns()
    {
        currentHealth = baseMaxHealth;
        currentStamina = baseMaxStamina;
        float startTime = Time.time;
        attackAvailableTime = startTime; dashAvailableTime = startTime; parryAvailableTime = startTime;
        skillAvailableTimes.Clear();
        if (learnedSkills != null) { // Chequeo null
             foreach (var skill in learnedSkills) {
                 if (skill != null && !skillAvailableTimes.ContainsKey(skill.skillID)) {
                     skillAvailableTimes.Add(skill.skillID, startTime);
                 }
             }
        }
        healthSystem?.TriggerInitialHealthChangedEvent();
        if (aiPath != null) aiPath.maxSpeed = baseMovementSpeed;
        // Debug.Log($"[{gameObject.name}] Initialized. HP: {currentHealth}/{baseMaxHealth}, Speed: {baseMovementSpeed}"); // Comentado log
    }

    void RegenerateStamina(float deltaTime) {
        currentStamina += baseStaminaRegenRate * deltaTime;
        currentStamina = Mathf.Min(currentStamina, baseMaxStamina);
    }

    public bool ConsumeStamina(float amount) {
        if (amount <= 0) return true;
        if (currentStamina >= amount) { currentStamina -= amount; return true; }
        return false;
    }

    public bool IsAttackReady() => Time.time >= attackAvailableTime;
    public bool IsDashReady() => Time.time >= dashAvailableTime;
    public bool IsParryReady() => Time.time >= parryAvailableTime;
    public bool IsSkillReady(SkillData skill) {
        if (skill == null) return false;
        return skillAvailableTimes.TryGetValue(skill.skillID, out float availableTime) && Time.time >= availableTime;
    }

    public void PutDashOnCooldown() { dashAvailableTime = Time.time + baseDashCooldown; }
    public void PutParryOnCooldown() { parryAvailableTime = Time.time + baseParryCooldown; }
    public void PutSkillOnCooldown(SkillData skill) {
        if (skill != null && learnedSkills != null && learnedSkills.Contains(skill)) { // Chequea si está aprendida
            if (!skillAvailableTimes.ContainsKey(skill.skillID)) skillAvailableTimes.Add(skill.skillID, Time.time + skill.cooldown);
            else skillAvailableTimes[skill.skillID] = Time.time + skill.cooldown;
        } else if (skill != null) Debug.LogWarning($"Skill '{skill.skillName}' not learned by {gameObject.name}");
    }

    public void SetBlocking(bool state) { if (isStunned && state) return; isBlocking = state; }
    public void SetDashing(bool state) => isDashing = state;
    public void SetInvulnerable(bool state) => isInvulnerable = state;
    public void SetAttemptingParry(bool state) => isAttemptingParry = state;

    public void ApplyStun(float duration) {
        if (duration <= 0) return;
        isStunned = true; stunEndTime = Mathf.Max(stunEndTime, Time.time + duration);
        combatController?.InterruptActions();
        if (aiPath != null) aiPath.canMove = false;
        if (rb != null) rb.linearVelocity = Vector2.zero;
        SetBlocking(false); SetDashing(false); SetInvulnerable(false); SetAttemptingParry(false);
        // Debug.Log($"{gameObject.name} STUNNED for {duration}s"); // Comentado log
    }

    void CheckStunEnd() {
        if (isStunned && Time.time >= stunEndTime) {
            isStunned = false;
            if (aiPath != null) aiPath.canMove = true;
            // Debug.Log($"{gameObject.name} NO LONGER stunned."); // Comentado log
        }
    }

    public void RestoreHealth(float amount) {
        if (amount <= 0) return;
        currentHealth += amount; currentHealth = Mathf.Clamp(currentHealth, 0, baseMaxHealth);
        healthSystem?.TriggerInitialHealthChangedEvent();
    }
    public void RestoreFullHealth() { RestoreHealth(baseMaxHealth); }
    public void SetCurrentHealth(float value) {
        currentHealth = Mathf.Clamp(value, 0, baseMaxHealth);
        healthSystem?.TriggerInitialHealthChangedEvent();
    }

    // Aplica datos desde el SaveData cargado
    // *** USA EL TIPO PÚBLICO SaveLoadSystem.CharacterSaveData ***
    public void ApplySaveData(SaveLoadSystem.CharacterSaveData saveData) // <<<--- CORRECCIÓN TIPO PARÁMETRO
    {
        if (saveData == null) { Debug.LogError($"ApplySaveData Error: saveData null for {gameObject.name}!"); return; }

        // Copia Stats Base
        this.baseMaxHealth = saveData.baseMaxHealth; this.baseMovementSpeed = saveData.baseMovementSpeed; this.baseAttackDamage = saveData.baseAttackDamage;
        this.baseAttackRange = saveData.baseAttackRange; this.baseAttackCooldown = saveData.baseAttackCooldown; this.baseMaxStamina = saveData.baseMaxStamina;
        this.baseStaminaRegenRate = saveData.baseStaminaRegenRate; this.baseDashSpeedMult = saveData.baseDashSpeedMult; this.baseDashDuration = saveData.baseDashDuration;
        this.baseDashCost = saveData.baseDashCost; this.baseDashCooldown = saveData.baseDashCooldown; this.baseDashInvulnerabilityDuration = saveData.baseDashInvulnerabilityDuration;
        this.baseBlockStaminaDrain = saveData.baseBlockStaminaDrain; this.baseBlockDamageMultiplier = saveData.baseBlockDamageMultiplier; this.baseBlockSuccessStaminaCostMult = saveData.baseBlockSuccessStaminaCostMult;
        this.baseBlockSpeedMultiplier = saveData.baseBlockSpeedMultiplier; this.baseParryWindow = saveData.baseParryWindow; this.baseParryStunDuration = saveData.baseParryStunDuration;
        this.baseParryCost = saveData.baseParryCost; this.baseParryCooldown = saveData.baseParryCooldown;
        // Copia Progresión
        this.level = saveData.level; this.currentXP = saveData.currentXP; this.xpToNextLevel = saveData.xpToNextLevel;

        // Carga Arma desde Resources
        this.equippedWeapon = null;
        if (!string.IsNullOrEmpty(saveData.equippedWeaponAssetName)) {
            this.equippedWeapon = Resources.Load<WeaponData>("Weapons/" + saveData.equippedWeaponAssetName);
            if (this.equippedWeapon == null) Debug.LogWarning($"[{gameObject.name}] Failed to load weapon '{saveData.equippedWeaponAssetName}'");
        }

        // Carga Skills desde Resources
        this.learnedSkills = new List<SkillData>();
        if (saveData.learnedSkillAssetNames != null) {
            foreach (string skillName in saveData.learnedSkillAssetNames) {
                 if (!string.IsNullOrEmpty(skillName)) {
                     SkillData skill = Resources.Load<SkillData>("Skills/" + skillName);
                     if (skill != null) this.learnedSkills.Add(skill);
                     else Debug.LogWarning($"[{gameObject.name}] Failed to load skill '{skillName}'");
                 }
            }
        }
        // Debug.Log($"Applied save data to {gameObject.name}. Weapon: {this.equippedWeapon?.weaponName ?? "None"}, Lvl: {this.level}"); // Comentado log
        // InitializeResourcesAndCooldowns() se llama DESPUÉS desde BattleManager
    }
}