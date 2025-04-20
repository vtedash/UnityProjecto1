// File: CharacterCombat.cs
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pathfinding; // Needed for AIPath reference

[RequireComponent(typeof(CharacterData))]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(HealthSystem))]
[RequireComponent(typeof(Animator))] // Animator is now required
public class CharacterCombat : MonoBehaviour
{
    // --- Component References ---
    private CharacterData characterData;
    private Rigidbody2D rb;
    private HealthSystem healthSystem;
    private AIPath aiPath; // Reference to control AIPath state during actions
    private Animator animator; // Animator is essential now

    // --- State ---
    private HealthSystem currentTargetHealth;
    private Coroutine dashCoroutine;
    private Coroutine parryWindowCoroutine;
    private List<Coroutine> activeSkillCoroutines = new List<Coroutine>(); // Track active skills

    void Awake()
    {
        characterData = GetComponent<CharacterData>();
        rb = GetComponent<Rigidbody2D>();
        healthSystem = GetComponent<HealthSystem>();
        aiPath = GetComponent<AIPath>(); // Can be null if AIPath isn't used (e.g., player control)
        animator = GetComponent<Animator>();

        // --- Component Validation ---
        if (characterData == null) Debug.LogError("CharacterData not found!", this);
        if (rb == null) Debug.LogError("Rigidbody2D not found!", this);
        if (healthSystem == null) Debug.LogError("HealthSystem not found!", this);
        if (animator == null) Debug.LogError("Animator not found! Combat actions and visuals will fail.", this); // Now an error
    }

    void Update()
    {
        // Continuous stamina drain while blocking
        if (characterData != null && characterData.isBlocking && !characterData.isStunned && characterData.baseStats != null)
        {
            bool stillHasStamina = characterData.ConsumeStamina(characterData.baseStats.blockStaminaDrain * Time.deltaTime);
            // Automatically stop blocking if stamina runs out
            if (!stillHasStamina)
            {
                StopBlocking();
            }
        }
    }

    // --- Target Management ---
    public void SetTarget(HealthSystem newTarget) { currentTargetHealth = newTarget; }
    public HealthSystem GetTarget() { return currentTargetHealth; }

    /// <summary>
    /// Stops ongoing actions like dashing, parrying, or skill casting. Crucial for stuns or interrupts.
    /// </summary>
    public void InterruptActions()
    {
        // Stop Dashing
        if (dashCoroutine != null)
        {
            StopCoroutine(dashCoroutine);
            characterData.SetDashing(false);
            characterData.SetInvulnerable(false); // Ensure invulnerability ends
            dashCoroutine = null;
            // Restore AIPath state if it exists and character is not stunned
            if (aiPath != null && !characterData.isStunned)
            {
                 // Let the AI decide if it should move again, don't force aiPath.isStopped = false here
                 // The AI's Update loop will handle setting isStopped based on its current decision.
            }
        }
        // Stop Parry Attempt
        if (parryWindowCoroutine != null)
        {
            StopCoroutine(parryWindowCoroutine);
            characterData.SetAttemptingParry(false);
            parryWindowCoroutine = null;
        }
        // Stop Blocking
        StopBlocking(); // Ensures block animation stops etc.

        // Stop any active skill coroutines
        foreach (Coroutine skillCoroutine in activeSkillCoroutines)
        {
            if (skillCoroutine != null) StopCoroutine(skillCoroutine);
        }
        activeSkillCoroutines.Clear();

        // Stop specific delayed actions if needed (like the attack damage application)
        StopCoroutine(nameof(ApplyAttackDamageAfterDelay));
        StopCoroutine(nameof(ExecuteSkillCoroutine)); // Added for safety
        StopCoroutine(nameof(ApplyBuffCoroutine)); // Added for safety
    }


    // --- Action Attempts ---

    /// <summary> Tries to perform a basic attack. </summary>
    /// <returns>True if the attack started, false otherwise.</returns>
    public bool TryAttack()
    {
        // Check conditions: Not stunned, dashing, blocking, stats available, cooldown ready, target valid, in range, grounded
        if (characterData == null || characterData.isStunned || characterData.isDashing || characterData.isBlocking || characterData.baseStats == null) return false;
        if (!characterData.IsAttackReady()) return false;
        if (currentTargetHealth == null || !currentTargetHealth.IsAlive()) return false; // Target must be valid and alive
        if (!IsTargetInBasicAttackRange(currentTargetHealth.transform)) return false;

        // Check if grounded using CharacterMovement
        CharacterMovement move = GetComponent<CharacterMovement>();
        if (move != null && !move.IsGrounded()) return false; // Cannot attack mid-air by default

        // If all checks pass, proceed with attack
        characterData.PutAttackOnCooldown();
        SetAnimatorTrigger("Attack");
        // Apply damage after a delay (consider using Animation Events instead)
        StartCoroutine(ApplyAttackDamageAfterDelay(currentTargetHealth, 0.2f)); // Adjust delay as needed
        return true;
    }

