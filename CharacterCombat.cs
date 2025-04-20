// File: CharacterCombat.cs
using UnityEngine;
using System.Collections;
using System.Collections.Generic; // Para AoE
using Pathfinding; // Para AIPath

[RequireComponent(typeof(CharacterData))]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(HealthSystem))] // Útil tener referencia
public class CharacterCombat : MonoBehaviour
{
    private CharacterData characterData;
    private Rigidbody2D rb;
    private HealthSystem healthSystem;
    private AIPath aiPath; // Asumiendo A* Pathfinding
    private Animator animator; // Para triggers de animación

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
        animator = GetComponent<Animator>();
    }

    void Update()
    {
        if (characterData.isBlocking && !characterData.isStunned && characterData.baseStats != null)
        {
            bool stillHasStamina = characterData.ConsumeStamina(characterData.baseStats.blockStaminaDrain * Time.deltaTime);
            if (!stillHasStamina)
            {
                StopBlocking();
            }
        }
    }

    public void SetTarget(HealthSystem newTarget)
    {
        currentTargetHealth = newTarget;
    }
    public HealthSystem GetTarget()
    {
        return currentTargetHealth;
    }

    public void InterruptActions()
    {
        if (dashCoroutine != null)
        {
            StopCoroutine(dashCoroutine);
            rb.linearVelocity = Vector2.zero;
            characterData.SetDashing(false);
            characterData.SetInvulnerable(false);
            dashCoroutine = null;
        }
        if (parryWindowCoroutine != null)
        {
            StopCoroutine(parryWindowCoroutine);
            characterData.SetAttemptingParry(false);
            parryWindowCoroutine = null;
        }
        StopBlocking();
        StopCoroutine(nameof(ExecuteSkillCoroutine));
        StopCoroutine(nameof(ApplyAttackDamageAfterDelay));
        if (animator != null) { }
    }

    public bool TryAttack()
    {
        if (characterData.isStunned || characterData.isDashing || characterData.isBlocking || characterData.baseStats == null) return false;
        if (!characterData.IsAttackReady()) return false;
        if (currentTargetHealth == null || !currentTargetHealth.IsAlive()) return false;
        if (!IsTargetInBasicAttackRange(currentTargetHealth.transform)) return false;

        characterData.PutAttackOnCooldown();
        Debug.Log($"{gameObject.name} ataca a {currentTargetHealth.gameObject.name}");
        if (animator != null) animator.SetTrigger("Attack");
        else Debug.LogWarning($"Animator no encontrado en {gameObject.name}, no se puede activar trigger 'Attack'");

        StartCoroutine(ApplyAttackDamageAfterDelay(currentTargetHealth, 0.2f));
        if (aiPath != null) StartCoroutine(PauseMovementDuringAction(0.5f));
        return true;
    }

    IEnumerator ApplyAttackDamageAfterDelay(HealthSystem target, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (!characterData.isStunned && target != null && target.IsAlive() && IsTargetInBasicAttackRange(target.transform) && characterData.baseStats != null)
        {
            target.TakeDamage(characterData.baseStats.attackDamage, this.gameObject);
        }
    }

    public bool TryDash(Vector2 direction)
    {
        if (characterData.isStunned || characterData.isDashing || characterData.isBlocking || characterData.baseStats == null) return false;
        if (!characterData.IsDashReady()) return false;
        if (!characterData.ConsumeStamina(characterData.baseStats.dashCost)) return false;

        characterData.PutDashOnCooldown();
        InterruptActions();
        if (dashCoroutine != null) StopCoroutine(dashCoroutine);
        dashCoroutine = StartCoroutine(DashCoroutine(direction.normalized));
        if (animator != null) animator.SetTrigger("Dash");
        else Debug.LogWarning($"Animator no encontrado en {gameObject.name}, no se puede activar trigger 'Dash'");
        return true;
    }

    IEnumerator DashCoroutine(Vector2 direction)
    {
        if (characterData.baseStats == null) yield break;
        characterData.SetDashing(true);
        StartCoroutine(InvulnerabilityWindow(characterData.baseStats.dashInvulnerabilityDuration));
        float startTime = Time.time;
        float moveSpeed = characterData.baseStats.movementSpeed;
        float dashSpeed = moveSpeed * characterData.baseStats.dashSpeedMult;
        if (aiPath != null) aiPath.canMove = false;
        float originalGravity = rb.gravityScale;
        rb.gravityScale = 0;

        while (Time.time < startTime + characterData.baseStats.dashDuration)
        {
            if (characterData.isStunned)
            {
                rb.linearVelocity = Vector2.zero;
                rb.gravityScale = originalGravity;
                characterData.SetDashing(false);
                dashCoroutine = null;
                yield break;
            }
            rb.linearVelocity = direction * dashSpeed;
            yield return null;
        }

        rb.linearVelocity = Vector2.zero;
        rb.gravityScale = originalGravity;
        characterData.SetDashing(false);
        if (aiPath != null && !characterData.isStunned) aiPath.canMove = true;
        dashCoroutine = null;
        Debug.Log($"{gameObject.name} terminó dash");
    }

    IEnumerator InvulnerabilityWindow(float duration)
    {
        if (characterData.baseStats == null) yield break;
        characterData.SetInvulnerable(true);
        float endTime = Time.time + duration;
        while(Time.time < endTime && !characterData.isStunned) { yield return null; }
        if (!characterData.isStunned) { characterData.SetInvulnerable(false); }
    }

    public bool TryStartBlocking()
    {
        if (characterData.isStunned || characterData.isDashing || characterData.isBlocking || characterData.baseStats == null) return false;
        if (characterData.currentStamina >= 0)
        {
            characterData.SetBlocking(true);
            if (aiPath != null) aiPath.maxSpeed = characterData.baseStats.movementSpeed * characterData.baseStats.blockSpeedMultiplier;
            if (animator != null) animator.SetBool("IsBlocking", true);
            else Debug.LogWarning($"Animator no encontrado en {gameObject.name}, no se puede activar bool 'IsBlocking'");
            Debug.Log($"{gameObject.name} empieza a bloquear");
            return true;
        }
        return false;
    }

    public void StopBlocking()
    {
        if (characterData.isBlocking && characterData.baseStats != null)
        {
            characterData.SetBlocking(false);
            if (aiPath != null) aiPath.maxSpeed = characterData.baseStats.movementSpeed;
            if (animator != null) animator.SetBool("IsBlocking", false);
            Debug.Log($"{gameObject.name} deja de bloquear");
        }
    }

    public bool TryParry()
    {
        if (characterData.isStunned || characterData.isDashing || characterData.isBlocking || characterData.baseStats == null) return false;
        if (!characterData.IsParryReady()) return false;
        if (!characterData.ConsumeStamina(characterData.baseStats.parryCost)) return false;

        characterData.PutParryOnCooldown();
        InterruptActions();
        if (parryWindowCoroutine != null) StopCoroutine(parryWindowCoroutine);
        parryWindowCoroutine = StartCoroutine(ParryWindowCoroutine());
        if (animator != null) animator.SetTrigger("Parry");
        else Debug.LogWarning($"Animator no encontrado en {gameObject.name}, no se puede activar trigger 'Parry'");
        Debug.Log($"{gameObject.name} intenta parry!");
        return true;
    }

    IEnumerator ParryWindowCoroutine()
    {
        if (characterData.baseStats == null) yield break;
        characterData.SetAttemptingParry(true);
        float endTime = Time.time + characterData.baseStats.parryWindow;
        while(Time.time < endTime && !characterData.isStunned) { yield return null; }
        bool coroutineStillActive = ReferenceEquals(parryWindowCoroutine, this.GetComponent<Coroutine>());
        if (characterData.isAttemptingParry && !characterData.isStunned && coroutineStillActive)
        {
            characterData.SetAttemptingParry(false);
        }
        if (coroutineStillActive) { parryWindowCoroutine = null; }
    }

    public void NotifySuccessfulParry(GameObject attacker)
    {
        if (!characterData.isAttemptingParry || characterData.baseStats == null) return;
        Debug.Log($"{gameObject.name} realizó PARRY exitoso contra {attacker.name}!");
        if (parryWindowCoroutine != null) StopCoroutine(parryWindowCoroutine);
        characterData.SetAttemptingParry(false);
        parryWindowCoroutine = null;
        CharacterData attackerData = attacker.GetComponent<CharacterData>();
        if (attackerData != null)
        {
            attackerData.ApplyStun(characterData.baseStats.parryStunDuration);
        }
    }

    public bool TryUseSkill(SkillData skill)
    {
        if (skill == null || characterData.baseStats == null) return false;
        if (characterData.isStunned || characterData.isDashing || characterData.isBlocking) return false;
        if (!characterData.IsSkillReady(skill)) return false;

        bool requiresTarget = skill.range > 0 || skill.skillType == SkillType.DirectDamage || skill.skillType == SkillType.Projectile;
        bool inRange = skill.range <= 0;

        if (requiresTarget && currentTargetHealth != null) { inRange = IsTargetInRange(currentTargetHealth.transform, skill.range); }
        else if (requiresTarget && currentTargetHealth == null) { return false; }

        bool canUseOutOfRange = skill.skillType == SkillType.Projectile || skill.skillType == SkillType.AreaOfEffect;
        if (!inRange && !canUseOutOfRange)
        {
             Debug.Log($"{gameObject.name} intentó usar {skill.skillName} pero objetivo fuera de rango ({skill.range}m)");
             return false;
        }

        characterData.PutSkillOnCooldown(skill);
        InterruptActions();
        Debug.Log($"{gameObject.name} usa habilidad: {skill.skillName}");
        string trigger = !string.IsNullOrEmpty(skill.animationTriggerName) ? skill.animationTriggerName : "UseSkill";
        if (animator != null) animator.SetTrigger(trigger);
        else Debug.LogWarning($"Animator no encontrado en {gameObject.name}, no se puede activar trigger '{trigger}'");

        StartCoroutine(ExecuteSkillCoroutine(skill));
        if (aiPath != null) StartCoroutine(PauseMovementDuringAction(0.8f));
        return true;
    }

    IEnumerator ExecuteSkillCoroutine(SkillData skill)
    {
        if (skill == null || characterData.baseStats == null) yield break;
        if (skill.castVFX != null) Instantiate(skill.castVFX, transform.position, transform.rotation);
        if (characterData.isStunned) yield break;

        switch (skill.skillType)
        {
            case SkillType.DirectDamage:
                if (currentTargetHealth != null && IsTargetInRange(currentTargetHealth.transform, skill.range))
                {
                    Debug.Log($"Habilidad golpea a {currentTargetHealth.name} por {skill.damage} daño.");
                    currentTargetHealth.TakeDamage(skill.damage, this.gameObject);
                    if (skill.hitVFX != null) Instantiate(skill.hitVFX, currentTargetHealth.transform.position, Quaternion.identity);
                }
                break;
            case SkillType.Projectile:
                 if (skill.projectilePrefab != null) {
                    Vector3 spawnPos = transform.position;
                    Vector3 targetPos = currentTargetHealth?.transform.position ?? (spawnPos + transform.right * skill.range);
                    Vector2 direction = ((Vector2)targetPos - (Vector2)spawnPos).normalized;
                    if (direction == Vector2.zero) direction = transform.right;
                    spawnPos += (Vector3)direction * 0.5f;
                    float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                    Quaternion rotation = Quaternion.AngleAxis(angle, Vector3.forward);
                    GameObject projGO = Instantiate(skill.projectilePrefab, spawnPos, rotation);
                    Projectile projectileScript = projGO.GetComponent<Projectile>();
                    if (projectileScript != null) {
                        projectileScript.Initialize(skill.damage, skill.projectileSpeed, skill.projectileLifetime, this.gameObject, skill.hitVFX);
                    } else {
                        Debug.LogWarning($"Prefab de proyectil {skill.projectilePrefab.name} no tiene script Projectile.");
                        Destroy(projGO, skill.projectileLifetime);
                    }
                }
                 break;
            case SkillType.SelfBuff:
                Coroutine buffCoroutine = StartCoroutine(ApplyBuffCoroutine(skill));
                activeBuffCoroutines.Add(buffCoroutine);
                Debug.Log($"Aplicando buff {skill.skillName} por {skill.duration}s");
                break;
            case SkillType.AreaOfEffect:
                ApplyAoEEffect(skill);
                break;
            case SkillType.Heal:
                 if(healthSystem != null) healthSystem.RestoreHealth(skill.healAmount);
                 Debug.Log($"{gameObject.name} se cura {skill.healAmount} puntos.");
                 if (skill.hitVFX != null) Instantiate(skill.hitVFX, transform.position, Quaternion.identity);
                 break;
        }
    }

     void ApplyAoEEffect(SkillData skill) {
         if (skill == null || characterData.baseStats == null) return;
         Vector2 centerPoint = transform.position;
         Collider2D[] hits = Physics2D.OverlapCircleAll(centerPoint, skill.aoeRadius);
         Debug.Log($"Skill AoE {skill.skillName} detectó {hits.Length} colliders en radio {skill.aoeRadius}.");

         if (skill.hitVFX != null) Instantiate(skill.hitVFX, centerPoint, Quaternion.identity);

         // --- CAMBIO: Tipo de componente a buscar ---
         string enemyTag = characterData.GetComponent<LuchadorAIController>()?.enemyTag ?? "Enemy";

         foreach (Collider2D hit in hits) {
             GameObject targetGO = hit.gameObject;
             bool isSelf = (targetGO == this.gameObject);
             bool isEnemy = targetGO.CompareTag(enemyTag);
             if (isSelf && !skill.affectsSelf) continue;
             if (isEnemy && !skill.affectsEnemies) continue;
             if (!isSelf && !isEnemy) continue;

             HealthSystem targetHealth = hit.GetComponent<HealthSystem>();
             if (targetHealth != null && targetHealth.IsAlive())
             {
                 if (skill.damage > 0 && (isEnemy || (isSelf && skill.affectsSelf))) {
                     targetHealth.TakeDamage(skill.damage, this.gameObject);
                     Debug.Log($"AoE golpea a {targetGO.name} por {skill.damage} daño.");
                 }
                 if (skill.healAmount > 0 && (isSelf || (isEnemy && skill.affectsEnemies))) {
                      targetHealth.RestoreHealth(skill.healAmount);
                     Debug.Log($"AoE cura a {targetGO.name} por {skill.healAmount}.");
                 }
             }
             CharacterData targetData = hit.GetComponent<CharacterData>();
              if(targetData != null && !targetData.isStunned) { }
         }
     }

    IEnumerator ApplyBuffCoroutine(SkillData buffSkill)
    {
        if (buffSkill == null || characterData.baseStats == null) yield break;
        float originalAiPathSpeed = aiPath != null ? aiPath.maxSpeed : 0;
        bool applied = false;
        try
        {
            switch (buffSkill.buffStat)
            {
                case StatToBuff.Speed:
                    if (aiPath != null) { aiPath.maxSpeed *= buffSkill.buffMultiplier; applied = true; }
                    break;
            }
            if (applied)
            {
                 float endTime = Time.time + buffSkill.duration;
                 while(Time.time < endTime && !characterData.isStunned) { yield return null; }
                 Debug.Log($"Buff {buffSkill.skillName} terminó (Duración o Stun).");
            }
        }
        finally
        {
             if (applied) {
                 switch (buffSkill.buffStat)
                 {
                    case StatToBuff.Speed:
                        if (aiPath != null) aiPath.maxSpeed = originalAiPathSpeed;
                        break;
                 }
                 Debug.Log($"Buff {buffSkill.skillName} revertido.");
             }
             activeBuffCoroutines.RemoveAll(c => c == this.GetComponent<Coroutine>());
        }
    }

    private bool IsTargetInBasicAttackRange(Transform targetTransform)
    {
        if (characterData.baseStats == null) return false;
        return IsTargetInRange(targetTransform, characterData.baseStats.attackRange);
    }

    public bool IsTargetInRange(Transform targetTransform, float range)
    {
        if (targetTransform == null) return false;
        return (targetTransform.position - transform.position).sqrMagnitude <= range * range;
    }

    IEnumerator PauseMovementDuringAction(float duration)
    {
        if (aiPath != null && aiPath.canMove)
        {
            bool wasMoving = aiPath.canMove;
            aiPath.canMove = false;
            float endTime = Time.time + duration;
            while(Time.time < endTime && !characterData.isStunned) { yield return null; }
            if (!characterData.isStunned && !characterData.isDashing && !characterData.isBlocking && !characterData.isAttemptingParry)
            {
                // La IA decidirá si moverse después
            }
        }
    }
}