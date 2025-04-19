using UnityEngine;

// Asegura que los componentes necesarios estén presentes
[RequireComponent(typeof(CharacterMovement))]
[RequireComponent(typeof(CharacterCombat))]
[RequireComponent(typeof(HealthSystem))]
[RequireComponent(typeof(CharacterData))] // Añadido para asegurar que CharacterData existe

public class BrutoAIController : MonoBehaviour
{
    // Estados posibles de la IA
    public enum AIState { Idle, Seeking, Attacking, Dead }

    [Header("State & Targeting")]
    public AIState currentState = AIState.Idle; // Estado actual visible en Inspector
    public string enemyTag = "Player";         // Etiqueta del equipo enemigo a buscar

    [Header("AI Jump Settings")]
    public float jumpDecisionCooldown = 1.0f;      // Tiempo mínimo entre decisiones de salto
    public float minHeightDifferenceToJump = 1.5f; // Cuánto más alto debe estar el enemigo para saltar
    public float maxHorizontalDistanceToJump = 3.0f;// Cuán cerca horizontalmente debe estar para intentar saltar

    [Header("AI Combat Settings")]
    public float engagementRangeBuffer = 0.2f; // Margen extra para dejar de atacar (evita oscilación)

    // Referencias internas a otros componentes del mismo GameObject
    private CharacterMovement movement;
    private CharacterCombat combat;
    private HealthSystem health;
    private CharacterData characterData; // Referencia a los datos (para attackRange)

    // Información sobre el objetivo actual
    private Transform currentTarget;
    private HealthSystem targetHealth;

    // Control de tiempos para decisiones
    private float lastJumpDecisionTime;

    // Se ejecuta una vez al inicio, antes de Start()
    void Awake()
    {
        // Obtener referencias a los componentes requeridos
        movement = GetComponent<CharacterMovement>();
        combat = GetComponent<CharacterCombat>();
        health = GetComponent<HealthSystem>();
        characterData = GetComponent<CharacterData>();

        // Verificar si todos los componentes esenciales fueron encontrados
        if (characterData == null || movement == null || combat == null || health == null)
        {
            Debug.LogError("BrutoAIController en " + gameObject.name + " no encontró todos los componentes requeridos (Data, Movement, Combat, Health). Desactivando IA.", this);
            enabled = false; // Desactivar este script si falta algo esencial
        }
    }

    // Se ejecuta después de todos los Awake(), antes del primer Update()
    void Start()
    {
        // Si el script sigue activo (Awake no falló)
        if (enabled)
        {
             // Suscribirse al evento OnDeath del HealthSystem
             health.OnDeath.AddListener(HandleDeath);
             // Inicializar el tiempo del último salto para permitir uno al inicio si es necesario
             lastJumpDecisionTime = -jumpDecisionCooldown;
             // Empezar en el estado de búsqueda
             ChangeState(AIState.Seeking);
        }
    }

