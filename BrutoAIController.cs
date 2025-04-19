using UnityEngine;
using Pathfinding; // Necesario para A* Pathfinding Project
using System.Collections;
using Random = UnityEngine.Random;

// Asegura que los componentes necesarios estén presentes .
[RequireComponent(typeof(Seeker))]
[RequireComponent(typeof(AIPath))]
[RequireComponent(typeof(CharacterCombat))]
[RequireComponent(typeof(HealthSystem))]
[RequireComponent(typeof(CharacterData))]
[RequireComponent(typeof(Rigidbody2D))]

public class BrutoAIController : MonoBehaviour
{
    // --- ESTADOS POSIBLES DE LA IA ---
    public enum AIState { Idle, Seeking, Attacking, Fleeing, Celebrating, Dead }

    [Header("State & Targeting")]
    [Tooltip("Estado actual de la IA (solo lectura en runtime)")]
    public AIState currentState = AIState.Idle;
    [Tooltip("Tag del equipo enemigo a buscar")]
    public string enemyTag = "Player";

    [Header("AI Pathfinding Settings")]
    [Tooltip("Con qué frecuencia (segundos) la IA recalcula su camino hacia el objetivo")]
    public float pathUpdateRate = 0.5f;

    [Header("AI Jump Settings")]
    [Tooltip("Tiempo mínimo entre decisiones de salto manual")]
    public float jumpDecisionCooldown = 1.0f;
    [Tooltip("Cuánto más alto debe estar el enemigo para considerar un salto manual")]
    public float minHeightDifferenceToJump = 1.5f;
    [Tooltip("Cuán cerca horizontalmente debe estar para intentar un salto manual")]
    public float maxHorizontalDistanceToJump = 3.0f;
    [Tooltip("Distancia hacia arriba para comprobar obstáculos antes de un salto manual")]
    public float jumpObstacleCheckDistance = 2.0f;
    [Tooltip("Fuerza aplicada en un salto manual")]
    public float manualJumpForce = 10f; // Renombrado para claridad

    [Header("AI Combat Settings")]
    [Tooltip("Margen extra para dejar de atacar (evita oscilación)")]
    public float engagementRangeBuffer = 0.2f;
    [Tooltip("Porcentaje de vida (0 a 1) para huir")]
    [Range(0f, 1f)] public float fleeHealthThreshold = 0.25f;

    [Header("AI Celebration Settings")]
    [Tooltip("Duración de la celebración en segundos")]
    public float celebrationDuration = 5.0f;

    [Header("Ground Check Settings (for Manual Jump)")]
    [Tooltip("Punto de origen para detectar el suelo")]
    public Transform groundCheckPoint;
    [Tooltip("Radio del círculo para detectar suelo")]
    public float groundCheckRadius = 0.2f;
    [Tooltip("Capas consideradas como suelo")]
    public LayerMask groundLayer;


    // Referencias a componentes
    private Seeker seeker;
    private AIPath aiPath; // Controla el movimiento A*
    private CharacterCombat combat;
    private HealthSystem health;
    private CharacterData characterData;
    private Rigidbody2D rb; // Necesario para saltos manuales

    // Objetivo
    private Transform currentTarget;
    private HealthSystem targetHealth;

    // Control de tiempos y estados internos
    private float lastPathRequestTime = -1f; // Para el recálculo de camino A*
    private float lastJumpDecisionTime;    // Para el cooldown del salto manual
    private Coroutine celebrationCoroutine;
    private bool isGrounded;               // Estado actual de si está en el suelo

    void Awake()
    {
        // Obtener referencias obligatorias
        seeker = GetComponent<Seeker>();
        aiPath = GetComponent<AIPath>();
        combat = GetComponent<CharacterCombat>();
        health = GetComponent<HealthSystem>();
        characterData = GetComponent<CharacterData>();
        rb = GetComponent<Rigidbody2D>();

        // Verificar componentes
        if (characterData == null || seeker == null || aiPath == null || combat == null || health == null || rb == null)
        {
            Debug.LogError("BrutoAIController en " + gameObject.name + " no encontró todos los componentes requeridos. Desactivando IA.", this);
            enabled = false;
            return; // Salir si falta algo
        }

        // Verificar/Crear GroundCheckPoint
         if (groundCheckPoint == null)
         {
             Transform foundGroundCheck = transform.Find("GroundCheck");
             if (foundGroundCheck != null) { groundCheckPoint = foundGroundCheck; }
             else
             {
                 GameObject groundCheckObj = new GameObject("GroundCheck");
                 groundCheckObj.transform.SetParent(transform);
                 // Posicionar en base al collider (si existe) o un valor por defecto
                 float yOffset = -(GetComponent<Collider2D>()?.bounds.extents.y ?? 0.5f);
                 groundCheckObj.transform.localPosition = new Vector3(0, yOffset, 0);
                 groundCheckPoint = groundCheckObj.transform;
                 Debug.LogWarning("GroundCheckPoint no asignado en " + gameObject.name + ". Se creó uno. Ajusta su posición.", this);
             }
         }
    }