    /// <summary> Applies attack damage after a specified delay. </summary>
    IEnumerator ApplyAttackDamageAfterDelay(HealthSystem target, float delay)
    {
        yield return new WaitForSeconds(delay);
        // Re-check conditions before applying damage (target might have died, moved, or character got stunned)
        if (characterData != null && !characterData.isStunned && target != null && target.IsAlive() && IsTargetInBasicAttackRange(target.transform) && characterData.baseStats != null)
        {
            target.TakeDamage(characterData.baseStats.attackDamage, this.gameObject);
        }
    }

    /// <summary> Tries to perform a dash in the specified direction. </summary>
    /// <returns>True if the dash started, false otherwise.</returns>
    public bool TryDash(Vector2 direction)
    {
        // Check conditions: Not stunned, dashing, blocking, stats available, cooldown ready, enough stamina
        if (characterData == null || characterData.isStunned || characterData.isDashing || characterData.isBlocking || characterData.baseStats == null) return false;
        if (!characterData.IsDashReady()) return false;
        if (!characterData.ConsumeStamina(characterData.baseStats.dashCost)) return false; // Check stamina cost

        // Stop previous dash if any (shouldn't happen often, but safety)
        if (dashCoroutine != null) StopCoroutine(dashCoroutine);

        // Start dash
        characterData.PutDashOnCooldown();
        dashCoroutine = StartCoroutine(DashCoroutine(direction.normalized));
        SetAnimatorTrigger("Dash");
        return true;
    }

    /// <summary> Handles the dash movement, invulnerability, and state changes. </summary>
    IEnumerator DashCoroutine(Vector2 direction)
    {
        if (characterData == null || characterData.baseStats == null || rb == null) yield break; // Safety check

        characterData.SetDashing(true);
        StartCoroutine(InvulnerabilityWindow(characterData.baseStats.dashInvulnerabilityDuration)); // Start invulnerability

        float dashSpeed = characterData.baseStats.movementSpeed * characterData.baseStats.dashSpeedMult;
        float dashEndTime = Time.time + characterData.baseStats.dashDuration;

        // Temporarily stop AI pathfinding during dash
        if (aiPath != null) aiPath.isStopped = true;

        // --- Dash Movement Loop ---
        while (Time.time < dashEndTime)
        {
            // Check if stunned during dash
            if (characterData.isStunned)
            {
                characterData.SetDashing(false); // Immediately stop dash state
                dashCoroutine = null;
                // Let stun logic handle AIPath state
                yield break; // Exit coroutine
            }
            // Apply dash velocity (primarily horizontal, maintain vertical velocity)
            rb.linearVelocity = new Vector2(direction.x * dashSpeed, rb.linearVelocity.y);
            yield return null; // Wait for next frame
        }
        // --- End of Dash ---
        characterData.SetDashing(false); // Reset dashing state

        // Optional: Smoothly transition back to normal speed if needed, or let AI/Player input take over.
        // Avoid sudden stops if possible. AI FixedUpdate or player input will recalculate velocity.
         if (aiPath != null && !characterData.isStunned)
         {
             // AI's Update loop will decide if aiPath should resume (isStopped = false)
         }

        dashCoroutine = null; // Clear coroutine reference
    }

    /// <summary> Manages the invulnerability window during actions like dashing. </summary>
    IEnumerator InvulnerabilityWindow(float duration)
    {
        if (characterData == null || duration <= 0) yield break; // No duration or data, exit

        characterData.SetInvulnerable(true);
        float endTime = Time.time + duration;

        // Wait for duration, unless stunned
        while (Time.time < endTime && !characterData.isStunned)
        {
            yield return null;
        }

        // Only remove invulnerability if not stunned (stun might have its own logic)
        if (!characterData.isStunned)
        {
            characterData.SetInvulnerable(false);
        }
        // If stunned, let the stun logic decide when/if invulnerability ends
    }


