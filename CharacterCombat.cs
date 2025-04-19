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

    // Referencia al objetivo (solo HealthSystem)
    private HealthSystem currentTargetHealth;

    // Coroutine references para poder detenerlas
    private Coroutine dashCoroutine;
    private Coroutine parryWindowCoroutine;
    private List<Coroutine> activeBuffCoroutines = new List<Coroutine>(); // Para buffs

    void Awake()
    {
        characterData = GetComponent<CharacterData>();
        rb = GetComponent<Rigidbody2D>();
        healthSystem = GetComponent<HealthSystem>();
        aiPath = GetComponent<AIPath>(); // Puede ser null
        animator = GetComponent<Animator>(); // Obtener Animator, puede ser null
    }

    void Update()
    {
        // Drenar stamina si está bloqueando y no está stuneado
        if (characterData.isBlocking && !characterData.isStunned && characterData.baseStats != null)
        {
            // Consumir stamina por segundo
            bool stillHasStamina = characterData.ConsumeStamina(characterData.baseStats.blockStaminaDrain * Time.deltaTime);
            if (!stillHasStamina)
            {
                StopBlocking(); // Dejar de bloquear si se acaba la stamina
            }
        }
    }

    // --- Establecer Objetivo ---
    public void SetTarget(HealthSystem newTarget)
    {
        currentTargetHealth = newTarget;
    }
    public HealthSystem GetTarget()
    {
        return currentTargetHealth;
    }

    // --- Interrumpir Acciones ---
    // Llamado por CharacterData cuando se aplica Stun u otra interrupción forzada
    public void InterruptActions()
    {
        // Detener Dash si está activo
        if (dashCoroutine != null)
        {
            StopCoroutine(dashCoroutine);
            rb.linearVelocity = Vector2.zero;
            characterData.SetDashing(false);
            characterData.SetInvulnerable(false);
            dashCoroutine = null;
        }

        // Detener ventana de Parry si está activa
        if (parryWindowCoroutine != null)
        {
            StopCoroutine(parryWindowCoroutine);
            characterData.SetAttemptingParry(false);
            parryWindowCoroutine = null;
        }

        // Detener Bloqueo si estaba activo
        StopBlocking();

        // Detener Buffs (Opcional) - Podrías decidir no detenerlos
        // foreach (var coroutine in activeBuffCoroutines) { StopCoroutine(coroutine); }
        // activeBuffCoroutines.Clear();

        // Detener otras corutinas de acción (ej. casteo/delay de skills)
        StopCoroutine(nameof(ExecuteSkillCoroutine));
        StopCoroutine(nameof(ApplyAttackDamageAfterDelay)); // Detener daño pendiente si es stuneado

        // Resetear triggers del animator (si es necesario)
        if (animator != null) {
             // animator.ResetTrigger("Attack");
             // animator.ResetTrigger("Dash");
             // animator.ResetTrigger("Parry");
             // animator.SetBool("IsBlocking", false); // StopBlocking ya lo hace
        }
    }

    // --- ATAQUE BÁSICO ---
    public bool TryAttack()
    {
        if (characterData.isStunned || characterData.isDashing || characterData.isBlocking || characterData.baseStats == null) return false;
        if (!characterData.IsAttackReady()) return false;
        if (currentTargetHealth == null || !currentTargetHealth.IsAlive()) return false;
        if (!IsTargetInBasicAttackRange(currentTargetHealth.transform)) return false;

        characterData.PutAttackOnCooldown();
        Debug.Log($"{gameObject.name} ataca a {currentTargetHealth.gameObject.name}");

        // Comprobar Animator antes de usarlo
        if (animator != null)
        {
            animator.SetTrigger("Attack");
        } else {
            Debug.LogWarning($"Animator no encontrado en {gameObject.name}, no se puede activar trigger 'Attack'");
        }

        StartCoroutine(ApplyAttackDamageAfterDelay(currentTargetHealth, 0.2f)); // Ajustar delay
        if (aiPath != null) StartCoroutine(PauseMovementDuringAction(0.5f)); // Ajustar duración

        return true;
    }

    IEnumerator ApplyAttackDamageAfterDelay(HealthSystem target, float delay)
    {
        yield return new WaitForSeconds(delay);
        // Volver a comprobar todo antes de aplicar daño, por si algo cambió durante el delay
        if (!characterData.isStunned && target != null && target.IsAlive() && IsTargetInBasicAttackRange(target.transform) && characterData.baseStats != null)
        {
            target.TakeDamage(characterData.baseStats.attackDamage, this.gameObject);
        }
    }

    // --- DASH ---
    public bool TryDash(Vector2 direction)
    {
        if (characterData.isStunned || characterData.isDashing || characterData.isBlocking || characterData.baseStats == null) return false;
        if (!characterData.IsDashReady()) return false;
        if (!characterData.ConsumeStamina(characterData.baseStats.dashCost)) return false;

        characterData.PutDashOnCooldown();
        InterruptActions(); // Interrumpir otras acciones

        if (dashCoroutine != null) StopCoroutine(dashCoroutine); // Seguridad extra
        dashCoroutine = StartCoroutine(DashCoroutine(direction.normalized));

        // Comprobar Animator antes de usarlo
        if (animator != null)
        {
            animator.SetTrigger("Dash");
        } else {
             Debug.LogWarning($"Animator no encontrado en {gameObject.name}, no se puede activar trigger 'Dash'");
        }
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
            if (characterData.isStunned) // Salir si es stuneado
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

        // Fin del Dash
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
        while(Time.time < endTime && !characterData.isStunned) {
            yield return null;
        }
        if (!characterData.isStunned) { // Solo quitar si no fue por stun
             characterData.SetInvulnerable(false);
        }
    }


    // --- BLOQUEO ---
    public bool TryStartBlocking()
    {
        if (characterData.isStunned || characterData.isDashing || characterData.isBlocking || characterData.baseStats == null) return false;
        if (characterData.currentStamina >= 0)
        {
            characterData.SetBlocking(true);
            if (aiPath != null) aiPath.maxSpeed = characterData.baseStats.movementSpeed * characterData.baseStats.blockSpeedMultiplier;

            // Comprobar Animator antes de usarlo
            if (animator != null)
            {
                animator.SetBool("IsBlocking", true);
            } else {
                 Debug.LogWarning($"Animator no encontrado en {gameObject.name}, no se puede activar bool 'IsBlocking'");
            }
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
            if (aiPath != null) aiPath.maxSpeed = characterData.baseStats.movementSpeed; // Restaurar velocidad

            // Comprobar Animator antes de usarlo
            if (animator != null)
            {
                 animator.SetBool("IsBlocking", false);
            }
            Debug.Log($"{gameObject.name} deja de bloquear");
        }
    }

    // --- PARRY ---
    public bool TryParry()
    {
        if (characterData.isStunned || characterData.isDashing || characterData.isBlocking || characterData.baseStats == null) return false;
        if (!characterData.IsParryReady()) return false;
        if (!characterData.ConsumeStamina(characterData.baseStats.parryCost)) return false;

        characterData.PutParryOnCooldown();
        InterruptActions(); // Detener otras acciones

        if (parryWindowCoroutine != null) StopCoroutine(parryWindowCoroutine);
        parryWindowCoroutine = StartCoroutine(ParryWindowCoroutine());

        // Comprobar Animator antes de usarlo
        if (animator != null)
        {
            animator.SetTrigger("Parry");
        } else {
            Debug.LogWarning($"Animator no encontrado en {gameObject.name}, no se puede activar trigger 'Parry'");
        }
        Debug.Log($"{gameObject.name} intenta parry!");
        return true;
    }

    IEnumerator ParryWindowCoroutine()
    {
        if (characterData.baseStats == null) yield break;
        characterData.SetAttemptingParry(true);
        float endTime = Time.time + characterData.baseStats.parryWindow;
         while(Time.time < endTime && !characterData.isStunned) {
            yield return null;
        }

        // Usar una variable local para chequear si esta instancia específica sigue siendo la activa
        bool coroutineStillActive = ReferenceEquals(parryWindowCoroutine, this.GetComponent<Coroutine>());

        // Solo desactivar si la corutina terminó normalmente Y sigue siendo la corutina activa
        if (characterData.isAttemptingParry && !characterData.isStunned && coroutineStillActive)
        {
            characterData.SetAttemptingParry(false);
        }
        // Limpiar la referencia si esta instancia específica terminó
        if (coroutineStillActive) {
           parryWindowCoroutine = null;
        }
    }

    // Llamado por HealthSystem si un ataque golpea durante la ventana de parry
    public void NotifySuccessfulParry(GameObject attacker)
    {
        if (!characterData.isAttemptingParry || characterData.baseStats == null) return;

        Debug.Log($"{gameObject.name} realizó PARRY exitoso contra {attacker.name}!");

        // Detener la corutina de la ventana y el estado
        if (parryWindowCoroutine != null) StopCoroutine(parryWindowCoroutine);
        characterData.SetAttemptingParry(false);
        parryWindowCoroutine = null;

        CharacterData attackerData = attacker.GetComponent<CharacterData>();
        if (attackerData != null)
        {
            attackerData.ApplyStun(characterData.baseStats.parryStunDuration);
        }

        // Comprobar Animator antes de usarlo
        // if (animator != null) animator.SetTrigger("ParrySuccess");
    }


    // --- HABILIDADES ---
    public bool TryUseSkill(SkillData skill)
    {
        if (skill == null || characterData.baseStats == null) return false;
        if (characterData.isStunned || characterData.isDashing || characterData.isBlocking) return false;
        if (!characterData.IsSkillReady(skill)) return false;
        // if (!characterData.ConsumeMana(skill.manaCost)) return false;

        bool requiresTarget = skill.range > 0 || skill.skillType == SkillType.DirectDamage || skill.skillType == SkillType.Projectile; // Tipos que suelen necesitar target
        bool inRange = skill.range <= 0; // Auto-aplicación siempre está en rango

        if (requiresTarget && currentTargetHealth != null)
        {
            inRange = IsTargetInRange(currentTargetHealth.transform, skill.range);
        }
        else if (requiresTarget && currentTargetHealth == null) // Necesita target pero no hay
        {
             return false;
        }

        // Permitir lanzar proyectiles/AoE aunque el target esté fuera de rango (irán en la dirección/posición)
        bool canUseOutOfRange = skill.skillType == SkillType.Projectile || skill.skillType == SkillType.AreaOfEffect;

        if (!inRange && !canUseOutOfRange)
        {
             Debug.Log($"{gameObject.name} intentó usar {skill.skillName} pero objetivo fuera de rango ({skill.range}m)");
             return false;
        }

        characterData.PutSkillOnCooldown(skill);
        InterruptActions(); // Detener otras acciones

        Debug.Log($"{gameObject.name} usa habilidad: {skill.skillName}");
        string trigger = !string.IsNullOrEmpty(skill.animationTriggerName) ? skill.animationTriggerName : "UseSkill";

        // Comprobar Animator antes de usarlo
         if (animator != null)
         {
            animator.SetTrigger(trigger);
         } else {
            Debug.LogWarning($"Animator no encontrado en {gameObject.name}, no se puede activar trigger '{trigger}'");
         }

        StartCoroutine(ExecuteSkillCoroutine(skill));
        if (aiPath != null) StartCoroutine(PauseMovementDuringAction(0.8f)); // Ajustar según animación

        return true;
    }

    IEnumerator ExecuteSkillCoroutine(SkillData skill)
    {
        if (skill == null || characterData.baseStats == null) yield break;

        if (skill.castVFX != null) Instantiate(skill.castVFX, transform.position, transform.rotation);
        // Play castSFX

        // yield return new WaitForSeconds(skill.castTime); // Si hay tiempo de casteo

        if (characterData.isStunned) yield break; // Salir si es stuneado durante casteo/ejecución

        switch (skill.skillType)
        {
            case SkillType.DirectDamage:
                if (currentTargetHealth != null && IsTargetInRange(currentTargetHealth.transform, skill.range))
                {
                    Debug.Log($"Habilidad golpea a {currentTargetHealth.name} por {skill.damage} daño.");
                    currentTargetHealth.TakeDamage(skill.damage, this.gameObject);
                    if (skill.hitVFX != null) Instantiate(skill.hitVFX, currentTargetHealth.transform.position, Quaternion.identity);
                    // Play hitSFX
                }
                break;

            case SkillType.Projectile:
                 if (skill.projectilePrefab != null) {
                    Vector3 spawnPos = transform.position; // Ajustar si es necesario
                    Vector3 targetPos = currentTargetHealth?.transform.position ?? (spawnPos + transform.right * skill.range);
                    Vector2 direction = ((Vector2)targetPos - (Vector2)spawnPos).normalized;
                    if (direction == Vector2.zero) direction = transform.right; // Dirección por defecto si target está encima
                    spawnPos += (Vector3)direction * 0.5f; // Evitar colisión inmediata

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
                 // Play heal SFX
                 break;
        }
    }

     void ApplyAoEEffect(SkillData skill) {
         if (skill == null || characterData.baseStats == null) return;
         Vector2 centerPoint = transform.position;
         Collider2D[] hits = Physics2D.OverlapCircleAll(centerPoint, skill.aoeRadius);
         Debug.Log($"Skill AoE {skill.skillName} detectó {hits.Length} colliders en radio {skill.aoeRadius}.");

         if (skill.hitVFX != null) Instantiate(skill.hitVFX, centerPoint, Quaternion.identity);
         // Play AoE SFX

         string enemyTag = characterData.GetComponent<BrutoAIController>()?.enemyTag ?? "Enemy"; // Cachear tag

         foreach (Collider2D hit in hits) {
             GameObject targetGO = hit.gameObject;
             bool isSelf = (targetGO == this.gameObject);
             bool isEnemy = targetGO.CompareTag(enemyTag);

             // --- Filtrar ---
             if (isSelf && !skill.affectsSelf) continue;
             if (isEnemy && !skill.affectsEnemies) continue;
             // Si no es self ni enemigo, y no afecta aliados (o no se chequea), ignorar
             if (!isSelf && !isEnemy /*&& !skill.affectsAllies*/) continue;

             // --- Aplicar ---
             HealthSystem targetHealth = hit.GetComponent<HealthSystem>();
             if (targetHealth != null && targetHealth.IsAlive())
             {
                 if (skill.damage > 0 && (isEnemy || (isSelf && skill.affectsSelf))) {
                     targetHealth.TakeDamage(skill.damage, this.gameObject);
                     Debug.Log($"AoE golpea a {targetGO.name} por {skill.damage} daño.");
                 }
                 if (skill.healAmount > 0 && (isSelf || (isEnemy && skill.affectsEnemies))) { // Curar enemigos es raro
                      targetHealth.RestoreHealth(skill.healAmount);
                     Debug.Log($"AoE cura a {targetGO.name} por {skill.healAmount}.");
                 }
             }
             // Aplicar otros efectos (Stun, Debuff) al CharacterData si existe y no está stuneado
             CharacterData targetData = hit.GetComponent<CharacterData>();
              if(targetData != null && !targetData.isStunned) {
                 // if(skill.stunDuration > 0) { targetData.ApplyStun(skill.stunDuration); }
             }
         }
     }


    IEnumerator ApplyBuffCoroutine(SkillData buffSkill)
    {
        if (buffSkill == null || characterData.baseStats == null) yield break;
        float originalAiPathSpeed = aiPath != null ? aiPath.maxSpeed : 0;
        // Añadir variables para otros stats
        bool applied = false;

        try
        {
            switch (buffSkill.buffStat)
            {
                case StatToBuff.Speed:
                    if (aiPath != null) { aiPath.maxSpeed *= buffSkill.buffMultiplier; applied = true; }
                    break;
                // Casos para otros buffs
            }

            if (applied)
            {
                 float endTime = Time.time + buffSkill.duration;
                 while(Time.time < endTime && !characterData.isStunned) {
                    yield return null;
                 }
                 Debug.Log($"Buff {buffSkill.skillName} terminó (Duración o Stun).");
            }
        }
        finally
        {
             if (applied) {
                 switch (buffSkill.buffStat)
                 {
                    case StatToBuff.Speed:
                        if (aiPath != null) aiPath.maxSpeed = originalAiPathSpeed; // Revertir
                        break;
                    // Revertir otros buffs
                 }
                 Debug.Log($"Buff {buffSkill.skillName} revertido.");
             }
             // Eliminar de la lista de activos (mejor buscar por referencia si es posible)
             activeBuffCoroutines.RemoveAll(c => c == this.GetComponent<Coroutine>()); // Intento simple
        }
    }


    // --- HELPERS ---
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
            bool wasMoving = aiPath.canMove; // Guardar estado original
            aiPath.canMove = false;
             float endTime = Time.time + duration;
             while(Time.time < endTime && !characterData.isStunned) {
                yield return null;
             }

            // Solo reanudar si no está stuneado/haciendo otra acción Y si originalmente se estaba moviendo
            // (La IA decidirá si realmente debe moverse después)
            if (!characterData.isStunned && !characterData.isDashing && !characterData.isBlocking && !characterData.isAttemptingParry /*&& wasMoving*/)
            {
                 // aiPath.canMove = true; // Dejar que la IA decida si volver a moverlo
            }
        }
    }
}