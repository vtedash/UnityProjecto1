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
    [Header("Targeting & Team")]
    public string enemyTag = "Player";
    [Header("Pathfinding")]
    public float pathUpdateRate = 0.5f;
    private float lastPathRequestTime = -1f;
    private Seeker seeker;
    private AIPath aiPath;
    [Header("AI Decision Parameters")]
    public float decisionInterval = 0.2f;
    [Range(0f, 1f)] public float aggression = 0.7f;
    public float preferredCombatDistance = 0.8f;
    public float dashEngageRangeBonus = 2.0f;
    [Range(0f, 1f)] public float skillUseChance = 0.4f;
    [Range(0f, 1f)] public float lowHealthThreshold = 0.3f;
    [Range(0f, 1f)] public float parryPreference = 0.3f;
    [Range(0f, 1f)] public float dodgePreference = 0.5f;
    public float jumpThresholdY = 1.5f;
    [Tooltip("How quickly the AI accelerates/decelerates horizontally")]
    public float horizontalAcceleration = 15f;

    // Referencias
    private CharacterCombat combat;
    private HealthSystem health;
    private CharacterData characterData;
    private Rigidbody2D rb;
    private Animator animator; // Referencia al Animator sigue siendo útil
    private CharacterMovement characterMovement;

    // Estado Interno
    private Transform currentTargetTransform;
    private HealthSystem currentTargetHealth;
    private float lastDecisionTime;
    private bool isCelebrating = false;
    private bool shouldAIMove = false;

    void Awake()
    {
        seeker = GetComponent<Seeker>();
        aiPath = GetComponent<AIPath>();
        combat = GetComponent<CharacterCombat>();
        health = GetComponent<HealthSystem>();
        characterData = GetComponent<CharacterData>();
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>(); // Obtener Animator
        characterMovement = GetComponent<CharacterMovement>();

        if (aiPath != null) {
            aiPath.gravity = Vector3.zero;
            aiPath.updatePosition = false; // ¡¡VERIFICAR EN INSPECTOR!!
            aiPath.updateRotation = false; // ¡¡VERIFICAR EN INSPECTOR!!
            aiPath.enableRotation = false;
            aiPath.orientation = OrientationMode.YAxisForward;
            aiPath.canMove = false; // IA controla movimiento via Rigidbody
            aiPath.canSearch = true;
            aiPath.isStopped = true;
        } else Debug.LogError("AIPath component not found!", this);
        if (characterMovement == null) Debug.LogError("CharacterMovement component not found!", this);
        if (rb == null) Debug.LogError("Rigidbody2D component not found!", this);
        if (animator == null) Debug.LogWarning("Animator component not found!", this); // Advertencia en lugar de error
    }

    void Start()
    {
        if (characterData == null || health == null || combat == null || aiPath == null || characterMovement == null) { enabled = false; return; }
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
        if (!enabled || characterData == null || characterData.isStunned) {
            if (aiPath != null && !aiPath.isStopped) aiPath.isStopped = true;
            shouldAIMove = false; return;
        }
        if (isCelebrating) {
            if (aiPath != null && !aiPath.isStopped) aiPath.isStopped = true;
            shouldAIMove = false; return;
        }
        if (aiPath != null && aiPath.isStopped && shouldAIMove) { aiPath.isStopped = false; }
        if (Time.time >= lastDecisionTime + decisionInterval) { MakeDecision(); lastDecisionTime = Time.time; }
        if (shouldAIMove) { UpdateAStarPath(); }
        else if(aiPath != null && !aiPath.isStopped) { aiPath.isStopped = true; }
        UpdateAnimatorVisuals(); // Llamar a la actualización visual del Animator
    }

     void FixedUpdate()
     {
        if (rb == null || characterData == null || !this.enabled) return;
        if (characterData.isStunned || isCelebrating || characterData.isDashing) {
            if(isCelebrating && rb.linearVelocity.sqrMagnitude > 0.01f) { rb.linearVelocity = Vector2.Lerp(rb.linearVelocity, Vector2.zero, Time.fixedDeltaTime * 10f); }
            return;
        }

        float targetVelocityX = 0f;
        if (shouldAIMove && aiPath != null && aiPath.hasPath && !aiPath.reachedEndOfPath) {
            Vector2 directionToSteeringTarget = ((Vector2)aiPath.steeringTarget - rb.position);
            float horizontalDirection = Mathf.Sign(directionToSteeringTarget.x);
            if (Mathf.Abs(directionToSteeringTarget.x) < aiPath.endReachedDistance * 0.5f) { horizontalDirection = 0f; }
            targetVelocityX = horizontalDirection * characterData.baseStats.movementSpeed;
            // Reducción de velocidad cerca del destino
            if (aiPath.remainingDistance < aiPath.slowdownDistance && aiPath.remainingDistance > aiPath.endReachedDistance) {
                 targetVelocityX *= Mathf.InverseLerp(aiPath.endReachedDistance, aiPath.slowdownDistance, aiPath.remainingDistance);
            }
        }

        float newVelocityX = Mathf.Lerp(rb.linearVelocity.x, targetVelocityX, Time.fixedDeltaTime * horizontalAcceleration);
        rb.linearVelocity = new Vector2(newVelocityX, rb.linearVelocity.y);
        FlipSpriteBasedOnVelocity(rb.linearVelocity.x);
     }

    void MakeDecision()
    {
        if (isCelebrating || characterMovement == null || characterData == null || combat == null || !enabled) {
            shouldAIMove = false; return; }
        if (!IsTargetValid()) { FindTarget(); if (!IsTargetValid()) { EnterIdleState(); return; } }
        if (characterData.isDashing || characterData.isAttemptingParry) { shouldAIMove = false; return; }

        bool isGrounded = characterMovement.IsGrounded();
        float targetRelativeY = IsTargetValid() ? currentTargetTransform.position.y - transform.position.y : 0;
        float targetDistanceX = IsTargetValid() ? Mathf.Abs(currentTargetTransform.position.x - transform.position.x) : float.MaxValue;

        // Salto
        if (isGrounded && IsTargetValid() && targetRelativeY > jumpThresholdY && targetDistanceX < preferredCombatDistance * 1.5f)
        { if (characterMovement.Jump()) { shouldAIMove = false; return; } }

        // Defensa
        bool predictedAttack = PredictEnemyAttack();
        if (predictedAttack && !characterData.isBlocking) {
            float choice = Random.value;
            if (choice < parryPreference && combat.TryParry()) { shouldAIMove = false; return; }
            else if (choice < parryPreference + dodgePreference && CanAffordDash()) {
                Vector2 evadeDir = IsTargetValid() ? ((Vector2)transform.position - (Vector2)currentTargetTransform.position).normalized : -transform.right;
                 if(evadeDir == Vector2.zero) evadeDir = -transform.right;
                 if (combat.TryDash(evadeDir)) { shouldAIMove = false; return; }
            }
            else if (CanAffordBlock() && isGrounded) {
                if (combat.TryStartBlocking()) { shouldAIMove = false; return; }
            }
        } else if (!predictedAttack && characterData.isBlocking) { combat.StopBlocking(); }

        if (characterData.isBlocking) { shouldAIMove = false; return; }

        // Ofensiva / Movimiento
        if (!IsTargetValid()) { EnterIdleState(); return; }
        float distanceSqr = (currentTargetTransform.position - transform.position).sqrMagnitude;
        float attackRangeSqr = characterData.baseStats.attackRange * characterData.baseStats.attackRange;
        float preferredDistSqr = preferredCombatDistance * preferredCombatDistance;
        bool isTargetInRange_Basic = distanceSqr <= attackRangeSqr;
        bool isTargetInRange_Preferred = distanceSqr <= preferredDistSqr;

        // Skill
        SkillData chosenSkill = ChooseSkillToUse();
        if (chosenSkill != null && Random.value < skillUseChance) {
            bool skillRequiresTarget = chosenSkill.range > 0 || chosenSkill.skillType == SkillType.DirectDamage || chosenSkill.skillType == SkillType.Projectile;
            float skillRangeSqr = chosenSkill.range * chosenSkill.range; bool skillInRange = chosenSkill.range <= 0 || distanceSqr <= skillRangeSqr;
            if (skillInRange) { if (combat.TryUseSkill(chosenSkill)) { shouldAIMove = false; return; } }
            else if (skillRequiresTarget) { shouldAIMove = true; EnsureMovingTowardsTarget(); return; }
        }
        // Ataque
        if (isTargetInRange_Basic && characterData.IsAttackReady() && isGrounded) { if (combat.TryAttack()) { shouldAIMove = false; return; } }
        // Dash Engage
        float dashEngageRangeSqr = (preferredCombatDistance + dashEngageRangeBonus) * (preferredCombatDistance + dashEngageRangeBonus);
        bool wantsToDashEngage = distanceSqr > dashEngageRangeSqr;
        if (wantsToDashEngage && CanAffordDash() && Random.value < aggression && isGrounded) {
             Vector2 engageDir = (currentTargetTransform.position - transform.position).normalized; if(engageDir == Vector2.zero) engageDir = transform.right;
             if (combat.TryDash(engageDir)) { shouldAIMove = false; return; }
        }
        // Moverse si no está en rango
        else if (!isTargetInRange_Preferred) { shouldAIMove = true; EnsureMovingTowardsTarget(); }
        // Detenerse si está en rango
        else { shouldAIMove = false; EnterIdleState(); }
    }

    void EnsureMovingTowardsTarget() {
        if (isCelebrating || aiPath == null) return; if (!IsTargetValid()) { EnterIdleState(); return; }
        if (aiPath.isStopped) { aiPath.isStopped = false; } if (currentTargetTransform != null) { aiPath.destination = currentTargetTransform.position; } else { EnterIdleState(); }
    }
    void EnterIdleState() { if (this.enabled && !isCelebrating) { if (aiPath != null) aiPath.isStopped = true; shouldAIMove = false; combat?.StopBlocking(); } }
    bool IsTargetValid() { return currentTargetTransform != null && currentTargetHealth != null && currentTargetHealth.IsAlive(); }
    void FindTarget() {
        if (isCelebrating || rb == null) return; GameObject[] potentialTargets = GameObject.FindGameObjectsWithTag(enemyTag);
        Transform closestTarget = null; float minDistanceSqr = float.MaxValue; Vector2 currentPos = rb.position;
        foreach (GameObject pt in potentialTargets) { if (pt == gameObject) continue; HealthSystem ph = pt.GetComponent<HealthSystem>(); if (ph == null || !ph.IsAlive()) continue;
            float distSqr = ((Vector2)pt.transform.position - currentPos).sqrMagnitude; if (distSqr < minDistanceSqr) { minDistanceSqr = distSqr; closestTarget = pt.transform; } }
        Transform previousTarget = currentTargetTransform;
        if (closestTarget != null) { currentTargetTransform = closestTarget; currentTargetHealth = currentTargetTransform.GetComponent<HealthSystem>(); combat.SetTarget(currentTargetHealth);
            if (currentTargetTransform != previousTarget) { lastPathRequestTime = -pathUpdateRate; shouldAIMove = true; EnsureMovingTowardsTarget(); } else if (!shouldAIMove) { shouldAIMove = true; EnsureMovingTowardsTarget(); }
        } else { if(previousTarget != null) /*Debug.Log(...)*/; currentTargetTransform = null; currentTargetHealth = null; EnterIdleState(); }
    }
    bool PredictEnemyAttack() {
         if (!IsTargetValid() || characterData.baseStats == null) return false;
         float distSqr = ((Vector2)currentTargetTransform.position - rb.position).sqrMagnitude; float predictRangeSqr = characterData.baseStats.attackRange * 1.5f * characterData.baseStats.attackRange * 1.5f;
         if (distSqr < predictRangeSqr) { Vector2 dirToMe = (rb.position - (Vector2)currentTargetTransform.position).normalized; Vector2 enemyForward = currentTargetTransform.right;
             SpriteRenderer targetSr = currentTargetTransform.GetComponent<SpriteRenderer>(); if (targetSr != null && targetSr.flipX) { enemyForward = -enemyForward; }
             float dot = Vector2.Dot(enemyForward, dirToMe); if (dot > 0.7f && Random.value < 0.15f) { return true; } }
         return false;
    }
    SkillData ChooseSkillToUse() {
        bool isHealthy = true; if (characterData != null && characterData.baseStats != null && characterData.baseStats.maxHealth > 0) { isHealthy = (characterData.currentHealth / characterData.baseStats.maxHealth) > lowHealthThreshold; }
        SkillData bestSkill = null; if (characterData?.skills == null) return null;
        foreach (SkillData skill in characterData.skills) { if (skill != null && characterData.IsSkillReady(skill)) { if (skill.skillType == SkillType.Heal && !isHealthy) { return skill; }
                if (bestSkill == null && (skill.skillType == SkillType.DirectDamage || skill.skillType == SkillType.Projectile || skill.skillType == SkillType.AreaOfEffect)) { bestSkill = skill; } } }
        return bestSkill;
    }
    bool CanAffordDash() { return characterData != null && characterData.baseStats != null && characterData.currentStamina >= characterData.baseStats.dashCost; }
    bool CanAffordBlock() { return characterData != null && characterData.baseStats != null && characterData.currentStamina > (characterData.baseStats.blockStaminaDrain * 0.1f); }
    void UpdateAStarPath() { if (isCelebrating || aiPath == null || !aiPath.canSearch || aiPath.isStopped || !shouldAIMove) return; if (IsTargetValid() && Time.time > lastPathRequestTime + pathUpdateRate) { RequestPathToTarget(); lastPathRequestTime = Time.time; } }
    void RequestPathToTarget() { if (isCelebrating || seeker == null || !seeker.IsDone() || !IsTargetValid() || aiPath == null || !aiPath.canSearch || aiPath.isStopped) return; aiPath.destination = currentTargetTransform.position; seeker.StartPath(rb.position, aiPath.destination, OnPathComplete); }
    public void OnPathComplete(Path p) { if (!enabled || p == null) return; if (p.error) { Debug.LogWarning($"{gameObject.name} path error: {p.errorLog}"); if(!isCelebrating) EnterIdleState(); } }
    void HandleDeath() { if (!enabled) return; if(aiPath != null) aiPath.isStopped = true; shouldAIMove = false; isCelebrating = false; enabled = false; }
    public void StartCelebrating() { if (!this.enabled || health == null || !health.IsAlive() || isCelebrating) return; isCelebrating = true; if (aiPath != null) aiPath.isStopped = true; shouldAIMove = false; combat?.InterruptActions(); combat?.StopBlocking(); currentTargetTransform = null; currentTargetHealth = null; combat?.SetAnimatorTrigger("Celebrate"); } // Usa el helper de CharacterCombat
    void FlipSpriteBasedOnVelocity(float currentVelocityX) { if(animator != null) { SpriteRenderer sr = animator.GetComponent<SpriteRenderer>(); if (sr != null) { if (currentVelocityX > 0.1f) sr.flipX = false; else if (currentVelocityX < -0.1f) sr.flipX = true; } } }
    void UpdateAnimatorVisuals() { if (combat != null && characterMovement != null && rb != null) { combat.UpdateAnimatorLogic(characterMovement.IsGrounded(), rb.linearVelocity); } } // Llama a un método en CharacterCombat
    void OnDestroy() { if (health != null) { health.OnDeath.RemoveListener(HandleDeath); } StopAllCoroutines(); }
}