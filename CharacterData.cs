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

    // Cooldown Timers (Guarda el Time.time en que la acción estará disponible de nuevo)
    private float attackAvailableTime = 0f;
    private float dashAvailableTime = 0f;
    private float parryAvailableTime = 0f;
    public Dictionary<string, float> skillAvailableTimes = new Dictionary<string, float>(); // Cooldowns por ID de habilidad

    // Combat State Flags (Reflejan el estado actual del personaje)
    public bool isBlocking { get; private set; } = false;        // ¿Está activamente bloqueando?
    public bool isDashing { get; private set; } = false;         // ¿Está en medio de un dash?
    public bool isInvulnerable { get; private set; } = false;    // ¿Es inmune al daño (ej. durante dash)?
    public bool isStunned { get; private set; } = false;         // ¿Está aturdido/incapacitado?
    public bool isAttemptingParry { get; private set; } = false; // ¿Está en la ventana activa de parry?
    private float stunEndTime;                                  // Momento en que termina el stun actual

    // --- Referencias (Cache para eficiencia) ---
    private CharacterCombat combatController; // Para llamar a InterruptActions
    private HealthSystem healthSystem;       // Referencia al sistema de vida (aunque raramente se usa desde aquí)
    private AIPath aiPath;                   // Para detener movimiento A* en stun
    private Rigidbody2D rb;                   // Para detener movimiento físico en stun

    void Awake()
    {
        // Obtener referencias a otros componentes importantes
        combatController = GetComponent<CharacterCombat>();
        healthSystem = GetComponent<HealthSystem>();
        aiPath = GetComponent<AIPath>(); // Puede ser null si no se usa
        rb = GetComponent<Rigidbody2D>();

        if (baseStats == null)
        {
            Debug.LogWarning("CharacterStats no asignado en el prefab o no sobreescrito antes de Start.", this);
        }
        // La inicialización se mueve a Start para dar tiempo a que los stats se asignen externamente si es necesario
    }

    void Start()
    {
        // Inicializa salud, stamina y cooldowns basados en los baseStats
        InitializeResourcesAndCooldowns();
    }


    void Update()
    {
        // Procesar estados temporales que dependen del tiempo
        CheckStunEnd(); // Comprobar si el aturdimiento ha terminado

        // Regenerar recursos si el personaje no está impedido
        if (!isStunned && !isDashing) // No regenerar si está stuneado o dasheando
        {
            RegenerateStamina(Time.deltaTime);
            // RegenerateMana(Time.deltaTime); // Si usas mana
        }
    }

    // Inicializa los recursos (vida, stamina) y resetea los cooldowns
    public void InitializeResourcesAndCooldowns()
    {
        if (baseStats != null) // Solo si tenemos stats asignados
        {
            currentHealth = baseStats.maxHealth;
            currentStamina = baseStats.maxStamina;
            // currentMana = baseStats.maxMana; // Si usas mana

            // Inicializar cooldowns (disponibles al inicio de la partida/ronda)
            float startTime = Time.time; // O -cooldown si quieres que estén listos un poco antes
            attackAvailableTime = startTime;
            dashAvailableTime = startTime;
            parryAvailableTime = startTime;

            // Inicializar cooldowns de habilidades
            skillAvailableTimes.Clear();
            foreach (var skill in skills)
            {
                if (skill != null && !skillAvailableTimes.ContainsKey(skill.skillID))
                {
                    skillAvailableTimes.Add(skill.skillID, startTime);
                }
            }
        }
        else // Error si no hay stats en Start (crítico)
        {
            Debug.LogError("¡CharacterStats SIGUE siendo nulo en Start! No se pueden inicializar recursos/cooldowns.", this);
            currentHealth = 1; // Valor mínimo por defecto para evitar división por cero, etc.
            currentStamina = 0;
            // currentMana = 0;
        }
    }

    // Regenera stamina a lo largo del tiempo
    void RegenerateStamina(float deltaTime)
    {
        // No regenerar si está bloqueando o si la stamina ya está al máximo
        if (baseStats != null && !isBlocking && currentStamina < baseStats.maxStamina)
        {
            currentStamina += baseStats.staminaRegenRate * deltaTime;
            currentStamina = Mathf.Min(currentStamina, baseStats.maxStamina); // No exceder el máximo
            // Aquí podrías actualizar una barra de UI si la tienes
        }
    }

    // Intenta consumir una cantidad de stamina. Devuelve true si fue posible, false si no.
    public bool ConsumeStamina(float amount)
    {
        if (baseStats == null) return false; // No se puede consumir si no hay stats definidos
        if (amount <= 0) return true;       // No consumir si el costo es cero o negativo
        if (currentStamina >= amount)        // Si hay suficiente stamina
        {
            currentStamina -= amount;
            // Actualizar UI si existe
            return true; // Consumo exitoso
        }
        Debug.Log($"{gameObject.name} no tiene suficiente stamina (Necesita: {amount}, Tiene: {currentStamina})");
        return false; // No hay suficiente stamina
    }

    // --- Cooldown Checks (Funciones para saber si una acción está lista) ---
    public bool IsAttackReady() => baseStats != null && Time.time >= attackAvailableTime;
    public bool IsDashReady() => baseStats != null && Time.time >= dashAvailableTime;
    public bool IsParryReady() => baseStats != null && Time.time >= parryAvailableTime;
    public bool IsSkillReady(SkillData skill) =>
        baseStats != null &&                    // Hay stats?
        skill != null &&                        // La habilidad existe?
        skillAvailableTimes.TryGetValue(skill.skillID, out float availableTime) && // Está en el diccionario?
        Time.time >= availableTime;             // Ha pasado el tiempo de cooldown?

    // --- Cooldown Setters (Funciones para poner una acción en cooldown) ---
    public void PutAttackOnCooldown() { if (baseStats != null) attackAvailableTime = Time.time + baseStats.attackCooldown; }
    public void PutDashOnCooldown() { if (baseStats != null) dashAvailableTime = Time.time + baseStats.dashCooldown; }
    public void PutParryOnCooldown() { if (baseStats != null) parryAvailableTime = Time.time + baseStats.parryCooldown; }
    public void PutSkillOnCooldown(SkillData skill)
    {
        if (baseStats != null && skill != null && skillAvailableTimes.ContainsKey(skill.skillID))
        {
            skillAvailableTimes[skill.skillID] = Time.time + skill.cooldown; // Actualizar tiempo en diccionario
        }
    }

    // --- State Setters (Funciones para cambiar los flags de estado, llamadas desde otros scripts) ---
    public void SetBlocking(bool state) {
        if (isStunned && state) return; // No puede empezar a bloquear si está aturdido
        isBlocking = state;
    }
    public void SetDashing(bool state) => isDashing = state;
    public void SetInvulnerable(bool state) => isInvulnerable = state;
    public void SetAttemptingParry(bool state) => isAttemptingParry = state;

    // --- Stun Logic ---
    // Aplica el estado de aturdimiento durante una duración específica
    public void ApplyStun(float duration)
    {
        if (duration <= 0) return; // Ignorar si la duración no es positiva
        isStunned = true;
        // Asegurar que el stun no termine antes de tiempo si se aplica de nuevo mientras ya está stuneado
        stunEndTime = Mathf.Max(stunEndTime, Time.time + duration);

        // Interrumpir acciones actuales (¡Muy Importante!)
        combatController?.InterruptActions(); // Llama al método centralizado en CharacterCombat

        // Detener movimiento inmediatamente
        if (aiPath != null) aiPath.canMove = false; // Detener movimiento A*
        if (rb != null) rb.linearVelocity = Vector2.zero; // Detener movimiento físico

        // Asegurar que los flags de acción se desactiven
        SetBlocking(false);
        SetDashing(false);
        SetAttemptingParry(false);
        // No desactivar isInvulnerable aquí, podría ser un stun durante invulnerabilidad

        Debug.Log($"{gameObject.name} está ATURDIDO por {duration}s (hasta {stunEndTime})");
        // Aquí podrías activar una animación de stun si la tienes
        // animator?.SetBool("IsStunned", true);
    }

    // Comprueba si el tiempo de stun ha terminado
    void CheckStunEnd()
    {
        if (isStunned && Time.time >= stunEndTime)
        {
            isStunned = false;
            // Al salir del stun, la IA (o el input del jugador) debe decidir si reanudar el movimiento.
            // NO se debe reactivar aiPath.canMove automáticamente aquí.
            Debug.Log($"{gameObject.name} ya NO está aturdido.");
            // Desactivar animación de stun
            // animator?.SetBool("IsStunned", false);
        }
    }

    // Restaura una cantidad de vida (usado por habilidades de curación, etc.)
    public void RestoreHealth(float amount)
    {
        if (baseStats == null) return;
        currentHealth += amount;
        currentHealth = Mathf.Clamp(currentHealth, 0, baseStats.maxHealth); // No exceder vida máxima
        // Actualizar UI si existe
    }
    // Restaura toda la vida
    public void RestoreFullHealth() { if(baseStats != null) RestoreHealth(baseStats.maxHealth); }

    // Método interno llamado por HealthSystem para actualizar la vida después de recibir daño
    public void SetCurrentHealth(float value)
    {
         if (baseStats == null) return;
         currentHealth = Mathf.Clamp(value, 0, baseStats.maxHealth); // Asegurar que esté entre 0 y maxHealth
    }
}