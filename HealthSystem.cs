// File: HealthSystem.cs
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(CharacterData))]
public class HealthSystem : MonoBehaviour
{
    // --- Componentes y Estado ---
    private CharacterData characterData; // Sigue siendo private
    private bool isDying = false;

    // --- Propiedades Públicas para Acceso Externo (UI, etc.) ---
    public float CurrentHealth => characterData != null ? characterData.currentHealth : 0; // Devuelve la vida actual desde CharacterData
    public float MaxHealth => characterData != null && characterData.baseStats != null ? characterData.baseStats.maxHealth : 100; // Devuelve la vida máx desde CharacterData

    [Header("Events")]
    [Tooltip("Se dispara justo antes de que el personaje sea destruido.")]
    public UnityEvent OnDeath;
    [Tooltip("Se dispara al recibir daño efectivo (después de bloqueos/invulnerabilidad). Pasa la cantidad de daño recibido.")]
    public UnityEvent<float> OnDamageTaken;
    [Tooltip("Se dispara cuando se bloquea daño (incluso si el daño final es 0).")]
    public UnityEvent OnBlockedDamage;
    [Tooltip("Se dispara cuando un ataque es parado con éxito.")]
    public UnityEvent OnParriedAttack;
    [Tooltip("Se dispara cuando el daño es evitado por invulnerabilidad (dash).")]
    public UnityEvent OnDodgedDamage;
    [Space]
    [Tooltip("Se dispara CADA VEZ que la vida cambia (daño o curación). Pasa (vida actual, vida máxima). Ideal para UI.")]
    public UnityEvent<float, float> OnHealthChanged; // Evento para la UI

    void Awake()
    {
        characterData = GetComponent<CharacterData>();
        if (characterData == null) {
            Debug.LogError("HealthSystem necesita un CharacterData en el mismo GameObject.", this);
            enabled = false;
        }
    }

     // Llamado desde CharacterData.Start() para establecer el estado inicial de la UI
     public void TriggerInitialHealthChangedEvent()
     {
         // Usa las propiedades públicas para asegurar que los datos están disponibles
         OnHealthChanged?.Invoke(this.CurrentHealth, this.MaxHealth);
     }

    public void TakeDamage(float damageAmount, GameObject attacker)
    {
        if (characterData == null || isDying || !IsAlive() || damageAmount <= 0) return;
        if (characterData.isInvulnerable) { OnDodgedDamage?.Invoke(); return; }
        if (characterData.isAttemptingParry && attacker != null) {
             CharacterCombat combat = GetComponent<CharacterCombat>();
             if (combat != null) { combat.NotifySuccessfulParry(attacker); OnParriedAttack?.Invoke(); return; }
        }

        float originalHealth = CurrentHealth; // Usa la propiedad pública
        float finalDamage = damageAmount;
        bool wasBlocked = false;

        if (characterData.isBlocking && characterData.baseStats != null) {
            wasBlocked = true;
            finalDamage *= characterData.baseStats.blockDamageMultiplier;
            float extraStaminaCost = damageAmount * characterData.baseStats.blockSuccessStaminaCostMult;
            characterData.ConsumeStamina(extraStaminaCost);
            OnBlockedDamage?.Invoke();
        }

        finalDamage = Mathf.Max(0, finalDamage);
        if (finalDamage > 0) {
             characterData.SetCurrentHealth(originalHealth - finalDamage); // Actualiza CharacterData
             Debug.Log($"{gameObject.name} recibió {finalDamage} daño. Salud: {CurrentHealth}/{MaxHealth}");
             OnDamageTaken?.Invoke(finalDamage);
             OnHealthChanged?.Invoke(CurrentHealth, MaxHealth); // Notifica UI usando propiedades

             Animator animator = GetComponent<Animator>();
             if (!wasBlocked) animator?.SetTrigger("Hit");
        } else if (wasBlocked) { Debug.Log($"{gameObject.name} bloqueó TODO el daño."); }

        if (CurrentHealth <= 0 && !isDying) { Die(attacker); }
    }

    public void TakeDamage(float damageAmount) { TakeDamage(damageAmount, null); }

    public void RestoreHealth(float amount)
    {
         if (characterData == null || amount <= 0 || isDying || characterData.baseStats == null) return;
         float healthBefore = CurrentHealth;
         if (healthBefore < MaxHealth && amount > 0) {
             characterData.RestoreHealth(amount);
             float healthAfter = CurrentHealth;
             Debug.Log($"{gameObject.name} restauró {healthAfter - healthBefore} salud. Actual: {healthAfter}/{MaxHealth}");
             OnHealthChanged?.Invoke(healthAfter, MaxHealth); // Notifica UI
         }
    }

    private void Die(GameObject killer)
    {
        if (isDying) return; isDying = true;
        characterData.SetCurrentHealth(0);
        OnHealthChanged?.Invoke(0, MaxHealth); // Última notificación a UI

        Debug.Log($"Die() executing for {gameObject.name}. Killer: {killer?.name ?? "Unknown"}");
        OnDeath?.Invoke();
        Debug.Log($"OnDeath invoked for {gameObject.name}.");

        // Desactivar componentes...
        CharacterCombat combat = GetComponent<CharacterCombat>(); if (combat != null) combat.enabled = false;
        Pathfinding.AIPath aiPath = GetComponent<Pathfinding.AIPath>(); if (aiPath != null) aiPath.canSearch = false; // Previene errores de pathfinding post-muerte
        LuchadorAIController aiController = GetComponent<LuchadorAIController>(); if (aiController != null) aiController.enabled = false;
        Collider2D col = GetComponent<Collider2D>(); if (col != null) col.enabled = false;
        Rigidbody2D rb = GetComponent<Rigidbody2D>(); if (rb != null) { rb.simulated = false; rb.linearVelocity = Vector2.zero; }

        Animator animator = GetComponent<Animator>(); animator?.SetTrigger("Die");
        Destroy(gameObject, 3.0f);
        Debug.Log($"Destroy scheduled for {gameObject.name}");
    }

    public bool IsAlive() { return CurrentHealth > 0 && !isDying; }
}