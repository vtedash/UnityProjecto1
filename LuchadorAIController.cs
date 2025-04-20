// File: LuchadorAIController.cs
using UnityEngine;
using Pathfinding;
using System.Collections;
using System.Collections.Generic;
using Random = UnityEngine.Random;

[RequireComponent(typeof(Seeker))]
[RequireComponent(typeof(AIPath))]
[RequireComponent(typeof(CharacterCombat))]
[RequireComponent(typeof(HealthSystem))]
[RequireComponent(typeof(CharacterData))]
[RequireComponent(typeof(Rigidbody2D))]

public class LuchadorAIController : MonoBehaviour
{
    [Header("Targeting & Team")]
    [Tooltip("Tag del equipo enemigo a buscar")]
    public string enemyTag = "Player";

    [Header("Pathfinding")]
    [Tooltip("Con qué frecuencia (segundos) la IA recalcula su camino hacia el objetivo")]
    public float pathUpdateRate = 0.5f;
    private float lastPathRequestTime = -1f;
    private Seeker seeker;
    private AIPath aiPath; // Componente de A* para movimiento

    [Header("AI Decision Parameters")]
    [Tooltip("Frecuencia (segundos) para reevaluar la situación y tomar una decisión.")]
    public float decisionInterval = 0.2f;
    [Tooltip("Probabilidad (0-1) de intentar una acción ofensiva vs defensiva o reposicionarse.")]
    [Range(0f, 1f)] public float aggression = 0.7f;
    [Tooltip("Distancia a la que intentará mantenerse del objetivo.")]
    public float preferredCombatDistance = 0.8f;
    [Tooltip("Distancia adicional antes de considerar usar Dash para acercarse.")]
    public float dashEngageRangeBonus = 2.0f;
    [Tooltip("Probabilidad (0-1) de usar una habilidad si está lista y en condiciones.")]
    [Range(0f, 1f)] public float skillUseChance = 0.4f;
    [Tooltip("Porcentaje de vida (0-1) por debajo del cual podría intentar retirarse o jugar más defensivo.")]
    [Range(0f, 1f)] public float lowHealthThreshold = 0.3f;
    [Tooltip("Probabilidad (0-1) de intentar un Parry si predice un ataque.")]
    [Range(0f, 1f)] public float parryPreference = 0.3f;
     [Tooltip("Probabilidad (0-1) de intentar un Dash evasivo si predice un ataque.")]
    [Range(0f, 1f)] public float dodgePreference = 0.5f;

    [Header("Ground Check (for potential future jump logic)")]
    public Transform groundCheckPoint;
    public float groundCheckRadius = 0.2f;
    public LayerMask groundLayer;
    private bool isGrounded;

    // Referencias a componentes propios
    private CharacterCombat combat;
    private HealthSystem health;
    private CharacterData characterData;
    private Rigidbody2D rb;
    private Animator animator;

    // Estado Interno IA
    private Transform currentTargetTransform;
    private HealthSystem currentTargetHealth;
    private float lastDecisionTime;
    private bool isCelebrating = false;

    void Awake()
    {
        // Obtener referencias a componentes necesarios
        seeker = GetComponent<Seeker>();
        aiPath = GetComponent<AIPath>();
        combat = GetComponent<CharacterCombat>();
        health = GetComponent<HealthSystem>();
        characterData = GetComponent<CharacterData>();
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();

        // Configurar GroundCheck si no está asignado
        if (groundCheckPoint == null)
        {
             Transform foundGroundCheck = transform.Find("GroundCheck");
             if (foundGroundCheck != null) { groundCheckPoint = foundGroundCheck; }
             else {
                 GameObject groundCheckObj = new GameObject("GroundCheck");
                 groundCheckObj.transform.SetParent(transform);
                 float yOffset = -(GetComponent<Collider2D>()?.bounds.extents.y ?? 0.5f);
                 groundCheckObj.transform.localPosition = new Vector3(0, yOffset - 0.05f, 0);
                 groundCheckPoint = groundCheckObj.transform;
                 Debug.LogWarning($"GroundCheckPoint no asignado en {gameObject.name}. Se creó uno. Ajusta su posición.", this);
             }
        }
    }