    // Se ejecuta en cada frame
    void Update()
    {
        // Si estamos muertos, no hay nada más que hacer
        if (currentState == AIState.Dead) return;

        // ----- Gestión Centralizada del Objetivo -----
        bool targetStillValid = IsTargetValid();
        if (!targetStillValid)
        {
            FindTarget(); // Intentar encontrar un nuevo objetivo
            // Si después de buscar NO encontramos objetivo...
            if (currentTarget == null)
            {
                 // ...cambiamos a Idle si no estábamos ya en Idle o Muerto.
                 if(currentState != AIState.Idle && currentState != AIState.Dead) ChangeState(AIState.Idle);
                 // Detenemos la ejecución de este Update porque no hay objetivo que procesar.
                 return;
            }
            // Si encontramos un NUEVO objetivo, podríamos necesitar re-evaluar el estado (p.ej., si estábamos atacando y ahora está fuera de rango)
            // La lógica del switch se encargará de esto.
        }
        // ----- Fin Gestión del Objetivo -----

        // ----- Máquina de Estados Principal -----
        switch (currentState)
        {
            case AIState.Idle:
                // Si estamos en Idle pero ahora SÍ tenemos un objetivo válido, empezamos a buscarlo.
                if (currentTarget != null) ChangeState(AIState.Seeking);
                // Nota: El movimiento ya está detenido por la lógica de entrada a Idle en ChangeState.
                break;

            case AIState.Seeking:
                // La validación/búsqueda del objetivo ya se hizo arriba.

                // Intentar saltar si las condiciones son adecuadas
                TryJumpLogic();

                // ¿Hemos alcanzado el rango de ataque del objetivo actual?
                if (combat.IsTargetInRange(currentTarget))
                {
                    ChangeState(AIState.Attacking); // Cambiar a estado de ataque
                }
                // Si no, el componente CharacterMovement ya se está encargando de movernos hacia el objetivo en FixedUpdate.
                break;

            case AIState.Attacking:
                // La validación del objetivo ya se hizo arriba.

                // --- Lógica de Desenganche (Histeresis) ---
                // Calcular distancia actual al objetivo
                float distanceToTarget = Vector2.Distance(transform.position, currentTarget.position);
                // ¿Se ha alejado MÁS ALLÁ del rango de ataque + el buffer?
                if (distanceToTarget > (characterData.baseStats.attackRange + engagementRangeBuffer))
                {
                     ChangeState(AIState.Seeking); // Volver a perseguir
                     // Importante: 'break' aquí para no intentar atacar en el mismo frame que cambiamos a Seeking
                     break;
                }
                // ------------------------------------------

                // Si seguimos en rango (y el objetivo es válido), intentar atacar.
                // La lógica de cooldown está dentro de combat.Attack()
                combat.Attack(targetHealth);
                break;

            // El estado Dead se maneja al principio del Update (simplemente no hace nada)
        }
    }

    // Verifica si el objetivo actual sigue siendo válido (existe y está vivo)
    bool IsTargetValid()
    {
        // No hay objetivo, no es válido
        if (currentTarget == null) return false;
        // No tenemos referencia a su salud (raro, pero posible), no es válido
        if (targetHealth == null) return false;
        // El objetivo ya no está vivo, no es válido
        if (!targetHealth.IsAlive()) return false;

        // Si pasó todas las comprobaciones, el objetivo es válido
        return true;
    }


    // Intenta decidir si saltar y ejecuta el salto si corresponde
    void TryJumpLogic()
    {
         // Comprobar cooldown, si estamos en suelo y tenemos objetivo
         if (Time.time >= lastJumpDecisionTime + jumpDecisionCooldown && movement.IsGrounded() && currentTarget != null)
         {
             float verticalDiff = currentTarget.position.y - transform.position.y;
             float horizontalDist = Mathf.Abs(currentTarget.position.x - transform.position.x);

             // Condición para saltar: Objetivo más alto y cercano horizontalmente
             if (verticalDiff > minHeightDifferenceToJump && horizontalDist < maxHorizontalDistanceToJump)
             {
                 Debug.Log(gameObject.name + " intenta saltar hacia " + currentTarget.name);
                 if (movement.Jump()) // Llama a Jump() y comprueba si se ejecutó (estaba en suelo)
                 {
                    lastJumpDecisionTime = Time.time; // Reiniciar cooldown solo si el salto fue posible
                 }
             }
         }
    }

