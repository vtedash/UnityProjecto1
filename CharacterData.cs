using UnityEngine;
using System.Collections.Generic; // Para Diccionario de Cooldowns
using Pathfinding; // Asegúrate de tener esto si usas AIPath

public class CharacterData : MonoBehaviour
{
    [Header("Configuration")]
    public CharacterStats baseStats; // Asigna stats base en el Inspector del PREFAB
    public List<SkillData> skills;   // Asigna las habilidades disponibles en el Inspector

    [Header("Current State (Runtime - Do Not Modify Directly)")]
    [SerializeField] private float _currentHealth;
    public float currentHealth { get => _currentHealth; private set => _currentHealth = value; }

    [SerializeField] private float _currentStamina;
    public float currentStamina { get => _currentStamina; private set => _currentStamina = value; }

    // public float currentMana; // Si usas mana

    // Cooldown Timers (Time.time when action becomes available again)
    private float attackAvailableTime = 0f;
    private float dashAvailableTime = 0f;
    private float parryAvailableTime = 0f;
    public Dictionary<string, float> skillAvailableTimes = new Dictionary<string, float>();

    // Combat State Flags
    public bool isBlocking { get; private set; } = false;
    public bool isDashing { get; private set; } = false;
    public bool isInvulnerable { get; private set; } = false; // Durante dash, etc.
    public bool isStunned { get; private set; } = false;
    public bool isAttemptingParry { get; private set; } = false; // Durante la ventana de parry
    private float stunEndTime;

    // --- Referencias (Cache para eficiencia) ---
    private CharacterCombat combatController;
    private HealthSystem healthSystem;
    private AIPath aiPath; // Asumiendo A* Pathfinding Project
    private Rigidbody2D rb; // Referencia al Rigidbody2D

    void Awake()
    {
        combatController = GetComponent<CharacterCombat>();
        healthSystem = GetComponent<HealthSystem>();
        aiPath = GetComponent<AIPath>(); // Puede ser null si no se usa
        rb = GetComponent<Rigidbody2D>(); // Obtener Rigidbody2D

        if (baseStats == null)
        {
            Debug.LogWarning("CharacterStats no asignado en el prefab o no sobreescrito antes de Start.", this);
        }
        // Asegurar que la inicialización ocurra incluso si baseStats es null inicialmente
        // ya que podría asignarse externamente antes de Start.
        // InitializeResourcesAndCooldowns(); // Llamada movida a Start para asegurar que baseStats esté listo
    }

    void Start()
    {
        // Start es un lugar más seguro para inicializar si los stats se asignan externamente
        InitializeResourcesAndCooldowns();
    }


    void Update()
    {
        // Procesar estados temporales
        CheckStunEnd();

        // Regenerar recursos si no está impedido
        if (!isStunned && !isDashing) // No regenerar si está stuneado o dasheando
        {
            RegenerateStamina(Time.deltaTime);
            // RegenerateMana(Time.deltaTime); // Si usas mana
        }
    }

    public void InitializeResourcesAndCooldowns()
    {
        if (baseStats != null)
        {
            currentHealth = baseStats.maxHealth;
            currentStamina = baseStats.maxStamina;
            // currentMana = baseStats.maxMana; // Si usas mana

            // Inicializar cooldowns (disponibles al inicio)
            float startTime = Time.time; // O -cooldown si quieres que estén listos antes
            attackAvailableTime = startTime;
            dashAvailableTime = startTime;
            parryAvailableTime = startTime;

            skillAvailableTimes.Clear();
            foreach (var skill in skills)
            {
                if (skill != null && !skillAvailableTimes.ContainsKey(skill.skillID))
                {
                    skillAvailableTimes.Add(skill.skillID, startTime);
                }
            }
        }
        else
        {
            Debug.LogError("¡CharacterStats SIGUE siendo nulo en Start! No se pueden inicializar recursos/cooldowns.", this);
            currentHealth = 1; // Valor mínimo por defecto
            currentStamina = 0;
            // currentMana = 0;
        }
    }

    void RegenerateStamina(float deltaTime)
    {
        // No regenerar si está bloqueando o si ya está lleno
        if (!isBlocking && currentStamina < baseStats.maxStamina)
        {
            currentStamina += baseStats.staminaRegenRate * deltaTime;
            currentStamina = Mathf.Min(currentStamina, baseStats.maxStamina);
            // Actualizar UI si existe
        }
    }