    void Start()
    {
        // Validar componentes críticos
        if (characterData == null || health == null || combat == null || aiPath == null) {
            Debug.LogError($"Faltan componentes críticos en {gameObject.name}, desactivando IA.");
            enabled = false;
            return;
        }

        // Suscribirse al evento de muerte
        if (health != null) {
            health.OnDeath.AddListener(HandleDeath);
            Debug.Log($"{gameObject.name} subscribed HandleDeath to OnDeath.");
        } else {
             Debug.LogError($"HealthSystem is null on {gameObject.name} in Start!");
        }

        // Configurar AIPath con stats base si existen
        if (characterData.baseStats != null)
        {
            aiPath.maxSpeed = characterData.baseStats.movementSpeed;
            aiPath.endReachedDistance = preferredCombatDistance * 0.9f; // Distancia a la que considera que llegó
            aiPath.slowdownDistance = preferredCombatDistance; // Distancia a la que empieza a frenar
            aiPath.canMove = true; // Permitir movimiento A*
            aiPath.canSearch = true; // Permitir búsqueda de caminos A*
        } else {
            Debug.LogWarning($"BaseStats no asignados en {gameObject.name} en Start. AIPath usará valores por defecto.");
        }

        lastDecisionTime = Time.time; // Inicializar timer de decisión
        FindTarget(); // Buscar objetivo inicial
    }

    void Update()
    {
        // No hacer nada si la IA está desactivada o el personaje está aturdido
        if (!enabled || characterData.isStunned)
        {
            if (characterData.isStunned && aiPath != null && aiPath.canMove) {
                 aiPath.canMove = false; // Detener A* si está aturdido
            }
            return;
        }

        // Si está celebrando, detener movimiento y no tomar decisiones
        if (isCelebrating)
        {
            if (aiPath != null && aiPath.canMove) aiPath.canMove = false;
            if (rb != null && rb.linearVelocity != Vector2.zero) rb.linearVelocity = Vector2.zero;
            return;
        }

        CheckIfGrounded(); // Actualizar estado de isGrounded

        // Tomar una decisión de IA a intervalos regulares
        if (Time.time >= lastDecisionTime + decisionInterval)
        {
            MakeDecision();
            lastDecisionTime = Time.time;
        }

        // Actualizar el path de A* si es necesario
        UpdateAStarPath();
    }

