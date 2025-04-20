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
    public float jumpThresholdY = 1.5f;
    public float jumpObstacleCheckDistance = 1.0f;

    [Header("Movement Control (Used in FixedUpdate)")]
    [Tooltip("Cuán rápido el personaje alcanza la velocidad horizontal objetivo. DEBE SER > 0.")]
    public float horizontalAcceleration = 15f;

    // --- Internal State ---
    private Transform currentTargetTransform;
    private HealthSystem currentTargetHealth;
    private float lastDecisionTime;
    private bool isTargetReachable = true;
    private bool shouldAIMoveHorizontally = false;

    void Awake()
    {
        // --- Get Components ---
        seeker = GetComponent<Seeker>();
        aiPath = GetComponent<AIPath>();
        combat = GetComponent<CharacterCombat>();
        health = GetComponent<HealthSystem>();
        characterData = GetComponent<CharacterData>();
        rb = GetComponent<Rigidbody2D>();
        characterMovement = GetComponent<CharacterMovement>();

        // --- Validate Components ---
        if (seeker == null) Debug.LogError("Seeker component not found!", this);
        if (aiPath == null) Debug.LogError("AIPath component not found!", this);
        if (combat == null) Debug.LogError("CharacterCombat component not found!", this);
        if (health == null) Debug.LogError("HealthSystem component not found!", this);
        if (characterData == null) Debug.LogError("CharacterData not found!", this);
        if (rb == null) Debug.LogError("Rigidbody2D component not found!", this);
        if (characterMovement == null) Debug.LogError("CharacterMovement component not found!", this);

        // --- Configure AIPath ---
        if (aiPath != null)
        {
            aiPath.gravity = Vector3.zero;
            aiPath.enableRotation = false;
            aiPath.updateRotation = false;
            aiPath.updatePosition = false;
            aiPath.canMove = false; // *** Mantenido en FALSE ***
            aiPath.canSearch = true;
            aiPath.orientation = OrientationMode.YAxisForward;
            aiPath.maxAcceleration = float.PositiveInfinity;
            aiPath.pickNextWaypointDist = 1f;
            aiPath.endReachedDistance = preferredCombatDistance * 0.8f;
            aiPath.slowdownDistance = preferredCombatDistance;
        }

        // --- Rigidbody & Acceleration Check ---
         if (rb != null && rb.gravityScale <= 0) {
             Debug.LogWarning($"Rigidbody2D Gravity Scale on {gameObject.name} is {rb.gravityScale}. Characters might fly! Set it > 0.", this);
         }
         if (horizontalAcceleration <= 0) {
             Debug.LogError($"Horizontal Acceleration on {gameObject.name} was {horizontalAcceleration}. Movement will not work! Setting to 15. Adjust in Inspector.", this);
             horizontalAcceleration = 15f;
         }
    }

    void Start()
    {
        if (!this.enabled) return;
        if (health != null) { health.OnDeath.AddListener(HandleDeath); }
        if (characterData.baseStats != null && aiPath != null) {
            aiPath.maxSpeed = characterData.baseStats.movementSpeed;
        } else if(aiPath != null) { Debug.LogWarning($"BaseStats not assigned on {gameObject.name} in Start. AIPath using default speed: {aiPath.maxSpeed}.", this); }
        TransitionToState(AIState.Idle);
    }

    public void InitializeAI()
    {
        if (!enabled) return;
        Debug.Log($"[{gameObject.name}] InitializeAI called. Enemy Tag to search: {enemyTag}", this);
        lastDecisionTime = Time.time - decisionInterval;
        TransitionToState(AIState.Searching);
    }

    void Update()
    {
        if (!enabled) return;

        UpdateStateFromCharacterData(); // Lee flags de CharacterData

        // Comprobación de objetivo si no estamos buscando/celebrando/aturdidos
        if (currentState != AIState.Celebrating && currentState != AIState.Stunned && currentState != AIState.Searching) {
            if (!IsTargetValid()) {
                Debug.Log($"[{gameObject.name}] Target lost or invalid in Update. Transitioning to Searching.");
                TransitionToState(AIState.Searching);
            }
        }

        // Ejecuta lógica de estado Y comprueba si hay que salir de estados de acción
        UpdateStateLogic();
        CheckActionStateExit(); // <--- NUEVA LLAMADA

        // Pide rutas si está persiguiendo
        if (currentState == AIState.Chasing) { UpdateAStarPath(); }

        // Toma decisiones si puede
        if (Time.time >= lastDecisionTime + decisionInterval && CanMakeDecision()) {
            MakeDecision();
            lastDecisionTime = Time.time;
        }

        // DEBUG LOG (Mantenido)
        Debug.Log($"[{Time.frameCount}] State: {currentState} | ShouldMove: {shouldAIMoveHorizontally} | Target: {currentTargetTransform?.name ?? "None"} | AI CanMove: {aiPath?.canMove ?? false} | HasPath: {aiPath?.hasPath ?? false} | ReachEnd: {aiPath?.reachedEndOfPath ?? true}", this);
    }

    // --- NUEVA FUNCIÓN ---
    /// <summary>
    /// Comprueba si la IA está en un estado de acción (Dash, Block, Parry)
    /// pero el flag correspondiente en CharacterData ya es false, indicando que la acción terminó.
    /// Si es así, transiciona de vuelta a un estado por defecto como Chasing o Idle.
    /// </summary>
    void CheckActionStateExit()
    {
        if (characterData == null) return;

        // Si estamos en Dashing pero el flag isDashing es false, salimos del estado Dashing
        if (currentState == AIState.Dashing && !characterData.isDashing)
        {
            Debug.Log($"[{gameObject.name}] Dash finished (isDashing flag is false). Transitioning out of Dashing state.");
            TransitionToDefaultState(); // Decide si ir a Chasing o Idle
        }
        // Si estamos en Blocking pero el flag isBlocking es false, salimos del estado Blocking
        else if (currentState == AIState.Blocking && !characterData.isBlocking)
        {
             Debug.Log($"[{gameObject.name}] Blocking finished/cancelled (isBlocking flag is false). Transitioning out of Blocking state.");
            TransitionToDefaultState();
        }
        // Si estamos en Parrying pero el flag isAttemptingParry es false, salimos del estado Parrying
        else if (currentState == AIState.Parrying && !characterData.isAttemptingParry)
        {
             Debug.Log($"[{gameObject.name}] Parry window closed (isAttemptingParry flag is false). Transitioning out of Parrying state.");
            TransitionToDefaultState();
        }
    }

    /// <summary>
    /// Función helper para decidir si volver a Chasing o Idle después de una acción.
    /// </summary>
    void TransitionToDefaultState()
    {
         if (IsTargetValid())
         {
             // Comprueba distancia para decidir si perseguir o estar idle
             float distSqr = (currentTargetTransform.position - transform.position).sqrMagnitude;
             float preferredSqr = preferredCombatDistance * preferredCombatDistance;
             TransitionToState(distSqr > preferredSqr ? AIState.Chasing : AIState.Idle);
         }
         else
         {
             // Si el objetivo se perdió, busca de nuevo
             TransitionToState(AIState.Searching);
         }
    }
    // --- FIN NUEVAS FUNCIONES ---


    void FixedUpdate()
    {
        if (shouldAIMoveHorizontally && CanMovePhysically()) {
            ApplyHorizontalMovement_SteeringTarget();
        } else {
            StopHorizontalMovement();
        }
    }

    // ApplyHorizontalMovement_SteeringTarget (Sin Cambios)
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

    // StopHorizontalMovement (Sin Cambios)
    private void StopHorizontalMovement() {
        if (rb == null || !CanMovePhysically() || currentState == AIState.Dashing) return;
        float newVelocityX = Mathf.MoveTowards(rb.linearVelocity.x, 0f, horizontalAcceleration * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector2(newVelocityX, rb.linearVelocity.y);
    }

    // CanMovePhysically (Sin Cambios)
    private bool CanMovePhysically() {
        return enabled && rb != null && characterData != null && !characterData.isStunned && !characterData.isDashing && currentState != AIState.Celebrating;
    }

    // UpdateStateFromCharacterData (Sin Cambios)
    private void UpdateStateFromCharacterData() {
         if (characterData == null) return;
        if (characterData.isStunned && currentState != AIState.Stunned) { TransitionToState(AIState.Stunned); return; }
        if (!characterData.isStunned && currentState == AIState.Stunned) { TransitionToState(AIState.Searching); }
        // Ya no transicionamos *hacia* Dashing/Blocking/Parrying aquí, MakeDecision lo hace.
        // CheckActionStateExit se encarga de salir de estos estados.
        // if (currentState != AIState.Stunned && currentState != AIState.Celebrating) {
        //     if (characterData.isDashing && currentState != AIState.Dashing) TransitionToState(AIState.Dashing);
        //     else if (characterData.isBlocking && currentState != AIState.Blocking) TransitionToState(AIState.Blocking);
        //     else if (characterData.isAttemptingParry && currentState != AIState.Parrying) TransitionToState(AIState.Parrying);
        // }
    }

    // UpdateStateLogic (Sin Cambios en su lógica principal, CheckActionStateExit se añade aparte)
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

    // TransitionToState (Ligero cambio: llama a FindTarget al entrar en Searching)
    private void TransitionToState(AIState newState) {
        if (currentState == newState && newState != AIState.Searching) return;
        AIState previousState = currentState; currentState = newState;
        // Debug.Log($"{gameObject.name} transitioned from {previousState} to {currentState}");
        shouldAIMoveHorizontally = false;
         switch (currentState) {
             case AIState.Chasing: EnsureMovingTowardsTarget(); break;
             case AIState.Searching: FindTarget(); break; // Intenta buscar al entrar en Searching
         }
     }

    // CanMakeDecision (Sin Cambios)
    private bool CanMakeDecision() {
        return currentState != AIState.Stunned && currentState != AIState.Celebrating &&
               currentState != AIState.Dashing && currentState != AIState.Parrying;
    }

    // MakeDecision (MODIFICADO para prevenir Dash Engage inmediato)
     void MakeDecision() {
         if (!IsTargetValid()) { TransitionToState(AIState.Searching); return; }
         if (characterData == null || combat == null || characterMovement == null) return;
         bool isGrounded = characterMovement.IsGrounded(); Vector3 currentPosition = transform.position; Vector3 targetPosition = currentTargetTransform.position;
         float distanceToTargetSqr = (targetPosition - currentPosition).sqrMagnitude; bool isLowHealth = characterData.currentHealth / characterData.baseStats.maxHealth <= lowHealthThreshold;
         bool predictedAttack = PredictEnemyAttack();
         // --- Defensive ---
         if (predictedAttack && currentState != AIState.Blocking && currentState != AIState.Dashing && currentState != AIState.Parrying) {
             float choice = Random.value; if (choice < parryPreference && combat.TryParry()) { TransitionToState(AIState.Parrying); return; }
             else if (choice < parryPreference + dodgePreference && CanAffordDash()) { Vector2 evadeDir = ((Vector2)currentPosition - (Vector2)targetPosition).normalized; if (evadeDir == Vector2.zero) evadeDir = -transform.right; if (combat.TryDash(evadeDir)) { TransitionToState(AIState.Dashing); return; } }
             else if (CanAffordBlock() && isGrounded) { if (combat.TryStartBlocking()) { TransitionToState(AIState.Blocking); return; } }
         } else if (!predictedAttack && currentState == AIState.Blocking) { combat.StopBlocking(); /* CheckActionStateExit lo gestionará */ return; } // Simplemente deja de bloquear
         // --- Jump ---
         if (isGrounded && currentState != AIState.Jumping && ShouldJump()) { if (characterMovement.Jump()) { TransitionToState(AIState.Jumping); StartCoroutine(ReturnToDefaultStateAfterDelay(0.2f)); return; } }
         // --- Si acción actual, no decidir ---
         if (currentState == AIState.Blocking || currentState == AIState.Dashing || currentState == AIState.Parrying) { return; }
         // --- Offensive ---
         float attackRangeSqr = characterData.baseStats.attackRange * characterData.baseStats.attackRange; float preferredDistSqr = preferredCombatDistance * preferredCombatDistance;
         SkillData chosenSkill = ChooseSkillToUse(isLowHealth);
         if (chosenSkill != null && Random.value < skillUseChance) { bool skillRequiresTarget = chosenSkill.range > 0 || chosenSkill.skillType == SkillType.DirectDamage || chosenSkill.skillType == SkillType.Projectile; bool skillInRange = chosenSkill.range <= 0 || distanceToTargetSqr <= (chosenSkill.range * chosenSkill.range); if (skillInRange || !skillRequiresTarget) { if (combat.TryUseSkill(chosenSkill)) { TransitionToState(AIState.UsingSkill); StartCoroutine(ReturnToDefaultStateAfterDelay(chosenSkill.cooldown)); return; } } else if (skillRequiresTarget && !skillInRange) { TransitionToState(AIState.Chasing); return; } }
         bool isTargetInRange_Basic = distanceToTargetSqr <= attackRangeSqr; if (isTargetInRange_Basic && characterData.IsAttackReady() && isGrounded) { if (combat.TryAttack()) { TransitionToState(AIState.Attacking); StartCoroutine(ReturnToDefaultStateAfterDelay(characterData.baseStats.attackCooldown * 0.8f)); return; } }
         // --- Movement ---
         bool isTargetInRange_Preferred = distanceToTargetSqr <= preferredDistSqr; float dashEngageRange = preferredCombatDistance + dashEngageRangeBonus;
         // *** MODIFICACIÓN CLAVE: Solo intentar Dash Engage si ya estamos persiguiendo ***
         if (currentState == AIState.Chasing && !isTargetInRange_Preferred && distanceToTargetSqr > (dashEngageRange * dashEngageRange) && CanAffordDash() && Random.value < aggression && isGrounded) {
             Vector2 engageDir = ((Vector2)targetPosition - (Vector2)currentPosition).normalized; if (engageDir == Vector2.zero) engageDir = transform.right; if (combat.TryDash(engageDir)) { TransitionToState(AIState.Dashing); return; }
         }
         if (!isTargetInRange_Preferred) { TransitionToState(AIState.Chasing); } else { TransitionToState(AIState.Idle); }
     }

    // ShouldJump (Sin Cambios)
    private bool ShouldJump() { if (!IsTargetValid() || !characterMovement.IsGrounded() || characterData == null) return false; Vector2 currentPos = transform.position; Vector2 targetPos = currentTargetTransform.position; float targetRelativeY = targetPos.y - currentPos.y; float targetDistanceX = Mathf.Abs(targetPos.x - currentPos.x); bool heightCondition = targetRelativeY > jumpThresholdY && targetDistanceX < preferredCombatDistance * 2.0f; if (!heightCondition) return false; Vector2 rayOrigin = currentPos + Vector2.up * 0.1f; Vector2 direction = (targetPos - currentPos).normalized; if (direction.x == 0) direction.x = Mathf.Sign(transform.localScale.x); RaycastHit2D hit = Physics2D.Raycast(rayOrigin, new Vector2(direction.x, 0).normalized, jumpObstacleCheckDistance, characterMovement.groundLayer); bool pathBlocked = hit.collider != null && hit.transform != currentTargetTransform; bool pathInvalid = aiPath != null && !aiPath.hasPath && isTargetReachable == false; bool directlyAbove = Mathf.Abs(targetPos.x - currentPos.x) < 0.5f; if (pathBlocked || pathInvalid || directlyAbove) { RaycastHit2D landingCheck = Physics2D.Raycast(targetPos + Vector2.down * 0.1f, Vector2.down, 1.0f, characterMovement.groundLayer); if (landingCheck.collider != null) { return true; } } return false; }

    // ReturnToDefaultStateAfterDelay (Ahora usa TransitionToDefaultState)
     IEnumerator ReturnToDefaultStateAfterDelay(float delay) {
         yield return new WaitForSeconds(delay);
         if (currentState == AIState.Attacking || currentState == AIState.UsingSkill || currentState == AIState.Jumping) {
             TransitionToDefaultState(); // Usa la función helper centralizada
         }
     }

    // IsTargetValid, CanAffordDash, CanAffordBlock (Sin Cambios)
    private bool IsTargetValid() { return currentTargetTransform != null && currentTargetHealth != null && currentTargetHealth.IsAlive(); }
    private bool CanAffordDash() { return characterData != null && characterData.baseStats != null && characterData.currentStamina >= characterData.baseStats.dashCost; }
    private bool CanAffordBlock() { return characterData != null && characterData.baseStats != null && characterData.currentStamina > 0; }

    // FindTarget (Sin Cambios)
    void FindTarget() { if (characterData == null) return; GameObject[] potentialTargets = GameObject.FindGameObjectsWithTag(enemyTag); Transform closestTarget = null; float minDistanceSqr = float.MaxValue; Vector2 currentPos = transform.position; foreach (GameObject pt in potentialTargets) { if (pt == gameObject) continue; HealthSystem ph = pt.GetComponent<HealthSystem>(); if (ph == null || !ph.IsAlive()) continue; float distSqr = ((Vector2)pt.transform.position - currentPos).sqrMagnitude; if (distSqr < minDistanceSqr) { minDistanceSqr = distSqr; closestTarget = pt.transform; } } if (closestTarget != null) { if (currentTargetTransform != closestTarget) { currentTargetTransform = closestTarget; currentTargetHealth = currentTargetTransform.GetComponent<HealthSystem>(); combat?.SetTarget(currentTargetHealth); lastPathRequestTime = -pathUpdateRate; isTargetReachable = true; TransitionToState(AIState.Chasing); } } else { if (currentTargetTransform != null) { currentTargetTransform = null; currentTargetHealth = null; combat?.SetTarget(null); TransitionToState(AIState.Idle); } } }

    // PredictEnemyAttack (Sin Cambios)
    bool PredictEnemyAttack() { if (!IsTargetValid() || characterData?.baseStats == null || rb == null) return false; float predictRange = characterData.baseStats.attackRange * 1.5f; float distSqr = ((Vector2)currentTargetTransform.position - rb.position).sqrMagnitude; if (distSqr < predictRange * predictRange) { Vector2 dirToMe = (rb.position - (Vector2)currentTargetTransform.position).normalized; Vector2 enemyForward = currentTargetTransform.right; if (currentTargetTransform.localScale.x < 0) enemyForward = -enemyForward; float dot = Vector2.Dot(enemyForward, dirToMe); if (dot > 0.7f && Random.value < 0.15f) { return true; } } return false; }

    // ChooseSkillToUse (Sin Cambios)
     SkillData ChooseSkillToUse(bool isLowHealth) { if (characterData?.skills == null || characterData.skills.Count == 0) return null; List<SkillData> usableSkills = new List<SkillData>(); foreach (SkillData skill in characterData.skills) { if (skill != null && characterData.IsSkillReady(skill)) { usableSkills.Add(skill); } } if (usableSkills.Count == 0) return null; if (isLowHealth) { foreach (SkillData skill in usableSkills) { if (skill.skillType == SkillType.Heal) return skill; } } List<SkillData> damageSkills = usableSkills.FindAll(s => s.skillType == SkillType.DirectDamage || s.skillType == SkillType.Projectile || s.skillType == SkillType.AreaOfEffect); if (damageSkills.Count > 0) { return damageSkills[Random.Range(0, damageSkills.Count)]; } return usableSkills[Random.Range(0, usableSkills.Count)]; }

    // EnsureMovingTowardsTarget (Sin Cambios)
    void EnsureMovingTowardsTarget() { if (aiPath == null) return; if (IsTargetValid()) { aiPath.destination = currentTargetTransform.position; } else { TransitionToState(AIState.Searching); } }

    // UpdateAStarPath (Sin Cambios)
    void UpdateAStarPath() { if (!enabled || aiPath == null || !aiPath.canSearch || seeker == null || !IsTargetValid()) return; if (Time.time > lastPathRequestTime + pathUpdateRate) { RequestPathToTarget(); lastPathRequestTime = Time.time; } }

    // RequestPathToTarget (Sin Cambios)
    void RequestPathToTarget() { if (seeker.IsDone() && IsTargetValid()) { seeker.StartPath(rb.position, currentTargetTransform.position, OnPathComplete); } }

    // OnPathComplete (Sin Cambios)
     public void OnPathComplete(Path p) { if (!enabled) return; if (p.error) { Debug.LogWarning($"{gameObject.name} path error: {p.errorLog}", this); isTargetReachable = false; if (currentState == AIState.Chasing) { TransitionToState(AIState.Idle); } } else { isTargetReachable = true; } }

    // HandleDeath (Sin Cambios)
     void HandleDeath() { if (!enabled) return; Debug.Log($"{gameObject.name} AIController reacting to death."); TransitionToState(AIState.Stunned); shouldAIMoveHorizontally = false; enabled = false; }

    // StartCelebrating (Sin Cambios)
    public void StartCelebrating() { if (!this.enabled || health == null || !health.IsAlive() || currentState == AIState.Celebrating) return; Debug.Log($"{gameObject.name} starts celebrating!"); TransitionToState(AIState.Celebrating); shouldAIMoveHorizontally = false; combat?.InterruptActions(); currentTargetTransform = null; currentTargetHealth = null; combat?.SetTarget(null); combat?.SetAnimatorTrigger("Celebrate"); }

    // OnDestroy (Sin Cambios)
    void OnDestroy() { if (health != null) { health.OnDeath.RemoveListener(HandleDeath); } StopAllCoroutines(); }

} // Fin de la clase