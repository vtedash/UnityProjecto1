using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pathfinding; // Necesario para referencia AIPath

[RequireComponent(typeof(CharacterData))]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(HealthSystem))]
[RequireComponent(typeof(Animator))]
public class CharacterCombat : MonoBehaviour
{
    // Component References
    private CharacterData characterData;
    private Rigidbody2D rb;
    private HealthSystem healthSystem;
    private AIPath aiPath;
    private Animator animator;

    // State
    private HealthSystem currentTargetHealth;
    private Coroutine dashCoroutine;
    private Coroutine parryWindowCoroutine;
    private List<Coroutine> activeSkillCoroutines = new List<Coroutine>();

    void Awake()
    {
        characterData = GetComponent<CharacterData>();
        rb = GetComponent<Rigidbody2D>();
        healthSystem = GetComponent<HealthSystem>();
        aiPath = GetComponent<AIPath>();
        animator = GetComponent<Animator>();

        if (characterData == null) Debug.LogError("CharacterData not found!", this);
        if (rb == null) Debug.LogError("Rigidbody2D not found!", this);
        if (healthSystem == null) Debug.LogError("HealthSystem not found!", this);
        if (animator == null) Debug.LogError("Animator not found!", this);
    }

    void Update()
    {
        // Stamina drain while blocking (usa stat base de CharacterData)
        if (characterData != null && characterData.isBlocking && !characterData.isStunned)
        {
            bool stillHasStamina = characterData.ConsumeStamina(characterData.baseBlockStaminaDrain * Time.deltaTime);
            if (!stillHasStamina) StopBlocking();
        }
    }

    // --- Target Management ---
    public void SetTarget(HealthSystem newTarget) { currentTargetHealth = newTarget; }
    public HealthSystem GetTarget() { return currentTargetHealth; }

    // --- Interrupción ---
    public void InterruptActions()
    {
         if (dashCoroutine != null) { StopCoroutine(dashCoroutine); dashCoroutine = null; }
         characterData?.SetDashing(false); // Usa los setters de CharacterData
         characterData?.SetInvulnerable(false);

         if (parryWindowCoroutine != null) { StopCoroutine(parryWindowCoroutine); parryWindowCoroutine = null; }
         characterData?.SetAttemptingParry(false);

         StopBlocking(); // Asegura que el bloqueo se detenga

         foreach (Coroutine skillCoroutine in activeSkillCoroutines) { if (skillCoroutine != null) StopCoroutine(skillCoroutine); }
         activeSkillCoroutines.Clear();

         StopCoroutine(nameof(ApplyAttackDamageAfterDelay));
         StopCoroutine(nameof(ExecuteSkillCoroutine));
         StopCoroutine(nameof(ApplyBuffCoroutine));

         // Restaura estado de AIPath si no está aturdido (la IA decidirá si moverse)
         if (aiPath != null && characterData != null && !characterData.isStunned) {
              // aiPath.isStopped = false; // No forzar, dejar que AI decida
              aiPath.maxSpeed = characterData.baseMovementSpeed; // Restaura velocidad por si estaba bloqueando/bufado
         }
         // Debug.Log($"[{gameObject.name}] Actions interrupted.");
    }

    // --- Ataque Básico ---
    public bool TryAttack()
    {
        if (characterData == null || characterData.isStunned || characterData.isDashing || characterData.isBlocking) return false;
        if (!characterData.IsAttackReady()) return false;
        if (currentTargetHealth == null || !currentTargetHealth.IsAlive()) return false;

        // Calcular stats de ataque REALES (con arma)
        float actualDamage = characterData.baseAttackDamage * (characterData.equippedWeapon?.attackDamageMultiplier ?? 1.0f);
        float actualRange = characterData.baseAttackRange * (characterData.equippedWeapon?.attackRangeMultiplier ?? 1.0f);
        float actualCooldown = characterData.baseAttackCooldown * (characterData.equippedWeapon?.attackCooldownMultiplier ?? 1.0f);

        if (!IsTargetInRange(currentTargetHealth.transform, actualRange)) return false;

        CharacterMovement move = GetComponent<CharacterMovement>();
        if (move != null && !move.IsGrounded()) return false;

        // Ejecutar
        characterData.attackAvailableTime = Time.time + actualCooldown; // Establece cooldown
        SetAnimatorTrigger("Attack"); // TODO: Usar override del arma si existe
        StartCoroutine(ApplyAttackDamageAfterDelay(currentTargetHealth, actualDamage, 0.2f)); // Pasa daño real

         // Debug.Log($"[{gameObject.name}] Attacking {currentTargetHealth.name} | Damage: {actualDamage}, Range: {actualRange}, Cooldown: {actualCooldown}");

        return true;
    }