    /// <summary> Tries to start blocking. </summary>
    /// <returns>True if blocking started, false otherwise.</returns>
    public bool TryStartBlocking()
    {
        // Check conditions: Not stunned, dashing, already blocking, stats available, grounded
        if (characterData == null || characterData.isStunned || characterData.isDashing || characterData.isBlocking || characterData.baseStats == null) return false;

        // Check if grounded
        CharacterMovement move = GetComponent<CharacterMovement>();
        if (move != null && !move.IsGrounded()) return false;

        // Check if has at least minimal stamina to start blocking
        if (characterData.currentStamina > 0) // Allow blocking even with low stamina, drain will stop it
        {
            characterData.SetBlocking(true);
            // Slow down AI movement speed while blocking
            if (aiPath != null)
            {
                 aiPath.maxSpeed = characterData.baseStats.movementSpeed * characterData.baseStats.blockSpeedMultiplier;
                 aiPath.isStopped = true; // Explicitly stop path movement while blocking
            }
            SetAnimatorBool("IsBlocking", true);
            return true;
        }
        return false; // Not enough stamina or other condition failed
    }

    /// <summary> Stops blocking. </summary>
    public void StopBlocking()
    {
        // Only stop if currently blocking
        if (characterData != null && characterData.isBlocking && characterData.baseStats != null)
        {
            characterData.SetBlocking(false);
            // Restore normal AI movement speed and allow movement
            if (aiPath != null && !characterData.isStunned) // Don't restore if stunned
            {
                 aiPath.maxSpeed = characterData.baseStats.movementSpeed;
                 // Let the AI decide if it should move again (isStopped)
            }
            SetAnimatorBool("IsBlocking", false);
        }
    }

    /// <summary> Tries to initiate a parry attempt. </summary>
    /// <returns>True if the parry attempt started, false otherwise.</returns>
    public bool TryParry()
    {
        // Check conditions: Not stunned, dashing, blocking, stats available, cooldown ready, enough stamina
        if (characterData == null || characterData.isStunned || characterData.isDashing || characterData.isBlocking || characterData.baseStats == null) return false;
        if (!characterData.IsParryReady()) return false;
        if (!characterData.ConsumeStamina(characterData.baseStats.parryCost)) return false;

        // Stop previous parry attempt if any
        if (parryWindowCoroutine != null) StopCoroutine(parryWindowCoroutine);

        // Start parry
        characterData.PutParryOnCooldown();
        // InterruptActions(); // Optional: Interrupt other actions when parrying? Maybe not attack.
        parryWindowCoroutine = StartCoroutine(ParryWindowCoroutine());
        SetAnimatorTrigger("Parry");
        return true;
    }

    /// <summary> Manages the active window for a parry attempt. </summary>
    IEnumerator ParryWindowCoroutine()
    {
        if (characterData == null || characterData.baseStats == null) yield break; // Safety

        characterData.SetAttemptingParry(true);
        float endTime = Time.time + characterData.baseStats.parryWindow;

        // Wait for the parry window duration, unless stunned
        while (Time.time < endTime && !characterData.isStunned)
        {
            yield return null;
        }

        // Clean up only if this specific coroutine instance is still the active one
        // (Prevents an old, stopped coroutine from interfering if TryParry was called again quickly)
        if (ReferenceEquals(parryWindowCoroutine, this.parryWindowCoroutine))
        {
             characterData.SetAttemptingParry(false); // Window closed
             parryWindowCoroutine = null; // Clear reference
        }
    }

    /// <summary> Called by HealthSystem when a parry is successful against an attacker. </summary>
    public void NotifySuccessfulParry(GameObject attacker)
    {
        // Must be actively attempting parry and have stats
        if (!characterData.isAttemptingParry || characterData.baseStats == null) return;

        Debug.Log($"{gameObject.name} PARRY successful against {attacker.name}!");

        // Immediately stop the parry window coroutine and state
        if (parryWindowCoroutine != null) StopCoroutine(parryWindowCoroutine);
        characterData.SetAttemptingParry(false);
        parryWindowCoroutine = null;

        // Apply stun to the attacker
        CharacterData attackerData = attacker.GetComponent<CharacterData>();
        if (attackerData != null)
        {
            attackerData.ApplyStun(characterData.baseStats.parryStunDuration);
        }

        // Optional: Trigger a parry success animation/effect
        // SetAnimatorTrigger("ParrySuccess");
    }

