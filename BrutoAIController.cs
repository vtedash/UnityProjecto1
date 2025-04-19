using UnityEngine;

[RequireComponent(typeof(CharacterMovement))]
[RequireComponent(typeof(CharacterCombat))]
[RequireComponent(typeof(HealthSystem))]
public class BrutoAIController : MonoBehaviour
{
    public enum AIState { Idle, Seeking, Attacking, Dead }

    [Header("State & Targeting")]
    public AIState currentState = AIState.Idle;
    public string enemyTag = "Player"; // Tag del equipo enemigo a buscar

    [Header("AI Jump Settings")]
    public float jumpDecisionCooldown = 1.0f; // Tiempo mínimo entre decisiones de salto
    public float minHeightDifferenceToJump = 1.5f; // Cuánto más alto debe estar el enemigo para saltar
    public float maxHorizontalDistanceToJump = 3.0f; // Cuán cerca horizontalmente debe estar para intentar saltar

    [Header("AI Combat Settings")]
    public float engagementRangeBuffer = 0.2f; // Margen extra para dejar de atacar (evita oscilación)

    // Referencias a otros componentes
    private CharacterMovement movement;
    private CharacterCombat combat;
    private HealthSystem health;
    private CharacterData characterData; // Necesario para acceder a attackRange

    // Información del objetivo
    private Transform currentTarget;
    private HealthSystem targetHealth;

    // Control de tiempos
    private float lastJumpDecisionTime;

    void Awake()
    {
        // Obtener referencias obligatorias
        movement = GetComponent<CharacterMovement>();
        combat = GetComponent<CharacterCombat>();
        health = GetComponent<HealthSystem>();
        characterData = GetComponent<CharacterData>();

        // Comprobación crucial
        if (characterData == null)
        {
            Debug.LogError("BrutoAIController necesita un componente CharacterData en el mismo GameObject.", this);
            enabled = false; // Desactivar IA si falta un componente esencial
            return; // Salir de Awake si falta
        }
         if (movement == null || combat == null || health == null)
        {
             Debug.LogError("BrutoAIController no encontró todos los componentes requeridos (Movement, Combat, Health).", this);
             enabled = false;
             return;
        }
    }

    void Start()
    {
        // Suscribirse al evento de muerte para limpieza y cambio de estado
        if (health != null) // Comprobación extra por si Awake falló
        {
            health.OnDeath.AddListener(HandleDeath);
        }
        else // Si health es null aquí, algo fue muy mal en Awake
        {
             enabled = false; // No continuar si falta la salud
             return;
        }

        lastJumpDecisionTime = -jumpDecisionCooldown; // Permitir decisión de salto inicial
        ChangeState(AIState.Seeking); // Empezar buscando activamente
    }

    void Update()
    {
        // Si estamos muertos, no hacemos nada más
        if (currentState == AIState.Dead) return;

        // ----- Gestión del Objetivo -----
        bool needNewTarget = false;
        if (currentState == AIState.Seeking || currentState == AIState.Attacking)
        {
            if (currentTarget == null || targetHealth == null || !targetHealth.IsAlive())
            {
                needNewTarget = true;
            }
        }

        if (needNewTarget || (currentState == AIState.Idle && currentTarget == null))
        {
            FindTarget();
            if (currentTarget == null)
            {
                if(currentState != AIState.Idle) ChangeState(AIState.Idle);
                return;
            }
            else if (currentState == AIState.Idle)
            {
                 ChangeState(AIState.Seeking);
                 return;
            }
        }
        // ----- Fin Gestión del Objetivo -----


        // ----- Lógica de Estados -----
        // Asegurarnos de tener objetivo antes de procesar Seeking/Attacking
        if (currentTarget == null)
        {
            // Si no tenemos objetivo pero no estamos en Idle o Dead, algo raro pasó.
            // Forzar búsqueda o ir a Idle como medida de seguridad.
             if (currentState != AIState.Idle && currentState != AIState.Dead)
             {
                FindTarget(); // Intenta buscar de nuevo
                if(currentTarget == null) ChangeState(AIState.Idle); // Si sigue sin encontrar, ir a Idle
             }
             return; // Salir si no hay objetivo para procesar
        }


        switch (currentState)
        {
            case AIState.Idle:
                // Si estamos en Idle pero apareció un objetivo, vamos a buscarlo.
                 ChangeState(AIState.Seeking);
                break;

            case AIState.Seeking:
                TryJumpLogic();

                // Comprobar si hemos llegado al rango de ataque para cambiar a Attacking
                if (combat.IsTargetInRange(currentTarget))
                {
                    ChangeState(AIState.Attacking);
                }
                break;

            case AIState.Attacking:
                // --- Lógica de Desenganche (Histeresis) ---
                float distanceToTarget = Vector2.Distance(transform.position, currentTarget.position);
                // Usar characterData obtenido en Awake (ya comprobamos que no sea null)
                if (distanceToTarget > (characterData.baseStats.attackRange + engagementRangeBuffer))
                {
                     ChangeState(AIState.Seeking);
                     break;
                }
                // ------------------------------------------

                // Intentar atacar
                combat.Attack(targetHealth);
                break;
        }
    }

