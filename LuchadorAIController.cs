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
[RequireComponent(typeof(CharacterMovement))]

public class LuchadorAIController : MonoBehaviour
{
    // --- AI States ---
    private enum AIState { Idle, Searching, Chasing, Attacking, UsingSkill, Blocking, Parrying, Dashing, Jumping, Fleeing, Stunned, Celebrating }
    [Header("Debug")]
    [SerializeField] private AIState currentState = AIState.Idle;

    [Header("Targeting & Team")]
    [Tooltip("La etiqueta usada para identificar enemigos potenciales.")]
    public string enemyTag = "Player";

    [Header("Pathfinding")]
    [Tooltip("Cada cuánto la IA recalcula su ruta hacia el objetivo (segundos).")]
    public float pathUpdateRate = 0.5f;
    private float lastPathRequestTime = -1f;

    // --- Component References ---
    private Seeker seeker;
    private AIPath aiPath;
    private CharacterCombat combat;
    private HealthSystem health;
    private CharacterData characterData;
    private Rigidbody2D rb;
    private CharacterMovement characterMovement;

    [Header("AI Decision Parameters")]
    public float decisionInterval = 0.2f;
    [Range(0f, 1f)] public float aggression = 0.7f;
    public float preferredCombatDistance = 1.0f;
    public float dashEngageRangeBonus = 2.0f;
    [Range(0f, 1f)] public float skillUseChance = 0.4f;
    [Range(0f, 1f)] public float lowHealthThreshold = 0.3f;
    [Range(0f, 1f)] public float parryPreference = 0.3f;
    [Range(0f, 1f)] public float dodgePreference = 0.5f;
    [Tooltip("Altura mínima relativa del objetivo para considerar saltar HACIA ARRIBA.")]
    public float jumpUpThresholdY = 1.5f; // Renombrado para claridad
    [Tooltip("A qué distancia hacia adelante comprobar obstáculos o plataformas al decidir saltar.")]
    public float jumpObstacleCheckDistance = 1.0f;
    [Tooltip("Tiempo máximo (segundos) que la IA puede estar persiguiendo sin moverse antes de considerar que está atascada.")]
    public float maxStuckTime = 1.0f; // Tiempo para detectar atasco

    [Header("Movement Control (Used in FixedUpdate)")]
    [Tooltip("Cuán rápido el personaje alcanza la velocidad horizontal objetivo. DEBE SER > 0.")]
    public float horizontalAcceleration = 15f;

    // --- Internal State ---
    private Transform currentTargetTransform;
    private HealthSystem currentTargetHealth;
    private float lastDecisionTime;
    private bool isTargetReachable = true;
    private bool shouldAIMoveHorizontally = false;
    private float timeStuck = 0f; // Temporizador para detectar atascos
    private const float MIN_MOVE_VELOCITY_SQR = 0.01f; // Velocidad cuadrada mínima para considerar que se mueve

    void Awake()
    {
        // --- Get Components & Validation ---
        seeker = GetComponent<Seeker>();
        aiPath = GetComponent<AIPath>();
        combat = GetComponent<CharacterCombat>();
        health = GetComponent<HealthSystem>();
        characterData = GetComponent<CharacterData>();
        rb = GetComponent<Rigidbody2D>();
        characterMovement = GetComponent<CharacterMovement>();
        if (seeker == null || aiPath == null || combat == null || health == null || characterData == null || rb == null || characterMovement == null) {
             Debug.LogError($"[{gameObject.name}] Missing one or more required components!", this);
             enabled = false; return;
        }

        // --- Configure AIPath ---
        if (aiPath != null) {
            aiPath.gravity = Vector3.zero; aiPath.enableRotation = false; aiPath.updateRotation = false;
            aiPath.updatePosition = false; aiPath.canMove = false; aiPath.canSearch = true;
            aiPath.orientation = OrientationMode.YAxisForward; aiPath.maxAcceleration = float.PositiveInfinity;
            aiPath.pickNextWaypointDist = 1f; aiPath.endReachedDistance = preferredCombatDistance * 0.8f;
            aiPath.slowdownDistance = preferredCombatDistance;
        }

        // --- Rigidbody & Acceleration Check ---
        if (rb != null && rb.gravityScale <= 0) { Debug.LogWarning($"[{gameObject.name}] Rigidbody2D Gravity Scale is <= 0. Set it > 0.", this); }
        if (horizontalAcceleration <= 0) { Debug.LogError($"[{gameObject.name}] Horizontal Acceleration is <= 0. Setting to 15.", this); horizontalAcceleration = 15f; }
    }