    /// <summary> Tries to use a specific skill. </summary>
    /// <returns>True if the skill usage started, false otherwise.</returns>
    public bool TryUseSkill(SkillData skill)
    {
        // Basic checks
        if (skill == null || characterData == null || characterData.baseStats == null) return false;
        if (characterData.isStunned || characterData.isDashing || characterData.isBlocking) return false; // Can't use skills while doing other actions
        if (!characterData.IsSkillReady(skill)) return false; // Check cooldown

        // Range checks
        bool requiresTarget = skill.range > 0 || skill.skillType == SkillType.DirectDamage || skill.skillType == SkillType.Projectile;
        bool inRange = true; // Assume in range for self-cast or AoE centered on self

        if (requiresTarget)
        {
            if (currentTargetHealth == null || !currentTargetHealth.IsAlive()) return false; // Need a live target
            inRange = IsTargetInRange(currentTargetHealth.transform, skill.range);
        }

        // Check if skill can be used even if target is out of specified range (e.g., projectile just needs a direction)
        bool canUseOutOfRange = skill.skillType == SkillType.Projectile || (skill.skillType == SkillType.AreaOfEffect && !skill.affectsEnemies); // Allow self-AoE always

        if (!inRange && !canUseOutOfRange)
        {
             // Debug.Log($"{skill.skillName} requires target in range {skill.range}, but target is out of range.");
             return false; // Target required and out of range
        }

        // If all checks pass, use the skill
        characterData.PutSkillOnCooldown(skill);
        string trigger = !string.IsNullOrEmpty(skill.animationTriggerName) ? skill.animationTriggerName : "UseSkill"; // Use specific or generic trigger
        SetAnimatorTrigger(trigger);

        Coroutine skillCoroutine = StartCoroutine(ExecuteSkillCoroutine(skill));
        activeSkillCoroutines.Add(skillCoroutine); // Track it
        return true;
    }

    /// <summary> Executes the logic for a given skill. </summary>
    IEnumerator ExecuteSkillCoroutine(SkillData skill)
    {
         // Reference Check
         if (skill == null || characterData == null || characterData.baseStats == null) yield break;

         Coroutine thisCoroutine = null; // Need to capture the specific instance
         // Find the coroutine in the active list (a bit inefficient, consider Dictionary if many skills)
         foreach(var co in activeSkillCoroutines) { /* Logic to identify 'this' coroutine might be needed if ExecuteSkillCoroutine itself yields */ thisCoroutine = co; break;}


         // Cast VFX (optional)
         if (skill.castVFX != null) Instantiate(skill.castVFX, transform.position, transform.rotation);
         // Cast SFX (optional)
         // if (skill.castSFX != null) AudioSource.PlayClipAtPoint(skill.castSFX, transform.position);

         // Wait for potential cast time (if you add it to SkillData)
         // yield return new WaitForSeconds(skill.castTime);

         // Check if stunned during cast time
         if (characterData.isStunned)
         {
             if (thisCoroutine != null) activeSkillCoroutines.Remove(thisCoroutine);
             yield break;
         }

         // --- Execute Skill Effect based on Type ---
         switch (skill.skillType)
         {
             case SkillType.DirectDamage:
                 if (currentTargetHealth != null && IsTargetInRange(currentTargetHealth.transform, skill.range))
                 {
                     currentTargetHealth.TakeDamage(skill.damage, this.gameObject);
                     if (skill.hitVFX != null) Instantiate(skill.hitVFX, currentTargetHealth.transform.position, Quaternion.identity);
                     // Play Hit SFX
                 }
                 break;

             case SkillType.Projectile:
                 if (skill.projectilePrefab != null)
                 {
                     Vector3 spawnPos = transform.position + transform.TransformDirection(new Vector3(0.5f, 0, 0)); // Spawn slightly ahead
                     Vector3 targetPos = currentTargetHealth?.transform.position ?? (transform.position + transform.right * skill.range); // Aim at target or forward

                     Vector2 direction = ((Vector2)targetPos - (Vector2)spawnPos).normalized;
                     if (direction == Vector2.zero) direction = transform.right; // Default direction if target is too close

                     float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                     Quaternion rotation = Quaternion.AngleAxis(angle, Vector3.forward);

                     GameObject projGO = Instantiate(skill.projectilePrefab, spawnPos, rotation);
                     Projectile projectileScript = projGO.GetComponent<Projectile>();
                     if (projectileScript != null)
                     {
                         projectileScript.Initialize(skill.damage, skill.projectileSpeed, skill.projectileLifetime, this.gameObject, skill.hitVFX);
                     }
                     else
                     {
                         Debug.LogWarning($"Projectile prefab '{skill.projectilePrefab.name}' is missing the Projectile script component. Destroying after lifetime.");
                         Destroy(projGO, skill.projectileLifetime); // Still destroy it
                     }
                 }
                 break;

             case SkillType.SelfBuff:
                 Coroutine buffCoroutine = StartCoroutine(ApplyBuffCoroutine(skill));
                 // Note: Managing multiple overlapping buffs requires more complex logic
                 // This simple version just starts it. Consider tracking buffs in CharacterData.
                 activeSkillCoroutines.Add(buffCoroutine); // Track the buff coroutine as well
                 break;

             case SkillType.AreaOfEffect:
                 ApplyAoEEffect(skill); // Call helper function
                 break;

             case SkillType.Heal:
                 if (healthSystem != null) healthSystem.RestoreHealth(skill.healAmount);
                 if (skill.hitVFX != null) Instantiate(skill.hitVFX, transform.position, Quaternion.identity); // Heal VFX on self
                 // Play Heal SFX
                 break;

                // case SkillType.Debuff: // Add debuff logic if needed
                // break;
         }

         // Remove this coroutine from tracking after execution
         if (thisCoroutine != null) activeSkillCoroutines.Remove(thisCoroutine);
         yield break; // Skill finished
    }