    IEnumerator ApplyAttackDamageAfterDelay(HealthSystem target, float damageToApply, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (characterData == null || characterData.isStunned) yield break; // Sal si aturdido durante delay

        float currentRange = characterData.baseAttackRange * (characterData.equippedWeapon?.attackRangeMultiplier ?? 1.0f);
        if (target != null && target.IsAlive() && IsTargetInRange(target.transform, currentRange))
        {
            target.TakeDamage(damageToApply, this.gameObject);
            // TODO: Instanciar VFX de golpe del arma
        }
    }

    // --- Dash ---
    public bool TryDash(Vector2 direction)
    {
        if (characterData == null || characterData.isStunned || characterData.isDashing || characterData.isBlocking) return false;
        if (!characterData.IsDashReady()) return false;
        if (!characterData.ConsumeStamina(characterData.baseDashCost)) return false; // Usa coste base

        InterruptActions(); // Interrumpe otras acciones antes de hacer dash (opcional pero a menudo bueno)
        if (dashCoroutine != null) StopCoroutine(dashCoroutine); // Seguridad extra

        characterData.PutDashOnCooldown(); // Usa cooldown base
        dashCoroutine = StartCoroutine(DashCoroutine(direction.normalized));
        SetAnimatorTrigger("Dash");
        return true;
    }

    IEnumerator DashCoroutine(Vector2 direction)
    {
         if (characterData == null || rb == null) yield break;
         float dashStartTime = Time.time;

         characterData.SetDashing(true);
         StartCoroutine(InvulnerabilityWindow(characterData.baseDashInvulnerabilityDuration)); // Usa duración inv. base

         float dashSpeed = characterData.baseMovementSpeed * characterData.baseDashSpeedMult; // Usa velocidad y multi base
         float dashEndTime = Time.time + characterData.baseDashDuration; // Usa duración base

         if (aiPath != null) aiPath.isStopped = true; // Detiene A* temporalmente

         while (Time.time < dashEndTime)
         {
             if (characterData.isStunned) // Interrumpido por stun
             {
                  // La limpieza (flags, coroutine) se hace en InterruptActions llamada por ApplyStun
                  yield break;
             }
             rb.linearVelocity = new Vector2(direction.x * dashSpeed, rb.linearVelocity.y);
             yield return null;
         }

         // Fin normal (si no fue interrumpido)
         // Comprueba que la corrutina aún es la activa (no fue interrumpida y reiniciada)
         if (ReferenceEquals(dashCoroutine, this.dashCoroutine))
         {
              InterruptActions(); // Llama a Interrupt para limpiar estado y restaurar AIPath/velocidad
              dashCoroutine = null; // Asegura que la referencia se limpia
         }
    }

    IEnumerator InvulnerabilityWindow(float duration)
    {
         if (characterData == null || duration <= 0) yield break;
         float invulnStartTime = Time.time;

         characterData.SetInvulnerable(true);
         // Debug.Log($"[{gameObject.name}] Invulnerable START (Duration: {duration})");

         // Espera mientras dure Y el personaje siga marcado como invulnerable Y no esté aturdido
         while (Time.time < invulnStartTime + duration && characterData.isInvulnerable && !characterData.isStunned)
         {
             yield return null;
         }

         // Solo quitar invulnerabilidad si no fue interrumpido por stun
         // Y si AÚN estaba invulnerable (evita quitarla si otra acción la activó después)
         if (!characterData.isStunned && characterData.isInvulnerable && Time.time >= invulnStartTime + duration)
         {
             characterData.SetInvulnerable(false);
             // Debug.Log($"[{gameObject.name}] Invulnerable END");
         } else if (characterData.isStunned) {
            // Debug.Log($"[{gameObject.name}] Invulnerability ended due to STUN.");
         } else if (!characterData.isInvulnerable) {
            // Debug.Log($"[{gameObject.name}] Invulnerability ended early (possibly InterruptActions).");
         }
    }

