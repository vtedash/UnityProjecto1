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
    private AIPath aiPath; // Referencia a AIPath para pausarlo si es necesario
    private Animator animator;

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
        if(rb == null) Debug.LogError("Rigidbody2D no encontrado en CharacterCombat!", this);
    }

    void Update()
    {
        // Drenar stamina si está bloqueando
        if (characterData.isBlocking && !characterData.isStunned && characterData.baseStats != null)
        {
            bool stillHasStamina = characterData.ConsumeStamina(characterData.baseStats.blockStaminaDrain * Time.deltaTime);
            if (!stillHasStamina)
            {
                StopBlocking();
            }
        }
    }

    public void SetTarget(HealthSystem newTarget){ currentTargetHealth = newTarget; }
    public HealthSystem GetTarget(){ return currentTargetHealth; }

    // Detiene acciones en curso (stuns, etc.)
    public void InterruptActions()
    {
        if (dashCoroutine != null)
        {
            StopCoroutine(dashCoroutine);
            // rb.velocity = Vector2.zero; // Dejar que la física lo detenga o la IA retome
            characterData.SetDashing(false);
            characterData.SetInvulnerable(false);
            dashCoroutine = null;
            // Reactivar AIPath si fue pausado por el dash y no está stuneado
             if(aiPath != null && !characterData.isStunned) aiPath.canMove = true;
        }
        if (parryWindowCoroutine != null)
        {
            StopCoroutine(parryWindowCoroutine);
            characterData.SetAttemptingParry(false);
            parryWindowCoroutine = null;
        }
        StopBlocking(); // Asegurarse de que deja de bloquear
        StopCoroutine(nameof(ExecuteSkillCoroutine));
        StopCoroutine(nameof(ApplyAttackDamageAfterDelay));
        // Detener buffs activos si es necesario (opcional)
        // foreach (var coroutine in activeBuffCoroutines) { StopCoroutine(coroutine); }
        // activeBuffCoroutines.Clear();
    }

    public bool TryAttack() {
         if (characterData.isStunned || characterData.isDashing || characterData.isBlocking || characterData.baseStats == null) return false;
        if (!characterData.IsAttackReady()) return false;
        if (currentTargetHealth == null || !currentTargetHealth.IsAlive()) return false;
        if (!IsTargetInBasicAttackRange(currentTargetHealth.transform)) return false;
        // Opcional: Requerir estar en el suelo para atacar
        // CharacterMovement move = GetComponent<CharacterMovement>();
        // if(move != null && !move.IsGrounded()) return false;

        characterData.PutAttackOnCooldown();
        Debug.Log($"{gameObject.name} ataca a {currentTargetHealth.gameObject.name}");
        animator?.SetTrigger("Attack");
        StartCoroutine(ApplyAttackDamageAfterDelay(currentTargetHealth, 0.2f));
        // Ya no pausamos el movimiento aquí, la IA lo gestiona
        return true;
     }

    IEnumerator ApplyAttackDamageAfterDelay(HealthSystem target, float delay) {
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
        // Opcional: Requerir estar en el suelo para dash terrestre
         // CharacterMovement move = GetComponent<CharacterMovement>();
         // if(move != null && !move.IsGrounded()) return false;

        characterData.PutDashOnCooldown();
        // InterruptActions(); // Decidir si el dash interrumpe todo

        if (dashCoroutine != null) StopCoroutine(dashCoroutine);
        dashCoroutine = StartCoroutine(DashCoroutine(direction.normalized));

        animator?.SetTrigger("Dash");
        return true;
    }

    IEnumerator DashCoroutine(Vector2 direction)
    {
        if (characterData.baseStats == null || rb == null) yield break;

        characterData.SetDashing(true);
        StartCoroutine(InvulnerabilityWindow(characterData.baseStats.dashInvulnerabilityDuration));

        float startTime = Time.time;
        float dashSpeed = characterData.baseStats.movementSpeed * characterData.baseStats.dashSpeedMult;
        if (aiPath != null) aiPath.canMove = false; // Pausar cálculo A* durante dash

        // NO SE MODIFICA LA GRAVEDAD AQUÍ
        float dashEndTime = startTime + characterData.baseStats.dashDuration;

        while (Time.time < dashEndTime)
        {
            if (characterData.isStunned)
            {
                characterData.SetDashing(false);
                dashCoroutine = null;
                // Reactivar AIPath si no está stuneado
                if(aiPath != null && !characterData.isStunned) aiPath.canMove = true;
                yield break;
            }
            // Aplicar velocidad de dash respetando gravedad (eje Y)
            rb.linearVelocity = new Vector2(direction.x * dashSpeed, rb.linearVelocity.y);
            // Para dash omnidireccional (pero afectado por gravedad aún):
            // rb.velocity = direction * dashSpeed + new Vector2(0, rb.velocity.y); // Incorrecto
            // rb.velocity = new Vector2(direction.x * dashSpeed, direction.y * dashSpeed + rb.velocity.y); // Complejo
            // Mejor usar AddForce si se quiere omnidireccional o controlar Y separadamente

            yield return null;
        }

        // Fin del Dash normal
        characterData.SetDashing(false);

        // Opcional: Limitar velocidad horizontal al terminar el dash para no deslizar demasiado
        if (Mathf.Abs(rb.linearVelocity.x) > characterData.baseStats.movementSpeed) {
             rb.linearVelocity = new Vector2(Mathf.Sign(rb.linearVelocity.x) * characterData.baseStats.movementSpeed, rb.linearVelocity.y);
        }

        if (aiPath != null && !characterData.isStunned) aiPath.canMove = true; // Permitir que A* calcule de nuevo
        dashCoroutine = null;
        Debug.Log($"{gameObject.name} terminó dash");
    }

     IEnumerator InvulnerabilityWindow(float duration) {
         if (characterData.baseStats == null || duration <= 0) yield break;
         characterData.SetInvulnerable(true);
         float endTime = Time.time + duration;
         while(Time.time < endTime && !characterData.isStunned) { yield return null; }
         if (!characterData.isStunned) { characterData.SetInvulnerable(false); }
      }

     public bool TryStartBlocking() {
         if (characterData.isStunned || characterData.isDashing || characterData.isBlocking || characterData.baseStats == null) return false;
         // Requerir estar en el suelo para bloquear
         CharacterMovement move = GetComponent<CharacterMovement>();
         if(move != null && !move.IsGrounded()) return false;

         if (characterData.currentStamina >= 0)
         {
            characterData.SetBlocking(true);
            if (aiPath != null) aiPath.maxSpeed = characterData.baseStats.movementSpeed * characterData.baseStats.blockSpeedMultiplier;
            animator?.SetBool("IsBlocking", true);
            Debug.Log($"{gameObject.name} empieza a bloquear");
            return true;
         }
         return false;
      }

     public void StopBlocking() {
         if (characterData.isBlocking && characterData.baseStats != null)
         {
            characterData.SetBlocking(false);
            if (aiPath != null) aiPath.maxSpeed = characterData.baseStats.movementSpeed;
            animator?.SetBool("IsBlocking", false);
            Debug.Log($"{gameObject.name} deja de bloquear");
         }
      }

     public bool TryParry() {
          if (characterData.isStunned || characterData.isDashing || characterData.isBlocking || characterData.baseStats == null) return false;
         if (!characterData.IsParryReady()) return false;
         if (!characterData.ConsumeStamina(characterData.baseStats.parryCost)) return false;
          // Requerir estar en el suelo para parry?
         // CharacterMovement move = GetComponent<CharacterMovement>();
         // if(move != null && !move.IsGrounded()) return false;

         characterData.PutParryOnCooldown();
         InterruptActions(); // Parry sí suele interrumpir todo
         if (parryWindowCoroutine != null) StopCoroutine(parryWindowCoroutine);
         parryWindowCoroutine = StartCoroutine(ParryWindowCoroutine());
         animator?.SetTrigger("Parry");
         Debug.Log($"{gameObject.name} intenta parry!");
         return true;
      }

     IEnumerator ParryWindowCoroutine() {
        if (characterData.baseStats == null) yield break;
        characterData.SetAttemptingParry(true);
        float endTime = Time.time + characterData.baseStats.parryWindow;
        while(Time.time < endTime && !characterData.isStunned) { yield return null; }
        bool coroutineStillActive = ReferenceEquals(parryWindowCoroutine, this.GetComponent<Coroutine>()); // Check specific instance
        if (characterData.isAttemptingParry && !characterData.isStunned && coroutineStillActive) { characterData.SetAttemptingParry(false); }
        if (coroutineStillActive) { parryWindowCoroutine = null; }
      }

     public void NotifySuccessfulParry(GameObject attacker) {
         if (!characterData.isAttemptingParry || characterData.baseStats == null) return;
         Debug.Log($"{gameObject.name} realizó PARRY exitoso contra {attacker.name}!");
         if (parryWindowCoroutine != null) StopCoroutine(parryWindowCoroutine);
         characterData.SetAttemptingParry(false);
         parryWindowCoroutine = null;
         CharacterData attackerData = attacker.GetComponent<CharacterData>();
         if (attackerData != null) { attackerData.ApplyStun(characterData.baseStats.parryStunDuration); }
         // animator?.SetTrigger("ParrySuccess");
      }

     public bool TryUseSkill(SkillData skill) {
         if (skill == null || characterData.baseStats == null) return false;
         if (characterData.isStunned || characterData.isDashing || characterData.isBlocking) return false;
         if (!characterData.IsSkillReady(skill)) return false;
         // Comprobar si se puede usar en el aire
         // CharacterMovement move = GetComponent<CharacterMovement>();
         // bool canUseInAir = true; // O leer de SkillData
         // if(move != null && !move.IsGrounded() && !canUseInAir) return false;

         bool requiresTarget = skill.range > 0 || skill.skillType == SkillType.DirectDamage || skill.skillType == SkillType.Projectile;
         bool inRange = skill.range <= 0;
         if (requiresTarget && currentTargetHealth != null) { inRange = IsTargetInRange(currentTargetHealth.transform, skill.range); }
         else if (requiresTarget && currentTargetHealth == null) { return false; }
         bool canUseOutOfRange = skill.skillType == SkillType.Projectile || skill.skillType == SkillType.AreaOfEffect;
         if (!inRange && !canUseOutOfRange) { Debug.Log($"{gameObject.name} intentó usar {skill.skillName} pero objetivo fuera de rango ({skill.range}m)"); return false; }

         characterData.PutSkillOnCooldown(skill);
         // InterruptActions(); // Decidir si las skills interrumpen todo

         Debug.Log($"{gameObject.name} usa habilidad: {skill.skillName}");
         string trigger = !string.IsNullOrEmpty(skill.animationTriggerName) ? skill.animationTriggerName : "UseSkill";
         animator?.SetTrigger(trigger);
         StartCoroutine(ExecuteSkillCoroutine(skill));
         // Ya no pausamos movimiento aquí
         return true;
      }

     IEnumerator ExecuteSkillCoroutine(SkillData skill) {
         if (skill == null || characterData.baseStats == null) yield break;
         if (skill.castVFX != null) Instantiate(skill.castVFX, transform.position, transform.rotation);
         // yield return new WaitForSeconds(skill.castTime); // Si hay cast time
         if (characterData.isStunned) yield break;
         switch (skill.skillType) { /* ... (Lógica de skills sin cambios) ... */ }
         yield break; // Asegurar que la corutina termine
      }

     void ApplyAoEEffect(SkillData skill) { /* ... (código existente sin cambios) ... */ }
     IEnumerator ApplyBuffCoroutine(SkillData buffSkill) { /* ... (código existente sin cambios) ... */ yield break; }

    private bool IsTargetInBasicAttackRange(Transform targetTransform) {
        if (characterData.baseStats == null) return false;
        return IsTargetInRange(targetTransform, characterData.baseStats.attackRange);
    }
    public bool IsTargetInRange(Transform targetTransform, float range) {
        if (targetTransform == null) return false;
        return (targetTransform.position - transform.position).sqrMagnitude <= range * range;
    }
    // IEnumerator PauseMovementDuringAction(float duration) { // Ya no se usa
    //     yield break;
    // }
}