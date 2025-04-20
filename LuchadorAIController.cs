// File: LuchadorAIController.cs
using UnityEngine;
using Pathfinding;
using System.Collections;
using System.Collections.Generic;
using Random = UnityEngine.Random;

[RequireComponent(typeof(Seeker))]
[RequireComponent(typeof(AIPath))] // AIPath sigue siendo necesario para calcular el camino y desiredVelocity
[RequireComponent(typeof(CharacterCombat))]
[RequireComponent(typeof(HealthSystem))]
[RequireComponent(typeof(CharacterData))]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CharacterMovement))] // Añadido requerimiento

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
    private AIPath aiPath; // ¡IMPORTANTE! Revisa su configuración en el Inspector

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
    [Tooltip("Altura mínima relativa del objetivo para considerar saltar.")]
    public float jumpThresholdY = 1.5f; // Umbral para saltar

    // Referencias a componentes propios
    private CharacterCombat combat;
    private HealthSystem health;
    private CharacterData characterData;
    private Rigidbody2D rb;
    private Animator animator;
    private CharacterMovement characterMovement; // Referencia a CharacterMovement

    // Estado Interno IA
    private Transform currentTargetTransform;
    private HealthSystem currentTargetHealth;
    private float lastDecisionTime;
    private bool isCelebrating = false;
    private bool shouldAIMove = false; // Flag para indicar si la IA quiere moverse

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
        characterMovement = GetComponent<CharacterMovement>();

        // Configurar AIPath para que no controle el Rigidbody directamente
        if (aiPath != null)
        {
            // Asegúrate de que la gravedad del AIPath esté desactivada si usamos la del Rigidbody2D
            aiPath.gravity = Vector3.zero;
             // Desactivar actualizaciones de posición/rotación si es posible en tu versión
             // Es MUY IMPORTANTE verificar esto en el Inspector de Unity
             // aiPath.updatePosition = false; // <-- ¡Verificar!
             // aiPath.updateRotation = false; // <-- ¡Verificar!
             aiPath.enableRotation = false; // Asegurar que no rote
             aiPath.orientation = OrientationMode.YAxisForward; // O ZAxisForward (puede no importar si la rotación está desactivada)
             // Permitir que AIPath calcule el camino, pero nosotros aplicaremos el movimiento
             aiPath.canMove = true; // Permite calcular desiredVelocity
             aiPath.canSearch = true; // Permite buscar caminos
             aiPath.isStopped = false; // Iniciar sin detener
        }
        else Debug.LogError("AIPath component not found!", this);

        if (characterMovement == null) Debug.LogError("CharacterMovement component not found!", this);
        if (rb == null) Debug.LogError("Rigidbody2D component not found!", this);
    }

    void Start()
    {
        // Validar componentes críticos
        if (characterData == null || health == null || combat == null || aiPath == null || characterMovement == null) {
             Debug.LogError($"Missing critical components on {gameObject.name}. Disabling AI.", this); enabled = false; return; }
        if (health != null) { health.OnDeath.AddListener(HandleDeath); }
        if (characterData.baseStats != null) {
            aiPath.maxSpeed = characterData.baseStats.movementSpeed;
            aiPath.endReachedDistance = preferredCombatDistance * 0.9f;
            aiPath.slowdownDistance = preferredCombatDistance;
        } else { Debug.LogWarning($"BaseStats not assigned on {gameObject.name}.", this); }
        lastDecisionTime = Time.time;
        FindTarget();
    }

    void Update()
    {
        // Gestionar estado general y decisión de IA
        if (!enabled || characterData == null || characterData.isStunned)
        {
            shouldAIMove = false; // No moverse si está stuneado o desactivado
            if (aiPath != null) aiPath.isStopped = true; // Detener cálculo A*
            return;
        }

        if (isCelebrating)
        {
            shouldAIMove = false; // No moverse si celebra
            if (aiPath != null) aiPath.isStopped = true;
            return;
        }

        // Si no está stuneado/celebrando, permitir que A* calcule
        if (aiPath != null && aiPath.isStopped) {
             aiPath.isStopped = false;
        }

        // Tomar decisión de IA a intervalos
        if (Time.time >= lastDecisionTime + decisionInterval)
        {
            MakeDecision(); // MakeDecision actualizará shouldAIMove
            lastDecisionTime = Time.time;
        }

        // Actualizar path si es necesario
        UpdateAStarPath();

        // Actualizar Animator (estado visual)
        UpdateAnimator();
    }

     void FixedUpdate()
     {
        // Aplicar movimiento FÍSICO
        if (rb == null || characterData == null || !this.enabled) return;

        // No aplicar movimiento AI si está stuneado, celebrando o dasheando
        if (characterData.isStunned || isCelebrating || characterData.isDashing)
        {
            if(isCelebrating && rb.linearVelocity.sqrMagnitude > 0.01f) { // Detener suavemente si celebra
                rb.linearVelocity = Vector2.Lerp(rb.linearVelocity, Vector2.zero, Time.fixedDeltaTime * 10f);
            }
            return;
        }

        // Aplicar movimiento SOLO si la IA decidió que debería moverse
        if (shouldAIMove && aiPath != null && aiPath.hasPath && !aiPath.reachedEndOfPath)
        {
            // Obtener velocidad deseada de A*
            Vector2 desiredVelocity = aiPath.desiredVelocity;

            // Calcular velocidad horizontal objetivo (usando maxSpeed como límite)
            float targetVelocityX = desiredVelocity.x; // A* ya debería considerar maxSpeed

            // Mantener velocidad vertical actual (para gravedad/salto)
            rb.linearVelocity = new Vector2(targetVelocityX, rb.linearVelocity.y);

            FlipSpriteBasedOnVelocity(targetVelocityX);
        }
        else // Si no debemos movernos (shouldAIMove = false o A* no da velocidad)
        {
             // Reducir velocidad horizontal gradualmente, pero mantener gravedad.
             rb.linearVelocity = new Vector2(Mathf.Lerp(rb.linearVelocity.x, 0, Time.fixedDeltaTime * 5f), rb.linearVelocity.y); // Ajustar factor de lerp (5f)
        }
     }

    // Lógica de Decisión Principal de la IA
    void MakeDecision()
    {
        if (isCelebrating || characterMovement == null || characterData == null || combat == null || !enabled) {
            shouldAIMove = false; return;
        }

        // Resetear flag de movimiento al inicio de cada decisión
        shouldAIMove = false;

        if (!IsTargetValid()) { FindTarget(); if (!IsTargetValid()) { EnterIdleState(); return; } }
        if (characterData.isDashing || characterData.isAttemptingParry) return; // No decidir si está en estas acciones

        bool isGrounded = characterMovement.IsGrounded();
        float targetRelativeY = IsTargetValid() ? currentTargetTransform.position.y - transform.position.y : 0;
        float targetDistanceX = IsTargetValid() ? Mathf.Abs(currentTargetTransform.position.x - transform.position.x) : float.MaxValue;

        // --- Lógica de Salto ---
        if (isGrounded && IsTargetValid() && targetRelativeY > jumpThresholdY && targetDistanceX < preferredCombatDistance * 1.5f)
        { if (characterMovement.Jump()) { Debug.Log($"AI ({gameObject.name}): Jumping!"); return; } }

        // --- Lógica Defensiva ---
        bool predictedAttack = PredictEnemyAttack();
        if (predictedAttack && !characterData.isBlocking) {
            float choice = Random.value;
            if (choice < parryPreference && combat.TryParry()) { /* Acción: Parry */ return; }
            else if (choice < parryPreference + dodgePreference && CanAffordDash()) {
                Vector2 evadeDir = IsTargetValid() ? (transform.position - currentTargetTransform.position).normalized : -transform.right;
                 if(evadeDir == Vector2.zero) evadeDir = -transform.right;
                 if (combat.TryDash(evadeDir)) { /* Acción: Dash Evasivo */ return; }
            }
            else if (CanAffordBlock() && isGrounded) {
                if (combat.TryStartBlocking()) { /* Acción: Bloquear */ return; }
            }
        } else if (!predictedAttack && characterData.isBlocking) {
             combat.StopBlocking(); // Deja de bloquear si no hay amenaza
        }

        if (characterData.isBlocking) return; // Si está bloqueando, no hacer más

        // --- Lógica Ofensiva / Movimiento ---
        if (!IsTargetValid()) { EnterIdleState(); return; } // Re-chequear target

        float distanceSqr = (currentTargetTransform.position - transform.position).sqrMagnitude;
        float attackRangeSqr = characterData.baseStats.attackRange * characterData.baseStats.attackRange;
        float preferredDistSqr = preferredCombatDistance * preferredCombatDistance;
        bool isTargetInRange_Basic = distanceSqr <= attackRangeSqr;
        bool isTargetInRange_Preferred = distanceSqr <= preferredDistSqr;

        // Skill Use
        SkillData chosenSkill = ChooseSkillToUse();
        if (chosenSkill != null && Random.value < skillUseChance) {
            bool skillRequiresTarget = chosenSkill.range > 0 || chosenSkill.skillType == SkillType.DirectDamage || chosenSkill.skillType == SkillType.Projectile;
            float skillRangeSqr = chosenSkill.range * chosenSkill.range;
            bool skillInRange = chosenSkill.range <= 0 || distanceSqr <= skillRangeSqr;
            if (skillInRange) {
                if (combat.TryUseSkill(chosenSkill)) { /* Acción: Usar Skill */ return; }
            } else if (skillRequiresTarget) {
                 /* Decisión: Moverse para usar Skill */
                 shouldAIMove = true; EnsureMovingTowardsTarget(); return;
            }
        }

        // Basic Attack
        if (isTargetInRange_Basic && characterData.IsAttackReady() && isGrounded) {
            if (combat.TryAttack()) { /* Acción: Atacar */ return; }
        }

        // Dash Engage
        float dashEngageRangeSqr = (preferredCombatDistance + dashEngageRangeBonus) * (preferredCombatDistance + dashEngageRangeBonus);
        bool wantsToDashEngage = distanceSqr > dashEngageRangeSqr;
        if (wantsToDashEngage && CanAffordDash() && Random.value < aggression && isGrounded) {
             Vector2 engageDir = (currentTargetTransform.position - transform.position).normalized;
             if(engageDir == Vector2.zero) engageDir = transform.right;
             if (combat.TryDash(engageDir)) { /* Acción: Dash Engage */ return; }
        }
        // Move if not in preferred range
        else if (!isTargetInRange_Preferred) {
            /* Decisión: Moverse */
            shouldAIMove = true; EnsureMovingTowardsTarget();
        }
        // Stop if in preferred range
        else {
             /* Decisión: Detenerse */
             shouldAIMove = false; EnterIdleState();
        }
    }

    // --- Funciones Auxiliares ---

    void EnsureMovingTowardsTarget() {
        if (isCelebrating || aiPath == null) return;
        if (!IsTargetValid()) { EnterIdleState(); return; }
        if (aiPath.isStopped) { aiPath.isStopped = false; }
        aiPath.destination = currentTargetTransform.position;
    }

    void EnterIdleState() {
        if (this.enabled && !isCelebrating) {
             if (aiPath != null) aiPath.isStopped = true;
             shouldAIMove = false;
             combat?.StopBlocking();
        }
    }

     bool IsTargetValid() { return currentTargetTransform != null && currentTargetHealth != null && currentTargetHealth.IsAlive(); }

     void FindTarget() {
        if (isCelebrating) return;
        GameObject[] potentialTargets = GameObject.FindGameObjectsWithTag(enemyTag);
        Transform closestTarget = null; float minDistanceSqr = float.MaxValue;
        Vector3 currentPos = transform.position;
        foreach (GameObject pt in potentialTargets) {
            if (pt == gameObject) continue; HealthSystem ph = pt.GetComponent<HealthSystem>(); if (ph == null || !ph.IsAlive()) continue;
            float distSqr = (pt.transform.position - currentPos).sqrMagnitude;
            if (distSqr < minDistanceSqr) { minDistanceSqr = distSqr; closestTarget = pt.transform; }
        }
        Transform previousTarget = currentTargetTransform;
        if (closestTarget != null) {
            currentTargetTransform = closestTarget; currentTargetHealth = currentTargetTransform.GetComponent<HealthSystem>(); combat.SetTarget(currentTargetHealth);
            if (currentTargetTransform != previousTarget) { Debug.Log($"{gameObject.name} found target: {currentTargetTransform.name}"); lastPathRequestTime = -pathUpdateRate; shouldAIMove = true; EnsureMovingTowardsTarget(); }
        } else { if(previousTarget != null) Debug.Log($"{gameObject.name} lost target."); currentTargetTransform = null; currentTargetHealth = null; EnterIdleState(); }
    }

    bool PredictEnemyAttack() {
         if (!IsTargetValid() || characterData.baseStats == null) return false;
         float distSqr = (currentTargetTransform.position - transform.position).sqrMagnitude;
         float predictRangeSqr = characterData.baseStats.attackRange * 1.5f * characterData.baseStats.attackRange * 1.5f;
         if (distSqr < predictRangeSqr) {
             Vector2 dirToMe = (transform.position - currentTargetTransform.position).normalized;
             Vector2 enemyForward = currentTargetTransform.right;
             SpriteRenderer targetSr = currentTargetTransform.GetComponent<SpriteRenderer>();
             if (targetSr != null && targetSr.flipX) { enemyForward = -enemyForward; }
             float dot = Vector2.Dot(enemyForward, dirToMe);
             if (dot > 0.7f && Random.value < 0.15f) { return true; }
         }
         return false;
    }

     SkillData ChooseSkillToUse() {
        bool isHealthy = true;
        if (characterData != null && characterData.baseStats != null && characterData.baseStats.maxHealth > 0) {
            isHealthy = (characterData.currentHealth / characterData.baseStats.maxHealth) > lowHealthThreshold;
        }
        SkillData bestSkill = null;
        if (characterData?.skills == null) return null;
        foreach (SkillData skill in characterData.skills) {
            if (skill != null && characterData.IsSkillReady(skill)) {
                if (skill.skillType == SkillType.Heal && !isHealthy) { return skill; }
                if (bestSkill == null && (skill.skillType == SkillType.DirectDamage || skill.skillType == SkillType.Projectile || skill.skillType == SkillType.AreaOfEffect)) { bestSkill = skill; }
            }
        }
        return bestSkill;
    }

     bool CanAffordDash() { return characterData != null && characterData.baseStats != null && characterData.currentStamina >= characterData.baseStats.dashCost; }
     bool CanAffordBlock() { return characterData != null && characterData.baseStats != null && characterData.currentStamina > (characterData.baseStats.blockStaminaDrain * 0.1f); } // Necesita un poco de stamina para empezar

    // --- Pathfinding Callbacks ---
     void UpdateAStarPath() {
        if (isCelebrating || aiPath == null || !aiPath.canSearch || aiPath.isStopped) return;
        if (IsTargetValid() && Time.time > lastPathRequestTime + pathUpdateRate) { RequestPathToTarget(); lastPathRequestTime = Time.time; }
    }

     void RequestPathToTarget() {
        if (isCelebrating || seeker == null || !seeker.IsDone() || !IsTargetValid() || aiPath == null || !aiPath.canSearch || aiPath.isStopped) return;
        // Asegurar que el destino se actualice antes de buscar
        aiPath.destination = currentTargetTransform.position;
        seeker.StartPath(rb.position, aiPath.destination, OnPathComplete);
    }

    public void OnPathComplete(Path p) {
        if (!enabled || p == null) return; if (p.error) { Debug.LogWarning($"{gameObject.name} path error: {p.errorLog}"); if(!isCelebrating) EnterIdleState(); }
    }

    // --- State Handling & Cleanup ---
    void HandleDeath() {
        Debug.Log($"Death handled for {gameObject.name}. Disabling AI.");
        if(aiPath != null) aiPath.isStopped = true;
        shouldAIMove = false;
        isCelebrating = false;
        // Verificar si el componente o GameObject todavía existen antes de desactivar
        if(this != null && this.gameObject != null) enabled = false;
    }

    // ***** MÉTODO PÚBLICO PARA BATTLE MANAGER *****
     public void StartCelebrating()
     {
          if (!this.enabled || health == null || !health.IsAlive() || isCelebrating) return;
           Debug.Log($"AI ({gameObject.name}): Celebrating!"); isCelebrating = true;
           if (aiPath != null) aiPath.isStopped = true; // Detener cálculo A*
           shouldAIMove = false; // Asegurar que no intente moverse
           combat?.InterruptActions(); combat?.StopBlocking(); currentTargetTransform = null; currentTargetHealth = null;
           animator?.SetTrigger("Celebrate");
           // FixedUpdate se encargará de detener la velocidad del Rigidbody gradualmente
     }

     void FlipSpriteBasedOnVelocity(float targetVelocityX) {
         // Voltear el sprite basado en la velocidad horizontal del Rigidbody
         // Es más fiable que usar targetVelocityX que puede ser 0 brevemente
         if(animator != null && rb != null) {
             SpriteRenderer sr = animator.GetComponent<SpriteRenderer>();
             if (sr != null) {
                 if (rb.linearVelocity.x > 0.1f) sr.flipX = false;
                 else if (rb.linearVelocity.x < -0.1f) sr.flipX = true;
                 // No hacer nada si la velocidad es muy cercana a cero para evitar flips rápidos
             }
         }
     }

     void UpdateAnimator() {
        if (animator != null && characterMovement != null && rb != null) {
            bool grounded = characterMovement.IsGrounded();
            // Moviéndose si hay velocidad horizontal Y está en el suelo
            bool isMovingHorizontally = Mathf.Abs(rb.linearVelocity.x) > 0.15f; // Aumentar umbral ligero
            animator.SetBool("IsMoving", isMovingHorizontally && grounded);
            animator.SetBool("IsGrounded", grounded);
            animator.SetFloat("VerticalVelocity", rb.linearVelocity.y);
        }
     }

     void OnDestroy() { if (health != null) { health.OnDeath.RemoveListener(HandleDeath); } StopAllCoroutines(); }
}