    // --- Bloqueo ---
    public bool TryStartBlocking()
    {
         if (characterData == null || characterData.isStunned || characterData.isDashing || characterData.isBlocking) return false;
         CharacterMovement move = GetComponent<CharacterMovement>();
         if (move != null && !move.IsGrounded()) return false;
         if (characterData.currentStamina <= 0) return false; // Necesita algo de estamina

         characterData.SetBlocking(true);
         if (aiPath != null) { // Ajusta velocidad de AIPath al bloquear
              aiPath.maxSpeed = characterData.baseMovementSpeed * characterData.baseBlockSpeedMultiplier; // Usa multi base
         }
         SetAnimatorBool("IsBlocking", true);
         return true;
    }

    public void StopBlocking()
    {
         if (characterData != null && characterData.isBlocking)
         {
             characterData.SetBlocking(false);
             if (aiPath != null && !characterData.isStunned) { // Restaura velocidad normal de AIPath
                  aiPath.maxSpeed = characterData.baseMovementSpeed;
             }
             SetAnimatorBool("IsBlocking", false);
         }
    }

    // --- Parry ---
    public bool TryParry()
    {
         if (characterData == null || characterData.isStunned || characterData.isDashing || characterData.isBlocking) return false;
         if (!characterData.IsParryReady()) return false;
         if (!characterData.ConsumeStamina(characterData.baseParryCost)) return false; // Usa coste base

         InterruptActions(); // Interrumpe otras acciones
         if (parryWindowCoroutine != null) StopCoroutine(parryWindowCoroutine); // Seguridad

         characterData.PutParryOnCooldown(); // Usa cooldown base
         parryWindowCoroutine = StartCoroutine(ParryWindowCoroutine());
         SetAnimatorTrigger("Parry");
         return true;
    }

    IEnumerator ParryWindowCoroutine()
    {
         if (characterData == null) yield break;
         float parryStartTime = Time.time;

         characterData.SetAttemptingParry(true);
         // Debug.Log($"[{gameObject.name}] Parry Window OPEN (Duration: {characterData.baseParryWindow})");

         while (Time.time < parryStartTime + characterData.baseParryWindow && !characterData.isStunned) // Usa ventana base
         {
             yield return null;
         }

         // Limpia solo si esta corrutina sigue activa y no fue interrumpida por stun o éxito
         if (ReferenceEquals(parryWindowCoroutine, this.parryWindowCoroutine) && characterData.isAttemptingParry)
         {
              characterData.SetAttemptingParry(false);
              parryWindowCoroutine = null;
              // Debug.Log($"[{gameObject.name}] Parry Window CLOSED (Missed/Expired)");
         }
    }

    public void NotifySuccessfulParry(GameObject attacker)
    {
         if (characterData == null || !characterData.isAttemptingParry) return;

         Debug.Log($"{gameObject.name} PARRY successful against {attacker.name}!");

         if (parryWindowCoroutine != null) StopCoroutine(parryWindowCoroutine);
         characterData.SetAttemptingParry(false);
         parryWindowCoroutine = null;

         CharacterData attackerData = attacker.GetComponent<CharacterData>();
         if (attackerData != null) {
             attackerData.ApplyStun(characterData.baseParryStunDuration); // Usa duración stun base
         }
         // SetAnimatorTrigger("ParrySuccess");
    }