    /// <summary> Applies Area of Effect damage/heal. </summary>
     void ApplyAoEEffect(SkillData skill)
     {
         if (skill == null || characterData == null || characterData.baseStats == null) return;

         Vector2 centerPoint = transform.position; // AoE centered on caster for now
         Collider2D[] hits = Physics2D.OverlapCircleAll(centerPoint, skill.aoeRadius);

         // Instantiate AoE VFX at center
         if (skill.hitVFX != null) Instantiate(skill.hitVFX, centerPoint, Quaternion.identity);
         // Play AoE SFX

         string enemyTag = characterData.GetComponent<LuchadorAIController>()?.enemyTag ?? "Enemy"; // Determine enemy tag dynamically if possible

         foreach (Collider2D hit in hits)
         {
             GameObject targetGO = hit.gameObject;
             bool isSelf = (targetGO == this.gameObject);

             // Determine if the target is an enemy (needs refinement for multi-team scenarios)
             bool isEnemy = targetGO.CompareTag(enemyTag);
             // bool isAlly = !isSelf && !isEnemy; // Basic ally assumption

             // --- Apply Effects based on Flags ---
             if (isSelf && !skill.affectsSelf) continue;
             // if (isAlly && !skill.affectsAllies) continue; // If allies exist
             if (isEnemy && !skill.affectsEnemies) continue;

             // Apply Damage
             if (skill.damage > 0)
             {
                 HealthSystem targetHealth = hit.GetComponent<HealthSystem>();
                 if (targetHealth != null && targetHealth.IsAlive())
                 {
                     targetHealth.TakeDamage(skill.damage, this.gameObject);
                 }
             }

             // Apply Heal
             if (skill.healAmount > 0)
             {
                  HealthSystem targetHealth = hit.GetComponent<HealthSystem>();
                  if (targetHealth != null && targetHealth.IsAlive())
                  {
                     targetHealth.RestoreHealth(skill.healAmount);
                  }
             }

             // Apply Stun/Debuff (Requires CharacterData on target)
             /*
             if (skill.stunDuration > 0) {
                 CharacterData targetData = hit.GetComponent<CharacterData>();
                 if(targetData != null) { targetData.ApplyStun(skill.stunDuration); }
             }
             */
         }
     }

