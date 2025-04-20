using UnityEngine;
using Pathfinding; // Necesario
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
    private enum AIState { Idle, Searching, Chasing, Attacking, UsingSkill, Blocking, Parrying, Dashing, Jumping, Fleeing, Stunned, Celebrating }

    [Header("Debug")]
    [SerializeField] private AIState currentState = AIState.Idle;

    [Header("Targeting & Team")]
    public string enemyTag = "Enemy"; // Revisa que sea el tag correcto de tu oponente

    [Header("Pathfinding")]
    public float pathUpdateRate = 0.5f;
    private float lastPathRequestTime = -1f;

    // Component References
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
    public float jumpUpThresholdY = 1.5f;
    public float jumpObstacleCheckDistance = 1.0f;
    public float maxStuckTime = 1.0f;

    [Header("Movement Control")]
    public float horizontalAcceleration = 15f;

    // Internal State
    private Transform currentTargetTransform;
    private HealthSystem currentTargetHealth;
    private float lastDecisionTime;
    private bool isTargetReachable = true;
    private bool shouldAIMoveHorizontally = false;
    private float timeStuck = 0f;
    private const float MIN_MOVE_VELOCITY_SQR = 0.01f;

    void Awake()
    {
        seeker = GetComponent<Seeker>();
        aiPath = GetComponent<AIPath>();
        combat = GetComponent<CharacterCombat>();
        health = GetComponent<HealthSystem>();
        characterData = GetComponent<CharacterData>();
        rb = GetComponent<Rigidbody2D>();
        characterMovement = GetComponent<CharacterMovement>();

        if (seeker==null || aiPath==null || combat==null || health==null || characterData==null || rb==null || characterMovement==null) {
            Debug.LogError($"[{gameObject.name}] Missing components!", this); enabled = false; return;
        }

        // Config AIPath base
        if (aiPath != null) {
            aiPath.gravity = Vector3.zero; aiPath.enableRotation = false; aiPath.updateRotation = false;
            aiPath.updatePosition = false; aiPath.canMove = false; aiPath.canSearch = true;
            aiPath.orientation = OrientationMode.YAxisForward;
            aiPath.maxAcceleration = float.PositiveInfinity; aiPath.pickNextWaypointDist = 1f;
            aiPath.endReachedDistance = preferredCombatDistance * 0.8f;
            aiPath.slowdownDistance = preferredCombatDistance;
        }
        if (rb != null && rb.gravityScale <= 0) Debug.LogWarning($"[{gameObject.name}] Rigidbody Gravity Scale <= 0", this);
        if (horizontalAcceleration <= 0) { Debug.LogError($"[{gameObject.name}] Horizontal Accel must be > 0", this); horizontalAcceleration = 15f; }
    }

    void Start()
    {
        if (!enabled) return;
        if (health != null) health.OnDeath.AddListener(HandleDeath);
        TransitionToState(AIState.Idle); // Empieza Idle hasta InitializeAI
    }

    public void InitializeAI()
    {
        if (!enabled || characterData == null || aiPath == null) {
             Debug.LogError($"Cannot Initialize AI for {gameObject.name}. Inactive or missing data/aiPath.", this);
             return;
        }
        Debug.Log($"[{gameObject.name}] InitializeAI. Enemy Tag: {enemyTag}");
        aiPath.maxSpeed = characterData.baseMovementSpeed;
        lastDecisionTime = Time.time - decisionInterval; // Fuerza decisión temprana
        TransitionToState(AIState.Searching); // Empieza buscando
    }

    void Update()
    {
        if (!enabled || characterData == null) return;
        UpdateStateFromCharacterData(); // Chequea stun
        // Si no está celebrando/aturdido/buscando y pierde el objetivo -> Buscar
        if (currentState != AIState.Celebrating && currentState != AIState.Stunned && currentState != AIState.Searching && !IsTargetValid()) {
            TransitionToState(AIState.Searching);
        }
        UpdateStuckTimer(); // Actualiza contador de atasco
        UpdateStateLogic(); // Define si debe intentar moverse este frame
        CheckActionStateExit(); // Sale de acciones si terminaron
        if (currentState == AIState.Chasing) UpdateAStarPath(); // Actualiza path si persigue
        // Toma una decisión si puede y ha pasado el intervalo
        if (Time.time >= lastDecisionTime + decisionInterval && CanMakeDecision()) {
            MakeDecision();
            lastDecisionTime = Time.time;
        }
    }

     void FixedUpdate() {
         // Aplica o detiene movimiento físico basado en shouldAIMoveHorizontally
         if (shouldAIMoveHorizontally && CanMovePhysically()) ApplyHorizontalMovement_SteeringTarget();
         else if (CanMovePhysically()) StopHorizontalMovement();
     }

    // --- Lógica de Estados ---
    void UpdateStateFromCharacterData() {
        if (characterData.isStunned && currentState != AIState.Stunned) TransitionToState(AIState.Stunned);
        if (!characterData.isStunned && currentState == AIState.Stunned) TransitionToState(AIState.Searching); // Al salir de stun, busca
    }

    void UpdateStateLogic() {
        shouldAIMoveHorizontally = false; // Por defecto no se mueve
        switch (currentState) {
            case AIState.Chasing: // Se mueve si persigue Y tiene objetivo/ruta válida
            case AIState.Blocking: // Puede moverse lento si persigue mientras bloquea
                shouldAIMoveHorizontally = IsTargetValid() && aiPath != null && aiPath.hasPath && !aiPath.reachedEndOfPath;
                break;
            // En otros estados (Idle, Attacking, etc.), la IA no controla el movimiento directamente aquí
        }
    }

    void TransitionToState(AIState newState) {
        if (currentState == newState && newState != AIState.Searching) return; // Evita re-entrar (excepto Searching)
        // Debug.Log($"[{gameObject.name}] Transition: {currentState} -> {newState}");
        currentState = newState;
        shouldAIMoveHorizontally = false; // Resetea flag de movimiento
        timeStuck = 0f; // Resetea contador de atasco

        // Acciones al ENTRAR al estado nuevo
        switch (currentState) {
            case AIState.Idle: case AIState.Stunned: case AIState.Celebrating:
                if(aiPath != null) aiPath.isStopped = true; StopHorizontalMovement(); break;
            case AIState.Searching:
                if(aiPath != null) aiPath.isStopped = true; FindTarget(); break; // Intenta encontrar target al entrar
            case AIState.Chasing: case AIState.Blocking:
                if(aiPath != null) aiPath.isStopped = false; EnsureMovingTowardsTarget(); break; // Empieza a moverse/calcular path
        }
    }

     // Si una acción como Dash o Parry termina, decide qué hacer a continuación
     void CheckActionStateExit() {
         if (characterData == null) return;
         bool stateChanged = false;
         if (currentState == AIState.Dashing && !characterData.isDashing) stateChanged = true;
         else if (currentState == AIState.Blocking && !characterData.isBlocking) stateChanged = true;
         else if (currentState == AIState.Parrying && !characterData.isAttemptingParry) stateChanged = true;
         // Si estaba saltando y ya tocó suelo, también debe re-evaluar
         else if (currentState == AIState.Jumping && characterMovement != null && characterMovement.IsGrounded()) stateChanged = true;
         // Podrías añadir flags IsAttacking, IsUsingSkill si las implementas en CharacterData/Combat

         if (stateChanged) {
              // Debug.Log($"[{gameObject.name}] Action ended, transitioning to default state check.");
              TransitionToDefaultState();
         }
     }

     // *** CORREGIDO: Esta función AHORA transiciona a Idle/Searching ***
     // La lógica real de Chasing vs Idle la hará MakeDecision en el siguiente ciclo.
     void TransitionToDefaultState() {
          // No chequea CanMakeDecision aquí, porque estamos saliendo de una acción.
         if (IsTargetValid()) {
             // Va a Idle. El próximo MakeDecision determinará si necesita Chase.
             TransitionToState(AIState.Idle);
         } else {
             // Si el objetivo se perdió durante la acción, busca de nuevo.
             TransitionToState(AIState.Searching);
         }
     }

    // --- Decisión Principal ---
    bool CanMakeDecision() {
        // Solo puede tomar decisiones si NO está en medio de una acción clave o incapacitado
        return currentState != AIState.Stunned && currentState != AIState.Celebrating && currentState != AIState.Dashing &&
               currentState != AIState.Parrying && currentState != AIState.Attacking && currentState != AIState.UsingSkill &&
               currentState != AIState.Jumping;
    }

    void MakeDecision()
    {
        // Re-chequeos
        if (!IsTargetValid() || combat == null || characterMovement == null) { TransitionToState(AIState.Searching); return; }

        bool isGrounded = characterMovement.IsGrounded();
        bool amStuck = IsStuck();
        Vector3 currentPosition = transform.position;
        Vector3 targetPosition = currentTargetTransform.position;
        float distanceToTargetSqr = (targetPosition - currentPosition).sqrMagnitude;

        // 1. Saltar
        if (isGrounded && (amStuck || ShouldJump())) {
            if (characterMovement.Jump()) { TransitionToState(AIState.Jumping); StartCoroutine(ReturnToDefaultStateAfterDelay(0.5f)); return; }
            else if (amStuck) Debug.LogWarning($"[{gameObject.name}] Stuck but failed jump!");
        }

        // 2. Defensa
        bool predictedAttack = PredictEnemyAttack();
        if (predictedAttack) {
            float choice = Random.value;
            if (choice < parryPreference && CanAffordParry() && combat.TryParry()) { TransitionToState(AIState.Parrying); return; }
            else if (choice < parryPreference + dodgePreference && CanAffordDash()) {
                Vector2 evadeDir = ((Vector2)currentPosition - (Vector2)targetPosition).normalized;
                if (evadeDir == Vector2.zero) evadeDir = (Random.value < 0.5f ? Vector2.left : Vector2.right) * Mathf.Sign(transform.localScale.x);
                if (combat.TryDash(evadeDir)) { TransitionToState(AIState.Dashing); return; }
            } else if (CanAffordBlock() && isGrounded && combat.TryStartBlocking()) { TransitionToState(AIState.Blocking); return; }
        } else if (currentState == AIState.Blocking) { combat.StopBlocking(); }

        // 3. Ofensiva
        bool isLowHealth = (characterData.currentHealth / characterData.baseMaxHealth) <= lowHealthThreshold;
        SkillData chosenSkill = ChooseSkillToUse(isLowHealth);
        if (chosenSkill != null && Random.value < skillUseChance) {
            bool skillReqTarget = chosenSkill.range > 0 || chosenSkill.skillType == SkillType.DirectDamage || chosenSkill.skillType == SkillType.Projectile;
            bool skillInRange = chosenSkill.range <= 0 || distanceToTargetSqr <= (chosenSkill.range * chosenSkill.range);
            if (skillInRange || !skillReqTarget) {
                if (combat.TryUseSkill(chosenSkill)) { TransitionToState(AIState.UsingSkill); StartCoroutine(ReturnToDefaultStateAfterDelay(chosenSkill.cooldown)); return; }
            } else if (skillReqTarget && !skillInRange) { TransitionToState(AIState.Chasing); return; }
        }

        float actualAttackRange = characterData.baseAttackRange * (characterData.equippedWeapon?.attackRangeMultiplier ?? 1.0f);
        float attackRangeSqr = actualAttackRange * actualAttackRange;
        bool inBasicAttackRange = distanceToTargetSqr <= attackRangeSqr;

        if (inBasicAttackRange && characterData.IsAttackReady() && isGrounded) {
            if (combat.TryAttack()) {
                TransitionToState(AIState.Attacking);
                float actualCd = characterData.baseAttackCooldown * (characterData.equippedWeapon?.attackCooldownMultiplier ?? 1.0f);
                StartCoroutine(ReturnToDefaultStateAfterDelay(actualCd * 0.8f)); return;
            }
        }

        // 4. Movimiento / Estado Final
        bool inPreferredRange = distanceToTargetSqr <= (preferredCombatDistance * preferredCombatDistance);
        float dashEngageSqr = (preferredCombatDistance + dashEngageRangeBonus) * (preferredCombatDistance + dashEngageRangeBonus);

        if (currentState == AIState.Chasing && !inPreferredRange && distanceToTargetSqr > dashEngageSqr && CanAffordDash() && Random.value < aggression && isGrounded) {
            Vector2 engageDir = ((Vector2)targetPosition - (Vector2)currentPosition).normalized;
            if (engageDir == Vector2.zero) engageDir = transform.right * Mathf.Sign(transform.localScale.x);
            if (combat.TryDash(engageDir)) { TransitionToState(AIState.Dashing); return; }
        }

        // Lógica Chasing vs Idle (Versión 3 - Corregida)
        if (!inBasicAttackRange) {
            TransitionToState(AIState.Chasing); // Si fuera de rango -> Perseguir
        } else { // Si dentro de rango...
            if (characterData.IsAttackReady()) {
                 TransitionToState(AIState.Idle); // Si listo para atacar pero no lo hizo -> Esperar
            } else {
                 TransitionToState(AIState.Chasing); // Si en rango PERO en cooldown -> Seguir persiguiendo (microajustes)
            }
        }
    }


    // --- Helpers de Decisión ---
    bool ShouldJump() {
         if (!IsTargetValid() || !characterMovement.IsGrounded() || aiPath == null) return false;
         float targetRelativeY = currentTargetTransform.position.y - transform.position.y;
         if (targetRelativeY <= jumpUpThresholdY) return false;
         bool pathIsBad = !aiPath.hasPath || !isTargetReachable;
         if (pathIsBad) return true;
         // Podrías añadir chequeo de obstáculo con Raycast aquí si es necesario
         return false;
    }

    IEnumerator ReturnToDefaultStateAfterDelay(float delay) {
         yield return new WaitForSeconds(delay);
         // Solo transiciona si NO está en un estado que no debería interrumpirse
         if (CanMakeDecision() || currentState == AIState.Attacking || currentState == AIState.UsingSkill || currentState == AIState.Jumping) {
             // Debug.Log($"[{gameObject.name}] Action/Skill/Jump finished, returning to default state check.");
             TransitionToDefaultState();
         } else {
              // Debug.Log($"[{gameObject.name}] Action finished, but current state ({currentState}) prevents transitioning to default.");
         }
     }

    bool IsTargetValid() { return currentTargetTransform != null && currentTargetHealth != null && currentTargetHealth.IsAlive(); }
    bool CanAffordDash() { return characterData != null && characterData.currentStamina >= characterData.baseDashCost; }
    bool CanAffordBlock() { return characterData != null && characterData.currentStamina > 0; } // Solo necesita > 0 para empezar
    bool CanAffordParry() { return characterData != null && characterData.currentStamina >= characterData.baseParryCost; }

    // --- Búsqueda Objetivo ---
     void FindTarget() {
         if (characterData == null) return;
         GameObject[] potentialTargets = GameObject.FindGameObjectsWithTag(enemyTag);
         Transform closestTarget = null; float minDistanceSqr = float.MaxValue; Vector2 currentPos = transform.position;
         foreach (GameObject pt in potentialTargets) {
             if (pt == gameObject) continue; HealthSystem ph = pt.GetComponent<HealthSystem>(); if (ph == null || !ph.IsAlive()) continue;
             float distSqr = ((Vector2)pt.transform.position - currentPos).sqrMagnitude;
             if (distSqr < minDistanceSqr) { minDistanceSqr = distSqr; closestTarget = pt.transform; }
         }
         if (closestTarget != null) {
             //if (currentTargetTransform != closestTarget) { // Comentado: siempre reasigna para refrescar path
                 currentTargetTransform = closestTarget; currentTargetHealth = currentTargetTransform.GetComponent<HealthSystem>();
                 combat?.SetTarget(currentTargetHealth); lastPathRequestTime = -pathUpdateRate; isTargetReachable = true;
                 TransitionToState(AIState.Chasing); // Encontró/refrescó objetivo -> perseguir
             //} else { TransitionToDefaultState(); } // Si era el mismo, reevalúa
         } else {
             if (currentTargetTransform != null) { // Si tenía objetivo y lo perdió
                 currentTargetTransform = null; currentTargetHealth = null; combat?.SetTarget(null);
                 TransitionToState(AIState.Idle); // Sin objetivo -> Idle
             }
             // Si ya estaba sin objetivo, se queda en Searching/Idle
         }
     }

     // --- Predicción Ataque ---
     bool PredictEnemyAttack() {
         if (!IsTargetValid() || rb == null) return false;
         CharacterData targetData = currentTargetTransform.GetComponent<CharacterData>();
         float enemyAttackRange = targetData?.baseAttackRange ?? 1f;
         float enemyWeaponRangeMult = targetData?.equippedWeapon?.attackRangeMultiplier ?? 1f;
         float actualEnemyRange = enemyAttackRange * enemyWeaponRangeMult;
         float predictRange = actualEnemyRange * 1.2f; // Margen
         float distSqr = ((Vector2)currentTargetTransform.position - rb.position).sqrMagnitude;
         if (distSqr < predictRange * predictRange) {
             Vector2 dirToMe = (rb.position - (Vector2)currentTargetTransform.position).normalized;
             Vector2 enemyForward = currentTargetTransform.right * Mathf.Sign(currentTargetTransform.localScale.x);
             float dot = Vector2.Dot(enemyForward, dirToMe);
             // Mayor probabilidad si mira directamente y está cerca
             if (dot > 0.8f && Random.value < 0.25f) return true;
         }
         return false;
     }

    // --- Elegir Habilidad ---
     SkillData ChooseSkillToUse(bool isLowHealth) {
         if (characterData?.learnedSkills == null || characterData.learnedSkills.Count == 0) return null;
         List<SkillData> usableSkills = new List<SkillData>();
         foreach (SkillData skill in characterData.learnedSkills) {
             if (skill != null && characterData.IsSkillReady(skill)) usableSkills.Add(skill);
         }
         if (usableSkills.Count == 0) return null;
         if (isLowHealth) foreach (SkillData skill in usableSkills) if (skill.skillType == SkillType.Heal) return skill;
         List<SkillData> damageSkills = usableSkills.FindAll(s => s.skillType == SkillType.DirectDamage || s.skillType == SkillType.Projectile || (s.skillType == SkillType.AreaOfEffect && s.affectsEnemies));
         if (damageSkills.Count > 0) return damageSkills[Random.Range(0, damageSkills.Count)];
         return usableSkills[Random.Range(0, usableSkills.Count)];
    }

    // --- Pathfinding Callbacks ---
    void EnsureMovingTowardsTarget() { if (aiPath != null && IsTargetValid()) aiPath.destination = currentTargetTransform.position; else if(!IsTargetValid()) TransitionToState(AIState.Searching); }
    void UpdateAStarPath() { if (aiPath != null && aiPath.canSearch && seeker != null && IsTargetValid() && Time.time > lastPathRequestTime + pathUpdateRate) { RequestPathToTarget(); lastPathRequestTime = Time.time; } }
    void RequestPathToTarget() { if (seeker.IsDone() && IsTargetValid()) seeker.StartPath(rb.position, currentTargetTransform.position, OnPathComplete); }
    public void OnPathComplete(Path p) { if (!enabled) return; isTargetReachable = !p.error; if (p.error) { Debug.LogWarning($"[{gameObject.name}] Path error: {p.errorLog}"); if (currentState == AIState.Chasing) TransitionToState(AIState.Idle); } }

    // --- Movimiento Físico ---
    bool CanMovePhysically() { return enabled && rb != null && characterData != null && !characterData.isStunned && !characterData.isDashing && currentState != AIState.Celebrating; }
    void ApplyHorizontalMovement_SteeringTarget() {
        if (aiPath == null || !aiPath.hasPath || aiPath.reachedEndOfPath || rb == null || characterData == null) return;
        Vector2 currentPos = rb.position; Vector2 steeringTarget = (Vector2)aiPath.steeringTarget;
        float hDir = Mathf.Sign(steeringTarget.x - currentPos.x);
        if (Mathf.Abs(steeringTarget.x - currentPos.x) < aiPath.endReachedDistance * 0.5f) hDir = 0; // Menos sensible a quedarse quieto
        float targetSpeed = hDir * characterData.baseMovementSpeed * (currentState == AIState.Blocking ? characterData.baseBlockSpeedMultiplier : 1f);
        float newVelX = Mathf.MoveTowards(rb.linearVelocity.x, targetSpeed, horizontalAcceleration * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector2(newVelX, rb.linearVelocity.y);
    }
    void StopHorizontalMovement() { if (rb == null || !CanMovePhysically()) return; rb.linearVelocity = new Vector2(Mathf.MoveTowards(rb.linearVelocity.x, 0f, horizontalAcceleration * Time.fixedDeltaTime), rb.linearVelocity.y); }

     // --- Otros ---
     void UpdateStuckTimer() { if (currentState == AIState.Chasing && shouldAIMoveHorizontally && rb.linearVelocity.sqrMagnitude < MIN_MOVE_VELOCITY_SQR) timeStuck += Time.deltaTime; else timeStuck = 0f; }
     bool IsStuck() => timeStuck >= maxStuckTime;
     void HandleDeath() { if (!enabled) return; Debug.Log($"[{gameObject.name}] AI Death"); TransitionToState(AIState.Stunned); enabled = false; }
     public void StartCelebrating() { if (!enabled || health == null || !health.IsAlive() || currentState == AIState.Celebrating) return; Debug.Log($"[{gameObject.name}] Celebrate!"); TransitionToState(AIState.Celebrating); combat?.InterruptActions(); combat?.SetTarget(null); combat?.SetAnimatorTrigger("Celebrate"); }
     void OnDestroy() { if (health != null) health.OnDeath.RemoveListener(HandleDeath); StopAllCoroutines(); }
}