    // --- Habilidades ---
     public bool TryUseSkill(SkillData skill)
     {
         if (skill == null || characterData == null || characterData.isStunned || characterData.isDashing || characterData.isBlocking) return false;

         // Verifica si está aprendida Y lista (cooldown)
         if (!characterData.learnedSkills.Contains(skill)) return false;
         if (!characterData.IsSkillReady(skill)) return false;

         // Lógica de Rango (sin cambios)
         bool requiresTarget = skill.range > 0 || skill.skillType == SkillType.DirectDamage || skill.skillType == SkillType.Projectile;
         bool inRange = true;
         if (requiresTarget) {
             if (currentTargetHealth == null || !currentTargetHealth.IsAlive()) return false;
             inRange = IsTargetInRange(currentTargetHealth.transform, skill.range);
         }
         bool canUseOutOfRange = skill.skillType == SkillType.Projectile || (skill.skillType == SkillType.AreaOfEffect && !skill.affectsEnemies);
         if (!inRange && !canUseOutOfRange) return false;

         // Ejecutar
         InterruptActions(); // Interrumpe otras acciones (opcional)
         characterData.PutSkillOnCooldown(skill); // Pone cooldown
         string trigger = !string.IsNullOrEmpty(skill.animationTriggerName) ? skill.animationTriggerName : "UseSkill";
         SetAnimatorTrigger(trigger);

         Coroutine skillCoroutine = StartCoroutine(ExecuteSkillCoroutine(skill));
         activeSkillCoroutines.Add(skillCoroutine);
         return true;
     }

    IEnumerator ExecuteSkillCoroutine(SkillData skill)
    {
         if (skill == null || characterData == null) yield break;
         Coroutine thisCoroutine = null; // Necesario para quitarse de la lista al final

         // Intenta encontrar esta misma corrutina en la lista activa
         // Esto es un poco ineficiente, una mejor gestión sería con IDs o diccionarios
         yield return null; // Espera un frame para que se añada a la lista
         foreach(var co in activeSkillCoroutines) {
             // ¿Cómo identificar esta corrutina? Difícil sin un ID único por ejecución.
             // Por ahora, simplemente asignamos la última añadida (puede fallar con skills muy rápidos)
             if (activeSkillCoroutines.Count > 0) thisCoroutine = activeSkillCoroutines[activeSkillCoroutines.Count - 1];
             break;
         }


         if (skill.castVFX != null) Instantiate(skill.castVFX, transform.position, transform.rotation);
         // if (skill.castSFX != null) AudioSource.PlayClipAtPoint(skill.castSFX, transform.position);
         // yield return new WaitForSeconds(skill.castTime); // Si añades cast time

         if (characterData.isStunned) { // Interrumpido por stun durante cast (si lo hubiera)
              if (thisCoroutine != null) activeSkillCoroutines.Remove(thisCoroutine);
              yield break;
         }

         // Lógica del efecto (SIN CAMBIOS GRANDES, usa datos de skill)
         switch (skill.skillType)
         {
             case SkillType.DirectDamage:
                 if (currentTargetHealth != null && IsTargetInRange(currentTargetHealth.transform, skill.range)) {
                     currentTargetHealth.TakeDamage(skill.damage, this.gameObject);
                     if (skill.hitVFX != null) Instantiate(skill.hitVFX, currentTargetHealth.transform.position, Quaternion.identity);
                 }
                 break;
             case SkillType.Projectile:
                  if (skill.projectilePrefab != null) {
                      // ... (lógica de instanciar y inicializar proyectil como antes, usando skill.damage, etc.) ...
                        Vector3 spawnPos = transform.position + transform.right * 0.5f * Mathf.Sign(transform.localScale.x); // Spawn delante
                        Vector3 targetPos = currentTargetHealth?.transform.position ?? (transform.position + transform.right * skill.range * Mathf.Sign(transform.localScale.x));
                        Vector2 direction = ((Vector2)targetPos - (Vector2)spawnPos).normalized;
                        if (direction == Vector2.zero) direction = transform.right * Mathf.Sign(transform.localScale.x);
                        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                        Quaternion rotation = Quaternion.AngleAxis(angle, Vector3.forward);
                        GameObject projGO = Instantiate(skill.projectilePrefab, spawnPos, rotation);
                        Projectile projectileScript = projGO.GetComponent<Projectile>();
                        if (projectileScript != null) {
                            projectileScript.Initialize(skill.damage, skill.projectileSpeed, skill.projectileLifetime, this.gameObject, skill.hitVFX);
                        } else {
                            Debug.LogWarning($"Projectile prefab '{skill.projectilePrefab.name}' is missing Projectile script.");
                            Destroy(projGO, skill.projectileLifetime);
                        }
                  }
                  break;
              case SkillType.SelfBuff:
                  // Aplicar Buff (la corrutina ApplyBuffCoroutine necesita existir y funcionar)
                   Coroutine buffCoroutine = StartCoroutine(ApplyBuffCoroutine(skill));
                   activeSkillCoroutines.Add(buffCoroutine); // Añade la corrutina del buff también
                  break;
             case SkillType.AreaOfEffect:
                  ApplyAoEEffect(skill);
                  break;
             case SkillType.Heal:
                  if (healthSystem != null) healthSystem.RestoreHealth(skill.healAmount); // Usa healAmount
                  if (skill.hitVFX != null) Instantiate(skill.hitVFX, transform.position, Quaternion.identity);
                  break;
             // case SkillType.Debuff: ...
         }

         // Quitar esta corrutina de la lista activa
         if (thisCoroutine != null) activeSkillCoroutines.Remove(thisCoroutine);
         yield break;
    }