    void Start()
    {
        if (!enabled) return;
        if (health != null) { health.OnDeath.AddListener(HandleDeath); }
        if (characterData.baseStats != null && aiPath != null) { aiPath.maxSpeed = characterData.baseStats.movementSpeed; }
        else if(aiPath != null) { Debug.LogWarning($"[{gameObject.name}] BaseStats missing in Start.", this); }
        TransitionToState(AIState.Idle);
    }

    public void InitializeAI()
    {
        if (!enabled) return;
        Debug.Log($"[{gameObject.name}] InitializeAI called. Enemy Tag: {enemyTag}", this);
        lastDecisionTime = Time.time - decisionInterval;
        TransitionToState(AIState.Searching);
    }

    void Update()
    {
        if (!enabled) return;

        UpdateStateFromCharacterData();

        if (currentState != AIState.Celebrating && currentState != AIState.Stunned && currentState != AIState.Searching) {
            if (!IsTargetValid()) { TransitionToState(AIState.Searching); }
        }

        // --- Detección de Atasco ---
        UpdateStuckTimer();

        // --- Lógica de Estado y Salida de Acción ---
        UpdateStateLogic(); // Determina shouldAIMoveHorizontally
        CheckActionStateExit();

        // --- Pathfinding y Decisiones ---
        if (currentState == AIState.Chasing) { UpdateAStarPath(); }
        if (Time.time >= lastDecisionTime + decisionInterval && CanMakeDecision()) { MakeDecision(); lastDecisionTime = Time.time; }

        // --- Debug ---
        // Debug.Log($"[{Time.frameCount}] State: {currentState} | ShouldMove: {shouldAIMoveHorizontally} | Target: {currentTargetTransform?.name ?? "None"} | HasPath: {aiPath?.hasPath ?? false} | StuckT: {timeStuck:F1}", this);
    }

    /// <summary> Actualiza el temporizador que detecta si la IA está atascada. </summary>
    void UpdateStuckTimer()
    {
        // Solo cuenta si está intentando perseguir pero no se mueve significativamente
        if (currentState == AIState.Chasing && shouldAIMoveHorizontally && rb.linearVelocity.sqrMagnitude < MIN_MOVE_VELOCITY_SQR)
        {
            timeStuck += Time.deltaTime;
        }
        else
        {
            timeStuck = 0f; // Resetea si se mueve, no persigue o no debe moverse
        }
    }

    /// <summary> Comprueba si la IA lleva demasiado tiempo atascada. </summary>
    bool IsStuck() => timeStuck >= maxStuckTime;

    // --- CheckActionStateExit ---
    // (Sin cambios)
     void CheckActionStateExit() {
        if (characterData == null) return;
        if (currentState == AIState.Dashing && !characterData.isDashing) { TransitionToDefaultState(); }
        else if (currentState == AIState.Blocking && !characterData.isBlocking) { TransitionToDefaultState(); }
        else if (currentState == AIState.Parrying && !characterData.isAttemptingParry) { TransitionToDefaultState(); }
    }

    // --- TransitionToDefaultState ---
    // (Sin cambios)
    void TransitionToDefaultState() {
        if (IsTargetValid()) {
            float distSqr = (currentTargetTransform.position - transform.position).sqrMagnitude;
            float preferredSqr = preferredCombatDistance * preferredCombatDistance;
            TransitionToState(distSqr > preferredSqr ? AIState.Chasing : AIState.Idle);
        } else { TransitionToState(AIState.Searching); }
    }

    // --- FixedUpdate ---
    // (Sin cambios)
    void FixedUpdate() {
        if (shouldAIMoveHorizontally && CanMovePhysically()) { ApplyHorizontalMovement_SteeringTarget(); }
        else { StopHorizontalMovement(); }
    }