    // Lógica de Decisión Principal de la IA
    void MakeDecision()
    {
        if (isCelebrating) return; // No decidir si está celebrando

        // Buscar objetivo si el actual no es válido
        if (!IsTargetValid())
        {
            FindTarget();
            if (!IsTargetValid())
            {
                EnterIdleState(); // Si no hay objetivo válido, quedarse quieto
                return;
            }
        }

        // No tomar nuevas decisiones si está en medio de un dash o parry
        if (characterData.isDashing || characterData.isAttemptingParry) return;

        // Evaluar acciones defensivas si predice un ataque
        bool predictedAttack = PredictEnemyAttack();
        if (predictedAttack && !characterData.isBlocking)
        {
            float choice = Random.value;
            if (choice < parryPreference && combat.TryParry()) // Intentar Parry
            {
                Debug.Log($"AI ({gameObject.name}): Intentando Parry!");
                return; // Acción tomada
            }
            else if (choice < parryPreference + dodgePreference && CanAffordDash()) // Intentar Dash evasivo
             {
                 Vector2 evadeDir = (transform.position - currentTargetTransform.position).normalized;
                 if(evadeDir == Vector2.zero) evadeDir = -transform.right; // Dirección por defecto si están superpuestos
                 if (combat.TryDash(evadeDir)) {
                     Debug.Log($"AI ({gameObject.name}): Intentando Dash Evasivo!");
                     return; // Acción tomada
                 }
             }
            else if (characterData.currentStamina > 0) // Intentar Bloquear como último recurso defensivo
            {
                 if (combat.TryStartBlocking()) {
                     Debug.Log($"AI ({gameObject.name}): Empezando a Bloquear por amenaza!");
                     return; // Acción tomada
                 }
            }
        }
        // Si no hay predicción de ataque y está bloqueando, dejar de bloquear
        else if (!predictedAttack && characterData.isBlocking) {
             combat.StopBlocking();
        }

        // No continuar si está bloqueando (ya sea por decisión o por predicción)
        if (characterData.isBlocking) return;

        // Evaluar salud actual
        bool isHealthy = true;
         if (characterData != null && characterData.baseStats != null && characterData.baseStats.maxHealth > 0) {
             isHealthy = (characterData.currentHealth / characterData.baseStats.maxHealth) > lowHealthThreshold;
         }

        // Calcular distancias (cuadradas para eficiencia)
        float distanceSqr = (currentTargetTransform.position - transform.position).sqrMagnitude;
        float attackRangeSqr = characterData.baseStats.attackRange * characterData.baseStats.attackRange;
        float preferredDistSqr = preferredCombatDistance * preferredCombatDistance;

        bool isTargetInRange_Basic = distanceSqr <= attackRangeSqr; // En rango para ataque básico
        bool isTargetInRange_Preferred = distanceSqr <= preferredDistSqr; // En rango de combate deseado

        // Evaluar uso de Habilidades
        SkillData chosenSkill = ChooseSkillToUse(); // Elegir la mejor habilidad disponible
        if (chosenSkill != null && Random.value < skillUseChance) // Si hay habilidad y toca usarla
        {
            bool skillRequiresTarget = chosenSkill.range > 0 || chosenSkill.skillType == SkillType.DirectDamage || chosenSkill.skillType == SkillType.Projectile;
            float skillRangeSqr = chosenSkill.range * chosenSkill.range;
            bool skillInRange = chosenSkill.range <= 0 || distanceSqr <= skillRangeSqr; // Habilidad en rango

            if (skillInRange) { // Si está en rango, intentar usarla
                 if (combat.TryUseSkill(chosenSkill)) {
                     Debug.Log($"AI ({gameObject.name}): Usando Skill {chosenSkill.skillName}!");
                     return; // Acción tomada
                 }
            } else if (skillRequiresTarget) { // Si no está en rango pero necesita target, moverse
                 Debug.Log($"AI ({gameObject.name}): Moviéndose para usar Skill {chosenSkill.skillName}");
                 EnsureMovingTowardsTarget(); // Asegurar que se mueve hacia el objetivo
                 return; // Acción tomada (moverse)
            }
        }

        // Evaluar Ataque Básico
        if (isTargetInRange_Basic && characterData.IsAttackReady()) // Si está en rango y el ataque no está en cooldown
        {
            if (combat.TryAttack()) // Intentar atacar
            {
                 return; // Acción tomada
            }
        }

        // Evaluar Dash Ofensivo (para acortar distancia rápidamente)
        float dashEngageRangeSqr = (preferredCombatDistance + dashEngageRangeBonus) * (preferredCombatDistance + dashEngageRangeBonus);
        bool wantsToDashEngage = distanceSqr > dashEngageRangeSqr; // Si está más lejos del rango preferido + bonus

        if (wantsToDashEngage && CanAffordDash() && Random.value < aggression) { // Si quiere, puede y es agresivo
             Vector2 engageDir = (currentTargetTransform.position - transform.position).normalized;
              if(engageDir == Vector2.zero) engageDir = transform.right; // Dirección por defecto
             if (combat.TryDash(engageDir)) { // Intentar dash hacia el objetivo
                 Debug.Log($"AI ({gameObject.name}): Usando Dash para Acercarse!");
                 return; // Acción tomada
             }
        }
        // Evaluar Movimiento Normal
        else if (!isTargetInRange_Preferred) // Si no está en la distancia preferida (pero no tan lejos como para dashear)
        {
             EnsureMovingTowardsTarget(); // Moverse normalmente hacia el objetivo
        }
        // Evaluar Detenerse
        else if (aiPath != null && aiPath.canMove) // Si está en la distancia preferida y A* se está moviendo
        {
             Debug.Log($"AI ({gameObject.name}): En rango preferido, deteniendo movimiento.");
             aiPath.canMove = false; // Detener A*
             aiPath.destination = transform.position; // Fijar destino actual para evitar deriva
             if(rb != null) rb.linearVelocity = Vector2.zero; // Detener física por si acaso
        }
    }

    // --- Funciones Auxiliares de IA ---

    // Asegura que el componente AIPath esté activo y buscando el objetivo
    void EnsureMovingTowardsTarget() {
        if (isCelebrating) {
            if (aiPath != null) aiPath.canMove = false; // No moverse si celebra
            return;
        }

        if(aiPath != null && !aiPath.canMove) { // Si A* no se está moviendo, activarlo
            Debug.Log($"AI ({gameObject.name}): Reanudando movimiento hacia el objetivo.");
            aiPath.canMove = true;
        }
        RequestPathToTarget(); // Pedir un nuevo camino (o actualizarlo)
    }

