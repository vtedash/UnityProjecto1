using UnityEngine;
using UnityEngine.Events;
using Pathfinding; // <--- AÑADIDO using Pathfinding;

[RequireComponent(typeof(CharacterData))]
public class HealthSystem : MonoBehaviour
{
    private CharacterData characterData;
    private bool isDying = false;

    // Propiedades Públicas
    public float CurrentHealth => characterData != null ? characterData.currentHealth : 0;
    public float MaxHealth => characterData != null ? characterData.baseMaxHealth : 100; // Usa baseMaxHealth

    // Eventos
    [Header("Events")]
    public UnityEvent OnDeath;
    public UnityEvent<float> OnDamageTaken;
    public UnityEvent OnBlockedDamage;
    public UnityEvent OnParriedAttack;
    public UnityEvent OnDodgedDamage;
    [Space]
    public UnityEvent<float, float> OnHealthChanged;

    void Awake()
    {
        characterData = GetComponent<CharacterData>();
        if (characterData == null) {
            Debug.LogError("HealthSystem necesita un CharacterData.", this);
            enabled = false;
        }
    }

    // Llamado desde CharacterData.InitializeResourcesAndCooldowns
    public void TriggerInitialHealthChangedEvent()
    {
        if (characterData != null) {
            OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);
        } else {
             Debug.LogError($"[{gameObject.name}] HealthSystem: CharacterData null en TriggerInitialHealthChangedEvent!");
        }
    }

    public void TakeDamage(float damageAmount, GameObject attacker)
    {
        if (characterData == null || isDying || !IsAlive() || damageAmount <= 0) return;

        if (characterData.isInvulnerable) { OnDodgedDamage?.Invoke(); return; }
        if (characterData.isAttemptingParry && attacker != null) {
             CharacterCombat combat = GetComponent<CharacterCombat>();
             if (combat != null) { combat.NotifySuccessfulParry(attacker); OnParriedAttack?.Invoke(); return; }
        }

        float originalHealth = CurrentHealth;
        float finalDamage = damageAmount;
        bool wasBlocked = false;

        // Lógica de Bloqueo (Usa stats base de CharacterData)
        if (characterData.isBlocking)
        {
            wasBlocked = true;
            finalDamage *= characterData.baseBlockDamageMultiplier;
            float extraStaminaCost = damageAmount * characterData.baseBlockSuccessStaminaCostMult;
            characterData.ConsumeStamina(extraStaminaCost);
            OnBlockedDamage?.Invoke();
        }

        finalDamage = Mathf.Max(0, finalDamage);

        if (finalDamage > 0)
        {
             characterData.SetCurrentHealth(originalHealth - finalDamage);
             Debug.Log($"{gameObject.name} received {finalDamage} damage. HP: {CurrentHealth}/{MaxHealth}");
             OnDamageTaken?.Invoke(finalDamage);
             // OnHealthChanged se llama desde characterData.SetCurrentHealth

             if (!wasBlocked) // Animación Hit solo si no bloqueó
             {
                  Animator animator = GetComponent<Animator>();
                  animator?.SetTrigger("Hit");
             }
        }
        else if (wasBlocked)
        {
            Debug.Log($"{gameObject.name} blocked all damage ({damageAmount}).");
        }

        if (CurrentHealth <= 0 && !isDying) { Die(attacker); }
    }

    public void TakeDamage(float damageAmount) { TakeDamage(damageAmount, null); }

    public void RestoreHealth(float amount)
    {
         if (characterData == null || amount <= 0 || isDying) return;
         float healthBefore = CurrentHealth;
         if (healthBefore < MaxHealth && amount > 0)
         {
             characterData.RestoreHealth(amount); // Llama a CharacterData
             // OnHealthChanged se llama desde characterData.RestoreHealth
         }
    }

    private void Die(GameObject killer)
    {
        if (isDying) return; isDying = true;

        characterData?.SetCurrentHealth(0); // Asegura vida a 0
        // OnHealthChanged se llama desde characterData.SetCurrentHealth

        Debug.Log($"Die() executing for {gameObject.name}. Killer: {killer?.name ?? "Unknown"}");
        OnDeath?.Invoke();

        // --- Desactivar Componentes ---
        CharacterCombat combat = GetComponent<CharacterCombat>();
        if (combat != null) combat.enabled = false;

        CharacterMovement movement = GetComponent<CharacterMovement>();
        if (movement != null) movement.enabled = false;

        LuchadorAIController aiController = GetComponent<LuchadorAIController>();
        if (aiController != null) aiController.enabled = false;

        // Acceder a AIPath usando el namespace Pathfinding
        Pathfinding.AIPath aiPath = GetComponent<Pathfinding.AIPath>(); // Corregido aquí
        if (aiPath != null) { aiPath.canSearch = false; aiPath.canMove = false; }

        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;

        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null) { rb.simulated = false; rb.linearVelocity = Vector2.zero; rb.angularVelocity = 0f;}

        Animator animator = GetComponent<Animator>();
        animator?.SetTrigger("Die");

        Destroy(gameObject, 3.0f);
        Debug.Log($"Destroy scheduled for {gameObject.name}");
    }

    public bool IsAlive() { return CurrentHealth > 0 && !isDying; }
}