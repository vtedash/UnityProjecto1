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

public class BrutoAIController : MonoBehaviour
{
    [Header("Targeting & Team")]
    [Tooltip("Tag del equipo enemigo a buscar")]
    public string enemyTag = "Player";

    [Header("Pathfinding")]
    [Tooltip("Con qué frecuencia (segundos) la IA recalcula su camino hacia el objetivo")]
    public float pathUpdateRate = 0.5f;
    private float lastPathRequestTime = -1f;
    private Seeker seeker;
    private AIPath aiPath;

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

    // --- Inicialización ---
    void Awake()
    {
        seeker = GetComponent<Seeker>();
        aiPath = GetComponent<AIPath>();
        combat = GetComponent<CharacterCombat>();
        health = GetComponent<HealthSystem>();
        characterData = GetComponent<CharacterData>();
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();

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
        if (characterData == null || health == null || combat == null || aiPath == null) {
            Debug.LogError($"Faltan componentes críticos en {gameObject.name}, desactivando IA.");
            enabled = false;
            return;
        }

        if (health != null) {
            health.OnDeath.AddListener(HandleDeath);
            Debug.Log($"{gameObject.name} subscribed HandleDeath to OnDeath.");
        } else {
             Debug.LogError($"HealthSystem is null on {gameObject.name} in Start!");
        }

        if (characterData.baseStats != null)
        {
            aiPath.maxSpeed = characterData.baseStats.movementSpeed;
            aiPath.endReachedDistance = preferredCombatDistance * 0.9f;
            aiPath.slowdownDistance = preferredCombatDistance;
            aiPath.canMove = true;
            aiPath.canSearch = true;
        } else {
            Debug.LogWarning($"BaseStats no asignados en {gameObject.name} en Start. AIPath usará valores por defecto.");
        }

        lastDecisionTime = Time.time;
        FindTarget();
    }

    // --- Bucle Principal de IA ---
    void Update()
    {
        if (!enabled || characterData.isStunned)
        {
            if (characterData.isStunned && aiPath != null && aiPath.canMove) {
                 aiPath.canMove = false;
            }
            return;
        }

        if (isCelebrating)
        {
            if (aiPath != null && aiPath.canMove) aiPath.canMove = false;
            if (rb != null && rb.linearVelocity != Vector2.zero) rb.linearVelocity = Vector2.zero;
            return;
        }

        CheckIfGrounded();

        if (Time.time >= lastDecisionTime + decisionInterval)
        {
            MakeDecision();
            lastDecisionTime = Time.time;
        }

        UpdateAStarPath();
    }