    // Intenta decidir si saltar y ejecuta el salto si corresponde
    void TryJumpLogic()
    {
         if (Time.time >= lastJumpDecisionTime + jumpDecisionCooldown && movement.IsGrounded() && currentTarget != null)
         {
             float verticalDiff = currentTarget.position.y - transform.position.y;
             float horizontalDist = Mathf.Abs(currentTarget.position.x - transform.position.x);

             if (verticalDiff > minHeightDifferenceToJump && horizontalDist < maxHorizontalDistanceToJump)
             {
                 Debug.Log(gameObject.name + " intenta saltar hacia " + currentTarget.name);
                 if (movement.Jump())
                 {
                    lastJumpDecisionTime = Time.time;
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
        Transform previousTarget = currentTarget;

        foreach (GameObject potentialTarget in potentialTargets)
        {
            if (potentialTarget == gameObject) continue;

            HealthSystem potentialHealth = potentialTarget.GetComponent<HealthSystem>();
            if (potentialHealth == null || !potentialHealth.IsAlive()) continue;

            float distance = Vector2.Distance(transform.position, potentialTarget.transform.position);
            if (distance < minDistance)
            {
                minDistance = distance;
                closestTarget = potentialTarget.transform;
            }
        }

         if (closestTarget != null)
         {
             currentTarget = closestTarget;
             targetHealth = currentTarget.GetComponent<HealthSystem>();

             movement.SetTarget(currentTarget);
             combat.SetTarget(targetHealth);

             if(currentTarget != previousTarget)
             {
                 Debug.Log(gameObject.name + " encontró/cambió objetivo: " + currentTarget.name);
             }
         }
         else // No se encontró ningún objetivo válido
         {
              if(previousTarget != null)
              {
                 Debug.Log(gameObject.name + " no encontró más objetivos válidos.");
              }
              currentTarget = null;
              targetHealth = null;
              movement.SetTarget(null);
              combat.SetTarget(null);
              // No cambiamos a Idle aquí directamente, dejamos que Update lo maneje
         }
    }


    // Gestiona las transiciones entre estados y la lógica de entrada/salida
    void ChangeState(AIState newState)
    {
        if (currentState == newState && currentState != AIState.Seeking) return; // Evitar cambios al mismo estado (excepto Seeking para re-evaluar salto)

        // --- Lógica de Salida del Estado Anterior ---
        switch (currentState)
        {
             case AIState.Attacking:
                movement.ResumeMovement(); // Reanudar movimiento si estaba detenido por atacar
                break;
             // Añadir más casos si es necesario
        }

        // Cambiar al nuevo estado y loguear
        currentState = newState;
        // Evitar log excesivo si se repite Seeking->Seeking
        // if (previousState != AIState.Seeking || newState != AIState.Seeking) {
        //     Debug.Log(gameObject.name + " cambió al estado: " + newState);
        // }


        // --- Lógica de Entrada al Nuevo Estado ---
        switch (currentState)
        {
            case AIState.Idle:
                movement.StopMovement(); // Detener movimiento horizontal
                break;
            case AIState.Seeking:
                movement.ResumeMovement(); // Asegurar que puede moverse
                break;
            case AIState.Attacking:
                // --- !!! ESTA ES LA LÍNEA CLAVE PARA DETENER LA OSCILACIÓN !!! ---
                movement.StopMovement(); // Detener movimiento horizontal al empezar a atacar
                // ------------------------------------------------------------------
                break;
             case AIState.Dead:
                // Acciones al morir: detener todo, desactivar física/colisiones
                movement.StopMovement();
                combat.DisableAttack();
                Rigidbody2D rb = GetComponent<Rigidbody2D>();
                if (rb != null) rb.simulated = false; // Detiene la simulación física
                Collider2D col = GetComponent<Collider2D>();
                if (col != null) col.enabled = false; // Desactiva colisiones
                break;
        }
    }

    // Método llamado por el evento OnDeath de HealthSystem
     void HandleDeath()
     {
          // Asegurarse de no llamar a ChangeState si ya estamos muertos
          if (currentState != AIState.Dead)
          {
              ChangeState(AIState.Dead);
          }
     }

    // Desuscribirse del evento OnDeath al destruir el objeto
     void OnDestroy()
     {
         if (health != null)
         {
             health.OnDeath.RemoveListener(HandleDeath);
         }
     }
}