    // Detiene el movimiento y acciones defensivas
    void EnterIdleState() {
        if (this.enabled && !isCelebrating) { // Solo si la IA está activa y no celebra
             Debug.Log($"AI ({gameObject.name}): Entrando en Estado Idle.");
             if (aiPath != null) aiPath.canMove = false; // Detener A*
             combat?.StopBlocking(); // Dejar de bloquear si lo estaba haciendo
        }
    }

    // Verifica si el objetivo actual sigue siendo válido (existe y está vivo)
    bool IsTargetValid()
    {
        return currentTargetTransform != null && currentTargetHealth != null && currentTargetHealth.IsAlive();
    }

    // Busca el enemigo más cercano con el tag especificado
    void FindTarget()
    {
        if (isCelebrating) return; // No buscar si celebra

        GameObject[] potentialTargets = GameObject.FindGameObjectsWithTag(enemyTag);
        Transform closestTarget = null;
        float minDistanceSqr = float.MaxValue;

        // Iterar sobre todos los posibles objetivos
        foreach (GameObject potentialTarget in potentialTargets)
        {
            if (potentialTarget == gameObject) continue; // Ignorarse a sí mismo
            HealthSystem potentialHealth = potentialTarget.GetComponent<HealthSystem>();
            if (potentialHealth == null || !potentialHealth.IsAlive()) continue; // Ignorar si no tiene vida o está muerto

            // Calcular distancia cuadrada (más eficiente que Distance)
            float distanceSqr = (potentialTarget.transform.position - transform.position).sqrMagnitude;
            if (distanceSqr < minDistanceSqr) // Si es el más cercano hasta ahora
            {
                minDistanceSqr = distanceSqr;
                closestTarget = potentialTarget.transform;
            }
        }

        Transform previousTarget = currentTargetTransform;
        if (closestTarget != null) // Si se encontró un objetivo
        {
            currentTargetTransform = closestTarget;
            currentTargetHealth = currentTargetTransform.GetComponent<HealthSystem>();
            combat.SetTarget(currentTargetHealth); // Informar al componente de combate
            if (currentTargetTransform != previousTarget) { // Si el objetivo cambió
                 Debug.Log($"{gameObject.name} encontró/cambió objetivo: {currentTargetTransform.name}");
                 lastPathRequestTime = -pathUpdateRate; // Forzar recálculo inmediato de path
                 EnsureMovingTowardsTarget(); // Empezar a moverse hacia él
            }
        }
        else // Si no se encontró ningún objetivo válido
        {
              if(previousTarget != null) Debug.Log($"{gameObject.name} perdió su objetivo o no encontró nuevos.");
              EnterIdleState(); // Quedarse quieto
        }
    }

    // Heurística simple para predecir si el enemigo va a atacar
    bool PredictEnemyAttack() {
         if (!IsTargetValid() || animator == null || characterData.baseStats == null) return false;
         float distSqr = (currentTargetTransform.position - transform.position).sqrMagnitude;
         // Considerar un rango de predicción un poco mayor que el rango de ataque base
         float predictRangeSqr = characterData.baseStats.attackRange * 1.5f * characterData.baseStats.attackRange * 1.5f;

         if (distSqr < predictRangeSqr) { // Si el enemigo está cerca
             // Calcular si el enemigo está mirando hacia mí (producto escalar)
             Vector2 dirToMe = (transform.position - currentTargetTransform.position).normalized;
             float dot = Vector2.Dot(currentTargetTransform.right, dirToMe); // Asume que 'right' es adelante
             // Si mira más o menos hacia mí y hay una pequeña probabilidad aleatoria
             if (dot > 0.7f && Random.value < 0.10f) { // Ajustar umbral de dot y probabilidad
                  return true; // Predecir ataque
             }
         }
         return false; // No predecir ataque
    }

