// File: CharacterCombat.cs
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pathfinding;

[RequireComponent(typeof(CharacterData))]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(HealthSystem))]
public class CharacterCombat : MonoBehaviour
{
    private CharacterData characterData;
    private Rigidbody2D rb;
    private HealthSystem healthSystem;
    private AIPath aiPath;
    private Animator animator; // Cacheado aquí

    private HealthSystem currentTargetHealth;
    private Coroutine dashCoroutine;
    private Coroutine parryWindowCoroutine;
    private List<Coroutine> activeBuffCoroutines = new List<Coroutine>();

    void Awake()
    {
        characterData = GetComponent<CharacterData>();
        rb = GetComponent<Rigidbody2D>();
        healthSystem = GetComponent<HealthSystem>();
        aiPath = GetComponent<AIPath>();
        animator = GetComponent<Animator>(); // Obtener el Animator aquí
        if(rb == null) Debug.LogError("Rigidbody2D no encontrado en CharacterCombat!", this);
        if(animator == null) Debug.LogWarning("Animator no encontrado en CharacterCombat!", this); // Advertencia si no hay animator
    }

    void Update()
    {
        if (characterData.isBlocking && !characterData.isStunned && characterData.baseStats != null)
        {
            bool stillHasStamina = characterData.ConsumeStamina(characterData.baseStats.blockStaminaDrain * Time.deltaTime);
            if (!stillHasStamina) { StopBlocking(); }
        }
    }

    public void SetTarget(HealthSystem newTarget){ currentTargetHealth = newTarget; }
    public HealthSystem GetTarget(){ return currentTargetHealth; }

    public void InterruptActions()
    {
        if (dashCoroutine != null) {
            StopCoroutine(dashCoroutine); characterData.SetDashing(false); characterData.SetInvulnerable(false); dashCoroutine = null;
             if(aiPath != null && !characterData.isStunned) aiPath.isStopped = false;
        }
        if (parryWindowCoroutine != null) {
            StopCoroutine(parryWindowCoroutine); characterData.SetAttemptingParry(false); parryWindowCoroutine = null;
        }
        StopBlocking(); StopCoroutine(nameof(ExecuteSkillCoroutine)); StopCoroutine(nameof(ApplyAttackDamageAfterDelay));
    }

    public bool TryAttack() {
         if (characterData.isStunned || characterData.isDashing || characterData.isBlocking || characterData.baseStats == null) return false;
        if (!characterData.IsAttackReady()) return false; if (currentTargetHealth == null || !currentTargetHealth.IsAlive()) return false;
        if (!IsTargetInBasicAttackRange(currentTargetHealth.transform)) return false;
        CharacterMovement move = GetComponent<CharacterMovement>(); if(move != null && !move.IsGrounded()) return false;
        characterData.PutAttackOnCooldown(); SetAnimatorTrigger("Attack"); StartCoroutine(ApplyAttackDamageAfterDelay(currentTargetHealth, 0.2f)); return true;
     }

    IEnumerator ApplyAttackDamageAfterDelay(HealthSystem target, float delay) {
         yield return new WaitForSeconds(delay);
         if (!characterData.isStunned && target != null && target.IsAlive() && IsTargetInBasicAttackRange(target.transform) && characterData.baseStats != null) {
             target.TakeDamage(characterData.baseStats.attackDamage, this.gameObject); }
     }

    public bool TryDash(Vector2 direction) {
        if (characterData.isStunned || characterData.isDashing || characterData.isBlocking || characterData.baseStats == null) return false;
        if (!characterData.IsDashReady()) return false; if (!characterData.ConsumeStamina(characterData.baseStats.dashCost)) return false;
        characterData.PutDashOnCooldown(); if (dashCoroutine != null) StopCoroutine(dashCoroutine);
        dashCoroutine = StartCoroutine(DashCoroutine(direction.normalized)); SetAnimatorTrigger("Dash"); return true;
    }

    IEnumerator DashCoroutine(Vector2 direction) {
        if (characterData.baseStats == null || rb == null) yield break; characterData.SetDashing(true);
        StartCoroutine(InvulnerabilityWindow(characterData.baseStats.dashInvulnerabilityDuration)); float startTime = Time.time;
        float dashSpeed = characterData.baseStats.movementSpeed * characterData.baseStats.dashSpeedMult; if (aiPath != null) aiPath.isStopped = true;
        float dashEndTime = startTime + characterData.baseStats.dashDuration;
        while (Time.time < dashEndTime) { if (characterData.isStunned) { characterData.SetDashing(false); dashCoroutine = null; if(aiPath != null && !characterData.isStunned) aiPath.isStopped = false; yield break; }
            rb.linearVelocity = new Vector2(direction.x * dashSpeed, rb.linearVelocity.y); yield return null; }
        characterData.SetDashing(false);
        if (Mathf.Abs(rb.linearVelocity.x) > characterData.baseStats.movementSpeed) { rb.linearVelocity = new Vector2(Mathf.Sign(rb.linearVelocity.x) * characterData.baseStats.movementSpeed, rb.linearVelocity.y); }
        if (aiPath != null && !characterData.isStunned) aiPath.isStopped = false; dashCoroutine = null;
    }

