using UnityEngine;

public class CharacterData : MonoBehaviour
{
    public CharacterStats baseStats; // Asigna un default en el Inspector del PREFAB

    [HideInInspector] public float currentHealth;
    // Añadir más stats actuales si es necesario

    void Awake()
    {
        // Awake es bueno para obtener referencias, pero puede ser pronto para datos
        // que se asignan externamente tras instanciar.
        if (baseStats == null)
        {
            // Ya no debería ocurrir si asignaste un default en el prefab,
            // pero es una advertencia útil si olvidas hacerlo.
            Debug.LogWarning("CharacterStats no asignado en el prefab o no sobreescrito por el manager ANTES de Start.", this);
        }
    }

    void Start()
    {
         // Start se ejecuta después de todos los Awakes y después de la instanciación completa.
         // Es un lugar más seguro para inicializar valores que dependen de asignaciones externas.
         InitializeHealth();
    }

    // Puedes llamar a esta función si necesitas resetear la salud manualmente
    public void InitializeHealth()
    {
        if (baseStats != null)
        {
            currentHealth = baseStats.maxHealth;
        }
        else
        {
             // Si llegamos aquí, algo falló gravemente (ni default ni asignación externa)
            Debug.LogError("¡CharacterStats SIGUE siendo nulo en Start! No se puede inicializar la salud.", this);
            currentHealth = 1; // Poner un valor mínimo para evitar errores de división por cero, etc.
        }
    }
}