    void Start()
    {
        if (!enabled) return; // No ejecutar si Awake falló

        health.OnDeath.AddListener(HandleDeath);
        // Configurar AIPath con stats iniciales
        if(characterData.baseStats != null)
        {
            aiPath.maxSpeed = characterData.baseStats.movementSpeed;
            // Podrías configurar otros parámetros de AIPath aquí si vienen de stats
            // aiPath.endReachedDistance = characterData.baseStats.attackRange * 0.9f; // Por ejemplo
        }

        lastJumpDecisionTime = -jumpDecisionCooldown;
        ChangeState(AIState.Seeking);
    }

    void Update()
    {
        if (currentState == AIState.Dead || !enabled) return;

        // Actualizar estado 'isGrounded' CADA frame
        CheckIfGrounded();

        // ----- Comprobación Prioritaria: HUIR -----
        CheckFleeCondition();
        if (currentState == AIState.Fleeing) return; // Salir si cambiamos a huir
        // --------------------------------------------

        // ----- Gestión Centralizada del Objetivo -----
        bool targetStillValid = IsTargetValid();
        if (!targetStillValid && (currentState == AIState.Seeking || currentState == AIState.Attacking))
        {
            FindTarget(); // Busca uno nuevo
            if (currentTarget == null) // Si sigue sin haber objetivo
            {
                 if(currentState != AIState.Idle) ChangeState(AIState.Idle);
                 return; // No hay nada que hacer este frame
            }
             // Si encontramos uno nuevo, forzar recálculo de camino
             lastPathRequestTime = -pathUpdateRate; // Para que se recalcule en la siguiente comprobación
        }
        // ----- Fin Gestión del Objetivo -----

        // ----- Recalcular Camino Periódicamente -----
        // Solo si estamos buscando o huyendo y tenemos un objetivo
        if ((currentState == AIState.Seeking || currentState == AIState.Fleeing) && currentTarget != null)
        {
             if (Time.time > lastPathRequestTime + pathUpdateRate)
             {
                 RequestPathToTarget();
                 lastPathRequestTime = Time.time;
             }
        }
        // ----- Fin Recalcular Camino -----

        // ----- Máquina de Estados Principal -----
        // Comprobación de seguridad: Estados que requieren objetivo
        if ((currentState == AIState.Seeking || currentState == AIState.Attacking || currentState == AIState.Fleeing) && currentTarget == null)
        {
            ChangeState(AIState.Idle); // Si perdimos el objetivo, ir a Idle
            return;
        }

        switch (currentState)
        {
            case AIState.Idle:
                if (currentTarget != null) ChangeState(AIState.Seeking);
                break;

            case AIState.Seeking:
                // Intentar salto manual si es necesario/ventajoso
                TryManualJumpLogic();

                // ¿Hemos llegado al destino según AIPath o estamos muy cerca?
                if (aiPath.reachedDestination || combat.IsTargetInRange(currentTarget)) // Comprobar ambos
                {
                     // Verificar si realmente estamos en rango para atacar
                     if(combat.IsTargetInRange(currentTarget))
                     {
                        ChangeState(AIState.Attacking);
                     }
                     // Si aiPath llegó pero no estamos en rango, puede que el stopping distance sea grande
                     // o necesitemos ajustar la lógica. Por ahora, solo atacamos si IsTargetInRange es true.
                }
                // Si no, AIPath sigue moviendo al personaje
                break;

            case AIState.Attacking:
                // Lógica de Desenganche (Histeresis)
                float distanceToTarget = Vector2.Distance(transform.position, currentTarget.position);
                if (distanceToTarget > (characterData.baseStats.attackRange + engagementRangeBuffer))
                {
                     ChangeState(AIState.Seeking);
                     break;
                }
                // Intentar atacar
                combat.Attack(targetHealth);
                // El movimiento está detenido por ChangeState
                break;

             case AIState.Fleeing:
                 // AIPath se encarga de moverse al punto de huida calculado en RequestPathToTarget
                 // Opcional: Intentar salto manual para evadir
                 TryManualJumpLogic();
                 break;

             case AIState.Celebrating:
                 // Saltar aleatoriamente si está en el suelo
                 if (isGrounded && Random.Range(0f, 1f) < 0.1f)
                 {
                     ManualJump();
                 }
                 break;
        }
    }