     void ApplyAoEEffect(SkillData skill)
     {
          if (skill == null || characterData == null) return;
          Vector2 centerPoint = transform.position; // Centrado en el lanzador por ahora
          Collider2D[] hits = Physics2D.OverlapCircleAll(centerPoint, skill.aoeRadius); // Usa aoeRadius

          if (skill.hitVFX != null) Instantiate(skill.hitVFX, centerPoint, Quaternion.identity);
          // Play AoE SFX

          string enemyTag = GetComponent<LuchadorAIController>()?.enemyTag ?? "Enemy"; // Obtener tag enemigo si es IA

          foreach (Collider2D hit in hits) {
              GameObject targetGO = hit.gameObject;
              bool isSelf = (targetGO == this.gameObject);
              bool isEnemy = targetGO.CompareTag(enemyTag);
              // bool isAlly = !isSelf && !isEnemy;

              if (isSelf && !skill.affectsSelf) continue;
              // if (isAlly && !skill.affectsAllies) continue;
              if (isEnemy && !skill.affectsEnemies) continue;

              HealthSystem targetHealth = hit.GetComponent<HealthSystem>();
              if(targetHealth != null && targetHealth.IsAlive()) {
                  if (skill.damage > 0) targetHealth.TakeDamage(skill.damage, this.gameObject); // Usa skill.damage
                  if (skill.healAmount > 0) targetHealth.RestoreHealth(skill.healAmount); // Usa skill.healAmount
              }
              // TODO: Aplicar debuffs si SkillData los tuviera
              /* CharacterData targetData = hit.GetComponent<CharacterData>();
                 if(targetData != null && skill.debuffEffect != null) { targetData.ApplyDebuff(skill.debuffEffect); } */
          }
     }