    // --- Lógica de Decisión Principal ---
    void MakeDecision()
    {
        if (isCelebrating) return;

        if (!IsTargetValid())
        {
            FindTarget();
            if (!IsTargetValid())
            {
                EnterIdleState();
                return;
            }
        }

        if (characterData.isDashing || characterData.isAttemptingParry) return;

        bool predictedAttack = PredictEnemyAttack();
        if (predictedAttack && !characterData.isBlocking)
        {
            float choice = Random.value;
            if (choice < parryPreference && combat.TryParry())
            {
                Debug.Log($"AI ({gameObject.name}): Intentando Parry!");
                return;
            }
            else if (choice < parryPreference + dodgePreference && CanAffordDash())
             {
                 Vector2 evadeDir = (transform.position - currentTargetTransform.position).normalized;
                 if(evadeDir == Vector2.zero) evadeDir = -transform.right;
                 if (combat.TryDash(evadeDir)) {
                     Debug.Log($"AI ({gameObject.name}): Intentando Dash Evasivo!");
                     return;
                 }
             }
            else if (characterData.currentStamina > 0)
            {
                 if (combat.TryStartBlocking()) {
                     Debug.Log($"AI ({gameObject.name}): Empezando a Bloquear por amenaza!");
                     return;
                 }
            }
        }
        else if (!predictedAttack && characterData.isBlocking) {
             combat.StopBlocking();
        }

        if (characterData.isBlocking) return;

        bool isHealthy = true;
         if (characterData != null && characterData.baseStats != null && characterData.baseStats.maxHealth > 0) {
             isHealthy = (characterData.currentHealth / characterData.baseStats.maxHealth) > lowHealthThreshold;
         }

        float distanceSqr = (currentTargetTransform.position - transform.position).sqrMagnitude;
        float attackRangeSqr = characterData.baseStats.attackRange * characterData.baseStats.attackRange;
        float preferredDistSqr = preferredCombatDistance * preferredCombatDistance;

        bool isTargetInRange_Basic = distanceSqr <= attackRangeSqr;
        bool isTargetInRange_Preferred = distanceSqr <= preferredDistSqr;

        SkillData chosenSkill = ChooseSkillToUse();
        if (chosenSkill != null && Random.value < skillUseChance)
        {
            bool skillRequiresTarget = chosenSkill.range > 0 || chosenSkill.skillType == SkillType.DirectDamage || chosenSkill.skillType == SkillType.Projectile;
            float skillRangeSqr = chosenSkill.range * chosenSkill.range;
            bool skillInRange = chosenSkill.range <= 0 || distanceSqr <= skillRangeSqr;

            if (skillInRange) {
                 if (combat.TryUseSkill(chosenSkill)) {
                     Debug.Log($"AI ({gameObject.name}): Usando Skill {chosenSkill.skillName}!");
                     return;
                 }
            } else if (skillRequiresTarget) {
                 Debug.Log($"AI ({gameObject.name}): Moviéndose para usar Skill {chosenSkill.skillName}");
                 EnsureMovingTowardsTarget();
                 return;
            }
        }

        if (isTargetInRange_Basic && characterData.IsAttackReady())
        {
            if (combat.TryAttack())
            {
                 return;
            }
        }

        float dashEngageRangeSqr = (preferredCombatDistance + dashEngageRangeBonus) * (preferredCombatDistance + dashEngageRangeBonus);
        bool wantsToDashEngage = distanceSqr > dashEngageRangeSqr;

        if (wantsToDashEngage && CanAffordDash() && Random.value < aggression) {
             Vector2 engageDir = (currentTargetTransform.position - transform.position).normalized;
              if(engageDir == Vector2.zero) engageDir = transform.right;
             if (combat.TryDash(engageDir)) {
                 Debug.Log($"AI ({gameObject.name}): Usando Dash para Acercarse!");
                 return;
             }
        }
        else if (!isTargetInRange_Preferred)
        {
             EnsureMovingTowardsTarget();
        }
        else if (aiPath != null && aiPath.canMove)
        {
             Debug.Log($"AI ({gameObject.name}): En rango preferido, deteniendo movimiento.");
             aiPath.canMove = false;
             aiPath.destination = transform.position;
             if(rb != null) rb.linearVelocity = Vector2.zero;
        }
    }

    // --- Funciones Auxiliares de IA ---

    void EnsureMovingTowardsTarget() {
        if (isCelebrating) {
            if (aiPath != null) aiPath.canMove = false;
            return;
        }

        if(aiPath != null && !aiPath.canMove) {
            Debug.Log($"AI ({gameObject.name}): Reanudando movimiento hacia el objetivo.");
            aiPath.canMove = true;
        }
        RequestPathToTarget();
    }

    void EnterIdleState() {
        if (this.enabled && !isCelebrating) {
             Debug.Log($"AI ({gameObject.name}): Entrando en Estado Idle.");
             if (aiPath != null) aiPath.canMove = false;
             combat?.StopBlocking();
        }
    }

    bool IsTargetValid()
    {
        return currentTargetTransform != null && currentTargetHealth != null && currentTargetHealth.IsAlive();
    }

    void FindTarget()
    {
        if (isCelebrating) return;

        GameObject[] potentialTargets = GameObject.FindGameObjectsWithTag(enemyTag);
        Transform closestTarget = null;
        float minDistanceSqr = float.MaxValue;

        foreach (GameObject potentialTarget in potentialTargets)
        {
            if (potentialTarget == gameObject) continue;
            HealthSystem potentialHealth = potentialTarget.GetComponent<HealthSystem>();
            if (potentialHealth == null || !potentialHealth.IsAlive()) continue;

            float distanceSqr = (potentialTarget.transform.position - transform.position).sqrMagnitude;
            if (distanceSqr < minDistanceSqr)
            {
                minDistanceSqr = distanceSqr;
                closestTarget = potentialTarget.transform;
            }
        }

        Transform previousTarget = currentTargetTransform;
        if (closestTarget != null)
        {
            currentTargetTransform = closestTarget;
            currentTargetHealth = currentTargetTransform.GetComponent<HealthSystem>();
            combat.SetTarget(currentTargetHealth);
            if (currentTargetTransform != previousTarget) {
                 Debug.Log($"{gameObject.name} encontró/cambió objetivo: {currentTargetTransform.name}");
                 lastPathRequestTime = -pathUpdateRate;
                 EnsureMovingTowardsTarget();
            }
        }
        else
        {
              if(previousTarget != null) Debug.Log($"{gameObject.name} perdió su objetivo o no encontró nuevos.");
              EnterIdleState();
        }
    }

