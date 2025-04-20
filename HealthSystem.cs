// File: HealthSystem.cs
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(CharacterData))]
public class HealthSystem : MonoBehaviour
{
    private CharacterData characterData;
    private bool isDying = false; // Flag para evitar llamadas múltiples a Die()

    [Header("Events")]
    public UnityEvent OnDeath; // Evento cuando la salud llega a 0
    public UnityEvent<float> OnDamageTaken; // Evento al recibir daño (pasa la cantidad)
    public UnityEvent OnBlockedDamage; // Evento si el daño fue bloqueado
    public UnityEvent OnParriedAttack; // Evento si el ataque fue parado con parry
    public UnityEvent OnDodgedDamage; // Evento si el daño fue evitado por invulnerabilidad

    void Awake()
    {
        characterData = GetComponent<CharacterData>();
        if (characterData == null)
        {
            Debug.LogError("HealthSystem necesita un CharacterData en el mismo GameObject.", this);
            enabled = false;
        }
    }

    // Método principal para recibir daño
    public void TakeDamage(float damageAmount, GameObject attacker)
    {
        // Salir si no hay datos, ya está muerto/muriendo, o daño inválido
        if (characterData == null || isDying || !IsAlive() || damageAmount <= 0) return;

        // Comprobaciones Defensivas
        if (characterData.isInvulnerable) // Si es invulnerable (ej. durante dash)
        {
            Debug.Log($"{gameObject.name} es INVULNERABLE y evita {damageAmount} de daño.");
            OnDodgedDamage?.Invoke(); // Invocar evento de esquiva
            return; // No aplicar daño
        }

        if (characterData.isAttemptingParry && attacker != null) // Si está intentando hacer parry
        {
             CharacterCombat combat = GetComponent<CharacterCombat>();
             if (combat != null)
             {
                 Debug.Log($"{gameObject.name} intenta PARRY contra {attacker.name}!");
                 combat.NotifySuccessfulParry(attacker); // Notificar al Combat que el parry fue exitoso
                 OnParriedAttack?.Invoke(); // Invocar evento de parry
                 return; // No aplicar daño
             }
        }

        // Aplicar Reducciones (Bloqueo)
        float finalDamage = damageAmount;
        bool wasBlocked = false;

        if (characterData.isBlocking && characterData.baseStats != null) // Si está bloqueando
        {
            wasBlocked = true;
            finalDamage *= characterData.baseStats.blockDamageMultiplier; // Reducir daño según multiplicador
            Debug.Log($"{gameObject.name} BLOQUEA! Daño original {damageAmount}, reducido a {finalDamage}");

            // Consumir stamina extra por bloqueo exitoso
            float extraStaminaCost = damageAmount * characterData.baseStats.blockSuccessStaminaCostMult;
            characterData.ConsumeStamina(extraStaminaCost);
            OnBlockedDamage?.Invoke(); // Invocar evento de bloqueo
        }

        // Aplicar Daño Final
        finalDamage = Mathf.Max(0, finalDamage); // Asegurar que el daño no sea negativo

        if (finalDamage > 0) // Si todavía hay daño después de reducciones
        {
             float healthBeforeDamage = characterData.currentHealth; // Guardar vida antes para lógica
             characterData.SetCurrentHealth(healthBeforeDamage - finalDamage); // Actualizar vida en CharacterData
             Debug.Log($"{gameObject.name} recibió {finalDamage} de daño final ({damageAmount} original desde {attacker?.name ?? "Unknown"}). Salud restante: {characterData.currentHealth}");
             OnDamageTaken?.Invoke(finalDamage); // Invocar evento de daño recibido

             Animator animator = GetComponent<Animator>();
             if (!wasBlocked) animator?.SetTrigger("Hit"); // Trigger animación de golpe si no fue bloqueado
        }
        else if (wasBlocked) { // Si fue bloqueado y el daño final es 0
             Debug.Log($"{gameObject.name} bloqueó TODO el daño.");
        }


        // Comprobar Muerte (DESPUÉS de aplicar el daño)
        if (characterData.currentHealth <= 0 && !isDying) // Comprobar flag isDying para evitar reentrada
        {
             Debug.Log($"Health is <= 0 for {gameObject.name} after damage. Calling Die().");
             Die(attacker); // Llamar a la función de muerte
        }
    }

    // Sobrecarga para permitir daño sin especificar atacante
    public void TakeDamage(float damageAmount)
    {
        TakeDamage(damageAmount, null);
    }

    // Restaura una cantidad de salud
    public void RestoreHealth(float amount)
    {
         if (characterData == null || amount <= 0 || isDying) return; // No curar si está muriendo o cantidad inválida
         if (characterData.currentHealth < characterData.baseStats.maxHealth) // Solo curar si no está ya al máximo
         {
             characterData.RestoreHealth(amount); // Llama a la función en CharacterData
             Debug.Log($"{gameObject.name} restauró {amount} de salud. Salud actual: {characterData.currentHealth}");
             // Podrías añadir un evento OnHealed?.Invoke(); aquí si lo necesitas
         }
    }

    // Lógica que se ejecuta al morir
    private void Die(GameObject killer)
    {
        // 1. Poner flag para evitar reentrada si Die() es llamado múltiples veces rápidamente
        if (isDying) return;
        isDying = true;

        // 2. Asegurar que la vida esté exactamente en 0
        characterData.SetCurrentHealth(0);

        Debug.Log($"Die() method EXECUTING for {gameObject.name}.");
        Debug.Log($"{gameObject.name} ha muerto (Asesino: {killer?.name ?? "Entorno/Desconocido"}).");

        // 3. Invocar el evento OnDeath ANTES de desactivar componentes
        Debug.Log($"Attempting to invoke OnDeath for {gameObject.name}...");
        OnDeath?.Invoke();
        Debug.Log($"OnDeath invoked for {gameObject.name}.");

        // 4. Desactivar componentes principales para detener acciones
        CharacterCombat combat = GetComponent<CharacterCombat>();
        if (combat != null) combat.enabled = false;

        Pathfinding.AIPath aiPath = GetComponent<Pathfinding.AIPath>();
        if (aiPath != null) aiPath.canMove = false; // Asegurar que A* se detenga

        LuchadorAIController aiController = GetComponent<LuchadorAIController>();
        if (aiController != null) aiController.enabled = false; // Desactivar script de IA

        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false; // Desactivar colisiones

        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null) rb.simulated = false; // Detener simulación física

        // 5. Trigger animación de muerte
        Animator animator = GetComponent<Animator>();
        animator?.SetTrigger("Die");

        // 6. Programar destrucción del GameObject después de un tiempo (para animación)
        Destroy(gameObject, 3.0f); // Ajustar tiempo según duración de animación de muerte
        Debug.Log($"Destroy scheduled for {gameObject.name}");
    }

    // Devuelve true si el personaje tiene salud > 0 y no está en proceso de morir
    public bool IsAlive()
    {
        return characterData != null && characterData.currentHealth > 0 && !isDying;
    }
}