    IEnumerator ApplyBuffCoroutine(SkillData buffSkill)
    {
         // --- ¡IMPORTANTE! Esta implementación es MUY BÁSICA ---
         // No maneja bien múltiples buffs al mismo stat o refrescar duraciones.
         // Necesitaría un sistema de modificadores más robusto en CharacterData.

         if (buffSkill == null || characterData == null || buffSkill.skillType != SkillType.SelfBuff) yield break;
         Coroutine thisCoroutine = null;
         yield return null; // Espera un frame
         if(activeSkillCoroutines.Count > 0) thisCoroutine = activeSkillCoroutines[activeSkillCoroutines.Count - 1];


         float originalValue = 0;
         bool applied = false;
         StatToBuff stat = buffSkill.buffStat;
         float multiplier = buffSkill.buffMultiplier;
         float duration = buffSkill.duration;

         Debug.Log($"[{gameObject.name}] Applying buff: {buffSkill.skillName} ({stat} x{multiplier} for {duration}s)");

         // --- Aplicar Buff ---
         try {
              switch (stat) {
                  case StatToBuff.Speed:
                      if (aiPath != null) { // Afecta a AIPath directamente por ahora
                           originalValue = aiPath.maxSpeed;
                           aiPath.maxSpeed *= multiplier;
                           applied = true;
                      } else { // Si no hay AIPath, modifica la stat base (menos ideal)
                          originalValue = characterData.baseMovementSpeed;
                          characterData.baseMovementSpeed *= multiplier;
                          applied = true;
                      }
                      break;
                  case StatToBuff.Damage: // Modifica stat base temporalmente
                      originalValue = characterData.baseAttackDamage;
                      characterData.baseAttackDamage *= multiplier;
                      applied = true;
                      break;
                 // case StatToBuff.Defense: // Necesitaría una stat base de defensa o modificar blockMultiplier
                 // case StatToBuff.AttackSpeed: // Modificar baseAttackCooldown
                 //     originalValue = characterData.baseAttackCooldown;
                 //     characterData.baseAttackCooldown /= multiplier; // Dividir para acelerar
                 //     applied = true;
                 //     break;
              }

              // --- Esperar Duración ---
              if (applied) {
                   float endTime = Time.time + duration;
                   while (Time.time < endTime && !characterData.isStunned) { // Buff termina si aturdido
                        yield return null;
                   }
                   if(characterData.isStunned) Debug.Log($"[{gameObject.name}] Buff {buffSkill.skillName} ended early due to stun.");
              }
         }
         finally // --- Quitar Buff (Garantizado) ---
         {
              if (applied) {
                   Debug.Log($"[{gameObject.name}] Removing buff: {buffSkill.skillName}");
                   switch (stat) {
                        case StatToBuff.Speed:
                             if (aiPath != null) { // Restaura valor original de AIPath
                                  // ¡Cuidado! Si otro buff de velocidad se aplicó, esto lo sobrescribe.
                                  aiPath.maxSpeed = originalValue;
                             } else { // Restaura stat base
                                  characterData.baseMovementSpeed = originalValue;
                             }
                             break;
                        case StatToBuff.Damage:
                             characterData.baseAttackDamage = originalValue; // Restaura stat base
                             break;
                        // case StatToBuff.AttackSpeed:
                        //     characterData.baseAttackCooldown = originalValue; // Restaura stat base
                        //     break;
                   }
              }
              if (thisCoroutine != null) activeSkillCoroutines.Remove(thisCoroutine);
         }
         yield break;
    }


    // --- Helpers ---
    public bool IsTargetInRange(Transform targetTransform, float range)
    {
        if (targetTransform == null) return false;
        return (targetTransform.position - transform.position).sqrMagnitude <= range * range;
    }

    // --- Animator ---
     public void UpdateAnimatorLogic(bool isGrounded, Vector2 velocity)
     {
         if (animator == null) return;
         bool isMovingHorizontally = Mathf.Abs(velocity.x) > 0.1f;
         SetAnimatorBool("IsMoving", isMovingHorizontally && isGrounded);
         SetAnimatorBool("IsGrounded", isGrounded);
         SetAnimatorFloat("VerticalVelocity", velocity.y);

         // Flip sprite
         if (Mathf.Abs(velocity.x) > 0.1f) // Solo voltea si hay movimiento significativo
         {
              float localScaleX = Mathf.Abs(transform.localScale.x) * Mathf.Sign(velocity.x);
              transform.localScale = new Vector3(localScaleX, transform.localScale.y, transform.localScale.z);
         }
     }
    public void SetAnimatorBool(string name, bool value) { if (HasParameter(name, animator)) animator.SetBool(name, value); }
    public void SetAnimatorFloat(string name, float value) { if (HasParameter(name, animator)) animator.SetFloat(name, value); }
    public void SetAnimatorTrigger(string name) { if (HasParameter(name, animator)) animator.SetTrigger(name); }

    // CORREGIDO: Añadido return false;
    private bool HasParameter(string paramName, Animator animatorToCheck)
    {
        if (animatorToCheck == null || string.IsNullOrEmpty(paramName) || animatorToCheck.runtimeAnimatorController == null) return false;
        foreach (AnimatorControllerParameter param in animatorToCheck.parameters)
        {
            if (param.name == paramName) return true;
        }
        // Si el bucle termina sin encontrarlo, devuelve false
        return false;
    }
}