    bool PredictEnemyAttack() {
         if (!IsTargetValid() || animator == null || characterData.baseStats == null) return false;
         float distSqr = (currentTargetTransform.position - transform.position).sqrMagnitude;
         float predictRangeSqr = characterData.baseStats.attackRange * 1.5f * characterData.baseStats.attackRange * 1.5f;

         if (distSqr < predictRangeSqr) {
             Vector2 dirToMe = (transform.position - currentTargetTransform.position).normalized;
             float dot = Vector2.Dot(currentTargetTransform.right, dirToMe);
             if (dot > 0.7f && Random.value < 0.10f) {
                  return true;
             }
         }
         return false;
    }

    SkillData ChooseSkillToUse() {
         bool isCurrentlyHealthy = true;
         if (characterData != null && characterData.baseStats != null && characterData.baseStats.maxHealth > 0) {
             isCurrentlyHealthy = (characterData.currentHealth / characterData.baseStats.maxHealth) > lowHealthThreshold;
         }

         SkillData bestSkill = null;
         foreach (SkillData currentSkill in characterData.skills) {
             if (characterData.IsSkillReady(currentSkill)) {
                 if (currentSkill.skillType == SkillType.Heal && !isCurrentlyHealthy) {
                     Debug.Log($"AI ({gameObject.name}): Prioritizing Heal skill.");
                     return currentSkill;
                 }
                 if (bestSkill == null && (currentSkill.skillType == SkillType.DirectDamage || currentSkill.skillType == SkillType.Projectile || currentSkill.skillType == SkillType.AreaOfEffect)) {
                     bestSkill = currentSkill;
                 }
             }
         }
         return bestSkill;
    }

     bool CanAffordDash() {
        return characterData.baseStats != null && characterData.currentStamina >= characterData.baseStats.dashCost;
    }


    // --- Pathfinding A* ---
    void UpdateAStarPath() {
        if (isCelebrating) return;

        if (aiPath != null && aiPath.canMove && IsTargetValid() && Time.time > lastPathRequestTime + pathUpdateRate)
        {
            RequestPathToTarget();
            lastPathRequestTime = Time.time;
        }
    }

    void RequestPathToTarget()
    {
        if (isCelebrating) return;

        if (seeker != null && seeker.IsDone() && currentTargetTransform != null && aiPath != null && aiPath.canSearch)
        {
            seeker.StartPath(transform.position, currentTargetTransform.position, OnPathComplete);
        }
    }

    public void OnPathComplete(Path p)
    {
        if (p.error)
        {
            Debug.LogWarning($"{gameObject.name} no pudo calcular camino: {p.errorLog}");
            if(!isCelebrating) EnterIdleState();
        }
    }

    // --- Manejo de Estados Propios ---
    void HandleDeath()
    {
        Debug.Log($"HandleDeath called by OnDeath event for {gameObject.name}. Disabling AI.");
        if(aiPath != null) aiPath.canMove = false;
        isCelebrating = false;
        enabled = false;
    }

     public void StartCelebrating()
     {
          if (!this.enabled || !health.IsAlive() || isCelebrating) return;

           Debug.Log($"AI ({gameObject.name}): ¡CELEBRANDO!");
           isCelebrating = true; // Establecer el flag

           // Detener acciones de combate y movimiento
           if (aiPath != null) aiPath.canMove = false;
           if (rb != null) rb.linearVelocity = Vector2.zero;
           combat?.InterruptActions();
           combat?.StopBlocking();
           currentTargetTransform = null;
           currentTargetHealth = null;

           // *** Usar ?. para llamada segura ***
           animator?.SetTrigger("Celebrate");
     }


    // --- Otros ---
    void CheckIfGrounded()
    {
        if (groundCheckPoint == null) return;
        isGrounded = Physics2D.OverlapCircle(groundCheckPoint.position, groundCheckRadius, groundLayer);
    }

    void OnDestroy()
    {
        if (health != null)
        {
             health.OnDeath.RemoveListener(HandleDeath);
        }
        StopAllCoroutines();
    }
}