    // Busca el enemigo más cercano con la tag correcta que esté vivo
    void FindTarget()
    {
        GameObject[] potentialTargets = GameObject.FindGameObjectsWithTag(enemyTag);
        Transform closestTarget = null;
        float minDistance = float.MaxValue;
        Transform previousTarget = currentTarget; // Guardar referencia al objetivo anterior para comparar

        foreach (GameObject potentialTarget in potentialTargets)
        {
            // Ignorar si es este mismo GameObject
            if (potentialTarget == gameObject) continue;

            // Comprobar si el objetivo potencial está vivo
            HealthSystem potentialHealth = potentialTarget.GetComponent<HealthSystem>();
            if (potentialHealth == null || !potentialHealth.IsAlive()) continue;

            // Calcular distancia
            float distance = Vector2.Distance(transform.position, potentialTarget.transform.position);

            // Si es el más cercano hasta ahora, registrarlo
            if (distance < minDistance)
            {
                minDistance = distance;
                closestTarget = potentialTarget.transform;
            }
        }

         // --- Actualizar Objetivo ---
         currentTarget = closestTarget; // Asignar el más cercano (o null si no se encontró ninguno)

         if (currentTarget != null) // Si encontramos un objetivo
         {
             targetHealth = currentTarget.GetComponent<HealthSystem>(); // Actualizar referencia de salud

             // Notificar a los otros componentes sobre el nuevo objetivo
             movement.SetTarget(currentTarget);
             combat.SetTarget(targetHealth);

             // Loguear solo si el objetivo es diferente al que teníamos antes
             if(currentTarget != previousTarget)
             {
                 Debug.Log(gameObject.name + " encontró/cambió objetivo: " + currentTarget.name);
             }
         }
         else // Si no encontramos ningún objetivo válido (closestTarget es null)
         {
              // Loguear solo si *antes* teníamos un objetivo y ahora no
              if(previousTarget != null)
              {
                 Debug.Log(gameObject.name + " no encontró más objetivos válidos.");
              }
              // Limpiar referencias y notificar a los componentes que no hay objetivo
              targetHealth = null;
              movement.SetTarget(null);
              combat.SetTarget(null);
         }
    }


    // Gestiona las transiciones entre estados y la lógica de entrada/salida
    void ChangeState(AIState newState)
    {
        // Evitar transiciones innecesarias al mismo estado (importante para no reiniciar lógica)
        if (currentState == newState) return;

        // --- Lógica de Salida del Estado Anterior ---
        // (Se ejecuta ANTES de cambiar el valor de currentState)
        switch (currentState)
        {
             case AIState.Attacking:
                // Si estábamos atacando, asegurarnos de que el movimiento se reactive
                // al salir de este estado (ya sea hacia Seeking o Idle).
                movement.ResumeMovement();
                break;
            // Añadir más lógica de salida para otros estados si fuera necesario
        }

        // Cambiar al nuevo estado y registrarlo
        currentState = newState;
        Debug.Log(gameObject.name + " cambió al estado: " + newState);

        // --- Lógica de Entrada al Nuevo Estado ---
        // (Se ejecuta DESPUÉS de cambiar el valor de currentState)
        switch (currentState)
        {
            case AIState.Idle:
                movement.StopMovement(); // Detener movimiento horizontal al estar inactivo
                break;
            case AIState.Seeking:
                movement.ResumeMovement(); // Asegurar que puede moverse al empezar a buscar/perseguir
                break;
            case AIState.Attacking:
                // --- ¡¡AQUÍ SE DETIENE LA OSCILACIÓN!! ---
                movement.StopMovement(); // Detener movimiento horizontal para poder atacar sin salirse del rango
                // ---------------------------------------
                break;
             case AIState.Dead:
                // Acciones definitivas al morir
                movement.StopMovement(); // Detener cualquier movimiento residual
                combat.DisableAttack();  // Asegurar que no pueda atacar más
                // Desactivar simulación física y colisiones para que el cuerpo quede inerte
                Rigidbody2D rb = GetComponent<Rigidbody2D>();
                if (rb != null) rb.simulated = false;
                Collider2D col = GetComponent<Collider2D>();
                if (col != null) col.enabled = false;
                break;
        }
    }

    // Método que se suscribe al evento OnDeath de HealthSystem
     void HandleDeath()
     {
          // Solo cambiar a Dead si no lo estábamos ya (evita llamadas múltiples)
          if (currentState != AIState.Dead)
          {
              ChangeState(AIState.Dead);
          }
     }

    // Método llamado automáticamente por Unity cuando el GameObject se destruye
    // Es crucial desuscribirse de eventos para evitar errores de referencia a objetos destruidos
     void OnDestroy()
     {
         // Comprobar si la referencia a health es válida antes de intentar desuscribirse
         if (health != null)
         {
             // Usar RemoveListener es seguro incluso si no estaba suscrito
             health.OnDeath.RemoveListener(HandleDeath);
         }
     }
}