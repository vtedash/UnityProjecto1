using UnityEngine;
using UnityEngine.Events; // Necesario para UnityEvent

public class HealthSystem : MonoBehaviour
{
    private CharacterData characterData;
    public UnityEvent OnDeath; // Evento para notificar la muerte

    void Awake()
    {
        characterData = GetComponent<CharacterData>();
        if (characterData == null)
        {
            Debug.LogError("HealthSystem necesita un CharacterData en el mismo GameObject.", this);
            enabled = false; // Desactivar si falta el componente esencial
        }
    }

    public void TakeDamage(float damageAmount)
    {
        if (characterData == null || characterData.currentHealth <= 0) return; // Ya está muerto o no hay datos

        characterData.currentHealth -= damageAmount;
        Debug.Log(gameObject.name + " recibió " + damageAmount + " de daño. Salud restante: " + characterData.currentHealth);

        if (characterData.currentHealth <= 0)
        {
            characterData.currentHealth = 0;
            Die();
        }
    }

    private void Die()
    {
        Debug.Log(gameObject.name + " ha muerto.");
        // Disparar el evento de muerte
        OnDeath?.Invoke();

        // Aquí podrías desactivar otros componentes (movimiento, IA),
        // iniciar una animación de muerte, etc.
        // Para el prototipo, simplemente destruiremos el objeto después de un pequeño retraso.
        Destroy(gameObject, 0.5f); // Destruir después de 0.5 segundos
    }

    // Helper para saber si está vivo
    public bool IsAlive()
    {
        return characterData != null && characterData.currentHealth > 0;
    }
}