    // --- ApplyHorizontalMovement_SteeringTarget ---
    // (Sin cambios)
    private void ApplyHorizontalMovement_SteeringTarget() {
        if (aiPath == null || !aiPath.hasPath || aiPath.reachedEndOfPath || rb == null || characterData?.baseStats == null) { StopHorizontalMovement(); return; }
        Vector2 currentPosition = rb.position; Vector2 steeringTargetPos = (Vector2)aiPath.steeringTarget;
        Vector2 directionToTarget = (steeringTargetPos - currentPosition); float horizontalDirection = Mathf.Sign(directionToTarget.x);
        if (Mathf.Abs(steeringTargetPos.x - currentPosition.x) < 0.1f) { horizontalDirection = 0; }
        float targetHorizontalSpeed = horizontalDirection * characterData.baseStats.movementSpeed;
        if (currentState == AIState.Blocking && characterData.baseStats != null) { targetHorizontalSpeed *= characterData.baseStats.blockSpeedMultiplier; }
        float newVelocityX = Mathf.MoveTowards(rb.linearVelocity.x, targetHorizontalSpeed, horizontalAcceleration * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector2(newVelocityX, rb.linearVelocity.y);
    }

    // --- StopHorizontalMovement ---
    // (Sin cambios)
    private void StopHorizontalMovement() {
        if (rb == null || !CanMovePhysically() || currentState == AIState.Dashing) return;
        float newVelocityX = Mathf.MoveTowards(rb.linearVelocity.x, 0f, horizontalAcceleration * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector2(newVelocityX, rb.linearVelocity.y);
    }

    // --- CanMovePhysically ---
    // (Sin cambios)
    private bool CanMovePhysically() {
        return enabled && rb != null && characterData != null && !characterData.isStunned && !characterData.isDashing && currentState != AIState.Celebrating;
    }

    // --- UpdateStateFromCharacterData ---
    // (Sin cambios)
    private void UpdateStateFromCharacterData() {
         if (characterData == null) return;
        if (characterData.isStunned && currentState != AIState.Stunned) { TransitionToState(AIState.Stunned); return; }
        if (!characterData.isStunned && currentState == AIState.Stunned) { TransitionToState(AIState.Searching); }
    }

    // --- UpdateStateLogic ---
    // (Sin cambios)
    private void UpdateStateLogic() {
        shouldAIMoveHorizontally = false;
        switch (currentState) {
            case AIState.Idle: case AIState.Searching: case AIState.Attacking: case AIState.UsingSkill:
            case AIState.Parrying: case AIState.Jumping: case AIState.Stunned: case AIState.Celebrating:
                shouldAIMoveHorizontally = false; break;
            case AIState.Chasing:
                shouldAIMoveHorizontally = IsTargetValid() && aiPath != null && aiPath.hasPath && !aiPath.reachedEndOfPath; break;
            case AIState.Blocking:
                 shouldAIMoveHorizontally = IsTargetValid() && aiPath != null && aiPath.hasPath && !aiPath.reachedEndOfPath; break;
            case AIState.Dashing:
                 shouldAIMoveHorizontally = false; break;
        }
    }

    // --- TransitionToState ---
    // (Resetear timer de atasco al cambiar de estado)
    private void TransitionToState(AIState newState) {
        if (currentState == newState && newState != AIState.Searching) return;
        AIState previousState = currentState; currentState = newState;
        shouldAIMoveHorizontally = false;
        timeStuck = 0f; // <-- RESETEAR TEMPORIZADOR DE ATASCO
         switch (currentState) {
             case AIState.Chasing: EnsureMovingTowardsTarget(); break;
             case AIState.Searching: FindTarget(); break;
         }
     }

    // --- CanMakeDecision ---
    // (Sin cambios)
    private bool CanMakeDecision() {
        return currentState != AIState.Stunned && currentState != AIState.Celebrating &&
               currentState != AIState.Dashing && currentState != AIState.Parrying;
    }

    // --- MakeDecision (Modificado para considerar atasco) ---
     void MakeDecision() {
         if (!IsTargetValid()) { TransitionToState(AIState.Searching); return; }
         if (characterData == null || combat == null || characterMovement == null) return;

         bool isGrounded = characterMovement.IsGrounded();
         bool amStuck = IsStuck(); // Comprobar si está atascado

         // --- Jump Decision (Prioridad si está atascado o necesita subir) ---
         if (isGrounded && currentState != AIState.Jumping && (amStuck || ShouldJump())) {
             if (amStuck) Debug.Log($"[{gameObject.name}] Is Stuck! Attempting jump.");
             if (characterMovement.Jump()) {
                 TransitionToState(AIState.Jumping);
                 StartCoroutine(ReturnToDefaultStateAfterDelay(0.3f)); // Dar un poco más de tiempo al salto
                 return;
             } else if (amStuck) {
                 // Si está atascado e intentar saltar falló, ¿qué hacer?
                 // ¿Quizás intentar un dash lateral si es posible? (Lógica futura)
                 Debug.LogWarning($"[{gameObject.name}] Is Stuck but failed to jump!");
             }
         }

         // --- Defensive ---
         Vector3 currentPosition = transform.position; Vector3 targetPosition = currentTargetTransform.position;
         bool predictedAttack = PredictEnemyAttack();
         if (predictedAttack && currentState != AIState.Blocking && currentState != AIState.Dashing && currentState != AIState.Parrying) {
             float choice = Random.value; if (choice < parryPreference && combat.TryParry()) { TransitionToState(AIState.Parrying); return; }
             else if (choice < parryPreference + dodgePreference && CanAffordDash()) { Vector2 evadeDir = ((Vector2)currentPosition - (Vector2)targetPosition).normalized; if (evadeDir == Vector2.zero) evadeDir = -transform.right; if (combat.TryDash(evadeDir)) { TransitionToState(AIState.Dashing); return; } }
             else if (CanAffordBlock() && isGrounded) { if (combat.TryStartBlocking()) { TransitionToState(AIState.Blocking); return; } }
         } else if (!predictedAttack && currentState == AIState.Blocking) { combat.StopBlocking(); return; }

         // --- Si acción actual, no decidir ofensiva/movimiento ---
         if (currentState == AIState.Blocking || currentState == AIState.Dashing || currentState == AIState.Parrying || currentState == AIState.Jumping) { return; }

         // --- Offensive ---
         float distanceToTargetSqr = (targetPosition - currentPosition).sqrMagnitude;
         float attackRangeSqr = characterData.baseStats.attackRange * characterData.baseStats.attackRange;
         float preferredDistSqr = preferredCombatDistance * preferredCombatDistance;
         bool isLowHealth = characterData.currentHealth / characterData.baseStats.maxHealth <= lowHealthThreshold;
         SkillData chosenSkill = ChooseSkillToUse(isLowHealth);
         if (chosenSkill != null && Random.value < skillUseChance) { bool skillRequiresTarget = chosenSkill.range > 0 || chosenSkill.skillType == SkillType.DirectDamage || chosenSkill.skillType == SkillType.Projectile; bool skillInRange = chosenSkill.range <= 0 || distanceToTargetSqr <= (chosenSkill.range * chosenSkill.range); if (skillInRange || !skillRequiresTarget) { if (combat.TryUseSkill(chosenSkill)) { TransitionToState(AIState.UsingSkill); StartCoroutine(ReturnToDefaultStateAfterDelay(chosenSkill.cooldown)); return; } } else if (skillRequiresTarget && !skillInRange) { TransitionToState(AIState.Chasing); return; } }
         bool isTargetInRange_Basic = distanceToTargetSqr <= attackRangeSqr; if (isTargetInRange_Basic && characterData.IsAttackReady() && isGrounded) { if (combat.TryAttack()) { TransitionToState(AIState.Attacking); StartCoroutine(ReturnToDefaultStateAfterDelay(characterData.baseStats.attackCooldown * 0.8f)); return; } }

         // --- Movement ---
         bool isTargetInRange_Preferred = distanceToTargetSqr <= preferredDistSqr; float dashEngageRange = preferredCombatDistance + dashEngageRangeBonus;
         if (currentState == AIState.Chasing && !isTargetInRange_Preferred && distanceToTargetSqr > (dashEngageRange * dashEngageRange) && CanAffordDash() && Random.value < aggression && isGrounded) { Vector2 engageDir = ((Vector2)targetPosition - (Vector2)currentPosition).normalized; if (engageDir == Vector2.zero) engageDir = transform.right; if (combat.TryDash(engageDir)) { TransitionToState(AIState.Dashing); return; } }
         if (!isTargetInRange_Preferred) { TransitionToState(AIState.Chasing); } else { TransitionToState(AIState.Idle); }
     }

    // --- ShouldJump (Modificado para simplificar y usar jumpUpThresholdY) ---
    private bool ShouldJump()
    {
        // Condiciones básicas: Tener objetivo, estar en suelo, tener componentes
        if (!IsTargetValid() || !characterMovement.IsGrounded() || characterData == null || aiPath == null) return false;

        Vector2 currentPos = transform.position;
        Vector2 targetPos = currentTargetTransform.position;
        float targetRelativeY = targetPos.y - currentPos.y;

        // Condición 1: El objetivo está significativamente más alto
        bool targetIsHigher = targetRelativeY > jumpUpThresholdY;
        if (!targetIsHigher) return false; // Solo salta si el objetivo está ARRIBA

        // Condición 2: El camino A* está bloqueado O es inválido
        // (Asumimos que si A* falla o está bloqueado Y el objetivo está arriba, saltar es una opción)
        bool pathIsBad = !aiPath.hasPath || (aiPath.hasPath && aiPath.reachedEndOfPath && !combat.IsTargetInRange(currentTargetTransform, preferredCombatDistance * 1.1f)); // Llegó al final de ruta pero no al objetivo?
        bool pathFailed = !aiPath.hasPath && !isTargetReachable; // A* explícitamente falló

        if (pathIsBad || pathFailed)
        {
             // Comprobación adicional: ¿Hay espacio para aterrizar cerca del objetivo? (Simple Raycast hacia abajo desde cerca del objetivo)
             Vector2 landingCheckOrigin = targetPos + Vector2.up * 0.1f; // Un poco por encima de los pies del objetivo
             RaycastHit2D landingHit = Physics2D.Raycast(landingCheckOrigin, Vector2.down, 1.5f, characterMovement.groundLayer);
             if(landingHit.collider != null) {
                 // Debug.Log($"[{gameObject.name}] Considering Jump UP: Target Higher ({targetRelativeY:F1}), Path Bad/Failed ({pathIsBad || pathFailed}), Landing Spot Found.");
                 return true;
             } else {
                 // Debug.Log($"[{gameObject.name}] Considered Jump UP, but no landing spot found near target.");
                 return false;
             }
        }
        return false; // Objetivo está arriba pero A* tiene una ruta válida (probablemente una rampa)
    }

    // ReturnToDefaultStateAfterDelay (Sin cambios)
     IEnumerator ReturnToDefaultStateAfterDelay(float delay) { yield return new WaitForSeconds(delay); if (currentState == AIState.Attacking || currentState == AIState.UsingSkill || currentState == AIState.Jumping) { TransitionToDefaultState(); } }
    // IsTargetValid, CanAffordDash, CanAffordBlock (Sin cambios)
    private bool IsTargetValid() { return currentTargetTransform != null && currentTargetHealth != null && currentTargetHealth.IsAlive(); }
    private bool CanAffordDash() { return characterData != null && characterData.baseStats != null && characterData.currentStamina >= characterData.baseStats.dashCost; }
    private bool CanAffordBlock() { return characterData != null && characterData.baseStats != null && characterData.currentStamina > 0; }
    // FindTarget (Sin cambios)
    void FindTarget() { if (characterData == null) return; GameObject[] potentialTargets = GameObject.FindGameObjectsWithTag(enemyTag); Transform closestTarget = null; float minDistanceSqr = float.MaxValue; Vector2 currentPos = transform.position; foreach (GameObject pt in potentialTargets) { if (pt == gameObject) continue; HealthSystem ph = pt.GetComponent<HealthSystem>(); if (ph == null || !ph.IsAlive()) continue; float distSqr = ((Vector2)pt.transform.position - currentPos).sqrMagnitude; if (distSqr < minDistanceSqr) { minDistanceSqr = distSqr; closestTarget = pt.transform; } } if (closestTarget != null) { if (currentTargetTransform != closestTarget) { currentTargetTransform = closestTarget; currentTargetHealth = currentTargetTransform.GetComponent<HealthSystem>(); combat?.SetTarget(currentTargetHealth); lastPathRequestTime = -pathUpdateRate; isTargetReachable = true; TransitionToState(AIState.Chasing); } } else { if (currentTargetTransform != null) { currentTargetTransform = null; currentTargetHealth = null; combat?.SetTarget(null); TransitionToState(AIState.Idle); } } }
    // PredictEnemyAttack (Sin cambios)
    bool PredictEnemyAttack() { if (!IsTargetValid() || characterData?.baseStats == null || rb == null) return false; float predictRange = characterData.baseStats.attackRange * 1.5f; float distSqr = ((Vector2)currentTargetTransform.position - rb.position).sqrMagnitude; if (distSqr < predictRange * predictRange) { Vector2 dirToMe = (rb.position - (Vector2)currentTargetTransform.position).normalized; Vector2 enemyForward = currentTargetTransform.right; if (currentTargetTransform.localScale.x < 0) enemyForward = -enemyForward; float dot = Vector2.Dot(enemyForward, dirToMe); if (dot > 0.7f && Random.value < 0.15f) { return true; } } return false; }
    // ChooseSkillToUse (Sin cambios)
     SkillData ChooseSkillToUse(bool isLowHealth) { if (characterData?.skills == null || characterData.skills.Count == 0) return null; List<SkillData> usableSkills = new List<SkillData>(); foreach (SkillData skill in characterData.skills) { if (skill != null && characterData.IsSkillReady(skill)) { usableSkills.Add(skill); } } if (usableSkills.Count == 0) return null; if (isLowHealth) { foreach (SkillData skill in usableSkills) { if (skill.skillType == SkillType.Heal) return skill; } } List<SkillData> damageSkills = usableSkills.FindAll(s => s.skillType == SkillType.DirectDamage || s.skillType == SkillType.Projectile || s.skillType == SkillType.AreaOfEffect); if (damageSkills.Count > 0) { return damageSkills[Random.Range(0, damageSkills.Count)]; } return usableSkills[Random.Range(0, usableSkills.Count)]; }
    // EnsureMovingTowardsTarget (Sin cambios)
    void EnsureMovingTowardsTarget() { if (aiPath == null) return; if (IsTargetValid()) { aiPath.destination = currentTargetTransform.position; } else { TransitionToState(AIState.Searching); } }
    // UpdateAStarPath (Sin cambios)
    void UpdateAStarPath() { if (!enabled || aiPath == null || !aiPath.canSearch || seeker == null || !IsTargetValid()) return; if (Time.time > lastPathRequestTime + pathUpdateRate) { RequestPathToTarget(); lastPathRequestTime = Time.time; } }
    // RequestPathToTarget (Sin cambios)
    void RequestPathToTarget() { if (seeker.IsDone() && IsTargetValid()) { seeker.StartPath(rb.position, currentTargetTransform.position, OnPathComplete); } }
    // OnPathComplete (Sin cambios)
     public void OnPathComplete(Path p) { if (!enabled) return; if (p.error) { Debug.LogWarning($"[{gameObject.name}] Path error: {p.errorLog}", this); isTargetReachable = false; if (currentState == AIState.Chasing) { TransitionToState(AIState.Idle); } } else { isTargetReachable = true; } }
    // HandleDeath (Sin cambios)
     void HandleDeath() { if (!enabled) return; Debug.Log($"[{gameObject.name}] AIController reacting to death."); TransitionToState(AIState.Stunned); shouldAIMoveHorizontally = false; enabled = false; }
    // StartCelebrating (Sin cambios)
    public void StartCelebrating() { if (!this.enabled || health == null || !health.IsAlive() || currentState == AIState.Celebrating) return; Debug.Log($"[{gameObject.name}] starts celebrating!"); TransitionToState(AIState.Celebrating); shouldAIMoveHorizontally = false; combat?.InterruptActions(); currentTargetTransform = null; currentTargetHealth = null; combat?.SetTarget(null); combat?.SetAnimatorTrigger("Celebrate"); }
    // OnDestroy (Sin cambios)
    void OnDestroy() { if (health != null) { health.OnDeath.RemoveListener(HandleDeath); } StopAllCoroutines(); }

} // Fin de la clase