    // Actualiza la variable isGrounded
    private void CheckIfGrounded()
    {
        if (groundCheckPoint == null) return;
        isGrounded = Physics2D.OverlapCircle(groundCheckPoint.position, groundCheckRadius, groundLayer);
    }

    // Verifica si el objetivo actual es válido
    bool IsTargetValid()
    {
        if (currentTarget == null) return false;
        if (targetHealth == null) targetHealth = currentTarget.GetComponent<HealthSystem>();
        if (targetHealth == null || !targetHealth.IsAlive()) return false;
        return true;
    }

    // Comprueba si debe huir
    void CheckFleeCondition()
    {
        if (currentState == AIState.Fleeing || currentState == AIState.Dead || currentState == AIState.Celebrating) return;
        if (characterData.baseStats != null && characterData.baseStats.maxHealth > 0)
        {
            float currentHealthPercentage = characterData.currentHealth / characterData.baseStats.maxHealth;
            if (currentHealthPercentage <= fleeHealthThreshold)
            {
                if (!IsTargetValid()) FindTarget(); // Buscar de quién huir si no lo sabemos
                if(IsTargetValid())
                {
                    ChangeState(AIState.Fleeing);
                    lastPathRequestTime = -pathUpdateRate; // Forzar recálculo inmediato de ruta de huida
                }
            }
        }
    }


    // Intenta decidir si realizar un salto MANUAL y lo ejecuta
    void TryManualJumpLogic()
    {
         // Usamos la variable 'isGrounded' actualizada en Update
         if (Time.time >= lastJumpDecisionTime + jumpDecisionCooldown && isGrounded && currentTarget != null)
         {
             float verticalDiff = currentTarget.position.y - transform.position.y;
             float horizontalDist = Mathf.Abs(currentTarget.position.x - transform.position.x);

             // Condición para saltar: Objetivo más alto y cercano
             if (verticalDiff > minHeightDifferenceToJump && horizontalDist < maxHorizontalDistanceToJump)
             {
                 // Comprobación de Obstáculos
                 if (IsJumpPathClear())
                 {
                     //Debug.Log(gameObject.name + " intenta salto MANUAL hacia " + currentTarget.name);
                     if (ManualJump()) // Llama a ManualJump
                     {
                        lastJumpDecisionTime = Time.time;
                     }
                 }
                 else { lastJumpDecisionTime = Time.time - (jumpDecisionCooldown * 0.5f); } // Penalizar menos si está bloqueado
             }
         }
    }

    // Comprueba si hay obstáculos directamente encima para el salto manual
    private bool IsJumpPathClear()
    {
        if (groundCheckPoint == null) return false; // Necesitamos el punto base
        float colliderHeightExtent = GetComponent<Collider2D>()?.bounds.extents.y ?? 0.5f;
        Vector2 checkOrigin = (Vector2)transform.position + Vector2.up * (colliderHeightExtent + 0.05f);
        RaycastHit2D hit = Physics2D.Raycast(checkOrigin, Vector2.up, jumpObstacleCheckDistance, groundLayer);
        //Debug.DrawRay(checkOrigin, Vector2.up * jumpObstacleCheckDistance, (hit.collider != null) ? Color.magenta : Color.cyan, 0.1f);
        return hit.collider == null;
    }


    // Busca el enemigo más cercano
    void FindTarget()
    {
        // ... (Sin cambios respecto a la versión anterior) ...
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

         currentTarget = closestTarget;

         if (currentTarget != null)
         {
             targetHealth = currentTarget.GetComponent<HealthSystem>();
             combat.SetTarget(targetHealth);
             if(currentTarget != previousTarget) Debug.Log(gameObject.name + " encontró/cambió objetivo: " + currentTarget.name);
             lastPathRequestTime = -pathUpdateRate; // Forzar recálculo al encontrar nuevo target
         }
         else
         {
              if(previousTarget != null) Debug.Log(gameObject.name + " no encontró más objetivos válidos.");
              targetHealth = null;
              aiPath.destination = transform.position; // Decirle a AIPath que se quede quieto
              aiPath.canMove = false;
              combat.SetTarget(null);
         }
    }

