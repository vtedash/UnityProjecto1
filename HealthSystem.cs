using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(CharacterData))]
public class HealthSystem : MonoBehaviour
{
    private CharacterData characterData;
    private bool isDying = false; // Flag para evitar llamadas múltiples a Die()

    [Header("Events")]
    public UnityEvent OnDeath;
    public UnityEvent<float> OnDamageTaken;
    public UnityEvent OnBlockedDamage;
    public UnityEvent OnParriedAttack;
    public UnityEvent OnDodgedDamage;

    void Awake()
    {
        characterData = GetComponent<CharacterData>();
        if (characterData == null)
        {
            Debug.LogError("HealthSystem necesita un CharacterData en el mismo GameObject.", this);
            enabled = false;
        }
    }

    public void TakeDamage(float damageAmount, GameObject attacker)
    {
        // Salir si no hay datos, ya está muerto/muriendo, o daño inválido
        if (characterData == null || isDying || !IsAlive() || damageAmount <= 0) return;

        // --- Comprobaciones Defensivas ---
        if (characterData.isInvulnerable)
        {
            Debug.Log($"{gameObject.name} es INVULNERABLE y evita {damageAmount} de daño.");
            OnDodgedDamage?.Invoke();
            return;
        }

        if (characterData.isAttemptingParry && attacker != null)
        {
             CharacterCombat combat = GetComponent<CharacterCombat>();
             if (combat != null)
             {
                 Debug.Log($"{gameObject.name} intenta PARRY contra {attacker.name}!");
                 combat.NotifySuccessfulParry(attacker);
                 OnParriedAttack?.Invoke();
                 return;
             }
        }

        // --- Aplicar Reducciones (Bloqueo) ---
        float finalDamage = damageAmount;
        bool wasBlocked = false;

        if (characterData.isBlocking && characterData.baseStats != null)
        {
            wasBlocked = true;
            finalDamage *= characterData.baseStats.blockDamageMultiplier;
            Debug.Log($"{gameObject.name} BLOQUEA! Daño original {damageAmount}, reducido a {finalDamage}");

            float extraStaminaCost = damageAmount * characterData.baseStats.blockSuccessStaminaCostMult;
            characterData.ConsumeStamina(extraStaminaCost);
            OnBlockedDamage?.Invoke();
        }

        // --- Aplicar Daño Final ---
        finalDamage = Mathf.Max(0, finalDamage);

        if (finalDamage > 0)
        {
             // Guardar vida *antes* de aplicar daño para check de muerte
             float healthBeforeDamage = characterData.currentHealth;
             characterData.SetCurrentHealth(healthBeforeDamage - finalDamage);
             Debug.Log($"{gameObject.name} recibió {finalDamage} de daño final ({damageAmount} original desde {attacker?.name ?? "Unknown"}). Salud restante: {characterData.currentHealth}");
             OnDamageTaken?.Invoke(finalDamage);

             Animator animator = GetComponent<Animator>();
             // *** CORRECCIÓN AQUÍ: Usar ?. para llamada segura ***
             if (!wasBlocked) animator?.SetTrigger("Hit");
        }
        else if (wasBlocked) {
             Debug.Log($"{gameObject.name} bloqueó TODO el daño.");
        }


        // --- Comprobar Muerte (DESPUÉS de aplicar el daño) ---
        if (characterData.currentHealth <= 0 && !isDying) // Comprobar isDying aquí
        {
             Debug.Log($"Health is <= 0 for {gameObject.name} after damage. Calling Die().");
             Die(attacker);
        }
    }

    public void TakeDamage(float damageAmount)
    {
        TakeDamage(damageAmount, null);
    }

    public void RestoreHealth(float amount)
    {
         if (characterData == null || amount <= 0 || isDying) return; // No curar si está muriendo
         // Solo curar si no está ya al máximo
         if (characterData.currentHealth < characterData.baseStats.maxHealth)
         {
             characterData.RestoreHealth(amount);
             Debug.Log($"{gameObject.name} restauró {amount} de salud. Salud actual: {characterData.currentHealth}");
             // Evento OnHealed?.Invoke();
         }
    }

    private void Die(GameObject killer)
    {
        // *** LÓGICA DE MUERTE CORREGIDA ***
        // 1. Poner flag para evitar reentrada
        if (isDying) return;
        isDying = true;

        // 2. Asegurar que la vida esté en 0 (aunque ya debería estarlo)
        characterData.SetCurrentHealth(0);

        Debug.Log($"Die() method EXECUTING for {gameObject.name}.");
        Debug.Log($"{gameObject.name} ha muerto (Asesino: {killer?.name ?? "Entorno/Desconocido"}).");

        // 3. Invocar el evento ANTES de desactivar cosas
        Debug.Log($"Attempting to invoke OnDeath for {gameObject.name}...");
        OnDeath?.Invoke();
        Debug.Log($"OnDeath invoked for {gameObject.name}.");

        // 4. Desactivar componentes principales
        CharacterCombat combat = GetComponent<CharacterCombat>();
        if (combat != null) combat.enabled = false;

        Pathfinding.AIPath aiPath = GetComponent<Pathfinding.AIPath>();
        if (aiPath != null) aiPath.canMove = false; // Asegurar que se detenga

        BrutoAIController aiController = GetComponent<BrutoAIController>();
        if (aiController != null) aiController.enabled = false; // Desactivar IA

        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;

        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null) rb.simulated = false;

        // 5. Trigger animación de muerte (con chequeo)
        Animator animator = GetComponent<Animator>();
        // *** CORRECCIÓN AQUÍ: Usar ?. para llamada segura ***
        animator?.SetTrigger("Die");

        // 6. Programar destrucción
        Destroy(gameObject, 3.0f);
        Debug.Log($"Destroy scheduled for {gameObject.name}");
    }

    public bool IsAlive()
    {
        // Considerar que no está vivo si está en proceso de morir
        return characterData != null && characterData.currentHealth > 0 && !isDying;
    }
}