    /// <summary> Applies a temporary buff from a skill. </summary>
    IEnumerator ApplyBuffCoroutine(SkillData buffSkill)
    {
        if (buffSkill == null || characterData == null || characterData.baseStats == null) yield break;

        Coroutine thisCoroutine = null;
        // Find self in list - necessary if buff coroutines can overlap and need specific removal
         foreach(var co in activeSkillCoroutines) { /* Find self */ thisCoroutine = co; break;}

        float originalValue = 0; // Store original value to restore later
        bool applied = false;

        // --- Apply Buff ---
        try
        {
            switch (buffSkill.buffStat)
            {
                case StatToBuff.Speed:
                    if (aiPath != null)
                    {
                        originalValue = aiPath.maxSpeed;
                        aiPath.maxSpeed *= buffSkill.buffMultiplier;
                        applied = true;
                        Debug.Log($"{name} Speed Buff Applied: {originalValue} -> {aiPath.maxSpeed}");
                    }
                    // Add cases for Damage, Defense, etc. modifying CharacterStats directly or via temporary multipliers in CharacterData
                    break;
                // Add other StatToBuff cases here
            }

            // --- Wait for Duration ---
            if (applied)
            {
                float endTime = Time.time + buffSkill.duration;
                while (Time.time < endTime && !characterData.isStunned) // Buff ends early if stunned
                {
                    yield return null;
                }
            }
        }
        finally // --- Remove Buff (Guaranteed to run) ---
        {
            if (applied)
            {
                switch (buffSkill.buffStat)
                {
                    case StatToBuff.Speed:
                        if (aiPath != null)
                        {
                            // Only restore if the current speed matches the buffed value (avoids issues if another buff was applied)
                            // This requires more robust buff tracking ideally. For now, restore directly.
                            aiPath.maxSpeed = originalValue;
                            Debug.Log($"{name} Speed Buff Removed: Restored to {aiPath.maxSpeed}");
                        }
                        break;
                    // Add restore logic for other stats
                }
            }
            // Remove this specific buff coroutine instance from tracking
             if (thisCoroutine != null) activeSkillCoroutines.Remove(thisCoroutine);
        }
        yield break;
    }


    // --- Helper Functions ---

    /// <summary> Checks if the target is within the basic attack range. </summary>
    private bool IsTargetInBasicAttackRange(Transform targetTransform)
    {
        if (characterData == null || characterData.baseStats == null) return false;
        return IsTargetInRange(targetTransform, characterData.baseStats.attackRange);
    }

    /// <summary> Generic check if a target Transform is within a specified range. </summary>
    public bool IsTargetInRange(Transform targetTransform, float range)
    {
        if (targetTransform == null) return false;
        // Use sqrMagnitude for efficiency (avoids square root)
        return (targetTransform.position - transform.position).sqrMagnitude <= range * range;
    }


    // --- Animator Control Helpers ---

    /// <summary> Updates Animator parameters based on movement state. Called by CharacterMovement. </summary>
    public void UpdateAnimatorLogic(bool isGrounded, Vector2 velocity)
    {
        if (animator == null) return; // Safety check

        // Determine horizontal movement based on velocity magnitude
        bool isMovingHorizontally = Mathf.Abs(velocity.x) > 0.1f; // Threshold to ignore tiny movements

        // Set Animator parameters if they exist
        SetAnimatorBool("IsMoving", isMovingHorizontally && isGrounded); // Only moving if grounded and velocity > threshold
        SetAnimatorBool("IsGrounded", isGrounded);
        SetAnimatorFloat("VerticalVelocity", velocity.y); // Useful for jump/fall animations

        // Flip sprite based on horizontal velocity direction
        if (velocity.x > 0.1f)
        {
             transform.localScale = new Vector3(Mathf.Abs(transform.localScale.x), transform.localScale.y, transform.localScale.z);
           //GetComponent<SpriteRenderer>().flipX = false; // Alternative: Flip SpriteRenderer directly if scale is fixed
        }
        else if (velocity.x < -0.1f)
        {
             transform.localScale = new Vector3(-Mathf.Abs(transform.localScale.x), transform.localScale.y, transform.localScale.z);
            //GetComponent<SpriteRenderer>().flipX = true; // Alternative
        }
    }

    /// <summary> Safely sets a boolean parameter on the Animator. </summary>
    public void SetAnimatorBool(string name, bool value)
    {
        if (HasParameter(name, animator)) animator.SetBool(name, value);
    }

    /// <summary> Safely sets a float parameter on the Animator. </summary>
    public void SetAnimatorFloat(string name, float value)
    {
        if (HasParameter(name, animator)) animator.SetFloat(name, value);
    }

    /// <summary> Safely sets a trigger parameter on the Animator. </summary>
    public void SetAnimatorTrigger(string name)
    {
        if (HasParameter(name, animator)) animator.SetTrigger(name);
    }

    /// <summary> Checks if an Animator Controller has a parameter with the given name. </summary>
    private bool HasParameter(string paramName, Animator animatorToCheck)
    {
        if (animatorToCheck == null || string.IsNullOrEmpty(paramName) || animatorToCheck.runtimeAnimatorController == null) return false;
        foreach (AnimatorControllerParameter param in animatorToCheck.parameters)
        {
            if (param.name == paramName) return true;
        }
         // Debug.LogWarning($"Animator on {gameObject.name} does not have parameter: {paramName}", animatorToCheck);
        return false; // Parameter not found
    }
}