    public bool ConsumeStamina(float amount)
    {
        if (baseStats == null) return false; // No se pueden verificar costos si no hay stats
        if (amount <= 0) return true; // No consumir si el costo es cero o negativo
        if (currentStamina >= amount)
        {
            currentStamina -= amount;
            // Actualizar UI
            return true;
        }
        Debug.Log($"{gameObject.name} no tiene suficiente stamina (Necesita: {amount}, Tiene: {currentStamina})");
        return false;
    }

    // --- Cooldown Checks ---
    public bool IsAttackReady() => baseStats != null && Time.time >= attackAvailableTime;
    public bool IsDashReady() => baseStats != null && Time.time >= dashAvailableTime;
    public bool IsParryReady() => baseStats != null && Time.time >= parryAvailableTime;
    public bool IsSkillReady(SkillData skill) =>
        baseStats != null &&
        skill != null &&
        skillAvailableTimes.TryGetValue(skill.skillID, out float availableTime) &&
        Time.time >= availableTime;

    // --- Cooldown Setters ---
    public void PutAttackOnCooldown() { if (baseStats != null) attackAvailableTime = Time.time + baseStats.attackCooldown; }
    public void PutDashOnCooldown() { if (baseStats != null) dashAvailableTime = Time.time + baseStats.dashCooldown; }
    public void PutParryOnCooldown() { if (baseStats != null) parryAvailableTime = Time.time + baseStats.parryCooldown; }
    public void PutSkillOnCooldown(SkillData skill)
    {
        if (baseStats != null && skill != null && skillAvailableTimes.ContainsKey(skill.skillID))
        {
            skillAvailableTimes[skill.skillID] = Time.time + skill.cooldown;
        }
    }

    // --- State Setters (llamados desde CharacterCombat u otros sistemas) ---
    public void SetBlocking(bool state) {
        if (isStunned && state) return; // No puede empezar a bloquear si está aturdido
        isBlocking = state;
    }
    public void SetDashing(bool state) => isDashing = state;
    public void SetInvulnerable(bool state) => isInvulnerable = state;
    public void SetAttemptingParry(bool state) => isAttemptingParry = state;

    // --- Stun Logic ---
    public void ApplyStun(float duration)
    {
        if (duration <= 0) return;
        isStunned = true;
        // Asegurar que el stun no termine antes de tiempo si se aplica de nuevo
        stunEndTime = Mathf.Max(stunEndTime, Time.time + duration);

        // Interrumpir acciones actuales (Importante!)
        combatController?.InterruptActions(); // Llamar al método centralizado

        // *** CORRECCIÓN AQUÍ ***
        if (aiPath != null) aiPath.canMove = false; // Detener movimiento A*
        if (rb != null) rb.linearVelocity = Vector2.zero; // Detener movimiento físico inmediatamente

        // Asegurar que los estados de acción se cancelen
        SetBlocking(false);
        SetDashing(false);
        SetAttemptingParry(false);

        Debug.Log($"{gameObject.name} está ATURDIDO por {duration}s (hasta {stunEndTime})");
        // Trigger animación de stun
    }

    void CheckStunEnd()
    {
        if (isStunned && Time.time >= stunEndTime)
        {
            isStunned = false;

            // *** CORRECCIÓN AQUÍ ***
            // Solo permitir que AIPath se mueva si la IA lo decide,
            // no automáticamente al salir del stun. La IA debería retomar el control.
            // if (aiPath != null) aiPath.canMove = true; // Permitir que A* controle de nuevo (QUITAR ESTO o condicionarlo)
            // En su lugar, la IA debería volver a evaluar y poner aiPath.canMove = true si decide moverse.

            Debug.Log($"{gameObject.name} ya NO está aturdido.");
            // Trigger fin animación de stun
        }
    }

    // Llama a esta función si necesitas resetear la salud manualmente (ej. al reiniciar ronda)
    public void RestoreHealth(float amount)
    {
        if (baseStats == null) return;
        currentHealth += amount;
        currentHealth = Mathf.Clamp(currentHealth, 0, baseStats.maxHealth);
        // Actualizar UI
    }
    public void RestoreFullHealth() { if(baseStats != null) RestoreHealth(baseStats.maxHealth); }

    // Internamente llamado por HealthSystem
    public void SetCurrentHealth(float value)
    {
         if (baseStats == null) return;
         currentHealth = Mathf.Clamp(value, 0, baseStats.maxHealth);
    }
}