     IEnumerator InvulnerabilityWindow(float duration) { if (characterData.baseStats == null || duration <= 0) yield break; characterData.SetInvulnerable(true); float endTime = Time.time + duration; while(Time.time < endTime && !characterData.isStunned) { yield return null; } if (!characterData.isStunned) { characterData.SetInvulnerable(false); } }

     public bool TryStartBlocking() {
         if (characterData.isStunned || characterData.isDashing || characterData.isBlocking || characterData.baseStats == null) return false;
         CharacterMovement move = GetComponent<CharacterMovement>(); if(move != null && !move.IsGrounded()) return false;
         if (characterData.currentStamina >= 0) { characterData.SetBlocking(true); if (aiPath != null) aiPath.maxSpeed = characterData.baseStats.movementSpeed * characterData.baseStats.blockSpeedMultiplier; SetAnimatorBool("IsBlocking", true); return true; }
         return false;
      }

     public void StopBlocking() { if (characterData.isBlocking && characterData.baseStats != null) { characterData.SetBlocking(false); if (aiPath != null) aiPath.maxSpeed = characterData.baseStats.movementSpeed; SetAnimatorBool("IsBlocking", false); } }

     public bool TryParry() {
         if (characterData.isStunned || characterData.isDashing || characterData.isBlocking || characterData.baseStats == null) return false;
         if (!characterData.IsParryReady()) return false; if (!characterData.ConsumeStamina(characterData.baseStats.parryCost)) return false;
         characterData.PutParryOnCooldown(); // InterruptActions(); // Opcional
         if (parryWindowCoroutine != null) StopCoroutine(parryWindowCoroutine); parryWindowCoroutine = StartCoroutine(ParryWindowCoroutine()); SetAnimatorTrigger("Parry"); return true;
      }

    // Corregida
    IEnumerator ParryWindowCoroutine()
    {
        if (characterData.baseStats == null) yield break;
        characterData.SetAttemptingParry(true);
        float endTime = Time.time + characterData.baseStats.parryWindow;
        Coroutine thisInstance = parryWindowCoroutine; // Guardar referencia local
        while(Time.time < endTime && !characterData.isStunned) { yield return null; }
        if (ReferenceEquals(parryWindowCoroutine, thisInstance)) { // Comparar referencia global con la local
            characterData.SetAttemptingParry(false);
            parryWindowCoroutine = null;
        }
    }

     public void NotifySuccessfulParry(GameObject attacker) {
         if (!characterData.isAttemptingParry || characterData.baseStats == null) return;
         Debug.Log($"{gameObject.name} PARRY exitoso contra {attacker.name}!");
         if (parryWindowCoroutine != null) StopCoroutine(parryWindowCoroutine);
         characterData.SetAttemptingParry(false); parryWindowCoroutine = null;
         CharacterData attackerData = attacker.GetComponent<CharacterData>(); if (attackerData != null) { attackerData.ApplyStun(characterData.baseStats.parryStunDuration); }
         // SetAnimatorTrigger("ParrySuccess");
      }

     public bool TryUseSkill(SkillData skill) {
        if (skill == null || characterData.baseStats == null) return false; if (characterData.isStunned || characterData.isDashing || characterData.isBlocking) return false;
        if (!characterData.IsSkillReady(skill)) return false;
        bool requiresTarget = skill.range > 0 || skill.skillType == SkillType.DirectDamage || skill.skillType == SkillType.Projectile; bool inRange = skill.range <= 0;
        if (requiresTarget && currentTargetHealth != null) { inRange = IsTargetInRange(currentTargetHealth.transform, skill.range); } else if (requiresTarget && currentTargetHealth == null) { return false; }
        bool canUseOutOfRange = skill.skillType == SkillType.Projectile || skill.skillType == SkillType.AreaOfEffect; if (!inRange && !canUseOutOfRange) { return false; }
        characterData.PutSkillOnCooldown(skill); string trigger = !string.IsNullOrEmpty(skill.animationTriggerName) ? skill.animationTriggerName : "UseSkill";
        SetAnimatorTrigger(trigger); StartCoroutine(ExecuteSkillCoroutine(skill)); return true;
    }