     // Solicita un nuevo camino al Seeker
    void RequestPathToTarget()
    {
        if (seeker.IsDone() && currentTarget != null && aiPath.canSearch) // Comprobar si puede buscar path
        {
            Vector3 targetPos;
            if (currentState == AIState.Fleeing)
            {
                Vector2 fleeDir = ((Vector2)transform.position - (Vector2)currentTarget.position).normalized;
                targetPos = transform.position + (Vector3)(fleeDir * 10f); // Punto de huida
                // Opcional: Buscar nodo válido más cercano a targetPos
                 targetPos = (Vector3)AstarPath.active.GetNearest(targetPos).position;
            }
            else // Seeking
            {
                targetPos = currentTarget.position;
            }
            seeker.StartPath(transform.position, targetPos, OnPathComplete);
        }
    }

    // Callback cuando el Seeker termina
    public void OnPathComplete(Path p)
    {
        if (p.error) Debug.LogWarning($"{gameObject.name} no pudo calcular camino: {p.errorLog}");
        // AIPath tomará el camino automáticamente si está configurado para hacerlo
    }

    // Gestiona las transiciones entre estados
    void ChangeState(AIState newState)
    {
        if (currentState == newState) return;

        // --- Lógica de Salida ---
        switch (currentState)
        {
             case AIState.Attacking: // Al salir de atacar, siempre permitir moverse de nuevo
                aiPath.canMove = true;
                break;
             case AIState.Fleeing:
                 aiPath.canMove = true; // Asegurar que pueda moverse si deja de huir
                 break;
             case AIState.Celebrating:
                 if (celebrationCoroutine != null) StopCoroutine(celebrationCoroutine);
                 aiPath.canMove = true; // Permitir moverse si deja de celebrar
                 break;
             // Idle, Seeking, Dead no necesitan lógica de salida específica aquí
        }

        currentState = newState;
        Debug.Log(gameObject.name + " cambió al estado: " + newState);

        // --- Lógica de Entrada ---
        switch (currentState)
        {
            case AIState.Idle:
                aiPath.canMove = false;
                aiPath.destination = transform.position; // Evitar deslizamiento
                if(rb != null) rb.linearVelocity = Vector2.zero; // Detener física
                break;
            case AIState.Seeking:
                aiPath.canMove = true;    // Habilitar movimiento A*
                lastPathRequestTime = -pathUpdateRate; // Forzar recálculo inmediato
                break;
            case AIState.Attacking:
                aiPath.canMove = false;   // Detener movimiento A*
                if(rb != null) rb.linearVelocity = Vector2.zero; // Detener física
                break;
             case AIState.Fleeing:
                 aiPath.canMove = true;    // Habilitar movimiento A* para huir
                 lastPathRequestTime = -pathUpdateRate; // Forzar cálculo de ruta de huida
                 break;
             case AIState.Celebrating:
                 aiPath.canMove = false;   // Detener movimiento A*
                 if(rb != null) rb.linearVelocity = Vector2.zero;
                 celebrationCoroutine = StartCoroutine(CelebrateRoutine());
                 break;
             case AIState.Dead:
                aiPath.canMove = false;
                combat.DisableAttack();
                if (rb != null) rb.simulated = false;
                Collider2D col = GetComponent<Collider2D>();
                if (col != null) col.enabled = false;
                StopAllCoroutines();
                break;
        }
    }

    // Corrutina para celebrar
    IEnumerator CelebrateRoutine()
    {
        float startTime = Time.time;
        while(Time.time < startTime + celebrationDuration)
        {
            // La lógica de salto se ejecuta en Update
            yield return null;
        }
        if (currentState == AIState.Celebrating) ChangeState(AIState.Idle);
    }


    // --- Salto Manual (Usa Rigidbody) ---
    public bool ManualJump()
    {
        // 'isGrounded' se actualiza en Update
        if (isGrounded && rb != null && rb.simulated)
        {
            //Debug.Log(gameObject.name + " ejecutando salto manual"); // Log de depuración
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f); // Anular velocidad Y antes de saltar
            rb.AddForce(Vector2.up * manualJumpForce, ForceMode2D.Impulse); // Usar la fuerza definida
            return true;
        }
        return false;
    }
    // ----------------------------------


     void HandleDeath()
     {
          if (currentState != AIState.Dead) ChangeState(AIState.Dead);
     }

     public void StartCelebrating()
     {
          if (currentState != AIState.Dead) ChangeState(AIState.Celebrating);
     }

     void OnDestroy()
     {
         if (health != null) health.OnDeath.RemoveListener(HandleDeath);
         StopAllCoroutines();
     }
}