    // Elige una habilidad para usar basada en prioridad (ej. curación si baja vida)
    SkillData ChooseSkillToUse() {
         bool isCurrentlyHealthy = true;
         if (characterData != null && characterData.baseStats != null && characterData.baseStats.maxHealth > 0) {
             isCurrentlyHealthy = (characterData.currentHealth / characterData.baseStats.maxHealth) > lowHealthThreshold;
         }

         SkillData bestSkill = null;
         foreach (SkillData currentSkill in characterData.skills) {
             if (characterData.IsSkillReady(currentSkill)) { // Si la habilidad no está en cooldown
                 // Prioridad 1: Curarse si tiene poca vida
                 if (currentSkill.skillType == SkillType.Heal && !isCurrentlyHealthy) {
                     Debug.Log($"AI ({gameObject.name}): Prioritizing Heal skill.");
                     return currentSkill;
                 }
                 // Prioridad 2: Guardar la primera habilidad ofensiva encontrada
                 if (bestSkill == null && (currentSkill.skillType == SkillType.DirectDamage || currentSkill.skillType == SkillType.Projectile || currentSkill.skillType == SkillType.AreaOfEffect)) {
                     bestSkill = currentSkill;
                 }
                 // Añadir más lógicas de prioridad aquí si es necesario
             }
         }
         return bestSkill; // Devolver la mejor opción encontrada (o null si ninguna)
    }

    // Comprueba si hay suficiente stamina para hacer un dash
     bool CanAffordDash() {
        return characterData.baseStats != null && characterData.currentStamina >= characterData.baseStats.dashCost;
    }


    // --- Pathfinding A* ---

    // Actualiza la petición de path si ha pasado el tiempo necesario
    void UpdateAStarPath() {
        if (isCelebrating) return;

        if (aiPath != null && aiPath.canMove && IsTargetValid() && Time.time > lastPathRequestTime + pathUpdateRate)
        {
            RequestPathToTarget();
            lastPathRequestTime = Time.time;
        }
    }

    // Solicita un nuevo camino al Seeker de A*
    void RequestPathToTarget()
    {
        if (isCelebrating) return;

        // Solo pedir path si el seeker ha terminado el anterior, hay target y A* puede buscar
        if (seeker != null && seeker.IsDone() && currentTargetTransform != null && aiPath != null && aiPath.canSearch)
        {
            seeker.StartPath(transform.position, currentTargetTransform.position, OnPathComplete);
        }
    }

    // Callback que se ejecuta cuando A* termina de calcular el path
    public void OnPathComplete(Path p)
    {
        if (p.error)
        {
            Debug.LogWarning($"{gameObject.name} no pudo calcular camino: {p.errorLog}");
            if(!isCelebrating) EnterIdleState(); // Si hay error, entrar en idle
        }
        // Si no hay error, A* (AIPath) usará el camino automáticamente
    }

    // --- Manejo de Estados Propios ---

    // Función llamada por el evento OnDeath de HealthSystem
    void HandleDeath()
    {
        Debug.Log($"HandleDeath called by OnDeath event for {gameObject.name}. Disabling AI.");
        if(aiPath != null) aiPath.canMove = false; // Detener movimiento A*
        isCelebrating = false; // Asegurarse de no estar celebrando
        enabled = false; // Desactivar este script de IA
    }

    // Inicia el estado de celebración (llamado por BattleManager)
     public void StartCelebrating()
     {
          // Solo celebrar si la IA está activa, el personaje está vivo y no está ya celebrando
          if (!this.enabled || !health.IsAlive() || isCelebrating) return;

           Debug.Log($"AI ({gameObject.name}): ¡CELEBRANDO!");
           isCelebrating = true; // Establecer el flag

           // Detener acciones de combate y movimiento
           if (aiPath != null) aiPath.canMove = false;
           if (rb != null) rb.linearVelocity = Vector2.zero;
           combat?.InterruptActions(); // Interrumpir acciones de combate
           combat?.StopBlocking(); // Dejar de bloquear
           currentTargetTransform = null; // Olvidar objetivo
           currentTargetHealth = null;

           animator?.SetTrigger("Celebrate"); // Activar animación de celebración
     }


    // --- Otros ---

    // Comprueba si el personaje está tocando el suelo
    void CheckIfGrounded()
    {
        if (groundCheckPoint == null) return;
        isGrounded = Physics2D.OverlapCircle(groundCheckPoint.position, groundCheckRadius, groundLayer);
        // Podrías querer actualizar un parámetro "IsGrounded" en el Animator aquí
        // animator?.SetBool("IsGrounded", isGrounded);
    }

    // Limpieza al destruir el objeto
    void OnDestroy()
    {
        // Desuscribirse del evento para evitar errores
        if (health != null)
        {
             health.OnDeath.RemoveListener(HandleDeath);
        }
        StopAllCoroutines(); // Detener todas las corutinas activas en este script
    }
}