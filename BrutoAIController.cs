using UnityEngine;

[RequireComponent(typeof(CharacterMovement))]
[RequireComponent(typeof(CharacterCombat))]
[RequireComponent(typeof(HealthSystem))]
public class BrutoAIController : MonoBehaviour
{
    public enum AIState { Idle, Seeking, Attacking, Dead }

    public AIState currentState = AIState.Idle;
    public string enemyTag = "Player"; // O la tag que uses para diferenciar equipos

    private CharacterMovement movement;
    private CharacterCombat combat;
    private HealthSystem health;
    private Transform currentTarget;
    private HealthSystem targetHealth;

    void Awake()
    {
        movement = GetComponent<CharacterMovement>();
        combat = GetComponent<CharacterCombat>();
        health = GetComponent<HealthSystem>();
    }

    void Start()
    {
        // Suscribirse al evento OnDeath para cambiar de estado
        health.OnDeath.AddListener(HandleDeath);
        // Empezar buscando un enemigo
        ChangeState(AIState.Seeking);
    }

    void Update()
    {
        if (currentState == AIState.Dead) return; // No hacer nada si estamos muertos

        // Lógica de estados
        switch (currentState)
        {
            case AIState.Idle:
                // Quizás buscar enemigo si no tenemos uno
                FindTarget();
                if (currentTarget != null) ChangeState(AIState.Seeking);
                break;

            case AIState.Seeking:
                // Si no tenemos objetivo o el objetivo murió, buscar uno nuevo
                if (currentTarget == null || targetHealth == null || !targetHealth.IsAlive())
                {
                    FindTarget();
                    // Si no encontramos nuevo objetivo, volver a Idle (o esperar)
                    if (currentTarget == null) {
                         ChangeState(AIState.Idle);
                         break;
                    }
                }

                // Comprobar si estamos en rango de ataque
                if (combat.IsTargetInRange(currentTarget))
                {
                    ChangeState(AIState.Attacking);
                }
                // Si no estamos en rango, seguir moviéndonos (el movimiento ya lo hace CharacterMovement)
                break;

            case AIState.Attacking:
                 // Si no tenemos objetivo, el objetivo murió o salimos de rango, volver a buscar/seguir
                if (currentTarget == null || targetHealth == null || !targetHealth.IsAlive())
                {
                    FindTarget(); // Intenta encontrar uno nuevo inmediatamente
                     if (currentTarget == null) {
                         ChangeState(AIState.Idle); // No hay nadie más
                     } else {
                        ChangeState(AIState.Seeking); // Encontró otro, ir a por él
                     }
                     break;
                }

                if (!combat.IsTargetInRange(currentTarget))
                {
                     ChangeState(AIState.Seeking); // Se salió de rango, perseguir
                     break;
                }

                // Intentar atacar (el cooldown está dentro de CharacterCombat)
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
            // Asegurarse de no seleccionarse a sí mismo si comparten tag por error
            if (potentialTarget == gameObject) continue;

            // Asegurarse de que el objetivo potencial está vivo
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
             targetHealth = currentTarget.GetComponent<HealthSystem>(); // Cachear la salud del objetivo
             movement.SetTarget(currentTarget);
             combat.SetTarget(targetHealth);
             Debug.Log(gameObject.name + " encontró objetivo: " + currentTarget.name);
         }
         else
         {
              Debug.Log(gameObject.name + " no encontró objetivos.");
              currentTarget = null;
              targetHealth = null;
              movement.SetTarget(null); // Decirle al movimiento que no hay objetivo
              combat.SetTarget(null);
         }
    }

    void ChangeState(AIState newState)
    {
        if (currentState == newState) return;

        // Salir del estado actual (lógica de limpieza si es necesaria)
        switch (currentState)
        {
             case AIState.Attacking:
                movement.ResumeMovement(); // Permitir moverse de nuevo al salir de atacar
                break;
            // Añadir otros casos si es necesario
        }

        currentState = newState;
        Debug.Log(gameObject.name + " cambió al estado: " + newState);

        // Entrar en el nuevo estado (lógica de inicialización)
        switch (currentState)
        {
            case AIState.Idle:
                movement.StopMovement(); // No moverse en Idle
                break;
            case AIState.Seeking:
                movement.ResumeMovement(); // Asegurarse de que puede moverse
                // El objetivo ya debería estar asignado por FindTarget()
                break;
            case AIState.Attacking:
                movement.StopMovement(); // Detenerse para atacar (puedes cambiar esto si quieres ataques en movimiento)
                break;
             case AIState.Dead:
                movement.StopMovement();
                combat.DisableAttack();
                // Podrías desactivar colliders aquí también
                break;
        }
    }

     void HandleDeath()
     {
          ChangeState(AIState.Dead);
     }

     // Es buena práctica desuscribirse de los eventos al destruir el objeto
     void OnDestroy()
     {
         if (health != null)
         {
             health.OnDeath.RemoveListener(HandleDeath);
         }
     }
}