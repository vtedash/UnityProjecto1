using UnityEngine;

[RequireComponent(typeof(CharacterMovement))]
[RequireComponent(typeof(CharacterCombat))]
[RequireComponent(typeof(HealthSystem))]
public class BrutoAIController : MonoBehaviour
{
    public enum AIState { Idle, Seeking, Attacking, Dead }

    public AIState currentState = AIState.Idle;
    public string enemyTag = "Player";

    private CharacterMovement movement;
    private CharacterCombat combat;
    private HealthSystem health;
    private Transform currentTarget;
    private HealthSystem targetHealth;

    // --- NUEVAS VARIABLES PARA DECISIÓN DE SALTO ---
    [Header("AI Jump Settings")]
    public float jumpDecisionCooldown = 1.0f; // Tiempo mínimo entre decisiones de salto
    public float minHeightDifferenceToJump = 1.5f; // Cuánto más alto debe estar el enemigo para saltar
    public float maxHorizontalDistanceToJump = 3.0f; // Cuán cerca horizontalmente debe estar para intentar saltar

    private float lastJumpDecisionTime;
    // ---------------------------------------------

    void Awake()
    {
        movement = GetComponent<CharacterMovement>();
        combat = GetComponent<CharacterCombat>();
        health = GetComponent<HealthSystem>();
    }

    void Start()
    {
        health.OnDeath.AddListener(HandleDeath);
        ChangeState(AIState.Seeking);
        lastJumpDecisionTime = -jumpDecisionCooldown; // Permitir decisión de salto inicial
    }

    void Update()
    {
        if (currentState == AIState.Dead) return;

        switch (currentState)
        {
            case AIState.Idle:
                FindTarget();
                if (currentTarget != null) ChangeState(AIState.Seeking);
                break;

            case AIState.Seeking:
                if (currentTarget == null || targetHealth == null || !targetHealth.IsAlive())
                {
                    FindTarget();
                    if (currentTarget == null) { ChangeState(AIState.Idle); break; }
                }

                // --- LÓGICA DE DECISIÓN DE SALTO ---
                // Comprobar si es momento de considerar un salto y si estamos en el suelo
                if (Time.time >= lastJumpDecisionTime + jumpDecisionCooldown && movement.IsGrounded())
                {
                    float verticalDiff = currentTarget.position.y - transform.position.y;
                    float horizontalDist = Mathf.Abs(currentTarget.position.x - transform.position.x);

                    // ¿El objetivo está significativamente más alto Y horizontalmente cerca?
                    if (verticalDiff > minHeightDifferenceToJump && horizontalDist < maxHorizontalDistanceToJump)
                    {
                        Debug.Log(gameObject.name + " intenta saltar hacia " + currentTarget.name);
                        movement.Jump(); // Intentar saltar
                        lastJumpDecisionTime = Time.time; // Reiniciar cooldown de decisión
                        // Nota: El movimiento horizontal continúa en FixedUpdate
                    }
                }
                // -----------------------------------

                // Comprobar si estamos en rango de ataque (independiente del salto)
                if (combat.IsTargetInRange(currentTarget))
                {
                    ChangeState(AIState.Attacking);
                }
                // Si no, CharacterMovement ya se encarga de moverse hacia el target en FixedUpdate
                break;

            case AIState.Attacking:
                if (currentTarget == null || targetHealth == null || !targetHealth.IsAlive())
                {
                    FindTarget();
                    if (currentTarget == null) { ChangeState(AIState.Idle); } else { ChangeState(AIState.Seeking); }
                    break;
                }

                // Si el objetivo se sale de rango mientras atacamos, volvemos a buscarlo/perseguirlo
                if (!combat.IsTargetInRange(currentTarget))
                {
                     ChangeState(AIState.Seeking);
                     break;
                }

                // Intentar atacar
                combat.Attack(targetHealth);
                break;
        }
    }

    void FindTarget()
    {
        GameObject[] potentialTargets = GameObject.FindGameObjectsWithTag(enemyTag);
        Transform closestTarget = null;
        float minDistance = float.MaxValue;

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
             if(currentTarget != closestTarget) // Solo loguear si cambia o es la primera vez
             {
                 Debug.Log(gameObject.name + " encontró/cambió objetivo: " + closestTarget.name);
             }
             currentTarget = closestTarget;
             targetHealth = currentTarget.GetComponent<HealthSystem>();
             movement.SetTarget(currentTarget); // Decirle a Movement a quién seguir horizontalmente
             combat.SetTarget(targetHealth);
         }
         else
         {
              if(currentTarget != null) // Solo loguear si antes tenía objetivo
              {
                 Debug.Log(gameObject.name + " no encontró más objetivos.");
              }
              currentTarget = null;
              targetHealth = null;
              movement.SetTarget(null);
              combat.SetTarget(null);
         }
    }

    void ChangeState(AIState newState)
    {
        if (currentState == newState) return;

        // Lógica al salir del estado anterior
        switch (currentState)
        {
             case AIState.Attacking:
                movement.ResumeMovement(); // Reanudar movimiento si estaba detenido por atacar
                break;
        }

        currentState = newState;
        Debug.Log(gameObject.name + " cambió al estado: " + newState);

        // Lógica al entrar al nuevo estado
        switch (currentState)
        {
            case AIState.Idle:
                movement.StopMovement(); // Detener movimiento horizontal
                break;
            case AIState.Seeking:
                movement.ResumeMovement(); // Asegurar que puede moverse
                break;
            case AIState.Attacking:
                // Considera si quieres que se detenga al atacar. Ahora con gravedad, quizás no es necesario.
                // movement.StopMovement(); // Comenta o descomenta esto según prefieras
                break;
             case AIState.Dead:
                movement.StopMovement();
                combat.DisableAttack();
                // Considera desactivar también el Rigidbody o el Collider aquí
                // rb.simulated = false;
                // GetComponent<Collider2D>().enabled = false;
                break;
        }
    }

     void HandleDeath()
     {
          ChangeState(AIState.Dead);
     }

     void OnDestroy()
     {
         if (health != null)
         {
             health.OnDeath.RemoveListener(HandleDeath);
         }
     }
}