    IEnumerator ExecuteSkillCoroutine(SkillData skill) {
         if (skill == null || characterData.baseStats == null) yield break; if (skill.castVFX != null) Instantiate(skill.castVFX, transform.position, transform.rotation);
         if (characterData.isStunned) yield break;
         switch (skill.skillType) {
             case SkillType.DirectDamage: if (currentTargetHealth != null && IsTargetInRange(currentTargetHealth.transform, skill.range)) { currentTargetHealth.TakeDamage(skill.damage, this.gameObject); if (skill.hitVFX != null) Instantiate(skill.hitVFX, currentTargetHealth.transform.position, Quaternion.identity); } break;
             case SkillType.Projectile: if (skill.projectilePrefab != null) { Vector3 spawnPos = transform.position; Vector3 targetPos = currentTargetHealth?.transform.position ?? (spawnPos + transform.right * skill.range); Vector2 direction = ((Vector2)targetPos - (Vector2)spawnPos).normalized; if (direction == Vector2.zero) direction = transform.right; spawnPos += (Vector3)direction * 0.5f; float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg; Quaternion rotation = Quaternion.AngleAxis(angle, Vector3.forward); GameObject projGO = Instantiate(skill.projectilePrefab, spawnPos, rotation); Projectile projectileScript = projGO.GetComponent<Projectile>(); if (projectileScript != null) { projectileScript.Initialize(skill.damage, skill.projectileSpeed, skill.projectileLifetime, this.gameObject, skill.hitVFX); } else { Debug.LogWarning($"Prefab {skill.projectilePrefab.name} sin script Projectile."); Destroy(projGO, skill.projectileLifetime); } } break;
             case SkillType.SelfBuff: Coroutine buffCoroutine = StartCoroutine(ApplyBuffCoroutine(skill)); activeBuffCoroutines.Add(buffCoroutine); break;
             case SkillType.AreaOfEffect: ApplyAoEEffect(skill); break;
             case SkillType.Heal: if(healthSystem != null) healthSystem.RestoreHealth(skill.healAmount); if (skill.hitVFX != null) Instantiate(skill.hitVFX, transform.position, Quaternion.identity); break;
         } yield break;
      }

     void ApplyAoEEffect(SkillData skill) {
         if (skill == null || characterData.baseStats == null) return; Vector2 centerPoint = transform.position; Collider2D[] hits = Physics2D.OverlapCircleAll(centerPoint, skill.aoeRadius);
         if (skill.hitVFX != null) Instantiate(skill.hitVFX, centerPoint, Quaternion.identity); string enemyTag = characterData.GetComponent<LuchadorAIController>()?.enemyTag ?? "Enemy";
         foreach (Collider2D hit in hits) { GameObject targetGO = hit.gameObject; bool isSelf = (targetGO == this.gameObject); bool isEnemy = targetGO.CompareTag(enemyTag); if ((isSelf && !skill.affectsSelf) || (isEnemy && !skill.affectsEnemies) || (!isSelf && !isEnemy)) continue; HealthSystem th = hit.GetComponent<HealthSystem>();
             if (th != null && th.IsAlive()) { if (skill.damage > 0 && (isEnemy || (isSelf && skill.affectsSelf))) { th.TakeDamage(skill.damage, this.gameObject); } if (skill.healAmount > 0 && (isSelf || (isEnemy && skill.affectsEnemies))) { th.RestoreHealth(skill.healAmount); } }
             CharacterData td = hit.GetComponent<CharacterData>(); if(td != null && !td.isStunned) { /*...*/ } }
      }

     IEnumerator ApplyBuffCoroutine(SkillData buffSkill) {
        if (buffSkill == null || characterData.baseStats == null) yield break; float originalAiPathSpeed = aiPath != null ? aiPath.maxSpeed : 0; bool applied = false;
        try { switch (buffSkill.buffStat) { case StatToBuff.Speed: if (aiPath != null) { aiPath.maxSpeed *= buffSkill.buffMultiplier; applied = true; } break; } if (applied) { float et = Time.time + buffSkill.duration; while(Time.time < et && !characterData.isStunned) { yield return null; } }
        } finally { if (applied) { switch (buffSkill.buffStat) { case StatToBuff.Speed: if (aiPath != null) aiPath.maxSpeed = originalAiPathSpeed; break; } }
            // Gestión robusta de lista de buffs es necesaria para casos más complejos
            // activeBuffCoroutines.Remove(...)
        }
         yield break; // Asegurarse de que termine
    }


    // --- Helpers ---
    private bool IsTargetInBasicAttackRange(Transform targetTransform) { if (characterData.baseStats == null) return false; return IsTargetInRange(targetTransform, characterData.baseStats.attackRange); }
    public bool IsTargetInRange(Transform targetTransform, float range) { if (targetTransform == null) return false; return (targetTransform.position - transform.position).sqrMagnitude <= range * range; }

    // --- Helpers para Animator (Movidos aquí) ---
    public void UpdateAnimatorLogic(bool isGrounded, Vector2 velocity) {
        if (animator != null) {
            bool isMovingHorizontally = Mathf.Abs(velocity.x) > 0.15f;
            SetAnimatorBool("IsMoving", isMovingHorizontally && isGrounded);
            SetAnimatorBool("IsGrounded", isGrounded);
            SetAnimatorFloat("VerticalVelocity", velocity.y);
        }
    }
    public void SetAnimatorBool(string name, bool value) { if (HasParameter(name, animator)) animator.SetBool(name, value); }
    public void SetAnimatorFloat(string name, float value) { if (HasParameter(name, animator)) animator.SetFloat(name, value); }
    public void SetAnimatorTrigger(string name) { if (HasParameter(name, animator)) animator.SetTrigger(name); }
    private bool HasParameter(string paramName, Animator animatorToCheck) { if (animatorToCheck == null || animatorToCheck.runtimeAnimatorController == null) return false; foreach (AnimatorControllerParameter param in animatorToCheck.parameters) { if (param.name == paramName) return true; } return false; }
}